# pyright: reportMissingImports=false
"""Build the deterministic weapon handling and locomotion Action set.

All exported motion lives in one armature Action Slot per clip. Three
non-deforming control bones carry the rigid rifle, detachable magazine and bolt,
so Blender's FBX exporter cannot split one gameplay clip into unrelated object
Actions. Geometry remains frozen by ``weapon_handling_contract.py``.
"""
from __future__ import annotations

import math
import sys
import traceback
from pathlib import Path

import bpy  # type: ignore
from mathutils import Matrix, Quaternion, Vector  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from powersuit_pipeline_common import (  # noqa: E402
    PIPELINE_TEMP_PREFIX,
    REQUIRED_ACTIONS,
    WEAPON_ANIMATION_ACTIONS,
    action_slot_curve_stats,
    activate_action,
    apply_pose_matrices,
    body_basis,
    bone_head_world,
    create_action_with_slot,
    ensure_action_channelbag,
    ensure_object_mode,
    evaluated_pose_matrices,
    expected_transform_curve_count,
    find_action_slot,
    get_action_channelbag,
    get_armature,
    get_rifle_root,
    matrix_world_for_pose_bone,
    named_shoulder_outward_axes,
    orientation_with_y_axis,
    remove_pipeline_temps,
    require_character_asset_versions,
    require_blender_52,
    rotate_pose_bone_world,
    save_current_blend,
    select_only,
)
from create_aim_animation import (  # noqa: E402
    _apply_basis_snapshot,
    _basis_snapshot,
    _clear_pipeline_constraints,
    _solve_arms,
)
from weapon_handling_contract import (  # noqa: E402
    ARTICULATED_COMPONENT_ROLES,
    COMPONENT_BOLT,
    COMPONENT_MAGAZINE,
    ROLE_PRIMARY_GRIP,
    ROLE_SUPPORT_GRIP,
    assert_articulated_components_at_rest,
    assert_weapon_rigid,
    require_weapon_helper,
    validate_weapon_contract,
    weapon_components,
    weapon_local_position,
)

FPS = 30
ANIMATION_CONTRACT_VERSION = 5
REQUIRED_GENERATOR_VERSION = 111
WEAPON_ROOT_BONE = "WeaponRoot"
MAGAZINE_BONE = "WeaponMagazine"
BOLT_BONE = "WeaponBolt"
CONTROL_BONES = (WEAPON_ROOT_BONE, MAGAZINE_BONE, BOLT_BONE)

LOWER_BODY_BONES = (
    "Root", "Hips",
    "UpperLeg.L", "LowerLeg.L", "Foot.L",
    "UpperLeg.R", "LowerLeg.R", "Foot.R",
)
WALK_SAMPLE_FRAMES = (1, 5, 9, 13, 17, 21, 25, 29, 31)
RUN_SAMPLE_FRAMES = (1, 4, 6, 9, 11, 14, 16, 19, 21)
POWERED_GAIT_STRIDE_SCALE = 1.65
RUN_STRIDE_SCALE = 1.90
RUN_FLIGHT_LIFT_METRES = {
    4: 0.030,
    6: 0.100,
    9: 0.030,
    14: 0.030,
    16: 0.100,
    19: 0.030,
}
LOOP_ACTIONS = {
    "PS_WeaponReady_Idle",
    "PS_WeaponStowed_Idle",
    "PS_Walk_Forward",
    "PS_Walk_Backward",
    "PS_Walk_Left",
    "PS_Walk_Right",
    "PS_Aim_Walk_Forward",
    "PS_Aim_Walk_Backward",
    "PS_Aim_Walk_Left",
    "PS_Aim_Walk_Right",
    "PS_WeaponStowed_Walk_Forward",
    "PS_WeaponStowed_Walk_Backward",
    "PS_WeaponStowed_Walk_Left",
    "PS_WeaponStowed_Walk_Right",
    "PS_WeaponStowed_Hover",
    "PS_Run_Forward",
}


def _copy_pose(pose: dict[str, Matrix]) -> dict[str, Matrix]:
    return {name: matrix.copy() for name, matrix in pose.items()}


def _evaluate_basis(armature: bpy.types.Object, action_name: str, frame: int) -> dict[str, Matrix]:
    activate_action(armature, action_name)
    neighbour = frame + 1 if frame < 60 else frame - 1
    bpy.context.scene.frame_set(neighbour)
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    return _basis_snapshot(armature)


def _blend_matrix(first: Matrix, second: Matrix, factor: float) -> Matrix:
    factor = max(0.0, min(1.0, float(factor)))
    first_location, first_rotation, first_scale = first.decompose()
    second_location, second_rotation, second_scale = second.decompose()
    return Matrix.LocRotScale(
        first_location.lerp(second_location, factor),
        first_rotation.slerp(second_rotation, factor),
        first_scale.lerp(second_scale, factor),
    )


def _blend_pose(
    first: dict[str, Matrix], second: dict[str, Matrix], factor: float
) -> dict[str, Matrix]:
    return {
        name: _blend_matrix(first[name], second[name], factor)
        for name in first
    }


def _combine_upper_and_lower(
    upper: dict[str, Matrix], lower: dict[str, Matrix]
) -> dict[str, Matrix]:
    result = _copy_pose(upper)
    for name in LOWER_BODY_BONES:
        result[name] = lower[name].copy()
    return result


