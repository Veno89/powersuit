# pyright: reportMissingImports=false
"""Shared Blender 5.2 helpers for the Powered Suit asset pipeline.

This module contains no modelling, animation, rendering, or export entry point.
It only centralizes deterministic state handling and Blender 5.2 Action Slot use.
"""
from __future__ import annotations

import json
import math
import os
import re
from pathlib import Path
from typing import Iterable, Sequence

import bpy  # type: ignore
from bpy_extras import anim_utils  # type: ignore
from mathutils import Matrix, Quaternion, Vector  # type: ignore

ARMATURE_NAME = "PowerSuit_Armature"
RIFLE_ROOT_NAME = "RifleRoot"
RIGHT_HAND_BONE = "Hand.R"
REQUIRED_ACTIONS = ("PS_Idle", "PS_Walk", "PS_Hover", "PS_Aim")
PIPELINE_TEMP_PREFIX = "PS_PIPELINE_TEMP_"


def require_blender_52() -> None:
    if tuple(bpy.app.version[:2]) < (5, 2):
        raise RuntimeError(
            f"Blender 5.2 or newer is required; running {bpy.app.version_string}."
        )


def ensure_object_mode() -> None:
    active = bpy.context.view_layer.objects.active
    if active is not None and active.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")


def get_armature() -> bpy.types.Object:
    armature = bpy.data.objects.get(ARMATURE_NAME)
    if armature is None or armature.type != "ARMATURE":
        raise RuntimeError(f"Required armature '{ARMATURE_NAME}' was not found.")
    return armature


def get_rifle_root() -> bpy.types.Object:
    root = bpy.data.objects.get(RIFLE_ROOT_NAME)
    if root is None:
        raise RuntimeError(
            f"Required rifle root '{RIFLE_ROOT_NAME}' was not found. "
            "Run upgrade_rifle_model.py first."
        )
    return root


def project_directory() -> Path:
    if bpy.data.filepath:
        return Path(bpy.data.filepath).resolve().parent
    return Path(__file__).resolve().parent.parent


def ensure_directory(*parts: str) -> Path:
    path = project_directory().joinpath(*parts)
    path.mkdir(parents=True, exist_ok=True)
    return path


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")


def remove_object_tree(root: bpy.types.Object) -> None:
    ensure_object_mode()
    # Avoid Blender's recursive children_recursive property here. A malformed
    # hierarchy should produce a controlled Python error instead of exhausting
    # Blender's native stack while a repair script is trying to remove it.
    hierarchy = object_tree(root)
    for obj in reversed(hierarchy[1:]):
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if data is not None and getattr(data, "users", 1) == 0:
            _remove_orphan_data(data)
    data = root.data
    bpy.data.objects.remove(root, do_unlink=True)
    if data is not None and getattr(data, "users", 1) == 0:
        _remove_orphan_data(data)


def _remove_orphan_data(data: object) -> None:
    collections = (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.cameras,
        bpy.data.lights,
    )
    for collection in collections:
        try:
            if data in collection.values():
                collection.remove(data)
                return
        except (TypeError, ReferenceError):
            pass


def remove_pipeline_temps() -> None:
    ensure_object_mode()
    for obj in list(bpy.data.objects):
        if obj.name.startswith(PIPELINE_TEMP_PREFIX):
            data = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if data is not None and getattr(data, "users", 1) == 0:
                _remove_orphan_data(data)


def select_only(objects: Iterable[bpy.types.Object], active=None) -> None:
    ensure_object_mode()
    bpy.ops.object.select_all(action="DESELECT")
    items = list(objects)
    for obj in items:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active or (items[0] if items else None)


def object_tree(root: bpy.types.Object) -> list[bpy.types.Object]:
    """Return a deterministic, cycle-checked object hierarchy."""
    result: list[bpy.types.Object] = []
    stack: list[bpy.types.Object] = [root]
    visited: set[int] = set()
    while stack:
        obj = stack.pop()
        pointer = int(obj.as_pointer())
        if pointer in visited:
            raise RuntimeError(
                f"Object parenting cycle or duplicate traversal detected at '{obj.name}'."
            )
        visited.add(pointer)
        result.append(obj)
        children = sorted(list(obj.children), key=lambda child: child.name, reverse=True)
        stack.extend(children)
    return result


