"""Fail-closed Blender production gate for Candidate006 / WeaponV2.

This adapter audits the actual visible meshes in the final Candidate006 blend.
It never generates geometry, edits the source, accepts clearance proxies, or
authorizes Unity integration.  Run through Blender 5.2 or newer, for example:

  blender --background --python-exit-code 1 --python validate_candidate006.py -- \
    --source ../candidates/nextgen_precision_rifle_candidate_v006.blend \
    --report reports/candidate006_production.json \
    --render-dir ../renders/nextgen_precision_rifle_candidate_v006
"""

from __future__ import annotations

import argparse
import json
import math
import sys
import traceback
from collections import Counter
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore
from mathutils.bvhtree import BVHTree  # type: ignore
from mathutils.kdtree import KDTree  # type: ignore


LANE_ROOT = Path(__file__).resolve().parent
NEXTGEN_ROOT = LANE_ROOT.parent
REPOSITORY_ROOT = LANE_ROOT.parents[2]
LEGACY_SCRIPTS = REPOSITORY_ROOT / "ArtSource" / "PoweredSuit" / "scripts"
SHARED_SCRIPTS = NEXTGEN_ROOT / "scripts"
for module_path in (LANE_ROOT, SHARED_SCRIPTS, LEGACY_SCRIPTS):
    sys.path.insert(0, str(module_path))

from clearance_face_policy import (  # noqa: E402
    MANIFEST_SCHEMA,
    MANIFEST_TEXT_NAME,
    POLICY_VERSION,
    SEMANTIC_SCHEMA,
    SUIT_ATTRIBUTE,
    SUIT_ZONE_NAMES,
    WEAPON_ATTRIBUTE,
    WEAPON_ZONE_NAMES,
    canonical_sha256 as clearance_manifest_sha256,
    semantic_counts,
    topology_semantics_sha256,
    validate_manifest as validate_clearance_manifest,
)
from powersuit_pipeline_common import (  # noqa: E402
    activate_action,
    body_basis,
    find_action_slot,
    get_action_channelbag,
    named_shoulder_outward_axes,
)
from weapon_handling_contract import assert_weapon_rigid  # noqa: E402
from weapon_v2_contract import (  # noqa: E402
    REQUIRED_EVIDENCE,
    ContractError,
    assert_exact_action_contract,
    evaluate_skin_motion_metrics,
    evaluate_triangle_budget,
    evaluate_hardpoint_envelopes,
    finalise_report,
    issue_code_scope_passed,
    load_profile,
    missing_source_report,
    safe_repository_path,
    sha256_file,
    sha256_manifest,
    validate_pbr_manifest,
    validate_bound_render_manifest,
    validate_projection_evidence,
    validate_render_set,
    write_canonical_json,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument(
        "--profile", type=Path, default=LANE_ROOT / "production_profile.json"
    )
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument(
        "--render-dir",
        type=Path,
        default=NEXTGEN_ROOT / "renders" / "nextgen_precision_rifle_candidate_v006",
    )
    parser.add_argument(
        "--soft-fail",
        action="store_true",
        help="Write a FAIL report but return zero. Baseline diagnosis only.",
    )
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    return parser.parse_args(argv)


def absolute(path: Path) -> Path:
    return path.resolve() if path.is_absolute() else (REPOSITORY_ROOT / path).resolve()


def report_path(path: Path) -> str:
    resolved = path.resolve()
    try:
        return resolved.relative_to(REPOSITORY_ROOT).as_posix()
    except ValueError:
        return resolved.as_posix()


class Audit:
    def __init__(self, report: dict[str, Any]) -> None:
        self.report = report
        self.issues: list[dict[str, Any]] = report["issues"]

    def issue(
        self,
        code: str,
        severity: str,
        message: str,
        *,
        actual: Any = None,
        expected: Any = None,
    ) -> None:
        entry: dict[str, Any] = {
            "code": code,
            "severity": severity,
            "message": message,
        }
        if actual is not None:
            entry["actual"] = actual
        if expected is not None:
            entry["expected"] = expected
        self.issues.append(entry)

    def begin(self) -> int:
        return len(self.issues)

    def section_passed(self, start: int) -> bool:
        return not any(
            issue.get("severity") == "error" for issue in self.issues[start:]
        )

    def code_scope_passed(
        self,
        start: int,
        *,
        exact: Iterable[str] = (),
        suffixes: Iterable[str] = (),
    ) -> bool:
        """Return whether a named issue-code scope has no blocking errors."""

        return issue_code_scope_passed(
            self.issues,
            start=start,
            exact=exact,
            suffixes=suffixes,
        )


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
    lod: int, profile: Mapping[str, Any], audit: Audit
) -> tuple[list[bpy.types.Object], str]:
    selection = profile["selection"]
    collection_name = f"{selection['collection_prefix']}{lod}"
    collection = bpy.data.collections.get(collection_name)
    if collection is None:
        audit.issue(
            f"LOD{lod}_COLLECTION_MISSING",
            "error",
            "Every authored WeaponV2 LOD collection is mandatory.",
            actual=None,
            expected=collection_name,
        )
        return [], f"missing:{collection_name}"
    all_objects = recursive_collection_objects(collection)
    unsupported = [
        obj.name_full
        for obj in all_objects
        if obj.type not in {"MESH", "EMPTY", "ARMATURE"} and not obj.hide_render
    ]
    if unsupported:
        audit.issue(
            f"LOD{lod}_UNSUPPORTED_RENDERABLES",
            "error",
            "WeaponV2 collections may contain only production mesh renderers.",
            actual=unsupported,
        )
    # LOD1-LOD3 are intentionally hidden while LOD0 is previewed. Collection
    # membership, not current render visibility, defines production selection.
    objects = [obj for obj in all_objects if obj.type == "MESH"]
    role_property = selection["role_property"]
    lod_property = selection["lod_property"]
    for obj in objects:
        role = str(obj.get(role_property, ""))
        if role not in {"rifle", "optic"}:
            audit.issue(
                f"LOD{lod}_ROLE_INVALID",
                "error",
                "Every visible WeaponV2 mesh requires an explicit rifle/optic role.",
                actual={"object": obj.name_full, "role": role},
            )
        if int(obj.get(lod_property, -1)) != lod:
            audit.issue(
                f"LOD{lod}_PROPERTY_INVALID",
                "error",
                "The object LOD property must match its collection.",
                actual={"object": obj.name_full, "lod": obj.get(lod_property)},
                expected=lod,
            )
    return objects, f"collection:{collection_name}"


def world_face_area(obj: bpy.types.Object, polygon: bpy.types.MeshPolygon) -> float:
    indices = list(polygon.vertices)
    if len(indices) < 3:
        return 0.0
    origin = obj.matrix_world @ obj.data.vertices[indices[0]].co
    area = 0.0
    for index in range(1, len(indices) - 1):
        point_a = obj.matrix_world @ obj.data.vertices[indices[index]].co
        point_b = obj.matrix_world @ obj.data.vertices[indices[index + 1]].co
        area += (point_a - origin).cross(point_b - origin).length * 0.5
    return area


def uv_face_area(
    polygon: bpy.types.MeshPolygon, uv_layer: bpy.types.MeshUVLoopLayer
) -> tuple[float, bool, int]:
    loop_indices = list(polygon.loop_indices)
    if len(loop_indices) < 3:
        return 0.0, False, 0
    coordinates = [uv_layer.data[index].uv.copy() for index in loop_indices]
    finite = all(math.isfinite(value) for uv in coordinates for value in uv)
    if not finite:
        return 0.0, False, len(coordinates)
    origin = coordinates[0]
    area = 0.0
    for index in range(1, len(coordinates) - 1):
        a = coordinates[index] - origin
        b = coordinates[index + 1] - origin
        area += abs(a.x * b.y - a.y * b.x) * 0.5
    return area, True, 0


def duplicate_vertex_pairs(mesh: bpy.types.Mesh, epsilon: float) -> int:
    if not mesh.vertices:
        return 0
    tree = KDTree(len(mesh.vertices))
    for vertex in mesh.vertices:
        tree.insert(vertex.co, vertex.index)
    tree.balance()
    count = 0
    for vertex in mesh.vertices:
        count += sum(
            1
            for _point, other_index, distance in tree.find_range(vertex.co, epsilon)
            if other_index > vertex.index and distance <= epsilon
        )
    return count


