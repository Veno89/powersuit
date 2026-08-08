# pyright: reportMissingImports=false
"""Render the exact mandatory rifle-validation close-ups.

This script performs no modelling and no animation creation. It evaluates PS_Aim
through its Blender 5.2 Action Slot, renders four isolated weapon close-ups plus
one suit-scale view, and appends the files to validation_report.json.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import bpy  # type: ignore
from mathutils import Vector  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from powersuit_pipeline_common import (  # noqa: E402
    PIPELINE_TEMP_PREFIX,
    activate_action,
    body_basis,
    bone_head_world,
    ensure_directory,
    ensure_object_mode,
    get_armature,
    get_rifle_root,
    object_tree,
    remove_pipeline_temps,
    create_static_render_scene,
    detach_rifle_for_validation,
    remove_static_render_scene,
    restore_rifle_after_validation,
    require_blender_52,
    set_camera_look_at,
    sync_detached_rifle_to_hand,
    update_static_render_proxies,
    world_bounds,
    write_json,
)

from weapon_handling_contract import (  # noqa: E402
    COMPONENT_OPTIC,
    ROLE_PRIMARY_GRIP,
    ROLE_SIGHT_OCULAR,
    ROLE_STOCK_CONTACT,
    ROLE_SUPPORT_GRIP,
    assert_weapon_rigid,
    require_weapon_helper,
    validate_weapon_contract,
    weapon_local_position,
    weapon_components,
)

REQUIRED_RIFLE_RENDERS = (
    "rifle_left_side_closeup.png",
    "rifle_right_side_closeup.png",
    "rifle_front_3q_closeup.png",
    "rifle_rear_3q_closeup.png",
    "rifle_with_suit_scale.png",
)

SUIT_SCALE_BONES = {
    "Hips", "Spine", "Chest", "Neck", "Head",
    "Shoulder.R", "UpperArm.R", "LowerArm.R", "Hand.R",
    "Shoulder.L", "UpperArm.L", "LowerArm.L", "Hand.L",
    "UpperLeg.R", "UpperLeg.L",
}


def _create_camera_and_lights(
    render_scene: bpy.types.Scene,
    render_collection: bpy.types.Collection,
):
    camera_data = bpy.data.cameras.new(PIPELINE_TEMP_PREFIX + "RifleCameraData")
    camera = bpy.data.objects.new(PIPELINE_TEMP_PREFIX + "RifleCamera", camera_data)
    render_collection.objects.link(camera)
    camera_data.lens = 70.0
    camera_data.sensor_width = 36.0
    render_scene.camera = camera
    return camera, []

def _position_lights(lights, center: Vector, right: Vector, forward: Vector, up: Vector):
    positions = (
        center - right * 1.4 - forward * 0.8 + up * 1.4,
        center + right * 1.2 + forward * 0.3 + up * 0.5,
        center + forward * 1.0 + up * 1.2,
    )
    for light, position in zip(lights, positions):
        light.location = position
        set_camera_look_at(light, position, center)


def _rifle_axes(root: bpy.types.Object):
    rotation = root.matrix_world.to_3x3()
    right = (rotation @ Vector((1, 0, 0))).normalized()
    forward = (rotation @ Vector((0, 1, 0))).normalized()
    up = (rotation @ Vector((0, 0, 1))).normalized()
    if right.cross(forward).dot(up) < 0.999:
        raise RuntimeError("RifleRoot axes are reflected or non-orthogonal.")
    return right, forward, up


def _validate_contact_geometry(root: bpy.types.Object) -> dict[str, object]:
    """Validate the rigid sniper asset and collect reviewable geometry blockers.

    Structural corruption still aborts immediately.  Ergonomic/style problems are
    returned as blockers so all mandatory rifle renders are produced in the same
    run and the user is not forced to discover one tolerance at a time.
    """
    contract = validate_weapon_contract(root)
    assert_weapon_rigid(root)
    required = {
        name: bpy.data.objects.get(name)
        for name in (
            "Rifle_SupportGrip_Mount",
            "Rifle_SupportGrip",
            "Rifle_Handguard_BottomRail",
            "Rifle_Stock_ButtPad",
            "Rifle_PistolGrip",
            "Rifle_ScopeTube",
            "Rifle_ScopeOcular",
            "Rifle_ScopeObjective",
        )
    }
    missing = [name for name, obj in required.items() if obj is None]
    if missing:
        raise RuntimeError("Rifle contact geometry is incomplete: " + ", ".join(missing))
    if int(root.get("ps_generator_version", 0)) < 102:
        raise RuntimeError("Rifle predates Weapon Framework Reset 06.")

    # Scope and bore-related sight geometry must stay on the rifle centreline.
    # A powered-suit stock is explicitly allowed to be laterally offset as a
    # designed ergonomic feature; Reset 05's validator incorrectly required the
    # stock hardpoint to be centred and contradicted the weapon design itself.
    optic = weapon_components(root, COMPONENT_OPTIC)
    if not optic:
        raise RuntimeError("Weapon contract has no optic-tagged rifle geometry.")
    optic_axis_names = {
        "Rifle_ScopeTube", "Rifle_ScopeObjective", "Rifle_ScopeOcular",
        "Rifle_ScopeLensFront", "Rifle_ScopeLensRear",
        "Rifle_ScopeMount_Rear", "Rifle_ScopeMount_Front",
    }
    optic_axis_parts = [obj for obj in optic if obj.name in optic_axis_names]
    if len(optic_axis_parts) != len(optic_axis_names):
        raise RuntimeError("Required centred optic-axis parts are missing.")
    optic_lateral = [
        abs(float(weapon_local_position(root, obj).x)) for obj in optic_axis_parts
    ]
    max_optic_lateral = max(optic_lateral, default=0.0)
    if max_optic_lateral > 0.012:
        raise RuntimeError(
            f"Sniper optic axis is laterally warped/off-centre ({max_optic_lateral:.3f} m)."
        )

    def maximum_dimension(name: str) -> float:
        obj = required[name]
        corners = [Vector(corner) for corner in obj.bound_box]
        if not corners:
            return 0.0
        minimum = Vector(tuple(min(point[i] for point in corners) for i in range(3)))
        maximum = Vector(tuple(max(point[i] for point in corners) for i in range(3)))
        return max(float(value) for value in (maximum - minimum))

    blockers: list[str] = []
    mount_max = maximum_dimension("Rifle_SupportGrip_Mount")
    grip_max = maximum_dimension("Rifle_SupportGrip")
    lower_rail_max = maximum_dimension("Rifle_Handguard_BottomRail")
    pad_max = maximum_dimension("Rifle_Stock_ButtPad")
    if mount_max > 0.090:
        blockers.append(f"support-grip mount oversized={mount_max:.3f} m")
    if grip_max > 0.150:
        blockers.append(f"support grip oversized={grip_max:.3f} m")
    if lower_rail_max > 0.095:
        blockers.append(f"handguard lower rail oversized={lower_rail_max:.3f} m")
    if pad_max > 0.150:
        blockers.append(f"rifle buttpad oversized={pad_max:.3f} m")

    support_helper = weapon_local_position(
        root, require_weapon_helper(root, ROLE_SUPPORT_GRIP)
    )
    grip_center = weapon_local_position(root, required["Rifle_SupportGrip"])
    support_to_grip = (support_helper - grip_center).length
    if support_to_grip > 0.060:
        blockers.append(f"support helper/foregrip drift={support_to_grip:.3f} m")

    primary_helper = weapon_local_position(
        root, require_weapon_helper(root, ROLE_PRIMARY_GRIP)
    )
    pistol_center = weapon_local_position(root, required["Rifle_PistolGrip"])
    primary_to_grip = (primary_helper - pistol_center).length
    if primary_to_grip > 0.110:
        blockers.append(f"primary helper/pistol-grip drift={primary_to_grip:.3f} m")

    stock_helper = weapon_local_position(
        root, require_weapon_helper(root, ROLE_STOCK_CONTACT)
    )
    sight_helper = weapon_local_position(
        root, require_weapon_helper(root, ROLE_SIGHT_OCULAR)
    )
    buttpad_center = weapon_local_position(root, required["Rifle_Stock_ButtPad"])
    stock_to_buttpad = (stock_helper - buttpad_center).length
    stock_lateral = abs(float(stock_helper.x))
    sight_lateral = abs(float(sight_helper.x))

    # Sight stays centred.  Stock may dogleg, but its semantic contact point must
    # remain attached to the physical buttpad and the offset must stay plausible.
    if sight_lateral > 0.012:
        raise RuntimeError(
            f"Sight hardpoint is off the rigid optic axis ({sight_lateral:.3f} m)."
        )
    if stock_to_buttpad > 0.040:
        blockers.append(
            f"stock hardpoint/buttpad separation={stock_to_buttpad:.3f} m"
        )
    if stock_lateral > 0.140:
        blockers.append(f"stock lateral ergonomic offset={stock_lateral:.3f} m")

    return {
        "weapon_contract_version": float(contract["contract_version"]),
        "max_optic_lateral_offset_m": max_optic_lateral,
        "sight_hardpoint_lateral_offset_m": sight_lateral,
        "stock_hardpoint_lateral_offset_m": stock_lateral,
        "stock_hardpoint_to_buttpad_center_m": stock_to_buttpad,
        "support_mount_max_dimension_m": mount_max,
        "support_grip_max_dimension_m": grip_max,
        "handguard_lower_rail_max_dimension_m": lower_rail_max,
        "buttpad_max_dimension_m": pad_max,
        "support_helper_to_grip_center_m": support_to_grip,
        "primary_helper_to_grip_center_m": primary_to_grip,
        "automated_blockers": blockers,
    }

def _suit_scale_meshes(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> list[bpy.types.Object]:
    meshes = [obj for obj in object_tree(root) if obj.type == "MESH"]
    meshes.extend(
        obj for obj in bpy.data.objects
        if obj.type == "MESH" and obj.parent == armature
        and obj.parent_type == "BONE" and obj.parent_bone in SUIT_SCALE_BONES
    )
    return list(dict.fromkeys(meshes))


def _render(
    camera: bpy.types.Object,
    lights,
    root: bpy.types.Object,
    filename: str,
    view: str,
    output_dir: Path,
    armature: bpy.types.Object,
    rifle_state: dict[str, object],
    render_scene: bpy.types.Scene,
    proxies: dict[str, bpy.types.Object],
    rifle_names: set[str],
    suit_scale_names: set[str],
) -> Path:
    activate_action(armature, "PS_Aim")
    bpy.context.scene.frame_set(1)
    sync_detached_rifle_to_hand(armature, root, rifle_state)
    visible_proxy_names = set(suit_scale_names if view == "suit_scale" else rifle_names)
    update_static_render_proxies(proxies, visible_names=visible_proxy_names)

    rifle_meshes = [obj for obj in object_tree(root) if obj.type == "MESH"]
    framed_meshes = _suit_scale_meshes(armature, root) if view == "suit_scale" else rifle_meshes
    minimum, maximum = world_bounds(framed_meshes)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    radius = max(size.x, size.y, size.z, 1.0)
    right, forward, up = _rifle_axes(root)

    if view == "left":
        target = center
        location = center - right * (radius * 1.28) + up * (radius * 0.06)
        camera.data.lens = 76.0
    elif view == "right":
        target = center
        location = center + right * (radius * 1.28) + up * (radius * 0.06)
        camera.data.lens = 76.0
    elif view == "front_3q":
        target = center
        location = center + forward * (radius * 1.20) - right * (radius * 0.48) + up * (radius * 0.24)
        camera.data.lens = 72.0
    elif view == "rear_3q":
        target = center
        location = center - forward * (radius * 1.20) + right * (radius * 0.48) + up * (radius * 0.24)
        camera.data.lens = 72.0
    elif view == "suit_scale":
        body_right, body_forward, body_up = body_basis(armature)
        chest = bone_head_world(armature, "Chest")
        head = bone_head_world(armature, "Head")
        target = center.lerp((chest + head) * 0.5, 0.30)
        location = (
            target
            + body_forward * (radius * 1.45)
            + body_right * (radius * 0.62)
            + body_up * (radius * 0.24)
        )
        right, forward, up = body_right, body_forward, body_up
        camera.data.lens = 64.0
    else:
        raise ValueError(view)

    set_camera_look_at(camera, location, target)
    _position_lights(lights, target, right, forward, up)
    path = output_dir / filename
    render_scene.render.filepath = str(path)
    print(f"[Rifle validation] Render {filename}: view {view}", flush=True)
    bpy.ops.render.render(write_still=True, scene=render_scene.name)
    if not path.exists() or path.stat().st_size < 4096:
        raise RuntimeError(f"Rifle render was not written correctly: {path}")
    print(f"Rendered: {path}")
    return path


def _append_report(paths: list[Path], geometry_metrics: dict[str, object]) -> Path:
    report_path = ensure_directory("renders") / "validation_report.json"
    if not report_path.exists():
        raise RuntimeError(
            "Run render_animation_validation.py before rifle validation."
        )
    report = json.loads(report_path.read_text(encoding="utf-8"))
    blend_parent = Path(bpy.data.filepath).resolve().parent
    report["rifle_render_files"] = [str(path.relative_to(blend_parent)) for path in paths]
    report["rifle_render_set_complete"] = {
        path.name for path in paths
    } == set(REQUIRED_RIFLE_RENDERS)

    combined_blockers = list(report.get("automated_blockers", []))
    for blocker in geometry_metrics.get("automated_blockers", []):
        text = str(blocker)
        if text not in combined_blockers:
            combined_blockers.append(text)
    report["automated_blockers"] = combined_blockers
    report["automated_validation"] = (
        "PASS" if not combined_blockers else "REVIEW_BLOCKED"
    )
    report["rifle_contact_geometry_metrics"] = geometry_metrics
    report["visual_validation"] = "NOT_REVIEWED"
    report["export_allowed"] = False
    write_json(report_path, report)
    return report_path

def main() -> None:
    require_blender_52()
    ensure_object_mode()
    armature = get_armature()
    root = get_rifle_root()

    print("[Rifle validation] Checking compact contact geometry...", flush=True)
    geometry_metrics = _validate_contact_geometry(root)
    geometry_blockers = list(geometry_metrics.get("automated_blockers", []))
    if geometry_blockers:
        print("[Rifle validation] Review blockers detected; rendering anyway:", flush=True)
        for blocker in geometry_blockers:
            print(f"  - {blocker}", flush=True)

    print("[Rifle validation] Detaching RifleRoot temporarily for safe rendering...", flush=True)
    rifle_state = detach_rifle_for_validation(armature, root)
    try:
        activate_action(armature, "PS_Aim")
        bpy.context.scene.frame_set(1)
        sync_detached_rifle_to_hand(armature, root, rifle_state)

        output_dir = ensure_directory("renders", "rifle_validation")
        rifle_sources = [obj for obj in object_tree(root) if obj.type == "MESH"]
        suit_scale_sources = _suit_scale_meshes(armature, root)
        source_objects = list(dict.fromkeys([*rifle_sources, *suit_scale_sources]))
        rifle_names = {obj.name for obj in rifle_sources}
        suit_scale_names = {obj.name for obj in suit_scale_sources}

        render_scene = None
        render_collection = None
        proxies: dict[str, bpy.types.Object] = {}
        paths = []
        try:
            render_scene, render_collection, proxies = create_static_render_scene(
                PIPELINE_TEMP_PREFIX + "RifleRenderScene",
                source_objects,
            )
            camera, lights = _create_camera_and_lights(render_scene, render_collection)
            render_scene.render.engine = "BLENDER_WORKBENCH"
            render_scene.display.shading.color_type = "OBJECT"
            render_scene.display.shading.light = "STUDIO"
            render_scene.display.shading.show_shadows = True
            render_scene.display.shading.show_cavity = True
            render_scene.display.shading.cavity_type = "BOTH"
            render_scene.render.resolution_x = 1400
            render_scene.render.resolution_y = 900
            render_scene.render.resolution_percentage = 100
            render_scene.render.image_settings.file_format = "PNG"
            render_scene.render.image_settings.color_mode = "RGBA"
            render_scene.render.film_transparent = False

            print("[Rifle validation] Rendering isolated rifle views...", flush=True)
            for filename, view in (
                ("rifle_left_side_closeup.png", "left"),
                ("rifle_right_side_closeup.png", "right"),
                ("rifle_front_3q_closeup.png", "front_3q"),
                ("rifle_rear_3q_closeup.png", "rear_3q"),
            ):
                paths.append(_render(
                    camera, lights, root, filename, view, output_dir, armature, rifle_state,
                    render_scene, proxies, rifle_names, suit_scale_names,
                ))
            paths.append(_render(
                camera, lights, root, "rifle_with_suit_scale.png", "suit_scale",
                output_dir, armature, rifle_state, render_scene, proxies,
                rifle_names, suit_scale_names,
            ))
        finally:
            remove_static_render_scene(render_scene, render_collection, proxies)
            remove_pipeline_temps()

        if {path.name for path in paths} != set(REQUIRED_RIFLE_RENDERS):
            raise RuntimeError("Mandatory rifle render set is incomplete.")
        report_path = _append_report(paths, geometry_metrics)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        print("\nRifle validation renders complete.")
        print("Visual approval is still required before FBX export.")
        print(f"Report: {report_path}")
    finally:
        try:
            activate_action(armature, "PS_Aim")
            bpy.context.scene.frame_set(1)
            sync_detached_rifle_to_hand(armature, root, rifle_state)
        finally:
            restore_rifle_after_validation(armature, root, rifle_state)


if __name__ == "__main__":
    main()
