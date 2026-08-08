# pyright: reportMissingImports=false
"""Create or replace only PS_Aim using the supplied rig's real matrices.

Blender 5.2 pipeline responsibility:
- evaluate PS_Idle through its explicit Action Slot
- position independent RifleRoot
- solve arms with independent temporary IK targets and poles
- bake a self-contained, slotted PS_Aim Action
- remove temporary controls and constraints
- parent only RifleRoot to Hand.R after baking

No modelling, rendering, FBX export, or modification of PS_Idle/Walk/Hover occurs.
"""
from __future__ import annotations

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
    RIFLE_ROOT_NAME,
    activate_action,
    apply_pose_matrices,
    body_basis,
    bone_head_world,
    bone_tail_world,
    create_action_with_slot,
    action_slot_curve_stats,
    ensure_action_channelbag,
    ensure_object_mode,
    expected_transform_curve_count,
    evaluated_pose_matrices,
    get_armature,
    get_rifle_root,
    matrix_from_axes,
    matrix_world_for_pose_bone,
    named_shoulder_outward_axes,
    remove_pipeline_temps,
    require_blender_52,
    rotate_pose_bone_world,
    save_current_blend,
)

from weapon_handling_contract import (  # noqa: E402
    ROLE_PRIMARY_GRIP,
    ROLE_SIGHT_OCULAR,
    ROLE_STOCK_CONTACT,
    ROLE_SUPPORT_GRIP,
    assert_weapon_rigid,
    get_stance_profile,
    require_weapon_helper,
    validate_weapon_contract,
    weapon_local_position,
)

ACTION_NAME = "PS_Aim"
BASE_ACTION_NAME = "PS_Idle"
FRAME_START = 1
FRAME_STABILIZE = 15
FRAME_END = 30
FPS = 30

UPPER_BODY_BONES = (
    "Spine", "Chest", "Neck", "Head",
    "Shoulder.R", "UpperArm.R", "LowerArm.R", "Hand.R",
    "Shoulder.L", "UpperArm.L", "LowerArm.L", "Hand.L",
)
LOWER_BODY_BONES = (
    "Root", "Hips",
    "UpperLeg.L", "LowerLeg.L", "Foot.L",
    "UpperLeg.R", "LowerLeg.R", "Foot.R",
)


def _assert_rifle_ready(root: bpy.types.Object) -> None:
    armature = get_armature()
    if int(armature.get("ps_v2_rig_upgrade_version", 0)) < 1:
        raise RuntimeError(
            "The controlled V2 arm-proportion pass has not run. "
            "Run upgrade_powersuit_rig.py before the weapon and aim stages."
        )
    if int(root.get("ps_generator_version", 0)) < 102:
        raise RuntimeError(
            "RifleRoot predates the rigid weapon-framework reset. "
            "Run upgrade_rifle_model.py first."
        )
    validate_weapon_contract(root, require_independent=True)
    assert_weapon_rigid(root)


def _copy_object_world(source: bpy.types.Object, name: str) -> bpy.types.Object:
    target = bpy.data.objects.new(PIPELINE_TEMP_PREFIX + name, None)
    bpy.context.scene.collection.objects.link(target)
    target.empty_display_type = "ARROWS"
    target.empty_display_size = 0.075
    target.matrix_world = source.matrix_world.copy()
    return target


def _create_pole(name: str, location: Vector) -> bpy.types.Object:
    pole = bpy.data.objects.new(PIPELINE_TEMP_PREFIX + name, None)
    bpy.context.scene.collection.objects.link(pole)
    pole.empty_display_type = "SPHERE"
    pole.empty_display_size = 0.085
    pole.location = location
    return pole


def _arm_reach(armature: bpy.types.Object, side: str) -> float:
    upper = armature.data.bones.get(f"UpperArm.{side}")
    lower = armature.data.bones.get(f"LowerArm.{side}")
    if upper is None or lower is None:
        raise RuntimeError(f"Arm bones are missing for side {side}.")
    return float(upper.length + lower.length)


