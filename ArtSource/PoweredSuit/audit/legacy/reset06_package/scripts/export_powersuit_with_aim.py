# pyright: reportMissingImports=false
"""Export the validated Powered Suit and four slotted Actions to Unity FBX.

Export is intentionally blocked unless:
- automated validation passed
- all 18 mandatory renders still exist and match their approved hashes
- the user explicitly approved the visual result
- the Action/Slot and rifle hierarchy remain valid
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import bpy  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from powersuit_pipeline_common import (  # noqa: E402
    PIPELINE_TEMP_PREFIX,
    REQUIRED_ACTIONS,
    activate_action,
    ensure_object_mode,
    find_action_slot,
    get_armature,
    get_rifle_root,
    object_tree,
    require_blender_52,
    select_only,
    write_json,
)

from weapon_handling_contract import (  # noqa: E402
    assert_weapon_rigid,
    validate_weapon_contract,
)

EXPORT_FILENAME = "powersuit_animated_with_aim.fbx"


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _load_and_verify_approval(root_dir: Path) -> tuple[dict, dict]:
    report_path = root_dir / "renders" / "validation_report.json"
    approval_path = root_dir / "renders" / "visual_approval.json"
    if not report_path.exists() or not approval_path.exists():
        raise RuntimeError(
            "Export is locked. Run both validation render scripts, inspect every PNG, "
            "then run approve_validation.py -- --approve."
        )
    report = json.loads(report_path.read_text(encoding="utf-8"))
    approval = json.loads(approval_path.read_text(encoding="utf-8"))
    if report.get("automated_validation") != "PASS":
        raise RuntimeError("Automated validation is not PASS.")
    if report.get("visual_validation") != "APPROVED" or not report.get("export_allowed"):
        raise RuntimeError("Visual validation is not approved.")
    if approval.get("approved") is not True:
        raise RuntimeError("Visual approval record is invalid.")
    if approval.get("validation_report_sha256") != _sha256(report_path):
        raise RuntimeError("Validation report changed after visual approval; review again.")
    for relative, expected_hash in approval.get("render_sha256", {}).items():
        path = root_dir / relative
        if not path.exists() or _sha256(path) != expected_hash:
            raise RuntimeError(f"Approved render changed or is missing: {relative}")
    if len(approval.get("render_sha256", {})) != 18:
        raise RuntimeError("Visual approval does not cover all 18 mandatory renders.")
    return report, approval


def _validate_scene(armature: bpy.types.Object, rifle_root: bpy.types.Object) -> None:
    for name in REQUIRED_ACTIONS:
        action = bpy.data.actions.get(name)
        if action is None:
            raise RuntimeError(f"Required Action is missing: {name}")
        slot = find_action_slot(action, armature)
        if len(list(action.slots)) != 1:
            raise RuntimeError(f"Action '{name}' must contain exactly one armature slot.")
        activate_action(armature, action)
        if armature.animation_data.action_slot != slot:
            raise RuntimeError(f"Could not activate the verified slot for '{name}'.")

    if (
        rifle_root.parent != armature
        or rifle_root.parent_type != "BONE"
        or rifle_root.parent_bone != "Hand.R"
    ):
        raise RuntimeError("RifleRoot final hierarchy is invalid.")
    direct = [
        obj.name for obj in bpy.data.objects
        if obj.parent == armature and obj.parent_type == "BONE"
        and (obj.name == "RifleRoot" or obj.name.startswith("Rifle_"))
    ]
    if direct != ["RifleRoot"]:
        raise RuntimeError("Only RifleRoot may be directly parented to Hand.R.")
    stray = [
        obj.name for obj in bpy.data.objects
        if obj.name.startswith("Rifle_") and obj.parent != rifle_root
    ]
    if stray:
        raise RuntimeError("Stray rifle objects: " + ", ".join(sorted(stray)))
    temps = [obj.name for obj in bpy.data.objects if obj.name.startswith(PIPELINE_TEMP_PREFIX)]
    if temps:
        raise RuntimeError("Temporary validation/IK objects remain: " + ", ".join(temps))
    ik = [
        f"{bone.name}:{constraint.name}"
        for bone in armature.pose.bones
        for constraint in bone.constraints
        if constraint.type == "IK"
    ]
    if ik:
        raise RuntimeError("Active IK remains: " + ", ".join(ik))
    validate_weapon_contract(rifle_root)
    assert_weapon_rigid(rifle_root)


def _export_objects(armature: bpy.types.Object, rifle_root: bpy.types.Object):
    objects = {armature}
    for obj in bpy.data.objects:
        if obj.type == "MESH" and obj.parent == armature and obj.parent_type == "BONE":
            if not obj.name.startswith("Preview_"):
                objects.add(obj)
    objects.update(object_tree(rifle_root))
    return sorted(objects, key=lambda item: item.name)


def _export_fbx(path: Path) -> None:
    try:
        result = bpy.ops.export_scene.fbx(
            filepath=str(path),
            check_existing=False,
            use_selection=True,
            object_types={"ARMATURE", "MESH", "EMPTY"},
            axis_forward="-Z",
            axis_up="Y",
            global_scale=1.0,
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_UNITS",
            use_space_transform=True,
            bake_space_transform=False,
            use_mesh_modifiers=True,
            mesh_smooth_type="FACE",
            add_leaf_bones=False,
            primary_bone_axis="Y",
            secondary_bone_axis="X",
            use_armature_deform_only=False,
            bake_anim=True,
            bake_anim_use_all_bones=True,
            bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=True,
            bake_anim_force_startend_keying=True,
            bake_anim_step=1.0,
            bake_anim_simplify_factor=0.0,
            path_mode="AUTO",
            embed_textures=False,
        )
    except (AttributeError, RuntimeError) as error:
        raise RuntimeError(
            "Blender's FBX add-on/export operator is unavailable or failed. "
            "Enable the bundled FBX add-on and rerun this script."
        ) from error
    if "FINISHED" not in result:
        raise RuntimeError(f"FBX exporter returned: {result}")
    if not path.exists() or path.stat().st_size < 1024:
        raise RuntimeError(f"FBX output was not created correctly: {path}")


def main() -> None:
    require_blender_52()
    ensure_object_mode()
    if not bpy.data.filepath:
        raise RuntimeError("Open powersuit_pipeline.blend before export.")
    root_dir = Path(bpy.data.filepath).resolve().parent
    report, _approval = _load_and_verify_approval(root_dir)
    armature = get_armature()
    rifle_root = get_rifle_root()
    _validate_scene(armature, rifle_root)
    if int(rifle_root.get("ps_generator_version", 0)) < 102:
        raise RuntimeError("RifleRoot predates the rigid weapon-framework reset.")
    current_blend_hash = _sha256(Path(bpy.data.filepath).resolve())
    if report.get("blend_sha256_at_validation") != current_blend_hash:
        raise RuntimeError(
            "The .blend changed after validation. Re-run both render scripts and approve again."
        )

    # Leave a deterministic, explicitly slotted action active for the bind-scene
    # evaluation while the exporter independently bakes all compatible Actions.
    activate_action(armature, "PS_Idle")
    bpy.context.scene.frame_set(1)
    objects = _export_objects(armature, rifle_root)
    select_only(objects, active=armature)

    export_dir = root_dir / "exports"
    export_dir.mkdir(parents=True, exist_ok=True)
    export_path = export_dir / EXPORT_FILENAME
    _export_fbx(export_path)

    manifest = {
        "fbx_path": str(export_path),
        "fbx_sha256": _sha256(export_path),
        "fbx_size_bytes": export_path.stat().st_size,
        "source_blend": str(Path(bpy.data.filepath).resolve()),
        "exported_actions": list(REQUIRED_ACTIONS),
        "exported_objects": [obj.name for obj in objects],
        "validation_report_blend_sha256": report.get("blend_sha256_at_validation", ""),
        "unity_import_notes": {
            "rig_type": "Generic",
            "scale_factor": 1.0,
            "expected_clips": list(REQUIRED_ACTIONS),
        },
    }
    manifest_path = export_dir / "export_manifest.json"
    write_json(manifest_path, manifest)
    print("\nValidated Unity-compatible FBX export complete.")
    print(f"FBX: {export_path}")
    print(f"Manifest: {manifest_path}")


if __name__ == "__main__":
    main()
