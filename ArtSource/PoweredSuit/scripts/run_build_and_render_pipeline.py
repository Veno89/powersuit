# pyright: reportMissingImports=false
"""Run the full build and validation pipeline in one Blender 5.2 process.

Keeping the stages in one process avoids a Blender 5.2 Windows dependency-graph
stack overflow observed only after reloading the freshly saved, bone-parented
rifle between the animation and render stages. Every stage remains a complete,
independently runnable script; this file only orchestrates them deterministically.
"""
from __future__ import annotations

import gc
import json
import runpy
import sys
import traceback
from pathlib import Path

import bpy  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPT_DIR.parent

STAGES = (
    "upgrade_powersuit_rig.py",
    "upgrade_powersuit_model.py",
    "upgrade_rifle_model.py",
    "create_aim_animation.py",
    "create_weapon_animation_set.py",
    "render_animation_validation.py",
    "render_rifle_validation.py",
    "render_weapon_animation_validation.py",
)

REQUIRED_AIM_RENDERS = {
    "idle_upperbody_front_3q.png",
    "idle_upperbody_side.png",
    "aim_frame_001_front_3q.png",
    "aim_frame_001_side.png",
    "aim_frame_015_front_3q.png",
    "aim_frame_015_side.png",
    "aim_frame_030_front_3q.png",
    "aim_frame_030_side.png",
    "aim_over_shoulder.png",
    "aim_close_trigger_grip.png",
    "aim_close_support_grip.png",
    "aim_close_stock_scope.png",
    "aim_close_elbows.png",
}

REQUIRED_RIFLE_RENDERS = {
    "rifle_left_side_closeup.png",
    "rifle_right_side_closeup.png",
    "rifle_front_3q_closeup.png",
    "rifle_rear_3q_closeup.png",
    "rifle_with_suit_scale.png",
}

REQUIRED_WEAPON_ANIMATION_RENDERS = {
    "ready_idle_front_3q.png",
    "stowed_idle_rear_3q.png",
    "draw_frame_010_rear_3q.png",
    "draw_frame_018_side.png",
    "sheathe_frame_021_rear_3q.png",
    "walk_forward_frame_009_side.png",
    "walk_backward_frame_009_side.png",
    "aim_walk_forward_frame_009_front_3q.png",
    "aim_walk_backward_frame_009_side.png",
    "reload_frame_050_magazine.png",
    "reload_frame_064_insert.png",
    "bolt_frame_012_close.png",
    "stowed_walk_frame_009_rear_3q.png",
    "stowed_hover_frame_031_rear_3q.png",
    "run_forward_frame_006_side.png",
}


def _run_stage(filename: str) -> None:
    path = SCRIPT_DIR / filename
    if not path.is_file():
        raise RuntimeError(f"Pipeline stage is missing: {path}")
    print("\n" + "=" * 70, flush=True)
    print(f"PIPELINE STAGE: {filename}", flush=True)
    print("=" * 70, flush=True)
    runpy.run_path(str(path), run_name="__main__")
    gc.collect()


def _verify_outputs() -> None:
    aim_dir = PROJECT_DIR / "renders" / "aim_validation"
    rifle_dir = PROJECT_DIR / "renders" / "rifle_validation"
    weapon_animation_dir = PROJECT_DIR / "renders" / "weapon_animation_validation"
    aim_names = {path.name for path in aim_dir.glob("*.png")} if aim_dir.is_dir() else set()
    rifle_names = {path.name for path in rifle_dir.glob("*.png")} if rifle_dir.is_dir() else set()
    weapon_animation_names = (
        {path.name for path in weapon_animation_dir.glob("*.png")}
        if weapon_animation_dir.is_dir() else set()
    )
    missing_aim = sorted(REQUIRED_AIM_RENDERS - aim_names)
    missing_rifle = sorted(REQUIRED_RIFLE_RENDERS - rifle_names)
    missing_weapon_animation = sorted(
        REQUIRED_WEAPON_ANIMATION_RENDERS - weapon_animation_names
    )
    if missing_aim or missing_rifle or missing_weapon_animation:
        details = []
        if missing_aim:
            details.append("aim: " + ", ".join(missing_aim))
        if missing_rifle:
            details.append("rifle: " + ", ".join(missing_rifle))
        if missing_weapon_animation:
            details.append(
                "weapon animation: " + ", ".join(missing_weapon_animation)
            )
        raise RuntimeError("Mandatory validation renders are incomplete (" + "; ".join(details) + ").")
    report = PROJECT_DIR / "renders" / "validation_report.json"
    if not report.is_file() or report.stat().st_size < 256:
        raise RuntimeError("validation_report.json was not created correctly.")


def main() -> None:
    if tuple(bpy.app.version[:2]) < (5, 2):
        raise RuntimeError(
            f"Blender 5.2 or newer is required; running {bpy.app.version_string}."
        )
    if not bpy.data.filepath:
        raise RuntimeError("The one-process runner requires a saved powersuit_pipeline.blend.")
    expected_blend = (PROJECT_DIR / "powersuit_pipeline.blend").resolve()
    if Path(bpy.data.filepath).resolve() != expected_blend:
        raise RuntimeError(
            "The loaded blend must be powersuit_pipeline.blend in the package root."
        )

    for stage in STAGES:
        _run_stage(stage)

    _verify_outputs()
    report_path = PROJECT_DIR / "renders" / "validation_report.json"
    report = json.loads(report_path.read_text(encoding="utf-8"))
    status = str(report.get("automated_validation", "UNKNOWN"))
    blockers = list(report.get("automated_blockers", []))
    print("\nFull build and validation render pipeline completed.", flush=True)
    print(f"Automated validation status: {status}", flush=True)
    if blockers:
        print("Automated blockers (renders were still produced):", flush=True)
        for blocker in blockers:
            print(f"  - {blocker}", flush=True)
    print("Visual inspection is required before approval/export.", flush=True)


if __name__ == "__main__":
    try:
        main()
    except BaseException:
        traceback.print_exc()
        sys.stdout.flush()
        sys.stderr.flush()
        raise
