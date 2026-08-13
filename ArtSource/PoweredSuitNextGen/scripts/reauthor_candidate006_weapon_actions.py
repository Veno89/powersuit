# pyright: reportMissingImports=false
"""Re-author Candidate006 weapon actions against its immutable hardpoints.

This is an in-memory Blender 5.2 pipeline stage.  It deliberately delegates the
actual aim and weapon-action construction to the already vetted Generator114
solvers, but adapts their pre-control-rig input contract to Candidate005/006's
existing 23-bone carrier rig.  The stage never saves, exports, models, or edits
the legacy pipeline blend.

Public API::

    evidence = reauthor_candidate006_weapon_actions(armature, rifle_root)

Preconditions:
- exactly the canonical 23 bones and 24 PS_ actions exist;
- RifleRoot is the new, frozen Candidate006 rigid source definition;
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
Candidate006 output; the owning builder decides whether that result is saved.
"""
from __future__ import annotations

import hashlib
import json
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
STOW_REARWARD_DELTA_M = 0.23
STOW_OUTWARD_DELTA_M = 0.04
DRAW_EXTRACTION_BACK_CLEARANCE_M = 0.08
DRAW_EXTRACTION_LATERAL_M = 0.04
RELOAD_HAND_OUTWARD_M = 0.05
RELOAD_MAGAZINE_OUTWARD_M = 0.05
BOLT_HAND_OUTWARD_M = 0.04
REAUTHOR_VERSION = "CANDIDATE006_WEAPON_ACTIONS_V3"
ACTION_SIGNATURE_SCHEMA = "CANDIDATE006_ACTION_SEMANTICS_V2"

# Candidate006 has a materially wider receiver and a different stock/optic
# relationship than the Generator114 rifle.  These values are authored on the
# isolated RifleRoot by the Candidate006 builder so a review blend records the
# exact stance inputs that produced its actions.  The legacy stance profile is
# never mutated.
STANCE_PROPERTY_DEFAULTS = {
    "ps_candidate006_stock_inward_m": 0.045,
    "ps_candidate006_stock_forward_m": 0.035,
    "ps_candidate006_stock_up_m": 0.070,
    "ps_candidate006_weapon_pitch_deg": -6.000,
    "ps_candidate006_trigger_shoulder_forward_deg": 14.000,
    "ps_candidate006_support_shoulder_forward_deg": 38.000,
    "ps_candidate006_aiming_eye_outward_m": 0.055,
}
STOW_REARWARD_PROPERTY = "ps_candidate006_stow_rearward_delta_m"
STOW_OUTWARD_PROPERTY = "ps_candidate006_stow_outward_delta_m"
READY_POSE_MODE = "forward_preaim_head_neutral"
TRANSITION_POSE_MODE = "outward_extract_then_rotate_symmetric"


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
            "Candidate006 animation input must be the ordered canonical 23-bone rig; "
            f"got {actual_bones}."
        )
    names = {action.name for action in bpy.data.actions if action.name.startswith("PS_")}
    if names != set(REQUIRED_ACTIONS):
        raise RuntimeError(
            "Candidate006 animation input must contain exactly 24 PS_ actions; "
            f"missing={sorted(set(REQUIRED_ACTIONS) - names)}, "
            f"unexpected={sorted(names - set(REQUIRED_ACTIONS))}."
        )
    if root.name != "RifleRoot" or root.type != "EMPTY":
        raise RuntimeError("Candidate006 rigid weapon root must be the RifleRoot empty.")
    if int(root.get("ps_generator_version", 0)) < 6006:
        raise RuntimeError("RifleRoot is not the Candidate006 rigid source definition.")
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
        raise RuntimeError("Candidate006 requires tagged magazine and bolt source components.")
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
        raise RuntimeError("Failed to normalize Candidate006 to the canonical 20 body bones.")
    return {
        "legacy_control_curves_removed": removed_curves,
        "magazine_objects": [obj.name for obj in magazines],
        "bolt_objects": [obj.name for obj in bolts],
    }


def _validate_exact_output(armature: bpy.types.Object, root: bpy.types.Object) -> None:
    actual_bones = tuple(bone.name for bone in armature.data.bones)
    if actual_bones != EXPECTED_BONES:
        raise RuntimeError(f"Candidate006 reauthor changed the canonical bone contract: {actual_bones}")
    names = {action.name for action in bpy.data.actions if action.name.startswith("PS_")}
    if names != set(EXPECTED_ACTION_RANGES):
        raise RuntimeError("Candidate006 reauthor did not restore the exact 24-action set.")
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


