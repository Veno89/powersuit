"""Validate a HeroV2 Blender handoff and optionally generate draft LODs.

Run this file through Blender, never plain Python.  The source blend is opened
read-only in practice: generated geometry is saved to a distinct derivative
path beneath this HeroV2 lane and the source SHA-256 is checked afterwards.

Example:
  blender --background --python validate_and_generate_lods.py -- \
    --source ../candidates/aegis_vanguard_candidate_v004.blend \
    --report reports/candidate004_production.json \
    --generate-lods --output-blend derivatives/candidate004_lods.blend
"""

from __future__ import annotations

import argparse
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable

import bpy  # type: ignore
from mathutils.kdtree import KDTree  # type: ignore


LANE_ROOT = Path(__file__).resolve().parent
REPOSITORY_ROOT = LANE_ROOT.parents[2]
sys.path.insert(0, str(LANE_ROOT))

from hero_v2_contract import (  # noqa: E402
    ContractError,
    assert_derivative_path,
    evaluate_triangle_budget,
    infer_role,
    load_profile,
    sha256_file,
    summarise_issues,
    triangle_budget,
    write_canonical_json,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument(
        "--profile", type=Path, default=LANE_ROOT / "production_profile.json"
    )
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--generate-lods", action="store_true")
    parser.add_argument("--output-blend", type=Path)
    parser.add_argument(
        "--allow-fallback-property",
        action="store_true",
        help="Allow Candidate003-style property selection when HeroV2_LOD0 is absent.",
    )
    parser.add_argument(
        "--require-lods",
        action="store_true",
        help="Treat missing LOD1-LOD3 collections as hard failures.",
    )
    parser.add_argument(
        "--soft-fail",
        action="store_true",
        help="Write failures to the report but return exit code zero (baseline audits only).",
    )
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    return parser.parse_args(argv)


def absolute(path: Path) -> Path:
    if path.is_absolute():
        return path.resolve()
    return (REPOSITORY_ROOT / path).resolve()


def report_path(path: Path) -> str:
    resolved = path.resolve()
    try:
        return resolved.relative_to(REPOSITORY_ROOT).as_posix()
    except ValueError:
        return resolved.as_posix()


def recursive_collection_objects(collection: bpy.types.Collection) -> list[bpy.types.Object]:
    found: dict[str, bpy.types.Object] = {}

    def visit(current: bpy.types.Collection) -> None:
        for obj in current.objects:
            found[obj.name_full] = obj
        for child in current.children:
            visit(child)

    visit(collection)
    return [found[name] for name in sorted(found)]


def select_lod_objects(
    lod: int, profile: dict[str, Any], allow_fallback: bool
) -> tuple[list[bpy.types.Object], str]:
    base_name = profile["selection"]["lod0_collection"]
    collection_name = base_name if lod == 0 else base_name.replace("LOD0", f"LOD{lod}")
    collection = bpy.data.collections.get(collection_name)
    if collection is not None:
        collection_objects = recursive_collection_objects(collection)
        unsupported_renderables = [
            obj.name_full
            for obj in collection_objects
            if obj.type in {"CURVE", "SURFACE", "META", "FONT", "VOLUME", "POINTCLOUD", "GREASEPENCIL"}
            and not obj.hide_render
        ]
        if unsupported_renderables:
            raise ContractError(
                f"{collection_name} contains non-MESH renderables; bake them before validation: "
                + ", ".join(unsupported_renderables)
            )
        objects = [obj for obj in collection_objects if obj.type == "MESH"]
        return objects, f"collection:{collection_name}"

    property_matches = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH" and int(obj.get("hero_v2_lod", -1)) == lod
    ]
    if property_matches:
        return sorted(property_matches, key=lambda item: item.name_full), "property:hero_v2_lod"

    if lod == 0 and allow_fallback:
        fallback = profile["selection"]["fallback_property"]
        matches = [
            obj
            for obj in bpy.data.objects
            if obj.type == "MESH" and bool(obj.get(fallback, False))
        ]
        return sorted(matches, key=lambda item: item.name_full), f"fallback:{fallback}"

    return [], f"missing:{collection_name}"


