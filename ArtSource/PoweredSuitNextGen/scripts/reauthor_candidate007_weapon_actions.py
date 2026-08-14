# pyright: reportMissingImports=false
"""Re-author Candidate007 weapon actions against its immutable hardpoints.

This is an in-memory Blender 5.2 pipeline stage.  It deliberately delegates the
actual aim and weapon-action construction to the already vetted Generator114
solvers, but adapts their pre-control-rig input contract to Candidate005/006's
existing 23-bone carrier rig.  The stage never saves, exports, models, or edits
the legacy pipeline blend.

Public API::

    evidence = reauthor_candidate007_weapon_actions(armature, rifle_root)

Preconditions:
- exactly the canonical 23 bones and 24 PS_ actions exist;
- RifleRoot is the new, frozen Candidate007 rigid source definition;
- its magazine/bolt objects may already be parented to their control bones;
- production render meshes are not part of the rigid weapon definition.

Postconditions:
- PS_Aim and all twenty weapon actions are rebuilt;
- PS_Idle, PS_Walk and PS_Hover body-curve semantics remain unchanged,
  including keys, handles, interpolation, extrapolation, groups and modifiers;
- the canonical 23-bone/24-action/range/single-slot contract is restored;
- RifleRoot, magazine and bolt finish on recreated non-deforming controls;
- a measured carrier-to-root matrix is returned and stored for render adapters.

Failure is intentionally fatal.  Call this only while constructing a disposable
Candidate007 output; the owning builder decides whether that result is saved.
"""
from __future__ import annotations

import hashlib
import json
import math
import sys
from contextlib import contextmanager
from dataclasses import replace
from pathlib import Path
from typing import Iterator

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore


ROOT = Path(__file__).resolve().parents[3]
PIPELINE = ROOT / "ArtSource" / "PoweredSuit" / "scripts"
PINNED_PIPELINE_BLEND = ROOT / "ArtSource" / "PoweredSuit" / "powersuit_pipeline.blend"

if str(PIPELINE) not in sys.path:
    sys.path.insert(0, str(PIPELINE))

import create_aim_animation as aim_stage  # type: ignore  # noqa: E402
import create_weapon_animation_set as weapon_stage  # type: ignore  # noqa: E402
from powersuit_pipeline_common import (  # type: ignore  # noqa: E402
    LEGACY_ACTIONS,
    REQUIRED_ACTIONS,
    WEAPON_ANIMATION_ACTIONS,
    activate_action,
    body_basis,
    create_action_with_slot,
    ensure_object_mode,
    find_action_slot,
    get_action_channelbag,
    matrix_world_for_pose_bone,
    remove_pipeline_temps,
    select_only,
)
from weapon_handling_contract import (  # type: ignore  # noqa: E402
    COMPONENT_BOLT,
    COMPONENT_MAGAZINE,
    WEAPON_OWNER_PROPERTY,
    assert_articulated_components_at_rest,
    assert_weapon_rigid,
    validate_weapon_contract,
    weapon_components,
)


CONTROL_BONES = ("WeaponRoot", "WeaponMagazine", "WeaponBolt")
BODY_BONES = (
    "Root", "Hips", "Spine", "Chest", "Neck", "Head",
    "Shoulder.L", "UpperArm.L", "LowerArm.L", "Hand.L",
    "Shoulder.R", "UpperArm.R", "LowerArm.R", "Hand.R",
    "UpperLeg.L", "LowerLeg.L", "Foot.L",
    "UpperLeg.R", "LowerLeg.R", "Foot.R",
)
EXPECTED_BONES = (*BODY_BONES, *CONTROL_BONES)
EXPECTED_ACTION_RANGES = {
    "PS_Idle": (1, 61),
    "PS_Walk": (1, 31),
    "PS_Hover": (1, 61),
    "PS_Aim": (1, 30),
    "PS_WeaponReady_Idle": (1, 61),
    "PS_WeaponStowed_Idle": (1, 61),
    "PS_Weapon_Draw": (1, 30),
    "PS_Weapon_Sheathe": (1, 30),
    "PS_Walk_Forward": (1, 31),
    "PS_Walk_Backward": (1, 31),
    "PS_Walk_Left": (1, 31),
    "PS_Walk_Right": (1, 31),
    "PS_Aim_Walk_Forward": (1, 31),
    "PS_Aim_Walk_Backward": (1, 31),
    "PS_Aim_Walk_Left": (1, 31),
    "PS_Aim_Walk_Right": (1, 31),
    "PS_Reload": (1, 84),
    "PS_BoltCycle": (1, 20),
    "PS_WeaponStowed_Walk_Forward": (1, 31),
    "PS_WeaponStowed_Walk_Backward": (1, 31),
    "PS_WeaponStowed_Walk_Left": (1, 31),
    "PS_WeaponStowed_Walk_Right": (1, 31),
    "PS_WeaponStowed_Hover": (1, 61),
    "PS_Run_Forward": (1, 21),
}
STOW_REARWARD_DELTA_M = 0.33
STOW_OUTWARD_DELTA_M = 0.04
DRAW_EXTRACTION_BACK_CLEARANCE_M = 0.08
DRAW_EXTRACTION_LATERAL_M = 0.04
RELOAD_HAND_OUTWARD_M = 0.09
RELOAD_MAGAZINE_OUTWARD_M = 0.05
RELOAD_PALM_ROLL_DEG = 25.0
BOLT_PALM_ROLL_DEG = 30.0
BOLT_HAND_OUTWARD_M = 0.04
HAND_CONTACT_PAD_CENTER_LOCAL = {
    "L": (0.0005016, 0.2179851, 0.0639991),
    "R": (0.0005006, 0.2178152, 0.0640004),
}
SHARED_RELOAD_TARGET_OUTWARD_M = 0.035
SHARED_BOLT_TARGET_OUTWARD_M = 0.035
BOLT_TARGET_TRAVEL_Y_RANGE_M = (-0.095, 0.0)
BOLT_TARGET_CORRIDOR_AXIS_TOLERANCE_M = 1.0e-6
BOLT_TARGET_CLASSIFIER_MODE = "exact_root_local_shared_bolt_call_corridor"
RELOAD_MAGAZINE_HALF_WIDTH_M = 0.030
RELOAD_CONTACT_INSET_M = 0.001
RELOAD_DETACHED_TWIST_DEG = 60.0
RELOAD_PULL_LUG_OBJECT_NAME = "NGPR_MagazinePullLug_L"
BOLT_CONTACT_INSET_M = 0.001
BOLT_KNOB_OBJECT_NAME = "NGPR_BoltKnob"
HAND_CONTACT_SOLVE_TOLERANCE_M = 5.0e-6
MANIPULATION_SOLVER_VERSION = "CANDIDATE007_MANIPULATION_SOLVER_V3"
MANIPULATION_DENSIFICATION_VERSION = "CANDIDATE007_MANIPULATION_DENSIFICATION_V5"
MANIPULATION_SAMPLE_STEP_FRAMES = 0.25
RELOAD_CONTACT_WINDOW = (25.0, 75.0)
BOLT_CONTACT_WINDOW = (4.0, 16.0)
RELOAD_APPROACH_FRAMES = (14.0, 16.0, 18.75, 20.0, 24.0, 25.0)
RELOAD_RETURN_FRAMES = (75.0, 79.0, 82.0, 84.0)
BOLT_APPROACH_FRAMES = (1.0, 1.75, 2.5, 3.0, 4.0)
BOLT_RETURN_FRAMES = (16.0, 17.0, 17.5, 18.5, 20.0)
MANIPULATION_HOVER_CLEARANCE_M = 0.025
MANIPULATION_TRANSIT_CLEARANCE_M = 0.100
MANIPULATION_GRIP_RELEASE_M = 0.060
MANIPULATION_GRIP_RELEASE_UP_M = 0.020
RELOAD_YOKE_CLEARANCE_M = 0.005
BOLT_GRIP_RELEASE_M = 0.072
BOLT_APPROACH_TRANSIT_CLEARANCE_M = 0.115
BOLT_TRANSIT_CONTACT_CLEARANCE_M = 0.035
BOLT_MEASURED_RELEASE_PATH_VERSION = "CANDIDATE007_BOLT_RELEASE_PATH_V2"
BOLT_MEASURED_POSE_SUBSTITUTIONS = {
    2.375: 3.0,
    2.5: 3.0,
    17.5: 17.0,
    17.625: 17.0,
}
BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M = {
    3.875: 0.025,
    6.125: 0.035,
    6.875: 0.035,
    13.875: 0.035,
    16.125: 0.025,
}
# Root-local Hand.R deltas measured from the V7 visible-geometry collision
# normals, then verified over every quarter-frame of PS_BoltCycle.  Keeping
# them root-local makes the path deterministic if the carrier is transformed
# in a downstream review scene.
BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M = {
    1.25: (0.000000008, 0.000053250, -0.002375834),
    1.50: (0.000000015, 0.000106500, -0.004751668),
    1.75: (0.000000008, 0.000053250, -0.002375834),
    18.75: (-0.001610478, 0.000063438, -0.001026265),
    19.00: (-0.003220956, 0.000126878, -0.002052529),
    19.25: (0.000000064, 0.000091366, -0.004085246),
    19.50: (-0.000000026, 0.000057798, -0.002586497),
    19.75: (-0.000000013, 0.000028899, -0.001293249),
}
RELOAD_MEASURED_RETURN_PATH_VERSION = "CANDIDATE007_RELOAD_RETURN_PATH_V1"
RELOAD_MEASURED_RETURN_BLEND_ENDPOINT_FRAMES = (79.0, 82.0)
RELOAD_MEASURED_RETURN_ANCHOR_FRAMES = (79.75, 80.0)
RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M = {
    79.875: (0.002, 0.0, 0.0),
}
MANIPULATION_HOVER_MODE = "grip_release__outboard_transit__face_normal_ramp"
TRANSITION_PATH_VERSION = "CANDIDATE007_GUIDED_DEPLOY_LATE_CATCH_V3"
TRANSITION_SAMPLE_STEP_FRAMES = 0.125
TRANSITION_CERTIFICATION_STEP_FRAMES = 0.125
TRANSITION_DRAW_KEY_FRAMES = (
    1.0, 6.0, 10.0, 16.0, 18.0, 20.0, 22.0, 24.0, 26.0, 27.0,
    28.0, 28.125, 28.25, 28.375, 28.5, 28.625, 28.75, 28.875,
    29.0, 29.125, 29.25, 29.375, 29.5, 29.625, 29.75, 29.875, 30.0,
)
TRANSITION_GUIDED_CORRIDOR_KEY_FRAMES = (
    1.0, 6.0, 10.0, 16.0, 18.0, 20.0, 22.0, 24.0, 26.0, 28.0, 29.0, 30.0,
)
TRANSITION_GUIDED_THROUGH_FRAME = 26.0
TRANSITION_PREGRASP_FRAME = 27.0
TRANSITION_PREGRASP_TARGET_FRAME = 28.0
TRANSITION_PREGRASP_CLEARANCE_M = 0.012
TRANSITION_OWNERSHIP_START_FRAME = 28.0
TRANSITION_OWNERSHIP_DENSE_END_FRAME = 29.875
TRANSITION_READY_FRAME = 30.0
TRANSITION_PRIMARY_CONTACT_DRAW_WINDOW = (26.75, 30.0)
TRANSITION_PRIMARY_CONTACT_SHEATHE_WINDOW = (1.0, 4.25)
TRANSITION_SUPPORT_CONTACT_DRAW_WINDOW = (29.0, 30.0)
TRANSITION_SUPPORT_CONTACT_SHEATHE_WINDOW = (1.0, 2.0)
TRANSITION_STOWED_BACK_M = 0.20
TRANSITION_STOWED_OUTBOARD_M = 0.55
TRANSITION_STOWED_UP_M = 0.15
TRANSITION_FORWARD_OUTBOARD_M = 0.65
TRANSITION_FORWARD_AHEAD_M = 0.85
TRANSITION_FORWARD_UP_M = 0.18
TRANSITION_PREDOCK_OUTBOARD_M = 0.35
TRANSITION_PREDOCK_AHEAD_M = 0.55
TRANSITION_PREDOCK_UP_M = 0.10
TRANSITION_DOCK_MID_OUTBOARD_M = 0.18
TRANSITION_DOCK_MID_AHEAD_M = 0.32
TRANSITION_DOCK_MID_UP_M = 0.05
TRANSITION_DOCK_NEAR_OUTBOARD_M = 0.08
TRANSITION_DOCK_NEAR_AHEAD_M = 0.14
TRANSITION_DOCK_NEAR_UP_M = 0.02
RELOAD_PATH_MODE = "identity_endpoints__outward_before_detached_delta"
BOLT_TARGET_MODE = "tagged_knob_min_x_face_distal_pad"
RELOAD_CONTACT_MODE = "seated_v2__detached_distal_pad_positive_x_face"
REAUTHOR_VERSION = "CANDIDATE007_WEAPON_ACTIONS_V11"
ACTION_SIGNATURE_SCHEMA = "CANDIDATE007_ACTION_SEMANTICS_V10"

# Candidate007 has a materially wider receiver and a different stock/optic
# relationship than the Generator114 rifle.  These values are authored on the
# isolated RifleRoot by the Candidate007 builder so a review blend records the
# exact stance inputs that produced its actions.  The legacy stance profile is
# never mutated.
STANCE_PROPERTY_DEFAULTS = {
    "ps_candidate007_stock_inward_m": 0.045,
    "ps_candidate007_stock_forward_m": 0.035,
    "ps_candidate007_stock_up_m": 0.070,
    "ps_candidate007_weapon_pitch_deg": -6.000,
    "ps_candidate007_trigger_shoulder_forward_deg": 14.000,
    "ps_candidate007_support_shoulder_forward_deg": 38.000,
    "ps_candidate007_aiming_eye_outward_m": 0.055,
}
STOW_REARWARD_PROPERTY = "ps_candidate007_stow_rearward_delta_m"
STOW_OUTWARD_PROPERTY = "ps_candidate007_stow_outward_delta_m"
READY_POSE_MODE = "forward_preaim_head_neutral"
TRANSITION_POSE_MODE = (
    "powered_back_mount_guided__measured_pregrasp__hand_r_owned_ready_dock_symmetric"
)