def _extrapolate_matrix(
    reference: Matrix,
    animated: Matrix,
    factor: float,
) -> Matrix:
    """Scale an authored local-space motion delta without changing its rest basis."""
    reference_location, reference_rotation, reference_scale = reference.decompose()
    animated_location, animated_rotation, animated_scale = animated.decompose()
    delta_rotation = reference_rotation.rotation_difference(animated_rotation)
    axis, angle = delta_rotation.to_axis_angle()
    scaled_delta = (
        Quaternion(axis, angle * factor)
        if abs(angle) > 1.0e-8
        else Quaternion()
    )
    return Matrix.LocRotScale(
        reference_location + (animated_location - reference_location) * factor,
        reference_rotation @ scaled_delta,
        reference_scale + (animated_scale - reference_scale) * factor,
    )


def _amplify_lower_body(
    reference: dict[str, Matrix],
    animated: dict[str, Matrix],
    factor: float,
) -> dict[str, Matrix]:
    result = _copy_pose(animated)
    for name in LOWER_BODY_BONES:
        result[name] = _extrapolate_matrix(reference[name], animated[name], factor)
    return result


def _lift_hips_world(
    armature: bpy.types.Object,
    pose: dict[str, Matrix],
    lift_metres: float,
) -> dict[str, Matrix]:
    if lift_metres <= 0.0:
        return _copy_pose(pose)
    _apply_basis_snapshot(armature, pose)
    _right, _forward, up = body_basis(armature)
    offset = armature.matrix_world.inverted_safe().to_3x3() @ (up * lift_metres)
    hips = armature.pose.bones["Hips"]
    hips.matrix = Matrix.Translation(offset) @ hips.matrix
    bpy.context.view_layer.update()
    return _basis_snapshot(armature)


def _lateral_lower_body(
    armature: bpy.types.Object,
    reference: dict[str, Matrix],
    frame: int,
    direction_sign: float,
) -> dict[str, Matrix]:
    """Author an in-place powered cross-step for left/right locomotion.

    The existing rig has no lateral source take. This keeps the audited idle
    basis and drives hip abduction, alternating knee lift, and a small pelvis
    bank in visual body space. Left/right clips are exact mirrors in timing,
    so Unity's 2D blend tree can form stable diagonal poses without sliding an
    idle lower body sideways.
    """
    if frame < WALK_SAMPLE_FRAMES[0] or frame > WALK_SAMPLE_FRAMES[-1]:
        raise RuntimeError(f"Lateral gait frame {frame} is out of range.")
    direction_sign = -1.0 if direction_sign < 0.0 else 1.0
    cycle = (frame - 1) / float(WALK_SAMPLE_FRAMES[-1] - 1)
    swing = math.sin(cycle * math.tau)
    left_lift = max(0.0, swing)
    right_lift = max(0.0, -swing)

    _apply_basis_snapshot(armature, reference)
    right, forward, _up = body_basis(armature)
    rotate_pose_bone_world(
        armature,
        "Hips",
        forward,
        math.radians(-direction_sign * 3.5),
    )
    rotate_pose_bone_world(
        armature,
        "UpperLeg.L",
        forward,
        math.radians(direction_sign * (7.0 + 21.0 * swing)),
    )
    rotate_pose_bone_world(
        armature,
        "UpperLeg.R",
        forward,
        math.radians(direction_sign * (7.0 - 21.0 * swing)),
    )
    rotate_pose_bone_world(
        armature,
        "LowerLeg.L",
        right,
        math.radians(24.0 * left_lift),
    )
    rotate_pose_bone_world(
        armature,
        "Foot.L",
        right,
        math.radians(-12.0 * left_lift),
    )
    rotate_pose_bone_world(
        armature,
        "LowerLeg.R",
        right,
        math.radians(24.0 * right_lift),
    )
    rotate_pose_bone_world(
        armature,
        "Foot.R",
        right,
        math.radians(-12.0 * right_lift),
    )
    return _basis_snapshot(armature)


def _matrix_max_delta(first: Matrix, second: Matrix) -> float:
    return max(
        abs(float(first[row][column]) - float(second[row][column]))
        for row in range(4)
        for column in range(4)
    )


def _stowed_world(armature: bpy.types.Object) -> Matrix:
    right, forward, up = body_basis(armature)
    chest = bone_head_world(armature, "Chest")
    weapon_forward = (right * 0.69 + forward * 0.16 + up * 0.70).normalized()
    rotation = orientation_with_y_axis(weapon_forward, -forward).to_3x3()
    origin = chest - right * 0.23 - forward * 0.285 + up * 0.015
    result = Matrix.Translation(origin) @ rotation.to_4x4()
    if result.to_3x3().determinant() <= 0.0:
        raise RuntimeError("Stowed weapon target became reflected.")
    return result


