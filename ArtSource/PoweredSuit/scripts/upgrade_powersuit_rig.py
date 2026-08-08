# pyright: reportMissingImports=false
"""Controlled Powered Suit V2 arm-proportion upgrade.

Pipeline responsibility:
- adjust only the armature rest geometry needed for weapon handling
- preserve the armature object and every existing bone name/hierarchy
- preserve PS_Idle, PS_Walk, and PS_Hover Action data exactly
- remove the old PS_Aim so create_aim_animation.py can rebuild it cleanly
- verify legacy Actions still evaluate safely on the revised rest skeleton

Important preservation rule:
Changing connected bone lengths intentionally changes arm endpoint positions.
Therefore old absolute arm-space matrices cannot and should not be required to
remain identical. The safe contract is to preserve the original Action curves
and keys byte-for-byte, verify all unaffected body bones retain their sampled
poses, and validate that the revised arm chains evaluate to finite transforms.

This script does not model the suit, create the rifle, render, or export.
The batch launcher always starts from the audited source file, making reruns
deterministic. A version marker also prevents accidental double application.
"""
from __future__ import annotations

import math
import sys
from pathlib import Path
from typing import Any

import bpy  # type: ignore
from mathutils import Matrix  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from powersuit_pipeline_common import (  # noqa: E402
    activate_action,
    action_rotation_modes,
    action_slot_curve_stats,
    ensure_object_mode,
    evaluated_pose_matrices,
    find_action_slot,
    get_action_channelbag,
    get_armature,
    REQUIRED_RIG_UPGRADE_VERSION,
    require_blender_52,
    save_current_blend,
)

RIG_UPGRADE_VERSION = REQUIRED_RIG_UPGRADE_VERSION
LEGACY_ACTIONS = ("PS_Idle", "PS_Walk", "PS_Hover")
AIM_ACTION = "PS_Aim"

# Controlled proportions. These are intentionally moderate and only affect the
# arm chains. The V2 shell modelling stage is matched to these values.
SHOULDER_LENGTH_SCALE = 0.80
UPPER_ARM_LENGTH_SCALE = 1.14
LOWER_ARM_LENGTH_SCALE = 1.16
HAND_LENGTH_SCALE = 1.03

ARM_BONES = {
    f"{stem}.{side}"
    for side in ("L", "R")
    for stem in ("Shoulder", "UpperArm", "LowerArm", "Hand")
}


def _set_scene_frame(frame: float) -> None:
    whole = math.floor(frame)
    bpy.context.scene.frame_set(int(whole), subframe=float(frame - whole))
    bpy.context.view_layer.update()


def _float_tuple(values) -> tuple[float, ...]:
    return tuple(float(value) for value in values)


def _action_key_frames(action: bpy.types.Action, slot) -> tuple[float, ...]:
    channelbag = get_action_channelbag(action, slot)
    frames = {
        float(point.co.x)
        for curve in channelbag.fcurves
        for point in curve.keyframe_points
    }
    if not frames:
        raise RuntimeError(f"Action '{action.name}' contains no keyframes.")
    return tuple(sorted(frames))


def _action_signature(action: bpy.types.Action, slot) -> tuple[Any, ...]:
    """Return an exact, deterministic signature of one Action Slot's data.

    This deliberately records curve paths, indices, extrapolation, key values,
    interpolation, and handle data. The rig stage must not rewrite or simplify
    the user's working Idle/Walk/Hover animation data.
    """
    channelbag = get_action_channelbag(action, slot)
    curves = []
    for curve in sorted(
        channelbag.fcurves,
        key=lambda item: (item.data_path, int(item.array_index)),
    ):
        points = []
        for point in curve.keyframe_points:
            points.append(
                (
                    _float_tuple(point.co),
                    str(point.interpolation),
                    str(point.easing),
                    _float_tuple(point.handle_left),
                    str(point.handle_left_type),
                    _float_tuple(point.handle_right),
                    str(point.handle_right_type),
                )
            )
        curves.append(
            (
                str(curve.data_path),
                int(curve.array_index),
                str(curve.extrapolation),
                tuple(points),
            )
        )

    slots = tuple(
        (
            str(getattr(item, "identifier", "")),
            str(getattr(item, "name", "")),
            str(getattr(item, "target_id_type", "")),
        )
        for item in action.slots
    )
    return (
        str(action.name),
        float(action.frame_start),
        float(action.frame_end),
        slots,
        tuple(curves),
    )


def _matrix_max_delta(a: Matrix, b: Matrix) -> float:
    return max(
        abs(float(a[row][column]) - float(b[row][column]))
        for row in range(4)
        for column in range(4)
    )


def _matrix_is_finite(matrix: Matrix) -> bool:
    return all(
        math.isfinite(float(matrix[row][column]))
        for row in range(4)
        for column in range(4)
    )