def find_action_slot(action: bpy.types.Action, animated_id) -> object:
    slots = list(getattr(action, "slots", ()))
    if not slots:
        raise RuntimeError(f"Action '{action.name}' has no Blender 5.2 Action Slot.")

    # Prefer a slot whose identifier/name explicitly contains the object name.
    for slot in slots:
        label = " ".join(
            str(getattr(slot, attr, ""))
            for attr in ("name", "identifier", "target_id_type")
        )
        if animated_id.name in label:
            return slot

    # A pipeline action must animate exactly one armature ID. Choosing the sole slot
    # is deterministic and avoids carrying a stale slot from another Action.
    if len(slots) == 1:
        return slots[0]

    raise RuntimeError(
        f"Action '{action.name}' has {len(slots)} slots and none can be "
        f"resolved for '{animated_id.name}'."
    )


def ensure_action_channelbag(action: bpy.types.Action, slot) -> object:
    """Return the channelbag owned by exactly this Action Slot.

    Blender 5.x no longer stores F-Curves directly on Action.  Using the
    official anim_utils helper prevents implicit key insertion from creating or
    selecting a different slot/channelbag than the one assigned to the armature.
    """
    channelbag = anim_utils.action_ensure_channelbag_for_slot(action, slot)
    if channelbag is None:
        raise RuntimeError(
            f"Failed to create a channelbag for Action '{action.name}' slot "
            f"'{getattr(slot, 'identifier', getattr(slot, 'name', '<slot>'))}'."
        )
    return channelbag


def get_action_channelbag(action: bpy.types.Action, slot) -> object:
    channelbag = anim_utils.action_get_channelbag_for_slot(action, slot)
    if channelbag is None:
        raise RuntimeError(
            f"Action '{action.name}' slot "
            f"'{getattr(slot, 'identifier', getattr(slot, 'name', '<slot>'))}' "
            "has no channelbag/F-Curves."
        )
    return channelbag


def action_slot_curve_stats(action: bpy.types.Action, slot) -> dict[str, int]:
    channelbag = get_action_channelbag(action, slot)
    curves = list(channelbag.fcurves)
    return {
        "curve_count": len(curves),
        "keyframe_count": sum(len(curve.keyframe_points) for curve in curves),
        "empty_curve_count": sum(1 for curve in curves if len(curve.keyframe_points) == 0),
    }



_ROTATION_PATH_RE = re.compile(
    r'^pose\.bones\["(?P<bone>.+)"\]\.(?P<property>rotation_euler|rotation_quaternion|rotation_axis_angle)$'
)


def action_rotation_modes(
    action: bpy.types.Action,
    slot,
) -> dict[str, str]:
    """Return the rotation representation stored for each pose bone.

    Rotation mode is persistent pose state, not part of an Action. Blender does
    not automatically switch a bone from QUATERNION back to XYZ merely because
    the newly activated Action contains rotation_euler curves. Without this
    explicit mapping, reopening a file saved on PS_Aim can make PS_Idle appear
    identical because its Euler curves are ignored by quaternion-mode bones.
    """
    channelbag = get_action_channelbag(action, slot)
    modes: dict[str, str] = {}
    for curve in channelbag.fcurves:
        match = _ROTATION_PATH_RE.match(curve.data_path)
        if match is None:
            continue
        bone_name = match.group("bone")
        property_name = match.group("property")
        mode = {
            "rotation_euler": "XYZ",
            "rotation_quaternion": "QUATERNION",
            "rotation_axis_angle": "AXIS_ANGLE",
        }[property_name]
        previous = modes.get(bone_name)
        if previous is not None and previous != mode:
            raise RuntimeError(
                f"Action '{action.name}' mixes {previous} and {mode} rotation "
                f"curves for pose bone '{bone_name}'."
            )
        modes[bone_name] = mode
    return modes


