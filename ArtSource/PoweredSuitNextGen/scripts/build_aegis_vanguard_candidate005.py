"""Build the isolated Aegis Vanguard Candidate005 production-architecture pass.

Candidate005 consumes the review-only Candidate004 blend, never Generator114 or
an active Unity asset.  It improves the silhouette and measured weapon envelopes,
then converts the visible suit into three renderer meshes: a continuous skinned
undersuit, rigid-weighted consolidated armor, and consolidated emissives.

This remains a candidate.  Automated skinning, UVs, preview PBR detail and draft
LODs prove the production path; they do not replace final sculpt/retopo/paint or
owner review.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import math
import sys
from collections import defaultdict, deque
from pathlib import Path
from typing import Iterable

import bmesh  # type: ignore
import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore


ROOT = Path(__file__).resolve().parents[3]
NEXTGEN = ROOT / "ArtSource" / "PoweredSuitNextGen"
SOURCE_BLEND = NEXTGEN / "candidates" / "aegis_vanguard_candidate_v004.blend"
SOURCE_REPORT = NEXTGEN / "candidates" / "aegis_vanguard_candidate_v004.json"
OUTPUT_BLEND = NEXTGEN / "candidates" / "aegis_vanguard_candidate_v005.blend"
OUTPUT_REPORT = NEXTGEN / "candidates" / "aegis_vanguard_candidate_v005.json"
RENDER_ROOT = NEXTGEN / "renders" / "aegis_vanguard_candidate_v005"
TEXTURE_ROOT = NEXTGEN / "textures" / "candidate005"
TEXTURE_MANIFEST = TEXTURE_ROOT / "manifest.json"
BUILDER004_PATH = NEXTGEN / "scripts" / "build_aegis_vanguard_candidate.py"
COLLECTION004 = "Aegis_Vanguard_Candidate004"
COLLECTION005 = "Aegis_Vanguard_Candidate005"
ARMATURE_NAME = "PowerSuit_Armature"
CLEARANCE_PROXY_PROPERTY = "aegis_clearance_proxy"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_builder004():
    specification = importlib.util.spec_from_file_location(
        "aegis_candidate004_builder", BUILDER004_PATH
    )
    if specification is None or specification.loader is None:
        raise RuntimeError(f"Could not load Candidate004 builder: {BUILDER004_PATH}")
    module = importlib.util.module_from_spec(specification)
    sys.modules[specification.name] = module
    specification.loader.exec_module(module)
    module.RENDER_ROOT = RENDER_ROOT
    return module


def expected_source_hash() -> str:
    report = json.loads(SOURCE_REPORT.read_text(encoding="utf-8"))
    return str(report["candidate_blend_sha256"])


def candidate_source_objects(collection: bpy.types.Collection) -> list[bpy.types.Object]:
    return sorted(
        [
            obj
            for obj in collection.objects
            if bool(obj.get("aegis_vanguard_candidate", False))
            and not bool(obj.get("aegis_runtime_anchor", False))
            and obj.type == "MESH"
        ],
        key=lambda item: item.name_full,
    )


def world_scale_mesh(
    obj: bpy.types.Object,
    pivot: Vector,
    scale: tuple[float, float, float],
) -> None:
    inverse = obj.matrix_world.inverted_safe()
    factors = Vector(scale)
    for vertex in obj.data.vertices:
        world = obj.matrix_world @ vertex.co
        world = pivot + Vector(tuple((world[index] - pivot[index]) * factors[index] for index in range(3)))
        vertex.co = inverse @ world
    obj.data.update()


def curve_chest_plate(obj: bpy.types.Object, strength: float = 0.025) -> None:
    inverse = obj.matrix_world.inverted_safe()
    for vertex in obj.data.vertices:
        world = obj.matrix_world @ vertex.co
        lateral = min(1.0, abs(world.x) / 0.34)
        world.y += strength * (1.0 - lateral * lateral)
        vertex.co = inverse @ world
    obj.data.update()


def improve_silhouette(objects: Iterable[bpy.types.Object]) -> None:
    for obj in objects:
        name = obj.name
        if any(token in name for token in ("Helmet", "Face", "Visor", "Optic", "Chin", "Jaw")):
            world_scale_mesh(obj, Vector((0.0, 0.0, 1.94)), (0.88, 0.92, 0.91))
        if name.startswith("AV_Collar"):
            world_scale_mesh(obj, Vector((0.0, 0.0, 1.72)), (0.84, 0.74, 0.94))
        if name.startswith(("AV_Pectoral", "AV_GothicRib", "AV_ChestFastener")):
            world_scale_mesh(obj, Vector((0.0, 0.0, 1.52)), (0.91, 0.82, 0.96))
            curve_chest_plate(obj)
        if any(token in name for token in ("Forearm", "Gauntlet", "Knuckle")):
            center = sum((obj.matrix_world @ Vector(corner) for corner in obj.bound_box), Vector()) / 8.0
            world_scale_mesh(obj, center, (0.78, 0.76, 0.86))
        if name.startswith(("AV_Finger", "AV_Fingertip", "AV_Thumb")):
            center = sum((obj.matrix_world @ Vector(corner) for corner in obj.bound_box), Vector()) / 8.0
            world_scale_mesh(obj, center, (1.12, 1.08, 0.86))
        if name.startswith(("AV_Boot", "AV_Sole")):
            side = 1.0 if name.endswith(".L") else -1.0
            world_scale_mesh(obj, Vector((0.17 * side, 0.08, 0.105)), (0.86, 0.80, 0.94))
        if name.startswith(("AV_Shoulder",)):
            side = 1.0 if name.endswith(".L") else -1.0
            world_scale_mesh(obj, Vector((0.34 * side, 0.0, 1.62)), (0.78, 0.72, 0.90))
        if name.startswith(("AV_Turbine", "AV_BackpackFairing")):
            side = 1.0 if ".L" in name else -1.0
            world_scale_mesh(obj, Vector((0.45 * side, -0.35, 1.57)), (0.82, 0.78, 0.82))
        if name.startswith(("AV_TurbineBrace", "AV_TurbineFeed")):
            side = 1.0 if ".L" in name else -1.0
            world_scale_mesh(obj, Vector((0.45 * side, -0.30, 1.54)), (0.72, 0.88, 0.88))
        if name.startswith(("AV_BackpackSpine", "AV_BackSpine", "AV_BackpackStatus")):
            world_scale_mesh(obj, Vector((0.0, -0.26, 1.48)), (0.55, 0.54, 0.92))
        if name in {"AV_UnderChest", "AV_UnderAbdomen", "AV_UnderPelvis", "AV_ChestLoadFrame"}:
            center = sum((obj.matrix_world @ Vector(corner) for corner in obj.bound_box), Vector()) / 8.0
            world_scale_mesh(obj, center + Vector((0.0, 0.025, 0.0)), (0.92, 0.72, 0.96))


def smooth_seed_shells(objects: Iterable[bpy.types.Object]) -> None:
    for obj in objects:
        for polygon in obj.data.polygons:
            polygon.use_smooth = True
        bevel = next((modifier for modifier in obj.modifiers if modifier.type == "BEVEL"), None)
        if bevel is not None:
            bevel.width = min(float(bevel.width), 0.004)
            bevel.segments = max(2, int(bevel.segments))
        obj.data.update()


def material_signature(obj: bpy.types.Object) -> tuple[tuple[float, float, float, float], float, float]:
    name = ""
    if obj.material_slots and obj.material_slots[0].material is not None:
        name = obj.material_slots[0].material.name
    table = {
        "AV_SootBlackArmor": ((0.010, 0.014, 0.021, 1.0), 0.14, 0.46),
        "AV_CarbonComposite": ((0.006, 0.010, 0.016, 1.0), 0.04, 0.62),
        "AV_BlueBlackCarbon": ((0.006, 0.020, 0.030, 1.0), 0.05, 0.56),
        "AV_CarbonUndersuit": ((0.004, 0.006, 0.010, 1.0), 0.02, 0.73),
        "AV_BraidedCarbonCable": ((0.005, 0.007, 0.011, 1.0), 0.03, 0.64),
        "AV_TarnishedChrome": ((0.20, 0.25, 0.32, 1.0), 0.92, 0.31),
        "AV_OilyGunmetal": ((0.035, 0.050, 0.070, 1.0), 0.76, 0.42),
        "AV_WornChromeDetail": ((0.31, 0.38, 0.48, 1.0), 0.96, 0.25),
        "AV_ExhaustSoot": ((0.002, 0.003, 0.005, 1.0), 0.0, 0.90),
        "AV_CyanEmission": ((0.00, 0.62, 0.82, 1.0), 0.34, 0.20),
    }
    return table.get(name, ((0.014, 0.018, 0.026, 1.0), 0.18, 0.52))


def is_emissive(obj: bpy.types.Object) -> bool:
    return any(
        slot.material is not None and slot.material.name == "AV_CyanEmission"
        for slot in obj.material_slots
    )


def is_undersuit_seed(obj: bpy.types.Object) -> bool:
    name = obj.name
    # The relaxed REST pose places the rigid palms and hip housings very close
    # to the opposite limb.  Feeding those shells to one voxel union creates
    # false arm-to-thigh bridges that explode under aim/reload deformation.
    # They remain rigid-weighted armor while the anatomical arm, torso and leg
    # shells still meet continuously at the shoulders and pelvis.
    if any(token in name for token in ("HandPalm", "HipJoint", "Gasket")):
        return False
    return (
        "Under" in name
        or "Joint" in name
        or "Gasket" in name
        or "HandPalm" in name
        or "BootBody" in name
    ) and not is_emissive(obj)


def combined_geometry(
    objects: Iterable[bpy.types.Object], armature: bpy.types.Object
) -> tuple[list[Vector], list[tuple[int, int, int]], list[str], list[tuple[float, float, float, float]], list[tuple[float, float, float, float]]]:
    inverse_armature = armature.matrix_world.inverted_safe()
    vertices: list[Vector] = []
    triangles: list[tuple[int, int, int]] = []
    bones: list[str] = []
    colors: list[tuple[float, float, float, float]] = []
    surfaces: list[tuple[float, float, float, float]] = []
    for obj in sorted(objects, key=lambda item: item.name_full):
        mesh = obj.data
        mesh.calc_loop_triangles()
        offset = len(vertices)
        transform = inverse_armature @ obj.matrix_world
        color, metallic, roughness = material_signature(obj)
        bone = str(obj.parent_bone) if obj.parent_type == "BONE" else "Chest"
        for vertex in mesh.vertices:
            vertices.append(transform @ vertex.co)
            bones.append(bone)
            colors.append(color)
            surfaces.append((metallic, roughness, 1.0, 1.0))
        triangles.extend(
            tuple(offset + int(index) for index in triangle.vertices)
            for triangle in mesh.loop_triangles
        )
    return vertices, triangles, bones, colors, surfaces


def set_point_colors(
    mesh: bpy.types.Mesh,
    colors: list[tuple[float, float, float, float]],
    surfaces: list[tuple[float, float, float, float]],
) -> None:
    base = mesh.color_attributes.new(name="H2BaseColor", type="FLOAT_COLOR", domain="POINT")
    surface = mesh.color_attributes.new(name="H2Surface", type="FLOAT_COLOR", domain="POINT")
    for index, color in enumerate(colors):
        base.data[index].color = color
        surface.data[index].color = surfaces[index]


def bind_rigid_weights(
    obj: bpy.types.Object, armature: bpy.types.Object, bones: list[str]
) -> None:
    groups: dict[str, list[int]] = defaultdict(list)
    for index, bone in enumerate(bones):
        if bone not in armature.data.bones:
            raise RuntimeError(f"Unknown rigid-weight bone '{bone}' on {obj.name}.")
        groups[bone].append(index)
    for bone, indices in groups.items():
        obj.vertex_groups.new(name=bone).add(indices, 1.0, "REPLACE")
    attach_armature(obj, armature)


def attach_armature(obj: bpy.types.Object, armature: bpy.types.Object) -> None:
    obj.parent = armature
    obj.parent_type = "OBJECT"
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = Matrix.Identity(4)
    modifier = obj.modifiers.new("HeroV2_Armature", "ARMATURE")
    modifier.object = armature
    modifier.use_deform_preserve_volume = True


def close_and_triangulate(mesh: bpy.types.Mesh) -> int:
    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary = [edge for edge in bm.edges if edge.is_boundary]
    boundary_count = len(boundary)
    if boundary:
        bmesh.ops.holes_fill(bm, edges=boundary)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update(calc_edges=True)
    return boundary_count


def create_consolidated_renderer(
    name: str,
    objects: list[bpy.types.Object],
    collection: bpy.types.Collection,
    armature: bpy.types.Object,
    material: bpy.types.Material,
) -> tuple[bpy.types.Object, int]:
    vertices, triangles, bones, colors, surfaces = combined_geometry(objects, armature)
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], triangles)
    mesh.update(calc_edges=True)
    set_point_colors(mesh, colors, surfaces)
    repaired_boundaries = close_and_triangulate(mesh)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    mesh.materials.append(material)
    bind_rigid_weights(obj, armature, bones)
    obj["aegis_vanguard_candidate"] = True
    obj["hero_v2_asset"] = "suit"
    obj["hero_v2_lod"] = 0
    obj["hero_v2_renderer"] = name
    return obj, repaired_boundaries


def create_continuous_undersuit(
    seeds: list[bpy.types.Object],
    collection: bpy.types.Collection,
    armature: bpy.types.Object,
    material: bpy.types.Material,
) -> bpy.types.Object:
    vertices, triangles, _bones, _colors, _surfaces = combined_geometry(seeds, armature)
    mesh = bpy.data.meshes.new("H2_Undersuit_LOD0_Mesh")
    mesh.from_pydata(vertices, [], triangles)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new("H2_Undersuit_LOD0", mesh)
    collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    mesh.remesh_voxel_size = 0.018
    mesh.remesh_voxel_adaptivity = 0.0
    bpy.ops.object.voxel_remesh()
    smooth = obj.modifiers.new("H2_UndersuitRelax", "SMOOTH")
    smooth.factor = 0.38
    smooth.iterations = 8
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    close_and_triangulate(mesh)
    colors = [(0.004, 0.006, 0.010, 1.0)] * len(mesh.vertices)
    surfaces = [(0.02, 0.73, 1.0, 1.0)] * len(mesh.vertices)
    set_point_colors(mesh, colors, surfaces)
    mesh.materials.append(material)
    bind_smooth_weights(obj, armature)
    obj["aegis_vanguard_candidate"] = True
    obj["hero_v2_asset"] = "suit"
    obj["hero_v2_lod"] = 0
    obj["hero_v2_renderer"] = obj.name
    return obj


def point_segment_distance(point: Vector, start: Vector, end: Vector) -> float:
    segment = end - start
    if segment.length_squared < 1.0e-12:
        return (point - start).length
    factor = max(0.0, min(1.0, (point - start).dot(segment) / segment.length_squared))
    return (point - (start + segment * factor)).length


def bind_smooth_weights(obj: bpy.types.Object, armature: bpy.types.Object) -> None:
    excluded = {"Root", "WeaponRoot", "WeaponMagazine", "WeaponBolt"}
    bones = [bone for bone in armature.data.bones if bone.use_deform and bone.name not in excluded]
    bones_by_name = {bone.name: bone for bone in bones}
    groups = {bone.name: obj.vertex_groups.new(name=bone.name) for bone in bones}
    vertex_weights: list[dict[str, float]] = []
    for vertex in obj.data.vertices:
        point = vertex.co
        absolute_x = abs(point.x)
        side = "L" if point.x >= 0.0 else "R"
        if point.z >= 1.18:
            # Every upper-body vertex sees the same core chain plus only the
            # anatomically correct arm.  This keeps the chest/shoulder seam
            # continuous without ever blending the sternum to both arms.
            candidate_names = [
                "Hips",
                "Spine",
                "Chest",
                "Neck",
                "Head",
                f"Shoulder.{side}",
                f"UpperArm.{side}",
                f"LowerArm.{side}",
                f"Hand.{side}",
            ]
        elif absolute_x >= 0.295 and point.z >= 0.45:
            candidate_names = [
                "Chest",
                f"Shoulder.{side}",
                f"UpperArm.{side}",
                f"LowerArm.{side}",
                f"Hand.{side}",
            ]
        elif point.z < 1.18 and absolute_x >= 0.040:
            candidate_names = [
                "Hips",
                f"UpperLeg.{side}",
                f"LowerLeg.{side}",
                f"Foot.{side}",
            ]
        else:
            candidate_names = ["Hips", "Spine", "Chest", "Neck", "Head"]
        candidate_bones = [bones_by_name[name] for name in candidate_names]
        candidates: list[tuple[float, str]] = []
        for bone in candidate_bones:
            distance = point_segment_distance(point, bone.head_local, bone.tail_local)
            candidates.append((distance, bone.name))
        selected = sorted(candidates)[:4]
        raw = [(math.exp(-distance * 28.0), name) for distance, name in selected]
        raw = [(weight, name) for weight, name in raw if weight >= 0.002]
        if not raw:
            raw = [(1.0, selected[0][1])]
        total = sum(weight for weight, _name in raw)
        vertex_weights.append({name: weight / total for weight, name in raw})

    adjacency = [set() for _vertex in obj.data.vertices]
    for edge in obj.data.edges:
        first, second = edge.vertices
        adjacency[first].add(second)
        adjacency[second].add(first)
    for _iteration in range(4):
        smoothed: list[dict[str, float]] = []
        for index, current in enumerate(vertex_weights):
            neighbors = adjacency[index]
            names = set(current)
            for neighbor in neighbors:
                names.update(vertex_weights[neighbor])
            mixed: dict[str, float] = {}
            for name in names:
                neighbor_average = (
                    sum(vertex_weights[neighbor].get(name, 0.0) for neighbor in neighbors)
                    / max(1, len(neighbors))
                )
                value = current.get(name, 0.0) * 0.58 + neighbor_average * 0.42
                if value >= 0.0005:
                    mixed[name] = value
            point = obj.data.vertices[index].co
            if abs(point.x) >= 0.02:
                wrong_side = ".R" if point.x > 0.0 else ".L"
                mixed = {name: value for name, value in mixed.items() if not name.endswith(wrong_side)}
            smoothed.append(mixed)
        vertex_weights = smoothed

    blended_vertices = 0
    for index, weights in enumerate(vertex_weights):
        strongest = sorted(weights.items(), key=lambda item: item[1], reverse=True)[:4]
        total = sum(weight for _name, weight in strongest)
        normalized = [(name, weight / total) for name, weight in strongest]
        if len(normalized) > 1:
            blended_vertices += 1
        for name, weight in normalized:
            groups[name].add([index], weight, "REPLACE")
    attach_armature(obj, armature)
    obj["hero_v2_blended_vertex_count"] = blended_vertices


def connected_components(mesh: bpy.types.Mesh) -> int:
    adjacency = [set() for _vertex in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        adjacency[a].add(b)
        adjacency[b].add(a)
    unseen = set(range(len(mesh.vertices)))
    count = 0
    while unseen:
        count += 1
        start = unseen.pop()
        queue = deque([start])
        while queue:
            current = queue.popleft()
            for neighbor in adjacency[current]:
                if neighbor in unseen:
                    unseen.remove(neighbor)
                    queue.append(neighbor)
    return count


def image(name: str, non_color: bool = False) -> bpy.types.Image:
    path = TEXTURE_ROOT / name
    if not path.is_file():
        raise RuntimeError(f"Candidate005 preview texture is missing: {path}")
    loaded = bpy.data.images.load(str(path), check_existing=True)
    if non_color:
        loaded.colorspace_settings.name = "Non-Color"
    return loaded


def preview_material(name: str, emissive: bool = False) -> bpy.types.Material:
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    vertex_color = nodes.new("ShaderNodeVertexColor")
    vertex_color.layer_name = "H2BaseColor"
    surface = nodes.new("ShaderNodeVertexColor")
    surface.layer_name = "H2Surface"
    separate = nodes.new("ShaderNodeSeparateColor")
    links.new(surface.outputs["Color"], separate.inputs["Color"])
    links.new(separate.outputs["Red"], shader.inputs["Metallic"])
    links.new(separate.outputs["Green"], shader.inputs["Roughness"])

    if emissive:
        coordinates = nodes.new("ShaderNodeTexCoord")
        emission_image = nodes.new("ShaderNodeTexImage")
        emission_image.image = image("AV_H2_Detail_Emission.png", True)
        emission_mix = nodes.new("ShaderNodeMixRGB")
        emission_mix.blend_type = "MULTIPLY"
        emission_mix.inputs[0].default_value = 1.0
        links.new(coordinates.outputs["UV"], emission_image.inputs["Vector"])
        links.new(vertex_color.outputs["Color"], emission_mix.inputs[1])
        links.new(emission_image.outputs["Color"], emission_mix.inputs[2])
        links.new(emission_mix.outputs["Color"], shader.inputs["Base Color"])
        emission_input = shader.inputs.get("Emission Color") or shader.inputs.get("Emission")
        if emission_input is not None:
            links.new(emission_mix.outputs["Color"], emission_input)
        if shader.inputs.get("Emission Strength") is not None:
            shader.inputs["Emission Strength"].default_value = 2.2
    else:
        coordinates = nodes.new("ShaderNodeTexCoord")
        mapping = nodes.new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (12.0, 12.0, 12.0)
        base_image = nodes.new("ShaderNodeTexImage")
        base_image.image = image("AV_H2_Detail_BaseColor.png")
        detail_mix = nodes.new("ShaderNodeMixRGB")
        detail_mix.blend_type = "MULTIPLY"
        detail_mix.inputs[0].default_value = 0.055
        normal_image = nodes.new("ShaderNodeTexImage")
        normal_image.image = image("AV_H2_Detail_Normal.png", True)
        mrao_image = nodes.new("ShaderNodeTexImage")
        mrao_image.image = image("AV_H2_Detail_MRAO.png", True)
        mrao_separate = nodes.new("ShaderNodeSeparateColor")
        metallic_multiply = nodes.new("ShaderNodeMath")
        metallic_multiply.operation = "MULTIPLY"
        detail_strength = nodes.new("ShaderNodeMath")
        detail_strength.operation = "MULTIPLY"
        detail_strength.inputs[1].default_value = 0.08
        ao_mix = nodes.new("ShaderNodeMixRGB")
        ao_mix.blend_type = "MULTIPLY"
        ao_mix.inputs[0].default_value = 0.25
        smoothness_to_roughness = nodes.new("ShaderNodeMath")
        smoothness_to_roughness.operation = "SUBTRACT"
        smoothness_to_roughness.inputs[0].default_value = 1.0
        surface_roughness_scale = nodes.new("ShaderNodeMath")
        surface_roughness_scale.operation = "MULTIPLY"
        surface_roughness_scale.inputs[1].default_value = 0.75
        texture_roughness_scale = nodes.new("ShaderNodeMath")
        texture_roughness_scale.operation = "MULTIPLY"
        texture_roughness_scale.inputs[1].default_value = 0.25
        roughness_add = nodes.new("ShaderNodeMath")
        roughness_add.operation = "ADD"
        normal = nodes.new("ShaderNodeNormalMap")
        normal.inputs["Strength"].default_value = 0.14
        links.new(coordinates.outputs["UV"], mapping.inputs["Vector"])
        links.new(mapping.outputs["Vector"], base_image.inputs["Vector"])
        links.new(mapping.outputs["Vector"], normal_image.inputs["Vector"])
        links.new(mapping.outputs["Vector"], mrao_image.inputs["Vector"])
        links.new(vertex_color.outputs["Color"], detail_mix.inputs[1])
        links.new(base_image.outputs["Color"], detail_mix.inputs[2])
        links.new(normal_image.outputs["Color"], normal.inputs["Color"])
        links.new(normal.outputs["Normal"], shader.inputs["Normal"])
        links.new(mrao_image.outputs["Color"], mrao_separate.inputs["Color"])
        links.new(mrao_separate.outputs["Blue"], detail_strength.inputs[0])
        links.new(detail_strength.outputs["Value"], detail_mix.inputs[0])
        links.new(detail_mix.outputs["Color"], ao_mix.inputs[1])
        links.new(mrao_separate.outputs["Green"], ao_mix.inputs[2])
        links.new(ao_mix.outputs["Color"], shader.inputs["Base Color"])
        links.new(separate.outputs["Red"], metallic_multiply.inputs[0])
        links.new(mrao_separate.outputs["Red"], metallic_multiply.inputs[1])
        links.new(metallic_multiply.outputs["Value"], shader.inputs["Metallic"])
        links.new(mrao_image.outputs["Alpha"], smoothness_to_roughness.inputs[1])
        links.new(separate.outputs["Green"], surface_roughness_scale.inputs[0])
        links.new(
            smoothness_to_roughness.outputs["Value"],
            texture_roughness_scale.inputs[0],
        )
        links.new(surface_roughness_scale.outputs["Value"], roughness_add.inputs[0])
        links.new(texture_roughness_scale.outputs["Value"], roughness_add.inputs[1])
        links.new(roughness_add.outputs["Value"], shader.inputs["Roughness"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return material


def unwrap_uv0(objects: list[bpy.types.Object]) -> None:
    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        while obj.data.uv_layers:
            obj.data.uv_layers.remove(obj.data.uv_layers[0])
        obj.data.uv_layers.new(name="UV0")
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(62.0), island_margin=0.005)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.005)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")


def audit_uv0_overlaps(objects: list[bpy.types.Object]) -> dict[str, object]:
    """Use Blender's own UV overlap selection on the globally packed atlas."""
    previous_uv_sync = bpy.context.scene.tool_settings.use_uv_select_sync
    bpy.context.scene.tool_settings.use_uv_select_sync = False
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    try:
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.select_all(action="DESELECT")
        bpy.ops.uv.select_overlap()
        bpy.ops.object.mode_set(mode="OBJECT")
    finally:
        if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        bpy.context.scene.tool_settings.use_uv_select_sync = previous_uv_sync
    per_renderer: dict[str, dict[str, int]] = {}
    total_faces = 0
    total_loops = 0
    for obj in objects:
        uv_layer = obj.data.uv_layers.get("UV0")
        if uv_layer is None:
            raise RuntimeError(f"{obj.name} lost required UV0 before overlap audit.")
        loop_selection = obj.data.attributes.get(".uv_select_vert")
        face_selection = obj.data.attributes.get(".uv_select_face")
        selected_loops = (
            sum(1 for item in loop_selection.data if bool(item.value))
            if loop_selection is not None
            else 0
        )
        selected_faces = (
            sum(1 for item in face_selection.data if bool(item.value))
            if face_selection is not None
            else 0
        )
        per_renderer[obj.name] = {
            "selected_overlap_faces": selected_faces,
            "selected_overlap_loops": selected_loops,
        }
        total_faces += selected_faces
        total_loops += selected_loops
    bpy.ops.object.select_all(action="DESELECT")
    if total_faces or total_loops:
        raise RuntimeError(
            f"Candidate005 UV0 overlap audit failed: {total_faces} faces / "
            f"{total_loops} loops selected."
        )
    return {
        "method": "Blender uv.select_overlap across all three LOD0 renderers",
        "selected_overlap_faces": total_faces,
        "selected_overlap_loops": total_loops,
        "per_renderer": per_renderer,
    }