def _place_rigid_weapon_from_stance(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> dict[str, object]:
    """Place the rigid weapon from its stance-family stock contact only.

    There is intentionally no optimizer here. The weapon asset owns its grips,
    stock and sight geometry. The stance owns the character offsets. If those two
    contracts are incompatible, the stage fails and the weapon must be redesigned
    at the asset level instead of deforming parts during animation.
    """
    assert_weapon_rigid(root)
    profile = get_stance_profile(str(root["ps_weapon_stance_family"]))
    right, forward, up = body_basis(armature)
    shoulder_r = bone_head_world(armature, "UpperArm.R")
    shoulder_l = bone_head_world(armature, "UpperArm.L")
    outward_r, outward_l = named_shoulder_outward_axes(
        armature, right, forward, up
    )

    helpers = {
        ROLE_PRIMARY_GRIP: require_weapon_helper(root, ROLE_PRIMARY_GRIP),
        ROLE_SUPPORT_GRIP: require_weapon_helper(root, ROLE_SUPPORT_GRIP),
        ROLE_STOCK_CONTACT: require_weapon_helper(root, ROLE_STOCK_CONTACT),
        ROLE_SIGHT_OCULAR: require_weapon_helper(root, ROLE_SIGHT_OCULAR),
    }

    # Pick a repeatable visible shoulder-pocket point. The selection is based on
    # suit geometry only; it never searches weapon offsets.
    depsgraph = bpy.context.evaluated_depsgraph_get()
    shoulder_points: list[Vector] = []
    for object_name in (
        "Shoulder_Armour.R", "Upper_Arm.R", "Upper_Chest", "Chest_Core"
    ):
        obj = bpy.data.objects.get(object_name)
        if obj is None or obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        shoulder_points.extend(
            evaluated.matrix_world @ Vector(corner)
            for corner in evaluated.bound_box
        )
    if not shoulder_points:
        shoulder_points = [shoulder_r]

    def shoulder_score(point: Vector) -> float:
        offset = point - shoulder_r
        # Prefer the visible front/inner pocket, not the outer armour tip.
        return (
            offset.dot(forward) * 7.0
            - abs(offset.dot(outward_r) + 0.035) * 8.0
            - abs(offset.dot(up) - 0.005) * 5.0
        )

    shoulder_anchor = max(shoulder_points, key=shoulder_score)
    stock_world = (
        shoulder_anchor
        - outward_r * profile.stock_inward_m
        + forward * profile.stock_forward_m
        + up * profile.stock_up_m
    )

    base_rotation = matrix_from_axes(
        Vector((0.0, 0.0, 0.0)), right, forward, up
    ).to_3x3()
    rotation3 = Matrix.Rotation(
        math.radians(profile.weapon_pitch_deg), 3, right
    ) @ base_rotation
    stock_local = weapon_local_position(root, helpers[ROLE_STOCK_CONTACT])
    origin = stock_world - rotation3 @ stock_local
    root.matrix_world = Matrix.Translation(origin) @ rotation3.to_4x4()
    bpy.context.view_layer.update()
    assert_weapon_rigid(root)

    primary_world = helpers[ROLE_PRIMARY_GRIP].matrix_world.translation.copy()
    support_world = helpers[ROLE_SUPPORT_GRIP].matrix_world.translation.copy()
    sight_world = helpers[ROLE_SIGHT_OCULAR].matrix_world.translation.copy()

    reach_r = _arm_reach(armature, "R")
    reach_l = _arm_reach(armature, "L")
    ratio_r = (primary_world - shoulder_r).length / reach_r
    ratio_l = (support_world - shoulder_l).length / reach_l
    if ratio_r > profile.max_reach or ratio_l > profile.max_reach:
        raise RuntimeError(
            "Rigid weapon ergonomics do not fit the shouldered_precision stance: "
            f"primary reach={ratio_r:.3f}, support reach={ratio_l:.3f}. "
            "Revise the weapon hardpoints/dimensions; the animation solver will "
            "not move or warp weapon parts to force a fit."
        )

    visor = bpy.data.objects.get("Helmet_Visor")
    if visor is None:
        raise RuntimeError("Helmet_Visor is required for sight-envelope validation.")
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
    sight_delta = sight_world - visor_center
    sight_lateral = abs(sight_delta.dot(visor_right))
    sight_vertical = abs(sight_delta.dot(visor_up))
    sight_front_clearance = sight_world.dot(visor_normal) - visor_front

    # Development-time policy: a non-catastrophic sight-envelope miss must not
    # prevent visual validation renders.  The head/neck has not settled yet at
    # this point, so this is only a pre-settle diagnostic.  Structural nonsense
    # (optic behind the helmet or far outside the character envelope) still stops.
    catastrophic_sight = (
        sight_lateral > 0.40
        or sight_vertical > 0.30
        or sight_front_clearance < -0.030
        or sight_front_clearance > 0.40
    )
    if catastrophic_sight:
        raise RuntimeError(
            "Rigid weapon is catastrophically outside the stance sight region: "
            f"lateral={sight_lateral:.3f} m, vertical={sight_vertical:.3f} m, "
            f"front={sight_front_clearance:.3f} m."
        )
    pre_sight_warning = (
        sight_lateral > profile.sight_lateral_tolerance_m
        or sight_vertical > profile.sight_vertical_tolerance_m
        or sight_front_clearance < profile.sight_front_min_m
        or sight_front_clearance > profile.sight_front_max_m
    )
    if pre_sight_warning:
        print(
            "WARNING: pre-settle sight envelope is outside preferred limits; "
            "continuing so the complete pose can be solved and rendered. "
            f"lateral={sight_lateral:.3f} m, vertical={sight_vertical:.3f} m, "
            f"front={sight_front_clearance:.3f} m",
            flush=True,
        )

    stock_surface_clearance = (stock_world - shoulder_anchor).length
    root["ps_aim_right_reach_ratio"] = float(ratio_r)
    root["ps_aim_left_reach_ratio"] = float(ratio_l)
    root["ps_aim_stock_inward_m"] = float(profile.stock_inward_m)
    root["ps_aim_stock_fore_aft_m"] = float(profile.stock_forward_m)
    root["ps_aim_stock_height_m"] = float(profile.stock_up_m)
    root["ps_aim_stock_surface_clearance_m"] = float(stock_surface_clearance)
    root["ps_aim_sight_lateral_m"] = float(sight_lateral)
    root["ps_aim_sight_vertical_m"] = float(sight_vertical)
    root["ps_aim_sight_front_clearance_m"] = float(sight_front_clearance)
    # Legacy report keys remain populated, but no exact eye-point target exists.
    root["ps_aim_scope_alignment_error_m"] = float(
        math.sqrt(sight_lateral * sight_lateral + sight_vertical * sight_vertical)
    )
    root["ps_aim_scope_lateral_error_m"] = float(sight_lateral)
    root["ps_aim_scope_height_error_m"] = float(sight_vertical)
    root["ps_aim_scope_forward_error_m"] = 0.0
    root["ps_aim_scope_front_clearance_m"] = float(sight_front_clearance)
    root["ps_aim_placement_mode"] = "stance_family_rigid"
    root["ps_aim_pre_settle_sight_warning"] = bool(pre_sight_warning)
    root["ps_aim_pre_settle_sight_lateral_m"] = float(sight_lateral)
    root["ps_aim_pre_settle_sight_vertical_m"] = float(sight_vertical)
    root["ps_aim_pre_settle_sight_front_m"] = float(sight_front_clearance)
    root["ps_aim_shoulder_anchor_world"] = tuple(float(v) for v in shoulder_anchor)
    root["ps_aim_sight_ocular_world"] = tuple(float(v) for v in sight_world)
    root["ps_aim_weapon_stance_family"] = profile.name

    return {
        "body_right": right,
        "body_forward": forward,
        "body_up": up,
        "outward_right_bone": outward_r,
        "outward_left_bone": outward_l,
        "stock_world": stock_world,
        "shoulder_anchor": shoulder_anchor,
        "right_reach_ratio": ratio_r,
        "left_reach_ratio": ratio_l,
        "stock_inward_m": profile.stock_inward_m,
        "stock_fore_aft_m": profile.stock_forward_m,
        "stock_height_m": profile.stock_up_m,
        "stock_surface_clearance_m": stock_surface_clearance,
        "scope_alignment_error_m": root["ps_aim_scope_alignment_error_m"],
        "scope_height_error_m": sight_vertical,
        "scope_lateral_error_m": sight_lateral,
        "scope_forward_error_m": 0.0,
        "scope_front_clearance_m": sight_front_clearance,
        "rifle_pitch_deg": profile.weapon_pitch_deg,
        "placement_mode": "stance_family_rigid",
        "stance_profile": profile,
    }


def _verify_visual_forward_alignment(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> float:
    """Reject any pose where the rifle points away from the helmet visor."""
    _right, forward, _up = body_basis(armature)
    rifle_forward = (root.matrix_world.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
    dot = rifle_forward.dot(forward)
    root["ps_aim_rifle_forward_dot_visual_forward"] = float(dot)
    if dot < 0.90:
        raise RuntimeError(
            "Rifle forward axis is opposite the character's visible face: "
            f"dot={dot:.3f}. The visor direction must be authoritative."
        )
    return dot

def _clear_pipeline_constraints(armature: bpy.types.Object) -> None:
    for pose_bone in armature.pose.bones:
        for constraint in list(pose_bone.constraints):
            if constraint.name.startswith(PIPELINE_TEMP_PREFIX):
                pose_bone.constraints.remove(constraint)


def _apply_torso_and_shoulders(
    armature: bpy.types.Object,
    right: Vector,
    forward: Vector,
    up: Vector,
    profile,
) -> None:
    """Apply the authored base stance before any hand IK is solved."""
    rotate_pose_bone_world(
        armature, "Spine", right, math.radians(profile.spine_pitch_deg)
    )
    rotate_pose_bone_world(
        armature, "Chest", right, math.radians(profile.chest_pitch_deg)
    )

    outward_r, _outward_l = named_shoulder_outward_axes(
        armature, right, forward, up
    )
    side_sign_r = 1.0 if outward_r.dot(right) >= 0.0 else -1.0
    rotate_pose_bone_world(
        armature, "Chest", up, math.radians(-side_sign_r * profile.chest_yaw_deg)
    )

    def rotate_shoulder_forward(bone_name: str, degrees: float) -> None:
        head = bone_head_world(armature, bone_name)
        tail = bone_tail_world(armature, bone_name)
        vector = tail - head
        angle = math.radians(degrees)
        positive = Matrix.Rotation(angle, 3, up) @ vector
        negative = Matrix.Rotation(-angle, 3, up) @ vector
        chosen = angle if positive.dot(forward) >= negative.dot(forward) else -angle
        rotate_pose_bone_world(armature, bone_name, up, chosen)

    rotate_shoulder_forward("Shoulder.R", profile.trigger_shoulder_forward_deg)
    rotate_shoulder_forward("Shoulder.L", profile.support_shoulder_forward_deg)


def _settle_head_toward_sight(
    armature: bpy.types.Object,
    placement: dict[str, object],
) -> None:
    """Settle the helmet toward the sight without making the optic drive the pose."""
    root = get_rifle_root()
    profile = placement["stance_profile"]
    right = Vector(placement["body_right"])
    forward = Vector(placement["body_forward"])
    up = Vector(placement["body_up"])
    visor = bpy.data.objects.get("Helmet_Visor")
    sight = require_weapon_helper(root, ROLE_SIGHT_OCULAR)
    if visor is None:
        raise RuntimeError("Helmet_Visor is required for sight settling.")

    def signed_angle_about(a: Vector, b: Vector, axis: Vector) -> float:
        axis_n = axis.normalized()
        aa = a - axis_n * a.dot(axis_n)
        bb = b - axis_n * b.dot(axis_n)
        if aa.length < 1.0e-6 or bb.length < 1.0e-6:
            return 0.0
        aa.normalize()
        bb.normalize()
        return math.atan2(axis_n.dot(aa.cross(bb)), aa.dot(bb))

    def visor_state():
        evaluated = visor.evaluated_get(bpy.context.evaluated_depsgraph_get())
        corners = [evaluated.matrix_world @ Vector(c) for c in evaluated.bound_box]
        center = (
            sum(corners, Vector((0.0, 0.0, 0.0))) / len(corners)
            if corners else evaluated.matrix_world.translation.copy()
        )
        basis = evaluated.matrix_world.to_3x3()
        normal = (basis @ Vector((0.0, 1.0, 0.0))).normalized()
        local_right = (basis @ Vector((1.0, 0.0, 0.0))).normalized()
        local_up = (basis @ Vector((0.0, 0.0, 1.0))).normalized()
        if normal.dot(forward) < 0.0:
            normal = -normal
        return center, normal, local_right, local_up, corners

    center, normal, _vr, _vu, _corners = visor_state()
    sight_direction = sight.matrix_world.translation - center
    if sight_direction.length < 1.0e-6:
        sight_direction = normal.copy()
    else:
        sight_direction.normalize()

    yaw = signed_angle_about(normal, sight_direction, up) * 0.30
    yaw = max(
        math.radians(-profile.head_yaw_limit_deg),
        min(math.radians(profile.head_yaw_limit_deg), yaw),
    )
    pitch = signed_angle_about(normal, sight_direction, right) * 0.22
    pitch = max(
        math.radians(-profile.head_pitch_limit_deg),
        min(math.radians(profile.head_pitch_limit_deg), pitch),
    )
    rotate_pose_bone_world(armature, "Neck", up, yaw * 0.35)
    rotate_pose_bone_world(armature, "Head", up, yaw * 0.65)
    rotate_pose_bone_world(armature, "Neck", right, pitch * 0.30)
    rotate_pose_bone_world(armature, "Head", right, pitch * 0.70)
    bpy.context.view_layer.update()

    # A human shooter naturally rolls/leans the head toward a shouldered optic.
    # The powered-suit helmet cannot translate, so a small deterministic roll is
    # the correct character-side adjustment.  Search only this bounded head/neck
    # degree of freedom; the rigid weapon is never moved or deformed.
    neck = armature.pose.bones.get("Neck")
    head = armature.pose.bones.get("Head")
    if neck is None or head is None:
        raise RuntimeError("Neck/Head pose bones are required for sight settling.")
    neck_base = neck.matrix_basis.copy()
    head_base = head.matrix_basis.copy()

    best_roll_deg = 0.0
    best_roll_score = None
    roll_limit = max(0, int(round(profile.head_roll_limit_deg)))
    for roll_deg in range(-roll_limit, roll_limit + 1):
        neck.matrix_basis = neck_base.copy()
        head.matrix_basis = head_base.copy()
        rotate_pose_bone_world(armature, "Neck", forward, math.radians(roll_deg * 0.25))
        rotate_pose_bone_world(armature, "Head", forward, math.radians(roll_deg * 0.75))
        bpy.context.view_layer.update()
        test_center, test_normal, test_right, test_up, test_corners = visor_state()
        test_delta = sight.matrix_world.translation - test_center
        test_lateral = abs(test_delta.dot(test_right))
        test_vertical = abs(test_delta.dot(test_up))
        test_front = sight.matrix_world.translation.dot(test_normal) - max(
            (point.dot(test_normal) for point in test_corners),
            default=test_center.dot(test_normal),
        )
        front_penalty = 0.0
        if test_front < profile.sight_front_min_m:
            front_penalty += profile.sight_front_min_m - test_front
        elif test_front > profile.sight_front_max_m:
            front_penalty += test_front - profile.sight_front_max_m
        score = (
            test_lateral
            + test_vertical * 0.30
            + front_penalty * 0.50
            + abs(roll_deg) * 0.00035
        )
        if best_roll_score is None or score < best_roll_score:
            best_roll_score = score
            best_roll_deg = float(roll_deg)

    neck.matrix_basis = neck_base.copy()
    head.matrix_basis = head_base.copy()
    rotate_pose_bone_world(armature, "Neck", forward, math.radians(best_roll_deg * 0.25))
    rotate_pose_bone_world(armature, "Head", forward, math.radians(best_roll_deg * 0.75))
    bpy.context.view_layer.update()

    center, normal, local_right, local_up, corners = visor_state()
    sight_world = sight.matrix_world.translation.copy()
    delta = sight_world - center
    lateral = abs(delta.dot(local_right))
    vertical = abs(delta.dot(local_up))
    front = sight_world.dot(normal) - max(
        (point.dot(normal) for point in corners), default=center.dot(normal)
    )
    final_sight_warning = (
        lateral > profile.sight_lateral_tolerance_m
        or vertical > profile.sight_vertical_tolerance_m
        or front < profile.sight_front_min_m
        or front > profile.sight_front_max_m
    )
    if final_sight_warning:
        print(
            "WARNING: final rigid sight relationship remains outside preferred "
            "stance limits. Validation renders will still be produced and export "
            "will remain locked until the blocker is resolved. "
            f"lateral={lateral:.3f} m, vertical={vertical:.3f} m, front={front:.3f} m",
            flush=True,
        )

    root["ps_aim_head_yaw_deg"] = float(math.degrees(yaw))
    root["ps_aim_final_sight_warning"] = bool(final_sight_warning)
    root["ps_aim_head_pitch_deg"] = float(math.degrees(pitch))
    root["ps_aim_head_roll_deg"] = float(best_roll_deg)
    root["ps_aim_sight_lateral_m"] = float(lateral)
    root["ps_aim_sight_vertical_m"] = float(vertical)
    root["ps_aim_sight_front_clearance_m"] = float(front)
    root["ps_aim_scope_lateral_error_m"] = float(lateral)
    root["ps_aim_scope_height_error_m"] = float(vertical)
    root["ps_aim_scope_front_clearance_m"] = float(front)
    root["ps_aim_scope_alignment_error_m"] = float(
        math.sqrt(lateral * lateral + vertical * vertical)
    )
    root["ps_aim_scope_forward_error_m"] = 0.0
    placement["scope_lateral_error_m"] = lateral
    placement["scope_height_error_m"] = vertical
    placement["scope_front_clearance_m"] = front
    placement["scope_alignment_error_m"] = root["ps_aim_scope_alignment_error_m"]


def _elbow_bend_degrees(
    shoulder: Vector,
    elbow: Vector,
    wrist: Vector,
) -> float:
    to_shoulder = shoulder - elbow
    to_wrist = wrist - elbow
    if to_shoulder.length < 1.0e-6 or to_wrist.length < 1.0e-6:
        return 180.0
    dot = max(-1.0, min(1.0, to_shoulder.normalized().dot(to_wrist.normalized())))
    return math.degrees(math.acos(dot))


def _elbow_plane_metrics(
    shoulder: Vector,
    elbow: Vector,
    wrist: Vector,
    outward_axis: Vector,
    forward: Vector,
    up: Vector,
) -> dict[str, float]:
    """Measure elbow bend relative to the shoulder-to-wrist chord.

    A support hand can legitimately cross the torso centreline, so measuring the
    elbow only from the shoulder falsely labels a good cross-body pose as
    "inward".  The useful test is whether the elbow bows outward/downward from
    the direct shoulder-to-wrist line.  Absolute values are retained only as
    diagnostics for the validation report.
    """
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
        "forward_clearance_m": offset.dot(forward),
        "absolute_outward_m": shoulder_offset.dot(outward_axis),
        "absolute_down_m": -shoulder_offset.dot(up),
        "absolute_forward_m": shoulder_offset.dot(forward),
        "bend_deg": _elbow_bend_degrees(shoulder, elbow, wrist),
    }


def _solve_arms(
    armature: bpy.types.Object,
    basis: dict[str, object],
) -> tuple[dict[str, Matrix], dict[str, float]]:
    right = basis["body_right"]
    forward = basis["body_forward"]
    up = basis["body_up"]
    outward_r = basis["outward_right_bone"]
    outward_l = basis["outward_left_bone"]

    source_r = require_weapon_helper(get_rifle_root(), ROLE_PRIMARY_GRIP)
    source_l = require_weapon_helper(get_rifle_root(), ROLE_SUPPORT_GRIP)
    target_r = _copy_object_world(source_r, "RightHandTarget")
    target_l = _copy_object_world(source_l, "LeftHandTarget")

    shoulder_r = bone_head_world(armature, "UpperArm.R")
    shoulder_l = bone_head_world(armature, "UpperArm.L")
    pole_r = _create_pole(
        "RightElbowPole",
        shoulder_r + outward_r * 0.40 + forward * 0.16 - up * 0.36,
    )
    pole_l = _create_pole(
        "LeftElbowPole",
        shoulder_l + outward_l * 0.40 + forward * 0.18 - up * 0.34,
    )

    lower_r = armature.pose.bones.get("LowerArm.R")
    lower_l = armature.pose.bones.get("LowerArm.L")
    if lower_r is None or lower_l is None:
        raise RuntimeError("Lower arm pose bones are missing.")

    constraint_r = lower_r.constraints.new("IK")
    constraint_r.name = PIPELINE_TEMP_PREFIX + "IK_R"
    constraint_r.target = target_r
    constraint_r.pole_target = pole_r
    constraint_r.chain_count = 2
    constraint_r.use_tail = True
    constraint_r.iterations = 128

    constraint_l = lower_l.constraints.new("IK")
    constraint_l.name = PIPELINE_TEMP_PREFIX + "IK_L"
    constraint_l.target = target_l
    constraint_l.pole_target = pole_l
    constraint_l.chain_count = 2
    constraint_l.use_tail = True
    constraint_l.iterations = 128

    # Each arm is independent, so search it independently at five-degree
    # increments.  This is both more accurate and much faster than testing every
    # right/left angle pair.  The score uses clearance from the shoulder-wrist
    # chord, which remains meaningful for a cross-body support hand.
    angle_candidates = tuple(math.radians(value) for value in range(-180, 180, 5))

    def choose_angle(
        constraint: bpy.types.Constraint,
        upper_bone_name: str,
        shoulder: Vector,
        wrist: Vector,
        outward_axis: Vector,
        target_bend: float,
        bend_weight: float,
    ) -> tuple[float, dict[str, float]]:
        best_score: float | None = None
        best_angle = 0.0
        best_metrics: dict[str, float] | None = None
        for angle in angle_candidates:
            constraint.pole_angle = angle
            bpy.context.view_layer.update()
            elbow = bone_tail_world(armature, upper_bone_name)
            metrics = _elbow_plane_metrics(
                shoulder, elbow, wrist, outward_axis, forward, up
            )

            outward_clearance = metrics["outward_clearance_m"]
            down_clearance = metrics["down_clearance_m"]
            absolute_forward = metrics["absolute_forward_m"]
            bend = metrics["bend_deg"]

            penalty = 0.0
            if outward_clearance < 0.0:
                penalty += -outward_clearance * 80.0
            if down_clearance < 0.0:
                penalty += -down_clearance * 90.0
            if absolute_forward < -0.10:
                penalty += (-0.10 - absolute_forward) * 20.0
            if bend < 35.0:
                penalty += (35.0 - bend) * 0.10
            if bend > 155.0:
                penalty += (bend - 155.0) * 0.16

            score = (
                outward_clearance * 6.0
                + down_clearance * 8.0
                + max(-0.10, absolute_forward) * 0.6
                - abs(bend - target_bend) * bend_weight
                - penalty
            )
            if best_score is None or score > best_score:
                best_score = score
                best_angle = angle
                best_metrics = metrics

        assert best_metrics is not None
        return best_angle, best_metrics

    angle_r, metrics_r = choose_angle(
        constraint_r,
        "UpperArm.R",
        shoulder_r,
        target_r.matrix_world.translation,
        outward_r,
        100.0,
        0.012,
    )
    constraint_r.pole_angle = angle_r
    bpy.context.view_layer.update()

    angle_l, metrics_l = choose_angle(
        constraint_l,
        "UpperArm.L",
        shoulder_l,
        target_l.matrix_world.translation,
        outward_l,
        142.0,
        0.045,
    )
    constraint_l.pole_angle = angle_l
    bpy.context.view_layer.update()

    elbow_metrics = {
        "right_elbow_outward_m": metrics_r["outward_clearance_m"],
        "left_elbow_outward_m": metrics_l["outward_clearance_m"],
        "right_elbow_down_m": metrics_r["down_clearance_m"],
        "left_elbow_down_m": metrics_l["down_clearance_m"],
        "right_elbow_forward_m": metrics_r["absolute_forward_m"],
        "left_elbow_forward_m": metrics_l["absolute_forward_m"],
        "right_elbow_bend_deg": metrics_r["bend_deg"],
        "left_elbow_bend_deg": metrics_l["bend_deg"],
        "right_elbow_absolute_outward_m": metrics_r["absolute_outward_m"],
        "left_elbow_absolute_outward_m": metrics_l["absolute_outward_m"],
        "right_elbow_absolute_down_m": metrics_r["absolute_down_m"],
        "left_elbow_absolute_down_m": metrics_l["absolute_down_m"],
    }

    # Reject only an unmistakably inverted or almost straight result.  Small
    # clearances are left for image inspection; numeric checks are not a
    # substitute for the mandatory close renders.
    bad_metrics = []
    for label in ("right_elbow_outward_m", "left_elbow_outward_m"):
        if elbow_metrics[label] < -0.015:
            bad_metrics.append(f"{label}={elbow_metrics[label]:.3f}")
    for label in ("right_elbow_down_m", "left_elbow_down_m"):
        if elbow_metrics[label] < -0.020:
            bad_metrics.append(f"{label}={elbow_metrics[label]:.3f}")
    for label in ("right_elbow_bend_deg", "left_elbow_bend_deg"):
        if elbow_metrics[label] < 20.0 or elbow_metrics[label] > 172.0:
            bad_metrics.append(f"{label}={elbow_metrics[label]:.1f}")
    catastrophic_arm = (
        metrics_r["bend_deg"] < 5.0
        or metrics_r["bend_deg"] > 178.0
        or metrics_l["bend_deg"] < 5.0
        or metrics_l["bend_deg"] > 178.0
        or metrics_r["outward_clearance_m"] < -0.15
        or metrics_l["outward_clearance_m"] < -0.15
    )
    if catastrophic_arm:
        raise RuntimeError(
            "Arm IK produced a structurally unusable result: " + ", ".join(bad_metrics)
        )
    if bad_metrics:
        print(
            "WARNING: arm solve is outside preferred pose limits; continuing to "
            "render for visual diagnosis: " + ", ".join(bad_metrics),
            flush=True,
        )

    # Capture the evaluated constrained arm pose, remove constraints, then reapply
    # the matrices. This is deterministic and avoids context-sensitive operators.
    evaluated = evaluated_pose_matrices(armature)
    lower_r.constraints.remove(constraint_r)
    lower_l.constraints.remove(constraint_l)
    bpy.context.view_layer.update()
    apply_pose_matrices(armature, evaluated)

    # Keep both wrist contact points fixed, but roll/tip the hands slightly around
    # the *existing* grip helpers.  Test Fix 21 reached the correct surfaces yet
    # left the segmented fingers visually crowded against the rails.  These small
    # local rotations do not move either wrist or change rifle placement.
    hand_r = armature.pose.bones.get("Hand.R")
    hand_l = armature.pose.bones.get("Hand.L")
    if hand_r is None or hand_l is None:
        raise RuntimeError("Hand pose bones are missing.")

    visual_right = Vector(basis["body_right"])
    side_sign_r = 1.0 if Vector(outward_r).dot(visual_right) >= 0.0 else -1.0
    side_sign_l = 1.0 if Vector(outward_l).dot(visual_right) >= 0.0 else -1.0

    def grip_oriented_world(
        target: bpy.types.Object,
        roll_deg: float,
        tip_deg: float,
    ) -> Matrix:
        world = target.matrix_world.copy()
        rotation = world.to_3x3()
        local_delta = (
            Matrix.Rotation(math.radians(roll_deg), 3, Vector((0.0, 1.0, 0.0)))
            @ Matrix.Rotation(math.radians(tip_deg), 3, Vector((1.0, 0.0, 0.0)))
        )
        return Matrix.Translation(world.translation) @ (rotation @ local_delta).to_4x4()

    # Keep the validated wrist contacts but reduce the aggressive roll.  The
    # connected finger shell added by the model stage now supplies the visible
    # wrap, so the wrists only need a small ergonomic settling rotation.
    trigger_roll_deg = -5.0 * side_sign_r
    support_roll_deg = 8.0 * side_sign_l
    trigger_tip_deg = 3.0
    support_tip_deg = -3.0
    hand_r.matrix = armature.matrix_world.inverted() @ grip_oriented_world(
        target_r, trigger_roll_deg, trigger_tip_deg
    )
    hand_l.matrix = armature.matrix_world.inverted() @ grip_oriented_world(
        target_l, support_roll_deg, support_tip_deg
    )
    bpy.context.view_layer.update()

    root = get_rifle_root()
    root["ps_aim_trigger_hand_roll_deg"] = float(trigger_roll_deg)
    root["ps_aim_trigger_hand_tip_deg"] = float(trigger_tip_deg)
    root["ps_aim_support_hand_roll_deg"] = float(support_roll_deg)
    root["ps_aim_support_hand_tip_deg"] = float(support_tip_deg)

    wrist_r = bone_head_world(armature, "Hand.R")
    wrist_l = bone_head_world(armature, "Hand.L")
    error_r = (wrist_r - target_r.matrix_world.translation).length
    error_l = (wrist_l - target_l.matrix_world.translation).length
    if error_r > 0.050 or error_l > 0.050:
        raise RuntimeError(
            f"Baked wrist solve is structurally off the weapon grips: R={error_r:.4f} m, "
            f"L={error_l:.4f} m."
        )
    wrist_warning = error_r > 0.012 or error_l > 0.012
    if wrist_warning:
        print(
            "WARNING: wrist solve missed preferred helper tolerance; continuing to "
            f"render: R={error_r:.4f} m, L={error_l:.4f} m",
            flush=True,
        )
    root["ps_aim_arm_solve_warning"] = bool(bad_metrics)
    root["ps_aim_wrist_contact_warning"] = bool(wrist_warning)

    solved = evaluated_pose_matrices(armature)
    metrics = {
        "right_wrist_error_m": error_r,
        "left_wrist_error_m": error_l,
        "right_pole_angle_deg": math.degrees(angle_r),
        "left_pole_angle_deg": math.degrees(angle_l),
        **elbow_metrics,
    }
    return solved, metrics

def _basis_snapshot(armature: bpy.types.Object) -> dict[str, Matrix]:
    return {bone.name: bone.matrix_basis.copy() for bone in armature.pose.bones}


def _apply_basis_snapshot(
    armature: bpy.types.Object,
    snapshot: dict[str, Matrix],
) -> None:
    for bone in armature.pose.bones:
        matrix = snapshot.get(bone.name)
        if matrix is not None:
            bone.matrix_basis = matrix
    bpy.context.view_layer.update()


def _ensure_fcurve(channelbag, data_path: str, index: int, group_name: str):
    curves = channelbag.fcurves
    ensure = getattr(curves, "ensure", None)
    if ensure is not None:
        return ensure(data_path, index=index, group_name=group_name)
    existing = curves.find(data_path, index=index)
    if existing is not None:
        return existing
    return curves.new(data_path=data_path, index=index, group_name=group_name)


def _insert_curve_key(curve, frame: int, value: float) -> None:
    point = curve.keyframe_points.insert(
        float(frame), float(value), options={"FAST"}
    )
    point.interpolation = "BEZIER"
    point.handle_left_type = "AUTO_CLAMPED"
    point.handle_right_type = "AUTO_CLAMPED"


def _key_current_pose(
    action: bpy.types.Action,
    slot,
    armature: bpy.types.Object,
    frame: int,
) -> None:
    """Write pose keys directly into the channelbag for the supplied slot.

    PoseBone.keyframe_insert() is intentionally not used here.  With Blender
    5.x slotted Actions it can implicitly create/select animation storage that
    is not the explicit slot later activated by validation.
    """
    if bpy.context.scene.frame_current != frame:
        raise RuntimeError(
            f"Keying frame {frame} while scene is on {bpy.context.scene.frame_current}."
        )
    adt = armature.animation_data
    if adt is None or adt.action != action or adt.action_slot != slot:
        raise RuntimeError("PS_Aim Action/Slot changed before direct key insertion.")

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
        data = (
            ("location", tuple(bone.location)),
            (rotation_property, rotation_values),
            ("scale", tuple(bone.scale)),
        )
        for property_name, values in data:
            data_path = f'pose.bones["{bone.name}"].{property_name}'
            for index, value in enumerate(values):
                curve = _ensure_fcurve(
                    channelbag, data_path, index, bone.name
                )
                _insert_curve_key(curve, frame, value)


def _matrix_basis_max_delta(a: Matrix, b: Matrix) -> float:
    return max(
        abs(a[row][column] - b[row][column])
        for row in range(4)
        for column in range(4)
    )


def _verify_baked_action(
    armature: bpy.types.Object,
    action: bpy.types.Action,
    slot,
    expected_frame_1: dict[str, Matrix],
    idle_frame_1: dict[str, Matrix],
) -> dict[str, float | int]:
    stats = action_slot_curve_stats(action, slot)
    expected_curves = expected_transform_curve_count(armature, action, slot)
    expected_keys = expected_curves * 3
    if stats["curve_count"] != expected_curves:
        raise RuntimeError(
            "PS_Aim was not written to its explicit Action Slot: "
            f"expected {expected_curves} F-Curves, found {stats['curve_count']}."
        )
    if stats["keyframe_count"] != expected_keys or stats["empty_curve_count"]:
        raise RuntimeError(
            "PS_Aim Action Slot has incomplete keyframe data: " + str(stats)
        )

    # Start from Idle deliberately.  If PS_Aim is empty or bound to the wrong
    # channelbag, switching to it will leave this Idle pose behind and fail.
    activate_action(armature, BASE_ACTION_NAME)
    bpy.context.scene.frame_set(FRAME_START)
    bpy.context.view_layer.update()
    activate_action(armature, action)
    bpy.context.scene.frame_set(FRAME_STABILIZE)
    bpy.context.scene.frame_set(FRAME_START)
    bpy.context.view_layer.update()

    evaluated = _basis_snapshot(armature)
    expected_delta = max(
        _matrix_basis_max_delta(evaluated[name], expected_frame_1[name])
        for name in expected_frame_1
    )
    if expected_delta > 2.0e-4:
        raise RuntimeError(
            "PS_Aim slot does not evaluate to the baked pose "
            f"(maximum basis error {expected_delta:.3e})."
        )

    required = (
        "Chest", "Head",
        "Shoulder.R", "UpperArm.R", "LowerArm.R", "Hand.R",
        "Shoulder.L", "UpperArm.L", "LowerArm.L", "Hand.L",
    )
    idle_deltas = {
        name: _matrix_basis_max_delta(evaluated[name], idle_frame_1[name])
        for name in required
    }
    changed = [name for name, value in idle_deltas.items() if value > 1.0e-3]
    maximum_idle_delta = max(idle_deltas.values())
    if len(changed) < 6 or maximum_idle_delta < 1.0e-2:
        raise RuntimeError(
            "PS_Aim slot still evaluates like PS_Idle: "
            f"changed={changed}, max basis delta={maximum_idle_delta:.3e}."
        )
    return {
        **stats,
        "expected_curve_count": expected_curves,
        "expected_keyframe_count": expected_keys,
        "maximum_baked_pose_basis_error": expected_delta,
        "maximum_idle_to_aim_basis_delta": maximum_idle_delta,
        "changed_required_bone_count": len(changed),
    }


def _all_action_fcurves(action: bpy.types.Action):
    direct = getattr(action, "fcurves", None)
    if direct is not None:
        yield from direct
    for layer in getattr(action, "layers", ()):
        for strip in getattr(layer, "strips", ()):
            for bag in getattr(strip, "channelbags", ()):
                yield from getattr(bag, "fcurves", ())


def _set_interpolation(action: bpy.types.Action) -> None:
    seen = set()
    for curve in _all_action_fcurves(action):
        pointer = curve.as_pointer()
        if pointer in seen:
            continue
        seen.add(pointer)
        for key in curve.keyframe_points:
            key.interpolation = "BEZIER"
            key.handle_left_type = "AUTO_CLAMPED"
            key.handle_right_type = "AUTO_CLAMPED"
        curve.update()


def _create_stabilization_pose(
    armature: bpy.types.Object,
    pose_frame_1: dict[str, Matrix],
    right: Vector,
    up: Vector,
) -> dict[str, Matrix]:
    _apply_basis_snapshot(armature, pose_frame_1)
    rotate_pose_bone_world(armature, "Chest", right, math.radians(-0.22))
    rotate_pose_bone_world(armature, "Chest", up, math.radians(-0.12))
    rotate_pose_bone_world(armature, "Head", right, math.radians(0.18))
    rotate_pose_bone_world(armature, "Head", up, math.radians(0.10))
    return _basis_snapshot(armature)


def _parent_rifle_after_bake(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> None:
    _clear_pipeline_constraints(armature)
    remove_pipeline_temps()
    active_constraints = [
        f"{bone.name}:{constraint.name}"
        for bone in armature.pose.bones
        for constraint in bone.constraints
        if constraint.type == "IK"
    ]
    if active_constraints:
        raise RuntimeError(
            "Refusing final rifle parenting while IK remains active: "
            + ", ".join(active_constraints)
        )

    world = root.matrix_world.copy()
    root.parent = armature
    root.parent_type = "BONE"
    root.parent_bone = "Hand.R"
    root.matrix_world = world
    bpy.context.view_layer.update()

    if root.parent != armature or root.parent_bone != "Hand.R":
        raise RuntimeError("RifleRoot final bone parenting failed.")
    if root.matrix_world.to_3x3().determinant() <= 0.0:
        raise RuntimeError("Final RifleRoot world transform became reflected.")
    if any(value <= 0.0 for value in root.scale):
        raise RuntimeError(
            "Final RifleRoot local scale is non-positive: " + str(tuple(root.scale))
        )


def _verify_lower_body_preserved(
    armature: bpy.types.Object,
    idle_basis: dict[str, Matrix],
    aim_basis: dict[str, Matrix],
) -> None:
    failures = []
    for name in LOWER_BODY_BONES:
        idle = idle_basis[name]
        aim = aim_basis[name]
        maximum = max(abs(idle[row][column] - aim[row][column]) for row in range(4) for column in range(4))
        if maximum > 1.0e-6:
            failures.append(f"{name} ({maximum:.2e})")
    if failures:
        raise RuntimeError(
            "PS_Aim changed lower-body/rest motion copied from PS_Idle: "
            + ", ".join(failures)
        )


def main() -> None:
    require_blender_52()
    ensure_object_mode()
    remove_pipeline_temps()

    armature = get_armature()
    root = get_rifle_root()
    _assert_rifle_ready(root)

    # Explicitly evaluate the known-good Idle Action and its Blender 5.2 slot.
    activate_action(armature, BASE_ACTION_NAME)
    bpy.context.scene.frame_set(FRAME_START)
    bpy.context.view_layer.update()
    idle_matrices = evaluated_pose_matrices(armature)
    idle_basis = _basis_snapshot(armature)
    idle_rotation_modes = {bone.name: bone.rotation_mode for bone in armature.pose.bones}

    # Begin from the evaluated idle pose, then modify only the aim upper body.
    apply_pose_matrices(armature, idle_matrices)
    right, forward, up = body_basis(armature)
    stance_profile = get_stance_profile(str(root["ps_weapon_stance_family"]))
    _apply_torso_and_shoulders(armature, right, forward, up, stance_profile)
    placement = _place_rigid_weapon_from_stance(armature, root)
    visual_forward_dot = _verify_visual_forward_alignment(armature, root)
    placement["rifle_forward_dot_visual_forward"] = visual_forward_dot
    solved_matrices, solve_metrics = _solve_arms(armature, placement)
    apply_pose_matrices(armature, solved_matrices)
    _settle_head_toward_sight(armature, placement)
    frame_1_basis = _basis_snapshot(armature)
    _verify_lower_body_preserved(armature, idle_basis, frame_1_basis)
    frame_15_basis = _create_stabilization_pose(armature, frame_1_basis, right, up)

    # Keep PS_Aim in the same per-bone rotation representation as PS_Idle.
    # Rotation mode is persistent armature state in Blender, so mixing Euler Idle
    # curves with quaternion Aim curves would make one action appear frozen after
    # saving/reopening the file.
    for bone in armature.pose.bones:
        bone.rotation_mode = idle_rotation_modes[bone.name]
    _apply_basis_snapshot(armature, frame_1_basis)

    # Replace only PS_Aim with a fresh datablock and a fresh slot. This prevents the
    # orphan channelbag accumulation found in the uploaded files.
    action, slot = create_action_with_slot(
        armature, ACTION_NAME, FRAME_START, FRAME_END
    )
    bpy.context.scene.frame_set(FRAME_START)
    _apply_basis_snapshot(armature, frame_1_basis)
    _key_current_pose(action, slot, armature, FRAME_START)
    bpy.context.scene.frame_set(FRAME_STABILIZE)
    _apply_basis_snapshot(armature, frame_15_basis)
    _key_current_pose(action, slot, armature, FRAME_STABILIZE)
    bpy.context.scene.frame_set(FRAME_END)
    _apply_basis_snapshot(armature, frame_1_basis)
    _key_current_pose(action, slot, armature, FRAME_END)
    _set_interpolation(action)
    bake_validation = _verify_baked_action(
        armature, action, slot, frame_1_basis, idle_basis
    )

    bpy.context.scene.render.fps = FPS
    bpy.context.scene.frame_start = FRAME_START
    bpy.context.scene.frame_end = FRAME_END
    activate_action(armature, action)
    bpy.context.scene.frame_set(FRAME_START)
    bpy.context.view_layer.update()

    # Required final order: all solving/baking is complete before this call.
    _parent_rifle_after_bake(armature, root)
    activate_action(armature, action)
    bpy.context.scene.frame_set(FRAME_START)
    assert_weapon_rigid(root)

    output = save_current_blend("powersuit_pipeline.blend")
    print("\nPS_Aim rebuilt with Blender 5.2 Action Slots.")
    print(f"Action slot: {getattr(slot, 'identifier', getattr(slot, 'name', '<slot>'))}")
    print(
        "Action data: "
        f"{bake_validation['curve_count']} curves, "
        f"{bake_validation['keyframe_count']} keys, "
        f"{bake_validation['changed_required_bone_count']} required bones changed"
    )
    print(f"Right reach ratio: {placement['right_reach_ratio']:.3f}")
    print(f"Left reach ratio:  {placement['left_reach_ratio']:.3f}")
    print(f"Placement mode:    {placement['placement_mode']}")
    print(f"Stance family:     {root.get('ps_weapon_stance_family', 'missing')}")
    print(
        "Stock fit offsets: "
        f"inward={placement['stock_inward_m']:.3f} m, "
        f"fore/aft={placement['stock_fore_aft_m']:.3f} m, "
        f"height={placement['stock_height_m']:.3f} m"
    )
    print(
        "Rigid fit envelope: "
        f"stock offset={placement['stock_surface_clearance_m']:.3f} m, "
        f"sight lateral/vertical={placement['scope_lateral_error_m']:.3f}/"
        f"{placement['scope_height_error_m']:.3f} m, "
        f"sight front={placement['scope_front_clearance_m']:.3f} m, "
        f"weapon pitch={placement['rifle_pitch_deg']:.1f} deg"
    )
    print(f"Right wrist error: {solve_metrics['right_wrist_error_m']:.4f} m")
    print(f"Left wrist error:  {solve_metrics['left_wrist_error_m']:.4f} m")
    print(
        "Hand grip rotations: "
        f"trigger roll/tip={root.get('ps_aim_trigger_hand_roll_deg', 0.0):.1f}/"
        f"{root.get('ps_aim_trigger_hand_tip_deg', 0.0):.1f} deg; "
        f"support roll/tip={root.get('ps_aim_support_hand_roll_deg', 0.0):.1f}/"
        f"{root.get('ps_aim_support_hand_tip_deg', 0.0):.1f} deg"
    )
    print(
        "Head sight correction: "
        f"yaw={root.get('ps_aim_head_yaw_deg', 0.0):.1f} deg, "
        f"pitch={root.get('ps_aim_head_pitch_deg', 0.0):.1f} deg"
    )
    print(
        "Elbows: "
        f"R clearance outward/down={solve_metrics['right_elbow_outward_m']:.3f}/"
        f"{solve_metrics['right_elbow_down_m']:.3f} m, "
        f"bend={solve_metrics['right_elbow_bend_deg']:.1f} deg; "
        f"L clearance outward/down={solve_metrics['left_elbow_outward_m']:.3f}/"
        f"{solve_metrics['left_elbow_down_m']:.3f} m, "
        f"bend={solve_metrics['left_elbow_bend_deg']:.1f} deg"
    )
    print("Rigid weapon child signature preserved through posing and baking.")
    print("Temporary IK controls removed; RifleRoot parented only after baking.")
    print(f"Saved: {output}")


if __name__ == "__main__":
    try:
        main()
    finally:
        # Re-running after any failure begins from a clean temporary-control state.
        armature = bpy.data.objects.get("PowerSuit_Armature")
        if armature is not None:
            _clear_pipeline_constraints(armature)
        remove_pipeline_temps()
