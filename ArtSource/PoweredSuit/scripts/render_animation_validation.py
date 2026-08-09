# pyright: reportMissingImports=false
"""Validate Blender 5.2 Actions/Slots and render mandatory aim close-ups.

This script performs automated structural and pose-difference validation, then
creates the exact required aim-validation images. It deliberately records visual
approval as NOT_REVIEWED; human image inspection remains mandatory before export.
"""
from __future__ import annotations

import hashlib
import math
import sys
from pathlib import Path

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore
from mathutils.bvhtree import BVHTree  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from powersuit_pipeline_common import (  # noqa: E402
    PIPELINE_TEMP_PREFIX,
    RIFLE_ROOT_NAME,
    activate_action,
    action_rotation_modes,
    action_slot_curve_stats,
    body_basis,
    bone_head_world,
    bone_tail_world,
    ensure_directory,
    expected_transform_curve_count,
    ensure_object_mode,
    evaluated_pose_matrices,
    find_action_slot,
    get_armature,
    get_rifle_root,
    matrix_world_for_pose_bone,
    named_shoulder_outward_axes,
    object_tree,
    quaternion_angle_degrees,
    remove_pipeline_temps,
    create_static_render_scene,
    detach_rifle_for_validation,
    remove_static_render_scene,
    restore_rifle_after_validation,
    require_character_asset_versions,
    require_blender_52,
    set_camera_look_at,
    sync_detached_rifle_to_hand,
    update_static_render_proxies,
    world_bounds,
    write_json,
)

from weapon_handling_contract import (  # noqa: E402
    COMPONENT_BOLT,
    COMPONENT_MAGAZINE,
    COMPONENT_OPTIC,
    COMPONENT_STOCK,
    ROLE_MUZZLE,
    ROLE_PRIMARY_GRIP,
    ROLE_SIGHT_OCULAR,
    ROLE_STOCK_CONTACT,
    ROLE_SUPPORT_GRIP,
    assert_weapon_rigid,
    get_stance_profile,
    require_weapon_helper,
    validate_weapon_contract,
    weapon_contact_surfaces,
    weapon_components,
    weapon_contract_objects,
    weapon_local_position,
)

COMPARE_BONES = (
    "Chest", "Head",
    "Shoulder.R", "UpperArm.R", "LowerArm.R", "Hand.R",
    "Shoulder.L", "UpperArm.L", "LowerArm.L", "Hand.L",
)
LOWER_BODY_BONES = (
    "Root", "Hips",
    "UpperLeg.L", "LowerLeg.L", "Foot.L",
    "UpperLeg.R", "LowerLeg.R", "Foot.R",
)
REQUIRED_AIM_RENDERS = (
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
)
UPPER_PARENT_BONES = {
    "Spine", "Chest", "Neck", "Head",
    "Shoulder.R", "UpperArm.R", "LowerArm.R", "Hand.R",
    "Shoulder.L", "UpperArm.L", "LowerArm.L", "Hand.L",
}


def _slot_summary(action: bpy.types.Action, armature: bpy.types.Object) -> dict[str, object]:
    slot = find_action_slot(action, armature)
    stats = action_slot_curve_stats(action, slot)
    rotation_modes = action_rotation_modes(action, slot)
    return {
        "action": action.name,
        "slot_name": str(getattr(slot, "name", "")),
        "slot_identifier": str(getattr(slot, "identifier", "")),
        "slot_target_id_type": str(getattr(slot, "target_id_type", "")),
        "slot_count": len(list(action.slots)),
        "rotation_modes": rotation_modes,
        **stats,
    }


def _evaluate(
    armature: bpy.types.Object,
    action_name: str,
    frame: int,
    bones=None,
) -> tuple[dict[str, Matrix], dict[str, object]]:
    action, slot = activate_action(armature, action_name)
    # Force a time change after Action/Slot switching.  This avoids reading a
    # cached pose when the requested frame already equals scene.frame_current.
    neighbour = frame + 1 if frame < 30 else frame - 1
    bpy.context.scene.frame_set(neighbour)
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    adt = armature.animation_data
    if adt is None or adt.action != action or adt.action_slot != slot:
        raise RuntimeError(
            f"Blender 5.2 failed to keep Action/Slot active for {action_name}."
        )
    return evaluated_pose_matrices(armature, bones), _slot_summary(action, armature)


def _translation_delta(a: Matrix, b: Matrix) -> float:
    return (a.translation - b.translation).length


def _pose_comparison(
    idle: dict[str, Matrix],
    aim: dict[str, Matrix],
) -> dict[str, object]:
    per_bone = {}
    for name in COMPARE_BONES:
        angle = quaternion_angle_degrees(idle[name], aim[name])
        translation = _translation_delta(idle[name], aim[name])
        per_bone[name] = {
            "rotation_delta_degrees": angle,
            "translation_delta_m": translation,
        }
    max_angle = max(item["rotation_delta_degrees"] for item in per_bone.values())
    max_translation = max(item["translation_delta_m"] for item in per_bone.values())
    changed = [
        name for name, values in per_bone.items()
        if values["rotation_delta_degrees"] >= 1.0
        or values["translation_delta_m"] >= 0.01
    ]
    if max_angle < 3.0 and max_translation < 0.025:
        raise RuntimeError(
            "PS_Idle and PS_Aim evaluate to effectively identical upper-body poses: "
            f"max rotation {max_angle:.3f}°, max translation {max_translation:.4f} m."
        )
    if len(changed) < 6:
        raise RuntimeError(
            "PS_Aim does not visibly involve enough required upper-body bones: "
            + ", ".join(changed)
        )
    return {
        "max_rotation_delta_degrees": max_angle,
        "max_translation_delta_m": max_translation,
        "changed_required_bones": changed,
        "per_bone": per_bone,
    }


def _matrix_max_delta(a: Matrix, b: Matrix) -> float:
    return max(abs(a[row][column] - b[row][column]) for row in range(4) for column in range(4))