def _sha256(path: Path) -> str | None:
    if not path.is_file():
        return None
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _matrix_values(matrix: Matrix) -> list[float]:
    return [round(float(matrix[row][column]), 10) for row in range(4) for column in range(4)]


def _signature_value(value: object) -> object:
    """Return deterministic JSON-safe evidence for a Blender RNA value."""
    if value is None or isinstance(value, (bool, int, str)):
        return value
    if isinstance(value, float):
        return round(value, 9)
    if hasattr(value, "to_tuple"):
        try:
            return [_signature_value(item) for item in value.to_tuple()]
        except (TypeError, ValueError):
            pass
    if isinstance(value, (list, tuple)):
        return [_signature_value(item) for item in value]
    return str(value)


def _rna_properties(owner: object, *, excluded: set[str] | None = None) -> dict[str, object]:
    """Capture writable/simple RNA properties without depending on modifier type."""
    excluded = {"rna_type", *(excluded or set())}
    bl_rna = getattr(owner, "bl_rna", None)
    properties = getattr(bl_rna, "properties", ())
    result: dict[str, object] = {}
    for definition in properties:
        identifier = str(getattr(definition, "identifier", ""))
        if not identifier or identifier in excluded:
            continue
        try:
            value = getattr(owner, identifier)
        except (AttributeError, RuntimeError, TypeError, ValueError):
            continue
        if getattr(definition, "type", "") == "COLLECTION":
            collection = []
            for item in value:
                collection.append(_rna_properties(item))
            result[identifier] = collection
            continue
        # Pointer properties are presentation/runtime links rather than curve
        # evaluation data. Avoid serialising unstable repr()/memory addresses.
        if getattr(definition, "type", "") == "POINTER":
            continue
        result[identifier] = _signature_value(value)
    return result


def _keyframe_document(point: object) -> dict[str, object]:
    return {
        "co": _signature_value(point.co),
        "handle_left": _signature_value(point.handle_left),
        "handle_right": _signature_value(point.handle_right),
        "interpolation": str(point.interpolation),
        "handle_left_type": str(point.handle_left_type),
        "handle_right_type": str(point.handle_right_type),
        "easing": str(point.easing),
        "amplitude": round(float(point.amplitude), 9),
        "back": round(float(point.back), 9),
        "period": round(float(point.period), 9),
        "type": str(point.type),
    }


def _modifier_document(modifier: object) -> dict[str, object]:
    return {
        "type": str(getattr(modifier, "type", "")),
        "properties": _rna_properties(modifier),
    }


def _curve_group_document(curve: object) -> dict[str, object] | None:
    group = getattr(curve, "group", None)
    if group is None:
        return None
    return {
        "name": str(group.name),
        "mute": bool(group.mute),
        "lock": bool(group.lock),
    }


def _action_document(action: bpy.types.Action, armature: bpy.types.Object) -> dict[str, object]:
    slot = find_action_slot(action, armature)
    bag = get_action_channelbag(action, slot)
    curves = []
    for curve in sorted(bag.fcurves, key=lambda item: (str(item.data_path), int(item.array_index))):
        curves.append({
            "data_path": str(curve.data_path),
            "array_index": int(curve.array_index),
            "extrapolation": str(curve.extrapolation),
            "auto_smoothing": str(curve.auto_smoothing),
            "mute": bool(curve.mute),
            "group": _curve_group_document(curve),
            "modifiers": [_modifier_document(modifier) for modifier in curve.modifiers],
            "keys": [_keyframe_document(point) for point in curve.keyframe_points],
        })
    return {
        "name": action.name,
        "range": [int(round(action.frame_start)), int(round(action.frame_end))],
        "slot_count": len(list(action.slots)),
        "slot_id_types": [str(getattr(slot_item, "target_id_type", "")) for slot_item in action.slots],
        "curves": curves,
    }