def _ready_pose(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    idle_basis: dict[str, Matrix],
    original_root_local: Matrix,
) -> dict[str, Matrix]:
    """Solve a diagonal two-hand chest-ready stance using rigid hardpoints."""
    parent = root.parent
    parent_type = root.parent_type
    parent_bone = root.parent_bone
    parent_inverse = root.matrix_parent_inverse.copy()
    saved_aim_properties = {
        key: root[key] for key in root.keys() if str(key).startswith("ps_aim_")
    }
    root.parent = None
    root.parent_type = "OBJECT"
    root.parent_bone = ""
    root.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    try:
        _apply_basis_snapshot(armature, idle_basis)
        right, _forward, _up = body_basis(armature)
        rotate_pose_bone_world(armature, "Spine", right, math.radians(-1.5))
        rotate_pose_bone_world(armature, "Chest", right, math.radians(-2.0))
        right, forward, up = body_basis(armature)
        outward_r, outward_l = named_shoulder_outward_axes(
            armature, right, forward, up
        )
        weapon_forward = (right * 0.68 + forward * 0.38 + up * 0.63).normalized()
        rotation = orientation_with_y_axis(weapon_forward, up).to_3x3()
        chest = bone_head_world(armature, "Chest")
        primary_target = chest - right * 0.18 + forward * 0.29 - up * 0.07
        primary_local = weapon_local_position(
            root, require_weapon_helper(root, ROLE_PRIMARY_GRIP)
        )
        root.matrix_world = Matrix.Translation(
            primary_target - rotation @ primary_local
        ) @ rotation.to_4x4()
        bpy.context.view_layer.update()
        shoulder_r = bone_head_world(armature, "UpperArm.R")
        shoulder_l = bone_head_world(armature, "UpperArm.L")
        target_r = require_weapon_helper(
            root, ROLE_PRIMARY_GRIP
        ).matrix_world.translation
        target_l = require_weapon_helper(
            root, ROLE_SUPPORT_GRIP
        ).matrix_world.translation
        reach_r = (target_r - shoulder_r).length / 0.871
        reach_l = (target_l - shoulder_l).length / 0.871
        if reach_r > 0.98 or reach_l > 0.98:
            raise RuntimeError(
                "Ready hardpoints exceed arm reach: "
                f"R={reach_r:.3f}, L={reach_l:.3f}."
            )
        solved, _metrics = _solve_arms(armature, {
            "body_right": right,
            "body_forward": forward,
            "body_up": up,
            "outward_right_bone": outward_r,
            "outward_left_bone": outward_l,
        })
        apply_pose_matrices(armature, solved)
        result = _basis_snapshot(armature)
    finally:
        _clear_pipeline_constraints(armature)
        remove_pipeline_temps()
        root.parent = parent
        root.parent_type = parent_type
        root.parent_bone = parent_bone
        root.matrix_parent_inverse = parent_inverse
        root.matrix_basis = original_root_local.copy()
        bpy.context.view_layer.update()
        for key in list(root.keys()):
            if str(key).startswith("ps_aim_") and key not in saved_aim_properties:
                del root[key]
        for key, value in saved_aim_properties.items():
            root[key] = value
    return result


def _single_arm_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    side: str,
    target_world: Vector,
) -> dict[str, Matrix]:
    """Bake one temporary two-bone IK reach and remove every solver object."""
    _apply_basis_snapshot(armature, base_pose)
    right, forward, up = body_basis(armature)
    outward_r, outward_l = named_shoulder_outward_axes(
        armature, right, forward, up
    )
    outward = outward_r if side == "R" else outward_l
    shoulder = bone_head_world(armature, f"UpperArm.{side}")
    distance = (target_world - shoulder).length
    if distance > 0.858:
        raise RuntimeError(
            f"{side} hand target is outside safe reach ({distance:.3f} m)."
        )
    target = bpy.data.objects.new(
        PIPELINE_TEMP_PREFIX + f"SingleHandTarget_{side}", None
    )
    pole = bpy.data.objects.new(
        PIPELINE_TEMP_PREFIX + f"SingleElbowPole_{side}", None
    )
    bpy.context.scene.collection.objects.link(target)
    bpy.context.scene.collection.objects.link(pole)
    target.location = target_world
    pole.location = shoulder + outward * 0.38 + forward * 0.14 - up * 0.30
    lower = armature.pose.bones[f"LowerArm.{side}"]
    constraint = lower.constraints.new("IK")
    constraint.name = PIPELINE_TEMP_PREFIX + f"SingleArmIK_{side}"
    constraint.target = target
    constraint.pole_target = pole
    constraint.chain_count = 2
    constraint.use_tail = True
    constraint.iterations = 128
    best_angle = 0.0
    best_score: float | None = None
    for degrees in range(-180, 180, 10):
        constraint.pole_angle = math.radians(degrees)
        bpy.context.view_layer.update()
        elbow = armature.matrix_world @ armature.pose.bones[
            f"UpperArm.{side}"
        ].tail
        span = target_world - shoulder
        parameter = 0.5
        if span.length_squared > 1.0e-8:
            parameter = max(
                0.0,
                min(1.0, (elbow - shoulder).dot(span) / span.length_squared),
            )
        offset = elbow - (shoulder + span * parameter)
        score = offset.dot(outward) * 5.0 - offset.dot(up) * 4.0
        if best_score is None or score > best_score:
            best_score = score
            best_angle = constraint.pole_angle
    constraint.pole_angle = best_angle
    bpy.context.view_layer.update()
    evaluated = evaluated_pose_matrices(armature)
    lower.constraints.remove(constraint)
    bpy.context.view_layer.update()
    apply_pose_matrices(armature, evaluated)
    result = _basis_snapshot(armature)
    remove_pipeline_temps()
    return result