def _validate_animation_invariants(armature: bpy.types.Object) -> dict[str, object]:
    require_character_asset_versions(armature)
    aim_action = bpy.data.actions["PS_Aim"]
    aim_data = _slot_summary(aim_action, armature)
    aim_slot_object = find_action_slot(aim_action, armature)
    expected_curves = expected_transform_curve_count(
        armature, aim_action, aim_slot_object
    )
    if aim_data["slot_count"] != 1:
        raise RuntimeError(
            f"PS_Aim must have exactly one slot; found {aim_data['slot_count']}."
        )
    if aim_data["curve_count"] != expected_curves or aim_data["empty_curve_count"]:
        raise RuntimeError(
            "PS_Aim explicit Action Slot is missing baked curves: " + str(aim_data)
        )

    idle_action = bpy.data.actions["PS_Idle"]
    idle_slot_object = find_action_slot(idle_action, armature)
    idle_modes = action_rotation_modes(idle_action, idle_slot_object)
    aim_modes = action_rotation_modes(aim_action, aim_slot_object)
    incompatible_modes = {
        name: (idle_modes.get(name), aim_modes.get(name))
        for name in aim_modes
        if idle_modes.get(name) != aim_modes.get(name)
    }
    if incompatible_modes:
        raise RuntimeError(
            "PS_Aim rotation representations do not match PS_Idle: "
            + ", ".join(
                f"{name}={modes[0]}->{modes[1]}"
                for name, modes in sorted(incompatible_modes.items())
            )
        )

    idle_upper, idle_slot = _evaluate(armature, "PS_Idle", 1, COMPARE_BONES)
    aim_upper, aim_slot = _evaluate(armature, "PS_Aim", 1, COMPARE_BONES)
    comparison = _pose_comparison(idle_upper, aim_upper)

    idle_lower, _ = _evaluate(armature, "PS_Idle", 1, LOWER_BODY_BONES)
    aim_lower, _ = _evaluate(armature, "PS_Aim", 1, LOWER_BODY_BONES)
    lower_deltas = {
        name: _matrix_max_delta(idle_lower[name], aim_lower[name])
        for name in LOWER_BODY_BONES
    }
    bad_lower = {name: value for name, value in lower_deltas.items() if value > 1.0e-5}
    if bad_lower:
        raise RuntimeError(
            "PS_Aim failed to preserve the PS_Idle lower body: "
            + ", ".join(f"{name}={value:.2e}" for name, value in bad_lower.items())
        )

    aim_1, _ = _evaluate(armature, "PS_Aim", 1)
    aim_15, _ = _evaluate(armature, "PS_Aim", 15)
    aim_30, _ = _evaluate(armature, "PS_Aim", 30)
    loop_delta = max(_matrix_max_delta(aim_1[name], aim_30[name]) for name in aim_1)
    stabilization_delta = max(_matrix_max_delta(aim_1[name], aim_15[name]) for name in aim_1)
    if loop_delta > 1.0e-5:
        raise RuntimeError(f"PS_Aim frame 30 is not an exact copy of frame 1 ({loop_delta:.2e}).")
    if stabilization_delta < 1.0e-5:
        raise RuntimeError("PS_Aim frame 15 has no stabilization variation.")

    root_locations = []
    for frame in (1, 15, 30):
        matrices, _ = _evaluate(armature, "PS_Aim", frame, ("Root",))
        root_locations.append(matrices["Root"].translation.copy())
    root_motion = max((root_locations[i] - root_locations[0]).length for i in range(1, 3))
    if root_motion > 1.0e-6:
        raise RuntimeError(f"PS_Aim contains root motion ({root_motion:.8f} m).")

    return {
        "aim_action_data": aim_data,
        "idle_slot": idle_slot,
        "aim_slot": aim_slot,
        "idle_vs_aim": comparison,
        "lower_body_max_matrix_delta": max(lower_deltas.values()),
        "frame_1_to_30_max_matrix_delta": loop_delta,
        "frame_1_to_15_max_matrix_delta": stabilization_delta,
        "root_motion_m": root_motion,
    }


def _validate_hierarchy(armature: bpy.types.Object, root: bpy.types.Object) -> dict[str, object]:
    if root.parent != armature or root.parent_type != "BONE" or root.parent_bone != "WeaponRoot":
        raise RuntimeError(
            "RifleRoot must be bone-parented to the verified WeaponRoot carrier."
        )
    expected_articulated = {
        obj.name: ("WeaponMagazine" if str(obj.get("ps_weapon_component_role", "")) == COMPONENT_MAGAZINE else "WeaponBolt")
        for obj in (
            weapon_components(root, COMPONENT_MAGAZINE)
            + weapon_components(root, COMPONENT_BOLT)
        )
    }
    direct_bone_rifle = [
        obj.name for obj in bpy.data.objects
        if obj.parent == armature and obj.parent_type == "BONE"
        and (obj.name == RIFLE_ROOT_NAME or obj.name.startswith("Rifle_"))
    ]
    expected_direct = {RIFLE_ROOT_NAME, *expected_articulated}
    if set(direct_bone_rifle) != expected_direct:
        raise RuntimeError(
            "Unexpected rifle objects directly bone-parented: "
            + ", ".join(sorted(direct_bone_rifle))
        )
    bad_articulated = [
        f"{name}->{bpy.data.objects[name].parent_bone}"
        for name, expected_bone in expected_articulated.items()
        if bpy.data.objects[name].parent_bone != expected_bone
    ]
    if bad_articulated:
        raise RuntimeError(
            "Articulated rifle components use wrong control bones: "
            + ", ".join(bad_articulated)
        )
    stray = [
        obj.name for obj in bpy.data.objects
        if obj.name.startswith("Rifle_")
        and obj.parent != root
        and obj.name not in expected_articulated
    ]
    if stray:
        raise RuntimeError("Stray rifle objects outside RifleRoot: " + ", ".join(sorted(stray)))
    temp_objects = [obj.name for obj in bpy.data.objects if obj.name.startswith(PIPELINE_TEMP_PREFIX)]
    if temp_objects:
        raise RuntimeError("Temporary solve objects remain: " + ", ".join(temp_objects))
    ik_constraints = [
        f"{bone.name}:{constraint.name}"
        for bone in armature.pose.bones
        for constraint in bone.constraints
        if constraint.type == "IK"
    ]
    if ik_constraints:
        raise RuntimeError("Active IK remains after bake: " + ", ".join(ik_constraints))
    if root.matrix_world.to_3x3().determinant() <= 0.0:
        raise RuntimeError("RifleRoot has a reflected world transform.")
    contract = validate_weapon_contract(root)
    assert_weapon_rigid(root)
    return {
        "weapon_contract": contract,
        "rifle_root_parent": armature.name,
        "rifle_root_parent_type": root.parent_type,
        "rifle_root_parent_bone": root.parent_bone,
        "rifle_root_children": sorted(child.name for child in root.children),
        "rifle_root_child_count": len(root.children),
        "direct_bone_parented_rifle_objects": direct_bone_rifle,
        "temporary_objects": temp_objects,
        "active_ik_constraints": ik_constraints,
        "rifle_world_determinant": root.matrix_world.to_3x3().determinant(),
    }