def _action_signature(action: bpy.types.Action, armature: bpy.types.Object) -> dict[str, object]:
    document = _action_document(action, armature)
    encoded = json.dumps(document, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return {
        "range": document["range"],
        "slot_count": document["slot_count"],
        "curve_count": len(document["curves"]),
        "sha256": hashlib.sha256(encoded).hexdigest(),
    }


def _body_only_action_hash(action: bpy.types.Action, armature: bpy.types.Object) -> str:
    document = _action_document(action, armature)
    document["curves"] = [
        curve for curve in document["curves"]
        if not any(f'pose.bones["{name}"]' in str(curve["data_path"]) for name in CONTROL_BONES)
    ]
    encoded = json.dumps(document, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _assert_exact_input(armature: bpy.types.Object, root: bpy.types.Object) -> None:
    actual_bones = tuple(bone.name for bone in armature.data.bones)
    if actual_bones != EXPECTED_BONES:
        raise RuntimeError(
            "Candidate007 animation input must be the ordered canonical 23-bone rig; "
            f"got {actual_bones}."
        )
    names = {action.name for action in bpy.data.actions if action.name.startswith("PS_")}
    if names != set(REQUIRED_ACTIONS):
        raise RuntimeError(
            "Candidate007 animation input must contain exactly 24 PS_ actions; "
            f"missing={sorted(set(REQUIRED_ACTIONS) - names)}, "
            f"unexpected={sorted(names - set(REQUIRED_ACTIONS))}."
        )
    if root.name != "RifleRoot" or root.type != "EMPTY":
        raise RuntimeError("Candidate007 rigid weapon root must be the RifleRoot empty.")
    if int(root.get("ps_generator_version", 0)) < 6006:
        raise RuntimeError("RifleRoot is not the Candidate007 rigid source definition.")
    assert_weapon_rigid(root)
    validate_weapon_contract(root)


def _remove_control_curves_from_legacy(armature: bpy.types.Object) -> dict[str, int]:
    removed: dict[str, int] = {}
    for action_name in LEGACY_ACTIONS:
        action = bpy.data.actions[action_name]
        slot = find_action_slot(action, armature)
        bag = get_action_channelbag(action, slot)
        doomed = [
            curve for curve in list(bag.fcurves)
            if any(f'pose.bones["{name}"]' in str(curve.data_path) for name in CONTROL_BONES)
        ]
        for curve in doomed:
            bag.fcurves.remove(curve)
        removed[action_name] = len(doomed)
    return removed


def _normalize_to_precontrol_rig(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> dict[str, object]:
    """Restore the vetted solvers' 20-bone/root-under-Hand.R entry contract."""
    ensure_object_mode()
    remove_pipeline_temps()
    activate_action(armature, "PS_Aim")
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()

    # The new authored source is immutable in root space.  Components may have
    # already been adapted to controls by the builder; put only those approved
    # pieces back under RifleRoot at their authored identity transform.
    magazines = weapon_components(root, COMPONENT_MAGAZINE)
    bolts = weapon_components(root, COMPONENT_BOLT)
    if not magazines or not bolts:
        raise RuntimeError("Candidate007 requires tagged magazine and bolt source components.")
    for obj in [*magazines, *bolts]:
        obj.parent = root
        obj.parent_type = "OBJECT"
        obj.parent_bone = ""
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_basis = Matrix.Identity(4)

    # Aim expects an independent RifleRoot, then owns the final Hand.R parenting.
    # Seed identity in authored/world space; its solver immediately places the
    # rigid definition entirely from the new stock/grip/sight helpers.
    root.parent = None
    root.parent_type = "OBJECT"
    root.parent_bone = ""
    root.matrix_parent_inverse = Matrix.Identity(4)
    root.matrix_world = Matrix.Identity(4)
    armature.data.pose_position = "POSE"
    bpy.context.view_layer.update()
    assert_articulated_components_at_rest(root)
    assert_weapon_rigid(root)

    removed_curves = _remove_control_curves_from_legacy(armature)
    select_only([armature], active=armature)
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        edit_bones = armature.data.edit_bones
        for name in reversed(CONTROL_BONES):
            bone = edit_bones.get(name)
            if bone is None:
                raise RuntimeError(f"Missing pre-existing control bone {name}.")
            edit_bones.remove(bone)
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    if tuple(bone.name for bone in armature.data.bones) != BODY_BONES:
        raise RuntimeError("Failed to normalize Candidate007 to the canonical 20 body bones.")
    return {
        "legacy_control_curves_removed": removed_curves,
        "magazine_objects": [obj.name for obj in magazines],
        "bolt_objects": [obj.name for obj in bolts],
    }


def _validate_exact_output(armature: bpy.types.Object, root: bpy.types.Object) -> None:
    actual_bones = tuple(bone.name for bone in armature.data.bones)
    if actual_bones != EXPECTED_BONES:
        raise RuntimeError(f"Candidate007 reauthor changed the canonical bone contract: {actual_bones}")
    names = {action.name for action in bpy.data.actions if action.name.startswith("PS_")}
    if names != set(EXPECTED_ACTION_RANGES):
        raise RuntimeError("Candidate007 reauthor did not restore the exact 24-action set.")
    for name, expected_range in EXPECTED_ACTION_RANGES.items():
        action = bpy.data.actions[name]
        actual_range = (int(round(action.frame_start)), int(round(action.frame_end)))
        if actual_range != expected_range:
            raise RuntimeError(f"{name} range {actual_range} != {expected_range}.")
        slots = list(action.slots)
        if len(slots) != 1 or str(getattr(slots[0], "target_id_type", "")) != "OBJECT":
            raise RuntimeError(f"{name} does not have exactly one OBJECT Action Slot.")
    if (
        root.parent != armature
        or root.parent_type != "BONE"
        or root.parent_bone != "WeaponRoot"
    ):
        raise RuntimeError("Vetted animation stage did not restore RifleRoot on WeaponRoot.")
    for name in CONTROL_BONES:
        if armature.data.bones[name].use_deform:
            raise RuntimeError(f"Control bone {name} unexpectedly deforms geometry.")
    activate_action(armature, "PS_Aim")
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    assert_articulated_components_at_rest(root)
    assert_weapon_rigid(root)


def _candidate007_stowed_world(armature: bpy.types.Object) -> Matrix:
    """Move the vetted scabbard target rearward and clear of the back plate."""
    target = weapon_stage._candidate007_original_stowed_world(armature)
    right, forward, _up = body_basis(armature)
    result = target.copy()
    rearward_delta = STOW_REARWARD_DELTA_M
    outward_delta = STOW_OUTWARD_DELTA_M
    if not 0.20 <= rearward_delta <= 0.35:
        raise RuntimeError(
            f"Candidate007 stow rearward delta {rearward_delta:.3f} m is outside "
            "the audited 0.20-0.35 m envelope."
        )
    if not 0.03 <= outward_delta <= 0.05:
        raise RuntimeError(
            f"Candidate007 stow outward delta {outward_delta:.3f} m is outside "
            "the requested 0.03-0.05 m envelope."
        )
    result.translation = (
        target.translation
        - forward * rearward_delta
        - right * outward_delta
    )
    if result.to_3x3().determinant() <= 0.0:
        raise RuntimeError("Candidate007 stowed target became reflected.")
    return result


def _candidate007_stance_profile(root: bpy.types.Object):
    """Return an immutable per-candidate derivative of the vetted long-gun stance."""
    base = aim_stage.get_stance_profile("shouldered_precision")
    values = dict(STANCE_PROPERTY_DEFAULTS)
    profile = replace(
        base,
        stock_inward_m=values["ps_candidate007_stock_inward_m"],
        stock_forward_m=values["ps_candidate007_stock_forward_m"],
        stock_up_m=values["ps_candidate007_stock_up_m"],
        weapon_pitch_deg=values["ps_candidate007_weapon_pitch_deg"],
        trigger_shoulder_forward_deg=values[
            "ps_candidate007_trigger_shoulder_forward_deg"
        ],
        support_shoulder_forward_deg=values[
            "ps_candidate007_support_shoulder_forward_deg"
        ],
        aiming_eye_outward_m=values["ps_candidate007_aiming_eye_outward_m"],
    )
    if not -0.08 <= profile.stock_inward_m <= 0.10:
        raise RuntimeError("Candidate007 stock inward offset is outside its audited envelope.")
    if not -0.02 <= profile.stock_forward_m <= 0.10:
        raise RuntimeError("Candidate007 stock fore/aft offset is outside its audited envelope.")
    if not 0.00 <= profile.stock_up_m <= 0.10:
        raise RuntimeError("Candidate007 stock height is outside its audited envelope.")
    if not -8.0 <= profile.weapon_pitch_deg <= 5.0:
        raise RuntimeError("Candidate007 weapon pitch is outside its audited envelope.")
    if not 8.0 <= profile.trigger_shoulder_forward_deg <= 30.0:
        raise RuntimeError("Candidate007 trigger shoulder angle is outside its audited envelope.")
    if not 20.0 <= profile.support_shoulder_forward_deg <= 55.0:
        raise RuntimeError("Candidate007 support shoulder angle is outside its audited envelope.")
    if not 0.04 <= profile.aiming_eye_outward_m <= 0.10:
        raise RuntimeError("Candidate007 aiming-eye offset is outside its audited envelope.")
    return profile


def _candidate007_ready_pose(
    armature: bpy.types.Object,
    _root: bpy.types.Object,
    idle_basis: dict[str, Matrix],
    _original_root_local: Matrix,
) -> dict[str, Matrix]:
    """Use the solved forward long-gun stance for hip fire, with a neutral head.

    Generator114's generic ready solver points a rifle diagonally upward across
    the chest.  That is incompatible with Candidate007 gameplay: unaimed fire
    still travels forward.  PS_Aim already owns the measured rigid hardpoint fit,
    so Ready inherits that upper-body solve while Neck/Head return to Idle.  The
    weapon therefore stays forward and both hands remain on their immutable
    helpers without pretending the character is looking through the optic.
    """
    ready = weapon_stage._evaluate_basis(armature, "PS_Aim", 1)
    for name in ("Neck", "Head"):
        ready[name] = idle_basis[name].copy()
    return ready


def _maximum_matrix_delta(first: Matrix, second: Matrix) -> float:
    return max(
        abs(float(first[row][column]) - float(second[row][column]))
        for row in range(4)
        for column in range(4)
    )


def _orient_solved_hand_about_wrist(
    armature: bpy.types.Object,
    solved_pose: dict[str, Matrix],
    side: str,
    palm_roll_deg: float,
) -> dict[str, Matrix]:
    """Roll only a solved manipulation hand without moving its IK target.

    The shared single-arm solver owns shoulder/elbow placement and the wrist
    contact point.  Candidate007 adds palm presentation only after that solve:
    the hand rotates around its own local Y axis while its world translation is
    held exactly.  Fail closed if either upstream arm matrix changes or the
    wrist moves beyond floating-point noise.
    """

    if side not in {"L", "R"}:
        raise ValueError(f"Unsupported manipulation-hand side: {side!r}")
    if not math.isfinite(palm_roll_deg):
        raise ValueError("Manipulation palm roll must be finite")

    upper_name = f"UpperArm.{side}"
    lower_name = f"LowerArm.{side}"
    hand_name = f"Hand.{side}"
    required = {upper_name, lower_name, hand_name}
    missing = sorted(required - set(solved_pose))
    if missing:
        raise RuntimeError(f"Solved manipulation pose lacks bones: {missing}")

    weapon_stage._apply_basis_snapshot(armature, solved_pose)
    upper_before = armature.pose.bones[upper_name].matrix_basis.copy()
    lower_before = armature.pose.bones[lower_name].matrix_basis.copy()
    hand = armature.pose.bones[hand_name]
    hand_world = matrix_world_for_pose_bone(armature, hand)
    wrist_before = hand_world.translation.copy()
    local_roll = Matrix.Rotation(
        math.radians(float(palm_roll_deg)),
        3,
        Vector((0.0, 1.0, 0.0)),
    )
    desired_world = (
        Matrix.Translation(wrist_before)
        @ (hand_world.to_3x3() @ local_roll).to_4x4()
    )
    hand.matrix = armature.matrix_world.inverted_safe() @ desired_world
    bpy.context.view_layer.update()

    wrist_after = matrix_world_for_pose_bone(armature, hand).translation
    wrist_error = float((wrist_after - wrist_before).length)
    upper_error = _maximum_matrix_delta(
        upper_before, armature.pose.bones[upper_name].matrix_basis
    )
    lower_error = _maximum_matrix_delta(
        lower_before, armature.pose.bones[lower_name].matrix_basis
    )
    if wrist_error > 1.0e-6 or upper_error > 1.0e-8 or lower_error > 1.0e-8:
        raise RuntimeError(
            "Candidate007 manipulation orientation changed positional IK: "
            f"wrist={wrist_error:.9f}, upper={upper_error:.9f}, "
            f"lower={lower_error:.9f}."
        )
    return weapon_stage._basis_snapshot(armature)


def _solve_hand_contact_frame(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    side: str,
    contact_world: Vector,
    desired_rotation_world: Matrix,
    original_solver=None,
) -> dict[str, Matrix]:
    """Place a measured distal pad on a contact point with an exact hand frame.

    The procedural glove has no finger bones.  Its manipulation semantic is a
    rigid fingertip cap roughly 227 mm from the wrist, so aiming the wrist at a
    magazine or bolt necessarily buries the palm.  This adapter derives the IK
    wrist target from that measured lever arm, solves the two-bone arm, then
    applies the desired rigid hand orientation without disturbing the solve.
    """

    if side not in HAND_CONTACT_PAD_CENTER_LOCAL:
        raise ValueError(f"Unsupported distal-pad side: {side!r}")
    if len(desired_rotation_world) != 3 or any(
        len(row) != 3 for row in desired_rotation_world
    ):
        raise ValueError("Distal-pad orientation must be a 3x3 rotation matrix")
    determinant = float(desired_rotation_world.determinant())
    if not math.isfinite(determinant) or determinant <= 0.0:
        raise RuntimeError("Distal-pad contact frame is not a positive rotation")

    pad_local = Vector(HAND_CONTACT_PAD_CENTER_LOCAL[side])
    wrist_target = contact_world - (desired_rotation_world @ pad_local)
    original = (
        original_solver
        if original_solver is not None
        else weapon_stage._candidate007_original_single_arm_pose
    )
    solved_pose = original(armature, base_pose, side, wrist_target)
    weapon_stage._apply_basis_snapshot(armature, solved_pose)
    bpy.context.view_layer.update()

    # Blender's temporary two-bone IK can stop a few micrometres short for
    # certain in-between targets.  Correct only that measured residual once;
    # this retains the strict 5 um contract instead of weakening it for dense
    # samples.
    initial_hand_world = matrix_world_for_pose_bone(
        armature, armature.pose.bones[f"Hand.{side}"]
    )
    initial_residual = wrist_target - initial_hand_world.translation
    if initial_residual.length > HAND_CONTACT_SOLVE_TOLERANCE_M:
        corrected_target = wrist_target + initial_residual
        solved_pose = original(
            armature,
            base_pose,
            side,
            corrected_target,
        )
        weapon_stage._apply_basis_snapshot(armature, solved_pose)
        bpy.context.view_layer.update()

    upper_name = f"UpperArm.{side}"
    lower_name = f"LowerArm.{side}"
    hand_name = f"Hand.{side}"
    upper_before = armature.pose.bones[upper_name].matrix_basis.copy()
    lower_before = armature.pose.bones[lower_name].matrix_basis.copy()
    hand = armature.pose.bones[hand_name]
    solved_wrist = matrix_world_for_pose_bone(armature, hand).translation.copy()
    wrist_target_error = float((solved_wrist - wrist_target).length)

    desired_hand_world = (
    Matrix.Translation(solved_wrist) @ desired_rotation_world.to_4x4()
    )
    hand.matrix = armature.matrix_world.inverted_safe() @ desired_hand_world
    bpy.context.view_layer.update()

    final_hand_world = matrix_world_for_pose_bone(armature, hand)
    final_wrist = final_hand_world.translation
    final_pad = final_hand_world @ pad_local
    final_wrist_error = float((final_wrist - wrist_target).length)
    pad_contact_error = float((final_pad - contact_world).length)
    upper_error = _maximum_matrix_delta(
        upper_before, armature.pose.bones[upper_name].matrix_basis
    )
    lower_error = _maximum_matrix_delta(
        lower_before, armature.pose.bones[lower_name].matrix_basis
    )
    tolerance_m = HAND_CONTACT_SOLVE_TOLERANCE_M
    if (
        wrist_target_error > tolerance_m
        or final_wrist_error > tolerance_m
        or pad_contact_error > tolerance_m
        or upper_error > 1.0e-8
        or lower_error > 1.0e-8
    ):
        raise RuntimeError(
            "Candidate007 distal-pad solve failed its rigid contact invariant: "
            f"side={side}, initial_wrist={wrist_target_error:.9f}, "
            f"final_wrist={final_wrist_error:.9f}, pad={pad_contact_error:.9f}, "
            f"upper={upper_error:.9f}, lower={lower_error:.9f}."
        )
    return weapon_stage._basis_snapshot(armature)


def _root_rotation_world(root: bpy.types.Object) -> Matrix:
    """Return the rigid weapon-root orientation without scale or translation."""

    rotation = root.matrix_world.to_quaternion().to_matrix()
    if float(rotation.determinant()) <= 0.0:
        raise RuntimeError("Candidate007 weapon root has a reflected orientation")
    return rotation


def _reload_detached_contact_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    root: bpy.types.Object,
    incoming_center_root: Vector,
    original_solver,
) -> dict[str, Matrix]:
    """Present the left distal pad to the detached magazine's +X face."""

    magazines = weapon_components(root, COMPONENT_MAGAZINE)
    lugs = [obj for obj in magazines if obj.name == RELOAD_PULL_LUG_OBJECT_NAME]
    if len(lugs) != 1:
        raise RuntimeError(
            f"Candidate007 requires one {RELOAD_PULL_LUG_OBJECT_NAME}, found {len(lugs)}"
        )
    lug = lugs[0]
    if not bool(lug.get("ps_weapon_mesh_transform_baked_v5", False)):
        raise RuntimeError("Candidate007 magazine pull lug is not in baked root space")
    points = [vertex.co.copy() for vertex in lug.data.vertices]
    magazine_rest_center = sum(
        (weapon_stage.weapon_local_position(root, obj) for obj in magazines),
        Vector((0.0, 0.0, 0.0)),
    ) / len(magazines)
    moving_center_root = incoming_center_root + Vector(
        (RELOAD_MAGAZINE_OUTWARD_M, 0.0, 0.0)
    )
    translation = moving_center_root - magazine_rest_center
    contact_root = Vector(
        (
            max(point.x for point in points) + translation.x - RELOAD_CONTACT_INSET_M,
            (min(point.y for point in points) + max(point.y for point in points)) * 0.5
            + translation.y,
            (min(point.z for point in points) + max(point.z for point in points)) * 0.5
            + translation.z,
        )
    )
    pad = Vector(HAND_CONTACT_PAD_CENTER_LOCAL["L"]).normalized()
    contact_axis = Vector((-1.0, 0.0, 0.0))
    alignment = pad.rotation_difference(contact_axis).to_matrix()
    twist = Matrix.Rotation(
        math.radians(RELOAD_DETACHED_TWIST_DEG), 3, contact_axis
    )
    desired_root_rotation = twist @ alignment
    desired_world_rotation = _root_rotation_world(root) @ desired_root_rotation
    contact_world = root.matrix_world @ contact_root
    return _solve_hand_contact_frame(
        armature,
        base_pose,
        "L",
        contact_world,
        desired_world_rotation,
        original_solver,
    )


def _bolt_distal_contact_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    root: bpy.types.Object,
    moving_center_root: Vector,
    bolt_center_rest: Vector,
    original_solver,
) -> dict[str, Matrix]:
    """Present the right distal pad to the tagged knob's moving outboard face."""

    knobs = [
        obj
        for obj in weapon_components(root, COMPONENT_BOLT)
        if obj.name == BOLT_KNOB_OBJECT_NAME
    ]
    if len(knobs) != 1:
        raise RuntimeError(
            f"Candidate007 requires one {BOLT_KNOB_OBJECT_NAME}, found {len(knobs)}"
        )
    knob = knobs[0]
    if not bool(knob.get("ps_weapon_mesh_transform_baked_v5", False)):
        raise RuntimeError("Candidate007 bolt knob is not in baked weapon-root space")
    if knob.type != "MESH" or not knob.data.vertices:
        raise RuntimeError("Candidate007 bolt knob has no mesh vertices")
    points = [vertex.co.copy() for vertex in knob.data.vertices]
    translation = moving_center_root - bolt_center_rest
    contact_root = Vector(
        (
            min(point.x for point in points) + translation.x + BOLT_CONTACT_INSET_M,
            (min(point.y for point in points) + max(point.y for point in points)) * 0.5
            + translation.y,
            (min(point.z for point in points) + max(point.z for point in points)) * 0.5
            + translation.z,
        )
    )
    # Columns: Hand +X -> rifle rearward (-Y), Hand +Y -> rifle right (+X),
    # Hand +Z -> rifle up (+Z).  This points the fingertip cap at the knob while
    # keeping the glove upright instead of rolling its long axis in place.
    desired_root_rotation = Matrix(
        (
            (0.0, 1.0, 0.0),
            (-1.0, 0.0, 0.0),
            (0.0, 0.0, 1.0),
        )
    )
    desired_world_rotation = _root_rotation_world(root) @ desired_root_rotation
    contact_world = root.matrix_world @ contact_root
    return _solve_hand_contact_frame(
        armature,
        base_pose,
        "R",
        contact_world,
        desired_world_rotation,
        original_solver,
    )


def _candidate007_single_arm_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    side: str,
    target_world: Vector,
) -> dict[str, Matrix]:
    """Keep reload/bolt reaches on the outside of the wide receiver.

    The shared solver targets component centres.  Candidate007's receiver cage
    is substantially wider, so the same targets pull the forearm through the
    armour before the hand reaches the magazine or lateral bolt handle.  Only
    the known magazine (left hand) and bolt-handle (right hand, close to the
    tagged bolt centre) calls are offset; draw reaches retain their authored
    target and are handled by the explicit extraction path below.  The bolt
    adapter recognizes only the exact root-local corridor emitted by the
    shared bolt-cycle calls; unrelated right-hand reaches fail closed.
    """
    root = weapon_stage._candidate007_root
    original = weapon_stage._candidate007_original_single_arm_pose
    # The wrapper needs the root transform belonging to this exact base pose;
    # the previous solver call may have left Blender on another action sample.
    weapon_stage._apply_basis_snapshot(armature, base_pose)
    bpy.context.view_layer.update()
    adjusted = target_world.copy()
    manipulation_roll: float | None = None
    rifle_right = (
        root.matrix_world.to_3x3() @ Vector((1.0, 0.0, 0.0))
    ).normalized()
    root_inverse = root.matrix_world.inverted_safe()
    if side == "L":
        magazines = weapon_components(root, COMPONENT_MAGAZINE)
        if not magazines:
            raise RuntimeError("Candidate007 reload has no tagged magazine components")
        magazine_center = sum(
            (weapon_stage.weapon_local_position(root, obj) for obj in magazines),
            Vector((0.0, 0.0, 0.0)),
        ) / len(magazines)
        incoming_center_root = (
            root_inverse @ target_world
            - Vector((SHARED_RELOAD_TARGET_OUTWARD_M, 0.0, 0.0))
        )
        if (incoming_center_root - magazine_center).length > 1.0e-6:
            return _reload_detached_contact_pose(
                armature, base_pose, root, incoming_center_root, original
            )
        # Seated reload contact retains the measured V2 pose; only detached
        # manipulation frames use the fingertip-pad frame above.
        adjusted += rifle_right * RELOAD_HAND_OUTWARD_M
        manipulation_roll = RELOAD_PALM_ROLL_DEG
    elif side == "R":
        bolts = weapon_components(root, COMPONENT_BOLT)
        bolt_center = sum(
            (weapon_stage.weapon_local_position(root, obj) for obj in bolts),
            Vector((0.0, 0.0, 0.0)),
        ) / len(bolts)
        local_target = root_inverse @ target_world
        bolt_target_offset = local_target - bolt_center
        if _is_candidate007_bolt_target_offset_root_local(
            bolt_target_offset.x,
            bolt_target_offset.y,
            bolt_target_offset.z,
        ):
            moving_center_root = local_target + Vector(
                (SHARED_BOLT_TARGET_OUTWARD_M, 0.0, 0.0)
            )
            return _bolt_distal_contact_pose(
                armature,
                base_pose,
                root,
                moving_center_root,
                bolt_center,
                original,
            )

    solved = original(armature, base_pose, side, adjusted)
    if manipulation_roll is None:
        return solved
    return _orient_solved_hand_about_wrist(
        armature,
        solved,
        side,
        manipulation_roll,
    )


