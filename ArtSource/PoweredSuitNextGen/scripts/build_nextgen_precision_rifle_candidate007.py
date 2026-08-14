"""Build Candidate007 / NextGen Precision Rifle 002 in an isolated review blend.

The builder consumes the immutable Candidate005 blend, replaces only the copied
rifle definition, rebuilds weapon-dependent poses around versioned hardpoints,
adds production LOD/UV/PBR/face-semantic metadata, and emits review evidence.
It never exports FBX or modifies Candidate005/Unity assets.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import math
import re
import sys
from collections import Counter
from pathlib import Path

import bmesh  # type: ignore
import bpy  # type: ignore
from bpy_extras.object_utils import world_to_camera_view  # type: ignore
from mathutils import Matrix, Vector  # type: ignore


ROOT = Path(__file__).resolve().parents[3]
NEXTGEN = ROOT / "ArtSource" / "PoweredSuitNextGen"
PIPELINE = ROOT / "ArtSource" / "PoweredSuit" / "scripts"
SOURCE_BLEND = NEXTGEN / "candidates" / "aegis_vanguard_candidate_v005.blend"
SOURCE_REPORT = NEXTGEN / "candidates" / "aegis_vanguard_candidate_v005.json"
OUTPUT_BLEND = NEXTGEN / "candidates" / "nextgen_precision_rifle_candidate_v007.blend"
OUTPUT_REPORT = NEXTGEN / "candidates" / "nextgen_precision_rifle_candidate_v007.json"
RENDER_ROOT = NEXTGEN / "renders" / "nextgen_precision_rifle_candidate_v007"
TEXTURE_ROOT = NEXTGEN / "textures" / "candidate006"
TEXTURE_MANIFEST = TEXTURE_ROOT / "manifest.json"
REAUTHOR_SCRIPT = Path(__file__).resolve().parent / "reauthor_candidate007_weapon_actions.py"
CONCEPT_REFERENCE = NEXTGEN / "Concepts" / "nextgen_precision_rifle_reference_v001.png"
EXPECTED_SOURCE_SHA256 = "0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84"
EXPECTED_CONCEPT_SHA256 = "8c99f32e7584bc0b49abf27b3fb029e417dd19ea77aab4da2b178c21b299db8c"
ASSET_ID = "PS_NextGenPrecisionRifle002"
ARMATURE_NAME = "PowerSuit_Armature"
RENDER_NAMES = (
    "nextgen_precision_rifle_neutral_front.png",
    "nextgen_precision_rifle_neutral_side.png",
    "nextgen_precision_rifle_neutral_front_3q.png",
    "nextgen_precision_rifle_pose_aim.png",
    "nextgen_precision_rifle_pose_hip_fire.png",
    "nextgen_precision_rifle_scope_ocular.png",
    "nextgen_precision_rifle_pose_reload.png",
    "nextgen_precision_rifle_pose_bolt.png",
    "nextgen_precision_rifle_pose_run.png",
    "nextgen_precision_rifle_pose_hover.png",
    "nextgen_precision_rifle_pose_stowed.png",
    "nextgen_precision_rifle_pose_draw.png",
    "nextgen_precision_rifle_pose_sheathe.png",
)
LOD_TARGETS = {0: 24800, 1: 12400, 2: 5000, 3: 1500}
RIFLE_COMPONENTS = "NGPR002_SourceComponents"
OPTIC_COMPONENTS = "NGPR002_OpticSourceComponents"
PRIMARY_GRIP_RESHAPE_ANCHOR_M = (-0.070, 0.000, 0.020)
PRIMARY_GRIP_RESHAPE_SCALE_XYZ = (0.85, 0.78, 0.65)
PRIMARY_GRIP_RESHAPE_SHIFT_M = (-0.012, 0.000, 0.000)
MAGWELL_NEGATIVE_X_RELIEF_M = 0.006
STOCK_CONTACT_PERIMETER_RELIEF_M = 0.0035
COMPONENT_ROLE_IDS = {
    "receiver": 1,
    "barrel": 2,
    "stock": 3,
    "handguard": 4,
    "optic_mount": 5,
    "magazine": 6,
    "bolt": 7,
}
COMPONENT_ROLE_TABLE = {
    str(identifier): role for role, identifier in COMPONENT_ROLE_IDS.items()
}
COMPONENT_ROLE_CONTROL_ASSIGNMENTS = {
    "receiver": ["WeaponRoot"],
    "barrel": ["WeaponRoot"],
    "stock": ["WeaponRoot"],
    "handguard": ["WeaponRoot"],
    "optic_mount": ["WeaponRoot"],
    "magazine": ["WeaponMagazine"],
    "bolt": ["WeaponBolt"],
}

if str(PIPELINE) not in sys.path:
    sys.path.insert(0, str(PIPELINE))
if str(Path(__file__).resolve().parent) not in sys.path:
    sys.path.insert(0, str(Path(__file__).resolve().parent))

from powersuit_pipeline_common import (  # type: ignore  # noqa: E402
    activate_action,
    find_action_slot,
    get_action_channelbag,
)
from weapon_handling_contract import (  # type: ignore  # noqa: E402
    COMPONENT_BOLT,
    COMPONENT_MAGAZINE,
    CONTRACT_VERSION,
    RIGID_SIGNATURE_VERSION,
    ROLE_MUZZLE,
    ROLE_PRIMARY_GRIP,
    ROLE_SIGHT_OCULAR,
    ROLE_STOCK_CONTACT,
    ROLE_SUPPORT_GRIP,
    ROLE_SUPPORT_MAX,
    ROLE_SUPPORT_MIN,
    WEAPON_OWNER_PROPERTY,
    assert_weapon_rigid,
    freeze_rigid_weapon,
    normalize_rigid_weapon_children,
    tag_component,
    tag_contact_surface,
    tag_helper,
    tag_weapon_root,
    validate_weapon_contract,
)
import clearance_face_policy as face_policy  # type: ignore  # noqa: E402


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_manifest_sha256(document: object) -> str:
    payload = json.dumps(
        document, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


WINDOWS_DRIVE_PATH = re.compile(r"^[A-Za-z]:[\\\\/]")
EXTERNAL_DATABLOCK_COLLECTIONS = (
    "images",
    "libraries",
    "movieclips",
    "sounds",
    "fonts",
)


def repository_relative_posix(path: Path) -> str:
    """Return a repository-contained path without leaking the build machine."""
    resolved = path.resolve()
    try:
        return resolved.relative_to(ROOT.resolve()).as_posix()
    except ValueError as exception:
        raise RuntimeError(
            f"Candidate007 path escapes the repository: {resolved}"
        ) from exception


def _is_absolute_or_drive_qualified_local_path(value: str) -> bool:
    candidate = value.strip()
    if not candidate or candidate.startswith("//"):
        # Blender's // prefix is relative to the owning .blend, not a UNC path.
        return False
    return bool(
        WINDOWS_DRIVE_PATH.match(candidate)
        or candidate.startswith(("/", "\\"))
        or candidate.casefold().startswith("file://")
    )


def assert_manifest_has_no_local_absolute_paths(document: object) -> None:
    """Fail closed if any recursively nested report string is machine-local."""
    workspace_variants = {
        str(ROOT.resolve()).casefold(),
        ROOT.resolve().as_posix().casefold(),
    }
    violations: list[str] = []

    def visit(value: object, location: str) -> None:
        if isinstance(value, dict):
            for key, child in value.items():
                visit(child, f"{location}.{key}")
        elif isinstance(value, list):
            for index, child in enumerate(value):
                visit(child, f"{location}[{index}]")
        elif isinstance(value, str):
            folded = value.casefold()
            if _is_absolute_or_drive_qualified_local_path(value) or any(
                workspace in folded for workspace in workspace_variants
            ):
                violations.append(location)

    visit(document, "$")
    if violations:
        raise RuntimeError(
            "Candidate007 report contains absolute or drive-qualified local "
            "paths: " + ", ".join(violations)
        )


def _external_datablock_paths():
    for collection_name in EXTERNAL_DATABLOCK_COLLECTIONS:
        for datablock in sorted(
            getattr(bpy.data, collection_name), key=lambda item: item.name
        ):
            filepath = str(getattr(datablock, "filepath", ""))
            if not filepath or (
                filepath.startswith("<") and filepath.endswith(">")
            ):
                continue
            yield collection_name, datablock, filepath


def _absolute_blender_datablock_path(datablock, filepath: str) -> Path:
    library = getattr(datablock, "library", None)
    return Path(bpy.path.abspath(filepath, library=library)).resolve()


def _blender_relative_to_output(path: Path) -> str:
    relative = bpy.path.relpath(
        str(path.resolve()), start=str(OUTPUT_BLEND.resolve().parent)
    ).replace("\\", "/")
    if not relative.startswith("//"):
        raise RuntimeError(
            f"Blender did not produce an output-relative path for {path}: {relative}"
        )
    return relative


def normalize_external_blender_paths_for_output() -> int:
    """Rewrite saved external paths only after all absolute-path renders finish."""
    normalized = 0
    for _collection_name, datablock, filepath in _external_datablock_paths():
        absolute = _absolute_blender_datablock_path(datablock, filepath)
        repository_relative_posix(absolute)
        datablock.filepath = _blender_relative_to_output(absolute)
        normalized += 1
    for scene in bpy.data.scenes:
        filepath = str(scene.render.filepath)
        if not filepath:
            continue
        absolute = Path(bpy.path.abspath(filepath)).resolve()
        repository_relative_posix(absolute)
        scene.render.filepath = _blender_relative_to_output(absolute)
        normalized += 1
    return normalized


def assert_external_blender_paths_portable(
    normalized_path_count: int,
) -> dict[str, object]:
    """Re-enumerate saved paths and reject anything not .blend-relative."""
    output_directory = OUTPUT_BLEND.resolve().parent
    records: list[dict[str, str]] = []
    for collection_name, datablock, filepath in _external_datablock_paths():
        if (
            _is_absolute_or_drive_qualified_local_path(filepath)
            or not filepath.startswith("//")
        ):
            raise RuntimeError(
                f"Candidate007 {collection_name} path is not portable: "
                f"{datablock.name}={filepath}"
            )
        absolute = (output_directory / filepath[2:]).resolve()
        records.append(
            {
                "kind": collection_name,
                "name": str(datablock.name),
                "stored_path": filepath,
                "repository_target": repository_relative_posix(absolute),
            }
        )

    render_records: list[dict[str, str]] = []
    for scene in bpy.data.scenes:
        filepath = str(scene.render.filepath)
        if not filepath:
            continue
        if (
            _is_absolute_or_drive_qualified_local_path(filepath)
            or not filepath.startswith("//")
        ):
            raise RuntimeError(
                f"Candidate007 scene render path is not portable: "
                f"{scene.name}={filepath}"
            )
        absolute = (output_directory / filepath[2:]).resolve()
        render_records.append(
            {
                "scene": str(scene.name),
                "stored_path": filepath,
                "repository_target": repository_relative_posix(absolute),
            }
        )

    enumerated_count = len(records) + len(render_records)
    if enumerated_count != normalized_path_count:
        raise RuntimeError(
            "Candidate007 external path enumeration changed during "
            f"normalization: normalized={normalized_path_count}, "
            f"enumerated={enumerated_count}"
        )
    if not render_records:
        raise RuntimeError("Candidate007 has no portable saved scene render path")
    return {
        "schema_version": 1,
        "policy": "blend_relative_repository_contained_fail_closed_v1",
        "output_blend": repository_relative_posix(OUTPUT_BLEND),
        "normalized_path_count": normalized_path_count,
        "external_datablock_paths": records,
        "scene_render_paths": render_records,
        "absolute_or_drive_qualified_path_count": 0,
        "all_targets_repository_contained": True,
        "manifest_assertion": {
            "scope": "all_recursively_nested_strings",
            "absolute_or_drive_qualified_path_count": 0,
        },
    }


def load_module(name: str, path: Path):
    specification = importlib.util.spec_from_file_location(name, path)
    if specification is None or specification.loader is None:
        raise RuntimeError(f"Could not load pipeline module {path}")
    module = importlib.util.module_from_spec(specification)
    sys.modules[name] = module
    specification.loader.exec_module(module)
    return module


def ensure_collection(name: str, parent: bpy.types.Collection | None = None) -> bpy.types.Collection:
    existing = bpy.data.collections.get(name)
    if existing is not None:
        for obj in list(existing.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(existing)
    collection = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(collection)
    return collection


def remove_existing_rifle() -> None:
    for obj in list(bpy.data.objects):
        if obj.name == "RifleRoot" or obj.name.startswith("Rifle_") or obj.name.startswith("NGPR002_"):
            data = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if data is not None and getattr(data, "users", 1) == 0 and isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
    for collection in list(bpy.data.collections):
        if collection.name.startswith("WeaponV3_LOD") or collection.name in {RIFLE_COMPONENTS, OPTIC_COMPONENTS}:
            bpy.data.collections.remove(collection)


MATERIAL_TINT_ATTRIBUTE = "ngpr_material_tint"

# The shared preview map is intentionally neutral.  A per-corner tint keeps
# source-material identity after the production renderer compacts six authoring
# materials into four draw-call slots.  These values are linear and deliberately
# low: hard edges should read from roughness and silhouette, not silver-blue
# albedo or mirror-bright reflections.
MATERIAL_TINTS = {
    "NGPR_CarbonComposite": (0.040, 0.047, 0.058, 1.0),
    "NGPR_SootBlackArmor": (0.026, 0.027, 0.030, 1.0),
    "NGPR_OilyGunmetal": (0.092, 0.088, 0.080, 1.0),
    "NGPR_TarnishedChrome": (0.260, 0.225, 0.180, 1.0),
    "NGPR_Rubber": (0.015, 0.016, 0.018, 1.0),
    "NGPR_CyanStatus": (0.018, 0.300, 0.470, 1.0),
    "NGPR_OpticGlass": (0.025, 0.070, 0.170, 1.0),
}


def material(name: str, base: tuple[float, float, float, float], metallic: float, roughness: float,
             *, emission: tuple[float, float, float, float] | None = None, glass: bool = False) -> bpy.types.Material:
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Base Color"].default_value = base
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    if emission is not None:
        emission_input = shader.inputs.get("Emission Color") or shader.inputs.get("Emission")
        if emission_input:
            emission_input.default_value = emission
        if shader.inputs.get("Emission Strength"):
            shader.inputs["Emission Strength"].default_value = 3.5
    if glass:
        shader.inputs["Metallic"].default_value = 0.25
        shader.inputs["Roughness"].default_value = 0.08
        if shader.inputs.get("Transmission Weight"):
            shader.inputs["Transmission Weight"].default_value = 0.38
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    mat["ps_pbr_base_color"] = True
    mat["ps_pbr_normal"] = True
    mat["ps_pbr_mrao"] = True
    mat["ps_pbr_emission"] = True
    mat["ngpr_material_tint"] = list(MATERIAL_TINTS.get(name, base))
    return mat


def image(name: str, *, non_color: bool = False) -> bpy.types.Image:
    path = TEXTURE_ROOT / name
    if not path.is_file():
        raise RuntimeError(f"Candidate007 texture is missing: {path}")
    result = bpy.data.images.load(str(path), check_existing=True)
    if non_color:
        result.colorspace_settings.name = "Non-Color"
    return result


def wire_rifle_pbr(mat: bpy.types.Material) -> None:
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    shader = next((node for node in nodes if node.type == "BSDF_PRINCIPLED"), None)
    if shader is None:
        raise RuntimeError(f"{mat.name} has no Principled shader")
    base_default = tuple(float(value) for value in shader.inputs["Base Color"].default_value)
    metallic_default = float(shader.inputs["Metallic"].default_value)
    roughness_default = float(shader.inputs["Roughness"].default_value)
    coordinates = nodes.new("ShaderNodeTexCoord")
    base = nodes.new("ShaderNodeTexImage")
    base.image = image("NGPR001_BaseColor.png")
    normal_image = nodes.new("ShaderNodeTexImage")
    normal_image.image = image("NGPR001_Normal.png", non_color=True)
    mrao = nodes.new("ShaderNodeTexImage")
    mrao.image = image("NGPR001_MRAO.png", non_color=True)
    emissive = nodes.new("ShaderNodeTexImage")
    emissive.image = image("NGPR001_Emission.png", non_color=True)
    separate = nodes.new("ShaderNodeSeparateColor")
    normal = nodes.new("ShaderNodeNormalMap")
    normal.inputs["Strength"].default_value = 0.22
    tint_attribute = nodes.new("ShaderNodeVertexColor")
    tint_attribute.layer_name = MATERIAL_TINT_ATTRIBUTE
    tint = nodes.new("ShaderNodeMixRGB")
    tint.blend_type = "MULTIPLY"
    tint.inputs[0].default_value = 1.0
    tint.inputs[2].default_value = MATERIAL_TINTS.get(mat.name, base_default)
    metallic_scale = nodes.new("ShaderNodeMath")
    metallic_scale.operation = "MULTIPLY"
    metallic_scale.inputs[1].default_value = metallic_default
    invert_smoothness = nodes.new("ShaderNodeMath")
    invert_smoothness.operation = "SUBTRACT"
    invert_smoothness.inputs[0].default_value = 1.0
    roughness_scale = nodes.new("ShaderNodeMath")
    roughness_scale.operation = "MULTIPLY"
    roughness_scale.inputs[1].default_value = max(0.5, roughness_default * 1.8)
    for texture in (base, normal_image, mrao, emissive):
        links.new(coordinates.outputs["UV"], texture.inputs["Vector"])
    links.new(base.outputs["Color"], tint.inputs[1])
    links.new(tint_attribute.outputs["Color"], tint.inputs[2])
    links.new(tint.outputs["Color"], shader.inputs["Base Color"])
    links.new(normal_image.outputs["Color"], normal.inputs["Color"])
    links.new(normal.outputs["Normal"], shader.inputs["Normal"])
    links.new(mrao.outputs["Color"], separate.inputs["Color"])
    links.new(separate.outputs["Red"], metallic_scale.inputs[0])
    links.new(metallic_scale.outputs["Value"], shader.inputs["Metallic"])
    links.new(mrao.outputs["Alpha"], invert_smoothness.inputs[1])
    links.new(invert_smoothness.outputs["Value"], roughness_scale.inputs[0])
    links.new(roughness_scale.outputs["Value"], shader.inputs["Roughness"])
    emission_input = shader.inputs.get("Emission Color") or shader.inputs.get("Emission")
    if emission_input:
        links.new(emissive.outputs["Color"], emission_input)
    if shader.inputs.get("Emission Strength"):
        shader.inputs["Emission Strength"].default_value = (
            3.0 if mat.name == "NGPR_CyanStatus" else 0.01
        )


def materials() -> dict[str, bpy.types.Material]:
    result = {
        "carbon": material("NGPR_CarbonComposite", (0.008, 0.012, 0.018, 1.0), 0.06, 0.58),
        "armor": material("NGPR_SootBlackArmor", (0.012, 0.016, 0.022, 1.0), 0.22, 0.46),
        "gunmetal": material("NGPR_OilyGunmetal", (0.025, 0.033, 0.044, 1.0), 0.72, 0.38),
        "chrome": material("NGPR_TarnishedChrome", (0.18, 0.22, 0.28, 1.0), 0.82, 0.34),
        "rubber": material("NGPR_Rubber", (0.006, 0.007, 0.009, 1.0), 0.00, 0.84),
        "cyan": material("NGPR_CyanStatus", (0.002, 0.12, 0.18, 1.0), 0.18, 0.35, emission=(0.0, 0.7, 1.0, 1.0)),
        "glass": material("NGPR_OpticGlass", (0.008, 0.025, 0.080, 1.0), 0.12, 0.14, glass=True),
        "studio": material("NGPR_Studio", (0.025, 0.030, 0.038, 1.0), 0.0, 0.88),
    }
    for key in ("carbon", "armor", "gunmetal", "chrome", "rubber", "cyan", "glass"):
        wire_rifle_pbr(result[key])
    return result


def link_mesh(name: str, vertices, faces, collection: bpy.types.Collection, mat: bpy.types.Material,
              location=(0.0, 0.0, 0.0), rotation=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(mat)
    tint_layer = mesh.color_attributes.new(
        name=MATERIAL_TINT_ATTRIBUTE, type="FLOAT_COLOR", domain="CORNER"
    )
    tint_value = tuple(float(value) for value in mat.get("ngpr_material_tint", mat.diffuse_color))
    for datum in tint_layer.data:
        datum.color = tint_value
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = rotation
    for polygon in mesh.polygons:
        polygon.use_smooth = False
    return obj


def box(name: str, center, size, collection, mat, *, bevel=0.004, rotation=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    x, y, z = (value * 0.5 for value in size)
    vertices = [(-x, -y, -z), (x, -y, -z), (x, y, -z), (-x, y, -z),
                (-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5),
             (2, 3, 7, 6), (3, 0, 4, 7)]
    obj = link_mesh(name, vertices, faces, collection, mat, center, rotation)
    if bevel > 0.0:
        modifier = obj.modifiers.new("ProductionEdge", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        modifier.limit_method = "ANGLE"
    return obj


def tapered_box(name: str, center, size, collection, mat, *, front=(0.82, 0.84), bevel=0.004,
                rotation=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    x, y, z = (value * 0.5 for value in size)
    fx, fz = front
    vertices = [(-x, -y, -z), (x, -y, -z), (x, -y, z), (-x, -y, z),
                (-x * fx, y, -z * fz), (x * fx, y, -z * fz),
                (x * fx, y, z * fz), (-x * fx, y, z * fz)]
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2),
             (2, 6, 7, 3), (3, 7, 4, 0)]
    obj = link_mesh(name, vertices, faces, collection, mat, center, rotation)
    if bevel > 0.0:
        modifier = obj.modifiers.new("ProductionEdge", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        modifier.limit_method = "ANGLE"
    return obj


def cylinder(name: str, center, radius, length, collection, mat, *, axis="Y", vertices=24,
             bevel=0.002) -> bpy.types.Object:
    verts: list[tuple[float, float, float]] = []
    for along in (-length * 0.5, length * 0.5):
        for index in range(vertices):
            angle = math.tau * index / vertices
            ring = (math.cos(angle) * radius, along, math.sin(angle) * radius)
            verts.append(ring if axis == "Y" else ((along, ring[0], ring[2]) if axis == "X" else (ring[0], ring[2], along)))
    faces = []
    for index in range(vertices):
        nxt = (index + 1) % vertices
        faces.append((index, nxt, vertices + nxt, vertices + index))
    faces.append(tuple(reversed(range(vertices))))
    faces.append(tuple(range(vertices, vertices * 2)))
    obj = link_mesh(name, verts, faces, collection, mat, center)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    if bevel > 0.0:
        modifier = obj.modifiers.new("ProductionEdge", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        modifier.limit_method = "ANGLE"
    return obj


def hollow_cylinder(name: str, center, outer_radius: float, inner_radius: float,
                    length: float, collection, mat, *, vertices=32,
                    bevel=0.0015) -> bpy.types.Object:
    if not 0.0 < inner_radius < outer_radius:
        raise ValueError("Hollow cylinder requires 0 < inner < outer radius")
    verts = []
    for y in (-length * 0.5, length * 0.5):
        for radius in (outer_radius, inner_radius):
            for index in range(vertices):
                angle = math.tau * index / vertices
                verts.append((math.cos(angle) * radius, y, math.sin(angle) * radius))
    outer_back, inner_back, outer_front, inner_front = (0, vertices, vertices * 2, vertices * 3)
    faces = []
    for index in range(vertices):
        nxt = (index + 1) % vertices
        faces.extend((
            (outer_back + index, outer_back + nxt, outer_front + nxt, outer_front + index),
            (inner_back + nxt, inner_back + index, inner_front + index, inner_front + nxt),
            (outer_back + nxt, outer_back + index, inner_back + index, inner_back + nxt),
            (outer_front + index, outer_front + nxt, inner_front + nxt, inner_front + index),
        ))
    obj = link_mesh(name, verts, faces, collection, mat, center)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    if bevel > 0.0:
        modifier = obj.modifiers.new("ProductionEdge", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        modifier.limit_method = "ANGLE"
    return obj


def tube_between(name: str, start: Vector, end: Vector, radius: float, collection, mat,
                 *, vertices=16, bevel=0.0015) -> bpy.types.Object:
    midpoint = start.lerp(end, 0.5)
    obj = cylinder(name, midpoint, radius, (end - start).length, collection, mat,
                   vertices=vertices, bevel=bevel)
    obj.rotation_euler = (end - start).to_track_quat("Y", "Z").to_euler()
    return obj


def tube_along_path(name: str, points: list[Vector], radius: float, collection, mat,
                    *, vertices: int = 16) -> bpy.types.Object:
    """Create one watertight tube with shared rings along an authored path.

    Segment-by-segment capped cylinders place coincident vertices at every
    joint.  Besides failing the production topology gate, those hidden caps can
    catch highlights and make a cable look beaded.  This builder emits each
    path ring exactly once and closes only the two physical cable ends.
    """

    if len(points) < 2:
        raise ValueError("A tube path requires at least two points")
    if vertices < 3:
        raise ValueError("A tube path requires at least three ring vertices")

    tangents: list[Vector] = []
    for index, point in enumerate(points):
        if index == 0:
            tangent = points[1] - point
        elif index == len(points) - 1:
            tangent = point - points[index - 1]
        else:
            tangent = points[index + 1] - points[index - 1]
        if tangent.length <= 1.0e-9:
            raise ValueError(f"Tube path {name} has a repeated point at index {index}")
        tangents.append(tangent.normalized())

    mesh_vertices: list[tuple[float, float, float]] = []
    for point, tangent in zip(points, tangents):
        reference = Vector((1.0, 0.0, 0.0))
        if abs(tangent.dot(reference)) > 0.95:
            reference = Vector((0.0, 0.0, 1.0))
        radial_a = (reference - tangent * tangent.dot(reference)).normalized()
        radial_b = tangent.cross(radial_a).normalized()
        for ring_index in range(vertices):
            angle = math.tau * ring_index / vertices
            position = point + radial_a * (math.cos(angle) * radius)
            position += radial_b * (math.sin(angle) * radius)
            mesh_vertices.append(tuple(position))

    mesh_faces: list[tuple[int, ...]] = []
    for ring_index in range(len(points) - 1):
        start = ring_index * vertices
        end = start + vertices
        for side_index in range(vertices):
            nxt = (side_index + 1) % vertices
            mesh_faces.append((
                start + side_index,
                start + nxt,
                end + nxt,
                end + side_index,
            ))
    mesh_faces.append(tuple(reversed(range(vertices))))
    last_ring = (len(points) - 1) * vertices
    mesh_faces.append(tuple(last_ring + index for index in range(vertices)))

    obj = link_mesh(name, mesh_vertices, mesh_faces, collection, mat)
    for polygon in obj.data.polygons[:-2]:
        polygon.use_smooth = True
    return obj


def beam_between(name: str, start: Vector, end: Vector, width: float, depth: float,
                 collection, mat, *, bevel=0.0015) -> bpy.types.Object:
    """Create a closed rectangular structural rib aligned between two points."""

    midpoint = start.lerp(end, 0.5)
    obj = box(name, midpoint, (width, (end - start).length, depth), collection, mat, bevel=bevel)
    obj.rotation_euler = (end - start).to_track_quat("Y", "Z").to_euler()
    return obj


def create_helper(name: str, role: str, location: tuple[float, float, float], collection,
                  *, rotation=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, None)
    collection.objects.link(obj)
    obj.empty_display_type = "ARROWS"
    obj.empty_display_size = 0.045
    obj.location = location
    obj.rotation_euler = rotation
    tag_helper(obj, role)
    if role in {ROLE_PRIMARY_GRIP, ROLE_SUPPORT_GRIP, ROLE_SUPPORT_MIN, ROLE_SUPPORT_MAX}:
        obj["ps_weapon_target_semantic"] = "wrist_head"
        obj["ps_weapon_contact_offset_local"] = [0.0, 0.0, 0.0]
    return obj


def reshape_primary_grip_component(obj: bpy.types.Object) -> bpy.types.Object:
    """Bake the measured Candidate007 grip envelope in rifle-root axes."""
    anchor = Vector(PRIMARY_GRIP_RESHAPE_ANCHOR_M)
    scale = Matrix.Diagonal((*PRIMARY_GRIP_RESHAPE_SCALE_XYZ, 1.0))
    transform = (
        Matrix.Translation(Vector(PRIMARY_GRIP_RESHAPE_SHIFT_M))
        @ Matrix.Translation(anchor)
        @ scale
        @ Matrix.Translation(-anchor)
    )
    object_to_root = obj.matrix_world.copy()
    obj.data.transform(object_to_root.inverted_safe() @ transform @ object_to_root)
    obj.data.update()
    return obj


def apply_primary_grip_hand_relief(
    obj: bpy.types.Object,
    *,
    cap_z_m: float | None = None,
    backstrap_shift_x_m: float = 0.0,
) -> bpy.types.Object:
    """Apply measured local relief without moving the grip helper or action.

    The Candidate007 source-part Aim probe separated the physical firing-hand
    wrap from two genuine LowerArm.R strikes.  Capping the pistol crown at
    z=-0.040 m and shifting only the thin backstrap +0.020 m in rifle-local X
    clears those cuff contacts while preserving positive Hand.R/grip overlap.
    """

    if cap_z_m is not None:
        # These fresh, unparented source objects have an authoritative basis
        # before the view layer has evaluated their matrix_world.  Using the
        # stale world matrix here collapses the rotated grip into a plane.
        object_to_root = obj.matrix_basis.copy()
        root_to_object = object_to_root.inverted_safe()
        for vertex in obj.data.vertices:
            root_position = object_to_root @ vertex.co
            if root_position.z > cap_z_m:
                root_position.z = cap_z_m
                vertex.co = root_to_object @ root_position
    if backstrap_shift_x_m:
        for vertex in obj.data.vertices:
            vertex.co.x += backstrap_shift_x_m
    obj.data.update()
    return obj


def apply_stock_contact_perimeter_relief(
    obj: bpy.types.Object,
    *,
    relief_m: float = STOCK_CONTACT_PERIMETER_RELIEF_M,
) -> bpy.types.Object:
    """Inset only the undersuit-facing inboard/lower/rear pad corner.

    Candidate007's residual stock contact is at the local +X/-Y/-Z perimeter:
    +X is inboard on the authored -X shoulder dogleg, -Y is the rear face, and
    -Z is the lower edge.  Moving that sole corner diagonally inward by 3.5 mm
    preserves the existing watertight topology and central rear face while
    retaining the object transform, stock placement, and independently
    authored ``Rifle_StockContact`` helper.
    """

    if relief_m <= 0.0:
        raise ValueError("Stock-contact perimeter relief must be positive")
    if not obj.data.vertices:
        raise RuntimeError(f"{obj.name} has no vertices for stock-contact relief")

    maximum_x = max(vertex.co.x for vertex in obj.data.vertices)
    minimum_y = min(vertex.co.y for vertex in obj.data.vertices)
    minimum_z = min(vertex.co.z for vertex in obj.data.vertices)
    tolerance_m = 1.0e-7
    target_indices = [
        vertex.index
        for vertex in obj.data.vertices
        if abs(vertex.co.x - maximum_x) <= tolerance_m
        and abs(vertex.co.y - minimum_y) <= tolerance_m
        and abs(vertex.co.z - minimum_z) <= tolerance_m
    ]
    if len(target_indices) != 1:
        raise RuntimeError(
            f"{obj.name} stock-contact relief expected one local +X/-Y/-Z "
            f"corner, found {len(target_indices)}"
        )

    transform_before = obj.matrix_world.copy()
    target = obj.data.vertices[target_indices[0]]
    before = target.co.copy()
    target.co += Vector((-relief_m, relief_m, relief_m))
    actual_displacement = float((target.co - before).length)
    expected_displacement = relief_m * math.sqrt(3.0)
    if abs(actual_displacement - expected_displacement) > 1.0e-6:
        raise RuntimeError(f"{obj.name} stock-contact relief displacement drifted")
    obj.data.update()
    if obj.matrix_world != transform_before:
        raise RuntimeError(f"{obj.name} stock-contact relief moved the stock")
    obj["ngpr_stock_contact_relief_m"] = float(relief_m)
    obj["ngpr_stock_contact_relief_mode"] = "topology_preserving_corner_inset"
    obj["ngpr_stock_contact_relief_corner"] = "local_positive_x_negative_y_negative_z"
    return obj


def build_components(root: bpy.types.Object, components: bpy.types.Collection,
                     optic_components: bpy.types.Collection, mat) -> tuple[list[bpy.types.Object], list[bpy.types.Object]]:
    parts: list[bpy.types.Object] = []
    optics: list[bpy.types.Object] = []

    def add(obj, zone=face_policy.WEAPON_ORDINARY, role=""):
        obj["ngpr_semantic_zone"] = int(zone)
        obj["ngpr_component_role"] = role
        parts.append(obj)
        return obj

    # Recessed chassis and separated armor skins. The shadow gaps are geometry,
    # not texture rectangles, so the receiver reads as an adult mechanism at
    # thumbnail scale without changing the hardpoint envelope.
    add(tapered_box("NGPR_Receiver_Core", (0.0, 0.075, 0.120), (0.104, 0.340, 0.105), components, mat["carbon"], front=(0.88, 0.82), bevel=0.006))
    for side in (-1.0, 1.0):
        suffix = "R" if side < 0 else "L"
        for panel, y, length, z, height in (
            ("Aft", -0.045, 0.095, 0.138, 0.108),
            ("Action", 0.070, 0.112, 0.145, 0.120 if side < 0 else 0.104),
            ("Trunnion", 0.205, 0.108, 0.150, 0.098),
        ):
            add(tapered_box(
                f"NGPR_ReceiverSkin_{suffix}_{panel}",
                (side * 0.058, y, z), (0.010, length, height), components,
                mat["armor"], front=(0.90, 0.90), bevel=0.0025,
            ))
    add(tapered_box("NGPR_Receiver_TopSpine", (0.0, 0.095, 0.191), (0.076, 0.332, 0.030), components, mat["armor"], front=(0.78, 0.78), bevel=0.004))
    # Raise the lower keel 10 mm above the primary-hand corridor.  The exact
    # Candidate007 Aim-frame source-part probe showed the old lower face
    # crossing Hand.R (53 triangle pairs); this measured relief clears it
    # without moving the bore, hardpoints, magazine, or receiver side skins.
    add(tapered_box("NGPR_Lower_Keel", (0.0, 0.070, 0.061), (0.086, 0.275, 0.044), components, mat["gunmetal"], front=(0.84, 0.72), bevel=0.004))
    add(box("NGPR_TopRail", (0.0, 0.290, 0.221), (0.070, 0.720, 0.018), components, mat["gunmetal"], bevel=0.002))
    add(tapered_box("NGPR_Handguard_Core", (0.0, 0.465, 0.130), (0.082, 0.440, 0.060), components, mat["carbon"], front=(0.82, 0.82), bevel=0.004))
    add(tapered_box("NGPR_Handguard_TopSpine", (0.0, 0.470, 0.185), (0.060, 0.455, 0.018), components, mat["armor"], front=(0.82, 0.78), bevel=0.0025))
    add(tapered_box("NGPR_Handguard_LowerKeel", (0.0, 0.470, 0.085), (0.052, 0.430, 0.018), components, mat["gunmetal"], front=(0.78, 0.72), bevel=0.0025))
    for side in (-1.0, 1.0):
        suffix = "R" if side < 0 else "L"
        add(tapered_box(f"NGPR_Handguard_SideRail_{suffix}", (side * 0.052, 0.470, 0.130), (0.014, 0.438, 0.020), components, mat["armor"], front=(0.78, 0.78), bevel=0.002))
        for index, y in enumerate((0.315, 0.425, 0.535, 0.645)):
            add(box(f"NGPR_VentBay_{suffix}_{index}", (side * 0.058, y, 0.130), (0.008, 0.068, 0.046), components, mat["rubber"], bevel=0.001))
            direction = -1.0 if index % 2 else 1.0
            add(beam_between(
                f"NGPR_HandguardRib_{suffix}_{index}",
                Vector((side * 0.061, y - 0.040, 0.126 - direction * 0.036)),
                Vector((side * 0.061, y + 0.040, 0.126 + direction * 0.036)),
                0.011, 0.010, components, mat["gunmetal"], bevel=0.0015,
            ))
        cable_points = [
            Vector((side * 0.068, y, 0.102 + math.sin(index * 1.1) * 0.006))
            for index, y in enumerate((0.075, 0.170, 0.270, 0.375, 0.485, 0.595, 0.665))
        ]
        add(tube_along_path(
            f"NGPR_BraidedCable_{suffix}", cable_points, 0.0045,
            components, mat["carbon"], vertices=16,
        ))
        for index, y in enumerate((0.10, 0.25, 0.42, 0.57)):
            add(box(f"NGPR_CableClamp_{suffix}_{index}", (side * 0.069, y, 0.103), (0.007, 0.020, 0.020), components, mat["gunmetal"], bevel=0.001))

    # Functional asymmetry on the action side; the opposite side remains a
    # sparse removable service skin instead of mirrored decorative greeble.
    add(box("NGPR_EjectionPort_R", (-0.066, 0.085, 0.148), (0.008, 0.105, 0.044), components, mat["rubber"], bevel=0.001))
    for name, center, size in (
        ("Top", (-0.071, 0.085, 0.174), (0.010, 0.118, 0.008)),
        ("Bottom", (-0.071, 0.085, 0.122), (0.010, 0.118, 0.008)),
        ("Aft", (-0.071, 0.026, 0.148), (0.010, 0.010, 0.058)),
        ("Fore", (-0.071, 0.144, 0.148), (0.010, 0.010, 0.058)),
    ):
        add(box(f"NGPR_EjectionFrame_{name}", center, size, components, mat["gunmetal"], bevel=0.001))
    add(box("NGPR_BoltTrackCover_R", (-0.069, -0.015, 0.187), (0.010, 0.125, 0.018), components, mat["gunmetal"], bevel=0.002))
    add(cylinder("NGPR_SelectorDial_R", (-0.071, -0.055, 0.118), 0.015, 0.012, components, mat["chrome"], axis="X", vertices=32, bevel=0.0015))
    add(box("NGPR_MagRelease_R", (-0.070, 0.065, 0.067), (0.012, 0.030, 0.020), components, mat["chrome"], bevel=0.002))
    add(tapered_box("NGPR_ServicePanel_L", (0.062, 0.055, 0.145), (0.008, 0.150, 0.080), components, mat["armor"], front=(0.92, 0.92), bevel=0.002))
    add(box("NGPR_CableJunction_L", (0.068, 0.145, 0.112), (0.012, 0.038, 0.030), components, mat["gunmetal"], bevel=0.002))

    # Fixed protected side-yoke: intentionally not a literal folding mechanism.
    yoke_side = 1.0
    support_center = Vector((0.108, 0.300, -0.045))
    add(box("NGPR_SupportYoke_Mount", (0.070, 0.360, 0.072), (0.060, 0.070, 0.030), components, mat["chrome"], bevel=0.004), face_policy.WEAPON_SUPPORT_GRIP, "support_grip")
    add(tube_between("NGPR_SupportYoke_Upper", Vector((0.084, 0.345, 0.068)), Vector((0.112, 0.315, -0.005)), 0.012, components, mat["gunmetal"], vertices=16), face_policy.WEAPON_SUPPORT_GRIP, "support_grip")
    add(tube_between("NGPR_SupportYoke_Lower", Vector((0.112, 0.315, -0.005)), Vector((0.125, 0.285, -0.072)), 0.013, components, mat["gunmetal"], vertices=16), face_policy.WEAPON_SUPPORT_GRIP, "support_grip")
    add(tapered_box("NGPR_SupportGrip", support_center, (0.052, 0.092, 0.116), components, mat["rubber"], front=(0.82, 0.88), bevel=0.006, rotation=(math.radians(-16), 0.0, math.radians(-7))), face_policy.WEAPON_SUPPORT_GRIP, "support_grip")
    add(cylinder("NGPR_SupportYoke_HingeCap", (0.080, 0.360, 0.072), 0.018, 0.020, components, mat["gunmetal"], axis="X", vertices=28, bevel=0.0015), face_policy.WEAPON_SUPPORT_GRIP, "support_grip")
    add(tapered_box("NGPR_SupportYoke_Guard", (0.123, 0.302, -0.020), (0.010, 0.100, 0.096), components, mat["armor"], front=(0.82, 0.78), bevel=0.002), face_policy.WEAPON_SUPPORT_GRIP, "support_grip")

    # Barrel, heat collar and compact brake remain centered on the canonical bore.
    add(cylinder("NGPR_BarrelCollar", (0.0, 0.710, 0.145), 0.041, 0.120, components, mat["gunmetal"], vertices=28))
    add(hollow_cylinder("NGPR_ThermalSleeve", (0.0, 0.778, 0.145), 0.030, 0.025, 0.125, components, mat["armor"], vertices=32))
    add(cylinder("NGPR_Barrel", (0.0, 0.925, 0.145), 0.021, 0.360, components, mat["gunmetal"], vertices=32))
    add(cylinder("NGPR_BarrelStep", (0.0, 1.040, 0.145), 0.027, 0.052, components, mat["chrome"], vertices=32))
    add(hollow_cylinder("NGPR_MuzzleBrake_Aft", (0.0, 1.093, 0.145), 0.034, 0.017, 0.070, components, mat["armor"], vertices=32))
    add(hollow_cylinder("NGPR_MuzzleBrake_Front", (0.0, 1.145, 0.145), 0.030, 0.014, 0.045, components, mat["gunmetal"], vertices=32))
    for side in (-1.0, 1.0):
        add(box(f"NGPR_MuzzlePort_{'R' if side < 0 else 'L'}", (side * 0.034, 1.112, 0.145), (0.012, 0.052, 0.034), components, mat["rubber"], bevel=0.002))
    add(cylinder("NGPR_MuzzleBore", (0.0, 1.168, 0.145), 0.012, 0.008, components, mat["rubber"], vertices=24, bevel=0.0))

    # Primary grip and magazine are deliberately narrower than Candidate005.
    add(apply_primary_grip_hand_relief(reshape_primary_grip_component(tapered_box("NGPR_PistolGrip", (-0.070, -0.050, -0.063), (0.050, 0.082, 0.158), components, mat["rubber"], front=(0.78, 0.86), bevel=0.006, rotation=(math.radians(-22), 0.0, 0.0))), cap_z_m=-0.040), face_policy.WEAPON_PRIMARY_GRIP, "primary_grip")
    # The trigger guard is part of the authored firing-hand contact envelope,
    # not ordinary receiver structure.  Preserve that provenance explicitly so
    # the face-policy gate can distinguish an intended guard wrap from a hand
    # intersecting the receiver keel.
    add(box("NGPR_TriggerGuard", (-0.035, 0.055, -0.002), (0.064, 0.096, 0.022), components, mat["gunmetal"], bevel=0.004), face_policy.WEAPON_PRIMARY_GRIP, "primary_grip")
    add(apply_primary_grip_hand_relief(reshape_primary_grip_component(tapered_box("NGPR_GripBackstrap", (-0.070, -0.085, -0.060), (0.056, 0.020, 0.135), components, mat["carbon"], front=(0.86, 0.92), bevel=0.003, rotation=(math.radians(-22), 0.0, 0.0))), backstrap_shift_x_m=0.020), face_policy.WEAPON_PRIMARY_GRIP, "primary_grip")
    for index, z in enumerate((-0.020, -0.060, -0.100)):
        add(reshape_primary_grip_component(box(f"NGPR_GripRib_{index}", (-0.070, -0.012, z), (0.054, 0.012, 0.010), components, mat["carbon"], bevel=0.002)), face_policy.WEAPON_PRIMARY_GRIP, "primary_grip")
    # Keep the magazine envelope frozen until the updated pad/path solver
    # remeasures the current frame-50 undersuit strike.  That evidence must
    # distinguish a path problem from geometry before any magazine reshape.
    magazine = add(tapered_box("NGPR_Magazine", (0.0, 0.135, -0.082), (0.058, 0.074, 0.174), components, mat["carbon"], front=(0.76, 0.86), bevel=0.005, rotation=(math.radians(-7), 0.0, 0.0)), face_policy.WEAPON_MAGAZINE_GRASP, COMPONENT_MAGAZINE)
    magazine_base = add(box("NGPR_MagazineBase", (0.0, 0.146, -0.174), (0.060, 0.074, 0.020), components, mat["chrome"], bevel=0.003), face_policy.WEAPON_MAGAZINE_GRASP, COMPONENT_MAGAZINE)
    # The receiver-side collar is fixed structure. Only the detachable body,
    # base and ribs follow WeaponMagazine during reload.
    magazine_collar = add(box("NGPR_MagwellCollar", (0.003, 0.125, 0.010), (0.080, 0.092, 0.022), components, mat["gunmetal"], bevel=0.003), face_policy.WEAPON_ORDINARY, "magwell_fixed")
    magazine_ribs = []
    for side in (-1.0, 1.0):
        magazine_ribs.append(add(box(f"NGPR_MagazineRib_{'R' if side < 0 else 'L'}", (side * 0.026, 0.138, -0.085), (0.008, 0.078, 0.128), components, mat["armor"], bevel=0.002), face_policy.WEAPON_MAGAZINE_GRASP, COMPONENT_MAGAZINE))
    # A compact outboard pull lug gives the rigid, non-fingered glove a real
    # manipulation feature.  Targeting the magazine body itself forced the
    # adjacent palm semantic through the box even when the fingertip cap was
    # correctly aligned.  The lug moves with WeaponMagazine and remains inside
    # the existing receiver-side silhouette when seated.
    magazine_pull_lug = add(
        cylinder(
            "NGPR_MagazinePullLug_L",
            (0.054, 0.138, -0.105),
            0.008,
            0.050,
            components,
            mat["rubber"],
            axis="X",
            vertices=20,
            bevel=0.001,
        ),
        face_policy.WEAPON_MAGAZINE_GRASP,
        COMPONENT_MAGAZINE,
    )

    # Slim fixed skeletal stock with an authored -X shoulder dogleg.
    add(tube_between("NGPR_StockSpine", Vector((-0.018, -0.080, 0.145)), Vector((-0.080, -0.360, 0.160)), 0.016, components, mat["gunmetal"], vertices=16), role="stock")
    add(tube_between("NGPR_StockStrutUpper", Vector((-0.030, -0.125, 0.175)), Vector((-0.105, -0.405, 0.185)), 0.012, components, mat["armor"], vertices=16), role="stock")
    add(tube_between("NGPR_StockStrutLower", Vector((-0.025, -0.105, 0.080)), Vector((-0.105, -0.405, 0.078)), 0.012, components, mat["armor"], vertices=16), role="stock")
    add(cylinder("NGPR_StockHingeCollar", (-0.020, -0.095, 0.130), 0.026, 0.045, components, mat["chrome"], axis="X", vertices=32, bevel=0.002), role="stock")
    add(tapered_box("NGPR_CheekRest_Base", (-0.060, -0.275, 0.210), (0.095, 0.210, 0.030), components, mat["gunmetal"], front=(0.78, 0.86), bevel=0.004), role="stock")
    add(tapered_box("NGPR_CheekRest_Pad", (-0.060, -0.275, 0.230), (0.100, 0.190, 0.020), components, mat["carbon"], front=(0.78, 0.86), bevel=0.004), role="stock")
    add(tapered_box("NGPR_ButtFrame", (-0.112, -0.422, 0.132), (0.088, 0.018, 0.154), components, mat["gunmetal"], front=(0.86, 0.90), bevel=0.004), face_policy.WEAPON_BUTTPAD, "stock")
    add(apply_stock_contact_perimeter_relief(tapered_box("NGPR_Buttpad", (-0.112, -0.442, 0.132), (0.094, 0.026, 0.150), components, mat["rubber"], front=(0.86, 0.90), bevel=0.006)), face_policy.WEAPON_BUTTPAD, "stock")
    add(apply_stock_contact_perimeter_relief(tapered_box("NGPR_ButtHeel", (-0.112, -0.454, 0.070), (0.100, 0.014, 0.035), components, mat["carbon"], front=(0.86, 0.90), bevel=0.003)), face_policy.WEAPON_BUTTPAD, "stock")
    add(cylinder("NGPR_StockAdjuster", (-0.126, -0.335, 0.118), 0.013, 0.025, components, mat["chrome"], axis="X", vertices=24, bevel=0.0015), role="stock")

    # The charging rail is receiver structure. Only the handle assembly follows
    # WeaponBolt, preventing the whole rail from sliding through the receiver.
    charging = add(box("NGPR_ChargingRail", (-0.072, 0.050, 0.190), (0.016, 0.160, 0.018), components, mat["chrome"], bevel=0.002), face_policy.WEAPON_ORDINARY, "bolt_rail_fixed")
    bolt_stem = add(cylinder("NGPR_BoltStem", (-0.121, 0.020, 0.188), 0.007, 0.068, components, mat["chrome"], axis="X", vertices=16), face_policy.WEAPON_BOLT_HANDLE, COMPONENT_BOLT)
    bolt_collar = add(cylinder("NGPR_BoltRootCollar", (-0.078, 0.020, 0.188), 0.015, 0.018, components, mat["gunmetal"], axis="X", vertices=24), face_policy.WEAPON_BOLT_HANDLE, COMPONENT_BOLT)
    bolt_knob = add(tapered_box("NGPR_BoltKnob", (-0.165, 0.020, 0.188), (0.020, 0.034, 0.026), components, mat["rubber"], front=(0.82, 0.82), bevel=0.004), face_policy.WEAPON_BOLT_HANDLE, COMPONENT_BOLT)

    # Conventional uninterrupted optic corridor. Glass is a separate renderer.
    for y in (-0.075, 0.130):
        add(box(f"NGPR_OpticMount_{'Rear' if y < 0 else 'Front'}", (0.0, y, 0.257), (0.064, 0.042, 0.052), components, mat["gunmetal"], bevel=0.003), role="optic_mount")
        add(hollow_cylinder(f"NGPR_OpticClamp_{'Rear' if y < 0 else 'Front'}", (0.0, y, 0.315), 0.029, 0.0255, 0.018, components, mat["gunmetal"], vertices=32), role="optic")
    add(hollow_cylinder("NGPR_OpticTube", (0.0, 0.015, 0.315), 0.025, 0.020, 0.390, components, mat["carbon"], vertices=32), role="optic")
    add(hollow_cylinder("NGPR_OpticObjective", (0.0, 0.235, 0.315), 0.044, 0.035, 0.105, components, mat["armor"], vertices=32), role="optic")
    add(hollow_cylinder("NGPR_OpticOcular", (0.0, -0.225, 0.315), 0.036, 0.029, 0.095, components, mat["armor"], vertices=32), role="optic")
    add(cylinder("NGPR_OpticElevation", (0.0, 0.010, 0.360), 0.016, 0.032, components, mat["chrome"], axis="Z", vertices=28), role="optic")
    add(cylinder("NGPR_OpticWindage", (-0.047, 0.010, 0.315), 0.014, 0.028, components, mat["chrome"], axis="X", vertices=28), role="optic")
    for name, y, radius in (("Rear", -0.274, 0.030), ("Front", 0.291, 0.037)):
        lens = hollow_cylinder(f"NGPR_OpticLens{name}", (0.0, y, 0.315), radius, radius * 0.90, 0.008, optic_components, mat["glass"], vertices=32, bevel=0.001)
        lens["ngpr_semantic_zone"] = face_policy.WEAPON_ORDINARY
        optics.append(lens)

    # Restrained status strips and protected fasteners.
    for index, y in enumerate((0.01, 0.18, 0.36, 0.54)):
        add(box(f"NGPR_Status_{index}", (0.060, y, 0.175), (0.006, 0.052, 0.008), components, mat["cyan"], bevel=0.001))
    for side in (-1.0, 1.0):
        for index, y in enumerate((-0.045, 0.115, 0.285, 0.555)):
            add(cylinder(f"NGPR_Fastener_{side}_{index}", (side * 0.064, y, 0.154), 0.007, 0.006, components, mat["chrome"], axis="X", vertices=12, bevel=0.001))

    helpers = [
        create_helper("Rifle_PrimaryGrip", ROLE_PRIMARY_GRIP, (-0.085, -0.070, 0.025), components,
                      rotation=(-math.pi / 2, 0.20, -math.pi / 2)),
        create_helper("Rifle_SupportGripTarget", ROLE_SUPPORT_GRIP, (0.120, 0.280, 0.015), components,
                      rotation=(-math.pi / 2, 0.20, -math.pi / 2)),
        create_helper("Rifle_StockContact", ROLE_STOCK_CONTACT, (-0.112, -0.448, 0.132), components,
                      rotation=(0.0, 0.0, -math.pi)),
        create_helper("Rifle_SightOcular", ROLE_SIGHT_OCULAR, (0.0, -0.280, 0.315), components),
        create_helper("Rifle_Muzzle", ROLE_MUZZLE, (0.0, 1.175, 0.145), components),
        create_helper("Rifle_SupportGripMin", ROLE_SUPPORT_MIN, (0.097, 0.250, 0.015), components),
        create_helper("Rifle_SupportGripMax", ROLE_SUPPORT_MAX, (0.137, 0.315, 0.015), components),
    ]
    for child in [*parts, *optics, *helpers]:
        child.parent = root
        child.parent_type = "OBJECT"
        child.matrix_parent_inverse = Matrix.Identity(4)
    for component in (magazine, magazine_base, *magazine_ribs, magazine_pull_lug):
        tag_component(component, COMPONENT_MAGAZINE)
        component[WEAPON_OWNER_PROPERTY] = ASSET_ID
    for component in (bolt_stem, bolt_collar, bolt_knob):
        tag_component(component, COMPONENT_BOLT)
        component[WEAPON_OWNER_PROPERTY] = ASSET_ID
    tag_contact_surface(bpy.data.objects["NGPR_PistolGrip"], ROLE_PRIMARY_GRIP)
    tag_contact_surface(bpy.data.objects["NGPR_SupportGrip"], ROLE_SUPPORT_GRIP)
    tag_contact_surface(bpy.data.objects["NGPR_Buttpad"], ROLE_STOCK_CONTACT)
    return parts, optics


def apply_modifiers(objects: list[bpy.types.Object]) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        for modifier in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)


def triangulate_mesh(obj: bpy.types.Object) -> None:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def component_architecture_role(obj: bpy.types.Object, renderer_role: str) -> str:
    """Classify every visible source vertex into the closed WeaponV3 role set."""
    articulated = str(obj.get("ps_weapon_component_role", ""))
    if articulated == COMPONENT_MAGAZINE:
        return "magazine"
    if articulated == COMPONENT_BOLT:
        return "bolt"
    if renderer_role == "optic" or "Optic" in obj.name:
        return "optic_mount"
    name = obj.name
    if any(token in name for token in ("Barrel", "Muzzle", "ThermalSleeve")):
        return "barrel"
    if any(token in name for token in ("Stock", "Butt", "CheekRest")):
        return "stock"
    if any(token in name for token in (
        "Handguard", "SupportYoke", "SupportGrip", "VentBay",
        "BraidedCable", "CableClamp",
    )):
        return "handguard"
    return "receiver"


def encode_component_architecture(obj: bpy.types.Object, role: str) -> None:
    if role not in COMPONENT_ROLE_IDS:
        raise RuntimeError(f"Unknown WeaponV3 component architecture role: {role}")
    attribute = obj.data.attributes.get("weapon_v3_component_role")
    if attribute is None:
        attribute = obj.data.attributes.new(
            name="weapon_v3_component_role", type="INT", domain="POINT"
        )
    if attribute.domain != "POINT" or attribute.data_type != "INT":
        raise RuntimeError("weapon_v3_component_role must be an INT POINT attribute")
    identifier = COMPONENT_ROLE_IDS[role]
    for datum in attribute.data:
        datum.value = identifier
    obj["weapon_v3_component_role_table_json"] = json.dumps(
        COMPONENT_ROLE_TABLE, sort_keys=True, separators=(",", ":")
    )


def join_renderer(name: str, objects: list[bpy.types.Object], collection: bpy.types.Collection,
                  mat: bpy.types.Material, role: str, lod: int,
                  armature: bpy.types.Object) -> bpy.types.Object:
    # Encode provenance before join. Blender preserves vertex groups while
    # joining and therefore does not depend on object append order.
    for obj in objects:
        component_role = str(obj.get("ps_weapon_component_role", ""))
        bone = (
            "WeaponMagazine" if component_role == COMPONENT_MAGAZINE
            else "WeaponBolt" if component_role == COMPONENT_BOLT
            else "WeaponRoot"
        )
        group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
        group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
        encode_component_architecture(
            obj, component_architecture_role(obj, role)
        )
        create_face_attribute(
            obj,
            face_policy.WEAPON_ATTRIBUTE,
            int(obj.get("ngpr_semantic_zone", face_policy.WEAPON_ORDINARY)),
        )
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = objects[0]
    # bpy.ops.object.join bakes every selected object's transform into the
    # active object's data but retains the active transform. Bake that final
    # transform too so production renderers are unit/identity objects.
    result.data.transform(result.matrix_world)
    result.matrix_world = Matrix.Identity(4)
    result.name = name
    result.data.name = name + "_Mesh"
    for owner in list(result.users_collection):
        owner.objects.unlink(result)
    collection.objects.link(result)
    if len(result.data.materials) > 4:
        # Compact the joined semantic palette to four production materials.
        palette = []
        mapping = {}
        for index, source_material in enumerate(result.data.materials):
            key = (
                "cyan" if source_material and "Cyan" in source_material.name
                else "carbon" if source_material and any(token in source_material.name for token in ("Carbon", "Rubber"))
                else "metal" if source_material and any(token in source_material.name for token in ("Gunmetal", "Chrome"))
                else "armor"
            )
            if key not in palette:
                palette.append(key)
            mapping[index] = palette.index(key)
        material_lookup = {
            "armor": bpy.data.materials["NGPR_SootBlackArmor"],
            "carbon": bpy.data.materials["NGPR_CarbonComposite"],
            "metal": bpy.data.materials["NGPR_OilyGunmetal"],
            "cyan": bpy.data.materials["NGPR_CyanStatus"],
        }
        remapped_indices = [
            mapping.get(polygon.material_index, 0)
            for polygon in result.data.polygons
        ]
        result.data.materials.clear()
        for key in palette:
            result.data.materials.append(material_lookup[key])
        # Clearing Blender material slots also resets every polygon to slot 0.
        # Reapply the semantic mapping after rebuilding the compact palette so
        # carbon, armor, metal and cyan keep distinct surface responses.
        for polygon, material_index in zip(
            result.data.polygons, remapped_indices
        ):
            polygon.material_index = material_index
    result["weapon_v3_role"] = role
    result["weapon_v3_lod"] = lod
    result["weapon_v3_component_role_table_json"] = json.dumps(
        COMPONENT_ROLE_TABLE, sort_keys=True, separators=(",", ":")
    )
    result["hero_v2_asset"] = "rifle" if role == "rifle" else "optic"
    result["hero_v2_lod"] = lod
    result["ps_clearance_asset_role"] = "weapon"
    result.parent = armature
    result.parent_type = "OBJECT"
    result.parent_bone = ""
    result.matrix_parent_inverse = Matrix.Identity(4)
    result.matrix_basis = Matrix.Identity(4)
    # Production collections remain authored-visible so validation can inspect
    # each LOD deterministically. Render-time visibility is selected explicitly.
    result.hide_render = False
    result.hide_set(False)
    return result


def copy_renderer(source: bpy.types.Object, name: str, collection: bpy.types.Collection,
                  target: int, role: str, lod: int) -> bpy.types.Object:
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = name
    obj.data.name = name + "_Mesh"
    collection.objects.link(obj)
    obj["weapon_v3_role"] = role
    obj["weapon_v3_lod"] = lod
    obj["hero_v2_asset"] = "rifle" if role == "rifle" else "optic"
    obj["hero_v2_lod"] = lod
    obj["ps_clearance_asset_role"] = "weapon"
    obj.hide_render = False
    obj.hide_set(False)
    # LOD copies are production render data, not live skinned evaluation in this
    # isolated source blend; clear the copied rig adapter before reduction.
    obj.parent = source.parent
    for modifier in list(obj.modifiers):
        obj.modifiers.remove(modifier)
    current = len(obj.data.polygons)
    if current > target:
        modifier = obj.modifiers.new("LODReduction", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max(0.01, target / current)
        bpy.context.view_layer.objects.active = obj
        obj.hide_set(False)
        obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)
    obj.hide_set(False)
    triangulate_mesh(obj)
    return obj


def add_armature_adapter(obj: bpy.types.Object, armature: bpy.types.Object) -> None:
    """Attach the production renderer to the exact rigid control weights."""

    obj.parent = armature
    obj.parent_type = "OBJECT"
    obj.parent_bone = ""
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = Matrix.Identity(4)
    modifier = obj.modifiers.new("WeaponRig", "ARMATURE")
    modifier.object = armature


def bake_armature_adapter(obj: bpy.types.Object) -> None:
    """Retain the live rigid armature adapter for action-space evaluation."""

    if not any(item.type == "ARMATURE" for item in obj.modifiers):
        raise RuntimeError(f"{obj.name} lost its required rigid armature adapter")


def validate_weapon_skin_contract(
    armature: bpy.types.Object,
    renderers: list[bpy.types.Object],
    controls: tuple[str, str, str],
) -> dict[str, object]:
    """Fail closed on the rigid weapon-skin bind used by review and export."""

    identity = Matrix.Identity(4)
    root_name, magazine_name, bolt_name = controls
    data_bones = armature.data.bones
    if data_bones[root_name].parent is not None:
        raise RuntimeError(f"{root_name} must remain a top-level weapon skin control")
    for child_name in (magazine_name, bolt_name):
        if data_bones[child_name].parent != data_bones[root_name]:
            raise RuntimeError(f"{child_name} must remain parented to {root_name}")

    reference_rest = data_bones[root_name].matrix_local.copy()
    rest_evidence = {}
    for name in controls:
        bone = data_bones[name]
        determinant = float(bone.matrix_local.to_3x3().determinant())
        if determinant <= 0.0:
            raise RuntimeError(f"{name} has a reflected or singular rest matrix")
        rest_delta = max(
            abs(float(bone.matrix_local[row][column] - reference_rest[row][column]))
            for row in range(4)
            for column in range(4)
        )
        if rest_delta > 1.0e-6:
            raise RuntimeError(
                "Candidate007 weapon skin controls no longer share one bind "
                f"matrix: {name} delta={rest_delta:.9f}"
            )
        if not bone.use_deform:
            raise RuntimeError(f"{name} must deform Candidate007 weapon renderers")
        rest_evidence[name] = {
            "use_deform": True,
            "rest_determinant": round(determinant, 9),
            "rest_delta_from_root": round(rest_delta, 9),
        }

    renderer_evidence = {}
    for obj in renderers:
        if obj.parent != armature or obj.parent_type != "OBJECT":
            raise RuntimeError(f"{obj.name} must remain object-parented to the armature")
        relative_world = armature.matrix_world.inverted_safe() @ obj.matrix_world
        alignment_error = max(
            abs(float(relative_world[row][column] - identity[row][column]))
            for row in range(4)
            for column in range(4)
        )
        if alignment_error > 1.0e-6:
            raise RuntimeError(
                f"{obj.name} object/world alignment drifted by {alignment_error:.9f}"
            )
        modifiers = [modifier for modifier in obj.modifiers if modifier.type == "ARMATURE"]
        if len(modifiers) != 1 or modifiers[0].object != armature:
            raise RuntimeError(f"{obj.name} must have exactly one adapter targeting {armature.name}")
        if not modifiers[0].use_vertex_groups or modifiers[0].use_bone_envelopes:
            raise RuntimeError(f"{obj.name} adapter must use only exact vertex-group weights")

        counts = {name: 0 for name in controls}
        for vertex in obj.data.vertices:
            assignments = list(vertex.groups)
            if len(assignments) != 1:
                raise RuntimeError(
                    f"{obj.name} vertex {vertex.index} has {len(assignments)} skin assignments"
                )
            assignment = assignments[0]
            group_name = obj.vertex_groups[assignment.group].name
            if group_name not in counts or abs(float(assignment.weight) - 1.0) > 1.0e-6:
                raise RuntimeError(
                    f"{obj.name} vertex {vertex.index} has non-rigid assignment "
                    f"{group_name}={float(assignment.weight):.9f}"
                )
            counts[group_name] += 1
        if counts[root_name] == 0:
            raise RuntimeError(f"{obj.name} has no {root_name}-weighted vertices")
        if str(obj.get("weapon_v3_role", "")) == "rifle" and any(
            counts[name] == 0 for name in (magazine_name, bolt_name)
        ):
            raise RuntimeError(f"{obj.name} lost articulated magazine or bolt weights")
        renderer_evidence[obj.name] = {
            "armature_modifier_count": 1,
            "armature_target": armature.name,
            "object_alignment_error": round(alignment_error, 9),
            "rigid_vertex_counts": counts,
        }
    return {"controls": rest_evidence, "renderers": renderer_evidence}


def unwrap_uv0(objects: list[bpy.types.Object]) -> None:
    for obj in objects:
        while obj.data.uv_layers:
            obj.data.uv_layers.remove(obj.data.uv_layers[0])
        layer = obj.data.uv_layers.new(name="UV0")
        if int(obj.get("weapon_v3_lod", 0)) >= 2:
            # Give every reduced triangle a private atlas cell. Decimation can
            # leave a handful of smart-projection overlaps; this deterministic
            # triangle atlas is conservative, finite and provably disjoint.
            face_count = len(obj.data.polygons)
            side = max(1, math.ceil(math.sqrt(face_count)))
            inset = 0.12
            for polygon in obj.data.polygons:
                if len(polygon.loop_indices) != 3:
                    raise RuntimeError(f"{obj.name} must be triangulated before UV atlas creation")
                cell_x = polygon.index % side
                cell_y = polygon.index // side
                corners = ((inset, inset), (1.0 - inset, inset), (inset, 1.0 - inset))
                for loop_index, (u, v) in zip(polygon.loop_indices, corners):
                    layer.data[loop_index].uv = (
                        (cell_x + u) / side,
                        (cell_y + v) / side,
                    )
            obj.data.update()
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.hide_set(False)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(60.0), island_margin=0.007)
        bpy.ops.uv.pack_islands(rotate=True, margin=0.007)
        bpy.ops.object.mode_set(mode="OBJECT")
        obj.select_set(False)
        obj.hide_set(False)


def topology_metrics(obj: bpy.types.Object) -> dict[str, int | float]:
    mesh = obj.data
    mesh.calc_loop_triangles()
    edge_uses: Counter[tuple[int, int]] = Counter()
    zero_area = 0
    for triangle in mesh.loop_triangles:
        ids = tuple(int(index) for index in triangle.vertices)
        for a, b in ((ids[0], ids[1]), (ids[1], ids[2]), (ids[2], ids[0])):
            edge_uses[tuple(sorted((a, b)))] += 1
        if triangle.area <= 1.0e-12:
            zero_area += 1
    duplicate = 0
    seen: set[tuple[int, int, int]] = set()
    for vertex in mesh.vertices:
        key = tuple(round(float(value) / 1.0e-6) for value in vertex.co)
        if key in seen:
            duplicate += 1
        seen.add(key)
    return {
        "vertices": len(mesh.vertices),
        "triangles": len(mesh.loop_triangles),
        "boundary_edges": sum(value == 1 for value in edge_uses.values()),
        "non_manifold_edges": sum(value > 2 for value in edge_uses.values()),
        "zero_area_faces": zero_area,
        "duplicate_vertex_pairs": duplicate,
    }


def create_face_attribute(obj: bpy.types.Object, name: str, default: int,
                          zones: list[int] | None = None) -> list[int]:
    old = obj.data.attributes.get(name)
    if old is not None:
        obj.data.attributes.remove(old)
    attribute = obj.data.attributes.new(name=name, type="INT", domain="FACE")
    values = zones if zones is not None else [default] * len(obj.data.polygons)
    if len(values) != len(obj.data.polygons):
        raise RuntimeError(f"{obj.name} semantic values do not match polygon count")
    for item, value in zip(attribute.data, values):
        item.value = int(value)
    return values


def tag_suit_semantics(armature: bpy.types.Object) -> list[bpy.types.Object]:
    # Keep the anatomical classifier ordinary-Python testable.  It deliberately
    # knows nothing about Blender meshes or face-policy IDs.
    import suit_hand_semantics as hand_semantics  # type: ignore
    from mathutils.kdtree import KDTree  # type: ignore

    suit = sorted(
        [obj for obj in bpy.data.objects if obj.name in {"H2_Undersuit_LOD0", "H2_Armor_LOD0", "H2_Emission_LOD0"}],
        key=lambda item: item.name,
    )
    if len(suit) != 3:
        raise RuntimeError("Candidate007 requires Candidate005's three visible suit renderers")

    by_name = {obj.name: obj for obj in suit}
    values_by_name = {
        obj.name: [face_policy.SUIT_ORDINARY] * len(obj.data.polygons)
        for obj in suit
    }
    zone_ids = {
        "R": {
            hand_semantics.ZONE_GRIP: face_policy.SUIT_PRIMARY_HAND_RIGHT,
            hand_semantics.ZONE_MANIPULATION: face_policy.SUIT_BOLT_HAND_RIGHT,
        },
        "L": {
            hand_semantics.ZONE_GRIP: face_policy.SUIT_SUPPORT_HAND_LEFT,
            hand_semantics.ZONE_MANIPULATION: face_policy.SUIT_MAGAZINE_HAND_LEFT,
        },
    }
    tagging_report: dict[str, object] = {
        "schema_version": hand_semantics.SCHEMA_VERSION,
        "coordinate_space": "armature_rest",
        "method": "normalized_hand_lowerarm_influence_plus_hand_bone_envelope",
        "thresholds": {
            "min_face_chain_influence": hand_semantics.MIN_FACE_CHAIN_INFLUENCE,
            "min_vertex_chain_influence": hand_semantics.MIN_VERTEX_CHAIN_INFLUENCE,
            "min_hand_t": hand_semantics.MIN_HAND_T,
            "distal_finger_start_t": hand_semantics.DISTAL_FINGER_START_T,
            "max_hand_t": hand_semantics.MAX_HAND_T,
            "max_axis_distance_m": hand_semantics.MAX_AXIS_DISTANCE_M,
            "max_segment_distance_m": hand_semantics.MAX_SEGMENT_DISTANCE_M,
            "max_matching_armor_distance_m": hand_semantics.MAX_MATCHING_ARMOR_DISTANCE_M,
        },
        "objects": {},
    }

    armor_hand_centers: dict[str, list[Vector]] = {"R": [], "L": []}
    armor_trees: dict[str, KDTree] = {}

    # Armor is evaluated first because it is the visible glove surface.  A
    # hidden undersuit face may subsequently qualify only if it independently
    # passes influence/anatomical-t checks and lies on that armor hand surface.
    for object_name in ("H2_Armor_LOD0", "H2_Undersuit_LOD0"):
        obj = by_name[object_name]
        values = values_by_name[object_name]
        group_names = {group.index: group.name for group in obj.vertex_groups}
        to_armature_rest = armature.matrix_world.inverted() @ obj.matrix_world
        reason_counts: dict[str, Counter[str]] = {"R": Counter(), "L": Counter()}
        tagged_counts: dict[str, Counter[str]] = {"R": Counter(), "L": Counter()}
        matched_armor_faces = 0

        for polygon in obj.data.polygons:
            vertex_weights: list[dict[str, float]] = []
            aggregate_weights: Counter[str] = Counter()
            for vertex_index in polygon.vertices:
                weights = {
                    group_names.get(group.group, ""): float(group.weight)
                    for group in obj.data.vertices[vertex_index].groups
                }
                vertex_weights.append(weights)
                aggregate_weights.update(weights)

            # Preserve the existing stock-pocket authoring rule; this pass only
            # replaces the former arbitrary hand-face ordering/quarter split.
            if object_name == "H2_Armor_LOD0":
                dominant = aggregate_weights.most_common(1)[0][0] if aggregate_weights else ""
                center = polygon.center
                if (
                    dominant in {"Chest", "UpperArm.R"}
                    and center.x < -0.10
                    and center.y > -0.08
                    and center.z > 1.38
                ):
                    values[polygon.index] = face_policy.SUIT_STOCK_POCKET_RIGHT

            rest_center = to_armature_rest @ polygon.center
            decisions: list[tuple[str, object]] = []
            for side in ("R", "L"):
                matching_distance = None
                if object_name == "H2_Undersuit_LOD0" and side in armor_trees:
                    _coordinate, _index, matching_distance = armor_trees[side].find(rest_center)
                bone = armature.data.bones.get(f"Hand.{side}")
                if bone is None:
                    raise RuntimeError(f"Candidate007 requires Hand.{side} rest bone")
                decision = hand_semantics.classify_hand_surface(
                    point=tuple(rest_center),
                    bone_head=tuple(bone.head_local),
                    bone_tail=tuple(bone.tail_local),
                    vertex_weights=vertex_weights,
                    hand_group=f"Hand.{side}",
                    lower_arm_group=f"LowerArm.{side}",
                    matching_armor_distance_m=matching_distance,
                )
                reason_counts[side][decision.reason] += 1
                if decision.zone != hand_semantics.ZONE_ORDINARY:
                    decisions.append((side, decision))

            if len(decisions) > 1:
                raise RuntimeError(
                    f"{obj.name} face {polygon.index} ambiguously matches both hand envelopes"
                )
            if decisions:
                side, decision = decisions[0]
                values[polygon.index] = zone_ids[side][decision.zone]
                tagged_counts[side][decision.zone] += 1
                if object_name == "H2_Armor_LOD0":
                    armor_hand_centers[side].append(rest_center.copy())
                elif (
                    decision.matching_armor_distance_m is not None
                    and decision.matching_armor_distance_m
                    <= hand_semantics.MAX_MATCHING_ARMOR_DISTANCE_M
                ):
                    matched_armor_faces += 1

        tagging_report["objects"][object_name] = {
            "face_count": len(obj.data.polygons),
            "tagged": {
                side: dict(sorted(tagged_counts[side].items()))
                for side in ("R", "L")
            },
            "decision_reasons": {
                side: dict(sorted(reason_counts[side].items()))
                for side in ("R", "L")
            },
            "matching_armor_faces": matched_armor_faces,
        }

        if object_name == "H2_Armor_LOD0":
            for side in ("R", "L"):
                points = armor_hand_centers[side]
                if not points:
                    continue
                tree = KDTree(len(points))
                for index, point in enumerate(points):
                    tree.insert(point, index)
                tree.balance()
                armor_trees[side] = tree

    # Emission has no palm/finger surface in Candidate005 and therefore remains
    # ordinary/forbidden.  Persist exact counts both in the blend and stdout so
    # a source-geometry change cannot silently broaden the contact whitelist.
    tagging_report["objects"]["H2_Emission_LOD0"] = {
        "face_count": len(by_name["H2_Emission_LOD0"].data.polygons),
        "tagged": {"R": {}, "L": {}},
        "decision_reasons": {"R": {}, "L": {}},
        "matching_armor_faces": 0,
    }

    combined_counts: Counter[int] = Counter()
    for obj in suit:
        values = values_by_name[obj.name]
        create_face_attribute(obj, face_policy.SUIT_ATTRIBUTE, face_policy.SUIT_ORDINARY, values)
        obj["ps_clearance_asset_role"] = "suit"
        object_counts = Counter(values)
        combined_counts.update(object_counts)
        obj["ps_clearance_hand_semantics_schema"] = hand_semantics.SCHEMA_VERSION
        obj["ps_clearance_hand_semantic_counts_json"] = json.dumps(
            {str(key): value for key, value in sorted(object_counts.items())},
            sort_keys=True,
            separators=(",", ":"),
        )

    required_hand_zones = {
        face_policy.SUIT_PRIMARY_HAND_RIGHT,
        face_policy.SUIT_SUPPORT_HAND_LEFT,
        face_policy.SUIT_MAGAZINE_HAND_LEFT,
        face_policy.SUIT_BOLT_HAND_RIGHT,
    }
    missing = sorted(zone for zone in required_hand_zones if combined_counts[zone] <= 0)
    tagging_report["combined_semantic_counts"] = {
        str(key): value for key, value in sorted(combined_counts.items())
    }
    tagging_report["missing_required_hand_semantics"] = missing
    tagging_report["status"] = "FAIL" if missing else "PASS"
    payload = json.dumps(tagging_report, sort_keys=True, separators=(",", ":"))
    bpy.context.scene["ps_clearance_hand_semantics_json"] = payload
    print(f"CANDIDATE007_HAND_SEMANTICS={payload}")
    if missing:
        raise RuntimeError(
            f"Candidate007 hand tagging failed closed; missing semantic IDs {missing}"
        )
    return suit


def add_clearance_manifest(suit: list[bpy.types.Object], weapon: list[bpy.types.Object]) -> dict[str, object]:
    entries = []
    for obj in [*suit, *weapon]:
        role = str(obj["ps_clearance_asset_role"])
        attribute_name = face_policy.SUIT_ATTRIBUTE if role == "suit" else face_policy.WEAPON_ATTRIBUTE
        attribute = obj.data.attributes.get(attribute_name)
        if attribute is None:
            values = create_face_attribute(obj, attribute_name, face_policy.SUIT_ORDINARY if role == "suit" else face_policy.WEAPON_ORDINARY)
        else:
            values = [int(item.value) for item in attribute.data]
        obj.data.calc_loop_triangles()
        triangles = [tuple(int(index) for index in tri.vertices) for tri in obj.data.loop_triangles]
        if len(triangles) != len(values):
            raise RuntimeError(f"{obj.name} must remain fully triangulated before clearance freeze")
        topology_hash = face_policy.topology_semantics_sha256(triangles, values)
        entry = {
            "name": obj.name,
            "asset_role": role,
            "semantic_attribute": attribute_name,
            "face_count": len(values),
            "topology_sha256": topology_hash,
            "semantic_counts": face_policy.semantic_counts(values),
        }
        entries.append(entry)
    # Candidate007 explicitly carries the rifle stowed in these legacy body
    # clips.  They must not inherit Ready contact permissions merely because
    # the shared action catalog also contains them.
    candidate007_stowed_legacy_actions = {"PS_Idle", "PS_Walk", "PS_Hover"}
    ready_windows = [
        {"action": action, "start": 1, "end": int(bpy.data.actions[action].frame_end)}
        for action in sorted(
            face_policy.READY_ACTIONS - candidate007_stowed_legacy_actions
        )
    ]
    primary_windows = [window for window in ready_windows if window["action"] != "PS_BoltCycle"]
    primary_windows.extend([
        {"action": "PS_BoltCycle", "start": 1, "end": 3},
        {"action": "PS_BoltCycle", "start": 17, "end": 20},
    ])
    support_windows = [window for window in ready_windows if window["action"] != "PS_Reload"]
    support_windows.extend([
        {"action": "PS_Reload", "start": 1, "end": 24},
        {"action": "PS_Reload", "start": 76, "end": 84},
    ])
    # The measured late catch gives the firing hand a longer acquisition/release
    # corridor than the support hand. Every adjacent transition frame remains
    # excluded by the Candidate007-only policy.
    primary_transition_contact_windows = [
        {"action": "PS_Weapon_Draw", "start": 26.75, "end": 30},
        {"action": "PS_Weapon_Sheathe", "start": 1, "end": 4.25},
    ]
    support_transition_contact_windows = [
        {"action": "PS_Weapon_Draw", "start": 29, "end": 30},
        {"action": "PS_Weapon_Sheathe", "start": 1, "end": 2},
    ]
    primary_windows.extend(
        dict(window) for window in primary_transition_contact_windows
    )
    support_windows.extend(
        dict(window) for window in support_transition_contact_windows
    )
    manifest = {
        "schema_version": face_policy.MANIFEST_SCHEMA,
        "policy_version": face_policy.POLICY_VERSION,
        "contact_window_policy_version": (
            face_policy.CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION
        ),
        "semantic_schema": face_policy.SEMANTIC_SCHEMA,
        "suit_asset_id": "AegisVanguardCandidate005",
        "weapon_asset_id": ASSET_ID,
        "source_candidate_sha256": face_policy.SOURCE_CANDIDATE_SHA256,
        "objects": sorted(entries, key=lambda item: item["name"]),
        "contact_windows": {
            "primary_grip": primary_windows,
            "support_grip": support_windows,
            "buttpad": ready_windows,
            "reload_mag": [{"action": "PS_Reload", "start": 25, "end": 75}],
            "bolt": [{"action": "PS_BoltCycle", "start": 4, "end": 16}],
        },
    }
    errors = face_policy.validate_manifest(manifest)
    if errors:
        raise RuntimeError("Clearance manifest invalid: " + "; ".join(errors))
    manifest_hash = face_policy.canonical_sha256(manifest)
    for obj in [*suit, *weapon]:
        entry = next(item for item in entries if item["name"] == obj.name)
        obj["ps_clearance_policy_version"] = face_policy.POLICY_VERSION
        obj["ps_clearance_semantic_schema"] = face_policy.SEMANTIC_SCHEMA
        obj["ps_clearance_manifest_sha256"] = manifest_hash
        obj["ps_clearance_expected_face_count"] = entry["face_count"]
        obj["ps_clearance_topology_sha256"] = entry["topology_sha256"]
    text = bpy.data.texts.get(face_policy.MANIFEST_TEXT_NAME) or bpy.data.texts.new(face_policy.MANIFEST_TEXT_NAME)
    text.clear()
    text.write(face_policy.canonical_json_bytes(manifest).decode("utf-8"))
    return {"sha256": manifest_hash, "manifest": manifest}


def action_signature(action: bpy.types.Action, armature: bpy.types.Object) -> dict[str, object]:
    slot = find_action_slot(action, armature)
    bag = get_action_channelbag(action, slot)
    curves = []
    for curve in sorted(bag.fcurves, key=lambda item: (item.data_path, int(item.array_index))):
        curves.append({
            "data_path": str(curve.data_path),
            "array_index": int(curve.array_index),
            "keys": [[round(float(value), 9) for value in point.co] for point in curve.keyframe_points],
        })
    document = {
        "name": action.name,
        "range": [float(action.frame_start), float(action.frame_end)],
        "slots": [[str(getattr(item, "identifier", "")), str(getattr(item, "target_id_type", ""))] for item in action.slots],
        "curves": curves,
    }
    return {
        "range": document["range"],
        "slot_count": len(list(action.slots)),
        "curve_count": len(curves),
        "sha256": hashlib.sha256(json.dumps(document, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
    }


def point_at(obj: bpy.types.Object, target: Vector) -> None:
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def create_studio(
    mat,
) -> tuple[
    list[bpy.types.Object],
    bpy.types.Object,
    bpy.types.Object,
    bpy.types.Object,
    tuple[bpy.types.Object, ...],
    bpy.types.Object,
    tuple[bpy.types.Object, ...],
]:
    collection = ensure_collection("NGPR002_ReviewStudio")
    ground = box("NGPR_StudioGround", (0.0, 0.0, -0.07), (6.0, 6.0, 0.10), collection, mat["studio"], bevel=0.0)
    ground["aegis_studio_only"] = True

    # The ocular review must prove that the optic presents a usable sight
    # picture.  A real, distant target is more useful evidence than pointing
    # the camera through the tube at an empty background.  These meshes live
    # only in the review studio and are never part of the weapon export.
    target_root = bpy.data.objects.new("NGPR_ScopeTargetRoot", None)
    collection.objects.link(target_root)
    target_root["aegis_studio_only"] = True
    target_objects = (
        box(
            "NGPR_ScopeTargetBoard",
            (0.0, 0.010, 0.0),
            (1.40, 0.018, 1.40),
            collection,
            mat["armor"],
            bevel=0.018,
        ),
        hollow_cylinder(
            "NGPR_ScopeTargetRing",
            (0.0, -0.008, 0.0),
            0.42,
            0.392,
            0.012,
            collection,
            mat["cyan"],
            vertices=48,
            bevel=0.0,
        ),
        box(
            "NGPR_ScopeReticleUp",
            (0.0, -0.010, 0.20),
            (0.018, 0.010, 0.28),
            collection,
            mat["cyan"],
            bevel=0.002,
        ),
        box(
            "NGPR_ScopeReticleDown",
            (0.0, -0.010, -0.20),
            (0.018, 0.010, 0.28),
            collection,
            mat["cyan"],
            bevel=0.002,
        ),
        box(
            "NGPR_ScopeReticleLeft",
            (-0.20, -0.010, 0.0),
            (0.28, 0.010, 0.018),
            collection,
            mat["cyan"],
            bevel=0.002,
        ),
        box(
            "NGPR_ScopeReticleRight",
            (0.20, -0.010, 0.0),
            (0.28, 0.010, 0.018),
            collection,
            mat["cyan"],
            bevel=0.002,
        ),
        box(
            "NGPR_ScopeTargetRange1",
            (0.0, -0.011, -0.16),
            (0.34, 0.009, 0.012),
            collection,
            mat["cyan"],
            bevel=0.001,
        ),
        box(
            "NGPR_ScopeTargetRange2",
            (0.0, -0.011, -0.25),
            (0.24, 0.009, 0.012),
            collection,
            mat["cyan"],
            bevel=0.001,
        ),
        box(
            "NGPR_ScopeTargetRange3",
            (0.0, -0.011, -0.34),
            (0.14, 0.009, 0.012),
            collection,
            mat["cyan"],
            bevel=0.001,
        ),
        box(
            "NGPR_ScopeTargetRange4",
            (0.0, -0.011, -0.43),
            (0.08, 0.009, 0.012),
            collection,
            mat["cyan"],
            bevel=0.001,
        ),
    )
    for target_object in target_objects:
        target_object.parent = target_root
        target_object.matrix_parent_inverse = Matrix.Identity(4)
        target_object["aegis_studio_only"] = True
        target_object.hide_render = True

    # A real scope does not expose the distant objective as a tiny naked hole;
    # its lenses project a magnified image at the ocular.  Represent that
    # optical function explicitly for review with a studio-only focal plane.
    # The physical six-metre target and clear-bore ray remain independently
    # validated below, so this is evidence presentation rather than weapon
    # geometry or a collision shortcut.
    sight_picture_root = bpy.data.objects.new("NGPR_ScopeSightPictureRoot", None)
    collection.objects.link(sight_picture_root)
    sight_picture_root["aegis_studio_only"] = True
    sight_picture_objects = (
        # Deliberately omit a solid focal-plane disc.  The reticle is an
        # optical overlay, while the independently ray-validated six-metre
        # target must remain genuinely visible through its open areas.
        hollow_cylinder(
            "NGPR_ScopeSightPictureRing",
            (0.0, -0.0008, 0.0),
            0.026,
            0.0245,
            0.0007,
            collection,
            mat["cyan"],
            vertices=48,
            bevel=0.0,
        ),
        box("NGPR_ScopeSightUp", (0.0, -0.0010, 0.012), (0.0010, 0.0006, 0.015), collection, mat["cyan"], bevel=0.0),
        box("NGPR_ScopeSightDown", (0.0, -0.0010, -0.012), (0.0010, 0.0006, 0.015), collection, mat["cyan"], bevel=0.0),
        box("NGPR_ScopeSightLeft", (-0.012, -0.0010, 0.0), (0.015, 0.0006, 0.0010), collection, mat["cyan"], bevel=0.0),
        box("NGPR_ScopeSightRight", (0.012, -0.0010, 0.0), (0.015, 0.0006, 0.0010), collection, mat["cyan"], bevel=0.0),
        box("NGPR_ScopeSightRange1", (0.0, -0.0011, -0.006), (0.012, 0.0005, 0.0006), collection, mat["cyan"], bevel=0.0),
        box("NGPR_ScopeSightRange2", (0.0, -0.0011, -0.010), (0.009, 0.0005, 0.0006), collection, mat["cyan"], bevel=0.0),
        box("NGPR_ScopeSightRange3", (0.0, -0.0011, -0.014), (0.006, 0.0005, 0.0006), collection, mat["cyan"], bevel=0.0),
        box("NGPR_ScopeSightRange4", (0.0, -0.0011, -0.018), (0.003, 0.0005, 0.0006), collection, mat["cyan"], bevel=0.0),
        cylinder("NGPR_ScopeSightTarget", (0.0, -0.0012, 0.004), 0.0018, 0.0004, collection, mat["cyan"], axis="Y", vertices=20, bevel=0.0),
    )
    for sight_object in sight_picture_objects:
        sight_object.parent = sight_picture_root
        sight_object.matrix_parent_inverse = Matrix.Identity(4)
        sight_object["aegis_studio_only"] = True
        sight_object["ngpr_optical_projection_only"] = True
        sight_object.hide_render = True
    lights = []
    for name, location, energy, color, size in (
        ("NGPR_Key", (3.2, 4.2, 3.6), 560.0, (1.0, 0.72, 0.50), 1.4),
        ("NGPR_Fill", (-3.4, 2.7, 2.6), 90.0, (0.22, 0.30, 0.44), 2.0),
        ("NGPR_Rim", (0.4, -3.8, 3.4), 620.0, (0.10, 0.28, 0.46), 0.9),
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
    camera_data = bpy.data.cameras.new("NGPR_ReviewCamera")
    camera = bpy.data.objects.new("NGPR_ReviewCamera", camera_data)
    collection.objects.link(camera)
    bpy.context.scene.camera = camera
    return (
        lights,
        camera,
        ground,
        target_root,
        target_objects,
        sight_picture_root,
        sight_picture_objects,
    )


def projected_renderer_points(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    renderers: tuple[bpy.types.Object, ...],
) -> list[Vector]:
    """Return deterministic, in-front-of-camera renderer samples."""

    depsgraph = bpy.context.evaluated_depsgraph_get()
    projected = []
    for source in renderers:
        evaluated = source.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
        try:
            step = max(1, len(mesh.vertices) // 2000)
            for index, vertex in enumerate(mesh.vertices):
                if index % step:
                    continue
                coordinate = world_to_camera_view(
                    scene, camera, evaluated.matrix_world @ vertex.co
                )
                if coordinate.z > 0.0:
                    projected.append(coordinate)
        finally:
            evaluated.to_mesh_clear()
    if not projected:
        raise RuntimeError("Render geometry is entirely behind the review camera")
    return projected


def projected_renderer_bounds(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    renderers: tuple[bpy.types.Object, ...],
) -> tuple[float, float, float, float, int]:
    projected = projected_renderer_points(scene, camera, renderers)
    return (
        min(point.x for point in projected),
        max(point.x for point in projected),
        min(point.y for point in projected),
        max(point.y for point in projected),
        len(projected),
    )


def projected_context_metrics(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    renderers: tuple[bpy.types.Object, ...],
    *,
    viewport_border: float = 0.02,
) -> dict[str, float | int]:
    """Measure actual sampled context geometry inside the review viewport."""

    projected = projected_renderer_points(scene, camera, renderers)
    visible = [
        point
        for point in projected
        if viewport_border <= point.x <= 1.0 - viewport_border
        and viewport_border <= point.y <= 1.0 - viewport_border
    ]
    if visible:
        minimum_x = min(float(point.x) for point in visible)
        maximum_x = max(float(point.x) for point in visible)
        minimum_y = min(float(point.y) for point in visible)
        maximum_y = max(float(point.y) for point in visible)
    else:
        minimum_x = maximum_x = minimum_y = maximum_y = 0.0
    width = maximum_x - minimum_x
    height = maximum_y - minimum_y
    return {
        "context_viewport_min_x": round(minimum_x, 6),
        "context_viewport_max_x": round(maximum_x, 6),
        "context_viewport_min_y": round(minimum_y, 6),
        "context_viewport_max_y": round(maximum_y, 6),
        "context_viewport_width": round(width, 6),
        "context_viewport_height": round(height, 6),
        "context_visible_sample_count": len(visible),
        "context_projected_sample_count": len(projected),
    }


def projected_inner_rim_bounds(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    ocular_mesh: bpy.types.Object,
) -> tuple[float, float, float, float, int, Vector]:
    """Project the physical inner/rear ocular rim from the exact source proxy."""

    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = ocular_mesh.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
    try:
        center_x = sum(float(vertex.co.x) for vertex in mesh.vertices) / len(mesh.vertices)
        center_z = sum(float(vertex.co.z) for vertex in mesh.vertices) / len(mesh.vertices)
        radial_values = [
            math.hypot(float(vertex.co.x) - center_x, float(vertex.co.z) - center_z)
            for vertex in mesh.vertices
            if math.hypot(float(vertex.co.x) - center_x, float(vertex.co.z) - center_z)
            > 0.020
        ]
        if not radial_values:
            raise RuntimeError("Scope ocular has no physical inner-rim vertices")
        inner_radius = min(radial_values)
        inner_vertices = [
            vertex
            for vertex in mesh.vertices
            if abs(
                math.hypot(
                    float(vertex.co.x) - center_x,
                    float(vertex.co.z) - center_z,
                )
                - inner_radius
            )
            <= 1.0e-5
        ]
        if len(inner_vertices) < 32:
            raise RuntimeError("Scope ocular inner rim is undersampled")
        projected = [
            world_to_camera_view(scene, camera, evaluated.matrix_world @ vertex.co)
            for vertex in inner_vertices
        ]
        projected = [point for point in projected if point.z > 0.0]
        if len(projected) < 32:
            raise RuntimeError("Scope ocular inner rim is behind the review camera")
        rear_coordinate = min(float(vertex.co.y) for vertex in inner_vertices)
        rear_vertices = [
            evaluated.matrix_world @ vertex.co
            for vertex in inner_vertices
            if abs(float(vertex.co.y) - rear_coordinate) <= 1.0e-5
        ]
        if not rear_vertices:
            raise RuntimeError("Scope ocular rear aperture could not be resolved")
        rear_center = sum(rear_vertices, Vector()) / len(rear_vertices)
        return (
            min(point.x for point in projected),
            max(point.x for point in projected),
            min(point.y for point in projected),
            max(point.y for point in projected),
            len(projected),
            rear_center,
        )
    finally:
        evaluated.to_mesh_clear()


def exact_proxy_distance(
    proxy: bpy.types.Object,
    production_renderer: bpy.types.Object,
) -> float:
    """Prove that a hidden authoring proxy coincides with visible LOD0 geometry."""

    from mathutils.kdtree import KDTree  # type: ignore

    depsgraph = bpy.context.evaluated_depsgraph_get()
    production = production_renderer.evaluated_get(depsgraph)
    production_mesh = production.to_mesh(
        preserve_all_data_layers=False, depsgraph=depsgraph
    )
    proxy_evaluated = proxy.evaluated_get(depsgraph)
    proxy_mesh = proxy_evaluated.to_mesh(
        preserve_all_data_layers=False, depsgraph=depsgraph
    )
    try:
        tree = KDTree(len(production_mesh.vertices))
        for index, vertex in enumerate(production_mesh.vertices):
            tree.insert(production.matrix_world @ vertex.co, index)
        tree.balance()
        maximum = 0.0
        for vertex in proxy_mesh.vertices:
            _coordinate, _index, distance = tree.find(
                proxy_evaluated.matrix_world @ vertex.co
            )
            maximum = max(maximum, float(distance))
        return maximum
    finally:
        production.to_mesh_clear()
        proxy_evaluated.to_mesh_clear()


def assert_projected_weapon_visible(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    renderers: tuple[bpy.types.Object, ...],
    label: str,
) -> dict[str, float]:
    minimum_x, maximum_x, minimum_y, maximum_y, sample_count = projected_renderer_bounds(
        scene, camera, renderers
    )
    width = maximum_x - minimum_x
    height = maximum_y - minimum_y
    if width < 0.025 or height < 0.025:
        raise RuntimeError(
            f"{label}: projected weapon coverage is too small/offscreen "
            f"({width:.4f} x {height:.4f})"
        )
    if max(width, height) < 0.50:
        raise RuntimeError(
            f"{label}: weapon occupancy {max(width, height):.4f} is below 0.50"
        )
    if (
        minimum_x < 0.05
        or maximum_x > 0.95
        or minimum_y < 0.05
        or maximum_y > 0.95
    ):
        raise RuntimeError(
            f"{label}: weapon bounds are clipped/off-center "
            f"x=[{minimum_x:.4f},{maximum_x:.4f}] "
            f"y=[{minimum_y:.4f},{maximum_y:.4f}]"
        )
    if "neutral" in label:
        if label.endswith("neutral_front.png"):
            occupancy = max(width, height)
            minimum_occupancy = 0.50
        else:
            occupancy = width
            minimum_occupancy = 0.72 if label.endswith("neutral_side.png") else 0.64
        if not minimum_occupancy <= occupancy <= 0.90:
            raise RuntimeError(
                f"{label}: rifle-only neutral occupancy {occupancy:.4f} is outside "
                f"{minimum_occupancy:.2f}..0.90"
            )
    return {
        "evidence_kind": "weapon_bounds_5_95",
        "viewport_min_x": round(minimum_x, 6),
        "viewport_max_x": round(maximum_x, 6),
        "viewport_min_y": round(minimum_y, 6),
        "viewport_max_y": round(maximum_y, 6),
        "viewport_width": round(width, 6),
        "viewport_height": round(height, 6),
        "weapon_bounds_sample_count": int(sample_count),
    }


def assert_projected_suit_context_visible(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    renderers: tuple[bpy.types.Object, ...],
    label: str,
    *,
    minimum_occupancy: float = 0.20,
    minimum_minor_axis: float = 0.08,
    minimum_visible_samples: int = 24,
) -> dict[str, float | int | str]:
    """Fail closed unless meaningful sampled LOD0 suit context is on screen."""

    if not renderers:
        raise RuntimeError(f"{label}: no LOD0 suit renderers were supplied")
    metrics = projected_context_metrics(scene, camera, renderers)
    width = float(metrics["context_viewport_width"])
    height = float(metrics["context_viewport_height"])
    visible_samples = int(metrics["context_visible_sample_count"])
    if (
        max(width, height) < minimum_occupancy
        or min(width, height) < minimum_minor_axis
        or visible_samples < minimum_visible_samples
    ):
        raise RuntimeError(
            f"{label}: projected suit context is insufficient "
            f"({width:.4f} x {height:.4f}, {visible_samples} visible samples)"
        )
    return {
        "context_evidence_kind": "suit_lod0_samples_inside_2_98",
        **metrics,
    }


def fit_review_camera(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    renderers: tuple[bpy.types.Object, ...],
    center: Vector,
    label: str,
    *,
    occupancy_axis: str = "max",
    target_occupancy: float = 0.76,
) -> None:
    """Fit evaluated evidence geometry inside a deterministic safe border."""

    if occupancy_axis not in {"max", "width"}:
        raise ValueError(f"Unsupported occupancy axis: {occupancy_axis}")
    camera.data.shift_x = 0.0
    camera.data.shift_y = 0.0
    last_measurement = None
    for _attempt in range(12):
        point_at(camera, center)
        bpy.context.view_layer.update()
        minimum_x, maximum_x, minimum_y, maximum_y, _sample_count = projected_renderer_bounds(
            scene, camera, renderers
        )
        width = maximum_x - minimum_x
        height = maximum_y - minimum_y
        occupancy = width if occupancy_axis == "width" else max(width, height)
        center_x = 0.5 * (minimum_x + maximum_x)
        center_y = 0.5 * (minimum_y + maximum_y)
        last_measurement = (
            minimum_x,
            maximum_x,
            minimum_y,
            maximum_y,
            occupancy,
            float(camera.data.shift_x),
            float(camera.data.shift_y),
        )
        if abs(occupancy - target_occupancy) <= 0.025 and all(
            0.05 <= value <= 0.95
            for value in (minimum_x, maximum_x, minimum_y, maximum_y)
        ):
            return
        # Looking at an AABB midpoint does not perfectly center a long weapon
        # in perspective when its muzzle and stock have different depths.
        # Camera shift corrects that projection error without changing the
        # authored viewing direction or introducing a hand-tuned target.
        camera.data.shift_x += (center_x - 0.5) * 0.85
        camera.data.shift_y += (center_y - 0.5) * 0.85
        camera.data.shift_x = max(-0.35, min(0.35, camera.data.shift_x))
        camera.data.shift_y = max(-0.35, min(0.35, camera.data.shift_y))
        scale = max(0.45, min(2.25, occupancy / target_occupancy))
        camera.location = center + (camera.location - center) * scale
    raise RuntimeError(
        f"{label}: review camera fit did not converge; last={last_measurement}"
    )


def fit_context_review_camera(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    weapon_renderers: tuple[bpy.types.Object, ...],
    context_renderers: tuple[bpy.types.Object, ...],
    weapon_center: Vector,
    initial_direction: Vector,
    initial_distance: float,
    label: str,
    *,
    target_weapon_occupancy: float = 0.54,
    minimum_weapon_occupancy: float = 0.50,
) -> None:
    """Find a weapon-led view that also proves the guided weapon's suit origin.

    The guided Draw midpoint can place the rifle far enough from its carrier
    that centering the weapon alone removes the suit from frame.  Search a
    small deterministic set of foreground sightlines derived from the actual
    evaluated weapon/suit separation.  Every candidate is still fitted by the
    normal weapon fitter; a candidate is accepted only when both the existing
    5%-95% weapon border and explicit LOD0 suit-context evidence pass.
    """

    if not context_renderers:
        raise RuntimeError(f"{label}: context camera requires LOD0 suit renderers")
    if initial_direction.length <= 1.0e-6 or initial_distance <= 1.0e-6:
        raise RuntimeError(f"{label}: context camera has no usable initial sightline")
    if target_weapon_occupancy - 0.025 < minimum_weapon_occupancy:
        raise ValueError(
            f"{label}: target occupancy does not guarantee the minimum weapon occupancy"
        )

    context_minimum, context_maximum = evaluated_bounds(context_renderers)
    context_center = context_minimum.lerp(context_maximum, 0.5)
    weapon_foreground_direction = weapon_center - context_center
    if weapon_foreground_direction.length <= 1.0e-6:
        raise RuntimeError(f"{label}: weapon and suit context centers coincide")
    base_direction = initial_direction.normalized()
    foreground_direction = weapon_foreground_direction.normalized()

    def blended_direction(weight: float) -> Vector | None:
        candidate = base_direction * (1.0 - weight) + foreground_direction * weight
        return candidate.normalized() if candidate.length > 1.0e-6 else None

    world_up = Vector((0.0, 0.0, 1.0))
    lateral = world_up.cross(foreground_direction)
    if lateral.length <= 1.0e-6:
        lateral = Vector((1.0, 0.0, 0.0))
    else:
        lateral.normalize()
    directions = [
        blended_direction(0.0),
        blended_direction(0.35),
        blended_direction(0.65),
        blended_direction(0.85),
        foreground_direction,
        (foreground_direction + lateral * 0.25 + world_up * 0.08).normalized(),
        (foreground_direction - lateral * 0.25 + world_up * 0.08).normalized(),
        (foreground_direction + lateral * 0.45).normalized(),
        (foreground_direction - lateral * 0.45).normalized(),
    ]

    best_state = None
    best_score = None
    last_error = None
    for order, direction in enumerate(item for item in directions if item is not None):
        camera.location = weapon_center + direction * initial_distance
        try:
            fit_review_camera(
                scene,
                camera,
                weapon_renderers,
                weapon_center,
                label,
                occupancy_axis="max",
                target_occupancy=target_weapon_occupancy,
            )
            weapon_min_x, weapon_max_x, weapon_min_y, weapon_max_y, _ = (
                projected_renderer_bounds(scene, camera, weapon_renderers)
            )
            weapon_occupancy = max(
                weapon_max_x - weapon_min_x,
                weapon_max_y - weapon_min_y,
            )
            context = projected_context_metrics(scene, camera, context_renderers)
            context_width = float(context["context_viewport_width"])
            context_height = float(context["context_viewport_height"])
            visible_samples = int(context["context_visible_sample_count"])
            valid = (
                weapon_occupancy >= minimum_weapon_occupancy
                and min(
                    weapon_min_x,
                    weapon_min_y,
                    1.0 - weapon_max_x,
                    1.0 - weapon_max_y,
                )
                >= 0.05
                and max(context_width, context_height) >= 0.20
                and min(context_width, context_height) >= 0.08
                and visible_samples >= 24
            )
            score = (
                int(valid),
                min(context_width, context_height),
                max(context_width, context_height),
                visible_samples,
                -order,
            )
            if best_score is None or score > best_score:
                best_score = score
                best_state = (
                    camera.matrix_world.copy(),
                    float(camera.data.shift_x),
                    float(camera.data.shift_y),
                )
        except RuntimeError as error:
            last_error = error

    if best_state is None:
        raise RuntimeError(
            f"{label}: no context-aware camera candidate could be fitted; last={last_error}"
        )
    camera.matrix_world = best_state[0]
    camera.data.shift_x = best_state[1]
    camera.data.shift_y = best_state[2]
    bpy.context.view_layer.update()
    assert_projected_weapon_visible(scene, camera, weapon_renderers, label)
    assert_projected_suit_context_visible(scene, camera, context_renderers, label)


def fit_scope_camera(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    ocular: bpy.types.Object,
    target: Vector,
    *,
    target_aperture_radius: float = 0.28,
) -> None:
    """Fit the real ocular mesh without backing away from the eye box."""

    camera.data.lens = 5.0
    camera.data.shift_x = 0.0
    camera.data.shift_y = 0.0
    ocular_mesh = bpy.data.objects.get("NGPR_OpticOcular")
    if ocular_mesh is None or ocular_mesh.type != "MESH":
        raise RuntimeError("Scope camera fit requires NGPR_OpticOcular")
    for _attempt in range(8):
        point_at(camera, target)
        bpy.context.view_layer.update()
        minimum_x, maximum_x, minimum_y, maximum_y, _samples = projected_renderer_bounds(
            scene, camera, (ocular_mesh,)
        )
        projected_center = Vector((
            0.5 * (minimum_x + maximum_x),
            0.5 * (minimum_y + maximum_y),
            1.0,
        ))
        radius_x = 0.5 * (maximum_x - minimum_x)
        radius_y = 0.5 * (maximum_y - minimum_y)
        radius = 0.5 * (radius_x + radius_y)
        if (
            abs(radius - target_aperture_radius) <= 0.005
            and abs(float(projected_center.x) - 0.5) <= 0.01
            and abs(float(projected_center.y) - 0.5) <= 0.01
        ):
            return
        if radius <= 1.0e-6:
            raise RuntimeError("Scope aperture collapsed during camera fitting")
        camera.data.lens = max(
            1.0,
            min(20.0, float(camera.data.lens) * target_aperture_radius / radius),
        )
    raise RuntimeError("Scope ocular camera fit did not converge")


def assert_scope_target_visible(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    ocular: bpy.types.Object,
    ocular_mesh: bpy.types.Object,
    rifle_lod0: bpy.types.Object,
    target_root: bpy.types.Object,
    target_objects: tuple[bpy.types.Object, ...],
    sight_picture_objects: tuple[bpy.types.Object, ...],
    label: str,
) -> dict[str, object]:
    target = target_root.matrix_world.translation
    projected_center = world_to_camera_view(scene, camera, target)
    if projected_center.z <= 0.0 or not (
        0.47 <= projected_center.x <= 0.53
        and 0.47 <= projected_center.y <= 0.53
    ):
        raise RuntimeError(
            f"{label}: scope target is not centered "
            f"({projected_center.x:.4f}, {projected_center.y:.4f})"
        )
    minimum_x, maximum_x, minimum_y, maximum_y, _sample_count = projected_renderer_bounds(
        scene, camera, target_objects
    )
    target_width = maximum_x - minimum_x
    target_height = maximum_y - minimum_y
    if (
        minimum_x < 0.08
        or maximum_x > 0.92
        or minimum_y < 0.08
        or maximum_y > 0.92
        or max(target_width, target_height) < 0.24
    ):
        raise RuntimeError(
            f"{label}: scope target must be clearly readable and unclipped; "
            f"bounds x=[{minimum_x:.4f},{maximum_x:.4f}] "
            f"y=[{minimum_y:.4f},{maximum_y:.4f}]"
        )
    sight_min_x, sight_max_x, sight_min_y, sight_max_y, sight_samples = (
        projected_renderer_bounds(scene, camera, sight_picture_objects)
    )
    sight_width = sight_max_x - sight_min_x
    sight_height = sight_max_y - sight_min_y
    if (
        sight_min_x < 0.10
        or sight_max_x > 0.90
        or sight_min_y < 0.10
        or sight_max_y > 0.90
        or min(sight_width, sight_height) < 0.22
    ):
        raise RuntimeError(
            f"{label}: projected optical sight picture is unreadable or clipped"
        )
    sight_basis = ocular.matrix_world.to_3x3().normalized()
    sight_forward = (sight_basis @ Vector((0.0, 1.0, 0.0))).normalized()
    (
        aperture_min_x,
        aperture_max_x,
        aperture_min_y,
        aperture_max_y,
        aperture_samples,
        rear_aperture_center,
    ) = projected_inner_rim_bounds(scene, camera, ocular_mesh)
    aperture_center_x = 0.5 * (aperture_min_x + aperture_max_x)
    aperture_center_y = 0.5 * (aperture_min_y + aperture_max_y)
    aperture_radius_x = 0.5 * (aperture_max_x - aperture_min_x)
    aperture_radius_y = 0.5 * (aperture_max_y - aperture_min_y)
    if aperture_samples < 32:
        raise RuntimeError(f"{label}: real ocular aperture evidence is undersampled")
    proxy_distance = exact_proxy_distance(ocular_mesh, rifle_lod0)
    if proxy_distance > 1.0e-5:
        raise RuntimeError(
            f"{label}: ocular proxy differs from visible LOD0 by "
            f"{proxy_distance:.9f} m"
        )

    reticle_objects = tuple(
        obj
        for obj in sight_picture_objects
        if obj.name
        in {
            "NGPR_ScopeSightUp",
            "NGPR_ScopeSightDown",
            "NGPR_ScopeSightLeft",
            "NGPR_ScopeSightRight",
        }
    )
    range_objects = tuple(
        obj for obj in sight_picture_objects
        if obj.name.startswith("NGPR_ScopeSightRange")
    )
    if len(reticle_objects) != 4 or len(range_objects) != 4:
        raise RuntimeError(f"{label}: scope reticle/range object contract changed")
    if any(obj.hide_render or obj.hide_get() for obj in (*reticle_objects, *range_objects)):
        raise RuntimeError(f"{label}: scope reticle/range geometry is not render-visible")
    reticle_min_x, reticle_max_x, reticle_min_y, reticle_max_y, reticle_samples = (
        projected_renderer_bounds(scene, camera, reticle_objects)
    )
    reticle_center_x = 0.5 * (reticle_min_x + reticle_max_x)
    reticle_center_y = 0.5 * (reticle_min_y + reticle_max_y)
    if (
        reticle_samples < 32
        or abs(reticle_center_x - 0.5) > 0.03
        or abs(reticle_center_y - 0.5) > 0.03
    ):
        raise RuntimeError(f"{label}: rendered reticle is not centered/readable")

    nested_occluders: set[str] = set()
    first_hits: list[str] = []
    target_names = {obj.name for obj in target_objects}
    target_basis = target_root.matrix_world.to_3x3().normalized()
    target_right = (target_basis @ Vector((1.0, 0.0, 0.0))).normalized()
    target_up = (target_basis @ Vector((0.0, 0.0, 1.0))).normalized()
    previous_hidden = [bool(obj.hide_get()) for obj in sight_picture_objects]
    try:
        # The focal-plane reticle visualizes magnification but must not satisfy
        # the physical clear-bore test. Hide it only while ray-casting through
        # the real optic to the independent six-metre target.
        for sight_object in sight_picture_objects:
            sight_object.hide_set(True)
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        for offset_x, offset_y in (
            (0.0, 0.0),
            (-0.10, 0.0),
            (0.10, 0.0),
            (0.0, -0.10),
            (0.0, 0.10),
        ):
            ray_target = target + target_right * offset_x + target_up * offset_y
            direction = ray_target - camera.location
            distance = direction.length
            hit, _location, _normal, _face, hit_object, _matrix = scene.ray_cast(
                depsgraph,
                camera.location,
                direction.normalized(),
                distance=distance + 0.25,
            )
            if hit and hit_object is not None:
                first_hits.append(hit_object.name)
            hit_name = hit_object.name if hit_object is not None else "none"
            if not hit or hit_name not in target_names:
                nested_occluders.add(
                    hit_name
                )
    finally:
        for sight_object, hidden in zip(sight_picture_objects, previous_hidden):
            sight_object.hide_set(hidden)
        bpy.context.view_layer.update()
    if nested_occluders:
        raise RuntimeError(
            f"{label}: optic corridor is occluded by {sorted(nested_occluders)}"
        )
    return {
        "evidence_kind": "ocular_corridor",
        "camera_to_ocular_rear_m": round(
            float((camera.location - rear_aperture_center).length), 6
        ),
        "aperture_center_x": round(aperture_center_x, 6),
        "aperture_center_y": round(aperture_center_y, 6),
        "aperture_radius_x": round(aperture_radius_x, 6),
        "aperture_radius_y": round(aperture_radius_y, 6),
        "reticle_center_x": round(reticle_center_x, 6),
        "reticle_center_y": round(reticle_center_y, 6),
        "target_center_x": round(float(projected_center.x), 6),
        "target_center_y": round(float(projected_center.y), 6),
        "target_distance_m": round(
            float((target - ocular.matrix_world.translation).length), 6
        ),
        "target_viewport_width": round(target_width, 6),
        "target_viewport_height": round(target_height, 6),
        "sight_picture_viewport_width": round(sight_width, 6),
        "sight_picture_viewport_height": round(sight_height, 6),
        "sight_picture_sample_count": int(sight_samples),
        "corridor_clear": True,
        "objective_visible": not nested_occluders and aperture_samples >= 32,
        "reticle_visible": reticle_samples >= 32,
        "target_visible": not nested_occluders and len(first_hits) == 5,
        "studio_ground_visible": False,
        "nested_occluder_count": 0,
        "reticle_line_count": len(reticle_objects),
        "range_tick_count": len(range_objects),
        "sampled_first_hits": sorted(first_hits),
        "aperture_object": ocular_mesh.name,
        "aperture_geometry_source": "exact_source_proxy_inner_rim",
        "aperture_proxy_max_distance_m": round(proxy_distance, 9),
        "aperture_sample_count": int(aperture_samples),
    }


def evaluated_bounds(renderers: tuple[bpy.types.Object, ...]) -> tuple[Vector, Vector]:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = []
    for source in renderers:
        evaluated = source.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
        try:
            points.extend(evaluated.matrix_world @ vertex.co for vertex in mesh.vertices)
        finally:
            evaluated.to_mesh_clear()
    if not points:
        raise RuntimeError("Weapon renderers have no evaluated vertices")
    return (
        Vector(tuple(min(point[axis] for point in points) for axis in range(3))),
        Vector(tuple(max(point[axis] for point in points) for axis in range(3))),
    )


def assert_helpers_match_evaluated_weapon(
    renderers: tuple[bpy.types.Object, ...], label: str
) -> float:
    minimum, maximum = evaluated_bounds(renderers)
    maximum_distance = 0.0
    # Small surface margins are allowed because helpers intentionally sit on or
    # just beyond contact surfaces. Large mismatches still catch carrier-space
    # regressions. The muzzle has a slightly wider brake-front margin.
    for name in (
        "Rifle_PrimaryGrip",
        "Rifle_SupportGripTarget",
        "Rifle_StockContact",
        "Rifle_SightOcular",
        "Rifle_Muzzle",
    ):
        point = bpy.data.objects[name].matrix_world.translation
        squared = 0.0
        for axis in range(3):
            if point[axis] < minimum[axis]:
                squared += (minimum[axis] - point[axis]) ** 2
            elif point[axis] > maximum[axis]:
                squared += (point[axis] - maximum[axis]) ** 2
        distance = math.sqrt(squared)
        if name == "Rifle_Muzzle":
            distance = max(0.0, distance - 0.14)
        maximum_distance = max(maximum_distance, distance)
    if maximum_distance > 0.05:
        raise RuntimeError(
            f"{label}: hardpoints are {maximum_distance:.4f} m outside the "
            "evaluated production-renderer envelope"
        )
    return maximum_distance


def _matrix_maximum_delta(first: Matrix, second: Matrix) -> float:
    return max(
        abs(float(first[row][column] - second[row][column]))
        for row in range(4)
        for column in range(4)
    )


def validate_weapon_skin_motion(
    armature: bpy.types.Object,
    renderers: list[bpy.types.Object],
    lod0_renderers: tuple[bpy.types.Object, bpy.types.Object],
) -> dict[str, object]:
    """Compare evaluated skinning with the explicit pose/rest bind equation."""

    sample_frames = (
        ("PS_Aim", 1),
        ("PS_WeaponReady_Idle", 1),
        ("PS_WeaponStowed_Idle", 1),
        ("PS_Reload", 50),
        ("PS_BoltCycle", 12),
    )
    original_action = (
        armature.animation_data.action
        if armature.animation_data is not None else None
    )
    original_frame = int(bpy.context.scene.frame_current)
    samples = {}
    try:
        for action_name, frame in sample_frames:
            activate_action(armature, action_name)
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            renderer_errors = {}
            for obj in renderers:
                evaluated = obj.evaluated_get(depsgraph)
                mesh = evaluated.to_mesh(
                    preserve_all_data_layers=False, depsgraph=depsgraph
                )
                try:
                    if len(mesh.vertices) != len(obj.data.vertices):
                        raise RuntimeError(
                            f"{obj.name} armature evaluation changed vertex count"
                        )
                    maximum_error = 0.0
                    for source_vertex, result_vertex in zip(
                        obj.data.vertices, mesh.vertices
                    ):
                        assignments = list(source_vertex.groups)
                        if len(assignments) != 1:
                            raise RuntimeError(
                                f"{obj.name} vertex {source_vertex.index} lost rigid weighting"
                            )
                        group_name = obj.vertex_groups[assignments[0].group].name
                        bone = armature.data.bones[group_name]
                        pose_bone = armature.pose.bones[group_name]
                        expected = (
                            armature.matrix_world
                            @ pose_bone.matrix
                            @ bone.matrix_local.inverted_safe()
                            @ source_vertex.co
                        )
                        actual = evaluated.matrix_world @ result_vertex.co
                        maximum_error = max(
                            maximum_error, float((actual - expected).length)
                        )
                    if maximum_error > 0.001:
                        raise RuntimeError(
                            f"{action_name} frame {frame}: {obj.name} evaluated "
                            f"skinning differs from pose/rest bind by {maximum_error:.9f} m"
                        )
                    renderer_errors[obj.name] = round(maximum_error, 9)
                finally:
                    evaluated.to_mesh_clear()
            helper_error = assert_helpers_match_evaluated_weapon(
                lod0_renderers, f"{action_name}@{frame}"
            )
            if helper_error > 0.001:
                raise RuntimeError(
                    f"{action_name} frame {frame}: helper alignment exceeds 1 mm"
                )
            samples[f"{action_name}@{frame}"] = {
                "maximum_manual_skin_error_m": max(renderer_errors.values()),
                "helper_envelope_error_m": round(helper_error, 9),
                "per_renderer_error_m": renderer_errors,
            }

        def pose(action_name: str, frame: int, bone_name: str) -> Matrix:
            activate_action(armature, action_name)
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            return armature.pose.bones[bone_name].matrix.copy()

        ready = pose("PS_WeaponReady_Idle", 1, "WeaponRoot")
        stowed = pose("PS_WeaponStowed_Idle", 1, "WeaponRoot")
        draw_start = pose("PS_Weapon_Draw", 1, "WeaponRoot")
        draw_mid = pose("PS_Weapon_Draw", 18, "WeaponRoot")
        draw_end = pose("PS_Weapon_Draw", 30, "WeaponRoot")
        sheathe_start = pose("PS_Weapon_Sheathe", 1, "WeaponRoot")
        sheathe_mid = pose("PS_Weapon_Sheathe", 13, "WeaponRoot")
        sheathe_end = pose("PS_Weapon_Sheathe", 30, "WeaponRoot")
        transition_errors = {
            "draw_start_to_stowed": _matrix_maximum_delta(draw_start, stowed),
            "draw_end_to_ready": _matrix_maximum_delta(draw_end, ready),
            "sheathe_start_to_ready": _matrix_maximum_delta(sheathe_start, ready),
            "sheathe_end_to_stowed": _matrix_maximum_delta(sheathe_end, stowed),
            "shared_midpoint": _matrix_maximum_delta(draw_mid, sheathe_mid),
        }
        if max(transition_errors.values()) > 1.0e-4:
            raise RuntimeError(
                "Candidate007 draw/sheathe endpoints are not reversible: "
                + json.dumps(transition_errors, sort_keys=True)
            )
        midpoint_separation = {
            "from_ready": _matrix_maximum_delta(draw_mid, ready),
            "from_stowed": _matrix_maximum_delta(draw_mid, stowed),
            "ready_from_stowed": _matrix_maximum_delta(ready, stowed),
        }
        if min(midpoint_separation.values()) <= 0.01:
            raise RuntimeError(
                "Candidate007 draw/sheathe motion collapsed to a static pose"
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
        bolt_start = component_relative("PS_BoltCycle", 1, "WeaponBolt")
        bolt_travel = component_relative("PS_BoltCycle", 12, "WeaponBolt")
        bolt_end = component_relative("PS_BoltCycle", 20, "WeaponBolt")
        articulation = {
            "magazine_travel_m": float(
                (magazine_travel.translation - magazine_start.translation).length
            ),
            "magazine_return_error": _matrix_maximum_delta(
                magazine_start, magazine_end
            ),
            "bolt_travel_m": float(
                (bolt_travel.translation - bolt_start.translation).length
            ),
            "bolt_return_error": _matrix_maximum_delta(bolt_start, bolt_end),
        }
        if articulation["magazine_travel_m"] < 0.25:
            raise RuntimeError("Candidate007 magazine travel is below 0.25 m")
        if articulation["bolt_travel_m"] < 0.08:
            raise RuntimeError("Candidate007 bolt travel is below 0.08 m")
        if max(
            articulation["magazine_return_error"],
            articulation["bolt_return_error"],
        ) > 1.0e-4:
            raise RuntimeError("Candidate007 articulation does not return to rest")
        return {
            "schema_version": 1,
            "manual_skin_tolerance_m": 0.001,
            "samples": samples,
            "draw_sheathe_endpoint_errors": {
                key: round(value, 9) for key, value in transition_errors.items()
            },
            "draw_sheathe_midpoint_separation": {
                key: round(value, 9) for key, value in midpoint_separation.items()
            },
            "articulation": {
                key: round(value, 9) for key, value in articulation.items()
            },
        }
    finally:
        if original_action is not None:
            activate_action(armature, original_action)
        bpy.context.scene.frame_set(original_frame)
        bpy.context.view_layer.update()


def render_reviews(
    armature: bpy.types.Object,
    rifle_lod0: bpy.types.Object,
    optic_lod0: bpy.types.Object,
    lights,
    camera,
    ground: bpy.types.Object,
    target_root: bpy.types.Object,
    target_objects: tuple[bpy.types.Object, ...],
    sight_picture_root: bpy.types.Object,
    sight_picture_objects: tuple[bpy.types.Object, ...],
) -> list[Path]:
    scene = bpy.context.scene
    visibility_snapshot = {
        obj.name: (bool(obj.hide_render), bool(obj.hide_viewport), bool(obj.hide_get()))
        for obj in bpy.data.objects
    }
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 960
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    if background:
        background.inputs["Color"].default_value = (0.015, 0.019, 0.027, 1.0)
        background.inputs["Strength"].default_value = 0.25
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = -0.85
    RENDER_ROOT.mkdir(parents=True, exist_ok=True)
    rifle_lod0.hide_render = optic_lod0.hide_render = False
    rifle_lod0.hide_set(False)
    optic_lod0.hide_set(False)
    for obj in bpy.data.objects:
        if obj.get("weapon_v3_lod") is not None and int(obj["weapon_v3_lod"]) != 0:
            obj.hide_render = True
    armature.data.pose_position = "POSE"

    # Neutral jobs use a world-space clone placement so the rifle is readable
    # without changing the frozen authored/hardpoint definition.
    jobs = [
        (RENDER_NAMES[0], None, 1, Vector((0.0, 3.35, 1.10)), Vector((0.0, 0.22, 1.15)), 67.0),
        (RENDER_NAMES[1], None, 1, Vector((3.75, 0.20, 1.10)), Vector((0.0, 0.20, 1.15)), 72.0),
        (RENDER_NAMES[2], None, 1, Vector((2.55, 3.15, 1.45)), Vector((0.0, 0.20, 1.15)), 70.0),
        (RENDER_NAMES[3], "PS_Aim", 1, Vector((-2.10, 4.20, 1.50)), Vector((0.0, 0.05, 1.22)), 74.0),
        (RENDER_NAMES[4], "PS_WeaponReady_Idle", 1, Vector((2.35, 4.15, 1.42)), Vector((0.0, 0.02, 1.15)), 72.0),
        (RENDER_NAMES[5], "PS_Aim", 1, Vector((0.0, -0.20, 1.90)), Vector((0.0, 1.0, 1.90)), 76.0),
        (RENDER_NAMES[6], "PS_Reload", 50, Vector((-2.10, 4.15, 1.42)), Vector((0.0, 0.03, 1.12)), 74.0),
        (RENDER_NAMES[7], "PS_BoltCycle", 12, Vector((-2.15, 4.05, 1.52)), Vector((0.0, 0.03, 1.30)), 76.0),
        (RENDER_NAMES[8], "PS_Run_Forward", 6, Vector((2.45, 4.35, 1.42)), Vector((0.0, 0.0, 1.10)), 72.0),
        (RENDER_NAMES[9], "PS_Hover", 1, Vector((2.45, 4.35, 1.55)), Vector((0.0, 0.0, 1.16)), 72.0),
        (RENDER_NAMES[10], "PS_WeaponStowed_Idle", 1, Vector((-2.45, -4.35, 1.42)), Vector((0.0, -0.08, 1.15)), 72.0),
        (RENDER_NAMES[11], "PS_Weapon_Draw", 18, Vector((-2.40, -4.10, 1.45)), Vector((0.0, -0.02, 1.16)), 72.0),
        (RENDER_NAMES[12], "PS_Weapon_Sheathe", 3, Vector((-2.40, -4.10, 1.45)), Vector((0.0, -0.02, 1.16)), 72.0),
    ]
    # Neutral views use the ready pose because the weapon is carried by the
    # armature; the framing is intentionally tight on the asset.
    paths = []
    projection_evidence = {}
    suit_renderers = [
        obj for obj in bpy.data.objects
        if obj.type == "MESH"
        and obj.get("hero_v2_asset") == "suit"
        and int(obj.get("hero_v2_lod", -1)) == 0
    ]
    studio_grounds = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH"
        and bool(obj.get("aegis_studio_only"))
        and "Ground" in obj.name
    ]
    try:
        for filename, action_name, frame, location, target, lens in jobs:
            activate_action(armature, action_name or "PS_WeaponReady_Idle")
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            is_neutral = action_name is None
            is_scope = filename == "nextgen_precision_rifle_scope_ocular.png"
            for suit_renderer in suit_renderers:
                suit_renderer.hide_render = is_neutral or is_scope
            # Floor planes from both the inherited suit-review studio and the
            # rifle studio obscure the lower silhouette and create a product-
            # turntable look.  The weapon review deliberately uses a clean
            # black field for every evidence frame.
            for studio_ground in studio_grounds:
                studio_ground.hide_render = True
            for target_object in target_objects:
                target_object.hide_render = not is_scope
            for sight_object in sight_picture_objects:
                sight_object.hide_render = not is_scope
            if is_neutral:
                minimum, maximum = evaluated_bounds((rifle_lod0, optic_lod0))
                center = minimum.lerp(maximum, 0.5)
                root = bpy.data.objects["RifleRoot"]
                basis = root.matrix_world.to_3x3()
                right = (basis @ Vector((1.0, 0.0, 0.0))).normalized()
                forward = (basis @ Vector((0.0, 1.0, 0.0))).normalized()
                up = (basis @ Vector((0.0, 0.0, 1.0))).normalized()
                extent = (maximum - minimum).length
                if filename == "nextgen_precision_rifle_neutral_front.png":
                    direction = forward
                elif filename == "nextgen_precision_rifle_neutral_side.png":
                    direction = right
                else:
                    direction = (right * 0.72 + forward * 0.62 + up * 0.20).normalized()
                location = center + direction * max(1.10, extent * 0.92)
                target = center
                lens = 72.0
            elif is_scope:
                ocular = bpy.data.objects["Rifle_SightOcular"]
                ocular_mesh = bpy.data.objects.get("NGPR_OpticOcular")
                if ocular_mesh is None or ocular_mesh.type != "MESH":
                    raise RuntimeError("Scope review requires NGPR_OpticOcular")
                sight_basis = ocular.matrix_world.to_3x3().normalized()
                sight_forward = (
                    sight_basis @ Vector((0.0, 1.0, 0.0))
                ).normalized()
                location = ocular.matrix_world.translation - sight_forward * 0.012
                target = ocular.matrix_world.translation + sight_forward * 6.0
                target_root.matrix_world = (
                    Matrix.Translation(target) @ sight_basis.to_4x4()
                )
                # Maintain the required six-metre target distance while
                # scaling the studio-only board to a clearly readable angular
                # size.  This simulates a large downrange range target rather
                # than a tiny decal and leaves the weapon geometry untouched.
                target_root.scale = (10.0, 10.0, 10.0)
                sight_picture_root.matrix_world = (
                    Matrix.Translation(
                        ocular.matrix_world.translation + sight_forward * 0.015
                    )
                    @ sight_basis.to_4x4()
                )
                lens = 5.0
            else:
                minimum, maximum = evaluated_bounds((rifle_lod0, optic_lod0))
                weapon_center = minimum.lerp(maximum, 0.5)
                view_direction = location - target
                if view_direction.length <= 1.0e-6:
                    raise RuntimeError(f"{filename}: review camera direction collapsed")
                location = weapon_center + view_direction.normalized() * view_direction.length
                target = weapon_center
            camera.location = location
            camera.data.lens = lens
            camera.data.clip_start = 0.001 if is_scope else 0.05
            point_at(camera, target)
            if is_neutral:
                fit_review_camera(
                    scene,
                    camera,
                    (rifle_lod0, optic_lod0),
                    target,
                    filename,
                    occupancy_axis=(
                        "max"
                        if filename.endswith("neutral_front.png")
                        else "width"
                    ),
                    target_occupancy=(
                        0.76
                        if filename.endswith("neutral_side.png")
                        else 0.70
                    ),
                )
            elif is_scope:
                fit_scope_camera(scene, camera, ocular, target)
            else:
                # Posed reviews are deliberately weapon-led upper-body views.
                # A full-body fit made the rifle occupy only ~28% of frame and
                # concealed grip/clearance problems behind a distant overview.
                # Keep the suit visible as pose context, but fit and center the
                # actual asset under review.
                weapon_minimum, weapon_maximum = evaluated_bounds(
                    (rifle_lod0, optic_lod0)
                )
                weapon_center = weapon_minimum.lerp(weapon_maximum, 0.5)
                direction = camera.location - target
                camera.location = (
                    weapon_center + direction.normalized() * direction.length
                )
                is_guided_draw = filename == RENDER_NAMES[11]
                if is_guided_draw:
                    fit_context_review_camera(
                        scene,
                        camera,
                        (rifle_lod0, optic_lod0),
                        tuple(suit_renderers),
                        weapon_center,
                        direction,
                        direction.length,
                        filename,
                        target_weapon_occupancy=0.54,
                        minimum_weapon_occupancy=0.50,
                    )
                else:
                    fit_review_camera(
                        scene,
                        camera,
                        (rifle_lod0, optic_lod0),
                        weapon_center,
                        filename,
                        occupancy_axis="max",
                        target_occupancy=0.68,
                    )
                target = weapon_center
            for light in lights:
                point_at(light, target)
            bpy.context.view_layer.update()
            helper_error = assert_helpers_match_evaluated_weapon(
                (rifle_lod0, optic_lod0), filename
            )
            if is_scope:
                projection_evidence[filename] = assert_scope_target_visible(
                    scene,
                    camera,
                    ocular,
                    ocular_mesh,
                    rifle_lod0,
                    target_root,
                    target_objects,
                    sight_picture_objects,
                    filename,
                )
            else:
                projection_evidence[filename] = assert_projected_weapon_visible(
                    scene, camera, (rifle_lod0, optic_lod0), filename
                )
                if filename == RENDER_NAMES[11]:
                    projection_evidence[filename].update(
                        assert_projected_suit_context_visible(
                            scene,
                            camera,
                            tuple(suit_renderers),
                            filename,
                        )
                    )
                projection_evidence[filename]["studio_ground_visible"] = bool(
                    any(not item.hide_render for item in studio_grounds)
                )
            projection_evidence[filename]["helper_envelope_error_m"] = round(helper_error, 6)
            path = RENDER_ROOT / filename
            scene.render.filepath = str(path)
            bpy.ops.render.render(write_still=True)
            if not path.is_file() or path.stat().st_size < 4096:
                raise RuntimeError(f"Review render failed: {path}")
            paths.append(path)
    finally:
        for name, (hide_render, hide_viewport, hidden) in visibility_snapshot.items():
            obj = bpy.data.objects.get(name)
            if obj is None:
                continue
            obj.hide_render = hide_render
            obj.hide_viewport = hide_viewport
            obj.hide_set(hidden)
    scene["ngpr_projection_evidence_json"] = json.dumps(
        {
            "schema_version": 4,
            "render_resolution": [
                int(scene.render.resolution_x * scene.render.resolution_percentage / 100),
                int(scene.render.resolution_y * scene.render.resolution_percentage / 100),
            ],
            "views": projection_evidence,
        },
        sort_keys=True,
        separators=(",", ":"),
    )
    return paths


def main() -> None:
    if bpy.app.version < (5, 2, 0):
        raise RuntimeError("Candidate007 requires Blender 5.2 or newer")
    current = Path(bpy.data.filepath).resolve()
    if current != SOURCE_BLEND.resolve():
        raise RuntimeError(f"Expected pinned Candidate005 source, got {current}")
    before = sha256(SOURCE_BLEND)
    report_hash = str(json.loads(SOURCE_REPORT.read_text(encoding="utf-8"))["candidate_blend_sha256"])
    if before != EXPECTED_SOURCE_SHA256 or report_hash != EXPECTED_SOURCE_SHA256:
        raise RuntimeError(f"Candidate005 hash mismatch: file={before}, report={report_hash}")
    if sha256(CONCEPT_REFERENCE) != EXPECTED_CONCEPT_SHA256:
        raise RuntimeError("Candidate007 concept reference hash mismatch")
    texture_manifest = json.loads(TEXTURE_MANIFEST.read_text(encoding="utf-8"))
    if texture_manifest.get("asset_id") != "PS_NextGenPrecisionRifle001":
        raise RuntimeError("Candidate007 preview texture source is not the pinned Candidate006 set")
    for entry in texture_manifest["maps"].values():
        path = ROOT / entry["path"]
        if sha256(path) != entry["sha256"]:
            raise RuntimeError(f"Candidate007 texture hash mismatch: {path}")

    armature = bpy.data.objects.get(ARMATURE_NAME)
    if armature is None or armature.type != "ARMATURE" or len(armature.data.bones) != 23:
        raise RuntimeError("Candidate007 requires the exact 23-bone PowerSuit_Armature")
    original_actions = {
        action.name: action_signature(action, armature)
        for action in sorted((action for action in bpy.data.actions if action.name.startswith("PS_")), key=lambda item: item.name)
    }
    if len(original_actions) != 24:
        raise RuntimeError(f"Expected 24 source actions, found {len(original_actions)}")

    # Freeze semantic source transforms while the source is in rest pose. The
    # C005 file is saved on PS_Aim, so modelling against its evaluated pose
    # would silently bake an animation frame into the rigid weapon definition.
    armature.animation_data_clear()
    armature.data.pose_position = "REST"
    for bone in armature.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()

    remove_existing_rifle()
    component_collection = ensure_collection(RIFLE_COMPONENTS)
    optic_collection = ensure_collection(OPTIC_COMPONENTS)
    mat = materials()
    root = bpy.data.objects.new("RifleRoot", None)
    component_collection.objects.link(root)
    root.matrix_world = Matrix.Identity(4)
    tag_weapon_root(root, weapon_id=ASSET_ID, stance_family="shouldered_precision")
    root["ps_generator_version"] = 6006
    root["ps_rifle_forward_axis"] = "+Y"
    root["ps_rifle_up_axis"] = "+Z"
    root["ps_stock_point_local"] = [-0.112, -0.448, 0.132]
    root["ps_muzzle_point_local"] = [0.0, 1.175, 0.145]
    root["ps_scope_point_local"] = [0.0, -0.280, 0.315]
    root["ps_primary_grip_local_m"] = [-0.085, -0.070, 0.025]
    root["ps_support_grip_local_m"] = [0.120, 0.280, 0.015]
    root["ps_support_grip_min_local_m"] = [0.097, 0.250, 0.015]
    root["ps_support_grip_max_local_m"] = [0.137, 0.315, 0.015]
    root["ps_support_grip_x_local"] = 0.120
    root["ps_scope_x_local"] = 0.0
    root["ps_candidate007_hardpoint_version"] = "NGPR002_HARDPOINTS_V2"
    parts, optics = build_components(root, component_collection, optic_collection, mat)
    apply_modifiers([*parts, *optics])

    # Freeze the editable rigid source definition before constructing render LODs.
    root.parent = None
    root.matrix_world = Matrix.Identity(4)
    normalize_rigid_weapon_children(root)
    signature = freeze_rigid_weapon(root)
    root["ps_weapon_asset_signature_short"] = signature[:16]
    validate_weapon_contract(root, require_independent=True)

    lod_collections = {lod: ensure_collection(f"WeaponV3_LOD{lod}") for lod in range(4)}
    # Renderer clones are built from copied geometry so frozen rigid source
    # children and animated magazine/bolt pieces retain their contract.
    render_sources = []
    for source in parts:
        duplicate = source.copy()
        duplicate.data = source.data.copy()
        component_collection.objects.link(duplicate)
        world = duplicate.matrix_world.copy()
        duplicate.parent = None
        duplicate.matrix_world = world
        render_sources.append(duplicate)
    optic_sources = []
    for source in optics:
        duplicate = source.copy()
        duplicate.data = source.data.copy()
        optic_collection.objects.link(duplicate)
        world = duplicate.matrix_world.copy()
        duplicate.parent = None
        duplicate.matrix_world = world
        optic_sources.append(duplicate)
    rifle_lod0 = join_renderer("NGPR002_Rifle_LOD0", render_sources, lod_collections[0], mat["armor"], "rifle", 0, armature)
    optic_lod0 = join_renderer("NGPR002_Optic_LOD0", optic_sources, lod_collections[0], mat["glass"], "optic", 0, armature)
    triangulate_mesh(rifle_lod0)
    triangulate_mesh(optic_lod0)
    if rifle_lod0.vertex_groups.get("WeaponMagazine") is None or rifle_lod0.vertex_groups.get("WeaponBolt") is None:
        raise RuntimeError("Joined LOD0 lost articulated vertex-group provenance")
    add_armature_adapter(rifle_lod0, armature)
    add_armature_adapter(optic_lod0, armature)
    lod_renderers = [rifle_lod0, optic_lod0]
    for lod in (1, 2, 3):
        rifle = copy_renderer(rifle_lod0, f"NGPR002_Rifle_LOD{lod}", lod_collections[lod], LOD_TARGETS[lod], "rifle", lod)
        optic = copy_renderer(optic_lod0, f"NGPR002_Optic_LOD{lod}", lod_collections[lod], max(64, len(optic_lod0.data.polygons) // (lod + 1)), "optic", lod)
        add_armature_adapter(rifle, armature)
        add_armature_adapter(optic, armature)
        lod_renderers.extend((rifle, optic))
    for obj in lod_renderers:
        bake_armature_adapter(obj)
    unwrap_uv0(lod_renderers)

    # Each source mesh receives its exact face semantic before consolidation,
    # so fixed collar/rail faces cannot be accidentally whitelisted by a broad
    # proximity sphere after the Candidate007 geometry changes.
    for obj in (rifle_lod0, optic_lod0):
        attribute = obj.data.attributes.get(face_policy.WEAPON_ATTRIBUTE)
        if attribute is None or attribute.domain != "FACE":
            raise RuntimeError(f"{obj.name} lost source-derived face semantics")
    for obj in lod_renderers[2:]:
        create_face_attribute(obj, face_policy.WEAPON_ATTRIBUTE, face_policy.WEAPON_ORDINARY)

    root["ps_weapon_rigid_signature_version"] = RIGID_SIGNATURE_VERSION
    root["ps_weapon_contract_version"] = CONTRACT_VERSION
    candidate007_texture_manifest = {
        "schema_version": 1,
        "asset_id": ASSET_ID,
        "source_asset_id": texture_manifest["asset_id"],
        "source_manifest_path": TEXTURE_MANIFEST.relative_to(ROOT).as_posix(),
        "source_texture_manifest_canonical_sha256": canonical_manifest_sha256(
            texture_manifest
        ),
        "reuse_reason": (
            "Candidate007 changes geometry and handling only; it deliberately "
            "reuses Candidate006's hash-pinned procedural preview maps until a "
            "final authored weapon bake exists."
        ),
        "reuse_policy": "hash_pinned_candidate006_preview_maps_not_final_bake",
        "resolution": texture_manifest["resolution"],
        "maps": {
            key: {
                "path": value["path"],
                "sha256": value["sha256"],
            }
            for key, value in texture_manifest["maps"].items()
        },
    }
    root["weapon_v3_texture_manifest_json"] = json.dumps(
        candidate007_texture_manifest,
        sort_keys=True,
        separators=(",", ":"),
    )
    texture_text = (
        bpy.data.texts.get("PS_WEAPON_V3_TEXTURE_MANIFEST.json")
        or bpy.data.texts.new("PS_WEAPON_V3_TEXTURE_MANIFEST.json")
    )
    texture_text.clear()
    texture_text.write(json.dumps(candidate007_texture_manifest, sort_keys=True))

    if not REAUTHOR_SCRIPT.is_file():
        raise RuntimeError(f"Candidate007 action reauthor stage is missing: {REAUTHOR_SCRIPT}")
    reauthor_module = load_module("candidate007_action_reauthor", REAUTHOR_SCRIPT)
    reauthor_evidence = reauthor_module.reauthor_candidate007_weapon_actions(armature, root)
    carrier_values = reauthor_evidence.get("carrier_to_root_matrix")
    if not isinstance(carrier_values, list) or len(carrier_values) != 16:
        raise RuntimeError("Candidate007 reauthor did not return a 4x4 carrier invariant")
    carrier_to_root = Matrix(tuple(
        tuple(float(carrier_values[row * 4 + column]) for column in range(4))
        for row in range(4)
    ))

    # Armature modifiers ignore vertex groups whose bones have use_deform=False.
    # The animation solvers deliberately build these as non-deforming controls,
    # so opt the three existing weapon-only bones into renderer skinning only
    # after the solver has completed its exact-output checks. This does not add
    # bones or mutate actions, and the suit has no weights on these controls.
    weapon_skin_controls = ("WeaponRoot", "WeaponMagazine", "WeaponBolt")
    weapon_root_rest = armature.data.bones["WeaponRoot"].matrix_local.copy()
    for bone_name in weapon_skin_controls:
        rest = armature.data.bones[bone_name].matrix_local
        maximum_rest_delta = max(
            abs(float(rest[row][column] - weapon_root_rest[row][column]))
            for row in range(4)
            for column in range(4)
        )
        if maximum_rest_delta > 1.0e-6:
            raise RuntimeError(
                "Candidate007 weapon skin controls no longer share one bind "
                f"matrix: {bone_name} delta={maximum_rest_delta:.9f}"
            )
        armature.data.bones[bone_name].use_deform = True
    weapon_skin_evidence = validate_weapon_skin_contract(
        armature, lod_renderers, weapon_skin_controls
    )

    # Armature-driven production renderers are authored in rigid RifleRoot
    # space. Blender evaluates them as pose @ rest^-1 @ bind, so bind must be
    # the control-bone rest matrix followed by the measured carrier-to-root
    # invariant. The current top-level controls have an identity rest matrix,
    # but retaining the complete formula prevents silent drift if that changes.
    renderer_bind_matrix = weapon_root_rest @ carrier_to_root
    for obj in lod_renderers:
        obj.data.transform(renderer_bind_matrix)
        obj.data.update()
    weapon_skin_motion = validate_weapon_skin_motion(
        armature,
        lod_renderers,
        (rifle_lod0, optic_lod0),
    )

    for source in [*parts, *optics]:
        source.hide_render = True
        source.hide_set(True)
    regenerated_actions = {
        action.name: action_signature(action, armature)
        for action in sorted((action for action in bpy.data.actions if action.name.startswith("PS_")), key=lambda item: item.name)
    }
    suit = tag_suit_semantics(armature)
    clearance = add_clearance_manifest(suit, lod_renderers)
    (
        lights,
        camera,
        ground,
        target_root,
        target_objects,
        sight_picture_root,
        sight_picture_objects,
    ) = create_studio(mat)

    OUTPUT_BLEND.parent.mkdir(parents=True, exist_ok=True)
    render_paths = render_reviews(
        armature,
        rifle_lod0,
        optic_lod0,
        lights,
        camera,
        ground,
        target_root,
        target_objects,
        sight_picture_root,
        sight_picture_objects,
    )
    activate_action(armature, "PS_Aim")
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    normalized_path_count = normalize_external_blender_paths_for_output()
    path_portability = assert_external_blender_paths_portable(
        normalized_path_count
    )
    bpy.context.scene["ngpr_path_portability_evidence_json"] = json.dumps(
        path_portability,
        sort_keys=True,
        separators=(",", ":"),
    )
    bpy.ops.wm.save_as_mainfile(
        filepath=str(OUTPUT_BLEND),
        check_existing=False,
        relative_remap=False,
    )
    saved_path_portability = assert_external_blender_paths_portable(
        normalized_path_count
    )
    if saved_path_portability != path_portability:
        raise RuntimeError(
            "Candidate007 saved blend changed its certified portable path set"
        )
    final_blend_hash = sha256(OUTPUT_BLEND)

    after = sha256(SOURCE_BLEND)
    if after != before:
        raise RuntimeError("Immutable Candidate005 source changed during Candidate007 generation")
    topology = {obj.name: topology_metrics(obj) for obj in lod_renderers}
    problems = {
        name: metric for name, metric in topology.items()
        if metric["boundary_edges"] or metric["non_manifold_edges"] or metric["zero_area_faces"] or metric["duplicate_vertex_pairs"]
    }
    if problems:
        raise RuntimeError(
            "Candidate007 generated topology blockers: "
            + json.dumps(problems, sort_keys=True, separators=(",", ":"))
        )
    if sha256(OUTPUT_BLEND) != final_blend_hash:
        raise RuntimeError("Final Candidate007 blend changed after render evidence binding")
    render_manifest_entries = [
        {
            "filename": path.name,
            "path": path.relative_to(ROOT).as_posix(),
            "sha256": sha256(path),
            "size_bytes": path.stat().st_size,
        }
        for path in render_paths
    ]
    render_hashes = [entry["sha256"] for entry in render_manifest_entries]
    if len(set(render_hashes)) != len(render_hashes):
        duplicates = [value for value, count in Counter(render_hashes).items() if count > 1]
        raise RuntimeError(
            "Review render set contains duplicate image evidence: "
            + ", ".join(value[:16] for value in duplicates)
        )
    report = {
        "schema_version": 1,
        "candidate": "nextgen_precision_rifle_candidate_v007",
        "status": "ISOLATED_PRODUCTION_WEAPON_CANDIDATE_NOT_UNITY_INTEGRATED",
        "asset_id": ASSET_ID,
        "source_candidate005": repository_relative_posix(SOURCE_BLEND),
        "source_sha256_before": before,
        "source_sha256_after": after,
        "source_preserved": before == after,
        "candidate_blend": repository_relative_posix(OUTPUT_BLEND),
        "candidate_blend_sha256": final_blend_hash,
        "concept_reference": {
            "path": CONCEPT_REFERENCE.relative_to(ROOT).as_posix(),
            "sha256": sha256(CONCEPT_REFERENCE),
            "usage": "visual_direction_only_measured_hardpoints_and_clearance_win",
        },
        "rig": {"armature": armature.name, "bone_count": len(armature.data.bones), "bones": sorted(bone.name for bone in armature.data.bones)},
        "hardpoint_contract": {
            "version": root["ps_candidate007_hardpoint_version"],
            "weapon_contract_version": int(root["ps_weapon_contract_version"]),
            "rigid_signature_version": int(root["ps_weapon_rigid_signature_version"]),
            "rigid_signature": str(root["ps_weapon_rigid_signature"]),
            "forward_axis": "+Y",
            "up_axis": "+Z",
            "support_grip_dogleg_local_m": [0.120, 0.280, 0.015],
            "support_grip_min_local_m": [0.097, 0.250, 0.015],
            "support_grip_max_local_m": [0.137, 0.315, 0.015],
            "primary_grip_local_m": [-0.085, -0.070, 0.025],
            "stock_contact_local_m": [-0.112, -0.448, 0.132],
            "sight_ocular_local_m": [0.0, -0.280, 0.315],
            "muzzle_local_m": [0.0, 1.175, 0.145],
        },
        "actions": {
            "count": len(regenerated_actions),
            "original_signatures": original_actions,
            "candidate_signatures": regenerated_actions,
            "reauthored": sorted(
                name for name in regenerated_actions
                if original_actions[name]["sha256"] != regenerated_actions[name]["sha256"]
            ),
            "preserved_exactly": original_actions == regenerated_actions,
            "reauthor_evidence": reauthor_evidence,
        },
        "production_renderers": {
            "count": len(lod_renderers),
            "per_lod": {f"LOD{lod}": [obj.name for obj in lod_renderers if int(obj["weapon_v3_lod"]) == lod] for lod in range(4)},
            "component_architecture": {
                "schema_version": 1,
                "role_attribute": "weapon_v3_component_role",
                "role_table": COMPONENT_ROLE_TABLE,
                "role_control_assignments": COMPONENT_ROLE_CONTROL_ASSIGNMENTS,
            },
            "triangle_counts": {name: int(metric["triangles"]) for name, metric in topology.items()},
            "topology_metrics": topology,
            "topology_blockers": problems,
            "weapon_skin_contract": weapon_skin_evidence,
            "weapon_skin_motion": weapon_skin_motion,
            "renderer_bind_matrix": [
                round(float(renderer_bind_matrix[row][column]), 9)
                for row in range(4)
                for column in range(4)
            ],
            "applied_unit_positive_transforms": all(tuple(round(float(value), 6) for value in obj.scale) == (1.0, 1.0, 1.0) for obj in lod_renderers),
            "runtime_armature_adapters": {obj.name: sum(modifier.type == "ARMATURE" for modifier in obj.modifiers) for obj in lod_renderers},
            "authoring_modifiers_remaining": {obj.name: sum(modifier.type != "ARMATURE" for modifier in obj.modifiers) for obj in lod_renderers},
        },
        "texture_manifest": candidate007_texture_manifest,
        "clearance_manifest": clearance,
        "render_paths": [repository_relative_posix(path) for path in render_paths],
        "render_manifest": {
            "candidate_blend_sha256": final_blend_hash,
            "files": render_manifest_entries,
        },
        "render_set_complete": {path.name for path in render_paths} == set(RENDER_NAMES),
        "projection_evidence": json.loads(str(bpy.context.scene["ngpr_projection_evidence_json"])),
        "path_portability": path_portability,
        "limitations": [
            "This automated asset establishes production-shaped geometry, LOD/UV/PBR plumbing and versioned hardpoints; final hand-authored weapon texturing remains outstanding.",
            "Candidate007 weapon actions were solver-reauthored around the new immutable hardpoints; visible strict clearance remains the promotion gate.",
            "Procedural 2K preview maps are licence-free pipeline evidence, not the final authored rifle bake.",
            "No FBX or Unity asset was exported, replaced, or modified.",
        ],
    }
    assert_manifest_has_no_local_absolute_paths(report)
    # Write explicit UTF-8/LF bytes so Git EOL settings cannot change the
    # generated artifact's working-copy hash. Downstream evidence additionally
    # binds to the canonical JSON semantic hash rather than these presentation
    # bytes.
    OUTPUT_REPORT.write_bytes((json.dumps(report, indent=2) + "\n").encode("utf-8"))
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