def _hand_helper_metrics(armature: bpy.types.Object) -> dict[str, float]:
    _evaluate(armature, "PS_Aim", 1)
    root = get_rifle_root()
    right_target = require_weapon_helper(root, ROLE_PRIMARY_GRIP).matrix_world.translation
    left_target = require_weapon_helper(root, ROLE_SUPPORT_GRIP).matrix_world.translation
    right_hand = bone_head_world(armature, "Hand.R")
    left_hand = bone_head_world(armature, "Hand.L")
    right_error = (right_hand - right_target).length
    left_error = (left_hand - left_target).length
    return {
        "right_wrist_to_primary_wrist_target_m": right_error,
        "left_wrist_to_support_wrist_target_m": left_error,
        # Compatibility aliases for older validation-report readers.
        "right_hand_to_grip_target_m": right_error,
        "left_hand_to_foregrip_target_m": left_error,
    }


def _upper_body_meshes(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    *,
    include_rifle: bool,
) -> list[bpy.types.Object]:
    result = []
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        if obj.parent == armature and obj.parent_type == "BONE" and obj.parent_bone in UPPER_PARENT_BONES:
            result.append(obj)
    if include_rifle:
        result.extend(
            obj for obj in weapon_contract_objects(root) if obj.type == "MESH"
        )
    return list(dict.fromkeys(result))


def _elbow_bend_degrees(shoulder: Vector, elbow: Vector, wrist: Vector) -> float:
    to_shoulder = shoulder - elbow
    to_wrist = wrist - elbow
    if to_shoulder.length < 1.0e-6 or to_wrist.length < 1.0e-6:
        return 180.0
    dot = max(-1.0, min(1.0, to_shoulder.normalized().dot(to_wrist.normalized())))
    return math.degrees(math.acos(dot))


def _bounds_overlap_volume(
    first: tuple[Vector, Vector],
    second: tuple[Vector, Vector],
) -> float:
    first_min, first_max = first
    second_min, second_max = second
    dimensions = [
        max(0.0, min(first_max[index], second_max[index]) - max(first_min[index], second_min[index]))
        for index in range(3)
    ]
    return dimensions[0] * dimensions[1] * dimensions[2]


def _world_bvh_from_meshes(objects: list[bpy.types.Object]) -> BVHTree | None:
    """Build one evaluated world-space BVH for a group of mesh objects.

    Axis-aligned bounds are intentionally not sufficient for the scope/helmet
    contact gate once the head yaws toward the optic: two rotated objects can
    have overlapping AABBs while their real surfaces remain separated.  This
    helper uses the evaluated meshes (including the existing bevel modifiers),
    transforms every vertex to world space, and aggregates their polygons into
    one BVH.  It does not alter the scene and is safe to call before the static
    proxy render stage.
    """
    if not objects:
        return None

    depsgraph = bpy.context.evaluated_depsgraph_get()
    vertices: list[Vector] = []
    polygons: list[tuple[int, ...]] = []

    for source in objects:
        if source.type != "MESH":
            continue
        evaluated = source.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
        if mesh is None:
            continue
        try:
            offset = len(vertices)
            world = evaluated.matrix_world
            vertices.extend(world @ vertex.co for vertex in mesh.vertices)
            polygons.extend(
                tuple(offset + int(index) for index in polygon.vertices)
                for polygon in mesh.polygons
                if len(polygon.vertices) >= 3
            )
        finally:
            evaluated.to_mesh_clear()

    if not vertices or not polygons:
        return None
    return BVHTree.FromPolygons(vertices, polygons, all_triangles=False, epsilon=1.0e-7)


def _mesh_overlap_pair_count(
    first: list[bpy.types.Object],
    second: list[bpy.types.Object],
) -> int:
    first_bvh = _world_bvh_from_meshes(first)
    second_bvh = _world_bvh_from_meshes(second)
    if first_bvh is None or second_bvh is None:
        return 0
    return len(first_bvh.overlap(second_bvh))


def _minimum_mesh_vertex_surface_distance(
    first: list[bpy.types.Object],
    second: list[bpy.types.Object],
) -> float:
    """Return a conservative evaluated-mesh surface distance in world space."""
    second_bvh = _world_bvh_from_meshes(second)
    if second_bvh is None:
        return math.inf
    depsgraph = bpy.context.evaluated_depsgraph_get()
    minimum = math.inf
    for source in first:
        if source.type != "MESH":
            continue
        evaluated = source.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(
            preserve_all_data_layers=False, depsgraph=depsgraph
        )
        if mesh is None:
            continue
        try:
            world = evaluated.matrix_world
            for vertex in mesh.vertices:
                nearest = second_bvh.find_nearest(world @ vertex.co)
                if nearest is not None and nearest[3] is not None:
                    minimum = min(minimum, float(nearest[3]))
        finally:
            evaluated.to_mesh_clear()
    return minimum



def _elbow_plane_metrics(
    shoulder: Vector,
    elbow: Vector,
    wrist: Vector,
    outward_axis: Vector,
    forward: Vector,
    up: Vector,
) -> dict[str, float]:
    """Return elbow clearance from the direct shoulder-to-wrist chord."""
    span = wrist - shoulder
    if span.length_squared < 1.0e-10:
        chord_point = shoulder.copy()
    else:
        parameter = (elbow - shoulder).dot(span) / span.length_squared
        parameter = max(0.0, min(1.0, parameter))
        chord_point = shoulder + span * parameter
    offset = elbow - chord_point
    shoulder_offset = elbow - shoulder
    return {
        "outward_clearance_m": offset.dot(outward_axis),
        "down_clearance_m": -offset.dot(up),
        "absolute_outward_m": shoulder_offset.dot(outward_axis),
        "absolute_down_m": -shoulder_offset.dot(up),
        "absolute_forward_m": shoulder_offset.dot(forward),
        "bend_deg": _elbow_bend_degrees(shoulder, elbow, wrist),
    }

