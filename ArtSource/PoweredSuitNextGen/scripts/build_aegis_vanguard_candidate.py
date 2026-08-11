"""Build a non-destructive, rig-compatible Aegis Vanguard concept candidate.

This script is intentionally separate from the approved PoweredSuit Generator114
pipeline.  It opens that generated working blend, hides (but never deletes) the
approved visible suit, preserves the armature/actions/weapon, and builds a new
review-only visual shell on the same bones.  The result is saved to the
PoweredSuitNextGen candidate directory and cannot overwrite the approved blend.
"""
from __future__ import annotations

import hashlib
import json
import math
import sys
from pathlib import Path

import bpy  # type: ignore
from mathutils import Euler, Matrix, Vector  # type: ignore


ROOT = Path(__file__).resolve().parents[3]
PIPELINE_SCRIPTS = ROOT / "ArtSource" / "PoweredSuit" / "scripts"
if str(PIPELINE_SCRIPTS) not in sys.path:
    sys.path.insert(0, str(PIPELINE_SCRIPTS))

from powersuit_pipeline_common import activate_action  # type: ignore  # noqa: E402

LEGACY_BLEND = ROOT / "ArtSource" / "PoweredSuit" / "powersuit_pipeline.blend"
OUTPUT_ROOT = ROOT / "ArtSource" / "PoweredSuitNextGen"
CANDIDATE_BLEND = OUTPUT_ROOT / "candidates" / "aegis_vanguard_candidate_v003.blend"
RENDER_ROOT = OUTPUT_ROOT / "renders" / "aegis_vanguard_candidate_v003"
REPORT_PATH = OUTPUT_ROOT / "candidates" / "aegis_vanguard_candidate_v003.json"
COLLECTION_NAME = "Aegis_Vanguard_Candidate003"
RUNTIME_ANCHORS: dict[str, tuple[tuple[float, float, float], str, str]] = {
    "Thruster_Nozzle.L": ((0.450, -0.382, 1.570), "AV_TurbineCore.L", "Chest"),
    "Thruster_Nozzle.R": ((-0.450, -0.382, 1.570), "AV_TurbineCore.R", "Chest"),
    "Heavy_Boot.L": ((0.170, -0.145, 0.115), "AV_BootThrusterCore.L", "Foot.L"),
    "Heavy_Boot.R": ((-0.170, -0.145, 0.115), "AV_BootThrusterCore.R", "Foot.R"),
}

