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
from pathlib import Path

import bpy  # type: ignore
from mathutils import Euler, Matrix, Vector  # type: ignore


ROOT = Path(__file__).resolve().parents[3]
LEGACY_BLEND = ROOT / "ArtSource" / "PoweredSuit" / "powersuit_pipeline.blend"
OUTPUT_ROOT = ROOT / "ArtSource" / "PoweredSuitNextGen"
CANDIDATE_BLEND = OUTPUT_ROOT / "candidates" / "aegis_vanguard_blockout_v002.blend"
RENDER_ROOT = OUTPUT_ROOT / "renders" / "aegis_vanguard_blockout_v002"
REPORT_PATH = OUTPUT_ROOT / "candidates" / "aegis_vanguard_blockout_v002.json"
COLLECTION_NAME = "Aegis_Vanguard_V001"

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
    if emission is None:
        noise = nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 85.0
        noise.inputs["Detail"].default_value = 2.2
        noise.inputs["Roughness"].default_value = 0.6
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (roughness * 0.72,) * 3 + (1.0,)
        ramp.color_ramp.elements[1].color = (min(1.0, roughness * 1.22),) * 3 + (1.0,)
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.07
        bump.inputs["Distance"].default_value = 0.025
        links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
        links.new(ramp.outputs["Color"], shader.inputs["Roughness"])
        links.new(noise.outputs["Fac"], bump.inputs["Height"])
        links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return material