def _add_control_bones(armature: bpy.types.Object) -> None:
    if any(name in armature.data.bones for name in CONTROL_BONES):
        raise RuntimeError("Weapon control bones already exist before deterministic rebuild.")
    ensure_object_mode()
    select_only([armature], active=armature)
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        edit_bones = armature.data.edit_bones
        carrier = edit_bones.new(WEAPON_ROOT_BONE)
        carrier.head = Vector((0.0, 0.0, 0.0))
        carrier.tail = Vector((0.0, 0.12, 0.0))
        # WeaponRoot is intentionally top-level. FBX round-trip testing showed
        # that translation curves on a non-connected child control bone can be
        # interpreted around a different parent-bone pivot. A top-level carrier
        # has unambiguous armature-space translation and still follows Hand.R
        # because every ready/aim clip bakes that relationship explicitly.
        carrier.parent = None
        carrier.use_connect = False
        for name in (MAGAZINE_BONE, BOLT_BONE):
            bone = edit_bones.new(name)
            bone.head = carrier.head.copy()
            bone.tail = carrier.tail.copy()
            bone.parent = carrier
            bone.use_connect = False
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    for name in CONTROL_BONES:
        bone = armature.data.bones[name]
        bone.use_deform = False
        pose_bone = armature.pose.bones[name]
        pose_bone.rotation_mode = "XYZ"
        pose_bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def _reparent_weapon_to_controls(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    magazines: list[bpy.types.Object],
    bolts: list[bpy.types.Object],
) -> Matrix:
    root_world = root.matrix_world.copy()
    component_world = {
        obj.name: obj.matrix_world.copy() for obj in [*magazines, *bolts]
    }
    root.parent = armature
    root.parent_type = "BONE"
    root.parent_bone = WEAPON_ROOT_BONE
    root.matrix_world = root_world
    for obj in magazines:
        obj.parent = armature
        obj.parent_type = "BONE"
        obj.parent_bone = MAGAZINE_BONE
        obj.matrix_world = component_world[obj.name]
    for obj in bolts:
        obj.parent = armature
        obj.parent_type = "BONE"
        obj.parent_bone = BOLT_BONE
        obj.matrix_world = component_world[obj.name]
    bpy.context.view_layer.update()
    if root.parent_bone != WEAPON_ROOT_BONE:
        raise RuntimeError("RifleRoot control-bone parenting failed.")
    return matrix_world_for_pose_bone(
        armature, armature.pose.bones[WEAPON_ROOT_BONE]
    ).inverted() @ root.matrix_world


def _extend_pose(pose: dict[str, Matrix]) -> dict[str, Matrix]:
    result = _copy_pose(pose)
    for name in CONTROL_BONES:
        result[name] = Matrix.Identity(4)
    return result


def _pose_weapon_at_world(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    target_root_world: Matrix,
    carrier_to_root: Matrix,
) -> dict[str, Matrix]:
    _apply_basis_snapshot(armature, base_pose)
    desired_carrier_world = target_root_world @ carrier_to_root.inverted()
    armature.pose.bones[WEAPON_ROOT_BONE].matrix = (
        armature.matrix_world.inverted() @ desired_carrier_world
    )
    bpy.context.view_layer.update()
    return _basis_snapshot(armature)


def _pose_weapon_follow_hand(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    hand_to_root: Matrix,
    carrier_to_root: Matrix,
) -> dict[str, Matrix]:
    _apply_basis_snapshot(armature, base_pose)
    hand_world = matrix_world_for_pose_bone(
        armature, armature.pose.bones["Hand.R"]
    )
    return _pose_weapon_at_world(
        armature,
        base_pose,
        hand_world @ hand_to_root,
        carrier_to_root,
    )


def _pose_component_delta(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    base_pose: dict[str, Matrix],
    control_bone: str,
    delta_in_root_space: Matrix,
) -> dict[str, Matrix]:
    _apply_basis_snapshot(armature, base_pose)
    bpy.context.view_layer.update()
    bone = armature.pose.bones[control_bone]
    bone_world = matrix_world_for_pose_bone(armature, bone)
    root_world = root.matrix_world.copy()
    desired = root_world @ delta_in_root_space @ root_world.inverted() @ bone_world
    bone.matrix = armature.matrix_world.inverted() @ desired
    bpy.context.view_layer.update()
    return _basis_snapshot(armature)


def _ensure_curve(channelbag, data_path: str, index: int, group_name: str):
    ensure = getattr(channelbag.fcurves, "ensure", None)
    if ensure is not None:
        return ensure(data_path, index=index, group_name=group_name)
    existing = channelbag.fcurves.find(data_path, index=index)
    if existing is not None:
        return existing
    return channelbag.fcurves.new(
        data_path=data_path, index=index, group_name=group_name
    )


def _insert_key(curve, frame: int, value: float) -> None:
    point = curve.keyframe_points.insert(
        float(frame), float(value), options={"FAST"}
    )
    point.interpolation = "BEZIER"
    point.handle_left_type = "AUTO_CLAMPED"
    point.handle_right_type = "AUTO_CLAMPED"


def _key_pose(
    action: bpy.types.Action,
    slot,
    armature: bpy.types.Object,
    pose: dict[str, Matrix],
    frame: int,
) -> None:
    _apply_basis_snapshot(armature, pose)
    channelbag = ensure_action_channelbag(action, slot)
    for bone in armature.pose.bones:
        if bone.rotation_mode == "QUATERNION":
            rotation_property = "rotation_quaternion"
            rotation_values = tuple(bone.rotation_quaternion)
        elif bone.rotation_mode == "AXIS_ANGLE":
            rotation_property = "rotation_axis_angle"
            rotation_values = tuple(bone.rotation_axis_angle)
        else:
            rotation_property = "rotation_euler"
            rotation_values = tuple(bone.rotation_euler)
        for property_name, values in (
            ("location", tuple(bone.location)),
            (rotation_property, rotation_values),
            ("scale", tuple(bone.scale)),
        ):
            path = f'pose.bones["{bone.name}"].{property_name}'
            for index, value in enumerate(values):
                _insert_key(
                    _ensure_curve(channelbag, path, index, bone.name),
                    frame,
                    value,
                )