def world_face_area(obj: bpy.types.Object, polygon: bpy.types.MeshPolygon) -> float:
    vertices = polygon.vertices
    if len(vertices) < 3:
        return 0.0
    origin = obj.matrix_world @ obj.data.vertices[vertices[0]].co
    area = 0.0
    for index in range(1, len(vertices) - 1):
        point_a = obj.matrix_world @ obj.data.vertices[vertices[index]].co
        point_b = obj.matrix_world @ obj.data.vertices[vertices[index + 1]].co
        area += (point_a - origin).cross(point_b - origin).length * 0.5
    return area


def uv_face_area(
    polygon: bpy.types.MeshPolygon, uv_layer: bpy.types.MeshUVLoopLayer
) -> tuple[float, bool, int]:
    loop_indices = list(polygon.loop_indices)
    if len(loop_indices) < 3:
        return 0.0, False, 0
    coords = [uv_layer.data[index].uv.copy() for index in loop_indices]
    finite = all(math.isfinite(value) for uv in coords for value in uv)
    if not finite:
        return 0.0, False, len(coords)
    origin = coords[0]
    area = 0.0
    for index in range(1, len(coords) - 1):
        a = coords[index] - origin
        b = coords[index + 1] - origin
        area += abs(a.x * b.y - a.y * b.x) * 0.5
    return area, True, 0


def duplicate_vertex_pairs(mesh: bpy.types.Mesh, epsilon: float) -> int:
    if not mesh.vertices:
        return 0
    tree = KDTree(len(mesh.vertices))
    for vertex in mesh.vertices:
        tree.insert(vertex.co, vertex.index)
    tree.balance()
    pairs = 0
    for vertex in mesh.vertices:
        pairs += sum(
            1
            for _co, other_index, distance in tree.find_range(vertex.co, epsilon)
            if other_index > vertex.index and distance <= epsilon
        )
    return pairs