def materials() -> dict[str, bpy.types.Material]:
    return {
        "ceramic": make_material("AV_Ceramic", (0.62, 0.57, 0.47, 1.0), 0.48, 0.32),
        "teal": make_material("AV_DeepTeal", (0.035, 0.16, 0.17, 1.0), 0.64, 0.27),
        "undersuit": make_material("AV_Undersuit", (0.012, 0.018, 0.021, 1.0), 0.08, 0.69),
        "steel": make_material("AV_Steel", (0.105, 0.125, 0.13, 1.0), 0.88, 0.22),
        "copper": make_material("AV_Copper", (0.36, 0.14, 0.045, 1.0), 0.80, 0.28),
        "cyan": make_material(
            "AV_CyanEmission",
            (0.0, 0.18, 0.22, 1.0),
            0.38,
            0.12,
            emission=(0.0, 0.82, 1.0, 1.0),
            emission_strength=7.5,
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
    bevel.width = width
    bevel.segments = segments
    bevel.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    obj.select_set(False)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True


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


def bone_points(armature: bpy.types.Object, name: str) -> tuple[Vector, Vector]:
    bone = armature.data.bones[name]
    return armature.matrix_world @ bone.head_local, armature.matrix_world @ bone.tail_local


def build_core(collection, armature, mat) -> None:
    # Flexible chassis first: it remains visible between deliberately separated plates.
    for name, center, dims, bone in (
        ("AV_UnderChest", (0.0, 0.0, 1.49), (0.56, 0.31, 0.40), "Chest"),
        ("AV_UnderAbdomen", (0.0, 0.0, 1.25), (0.38, 0.27, 0.33), "Spine"),
        ("AV_UnderPelvis", (0.0, 0.0, 1.03), (0.46, 0.29, 0.25), "Hips"),
        ("AV_UnderNeck", (0.0, 0.0, 1.72), (0.22, 0.22, 0.18), "Neck"),
    ):
        parent_to_bone(ellipsoid(collection, name, center, dims, mat["undersuit"]), armature, bone)

    parent_to_bone(
        frustum(collection, "AV_ChestShell", (0.0, -0.01, 1.50), (0.43, 0.28), (0.63, 0.34), 0.37, mat["steel"], bevel=0.030),
        armature, "Chest",
    )
    # Split front armor and a strong original vertical service-channel motif.
    for side, x, angle in (("L", 0.145, -0.13), ("R", -0.145, 0.13)):
        parent_to_bone(
            cube(collection, f"AV_Pectoral.{side}", (x, 0.185, 1.53), (0.235, 0.062, 0.215), mat["ceramic"], rotation=(0.0, 0.0, angle), bevel=0.027),
            armature, "Chest",
        )
        parent_to_bone(
            cube(collection, f"AV_RibPlate.{side}", (x * 1.35, 0.165, 1.35), (0.17, 0.048, 0.095), mat["teal"], rotation=(0.0, 0.0, angle * 0.7), bevel=0.016),
            armature, "Spine",
        )
    parent_to_bone(cube(collection, "AV_Sternum", (0.0, 0.215, 1.50), (0.095, 0.055, 0.30), mat["steel"], bevel=0.015), armature, "Chest")
    for index, z in enumerate((1.39, 1.49, 1.59)):
        parent_to_bone(cube(collection, f"AV_ReactorSegment.{index}", (0.0, 0.246, z), (0.044, 0.018, 0.060), mat["cyan"], bevel=0.008), armature, "Chest")
    for index, (z, width) in enumerate(((1.18, 0.30), (1.26, 0.34), (1.34, 0.38))):
        parent_to_bone(cube(collection, f"AV_AbPlate.{index}", (0.0, 0.155, z), (width, 0.055, 0.090), mat["ceramic" if index != 1 else "teal"], bevel=0.018), armature, "Spine")
    parent_to_bone(frustum(collection, "AV_PelvisShell", (0.0, 0.0, 1.03), (0.43, 0.27), (0.36, 0.24), 0.22, mat["ceramic"], bevel=0.025), armature, "Hips")
    parent_to_bone(cube(collection, "AV_PelvisFront", (0.0, 0.155, 1.05), (0.23, 0.052, 0.12), mat["teal"], bevel=0.020), armature, "Hips")
    for side, x in (("L", 0.265), ("R", -0.265)):
        parent_to_bone(ellipsoid(collection, f"AV_HipGuard.{side}", (x, 0.01, 1.02), (0.13, 0.23, 0.25), mat["ceramic"]), armature, "Hips")
    parent_to_bone(cube(collection, "AV_BackSpine", (0.0, -0.205, 1.43), (0.13, 0.055, 0.49), mat["steel"], bevel=0.018), armature, "Chest")
    for z in (1.30, 1.42, 1.54, 1.66):
        parent_to_bone(cube(collection, f"AV_BackSpinePlate.{z:.2f}", (0.0, -0.245, z), (0.18, 0.045, 0.085), mat["teal"], bevel=0.012), armature, "Chest")


def build_head(collection, armature, mat) -> None:
    parent_to_bone(ellipsoid(collection, "AV_HelmetChassis", (0.0, 0.0, 1.92), (0.31, 0.29, 0.36), mat["ceramic"]), armature, "Head")
    parent_to_bone(frustum(collection, "AV_FacePlate", (0.0, 0.158, 1.94), (0.20, 0.060), (0.275, 0.070), 0.25, mat["teal"], bevel=0.023), armature, "Head")
    parent_to_bone(cube(collection, "AV_OpticalBand", (0.0, 0.207, 2.00), (0.265, 0.022, 0.045), mat["cyan"], bevel=0.010), armature, "Head")
    # Break the band into three facets with dark separators.
    for x in (-0.085, 0.085):
        parent_to_bone(cube(collection, f"AV_OpticDivider.{x:+.3f}", (x, 0.220, 2.00), (0.014, 0.012, 0.052), mat["steel"], bevel=0.004), armature, "Head")
    parent_to_bone(cube(collection, "AV_HelmetCrown", (0.0, 0.005, 2.100), (0.18, 0.22, 0.055), mat["steel"], bevel=0.020), armature, "Head")
    parent_to_bone(cube(collection, "AV_JawGuard", (0.0, 0.170, 1.84), (0.18, 0.060, 0.090), mat["ceramic"], bevel=0.020), armature, "Head")
    for side, x in (("L", 0.175), ("R", -0.175)):
        parent_to_bone(ring(collection, f"AV_HelmetPivot.{side}", (x, 0.0, 1.95), 0.060, 0.014, mat["copper"], axis="X"), armature, "Head")
        parent_to_bone(cube(collection, f"AV_CheekPlate.{side}", (x * 0.78, 0.175, 1.90), (0.080, 0.050, 0.16), mat["ceramic"], rotation=(0.0, 0.0, -0.10 if side == "L" else 0.10), bevel=0.014), armature, "Head")
    for side, x in (("L", 0.19), ("R", -0.19)):
        parent_to_bone(cube(collection, f"AV_Collar.{side}", (x, 0.0, 1.72), (0.21, 0.25, 0.085), mat["ceramic"], rotation=(0.0, 0.0, -0.12 if side == "L" else 0.12), bevel=0.022), armature, "Chest")


def build_limb(collection, armature, mat, side: str) -> None:
    suffix = ".L" if side == "L" else ".R"
    sign = 1.0 if side == "L" else -1.0
    upper_start, upper_end = bone_points(armature, "UpperArm" + suffix)
    lower_start, lower_end = bone_points(armature, "LowerArm" + suffix)
    hand_start, hand_end = bone_points(armature, "Hand" + suffix)
    upper_leg_start, upper_leg_end = bone_points(armature, "UpperLeg" + suffix)
    lower_leg_start, lower_leg_end = bone_points(armature, "LowerLeg" + suffix)

    # Arm understructure and plated shells.
    for label, start, end, bone, radius in (
        ("UpperArm", upper_start, upper_end, "UpperArm" + suffix, 0.095),
        ("Forearm", lower_start, lower_end, "LowerArm" + suffix, 0.090),
        ("Thigh", upper_leg_start, upper_leg_end, "UpperLeg" + suffix, 0.125),
        ("Calf", lower_leg_start, lower_leg_end, "LowerLeg" + suffix, 0.105),
    ):
        parent_to_bone(cylinder_between(collection, f"AV_{label}Under{suffix}", start, end, radius, mat["undersuit"], radial_scale=(1.0, 0.92)), armature, bone)
    shoulder = Vector((0.34 * sign, 0.0, 1.61))
    parent_to_bone(ellipsoid(collection, "AV_ShoulderJoint" + suffix, shoulder, (0.22, 0.22, 0.22), mat["steel"]), armature, "UpperArm" + suffix)
    parent_to_bone(ellipsoid(collection, "AV_ShoulderShell" + suffix, (0.37 * sign, 0.015, 1.62), (0.27, 0.30, 0.20), mat["ceramic"]), armature, "UpperArm" + suffix)
    parent_to_bone(cube(collection, "AV_ShoulderInset" + suffix, (0.40 * sign, 0.175, 1.63), (0.20, 0.045, 0.12), mat["teal"], rotation=(0.0, 0.0, -0.10 * sign), bevel=0.018), armature, "UpperArm" + suffix)

    upper_mid = (upper_start + upper_end) * 0.5
    lower_mid = (lower_start + lower_end) * 0.5
    parent_to_bone(cylinder_between(collection, "AV_UpperArmShell" + suffix, upper_start + (upper_end-upper_start)*0.10, upper_end - (upper_end-upper_start)*0.18, 0.100, mat["teal"], radial_scale=(1.0, 0.84)), armature, "UpperArm" + suffix)
    parent_to_bone(ellipsoid(collection, "AV_Elbow" + suffix, upper_end, (0.18, 0.18, 0.18), mat["steel"]), armature, "LowerArm" + suffix)
    parent_to_bone(cylinder_between(collection, "AV_ForearmShell" + suffix, lower_start + (lower_end-lower_start)*0.12, lower_end - (lower_end-lower_start)*0.08, 0.105, mat["ceramic"], radial_scale=(1.0, 0.80)), armature, "LowerArm" + suffix)
    parent_to_bone(cube(collection, "AV_ForearmPlate" + suffix, tuple(lower_mid + Vector((0.0, 0.105, 0.0))), (0.13, 0.045, 0.27), mat["teal"], bevel=0.018), armature, "LowerArm" + suffix)
    parent_to_bone(ellipsoid(collection, "AV_Hand" + suffix, hand_start + (hand_end-hand_start)*0.35, (0.15, 0.16, 0.20), mat["steel"]), armature, "Hand" + suffix)
    for finger in range(4):
        x_offset = sign * (0.045 - finger * 0.030)
        start = hand_end + Vector((x_offset, 0.010, 0.025))
        end = start + Vector((0.0, 0.0, -0.095))
        parent_to_bone(cylinder_between(collection, f"AV_Finger{finger}{suffix}", start, end, 0.013, mat["steel"], vertices=12), armature, "Hand" + suffix)

    # Legs emphasize powered-suit mass while preserving large joint gaps.
    thigh_mid = (upper_leg_start + upper_leg_end) * 0.5
    calf_mid = (lower_leg_start + lower_leg_end) * 0.5
    parent_to_bone(frustum(collection, "AV_ThighShell" + suffix, tuple(thigh_mid), (0.20, 0.22), (0.26, 0.27), (upper_leg_start-upper_leg_end).length * 0.76, mat["ceramic"], bevel=0.027), armature, "UpperLeg" + suffix)
    parent_to_bone(cube(collection, "AV_ThighInset" + suffix, tuple(thigh_mid + Vector((0.0, 0.145, 0.02))), (0.15, 0.045, 0.27), mat["teal"], bevel=0.018), armature, "UpperLeg" + suffix)
    parent_to_bone(ellipsoid(collection, "AV_KneeJoint" + suffix, upper_leg_end, (0.19, 0.19, 0.17), mat["steel"]), armature, "LowerLeg" + suffix)
    parent_to_bone(cube(collection, "AV_KneeGuard" + suffix, tuple(upper_leg_end + Vector((0.0, 0.125, 0.01))), (0.17, 0.070, 0.18), mat["ceramic"], bevel=0.025), armature, "LowerLeg" + suffix)
    parent_to_bone(frustum(collection, "AV_CalfShell" + suffix, tuple(calf_mid), (0.20, 0.22), (0.17, 0.19), (lower_leg_start-lower_leg_end).length * 0.78, mat["teal"], bevel=0.025), armature, "LowerLeg" + suffix)
    parent_to_bone(cube(collection, "AV_ShinPlate" + suffix, tuple(calf_mid + Vector((0.0, 0.135, 0.01))), (0.15, 0.050, 0.25), mat["ceramic"], bevel=0.020), armature, "LowerLeg" + suffix)
    foot_center = Vector((0.17 * sign, 0.12, 0.105))
    parent_to_bone(frustum(collection, "AV_Boot" + suffix, tuple(foot_center), (0.20, 0.35), (0.22, 0.30), 0.18, mat["ceramic"], rotation=(math.pi * 0.5, 0.0, 0.0), bevel=0.030), armature, "Foot" + suffix)
    parent_to_bone(cube(collection, "AV_BootTop" + suffix, (foot_center.x, 0.055, 0.190), (0.17, 0.23, 0.10), mat["teal"], bevel=0.025), armature, "Foot" + suffix)
    parent_to_bone(cube(collection, "AV_Sole" + suffix, (foot_center.x, 0.13, 0.020), (0.23, 0.38, 0.050), mat["steel"], bevel=0.018), armature, "Foot" + suffix)
    parent_to_bone(ring(collection, "AV_BootThrusterRing" + suffix, (foot_center.x, -0.105, 0.105), 0.050, 0.012, mat["copper"], axis="Y"), armature, "Foot" + suffix)
    parent_to_bone(ellipsoid(collection, "AV_BootThrusterCore" + suffix, (foot_center.x, -0.115, 0.105), (0.070, 0.028, 0.070), mat["cyan"]), armature, "Foot" + suffix)


def build_backpack(collection, armature, mat) -> None:
    parent_to_bone(frustum(collection, "AV_BackpackSpine", (0.0, -0.245, 1.52), (0.27, 0.16), (0.22, 0.14), 0.42, mat["steel"], bevel=0.025), armature, "Chest")
    for side, x in (("L", 0.23), ("R", -0.23)):
        center = Vector((x, -0.255, 1.57))
        # Turbine housing aligned front-to-back.
        start = center + Vector((0.0, -0.12, 0.0))
        end = center + Vector((0.0, 0.10, 0.0))
        parent_to_bone(cylinder_between(collection, f"AV_TurbineHousing.{side}", start, end, 0.128, mat["steel"], radial_scale=(1.0, 1.0), vertices=28), armature, "Chest")
        parent_to_bone(ring(collection, f"AV_TurbineRim.{side}", tuple(center + Vector((0.0, -0.13, 0.0))), 0.105, 0.018, mat["ceramic"], axis="Y"), armature, "Chest")
        parent_to_bone(ellipsoid(collection, f"AV_TurbineCore.{side}", tuple(center + Vector((0.0, -0.142, 0.0))), (0.145, 0.035, 0.145), mat["cyan"]), armature, "Chest")
        for fin in range(6):
            angle = math.tau * fin / 6.0
            offset = Vector((math.cos(angle) * 0.070, -0.156, math.sin(angle) * 0.070))
            parent_to_bone(cube(collection, f"AV_TurbineFin.{side}.{fin}", tuple(center + offset), (0.050, 0.012, 0.016), mat["steel"], rotation=(0.0, -angle, 0.0), bevel=0.004), armature, "Chest")
    for side, x in (("L", 0.36), ("R", -0.36)):
        parent_to_bone(cube(collection, f"AV_BackpackVane.{side}", (x, -0.21, 1.49), (0.075, 0.16, 0.30), mat["ceramic"], rotation=(0.0, 0.0, -0.12 if side == "L" else 0.12), bevel=0.018), armature, "Chest")


def add_studio(collection, mat) -> tuple[bpy.types.Object, list[bpy.types.Object]]:
    ground = cube(collection, "AV_StudioGround", (0.0, 0.0, -0.055), (7.0, 7.0, 0.10), mat["undersuit"], bevel=0.0)
    ground["aegis_studio_only"] = True
    lights = []
    for name, location, energy, color, size in (
        ("AV_Key", (4.0, 4.5, 5.8), 1500.0, (1.0, 0.88, 0.74), 4.0),
        ("AV_Fill", (-4.5, 3.0, 3.5), 1050.0, (0.55, 0.72, 1.0), 4.0),
        ("AV_Rim", (0.0, -4.0, 4.5), 1350.0, (0.30, 0.65, 1.0), 3.0),
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


def render_views(collection: bpy.types.Collection, lights: list[bpy.types.Object]) -> None:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1100
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.color = (0.035, 0.035, 0.035)
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.render.image_settings.color_mode = "RGBA"

    camera_data = bpy.data.cameras.new("AV_ReviewCamera")
    camera = bpy.data.objects.new("AV_ReviewCamera", camera_data)
    collection.objects.link(camera)
    camera.data.lens = 68
    scene.camera = camera
    target = Vector((0.0, 0.0, 1.08))
    views = {
        "front_3q": Vector((3.0, 5.6, 2.55)),
        "side": Vector((6.2, 0.0, 2.35)),
        "back_3q": Vector((-3.0, -5.6, 2.55)),
    }
    RENDER_ROOT.mkdir(parents=True, exist_ok=True)
    for name, location in views.items():
        camera.location = location
        point_at(camera, target)
        for light in lights:
            point_at(light, target)
        scene.render.filepath = str(RENDER_ROOT / f"aegis_vanguard_{name}.png")
        bpy.ops.render.render(write_still=True)


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
    for obj in bpy.data.objects:
        if obj.name.startswith("Rifle") or obj.name == "Preview_Ground":
            obj.hide_render = True
            obj.hide_set(True)
    armature.show_in_front = False
    armature.hide_render = True

    build_core(collection, armature, mat)
    build_head(collection, armature, mat)
    build_limb(collection, armature, mat, "L")
    build_limb(collection, armature, mat, "R")
    build_backpack(collection, armature, mat)
    _ground, lights = add_studio(collection, mat)

    CANDIDATE_BLEND.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(CANDIDATE_BLEND), check_existing=False)
    render_views(collection, lights)
    bpy.ops.wm.save_as_mainfile(filepath=str(CANDIDATE_BLEND), check_existing=False)

    legacy_hash_after = sha256(LEGACY_BLEND)
    if legacy_hash_after != legacy_hash_before:
        raise RuntimeError("Approved legacy blend changed during candidate generation.")
    candidate_objects = [obj for obj in collection.objects if obj.type == "MESH" and not obj.get("aegis_studio_only")]
    polygon_count = sum(len(obj.data.polygons) for obj in candidate_objects)
    triangle_count = sum(sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons) for obj in candidate_objects)
    report = {
        "candidate": "Aegis Vanguard blockout v002",
        "status": "REVIEW_ONLY_NOT_UNITY_INTEGRATED",
        "source_blend": str(LEGACY_BLEND),
        "source_sha256_before": legacy_hash_before,
        "source_sha256_after": legacy_hash_after,
        "legacy_preserved": legacy_hash_before == legacy_hash_after,
        "candidate_blend": str(CANDIDATE_BLEND),
        "candidate_blend_sha256": sha256(CANDIDATE_BLEND),
        "candidate_mesh_objects": len(candidate_objects),
        "candidate_polygons": polygon_count,
        "candidate_triangles_estimate": triangle_count,
        "armature": armature.name,
        "bone_count": len(armature.data.bones),
        "render_paths": [
            str(RENDER_ROOT / "aegis_vanguard_front_3q.png"),
            str(RENDER_ROOT / "aegis_vanguard_side.png"),
            str(RENDER_ROOT / "aegis_vanguard_back_3q.png"),
        ],
        "limitations": [
            "Concept blockout; not final production topology or texture bake.",
            "Existing animations and weapon were preserved but not revalidated against this shell.",
            "No Unity prefab or FBX was replaced.",
        ],
    }
    REPORT_PATH.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