def apply_action_rotation_modes(
    armature: bpy.types.Object,
    action: bpy.types.Action,
    slot,
) -> dict[str, str]:
    """Set pose-bone rotation modes to match the Action's F-Curve paths."""
    modes = action_rotation_modes(action, slot)
    for bone_name, mode in modes.items():
        bone = armature.pose.bones.get(bone_name)
        if bone is None:
            raise RuntimeError(
                f"Action '{action.name}' targets missing pose bone '{bone_name}'."
            )
        bone.rotation_mode = mode
    return modes


def expected_transform_curve_count(
    armature: bpy.types.Object,
    action: bpy.types.Action,
    slot,
    *,
    require_every_bone: bool = True,
) -> int:
    """Expected location + rotation + scale curve count for a baked pose Action."""
    modes = action_rotation_modes(action, slot)
    if require_every_bone:
        missing = [bone.name for bone in armature.pose.bones if bone.name not in modes]
        if missing:
            raise RuntimeError(
                f"Action '{action.name}' has no rotation curves for: "
                + ", ".join(missing)
            )
    total = 0
    for bone in armature.pose.bones:
        mode = modes.get(bone.name)
        if mode is None:
            continue
        rotation_components = 4 if mode in {"QUATERNION", "AXIS_ANGLE"} else 3
        total += 3 + rotation_components + 3
    return total


def create_action_with_slot(
    armature: bpy.types.Object,
    name: str,
    frame_start: float,
    frame_end: float,
) -> tuple[bpy.types.Action, object]:
    old = bpy.data.actions.get(name)
    if old is not None:
        adt = armature.animation_data
        if adt is not None and adt.action == old:
            adt.action = None
        bpy.data.actions.remove(old, do_unlink=True)

    action = bpy.data.actions.new(name=name)
    action.use_fake_user = True
    if hasattr(action, "use_frame_range"):
        action.use_frame_range = True
        action.frame_start = frame_start
        action.frame_end = frame_end

    # Blender 5.2 uses slotted/layered Actions. The signature changed during the
    # transition, so use the 5.2 form first and retain a named fallback.
    try:
        slot = action.slots.new(armature.id_type, armature.name)
    except TypeError:
        slot = action.slots.new(for_id=armature)

    adt = armature.animation_data_create()
    adt.action = action
    adt.action_slot = slot
    if adt.action != action or adt.action_slot != slot:
        raise RuntimeError(f"Failed to activate slot for Action '{name}'.")
    ensure_action_channelbag(action, slot)
    return action, slot


def activate_action(
    armature: bpy.types.Object,
    action_or_name: str | bpy.types.Action,
) -> tuple[bpy.types.Action, object]:
    action = (
        bpy.data.actions.get(action_or_name)
        if isinstance(action_or_name, str)
        else action_or_name
    )
    if action is None:
        raise RuntimeError(f"Action '{action_or_name}' was not found.")
    slot = find_action_slot(action, armature)
    adt = armature.animation_data_create()

    # Rotation mode lives on PoseBone rather than inside Action evaluation. Set
    # it from this Action's exact F-Curve paths before assignment/evaluation.
    # This is essential when switching between legacy Euler Actions and a file
    # that was last saved with quaternion-mode bones.
    if adt.action != action or getattr(adt, "action_slot", None) != slot:
        adt.action = None
        bpy.context.view_layer.update()
    apply_action_rotation_modes(armature, action, slot)
    adt.action = action
    adt.action_slot = slot

    if adt.action != action or adt.action_slot != slot:
        raise RuntimeError(
            f"Action/slot activation failed for '{action.name}' on '{armature.name}'."
        )
    bpy.context.view_layer.update()
    return action, slot

def matrix_world_for_pose_bone(
    armature: bpy.types.Object,
    pose_bone: bpy.types.PoseBone,
) -> Matrix:
    return armature.matrix_world @ pose_bone.matrix


def bone_head_world(armature: bpy.types.Object, bone_name: str) -> Vector:
    bone = armature.pose.bones.get(bone_name)
    if bone is None:
        raise RuntimeError(f"Missing pose bone '{bone_name}'.")
    return armature.matrix_world @ bone.head


def bone_tail_world(armature: bpy.types.Object, bone_name: str) -> Vector:
    bone = armature.pose.bones.get(bone_name)
    if bone is None:
        raise RuntimeError(f"Missing pose bone '{bone_name}'.")
    return armature.matrix_world @ bone.tail