def object_metrics(obj: bpy.types.Object, profile: Mapping[str, Any]) -> dict[str, Any]:
    mesh = obj.data
    mesh.calc_loop_triangles()
    edge_indices = {tuple(sorted(edge.vertices)): edge.index for edge in mesh.edges}
    edge_face_uses = [0] * len(mesh.edges)
    used_vertices: set[int] = set()
    for polygon in mesh.polygons:
        used_vertices.update(polygon.vertices)
        for key in polygon.edge_keys:
            edge_index = edge_indices.get(tuple(sorted(key)))
            if edge_index is not None:
                edge_face_uses[edge_index] += 1

    topology = profile["topology"]
    zero_area_faces = sum(
        world_face_area(obj, polygon) <= topology["zero_area_epsilon_m2"]
        for polygon in mesh.polygons
    )
    degenerate_edges = 0
    for edge in mesh.edges:
        start = obj.matrix_world @ mesh.vertices[edge.vertices[0]].co
        end = obj.matrix_world @ mesh.vertices[edge.vertices[1]].co
        if (end - start).length <= topology["duplicate_position_epsilon_m"]:
            degenerate_edges += 1

    uv_profile = profile["uv"]
    uv_layer = mesh.uv_layers.get(uv_profile["required_map"])
    covered_faces = 0
    zero_area_uv_faces = 0
    invalid_uv_values = 0
    out_of_bounds_loops = 0
    if uv_layer is not None:
        epsilon = uv_profile["bounds_epsilon"]
        for polygon in mesh.polygons:
            area, finite, invalid = uv_face_area(polygon, uv_layer)
            invalid_uv_values += invalid
            if finite and area > uv_profile["zero_area_epsilon"]:
                covered_faces += 1
            else:
                zero_area_uv_faces += 1
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
        zero_area_uv_faces = len(mesh.polygons)

    used_material_indices = sorted({polygon.material_index for polygon in mesh.polygons})
    material_names: list[str] = []
    empty_material_assignments = 0
    for index in used_material_indices:
        if index >= len(obj.material_slots) or obj.material_slots[index].material is None:
            empty_material_assignments += 1
        else:
            material_names.append(obj.material_slots[index].material.name_full)

    scale = tuple(float(value) for value in obj.scale)
    return {
        "name": obj.name_full,
        "role": str(obj.get(profile["selection"]["role_property"], "")),
        "vertices": len(mesh.vertices),
        "edges": len(mesh.edges),
        "faces": len(mesh.polygons),
        "triangles": len(mesh.loop_triangles),
        "topology": {
            "boundary_edges": sum(uses == 1 for uses in edge_face_uses),
            "non_manifold_edges": sum(uses > 2 for uses in edge_face_uses),
            "loose_edges": sum(uses == 0 for uses in edge_face_uses),
            "loose_vertices": len(mesh.vertices) - len(used_vertices),
            "ngons": sum(len(polygon.vertices) > 4 for polygon in mesh.polygons),
            "untriangulated_faces": sum(
                len(polygon.vertices) != 3 for polygon in mesh.polygons
            ),
            "zero_area_faces": zero_area_faces,
            "degenerate_edges": degenerate_edges,
            "duplicate_vertex_pairs": duplicate_vertex_pairs(
                mesh, topology["duplicate_position_epsilon_m"]
            ),
            "unapplied_scale": any(abs(value - 1.0) > 1e-5 for value in scale),
            "negative_transform_determinant": (
                obj.matrix_world.to_3x3().determinant() <= 0.0
            ),
            "runtime_modifiers": [
                f"{modifier.name}:{modifier.type}"
                for modifier in obj.modifiers
                if modifier.type == "ARMATURE"
            ],
            "unapplied_authoring_modifiers": [
                f"{modifier.name}:{modifier.type}"
                for modifier in obj.modifiers
                if modifier.type != "ARMATURE"
            ],
        },
        "uv": {
            "required_map": uv_profile["required_map"],
            "present": uv_layer is not None,
            "covered_faces": covered_faces,
            "face_coverage": (
                covered_faces / len(mesh.polygons) if mesh.polygons else 0.0
            ),
            "zero_area_faces": zero_area_uv_faces,
            "invalid_values": invalid_uv_values,
            "out_of_bounds_loops": out_of_bounds_loops,
        },
        "materials": {
            "slot_count": len(obj.material_slots),
            "used_slot_count": len(used_material_indices),
            "used_materials": material_names,
            "empty_used_slots": empty_material_assignments,
        },
    }


def rigid_weight_metrics(
    obj: bpy.types.Object, armature_name: str, required_control_bones: Sequence[str]
) -> dict[str, Any]:
    allowed = set(required_control_bones)
    group_names = {
        group.index: group.name for group in obj.vertex_groups
    }
    violations: list[dict[str, Any]] = []
    represented: set[str] = set()
    for vertex in obj.data.vertices:
        influences = [
            (group_names.get(group.group, ""), float(group.weight))
            for group in vertex.groups
            if group.weight > 1e-8
        ]
        if (
            len(influences) != 1
            or influences[0][0] not in allowed
            or abs(influences[0][1] - 1.0) > 1e-5
        ):
            if len(violations) < 20:
                violations.append(
                    {"vertex": vertex.index, "influences": influences}
                )
        elif influences:
            represented.add(influences[0][0])
    armature_modifiers = [
        modifier
        for modifier in obj.modifiers
        if modifier.type == "ARMATURE"
    ]
    binding_valid = (
        len(armature_modifiers) == 1
        and armature_modifiers[0].object is not None
        and armature_modifiers[0].object.name_full == armature_name
    )
    return {
        "binding_valid": binding_valid,
        "armature_modifiers": [
            {
                "name": modifier.name,
                "object": None if modifier.object is None else modifier.object.name_full,
            }
            for modifier in armature_modifiers
        ],
        "represented_control_bones": sorted(represented),
        "violation_count": len(violations),
        "first_violations": violations,
    }


def matrix_maximum_delta(first: Matrix, second: Matrix) -> float:
    return max(
        abs(float(first[row][column] - second[row][column]))
        for row in range(4)
        for column in range(4)
    )


def validate_weapon_skin_motion(
    profile: Mapping[str, Any],
    armature: bpy.types.Object | None,
    lod0_objects: Sequence[bpy.types.Object],
    audit: Audit,
) -> dict[str, Any]:
    """Independently prove that LOD0 follows its rigid animated skin controls."""

    start = audit.begin()
    requirements = profile["skin_motion"]
    metrics: dict[str, Any] = {"samples": {}}
    if armature is None or not lod0_objects:
        audit.issue(
            "WEAPON_SKIN_MOTION",
            "error",
            "Weapon skin motion requires the canonical armature and LOD0 renderers.",
        )
        audit.report["evidence"]["weapon_skin_motion"] = False
        return metrics

    original_action = (
        armature.animation_data.action
        if armature.animation_data is not None else None
    )
    original_frame = float(bpy.context.scene.frame_current_final)
    try:
        for specification in requirements["required_samples"]:
            action_name = str(specification["action"])
            frame = int(specification["frame"])
            activate_action(armature, action_name)
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            renderer_errors: dict[str, float] = {}
            for obj in lod0_objects:
                evaluated = obj.evaluated_get(depsgraph)
                mesh = evaluated.to_mesh(
                    preserve_all_data_layers=False, depsgraph=depsgraph
                )
                try:
                    if len(mesh.vertices) != len(obj.data.vertices):
                        raise ContractError(
                            f"{obj.name_full} evaluation changed vertex count."
                        )
                    maximum_error = 0.0
                    object_to_armature = (
                        armature.matrix_world.inverted_safe() @ obj.matrix_world
                    )
                    for source_vertex, result_vertex in zip(
                        obj.data.vertices, mesh.vertices
                    ):
                        assignments = [
                            item for item in source_vertex.groups if item.weight > 1.0e-8
                        ]
                        if len(assignments) != 1:
                            raise ContractError(
                                f"{obj.name_full} vertex {source_vertex.index} is not rigidly weighted."
                            )
                        group_name = obj.vertex_groups[assignments[0].group].name
                        data_bone = armature.data.bones.get(group_name)
                        pose_bone = armature.pose.bones.get(group_name)
                        if data_bone is None or pose_bone is None:
                            raise ContractError(
                                f"{obj.name_full} references missing control {group_name!r}."
                            )
                        expected_world = (
                            armature.matrix_world
                            @ pose_bone.matrix
                            @ data_bone.matrix_local.inverted_safe()
                            @ object_to_armature
                            @ source_vertex.co
                        )
                        actual_world = evaluated.matrix_world @ result_vertex.co
                        maximum_error = max(
                            maximum_error, float((actual_world - expected_world).length)
                        )
                    renderer_errors[obj.name_full] = maximum_error
                finally:
                    evaluated.to_mesh_clear()
            metrics["samples"][f"{action_name}@{frame}"] = {
                "maximum_manual_skin_error_m": max(renderer_errors.values()),
                "per_renderer_error_m": renderer_errors,
            }

        def pose(action_name: str, frame: int, bone_name: str) -> Matrix:
            activate_action(armature, action_name)
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            return armature.pose.bones[bone_name].matrix.copy()

        ready = pose("PS_WeaponReady_Idle", 1, "WeaponRoot")
        stowed = pose("PS_WeaponStowed_Idle", 1, "WeaponRoot")
        metrics["root_ready_to_stowed_travel_m"] = float(
            (ready.translation - stowed.translation).length
        )
        metrics["root_transition_return_matrix_error"] = max(
            matrix_maximum_delta(pose("PS_Weapon_Draw", 1, "WeaponRoot"), stowed),
            matrix_maximum_delta(pose("PS_Weapon_Draw", 30, "WeaponRoot"), ready),
            matrix_maximum_delta(pose("PS_Weapon_Sheathe", 1, "WeaponRoot"), ready),
            matrix_maximum_delta(pose("PS_Weapon_Sheathe", 30, "WeaponRoot"), stowed),
        )

        def component_relative(
            action_name: str, frame: int, component_name: str
        ) -> Matrix:
            root_pose = pose(action_name, frame, "WeaponRoot")
            component_pose = armature.pose.bones[component_name].matrix.copy()
            return root_pose.inverted_safe() @ component_pose

        magazine_start = component_relative("PS_Reload", 1, "WeaponMagazine")
        magazine_travel = component_relative("PS_Reload", 50, "WeaponMagazine")
        magazine_end = component_relative("PS_Reload", 84, "WeaponMagazine")
        metrics["magazine_travel_m"] = float(
            (magazine_travel.translation - magazine_start.translation).length
        )
        metrics["magazine_return_matrix_error"] = matrix_maximum_delta(
            magazine_start, magazine_end
        )
        bolt_start = component_relative("PS_BoltCycle", 1, "WeaponBolt")
        bolt_travel = component_relative("PS_BoltCycle", 12, "WeaponBolt")
        bolt_end = component_relative("PS_BoltCycle", 20, "WeaponBolt")
        metrics["bolt_travel_m"] = float(
            (bolt_travel.translation - bolt_start.translation).length
        )
        metrics["bolt_return_matrix_error"] = matrix_maximum_delta(
            bolt_start, bolt_end
        )
        errors = evaluate_skin_motion_metrics(metrics, requirements)
    except Exception as exc:
        errors = [f"Independent skin-motion evaluation failed: {type(exc).__name__}: {exc}"]
    finally:
        if original_action is not None:
            activate_action(armature, original_action)
        elif armature.animation_data is not None:
            armature.animation_data.action = None
        bpy.context.scene.frame_set(int(math.floor(original_frame)), subframe=original_frame % 1.0)
        bpy.context.view_layer.update()

    audit.issue(
        "WEAPON_SKIN_MOTION",
        "pass" if not errors else "error",
        "LOD0 must match the explicit pose/rest skin equation and show reversible root, magazine and bolt motion.",
        actual={"metrics": metrics, "errors": errors},
        expected=requirements,
    )
    audit.report["evidence"]["weapon_skin_motion"] = audit.section_passed(start)
    return metrics