def _is_candidate007_bolt_target_offset_root_local(
    offset_x_m: float,
    offset_y_m: float,
    offset_z_m: float,
) -> bool:
    """Recognize only the root-local target corridor emitted by shared bolt calls."""
    tolerance = BOLT_TARGET_CORRIDOR_AXIS_TOLERANCE_M
    return (
        abs(offset_x_m + SHARED_BOLT_TARGET_OUTWARD_M) <= tolerance
        and BOLT_TARGET_TRAVEL_Y_RANGE_M[0] - tolerance
        <= offset_y_m
        <= BOLT_TARGET_TRAVEL_Y_RANGE_M[1] + tolerance
        and abs(offset_z_m) <= tolerance
    )


def _bolt_target_corridor_root_local_evidence() -> dict[str, object]:
    """Describe the fail-closed classifier in the same coordinates it evaluates."""
    return {
        "relative_to": "tagged_bolt_center",
        "x_offset_m": -SHARED_BOLT_TARGET_OUTWARD_M,
        "y_min_m": BOLT_TARGET_TRAVEL_Y_RANGE_M[0],
        "y_max_m": BOLT_TARGET_TRAVEL_Y_RANGE_M[1],
        "z_offset_m": 0.0,
        "axis_tolerance_m": BOLT_TARGET_CORRIDOR_AXIS_TOLERANCE_M,
    }


def _candidate007_pose_component_delta(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    base_pose: dict[str, Matrix],
    control_bone: str,
    delta_in_root_space: Matrix,
) -> dict[str, Matrix]:
    """Move detached magazine phases outward while preserving seated endpoints."""
    adjusted = delta_in_root_space
    if (
        control_bone == weapon_stage.MAGAZINE_BONE
        and _maximum_matrix_delta(delta_in_root_space, Matrix.Identity(4)) > 1.0e-8
    ):
        adjusted = (
            Matrix.Translation(Vector((RELOAD_MAGAZINE_OUTWARD_M, 0.0, 0.0)))
            @ delta_in_root_space
        )
    return weapon_stage._candidate007_original_pose_component_delta(
        armature,
        root,
        base_pose,
        control_bone,
        adjusted,
    )


def _half_frame_samples(start: float, end: float) -> list[float]:
    """Return an inclusive sequence at the manipulation certification cadence."""

    first_tick = int(round(start / MANIPULATION_SAMPLE_STEP_FRAMES))
    last_tick = int(round(end / MANIPULATION_SAMPLE_STEP_FRAMES))
    if (
        first_tick > last_tick
        or abs(first_tick * MANIPULATION_SAMPLE_STEP_FRAMES - start) > 1.0e-8
        or abs(last_tick * MANIPULATION_SAMPLE_STEP_FRAMES - end) > 1.0e-8
    ):
        raise ValueError(f"Invalid half-frame range: {start!r}..{end!r}")
    return [tick * MANIPULATION_SAMPLE_STEP_FRAMES for tick in range(first_tick, last_tick + 1)]


def _frame_samples(start: float, end: float, step: float) -> list[float]:
    """Return a deterministic inclusive sub-frame sequence."""

    if not math.isfinite(step) or step <= 0.0:
        raise ValueError(f"Invalid frame step: {step!r}")
    first_tick = int(round(start / step))
    last_tick = int(round(end / step))
    if (
        first_tick > last_tick
        or abs(first_tick * step - start) > 1.0e-8
        or abs(last_tick * step - end) > 1.0e-8
    ):
        raise ValueError(f"Invalid frame range {start!r}..{end!r} at step {step!r}")
    return [tick * step for tick in range(first_tick, last_tick + 1)]


def _pose_hand_wrist_frame(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    side: str,
    wrist_world: Vector,
    rotation_world: Matrix,
    original_solver,
) -> dict[str, Matrix]:
    """Solve one arm to an explicit wrist frame without moving the weapon."""

    target = wrist_world.copy()
    solved = original_solver(armature, base_pose, side, target)
    weapon_stage._apply_basis_snapshot(armature, solved)
    bpy.context.view_layer.update()
    initial = matrix_world_for_pose_bone(
        armature, armature.pose.bones[f"Hand.{side}"]
    ).translation
    residual = target - initial
    if residual.length > HAND_CONTACT_SOLVE_TOLERANCE_M:
        solved = original_solver(armature, base_pose, side, target + residual)
        weapon_stage._apply_basis_snapshot(armature, solved)
        bpy.context.view_layer.update()
    hand = armature.pose.bones[f"Hand.{side}"]
    solved_wrist = matrix_world_for_pose_bone(armature, hand).translation.copy()
    hand.matrix = armature.matrix_world.inverted_safe() @ (
        Matrix.Translation(solved_wrist) @ rotation_world.to_4x4()
    )
    bpy.context.view_layer.update()
    final_wrist = matrix_world_for_pose_bone(armature, hand).translation
    if (final_wrist - wrist_world).length > HAND_CONTACT_SOLVE_TOLERANCE_M:
        raise RuntimeError(
            f"Candidate007 {side} wrist-frame solve missed its target by "
            f"{(final_wrist - wrist_world).length:.9f} m"
        )
    return weapon_stage._basis_snapshot(armature)


def _evaluated_extreme_contact(
    root: bpy.types.Object,
    obj: bpy.types.Object,
    *,
    axis_index: int,
    maximum: bool,
    inset_m: float,
) -> tuple[Vector, Vector]:
    """Return an evaluated contact point and outward normal on a rigid part.

    Candidate007 source meshes have their authored rifle-root transforms baked
    into vertex coordinates before the animation stage.  Once an articulated
    object is bone-parented, its evaluated ``matrix_world`` carries the exact
    WeaponMagazine/WeaponBolt delta.  Sampling the designated extreme face
    through that matrix therefore preserves both translation *and rotation*;
    reconstructing the point from an averaged component centre does not.
    """

    if obj.type != "MESH" or not obj.data.vertices:
        raise RuntimeError(f"Candidate007 contact object {obj.name!r} has no mesh vertices")
    if not bool(obj.get("ps_weapon_mesh_transform_baked_v5", False)):
        raise RuntimeError(f"Candidate007 contact object {obj.name!r} is not in baked root space")
    if axis_index not in {0, 1, 2}:
        raise ValueError(f"Unsupported contact axis index: {axis_index}")
    if not math.isfinite(inset_m) or inset_m < 0.0:
        raise ValueError(f"Invalid contact inset: {inset_m!r}")

    local_outward = Vector((0.0, 0.0, 0.0))
    local_outward[axis_index] = 1.0 if maximum else -1.0
    # Use a real polygon, not an axis-aligned bounds point. The tapered bolt
    # knob exposes only an edge at its minimum X, so an exact-extreme vertex
    # test can manufacture a point that is not on any graspable surface.
    polygons = [
        polygon for polygon in obj.data.polygons
        if float(polygon.normal.dot(local_outward)) > 1.0e-6
    ]
    if not polygons:
        raise RuntimeError(f"Candidate007 contact object {obj.name!r} has no outward face")
    polygon = max(
        polygons,
        key=lambda item: (
            round(float(item.normal.dot(local_outward)), 9),
            round(float(item.area), 9),
            -int(item.index),
        ),
    )
    face_center_local = polygon.center.copy()
    local_outward = polygon.normal.normalized()
    object_world = obj.matrix_world.copy()
    normal_matrix = object_world.to_3x3().inverted_safe().transposed()
    outward_world = (normal_matrix @ local_outward).normalized()
    contact_world = object_world @ face_center_local - outward_world * inset_m
    if not all(math.isfinite(float(value)) for value in (*contact_world, *outward_world)):
        raise RuntimeError(f"Candidate007 contact frame on {obj.name!r} is non-finite")
    # Fail closed if an unexpected parenting/bind error places the contact far
    # outside the weapon's local review envelope.
    contact_root = root.matrix_world.inverted_safe() @ contact_world
    if contact_root.length > 2.0:
        raise RuntimeError(
            f"Candidate007 contact frame on {obj.name!r} left the weapon envelope"
        )
    return contact_world, outward_world


def _reload_evaluated_contact_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    root: bpy.types.Object,
    original_solver,
    *,
    clearance_m: float = 0.0,
) -> dict[str, Matrix]:
    """Co-solve the left distal pad against the evaluated moving pull lug."""

    weapon_stage._apply_basis_snapshot(armature, base_pose)
    bpy.context.view_layer.update()
    lugs = [
        obj for obj in weapon_components(root, COMPONENT_MAGAZINE)
        if obj.name == RELOAD_PULL_LUG_OBJECT_NAME
    ]
    if len(lugs) != 1:
        raise RuntimeError(
            f"Candidate007 requires one {RELOAD_PULL_LUG_OBJECT_NAME}, found {len(lugs)}"
        )
    lug = lugs[0]
    object_world = lug.matrix_world.copy()
    maximum_x = max(float(vertex.co.x) for vertex in lug.data.vertices)
    cap_points = [
        vertex.co.copy()
        for vertex in lug.data.vertices
        if abs(float(vertex.co.x) - maximum_x) <= 1.0e-6
    ]
    if len(cap_points) < 3:
        raise RuntimeError("Candidate007 magazine pull lug has no stable +X cap")
    cap_center_local = sum(cap_points, Vector((0.0, 0.0, 0.0))) / len(cap_points)
    normal_matrix = object_world.to_3x3().inverted_safe().transposed()
    outward_world = (normal_matrix @ Vector((1.0, 0.0, 0.0))).normalized()
    contact_world = object_world @ cap_center_local - outward_world * RELOAD_CONTACT_INSET_M
    if clearance_m:
        contact_world += outward_world * (
            RELOAD_CONTACT_INSET_M + float(clearance_m)
        )
    contact_axis = -outward_world
    pad = Vector(HAND_CONTACT_PAD_CENTER_LOCAL["L"]).normalized()
    alignment = pad.rotation_difference(contact_axis).to_matrix()
    twist = Matrix.Rotation(
        math.radians(RELOAD_DETACHED_TWIST_DEG), 3, contact_axis
    )
    desired_world_rotation = twist @ alignment
    return _solve_hand_contact_frame(
        armature,
        base_pose,
        "L",
        contact_world,
        desired_world_rotation,
        original_solver,
    )


def _bolt_evaluated_contact_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    root: bpy.types.Object,
    original_solver,
    *,
    clearance_m: float = 0.0,
) -> dict[str, Matrix]:
    """Co-solve the right distal pad against the evaluated moving bolt knob."""

    weapon_stage._apply_basis_snapshot(armature, base_pose)
    bpy.context.view_layer.update()
    knobs = [
        obj for obj in weapon_components(root, COMPONENT_BOLT)
        if obj.name == BOLT_KNOB_OBJECT_NAME
    ]
    if len(knobs) != 1:
        raise RuntimeError(
            f"Candidate007 requires one {BOLT_KNOB_OBJECT_NAME}, found {len(knobs)}"
        )
    contact_world, outward_world = _evaluated_extreme_contact(
        root,
        knobs[0],
        axis_index=0,
        maximum=False,
        inset_m=BOLT_CONTACT_INSET_M,
    )
    if clearance_m:
        contact_world += outward_world * (
            BOLT_CONTACT_INSET_M + float(clearance_m)
        )
    desired_world_rotation = _root_rotation_world(root) @ Matrix(
        (
            (0.0, 1.0, 0.0),
            (-1.0, 0.0, 0.0),
            (0.0, 0.0, 1.0),
        )
    )
    return _solve_hand_contact_frame(
        armature,
        base_pose,
        "R",
        contact_world,
        desired_world_rotation,
        original_solver,
    )