def evaluated_pose_matrices(
    armature: bpy.types.Object,
    bone_names: Sequence[str] | None = None,
) -> dict[str, Matrix]:
    bpy.context.view_layer.update()
    names = bone_names or [bone.name for bone in armature.pose.bones]
    return {
        name: armature.pose.bones[name].matrix.copy()
        for name in names
        if name in armature.pose.bones
    }


def apply_pose_matrices(
    armature: bpy.types.Object,
    matrices: dict[str, Matrix],
) -> None:
    # Armature order is parent-first for this generated rig. Multiple updates make
    # matrix assignment deterministic even if a user later reorders bones.
    pending = dict(matrices)
    for _ in range(3):
        for bone in armature.pose.bones:
            matrix = pending.get(bone.name)
            if matrix is not None:
                bone.matrix = matrix
        bpy.context.view_layer.update()


def body_basis(armature: bpy.types.Object) -> tuple[Vector, Vector, Vector]:
    """Return visual right, visual forward, and up vectors in world space.

    The helmet visor is authoritative for visual forward.  The supplied rig uses
    a mirrored naming convention where ``Shoulder.R`` can lie on negative world
    X.  That naming must never be allowed to reverse the character's face
    direction.  Side-specific arm logic derives named-shoulder outward axes
    separately from this right-handed visual basis.
    """
    chest = bone_head_world(armature, "Chest")
    head = bone_tail_world(armature, "Head")
    up = (head - chest).normalized()

    def object_center(name: str) -> Vector | None:
        obj = bpy.data.objects.get(name)
        if obj is None:
            return None
        if obj.type != "MESH":
            return obj.matrix_world.translation.copy()
        # Direct object bounds are sufficient for directional inference and do
        # not force a full evaluated dependency graph merely to find a centre.
        corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
        if not corners:
            return obj.matrix_world.translation.copy()
        return sum(corners, Vector((0.0, 0.0, 0.0))) / len(corners)

    forward_hint: Vector | None = None
    visor_center = object_center("Helmet_Visor")
    helmet_center = object_center("Helmet_Core")
    if visor_center is not None and helmet_center is not None:
        face = visor_center - helmet_center
        face -= up * face.dot(up)
        if face.length > 1.0e-5:
            forward_hint = face.normalized()

    if forward_hint is None:
        backpack_center = object_center("Backpack_Core")
        chest_center = object_center("Chest_Core")
        if backpack_center is not None and chest_center is not None:
            back = backpack_center - chest_center
            back -= up * back.dot(up)
            if back.length > 1.0e-5:
                forward_hint = (-back).normalized()

    if forward_hint is None:
        foot_head = bone_head_world(armature, "Foot.L")
        foot_tail = bone_tail_world(armature, "Foot.L")
        hint = foot_tail - foot_head
        hint -= up * hint.dot(up)
        if hint.length > 1.0e-5:
            forward_hint = hint.normalized()

    if forward_hint is None:
        raise RuntimeError("Could not derive the character's visual forward axis.")

    forward = forward_hint - up * forward_hint.dot(up)
    if forward.length < 1.0e-6:
        raise RuntimeError("Helmet visor direction is parallel to the body up axis.")
    forward.normalize()

    # For local rifle axes X=right, Y=forward, Z=up, X cross Y must equal Z.
    right = forward.cross(up)
    if right.length < 1.0e-6:
        raise RuntimeError("Could not construct a right-handed body basis.")
    right.normalize()
    up = right.cross(forward).normalized()

    if right.cross(forward).dot(up) < 0.9999:
        raise RuntimeError("Body basis is not right-handed.")
    return right, forward, up


