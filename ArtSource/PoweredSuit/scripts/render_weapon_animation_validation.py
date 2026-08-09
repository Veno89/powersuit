# pyright: reportMissingImports=false
"""Validate and render the complete weapon-handling animation pass.

The stage appends its results to ``renders/validation_report.json`` and leaves
visual approval locked. It runs after the legacy aim and isolated-rifle render
stages, so all old Generator 110 gates remain in force alongside these images.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from powersuit_pipeline_common import (  # noqa: E402
    PIPELINE_TEMP_PREFIX,
    REQUIRED_ACTIONS,
    WEAPON_ANIMATION_ACTIONS,
    activate_action,
    body_basis,
    bone_head_world,
    create_static_render_scene,
    ensure_directory,
    ensure_object_mode,
    find_action_slot,
    get_armature,
    get_rifle_root,
    remove_pipeline_temps,
    remove_static_render_scene,
    require_blender_52,
    set_camera_look_at,
    update_static_render_proxies,
    world_bounds,
    write_json,
)
from weapon_handling_contract import (  # noqa: E402
    COMPONENT_BOLT,
    COMPONENT_MAGAZINE,
    ROLE_PRIMARY_GRIP,
    ROLE_SUPPORT_GRIP,
    assert_weapon_rigid,
    require_weapon_helper,
    validate_weapon_contract,
    weapon_components,
    weapon_contract_objects,
    weapon_local_position,
)
from render_animation_validation import _validate_render_content  # noqa: E402

CONTROL_BONES = ("WeaponRoot", "WeaponMagazine", "WeaponBolt")
EXPECTED_RANGES = {
    "PS_WeaponReady_Idle": (1, 61),
    "PS_WeaponStowed_Idle": (1, 61),
    "PS_Weapon_Draw": (1, 30),
    "PS_Weapon_Sheathe": (1, 30),
    "PS_Walk_Forward": (1, 31),
    "PS_Walk_Backward": (1, 31),
    "PS_Aim_Walk_Forward": (1, 31),
    "PS_Aim_Walk_Backward": (1, 31),
    "PS_Reload": (1, 84),
    "PS_BoltCycle": (1, 20),
    "PS_WeaponStowed_Walk_Forward": (1, 31),
    "PS_WeaponStowed_Walk_Backward": (1, 31),
    "PS_WeaponStowed_Hover": (1, 61),
}
REQUIRED_RENDERS = (
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
)


def _evaluate(armature: bpy.types.Object, action_name: str, frame: int) -> None:
    action, slot = activate_action(armature, action_name)
    neighbour = frame + 1 if frame < int(action.frame_end) else max(1, frame - 1)
    bpy.context.scene.frame_set(neighbour)
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    animation_data = armature.animation_data
    if (
        animation_data is None
        or animation_data.action != action
        or animation_data.action_slot != slot
    ):
        raise RuntimeError(f"Could not evaluate synchronized Action '{action_name}'.")


def _root_world(armature: bpy.types.Object, root: bpy.types.Object, name: str, frame: int) -> Matrix:
    _evaluate(armature, name, frame)
    return root.matrix_world.copy()


def _matrix_delta(first: Matrix, second: Matrix) -> float:
    return max(
        abs(float(first[row][column]) - float(second[row][column]))
        for row in range(4)
        for column in range(4)
    )


def _component_relative(root: bpy.types.Object, obj: bpy.types.Object) -> Matrix:
    return root.matrix_world.inverted_safe() @ obj.matrix_world


def _validate(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> dict[str, object]:
    blockers: list[str] = []
    action_names = {action.name for action in bpy.data.actions}
    if action_names != set(REQUIRED_ACTIONS):
        raise RuntimeError(
            "Weapon validation Action set mismatch: "
            f"missing={sorted(set(REQUIRED_ACTIONS) - action_names)}, "
            f"unexpected={sorted(action_names - set(REQUIRED_ACTIONS))}."
        )
    for name in WEAPON_ANIMATION_ACTIONS:
        action = bpy.data.actions[name]
        if len(list(action.slots)) != 1:
            raise RuntimeError(f"{name} is not a single synchronized armature Action.")
        find_action_slot(action, armature)
        actual = (int(action.frame_start), int(action.frame_end))
        if actual != EXPECTED_RANGES[name]:
            raise RuntimeError(
                f"{name} range is {actual}; expected {EXPECTED_RANGES[name]}."
            )

    missing_controls = [name for name in CONTROL_BONES if name not in armature.data.bones]
    if missing_controls:
        raise RuntimeError("Weapon control bones are missing: " + ", ".join(missing_controls))
    deforming = [name for name in CONTROL_BONES if armature.data.bones[name].use_deform]
    if deforming:
        raise RuntimeError("Weapon control bones must be non-deforming: " + ", ".join(deforming))
    if root.parent != armature or root.parent_bone != "WeaponRoot":
        raise RuntimeError("RifleRoot is not parented to WeaponRoot.")

    magazines = weapon_components(root, COMPONENT_MAGAZINE)
    bolts = weapon_components(root, COMPONENT_BOLT)
    for obj in magazines:
        if obj.parent != armature or obj.parent_bone != "WeaponMagazine":
            raise RuntimeError(f"{obj.name} is not parented to WeaponMagazine.")
    for obj in bolts:
        if obj.parent != armature or obj.parent_bone != "WeaponBolt":
            raise RuntimeError(f"{obj.name} is not parented to WeaponBolt.")
    contract = validate_weapon_contract(root)

    _evaluate(armature, "PS_WeaponReady_Idle", 1)
    ready_root = root.matrix_world.copy()
    right_error = (
        bone_head_world(armature, "Hand.R")
        - require_weapon_helper(root, ROLE_PRIMARY_GRIP).matrix_world.translation
    ).length
    left_error = (
        bone_head_world(armature, "Hand.L")
        - require_weapon_helper(root, ROLE_SUPPORT_GRIP).matrix_world.translation
    ).length
    if right_error > 0.020:
        blockers.append(f"ready trigger-hand contact={right_error:.3f} m")
    if left_error > 0.020:
        blockers.append(f"ready support-hand contact={left_error:.3f} m")

    _evaluate(armature, "PS_WeaponStowed_Idle", 1)
    stowed_root = root.matrix_world.copy()
    _right, forward, _up = body_basis(armature)
    chest = bone_head_world(armature, "Chest")
    back_offset = (stowed_root.translation - chest).dot(forward)
    if back_offset > -0.18:
        blockers.append(f"stowed rifle is not behind torso ({back_offset:.3f} m)")

    draw_start = _root_world(armature, root, "PS_Weapon_Draw", 1)
    draw_end = _root_world(armature, root, "PS_Weapon_Draw", 30)
    sheathe_start = _root_world(armature, root, "PS_Weapon_Sheathe", 1)
    sheathe_end = _root_world(armature, root, "PS_Weapon_Sheathe", 30)
    endpoint_deltas = {
        "draw_start_to_stowed": _matrix_delta(draw_start, stowed_root),
        "draw_end_to_ready": _matrix_delta(draw_end, ready_root),
        "sheathe_start_to_ready": _matrix_delta(sheathe_start, ready_root),
        "sheathe_end_to_stowed": _matrix_delta(sheathe_end, stowed_root),
    }
    for label, value in endpoint_deltas.items():
        if value > 2.0e-4:
            blockers.append(f"{label}={value:.3e}")

    _evaluate(armature, "PS_Reload", 50)
    magazine_travel = max(
        _component_relative(root, obj).translation.length for obj in magazines
    )
    assert_weapon_rigid(root)
    if magazine_travel < 0.20:
        blockers.append(f"reload magazine travel={magazine_travel:.3f} m")

    _evaluate(armature, "PS_BoltCycle", 12)
    bolt_travel = max(
        _component_relative(root, obj).translation.length for obj in bolts
    )
    assert_weapon_rigid(root)
    if bolt_travel < 0.065:
        blockers.append(f"bolt travel={bolt_travel:.3f} m")

    _evaluate(armature, "PS_Walk_Forward", 9)
    forward_foot = bone_head_world(armature, "Foot.L")
    _evaluate(armature, "PS_Walk_Backward", 9)
    backward_foot = bone_head_world(armature, "Foot.L")
    directional_foot_delta = (forward_foot - backward_foot).length
    if directional_foot_delta < 0.12:
        blockers.append(
            f"forward/backpedal foot phases too similar={directional_foot_delta:.3f} m"
        )

    return {
        "fps": 30,
        "action_ranges": {name: list(values) for name, values in EXPECTED_RANGES.items()},
        "control_bones": list(CONTROL_BONES),
        "single_armature_slot_per_action": True,
        "weapon_contract": contract,
        "ready_right_wrist_error_m": right_error,
        "ready_left_wrist_error_m": left_error,
        "stowed_back_offset_m": back_offset,
        "draw_sheathe_endpoint_deltas": endpoint_deltas,
        "reload_magazine_travel_m": magazine_travel,
        "bolt_travel_m": bolt_travel,
        "forward_backward_foot_phase_delta_m": directional_foot_delta,
        "automated_blockers": blockers,
    }


def _create_camera(render_scene: bpy.types.Scene, collection: bpy.types.Collection):
    data = bpy.data.cameras.new(PIPELINE_TEMP_PREFIX + "WeaponAnimationCameraData")
    camera = bpy.data.objects.new(PIPELINE_TEMP_PREFIX + "WeaponAnimationCamera", data)
    collection.objects.link(camera)
    render_scene.camera = camera
    data.lens = 55.0
    data.sensor_width = 36.0
    return camera


def _render_one(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    render_scene: bpy.types.Scene,
    camera: bpy.types.Object,
    proxies: dict[str, bpy.types.Object],
    action_name: str,
    frame: int,
    view: str,
    path: Path,
) -> None:
    _evaluate(armature, action_name, frame)
    update_static_render_proxies(proxies, visible_names=set(proxies))
    sources = [obj for obj in bpy.data.objects if obj.name in proxies]
    minimum, maximum = world_bounds(sources)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    right, forward, up = body_basis(armature)
    target = center + up * 0.06
    radius = max(size.x, size.y, size.z, 1.5)

    if view == "front_3q":
        location = target + forward * radius * 1.70 + right * radius * 0.82 + up * radius * 0.20
        camera.data.lens = 56.0
    elif view == "rear_3q":
        location = target - forward * radius * 1.70 - right * radius * 0.72 + up * radius * 0.22
        camera.data.lens = 56.0
    elif view == "side":
        location = target + right * radius * 1.95 + forward * radius * 0.10 + up * radius * 0.17
        camera.data.lens = 58.0
    elif view in {"magazine_close", "bolt_close"}:
        role = COMPONENT_MAGAZINE if view == "magazine_close" else COMPONENT_BOLT
        components = weapon_components(root, role)
        points = [
            obj.matrix_world @ weapon_local_position(root, obj)
            for obj in components
        ]
        target = sum(points, Vector((0.0, 0.0, 0.0))) / len(points)
        rifle_basis = root.matrix_world.to_3x3()
        rifle_right = (rifle_basis @ Vector((1.0, 0.0, 0.0))).normalized()
        rifle_forward = (rifle_basis @ Vector((0.0, 1.0, 0.0))).normalized()
        rifle_up = (rifle_basis @ Vector((0.0, 0.0, 1.0))).normalized()
        if view == "bolt_close":
            # The bolt is on rifle-local -X. View it from that exposed side so
            # the receiver and trigger forearm cannot fill/blank the frame.
            location = (
                target
                - rifle_right * 0.78
                + rifle_forward * 0.24
                + rifle_up * 0.20
            )
            camera.data.lens = 50.0
        else:
            # Frame the moving magazine and the reload wrist together from the
            # exposed receiver side.  Looking from local +X placed the trigger
            # forearm directly between the camera and the magazine.
            reload_wrist = armature.pose.bones.get("Hand.L")
            if reload_wrist is not None:
                wrist_world = armature.matrix_world @ reload_wrist.head
                target = target.lerp(wrist_world, 0.35)
            location = (
                target
                - rifle_right * 0.76
                + rifle_forward * 0.28
                + rifle_up * 0.18
            )
            camera.data.lens = 55.0
        camera.data.clip_start = 0.02
    else:
        raise ValueError(view)

    set_camera_look_at(camera, location, target)
    render_scene.render.filepath = str(path)
    print(
        f"[Weapon animation validation] {path.name}: "
        f"{action_name} frame {frame} {view}",
        flush=True,
    )
    bpy.ops.render.render(write_still=True, scene=render_scene.name)
    if not path.exists() or path.stat().st_size < 4096:
        raise RuntimeError(f"Weapon animation render failed: {path}")
    _validate_render_content(path)


def _render_all(armature: bpy.types.Object, root: bpy.types.Object) -> list[Path]:
    output = ensure_directory("renders", "weapon_animation_validation")
    character = [
        obj for obj in bpy.data.objects
        if obj.type == "MESH"
        and obj.parent == armature
        and obj.parent_type == "BONE"
        and not obj.name.startswith("Preview_")
    ]
    weapon = [obj for obj in weapon_contract_objects(root) if obj.type == "MESH"]
    sources = list(dict.fromkeys([*character, *weapon]))
    render_scene = None
    collection = None
    proxies: dict[str, bpy.types.Object] = {}
    try:
        render_scene, collection, proxies = create_static_render_scene(
            PIPELINE_TEMP_PREFIX + "WeaponAnimationRenderScene", sources
        )
        camera = _create_camera(render_scene, collection)
        render_scene.render.engine = "BLENDER_WORKBENCH"
        render_scene.display.shading.color_type = "OBJECT"
        render_scene.display.shading.light = "STUDIO"
        render_scene.display.shading.show_shadows = True
        render_scene.display.shading.show_cavity = True
        render_scene.display.shading.cavity_type = "BOTH"
        render_scene.render.resolution_x = 1280
        render_scene.render.resolution_y = 960
        render_scene.render.resolution_percentage = 100
        render_scene.render.image_settings.file_format = "PNG"
        jobs = (
            ("PS_WeaponReady_Idle", 1, "front_3q", REQUIRED_RENDERS[0]),
            ("PS_WeaponStowed_Idle", 1, "rear_3q", REQUIRED_RENDERS[1]),
            ("PS_Weapon_Draw", 10, "rear_3q", REQUIRED_RENDERS[2]),
            ("PS_Weapon_Draw", 18, "side", REQUIRED_RENDERS[3]),
            ("PS_Weapon_Sheathe", 21, "rear_3q", REQUIRED_RENDERS[4]),
            ("PS_Walk_Forward", 9, "side", REQUIRED_RENDERS[5]),
            ("PS_Walk_Backward", 9, "side", REQUIRED_RENDERS[6]),
            ("PS_Aim_Walk_Forward", 9, "front_3q", REQUIRED_RENDERS[7]),
            ("PS_Aim_Walk_Backward", 9, "side", REQUIRED_RENDERS[8]),
            ("PS_Reload", 50, "magazine_close", REQUIRED_RENDERS[9]),
            ("PS_Reload", 64, "magazine_close", REQUIRED_RENDERS[10]),
            ("PS_BoltCycle", 12, "bolt_close", REQUIRED_RENDERS[11]),
            ("PS_WeaponStowed_Walk_Forward", 9, "rear_3q", REQUIRED_RENDERS[12]),
            ("PS_WeaponStowed_Hover", 31, "rear_3q", REQUIRED_RENDERS[13]),
        )
        paths: list[Path] = []
        for action, frame, view, filename in jobs:
            path = output / filename
            _render_one(
                armature, root, render_scene, camera, proxies,
                action, frame, view, path,
            )
            paths.append(path)
        return paths
    finally:
        remove_static_render_scene(render_scene, collection, proxies)
        remove_pipeline_temps()


def _append_report(paths: list[Path], metrics: dict[str, object]) -> Path:
    report_path = ensure_directory("renders") / "validation_report.json"
    if not report_path.exists():
        raise RuntimeError("Legacy aim/rifle validation report is missing.")
    report = json.loads(report_path.read_text(encoding="utf-8"))
    blend_parent = Path(bpy.data.filepath).resolve().parent
    report["weapon_animation_render_files"] = [
        str(path.relative_to(blend_parent)) for path in paths
    ]
    report["weapon_animation_render_set_complete"] = {
        path.name for path in paths
    } == set(REQUIRED_RENDERS)
    report["weapon_animation_validation"] = metrics
    blockers = list(report.get("automated_blockers", []))
    for blocker in metrics.get("automated_blockers", []):
        text = str(blocker)
        if text not in blockers:
            blockers.append(text)
    report["automated_blockers"] = blockers
    report["automated_validation"] = "PASS" if not blockers else "REVIEW_BLOCKED"
    report["visual_validation"] = "NOT_REVIEWED"
    report["export_allowed"] = False
    visual_review_required = list(
        dict.fromkeys(report.setdefault("visual_review_required", []))
    )
    for requirement in [
        "ready idle holds the rifle diagonally with both hands",
        "stowed rifle lies diagonally against the back without torso penetration",
        "draw and sheath transitions do not pop or detach",
        "forward walk plants feet without moonwalking",
        "S/backward locomotion visibly backpedals while facing forward",
        "aim-walk keeps the shouldered sight picture while legs move",
        "reload hand follows the magazine through removal and insertion",
        "bolt hand and bolt mechanism move together",
        "stowed walk and hover keep the rifle on the back",
    ]:
        if requirement not in visual_review_required:
            visual_review_required.append(requirement)
    report["visual_review_required"] = visual_review_required
    write_json(report_path, report)
    return report_path


def main() -> None:
    require_blender_52()
    ensure_object_mode()
    armature = get_armature()
    root = get_rifle_root()
    print("[Weapon animation validation] Checking actions and controls...", flush=True)
    metrics = _validate(armature, root)
    blockers = list(metrics.get("automated_blockers", []))
    if blockers:
        print("[Weapon animation validation] Review blockers detected:", flush=True)
        for blocker in blockers:
            print(f"  - {blocker}", flush=True)
    print("[Weapon animation validation] Rendering mandatory views...", flush=True)
    paths = _render_all(armature, root)
    if {path.name for path in paths} != set(REQUIRED_RENDERS):
        raise RuntimeError("Mandatory weapon-animation render set is incomplete.")
    report_path = _append_report(paths, metrics)
    print("\nWeapon animation validation renders complete.")
    print("Visual approval remains locked pending human review.")
    print(f"Report: {report_path}")


if __name__ == "__main__":
    main()