def object_metrics(
    obj: bpy.types.Object, role: str, profile: dict[str, Any]
) -> dict[str, Any]:
    mesh = obj.data
    mesh.calc_loop_triangles()
    triangle_count = len(mesh.loop_triangles)

    edge_index = {tuple(sorted(edge.vertices)): edge.index for edge in mesh.edges}
    edge_face_uses = [0] * len(mesh.edges)
    face_vertices: set[int] = set()
    for polygon in mesh.polygons:
        face_vertices.update(polygon.vertices)
        for key in polygon.edge_keys:
            index = edge_index.get(tuple(sorted(key)))
            if index is not None:
                edge_face_uses[index] += 1

    boundary_edges = sum(uses == 1 for uses in edge_face_uses)
    non_manifold_edges = sum(uses > 2 for uses in edge_face_uses)
    loose_edges = sum(uses == 0 for uses in edge_face_uses)
    loose_vertices = len(mesh.vertices) - len(face_vertices)
    ngons = sum(len(polygon.vertices) > 4 for polygon in mesh.polygons)

    topology_profile = profile["topology"]
    zero_area_faces = sum(
        world_face_area(obj, polygon) <= topology_profile["zero_area_epsilon_m2"]
        for polygon in mesh.polygons
    )
    degenerate_edges = 0
    for edge in mesh.edges:
        start = obj.matrix_world @ mesh.vertices[edge.vertices[0]].co
        end = obj.matrix_world @ mesh.vertices[edge.vertices[1]].co
        if (end - start).length <= topology_profile["duplicate_position_epsilon_m"]:
            degenerate_edges += 1

    duplicate_pairs = duplicate_vertex_pairs(
        mesh, topology_profile["duplicate_position_epsilon_m"]
    )

    uv_profile = profile["uv"]
    uv_layer = mesh.uv_layers.get(uv_profile["required_map"])
    uv_covered_faces = 0
    uv_zero_area_faces = 0
    invalid_uv_values = 0
    out_of_bounds_loops = 0
    total_uv_area = 0.0
    total_world_area = sum(world_face_area(obj, polygon) for polygon in mesh.polygons)
    if uv_layer is not None:
        epsilon = uv_profile["bounds_epsilon"]
        for polygon in mesh.polygons:
            area, finite, invalid_values = uv_face_area(polygon, uv_layer)
            invalid_uv_values += invalid_values
            total_uv_area += area
            if finite and area > uv_profile["zero_area_epsilon"]:
                uv_covered_faces += 1
            else:
                uv_zero_area_faces += 1
            for loop_index in polygon.loop_indices:
                uv = uv_layer.data[loop_index].uv
                if (
                    uv.x < -epsilon
                    or uv.x > 1.0 + epsilon
                    or uv.y < -epsilon
                    or uv.y > 1.0 + epsilon
                ):
                    out_of_bounds_loops += 1
    else:
        uv_zero_area_faces = len(mesh.polygons)

    face_count = len(mesh.polygons)
    coverage = uv_covered_faces / face_count if face_count else 0.0
    texture_resolution = uv_profile["texture_resolution"][role]
    texel_density = (
        texture_resolution * math.sqrt(total_uv_area / total_world_area) / 100.0
        if total_uv_area > 0.0 and total_world_area > 0.0
        else 0.0
    )

    used_material_indices = sorted({polygon.material_index for polygon in mesh.polygons})
    material_names: list[str] = []
    empty_material_assignments = 0
    for index in used_material_indices:
        if index >= len(obj.material_slots) or obj.material_slots[index].material is None:
            empty_material_assignments += 1
        else:
            material_names.append(obj.material_slots[index].material.name_full)

    scale = obj.scale
    unapplied_scale = any(abs(float(component) - 1.0) > 1e-5 for component in scale)
    negative_determinant = obj.matrix_world.to_3x3().determinant() < 0.0

    authoring_modifiers = sorted(
        f"{modifier.name}:{modifier.type}"
        for modifier in obj.modifiers
        if modifier.type != "ARMATURE"
    )
    runtime_modifiers = sorted(
        f"{modifier.name}:{modifier.type}"
        for modifier in obj.modifiers
        if modifier.type == "ARMATURE"
    )

    return {
        "name": obj.name_full,
        "role": role,
        "vertices": len(mesh.vertices),
        "edges": len(mesh.edges),
        "faces": face_count,
        "triangles": triangle_count,
        "topology": {
            "boundary_edges": boundary_edges,
            "non_manifold_edges": non_manifold_edges,
            "loose_edges": loose_edges,
            "loose_vertices": loose_vertices,
            "ngons": ngons,
            "zero_area_faces": zero_area_faces,
            "degenerate_edges": degenerate_edges,
            "duplicate_vertex_pairs": duplicate_pairs,
            "unapplied_scale": unapplied_scale,
            "negative_transform_determinant": negative_determinant,
            "unapplied_authoring_modifiers": authoring_modifiers,
            "runtime_modifiers": runtime_modifiers,
        },
        "uv": {
            "required_map": uv_profile["required_map"],
            "present": uv_layer is not None,
            "covered_faces": uv_covered_faces,
            "face_coverage": round(coverage, 8),
            "zero_area_faces": uv_zero_area_faces,
            "invalid_values": invalid_uv_values,
            "out_of_bounds_loops": out_of_bounds_loops,
            "summed_uv_area_overlap_unaware": round(total_uv_area, 8),
            "world_surface_area_m2": round(total_world_area, 8),
            "texel_density_px_per_cm": round(texel_density, 4),
        },
        "materials": {
            "slot_count": len(obj.material_slots),
            "used_slot_count": len(used_material_indices),
            "used_materials": sorted(material_names),
            "empty_used_slots": empty_material_assignments,
        },
    }


def issue(
    code: str,
    severity: str,
    message: str,
    actual: Any | None = None,
    limit: Any | None = None,
    offenders: Iterable[str] = (),
) -> dict[str, Any]:
    result: dict[str, Any] = {
        "code": code,
        "severity": severity,
        "message": message,
    }
    if actual is not None:
        result["actual"] = actual
    if limit is not None:
        result["limit"] = limit
    offender_list = sorted(offenders)
    if offender_list:
        result["offenders"] = offender_list[:30]
        if len(offender_list) > 30:
            result["offenders_truncated"] = len(offender_list) - 30
    return result


def threshold_issue(
    metrics: list[dict[str, Any]], field: str, limit: int, code: str
) -> dict[str, Any]:
    offenders = [item["name"] for item in metrics if item["topology"][field] > 0]
    actual = sum(item["topology"][field] for item in metrics)
    severity = "error" if actual > limit else "pass"
    return issue(
        code,
        severity,
        f"Topology metric {field} must not exceed its production limit.",
        actual,
        limit,
        offenders,
    )