def named_shoulder_outward_axes(
    armature: bpy.types.Object,
    right: Vector,
    forward: Vector,
    up: Vector,
) -> tuple[Vector, Vector]:
    """Return outward directions for the bones named .R and .L.

    These are intentionally independent from the visual right axis because the
    source rig's side labels are mirrored relative to a conventional world-right
    basis.  The returned vectors are projected into the lateral plane.
    """
    chest = bone_head_world(armature, "Chest")
    shoulder_r = bone_head_world(armature, "UpperArm.R")
    shoulder_l = bone_head_world(armature, "UpperArm.L")

    def lateral_axis(point: Vector, fallback: Vector) -> Vector:
        axis = point - chest
        axis -= up * axis.dot(up)
        axis -= forward * axis.dot(forward)
        if axis.length < 1.0e-6:
            return fallback.copy()
        return axis.normalized()

    outward_r = lateral_axis(shoulder_r, right)
    outward_l = lateral_axis(shoulder_l, -right)
    if outward_r.dot(outward_l) > -0.5:
        outward_l = -outward_r
    return outward_r, outward_l

def matrix_from_axes(
    origin: Vector,
    x_axis: Vector,
    y_axis: Vector,
    z_axis: Vector,
) -> Matrix:
    rot = Matrix((x_axis.normalized(), y_axis.normalized(), z_axis.normalized())).transposed()
    return Matrix.Translation(origin) @ rot.to_4x4()


def orientation_with_y_axis(y_axis: Vector, z_hint: Vector) -> Matrix:
    y = y_axis.normalized()
    z = z_hint - y * z_hint.dot(y)
    if z.length < 1.0e-6:
        z = Vector((0.0, 0.0, 1.0))
        z -= y * z.dot(y)
    z.normalize()
    x = y.cross(z).normalized()
    z = x.cross(y).normalized()
    return matrix_from_axes(Vector((0.0, 0.0, 0.0)), x, y, z)


def rotate_pose_bone_world(
    armature: bpy.types.Object,
    bone_name: str,
    axis_world: Vector,
    radians: float,
) -> None:
    bone = armature.pose.bones.get(bone_name)
    if bone is None:
        raise RuntimeError(f"Missing pose bone '{bone_name}'.")
    current = matrix_world_for_pose_bone(armature, bone)
    location = current.translation.copy()
    rotation = current.to_3x3()
    delta = Matrix.Rotation(radians, 3, axis_world.normalized())
    desired = Matrix.Translation(location) @ (delta @ rotation).to_4x4()
    bone.matrix = armature.matrix_world.inverted() @ desired
    bpy.context.view_layer.update()


def world_bounds(objects: Iterable[bpy.types.Object]) -> tuple[Vector, Vector]:
    points: list[Vector] = []
    for obj in objects:
        if obj.type != "MESH" or obj.hide_render:
            continue
        for corner in obj.bound_box:
            points.append(obj.matrix_world @ Vector(corner))
    if not points:
        raise RuntimeError("No visible mesh bounds were available for camera framing.")
    minimum = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    maximum = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return minimum, maximum




def detach_rifle_for_validation(
    armature: bpy.types.Object,
    root: bpy.types.Object,
) -> dict[str, object]:
    """Temporarily break RifleRoot's bone-parent dependency for rendering.

    The saved asset remains bone-parented for Unity. Validation can safely move
    the detached root from the baked Hand.R matrix at each requested frame,
    avoiding a fresh-file dependency-graph recursion observed in Blender 5.2.
    """
    if root.parent != armature or root.parent_type != "BONE" or root.parent_bone != RIGHT_HAND_BONE:
        raise RuntimeError(
            "RifleRoot must be bone-parented only to Hand.R before validation detaches it."
        )
    hand = armature.pose.bones.get(RIGHT_HAND_BONE)
    if hand is None:
        raise RuntimeError(f"Missing required pose bone '{RIGHT_HAND_BONE}'.")
    world = root.matrix_world.copy()
    hand_world = matrix_world_for_pose_bone(armature, hand)
    hand_offset = hand_world.inverted() @ world
    state = {
        "parent": root.parent,
        "parent_type": root.parent_type,
        "parent_bone": root.parent_bone,
        "world": world,
        "matrix_parent_inverse": root.matrix_parent_inverse.copy(),
        "hand_offset": hand_offset,
    }
    root.parent = None
    root.parent_type = "OBJECT"
    root.parent_bone = ""
    root.matrix_parent_inverse = Matrix.Identity(4)
    root.matrix_world = world
    bpy.context.view_layer.update()
    return state