def _candidate006_stowed_world(armature: bpy.types.Object) -> Matrix:
    """Move the vetted scabbard target rearward and clear of the back plate."""
    target = weapon_stage._candidate006_original_stowed_world(armature)
    right, forward, _up = body_basis(armature)
    result = target.copy()
    rearward_delta = STOW_REARWARD_DELTA_M
    outward_delta = STOW_OUTWARD_DELTA_M
    if not 0.20 <= rearward_delta <= 0.35:
        raise RuntimeError(
            f"Candidate006 stow rearward delta {rearward_delta:.3f} m is outside "
            "the audited 0.20-0.35 m envelope."
        )
    if not 0.03 <= outward_delta <= 0.05:
        raise RuntimeError(
            f"Candidate006 stow outward delta {outward_delta:.3f} m is outside "
            "the requested 0.03-0.05 m envelope."
        )
    result.translation = (
        target.translation
        - forward * rearward_delta
        - right * outward_delta
    )
    if result.to_3x3().determinant() <= 0.0:
        raise RuntimeError("Candidate006 stowed target became reflected.")
    return result


def _candidate006_stance_profile(root: bpy.types.Object):
    """Return an immutable per-candidate derivative of the vetted long-gun stance."""
    base = aim_stage.get_stance_profile("shouldered_precision")
    values = dict(STANCE_PROPERTY_DEFAULTS)
    profile = replace(
        base,
        stock_inward_m=values["ps_candidate006_stock_inward_m"],
        stock_forward_m=values["ps_candidate006_stock_forward_m"],
        stock_up_m=values["ps_candidate006_stock_up_m"],
        weapon_pitch_deg=values["ps_candidate006_weapon_pitch_deg"],
        trigger_shoulder_forward_deg=values[
            "ps_candidate006_trigger_shoulder_forward_deg"
        ],
        support_shoulder_forward_deg=values[
            "ps_candidate006_support_shoulder_forward_deg"
        ],
        aiming_eye_outward_m=values["ps_candidate006_aiming_eye_outward_m"],
    )
    if not -0.08 <= profile.stock_inward_m <= 0.10:
        raise RuntimeError("Candidate006 stock inward offset is outside its audited envelope.")
    if not -0.02 <= profile.stock_forward_m <= 0.10:
        raise RuntimeError("Candidate006 stock fore/aft offset is outside its audited envelope.")
    if not 0.00 <= profile.stock_up_m <= 0.10:
        raise RuntimeError("Candidate006 stock height is outside its audited envelope.")
    if not -8.0 <= profile.weapon_pitch_deg <= 5.0:
        raise RuntimeError("Candidate006 weapon pitch is outside its audited envelope.")
    if not 8.0 <= profile.trigger_shoulder_forward_deg <= 30.0:
        raise RuntimeError("Candidate006 trigger shoulder angle is outside its audited envelope.")
    if not 20.0 <= profile.support_shoulder_forward_deg <= 55.0:
        raise RuntimeError("Candidate006 support shoulder angle is outside its audited envelope.")
    if not 0.04 <= profile.aiming_eye_outward_m <= 0.10:
        raise RuntimeError("Candidate006 aiming-eye offset is outside its audited envelope.")
    return profile