def mark_clearance_proxies(objects: Iterable[bpy.types.Object]) -> None:
    for obj in objects:
        obj[CLEARANCE_PROXY_PROPERTY] = True
        obj["aegis_vanguard_candidate"] = False
        obj.hide_render = True
        obj.hide_set(True)


def validate_render_architecture(objects: list[bpy.types.Object], armature: bpy.types.Object) -> dict[str, object]:
    if [obj.name for obj in objects] != ["H2_Undersuit_LOD0", "H2_Armor_LOD0", "H2_Emission_LOD0"]:
        raise RuntimeError("Candidate005 must expose exactly the three canonical suit renderers.")
    weight_violations = []
    for obj in objects:
        if len(obj.material_slots) != 1:
            raise RuntimeError(f"{obj.name} must use exactly one preview material.")
        if obj.parent != armature or not any(mod.type == "ARMATURE" for mod in obj.modifiers):
            raise RuntimeError(f"{obj.name} is not bound to the canonical armature.")
        for vertex in obj.data.vertices:
            influences = [group.weight for group in vertex.groups if group.weight > 0.0]
            if not influences or len(influences) > 4 or abs(sum(influences) - 1.0) > 0.001:
                weight_violations.append((obj.name, vertex.index, len(influences), sum(influences)))
    if weight_violations:
        raise RuntimeError(f"Candidate005 has invalid skin weights: {weight_violations[:8]}")
    components = connected_components(objects[0].data)
    if components != 1:
        raise RuntimeError(f"Continuous undersuit has {components} connected components, expected 1.")
    return {
        "renderer_count": len(objects),
        "draw_call_estimate": sum(len(obj.material_slots) for obj in objects),
        "undersuit_connected_components": components,
        "undersuit_blended_vertices": int(objects[0].get("hero_v2_blended_vertex_count", 0)),
        "triangle_counts": {
            obj.name: sum(max(0, len(face.vertices) - 2) for face in obj.data.polygons)
            for obj in objects
        },
        "uv0_face_coverage": {
            obj.name: 1.0 if obj.data.uv_layers.get("UV0") is not None else 0.0
            for obj in objects
        },
    }