def _capture_legacy_state(armature: bpy.types.Object) -> dict[str, dict[str, Any]]:
    state: dict[str, dict[str, Any]] = {}
    unaffected = [
        bone.name for bone in armature.pose.bones if bone.name not in ARM_BONES
    ]

    for name in LEGACY_ACTIONS:
        action = bpy.data.actions.get(name)
        if action is None:
            raise RuntimeError(f"Required legacy Action '{name}' was not found.")
        slot = find_action_slot(action, armature)
        frames = _action_key_frames(action, slot)
        _reset_pose(armature)
        activate_action(armature, action)

        non_arm_poses: dict[float, dict[str, Matrix]] = {}
        for frame in frames:
            _set_scene_frame(frame)
            non_arm_poses[frame] = evaluated_pose_matrices(armature, unaffected)

        state[name] = {
            "action": action,
            "slot": slot,
            "frames": frames,
            "signature": _action_signature(action, slot),
            "rotation_modes": action_rotation_modes(action, slot),
            "non_arm_poses": non_arm_poses,
            "stats": action_slot_curve_stats(action, slot),
        }
    return state


def _reset_pose(armature: bpy.types.Object) -> None:
    adt = armature.animation_data_create()
    adt.action = None
    for bone in armature.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def _upgrade_arm_chain(edit_bones, side: str) -> dict[str, float]:
    shoulder = edit_bones[f"Shoulder.{side}"]
    upper = edit_bones[f"UpperArm.{side}"]
    lower = edit_bones[f"LowerArm.{side}"]
    hand = edit_bones[f"Hand.{side}"]

    original_shoulder_head = shoulder.head.copy()
    original_shoulder_tail = shoulder.tail.copy()
    original_upper_head = upper.head.copy()
    original_upper_tail = upper.tail.copy()
    original_lower_head = lower.head.copy()
    original_lower_tail = lower.tail.copy()
    original_hand_head = hand.head.copy()
    original_hand_tail = hand.tail.copy()

    shoulder_vector = original_shoulder_tail - original_shoulder_head
    upper_vector = original_upper_tail - original_upper_head
    lower_vector = original_lower_tail - original_lower_head
    hand_vector = original_hand_tail - original_hand_head

    shoulder_length = shoulder_vector.length
    upper_length = upper_vector.length
    lower_length = lower_vector.length
    hand_length = hand_vector.length
    if min(shoulder_length, upper_length, lower_length, hand_length) < 1.0e-5:
        raise RuntimeError(f"Arm chain {side} contains a zero-length bone.")

    shoulder_offset_to_upper = original_upper_head - original_shoulder_tail

    shoulder.tail = (
        original_shoulder_head
        + shoulder_vector.normalized() * shoulder_length * SHOULDER_LENGTH_SCALE
    )

    # UpperArm is intentionally not connected in the source rig. Preserve its
    # original shoulder-joint offset while moving it with the shorter clavicle.
    upper.head = shoulder.tail + shoulder_offset_to_upper
    upper.tail = (
        upper.head
        + upper_vector.normalized() * upper_length * UPPER_ARM_LENGTH_SCALE
    )

    # LowerArm and Hand remain connected to the revised parent tails.
    lower.use_connect = True
    lower.tail = (
        lower.head
        + lower_vector.normalized() * lower_length * LOWER_ARM_LENGTH_SCALE
    )

    hand.use_connect = True
    hand.tail = (
        hand.head
        + hand_vector.normalized() * hand_length * HAND_LENGTH_SCALE
    )

    return {
        "shoulder_old": shoulder_length,
        "shoulder_new": shoulder.length,
        "upper_old": upper_length,
        "upper_new": upper.length,
        "lower_old": lower_length,
        "lower_new": lower.length,
        "hand_old": hand_length,
        "hand_new": hand.length,
        "reach_old": shoulder_length + upper_length + lower_length + hand_length,
        "reach_new": shoulder.length + upper.length + lower.length + hand.length,
    }


def _edit_rest_skeleton(armature: bpy.types.Object) -> dict[str, dict[str, float]]:
    ensure_object_mode()
    bpy.ops.object.select_all(action="DESELECT")
    armature.hide_set(False)
    armature.hide_viewport = False
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        edit_bones = armature.data.edit_bones
        required = sorted(ARM_BONES)
        missing = [name for name in required if edit_bones.get(name) is None]
        if missing:
            raise RuntimeError("Missing required arm bones: " + ", ".join(missing))
        metrics = {side: _upgrade_arm_chain(edit_bones, side) for side in ("L", "R")}
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    return metrics