def validate_lod(
    lod: int,
    objects: list[bpy.types.Object],
    selection_method: str,
    profile: dict[str, Any],
    require_lod: bool,
) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    issues: list[dict[str, Any]] = []
    if not objects:
        severity = "error" if lod == 0 or require_lod else "warning"
        issues.append(
            issue(
                f"LOD{lod}_MISSING",
                severity,
                f"No mesh objects were found for LOD{lod}.",
            )
        )
        return {
            "lod": lod,
            "selection_method": selection_method,
            "object_count": 0,
            "objects": [],
        }, issues

    role_property = profile["selection"]["role_property"]
    metrics: list[dict[str, Any]] = []
    role_errors: list[str] = []
    for obj in objects:
        try:
            role = infer_role(obj.name_full, obj.get(role_property))
        except ContractError:
            role_errors.append(obj.name_full)
            role = "suit"
        metrics.append(object_metrics(obj, role, profile))

    if role_errors:
        issues.append(
            issue(
                f"LOD{lod}_INVALID_ROLE",
                "error",
                "One or more objects use an unsupported hero_v2_asset role.",
                len(role_errors),
                0,
                role_errors,
            )
        )

    present_roles = sorted({item["role"] for item in metrics})
    if lod == 0:
        missing_roles = sorted(set(profile["selection"]["required_roles"]) - set(present_roles))
        if missing_roles:
            issues.append(
                issue(
                    "LOD0_REQUIRED_ROLE_MISSING",
                    "error",
                    "The LOD0 handoff is missing required geometry roles.",
                    missing_roles,
                )
            )

    topology = profile["topology"]
    topology_fields = {
        "boundary_edges": "max_boundary_edges",
        "non_manifold_edges": "max_non_manifold_edges",
        "loose_edges": "max_loose_edges",
        "loose_vertices": "max_loose_vertices",
        "ngons": "max_ngons",
        "zero_area_faces": "max_zero_area_faces",
        "degenerate_edges": "max_degenerate_edges",
        "duplicate_vertex_pairs": "max_duplicate_vertex_pairs",
    }
    for field, profile_field in topology_fields.items():
        issues.append(
            threshold_issue(
                metrics,
                field,
                topology[profile_field],
                f"LOD{lod}_TOPOLOGY_{field.upper()}",
            )
        )

    transform_offenders = [
        item["name"]
        for item in metrics
        if item["topology"]["unapplied_scale"]
        or item["topology"]["negative_transform_determinant"]
    ]
    issues.append(
        issue(
            f"LOD{lod}_TRANSFORMS",
            "error" if transform_offenders else "pass",
            "Mesh transforms must have unit scale and positive determinant.",
            len(transform_offenders),
            0,
            transform_offenders,
        )
    )

    modifier_offenders = [
        item["name"]
        for item in metrics
        if item["topology"]["unapplied_authoring_modifiers"]
    ]
    issues.append(
        issue(
            f"LOD{lod}_UNAPPLIED_MODIFIERS",
            "warning" if modifier_offenders else "pass",
            "Production handoffs should apply non-runtime modifiers before validation.",
            len(modifier_offenders),
            0,
            modifier_offenders,
        )
    )

    uv_profile = profile["uv"]
    missing_uv = [item["name"] for item in metrics if not item["uv"]["present"]]
    issues.append(
        issue(
            f"LOD{lod}_UV0_MISSING",
            "error" if missing_uv else "pass",
            f"Every mesh requires an authored {uv_profile['required_map']} map.",
            len(missing_uv),
            0,
            missing_uv,
        )
    )
    coverage_offenders = [
        item["name"]
        for item in metrics
        if item["uv"]["face_coverage"] < uv_profile["min_face_coverage"]
    ]
    minimum_coverage = min(item["uv"]["face_coverage"] for item in metrics)
    issues.append(
        issue(
            f"LOD{lod}_UV_COVERAGE",
            "error" if coverage_offenders else "pass",
            "Every polygon must have finite, non-zero UV area.",
            minimum_coverage,
            uv_profile["min_face_coverage"],
            coverage_offenders,
        )
    )
    uv_zero_area = sum(item["uv"]["zero_area_faces"] for item in metrics)
    issues.append(
        issue(
            f"LOD{lod}_UV_ZERO_AREA",
            "error" if uv_zero_area > uv_profile["max_zero_area_faces"] else "pass",
            "UV0 must not contain zero-area faces.",
            uv_zero_area,
            uv_profile["max_zero_area_faces"],
            [item["name"] for item in metrics if item["uv"]["zero_area_faces"]],
        )
    )
    out_of_bounds = sum(item["uv"]["out_of_bounds_loops"] for item in metrics)
    issues.append(
        issue(
            f"LOD{lod}_UV_BOUNDS",
            "error" if out_of_bounds > uv_profile["max_out_of_bounds_loops"] else "pass",
            "Conventional HeroV2 UV0 coordinates must remain in the 0-1 tile.",
            out_of_bounds,
            uv_profile["max_out_of_bounds_loops"],
            [item["name"] for item in metrics if item["uv"]["out_of_bounds_loops"]],
        )
    )

    material_profile = profile["materials"]
    renderer_count = len(metrics)
    draw_calls = sum(item["materials"]["used_slot_count"] for item in metrics)
    max_slots = max(item["materials"]["slot_count"] for item in metrics)
    empty_slots = sum(item["materials"]["empty_used_slots"] for item in metrics)
    issues.extend(
        [
            issue(
                f"LOD{lod}_RENDERER_HARD_MAX",
                "error" if renderer_count > material_profile["renderer_hard_max"] else "pass",
                "Renderer-bearing mesh count must stay within the runtime hard ceiling.",
                renderer_count,
                material_profile["renderer_hard_max"],
            ),
            issue(
                f"LOD{lod}_RENDERER_TARGET",
                "warning" if renderer_count > material_profile["renderer_target_max"] else "pass",
                "HeroV2 targets five consolidated renderer meshes.",
                renderer_count,
                material_profile["renderer_target_max"],
            ),
            issue(
                f"LOD{lod}_DRAW_CALL_HARD_MAX",
                "error" if draw_calls > material_profile["draw_call_hard_max"] else "pass",
                "Used material slots approximate ordinary draw calls and must meet the hard ceiling.",
                draw_calls,
                material_profile["draw_call_hard_max"],
            ),
            issue(
                f"LOD{lod}_DRAW_CALL_TARGET",
                "warning" if draw_calls > material_profile["draw_call_target_max"] else "pass",
                "HeroV2 targets at most six ordinary suit/weapon draw calls.",
                draw_calls,
                material_profile["draw_call_target_max"],
            ),
            issue(
                f"LOD{lod}_SLOTS_PER_RENDERER",
                "error"
                if max_slots > material_profile["material_slots_per_renderer_hard_max"]
                else "pass",
                "No renderer may exceed the material-slot hard ceiling.",
                max_slots,
                material_profile["material_slots_per_renderer_hard_max"],
                [
                    item["name"]
                    for item in metrics
                    if item["materials"]["slot_count"]
                    > material_profile["material_slots_per_renderer_hard_max"]
                ],
            ),
            issue(
                f"LOD{lod}_EMPTY_MATERIAL_ASSIGNMENTS",
                "error" if empty_slots else "pass",
                "Every used material index must resolve to a material.",
                empty_slots,
                0,
            ),
        ]
    )

    triangles_by_role: dict[str, int] = defaultdict(int)
    for item in metrics:
        triangles_by_role[item["role"]] += item["triangles"]
    budget_results: dict[str, Any] = {}
    triangles_total = sum(item["triangles"] for item in metrics)
    combined_hard_max = profile["lods"]["combined_triangle_hard_max"][f"LOD{lod}"]
    issues.append(
        issue(
            f"LOD{lod}_COMBINED_TRIANGLES",
            "error" if triangles_total > combined_hard_max else "pass",
            "Combined suit, rifle, and optic triangles must meet the runtime hard ceiling.",
            triangles_total,
            combined_hard_max,
        )
    )
    for role, actual in sorted(triangles_by_role.items()):
        budget = triangle_budget(profile, role, lod)
        if budget is None:
            continue
        result = evaluate_triangle_budget(actual, budget)
        budget_results[role] = result
        issues.append(
            issue(
                f"LOD{lod}_{role.upper()}_TRIANGLES",
                result["severity"],
                f"{role.title()} triangles are evaluated against the authored LOD target.",
                actual,
                {key: budget[key] for key in ("target_min", "target_max", "hard_max")},
            )
        )

    density_by_role: dict[str, Any] = {}
    target_density = uv_profile["texel_density_target_px_per_cm"]
    tolerance = uv_profile["texel_density_tolerance_fraction"]
    for role in present_roles:
        role_objects = [item for item in metrics if item["role"] == role]
        world_area = sum(item["uv"]["world_surface_area_m2"] for item in role_objects)
        uv_area = sum(item["uv"]["summed_uv_area_overlap_unaware"] for item in role_objects)
        resolution = uv_profile["texture_resolution"][role]
        density = (
            resolution * math.sqrt(uv_area / world_area) / 100.0
            if uv_area > 0.0 and world_area > 0.0
            else 0.0
        )
        density_by_role[role] = round(density, 4)
        low = target_density * (1.0 - tolerance)
        high = target_density * (1.0 + tolerance)
        density_ok = low <= density <= high
        issues.append(
            issue(
                f"LOD{lod}_{role.upper()}_TEXEL_DENSITY",
                "pass" if density_ok else "warning",
                "Texel density is overlap-unaware and is a quality warning, not a UV-overlap proof.",
                round(density, 4),
                {"target": target_density, "tolerance_fraction": tolerance},
            )
        )

    unique_materials = sorted(
        {
            material
            for item in metrics
            for material in item["materials"]["used_materials"]
        }
    )
    summary = {
        "lod": lod,
        "selection_method": selection_method,
        "object_count": len(metrics),
        "renderer_count": renderer_count,
        "draw_calls_estimate": draw_calls,
        "unique_material_count": len(unique_materials),
        "unique_materials": unique_materials,
        "triangles_total": triangles_total,
        "triangles_by_role": dict(sorted(triangles_by_role.items())),
        "triangle_budget_results": budget_results,
        "texel_density_by_role_px_per_cm": density_by_role,
        "objects": metrics,
    }
    return summary, issues


