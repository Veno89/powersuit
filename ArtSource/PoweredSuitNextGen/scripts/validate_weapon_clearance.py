"""Audit Aegis candidate/rifle intersections at authored action keyframes.

This is a read-only validation tool.  It never saves the open .blend, exports an
FBX, or changes an active Unity asset.  Run it against a locally generated
Candidate003/004 blend and it will write a machine-readable JSON report plus a
compact, actionable text summary.

Blender 5.2 example (from the repository root)::

    blender --background \
      ArtSource/PoweredSuitNextGen/candidates/aegis_vanguard_candidate_v004.blend \
      --python ArtSource/PoweredSuitNextGen/scripts/validate_weapon_clearance.py

Add ``-- --strict`` to make forbidden intersections fail the Blender process
after both reports have been written.  Add ``-- --all-frames`` for an integer
frame sweep rather than the default authored-keyframe sweep.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore
from mathutils.bvhtree import BVHTree  # type: ignore


ROOT = Path(__file__).resolve().parents[3]
PIPELINE_SCRIPTS = ROOT / "ArtSource" / "PoweredSuit" / "scripts"
if str(PIPELINE_SCRIPTS) not in sys.path:
    sys.path.insert(0, str(PIPELINE_SCRIPTS))

from powersuit_pipeline_common import activate_action  # type: ignore  # noqa: E402


ARMATURE_NAME = "PowerSuit_Armature"
EXPECTED_ACTION_COUNT = 24
REPORT_ROOT = (
    ROOT / "ArtSource" / "PoweredSuitNextGen" / "validation" / "weapon_clearance"
)
CANDIDATE_PROPERTY = "aegis_vanguard_candidate"
RUNTIME_ANCHOR_PROPERTY = "aegis_runtime_anchor"
RIFLE_PREFIX = "Rifle_"
BVH_EPSILON_M = 1.0e-6
AABB_EPSILON_M = 1.0e-6
CONTAINMENT_RAY_EPSILON_M = 1.0e-5
MAX_CONTAINMENT_RAY_STEPS = 256

# These modifier types do not depend on pose or time in this procedural lane.
# A mesh carrying any other modifier is evaluated again at every sampled frame.
STATIC_LOCAL_MODIFIERS = frozenset({
    "BEVEL",
    "WEIGHTED_NORMAL",
    "NORMAL_EDIT",
    "SOLIDIFY",
    "TRIANGULATE",
})

READY_STOCK_ACTION_PREFIXES = (
    "PS_Aim",
    "PS_BoltCycle",
    "PS_Hover",
    "PS_Idle",
    "PS_Reload",
    "PS_Run",
    "PS_Walk",
    "PS_WeaponReady",
)


@dataclass(frozen=True)
class LocalGeometry:
    """Evaluated object-space triangles and their local bounds."""

    vertices: tuple[Vector, ...]
    triangles: tuple[tuple[int, int, int], ...]
    minimum: Vector
    maximum: Vector


@dataclass(frozen=True)
class WorldGeometry:
    """Lazily created world-space geometry for one sampled frame."""

    vertices: tuple[Vector, ...]
    triangles: tuple[tuple[int, int, int], ...]
    bvh: BVHTree


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(
        description="Audit candidate/rifle mesh intersections across actions."
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=REPORT_ROOT,
        help="Report directory (default: NextGen validation/weapon_clearance).",
    )
    parser.add_argument(
        "--label",
        default=None,
        help="Output stem override; defaults to the open blend filename.",
    )
    parser.add_argument(
        "--all-frames",
        action="store_true",
        help="Sample every integer action frame, not just authored keyframes.",
    )
    parser.add_argument(
        "--strict",
        action="store_true",
        help="Exit non-zero after writing reports if forbidden contacts exist.",
    )
    parser.add_argument(
        "--include-instances",
        action="store_true",
        help="Include every raw contact instance; grouped evidence is the default.",
    )
    return parser.parse_args(argv)


def candidate_objects() -> list[bpy.types.Object]:
    result = [
        obj
        for obj in bpy.data.objects
        if bool(obj.get(CANDIDATE_PROPERTY, False))
        and not bool(obj.get(RUNTIME_ANCHOR_PROPERTY, False))
        and obj.type in {"MESH", "CURVE", "SURFACE", "FONT"}
    ]
    # Candidate004 is expected to preserve the property.  The AV_ fallback
    # keeps the gate useful while topology is being consolidated into new mesh
    # objects, without ever pulling the hidden Generator114 suit into scope.
    if not result:
        result = [
            obj
            for obj in bpy.data.objects
            if obj.name.startswith("AV_")
            and obj.type in {"MESH", "CURVE", "SURFACE", "FONT"}
        ]
    return sorted(result, key=lambda item: item.name)


def rifle_objects() -> list[bpy.types.Object]:
    return sorted(
        [
            obj
            for obj in bpy.data.objects
            if obj.type == "MESH" and obj.name.startswith(RIFLE_PREFIX)
        ],
        key=lambda item: item.name,
    )


def object_is_dynamic(obj: bpy.types.Object) -> bool:
    data = getattr(obj, "data", None)
    shape_keys = getattr(data, "shape_keys", None)
    if shape_keys is not None and (
        shape_keys.animation_data is not None
        or any(key.value != 0.0 for key in shape_keys.key_blocks[1:])
    ):
        return True
    if obj.animation_data is not None:
        return True
    return any(modifier.type not in STATIC_LOCAL_MODIFIERS for modifier in obj.modifiers)


def evaluated_local_geometry(
    obj: bpy.types.Object,
    depsgraph: bpy.types.Depsgraph,
) -> LocalGeometry:
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
    try:
        if mesh is None:
            raise RuntimeError(f"Could not evaluate collision mesh for '{obj.name}'.")
        mesh.calc_loop_triangles()
        vertices = tuple(vertex.co.copy() for vertex in mesh.vertices)
        triangles = tuple(
            tuple(int(index) for index in triangle.vertices)
            for triangle in mesh.loop_triangles
        )
        if not vertices or not triangles:
            raise RuntimeError(
                f"Collision object '{obj.name}' has no evaluated triangle geometry."
            )
        minimum = Vector(tuple(min(vertex[axis] for vertex in vertices) for axis in range(3)))
        maximum = Vector(tuple(max(vertex[axis] for vertex in vertices) for axis in range(3)))
        return LocalGeometry(vertices, triangles, minimum, maximum)
    finally:
        evaluated.to_mesh_clear()


def local_bound_corners(geometry: LocalGeometry) -> tuple[Vector, ...]:
    minimum = geometry.minimum
    maximum = geometry.maximum
    return tuple(
        Vector((x, y, z))
        for x in (minimum.x, maximum.x)
        for y in (minimum.y, maximum.y)
        for z in (minimum.z, maximum.z)
    )


def world_aabb(
    obj: bpy.types.Object,
    geometry: LocalGeometry,
) -> tuple[Vector, Vector]:
    points = [obj.matrix_world @ corner for corner in local_bound_corners(geometry)]
    minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return minimum, maximum


def aabb_intersection(
    first: tuple[Vector, Vector],
    second: tuple[Vector, Vector],
) -> tuple[Vector, float] | None:
    overlap = Vector(
        tuple(
            min(first[1][axis], second[1][axis])
            - max(first[0][axis], second[0][axis])
            for axis in range(3)
        )
    )
    if any(value < -AABB_EPSILON_M for value in overlap):
        return None
    clamped = Vector(tuple(max(0.0, value) for value in overlap))
    return clamped, clamped.x * clamped.y * clamped.z


def build_world_geometry(
    obj: bpy.types.Object,
    geometry: LocalGeometry,
) -> WorldGeometry:
    vertices = tuple(obj.matrix_world @ vertex for vertex in geometry.vertices)
    bvh = BVHTree.FromPolygons(
        vertices,
        geometry.triangles,
        all_triangles=True,
        epsilon=BVH_EPSILON_M,
    )
    if bvh is None:
        raise RuntimeError(f"Could not build BVH for '{obj.name}'.")
    return WorldGeometry(vertices, geometry.triangles, bvh)


def point_inside_closed_mesh(point: Vector, bvh: BVHTree) -> bool:
    """Return an odd/even ray estimate for a point and a nominally closed mesh."""

    direction = Vector((1.0, 0.371390676, 0.127831)).normalized()
    origin = point.copy()
    hits = 0
    # Candidate scale is under a few metres.  A generous finite ray avoids
    # relying on Blender's platform-specific infinity handling.
    remaining = 100.0
    for _index in range(MAX_CONTAINMENT_RAY_STEPS):
        location, _normal, _triangle, distance = bvh.ray_cast(
            origin, direction, remaining
        )
        if location is None or distance is None:
            break
        hits += 1
        advance = float(distance) + CONTAINMENT_RAY_EPSILON_M
        origin += direction * advance
        remaining -= advance
        if remaining <= 0.0:
            break
    return bool(hits % 2)


def containment_direction(
    first: WorldGeometry,
    second: WorldGeometry,
) -> str | None:
    """Catch full containment when no surface triangles cross.

    Testing a deterministic spread of vertices is sufficient for the compact,
    mostly convex procedural parts in this lane.  The limitation is reported
    explicitly; this is not a general constructive-solid-geometry solver.
    """

    def samples(vertices: tuple[Vector, ...]) -> Iterable[Vector]:
        if len(vertices) <= 9:
            return vertices
        indices = {0, len(vertices) - 1}
        indices.update(round(index * (len(vertices) - 1) / 7) for index in range(1, 7))
        return tuple(vertices[index] for index in sorted(indices))

    if any(point_inside_closed_mesh(point, second.bvh) for point in samples(first.vertices)):
        return "weapon_inside_suit"
    if any(point_inside_closed_mesh(point, first.bvh) for point in samples(second.vertices)):
        return "suit_inside_weapon"
    return None


def action_keyframes(action: bpy.types.Action) -> list[float]:
    frames: set[float] = set()
    layers = getattr(action, "layers", ())
    for layer in layers:
        for strip in layer.strips:
            for channelbag in getattr(strip, "channelbags", ()):
                for fcurve in channelbag.fcurves:
                    frames.update(float(point.co.x) for point in fcurve.keyframe_points)
    # Compatibility with pre-slotted actions if a future source is opened in a
    # Blender build which still exposes Action.fcurves.
    for fcurve in getattr(action, "fcurves", ()):
        frames.update(float(point.co.x) for point in fcurve.keyframe_points)
    if not frames:
        frames.update(float(value) for value in action.frame_range)
    return sorted(frames)


def sampled_frames(action: bpy.types.Action, all_frames: bool) -> list[float]:
    if not all_frames:
        return action_keyframes(action)
    start, end = action.frame_range
    return [float(frame) for frame in range(math.floor(start), math.ceil(end) + 1)]


def set_scene_frame(frame: float) -> None:
    integer = math.floor(frame)
    bpy.context.scene.frame_set(integer, subframe=frame - integer)
    bpy.context.view_layer.update()


def parent_bone(obj: bpy.types.Object) -> str:
    if obj.parent_type == "BONE":
        return str(obj.parent_bone)
    for modifier in obj.modifiers:
        if modifier.type == "ARMATURE" and modifier.object is not None:
            return "SKINNED"
    return ""


def stock_target_zone(suit: bpy.types.Object) -> bool:
    bone = parent_bone(suit)
    if bone == "UpperArm.R" and "Shoulder" in suit.name:
        return True
    if bone == "Chest" and suit.name in {
        "AV_Collar.R",
        "AV_Pectoral.R",
        "AV_PectoralInset.R",
    }:
        return True
    # Consolidated Candidate004 meshes may declare a semantic zone rather than
    # retaining Candidate003's per-panel object names.
    return str(suit.get("aegis_contact_zone", "")) == "stock_shoulder_right"


def classify_contact(
    action_name: str,
    suit: bpy.types.Object,
    weapon: bpy.types.Object,
) -> tuple[str, bool, str]:
    suit_bone = parent_bone(suit)
    suit_zone = str(suit.get("aegis_contact_zone", ""))
    component_role = str(weapon.get("ps_weapon_component_role", ""))
    contact_role = str(weapon.get("ps_weapon_contact_surface_role", ""))

    if (
        suit_bone == "Hand.R" or suit_zone == "primary_grip_hand_right"
    ) and component_role == "primary_grip":
        return (
            "allowed_primary_grip_contact",
            True,
            "Right hand against a primary-grip component.",
        )
    if (
        suit_bone == "Hand.L" or suit_zone == "support_grip_hand_left"
    ) and component_role == "support_grip":
        return (
            "allowed_support_grip_contact",
            True,
            "Left hand against a support-grip component.",
        )
    if (
        contact_role == "stock_contact"
        and stock_target_zone(suit)
        and action_name.startswith(READY_STOCK_ACTION_PREFIXES)
    ):
        return (
            "allowed_stock_shoulder_docking",
            True,
            "Buttpad against the authored right shoulder stock-contact zone.",
        )

    if suit_bone in {"Hand.L", "Hand.R"} or suit_zone in {
        "primary_grip_hand_right",
        "support_grip_hand_left",
    }:
        reason = (
            "Hand intersects a weapon component outside its matching semantic grip role."
        )
    elif weapon.name == "Rifle_Stock_ButtPad":
        reason = "Buttpad intersects outside the ready-action right shoulder docking zone."
    elif action_name.startswith("PS_WeaponStowed"):
        reason = "Stowed weapon intersects visible suit/backpack geometry."
    elif action_name in {"PS_Weapon_Draw", "PS_Weapon_Sheathe"}:
        reason = "Draw/sheathe sweep crosses visible suit geometry."
    else:
        reason = "Weapon intersects visible suit geometry outside an allowed contact zone."
    return "forbidden_weapon_suit_intersection", False, reason


def remember_state(armature: bpy.types.Object) -> dict[str, object]:
    animation_data = armature.animation_data
    return {
        "frame": float(bpy.context.scene.frame_current_final),
        "pose_position": str(armature.data.pose_position),
        "action": animation_data.action if animation_data else None,
        "slot": (
            animation_data.action_slot
            if animation_data and hasattr(animation_data, "action_slot")
            else None
        ),
        "pose": {bone.name: bone.matrix_basis.copy() for bone in armature.pose.bones},
    }


def restore_state(armature: bpy.types.Object, state: dict[str, object]) -> None:
    animation_data = armature.animation_data_create()
    animation_data.action = state["action"]
    if state["action"] is not None and state["slot"] is not None:
        animation_data.action_slot = state["slot"]
    for name, matrix in state["pose"].items():
        bone = armature.pose.bones.get(name)
        if bone is not None:
            bone.matrix_basis = matrix
    armature.data.pose_position = str(state["pose_position"])
    set_scene_frame(float(state["frame"]))


def compact_frame(frame: float) -> int | float:
    rounded = round(frame)
    return int(rounded) if abs(frame - rounded) < 1.0e-6 else round(frame, 6)


def audit(args: argparse.Namespace) -> dict[str, object]:
    blend_path = Path(bpy.data.filepath).resolve()
    if not bpy.data.filepath or not blend_path.exists():
        raise RuntimeError("Open a saved local Aegis candidate .blend before validation.")
    if "PoweredSuitNextGen" not in blend_path.parts or "candidate" not in blend_path.stem:
        raise RuntimeError(
            "Weapon-clearance validation is restricted to a local PoweredSuitNextGen "
            f"candidate blend; got '{blend_path}'."
        )

    source_hash_before = sha256(blend_path)
    armature = bpy.data.objects.get(ARMATURE_NAME)
    if armature is None or armature.type != "ARMATURE":
        raise RuntimeError(f"Required armature '{ARMATURE_NAME}' is missing.")
    suit_objects = candidate_objects()
    weapon_objects = rifle_objects()
    if not suit_objects:
        raise RuntimeError("No Candidate003/004 collision objects were found.")
    if not weapon_objects:
        raise RuntimeError("No Rifle_* mesh objects were found.")

    actions = sorted(
        [action for action in bpy.data.actions if action.name.startswith("PS_")],
        key=lambda item: item.name,
    )
    if len(actions) != EXPECTED_ACTION_COUNT:
        raise RuntimeError(
            f"Expected exactly {EXPECTED_ACTION_COUNT} PS_* actions; found {len(actions)}."
        )

    state = remember_state(armature)
    armature.data.pose_position = "POSE"
    depsgraph = bpy.context.evaluated_depsgraph_get()
    all_collision_objects = [*suit_objects, *weapon_objects]
    dynamic = {obj.name: object_is_dynamic(obj) for obj in all_collision_objects}
    static_geometry: dict[str, LocalGeometry] = {}
    for obj in all_collision_objects:
        if not dynamic[obj.name]:
            static_geometry[obj.name] = evaluated_local_geometry(obj, depsgraph)

    contact_instances: list[dict[str, object]] = []
    action_reports: list[dict[str, object]] = []
    try:
        total_actions = len(actions)
        for action_index, action in enumerate(actions, start=1):
            frames = sampled_frames(action, args.all_frames)
            if not frames:
                raise RuntimeError(f"Action '{action.name}' has no sample frames.")
            print(
                f"[Weapon clearance] {action_index:02d}/{total_actions:02d} "
                f"{action.name}: {len(frames)} frames",
                flush=True,
            )
            activate_action(armature, action)
            action_allowed = 0
            action_forbidden = 0

            for frame in frames:
                set_scene_frame(frame)
                depsgraph = bpy.context.evaluated_depsgraph_get()
                local_geometry: dict[str, LocalGeometry] = dict(static_geometry)
                for obj in all_collision_objects:
                    if dynamic[obj.name]:
                        local_geometry[obj.name] = evaluated_local_geometry(obj, depsgraph)

                suit_bounds = {
                    obj.name: world_aabb(obj, local_geometry[obj.name])
                    for obj in suit_objects
                }
                weapon_bounds = {
                    obj.name: world_aabb(obj, local_geometry[obj.name])
                    for obj in weapon_objects
                }
                world_geometry: dict[str, WorldGeometry] = {}

                def world_for(obj: bpy.types.Object) -> WorldGeometry:
                    cached = world_geometry.get(obj.name)
                    if cached is None:
                        cached = build_world_geometry(obj, local_geometry[obj.name])
                        world_geometry[obj.name] = cached
                    return cached

                for weapon in weapon_objects:
                    weapon_bound = weapon_bounds[weapon.name]
                    for suit in suit_objects:
                        aabb = aabb_intersection(weapon_bound, suit_bounds[suit.name])
                        if aabb is None:
                            continue
                        weapon_world = world_for(weapon)
                        suit_world = world_for(suit)
                        triangle_pairs = weapon_world.bvh.overlap(suit_world.bvh)
                        method = "surface_triangle_crossing"
                        containment = None
                        if not triangle_pairs:
                            containment = containment_direction(weapon_world, suit_world)
                            if containment is None:
                                continue
                            method = containment

                        classification, allowed, reason = classify_contact(
                            action.name, suit, weapon
                        )
                        overlap_axes, overlap_volume = aabb
                        contact_instances.append({
                            "action": action.name,
                            "frame": compact_frame(frame),
                            "suit_object": suit.name,
                            "suit_bone_or_zone": parent_bone(suit),
                            "weapon_object": weapon.name,
                            "weapon_component_role": str(
                                weapon.get("ps_weapon_component_role", "")
                            ),
                            "weapon_contact_surface_role": str(
                                weapon.get("ps_weapon_contact_surface_role", "")
                            ),
                            "classification": classification,
                            "allowed": allowed,
                            "reason": reason,
                            "detection_method": method,
                            "triangle_pair_count": len(triangle_pairs),
                            "aabb_overlap_axes_m": [round(float(value), 6) for value in overlap_axes],
                            "aabb_overlap_min_axis_m": round(float(min(overlap_axes)), 6),
                            "aabb_overlap_volume_m3": round(float(overlap_volume), 9),
                        })
                        if allowed:
                            action_allowed += 1
                        else:
                            action_forbidden += 1

            action_reports.append({
                "action": action.name,
                "sample_frames": [compact_frame(frame) for frame in frames],
                "sample_count": len(frames),
                "allowed_contact_instances": action_allowed,
                "forbidden_intersection_instances": action_forbidden,
                "status": "PASS" if action_forbidden == 0 else "FAIL",
            })
    finally:
        restore_state(armature, state)

    forbidden = [item for item in contact_instances if not bool(item["allowed"])]
    allowed = [item for item in contact_instances if bool(item["allowed"])]

    def group_instances(
        source: list[dict[str, object]],
    ) -> list[dict[str, object]]:
        grouped: dict[tuple[str, str, str], list[dict[str, object]]] = defaultdict(list)
        for instance in source:
            key = (
                str(instance["weapon_object"]),
                str(instance["suit_object"]),
                str(instance["classification"]),
            )
            grouped[key].append(instance)
        result: list[dict[str, object]] = []
        for (weapon_name, suit_name, classification), instances in grouped.items():
            action_frames: dict[str, list[int | float]] = defaultdict(list)
            for instance in instances:
                action_frames[str(instance["action"])].append(instance["frame"])
            result.append({
                "weapon_object": weapon_name,
                "suit_object": suit_name,
                "classification": classification,
                "instance_count": len(instances),
                "actions_and_frames": {
                    action: sorted(set(frames), key=float)
                    for action, frames in sorted(action_frames.items())
                },
                "max_triangle_pair_count": max(
                    int(instance["triangle_pair_count"]) for instance in instances
                ),
                "max_aabb_overlap_min_axis_m": max(
                    float(instance["aabb_overlap_min_axis_m"]) for instance in instances
                ),
                "reason": str(instances[0]["reason"]),
            })
        result.sort(
            key=lambda group: (
                -int(group["instance_count"]),
                -float(group["max_aabb_overlap_min_axis_m"]),
                str(group["weapon_object"]),
                str(group["suit_object"]),
            )
        )
        return result

    forbidden_groups = group_instances(forbidden)
    allowed_groups = group_instances(allowed)
    classification_counts: dict[str, int] = defaultdict(int)
    for instance in contact_instances:
        classification_counts[str(instance["classification"])] += 1

    source_hash_after = sha256(blend_path)
    if source_hash_after != source_hash_before:
        raise RuntimeError("Open candidate blend changed during read-only validation.")

    report: dict[str, object] = {
        "schema_version": 1,
        "gate": "AEGIS_WEAPON_SUIT_CLEARANCE",
        "status": "PASS" if not forbidden else "FAIL",
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "blender_version": ".".join(str(value) for value in bpy.app.version),
        "candidate_blend": str(blend_path),
        "candidate_blend_sha256_before": source_hash_before,
        "candidate_blend_sha256_after": source_hash_after,
        "candidate_blend_preserved": source_hash_before == source_hash_after,
        "sample_mode": "all_integer_frames" if args.all_frames else "authored_keyframes",
        "armature": armature.name,
        "candidate_collision_objects": len(suit_objects),
        "rifle_collision_objects": len(weapon_objects),
        "dynamic_collision_objects": sorted(
            name for name, is_dynamic in dynamic.items() if is_dynamic
        ),
        "action_count": len(actions),
        "sampled_frame_count": sum(int(item["sample_count"]) for item in action_reports),
        "allowed_contact_instances": len(allowed),
        "forbidden_intersection_instances": len(forbidden),
        "forbidden_object_pair_groups": len(forbidden_groups),
        "contact_classification_counts": dict(sorted(classification_counts.items())),
        "contact_policy": {
            "allowed": [
                "primary-grip components against Hand.R geometry",
                "support-grip components against Hand.L geometry",
                "Rifle_Stock_ButtPad against the authored right shoulder/chest stock zone in ready-action families",
            ],
            "forbidden": [
                "all other rifle/suit triangle crossings or detected containment",
                "all stowed-rifle contacts with backpack/armor geometry",
                "all draw/sheathe sweep contacts outside matching hand grips",
            ],
        },
        "actions": action_reports,
        "allowed_groups": allowed_groups,
        "forbidden_groups": forbidden_groups,
        "limitations": [
            "The default sweep samples authored action keyframes; inter-keyframe collisions require --all-frames and fractional high-speed tunnelling still requires denser sampling.",
            "BVH surface crossings are exact for evaluated render triangles; full containment uses deterministic vertex/ray sampling and may miss pathological concave or open meshes.",
            "AABB overlap depth/volume are prioritisation proxies, not true penetration depth or swept volume.",
            "Allowed contacts are semantic authoring policy, not a physics or comfort judgement; visual review remains required.",
            "The tool audits weapon versus candidate render geometry only. It does not test suit self-intersection, cloth, Unity colliders, skin quality, or runtime retargeting.",
        ],
    }
    if args.include_instances:
        report["contact_instances"] = contact_instances
    return report


def write_text_report(report: dict[str, object], path: Path) -> None:
    lines = [
        "Aegis Vanguard weapon/suit clearance gate",
        "=" * 43,
        f"Status: {report['status']}",
        f"Candidate: {report['candidate_blend']}",
        f"SHA-256 preserved: {report['candidate_blend_preserved']}",
        f"Blender: {report['blender_version']}",
        f"Sampling: {report['sample_mode']}",
        f"Actions: {report['action_count']}/{EXPECTED_ACTION_COUNT}",
        f"Sampled frames: {report['sampled_frame_count']}",
        f"Allowed contact instances: {report['allowed_contact_instances']}",
        f"Forbidden intersection instances: {report['forbidden_intersection_instances']}",
        f"Forbidden object-pair groups: {report['forbidden_object_pair_groups']}",
        "",
        "Action results",
        "--------------",
    ]
    for action in report["actions"]:
        lines.append(
            f"{action['status']:4} {action['action']}: "
            f"{action['sample_count']} frames, "
            f"{action['allowed_contact_instances']} allowed, "
            f"{action['forbidden_intersection_instances']} forbidden"
        )

    lines.extend(["", "Forbidden groups (highest recurrence first)", "-------------------------------------------"])
    groups = report["forbidden_groups"]
    if not groups:
        lines.append("None.")
    visible_group_limit = 50
    for index, group in enumerate(groups[:visible_group_limit], start=1):
        action_frames = "; ".join(
            f"{action}={','.join(str(frame) for frame in frames)}"
            for action, frames in group["actions_and_frames"].items()
        )
        lines.extend([
            f"{index}. {group['weapon_object']} x {group['suit_object']}",
            f"   instances={group['instance_count']}; max triangle pairs={group['max_triangle_pair_count']}; "
            f"AABB min-axis proxy={group['max_aabb_overlap_min_axis_m']:.6f} m",
            f"   {group['reason']}",
            f"   {action_frames}",
        ])
    if len(groups) > visible_group_limit:
        lines.append(
            f"... {len(groups) - visible_group_limit} more grouped failures are retained in JSON."
        )

    lines.extend(["", "Allowed contact groups", "----------------------"])
    allowed_groups = report["allowed_groups"]
    if not allowed_groups:
        lines.append("None.")
    for group in allowed_groups:
        lines.append(
            f"- {group['classification']}: {group['weapon_object']} x "
            f"{group['suit_object']} ({group['instance_count']} instances)"
        )

    lines.extend(["", "Limitations", "-----------"])
    lines.extend(f"- {item}" for item in report["limitations"])
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    if bpy.app.version < (5, 2, 0):
        raise RuntimeError("Aegis weapon-clearance validation requires Blender 5.2+.")
    args = parse_args()
    report = audit(args)
    output_dir = args.output_dir
    if not output_dir.is_absolute():
        output_dir = (ROOT / output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    label = args.label or (Path(str(report["candidate_blend"])).stem + "_weapon_clearance")
    json_path = output_dir / f"{label}.json"
    text_path = output_dir / f"{label}.txt"
    json_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    write_text_report(report, text_path)
    print(json.dumps({
        "status": report["status"],
        "json_report": str(json_path),
        "text_report": str(text_path),
        "forbidden_intersection_instances": report["forbidden_intersection_instances"],
        "forbidden_object_pair_groups": report["forbidden_object_pair_groups"],
    }, indent=2), flush=True)
    if args.strict and report["status"] != "PASS":
        raise RuntimeError(
            "Weapon/suit clearance gate failed; reports were written before exit."
        )


if __name__ == "__main__":
    main()