def action_keyframes(action: bpy.types.Action) -> list[float]:
    frames: set[float] = set()
    for layer in getattr(action, "layers", ()):
        for strip in layer.strips:
            for channelbag in getattr(strip, "channelbags", ()):
                for fcurve in channelbag.fcurves:
                    frames.update(float(point.co.x) for point in fcurve.keyframe_points)
    for fcurve in getattr(action, "fcurves", ()):
        frames.update(float(point.co.x) for point in fcurve.keyframe_points)
    if not frames:
        frames.update(float(value) for value in action.frame_range)
    return sorted(frames)


def audit_undersuit_deformation(
    undersuit: bpy.types.Object,
    armature: bpy.types.Object,
    activate_action,
) -> dict[str, object]:
    armature.data.pose_position = "POSE"
    rest_lengths = [
        (undersuit.data.vertices[edge.vertices[0]].co - undersuit.data.vertices[edge.vertices[1]].co).length
        for edge in undersuit.data.edges
    ]
    maximum_ratio = 0.0
    maximum_location: dict[str, object] = {}
    minimum_area = math.inf
    sampled_frames = 0
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for action in sorted(
        (action for action in bpy.data.actions if action.name.startswith("PS_")),
        key=lambda item: item.name,
    ):
        activate_action(armature, action)
        for frame in action_keyframes(action):
            integer = math.floor(frame)
            bpy.context.scene.frame_set(integer, subframe=frame - integer)
            bpy.context.view_layer.update()
            evaluated = undersuit.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            mesh.calc_loop_triangles()
            if len(mesh.vertices) != len(undersuit.data.vertices):
                evaluated.to_mesh_clear()
                raise RuntimeError("Armature evaluation changed undersuit vertex order/count.")
            if any(not math.isfinite(component) for vertex in mesh.vertices for component in vertex.co):
                evaluated.to_mesh_clear()
                raise RuntimeError(f"Non-finite undersuit vertex in {action.name} at frame {frame}.")
            for index, edge in enumerate(mesh.edges):
                rest = rest_lengths[index]
                posed = (mesh.vertices[edge.vertices[0]].co - mesh.vertices[edge.vertices[1]].co).length
                ratio = posed / max(rest, 1.0e-8)
                if ratio > maximum_ratio:
                    maximum_ratio = ratio
                    maximum_location = {"action": action.name, "frame": frame, "edge": index}
            for triangle in mesh.loop_triangles:
                first, second, third = (mesh.vertices[index].co for index in triangle.vertices)
                area = (second - first).cross(third - first).length * 0.5
                minimum_area = min(minimum_area, area)
            sampled_frames += 1
            evaluated.to_mesh_clear()
    armature.animation_data_clear()
    for pose_bone in armature.pose.bones:
        pose_bone.matrix_basis = Matrix.Identity(4)
    armature.data.pose_position = "REST"
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    if maximum_ratio > 8.0 or minimum_area <= 1.0e-12:
        raise RuntimeError(
            f"Undersuit deformation failed: max edge ratio {maximum_ratio:.4f}, "
            f"minimum triangle area {minimum_area:.8g}."
        )
    return {
        "sample_mode": "all_authored_keyframes",
        "sampled_frames": sampled_frames,
        "maximum_edge_stretch_ratio": round(maximum_ratio, 6),
        "maximum_edge_stretch_location": maximum_location,
        "minimum_evaluated_triangle_area": round(minimum_area, 12),
        "acceptance": {"maximum_edge_stretch_ratio": 8.0, "minimum_triangle_area": 1.0e-12},
    }