def _verify_legacy_actions(
    armature: bpy.types.Object,
    captured: dict[str, dict[str, Any]],
) -> dict[str, dict[str, float | int]]:
    results: dict[str, dict[str, float | int]] = {}

    for name in LEGACY_ACTIONS:
        expected = captured[name]
        action = bpy.data.actions.get(name)
        if action is None or action is not expected["action"]:
            raise RuntimeError(f"Legacy Action '{name}' was replaced unexpectedly.")
        slot = find_action_slot(action, armature)

        current_signature = _action_signature(action, slot)
        if current_signature != expected["signature"]:
            raise RuntimeError(
                f"Legacy Action '{name}' curve/key data changed during rig upgrade."
            )

        current_modes = action_rotation_modes(action, slot)
        if current_modes != expected["rotation_modes"]:
            raise RuntimeError(
                f"Legacy Action '{name}' rotation representation changed."
            )

        _reset_pose(armature)
        activate_action(armature, action)
        maximum_non_arm_error = 0.0
        maximum_arm_scale = 0.0
        minimum_arm_scale = float("inf")

        for frame in expected["frames"]:
            _set_scene_frame(frame)
            evaluated = evaluated_pose_matrices(armature)
            for bone_name, matrix in evaluated.items():
                if not _matrix_is_finite(matrix):
                    raise RuntimeError(
                        f"Legacy Action '{name}' produced a non-finite matrix for "
                        f"'{bone_name}' at frame {frame:g}."
                    )

            for bone_name, reference in expected["non_arm_poses"][frame].items():
                maximum_non_arm_error = max(
                    maximum_non_arm_error,
                    _matrix_max_delta(evaluated[bone_name], reference),
                )

            for bone_name in ARM_BONES:
                bone = armature.pose.bones.get(bone_name)
                if bone is None:
                    raise RuntimeError(f"Missing revised arm pose bone '{bone_name}'.")
                scales = [abs(float(value)) for value in bone.matrix.to_scale()]
                maximum_arm_scale = max(maximum_arm_scale, *scales)
                minimum_arm_scale = min(minimum_arm_scale, *scales)

        # Only arm-chain rest geometry was edited. Torso, head, legs, and root
        # sampled matrices must remain unchanged to tight numerical tolerance.
        if maximum_non_arm_error > 2.0e-4:
            raise RuntimeError(
                f"Rig upgrade changed non-arm motion in '{name}' "
                f"(maximum matrix error {maximum_non_arm_error:.3e})."
            )
        if minimum_arm_scale < 0.05 or maximum_arm_scale > 8.0:
            raise RuntimeError(
                f"Legacy Action '{name}' evaluates with implausible arm scale "
                f"range {minimum_arm_scale:.3f}..{maximum_arm_scale:.3f}."
            )

        stats = action_slot_curve_stats(action, slot)
        if stats != expected["stats"]:
            raise RuntimeError(f"Legacy Action '{name}' statistics changed.")
        results[name] = {
            **stats,
            "maximum_non_arm_matrix_error": maximum_non_arm_error,
            "minimum_arm_matrix_scale": minimum_arm_scale,
            "maximum_arm_matrix_scale": maximum_arm_scale,
        }
    return results


def main() -> None:
    require_blender_52()
    ensure_object_mode()
    armature = get_armature()

    if int(armature.get("ps_v2_rig_upgrade_version", 0)) >= RIG_UPGRADE_VERSION:
        print("Powered Suit V2 rig proportions are already current; no changes made.")
        return

    captured = _capture_legacy_state(armature)
    _reset_pose(armature)
    metrics = _edit_rest_skeleton(armature)

    # PS_Aim must be rebuilt after the revised rig and rifle exist.
    old_aim = bpy.data.actions.get(AIM_ACTION)
    if old_aim is not None:
        adt = armature.animation_data
        if adt is not None and adt.action == old_aim:
            adt.action = None
        bpy.data.actions.remove(old_aim, do_unlink=True)

    verification = _verify_legacy_actions(armature, captured)

    activate_action(armature, "PS_Idle")
    _set_scene_frame(float(captured["PS_Idle"]["frames"][0]))

    armature["ps_v2_rig_upgrade_version"] = RIG_UPGRADE_VERSION
    armature["ps_v2_shoulder_length_scale"] = SHOULDER_LENGTH_SCALE
    armature["ps_v2_upper_arm_length_scale"] = UPPER_ARM_LENGTH_SCALE
    armature["ps_v2_lower_arm_length_scale"] = LOWER_ARM_LENGTH_SCALE
    armature["ps_v2_hand_length_scale"] = HAND_LENGTH_SCALE
    armature["ps_v2_legacy_actions_preserved"] = True

    path = save_current_blend()
    print("Powered Suit V2 rig proportions upgraded.")
    for side in ("L", "R"):
        values = metrics[side]
        gain = values["reach_new"] / values["reach_old"]
        print(
            f"Arm {side}: clavicle {values['shoulder_old']:.3f}->{values['shoulder_new']:.3f} m, "
            f"upper {values['upper_old']:.3f}->{values['upper_new']:.3f} m, "
            f"lower {values['lower_old']:.3f}->{values['lower_new']:.3f} m, "
            f"hand {values['hand_old']:.3f}->{values['hand_new']:.3f} m, "
            f"total chain x{gain:.3f}"
        )
    for name, result in verification.items():
        print(
            f"Preserved {name}: {result['curve_count']} curves, "
            f"{result['keyframe_count']} keys, non-arm error "
            f"{result['maximum_non_arm_matrix_error']:.3e}, arm scale "
            f"{result['minimum_arm_matrix_scale']:.3f}.."
            f"{result['maximum_arm_matrix_scale']:.3f}"
        )
    print("PS_Aim removed for clean reconstruction by create_aim_animation.py.")
    print(f"Saved: {path}")


if __name__ == "__main__":
    main()