def audit_uv_overlaps(obj: bpy.types.Object) -> dict[str, int]:
    """Use Blender's own UV overlap selection on one renderer's unique atlas."""

    if obj.data.uv_layers.get("UV0") is None:
        return {"selected_overlap_faces": -1, "selected_overlap_loops": -1}
    previous_sync = bpy.context.scene.tool_settings.use_uv_select_sync
    bpy.context.scene.tool_settings.use_uv_select_sync = False
    bpy.ops.object.select_all(action="DESELECT")
    obj.hide_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    try:
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.select_all(action="DESELECT")
        bpy.ops.uv.select_overlap()
        bpy.ops.object.mode_set(mode="OBJECT")
    finally:
        if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        bpy.context.scene.tool_settings.use_uv_select_sync = previous_sync
    loop_selection = obj.data.attributes.get(".uv_select_vert")
    face_selection = obj.data.attributes.get(".uv_select_face")
    result = {
        "selected_overlap_faces": (
            sum(bool(item.value) for item in face_selection.data)
            if face_selection is not None
            else 0
        ),
        "selected_overlap_loops": (
            sum(bool(item.value) for item in loop_selection.data)
            if loop_selection is not None
            else 0
        ),
    }
    bpy.ops.object.select_all(action="DESELECT")
    return result


def verify_immutable_inputs(
    profile: Mapping[str, Any], audit: Audit
) -> dict[str, dict[str, Any]]:
    results: dict[str, dict[str, Any]] = {}
    for name, expected in sorted(profile["immutable_inputs"].items()):
        try:
            path = safe_repository_path(REPOSITORY_ROOT, expected["path"])
        except ContractError as exc:
            audit.issue("IMMUTABLE_PATH_INVALID", "error", str(exc), actual=name)
            continue
        actual_hash = sha256_file(path) if path.is_file() else None
        matches = actual_hash is not None and actual_hash.lower() == expected["sha256"].lower()
        results[name] = {
            "path": report_path(path),
            "expected_sha256": expected["sha256"].lower(),
            "actual_sha256": actual_hash,
            "matches": matches,
        }
        audit.issue(
            f"IMMUTABLE_{name.upper()}",
            "pass" if matches else "error",
            "Immutable baseline must exist and match its pinned SHA-256.",
            actual=actual_hash,
            expected=expected["sha256"].lower(),
        )
    return results


def action_range(action: bpy.types.Action) -> list[int]:
    if bool(getattr(action, "use_frame_range", False)):
        return [int(round(action.frame_start)), int(round(action.frame_end))]
    return [int(round(action.frame_range[0])), int(round(action.frame_range[1]))]


def validate_rig_and_actions(
    profile: Mapping[str, Any], audit: Audit
) -> tuple[bpy.types.Object | None, dict[str, Any]]:
    start = audit.begin()
    rig_profile = profile["rig"]
    armature = bpy.data.objects.get(rig_profile["armature_name"])
    details: dict[str, Any] = {}
    if armature is None or armature.type != "ARMATURE":
        audit.issue(
            "RIG_ARMATURE_MISSING",
            "error",
            "Candidate006 must preserve the canonical PowerSuit armature.",
            actual=None if armature is None else armature.type,
            expected=rig_profile["armature_name"],
        )
        return None, details

    actual_bones = sorted(bone.name for bone in armature.data.bones)
    expected_bones = sorted(rig_profile["bone_names"])
    bone_match = actual_bones == expected_bones
    audit.issue(
        "RIG_EXACT_23_BONES",
        "pass" if bone_match else "error",
        "The bone set must remain exactly the pinned 23-bone contract.",
        actual=actual_bones,
        expected=expected_bones,
    )
    deforming_controls = {
        name: bool(armature.data.bones[name].use_deform)
        for name in rig_profile["weapon_control_bones"]
        if armature.data.bones.get(name) is not None
    }
    controls_valid = (
        rig_profile["weapon_control_deform_required"] is True
        and set(deforming_controls) == set(rig_profile["weapon_control_bones"])
        and all(deforming_controls.values())
    )
    audit.issue(
        "RIG_DEFORMING_WEAPON_CONTROLS",
        "pass" if controls_valid else "error",
        "Candidate006 requires WeaponRoot, WeaponMagazine and WeaponBolt to deform their one-hot weighted production renderers.",
        actual=deforming_controls,
        expected={name: True for name in rig_profile["weapon_control_bones"]},
    )

    expected_ranges = rig_profile["action_ranges"]
    actions = {action.name: action for action in bpy.data.actions if action.name.startswith("PS_")}
    ranges = {name: action_range(action) for name, action in sorted(actions.items())}
    slot_counts = {name: len(list(action.slots)) for name, action in actions.items()}
    try:
        assert_exact_action_contract(ranges, slot_counts, expected_ranges)
        exact_actions = True
    except ContractError as exc:
        exact_actions = False
        audit.issue("RIG_ACTION_CONTRACT", "error", str(exc), actual=ranges, expected=expected_ranges)
    if exact_actions:
        audit.issue(
            "RIG_ACTION_CONTRACT",
            "pass",
            "All 24 action names, ranges and one-slot counts match Generator114.",
            actual=ranges,
        )

    action_details: dict[str, Any] = {}
    action_errors = False
    root_motion_tolerance = float(rig_profile["root_motion_tolerance_m"])
    for name in sorted(set(actions) & set(expected_ranges)):
        action = actions[name]
        slots = list(action.slots)
        slot_valid = (
            len(slots) == 1
            and str(getattr(slots[0], "target_id_type", "")) == rig_profile["action_slot_id_type"]
        )
        control_curve_bones: set[str] = set()
        root_location_range = 0.0
        curve_count = 0
        if slots:
            try:
                slot = find_action_slot(action, armature)
                channelbag = get_action_channelbag(action, slot)
                curves = list(channelbag.fcurves)
                curve_count = len(curves)
                for curve in curves:
                    for control in rig_profile["weapon_control_bones"]:
                        if f'pose.bones["{control}"]' in curve.data_path:
                            control_curve_bones.add(control)
                    if curve.data_path == 'pose.bones["Root"].location':
                        values = [float(point.co.y) for point in curve.keyframe_points]
                        if values:
                            root_location_range = max(
                                root_location_range, max(values) - min(values)
                            )
            except Exception as exc:
                slot_valid = False
                action_errors = True
                audit.issue(
                    "RIG_ACTION_SLOT_READ",
                    "error",
                    f"Could not resolve {name}'s armature Action Slot: {exc}",
                )
        controls_valid_for_action = control_curve_bones == set(
            rig_profile["weapon_control_bones"]
        )
        root_motion_valid = root_location_range <= root_motion_tolerance
        if not slot_valid or not controls_valid_for_action or not root_motion_valid:
            action_errors = True
        action_details[name] = {
            "frame_range": ranges.get(name),
            "slot_count": len(slots),
            "slot_target_id_type": (
                str(getattr(slots[0], "target_id_type", "")) if slots else None
            ),
            "curve_count": curve_count,
            "weapon_control_curve_bones": sorted(control_curve_bones),
            "root_location_range_m": root_location_range,
        }
    audit.issue(
        "RIG_ACTION_SLOT_CONTROL_ROOT_MOTION",
        "error" if action_errors else "pass",
        "Every action must target one armature slot, animate all weapon controls, and contain zero root motion.",
        actual=action_details,
    )
    fps_valid = int(round(bpy.context.scene.render.fps / bpy.context.scene.render.fps_base)) == int(
        rig_profile["action_fps"]
    )
    audit.issue(
        "RIG_ACTION_FPS",
        "pass" if fps_valid else "error",
        "Candidate006 must preserve the 30 FPS animation contract.",
        actual=bpy.context.scene.render.fps / bpy.context.scene.render.fps_base,
        expected=rig_profile["action_fps"],
    )
    details = {
        "armature": armature.name_full,
        "bone_count": len(actual_bones),
        "bone_names": actual_bones,
        "deforming_weapon_controls": deforming_controls,
        "actions": action_details,
        "fps": bpy.context.scene.render.fps / bpy.context.scene.render.fps_base,
    }
    audit.report["evidence"]["rig_and_actions"] = audit.section_passed(start)
    return armature, details