def _evaluated_manipulation_contact_frame(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    root: bpy.types.Object,
    side: str,
) -> tuple[Vector, Vector, Matrix]:
    """Return contact, outward normal and authored hand rotation for one part."""

    weapon_stage._apply_basis_snapshot(armature, base_pose)
    bpy.context.view_layer.update()
    if side == "L":
        lugs = [
            obj for obj in weapon_components(root, COMPONENT_MAGAZINE)
            if obj.name == RELOAD_PULL_LUG_OBJECT_NAME
        ]
        if len(lugs) != 1:
            raise RuntimeError("Candidate007 reload contact object is incomplete")
        lug = lugs[0]
        maximum_x = max(float(vertex.co.x) for vertex in lug.data.vertices)
        cap_points = [
            vertex.co.copy() for vertex in lug.data.vertices
            if abs(float(vertex.co.x) - maximum_x) <= 1.0e-6
        ]
        if len(cap_points) < 3:
            raise RuntimeError("Candidate007 reload contact cap is incomplete")
        center_local = sum(cap_points, Vector((0.0, 0.0, 0.0))) / len(cap_points)
        object_world = lug.matrix_world.copy()
        outward = (
            object_world.to_3x3().inverted_safe().transposed()
            @ Vector((1.0, 0.0, 0.0))
        ).normalized()
        contact = object_world @ center_local - outward * RELOAD_CONTACT_INSET_M
        pad = Vector(HAND_CONTACT_PAD_CENTER_LOCAL[side]).normalized()
        axis = -outward
        rotation = (
            Matrix.Rotation(math.radians(RELOAD_DETACHED_TWIST_DEG), 3, axis)
            @ pad.rotation_difference(axis).to_matrix()
        )
        return contact, outward, rotation
    knobs = [
        obj for obj in weapon_components(root, COMPONENT_BOLT)
        if obj.name == BOLT_KNOB_OBJECT_NAME
    ]
    if len(knobs) != 1:
        raise RuntimeError("Candidate007 bolt contact object is incomplete")
    contact, outward = _evaluated_extreme_contact(
        root,
        knobs[0],
        axis_index=0,
        maximum=False,
        inset_m=BOLT_CONTACT_INSET_M,
    )
    rotation = _root_rotation_world(root) @ Matrix(
        ((0.0, 1.0, 0.0), (-1.0, 0.0, 0.0), (0.0, 0.0, 1.0))
    )
    return contact, outward, rotation


def _solve_manipulation_clearance_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    root: bpy.types.Object,
    side: str,
    clearance_m: float,
    original_solver,
) -> dict[str, Matrix]:
    """Place the distal pad outside the moving part by a measured clearance."""

    contact, outward, rotation = _evaluated_manipulation_contact_frame(
        armature, base_pose, root, side
    )
    inset = RELOAD_CONTACT_INSET_M if side == "L" else BOLT_CONTACT_INSET_M
    target = contact + outward * (inset + float(clearance_m))
    return _solve_hand_contact_frame(
        armature, base_pose, side, target, rotation, original_solver
    )


def _solve_grip_release_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    root: bpy.types.Object,
    side: str,
    outward_m: float,
    up_m: float,
    original_solver,
) -> dict[str, Matrix]:
    """Detach a gripping hand normal to the rifle before reorienting it."""

    weapon_stage._apply_basis_snapshot(armature, base_pose)
    bpy.context.view_layer.update()
    hand = armature.pose.bones[f"Hand.{side}"]
    hand_world = matrix_world_for_pose_bone(armature, hand)
    root_rotation = _root_rotation_world(root)
    local_sign = 1.0 if side == "L" else -1.0
    wrist = (
        hand_world.translation
        + (root_rotation @ Vector((local_sign * outward_m, 0.0, up_m)))
    )
    return _pose_hand_wrist_frame(
        armature,
        base_pose,
        side,
        wrist,
        hand_world.to_3x3(),
        original_solver,
    )


def _offset_hand_in_root_space(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    root: bpy.types.Object,
    side: str,
    root_local_delta_m: tuple[float, float, float],
    original_solver,
) -> dict[str, Matrix]:
    """Translate one solved wrist by a measured RifleRoot-local delta."""

    weapon_stage._apply_basis_snapshot(armature, base_pose)
    bpy.context.view_layer.update()
    hand = armature.pose.bones[f"Hand.{side}"]
    hand_world = matrix_world_for_pose_bone(armature, hand)
    root_rotation = _root_rotation_world(root)
    delta_world = root_rotation @ Vector(root_local_delta_m)
    return _pose_hand_wrist_frame(
        armature,
        base_pose,
        side,
        hand_world.translation + delta_world,
        hand_world.to_3x3(),
        original_solver,
    )


def _densify_manipulation_poses(
    armature: bpy.types.Object,
    name: str,
    poses: dict[float, dict[str, Matrix]],
) -> tuple[dict[float, dict[str, Matrix]], dict[str, object]]:
    """Bake continuous quarter-frame manipulation and outboard approach paths."""

    specs = {
        "PS_Reload": (RELOAD_CONTACT_WINDOW, "L"),
        "PS_BoltCycle": (BOLT_CONTACT_WINDOW, "R"),
    }
    if name not in specs:
        return poses, {}
    (start, end), side = specs[name]
    authored_frames = sorted(float(frame) for frame in poses)
    if authored_frames[0] != 1.0 or authored_frames[-1] != EXPECTED_ACTION_RANGES[name][1]:
        raise RuntimeError(f"{name} authored range changed before densification")
    original = {float(frame): weapon_stage._copy_pose(pose) for frame, pose in poses.items()}
    dense = dict(original)

    def interpolated(frame: float) -> dict[str, Matrix]:
        if frame in original:
            return weapon_stage._copy_pose(original[frame])
        lower = max(value for value in authored_frames if value < frame)
        upper = min(value for value in authored_frames if value > frame)
        factor = (frame - lower) / (upper - lower)
        return weapon_stage._blend_pose(original[lower], original[upper], factor)

    root = weapon_stage._candidate007_root
    original_solver = weapon_stage._candidate007_original_single_arm_pose

    # Exact Ready endpoints release directly away from the immutable grip before
    # the hand is reoriented into the manipulation contact frame. This prevents
    # the long ready->hover interpolation from sweeping through the receiver.
    if side == "L":
        ready_start = original[1.0]
        ready_end = original[authored_frames[-1]]
        dense[14.0] = weapon_stage._copy_pose(ready_start)
        release_start = _solve_grip_release_pose(
            armature, ready_start, root, side,
            MANIPULATION_GRIP_RELEASE_M,
            MANIPULATION_GRIP_RELEASE_UP_M,
            original_solver,
        )
        release_end = _solve_grip_release_pose(
            armature, ready_end, root, side,
            MANIPULATION_GRIP_RELEASE_M,
            MANIPULATION_GRIP_RELEASE_UP_M,
            original_solver,
        )
        dense[16.0] = release_start
        dense[18.75] = _solve_grip_release_pose(
            armature,
            weapon_stage._blend_pose(
                release_start,
                _solve_manipulation_clearance_pose(
                    armature, original[25.0], root, side,
                    MANIPULATION_TRANSIT_CLEARANCE_M,
                    original_solver,
                ),
                (18.75 - 16.0) / (20.0 - 16.0),
            ),
            root,
            side,
            RELOAD_YOKE_CLEARANCE_M,
            0.0,
            original_solver,
        )
        dense[20.0] = _solve_manipulation_clearance_pose(
            armature, original[25.0], root, side,
            MANIPULATION_TRANSIT_CLEARANCE_M,
            original_solver,
        )
        dense[24.0] = _solve_manipulation_clearance_pose(
            armature, original[25.0], root, side,
            MANIPULATION_HOVER_CLEARANCE_M,
            original_solver,
        )
        dense[79.0] = _solve_manipulation_clearance_pose(
            armature, original[75.0], root, side,
            MANIPULATION_TRANSIT_CLEARANCE_M,
            original_solver,
        )
        dense[82.0] = release_end
    else:
        ready_start = original[1.0]
        ready_end = original[authored_frames[-1]]
        for frame in _frame_samples(1.25, 2.25, 0.25):
            factor = (frame - 1.0) / (2.5 - 1.0)
            dense[frame] = _solve_grip_release_pose(
                armature, ready_start, root, side,
                BOLT_GRIP_RELEASE_M * factor,
                MANIPULATION_GRIP_RELEASE_UP_M * factor,
                original_solver,
            )
        dense[2.5] = _solve_manipulation_clearance_pose(
            armature, original[4.0], root, side,
            BOLT_APPROACH_TRANSIT_CLEARANCE_M,
            original_solver,
        )
        dense[3.0] = _solve_manipulation_clearance_pose(
            armature, original[4.0], root, side,
            MANIPULATION_HOVER_CLEARANCE_M,
            original_solver,
        )
        dense[17.0] = _solve_manipulation_clearance_pose(
            armature, original[16.0], root, side,
            MANIPULATION_HOVER_CLEARANCE_M,
            original_solver,
        )
        dense[17.5] = _solve_manipulation_clearance_pose(
            armature, original[16.0], root, side,
            BOLT_APPROACH_TRANSIT_CLEARANCE_M,
            original_solver,
        )
        for frame in _frame_samples(17.75, 19.75, 0.25):
            factor = (20.0 - frame) / (20.0 - 17.5)
            dense[frame] = _solve_grip_release_pose(
                armature, ready_end, root, side,
                BOLT_GRIP_RELEASE_M * factor,
                MANIPULATION_GRIP_RELEASE_UP_M * factor,
                original_solver,
            )

        # Replace the remaining release/return crossings with the measured
        # visible-geometry path.  The transit substitutions reuse exact
        # adjacent poses that already passed the dense audit; the root-local
        # wrist deltas retain smooth endpoint tapers and are applied only
        # outside the bolt contact window.
        for target_frame, source_frame in BOLT_MEASURED_POSE_SUBSTITUTIONS.items():
            dense[target_frame] = weapon_stage._copy_pose(dense[source_frame])
        for frame, delta in BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M.items():
            dense[frame] = _offset_hand_in_root_space(
                armature,
                dense[frame],
                root,
                side,
                delta,
                original_solver,
            )

    # Co-solve the moving part and hand at the same quarter-frame. Boundary
    # samples retain explicit face-normal clearance and enter contact only after
    # the policy window opens.
    for frame in _half_frame_samples(start, end):
        component_pose = interpolated(frame)
        if side == "L" and (frame < 26.0 or frame > 74.0):
            if frame < 26.0:
                factor = max(0.0, min(1.0, (frame - 25.0) / 1.0))
            else:
                factor = max(0.0, min(1.0, (75.0 - frame) / 1.0))
            clearance = MANIPULATION_HOVER_CLEARANCE_M * (1.0 - factor)
            hand_pose = _solve_manipulation_clearance_pose(
                armature, component_pose, root, side, clearance, original_solver
            )
        elif side == "R" and (
            6.0 <= frame <= 6.75 or 13.25 <= frame <= 14.0
        ):
            hand_pose = _solve_manipulation_clearance_pose(
                armature,
                component_pose,
                root,
                side,
                BOLT_TRANSIT_CONTACT_CLEARANCE_M,
                original_solver,
            )
        elif side == "L":
            hand_pose = _reload_evaluated_contact_pose(
                armature, component_pose, root, original_solver
            )
        else:
            hand_pose = _bolt_evaluated_contact_pose(
                armature, component_pose, root, original_solver
            )
        dense[frame] = hand_pose
    # V11 certifies manipulation at eighth-frame cadence.  Apply its explicit
    # clearance solves only after the quarter-frame contact loop so no generic
    # co-solve can overwrite the measured waypoints.
    if side == "R":
        for frame, clearance_m in BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M.items():
            dense[frame] = _solve_manipulation_clearance_pose(
                armature,
                interpolated(frame),
                root,
                side,
                clearance_m,
                original_solver,
            )
    else:
        return_start, return_end = RELOAD_MEASURED_RETURN_BLEND_ENDPOINT_FRAMES
        measured_return_frames = sorted({
            *RELOAD_MEASURED_RETURN_ANCHOR_FRAMES,
            *RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M,
        })
        for frame in measured_return_frames:
            factor = (frame - return_start) / (return_end - return_start)
            dense[frame] = weapon_stage._blend_pose(
                dense[return_start], dense[return_end], factor
            )
        for frame, delta in RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M.items():
            dense[frame] = _offset_hand_in_root_space(
                armature,
                dense[frame],
                root,
                side,
                delta,
                original_solver,
            )
    dense = dict(sorted(dense.items()))
    evidence = {
        "sample_step_frames": MANIPULATION_SAMPLE_STEP_FRAMES,
        "contact_window": [start, end],
        "approach_frames": list(
            RELOAD_APPROACH_FRAMES if side == "L" else BOLT_APPROACH_FRAMES
        ),
        "return_frames": list(
            RELOAD_RETURN_FRAMES if side == "L" else BOLT_RETURN_FRAMES
        ),
        "hover_mode": MANIPULATION_HOVER_MODE,
        "hover_clearance_m": MANIPULATION_HOVER_CLEARANCE_M,
        "transit_clearance_m": MANIPULATION_TRANSIT_CLEARANCE_M,
        "grip_release_m": MANIPULATION_GRIP_RELEASE_M,
        "authored_frames": authored_frames,
        "result_frames": list(dense),
        "co_solved_sample_count": sum(
            1 for frame in dense if start <= frame <= end
        ),
    }
    if side == "R":
        evidence.update({
            "grip_release_m": BOLT_GRIP_RELEASE_M,
            "approach_transit_clearance_m": BOLT_APPROACH_TRANSIT_CLEARANCE_M,
            "transit_contact_clearance_m": BOLT_TRANSIT_CONTACT_CLEARANCE_M,
            "measured_release_path_version": BOLT_MEASURED_RELEASE_PATH_VERSION,
            "measured_pose_substitutions": {
                str(frame): source
                for frame, source in sorted(BOLT_MEASURED_POSE_SUBSTITUTIONS.items())
            },
            "measured_release_deltas_root_local_m": {
                str(frame): list(delta)
                for frame, delta in sorted(
                    BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M.items()
                )
            },
            "measured_eighth_frame_clearances_m": {
                str(frame): clearance_m
                for frame, clearance_m in sorted(
                    BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M.items()
                )
            },
        })
    else:
        evidence["yoke_clearance_m"] = RELOAD_YOKE_CLEARANCE_M
        evidence["measured_return_path_version"] = (
            RELOAD_MEASURED_RETURN_PATH_VERSION
        )
        evidence["measured_return_blend_endpoint_frames"] = list(
            RELOAD_MEASURED_RETURN_BLEND_ENDPOINT_FRAMES
        )
        evidence["measured_return_anchor_frames"] = list(
            RELOAD_MEASURED_RETURN_ANCHOR_FRAMES
        )
        evidence["measured_return_deltas_root_local_m"] = {
            str(frame): list(delta)
            for frame, delta in sorted(
                RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M.items()
            )
        }
    return dense, evidence


