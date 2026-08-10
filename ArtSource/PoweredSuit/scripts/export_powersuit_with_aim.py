# pyright: reportMissingImports=false
"""Export the validated Powered Suit and deterministic slotted Actions to Unity FBX.

Export is intentionally blocked unless:
- automated validation passed
- all 33 mandatory renders still exist and match their approved hashes
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
    require_character_asset_versions,
    require_blender_52,
    select_only,
    write_json,
)

from weapon_handling_contract import (  # noqa: E402
    COMPONENT_BOLT,
    COMPONENT_MAGAZINE,
    assert_weapon_rigid,
    validate_weapon_contract,
    weapon_components,
)

EXPORT_FILENAME = "powersuit_animated_with_aim.fbx"
EXPECTED_RENDER_FILES = {
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
    "renders/rifle_validation/rifle_left_side_closeup.png",
    "renders/rifle_validation/rifle_right_side_closeup.png",
    "renders/rifle_validation/rifle_front_3q_closeup.png",
    "renders/rifle_validation/rifle_rear_3q_closeup.png",
    "renders/rifle_validation/rifle_with_suit_scale.png",
    "renders/weapon_animation_validation/ready_idle_front_3q.png",
    "renders/weapon_animation_validation/stowed_idle_rear_3q.png",
    "renders/weapon_animation_validation/draw_frame_010_rear_3q.png",
    "renders/weapon_animation_validation/draw_frame_018_side.png",
    "renders/weapon_animation_validation/sheathe_frame_021_rear_3q.png",
    "renders/weapon_animation_validation/walk_forward_frame_009_side.png",
    "renders/weapon_animation_validation/walk_backward_frame_009_side.png",
    "renders/weapon_animation_validation/aim_walk_forward_frame_009_front_3q.png",
    "renders/weapon_animation_validation/aim_walk_backward_frame_009_side.png",
    "renders/weapon_animation_validation/reload_frame_050_magazine.png",
    "renders/weapon_animation_validation/reload_frame_064_insert.png",
    "renders/weapon_animation_validation/bolt_frame_012_close.png",
    "renders/weapon_animation_validation/stowed_walk_frame_009_rear_3q.png",
    "renders/weapon_animation_validation/stowed_hover_frame_031_rear_3q.png",
    "renders/weapon_animation_validation/run_forward_frame_006_side.png",
}


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
    approved_hashes = {
        str(relative).replace("\\", "/"): expected_hash
        for relative, expected_hash in approval.get("render_sha256", {}).items()
    }
    if set(approved_hashes) != EXPECTED_RENDER_FILES:
        raise RuntimeError("Visual approval does not cover the canonical 33-render set.")
    report_files = {
        str(relative).replace("\\", "/")
        for relative in (
            *report.get("aim_render_files", []),
            *report.get("rifle_render_files", []),
            *report.get("weapon_animation_render_files", []),
        )
    }
    if (
        report_files != EXPECTED_RENDER_FILES
        or report.get("rifle_render_set_complete") is not True
        or report.get("weapon_animation_render_set_complete") is not True
    ):
        raise RuntimeError("Validation report does not describe the canonical 33-render set.")
    for relative, expected_hash in approved_hashes.items():
        path = root_dir / relative
        if not path.exists() or _sha256(path) != expected_hash:
            raise RuntimeError(f"Approved render changed or is missing: {relative}")
    return report, approval


def _validate_scene(armature: bpy.types.Object, rifle_root: bpy.types.Object) -> None:
    require_character_asset_versions(armature)
    action_names = {action.name for action in bpy.data.actions}
    if action_names != set(REQUIRED_ACTIONS):
        unexpected = sorted(action_names - set(REQUIRED_ACTIONS))
        missing = sorted(set(REQUIRED_ACTIONS) - action_names)
        raise RuntimeError(
            "The export scene must contain exactly the required deterministic Actions "
            f"(missing={missing}, unexpected={unexpected})."
        )
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
        or rifle_root.parent_bone != "WeaponRoot"
    ):
        raise RuntimeError("RifleRoot final hierarchy is invalid.")
    expected_articulated = {
        obj.name: (
            "WeaponMagazine"
            if str(obj.get("ps_weapon_component_role", "")) == COMPONENT_MAGAZINE
            else "WeaponBolt"
        )
        for obj in (
            weapon_components(rifle_root, COMPONENT_MAGAZINE)
            + weapon_components(rifle_root, COMPONENT_BOLT)
        )
    }
    direct = [
        obj.name for obj in bpy.data.objects
        if obj.parent == armature and obj.parent_type == "BONE"
        and (obj.name == "RifleRoot" or obj.name.startswith("Rifle_"))
    ]
    if set(direct) != {"RifleRoot", *expected_articulated}:
        raise RuntimeError(
            "Direct weapon/control-bone hierarchy is incomplete: "
            + ", ".join(sorted(direct))
        )
    bad_articulated = [
        f"{name}->{bpy.data.objects[name].parent_bone}"
        for name, expected_bone in expected_articulated.items()
        if bpy.data.objects[name].parent_bone != expected_bone
    ]
    if bad_articulated:
        raise RuntimeError(
            "Articulated components use wrong control bones: "
            + ", ".join(bad_articulated)
        )
    stray = [
        obj.name for obj in bpy.data.objects
        if obj.name.startswith("Rifle_")
        and obj.parent != rifle_root
        and obj.name not in expected_articulated
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

    inward = []
    for obj in _export_objects(armature, rifle_root):
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        signed_volume = sum(
            obj.data.vertices[triangle.vertices[0]].co.dot(
                obj.data.vertices[triangle.vertices[1]].co.cross(
                    obj.data.vertices[triangle.vertices[2]].co
                )
            )
            for triangle in obj.data.loop_triangles
        ) / 6.0
        if signed_volume <= 1.0e-9:
            inward.append(f"{obj.name} ({signed_volume:.9g} m^3)")
    if inward:
        raise RuntimeError(
            "Export meshes must be closed and outward-wound for Unity backface "
            "culling: " + ", ".join(inward)
        )


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
    if int(rifle_root.get("ps_generator_version", 0)) < 111:
        raise RuntimeError("RifleRoot predates the rigid weapon-framework reset.")
    current_blend_hash = _sha256(Path(bpy.data.filepath).resolve())
    if report.get("blend_sha256_at_validation") != current_blend_hash:
        raise RuntimeError(
            "The .blend changed after validation. Re-run both render scripts and approve again."
        )
    if _approval.get("blend_sha256_at_approval") != current_blend_hash:
        raise RuntimeError("The .blend no longer matches the explicitly approved asset.")

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
        "action_frame_ranges": {
            name: [
                int(bpy.data.actions[name].frame_start),
                int(bpy.data.actions[name].frame_end),
            ]
            for name in REQUIRED_ACTIONS
        },
        "animation_fps": int(bpy.context.scene.render.fps),
        "animation_contract_version": int(
            rifle_root.get("ps_weapon_animation_contract_version", 0)
        ),
        "gameplay_timing_markers": {
            "reload_commit_frame": int(
                rifle_root.get("ps_reload_commit_frame", 75)
            ),
            "reload_end_frame": int(
                rifle_root.get("ps_reload_frame_end", 84)
            ),
            "bolt_cycle_end_frame": int(
                rifle_root.get("ps_bolt_cycle_frame_end", 20)
            ),
        },
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