def weapon_helpers(root: bpy.types.Object, helper_property: str) -> dict[str, bpy.types.Object]:
    helpers: dict[str, bpy.types.Object] = {}
    for child in root.children:
        role = str(child.get(helper_property, ""))
        if role:
            if role in helpers:
                raise ContractError(f"Duplicate weapon helper role {role!r}.")
            helpers[role] = child
    return helpers


def weapon_local_position(root: bpy.types.Object, obj: bpy.types.Object) -> Vector:
    return root.matrix_world.inverted() @ obj.matrix_world.translation


def validate_weapon_contract(
    profile: Mapping[str, Any], armature: bpy.types.Object | None, audit: Audit
) -> tuple[bpy.types.Object | None, dict[str, Any]]:
    start = audit.begin()
    contract = profile["weapon"]
    root = bpy.data.objects.get(contract["root_name"])
    details: dict[str, Any] = {}
    if root is None:
        audit.issue(
            "WEAPON_ROOT_MISSING",
            "error",
            "Candidate006 requires the canonical RifleRoot object.",
            expected=contract["root_name"],
        )
        audit.report["evidence"]["weapon_contract"] = False
        audit.report["evidence"]["rigid_geometry"] = False
        return None, details

    expected_properties = {
        "ps_weapon_contract_version": contract["contract_version"],
        "ps_weapon_rigid_signature_version": contract["rigid_signature_version"],
        "ps_weapon_id": contract["weapon_id"],
        "ps_weapon_family": contract["weapon_family"],
        "ps_weapon_stance_family": contract["stance_family"],
        "ps_weapon_forward_axis": contract["forward_axis"],
        "ps_weapon_up_axis": contract["up_axis"],
        "ps_weapon_active": True,
        "ps_weapon_rigid": True,
    }
    actual_properties = {name: root.get(name) for name in expected_properties}
    properties_valid = all(
        actual_properties[name] == expected for name, expected in expected_properties.items()
    )
    audit.issue(
        "WEAPON_ROOT_METADATA",
        "pass" if properties_valid else "error",
        "RifleRoot metadata must exactly identify the parallel Candidate006 weapon.",
        actual=actual_properties,
        expected=expected_properties,
    )
    parent_valid = (
        armature is not None
        and root.parent == armature
        and root.parent_type == "BONE"
        and root.parent_bone == contract["root_bone"]
    )
    audit.issue(
        "WEAPON_ROOT_HIERARCHY",
        "pass" if parent_valid else "error",
        "RifleRoot must be bone-parented to WeaponRoot.",
        actual={
            "parent": None if root.parent is None else root.parent.name_full,
            "parent_type": root.parent_type,
            "parent_bone": root.parent_bone,
        },
        expected={
            "parent": profile["rig"]["armature_name"],
            "parent_type": "BONE",
            "parent_bone": contract["root_bone"],
        },
    )
    try:
        helpers = weapon_helpers(root, contract["helper_property"])
    except ContractError as exc:
        helpers = {}
        audit.issue("WEAPON_HELPERS", "error", str(exc))
    required_helpers = set(contract["required_helper_roles"])
    helper_roles_valid = required_helpers <= set(helpers)
    helper_details: dict[str, Any] = {}
    for role in sorted(required_helpers & set(helpers)):
        helper = helpers[role]
        local = weapon_local_position(root, helper)
        entry = {
            "object": helper.name_full,
            "direct_child": helper.parent == root,
            "local_position": [float(value) for value in local],
        }
        if role in {"primary_grip", "support_grip"}:
            entry["target_semantic"] = str(
                helper.get("ps_weapon_target_semantic", "")
            )
            entry["contact_offset_local"] = list(
                helper.get("ps_weapon_contact_offset_local", ())
            )
            if (
                entry["target_semantic"] != "wrist_head"
                or len(entry["contact_offset_local"]) != 3
                or not all(math.isfinite(float(value)) for value in entry["contact_offset_local"])
            ):
                helper_roles_valid = False
        if helper.parent != root or not all(math.isfinite(float(value)) for value in local):
            helper_roles_valid = False
        helper_details[role] = entry
    if helper_roles_valid:
        primary = Vector(helper_details["primary_grip"]["local_position"])
        muzzle = Vector(helper_details["muzzle"]["local_position"])
        stock = Vector(helper_details["stock_contact"]["local_position"])
        sight = Vector(helper_details["sight_ocular"]["local_position"])
        helper_roles_valid = (
            muzzle.y > primary.y and stock.y < primary.y and sight.z > primary.z
        )
    audit.issue(
        "WEAPON_HELPERS",
        "pass" if helper_roles_valid else "error",
        "All five unique direct-child hardpoints must exist with valid canonical geometry.",
        actual=helper_details,
        expected=sorted(required_helpers),
    )
    hardpoint_values = {
        role: entry["local_position"] for role, entry in helper_details.items()
    }
    hardpoint_errors = evaluate_hardpoint_envelopes(
        hardpoint_values, contract["hardpoint_envelopes_m"]
    )
    audit.issue(
        "WEAPON_HARDPOINT_ENVELOPES",
        "pass" if not hardpoint_errors else "error",
        "Versioned local hardpoints must remain inside the bounded ergonomic design envelope.",
        actual={"hardpoints": hardpoint_values, "errors": hardpoint_errors},
        expected=contract["hardpoint_envelopes_m"],
    )

    articulated_details: dict[str, Any] = {}
    articulated_valid = armature is not None
    for role, bone in contract["articulated_component_bones"].items():
        components = [
            obj
            for obj in bpy.data.objects
            if str(obj.get("ps_weapon_component_role", "")) == role
            and str(obj.get("ps_weapon_owner_id", "")) == contract["weapon_id"]
        ]
        valid = bool(components)
        for obj in components:
            valid = valid and (
                obj.type == "MESH"
                and obj.parent == armature
                and obj.parent_type == "BONE"
                and obj.parent_bone == bone
            )
        articulated_valid = articulated_valid and valid
        articulated_details[role] = {
            "expected_bone": bone,
            "objects": [obj.name_full for obj in components],
            "valid": valid,
        }
    audit.issue(
        "WEAPON_ARTICULATED_HIERARCHY",
        "pass" if articulated_valid else "error",
        "Magazine and bolt components must use only their approved armature controls.",
        actual=articulated_details,
    )

    rigid_start = audit.begin()
    try:
        signature = assert_weapon_rigid(root)
        audit.issue(
            "WEAPON_RIGID_SIGNATURE",
            "pass",
            "The actual weapon hierarchy matches its frozen rigid v6 manifest.",
            actual=signature,
        )
    except Exception as exc:
        signature = None
        audit.issue(
            "WEAPON_RIGID_SIGNATURE",
            "error",
            f"Rigid manifest/signature verification failed: {exc}",
        )
    object_action_owners = [
        obj.name_full
        for obj in bpy.data.objects
        if obj != armature
        and obj.animation_data is not None
        and obj.animation_data.action is not None
        and (
            obj == root
            or str(obj.get("ps_weapon_owner_id", "")) == contract["weapon_id"]
            or obj in root.children_recursive
        )
    ]
    audit.issue(
        "WEAPON_NO_OBJECT_ACTIONS",
        "pass" if not object_action_owners else "error",
        "Weapon motion must live only in the synchronized armature actions.",
        actual=object_action_owners,
        expected=[],
    )
    details = {
        "root": root.name_full,
        "root_properties": actual_properties,
        "helpers": helper_details,
        "articulated_components": articulated_details,
        "rigid_signature": signature,
        "object_action_owners": object_action_owners,
    }
    audit.report["evidence"]["weapon_contract"] = audit.section_passed(start)
    audit.report["evidence"]["rigid_geometry"] = audit.section_passed(rigid_start)
    return root, details


def candidate005_runtime_metrics(
    profile: Mapping[str, Any], audit: Audit
) -> dict[int, dict[str, int]]:
    entry = profile["immutable_inputs"]["candidate005_production_report"]
    path = safe_repository_path(REPOSITORY_ROOT, entry["path"])
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
        metrics = {
            int(lod["lod"]): {
                "triangles": int(lod["triangles_total"]),
                "renderers": int(lod["renderer_count"]),
                "draw_calls": int(lod["draw_calls_estimate"]),
            }
            for lod in document["lods"]
        }
    except Exception as exc:
        audit.issue(
            "CANDIDATE005_RUNTIME_METRICS",
            "error",
            f"Could not load pinned Candidate005 production metrics: {exc}",
        )
        return {}
    valid = set(metrics) == {0, 1, 2, 3}
    audit.issue(
        "CANDIDATE005_RUNTIME_METRICS",
        "pass" if valid else "error",
        "Combined runtime budgets require all four pinned Candidate005 LOD metrics.",
        actual=metrics,
    )
    return metrics