def sync_detached_rifle_to_hand(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    state: dict[str, object],
) -> None:
    if root.parent is not None:
        raise RuntimeError("RifleRoot must remain detached during validation sync.")
    hand = armature.pose.bones.get(RIGHT_HAND_BONE)
    if hand is None:
        raise RuntimeError(f"Missing required pose bone '{RIGHT_HAND_BONE}'.")
    root.matrix_world = matrix_world_for_pose_bone(armature, hand) @ state["hand_offset"]
    bpy.context.view_layer.update()


def restore_rifle_after_validation(
    armature: bpy.types.Object,
    root: bpy.types.Object,
    state: dict[str, object],
) -> None:
    if root.parent is not None:
        return
    world = root.matrix_world.copy()
    root.parent = state["parent"]
    root.parent_type = str(state["parent_type"])
    root.parent_bone = str(state["parent_bone"])
    root.matrix_parent_inverse = state["matrix_parent_inverse"]
    root.matrix_world = world
    bpy.context.view_layer.update()
    if root.parent != armature or root.parent_type != "BONE" or root.parent_bone != RIGHT_HAND_BONE:
        raise RuntimeError("Failed to restore RifleRoot bone parenting after validation.")


def set_camera_look_at(
    camera: bpy.types.Object,
    location: Vector,
    target: Vector,
) -> None:
    camera.location = location
    direction = target - location
    if direction.length < 1.0e-6:
        raise RuntimeError("Camera location equals its target.")
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def quaternion_angle_degrees(a: Matrix, b: Matrix) -> float:
    qa = a.to_quaternion().normalized()
    qb = b.to_quaternion().normalized()
    dot = max(-1.0, min(1.0, abs(qa.dot(qb))))
    return math.degrees(2.0 * math.acos(dot))


def remember_scene_state(armature: bpy.types.Object) -> dict[str, object]:
    adt = armature.animation_data
    return {
        "selected": [obj.name for obj in bpy.context.selected_objects],
        "active": bpy.context.view_layer.objects.active.name
        if bpy.context.view_layer.objects.active
        else None,
        "frame": bpy.context.scene.frame_current,
        "action": adt.action if adt else None,
        "slot": adt.action_slot if adt and hasattr(adt, "action_slot") else None,
        "pose": {bone.name: bone.matrix_basis.copy() for bone in armature.pose.bones},
    }


def restore_scene_state(armature: bpy.types.Object, state: dict[str, object]) -> None:
    ensure_object_mode()
    adt = armature.animation_data_create()
    adt.action = state["action"]
    if state["action"] is not None and state["slot"] is not None:
        adt.action_slot = state["slot"]
    for name, matrix in state["pose"].items():
        bone = armature.pose.bones.get(name)
        if bone is not None:
            bone.matrix_basis = matrix
    bpy.context.scene.frame_set(int(state["frame"]))
    bpy.ops.object.select_all(action="DESELECT")
    for name in state["selected"]:
        obj = bpy.data.objects.get(name)
        if obj is not None:
            obj.select_set(True)
    active_name = state["active"]
    if active_name:
        active = bpy.data.objects.get(active_name)
        if active is not None:
            bpy.context.view_layer.objects.active = active
    bpy.context.view_layer.update()


def save_current_blend(output_name: str | None = None) -> Path:
    if output_name:
        path = project_directory() / output_name
        bpy.ops.wm.save_as_mainfile(filepath=str(path))
        return path
    if not bpy.data.filepath:
        raise RuntimeError("Save the Blender file before running the pipeline.")
    bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
    return Path(bpy.data.filepath)