def _set_action_interpolation(
    armature: bpy.types.Object,
    action_name: str,
    interpolation: str,
    bone_names: set[str],
) -> dict[str, int]:
    action = bpy.data.actions[action_name]
    bag = get_action_channelbag(action, find_action_slot(action, armature))
    curves = list(bag.fcurves)
    if not curves:
        raise RuntimeError(f"{action_name} contains no fcurves")
    affected_curves = 0
    affected_points = 0
    for curve in curves:
        if not any(f'pose.bones["{bone_name}"]' in curve.data_path for bone_name in bone_names):
            continue
        affected_curves += 1
        for point in curve.keyframe_points:
            point.interpolation = interpolation
            affected_points += 1
        curve.update()
    if affected_curves == 0 or affected_points == 0:
        raise RuntimeError(f"{action_name} has no selected manipulation curves")
    return {
        "total_curve_count": len(curves),
        "affected_curve_count": affected_curves,
        "affected_key_count": affected_points,
    }


def _build_fractional_action(
    armature: bpy.types.Object,
    name: str,
    poses: dict[float, dict[str, Matrix]],
    quaternion_bones: set[str] | None = None,
) -> None:
    """Build an action whose authored samples include exact subframes."""

    frames = sorted(float(frame) for frame in poses)
    expected_end = float(EXPECTED_ACTION_RANGES[name][1])
    if not frames or frames[0] != 1.0 or frames[-1] != expected_end:
        raise RuntimeError(
            f"{name} fractional action range is {frames[:1]}..{frames[-1:]}, "
            f"expected 1..{expected_end:g}"
        )
    action, slot = create_action_with_slot(
        armature, name, int(frames[0]), int(frames[-1])
    )
    action["ps_animation_contract_version"] = weapon_stage.ANIMATION_CONTRACT_VERSION
    action["ps_looping"] = name in weapon_stage.LOOP_ACTIONS
    quaternion_bones = set(quaternion_bones or ())
    previous_quaternions: dict[str, object] = {}
    original_modes = {bone.name: bone.rotation_mode for bone in armature.pose.bones}
    for bone_name in quaternion_bones:
        armature.pose.bones[bone_name].rotation_mode = "QUATERNION"
    for frame in frames:
        integer_frame = math.floor(frame)
        bpy.context.scene.frame_set(integer_frame, subframe=frame - integer_frame)
        weapon_stage._apply_basis_snapshot(armature, poses[frame])
        channelbag = weapon_stage.ensure_action_channelbag(action, slot)
        for bone in armature.pose.bones:
            if bone.rotation_mode == "QUATERNION":
                rotation_property = "rotation_quaternion"
                rotation = bone.rotation_quaternion.copy()
                previous = previous_quaternions.get(bone.name)
                if previous is not None and previous.dot(rotation) < 0.0:
                    rotation.negate()
                previous_quaternions[bone.name] = rotation.copy()
                rotation_values = tuple(rotation)
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
                    point = weapon_stage._ensure_curve(
                        channelbag, path, index, bone.name
                    ).keyframe_points.insert(
                        float(frame), float(value), options={"FAST"}
                    )
                    point.interpolation = "BEZIER"
                    point.handle_left_type = "AUTO_CLAMPED"
                    point.handle_right_type = "AUTO_CLAMPED"
    for curve in weapon_stage.get_action_channelbag(action, slot).fcurves:
        curve.update()
    for bone_name, mode in original_modes.items():
        armature.pose.bones[bone_name].rotation_mode = mode


def _candidate007_transition_poses(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    source: dict[int, dict[str, Matrix]],
    original_single_arm_solver,
) -> dict[float, dict[str, Matrix]]:
    """Guide the rifle around the suit, acquire it, then dock without slip.

    The immutable V9 powered-guide corridor is retained exactly through frame
    26.  At frame 27 the firing hand uses the measured 12 mm early-acquisition
    offset from the future frame-28 catch.  From frame 28 through 29.875 every
    eighth-frame sample uses the exact Ready root-to-Hand.R transform while the
    cached V9 WeaponRoot path is restored.  Frame 30 remains the exact Ready
    endpoint.  The raw shared one-arm solver is passed explicitly so the bolt
    target classifier wrapper can never reinterpret these transition reaches.
    """
    apply_pose = weapon_stage._apply_basis_snapshot
    apply_pose(armature, source[1])
    bpy.context.view_layer.update()
    stowed_root = root.matrix_world.copy()
    carrier_world = matrix_world_for_pose_bone(
        armature, armature.pose.bones[weapon_stage.WEAPON_ROOT_BONE]
    )
    carrier_to_root = carrier_world.inverted_safe() @ stowed_root
    right, forward, up = body_basis(armature)

    apply_pose(armature, source[30])
    bpy.context.view_layer.update()
    ready_root = root.matrix_world.copy()
    ready_hand_world = matrix_world_for_pose_bone(
        armature, armature.pose.bones["Hand.R"]
    )
    ready_root_to_hand = ready_root.inverted_safe() @ ready_hand_world

    def placed(
        body_pose: dict[str, Matrix],
        orientation: Matrix,
        translation: Vector,
    ) -> dict[str, Matrix]:
        target = orientation.copy()
        target.translation = translation
        return weapon_stage._pose_weapon_at_world(
            armature,
            weapon_stage._copy_pose(body_pose),
            target,
            carrier_to_root,
        )

    far_back = (
        stowed_root.translation
        - right * TRANSITION_STOWED_OUTBOARD_M
        - forward * TRANSITION_STOWED_BACK_M
        + up * TRANSITION_STOWED_UP_M
    )
    far_front = (
        ready_root.translation
        - right * TRANSITION_FORWARD_OUTBOARD_M
        + forward * TRANSITION_FORWARD_AHEAD_M
        + up * TRANSITION_FORWARD_UP_M
    )
    predock = (
        ready_root.translation
        - right * TRANSITION_PREDOCK_OUTBOARD_M
        + forward * TRANSITION_PREDOCK_AHEAD_M
        + up * TRANSITION_PREDOCK_UP_M
    )
    dock_mid = (
        ready_root.translation
        - right * TRANSITION_DOCK_MID_OUTBOARD_M
        + forward * TRANSITION_DOCK_MID_AHEAD_M
        + up * TRANSITION_DOCK_MID_UP_M
    )
    dock_near = (
        ready_root.translation
        - right * TRANSITION_DOCK_NEAR_OUTBOARD_M
        + forward * TRANSITION_DOCK_NEAR_AHEAD_M
        + up * TRANSITION_DOCK_NEAR_UP_M
    )
    body_25 = weapon_stage._blend_pose(source[1], source[30], 0.25)
    body_50 = weapon_stage._blend_pose(source[1], source[30], 0.50)
    body_75 = weapon_stage._blend_pose(source[1], source[30], 0.75)
    guided = {
        1.0: weapon_stage._copy_pose(source[1]),
        6.0: placed(source[1], stowed_root, far_back),
        10.0: placed(source[1], ready_root, far_back),
        16.0: placed(source[1], ready_root, far_front),
        18.0: placed(body_25, ready_root, far_front),
        20.0: placed(body_50, ready_root, far_front),
        22.0: placed(body_75, ready_root, far_front),
        24.0: placed(source[30], ready_root, far_front),
        26.0: placed(source[30], ready_root, predock),
        28.0: placed(source[30], ready_root, dock_mid),
        29.0: placed(source[30], ready_root, dock_near),
        30.0: weapon_stage._copy_pose(source[30]),
    }
    if tuple(guided) != TRANSITION_GUIDED_CORRIDOR_KEY_FRAMES:
        raise RuntimeError("Candidate007 guided corridor key schedule drifted")

    def root_world_for_pose(pose: dict[str, Matrix]) -> Matrix:
        apply_pose(armature, pose)
        bpy.context.view_layer.update()
        return root.matrix_world.copy()

    def guided_pose_at(frame: float) -> dict[str, Matrix]:
        if frame in guided:
            return weapon_stage._copy_pose(guided[frame])
        lower = max(key for key in guided if key < frame)
        upper = min(key for key in guided if key > frame)
        return weapon_stage._blend_pose(
            guided[lower], guided[upper], (frame - lower) / (upper - lower)
        )

    catch_root = root_world_for_pose(guided[TRANSITION_PREGRASP_TARGET_FRAME])
    acquisition_body = weapon_stage._blend_pose(
        guided[TRANSITION_GUIDED_THROUGH_FRAME],
        guided[TRANSITION_PREGRASP_TARGET_FRAME],
        0.5,
    )
    acquisition_hand = catch_root @ ready_root_to_hand
    acquisition_hand.translation += catch_root.to_3x3() @ Vector(
        (-TRANSITION_PREGRASP_CLEARANCE_M, 0.0, 0.0)
    )
    acquisition_pose = _pose_hand_wrist_frame(
        armature,
        acquisition_body,
        "R",
        acquisition_hand.translation,
        acquisition_hand.to_3x3(),
        original_single_arm_solver,
    )
    acquisition_pose = weapon_stage._pose_weapon_at_world(
        armature, acquisition_pose, catch_root, carrier_to_root
    )

    ownership_frames = _frame_samples(
        TRANSITION_OWNERSHIP_START_FRAME,
        TRANSITION_OWNERSHIP_DENSE_END_FRAME,
        TRANSITION_SAMPLE_STEP_FRAMES,
    )
    # Cache the complete V9 pose and resulting RifleRoot before solving a hand.
    # The weapon control is top-level, but restoring it explicitly makes the
    # no-slip ownership contract independent of that implementation detail.
    ownership_bases = {frame: guided_pose_at(frame) for frame in ownership_frames}
    ownership_roots = {
        frame: root_world_for_pose(pose)
        for frame, pose in ownership_bases.items()
    }
    owned: dict[float, dict[str, Matrix]] = {}
    for frame in ownership_frames:
        target_root = ownership_roots[frame]
        desired_hand = target_root @ ready_root_to_hand
        solved = _pose_hand_wrist_frame(
            armature,
            ownership_bases[frame],
            "R",
            desired_hand.translation,
            desired_hand.to_3x3(),
            original_single_arm_solver,
        )
        owned[frame] = weapon_stage._pose_weapon_at_world(
            armature, solved, target_root, carrier_to_root
        )

    result = {
        frame: weapon_stage._copy_pose(pose)
        for frame, pose in guided.items()
        if frame <= TRANSITION_GUIDED_THROUGH_FRAME
    }
    result[TRANSITION_PREGRASP_FRAME] = acquisition_pose
    result.update(owned)
    result[TRANSITION_READY_FRAME] = weapon_stage._copy_pose(source[30])
    result = dict(sorted(result.items()))
    if tuple(result) != TRANSITION_DRAW_KEY_FRAMES:
        raise RuntimeError("Candidate007 late-catch transition key schedule drifted")
    return result


def _validate_candidate007_transition_actions(
    armature: bpy.types.Object,
) -> dict[str, object]:
    """Prove exact endpoints and subframe draw/sheath time reversal."""
    def evaluated(action_name: str, frame: float) -> dict[str, Matrix]:
        activate_action(armature, action_name)
        integer_frame = math.floor(frame)
        bpy.context.scene.frame_set(integer_frame, subframe=frame - integer_frame)
        bpy.context.view_layer.update()
        return weapon_stage._basis_snapshot(armature)

    endpoint_pairs = (
        (("PS_Weapon_Draw", 1), ("PS_WeaponStowed_Idle", 1)),
        (("PS_Weapon_Draw", 30), ("PS_WeaponReady_Idle", 1)),
        (("PS_Weapon_Sheathe", 1), ("PS_WeaponReady_Idle", 1)),
        (("PS_Weapon_Sheathe", 30), ("PS_WeaponStowed_Idle", 1)),
    )
    endpoint_error = 0.0
    for first, second in endpoint_pairs:
        first_pose = evaluated(*first)
        second_pose = evaluated(*second)
        endpoint_error = max(
            endpoint_error,
            *(
                _maximum_matrix_delta(first_pose[name], second_pose[name])
                for name in EXPECTED_BONES
            ),
        )
    reversal_error = 0.0
    if TRANSITION_CERTIFICATION_STEP_FRAMES != TRANSITION_SAMPLE_STEP_FRAMES:
        raise RuntimeError("Candidate007 transition certification step drifted from dense authoring")
    reversal_samples = [
        tick * TRANSITION_CERTIFICATION_STEP_FRAMES
        for tick in range(
            int(round(1.0 / TRANSITION_CERTIFICATION_STEP_FRAMES)),
            int(round(30.0 / TRANSITION_CERTIFICATION_STEP_FRAMES)) + 1,
        )
    ]
    for frame in reversal_samples:
        draw_pose = evaluated("PS_Weapon_Draw", frame)
        sheathe_pose = evaluated("PS_Weapon_Sheathe", 31 - frame)
        reversal_error = max(
            reversal_error,
            *(
                _maximum_matrix_delta(draw_pose[name], sheathe_pose[name])
                for name in EXPECTED_BONES
            ),
        )
    if endpoint_error > 1.0e-5 or reversal_error > 1.0e-5:
        raise RuntimeError(
            "Candidate007 draw/sheath symmetry failed: "
            f"endpoint={endpoint_error:.9f}, reversal={reversal_error:.9f}."
        )
    return {
        "endpoint_max_matrix_error": endpoint_error,
        "subframe_reversal_max_matrix_error": reversal_error,
        "reversal_certification_step_frames": TRANSITION_CERTIFICATION_STEP_FRAMES,
        "reversal_sample_count": len(reversal_samples),
    }