def _candidate006_ready_pose(
    armature: bpy.types.Object,
    _root: bpy.types.Object,
    idle_basis: dict[str, Matrix],
    _original_root_local: Matrix,
) -> dict[str, Matrix]:
    """Use the solved forward long-gun stance for hip fire, with a neutral head.

    Generator114's generic ready solver points a rifle diagonally upward across
    the chest.  That is incompatible with Candidate006 gameplay: unaimed fire
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


def _candidate006_single_arm_pose(
    armature: bpy.types.Object,
    base_pose: dict[str, Matrix],
    side: str,
    target_world: Vector,
) -> dict[str, Matrix]:
    """Keep reload/bolt reaches on the outside of the wide receiver.

    The shared solver targets component centres.  Candidate006's receiver cage
    is substantially wider, so the same targets pull the forearm through the
    armour before the hand reaches the magazine or lateral bolt handle.  Only
    the known magazine (left hand) and bolt-handle (right hand, close to the
    tagged bolt centre) calls are offset; draw reaches retain their authored
    target and are handled by the explicit extraction path below.
    """
    root = weapon_stage._candidate006_root
    original = weapon_stage._candidate006_original_single_arm_pose
    adjusted = target_world.copy()
    rifle_right = (
        root.matrix_world.to_3x3() @ Vector((1.0, 0.0, 0.0))
    ).normalized()
    if side == "L":
        adjusted += rifle_right * RELOAD_HAND_OUTWARD_M
    elif side == "R":
        bolts = weapon_components(root, COMPONENT_BOLT)
        bolt_center = sum(
            (weapon_stage.weapon_local_position(root, obj) for obj in bolts),
            Vector((0.0, 0.0, 0.0)),
        ) / len(bolts)
        local_target = root.matrix_world.inverted_safe() @ target_world
        if (local_target - bolt_center).length <= 0.09:
            adjusted -= rifle_right * BOLT_HAND_OUTWARD_M
    return original(armature, base_pose, side, adjusted)


def _candidate006_pose_component_delta(
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
    return weapon_stage._candidate006_original_pose_component_delta(
        armature,
        root,
        base_pose,
        control_bone,
        adjusted,
    )


def _candidate006_transition_poses(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    source: dict[int, dict[str, Matrix]],
) -> dict[int, dict[str, Matrix]]:
    """Author an outward translation waypoint before rotating the long rifle."""
    apply_pose = weapon_stage._apply_basis_snapshot
    apply_pose(armature, source[10])
    bpy.context.view_layer.update()
    stowed_root = root.matrix_world.copy()
    carrier_world = matrix_world_for_pose_bone(
        armature, armature.pose.bones[weapon_stage.WEAPON_ROOT_BONE]
    )
    carrier_to_root = carrier_world.inverted_safe() @ stowed_root
    right, forward, up = body_basis(armature)

    extraction_root = stowed_root.copy()
    # First pull normal to the back plate while retaining the stowed
    # orientation.  A smaller lateral offset clears the layered shoulder edge;
    # rotation begins only at the following authored key.
    extraction_root.translation = (
        stowed_root.translation
        - forward * DRAW_EXTRACTION_BACK_CLEARANCE_M
        - right * DRAW_EXTRACTION_LATERAL_M
        + up * 0.01
    )
    primary_local = weapon_stage.weapon_local_position(
        root,
        weapon_stage.require_weapon_helper(root, weapon_stage.ROLE_PRIMARY_GRIP),
    )
    original_single_arm = weapon_stage._candidate006_original_single_arm_pose
    extraction_body = original_single_arm(
        armature,
        source[10],
        "R",
        extraction_root @ primary_local,
    )
    extraction_pose = weapon_stage._pose_weapon_at_world(
        armature,
        extraction_body,
        extraction_root,
        carrier_to_root,
    )

    return {
        1: weapon_stage._copy_pose(source[1]),
        10: extraction_pose,
        18: weapon_stage._copy_pose(source[18]),
        30: weapon_stage._copy_pose(source[30]),
    }


def _validate_candidate006_transition_actions(
    armature: bpy.types.Object,
) -> dict[str, float]:
    """Prove exact endpoints and full-frame draw/sheath time reversal."""
    def evaluated(action_name: str, frame: int) -> dict[str, Matrix]:
        return weapon_stage._evaluate_basis(armature, action_name, frame)

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
    for frame in range(1, 31):
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
            "Candidate006 draw/sheath symmetry failed: "
            f"endpoint={endpoint_error:.9f}, reversal={reversal_error:.9f}."
        )
    return {
        "endpoint_max_matrix_error": endpoint_error,
        "full_frame_reversal_max_matrix_error": reversal_error,
    }


def _candidate006_append_control_curves_to_legacy(
    armature: bpy.types.Object,
    hand_to_root: Matrix,
    carrier_to_root: Matrix,
) -> None:
    """Stow the rifle for base movement clips and retain the solved Aim carrier.

    The shared stage assumes every legacy clip should carry the rifle relative
    to Hand.R.  That is correct for PS_Aim, but Idle/Walk/Hover have both hands
    lowered and would drag a long gun through the torso.  Candidate006 instead
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
def _candidate006_pipeline_overrides(root: bpy.types.Object) -> Iterator[None]:
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
    candidate_profile = _candidate006_stance_profile(root)
    transition_cache: dict[str, dict[int, dict[str, Matrix]]] = {}

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
    weapon_stage._candidate006_original_stowed_world = original_stowed_world
    weapon_stage._candidate006_original_single_arm_pose = original_single_arm_pose
    weapon_stage._candidate006_original_pose_component_delta = (
        original_pose_component_delta
    )
    weapon_stage._candidate006_root = root
    weapon_stage._stowed_world = _candidate006_stowed_world
    weapon_stage._ready_pose = _candidate006_ready_pose
    weapon_stage._single_arm_pose = _candidate006_single_arm_pose
    weapon_stage._pose_component_delta = _candidate006_pose_component_delta

    def candidate_build_action(
        armature: bpy.types.Object,
        name: str,
        poses: dict[int, dict[str, Matrix]],
    ) -> None:
        if name == "PS_Weapon_Draw":
            poses = _candidate006_transition_poses(armature, root, poses)
            transition_cache["draw"] = poses
        elif name == "PS_Weapon_Sheathe":
            draw = transition_cache.get("draw")
            if draw is None:
                raise RuntimeError("Candidate006 sheathe was built before its draw source.")
            poses = {
                1: weapon_stage._copy_pose(draw[30]),
                13: weapon_stage._copy_pose(draw[18]),
                21: weapon_stage._copy_pose(draw[10]),
                30: weapon_stage._copy_pose(draw[1]),
            }
        original_build_action(armature, name, poses)

    weapon_stage._build_action = candidate_build_action
    weapon_stage._append_control_curves_to_legacy = (
        _candidate006_append_control_curves_to_legacy
    )
    try:
        yield
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
            "_candidate006_original_stowed_world",
            "_candidate006_original_single_arm_pose",
            "_candidate006_original_pose_component_delta",
            "_candidate006_root",
        ):
            if hasattr(weapon_stage, temporary):
                delattr(weapon_stage, temporary)
        after_hash = _sha256(PINNED_PIPELINE_BLEND)
        if after_hash != before_hash:
            raise RuntimeError(
                "Pinned Generator114 powersuit_pipeline.blend changed during Candidate006 reauthor."
            )