def main() -> None:
    def stage(message: str) -> None:
        print(f"[Candidate005] {message}", flush=True)

    stage("validating inputs")
    if bpy.app.version < (5, 2, 0):
        raise RuntimeError("Candidate005 requires Blender 5.2 or newer.")
    current = Path(bpy.data.filepath).resolve()
    if current != SOURCE_BLEND.resolve():
        raise RuntimeError(f"Expected Candidate004 input {SOURCE_BLEND}, got {current}")
    source_hash_before = sha256(SOURCE_BLEND)
    expected = expected_source_hash()
    if source_hash_before != expected:
        raise RuntimeError(f"Candidate004 input hash mismatch: {source_hash_before} != {expected}")
    if not TEXTURE_MANIFEST.is_file():
        raise RuntimeError("Run generate_candidate005_preview_textures.py first.")

    legacy = load_builder004()
    stage("loaded Candidate004 helper")
    armature = bpy.data.objects.get(ARMATURE_NAME)
    collection = bpy.data.collections.get(COLLECTION004)
    if armature is None or armature.type != "ARMATURE" or collection is None:
        raise RuntimeError("Candidate004 armature or collection is missing.")
    armature.data.pose_position = "REST"
    armature.animation_data_clear()
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    collection.name = COLLECTION005
    sources = candidate_source_objects(collection)
    improve_silhouette(sources)
    stage(f"adjusted {len(sources)} source meshes")

    old_handoff = bpy.data.collections.get("HeroV2_LOD0")
    if old_handoff is not None:
        bpy.data.collections.remove(old_handoff)
    production_collection = bpy.data.collections.new("HeroV2_LOD0")
    bpy.context.scene.collection.children.link(production_collection)

    undersuit_sources = [obj for obj in sources if is_undersuit_seed(obj)]
    smooth_seed_shells(undersuit_sources)
    emissive_sources = [obj for obj in sources if is_emissive(obj)]
    omitted_joint_proxies = [obj for obj in sources if "Gasket" in obj.name]
    armor_sources = [
        obj
        for obj in sources
        if obj not in undersuit_sources
        and obj not in emissive_sources
        and obj not in omitted_joint_proxies
    ]
    armor_material = preview_material("AV_H2_ArmorPBRPreview")
    stage("created armor preview material")
    undersuit_material = preview_material("AV_H2_UndersuitPBRPreview")
    stage("created undersuit preview material")
    emission_material = preview_material("AV_H2_EmissionPBRPreview", emissive=True)
    stage("created emission preview material")

    undersuit = create_continuous_undersuit(
        undersuit_sources, production_collection, armature, undersuit_material
    )
    stage("created continuous undersuit")
    armor, repaired_boundaries = create_consolidated_renderer(
        "H2_Armor_LOD0", armor_sources, production_collection, armature, armor_material
    )
    stage("created consolidated armor")
    emission, _emission_boundaries = create_consolidated_renderer(
        "H2_Emission_LOD0", emissive_sources, production_collection, armature, emission_material
    )
    stage("created consolidated emission")
    production_objects = [undersuit, armor, emission]
    unwrap_uv0(production_objects)
    stage("created and packed UV0")
    uv_overlap_audit = audit_uv0_overlaps(production_objects)
    stage("validated non-overlapping UV0 atlas")
    mark_clearance_proxies(sources)
    stage("prepared clearance proxies")

    # The production meshes belong to both the explicit handoff and the review
    # collection; anchors, studio and hidden proxies remain out of the handoff.
    for obj in production_objects:
        if obj.name not in collection.objects:
            collection.objects.link(obj)
    stage("linked production renderers into review collection")
    architecture = validate_render_architecture(production_objects, armature)
    architecture["armor_boundary_edges_repaired_before_triangulation"] = repaired_boundaries
    stage("validated production renderer architecture")

    lights = [bpy.data.objects.get(name) for name in ("AV_Key", "AV_Fill", "AV_Rim")]
    lights = [light for light in lights if light is not None]
    if len(lights) != 3:
        _ground, lights = legacy.add_studio(collection, legacy.materials())
    rifle_objects = [obj for obj in bpy.data.objects if obj.name.startswith("Rifle")]
    stage("resolved review lights and rifle objects")

    OUTPUT_BLEND.parent.mkdir(parents=True, exist_ok=True)
    stage("saving production candidate before renders")
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND), check_existing=False)
    stage("saved production candidate; starting neutral renders")
    render_paths = legacy.render_views(collection, lights, legacy.materials())
    stage("completed neutral renders; starting pose renders")
    render_paths.extend(legacy.render_pose_reviews(lights, armature, rifle_objects))
    stage("completed pose renders")
    deformation = audit_undersuit_deformation(undersuit, armature, legacy.activate_action)
    stage(
        "validated undersuit deformation across "
        f"{deformation['sampled_frames']} authored keyframes"
    )
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND), check_existing=False)

    source_hash_after = sha256(SOURCE_BLEND)
    if source_hash_after != source_hash_before:
        raise RuntimeError("Candidate004 changed during Candidate005 generation.")
    actions = sorted(action.name for action in bpy.data.actions if action.name.startswith("PS_"))
    if len(actions) != 24:
        raise RuntimeError(f"Expected 24 preserved actions, found {len(actions)}")
    texture_manifest = json.loads(TEXTURE_MANIFEST.read_text(encoding="utf-8"))
    report = {
        "schema_version": 1,
        "candidate": "Aegis Vanguard Candidate005",
        "status": "PRODUCTION_ARCHITECTURE_CANDIDATE_NOT_UNITY_INTEGRATED",
        "source_candidate004": str(SOURCE_BLEND),
        "source_sha256_before": source_hash_before,
        "source_sha256_after": source_hash_after,
        "source_preserved": source_hash_before == source_hash_after,
        "candidate_blend": str(OUTPUT_BLEND),
        "candidate_blend_sha256": sha256(OUTPUT_BLEND),
        "armature": armature.name,
        "bone_count": len(armature.data.bones),
        "preserved_actions": actions,
        "clearance_proxy_count": len(sources),
        "source_partition": {
            "undersuit_seeds": len(undersuit_sources),
            "armor_parts": len(armor_sources),
            "emission_parts": len(emissive_sources),
            "omitted_redundant_joint_gaskets": len(omitted_joint_proxies),
        },
        "production_architecture": architecture,
        "uv0_overlap_audit": uv_overlap_audit,
        "deformation_validation": deformation,
        "preview_texture_manifest": texture_manifest,
        "render_paths": [str(path) for path in render_paths],
        "limitations": [
            "Automated production-architecture pass; final anatomical sculpt, retopology, seam placement, weight polish and authored character bake remain manual art work.",
            "Preview BaseColor, Normal, packed MRAO and Emission maps exercise the UV/PBR channel path but are deterministic 1K scaffolds, not the final unique 4K character atlas.",
            "Clearance proxies exactly preserve rigid armor/emission source parts but are not equivalent to the remeshed, smoothly skinned visible undersuit. Proxy results are directional diagnostics only; actual visible geometry is audited separately and remains blocking.",
            "The automated deformation ceiling catches catastrophic failures, not production skin quality; joint weighting and anatomical deformation remain manual polish work.",
            "No Unity FBX, GUID, controller, prefab or scene was replaced.",
        ],
    }
    OUTPUT_REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