def _legacy_key_frames(action: bpy.types.Action, armature: bpy.types.Object) -> list[int]:
    slot = find_action_slot(action, armature)
    channelbag = get_action_channelbag(action, slot)
    frames = {
        int(round(point.co.x))
        for curve in channelbag.fcurves
        for point in curve.keyframe_points
    }
    if not frames:
        raise RuntimeError(f"Legacy Action '{action.name}' contains no keys.")
    return sorted(frames)


def _key_control_bones(
    action: bpy.types.Action,
    slot,
    armature: bpy.types.Object,
    frame: int,
) -> None:
    channelbag = ensure_action_channelbag(action, slot)
    for bone_name in CONTROL_BONES:
        bone = armature.pose.bones[bone_name]
        for property_name, values in (
            ("location", tuple(bone.location)),
            ("rotation_euler", tuple(bone.rotation_euler)),
            ("scale", tuple(bone.scale)),
        ):
            path = f'pose.bones["{bone_name}"].{property_name}'
            for index, value in enumerate(values):
                _insert_key(
                    _ensure_curve(channelbag, path, index, bone_name),
                    frame,
                    value,
                )


def _append_control_curves_to_legacy(
    armature: bpy.types.Object,
    hand_to_root: Matrix,
    carrier_to_root: Matrix,
) -> None:
    for name in ("PS_Idle", "PS_Walk", "PS_Hover", "PS_Aim"):
        action = bpy.data.actions[name]
        slot = find_action_slot(action, armature)
        frames = _legacy_key_frames(action, armature)
        for frame in frames:
            activate_action(armature, action)
            bpy.context.scene.frame_set(frame)
            for bone_name in CONTROL_BONES:
                armature.pose.bones[bone_name].matrix_basis = Matrix.Identity(4)
            bpy.context.view_layer.update()
            hand_world = matrix_world_for_pose_bone(
                armature, armature.pose.bones["Hand.R"]
            )
            desired_carrier = (
                hand_world @ hand_to_root @ carrier_to_root.inverted()
            )
            armature.pose.bones[WEAPON_ROOT_BONE].matrix = (
                armature.matrix_world.inverted() @ desired_carrier
            )
            bpy.context.view_layer.update()
            _key_control_bones(action, slot, armature, frame)
        channelbag = get_action_channelbag(action, slot)
        for curve in channelbag.fcurves:
            curve.update()


def _build_action(
    armature: bpy.types.Object,
    name: str,
    poses: dict[int, dict[str, Matrix]],
) -> None:
    frames = sorted(poses)
    if not frames or frames[0] != 1:
        raise RuntimeError(f"{name} must begin at frame 1.")
    action, slot = create_action_with_slot(
        armature, name, frames[0], frames[-1]
    )
    action["ps_animation_contract_version"] = ANIMATION_CONTRACT_VERSION
    action["ps_looping"] = name in LOOP_ACTIONS
    for frame in frames:
        bpy.context.scene.frame_set(frame)
        _key_pose(action, slot, armature, poses[frame], frame)
    channelbag = get_action_channelbag(action, slot)
    for curve in channelbag.fcurves:
        for point in curve.keyframe_points:
            point.interpolation = "BEZIER"
            point.handle_left_type = "AUTO_CLAMPED"
            point.handle_right_type = "AUTO_CLAMPED"
        curve.update()


def _validate_actions(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    magazines: list[bpy.types.Object],
    bolts: list[bpy.types.Object],
) -> None:
    names = {action.name for action in bpy.data.actions}
    if names != set(REQUIRED_ACTIONS):
        raise RuntimeError(
            "Action set mismatch: "
            f"missing={sorted(set(REQUIRED_ACTIONS) - names)}, "
            f"unexpected={sorted(names - set(REQUIRED_ACTIONS))}."
        )
    for name in REQUIRED_ACTIONS:
        action = bpy.data.actions[name]
        if len(list(action.slots)) != 1:
            raise RuntimeError(
                f"{name} must have one synchronized armature slot."
            )
        slot = find_action_slot(action, armature)
        stats = action_slot_curve_stats(action, slot)
        if stats["empty_curve_count"]:
            raise RuntimeError(f"{name} slot is incomplete: {stats}.")
        if name in WEAPON_ANIMATION_ACTIONS or name == "PS_Aim":
            expected = expected_transform_curve_count(armature, action, slot)
            if stats["curve_count"] != expected:
                raise RuntimeError(f"{name} slot is incomplete: {stats}.")
        if name in WEAPON_ANIMATION_ACTIONS:
            version = int(action.get("ps_animation_contract_version", 0))
            if version != ANIMATION_CONTRACT_VERSION:
                raise RuntimeError(
                    f"{name} animation contract version is {version}; expected "
                    f"{ANIMATION_CONTRACT_VERSION}."
                )

    for name in LOOP_ACTIONS:
        action = bpy.data.actions[name]
        start = _evaluate_basis(armature, name, 1)
        end = _evaluate_basis(armature, name, int(action.frame_end))
        delta = max(
            _matrix_max_delta(start[bone], end[bone]) for bone in start
        )
        if delta > 2.0e-5:
            raise RuntimeError(f"{name} loop delta is {delta:.3e}.")

    activate_action(armature, "PS_Reload")
    bpy.context.scene.frame_set(50)
    bpy.context.view_layer.update()
    magazine_travel = max(
        (root.matrix_world.inverted() @ obj.matrix_world).translation.length
        for obj in magazines
    )
    if magazine_travel < 0.20:
        raise RuntimeError(
            f"PS_Reload magazine travel is too small ({magazine_travel:.3f} m)."
        )
    assert_weapon_rigid(root)
    activate_action(armature, "PS_BoltCycle")
    bpy.context.scene.frame_set(12)
    bpy.context.view_layer.update()
    bolt_travel = max(
        (root.matrix_world.inverted() @ obj.matrix_world).translation.length
        for obj in bolts
    )
    if bolt_travel < 0.065:
        raise RuntimeError(
            f"PS_BoltCycle travel is too small ({bolt_travel:.3f} m)."
        )
    assert_weapon_rigid(root)