def remove_generated_collection(name: str) -> None:
    existing = bpy.data.collections.get(name)
    if existing is None:
        return
    if not bool(existing.get("hero_v2_generated", False)):
        raise ContractError(
            f"Refusing to replace authored collection {name!r}; only generated collections are replaceable."
        )
    for obj in list(recursive_collection_objects(existing)):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(existing)


def generate_lods(
    source_objects: list[bpy.types.Object], profile: dict[str, Any]
) -> dict[int, list[bpy.types.Object]]:
    generated: dict[int, list[bpy.types.Object]] = {}
    base_name = profile["selection"]["lod0_collection"]
    role_property = profile["selection"]["role_property"]
    minimum_triangles = profile["lods"]["minimum_triangles_per_object"]

    for lod in (1, 2, 3):
        collection_name = base_name.replace("LOD0", f"LOD{lod}")
        remove_generated_collection(collection_name)
        collection = bpy.data.collections.new(collection_name)
        collection["hero_v2_generated"] = True
        collection["hero_v2_generation_ratio"] = profile["lods"]["generation_ratios"][
            f"LOD{lod}"
        ]
        bpy.context.scene.collection.children.link(collection)

        ratio = float(profile["lods"]["generation_ratios"][f"LOD{lod}"])
        generated[lod] = []
        for source in sorted(source_objects, key=lambda item: item.name_full):
            duplicate = source.copy()
            duplicate.data = source.data.copy()
            duplicate.name = f"H2_{source.name}_LOD{lod}"
            duplicate.data.name = f"{duplicate.name}_Mesh"
            duplicate["hero_v2_lod"] = lod
            duplicate["hero_v2_generated"] = True
            duplicate["hero_v2_source_object"] = source.name_full
            duplicate[role_property] = infer_role(source.name_full, source.get(role_property))
            duplicate.hide_set(False)
            duplicate.hide_viewport = False
            duplicate.hide_render = False
            collection.objects.link(duplicate)

            duplicate.data.calc_loop_triangles()
            if len(duplicate.data.loop_triangles) >= minimum_triangles:
                bpy.context.view_layer.objects.active = duplicate
                duplicate.select_set(True)
                modifier = duplicate.modifiers.new(
                    name=f"HeroV2_DraftDecimate_LOD{lod}", type="DECIMATE"
                )
                # Decimate the undeformed bind mesh. Runtime Armature modifiers
                # must remain intact and must never be baked into a generated LOD.
                duplicate.modifiers.move(len(duplicate.modifiers) - 1, 0)
                modifier.decimate_type = "COLLAPSE"
                modifier.ratio = ratio
                if hasattr(modifier, "use_collapse_triangulate"):
                    modifier.use_collapse_triangulate = True
                bpy.ops.object.modifier_apply(modifier=modifier.name)
                duplicate.select_set(False)
            generated[lod].append(duplicate)

    return generated