LEGACY_SUIT_OBJECTS = {
    "Backpack_Core", "Backpack_Thruster.L", "Backpack_Thruster.R",
    "Thruster_Nozzle.L", "Thruster_Nozzle.R", "Chest_Core", "Chest_Plate",
    "Chest_Plate.L", "Chest_Plate.R", "Upper_Chest", "Boot_Toe.L",
    "Boot_Toe.R", "Heavy_Boot.L", "Heavy_Boot.R", "Hand.L", "Hand.R",
    "Helmet_Core", "Helmet_Crown", "Helmet_Jaw", "Helmet_Plate.L",
    "Helmet_Plate.R", "Helmet_Visor", "Hip_Guard.L", "Hip_Guard.R", "Pelvis",
    "Elbow.L", "Elbow.R", "Forearm.L", "Forearm.R", "Forearm_Plate.L",
    "Forearm_Plate.R", "Knee.L", "Knee.R", "Knee_Guard.L", "Knee_Guard.R",
    "Lower_Leg.L", "Lower_Leg.R", "Shin_Plate.L", "Shin_Plate.R", "Neck",
    "Waist", "Shoulder_Armour.L", "Shoulder_Armour.R", "Shoulder_Wing.L",
    "Shoulder_Wing.R", "Upper_Arm.L", "Upper_Arm.R", "Thigh_Plate.L",
    "Thigh_Plate.R", "Upper_Leg.L", "Upper_Leg.R",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def ensure_clean_collection() -> bpy.types.Collection:
    old = bpy.data.collections.get(COLLECTION_NAME)
    if old is not None:
        for obj in list(old.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(old)
    collection = bpy.data.collections.new(COLLECTION_NAME)
    bpy.context.scene.collection.children.link(collection)
    return collection


def move_to_collection(obj: bpy.types.Object, collection: bpy.types.Collection) -> None:
    for source in list(obj.users_collection):
        source.objects.unlink(obj)
    collection.objects.link(obj)


def make_material(
    name: str,
    color: tuple[float, float, float, float],
    metallic: float,
    roughness: float,
    *,
    emission: tuple[float, float, float, float] | None = None,
    emission_strength: float = 0.0,
) -> bpy.types.Material:
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    if emission is not None:
        emission_input = shader.inputs.get("Emission Color") or shader.inputs.get("Emission")
        strength_input = shader.inputs.get("Emission Strength")
        if emission_input is not None:
            emission_input.default_value = emission
        if strength_input is not None:
            strength_input.default_value = emission_strength
    if "Armor" in name or "Ceramic" in name or "DeepTeal" in name:
        coat_weight = shader.inputs.get("Coat Weight")
        coat_roughness = shader.inputs.get("Coat Roughness")
        if coat_weight is not None:
            coat_weight.default_value = 0.22
        if coat_roughness is not None:
            coat_roughness.default_value = 0.19
    if emission is None and "Studio" not in name and "Clay" not in name and "Chrome" not in name:
        coordinates = nodes.new("ShaderNodeTexCoord")
        noise = nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 165.0 if "Undersuit" in name or "Rubber" in name else 95.0
        noise.inputs["Detail"].default_value = 3.0
        noise.inputs["Roughness"].default_value = 0.6
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (roughness * 0.72,) * 3 + (1.0,)
        ramp.color_ramp.elements[1].color = (min(1.0, roughness * 1.22),) * 3 + (1.0,)
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.16 if "Undersuit" in name else 0.055
        bump.inputs["Distance"].default_value = 0.0012 if "Undersuit" in name else 0.00055
        color_ramp = nodes.new("ShaderNodeValToRGB")
        color_ramp.color_ramp.elements[0].position = 0.28
        color_ramp.color_ramp.elements[1].position = 0.72
        color_ramp.color_ramp.elements[0].color = tuple(max(0.0, channel * 0.78) for channel in color[:3]) + (1.0,)
        color_ramp.color_ramp.elements[1].color = tuple(min(1.0, channel * 1.08 + 0.008) for channel in color[:3]) + (1.0,)
        links.new(coordinates.outputs["Generated"], noise.inputs["Vector"])
        links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
        links.new(noise.outputs["Fac"], color_ramp.inputs["Fac"])
        links.new(color_ramp.outputs["Color"], shader.inputs["Base Color"])
        links.new(ramp.outputs["Color"], shader.inputs["Roughness"])
        links.new(noise.outputs["Fac"], bump.inputs["Height"])
        links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return material


def make_carbon_material(
    name: str,
    base_color: tuple[float, float, float, float],
    roughness: float,
) -> bpy.types.Material:
    """Create a restrained woven carbon-fibre material in object space."""
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Metallic"].default_value = 0.04
    shader.inputs["Roughness"].default_value = roughness
    coat_weight = shader.inputs.get("Coat Weight")
    coat_roughness = shader.inputs.get("Coat Roughness")
    if coat_weight is not None:
        coat_weight.default_value = 0.16
    if coat_roughness is not None:
        coat_roughness.default_value = 0.26

    coordinates = nodes.new("ShaderNodeTexCoord")
    mapping_a = nodes.new("ShaderNodeMapping")
    mapping_b = nodes.new("ShaderNodeMapping")
    mapping_a.inputs["Rotation"].default_value[2] = math.radians(45.0)
    mapping_b.inputs["Rotation"].default_value[2] = math.radians(-45.0)
    for mapping in (mapping_a, mapping_b):
        mapping.inputs["Scale"].default_value = (46.0, 46.0, 46.0)
        links.new(coordinates.outputs["Generated"], mapping.inputs["Vector"])
    weave_a = nodes.new("ShaderNodeTexWave")
    weave_b = nodes.new("ShaderNodeTexWave")
    for weave in (weave_a, weave_b):
        weave.wave_type = "BANDS"
        weave.bands_direction = "X"
        weave.inputs["Scale"].default_value = 5.5
        weave.inputs["Distortion"].default_value = 0.18
        weave.inputs["Detail"].default_value = 2.0
    links.new(mapping_a.outputs["Vector"], weave_a.inputs["Vector"])
    links.new(mapping_b.outputs["Vector"], weave_b.inputs["Vector"])
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1.0
    links.new(weave_a.outputs["Color"], multiply.inputs[1])
    links.new(weave_b.outputs["Color"], multiply.inputs[2])
    color_ramp = nodes.new("ShaderNodeValToRGB")
    color_ramp.color_ramp.elements[0].color = tuple(channel * 0.55 for channel in base_color[:3]) + (1.0,)
    color_ramp.color_ramp.elements[1].color = tuple(min(1.0, channel * 1.65 + 0.018) for channel in base_color[:3]) + (1.0,)
    links.new(multiply.outputs["Color"], color_ramp.inputs["Fac"])
    links.new(color_ramp.outputs["Color"], shader.inputs["Base Color"])
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.20
    bump.inputs["Distance"].default_value = 0.00065
    links.new(multiply.outputs["Color"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return material


def materials() -> dict[str, bpy.types.Material]:
    return {
        "ceramic": make_material("AV_SatinBlackArmor", (0.0080, 0.0110, 0.0170, 1.0), 0.13, 0.29),
        "ceramic_dark": make_carbon_material("AV_CarbonComposite", (0.0035, 0.0050, 0.0080, 1.0), 0.43),
        "teal": make_carbon_material("AV_BlueBlackCarbon", (0.0040, 0.0120, 0.0180, 1.0), 0.38),
        "undersuit": make_carbon_material("AV_CarbonUndersuit", (0.0015, 0.0020, 0.0030, 1.0), 0.68),
        "rubber": make_carbon_material("AV_BraidedCarbonCable", (0.0018, 0.0022, 0.0030, 1.0), 0.55),
        "steel": make_material("AV_Chrome", (0.38, 0.44, 0.52, 1.0), 1.0, 0.075),
        "steel_dark": make_material("AV_BlackChrome", (0.032, 0.042, 0.058, 1.0), 1.0, 0.18),
        "copper": make_material("AV_BrightChromeDetail", (0.65, 0.72, 0.82, 1.0), 1.0, 0.055),
        "studio": make_material("AV_StudioMatte", (0.012, 0.014, 0.017, 1.0), 0.0, 0.82),
        "clay": make_material("AV_ClayReview", (0.33, 0.35, 0.36, 1.0), 0.0, 0.47),
        "cyan": make_material(
            "AV_CyanEmission",
            (0.0, 0.18, 0.22, 1.0),
            0.38,
            0.12,
            emission=(0.0, 0.82, 1.0, 1.0),
            emission_strength=3.6,
        ),
    }


def assign(obj: bpy.types.Object, material: bpy.types.Material) -> bpy.types.Object:
    obj.data.materials.append(material)
    return obj


def parent_to_bone(obj: bpy.types.Object, armature: bpy.types.Object, bone: str) -> None:
    world = obj.matrix_world.copy()
    obj.parent = armature
    obj.parent_type = "BONE"
    obj.parent_bone = bone
    obj.matrix_world = world
    obj["aegis_vanguard_candidate"] = True


def apply_bevel(obj: bpy.types.Object, width: float, segments: int = 3) -> None:
    if width <= 0.0:
        for polygon in obj.data.polygons:
            polygon.use_smooth = False
        return
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bevel = obj.modifiers.new("AV_EdgeRadius", "BEVEL")
    dimensions = [dimension for dimension in obj.dimensions if dimension > 0.0001]
    safe_width = min(width, max(0.002, min(dimensions) * 0.18)) if dimensions else width
    bevel.width = safe_width
    bevel.segments = segments
    bevel.limit_method = "ANGLE"
    bevel.angle_limit = math.radians(28.0)
    bevel.harden_normals = True
    bevel.face_strength_mode = "FSTR_AFFECTED"
    bevel.miter_outer = "MITER_ARC"
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    obj.data.set_sharp_from_angle(angle=math.radians(38.0))
    weighted = obj.modifiers.new("AV_WeightedNormals", "WEIGHTED_NORMAL")
    weighted.mode = "FACE_AREA_WITH_ANGLE"
    weighted.keep_sharp = True
    weighted.use_face_influence = True
    bpy.ops.object.modifier_apply(modifier=weighted.name)
    obj.select_set(False)


def cube(
    collection: bpy.types.Collection,
    name: str,
    center: tuple[float, float, float],
    dimensions: tuple[float, float, float],
    material: bpy.types.Material,
    *,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    bevel: float = 0.018,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=center, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    move_to_collection(obj, collection)
    assign(obj, material)
    apply_bevel(obj, bevel)
    return obj


def ellipsoid(
    collection: bpy.types.Collection,
    name: str,
    center: tuple[float, float, float],
    dimensions: tuple[float, float, float],
    material: bpy.types.Material,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=14, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    move_to_collection(obj, collection)
    assign(obj, material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def frustum(
    collection: bpy.types.Collection,
    name: str,
    center: tuple[float, float, float],
    bottom: tuple[float, float],
    top: tuple[float, float],
    height: float,
    material: bpy.types.Material,
    *,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    bevel: float = 0.018,
) -> bpy.types.Object:
    bx, by = bottom[0] * 0.5, bottom[1] * 0.5
    tx, ty = top[0] * 0.5, top[1] * 0.5
    hz = height * 0.5
    vertices = [
        (-bx, -by, -hz), (bx, -by, -hz), (bx, by, -hz), (-bx, by, -hz),
        (-tx, -ty, hz), (tx, -ty, hz), (tx, ty, hz), (-tx, ty, hz),
    ]
    faces = [
        (3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.location = center
    obj.rotation_euler = Euler(rotation)
    assign(obj, material)
    apply_bevel(obj, bevel)
    return obj


def cylinder_between(
    collection: bpy.types.Collection,
    name: str,
    start: Vector,
    end: Vector,
    radius: float,
    material: bpy.types.Material,
    *,
    radial_scale: tuple[float, float] = (1.0, 1.0),
    vertices: int = 20,
) -> bpy.types.Object:
    direction = end - start
    midpoint = (start + end) * 0.5
    rotation = Vector((0.0, 0.0, 1.0)).rotation_difference(direction.normalized())
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=direction.length,
        location=midpoint,
        rotation=rotation.to_euler(),
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale.x = radial_scale[0]
    obj.scale.y = radial_scale[1]
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    move_to_collection(obj, collection)
    assign(obj, material)
    apply_bevel(obj, min(0.012, radius * 0.18), 2)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def loft_between(
    collection: bpy.types.Collection,
    name: str,
    start: Vector,
    end: Vector,
    sections: list[tuple[float, float, float]],
    material: bpy.types.Material,
    *,
    vertices: int = 12,
    exponent: float = 2.8,
    bevel: float = 0.006,
) -> bpy.types.Object:
    """Build a station-based superellipse shell along an arbitrary bone span."""
    direction = (end - start).normalized()
    front_reference = Vector((0.0, 1.0, 0.0))
    side_axis = front_reference.cross(direction)
    if side_axis.length < 0.001:
        side_axis = Vector((1.0, 0.0, 0.0))
    else:
        side_axis.normalize()
    front_axis = direction.cross(side_axis).normalized()
    power = 2.0 / exponent
    mesh_vertices: list[tuple[float, float, float]] = []
    for t, side_radius, front_radius in sections:
        center = start.lerp(end, t)
        for index in range(vertices):
            angle = math.tau * index / vertices
            cosine = math.cos(angle)
            sine = math.sin(angle)
            side = math.copysign(abs(cosine) ** power, cosine) * side_radius
            front = math.copysign(abs(sine) ** power, sine) * front_radius
            point = center + side_axis * side + front_axis * front
            mesh_vertices.append(tuple(point))
    faces: list[tuple[int, ...]] = []
    for section_index in range(len(sections) - 1):
        base = section_index * vertices
        next_base = (section_index + 1) * vertices
        for index in range(vertices):
            following = (index + 1) % vertices
            faces.append((base + index, base + following, next_base + following, next_base + index))
    faces.append(tuple(reversed(range(vertices))))
    last = (len(sections) - 1) * vertices
    faces.append(tuple(last + index for index in range(vertices)))
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(mesh_vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    assign(obj, material)
    apply_bevel(obj, bevel, 2)
    return obj


def ring(
    collection: bpy.types.Collection,
    name: str,
    center: tuple[float, float, float],
    major_radius: float,
    minor_radius: float,
    material: bpy.types.Material,
    *,
    axis: str = "Y",
) -> bpy.types.Object:
    rotation = (math.pi * 0.5, 0.0, 0.0) if axis == "Y" else (0.0, math.pi * 0.5, 0.0)
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=28,
        minor_segments=8,
        location=center,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    move_to_collection(obj, collection)
    assign(obj, material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def panel_xz(
    collection: bpy.types.Collection,
    name: str,
    points: list[tuple[float, float]],
    center_y: float,
    depth: float,
    material: bpy.types.Material,
    *,
    bevel: float = 0.012,
) -> bpy.types.Object:
    """Create a beveled armor panel from an X/Z silhouette, extruded in Y."""
    back_y = center_y - depth * 0.5
    front_y = center_y + depth * 0.5
    vertices = [(x, back_y, z) for x, z in points] + [(x, front_y, z) for x, z in points]
    count = len(points)
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces.extend((index, (index + 1) % count, (index + 1) % count + count, index + count) for index in range(count))
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    assign(obj, material)
    apply_bevel(obj, bevel, 3)
    return obj


def panel_yz(
    collection: bpy.types.Collection,
    name: str,
    points: list[tuple[float, float]],
    center_x: float,
    depth: float,
    material: bpy.types.Material,
    *,
    bevel: float = 0.010,
) -> bpy.types.Object:
    """Create a beveled side armor panel from a Y/Z silhouette, extruded in X."""
    left_x = center_x - depth * 0.5
    right_x = center_x + depth * 0.5
    vertices = [(left_x, y, z) for y, z in points] + [(right_x, y, z) for y, z in points]
    count = len(points)
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces.extend((index, (index + 1) % count, (index + 1) % count + count, index + count) for index in range(count))
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    assign(obj, material)
    apply_bevel(obj, bevel, 3)
    return obj


def cable_between(
    collection: bpy.types.Collection,
    name: str,
    points: list[tuple[float, float, float]],
    radius: float,
    material: bpy.types.Material,
) -> bpy.types.Object:
    curve = bpy.data.curves.new(name + "_Curve", "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 3
    curve.bevel_depth = radius
    curve.bevel_resolution = 3
    spline = curve.splines.new("BEZIER")
    spline.bezier_points.add(len(points) - 1)
    for point, coordinate in zip(spline.bezier_points, points):
        point.co = coordinate
        point.handle_left_type = "AUTO"
        point.handle_right_type = "AUTO"
    obj = bpy.data.objects.new(name, curve)
    collection.objects.link(obj)
    assign(obj, material)
    return obj


def fastener(
    collection: bpy.types.Collection,
    name: str,
    center: tuple[float, float, float],
    material: bpy.types.Material,
    radius: float = 0.012,
    depth: float = 0.008,
) -> bpy.types.Object:
    start = Vector((center[0], center[1] - depth * 0.5, center[2]))
    end = Vector((center[0], center[1] + depth * 0.5, center[2]))
    return cylinder_between(collection, name, start, end, radius, material, vertices=12)


def create_runtime_anchors(collection: bpy.types.Collection) -> None:
    for name, (location, target_name, _bone) in RUNTIME_ANCHORS.items():
        legacy = bpy.data.objects.get(name)
        if legacy is not None:
            legacy.name = "Legacy_Generator114_" + name
            legacy.hide_render = True
            legacy.hide_set(True)
        target = bpy.data.objects.get(target_name)
        if target is None:
            raise RuntimeError(f"Runtime anchor target '{target_name}' is missing.")
        anchor = bpy.data.objects.new(name, None)
        collection.objects.link(anchor)
        anchor.empty_display_type = "SPHERE"
        anchor.empty_display_size = 0.025
        anchor.hide_render = True
        anchor["aegis_runtime_anchor"] = True
        anchor.parent = target
        bpy.context.view_layer.update()
        anchor.matrix_parent_inverse = target.matrix_world.inverted()
        anchor.matrix_world = Matrix.Translation(Vector(location))
        anchor["aegis_vanguard_candidate"] = True


def bone_points(armature: bpy.types.Object, name: str) -> tuple[Vector, Vector]:
    bone = armature.data.bones[name]
    return armature.matrix_world @ bone.head_local, armature.matrix_world @ bone.tail_local


def build_core(collection, armature, mat) -> None:
    # A continuous dark chassis carries the silhouette; armor floats above it with
    # deliberate gaps rather than reading as a stack of unrelated boxes.
    for name, center, dims, bone, material in (
        ("AV_UnderChest", (0.0, -0.005, 1.49), (0.58, 0.31, 0.42), "Chest", "undersuit"),
        ("AV_UnderAbdomen", (0.0, 0.0, 1.25), (0.39, 0.27, 0.34), "Spine", "undersuit"),
        ("AV_UnderPelvis", (0.0, 0.0, 1.03), (0.47, 0.29, 0.25), "Hips", "undersuit"),
        ("AV_UnderNeck", (0.0, 0.0, 1.73), (0.22, 0.22, 0.18), "Neck", "rubber"),
    ):
        parent_to_bone(ellipsoid(collection, name, center, dims, mat[material]), armature, bone)

    parent_to_bone(
        frustum(collection, "AV_ChestLoadFrame", (0.0, -0.025, 1.50), (0.44, 0.27), (0.62, 0.33), 0.38, mat["steel_dark"], bevel=0.026),
        armature, "Chest",
    )

    left_chest = [(0.022, 1.665), (0.255, 1.660), (0.350, 1.585), (0.315, 1.465), (0.185, 1.385), (0.058, 1.410)]
    left_inset = [(0.095, 1.620), (0.245, 1.610), (0.285, 1.565), (0.250, 1.500), (0.140, 1.455), (0.085, 1.475)]
    left_rib = [(0.055, 1.390), (0.185, 1.370), (0.275, 1.315), (0.235, 1.270), (0.075, 1.285)]
    for side, mirror in (("L", False), ("R", True)):
        chest_points = [(-x, z) for x, z in reversed(left_chest)] if mirror else left_chest
        inset_points = [(-x, z) for x, z in reversed(left_inset)] if mirror else left_inset
        rib_points = [(-x, z) for x, z in reversed(left_rib)] if mirror else left_rib
        parent_to_bone(panel_xz(collection, f"AV_Pectoral.{side}", chest_points, 0.150, 0.055, mat["ceramic"], bevel=0.018), armature, "Chest")
        parent_to_bone(panel_xz(collection, f"AV_PectoralInset.{side}", inset_points, 0.184, 0.020, mat["teal"], bevel=0.008), armature, "Chest")
        parent_to_bone(panel_xz(collection, f"AV_RibPlate.{side}", rib_points, 0.142, 0.040, mat["ceramic_dark"], bevel=0.012), armature, "Spine")
        sign = 1.0 if side == "L" else -1.0
        for index, z in enumerate((1.445, 1.565)):
            parent_to_bone(fastener(collection, f"AV_ChestFastener.{side}.{index}", (0.245 * sign, 0.198, z), mat["copper"], 0.010, 0.009), armature, "Chest")

    # Central service channel is the suit's strongest identifying motif.
    parent_to_bone(panel_xz(collection, "AV_SternumFrame", [(-0.055, 1.665), (0.055, 1.665), (0.072, 1.390), (0.0, 1.335), (-0.072, 1.390)], 0.176, 0.042, mat["steel"], bevel=0.012), armature, "Chest")
    for index, z in enumerate((1.405, 1.485, 1.565, 1.630)):
        width = 0.034 if index in (0, 3) else 0.043
        parent_to_bone(cube(collection, f"AV_ReactorSegment.{index}", (0.0, 0.204, z), (width, 0.012, 0.047), mat["cyan"], bevel=0.006), armature, "Chest")

    # Telescoping abdomen and pelvis plates leave black articulation seams visible.
    ab_profiles = (
        ([(-0.175, 1.365), (0.175, 1.365), (0.155, 1.295), (0.0, 1.270), (-0.155, 1.295)], "ceramic"),
        ([(-0.155, 1.275), (0.155, 1.275), (0.135, 1.215), (0.0, 1.190), (-0.135, 1.215)], "teal"),
        ([(-0.135, 1.185), (0.135, 1.185), (0.115, 1.130), (0.0, 1.105), (-0.115, 1.130)], "ceramic"),
    )
    for index, (profile, material) in enumerate(ab_profiles):
        parent_to_bone(panel_xz(collection, f"AV_AbPlate.{index}", profile, 0.140 + index * 0.004, 0.040, mat[material], bevel=0.011), armature, "Spine")

    parent_to_bone(panel_xz(collection, "AV_PelvisFront", [(-0.245, 1.115), (0.245, 1.115), (0.205, 0.985), (0.095, 0.940), (-0.095, 0.940), (-0.205, 0.985)], 0.142, 0.052, mat["ceramic"], bevel=0.018), armature, "Hips")
    parent_to_bone(panel_xz(collection, "AV_PelvisInset", [(-0.105, 1.075), (0.105, 1.075), (0.080, 1.000), (0.0, 0.965), (-0.080, 1.000)], 0.174, 0.020, mat["teal"], bevel=0.008), armature, "Hips")
    for side, x in (("L", 0.268), ("R", -0.268)):
        parent_to_bone(ellipsoid(collection, f"AV_HipJoint.{side}", (x, 0.0, 1.005), (0.15, 0.22, 0.23), mat["steel_dark"]), armature, "Hips")
        sign = 1.0 if side == "L" else -1.0
        points = [(0.205 * sign, 1.105), (0.330 * sign, 1.080), (0.345 * sign, 0.985), (0.285 * sign, 0.905), (0.205 * sign, 0.950)]
        if side == "R":
            points.reverse()
        parent_to_bone(panel_xz(collection, f"AV_HipGuard.{side}", points, 0.055, 0.075, mat["ceramic_dark"], bevel=0.016), armature, "Hips")

    parent_to_bone(panel_xz(collection, "AV_BackSpine", [(-0.075, 1.690), (0.075, 1.690), (0.090, 1.270), (0.0, 1.220), (-0.090, 1.270)], -0.215, 0.060, mat["steel"], bevel=0.014), armature, "Chest")
    for index, z in enumerate((1.295, 1.395, 1.495, 1.595, 1.670)):
        parent_to_bone(panel_xz(collection, f"AV_BackSpinePlate.{index}", [(-0.080, z + 0.032), (0.080, z + 0.032), (0.068, z - 0.032), (-0.068, z - 0.032)], -0.252, 0.025, mat["teal" if index % 2 else "ceramic_dark"], bevel=0.007), armature, "Chest")
    for side, x in (("L", 0.115), ("R", -0.115)):
        parent_to_bone(cable_between(collection, f"AV_SpineCable.{side}", [(x, -0.245, 1.64), (x * 1.15, -0.285, 1.47), (x * 0.90, -0.245, 1.28)], 0.012, mat["rubber"]), armature, "Chest")


def build_head(collection, armature, mat) -> None:
    # Dark pressure shell first, then a faceted ceramic helmet with a narrow
    # three-part optical band.  The asymmetrical brow prevents a generic robot face.
    parent_to_bone(ellipsoid(collection, "AV_HelmetChassis", (0.0, -0.005, 1.925), (0.292, 0.275, 0.345), mat["steel_dark"]), armature, "Head")
    parent_to_bone(panel_xz(collection, "AV_HelmetBrow", [(-0.145, 2.075), (-0.040, 2.115), (0.090, 2.100), (0.155, 2.035), (0.135, 1.985), (-0.135, 1.985)], 0.145, 0.074, mat["ceramic"], bevel=0.014), armature, "Head")
    parent_to_bone(panel_xz(collection, "AV_FacePlate", [(-0.132, 1.975), (0.132, 1.975), (0.115, 1.865), (0.055, 1.805), (-0.055, 1.805), (-0.115, 1.865)], 0.168, 0.068, mat["ceramic_dark"], bevel=0.013), armature, "Head")
    parent_to_bone(panel_xz(collection, "AV_ChinPlate", [(-0.070, 1.865), (0.070, 1.865), (0.090, 1.815), (0.0, 1.770), (-0.090, 1.815)], 0.202, 0.045, mat["ceramic"], bevel=0.010), armature, "Head")
    for side, sign in (("L", 1.0), ("R", -1.0)):
        cheek = [(0.040 * sign, 1.955), (0.125 * sign, 1.965), (0.112 * sign, 1.875), (0.060 * sign, 1.825), (0.025 * sign, 1.865)]
        if side == "R":
            cheek.reverse()
        parent_to_bone(panel_xz(collection, f"AV_FaceCheek.{side}", cheek, 0.210, 0.025, mat["teal"], bevel=0.006), armature, "Head")
    parent_to_bone(panel_xz(collection, "AV_OpticalBand", [(-0.138, 2.004), (-0.042, 2.015), (0.010, 2.007), (0.138, 2.020), (0.126, 1.982), (0.010, 1.973), (-0.045, 1.980), (-0.132, 1.970)], 0.210, 0.018, mat["cyan"], bevel=0.005), armature, "Head")
    for x in (-0.043, 0.012):
        parent_to_bone(cube(collection, f"AV_OpticDivider.{x:+.3f}", (x, 0.224, 1.994), (0.010, 0.010, 0.047), mat["steel_dark"], bevel=0.003), armature, "Head")

    parent_to_bone(panel_xz(collection, "AV_HelmetCrown", [(-0.090, 2.115), (0.075, 2.115), (0.115, 2.070), (0.085, 2.035), (-0.085, 2.045), (-0.120, 2.080)], -0.005, 0.245, mat["ceramic"], bevel=0.014), armature, "Head")
    for side, x in (("L", 0.145), ("R", -0.145)):
        side_profile = [(-0.105, 2.070), (0.070, 2.050), (0.145, 1.970), (0.110, 1.845), (-0.020, 1.800), (-0.100, 1.865)]
        parent_to_bone(panel_yz(collection, f"AV_HelmetSide.{side}", side_profile, x, 0.050, mat["teal" if side == "L" else "ceramic"], bevel=0.010), armature, "Head")
        parent_to_bone(ring(collection, f"AV_HelmetPivot.{side}", (x * 1.10, -0.005, 1.950), 0.052, 0.012, mat["copper"], axis="X"), armature, "Head")
        parent_to_bone(ellipsoid(collection, f"AV_HelmetPivotCore.{side}", (x * 1.10, -0.005, 1.950), (0.025, 0.065, 0.065), mat["steel"]), armature, "Head")

    # Recessed respirator/vent stack.
    for index, x in enumerate((-0.042, -0.014, 0.014, 0.042)):
        parent_to_bone(cube(collection, f"AV_JawVent.{index}", (x, 0.229, 1.842), (0.014, 0.010, 0.040), mat["steel"], bevel=0.003), armature, "Head")

    # Protective collar arcs visually seat the helmet into the torso.
    for side, sign in (("L", 1.0), ("R", -1.0)):
        collar = [(0.055 * sign, 1.790), (0.245 * sign, 1.760), (0.300 * sign, 1.700), (0.255 * sign, 1.655), (0.105 * sign, 1.685)]
        if side == "R":
            collar.reverse()
        parent_to_bone(panel_xz(collection, f"AV_Collar.{side}", collar, 0.025, 0.235, mat["ceramic_dark"], bevel=0.016), armature, "Chest")


def build_limb(collection, armature, mat, side: str) -> None:
    suffix = ".L" if side == "L" else ".R"
    sign = 1.0 if side == "L" else -1.0
    upper_start, upper_end = bone_points(armature, "UpperArm" + suffix)
    lower_start, lower_end = bone_points(armature, "LowerArm" + suffix)
    hand_start, hand_end = bone_points(armature, "Hand" + suffix)
    upper_leg_start, upper_leg_end = bone_points(armature, "UpperLeg" + suffix)
    lower_leg_start, lower_leg_end = bone_points(armature, "LowerLeg" + suffix)

    # Flexible understructure remains visible as an intentional gasket at joints.
    for label, start, end, bone, radius in (
        ("UpperArm", upper_start, upper_end, "UpperArm" + suffix, 0.082),
        ("Forearm", lower_start, lower_end, "LowerArm" + suffix, 0.078),
        ("Thigh", upper_leg_start, upper_leg_end, "UpperLeg" + suffix, 0.112),
        ("Calf", lower_leg_start, lower_leg_end, "LowerLeg" + suffix, 0.092),
    ):
        parent_to_bone(loft_between(collection, f"AV_{label}Under{suffix}", start, end, [(0.0, radius, radius * 0.92), (0.5, radius * 0.94, radius * 0.88), (1.0, radius * 0.82, radius * 0.82)], mat["undersuit"], vertices=14, exponent=2.2, bevel=0.003), armature, bone)

    shoulder = Vector((0.34 * sign, 0.0, 1.61))
    parent_to_bone(ellipsoid(collection, "AV_ShoulderJoint" + suffix, shoulder, (0.185, 0.190, 0.185), mat["steel_dark"]), armature, "UpperArm" + suffix)
    pauldron = [(0.250 * sign, 1.690), (0.405 * sign, 1.720), (0.505 * sign, 1.650), (0.485 * sign, 1.555), (0.350 * sign, 1.525), (0.270 * sign, 1.570)]
    if side == "R":
        pauldron.reverse()
    parent_to_bone(panel_xz(collection, "AV_ShoulderShell" + suffix, pauldron, 0.035, 0.235, mat["ceramic"], bevel=0.012), armature, "UpperArm" + suffix)
    pauldron_inset = [(0.305 * sign, 1.665), (0.405 * sign, 1.685), (0.458 * sign, 1.640), (0.435 * sign, 1.585), (0.350 * sign, 1.565), (0.300 * sign, 1.595)]
    if side == "R":
        pauldron_inset.reverse()
    parent_to_bone(panel_xz(collection, "AV_ShoulderChromeBed" + suffix, pauldron_inset, 0.162, 0.030, mat["steel"], bevel=0.006), armature, "UpperArm" + suffix)
    inset_center_x = sum(x for x, _z in pauldron_inset) / len(pauldron_inset)
    inset_center_z = sum(z for _x, z in pauldron_inset) / len(pauldron_inset)
    carbon_inset = [
        (inset_center_x + (x - inset_center_x) * 0.88, inset_center_z + (z - inset_center_z) * 0.82)
        for x, z in pauldron_inset
    ]
    parent_to_bone(panel_xz(collection, "AV_ShoulderInset" + suffix, carbon_inset, 0.181, 0.018, mat["teal"], bevel=0.005), armature, "UpperArm" + suffix)
    upper_layer = [(0.285 * sign, 1.710), (0.400 * sign, 1.742), (0.505 * sign, 1.685), (0.480 * sign, 1.640), (0.365 * sign, 1.665)]
    middle_layer = [(0.310 * sign, 1.655), (0.475 * sign, 1.655), (0.490 * sign, 1.595), (0.350 * sign, 1.575), (0.300 * sign, 1.605)]
    lower_layer = [(0.315 * sign, 1.595), (0.455 * sign, 1.585), (0.445 * sign, 1.525), (0.345 * sign, 1.495), (0.300 * sign, 1.535)]
    if side == "R":
        upper_layer.reverse()
        middle_layer.reverse()
        lower_layer.reverse()
    parent_to_bone(panel_xz(collection, "AV_ShoulderLayerUpper" + suffix, upper_layer, 0.188, 0.026, mat["ceramic"], bevel=0.007), armature, "UpperArm" + suffix)
    parent_to_bone(panel_xz(collection, "AV_ShoulderLayerMiddle" + suffix, middle_layer, 0.193, 0.024, mat["ceramic_dark"], bevel=0.006), armature, "UpperArm" + suffix)
    parent_to_bone(panel_xz(collection, "AV_ShoulderLayerLower" + suffix, lower_layer, 0.198, 0.022, mat["ceramic"], bevel=0.006), armature, "UpperArm" + suffix)

    upper_span_start = upper_start.lerp(upper_end, 0.10)
    upper_span_end = upper_start.lerp(upper_end, 0.80)
    parent_to_bone(loft_between(collection, "AV_UpperArmFrame" + suffix, upper_span_start, upper_span_end, [(0.0, 0.105, 0.090), (0.42, 0.102, 0.085), (1.0, 0.075, 0.070)], mat["ceramic_dark"], vertices=12, bevel=0.005), armature, "UpperArm" + suffix)
    top, bottom = (upper_start, upper_end) if upper_start.z >= upper_end.z else (upper_end, upper_start)
    upper_plate = [(top.x - 0.082, top.z - 0.045), (top.x + 0.082, top.z - 0.045), (bottom.x + 0.060, bottom.z + 0.060), (bottom.x, bottom.z + 0.025), (bottom.x - 0.060, bottom.z + 0.060)]
    parent_to_bone(panel_xz(collection, "AV_UpperArmPlate" + suffix, upper_plate, 0.090, 0.036, mat["teal"], bevel=0.007), armature, "UpperArm" + suffix)

    parent_to_bone(ellipsoid(collection, "AV_Elbow" + suffix, upper_end, (0.155, 0.160, 0.155), mat["steel"]), armature, "LowerArm" + suffix)
    for ring_index, t in enumerate((0.03, 0.09, 0.15)):
        a = lower_start.lerp(lower_end, t)
        b = lower_start.lerp(lower_end, t + 0.025)
        parent_to_bone(cylinder_between(collection, f"AV_ElbowGasket.{ring_index}{suffix}", a, b, 0.091 - ring_index * 0.004, mat["rubber"], vertices=16), armature, "LowerArm" + suffix)
    forearm_span_start = lower_start.lerp(lower_end, 0.16)
    forearm_span_end = lower_start.lerp(lower_end, 0.88)
    parent_to_bone(loft_between(collection, "AV_ForearmFrame" + suffix, forearm_span_start, forearm_span_end, [(0.0, 0.096, 0.084), (0.42, 0.103, 0.089), (1.0, 0.071, 0.064)], mat["ceramic"], vertices=12, bevel=0.005), armature, "LowerArm" + suffix)
    top, bottom = (lower_start, lower_end) if lower_start.z >= lower_end.z else (lower_end, lower_start)
    forearm_plate = [(top.x - 0.075, top.z - 0.050), (top.x + 0.075, top.z - 0.050), (bottom.x + 0.052, bottom.z + 0.040), (bottom.x, bottom.z + 0.015), (bottom.x - 0.052, bottom.z + 0.040)]
    parent_to_bone(panel_xz(collection, "AV_ForearmPlate" + suffix, forearm_plate, 0.098, 0.030, mat["teal"], bevel=0.006), armature, "LowerArm" + suffix)
    for index, t in enumerate((0.35, 0.58)):
        point = lower_start.lerp(lower_end, t)
        parent_to_bone(fastener(collection, f"AV_ForearmFastener.{index}{suffix}", (point.x + sign * 0.056, 0.120, point.z), mat["copper"], 0.007, 0.005), armature, "LowerArm" + suffix)

    # Glove: tapered palm, dorsal plate, curled two-link fingers and an angled thumb.
    palm_start = hand_start.lerp(hand_end, 0.05)
    palm_end = hand_start.lerp(hand_end, 0.55)
    parent_to_bone(loft_between(collection, "AV_HandPalm" + suffix, palm_start, palm_end, [(0.0, 0.072, 0.070), (1.0, 0.060, 0.062)], mat["steel_dark"], vertices=12, bevel=0.004), armature, "Hand" + suffix)
    hand_mid = palm_start.lerp(palm_end, 0.45)
    parent_to_bone(cube(collection, "AV_Gauntlet" + suffix, tuple(hand_mid + Vector((0.0, 0.068, 0.0))), (0.125, 0.030, 0.105), mat["ceramic"], bevel=0.008), armature, "Hand" + suffix)
    for knuckle in range(4):
        parent_to_bone(cube(collection, f"AV_Knuckle{knuckle}{suffix}", (hand_mid.x - 0.043 + knuckle * 0.029, hand_mid.y + 0.089, hand_mid.z - 0.028), (0.021, 0.012, 0.030), mat["teal"], bevel=0.004), armature, "Hand" + suffix)
    for finger in range(4):
        x_offset = (-0.043 + finger * 0.029)
        first_start = hand_end + Vector((x_offset, 0.012, 0.035))
        first_end = first_start + Vector((0.0, 0.020, -0.052))
        second_end = first_end + Vector((0.0, 0.032, -0.042))
        parent_to_bone(cylinder_between(collection, f"AV_FingerA{finger}{suffix}", first_start, first_end, 0.012, mat["steel"], vertices=12), armature, "Hand" + suffix)
        parent_to_bone(cylinder_between(collection, f"AV_FingerB{finger}{suffix}", first_end, second_end, 0.010, mat["steel_dark"], vertices=12), armature, "Hand" + suffix)
    thumb_start = hand_start.lerp(hand_end, 0.42) + Vector((0.066 * sign, 0.015, 0.015))
    thumb_end = thumb_start + Vector((0.045 * sign, 0.035, -0.050))
    parent_to_bone(cylinder_between(collection, "AV_Thumb" + suffix, thumb_start, thumb_end, 0.014, mat["steel"], vertices=12), armature, "Hand" + suffix)

    # Athletic powered legs: large proximal masses, narrow joints, tapered ankles.
    thigh_span_start = upper_leg_start.lerp(upper_leg_end, 0.08)
    thigh_span_end = upper_leg_start.lerp(upper_leg_end, 0.82)
    parent_to_bone(loft_between(collection, "AV_ThighFrame" + suffix, thigh_span_start, thigh_span_end, [(0.0, 0.142, 0.132), (0.48, 0.150, 0.136), (1.0, 0.108, 0.102)], mat["ceramic_dark"], vertices=14, bevel=0.006), armature, "UpperLeg" + suffix)
    top, bottom = (upper_leg_start, upper_leg_end) if upper_leg_start.z >= upper_leg_end.z else (upper_leg_end, upper_leg_start)
    thigh_plate = [(top.x - 0.110, top.z - 0.040), (top.x + 0.110, top.z - 0.040), (bottom.x + 0.080, bottom.z + 0.075), (bottom.x, bottom.z + 0.035), (bottom.x - 0.080, bottom.z + 0.075)]
    parent_to_bone(panel_xz(collection, "AV_ThighPlate" + suffix, thigh_plate, 0.148, 0.050, mat["ceramic"], bevel=0.010), armature, "UpperLeg" + suffix)
    outer_x = (top.x + bottom.x) * 0.5 + sign * 0.130
    parent_to_bone(panel_yz(collection, "AV_ThighOuter" + suffix, [(-0.095, top.z - 0.070), (0.070, top.z - 0.095), (0.115, bottom.z + 0.110), (-0.055, bottom.z + 0.070)], outer_x, 0.040, mat["teal"], bevel=0.008), armature, "UpperLeg" + suffix)

    parent_to_bone(ellipsoid(collection, "AV_KneeJoint" + suffix, upper_leg_end, (0.165, 0.170, 0.155), mat["steel"]), armature, "LowerLeg" + suffix)
    knee = upper_leg_end
    knee_plate = [(knee.x - 0.092, knee.z + 0.080), (knee.x + 0.092, knee.z + 0.080), (knee.x + 0.075, knee.z - 0.040), (knee.x, knee.z - 0.105), (knee.x - 0.075, knee.z - 0.040)]
    parent_to_bone(panel_xz(collection, "AV_KneeGuard" + suffix, knee_plate, 0.147, 0.060, mat["ceramic"], bevel=0.010), armature, "LowerLeg" + suffix)
    for ring_index, t in enumerate((0.05, 0.11)):
        a = lower_leg_start.lerp(lower_leg_end, t)
        b = lower_leg_start.lerp(lower_leg_end, t + 0.025)
        parent_to_bone(cylinder_between(collection, f"AV_KneeGasket.{ring_index}{suffix}", a, b, 0.108 - ring_index * 0.006, mat["rubber"], vertices=16), armature, "LowerLeg" + suffix)

    calf_span_start = lower_leg_start.lerp(lower_leg_end, 0.18)
    calf_span_end = lower_leg_start.lerp(lower_leg_end, 0.86)
    parent_to_bone(loft_between(collection, "AV_CalfFrame" + suffix, calf_span_start, calf_span_end, [(0.0, 0.125, 0.115), (0.34, 0.135, 0.122), (1.0, 0.085, 0.080)], mat["teal"], vertices=14, bevel=0.006), armature, "LowerLeg" + suffix)
    top, bottom = (lower_leg_start, lower_leg_end) if lower_leg_start.z >= lower_leg_end.z else (lower_leg_end, lower_leg_start)
    shin_plate = [(top.x - 0.088, top.z - 0.065), (top.x + 0.088, top.z - 0.065), (bottom.x + 0.060, bottom.z + 0.060), (bottom.x, bottom.z + 0.018), (bottom.x - 0.060, bottom.z + 0.060)]
    parent_to_bone(panel_xz(collection, "AV_ShinPlate" + suffix, shin_plate, 0.143, 0.045, mat["ceramic"], bevel=0.009), armature, "LowerLeg" + suffix)

    foot_center = Vector((0.17 * sign, 0.105, 0.105))
    boot_profile = [(-0.115, 0.175), (0.055, 0.220), (0.275, 0.145), (0.300, 0.072), (0.235, 0.042), (-0.120, 0.042)]
    parent_to_bone(panel_yz(collection, "AV_BootBody" + suffix, boot_profile, foot_center.x, 0.205, mat["ceramic_dark"], bevel=0.010), armature, "Foot" + suffix)
    toe_profile = [(0.035, 0.175), (0.270, 0.140), (0.292, 0.082), (0.225, 0.060), (0.020, 0.082)]
    parent_to_bone(panel_yz(collection, "AV_BootToe" + suffix, toe_profile, foot_center.x, 0.180, mat["ceramic"], bevel=0.008), armature, "Foot" + suffix)
    toe_inset = [(0.070, 0.145), (0.245, 0.118), (0.260, 0.086), (0.205, 0.072), (0.060, 0.090)]
    parent_to_bone(panel_yz(collection, "AV_BootToeInset" + suffix, toe_inset, foot_center.x, 0.188, mat["teal"], bevel=0.005), armature, "Foot" + suffix)
    parent_to_bone(cube(collection, "AV_BootCuff" + suffix, (foot_center.x, -0.035, 0.205), (0.180, 0.175, 0.095), mat["teal"], bevel=0.010), armature, "Foot" + suffix)
    heel_profile = [(-0.165, 0.165), (-0.060, 0.200), (0.018, 0.160), (0.005, 0.062), (-0.145, 0.046)]
    parent_to_bone(panel_yz(collection, "AV_BootHeel" + suffix, heel_profile, foot_center.x, 0.185, mat["ceramic_dark"], bevel=0.008), armature, "Foot" + suffix)
    sole_profile = [(-0.165, 0.055), (0.255, 0.055), (0.325, 0.036), (0.312, 0.006), (-0.135, 0.006), (-0.178, 0.024)]
    parent_to_bone(panel_yz(collection, "AV_Sole" + suffix, sole_profile, foot_center.x, 0.212, mat["steel_dark"], bevel=0.006), armature, "Foot" + suffix)
    for tread in range(4):
        parent_to_bone(cube(collection, f"AV_BootTread{tread}{suffix}", (foot_center.x, -0.065 + tread * 0.100, -0.004), (0.190, 0.055, 0.018), mat["rubber"], bevel=0.003), armature, "Foot" + suffix)
    parent_to_bone(ring(collection, "AV_BootThrusterRing" + suffix, (foot_center.x, -0.137, 0.115), 0.044, 0.010, mat["copper"], axis="Y"), armature, "Foot" + suffix)
    parent_to_bone(ellipsoid(collection, "AV_BootThrusterCore" + suffix, (foot_center.x, -0.128, 0.115), (0.052, 0.018, 0.052), mat["cyan"]), armature, "Foot" + suffix)


def build_backpack(collection, armature, mat) -> None:
    # Keep the diagonal rifle corridor clear: nacelles sit outboard rather than
    # occupying the center-back path used by the stowed precision rifle.
    parent_to_bone(panel_xz(collection, "AV_BackpackSpine", [(-0.105, 1.715), (0.105, 1.715), (0.120, 1.325), (0.0, 1.255), (-0.120, 1.325)], -0.270, 0.105, mat["steel_dark"], bevel=0.012), armature, "Chest")
    parent_to_bone(panel_xz(collection, "AV_BackpackSpineInset", [(-0.065, 1.670), (0.065, 1.670), (0.070, 1.360), (0.0, 1.305), (-0.070, 1.360)], -0.329, 0.018, mat["teal"], bevel=0.006), armature, "Chest")
    for index, z in enumerate((1.385, 1.465, 1.545, 1.625)):
        parent_to_bone(cube(collection, f"AV_BackpackStatus.{index}", (0.0, -0.345, z), (0.030, 0.010, 0.043), mat["cyan" if index in (0, 3) else "ceramic"], bevel=0.004), armature, "Chest")
    for side, x in (("L", 0.450), ("R", -0.450)):
        center = Vector((x, -0.245, 1.570))
        start = center + Vector((0.0, -0.115, 0.0))
        end = center + Vector((0.0, 0.105, 0.0))
        parent_to_bone(cylinder_between(collection, f"AV_TurbineNacelle.{side}", start, end, 0.145, mat["steel_dark"], radial_scale=(1.0, 1.0), vertices=32), armature, "Chest")
        parent_to_bone(ring(collection, f"AV_TurbineOuterRim.{side}", (x, -0.374, 1.570), 0.123, 0.018, mat["ceramic"], axis="Y"), armature, "Chest")
        parent_to_bone(ring(collection, f"AV_TurbineTealRing.{side}", (x, -0.365, 1.570), 0.093, 0.013, mat["teal"], axis="Y"), armature, "Chest")
        parent_to_bone(ellipsoid(collection, f"AV_TurbineThroat.{side}", (x, -0.347, 1.570), (0.205, 0.030, 0.205), mat["rubber"]), armature, "Chest")
        for fin in range(10):
            angle = math.tau * fin / 10.0 + 0.12
            radial = 0.072
            fin_center = (x + math.cos(angle) * radial, -0.359, 1.570 + math.sin(angle) * radial)
            parent_to_bone(cube(collection, f"AV_TurbineBlade.{side}.{fin}", fin_center, (0.058, 0.010, 0.014), mat["steel"], rotation=(0.0, -angle - 0.34, 0.0), bevel=0.003), armature, "Chest")
        parent_to_bone(ring(collection, f"AV_TurbineHubRing.{side}", (x, -0.352, 1.570), 0.035, 0.008, mat["steel"], axis="Y"), armature, "Chest")
        parent_to_bone(ellipsoid(collection, f"AV_TurbineCore.{side}", (x, -0.350, 1.570), (0.048, 0.012, 0.048), mat["cyan"]), armature, "Chest")

        sign = 1.0 if side == "L" else -1.0
        outer_profile = [(0.285 * sign, 1.705), (0.555 * sign, 1.690), (0.605 * sign, 1.600), (0.585 * sign, 1.420), (0.505 * sign, 1.355), (0.320 * sign, 1.405)]
        if side == "R":
            outer_profile.reverse()
        parent_to_bone(panel_xz(collection, f"AV_BackpackFairing.{side}", outer_profile, -0.215, 0.095, mat["ceramic_dark"], bevel=0.011), armature, "Chest")
        parent_to_bone(cylinder_between(collection, f"AV_TurbineBraceUpper.{side}", Vector((0.135 * sign, -0.245, 1.680)), Vector((0.390 * sign, -0.250, 1.640)), 0.026, mat["steel"], radial_scale=(1.0, 0.72), vertices=16), armature, "Chest")
        parent_to_bone(cylinder_between(collection, f"AV_TurbineBraceLower.{side}", Vector((0.130 * sign, -0.245, 1.385)), Vector((0.390 * sign, -0.250, 1.485)), 0.024, mat["steel"], radial_scale=(1.0, 0.72), vertices=16), armature, "Chest")
        parent_to_bone(cable_between(collection, f"AV_TurbineFeed.{side}", [(0.120 * sign, -0.265, 1.405), (0.270 * sign, -0.315, 1.455), (0.405 * sign, -0.285, 1.525)], 0.010, mat["rubber"]), armature, "Chest")
        for collar_index, point in enumerate(((0.120 * sign, -0.265, 1.405), (0.405 * sign, -0.285, 1.525))):
            parent_to_bone(fastener(collection, f"AV_TurbineFeedCollar.{side}.{collar_index}", point, mat["copper"], 0.015, 0.010), armature, "Chest")


def add_studio(collection, mat) -> tuple[bpy.types.Object, list[bpy.types.Object]]:
    ground = cube(collection, "AV_StudioGround", (0.0, 0.0, -0.055), (7.0, 7.0, 0.10), mat["studio"], bevel=0.0)
    ground["aegis_studio_only"] = True
    lights = []
    for name, location, energy, color, size in (
        ("AV_Key", (3.5, 4.0, 4.2), 720.0, (1.0, 0.92, 0.82), 2.4),
        ("AV_Fill", (-3.8, 2.7, 3.0), 360.0, (0.50, 0.68, 1.0), 2.7),
        ("AV_Rim", (0.0, -3.8, 3.6), 980.0, (0.24, 0.62, 1.0), 2.0),
    ):
        data = bpy.data.lights.new(name, "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = size
        obj = bpy.data.objects.new(name, data)
        collection.objects.link(obj)
        obj.location = location
        lights.append(obj)
    return ground, lights


def point_at(obj: bpy.types.Object, target: Vector) -> None:
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_views(collection: bpy.types.Collection, lights: list[bpy.types.Object], mat) -> list[Path]:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1250
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.use_nodes = True
    world_background = scene.world.node_tree.nodes.get("Background")
    if world_background is not None:
        world_background.inputs["Color"].default_value = (0.025, 0.030, 0.040, 1.0)
        world_background.inputs["Strength"].default_value = 0.28
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = -0.55
    scene.render.image_settings.color_mode = "RGBA"

    camera_data = bpy.data.cameras.new("AV_ReviewCamera")
    camera = bpy.data.objects.new("AV_ReviewCamera", camera_data)
    collection.objects.link(camera)
    scene.camera = camera
    views = {
        "front": (Vector((0.0, 4.75, 1.38)), Vector((0.0, 0.0, 1.08)), 72.0),
        "front_3q": (Vector((2.45, 4.45, 1.45)), Vector((0.0, 0.0, 1.08)), 72.0),
        "side": (Vector((5.15, 0.0, 1.40)), Vector((0.0, 0.0, 1.08)), 72.0),
        "back": (Vector((0.0, -4.85, 1.40)), Vector((0.0, -0.08, 1.08)), 72.0),
        "back_3q": (Vector((-2.45, -4.45, 1.45)), Vector((0.0, -0.05, 1.08)), 72.0),
        "helmet_close": (Vector((0.92, 2.20, 1.99)), Vector((0.0, 0.0, 1.94)), 86.0),
        "backpack_close": (Vector((-1.05, -2.25, 1.72)), Vector((0.0, -0.22, 1.53)), 86.0),
    }
    RENDER_ROOT.mkdir(parents=True, exist_ok=True)
    paths: list[Path] = []
    for name, (location, target, lens) in views.items():
        camera.location = location
        camera.data.lens = lens
        point_at(camera, target)
        for light in lights:
            point_at(light, target)
        output = RENDER_ROOT / f"aegis_vanguard_{name}.png"
        scene.render.filepath = str(output)
        bpy.ops.render.render(write_still=True)
        paths.append(output)

    bpy.context.view_layer.material_override = mat["clay"]
    camera.location = views["front_3q"][0]
    camera.data.lens = views["front_3q"][2]
    point_at(camera, views["front_3q"][1])
    clay_output = RENDER_ROOT / "aegis_vanguard_clay_front_3q.png"
    scene.render.filepath = str(clay_output)
    bpy.ops.render.render(write_still=True)
    bpy.context.view_layer.material_override = None
    paths.append(clay_output)
    return paths


def render_pose_reviews(
    lights: list[bpy.types.Object],
    armature: bpy.types.Object,
    rifle_objects: list[bpy.types.Object],
) -> list[Path]:
    """Render contract-relevant poses from the real slotted Generator114 actions."""
    scene = bpy.context.scene
    camera = bpy.data.objects.get("AV_ReviewCamera")
    if camera is None:
        raise RuntimeError("Review camera was not created.")
    for obj in rifle_objects:
        obj.hide_render = False
        obj.hide_set(False)
    armature.data.pose_position = "POSE"
    armature.hide_set(True)
    armature.hide_render = True
    pose_views = {
        "pose_stowed": ("PS_WeaponStowed_Idle", 1, Vector((-2.55, -4.45, 1.45)), Vector((0.0, -0.05, 1.08)), 72.0),
        "pose_aim": ("PS_Aim", 1, Vector((-2.10, 4.20, 1.45)), Vector((0.0, 0.04, 1.18)), 74.0),
        "pose_run": ("PS_Run_Forward", 6, Vector((2.45, 4.45, 1.45)), Vector((0.0, 0.0, 1.08)), 72.0),
        "pose_hover": ("PS_Hover", 1, Vector((2.45, 4.45, 1.55)), Vector((0.0, 0.0, 1.13)), 72.0),
        "pose_reload": ("PS_Reload", 50, Vector((-2.20, 4.20, 1.47)), Vector((0.0, 0.03, 1.18)), 74.0),
    }
    paths: list[Path] = []
    for label, (action_name, frame, location, target, lens) in pose_views.items():
        action = bpy.data.actions.get(action_name)
        if action is None:
            raise RuntimeError(f"Required pose-review Action '{action_name}' is missing.")
        activate_action(armature, action)
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        if armature.data.pose_position != "POSE":
            raise RuntimeError(f"Armature did not enter POSE mode for {action_name}.")
        camera.location = location
        camera.data.lens = lens
        point_at(camera, target)
        for light in lights:
            point_at(light, target)
        output = RENDER_ROOT / f"aegis_vanguard_{label}.png"
        scene.render.filepath = str(output)
        bpy.ops.render.render(write_still=True)
        paths.append(output)

    armature.animation_data_clear()
    for pose_bone in armature.pose.bones:
        pose_bone.matrix_basis = Matrix.Identity(4)
    armature.data.pose_position = "REST"
    scene.frame_set(1)
    for obj in rifle_objects:
        obj.hide_render = True
        obj.hide_set(True)
    bpy.context.view_layer.update()
    return paths


def main() -> None:
    if bpy.app.version < (5, 2, 0):
        raise RuntimeError("Aegis candidate requires Blender 5.2 or newer.")
    current = Path(bpy.data.filepath).resolve()
    if current != LEGACY_BLEND.resolve():
        raise RuntimeError(f"Expected approved working source {LEGACY_BLEND}, got {current}")
    legacy_hash_before = sha256(LEGACY_BLEND)
    armature = bpy.data.objects.get("PowerSuit_Armature")
    if armature is None or armature.type != "ARMATURE":
        raise RuntimeError("PowerSuit_Armature is missing.")

    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    armature.data.pose_position = "REST"
    armature.animation_data_clear()
    collection = ensure_clean_collection()
    mat = materials()

    # The validated suit stays in this blend as a hidden rollback collection of objects.
    for name in LEGACY_SUIT_OBJECTS:
        obj = bpy.data.objects.get(name)
        if obj is not None:
            obj.hide_render = True
            obj.hide_set(True)
            obj["preserved_legacy_generator114"] = True
    rifle_objects = [obj for obj in bpy.data.objects if obj.name.startswith("Rifle")]
    for obj in bpy.data.objects:
        if obj in rifle_objects or obj.name == "Preview_Ground":
            obj.hide_render = True
            obj.hide_set(True)
    armature.show_in_front = False
    armature.hide_render = True

    build_core(collection, armature, mat)
    build_head(collection, armature, mat)
    build_limb(collection, armature, mat, "L")
    build_limb(collection, armature, mat, "R")
    build_backpack(collection, armature, mat)
    create_runtime_anchors(collection)
    _ground, lights = add_studio(collection, mat)

    CANDIDATE_BLEND.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(CANDIDATE_BLEND), check_existing=False)
    render_paths = render_views(collection, lights, mat)
    render_paths.extend(render_pose_reviews(lights, armature, rifle_objects))
    bpy.ops.wm.save_as_mainfile(filepath=str(CANDIDATE_BLEND), check_existing=False)

    legacy_hash_after = sha256(LEGACY_BLEND)
    if legacy_hash_after != legacy_hash_before:
        raise RuntimeError("Approved legacy blend changed during candidate generation.")
    candidate_objects = [obj for obj in collection.objects if obj.get("aegis_vanguard_candidate")]
    renderable_objects = [obj for obj in candidate_objects if obj.type in {"MESH", "CURVE"}]
    mesh_objects = [obj for obj in renderable_objects if obj.type == "MESH"]
    curve_objects = [obj for obj in renderable_objects if obj.type == "CURVE"]
    polygon_count = sum(len(obj.data.polygons) for obj in mesh_objects)
    triangle_count = sum(sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons) for obj in mesh_objects)
    actions = sorted(action.name for action in bpy.data.actions if action.name.startswith("PS_"))
    if len(actions) != 24:
        raise RuntimeError(f"Candidate must preserve exactly 24 PS_* Actions, found {len(actions)}.")
    unparented = [
        obj.name
        for obj in candidate_objects
        if not obj.get("aegis_runtime_anchor")
        and (obj.parent != armature or obj.parent_type != "BONE")
    ]
    if unparented:
        raise RuntimeError(f"Candidate objects are not bone-parented: {unparented}")
    bpy.context.view_layer.update()
    anchor_validation: dict[str, dict[str, object]] = {}
    for name, (expected_tuple, expected_target, expected_bone) in RUNTIME_ANCHORS.items():
        anchor = bpy.data.objects.get(name)
        if anchor is None:
            raise RuntimeError(f"Candidate runtime anchor '{name}' is missing.")
        target = bpy.data.objects.get(expected_target)
        if target is None:
            raise RuntimeError(f"Candidate runtime anchor target '{expected_target}' is missing.")
        expected = Vector(expected_tuple)
        actual = anchor.matrix_world.translation.copy()
        error = (actual - expected).length
        if anchor.parent != target or target.parent != armature or target.parent_bone != expected_bone:
            raise RuntimeError(f"Candidate runtime anchor '{name}' has the wrong parent contract.")
        if error > 0.0001:
            raise RuntimeError(
                f"Candidate runtime anchor '{name}' is {error:.6f} m from its authored hardware position."
            )
        anchor_validation[name] = {
            "world_position": [round(value, 6) for value in actual],
            "parent_object": expected_target,
            "expected_bone": expected_bone,
            "position_error_m": error,
        }
    report = {
        "candidate": "Aegis Vanguard Candidate003",
        "status": "REVIEW_ONLY_NOT_UNITY_INTEGRATED",
        "source_blend": str(LEGACY_BLEND),
        "source_sha256_before": legacy_hash_before,
        "source_sha256_after": legacy_hash_after,
        "legacy_preserved": legacy_hash_before == legacy_hash_after,
        "candidate_blend": str(CANDIDATE_BLEND),
        "candidate_blend_sha256": sha256(CANDIDATE_BLEND),
        "candidate_objects": len(candidate_objects),
        "candidate_mesh_objects": len(mesh_objects),
        "candidate_curve_objects": len(curve_objects),
        "candidate_polygons": polygon_count,
        "candidate_mesh_triangles_estimate": triangle_count,
        "armature": armature.name,
        "bone_count": len(armature.data.bones),
        "runtime_anchor_validation": anchor_validation,
        "preserved_actions": actions,
        "render_paths": [str(path) for path in render_paths],
        "limitations": [
            "Procedural concept maquette; not final production topology, UVs, texture bake, or skin deformation.",
            "Selected action poses are rendered for review, but geometric intersection gates remain pending.",
            "No Unity prefab or FBX was replaced.",
        ],
    }
    REPORT_PATH.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