def validate_lods(
    profile: Mapping[str, Any], armature: bpy.types.Object | None, audit: Audit
) -> tuple[dict[int, list[bpy.types.Object]], list[dict[str, Any]]]:
    start = audit.begin()
    suit_metrics = candidate005_runtime_metrics(profile, audit)
    lod_objects: dict[int, list[bpy.types.Object]] = {}
    lod_reports: list[dict[str, Any]] = []
    for lod in range(4):
        objects, selection_method = select_lod_objects(lod, profile, audit)
        lod_objects[lod] = objects
        metrics = [object_metrics(obj, profile) for obj in objects]
        roles = Counter(metric["role"] for metric in metrics)
        role_valid = roles.get("rifle", 0) == 1 and roles.get("optic", 0) <= 1
        audit.issue(
            f"LOD{lod}_RENDERER_ROLES",
            "pass" if role_valid else "error",
            "Each WeaponV2 LOD needs one rifle renderer and at most one optic renderer.",
            actual=dict(roles),
            expected={"rifle": 1, "optic": "0 or 1"},
        )
        triangles_by_role = Counter()
        for metric in metrics:
            obj = next(obj for obj in objects if obj.name_full == metric["name"])
            triangles_by_role[metric["role"]] += metric["triangles"]
            topology = metric["topology"]
            for field in (
                "boundary_edges",
                "non_manifold_edges",
                "loose_edges",
                "loose_vertices",
                "ngons",
                "untriangulated_faces",
                "zero_area_faces",
                "degenerate_edges",
                "duplicate_vertex_pairs",
            ):
                limit = int(profile["topology"][f"max_{field}"])
                audit.issue(
                    f"LOD{lod}_{metric['name']}_{field}".upper(),
                    "pass" if topology[field] <= limit else "error",
                    f"Production topology metric {field} must meet its limit.",
                    actual=topology[field],
                    expected=limit,
                )
            transform_valid = not topology["unapplied_scale"] and not topology[
                "negative_transform_determinant"
            ]
            modifier_valid = not topology["unapplied_authoring_modifiers"]
            audit.issue(
                f"LOD{lod}_{metric['name']}_TRANSFORMS".upper(),
                "pass" if transform_valid else "error",
                "Visible renderer transforms must use unit scale and positive determinant.",
                actual=topology,
            )
            audit.issue(
                f"LOD{lod}_{metric['name']}_MODIFIERS".upper(),
                "pass" if modifier_valid else "error",
                "Only an Armature runtime modifier may remain on production meshes.",
                actual=topology["unapplied_authoring_modifiers"],
            )
            uv = metric["uv"]
            uv_valid = (
                uv["present"]
                and uv["face_coverage"] >= profile["uv"]["min_face_coverage"]
                and uv["zero_area_faces"] <= profile["uv"]["max_zero_area_faces"]
                and uv["invalid_values"] == 0
                and uv["out_of_bounds_loops"] <= profile["uv"]["max_out_of_bounds_loops"]
            )
            overlap = audit_uv_overlaps(obj)
            metric["uv_overlap_audit"] = overlap
            overlap_valid = overlap["selected_overlap_faces"] <= profile["uv"][
                "max_overlap_faces"
            ]
            audit.issue(
                f"LOD{lod}_{metric['name']}_UV0".upper(),
                "pass" if uv_valid and overlap_valid else "error",
                "UV0 requires complete finite in-bounds non-overlapping face coverage.",
                actual={**uv, **overlap},
            )
            materials = metric["materials"]
            materials_valid = (
                materials["used_slot_count"] >= 1
                and materials["empty_used_slots"] == 0
                and materials["slot_count"]
                <= profile["runtime_budget"]["material_slots_per_renderer_hard_max"]
            )
            audit.issue(
                f"LOD{lod}_{metric['name']}_MATERIALS".upper(),
                "pass" if materials_valid else "error",
                "Every used material assignment must be non-empty and within slot budget.",
                actual=materials,
            )
            weight_metrics = rigid_weight_metrics(
                obj,
                profile["rig"]["armature_name"],
                profile["rig"]["weapon_control_bones"],
            )
            metric["rigid_weights"] = weight_metrics
            required_represented = (
                set(profile["rig"]["weapon_control_bones"])
                if metric["role"] == "rifle"
                else {"WeaponRoot"}
            )
            weights_valid = (
                armature is not None
                and weight_metrics["binding_valid"]
                and weight_metrics["violation_count"] == 0
                and required_represented
                <= set(weight_metrics["represented_control_bones"])
            )
            audit.issue(
                f"LOD{lod}_{metric['name']}_RIGID_WEIGHTS".upper(),
                "pass" if weights_valid else "error",
                "Every visible vertex must have exactly one weight-1 weapon control influence.",
                actual=weight_metrics,
                expected=sorted(required_represented),
            )
        budget = profile["lods"]["rifle_triangle_budgets"][f"LOD{lod}"]
        budget_result = evaluate_triangle_budget(triangles_by_role.get("rifle", 0), budget)
        audit.issue(
            f"LOD{lod}_RIFLE_TRIANGLES",
            budget_result["severity"],
            "Visible rifle triangles are evaluated against the Candidate006 target.",
            actual=budget_result["actual"],
            expected=budget,
        )
        weapon_triangles = sum(metric["triangles"] for metric in metrics)
        weapon_renderers = len(metrics)
        weapon_draw_calls = sum(
            metric["materials"]["used_slot_count"] for metric in metrics
        )
        suit = suit_metrics.get(lod, {"triangles": 0, "renderers": 0, "draw_calls": 0})
        combined = {
            "triangles": suit["triangles"] + weapon_triangles,
            "renderers": suit["renderers"] + weapon_renderers,
            "draw_calls": suit["draw_calls"] + weapon_draw_calls,
        }
        runtime = profile["runtime_budget"]
        combined_valid = (
            combined["triangles"] <= runtime["combined_triangle_hard_max"][f"LOD{lod}"]
            and combined["renderers"] <= runtime["renderer_hard_max"]
            and combined["draw_calls"] <= runtime["draw_call_hard_max"]
            and weapon_renderers <= runtime["weapon_renderer_hard_max"]
        )
        audit.issue(
            f"LOD{lod}_COMBINED_RUNTIME_BUDGET",
            "pass" if combined_valid else "error",
            "Candidate005 suit plus actual Candidate006 visible meshes must meet combined hard ceilings.",
            actual=combined,
            expected={
                "triangles": runtime["combined_triangle_hard_max"][f"LOD{lod}"],
                "renderers": runtime["renderer_hard_max"],
                "draw_calls": runtime["draw_call_hard_max"],
                "weapon_renderers": runtime["weapon_renderer_hard_max"],
            },
        )
        lod_reports.append(
            {
                "lod": lod,
                "selection_method": selection_method,
                "renderer_count": len(metrics),
                "draw_calls_estimate": sum(
                    metric["materials"]["used_slot_count"] for metric in metrics
                ),
                "triangles_total": sum(metric["triangles"] for metric in metrics),
                "triangles_by_role": dict(triangles_by_role),
                "triangle_budget_result": budget_result,
                "candidate005_suit_metrics": suit,
                "combined_runtime_metrics": combined,
                "objects": metrics,
            }
        )
    shared_lod_suffixes = (
        "_COLLECTION_MISSING",
        "_UNSUPPORTED_RENDERABLES",
        "_ROLE_INVALID",
        "_PROPERTY_INVALID",
        "_RENDERER_ROLES",
    )
    topology_suffixes = shared_lod_suffixes + (
        "_BOUNDARY_EDGES",
        "_NON_MANIFOLD_EDGES",
        "_LOOSE_EDGES",
        "_LOOSE_VERTICES",
        "_NGONS",
        "_UNTRIANGULATED_FACES",
        "_ZERO_AREA_FACES",
        "_DEGENERATE_EDGES",
        "_DUPLICATE_VERTEX_PAIRS",
        "_TRANSFORMS",
        "_MODIFIERS",
        "_UV0",
    )
    budget_suffixes = shared_lod_suffixes + (
        "_RIFLE_TRIANGLES",
        "_COMBINED_RUNTIME_BUDGET",
    )
    # Keep the evidence scopes independent: a UV/topology defect is still a
    # blocking issue, but it must not misreport a valid LOD/runtime budget as
    # failed (and a budget overrun must not erase valid topology evidence).
    audit.report["evidence"]["topology_and_uv"] = audit.code_scope_passed(
        start, suffixes=topology_suffixes
    )
    audit.report["evidence"]["lod_and_render_budget"] = audit.code_scope_passed(
        start,
        exact=("CANDIDATE005_RUNTIME_METRICS",),
        suffixes=budget_suffixes,
    )
    return lod_objects, lod_reports