def main() -> int:
    args = parse_args()
    source = absolute(args.source)
    profile_path = absolute(args.profile)
    report = absolute(args.report)
    output = absolute(args.output_blend) if args.output_blend else None
    profile = load_profile(profile_path)

    if not source.is_file():
        raise ContractError(f"HeroV2 source blend does not exist: {source}")
    if args.generate_lods and output is None:
        raise ContractError("--generate-lods requires --output-blend.")
    if output is not None:
        assert_derivative_path(source, output)
    assert_derivative_path(source, report)

    source_hash_before = sha256_file(source)
    bpy.ops.wm.open_mainfile(filepath=str(source))

    lod0_objects, lod0_method = select_lod_objects(
        0, profile, args.allow_fallback_property
    )
    generated: dict[int, list[bpy.types.Object]] = {}
    if args.generate_lods:
        if not lod0_objects:
            raise ContractError("Cannot generate LODs without a valid LOD0 selection.")
        generated = generate_lods(lod0_objects, profile)

    lod_reports: list[dict[str, Any]] = []
    issues: list[dict[str, Any]] = []
    for lod in (0, 1, 2, 3):
        if lod == 0:
            objects, method = lod0_objects, lod0_method
        elif lod in generated:
            objects, method = generated[lod], "generated:deterministic_decimate"
        else:
            objects, method = select_lod_objects(lod, profile, False)
        lod_report, lod_issues = validate_lod(
            lod,
            objects,
            method,
            profile,
            args.require_lods or args.generate_lods,
        )
        lod_reports.append(lod_report)
        issues.extend(lod_issues)

    output_hash = None
    if output is not None:
        output.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
        output_hash = sha256_file(output)

    source_hash_after = sha256_file(source)
    if source_hash_after != source_hash_before:
        issues.append(
            issue(
                "SOURCE_BLEND_MUTATED",
                "error",
                "The immutable source blend hash changed during processing.",
                source_hash_after,
                source_hash_before,
            )
        )
    else:
        issues.append(
            issue(
                "SOURCE_BLEND_IMMUTABLE",
                "pass",
                "The source blend SHA-256 is unchanged.",
                source_hash_after,
                source_hash_before,
            )
        )

    counts = summarise_issues(issues)
    result = {
        "schema_version": 1,
        "asset": profile["asset_name"],
        "status": "FAIL" if counts["error"] else "PASS",
        "source": {
            "path": report_path(source),
            "sha256_before": source_hash_before,
            "sha256_after": source_hash_after,
            "immutable": source_hash_before == source_hash_after,
        },
        "derivative": {
            "generated": output is not None,
            "path": report_path(output) if output is not None else None,
            "sha256": output_hash,
        },
        "profile": {
            "path": report_path(profile_path),
            "sha256": sha256_file(profile_path),
        },
        "blender_version": bpy.app.version_string,
        "lod_generation": {
            "enabled": bool(args.generate_lods),
            "method": "Blender COLLAPSE decimate per source renderer; draft LODs require hand repair",
            "ratios": profile["lods"]["generation_ratios"],
        },
        "summary": counts,
        "lods": lod_reports,
        "issues": issues,
        "limitations": [
            "UV area and texel-density metrics do not prove island non-overlap or padding.",
            "Generated decimation is a deterministic first pass, not an approved final LOD.",
            "Renderer and draw-call counts assume one Blender mesh object per runtime renderer.",
            "Animation deformation and weapon-clearance sweeps are separate production gates.",
        ],
    }
    write_canonical_json(report, result)
    print(
        f"HeroV2 validation {result['status']}: "
        f"{counts['error']} errors, {counts['warning']} warnings; {report}"
    )
    return 0 if args.soft_fail or counts["error"] == 0 else 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ContractError as exc:
        print(f"HeroV2 contract error: {exc}", file=sys.stderr)
        raise SystemExit(3) from exc