def main() -> None:
    require_blender_52()
    ensure_object_mode()
    remove_pipeline_temps()
    armature = get_armature()
    root = get_rifle_root()
    require_character_asset_versions(armature)
    if int(root.get("ps_generator_version", 0)) < REQUIRED_GENERATOR_VERSION:
        raise RuntimeError(
            f"Rifle generator {root.get('ps_generator_version', 0)} is too old; "
            f"expected {REQUIRED_GENERATOR_VERSION}."
        )
    validate_weapon_contract(root)
    assert_weapon_rigid(root)
    assert_articulated_components_at_rest(root)
    if (
        root.parent != armature
        or root.parent_type != "BONE"
        or root.parent_bone != "Hand.R"
    ):
        raise RuntimeError("RifleRoot must enter this stage parented to Hand.R.")
    magazines = weapon_components(root, COMPONENT_MAGAZINE)
    bolts = weapon_components(root, COMPONENT_BOLT)
    if {str(obj.get("ps_weapon_component_role", "")) for obj in [*magazines, *bolts]} != set(
        ARTICULATED_COMPONENT_ROLES
    ):
        raise RuntimeError("Articulated magazine/bolt component set is incomplete.")

    original_root_local = root.matrix_basis.copy()
    idle_raw = _evaluate_basis(armature, "PS_Idle", 1)
    idle_mid_raw = _evaluate_basis(armature, "PS_Idle", 31)
    idle_end_raw = _evaluate_basis(armature, "PS_Idle", 61)
    aim_raw = _evaluate_basis(armature, "PS_Aim", 1)
    hover_raw = {
        frame: _evaluate_basis(armature, "PS_Hover", frame)
        for frame in (1, 31, 61)
    }
    walk_raw = {
        frame: _evaluate_basis(armature, "PS_Walk", frame)
        for frame in WALK_SAMPLE_FRAMES
    }
    ready_raw = _ready_pose(armature, root, idle_raw, original_root_local)

    # Build the export-safe carrier rig while the known-good Aim pose is active.
    activate_action(armature, "PS_Aim")
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    hand_to_root = matrix_world_for_pose_bone(
        armature, armature.pose.bones["Hand.R"]
    ).inverted() @ root.matrix_world
    _add_control_bones(armature)
    carrier_to_root = _reparent_weapon_to_controls(
        armature, root, magazines, bolts
    )
    _append_control_curves_to_legacy(
        armature, hand_to_root, carrier_to_root
    )

    idle = _extend_pose(idle_raw)
    idle_mid = _extend_pose(idle_mid_raw)
    idle_end = _extend_pose(idle_end_raw)
    aim = _pose_weapon_follow_hand(
        armature, _extend_pose(aim_raw), hand_to_root, carrier_to_root
    )
    ready = _pose_weapon_follow_hand(
        armature, _extend_pose(ready_raw), hand_to_root, carrier_to_root
    )
    walk_sources = {
        frame: _extend_pose(pose) for frame, pose in walk_raw.items()
    }
    hover_sources = {
        frame: _extend_pose(pose) for frame, pose in hover_raw.items()
    }

    _apply_basis_snapshot(armature, ready)
    rotate_pose_bone_world(
        armature, "Chest", body_basis(armature)[0], math.radians(-0.30)
    )
    rotate_pose_bone_world(
        armature, "Head", body_basis(armature)[0], math.radians(0.20)
    )
    ready_mid = _pose_weapon_follow_hand(
        armature, _basis_snapshot(armature), hand_to_root, carrier_to_root
    )

    # Run keeps the two-hand chest-ready weapon contract while committing the
    # upper body farther forward than walk. The lower body is a deliberately
    # amplified version of the audited gait, retimed to a 20-frame cycle with
    # two brief airborne phases (180 steps/minute at 30 FPS).
    _apply_basis_snapshot(armature, ready)
    run_right = body_basis(armature)[0]
    rotate_pose_bone_world(
        armature, "Spine", run_right, math.radians(-8.0)
    )
    rotate_pose_bone_world(
        armature, "Chest", run_right, math.radians(-5.0)
    )
    rotate_pose_bone_world(
        armature, "Head", run_right, math.radians(3.0)
    )
    run_upper = _pose_weapon_follow_hand(
        armature, _basis_snapshot(armature), hand_to_root, carrier_to_root
    )

    ready_idle = {1: ready, 31: ready_mid, 61: ready}
    stowed_idle: dict[int, dict[str, Matrix]] = {}
    for frame, pose in {1: idle, 31: idle_mid, 61: idle_end}.items():
        _apply_basis_snapshot(armature, pose)
        stowed_idle[frame] = _pose_weapon_at_world(
            armature, pose, _stowed_world(armature), carrier_to_root
        )

    locomotion: dict[str, dict[int, dict[str, Matrix]]] = {}
    for name, upper, backwards, stowed in (
        ("PS_Walk_Forward", ready, False, False),
        ("PS_Walk_Backward", ready, True, False),
        ("PS_Aim_Walk_Forward", aim, False, False),
        ("PS_Aim_Walk_Backward", aim, True, False),
        ("PS_WeaponStowed_Walk_Forward", idle, False, True),
        ("PS_WeaponStowed_Walk_Backward", idle, True, True),
    ):
        poses: dict[int, dict[str, Matrix]] = {}
        for index, output_frame in enumerate(WALK_SAMPLE_FRAMES):
            source_frame = (
                WALK_SAMPLE_FRAMES[-1 - index] if backwards else output_frame
            )
            lower = _amplify_lower_body(
                idle,
                walk_sources[source_frame],
                POWERED_GAIT_STRIDE_SCALE,
            )
            pose = _combine_upper_and_lower(upper, lower)
            if stowed:
                _apply_basis_snapshot(armature, pose)
                pose = _pose_weapon_at_world(
                    armature, pose, _stowed_world(armature), carrier_to_root
                )
            else:
                pose = _pose_weapon_follow_hand(
                    armature, pose, hand_to_root, carrier_to_root
                )
            poses[output_frame] = pose
        locomotion[name] = poses

    for name, upper, direction_sign, stowed in (
        ("PS_Walk_Left", ready, -1.0, False),
        ("PS_Walk_Right", ready, 1.0, False),
        ("PS_Aim_Walk_Left", aim, -1.0, False),
        ("PS_Aim_Walk_Right", aim, 1.0, False),
        ("PS_WeaponStowed_Walk_Left", idle, -1.0, True),
        ("PS_WeaponStowed_Walk_Right", idle, 1.0, True),
    ):
        poses: dict[int, dict[str, Matrix]] = {}
        for output_frame in WALK_SAMPLE_FRAMES:
            lower = _lateral_lower_body(
                armature,
                idle,
                output_frame,
                direction_sign,
            )
            pose = _combine_upper_and_lower(upper, lower)
            if stowed:
                _apply_basis_snapshot(armature, pose)
                pose = _pose_weapon_at_world(
                    armature,
                    pose,
                    _stowed_world(armature),
                    carrier_to_root,
                )
            else:
                pose = _pose_weapon_follow_hand(
                    armature,
                    pose,
                    hand_to_root,
                    carrier_to_root,
                )
            poses[output_frame] = pose
        locomotion[name] = poses

    run_forward: dict[int, dict[str, Matrix]] = {}
    for output_frame, source_frame in zip(RUN_SAMPLE_FRAMES, WALK_SAMPLE_FRAMES):
        lower = _amplify_lower_body(
            idle, walk_sources[source_frame], RUN_STRIDE_SCALE
        )
        pose = _combine_upper_and_lower(run_upper, lower)
        pose = _lift_hips_world(
            armature, pose, RUN_FLIGHT_LIFT_METRES.get(output_frame, 0.0)
        )
        run_forward[output_frame] = _pose_weapon_follow_hand(
            armature, pose, hand_to_root, carrier_to_root
        )

    stowed_hover: dict[int, dict[str, Matrix]] = {}
    for frame in (1, 31, 61):
        pose = _combine_upper_and_lower(idle, hover_sources[frame])
        _apply_basis_snapshot(armature, pose)
        stowed_hover[frame] = _pose_weapon_at_world(
            armature, pose, _stowed_world(armature), carrier_to_root
        )

    # Draw and sheath share exact endpoint poses, preventing state-transition snaps.
    _apply_basis_snapshot(armature, idle)
    stowed_target = _stowed_world(armature)
    stowed_primary = stowed_target @ weapon_local_position(
        root, require_weapon_helper(root, ROLE_PRIMARY_GRIP)
    )
    reach = _extend_pose(
        _single_arm_pose(armature, idle_raw, "R", stowed_primary)
    )
    reach_stowed = _pose_weapon_at_world(
        armature, reach, stowed_target, carrier_to_root
    )
    transition = _blend_pose(reach_stowed, ready, 0.48)
    draw = {1: stowed_idle[1], 10: reach_stowed, 18: transition, 30: ready}
    sheathe = {1: ready, 13: transition, 21: reach_stowed, 30: stowed_idle[1]}

    # Reload timing is 84 frames at 30 FPS (2.8 seconds by gameplay convention).
    _apply_basis_snapshot(armature, ready)
    bpy.context.view_layer.update()
    ready_root_world = root.matrix_world.copy()
    magazine_center = sum(
        (weapon_local_position(root, obj) for obj in magazines),
        Vector((0.0, 0.0, 0.0)),
    ) / len(magazines)

    def magazine_delta(x: float, y: float, z: float, roll_deg: float = 0.0) -> Matrix:
        pivot = Matrix.Translation(magazine_center)
        rotation = Matrix.Rotation(
            math.radians(roll_deg), 4, Vector((0.0, 1.0, 0.0))
        )
        return Matrix.Translation(Vector((x, y, z))) @ pivot @ rotation @ pivot.inverted()

    magazine_deltas = {
        1: Matrix.Identity(4),
        14: Matrix.Identity(4),
        25: Matrix.Identity(4),
        36: magazine_delta(0.025, -0.015, -0.22, 6.0),
        50: magazine_delta(0.12, -0.025, -0.28, 14.0),
        64: magazine_delta(0.0, -0.005, -0.12, 3.0),
        75: Matrix.Identity(4),
        84: Matrix.Identity(4),
    }

    def magazine_hand_pose(delta: Matrix) -> dict[str, Matrix]:
        target = ready_root_world @ (delta @ magazine_center)
        rifle_right = (
            ready_root_world.to_3x3() @ Vector((1.0, 0.0, 0.0))
        ).normalized()
        return _pose_weapon_follow_hand(
            armature,
            _extend_pose(
                _single_arm_pose(
                    armature,
                    ready_raw,
                    "L",
                    target + rifle_right * 0.035,
                )
            ),
            hand_to_root,
            carrier_to_root,
        )

    contact = magazine_hand_pose(Matrix.Identity(4))
    reload_base = {
        1: ready,
        14: _blend_pose(ready, contact, 0.55),
        25: contact,
        36: magazine_hand_pose(magazine_deltas[36]),
        50: magazine_hand_pose(magazine_deltas[50]),
        64: magazine_hand_pose(magazine_deltas[64]),
        75: contact,
        84: ready,
    }
    reload_poses = {
        frame: _pose_component_delta(
            armature, root, reload_base[frame], MAGAZINE_BONE, delta
        )
        for frame, delta in magazine_deltas.items()
    }

    # Manual bolt cycle is 20 frames (0.67 seconds) and keeps RifleRoot stable
    # while Hand.R reaches off the primary grip.
    bolt_center = sum(
        (weapon_local_position(root, obj) for obj in bolts),
        Vector((0.0, 0.0, 0.0)),
    ) / len(bolts)
    bolt_deltas = {
        1: Matrix.Identity(4),
        4: Matrix.Identity(4),
        8: Matrix.Translation(Vector((0.0, -0.095, 0.0))),
        12: Matrix.Translation(Vector((0.0, -0.095, 0.0))),
        16: Matrix.Identity(4),
        20: Matrix.Identity(4),
    }

    def bolt_hand_pose(delta: Matrix) -> dict[str, Matrix]:
        target = ready_root_world @ (delta @ bolt_center)
        rifle_right = (
            ready_root_world.to_3x3() @ Vector((1.0, 0.0, 0.0))
        ).normalized()
        raw = _single_arm_pose(
            armature, ready_raw, "R", target - rifle_right * 0.035
        )
        return _pose_weapon_at_world(
            armature, _extend_pose(raw), ready_root_world, carrier_to_root
        )

    bolt_contact = bolt_hand_pose(Matrix.Identity(4))
    bolt_rear = bolt_hand_pose(bolt_deltas[8])
    bolt_base = {
        1: ready,
        4: bolt_contact,
        8: bolt_rear,
        12: bolt_rear,
        16: bolt_contact,
        20: ready,
    }
    bolt_poses = {
        frame: _pose_component_delta(
            armature, root, bolt_base[frame], BOLT_BONE, delta
        )
        for frame, delta in bolt_deltas.items()
    }

    specs = {
        "PS_WeaponReady_Idle": ready_idle,
        "PS_WeaponStowed_Idle": stowed_idle,
        "PS_Weapon_Draw": draw,
        "PS_Weapon_Sheathe": sheathe,
        **locomotion,
        "PS_Run_Forward": run_forward,
        "PS_WeaponStowed_Hover": stowed_hover,
        "PS_Reload": reload_poses,
        "PS_BoltCycle": bolt_poses,
    }
    if set(specs) != set(WEAPON_ANIMATION_ACTIONS):
        raise RuntimeError("Internal weapon Action specification is incomplete.")
    for name in WEAPON_ANIMATION_ACTIONS:
        print(f"[Weapon animation] Building {name}...", flush=True)
        _build_action(armature, name, specs[name])

    root["ps_weapon_animation_contract_version"] = ANIMATION_CONTRACT_VERSION
    root["ps_weapon_control_bones"] = list(CONTROL_BONES)
    root["ps_reload_commit_frame"] = 75
    root["ps_reload_frame_end"] = 84
    root["ps_bolt_cycle_frame_end"] = 20
    root["ps_run_cycle_frame_end"] = 21
    root["ps_run_cycle_seconds"] = 20.0 / FPS
    root["ps_run_step_cadence_per_minute"] = 180
    root["ps_animation_action_names"] = list(WEAPON_ANIMATION_ACTIONS)
    root["ps_stowed_locomotion_actions"] = [
        "PS_WeaponStowed_Walk_Forward",
        "PS_WeaponStowed_Walk_Backward",
        "PS_WeaponStowed_Walk_Left",
        "PS_WeaponStowed_Walk_Right",
        "PS_WeaponStowed_Hover",
    ]
    _validate_actions(armature, root, magazines, bolts)

    activate_action(armature, "PS_Aim")
    bpy.context.scene.frame_set(1)
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 84
    bpy.context.view_layer.update()
    assert_articulated_components_at_rest(root)
    assert_weapon_rigid(root)
    output = save_current_blend("powersuit_pipeline.blend")
    print("\nWeapon animation set rebuilt on export-safe control bones.")
    print(f"Actions: {len(REQUIRED_ACTIONS)} total ({len(WEAPON_ANIMATION_ACTIONS)} new).")
    print(f"Control bones: {', '.join(CONTROL_BONES)}")
    print(f"Saved: {output}")


if __name__ == "__main__":
    try:
        main()
    except BaseException:
        traceback.print_exc()
        sys.stdout.flush()
        sys.stderr.flush()
        raise
