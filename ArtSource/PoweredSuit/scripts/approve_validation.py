# pyright: reportMissingImports=false
"""Record the user's explicit visual review decision for the validation renders.

Usage after inspecting every image:
  blender --background powersuit_pipeline.blend \
    --python scripts/approve_validation.py -- --approve

To reject the current result instead:
  ... -- --reject "scope intersects helmet"
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy  # type: ignore

AIM_FILES = (
    "renders/aim_validation/idle_upperbody_front_3q.png",
    "renders/aim_validation/idle_upperbody_side.png",
    "renders/aim_validation/aim_frame_001_front_3q.png",
    "renders/aim_validation/aim_frame_001_side.png",
    "renders/aim_validation/aim_frame_015_front_3q.png",
    "renders/aim_validation/aim_frame_015_side.png",
    "renders/aim_validation/aim_frame_030_front_3q.png",
    "renders/aim_validation/aim_frame_030_side.png",
    "renders/aim_validation/aim_over_shoulder.png",
    "renders/aim_validation/aim_close_trigger_grip.png",
    "renders/aim_validation/aim_close_support_grip.png",
    "renders/aim_validation/aim_close_stock_scope.png",
    "renders/aim_validation/aim_close_elbows.png",
)
RIFLE_FILES = (
    "renders/rifle_validation/rifle_left_side_closeup.png",
    "renders/rifle_validation/rifle_right_side_closeup.png",
    "renders/rifle_validation/rifle_front_3q_closeup.png",
    "renders/rifle_validation/rifle_rear_3q_closeup.png",
    "renders/rifle_validation/rifle_with_suit_scale.png",
)
EXPECTED_RENDER_FILES = (*AIM_FILES, *RIFLE_FILES)
CHECKLIST = (
    "arms bend naturally down and outward",
    "trigger hand visibly holds the pistol grip",
    "support palm and fingers visibly wrap the compact foregrip",
    "no oversized platform remains beneath the handguard",
    "weapon and arms clear the torso",
    "small buttpad seats in the right shoulder pocket",
    "ocular lens is in front of the visor without penetration",
    "major rifle components and silhouette read clearly",
    "PS_Idle and PS_Aim visibly differ",
)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _arguments() -> argparse.Namespace:
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--approve", action="store_true")
    group.add_argument("--reject", metavar="REASON")
    return parser.parse_args(arguments)


def main() -> None:
    args = _arguments()
    if not bpy.data.filepath:
        raise RuntimeError("Open powersuit_pipeline.blend before recording visual review.")
    blend_path = Path(bpy.data.filepath).resolve()
    if blend_path.name != "powersuit_pipeline.blend":
        raise RuntimeError("Visual review must be recorded against powersuit_pipeline.blend.")
    root = blend_path.parent
    report_path = root / "renders" / "validation_report.json"
    if not report_path.exists():
        raise RuntimeError("Validation report is missing. Run both render scripts first.")
    report = json.loads(report_path.read_text(encoding="utf-8"))

    if args.reject:
        report["visual_validation"] = "REJECTED"
        report["visual_rejection_reason"] = args.reject
        report["export_allowed"] = False
        report_path.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        approval_path = root / "renders" / "visual_approval.json"
        if approval_path.exists():
            approval_path.unlink()
        print(f"Visual validation rejected: {args.reject}")
        return

    if report.get("automated_validation") != "PASS":
        raise RuntimeError("Automated validation has not passed.")
    if report.get("automated_blockers"):
        raise RuntimeError("Automated blockers remain in the validation report.")
    if report.get("rifle_render_set_complete") is not True:
        raise RuntimeError("The validation report does not confirm the complete rifle render set.")

    normalized_aim = {
        str(relative).replace("\\", "/")
        for relative in report.get("aim_render_files", [])
    }
    normalized_rifle = {
        str(relative).replace("\\", "/")
        for relative in report.get("rifle_render_files", [])
    }
    if normalized_aim != set(AIM_FILES) or normalized_rifle != set(RIFLE_FILES):
        raise RuntimeError("Validation report render paths are not the canonical 18-file set.")
    if report.get("blend_sha256_at_validation") != _sha256(blend_path):
        raise RuntimeError("The .blend changed after validation. Rebuild and review again.")

    required = [root / relative for relative in EXPECTED_RENDER_FILES]
    missing = [
        str(path.relative_to(root))
        for path in required
        if not path.exists() or path.stat().st_size < 4096
    ]
    if missing:
        raise RuntimeError("Mandatory validation renders are missing: " + ", ".join(missing))

    report["visual_validation"] = "APPROVED"
    report["visual_rejection_reason"] = ""
    report["export_allowed"] = True
    report_path.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")

    approval = {
        "approved": True,
        "approval_basis": "User explicitly reviewed all mandatory PNG renders, including the close grip/stock/elbow views.",
        "checklist_confirmed": list(CHECKLIST),
        "blend_sha256_at_approval": _sha256(blend_path),
        "validation_report_sha256": _sha256(report_path),
        "render_sha256": {
            str(path.relative_to(root)): _sha256(path)
            for path in required
        },
    }
    approval_path = root / "renders" / "visual_approval.json"
    approval_path.write_text(json.dumps(approval, indent=2, sort_keys=True), encoding="utf-8")
    print("Visual validation approved. FBX export is now unlocked.")
    print(f"Approval record: {approval_path}")


if __name__ == "__main__":
    main()