def reauthor_candidate006_weapon_actions(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> dict[str, object]:
    """Rebuild PS_Aim plus all twenty weapon actions, without saving the blend."""
    if bpy.app.version < (5, 2, 0):
        raise RuntimeError("Candidate006 action reauthor requires Blender 5.2 or newer.")
    if bpy.data.objects.get(armature.name) != armature or bpy.data.objects.get(root.name) != root:
        raise RuntimeError("Candidate006 reauthor arguments must be live Blender objects.")
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
    with _candidate006_pipeline_overrides(root):
        normalization = _normalize_to_precontrol_rig(armature, root)
        # Aim consumes the independent Candidate006 definition and leaves it on
        # Hand.R.  Weapon stage then recreates all controls and the 20 actions.
        aim_stage.main()
        weapon_stage.main()

    transition_evidence = _validate_candidate006_transition_actions(armature)
    _validate_exact_output(armature, root)
    for name, expected in preserved_body_hashes.items():
        actual = _body_only_action_hash(bpy.data.actions[name], armature)
        if actual != expected:
            raise RuntimeError(f"{name} body curves changed during Candidate006 reauthor.")

    carrier_world = matrix_world_for_pose_bone(
        armature, armature.pose.bones["WeaponRoot"]
    )
    carrier_to_root = carrier_world.inverted_safe() @ root.matrix_world
    if carrier_to_root.to_3x3().determinant() <= 0.0:
        raise RuntimeError("Candidate006 carrier-to-root invariant became reflected.")
    carrier_values = _matrix_values(carrier_to_root)
    root["ps_candidate006_reauthor_version"] = REAUTHOR_VERSION
    stow_rearward_delta = STOW_REARWARD_DELTA_M
    for key, value in STANCE_PROPERTY_DEFAULTS.items():
        root[key] = float(value)
    root[STOW_REARWARD_PROPERTY] = float(stow_rearward_delta)
    root[STOW_OUTWARD_PROPERTY] = float(STOW_OUTWARD_DELTA_M)
    root["ps_candidate006_ready_pose_mode"] = READY_POSE_MODE
    root["ps_candidate006_transition_pose_mode"] = TRANSITION_POSE_MODE
    root["ps_candidate006_reload_hand_outward_m"] = float(
        RELOAD_HAND_OUTWARD_M
    )
    root["ps_candidate006_reload_magazine_outward_m"] = float(
        RELOAD_MAGAZINE_OUTWARD_M
    )
    root["ps_candidate006_bolt_hand_outward_m"] = float(
        BOLT_HAND_OUTWARD_M
    )
    root["ps_candidate006_legacy_carrier_mode"] = "idle_walk_hover_stowed__aim_solved"
    root["ps_candidate006_carrier_to_root_matrix_json"] = json.dumps(
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
            "Candidate006 action mutation scope mismatch: "
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
        "legacy_carrier_mode": "idle_walk_hover_stowed__aim_solved",
        "stance_profile": {
            key.removeprefix("ps_candidate006_"): float(default)
            for key, default in STANCE_PROPERTY_DEFAULTS.items()
        },
        "stow_rearward_delta_m": stow_rearward_delta,
        "stow_outward_delta_m": STOW_OUTWARD_DELTA_M,
        "draw_extraction_back_clearance_m": DRAW_EXTRACTION_BACK_CLEARANCE_M,
        "draw_extraction_lateral_m": DRAW_EXTRACTION_LATERAL_M,
        "reload_hand_outward_m": RELOAD_HAND_OUTWARD_M,
        "reload_magazine_outward_m": RELOAD_MAGAZINE_OUTWARD_M,
        "bolt_hand_outward_m": BOLT_HAND_OUTWARD_M,
        "pinned_pipeline_blend": str(PINNED_PIPELINE_BLEND),
        "pinned_pipeline_sha256": pinned_after,
    }


if __name__ == "__main__":
    raise RuntimeError(
        "This module is a callable Candidate006 builder stage; import and call "
        "reauthor_candidate006_weapon_actions(armature, root)."
    )