def load_embedded_json(text_name: str, root: bpy.types.Object | None, property_name: str) -> Any:
    raw: str | None = None
    text = bpy.data.texts.get(text_name)
    if text is not None:
        raw = text.as_string()
    elif root is not None:
        value = root.get(property_name)
        if value is not None:
            raw = str(value)
    if not raw:
        raise ContractError(
            f"Missing embedded JSON Text {text_name!r} and root property {property_name!r}."
        )
    try:
        return json.loads(raw)
    except json.JSONDecodeError as exc:
        raise ContractError(f"Embedded JSON {text_name!r} is invalid: {exc}") from exc


def principled_input_linked(material: bpy.types.Material, names: Sequence[str]) -> bool:
    if not material.use_nodes or material.node_tree is None:
        return False
    principled = next(
        (
            node
            for node in material.node_tree.nodes
            if node.type == "BSDF_PRINCIPLED"
        ),
        None,
    )
    if principled is None:
        return False
    socket = next((principled.inputs.get(name) for name in names if principled.inputs.get(name)), None)
    return socket is not None and socket.is_linked


def validate_pbr(
    profile: Mapping[str, Any],
    root: bpy.types.Object | None,
    lod0_objects: Sequence[bpy.types.Object],
    audit: Audit,
) -> dict[str, Any]:
    start = audit.begin()
    pbr = profile["pbr"]
    try:
        manifest = load_embedded_json(
            pbr["manifest_text"], root, pbr["manifest_property"]
        )
        errors = validate_pbr_manifest(
            manifest, REPOSITORY_ROOT, pbr["texture_resolution"]
        )
    except ContractError as exc:
        manifest = None
        errors = [str(exc)]
    audit.issue(
        "PBR_TEXTURE_MANIFEST",
        "pass" if not errors else "error",
        "Candidate006 requires four existing hash-verified 2K PBR maps.",
        actual={"manifest": manifest, "errors": errors},
    )
    image_dimensions: dict[str, Any] = {}
    if isinstance(manifest, Mapping) and isinstance(manifest.get("maps"), Mapping):
        for role, entry in manifest["maps"].items():
            if not isinstance(entry, Mapping) or not isinstance(entry.get("path"), str):
                continue
            try:
                image_path = safe_repository_path(REPOSITORY_ROOT, entry["path"])
                image = bpy.data.images.load(str(image_path), check_existing=False)
                dimensions = [int(image.size[0]), int(image.size[1])]
                bpy.data.images.remove(image)
            except Exception as exc:
                dimensions = {"error": str(exc)}
                errors.append(f"Could not inspect {role} map dimensions: {exc}")
            image_dimensions[role] = dimensions
            if dimensions != list(pbr["texture_resolution"]):
                errors.append(
                    f"{role} dimensions differ: {dimensions} != {pbr['texture_resolution']}"
                )
    dimensions_valid = (
        set(image_dimensions) == set(pbr["required_maps"])
        and all(
            dimensions == list(pbr["texture_resolution"])
            for dimensions in image_dimensions.values()
        )
    )
    audit.issue(
        "PBR_TEXTURE_DIMENSIONS",
        "pass" if dimensions_valid else "error",
        "Each hash-bound PBR map must decode at exactly 2048x2048.",
        actual=image_dimensions,
        expected=pbr["texture_resolution"],
    )
    material_details: dict[str, Any] = {}
    material_valid = True
    materials = {
        slot.material
        for obj in lod0_objects
        for slot in obj.material_slots
        if slot.material is not None
    }
    for material in sorted(materials, key=lambda item: item.name_full):
        channels = {
            "base_color": principled_input_linked(material, ("Base Color",)),
            "metallic": principled_input_linked(material, ("Metallic",)),
            "roughness": principled_input_linked(material, ("Roughness",)),
            "normal": principled_input_linked(material, ("Normal",)),
            "emission": principled_input_linked(
                material, ("Emission Color", "Emission")
            ),
        }
        material_details[material.name_full] = channels
        material_valid = material_valid and all(channels.values())
    material_valid = material_valid and bool(materials)
    audit.issue(
        "PBR_PRINCIPLED_CHANNELS",
        "pass" if material_valid else "error",
        "Every LOD0 render material must exercise all required Principled PBR channels.",
        actual=material_details,
    )
    audit.report["evidence"]["pbr_materials"] = audit.section_passed(start)
    return {
        "texture_manifest": manifest,
        "image_dimensions": image_dimensions,
        "materials": material_details,
    }


def mesh_clearance_entry(
    obj: bpy.types.Object, attribute_name: str, asset_role: str
) -> tuple[dict[str, Any] | None, list[str]]:
    errors: list[str] = []
    mesh = obj.data
    mesh.calc_loop_triangles()
    attribute = mesh.attributes.get(attribute_name)
    if attribute is None or attribute.domain != "FACE" or attribute.data_type != "INT":
        return None, [
            f"{obj.name_full} requires FACE/INT attribute {attribute_name!r}"
        ]
    if any(len(polygon.vertices) != 3 for polygon in mesh.polygons):
        errors.append(f"{obj.name_full} clearance topology is not triangulated")
    values = [int(item.value) for item in attribute.data]
    if len(values) != len(mesh.polygons):
        errors.append(f"{obj.name_full} semantic count differs from face count")
    valid_ids = SUIT_ZONE_NAMES if asset_role == "suit" else WEAPON_ZONE_NAMES
    unknown_ids = sorted(set(values) - set(valid_ids))
    if unknown_ids:
        errors.append(f"{obj.name_full} contains unknown semantic IDs {unknown_ids}")
    triangles = [tuple(int(index) for index in polygon.vertices) for polygon in mesh.polygons]
    topology_hash = (
        topology_semantics_sha256(triangles, values) if not errors else None
    )
    entry = {
        "name": obj.name_full,
        "asset_role": asset_role,
        "semantic_attribute": attribute_name,
        "face_count": len(mesh.polygons),
        "topology_sha256": topology_hash,
        "semantic_counts": semantic_counts(values),
    }
    return entry, errors


def validate_clearance_semantics(
    profile: Mapping[str, Any], lod0_objects: Sequence[bpy.types.Object], audit: Audit
) -> dict[str, Any]:
    start = audit.begin()
    clearance = profile["clearance"]
    contract_matches_shared = {
        "policy": clearance["policy_version"] == POLICY_VERSION,
        "semantic_schema": clearance["semantic_schema"] == SEMANTIC_SCHEMA,
        "manifest_schema": clearance["manifest_schema"] == MANIFEST_SCHEMA,
        "manifest_text": clearance["manifest_text"] == MANIFEST_TEXT_NAME,
        "suit_attribute": clearance["suit_attribute"] == SUIT_ATTRIBUTE,
        "weapon_attribute": clearance["weapon_attribute"] == WEAPON_ATTRIBUTE,
    }
    audit.issue(
        "CLEARANCE_SHARED_CONTRACT",
        "pass" if all(contract_matches_shared.values()) else "error",
        "WeaponV2 must import the same face-policy identifiers as the clearance gate.",
        actual=contract_matches_shared,
    )
    text = bpy.data.texts.get(MANIFEST_TEXT_NAME)
    try:
        manifest = json.loads(text.as_string()) if text is not None else None
    except json.JSONDecodeError as exc:
        manifest = None
        manifest_errors = [f"Embedded clearance manifest JSON is invalid: {exc}"]
    else:
        manifest_errors = validate_clearance_manifest(manifest)
    if manifest is None and not manifest_errors:
        manifest_errors = [f"Missing canonical Blender Text {MANIFEST_TEXT_NAME}"]
    audit.issue(
        "CLEARANCE_MANIFEST",
        "pass" if not manifest_errors else "error",
        "The embedded canonical clearance manifest must validate without policy errors.",
        actual={"errors": manifest_errors, "manifest_sha256": (
            clearance_manifest_sha256(manifest) if manifest is not None else None
        )},
    )
    object_entries: list[dict[str, Any]] = []
    object_errors: list[str] = []
    raw_manifest_entries = (
        manifest.get("objects", []) if isinstance(manifest, Mapping) else []
    )
    manifest_entries = {
        entry.get("name"): entry
        for entry in raw_manifest_entries
        if isinstance(entry, Mapping)
    }
    for name, expected_entry in sorted(manifest_entries.items()):
        obj = bpy.data.objects.get(name)
        role = str(expected_entry.get("asset_role", ""))
        if obj is None or obj.type != "MESH" or role not in {"suit", "weapon"}:
            object_errors.append(
                f"Manifest object {name!r} is missing, non-mesh, or has invalid role"
            )
            continue
        attribute_name = SUIT_ATTRIBUTE if role == "suit" else WEAPON_ATTRIBUTE
        entry, errors = mesh_clearance_entry(obj, attribute_name, role)
        if entry is not None:
            object_entries.append(entry)
        object_errors.extend(errors)
    actual_entry_names = {entry["name"] for entry in object_entries}
    for obj in lod0_objects:
        if obj.name_full not in actual_entry_names:
            object_errors.append(
                f"Visible WeaponV2 renderer {obj.name_full} is absent from clearance manifest"
            )
    for entry in object_entries:
        expected = manifest_entries.get(entry["name"])
        if expected is None:
            object_errors.append(f"{entry['name']} is absent from clearance manifest")
            continue
        for field in (
            "asset_role",
            "semantic_attribute",
            "face_count",
            "topology_sha256",
            "semantic_counts",
        ):
            if expected.get(field) != entry[field]:
                object_errors.append(
                    f"{entry['name']} manifest field {field} differs from visible mesh"
                )
        obj = bpy.data.objects.get(entry["name"])
        if obj is not None and manifest is not None:
            manifest_hash = clearance_manifest_sha256(manifest)
            expected_props = {
                "ps_clearance_asset_role": entry["asset_role"],
                "ps_clearance_policy_version": POLICY_VERSION,
                "ps_clearance_semantic_schema": SEMANTIC_SCHEMA,
                "ps_clearance_manifest_sha256": manifest_hash,
                "ps_clearance_expected_face_count": entry["face_count"],
                "ps_clearance_topology_sha256": entry["topology_sha256"],
            }
            for name, value in expected_props.items():
                if obj.get(name) != value:
                    object_errors.append(
                        f"{obj.name_full} property {name} differs from manifest evidence"
                    )
    combined_semantic_ids = {
        "suit": set(),
        "weapon": set(),
    }
    for entry in object_entries:
        combined_semantic_ids[entry["asset_role"]].update(
            int(value) for value, count in entry["semantic_counts"].items() if count > 0
        )
    expected_semantic_ids = {
        "suit": set(SUIT_ZONE_NAMES),
        "weapon": set(WEAPON_ZONE_NAMES),
    }
    for role in ("suit", "weapon"):
        missing_ids = sorted(expected_semantic_ids[role] - combined_semantic_ids[role])
        if missing_ids:
            object_errors.append(
                f"{role} manifest geometry does not exercise semantic IDs {missing_ids}"
            )
    audit.issue(
        "CLEARANCE_VISIBLE_WEAPON_SEMANTICS",
        "pass" if not object_errors and bool(object_entries) else "error",
        "Every actual visible LOD0 weapon renderer needs complete hash-bound face semantics.",
        actual={"objects": object_entries, "errors": object_errors},
    )
    audit.report["evidence"]["clearance_semantics"] = audit.section_passed(start)
    return {
        "manifest": manifest,
        "manifest_sha256": (
            clearance_manifest_sha256(manifest) if manifest is not None else None
        ),
        "manifest_objects": object_entries,
        "errors": [*manifest_errors, *object_errors],
    }