def create_static_render_scene(
    scene_name: str,
    source_objects: Iterable[bpy.types.Object],
) -> tuple[bpy.types.Scene, bpy.types.Collection, dict[str, bpy.types.Object]]:
    """Create an isolated render scene containing dependency-free mesh proxies.

    Blender 5.2 on Windows has shown native stack overflows when the renderer
    traverses the live armature -> Hand.R -> RifleRoot hierarchy.  Validation
    only needs the visible geometry at already evaluated frames, so copy each
    mesh into a temporary scene with no parent, constraints, animation, or
    external-reference modifiers.  The proxy mesh data remains local and its
    world matrix is updated from the source object before each render.
    """
    existing = bpy.data.scenes.get(scene_name)
    if existing is not None:
        bpy.data.scenes.remove(existing)

    scene = bpy.data.scenes.new(scene_name)
    collection = bpy.data.collections.new(scene_name + "_Collection")
    scene.collection.children.link(collection)

    world = bpy.data.worlds.new(scene_name + "_World")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.010, 0.013, 0.020, 1.0)
        background.inputs["Strength"].default_value = 0.22
    scene.world = world

    proxies: dict[str, bpy.types.Object] = {}
    for source in sorted(source_objects, key=lambda obj: obj.name):
        if source.type != "MESH":
            continue
        if source.name in proxies:
            raise RuntimeError(f"Duplicate proxy source name: {source.name}")

        proxy = source.copy()
        proxy.name = PIPELINE_TEMP_PREFIX + "Proxy_" + source.name
        proxy.data = source.data.copy()
        proxy.parent = None
        proxy.parent_type = "OBJECT"
        proxy.parent_bone = ""
        proxy.matrix_parent_inverse = Matrix.Identity(4)
        if proxy.animation_data is not None:
            proxy.animation_data_clear()
        for constraint in list(proxy.constraints):
            proxy.constraints.remove(constraint)
        # Generated suit/rifle objects use local bevel modifiers.  Keep only
        # dependency-free modifier types so the proxy scene cannot reference
        # the live armature or another source object.
        for modifier in list(proxy.modifiers):
            if modifier.type not in {"BEVEL", "WEIGHTED_NORMAL"}:
                proxy.modifiers.remove(modifier)

        # Workbench validation uses object colors so it never needs to compile
        # the source material node graphs. Pull the visible Principled base color
        # when available and fall back to the material display color.
        display_color = tuple(source.color)
        material = source.active_material
        if material is not None:
            display_color = tuple(material.diffuse_color)
            if material.use_nodes and material.node_tree is not None:
                bsdf = material.node_tree.nodes.get("Principled BSDF")
                if bsdf is not None and "Base Color" in bsdf.inputs:
                    display_color = tuple(bsdf.inputs["Base Color"].default_value)
        proxy.color = display_color
        proxy.matrix_world = source.matrix_world.copy()
        proxy.hide_render = bool(source.hide_render)
        proxy.hide_viewport = False
        collection.objects.link(proxy)
        proxies[source.name] = proxy

    if not proxies:
        remove_static_render_scene(scene, collection, proxies)
        raise RuntimeError("No mesh objects were available for static render proxies.")
    return scene, collection, proxies


def update_static_render_proxies(
    proxies: dict[str, bpy.types.Object],
    *,
    visible_names: set[str] | None = None,
) -> None:
    """Copy source world transforms into the isolated proxy scene."""
    for source_name, proxy in proxies.items():
        source = bpy.data.objects.get(source_name)
        if source is None:
            raise RuntimeError(f"Proxy source object disappeared: {source_name}")
        proxy.matrix_world = source.matrix_world.copy()
        proxy.hide_render = (
            source.hide_render
            or (visible_names is not None and source_name not in visible_names)
        )


def remove_static_render_scene(
    scene: bpy.types.Scene | None,
    collection: bpy.types.Collection | None,
    proxies: dict[str, bpy.types.Object] | None = None,
) -> None:
    """Remove an isolated validation scene and all of its owned data."""
    owned_objects = []
    if collection is not None:
        owned_objects = list(collection.objects)
    elif proxies:
        owned_objects = list(proxies.values())

    for obj in owned_objects:
        data = obj.data
        if obj.name in bpy.data.objects:
            bpy.data.objects.remove(obj, do_unlink=True)
        if data is not None and getattr(data, "users", 1) == 0:
            _remove_orphan_data(data)

    world = scene.world if scene is not None else None
    if scene is not None and scene.name in bpy.data.scenes:
        bpy.data.scenes.remove(scene)
    if collection is not None and collection.name in bpy.data.collections:
        bpy.data.collections.remove(collection)
    if world is not None and world.users == 0 and world.name in bpy.data.worlds:
        bpy.data.worlds.remove(world)
