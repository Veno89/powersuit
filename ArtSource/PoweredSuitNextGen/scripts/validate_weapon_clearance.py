"""Audit Aegis candidate/rifle intersections at authored action keyframes.

This is a read-only validation tool.  It never saves the open .blend, exports an
FBX, or changes an active Unity asset.  Run it against a locally generated
Candidate003/004 blend and it will write a machine-readable JSON report plus a
compact, actionable text summary.

Blender 5.2 example (from the repository root)::

    blender --background \
      --python-exit-code 1 \
      ArtSource/PoweredSuitNextGen/candidates/aegis_vanguard_candidate_v004.blend \
      --python ArtSource/PoweredSuitNextGen/scripts/validate_weapon_clearance.py

Use Blender's ``--python-exit-code 1`` and add ``-- --strict`` to make
forbidden intersections fail the Blender process
after both reports have been written.  Add ``-- --all-frames`` for an integer
frame sweep rather than the default authored-keyframe sweep.  The default
``--geometry-source visible`` audits the actual rendered candidate.  The
explicit ``--geometry-source proxy`` mode is only a directional diagnostic for
consolidated candidates and is never production-clearance proof.

Repeat ``--action PS_ActionName`` to audit an exact subset.  Use
``--frame-step 0.5`` for inclusive dense subframe sampling; it is mutually
exclusive with ``--all-frames`` and accepts only finite steps in ``(0, 1]``.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Mapping

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore
from mathutils.bvhtree import BVHTree  # type: ignore


ROOT = Path(__file__).resolve().parents[3]
SCRIPT_ROOT = Path(__file__).resolve().parent
PIPELINE_SCRIPTS = ROOT / "ArtSource" / "PoweredSuit" / "scripts"
if str(SCRIPT_ROOT) not in sys.path:
    sys.path.insert(0, str(SCRIPT_ROOT))
if str(PIPELINE_SCRIPTS) not in sys.path:
    sys.path.insert(0, str(PIPELINE_SCRIPTS))

from powersuit_pipeline_common import activate_action  # type: ignore  # noqa: E402
from clearance_face_policy import (  # noqa: E402
    MANIFEST_SCHEMA,
    MANIFEST_TEXT_NAME,
    POLICY_VERSION,
    SEMANTIC_SCHEMA,
    SUIT_ATTRIBUTE,
    SUIT_ZONE_NAMES,
    WEAPON_ATTRIBUTE,
    WEAPON_ZONE_NAMES,
    canonical_json_bytes,
    canonical_sha256,
    classify_face_contact,
    evaluated_geometry_sha256,
    semantic_counts,
    topology_semantics_sha256,
    validate_manifest,
)
from clearance_sampling import (  # noqa: E402
    SamplingError,
    inclusive_frame_samples,
    sampling_mode,
    select_action_names,
    validate_frame_step,
)


ARMATURE_NAME = "PowerSuit_Armature"
EXPECTED_ACTION_COUNT = 24
REPORT_ROOT = (
    ROOT / "ArtSource" / "PoweredSuitNextGen" / "validation" / "weapon_clearance"
)
CANDIDATE_PROPERTY = "aegis_vanguard_candidate"
RUNTIME_ANCHOR_PROPERTY = "aegis_runtime_anchor"
CLEARANCE_PROXY_PROPERTY = "aegis_clearance_proxy"
RIFLE_PREFIX = "Rifle_"
WEAPON_V2_ROLE_PROPERTY = "weapon_v2_role"
WEAPON_V2_LOD_PROPERTY = "weapon_v2_lod"
ASSET_ROLE_PROPERTY = "ps_clearance_asset_role"
POLICY_PROPERTY = "ps_clearance_policy_version"
SEMANTIC_SCHEMA_PROPERTY = "ps_clearance_semantic_schema"
MANIFEST_HASH_PROPERTY = "ps_clearance_manifest_sha256"
EXPECTED_FACE_COUNT_PROPERTY = "ps_clearance_expected_face_count"
TOPOLOGY_HASH_PROPERTY = "ps_clearance_topology_sha256"
MISSING_SEMANTIC_ID = -1
BVH_EPSILON_M = 1.0e-6
AABB_EPSILON_M = 1.0e-6
CONTAINMENT_RAY_EPSILON_M = 1.0e-5
MAX_CONTAINMENT_RAY_STEPS = 256
MAX_DEPTH_VERTICES_PER_SIDE = 2048
CONTACT_TOLERANCE_M = 0.0

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
    triangle_semantic_ids: tuple[int, ...]
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
        "--action",
        dest="action_filters",
        action="append",
        default=None,
        metavar="PS_ACTION_NAME",
        help=(
            "Audit one exact action name. Repeat to select multiple actions; "
            "unknown and duplicate names fail closed."
        ),
    )
    parser.add_argument(
        "--frame-step",
        type=float,
        default=None,
        metavar="FRAMES",
        help=(
            "Inclusive dense sampling step in (0, 1]; mutually exclusive with "
            "--all-frames."
        ),
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
    parser.add_argument(
        "--geometry-source",
        choices=("visible", "proxy"),
        default="visible",
        help=(
            "Suit collision geometry. 'visible' audits the actual render meshes "
            "(default); 'proxy' requires explicitly tagged diagnostic proxies."
        ),
    )
    return parser.parse_args(argv)


def candidate_objects(geometry_source: str) -> list[bpy.types.Object]:
    proxies = [
        obj
        for obj in bpy.data.objects
        if bool(obj.get(CLEARANCE_PROXY_PROPERTY, False))
        and obj.type in {"MESH", "CURVE", "SURFACE", "FONT"}
    ]
    if geometry_source == "proxy":
        if not proxies:
            raise RuntimeError(
                "--geometry-source proxy requested, but no explicitly tagged "
                "clearance proxies exist in the candidate."
            )
        return sorted(proxies, key=lambda item: item.name)
    result = [
        obj
        for obj in bpy.data.objects
        if bool(obj.get(CANDIDATE_PROPERTY, False))
        and not bool(obj.get(CLEARANCE_PROXY_PROPERTY, False))
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
    # Candidate006 and later declare the production weapon renderer explicitly.
    # Prefer only LOD0 from that contract so retained legacy/source pieces and
    # generated lower LODs cannot silently enter the canonical audit.
    weapon_v2 = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH"
        and str(obj.get(WEAPON_V2_ROLE_PROPERTY, "")) in {"rifle", "optic"}
        and obj.get(WEAPON_V2_LOD_PROPERTY, -1) == 0
    ]
    if weapon_v2:
        return sorted(weapon_v2, key=lambda item: item.name)
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
    asset_role: str | None,
) -> LocalGeometry:
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
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
        semantic_attribute_name = (
            SUIT_ATTRIBUTE if asset_role == "suit"
            else WEAPON_ATTRIBUTE if asset_role == "weapon"
            else None
        )
        semantic_attribute = (
            mesh.attributes.get(semantic_attribute_name)
            if semantic_attribute_name is not None
            else None
        )
        if (
            semantic_attribute is None
            or semantic_attribute.domain != "FACE"
            or semantic_attribute.data_type != "INT"
            or len(semantic_attribute.data) != len(mesh.polygons)
        ):
            triangle_semantic_ids = (MISSING_SEMANTIC_ID,) * len(triangles)
        else:
            triangle_semantic_ids = tuple(
                int(semantic_attribute.data[triangle.polygon_index].value)
                for triangle in mesh.loop_triangles
            )
        minimum = Vector(tuple(min(vertex[axis] for vertex in vertices) for axis in range(3)))
        maximum = Vector(tuple(max(vertex[axis] for vertex in vertices) for axis in range(3)))
        return LocalGeometry(
            vertices,
            triangles,
            triangle_semantic_ids,
            minimum,
            maximum,
        )
    finally:
        evaluated.to_mesh_clear()


def load_clearance_manifest() -> tuple[dict[str, object] | None, str, list[str]]:
    """Load and validate the canonical embedded manifest without mutating it."""

    text = bpy.data.texts.get(MANIFEST_TEXT_NAME)
    if text is None:
        return None, "", [f"Missing embedded Blender text '{MANIFEST_TEXT_NAME}'."]
    raw = text.as_string()
    try:
        parsed = json.loads(raw)
    except (TypeError, json.JSONDecodeError) as error:
        return None, "", [f"Clearance manifest is not valid JSON: {error}"]
    if not isinstance(parsed, dict):
        return None, "", ["Clearance manifest root must be a JSON object."]
    canonical = canonical_json_bytes(parsed)
    errors = validate_manifest(parsed)
    if raw.encode("utf-8") != canonical:
        errors.append(
            "Embedded clearance manifest must use canonical compact/sorted JSON bytes."
        )
    return parsed, hashlib.sha256(canonical).hexdigest(), errors


def validate_action_windows(
    manifest: Mapping[str, object] | None,
    actions: Iterable[bpy.types.Action],
) -> list[str]:
    if manifest is None:
        return []
    action_ranges = {
        action.name: (float(action.frame_range[0]), float(action.frame_range[1]))
        for action in actions
    }
    errors: list[str] = []
    windows_by_key = manifest.get("contact_windows")
    if not isinstance(windows_by_key, Mapping):
        return errors
    for contact_key, windows in windows_by_key.items():
        if not isinstance(windows, list):
            continue
        for index, window in enumerate(windows):
            if not isinstance(window, Mapping):
                continue
            action_name = window.get("action")
            if not isinstance(action_name, str) or action_name not in action_ranges:
                errors.append(
                    f"contact_windows.{contact_key}[{index}] references a missing action."
                )
                continue
            start = window.get("start")
            end = window.get("end")
            if not isinstance(start, (int, float)) or not isinstance(end, (int, float)):
                continue
            action_start, action_end = action_ranges[action_name]
            if float(start) < action_start or float(end) > action_end:
                errors.append(
                    f"contact_windows.{contact_key}[{index}] escapes the authored "
                    f"{action_name} range {action_start:g}-{action_end:g}."
                )
    return errors


def clearance_metadata_evidence(
    suit_objects: list[bpy.types.Object],
    weapon_objects: list[bpy.types.Object],
    depsgraph: bpy.types.Depsgraph,
) -> tuple[dict[str, object] | None, str, list[dict[str, object]], list[str]]:
    """Verify manifest, object properties, evaluated topology, and face coverage."""

    manifest, manifest_hash, errors = load_clearance_manifest()
    raw_entries = (manifest or {}).get("objects", [])
    if not isinstance(raw_entries, list):
        raw_entries = []
    entries = {
        str(entry.get("name")): entry
        for entry in raw_entries
        if isinstance(entry, Mapping)
    }
    evidence: list[dict[str, object]] = []
    aggregate_ids = {"suit": set(), "weapon": set()}
    for asset_role, objects in (("suit", suit_objects), ("weapon", weapon_objects)):
        expected_attribute = SUIT_ATTRIBUTE if asset_role == "suit" else WEAPON_ATTRIBUTE
        known_ids = set(SUIT_ZONE_NAMES if asset_role == "suit" else WEAPON_ZONE_NAMES)
        for obj in objects:
            geometry = evaluated_local_geometry(obj, depsgraph, asset_role)
            topology_hash = topology_semantics_sha256(
                geometry.triangles, geometry.triangle_semantic_ids
            )
            geometry_hash = evaluated_geometry_sha256(
                geometry.vertices, geometry.triangles, geometry.triangle_semantic_ids
            )
            counts = semantic_counts(geometry.triangle_semantic_ids)
            object_errors: list[str] = []
            entry = entries.get(obj.name)
            if entry is None:
                object_errors.append("Object is absent from the clearance manifest.")
            if str(obj.get(ASSET_ROLE_PROPERTY, "")) != asset_role:
                object_errors.append(f"{ASSET_ROLE_PROPERTY} must equal {asset_role}.")
            if str(obj.get(POLICY_PROPERTY, "")) != POLICY_VERSION:
                object_errors.append(f"{POLICY_PROPERTY} does not match {POLICY_VERSION}.")
            if str(obj.get(SEMANTIC_SCHEMA_PROPERTY, "")) != SEMANTIC_SCHEMA:
                object_errors.append(
                    f"{SEMANTIC_SCHEMA_PROPERTY} does not match {SEMANTIC_SCHEMA}."
                )
            if str(obj.get(MANIFEST_HASH_PROPERTY, "")) != manifest_hash:
                object_errors.append(f"{MANIFEST_HASH_PROPERTY} does not match manifest.")
            expected_face_count = obj.get(EXPECTED_FACE_COUNT_PROPERTY, None)
            if (
                not isinstance(expected_face_count, int)
                or isinstance(expected_face_count, bool)
                or expected_face_count != len(geometry.triangles)
            ):
                object_errors.append(
                    f"{EXPECTED_FACE_COUNT_PROPERTY} does not match evaluated faces."
                )
            if str(obj.get(TOPOLOGY_HASH_PROPERTY, "")) != topology_hash:
                object_errors.append(
                    f"{TOPOLOGY_HASH_PROPERTY} does not match evaluated topology/semantics."
                )
            unknown_ids = sorted(set(geometry.triangle_semantic_ids) - known_ids)
            if unknown_ids:
                object_errors.append(
                    f"{expected_attribute} contains unknown/missing IDs: {unknown_ids}."
                )
            aggregate_ids[asset_role].update(geometry.triangle_semantic_ids)
            if entry is not None:
                comparisons = {
                    "asset_role": asset_role,
                    "semantic_attribute": expected_attribute,
                    "face_count": len(geometry.triangles),
                    "topology_sha256": topology_hash,
                    "semantic_counts": counts,
                }
                for key, actual in comparisons.items():
                    if entry.get(key) != actual:
                        object_errors.append(
                            f"Manifest field {key} does not match evaluated object evidence."
                        )
            errors.extend(f"{obj.name}: {message}" for message in object_errors)
            evidence.append({
                "name": obj.name,
                "asset_role": asset_role,
                "semantic_attribute": expected_attribute,
                "evaluated_vertex_count": len(geometry.vertices),
                "evaluated_triangle_count": len(geometry.triangles),
                "semantic_counts": counts,
                "topology_semantics_sha256": topology_hash,
                "evaluated_geometry_sha256": geometry_hash,
                "object_manifest_sha256": str(obj.get(MANIFEST_HASH_PROPERTY, "")),
                "metadata_status": "PASS" if not object_errors else "FAIL",
                "metadata_errors": object_errors,
            })

    # Every intentional contact family must have actual face coverage in LOD0.
    missing_suit_ids = sorted(set(SUIT_ZONE_NAMES) - {0} - aggregate_ids["suit"])
    missing_weapon_ids = sorted(set(WEAPON_ZONE_NAMES) - {0} - aggregate_ids["weapon"])
    if missing_suit_ids:
        errors.append(f"Visible suit geometry lacks required semantic IDs: {missing_suit_ids}.")
    if missing_weapon_ids:
        errors.append(
            f"Visible weapon geometry lacks required semantic IDs: {missing_weapon_ids}."
        )
    return manifest, manifest_hash, evidence, sorted(set(errors))


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


def _bounded_indices(indices: Iterable[int]) -> tuple[int, ...]:
    ordered = sorted(set(int(index) for index in indices))
    if len(ordered) <= MAX_DEPTH_VERTICES_PER_SIDE:
        return tuple(ordered)
    last = len(ordered) - 1
    return tuple(
        ordered[round(sample * last / (MAX_DEPTH_VERTICES_PER_SIDE - 1))]
        for sample in range(MAX_DEPTH_VERTICES_PER_SIDE)
    )


def contact_depth_metric(
    weapon: WorldGeometry,
    suit: WorldGeometry,
    triangle_pairs: Iterable[tuple[int, int]],
    containment: str | None = None,
) -> dict[str, object]:
    """Measure sampled interior-vertex distance to the opposing real surface.

    This is a geometric distance in metres, not an AABB dimension.  It is
    recorded before the (currently zero) contact tolerance is applied.  A
    surface crossing can legitimately report zero when no sampled triangle
    vertex lies inside the opposing closed surface.
    """

    pairs = tuple((int(first), int(second)) for first, second in triangle_pairs)
    if containment == "weapon_inside_suit":
        weapon_indices = _bounded_indices(range(len(weapon.vertices)))
        suit_indices: tuple[int, ...] = ()
    elif containment == "suit_inside_weapon":
        weapon_indices = ()
        suit_indices = _bounded_indices(range(len(suit.vertices)))
    else:
        weapon_indices = _bounded_indices(
            vertex_index
            for weapon_triangle, _suit_triangle in pairs
            for vertex_index in weapon.triangles[weapon_triangle]
        )
        suit_indices = _bounded_indices(
            vertex_index
            for _weapon_triangle, suit_triangle in pairs
            for vertex_index in suit.triangles[suit_triangle]
        )

    maximum_depth = 0.0
    interior_samples = 0
    for vertices, indices, opposing_bvh in (
        (weapon.vertices, weapon_indices, suit.bvh),
        (suit.vertices, suit_indices, weapon.bvh),
    ):
        for index in indices:
            point = vertices[index]
            if not point_inside_closed_mesh(point, opposing_bvh):
                continue
            nearest = opposing_bvh.find_nearest(point)
            if nearest is None or nearest[0] is None:
                continue
            interior_samples += 1
            distance = nearest[3]
            if distance is None:
                distance = (point - nearest[0]).length
            maximum_depth = max(maximum_depth, float(distance))
    return {
        "metric": "sampled_interior_vertex_to_opposing_surface_distance",
        "pre_tolerance_max_depth_m": round(maximum_depth, 9),
        "contact_tolerance_m": CONTACT_TOLERANCE_M,
        "sampled_candidate_vertices": len(weapon_indices) + len(suit_indices),
        "sampled_interior_vertices": interior_samples,
    }


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


def sampled_frames(
    action: bpy.types.Action,
    all_frames: bool,
    frame_step: float | None = None,
) -> list[float]:
    if frame_step is not None:
        start, end = action.frame_range
        return inclusive_frame_samples(start, end, frame_step)
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
    suit_objects = candidate_objects(args.geometry_source)
    weapon_objects = rifle_objects()
    if not suit_objects:
        raise RuntimeError("No candidate collision objects were found.")
    if not weapon_objects:
        raise RuntimeError("No Rifle_* mesh objects were found.")

    available_actions = sorted(
        [action for action in bpy.data.actions if action.name.startswith("PS_")],
        key=lambda item: item.name,
    )
    if len(available_actions) != EXPECTED_ACTION_COUNT:
        raise RuntimeError(
            f"Expected exactly {EXPECTED_ACTION_COUNT} PS_* actions; "
            f"found {len(available_actions)}."
        )
    try:
        resolved_sample_mode = sampling_mode(
            all_frames=args.all_frames,
            frame_step=args.frame_step,
        )
        resolved_frame_step = (
            validate_frame_step(args.frame_step)
            if args.frame_step is not None
            else None
        )
        selected_action_names = select_action_names(
            [action.name for action in available_actions],
            args.action_filters,
        )
    except SamplingError as error:
        raise RuntimeError(f"Invalid clearance sampling request: {error}") from error
    available_actions_by_name = {action.name: action for action in available_actions}
    actions = [available_actions_by_name[name] for name in selected_action_names]

    state = remember_state(armature)
    armature.data.pose_position = "POSE"
    depsgraph = bpy.context.evaluated_depsgraph_get()
    all_collision_objects = [*suit_objects, *weapon_objects]
    asset_roles = {
        **{obj.name: "suit" for obj in suit_objects},
        **{obj.name: "weapon" for obj in weapon_objects},
    }
    if args.geometry_source == "visible":
        (
            clearance_manifest,
            clearance_manifest_hash,
            evaluated_object_evidence,
            metadata_errors,
        ) = clearance_metadata_evidence(suit_objects, weapon_objects, depsgraph)
        metadata_errors.extend(
            validate_action_windows(clearance_manifest, available_actions)
        )
        metadata_errors = sorted(set(metadata_errors))
        contact_windows = (
            clearance_manifest.get("contact_windows", {})
            if clearance_manifest is not None
            else {}
        )
        metadata_status = "PASS" if not metadata_errors else "FAIL"
    else:
        clearance_manifest = None
        clearance_manifest_hash = ""
        evaluated_object_evidence = []
        metadata_errors = []
        contact_windows = {}
        metadata_status = "NOT_APPLICABLE_PROXY_DIAGNOSTIC"
    dynamic = {obj.name: object_is_dynamic(obj) for obj in all_collision_objects}
    static_geometry: dict[str, LocalGeometry] = {}
    for obj in all_collision_objects:
        if not dynamic[obj.name]:
            static_geometry[obj.name] = evaluated_local_geometry(
                obj,
                depsgraph,
                asset_roles[obj.name] if args.geometry_source == "visible" else None,
            )

    contact_instances: list[dict[str, object]] = []
    action_reports: list[dict[str, object]] = []
    try:
        total_actions = len(actions)
        for action_index, action in enumerate(actions, start=1):
            frames = sampled_frames(
                action,
                args.all_frames,
                resolved_frame_step,
            )
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
                        local_geometry[obj.name] = evaluated_local_geometry(
                            obj,
                            depsgraph,
                            asset_roles[obj.name]
                            if args.geometry_source == "visible"
                            else None,
                        )

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
                        overlap_axes, overlap_volume = aabb
                        if args.geometry_source == "visible":
                            semantic_buckets: dict[
                                tuple[int, int], list[tuple[int, int]]
                            ] = defaultdict(list)
                            if containment is not None:
                                semantic_buckets[(MISSING_SEMANTIC_ID, MISSING_SEMANTIC_ID)] = []
                            else:
                                weapon_local = local_geometry[weapon.name]
                                suit_local = local_geometry[suit.name]
                                for weapon_triangle, suit_triangle in triangle_pairs:
                                    semantic_buckets[(
                                        suit_local.triangle_semantic_ids[suit_triangle],
                                        weapon_local.triangle_semantic_ids[weapon_triangle],
                                    )].append((weapon_triangle, suit_triangle))
                        else:
                            semantic_buckets = {
                                (MISSING_SEMANTIC_ID, MISSING_SEMANTIC_ID): list(triangle_pairs)
                            }

                        for (suit_zone_id, weapon_zone_id), bucket_pairs in sorted(
                            semantic_buckets.items()
                        ):
                            if args.geometry_source == "visible":
                                decision = classify_face_contact(
                                    action.name,
                                    frame,
                                    suit_zone_id,
                                    weapon_zone_id,
                                    contact_windows
                                    if isinstance(contact_windows, Mapping)
                                    else None,
                                    containment=containment is not None,
                                    metadata_valid=metadata_status == "PASS",
                                )
                                classification = decision.classification
                                allowed = decision.allowed
                                reason = decision.reason
                                contact_key = decision.contact_key
                                matched_window = decision.matched_window
                            else:
                                classification, allowed, reason = classify_contact(
                                    action.name, suit, weapon
                                )
                                contact_key = None
                                matched_window = None

                            depth = contact_depth_metric(
                                weapon_world,
                                suit_world,
                                bucket_pairs,
                                containment,
                            )
                            contact_instances.append({
                                "action": action.name,
                                "frame": compact_frame(frame),
                                "suit_object": suit.name,
                                "suit_bone_or_zone": parent_bone(suit),
                                "suit_face_semantic_id": (
                                    suit_zone_id
                                    if args.geometry_source == "visible"
                                    else None
                                ),
                                "suit_face_semantic": SUIT_ZONE_NAMES.get(
                                    suit_zone_id, "unknown_or_not_applicable"
                                ),
                                "weapon_object": weapon.name,
                                "weapon_component_role": str(
                                    weapon.get("ps_weapon_component_role", "")
                                ),
                                "weapon_contact_surface_role": str(
                                    weapon.get("ps_weapon_contact_surface_role", "")
                                ),
                                "weapon_face_semantic_id": (
                                    weapon_zone_id
                                    if args.geometry_source == "visible"
                                    else None
                                ),
                                "weapon_face_semantic": WEAPON_ZONE_NAMES.get(
                                    weapon_zone_id, "unknown_or_not_applicable"
                                ),
                                "policy_version": (
                                    POLICY_VERSION
                                    if args.geometry_source == "visible"
                                    else "legacy_proxy_object_policy"
                                ),
                                "contact_key": contact_key,
                                "matched_action_frame_window": matched_window,
                                "classification": classification,
                                "allowed": allowed,
                                "reason": reason,
                                "detection_method": method,
                                "triangle_pair_count": len(bucket_pairs),
                                "contact_depth": depth,
                                "aabb_overlap_axes_m": [
                                    round(float(value), 6) for value in overlap_axes
                                ],
                                "aabb_overlap_min_axis_m": round(
                                    float(min(overlap_axes)), 6
                                ),
                                "aabb_overlap_volume_m3": round(
                                    float(overlap_volume), 9
                                ),
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
                "status": (
                    "PASS"
                    if action_forbidden == 0
                    and (
                        args.geometry_source == "proxy" or metadata_status == "PASS"
                    )
                    else "FAIL"
                ),
            })
    finally:
        restore_state(armature, state)

    forbidden = [item for item in contact_instances if not bool(item["allowed"])]
    allowed = [item for item in contact_instances if bool(item["allowed"])]

    def group_instances(
        source: list[dict[str, object]],
    ) -> list[dict[str, object]]:
        grouped: dict[
            tuple[str, str, str, str, str], list[dict[str, object]]
        ] = defaultdict(list)
        for instance in source:
            key = (
                str(instance["weapon_object"]),
                str(instance["suit_object"]),
                str(instance["classification"]),
                str(instance["suit_face_semantic_id"]),
                str(instance["weapon_face_semantic_id"]),
            )
            grouped[key].append(instance)
        result: list[dict[str, object]] = []
        for (
            weapon_name,
            suit_name,
            classification,
            suit_semantic_id,
            weapon_semantic_id,
        ), instances in grouped.items():
            action_frames: dict[str, list[int | float]] = defaultdict(list)
            for instance in instances:
                action_frames[str(instance["action"])].append(instance["frame"])
            result.append({
                "weapon_object": weapon_name,
                "suit_object": suit_name,
                "classification": classification,
                "suit_face_semantic_id": (
                    None if suit_semantic_id == "None" else int(suit_semantic_id)
                ),
                "suit_face_semantic": str(instances[0]["suit_face_semantic"]),
                "weapon_face_semantic_id": (
                    None if weapon_semantic_id == "None" else int(weapon_semantic_id)
                ),
                "weapon_face_semantic": str(instances[0]["weapon_face_semantic"]),
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
                "max_pre_tolerance_contact_depth_m": max(
                    float(instance["contact_depth"]["pre_tolerance_max_depth_m"])
                    for instance in instances
                ),
                "detection_methods": sorted({
                    str(instance["detection_method"]) for instance in instances
                }),
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

    visible_metadata_valid = metadata_status == "PASS"
    gate_passed = not forbidden and (
        args.geometry_source == "proxy" or visible_metadata_valid
    )
    try:
        reported_blend_path = blend_path.relative_to(ROOT).as_posix()
    except ValueError:
        reported_blend_path = blend_path.as_posix()
    report: dict[str, object] = {
        "schema_version": 3,
        "gate": "AEGIS_WEAPON_SUIT_CLEARANCE",
        "status": "PASS" if gate_passed else "FAIL",
        "deterministic_report": True,
        "blender_version": ".".join(str(value) for value in bpy.app.version),
        "candidate_blend": reported_blend_path,
        "candidate_blend_sha256_before": source_hash_before,
        "candidate_blend_sha256_after": source_hash_after,
        "candidate_blend_preserved": source_hash_before == source_hash_after,
        "sample_mode": resolved_sample_mode,
        "sampling": {
            "mode": resolved_sample_mode,
            "action_filters": list(args.action_filters or []),
            "selected_action_names": selected_action_names,
            "frame_step": resolved_frame_step,
            "inclusive_action_endpoints": resolved_frame_step is not None,
            "sampled_frame_count": sum(
                int(item["sample_count"]) for item in action_reports
            ),
        },
        "collision_geometry_source": args.geometry_source,
        "collision_geometry_scope": (
            "actual visible candidate render geometry"
            if args.geometry_source == "visible"
            else "hidden tagged diagnostic proxies; not visible-geometry clearance proof"
        ),
        "promotion_eligible_geometry_source": args.geometry_source == "visible",
        "armature": armature.name,
        "candidate_collision_objects": len(suit_objects),
        "candidate_collision_object_names": [obj.name for obj in suit_objects],
        "rifle_collision_objects": len(weapon_objects),
        "rifle_collision_object_names": [obj.name for obj in weapon_objects],
        "dynamic_collision_objects": sorted(
            name for name, is_dynamic in dynamic.items() if is_dynamic
        ),
        "available_action_count": len(available_actions),
        "action_count": len(actions),
        "sampled_frame_count": sum(int(item["sample_count"]) for item in action_reports),
        "allowed_contact_instances": len(allowed),
        "forbidden_intersection_instances": len(forbidden),
        "forbidden_object_pair_groups": len(forbidden_groups),
        "contact_classification_counts": dict(sorted(classification_counts.items())),
        "clearance_metadata": {
            "status": metadata_status,
            "failure_is_hard_error": args.geometry_source == "visible",
            "policy_version": (
                POLICY_VERSION if args.geometry_source == "visible"
                else "legacy_proxy_object_policy"
            ),
            "semantic_schema": (
                SEMANTIC_SCHEMA if args.geometry_source == "visible" else None
            ),
            "manifest_schema": (
                MANIFEST_SCHEMA if args.geometry_source == "visible" else None
            ),
            "manifest_text_name": (
                MANIFEST_TEXT_NAME if args.geometry_source == "visible" else None
            ),
            "manifest_sha256": clearance_manifest_hash or None,
            "manifest": clearance_manifest,
            "errors": metadata_errors,
            "evaluated_objects": evaluated_object_evidence,
        },
        "contact_policy": {
            "classification_domain": (
                "actual intersecting evaluated triangle faces"
                if args.geometry_source == "visible"
                else "legacy object/bone diagnostic semantics"
            ),
            "allowed": (
                [
                    "face IDs 101/201: right primary hand / primary grip, only in an explicit manifest window",
                    "face IDs 102/202: left support hand / support grip, only in an explicit manifest window",
                    "face IDs 103/203: right shoulder pocket / buttpad, only in an explicit ready-family manifest window",
                    "face IDs 104/204: left manipulation hand / magazine grasp, only in PS_Reload frames 25-75",
                    "face IDs 105/205: right manipulation hand / bolt handle, only in PS_BoltCycle frames 4-16",
                ]
                if args.geometry_source == "visible"
                else [
                    "legacy matching grip-hand and buttpad-shoulder object contacts for localisation only"
                ]
            ),
            "forbidden": [
                "all incompatible, unknown, missing, or out-of-window face pairs",
                "all containment, including inside otherwise compatible contact zones",
                "all armor contact while stowed or during draw/sheathe",
                "all contacts when policy, face coverage, action/frame, or source-manifest evidence is invalid",
            ],
            "contact_tolerance_m": CONTACT_TOLERANCE_M,
            "contact_depth_metric": (
                "sampled interior-vertex distance to the actual opposing surface; recorded before tolerance"
            ),
            "aabb_metrics_are_penetration_depth": False,
        },
        "actions": action_reports,
        "allowed_groups": allowed_groups,
        "forbidden_groups": forbidden_groups,
        "limitations": [
            "The default sweep samples authored action keyframes. Use --all-frames for integer coverage or --frame-step with exact --action filters for denser subframe evidence; all discrete sampling can still miss collisions between samples.",
            "BVH surface crossings are exact for evaluated render triangles; full containment uses deterministic vertex/ray sampling and may miss pathological concave or open meshes.",
            "The reported contact-depth metric is a real sampled vertex-to-surface distance, but surface crossings with no interior sampled vertex can report zero; AABB values remain prioritisation proxies only.",
            "Allowed contacts are semantic authoring policy, not a physics or comfort judgement; visual review remains required.",
            (
                "Visible mode audits weapon versus the actual evaluated candidate render geometry. "
                "Consolidated meshes without face-level contact tags are intentionally classified conservatively."
                if args.geometry_source == "visible"
                else "Proxy mode audits hidden authoring proxies only. Remeshed or smoothly skinned visible surfaces may differ, so this mode is directional comparison evidence, not a clearance gate."
            ),
            "The tool does not test suit self-intersection, cloth, Unity colliders, skin quality, or runtime retargeting.",
        ],
    }
    if args.include_instances:
        report["contact_instances"] = contact_instances
    report["report_evidence_sha256"] = canonical_sha256(report)
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
        "Action filters: "
        + (
            ", ".join(report["sampling"]["action_filters"])
            if report["sampling"]["action_filters"]
            else "all available actions"
        ),
        f"Frame step: {report['sampling']['frame_step']}",
        f"Collision geometry: {report['collision_geometry_source']} ({report['collision_geometry_scope']})",
        f"Policy metadata: {report['clearance_metadata']['status']}",
        f"Manifest SHA-256: {report['clearance_metadata']['manifest_sha256']}",
        f"Report evidence SHA-256: {report['report_evidence_sha256']}",
        f"Actions sampled: {report['action_count']}/{report['available_action_count']}",
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
            f"   face semantics: suit={group['suit_face_semantic_id']} "
            f"({group['suit_face_semantic']}); weapon={group['weapon_face_semantic_id']} "
            f"({group['weapon_face_semantic']})",
            f"   instances={group['instance_count']}; max triangle pairs={group['max_triangle_pair_count']}; "
            f"real sampled depth={group['max_pre_tolerance_contact_depth_m']:.6f} m; "
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
            f"{group['suit_object']} ({group['instance_count']} instances; "
            f"suit face {group['suit_face_semantic_id']}, "
            f"weapon face {group['weapon_face_semantic_id']})"
        )

    metadata_errors = report["clearance_metadata"]["errors"]
    lines.extend(["", "Metadata/manifest errors", "------------------------"])
    if not metadata_errors:
        lines.append("None.")
    else:
        lines.extend(f"- {item}" for item in metadata_errors)

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
    json_path.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    write_text_report(report, text_path)
    print(json.dumps({
        "status": report["status"],
        "json_report": str(json_path),
        "text_report": str(text_path),
        "forbidden_intersection_instances": report["forbidden_intersection_instances"],
        "forbidden_object_pair_groups": report["forbidden_object_pair_groups"],
    }, indent=2), flush=True)
    if (
        report["collision_geometry_source"] == "visible"
        and report["clearance_metadata"]["status"] != "PASS"
    ):
        raise RuntimeError(
            "Visible clearance metadata/manifest gate failed; reports were "
            "written before exit. Proxy mode remains available for legacy diagnostics."
        )
    if args.strict and report["status"] != "PASS":
        raise RuntimeError(
            "Weapon/suit clearance gate failed; reports were written before exit."
        )


if __name__ == "__main__":
    main()