def _candidate007_append_control_curves_to_legacy(
    armature: bpy.types.Object,
    hand_to_root: Matrix,
    carrier_to_root: Matrix,
) -> None:
    """Stow the rifle for base movement clips and retain the solved Aim carrier.

    The shared stage assumes every legacy clip should carry the rifle relative
    to Hand.R.  That is correct for PS_Aim, but Idle/Walk/Hover have both hands
    lowered and would drag a long gun through the torso.  Candidate007 instead
    treats those three weapon-agnostic body sources as safely stowed.
    """
    for name in ("PS_Idle", "PS_Walk", "PS_Hover", "PS_Aim"):
        action = bpy.data.actions[name]
        slot = find_action_slot(action, armature)
        frames = weapon_stage._legacy_key_frames(action, armature)
        for frame in frames:
            activate_action(armature, action)
            bpy.context.scene.frame_set(frame)
            for bone_name in CONTROL_BONES:
                armature.pose.bones[bone_name].matrix_basis = Matrix.Identity(4)
            bpy.context.view_layer.update()
            if name == "PS_Aim":
                hand_world = matrix_world_for_pose_bone(
                    armature, armature.pose.bones["Hand.R"]
                )
                desired_root = hand_world @ hand_to_root
            else:
                desired_root = weapon_stage._stowed_world(armature)
            desired_carrier = desired_root @ carrier_to_root.inverted()
            armature.pose.bones[weapon_stage.WEAPON_ROOT_BONE].matrix = (
                armature.matrix_world.inverted() @ desired_carrier
            )
            bpy.context.view_layer.update()
            weapon_stage._key_control_bones(
                action, slot, armature, frame
            )
        bag = get_action_channelbag(action, slot)
        for curve in bag.fcurves:
            curve.update()


@contextmanager
def _candidate007_pipeline_overrides(
    root: bpy.types.Object,
) -> Iterator[dict[str, dict[str, object]]]:
    """Patch both direct-import save bindings; never touch Generator114 output."""
    before_hash = _sha256(PINNED_PIPELINE_BLEND)
    original_aim_save = aim_stage.save_current_blend
    original_weapon_save = weapon_stage.save_current_blend
    original_stowed_world = weapon_stage._stowed_world
    original_ready_pose = weapon_stage._ready_pose
    original_append_legacy = weapon_stage._append_control_curves_to_legacy
    original_single_arm_pose = weapon_stage._single_arm_pose
    original_pose_component_delta = weapon_stage._pose_component_delta
    original_build_action = weapon_stage._build_action
    original_get_stance_profile = aim_stage.get_stance_profile
    candidate_profile = _candidate007_stance_profile(root)
    transition_cache: dict[str, dict[float, dict[str, Matrix]]] = {}
    build_evidence: dict[str, dict[str, object]] = {
        "manipulation": {},
        "transition_paths": {},
    }

    def no_save(_filename: str) -> Path:
        return Path(bpy.data.filepath).resolve()

    aim_stage.save_current_blend = no_save
    weapon_stage.save_current_blend = no_save
    aim_stage.get_stance_profile = lambda name: (
        candidate_profile
        if name == "shouldered_precision"
        else original_get_stance_profile(name)
    )
    # Keep the original callable reachable without a closure that becomes hard
    # to diagnose in a Blender traceback.
    weapon_stage._candidate007_original_stowed_world = original_stowed_world
    weapon_stage._candidate007_original_single_arm_pose = original_single_arm_pose
    weapon_stage._candidate007_original_pose_component_delta = (
        original_pose_component_delta
    )
    weapon_stage._candidate007_root = root
    weapon_stage._stowed_world = _candidate007_stowed_world
    weapon_stage._ready_pose = _candidate007_ready_pose
    weapon_stage._single_arm_pose = _candidate007_single_arm_pose
    weapon_stage._pose_component_delta = _candidate007_pose_component_delta

    def candidate_build_action(
        armature: bpy.types.Object,
        name: str,
        poses: dict[float, dict[str, Matrix]],
    ) -> None:
        if name == "PS_Weapon_Draw":
            poses = _candidate007_transition_poses(
                armature, root, poses, original_single_arm_pose
            )
            if tuple(poses) != TRANSITION_DRAW_KEY_FRAMES:
                raise RuntimeError("Candidate007 late-catch key schedule drifted")
            transition_cache["draw"] = poses
            build_evidence["transition_paths"][name] = {
                "version": TRANSITION_PATH_VERSION,
                "sample_step_frames": TRANSITION_SAMPLE_STEP_FRAMES,
                "certification_step_frames": TRANSITION_CERTIFICATION_STEP_FRAMES,
                "key_frames": list(TRANSITION_DRAW_KEY_FRAMES),
                "result_frames": list(poses),
                "construction": (
                    "powered_back_mount_guided__measured_early_acquisition__"
                    "hand_r_no_slip_ready_dock"
                ),
                "deployment_mode": "powered_back_mount_guided",
                "guided_through_frame": TRANSITION_GUIDED_THROUGH_FRAME,
                "early_acquisition_frame": TRANSITION_PREGRASP_FRAME,
                "early_acquisition_target_frame": TRANSITION_PREGRASP_TARGET_FRAME,
                "early_acquisition_clearance_m": TRANSITION_PREGRASP_CLEARANCE_M,
                "primary_contact_window": list(
                    TRANSITION_PRIMARY_CONTACT_DRAW_WINDOW
                ),
                "ownership_bone": "Hand.R",
                "ownership_start_frame": TRANSITION_OWNERSHIP_START_FRAME,
                "ownership_dense_end_frame": TRANSITION_OWNERSHIP_DENSE_END_FRAME,
                "ownership_sample_step_frames": TRANSITION_SAMPLE_STEP_FRAMES,
                "ownership_mode": (
                    "full_ready_root_relative_hand_frame__cached_v9_root_restored"
                ),
                "support_contact_window": list(
                    TRANSITION_SUPPORT_CONTACT_DRAW_WINDOW
                ),
            }
        elif name == "PS_Weapon_Sheathe":
            draw = transition_cache.get("draw")
            if draw is None:
                raise RuntimeError("Candidate007 sheathe was built before its draw source.")
            poses = {
                31.0 - frame: weapon_stage._copy_pose(pose)
                for frame, pose in draw.items()
            }
            poses = dict(sorted(poses.items()))
            build_evidence["transition_paths"][name] = {
                "version": TRANSITION_PATH_VERSION,
                "sample_step_frames": TRANSITION_SAMPLE_STEP_FRAMES,
                "certification_step_frames": TRANSITION_CERTIFICATION_STEP_FRAMES,
                "key_frames": list(poses),
                "result_frames": list(poses),
                "construction": "exact_time_reverse_of_draw",
                "deployment_mode": "powered_back_mount_guided_exact_reverse",
                "guided_from_frame": 31.0 - TRANSITION_GUIDED_THROUGH_FRAME,
                "early_acquisition_frame": 31.0 - TRANSITION_PREGRASP_FRAME,
                "early_acquisition_target_frame": (
                    31.0 - TRANSITION_PREGRASP_TARGET_FRAME
                ),
                "early_acquisition_clearance_m": TRANSITION_PREGRASP_CLEARANCE_M,
                "primary_contact_window": list(
                    TRANSITION_PRIMARY_CONTACT_SHEATHE_WINDOW
                ),
                "ownership_bone": "Hand.R",
                "ownership_end_frame": 31.0 - TRANSITION_OWNERSHIP_START_FRAME,
                "ownership_dense_start_frame": (
                    31.0 - TRANSITION_OWNERSHIP_DENSE_END_FRAME
                ),
                "ownership_sample_step_frames": TRANSITION_SAMPLE_STEP_FRAMES,
                "ownership_mode": (
                    "exact_reverse_full_ready_root_relative_hand_frame__"
                    "cached_v9_root_restored"
                ),
                "support_contact_window": list(
                    TRANSITION_SUPPORT_CONTACT_SHEATHE_WINDOW
                ),
            }
        elif name in {"PS_Reload", "PS_BoltCycle"}:
            poses, evidence = _densify_manipulation_poses(armature, name, poses)
            build_evidence["manipulation"][name] = evidence
        if name in {"PS_Reload", "PS_BoltCycle"}:
            manipulating_side = "L" if name == "PS_Reload" else "R"
            component_bone = (
                weapon_stage.MAGAZINE_BONE
                if name == "PS_Reload"
                else weapon_stage.BOLT_BONE
            )
            quaternion_bones = {
                f"Shoulder.{manipulating_side}",
                f"UpperArm.{manipulating_side}",
                f"LowerArm.{manipulating_side}",
                f"Hand.{manipulating_side}",
                component_bone,
            }
            _build_fractional_action(
                armature, name, poses, quaternion_bones=quaternion_bones
            )
            interpolation_counts = _set_action_interpolation(
                armature,
                name,
                "LINEAR",
                quaternion_bones,
            )
            build_evidence["manipulation"][name]["interpolation_counts"] = (
                interpolation_counts
            )
            build_evidence["manipulation"][name]["interpolation"] = "LINEAR"
        elif name in {"PS_Weapon_Draw", "PS_Weapon_Sheathe"}:
            transition_quaternion_bones = set(EXPECTED_BONES)
            _build_fractional_action(
                armature,
                name,
                poses,
                quaternion_bones=transition_quaternion_bones,
            )
            interpolation_counts = _set_action_interpolation(
                armature,
                name,
                "LINEAR",
                transition_quaternion_bones,
            )
            build_evidence["transition_paths"][name]["interpolation"] = "LINEAR"
            build_evidence["transition_paths"][name]["interpolation_counts"] = (
                interpolation_counts
            )
        else:
            original_build_action(armature, name, poses)

    weapon_stage._build_action = candidate_build_action
    weapon_stage._append_control_curves_to_legacy = (
        _candidate007_append_control_curves_to_legacy
    )
    try:
        yield build_evidence
    finally:
        aim_stage.save_current_blend = original_aim_save
        weapon_stage.save_current_blend = original_weapon_save
        aim_stage.get_stance_profile = original_get_stance_profile
        weapon_stage._stowed_world = original_stowed_world
        weapon_stage._ready_pose = original_ready_pose
        weapon_stage._single_arm_pose = original_single_arm_pose
        weapon_stage._pose_component_delta = original_pose_component_delta
        weapon_stage._build_action = original_build_action
        weapon_stage._append_control_curves_to_legacy = original_append_legacy
        for temporary in (
            "_candidate007_original_stowed_world",
            "_candidate007_original_single_arm_pose",
            "_candidate007_original_pose_component_delta",
            "_candidate007_root",
        ):
            if hasattr(weapon_stage, temporary):
                delattr(weapon_stage, temporary)
        after_hash = _sha256(PINNED_PIPELINE_BLEND)
        if after_hash != before_hash:
            raise RuntimeError(
                "Pinned Generator114 powersuit_pipeline.blend changed during Candidate007 reauthor."
            )