def _aim_pose_geometry_metrics(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> dict[str, float]:
    _evaluate(armature, "PS_Aim", 1)
    assert_weapon_rigid(root)
    contract = validate_weapon_contract(root)
    profile = get_stance_profile(str(root["ps_weapon_stance_family"]))
    right, forward, up = body_basis(armature)
    shoulder_r = bone_head_world(armature, "UpperArm.R")
    shoulder_l = bone_head_world(armature, "UpperArm.L")
    outward_r, outward_l = named_shoulder_outward_axes(
        armature, right, forward, up
    )
    elbow_r = armature.matrix_world @ armature.pose.bones["UpperArm.R"].tail
    elbow_l = armature.matrix_world @ armature.pose.bones["UpperArm.L"].tail
    wrist_r = bone_head_world(armature, "Hand.R")
    wrist_l = bone_head_world(armature, "Hand.L")

    right_elbow = _elbow_plane_metrics(
        shoulder_r, elbow_r, wrist_r, outward_r, forward, up
    )
    left_elbow = _elbow_plane_metrics(
        shoulder_l, elbow_l, wrist_l, outward_l, forward, up
    )

    stock_helper = require_weapon_helper(root, ROLE_STOCK_CONTACT)
    sight_helper = require_weapon_helper(root, ROLE_SIGHT_OCULAR)
    stock_world = stock_helper.matrix_world.translation.copy()
    stock_anchor = Vector(root.get("ps_aim_shoulder_anchor_world", tuple(stock_world)))
    stock_to_anchor = (stock_world - stock_anchor).length
    expected_stock_to_anchor = math.sqrt(
        profile.stock_inward_m * profile.stock_inward_m
        + profile.stock_forward_m * profile.stock_forward_m
        + profile.stock_up_m * profile.stock_up_m
    )
    stock_anchor_error = abs(stock_to_anchor - expected_stock_to_anchor)
    right_reach_ratio = float(root.get("ps_aim_right_reach_ratio", 999.0))
    left_reach_ratio = float(root.get("ps_aim_left_reach_ratio", 999.0))
    placement_mode = str(root.get("ps_aim_placement_mode", "missing"))
    head_yaw_deg = float(root.get("ps_aim_head_yaw_deg", 0.0))
    head_pitch_deg = float(root.get("ps_aim_head_pitch_deg", 0.0))
    head_roll_deg = float(root.get("ps_aim_head_roll_deg", 0.0))

    visor = bpy.data.objects.get("Helmet_Visor")
    if visor is None:
        raise RuntimeError("Helmet_Visor is missing.")
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated_visor = visor.evaluated_get(depsgraph)
    visor_corners = [
        evaluated_visor.matrix_world @ Vector(corner)
        for corner in evaluated_visor.bound_box
    ]
    visor_center = (
        sum(visor_corners, Vector((0.0, 0.0, 0.0))) / len(visor_corners)
        if visor_corners else evaluated_visor.matrix_world.translation.copy()
    )
    visor_basis = evaluated_visor.matrix_world.to_3x3()
    visor_right = (visor_basis @ Vector((1.0, 0.0, 0.0))).normalized()
    visor_up = (visor_basis @ Vector((0.0, 0.0, 1.0))).normalized()
    visor_normal = (visor_basis @ Vector((0.0, 1.0, 0.0))).normalized()
    if visor_normal.dot(forward) < 0.0:
        visor_normal = -visor_normal
    visor_front = max(
        (point.dot(visor_normal) for point in visor_corners),
        default=visor_center.dot(visor_normal),
    )
    firing_side_sign = 1.0 if outward_r.dot(visor_right) >= 0.0 else -1.0
    aiming_eye = (
        visor_center
        + visor_right * firing_side_sign * profile.aiming_eye_outward_m
    )
    sight_world = sight_helper.matrix_world.translation.copy()
    delta = sight_world - aiming_eye
    sight_lateral = abs(delta.dot(visor_right))
    sight_vertical = abs(delta.dot(visor_up))
    sight_front = sight_world.dot(visor_normal) - visor_front

    rifle_forward = (
        root.matrix_world.to_3x3() @ Vector((0.0, 1.0, 0.0))
    ).normalized()
    forward_dot = rifle_forward.dot(forward)
    sight_axis_dot = max(-1.0, min(1.0, visor_normal.dot(rifle_forward)))
    sight_axis_angle = math.degrees(math.acos(sight_axis_dot))
    if delta.length < 1.0e-6:
        sight_ray_angle = 180.0
    else:
        sight_ray_dot = max(
            -1.0, min(1.0, delta.normalized().dot(rifle_forward))
        )
        sight_ray_angle = math.degrees(math.acos(sight_ray_dot))

    metrics = {
        "stock_contact_to_right_shoulder_m": (stock_world - shoulder_r).length,
        "stock_contact_to_stance_anchor_m": stock_to_anchor,
        "stock_contact_to_expected_stance_offset_error_m": stock_anchor_error,
        "rifle_forward_dot_visual_forward": forward_dot,
        "sight_lateral_m": sight_lateral,
        "sight_vertical_m": sight_vertical,
        "sight_front_clearance_m": sight_front,
        "sight_axis_angle_deg": sight_axis_angle,
        "eye_to_ocular_ray_angle_deg": sight_ray_angle,
        "eye_to_ocular_ray_preferred_deg": profile.sight_ray_preferred_deg,
        "eye_to_ocular_ray_blocker_deg": profile.sight_ray_tolerance_deg,
        "aiming_eye_reference_semantic": profile.aiming_eye_reference_semantic,
        "aiming_eye_outward_m": profile.aiming_eye_outward_m,
        "aiming_eye_reference_world": tuple(float(value) for value in aiming_eye),
        "head_search_violation": float(root.get("ps_aim_head_search_violation", 999.0)),
        "head_search_quality": float(root.get("ps_aim_head_search_quality", 999.0)),
        "head_scope_yaw_deg": head_yaw_deg,
        "head_scope_pitch_deg": head_pitch_deg,
        "head_scope_roll_deg": head_roll_deg,
        "right_reach_ratio": right_reach_ratio,
        "left_reach_ratio": left_reach_ratio,
        "placement_mode": placement_mode,
        "rifle_generator_version": int(root.get("ps_generator_version", 0)),
        "right_elbow_outward_m": right_elbow["outward_clearance_m"],
        "left_elbow_outward_m": left_elbow["outward_clearance_m"],
        "right_elbow_down_m": right_elbow["down_clearance_m"],
        "left_elbow_down_m": left_elbow["down_clearance_m"],
        "right_elbow_forward_m": right_elbow["absolute_forward_m"],
        "left_elbow_forward_m": left_elbow["absolute_forward_m"],
        "right_elbow_bend_deg": right_elbow["bend_deg"],
        "left_elbow_bend_deg": left_elbow["bend_deg"],
        "right_elbow_absolute_outward_m": right_elbow["absolute_outward_m"],
        "left_elbow_absolute_outward_m": left_elbow["absolute_outward_m"],
        "right_elbow_absolute_down_m": right_elbow["absolute_down_m"],
        "left_elbow_absolute_down_m": left_elbow["absolute_down_m"],
    }

    helmet_meshes = [
        obj for obj in bpy.data.objects
        if obj.type == "MESH" and obj.parent == armature
        and obj.parent_type == "BONE" and obj.parent_bone == "Head"
    ]
    optic_meshes = weapon_components(root, COMPONENT_OPTIC)
    if helmet_meshes and optic_meshes:
        metrics["scope_helmet_aabb_overlap_m3"] = _bounds_overlap_volume(
            world_bounds(optic_meshes), world_bounds(helmet_meshes)
        )
        metrics["scope_helmet_mesh_overlap_pairs"] = _mesh_overlap_pair_count(
            optic_meshes, helmet_meshes
        )
    else:
        metrics["scope_helmet_aabb_overlap_m3"] = 0.0
        metrics["scope_helmet_mesh_overlap_pairs"] = 0

    hand_r_mesh = bpy.data.objects.get("Hand.R")
    hand_l_mesh = bpy.data.objects.get("Hand.L")
    primary_surfaces = weapon_contact_surfaces(root, ROLE_PRIMARY_GRIP)
    support_surfaces = weapon_contact_surfaces(root, ROLE_SUPPORT_GRIP)
    stock_surfaces = weapon_contact_surfaces(root, ROLE_STOCK_CONTACT)
    shoulder_pocket_meshes = [
        bpy.data.objects[name]
        for name in (
            "Shoulder_Armour.R", "Upper_Arm.R", "Upper_Chest",
            "Chest_Core", "Chest_Plate.R",
        )
        if bpy.data.objects.get(name) is not None
    ]
    torso_meshes = [
        bpy.data.objects[name]
        for name in (
            "Chest_Core", "Upper_Chest", "Chest_Plate",
            "Chest_Plate.R", "Chest_Plate.L",
        )
        if bpy.data.objects.get(name) is not None
    ]
    non_stock_weapon_meshes = [
        child for child in weapon_contract_objects(root)
        if child.type == "MESH"
        and str(child.get("ps_weapon_component_role", "")) != COMPONENT_STOCK
    ]
    metrics["trigger_hand_grip_mesh_overlap_pairs"] = (
        _mesh_overlap_pair_count([hand_r_mesh], primary_surfaces)
        if hand_r_mesh is not None else 0
    )
    metrics["support_hand_grip_mesh_overlap_pairs"] = (
        _mesh_overlap_pair_count([hand_l_mesh], support_surfaces)
        if hand_l_mesh is not None else 0
    )
    metrics["stock_shoulder_mesh_overlap_pairs"] = _mesh_overlap_pair_count(
        stock_surfaces, shoulder_pocket_meshes
    )
    metrics["stock_shoulder_min_vertex_surface_distance_m"] = (
        _minimum_mesh_vertex_surface_distance(
            stock_surfaces, shoulder_pocket_meshes
        )
    )
    metrics["non_stock_weapon_torso_mesh_overlap_pairs"] = (
        _mesh_overlap_pair_count(non_stock_weapon_meshes, torso_meshes)
    )

    failures = []
    if metrics["scope_helmet_mesh_overlap_pairs"] > 0:
        failures.append(
            "optic/helmet evaluated-mesh intersection pairs="
            f"{int(metrics['scope_helmet_mesh_overlap_pairs'])}"
        )
    if metrics["trigger_hand_grip_mesh_overlap_pairs"] > 24:
        failures.append(
            "trigger hand/pistol-grip heavy mesh intersection pairs="
            f"{int(metrics['trigger_hand_grip_mesh_overlap_pairs'])}"
        )
    if metrics["support_hand_grip_mesh_overlap_pairs"] > 24:
        failures.append(
            "support hand/foregrip heavy mesh intersection pairs="
            f"{int(metrics['support_hand_grip_mesh_overlap_pairs'])}"
        )
    if (
        metrics["stock_shoulder_mesh_overlap_pairs"] == 0
        and metrics["stock_shoulder_min_vertex_surface_distance_m"] > 0.020
    ):
        failures.append(
            "stock/shoulder visible surface gap="
            f"{metrics['stock_shoulder_min_vertex_surface_distance_m']:.3f} m"
        )
    if metrics["stock_shoulder_mesh_overlap_pairs"] > 64:
        failures.append(
            "stock/shoulder heavy mesh intersection pairs="
            f"{int(metrics['stock_shoulder_mesh_overlap_pairs'])}"
        )
    if metrics["non_stock_weapon_torso_mesh_overlap_pairs"] > 0:
        failures.append(
            "non-stock weapon/torso mesh intersection pairs="
            f"{int(metrics['non_stock_weapon_torso_mesh_overlap_pairs'])}"
        )
    if forward_dot < 0.90:
        failures.append(f"weapon/face forward dot={forward_dot:.3f}")
    if stock_anchor_error > 0.005:
        failures.append(
            "stock/stance offset contract error="
            f"{stock_anchor_error:.3f} m"
        )
    if right_reach_ratio > profile.max_reach + 0.0005 or left_reach_ratio > profile.max_reach + 0.0005:
        failures.append(
            f"arm reach ratio=R {right_reach_ratio:.3f}, L {left_reach_ratio:.3f}"
        )
    if sight_lateral > profile.sight_lateral_tolerance_m:
        failures.append(f"sight lateral={sight_lateral:.3f} m")
    if sight_vertical > profile.sight_vertical_tolerance_m:
        failures.append(f"sight vertical={sight_vertical:.3f} m")
    if sight_front < profile.sight_front_min_m or sight_front > profile.sight_front_max_m:
        failures.append(f"sight front clearance={sight_front:.3f} m")
    if sight_axis_angle > profile.sight_axis_tolerance_deg:
        failures.append(f"visor/rifle sight axis={sight_axis_angle:.1f} deg")
    if sight_ray_angle > profile.sight_ray_tolerance_deg:
        failures.append(f"eye-to-ocular sight ray={sight_ray_angle:.1f} deg")
    if abs(head_yaw_deg) > profile.head_yaw_limit_deg + 0.1:
        failures.append(f"head/sight yaw={head_yaw_deg:.1f} deg")
    if abs(head_pitch_deg) > profile.head_pitch_limit_deg + 0.1:
        failures.append(f"head/sight pitch={head_pitch_deg:.1f} deg")
    if abs(head_roll_deg) > profile.head_roll_limit_deg + 0.1:
        failures.append(f"head/sight roll={head_roll_deg:.1f} deg")
    if placement_mode != "stance_family_rigid":
        failures.append(f"placement mode={placement_mode}")
    if metrics["rifle_generator_version"] < 109:
        failures.append(f"rifle generator version={metrics['rifle_generator_version']}")
    for label in ("right_elbow_outward_m", "left_elbow_outward_m"):
        if metrics[label] < -0.015:
            failures.append(f"{label}={metrics[label]:.3f}")
    for label in ("right_elbow_down_m", "left_elbow_down_m"):
        if metrics[label] < -0.020:
            failures.append(f"{label}={metrics[label]:.3f}")
    if metrics["right_elbow_bend_deg"] < 20.0 or metrics["right_elbow_bend_deg"] > 168.0:
        failures.append(f"right_elbow_bend_deg={metrics['right_elbow_bend_deg']:.1f}")
    if metrics["left_elbow_bend_deg"] < 35.0 or metrics["left_elbow_bend_deg"] > 164.0:
        failures.append(f"left_elbow_bend_deg={metrics['left_elbow_bend_deg']:.1f}")
    # Development validation is render-first.  These are export blockers, not
    # reasons to hide the actual pose from visual inspection.  Structural scene
    # corruption is still raised earlier by hierarchy/action/rigidity checks.
    metrics["automated_blockers"] = failures
    metrics["weapon_contract_version"] = float(contract["contract_version"])
    return metrics


def _create_camera_and_lights(
    render_scene: bpy.types.Scene,
    render_collection: bpy.types.Collection,
):
    camera_data = bpy.data.cameras.new(PIPELINE_TEMP_PREFIX + "AimCameraData")
    camera = bpy.data.objects.new(PIPELINE_TEMP_PREFIX + "AimCamera", camera_data)
    render_collection.objects.link(camera)
    camera_data.lens = 58.0
    camera_data.sensor_width = 36.0
    render_scene.camera = camera
    return camera, []

def _position_lights(lights, target: Vector, right: Vector, forward: Vector, up: Vector) -> None:
    positions = (
        target - forward * 2.5 - right * 2.0 + up * 2.2,
        target - forward * 1.7 + right * 2.1 + up * 0.7,
        target + forward * 1.4 - right * 1.2 + up * 2.4,
    )
    for light, location in zip(lights, positions):
        light.location = location
        set_camera_look_at(light, location, target)


def _render_one(
    camera: bpy.types.Object,
    lights,
    armature: bpy.types.Object,
    root: bpy.types.Object,
    rifle_state: dict[str, object],
    render_scene: bpy.types.Scene,
    proxies: dict[str, bpy.types.Object],
    suit_names: set[str],
    rifle_names: set[str],
    action_name: str,
    frame: int,
    view: str,
    output_path: Path,
) -> None:
    _evaluate(armature, action_name, frame)
    sync_detached_rifle_to_hand(armature, root, rifle_state)
    right, forward, up = body_basis(armature)
    include_rifle = action_name == "PS_Aim"
    visible_proxy_names = set(suit_names)
    if include_rifle:
        visible_proxy_names.update(rifle_names)
    update_static_render_proxies(proxies, visible_names=visible_proxy_names)
    visible = _upper_body_meshes(armature, root, include_rifle=include_rifle)
    minimum, maximum = world_bounds(visible)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum

    # Idle views frame only the suit. Aim views include the complete weapon and
    # are biased only slightly toward the torso so the muzzle is not cropped.
    chest = bone_head_world(armature, "Chest")
    head = bone_head_world(armature, "Head")
    torso_target = (chest + head) * 0.5 + forward * 0.08
    target = center.lerp(torso_target, 0.22 if include_rifle else 0.55)
    radius = max(size.x, size.y, size.z, 0.9)

    if view == "front_3q":
        distance = radius * (1.62 if include_rifle else 1.50)
        location = (
            target
            + forward * distance
            + right * (radius * 0.72)
            + up * (radius * 0.24)
        )
        camera.data.lens = 55.0 if include_rifle else 60.0
    elif view == "side":
        distance = radius * (1.76 if include_rifle else 1.60)
        location = (
            target
            + right * distance
            + forward * (radius * 0.10)
            + up * (radius * 0.15)
        )
        camera.data.lens = 55.0 if include_rifle else 60.0
    elif view == "over_shoulder":
        scope = require_weapon_helper(root, ROLE_SIGHT_OCULAR).matrix_world.translation
        muzzle = require_weapon_helper(root, ROLE_MUZZLE).matrix_world.translation
        visor = bpy.data.objects.get("Helmet_Visor")
        visor_point = visor.matrix_world.translation if visor is not None else bone_head_world(armature, "Head")
        rifle_basis = root.matrix_world.to_3x3()
        rifle_forward = (rifle_basis @ Vector((0.0, 1.0, 0.0))).normalized()
        rifle_right = (rifle_basis @ Vector((1.0, 0.0, 0.0))).normalized()
        lateral_sign = 1.0 if (scope - visor_point).dot(rifle_right) >= 0.0 else -1.0
        camera_side = rifle_right * lateral_sign
        target = visor_point.lerp(scope, 0.56).lerp(muzzle, 0.08)
        # True outside/rear sight-line view.  Camera side is derived from the
        # actual ocular/visor lateral relationship rather than bone naming.
        location = (
            target
            - rifle_forward * 0.78
            + camera_side * 0.86
            + up * 0.20
        )
        camera.data.lens = 58.0
        camera.data.clip_start = 0.025
    elif view == "trigger_close":
        rifle_basis = root.matrix_world.to_3x3()
        rifle_forward = (rifle_basis @ Vector((0.0, 1.0, 0.0))).normalized()
        rifle_up = (rifle_basis @ Vector((0.0, 0.0, 1.0))).normalized()
        outward_r, _outward_l = named_shoulder_outward_axes(
            armature, right, forward, up
        )
        wrist = bone_head_world(armature, "Hand.R")
        helper = require_weapon_helper(root, ROLE_PRIMARY_GRIP).matrix_world.translation
        grip = bpy.data.objects.get("Rifle_PistolGrip")
        grip_point = (
            root.matrix_world @ weapon_local_position(root, grip)
            if grip is not None else helper
        )
        target = (wrist + helper + grip_point) / 3.0 - rifle_up * 0.015
        # Inboard/muzzleward/high view.  This is the one unobstructed quadrant for
        # this armoured rig: it exposes palm, fingers, trigger and pistol grip while
        # retaining enough forearm context to judge the wrist connection.
        location = (
            target
            - outward_r * 0.72
            + rifle_forward * 0.56
            + rifle_up * 0.24
        )
        camera.data.lens = 52.0
        camera.data.clip_start = 0.020
    elif view == "support_close":
        rifle_basis = root.matrix_world.to_3x3()
        rifle_forward = (rifle_basis @ Vector((0.0, 1.0, 0.0))).normalized()
        rifle_up = (rifle_basis @ Vector((0.0, 0.0, 1.0))).normalized()
        _outward_r, outward_l = named_shoulder_outward_axes(
            armature, right, forward, up
        )
        wrist = bone_head_world(armature, "Hand.L")
        helper = require_weapon_helper(root, ROLE_SUPPORT_GRIP).matrix_world.translation
        grip = bpy.data.objects.get("Rifle_SupportGrip")
        grip_point = (
            root.matrix_world @ weapon_local_position(root, grip)
            if grip is not None else helper
        )
        target = (wrist + helper + grip_point) / 3.0 - rifle_up * 0.015
        # Named support-side, muzzleward view.  This exposes the far-side finger
        # pads and the deliberate vertical foregrip instead of the forearm plate.
        location = (
            target
            + outward_l * 0.72
            + rifle_forward * 0.56
            - rifle_up * 0.06
        )
        camera.data.lens = 52.0
        camera.data.clip_start = 0.020
    elif view == "stock_scope_close":
        rifle_basis = root.matrix_world.to_3x3()
        rifle_forward = (rifle_basis @ Vector((0.0, 1.0, 0.0))).normalized()
        rifle_up = (rifle_basis @ Vector((0.0, 0.0, 1.0))).normalized()
        scope = require_weapon_helper(root, ROLE_SIGHT_OCULAR).matrix_world.translation
        stock_helper = require_weapon_helper(
            root, ROLE_STOCK_CONTACT
        ).matrix_world.translation.copy()
        buttpad = bpy.data.objects.get("Rifle_Stock_ButtPad")
        stock = (
            root.matrix_world @ weapon_local_position(root, buttpad)
            if buttpad is not None else stock_helper
        )
        shoulder = Vector(root.get("ps_aim_shoulder_anchor_world", tuple(stock_helper)))
        visor = bpy.data.objects.get("Helmet_Visor")
        visor_point = visor.matrix_world.translation if visor is not None else bone_head_world(armature, "Head")
        outward_r, _outward_l = named_shoulder_outward_axes(
            armature, right, forward, up
        )
        target = (visor_point + scope + stock + shoulder) * 0.25
        # Direct named-shoulder side view.  All four required contact landmarks—
        # visor, ocular, physical buttpad and shoulder pocket—share the shot, and
        # neither the helmet nor chest plate can hide the stock interface.
        location = (
            target
            + outward_r * 1.15
            + rifle_forward * 0.06
            + rifle_up * 0.10
        )
        camera.data.lens = 52.0
        camera.data.clip_start = 0.025
    elif view == "elbows_close":
        shoulder_r = bone_head_world(armature, "UpperArm.R")
        shoulder_l = bone_head_world(armature, "UpperArm.L")
        elbow_r = bone_tail_world(armature, "UpperArm.R")
        elbow_l = bone_tail_world(armature, "UpperArm.L")
        wrist_r = bone_head_world(armature, "Hand.R")
        wrist_l = bone_head_world(armature, "Hand.L")
        target = (shoulder_r + shoulder_l + elbow_r + elbow_l + wrist_r + wrist_l) / 6.0
        # A medium inspection shot rather than a macro crop. Both shoulders,
        # elbows, wrists, and the weapon contact region remain visible.
        location = target + forward * 1.28 + right * 0.82 + up * 0.56
        camera.data.lens = 55.0
        camera.data.clip_start = 0.035
    else:
        raise ValueError(view)

    set_camera_look_at(camera, location, target)
    _position_lights(lights, target, right, forward, up)
    render_scene.render.filepath = str(output_path)
    print(
        f"[Aim validation] Render {output_path.name}: "
        f"{action_name} frame {frame} view {view}",
        flush=True,
    )
    bpy.ops.render.render(write_still=True, scene=render_scene.name)
    if not output_path.exists() or output_path.stat().st_size < 4096:
        raise RuntimeError(f"Render was not written correctly: {output_path}")
    _validate_render_content(output_path)



def _validate_render_content(output_path: Path) -> None:
    """Reject a saved PNG that contains only a near-uniform field.

    In Blender background mode the ``Render Result`` datablock is not guaranteed
    to remain available after ``write_still=True``.  The PNG on disk is the
    actual validation artifact, so load that exact file into a temporary image,
    sample it, and remove the temporary datablock immediately afterwards.
    """
    image = None
    try:
        image = bpy.data.images.load(str(output_path), check_existing=False)
        if image.size[0] <= 1 or image.size[1] <= 1:
            raise RuntimeError(
                f"Saved render has invalid dimensions: {output_path} "
                f"({image.size[0]}x{image.size[1]})."
            )

        pixels = image.pixels
        pixel_count = image.size[0] * image.size[1]
        step = max(1, pixel_count // 4096)
        minimum = 10.0
        maximum = -10.0
        sampled = 0
        for pixel_index in range(0, pixel_count, step):
            index = pixel_index * 4
            luminance = (
                float(pixels[index]) * 0.2126
                + float(pixels[index + 1]) * 0.7152
                + float(pixels[index + 2]) * 0.0722
            )
            minimum = min(minimum, luminance)
            maximum = max(maximum, luminance)
            sampled += 1

        if sampled == 0:
            raise RuntimeError(f"Saved render contains no readable pixels: {output_path}.")

        luminance_range = maximum - minimum
        if luminance_range < 0.045:
            raise RuntimeError(
                f"Validation camera produced a near-uniform/blank image: "
                f"{output_path} (luminance range {luminance_range:.4f})."
            )
    except RuntimeError:
        raise
    except Exception as exc:
        raise RuntimeError(
            f"Could not inspect the saved validation PNG '{output_path}': {exc}"
        ) from exc
    finally:
        if image is not None:
            bpy.data.images.remove(image)

def _render_all(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    rifle_state: dict[str, object],
) -> list[Path]:
    output_dir = ensure_directory("renders", "aim_validation")

    suit_sources = _upper_body_meshes(armature, root, include_rifle=False)
    rifle_sources = [
        obj for obj in weapon_contract_objects(root) if obj.type == "MESH"
    ]
    source_objects = list(dict.fromkeys([*suit_sources, *rifle_sources]))
    suit_names = {obj.name for obj in suit_sources}
    rifle_names = {obj.name for obj in rifle_sources}

    render_scene = None
    render_collection = None
    proxies: dict[str, bpy.types.Object] = {}
    try:
        render_scene, render_collection, proxies = create_static_render_scene(
            PIPELINE_TEMP_PREFIX + "AimRenderScene",
            source_objects,
        )
        camera, lights = _create_camera_and_lights(render_scene, render_collection)
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
        render_scene.render.film_transparent = False
        render_scene.render.image_settings.color_mode = "RGBA"

        jobs = (
            ("PS_Idle", 1, "front_3q", "idle_upperbody_front_3q.png"),
            ("PS_Idle", 1, "side", "idle_upperbody_side.png"),
            ("PS_Aim", 1, "front_3q", "aim_frame_001_front_3q.png"),
            ("PS_Aim", 1, "side", "aim_frame_001_side.png"),
            ("PS_Aim", 15, "front_3q", "aim_frame_015_front_3q.png"),
            ("PS_Aim", 15, "side", "aim_frame_015_side.png"),
            ("PS_Aim", 30, "front_3q", "aim_frame_030_front_3q.png"),
            ("PS_Aim", 30, "side", "aim_frame_030_side.png"),
            ("PS_Aim", 1, "over_shoulder", "aim_over_shoulder.png"),
            ("PS_Aim", 1, "trigger_close", "aim_close_trigger_grip.png"),
            ("PS_Aim", 1, "support_close", "aim_close_support_grip.png"),
            ("PS_Aim", 1, "stock_scope_close", "aim_close_stock_scope.png"),
            ("PS_Aim", 1, "elbows_close", "aim_close_elbows.png"),
        )
        paths = []
        for action, frame, view, filename in jobs:
            path = output_dir / filename
            _render_one(
                camera, lights, armature, root, rifle_state,
                render_scene, proxies, suit_names, rifle_names,
                action, frame, view, path,
            )
            paths.append(path)
            print(f"Rendered: {path}", flush=True)
        return paths
    finally:
        remove_static_render_scene(render_scene, render_collection, proxies)
        remove_pipeline_temps()


def _file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    require_blender_52()
    ensure_object_mode()
    armature = get_armature()
    root = get_rifle_root()

    print("[Aim validation] Reading Action Slot metadata...", flush=True)
    actions = {}
    for name in ("PS_Idle", "PS_Walk", "PS_Hover", "PS_Aim"):
        action = bpy.data.actions.get(name)
        if action is None:
            raise RuntimeError(f"Required Action is missing: {name}")
        actions[name] = _slot_summary(action, armature)

    print("[Aim validation] Checking saved hierarchy...", flush=True)
    hierarchy = _validate_hierarchy(armature, root)

    # Blender 5.2 on Windows can recurse inside the dependency graph when a
    # freshly reloaded, bone-parented rifle is evaluated repeatedly by a
    # background render. The saved asset remains correctly bone-parented; only
    # the in-memory validation copy is detached and driven from the baked hand.
    print("[Aim validation] Detaching RifleRoot temporarily for safe evaluation...", flush=True)
    rifle_state = detach_rifle_for_validation(armature, root)
    try:
        print("[Aim validation] Checking animation invariants...", flush=True)
        animation = _validate_animation_invariants(armature)

        activate_action(armature, "PS_Aim")
        bpy.context.scene.frame_set(1)
        sync_detached_rifle_to_hand(armature, root, rifle_state)

        print("[Aim validation] Checking hand contacts and pose geometry...", flush=True)
        hand_metrics = _hand_helper_metrics(armature)
        geometry_metrics = _aim_pose_geometry_metrics(armature, root)
        automated_blockers = list(geometry_metrics.get("automated_blockers", []))
        if hand_metrics["right_wrist_to_primary_wrist_target_m"] > 0.020:
            automated_blockers.append(
                "right wrist/primary wrist target="
                f"{hand_metrics['right_wrist_to_primary_wrist_target_m']:.3f} m"
            )
        if hand_metrics["left_wrist_to_support_wrist_target_m"] > 0.020:
            automated_blockers.append(
                "left wrist/support wrist target="
                f"{hand_metrics['left_wrist_to_support_wrist_target_m']:.3f} m"
            )
        geometry_metrics["automated_blockers"] = automated_blockers
        if automated_blockers:
            print("[Aim validation] Automated blockers detected; rendering anyway:", flush=True)
            for blocker in automated_blockers:
                print(f"  - {blocker}", flush=True)

        print("[Aim validation] Rendering mandatory close-ups...", flush=True)
        render_paths = _render_all(armature, root, rifle_state)
        names = {path.name for path in render_paths}
        if names != set(REQUIRED_AIM_RENDERS):
            raise RuntimeError("Mandatory aim render set is incomplete.")

        blend_path = Path(bpy.data.filepath).resolve()
        report = {
            "blender_version": bpy.app.version_string,
            "blend_file": blend_path.name,
            "blend_sha256_at_validation": _file_sha256(blend_path),
            "automated_validation": ("PASS" if not automated_blockers else "REVIEW_BLOCKED"),
            "automated_blockers": automated_blockers,
            "visual_validation": "NOT_REVIEWED",
            "export_allowed": False,
            "visual_review_required": [
                "arms do not bend backward",
                "trigger hand visibly holds the pistol grip",
                "support palm and fingers visibly wrap the compact foregrip",
                "weapon and arms do not pass through torso",
                "small buttpad seats in the right shoulder pocket",
                "helmet visor is attached to the helmet and ocular lens aligns with its sight line",
                "no oversized platform remains beneath the handguard",
                "rifle silhouette reads as one hero weapon",
                "idle and aim visibly differ",
            ],
            "actions_and_slots": actions,
            "hierarchy": hierarchy,
            "animation": animation,
            "hand_helper_metrics": hand_metrics,
            "aim_pose_geometry_metrics": geometry_metrics,
            "aim_render_files": [str(path.relative_to(blend_path.parent)) for path in render_paths],
        }
        report_path = ensure_directory("renders") / "validation_report.json"
        write_json(report_path, report)
        if automated_blockers:
            print("\nAim renders completed with automated blockers.")
            print("Export remains locked; inspect the renders and blocker list together.")
        else:
            print("\nAutomated aim validation passed.")
            print("Visual validation remains NOT_REVIEWED; export is intentionally locked.")
        print(f"Report: {report_path}")
    finally:
        # Restore the in-memory scene for subsequent scripts in the one-process
        # runner. The on-disk blend was never modified by validation.
        try:
            activate_action(armature, "PS_Aim")
            bpy.context.scene.frame_set(1)
            sync_detached_rifle_to_hand(armature, root, rifle_state)
        finally:
            restore_rifle_after_validation(armature, root, rifle_state)


if __name__ == "__main__":
    main()