def evaluated_world_bvh(obj: bpy.types.Object) -> tuple[BVHTree, bpy.types.Mesh, bpy.types.Object]:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    vertices = [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
    polygons = [tuple(polygon.vertices) for polygon in mesh.polygons]
    return BVHTree.FromPolygons(vertices, polygons, all_triangles=False), mesh, evaluated


def validate_sighting(
    profile: Mapping[str, Any],
    armature: bpy.types.Object | None,
    root: bpy.types.Object | None,
    lod0_objects: Sequence[bpy.types.Object],
    audit: Audit,
) -> dict[str, Any]:
    start = audit.begin()
    sighting = profile["sighting"]
    metrics: dict[str, Any] = {}
    if armature is None or root is None:
        audit.issue("SIGHT_INPUT_MISSING", "error", "Sighting requires armature and RifleRoot.")
        audit.report["evidence"]["sight_and_ocular"] = False
        return metrics
    action = bpy.data.actions.get(sighting["aim_action"])
    visor = bpy.data.objects.get("Helmet_Visor")
    try:
        helpers = weapon_helpers(root, profile["weapon"]["helper_property"])
        ocular = helpers["sight_ocular"]
        muzzle = helpers["muzzle"]
    except Exception as exc:
        audit.issue("SIGHT_HELPERS_MISSING", "error", f"Sighting helper failure: {exc}")
        audit.report["evidence"]["sight_and_ocular"] = False
        return metrics
    if action is None or visor is None:
        audit.issue(
            "SIGHT_ACTION_OR_VISOR_MISSING",
            "error",
            "PS_Aim and Helmet_Visor are required for ocular validation.",
        )
        audit.report["evidence"]["sight_and_ocular"] = False
        return metrics
    activate_action(armature, action)
    bpy.context.scene.frame_set(int(sighting["aim_frame"]))
    bpy.context.view_layer.update()

    right, forward, up = body_basis(armature)
    outward_r, _outward_l = named_shoulder_outward_axes(
        armature, right, forward, up
    )
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated_visor = visor.evaluated_get(depsgraph)
    corners = [
        evaluated_visor.matrix_world @ Vector(corner)
        for corner in evaluated_visor.bound_box
    ]
    center = sum(corners, Vector((0.0, 0.0, 0.0))) / len(corners)
    basis = evaluated_visor.matrix_world.to_3x3()
    visor_right = (basis @ Vector((1.0, 0.0, 0.0))).normalized()
    visor_up = (basis @ Vector((0.0, 0.0, 1.0))).normalized()
    visor_normal = (basis @ Vector((0.0, 1.0, 0.0))).normalized()
    if visor_normal.dot(forward) < 0.0:
        visor_normal = -visor_normal
    front_plane = max(point.dot(visor_normal) for point in corners)
    firing_sign = 1.0 if outward_r.dot(visor_right) >= 0.0 else -1.0
    aiming_eye = center + visor_right * firing_sign * 0.055
    ocular_world = ocular.matrix_world.translation.copy()
    delta = ocular_world - aiming_eye
    rifle_forward = (
        root.matrix_world.to_3x3() @ Vector((0.0, 1.0, 0.0))
    ).normalized()
    lateral = abs(delta.dot(visor_right))
    vertical = abs(delta.dot(visor_up))
    front_clearance = ocular_world.dot(visor_normal) - front_plane
    axis_angle = math.degrees(
        math.acos(max(-1.0, min(1.0, visor_normal.dot(rifle_forward))))
    )
    muzzle_forward = (muzzle.matrix_world.translation - ocular_world).normalized()
    ocular_to_muzzle_point_angle = math.degrees(
        math.acos(max(-1.0, min(1.0, rifle_forward.dot(muzzle_forward))))
    )
    rifle_right = (
        root.matrix_world.to_3x3() @ Vector((1.0, 0.0, 0.0))
    ).normalized()
    ocular_to_muzzle = muzzle.matrix_world.translation - ocular_world
    sight_bore_lateral_offset = abs(ocular_to_muzzle.dot(rifle_right))
    muzzle_forward_distance = ocular_to_muzzle.dot(rifle_forward)
    metrics.update(
        {
            "action": action.name,
            "frame": int(sighting["aim_frame"]),
            "sight_lateral_m": lateral,
            "sight_vertical_m": vertical,
            "ocular_front_clearance_m": front_clearance,
            "sight_axis_angle_deg": axis_angle,
            "ocular_to_muzzle_point_angle_deg": ocular_to_muzzle_point_angle,
            "sight_bore_lateral_offset_m": sight_bore_lateral_offset,
            "muzzle_forward_distance_m": muzzle_forward_distance,
            "ocular_world": list(ocular_world),
            "muzzle_world": list(muzzle.matrix_world.translation),
            "aiming_eye_world": list(aiming_eye),
        }
    )
    metrics_valid = (
        lateral <= sighting["lateral_tolerance_m"]
        and vertical <= sighting["vertical_tolerance_m"]
        and sighting["ocular_front_min_m"]
        <= front_clearance
        <= sighting["ocular_front_max_m"]
        and axis_angle <= sighting["sight_axis_tolerance_deg"]
        and sight_bore_lateral_offset <= 0.005
        and muzzle_forward_distance > 0.5
    )
    audit.issue(
        "SIGHT_ALIGNMENT",
        "pass" if metrics_valid else "error",
        "Aim-frame ocular and bore alignment must meet the shouldered precision envelope.",
        actual=metrics,
        expected=sighting,
    )

    optic_objects = [
        obj
        for obj in lod0_objects
        if str(obj.get(profile["selection"]["role_property"], "")) == "optic"
    ]
    corridor_hits: list[dict[str, Any]] = []
    helmet_optic_overlap_pairs = 0
    visible_suit_optic_overlap_pairs = 0
    evaluated_cleanup: list[tuple[bpy.types.Object, bpy.types.Mesh]] = []
    try:
        if not optic_objects:
            corridor_hits.append({"error": "No explicit optic renderer exists."})
        else:
            optic_bvhs: list[BVHTree] = []
            for obj in optic_objects:
                bvh, mesh, evaluated = evaluated_world_bvh(obj)
                optic_bvhs.append(bvh)
                evaluated_cleanup.append((evaluated, mesh))
            blockers = [
                obj
                for obj in bpy.data.objects
                if obj.type == "MESH"
                and not obj.hide_render
                and obj not in optic_objects
                and (
                    obj in lod0_objects
                    or obj.name_full.startswith("H2_")
                )
            ]
            blocker_bvhs: list[tuple[str, BVHTree]] = []
            for blocker in blockers:
                blocker_bvh, mesh, evaluated = evaluated_world_bvh(blocker)
                evaluated_cleanup.append((evaluated, mesh))
                blocker_bvhs.append((blocker.name_full, blocker_bvh))

            rifle_up = (
                root.matrix_world.to_3x3() @ Vector((0.0, 0.0, 1.0))
            ).normalized()
            half_width = float(sighting["corridor_half_width_m"])
            offsets = (
                (0.0, 0.0),
                (half_width, 0.0),
                (-half_width, 0.0),
                (0.0, half_width),
                (0.0, -half_width),
            )
            corridor_length = max(
                0.5,
                (muzzle.matrix_world.translation - ocular_world).dot(rifle_forward)
                + 0.5,
            )
            for horizontal, vertical_offset in offsets:
                origin = (
                    ocular_world
                    + rifle_forward * 0.015
                    + rifle_right * horizontal
                    + rifle_up * vertical_offset
                )
                for blocker_name, blocker_bvh in blocker_bvhs:
                    hit = blocker_bvh.ray_cast(
                        origin, rifle_forward, corridor_length
                    )
                    if hit[0] is not None:
                        corridor_hits.append(
                            {
                                "blocker": blocker_name,
                                "offset_m": [horizontal, vertical_offset],
                                "distance_m": float(hit[3]),
                            }
                        )

            for blocker_name, blocker_bvh in blocker_bvhs:
                if blocker_name.startswith("H2_"):
                    visible_suit_optic_overlap_pairs += sum(
                        len(optic_bvh.overlap(blocker_bvh))
                        for optic_bvh in optic_bvhs
                    )

            helmet_meshes = [
                obj
                for obj in bpy.data.objects
                if obj.type == "MESH"
                and not obj.hide_render
                and (
                    obj.name_full.startswith("Helmet")
                    or "Helmet" in obj.name_full
                )
            ]
            for helmet in helmet_meshes:
                helmet_bvh, mesh, evaluated = evaluated_world_bvh(helmet)
                evaluated_cleanup.append((evaluated, mesh))
                helmet_optic_overlap_pairs += sum(
                    len(optic_bvh.overlap(helmet_bvh)) for optic_bvh in optic_bvhs
                )
    finally:
        for evaluated, mesh in evaluated_cleanup:
            evaluated.to_mesh_clear()
    audit.issue(
        "SIGHT_OCULAR_OBSTRUCTION",
        "pass"
        if (
            not corridor_hits
            and helmet_optic_overlap_pairs == 0
            and visible_suit_optic_overlap_pairs == 0
        )
        else "error",
        "Five evaluated rays through the ocular corridor must remain clear, and the optic must not overlap actual visible suit or helmet geometry.",
        actual={
            "corridor_hits": corridor_hits,
            "helmet_optic_overlap_pairs": helmet_optic_overlap_pairs,
            "visible_suit_optic_overlap_pairs": visible_suit_optic_overlap_pairs,
        },
        expected={
            "corridor_hits": [],
            "helmet_optic_overlap_pairs": 0,
            "visible_suit_optic_overlap_pairs": 0,
        },
    )
    metrics["corridor_hits"] = corridor_hits
    metrics["helmet_optic_overlap_pairs"] = helmet_optic_overlap_pairs
    metrics["visible_suit_optic_overlap_pairs"] = visible_suit_optic_overlap_pairs
    audit.report["evidence"]["sight_and_ocular"] = audit.section_passed(start)
    return metrics


def validate_review_renders(
    profile: Mapping[str, Any],
    source_path: Path,
    source_sha256: str,
    render_dir: Path,
    audit: Audit,
) -> dict[str, Any]:
    start = audit.begin()
    expected = profile["renders"]["required_filenames"]
    errors = validate_render_set(render_dir, expected)
    # Enforce the profile threshold even if a future helper default changes.
    for name in expected:
        path = render_dir / name
        if path.is_file() and path.stat().st_size < profile["renders"][
            "minimum_file_size_bytes"
        ]:
            errors.append(f"Render is below configured size floor: {name}")
    files = [
        {
            "path": report_path(render_dir / name),
            "sha256": sha256_file(render_dir / name),
            "size_bytes": (render_dir / name).stat().st_size,
        }
        for name in expected
        if (render_dir / name).is_file()
    ]
    builder_manifest_path = source_path.with_suffix(".json")
    builder_manifest_canonical_sha256 = None
    try:
        builder_manifest = json.loads(
            builder_manifest_path.read_text(encoding="utf-8")
        )
        builder_manifest_canonical_sha256 = sha256_manifest(builder_manifest)
        bound_errors = validate_bound_render_manifest(
            builder_manifest,
            REPOSITORY_ROOT,
            render_dir,
            expected,
            source_sha256,
        )
        projection_errors = validate_projection_evidence(
            builder_manifest.get("projection_evidence"), expected
        )
    except Exception as exc:
        builder_manifest = None
        bound_errors = [f"Could not load builder render manifest: {exc}"]
        projection_errors = ["Could not validate source-bound projection evidence"]
    errors.extend(bound_errors)
    errors.extend(projection_errors)
    audit.issue(
        "REVIEW_RENDER_MANIFEST",
        "pass" if not errors else "error",
        "Review evidence must contain 13 unique, source-bound, correctly framed PNGs.",
        actual={
            "files": files,
            "builder_manifest_path": report_path(builder_manifest_path),
            "builder_manifest_canonical_sha256": (
                builder_manifest_canonical_sha256
            ),
            "errors": errors,
            "projection_errors": projection_errors,
        },
    )
    audit.report["evidence"]["review_renders"] = audit.section_passed(start)
    return {
        "directory": report_path(render_dir),
        "files": files,
        "builder_manifest_path": report_path(builder_manifest_path),
        "builder_manifest_canonical_sha256": (
            builder_manifest_canonical_sha256
        ),
        "errors": errors,
        "projection_errors": projection_errors,
    }


def run_validation(args: argparse.Namespace) -> dict[str, Any]:
    profile_path = absolute(args.profile)
    source_path = absolute(args.source)
    report_output = absolute(args.report)
    render_dir = absolute(args.render_dir)
    profile = load_profile(profile_path)
    report: dict[str, Any] = {
        "schema_version": 1,
        "asset": profile["asset"],
        "blender_version": bpy.app.version_string,
        "profile": {
            "path": report_path(profile_path),
            "sha256": sha256_file(profile_path),
        },
        "source": {
            "path": report_path(source_path),
            "sha256_before": None,
            "sha256_after": None,
            "immutable": False,
        },
        "issues": [],
        "evidence": {name: False for name in REQUIRED_EVIDENCE},
        "promotion_authorized": False,
    }
    audit = Audit(report)
    if tuple(bpy.app.version[:2]) < (5, 2):
        audit.issue(
            "BLENDER_VERSION",
            "error",
            "Candidate006 production validation requires Blender 5.2 or newer.",
            actual=bpy.app.version_string,
        )
    if not source_path.is_file():
        missing = missing_source_report(report_path(source_path))
        missing.update(
            {
                "asset": profile["asset"],
                "blender_version": bpy.app.version_string,
                "profile": report["profile"],
            }
        )
        write_canonical_json(report_output, missing)
        return missing

    source_hash_before = sha256_file(source_path)
    report["source"]["sha256_before"] = source_hash_before
    immutable_start = audit.begin()
    report["immutable_inputs"] = verify_immutable_inputs(profile, audit)
    report["evidence"]["immutable_inputs"] = audit.section_passed(immutable_start)
    bpy.ops.wm.open_mainfile(filepath=str(source_path))

    armature, report["rig_and_actions"] = validate_rig_and_actions(profile, audit)
    root, report["weapon_contract"] = validate_weapon_contract(profile, armature, audit)
    lod_objects, report["lods"] = validate_lods(profile, armature, audit)
    report["weapon_skin_motion"] = validate_weapon_skin_motion(
        profile, armature, lod_objects.get(0, []), audit
    )
    report["pbr"] = validate_pbr(profile, root, lod_objects.get(0, []), audit)
    report["clearance_semantics"] = validate_clearance_semantics(
        profile, lod_objects.get(0, []), audit
    )
    report["sighting"] = validate_sighting(
        profile, armature, root, lod_objects.get(0, []), audit
    )
    report["review_renders"] = validate_review_renders(
        profile, source_path, source_hash_before, render_dir, audit
    )

    source_hash_after = sha256_file(source_path)
    report["source"]["sha256_after"] = source_hash_after
    source_preserved = source_hash_before == source_hash_after
    report["source"]["immutable"] = source_preserved
    audit.issue(
        "SOURCE_IMMUTABLE",
        "pass" if source_preserved else "error",
        "Validation must not mutate the Candidate006 source blend.",
        actual=source_hash_after,
        expected=source_hash_before,
    )
    report["evidence"]["source_immutability"] = source_preserved

    finalise_report(report)
    write_canonical_json(report_output, report)
    return report


def main() -> int:
    args = parse_args()
    try:
        report = run_validation(args)
    except Exception as exc:
        # Even unexpected adapter failures create a deterministic blocking report.
        report_path_value = absolute(args.report)
        report = {
            "schema_version": 1,
            "status": "FAIL",
            "promotion_authorized": False,
            "summary": {"error": 1, "warning": 0, "pass": 0},
            "issues": [
                {
                    "code": "VALIDATOR_EXCEPTION",
                    "severity": "error",
                    "message": f"{type(exc).__name__}: {exc}",
                    "traceback": traceback.format_exc(),
                }
            ],
            "evidence": {name: False for name in REQUIRED_EVIDENCE},
        }
        write_canonical_json(report_path_value, report)
    print(
        f"WeaponV2 Candidate006 validation: {report['status']} "
        f"(errors={report['summary']['error']}, warnings={report['summary']['warning']})"
    )
    return 0 if args.soft_fail or report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