def reauthor_candidate007_weapon_actions(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> dict[str, object]:
    """Rebuild PS_Aim plus all twenty weapon actions, without saving the blend."""
    if bpy.app.version < (5, 2, 0):
        raise RuntimeError("Candidate007 action reauthor requires Blender 5.2 or newer.")
    if bpy.data.objects.get(armature.name) != armature or bpy.data.objects.get(root.name) != root:
        raise RuntimeError("Candidate007 reauthor arguments must be live Blender objects.")
    _assert_exact_input(armature, root)

    before = {
        action.name: _action_signature(action, armature)
        for action in bpy.data.actions if action.name.startswith("PS_")
    }
    preserved_body_hashes = {
        name: _body_only_action_hash(bpy.data.actions[name], armature)
        for name in ("PS_Idle", "PS_Walk", "PS_Hover")
    }
    pinned_before = _sha256(PINNED_PIPELINE_BLEND)
    normalization: dict[str, object]
    with _candidate007_pipeline_overrides(root) as build_evidence:
        normalization = _normalize_to_precontrol_rig(armature, root)
        # Aim consumes the independent Candidate007 definition and leaves it on
        # Hand.R.  Weapon stage then recreates all controls and the 20 actions.
        aim_stage.main()
        weapon_stage.main()

    expected_manipulation_evidence = {"PS_Reload", "PS_BoltCycle"}
    expected_transition_evidence = {"PS_Weapon_Draw", "PS_Weapon_Sheathe"}
    if set(build_evidence.get("manipulation", {})) != expected_manipulation_evidence:
        raise RuntimeError("Candidate007 dense manipulation evidence is incomplete")
    if set(build_evidence.get("transition_paths", {})) != expected_transition_evidence:
        raise RuntimeError("Candidate007 transition-path evidence is incomplete")

    transition_evidence = _validate_candidate007_transition_actions(armature)
    _validate_exact_output(armature, root)
    for name, expected in preserved_body_hashes.items():
        actual = _body_only_action_hash(bpy.data.actions[name], armature)
        if actual != expected:
            raise RuntimeError(f"{name} body curves changed during Candidate007 reauthor.")

    carrier_world = matrix_world_for_pose_bone(
        armature, armature.pose.bones["WeaponRoot"]
    )
    carrier_to_root = carrier_world.inverted_safe() @ root.matrix_world
    if carrier_to_root.to_3x3().determinant() <= 0.0:
        raise RuntimeError("Candidate007 carrier-to-root invariant became reflected.")
    carrier_values = _matrix_values(carrier_to_root)
    bolt_target_corridor_root_local = _bolt_target_corridor_root_local_evidence()
    root["ps_candidate007_reauthor_version"] = REAUTHOR_VERSION
    stow_rearward_delta = STOW_REARWARD_DELTA_M
    for key, value in STANCE_PROPERTY_DEFAULTS.items():
        root[key] = float(value)
    root[STOW_REARWARD_PROPERTY] = float(stow_rearward_delta)
    root[STOW_OUTWARD_PROPERTY] = float(STOW_OUTWARD_DELTA_M)
    root["ps_candidate007_ready_pose_mode"] = READY_POSE_MODE
    root["ps_candidate007_transition_pose_mode"] = TRANSITION_POSE_MODE
    root["ps_candidate007_manipulation_densification_version"] = (
        MANIPULATION_DENSIFICATION_VERSION
    )
    root["ps_candidate007_manipulation_densification_json"] = json.dumps(
        build_evidence["manipulation"], sort_keys=True, separators=(",", ":")
    )
    root["ps_candidate007_bolt_measured_release_path_version"] = (
        BOLT_MEASURED_RELEASE_PATH_VERSION
    )
    root["ps_candidate007_bolt_measured_release_deltas_root_local_json"] = (
        json.dumps(
            BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M,
            sort_keys=True,
            separators=(",", ":"),
        )
    )
    root["ps_candidate007_bolt_measured_pose_substitutions_json"] = json.dumps(
        BOLT_MEASURED_POSE_SUBSTITUTIONS, sort_keys=True, separators=(",", ":")
    )
    root["ps_candidate007_bolt_measured_eighth_frame_clearances_json"] = (
        json.dumps(
            BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M,
            sort_keys=True,
            separators=(",", ":"),
        )
    )
    root["ps_candidate007_reload_measured_return_path_version"] = (
        RELOAD_MEASURED_RETURN_PATH_VERSION
    )
    root["ps_candidate007_reload_measured_return_anchor_frames_json"] = (
        json.dumps(RELOAD_MEASURED_RETURN_ANCHOR_FRAMES, separators=(",", ":"))
    )
    root["ps_candidate007_reload_measured_return_deltas_root_local_json"] = (
        json.dumps(
            RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M,
            sort_keys=True,
            separators=(",", ":"),
        )
    )
    root["ps_candidate007_transition_path_version"] = TRANSITION_PATH_VERSION
    root["ps_candidate007_transition_path_json"] = json.dumps(
        build_evidence["transition_paths"], sort_keys=True, separators=(",", ":")
    )
    root["ps_candidate007_transition_deployment_mode"] = (
        "powered_back_mount_guided"
    )
    root["ps_candidate007_transition_guided_through_frame"] = float(
        TRANSITION_GUIDED_THROUGH_FRAME
    )
    root["ps_candidate007_transition_early_acquisition_frame"] = float(
        TRANSITION_PREGRASP_FRAME
    )
    root["ps_candidate007_transition_primary_contact_draw_window_json"] = (
        json.dumps(TRANSITION_PRIMARY_CONTACT_DRAW_WINDOW, separators=(",", ":"))
    )
    root["ps_candidate007_transition_ownership_start_frame"] = float(
        TRANSITION_OWNERSHIP_START_FRAME
    )
    root["ps_candidate007_reload_hand_outward_m"] = float(
        RELOAD_HAND_OUTWARD_M
    )
    root["ps_candidate007_reload_magazine_outward_m"] = float(
        RELOAD_MAGAZINE_OUTWARD_M
    )
    root["ps_candidate007_manipulation_solver_version"] = (
        MANIPULATION_SOLVER_VERSION
    )
    root["ps_candidate007_reload_palm_roll_deg"] = float(
        RELOAD_PALM_ROLL_DEG
    )
    root["ps_candidate007_bolt_palm_roll_deg"] = float(BOLT_PALM_ROLL_DEG)
    root["ps_candidate007_bolt_hand_outward_m"] = float(
        BOLT_HAND_OUTWARD_M
    )
    root["ps_candidate007_reload_hand_to_mag_outward_delta_m"] = float(
        RELOAD_HAND_OUTWARD_M - RELOAD_MAGAZINE_OUTWARD_M
    )
    root["ps_candidate007_reload_path_mode"] = RELOAD_PATH_MODE
    root["ps_candidate007_bolt_target_mode"] = BOLT_TARGET_MODE
    root["ps_candidate007_bolt_target_classifier_mode"] = (
        BOLT_TARGET_CLASSIFIER_MODE
    )
    root["ps_candidate007_bolt_target_corridor_root_local_json"] = json.dumps(
        bolt_target_corridor_root_local, sort_keys=True, separators=(",", ":")
    )
    root["ps_candidate007_hand_contact_pad_center_local_json"] = json.dumps(
        HAND_CONTACT_PAD_CENTER_LOCAL, sort_keys=True, separators=(",", ":")
    )
    root["ps_candidate007_hand_contact_solve_tolerance_m"] = float(
        HAND_CONTACT_SOLVE_TOLERANCE_M
    )
    root["ps_candidate007_reload_contact_mode"] = RELOAD_CONTACT_MODE
    root["ps_candidate007_reload_detached_frames_json"] = json.dumps([36, 50, 64])
    root["ps_candidate007_reload_seated_frames_json"] = json.dumps([14, 25, 75])
    root["ps_candidate007_reload_shared_target_outward_m"] = float(
        SHARED_RELOAD_TARGET_OUTWARD_M
    )
    root["ps_candidate007_reload_magazine_half_width_m"] = float(
        RELOAD_MAGAZINE_HALF_WIDTH_M
    )
    root["ps_candidate007_reload_contact_inset_m"] = float(RELOAD_CONTACT_INSET_M)
    root["ps_candidate007_reload_detached_twist_deg"] = float(
        RELOAD_DETACHED_TWIST_DEG
    )
    root["ps_candidate007_reload_pull_lug_object_name"] = RELOAD_PULL_LUG_OBJECT_NAME
    root["ps_candidate007_bolt_contact_mode"] = BOLT_TARGET_MODE
    root["ps_candidate007_bolt_contact_frames_json"] = json.dumps([4, 8, 12, 16])
    root["ps_candidate007_bolt_shared_target_outward_m"] = float(
        SHARED_BOLT_TARGET_OUTWARD_M
    )
    root["ps_candidate007_bolt_contact_inset_m"] = float(BOLT_CONTACT_INSET_M)
    root["ps_candidate007_bolt_knob_object_name"] = BOLT_KNOB_OBJECT_NAME
    root["ps_candidate007_legacy_carrier_mode"] = "idle_walk_hover_stowed__aim_solved"
    root["ps_candidate007_carrier_to_root_matrix_json"] = json.dumps(
        carrier_values, separators=(",", ":")
    )

    after = {
        action.name: _action_signature(action, armature)
        for action in bpy.data.actions if action.name.startswith("PS_")
    }
    intentionally_reauthored = ["PS_Aim", *WEAPON_ANIMATION_ACTIONS]
    carrier_only_retargeted = ["PS_Idle", "PS_Walk", "PS_Hover"]
    unchanged_body_actions = carrier_only_retargeted
    changed = sorted(name for name in before if before[name]["sha256"] != after[name]["sha256"])
    # The three body-source clips retain their body curves but must receive new
    # WeaponRoot/Magazine/Bolt carrier curves.  Their complete signatures are
    # therefore also expected to change for a genuinely different rifle fit.
    expected_changed = sorted([*intentionally_reauthored, *carrier_only_retargeted])
    if changed != expected_changed:
        raise RuntimeError(
            "Candidate007 action mutation scope mismatch: "
            f"changed={changed}, expected={expected_changed}."
        )
    pinned_after = _sha256(PINNED_PIPELINE_BLEND)
    if pinned_after != pinned_before:
        raise RuntimeError("Pinned Generator114 blend hash changed outside the save guard.")

    return {
        "schema_version": 1,
        "reauthor_version": REAUTHOR_VERSION,
        "action_signature_schema": ACTION_SIGNATURE_SCHEMA,
        "input_bone_count": 23,
        "normalization_bone_count": 20,
        "output_bone_count": 23,
        "normalization": normalization,
        "intentionally_reauthored_actions": intentionally_reauthored,
        "carrier_only_retargeted_actions": carrier_only_retargeted,
        "changed_action_signatures": {
            name: {"before": before[name], "after": after[name]}
            for name in intentionally_reauthored
        },
        "preserved_body_actions": {
            name: {
                "body_curve_semantic_sha256": preserved_body_hashes[name],
                "after": after[name],
            }
            for name in unchanged_body_actions
        },
        "action_count": len(after),
        "carrier_parent": root.parent.name if root.parent is not None else None,
        "carrier_parent_bone": root.parent_bone,
        "carrier_to_root_matrix": carrier_values,
        "ready_pose_mode": READY_POSE_MODE,
        "transition_pose_mode": TRANSITION_POSE_MODE,
        "transition_evidence": transition_evidence,
        "transition_path_version": TRANSITION_PATH_VERSION,
        "transition_path_evidence": build_evidence["transition_paths"],
        "legacy_carrier_mode": "idle_walk_hover_stowed__aim_solved",
        "stance_profile": {
            key.removeprefix("ps_candidate007_"): float(default)
            for key, default in STANCE_PROPERTY_DEFAULTS.items()
        },
        "stow_rearward_delta_m": stow_rearward_delta,
        "stow_outward_delta_m": STOW_OUTWARD_DELTA_M,
        "draw_extraction_back_clearance_m": DRAW_EXTRACTION_BACK_CLEARANCE_M,
        "draw_extraction_lateral_m": DRAW_EXTRACTION_LATERAL_M,
        "manipulation_densification_version": MANIPULATION_DENSIFICATION_VERSION,
        "manipulation_densification_evidence": build_evidence["manipulation"],
        "bolt_measured_release_path_version": BOLT_MEASURED_RELEASE_PATH_VERSION,
        "bolt_measured_release_deltas_root_local_m": {
            str(frame): list(delta)
            for frame, delta in sorted(
                BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M.items()
            )
        },
        "bolt_measured_pose_substitutions": {
            str(frame): source
            for frame, source in sorted(BOLT_MEASURED_POSE_SUBSTITUTIONS.items())
        },
        "bolt_measured_eighth_frame_clearances_m": {
            str(frame): clearance_m
            for frame, clearance_m in sorted(
                BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M.items()
            )
        },
        "reload_measured_return_path_version": RELOAD_MEASURED_RETURN_PATH_VERSION,
        "reload_measured_return_blend_endpoint_frames": list(
            RELOAD_MEASURED_RETURN_BLEND_ENDPOINT_FRAMES
        ),
        "reload_measured_return_anchor_frames": list(
            RELOAD_MEASURED_RETURN_ANCHOR_FRAMES
        ),
        "reload_measured_return_deltas_root_local_m": {
            str(frame): list(delta)
            for frame, delta in sorted(
                RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M.items()
            )
        },
        "reload_hand_outward_m": RELOAD_HAND_OUTWARD_M,
        "reload_magazine_outward_m": RELOAD_MAGAZINE_OUTWARD_M,
        "manipulation_solver_version": MANIPULATION_SOLVER_VERSION,
        "reload_palm_roll_deg": RELOAD_PALM_ROLL_DEG,
        "bolt_palm_roll_deg": BOLT_PALM_ROLL_DEG,
        "reload_hand_to_mag_outward_delta_m": (
            RELOAD_HAND_OUTWARD_M - RELOAD_MAGAZINE_OUTWARD_M
        ),
        "bolt_hand_outward_m": BOLT_HAND_OUTWARD_M,
        "reload_path_mode": RELOAD_PATH_MODE,
        "bolt_target_mode": BOLT_TARGET_MODE,
        "bolt_target_classifier_mode": BOLT_TARGET_CLASSIFIER_MODE,
        "bolt_target_corridor_root_local_m": bolt_target_corridor_root_local,
        "hand_contact_pad_center_local": HAND_CONTACT_PAD_CENTER_LOCAL,
        "hand_contact_solve_tolerance_m": HAND_CONTACT_SOLVE_TOLERANCE_M,
        "reload_contact_mode": RELOAD_CONTACT_MODE,
        "reload_detached_frames": [36, 50, 64],
        "reload_seated_frames": [14, 25, 75],
        "reload_shared_target_outward_m": SHARED_RELOAD_TARGET_OUTWARD_M,
        "reload_magazine_half_width_m": RELOAD_MAGAZINE_HALF_WIDTH_M,
        "reload_contact_inset_m": RELOAD_CONTACT_INSET_M,
        "reload_detached_twist_deg": RELOAD_DETACHED_TWIST_DEG,
        "reload_pull_lug_object_name": RELOAD_PULL_LUG_OBJECT_NAME,
        "bolt_contact_mode": BOLT_TARGET_MODE,
        "bolt_contact_frames": [4, 8, 12, 16],
        "bolt_shared_target_outward_m": SHARED_BOLT_TARGET_OUTWARD_M,
        "bolt_contact_inset_m": BOLT_CONTACT_INSET_M,
        "bolt_knob_object_name": BOLT_KNOB_OBJECT_NAME,
        "pinned_pipeline_blend": PINNED_PIPELINE_BLEND.relative_to(ROOT).as_posix(),
        "pinned_pipeline_sha256": pinned_after,
    }


if __name__ == "__main__":
    raise RuntimeError(
        "This module is a callable Candidate007 builder stage; import and call "
        "reauthor_candidate007_weapon_actions(armature, root)."
    )
