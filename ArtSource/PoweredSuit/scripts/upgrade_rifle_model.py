# pyright: reportMissingImports=false
"""Create only the deterministic Powered Suit hero sniper-rifle asset.

Blender 5.2 pipeline responsibility:
- rifle geometry
- rifle materials
- RifleRoot hierarchy
- canonical helper empties

This script deliberately leaves RifleRoot independent. It does not pose the suit,
create IK, create animation, render, or export.
"""
from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from powersuit_pipeline_common import (  # noqa: E402
    RIFLE_ROOT_NAME,
    ensure_object_mode,
    get_armature,
    orientation_with_y_axis,
    remove_object_tree,
    require_blender_52,
    save_current_blend,
)
from weapon_handling_contract import (  # noqa: E402
    COMPONENT_BOLT,
    COMPONENT_MAGAZINE,
    COMPONENT_OPTIC,
    COMPONENT_PRIMARY_GRIP,
    COMPONENT_STOCK,
    COMPONENT_SUPPORT_GRIP,
    ROLE_MUZZLE,
    ROLE_PRIMARY_GRIP,
    ROLE_SIGHT_OCULAR,
    ROLE_STOCK_CONTACT,
    ROLE_SUPPORT_GRIP,
    ROLE_SUPPORT_MAX,
    ROLE_SUPPORT_MIN,
    freeze_rigid_weapon,
    normalize_rigid_weapon_children,
    tag_component,
    tag_articulated_owner,
    tag_contact_surface,
    tag_helper,
    tag_weapon_root,
    validate_weapon_contract,
)

# -----------------------------------------------------------------------------
# Exposed rifle parameters (metres)
# -----------------------------------------------------------------------------

RIFLE_COLLECTION = "PowerSuitRifle"
GENERATED_TAG = "powersuit_rifle_generated"

RECEIVER_LENGTH = 0.36
RECEIVER_WIDTH = 0.175
RECEIVER_HEIGHT = 0.170

HANDGUARD_LENGTH = 0.34
HANDGUARD_WIDTH = 0.155
HANDGUARD_HEIGHT = 0.140

BARREL_LENGTH = 0.52
BARREL_RADIUS = 0.021
BARREL_SHROUD_RADIUS = 0.034

STOCK_LENGTH = 0.34
STOCK_WIDTH = 0.135
STOCK_HEIGHT = 0.195

SCOPE_LENGTH = 0.38
SCOPE_TUBE_RADIUS = 0.023
SCOPE_OBJECTIVE_RADIUS = 0.043
SCOPE_OCULAR_RADIUS = 0.034

MAGAZINE_LENGTH = 0.185
MAGAZINE_WIDTH = 0.088
MAGAZINE_DEPTH = 0.060

GRIP_LENGTH = 0.155
GRIP_WIDTH = 0.060
GRIP_DEPTH = 0.052

MUZZLE_LENGTH = 0.150
MUZZLE_WIDTH = 0.090
MUZZLE_HEIGHT = 0.078

PRIMARY_BEVEL = 0.012
SECONDARY_BEVEL = 0.006
SMALL_BEVEL = 0.003
CYLINDER_SIDES = 16

# Canonical rifle-local frame:
#   +Y = muzzle / firing direction
#   +Z = up / scope direction
#   +X = rifle right side
# Weapon-framework v1 ergonomic hardpoints. Geometry is designed once around
# these points and then frozen. Animation is never allowed to offset individual
# scope/stock/grip parts to satisfy a pose.
PRIMARY_GRIP_CENTER = Vector((0.0, -0.026, -0.064))
PRIMARY_WRIST_OFFSET = Vector((0.0, -0.014, 0.080))
PRIMARY_GRIP = PRIMARY_GRIP_CENTER + PRIMARY_WRIST_OFFSET
# Grip helpers are wrist transforms, not physical grip centres.  The original
# support helper sat inside the foregrip, placing the visible palm/fingers below
# it while the wrist metric misleadingly read zero.  This offset matches the
# existing Hand-bone-to-palm relationship already used by the primary grip.
SUPPORT_GRIP_CENTER = Vector((0.0, 0.205, -0.022))
SUPPORT_WRIST_OFFSET = Vector((0.013, -0.014, 0.067))
SUPPORT_GRIP = SUPPORT_GRIP_CENTER + SUPPORT_WRIST_OFFSET
SUPPORT_GRIP_MIN = Vector((0.0, 0.165, -0.022)) + SUPPORT_WRIST_OFFSET
SUPPORT_GRIP_MAX = Vector((0.0, 0.245, -0.022)) + SUPPORT_WRIST_OFFSET
# The legacy source rig's bones named ``.R`` are on negative visual X.  The
# weapon is attached to Hand.R, so its buttstock must dogleg toward local -X in
# order for the centred receiver/optic to sit inboard of that shoulder.  Reset
# 05/06 used +0.110 here, which moved the optic farther outboard.
STOCK_LATERAL_OFFSET = -0.110
STOCK_CONTACT_Z = 0.035
STOCK_CONTACT = Vector((STOCK_LATERAL_OFFSET, -0.365, STOCK_CONTACT_Z))
SCOPE_OCULAR = Vector((0.0, -0.253, 0.315))
MUZZLE_POINT = Vector((0.0, 1.111, 0.145))

# The optic is deliberately centred on the rifle. This is a rigid-asset
# invariant and the key architectural reset from the previous pose-specific
# offset strategy.
SCOPE_X = 0.0
SCOPE_LONGITUDINAL_SHIFT = -0.020
SCOPE_CENTER_Z = 0.315


# -----------------------------------------------------------------------------
# Data creation helpers
# -----------------------------------------------------------------------------

def _collection() -> bpy.types.Collection:
    collection = bpy.data.collections.get(RIFLE_COLLECTION)
    if collection is None:
        collection = bpy.data.collections.new(RIFLE_COLLECTION)
    if bpy.context.scene.collection.children.get(collection.name) is None:
        bpy.context.scene.collection.children.link(collection)
    return collection


def _material(name: str, color, metallic: float, roughness: float):
    material = bpy.data.materials.get(name)
    if material is None:
        material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
    return material


def _materials() -> dict[str, bpy.types.Material]:
    return {
        "body": _material("PS_Rifle_Gunmetal", (0.055, 0.065, 0.080, 1.0), 0.82, 0.28),
        "armor": _material("PS_Rifle_Armor", (0.18, 0.205, 0.24, 1.0), 0.76, 0.32),
        "edge": _material("PS_Rifle_Edge", (0.36, 0.40, 0.46, 1.0), 0.88, 0.22),
        "grip": _material("PS_Rifle_Grip", (0.025, 0.028, 0.034, 1.0), 0.05, 0.72),
        "accent": _material("PS_Rifle_Accent", (0.035, 0.36, 0.50, 1.0), 0.58, 0.20),
        "glass": _material("PS_Rifle_OpticGlass", (0.018, 0.12, 0.17, 1.0), 0.72, 0.09),
    }


def _link_object(name: str, mesh: bpy.types.Mesh | None, collection) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj[GENERATED_TAG] = True
    return obj


def _assign_material(obj: bpy.types.Object, material: bpy.types.Material | None) -> None:
    if material is not None and obj.type == "MESH":
        obj.data.materials.append(material)


def _bevel(obj: bpy.types.Object, width: float) -> None:
    if width <= 0.0:
        return
    modifier = obj.modifiers.new("PS_Bevel", "BEVEL")
    modifier.width = width
    modifier.segments = 2
    modifier.limit_method = "ANGLE"


def _smooth_cylinder(obj: bpy.types.Object) -> None:
    for polygon in obj.data.polygons:
        polygon.use_smooth = len(polygon.vertices) == 4


def create_box(
    name: str,
    location,
    dimensions,
    *,
    material=None,
    rotation=(0.0, 0.0, 0.0),
    bevel=PRIMARY_BEVEL,
    collection=None,
) -> bpy.types.Object:
    x, y, z = (float(v) * 0.5 for v in dimensions)
    vertices = [
        (-x, -y, -z), (x, -y, -z), (x, y, -z), (-x, y, -z),
        (-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z),
    ]
    faces = [
        (3, 2, 1, 0), (5, 6, 7, 4),
        (1, 5, 4, 0), (2, 6, 5, 1),
        (3, 7, 6, 2), (7, 3, 0, 4),
    ]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = _link_object(name, mesh, collection or _collection())
    obj.location = location
    obj.rotation_euler = rotation
    _assign_material(obj, material)
    _bevel(obj, bevel)
    return obj


def create_tapered_box(
    name: str,
    location,
    dimensions,
    *,
    front_scale=(1.0, 1.0),
    material=None,
    rotation=(0.0, 0.0, 0.0),
    bevel=PRIMARY_BEVEL,
    collection=None,
) -> bpy.types.Object:
    """Create a +Y-forward wedge with width/height scaling at its front end."""
    width, length, height = dimensions
    rx, rz = width * 0.5, height * 0.5
    fx, fz = rx * front_scale[0], rz * front_scale[1]
    back_y, front_y = -length * 0.5, length * 0.5
    vertices = [
        (-rx, back_y, -rz), (rx, back_y, -rz), (fx, front_y, -fz), (-fx, front_y, -fz),
        (-rx, back_y, rz), (rx, back_y, rz), (fx, front_y, fz), (-fx, front_y, fz),
    ]
    faces = [
        (3, 2, 1, 0), (5, 6, 7, 4),
        (1, 5, 4, 0), (2, 6, 5, 1),
        (3, 7, 6, 2), (7, 3, 0, 4),
    ]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = _link_object(name, mesh, collection or _collection())
    obj.location = location
    obj.rotation_euler = rotation
    _assign_material(obj, material)
    _bevel(obj, bevel)
    return obj


def create_cylinder(
    name: str,
    location,
    *,
    radius: float,
    length: float,
    material=None,
    sides=CYLINDER_SIDES,
    axis="Y",
    rotation=(0.0, 0.0, 0.0),
    bevel=SMALL_BEVEL,
    collection=None,
) -> bpy.types.Object:
    vertices = []
    faces = []
    half = length * 0.5
    for index in range(sides):
        angle = 2.0 * math.pi * index / sides
        a = radius * math.cos(angle)
        b = radius * math.sin(angle)
        if axis == "Y":
            vertices.extend(((a, -half, b), (a, half, b)))
        elif axis == "X":
            vertices.extend(((-half, a, b), (half, a, b)))
        elif axis == "Z":
            vertices.extend(((a, b, -half), (a, b, half)))
        else:
            raise ValueError(f"Unsupported cylinder axis: {axis}")
    for index in range(sides):
        nxt = (index + 1) % sides
        faces.append((index * 2, nxt * 2, nxt * 2 + 1, index * 2 + 1))
    faces.append(tuple(index * 2 for index in reversed(range(sides))))
    faces.append(tuple(index * 2 + 1 for index in range(sides)))
    if axis == "Y":
        faces = [tuple(reversed(face)) for face in faces]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = _link_object(name, mesh, collection or _collection())
    obj.location = location
    obj.rotation_euler = rotation
    _assign_material(obj, material)
    _smooth_cylinder(obj)
    _bevel(obj, bevel)
    return obj


def create_helper(
    name: str,
    location: Vector,
    y_axis: Vector,
    z_hint: Vector,
    collection,
) -> bpy.types.Object:
    helper = _link_object(name, None, collection)
    helper.empty_display_type = "ARROWS"
    helper.empty_display_size = 0.055
    helper.matrix_local = Matrix.Translation(location) @ orientation_with_y_axis(y_axis, z_hint)
    return helper


def _parent_local(children: list[bpy.types.Object], root: bpy.types.Object) -> None:
    for child in children:
        child.parent = root
        child.parent_type = "OBJECT"
        # Local transforms were authored before parenting while the root was identity.
        # Preserve those transforms explicitly rather than preserving world space.
        child.matrix_parent_inverse.identity()


# -----------------------------------------------------------------------------
# Cleanup and assembly
# -----------------------------------------------------------------------------

def cleanup_previous_rifle() -> None:
    ensure_object_mode()
    root = bpy.data.objects.get(RIFLE_ROOT_NAME)
    if root is not None:
        remove_object_tree(root)
    for obj in list(bpy.data.objects):
        if obj.name.startswith("Rifle_") or obj.get(GENERATED_TAG, False):
            data = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if data is not None and getattr(data, "users", 1) == 0:
                if isinstance(data, bpy.types.Mesh):
                    bpy.data.meshes.remove(data)


def build_rifle() -> bpy.types.Object:
    collection = _collection()
    materials = _materials()

    get_armature()  # Confirms this is the intended powered-suit source file.
    support_x = 0.0
    scope_x = SCOPE_X

    root = _link_object(RIFLE_ROOT_NAME, None, collection)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.10
    root.matrix_world = Matrix.Identity(4)
    tag_weapon_root(root, weapon_id="PS_HeroSniper", stance_family="shouldered_precision")
    # Legacy-readable properties are retained for the existing report/export
    # ecosystem, but their values now describe the rigid contract rather than a
    # pose-specific search result.
    root["ps_rifle_forward_axis"] = "+Y"
    root["ps_rifle_up_axis"] = "+Z"
    root["ps_stock_point_local"] = tuple(STOCK_CONTACT)
    root["ps_muzzle_point_local"] = tuple(MUZZLE_POINT)
    root["ps_scope_point_local"] = tuple(SCOPE_OCULAR)
    root["ps_support_grip_x_local"] = 0.0
    root["ps_scope_x_local"] = 0.0
    root["ps_generator_version"] = 111
    root["ps_stock_lateral_offset_m"] = float(STOCK_LATERAL_OFFSET)

    p: list[bpy.types.Object] = []

    # Receiver: overlapping tapered masses produce one readable, cohesive silhouette.
    p.append(create_tapered_box("Rifle_Receiver_Core", (0, 0.11, 0.120),
        (RECEIVER_WIDTH, RECEIVER_LENGTH, RECEIVER_HEIGHT), front_scale=(0.90, 0.84),
        material=materials["body"], collection=collection))
    p.append(create_tapered_box("Rifle_Receiver_UpperShell", (0, 0.10, 0.192),
        (0.160, 0.33, 0.062), front_scale=(0.84, 0.72), material=materials["armor"],
        bevel=SECONDARY_BEVEL, collection=collection))
    p.append(create_box("Rifle_Receiver_LowerSpine", (0, 0.09, 0.035),
        (0.125, 0.27, 0.050), material=materials["edge"], bevel=SECONDARY_BEVEL,
        collection=collection))
    p.append(create_box("Rifle_EjectionPort_R", (-0.089, 0.13, 0.142),
        (0.012, 0.14, 0.058), material=materials["grip"], bevel=SMALL_BEVEL,
        collection=collection))
    p.append(create_box("Rifle_ChargingRail_R", (-0.100, 0.03, 0.198),
        (0.018, 0.18, 0.022), material=materials["edge"], bevel=SMALL_BEVEL,
        collection=collection))
    p.append(create_cylinder("Rifle_BoltHandleStem_R", (-0.126, 0.025, 0.198),
        radius=0.008, length=0.052, axis="X", material=materials["edge"],
        bevel=0.0015, collection=collection))
    p.append(create_cylinder("Rifle_BoltHandleKnob_R", (-0.158, 0.025, 0.198),
        radius=0.015, length=0.020, axis="X", material=materials["grip"],
        bevel=0.0015, collection=collection))

    # Handguard and barrel.
    p.append(create_tapered_box("Rifle_Handguard_Core", (0, 0.35, 0.140),
        (HANDGUARD_WIDTH, HANDGUARD_LENGTH, HANDGUARD_HEIGHT), front_scale=(0.75, 0.72),
        material=materials["armor"], collection=collection))
    p.append(create_box("Rifle_Handguard_TopRail", (0, 0.35, 0.218),
        (0.090, 0.35, 0.022), material=materials["edge"], bevel=SMALL_BEVEL,
        collection=collection))
    # Short integrated foregrip shoe only.  The previous long lower rail read as
    # a second floating hand-rest platform in the validation close-ups.
    p.append(create_box("Rifle_Handguard_BottomRail", (0, 0.250, 0.061),
        (0.050, 0.080, 0.012), material=materials["body"], bevel=SMALL_BEVEL,
        collection=collection))
    for side in (-1.0, 1.0):
        p.append(create_tapered_box(
            f"Rifle_Handguard_SidePanel_{'R' if side < 0 else 'L'}",
            (side * 0.079, 0.35, 0.140), (0.016, 0.28, 0.086),
            front_scale=(0.72, 0.78), material=materials["body"],
            bevel=SMALL_BEVEL, collection=collection))
        for index, y in enumerate((0.235, 0.315, 0.395, 0.475)):
            p.append(create_box(
                f"Rifle_Vent_{'R' if side < 0 else 'L'}_{index+1}",
                (side * 0.087, y, 0.145), (0.010, 0.045, 0.042),
                material=materials["grip"], bevel=0.0015, collection=collection))

    # Compact integrated foregrip.  It sits directly below the handguard and is
    # deliberately rearward enough that the support elbow can remain bent while
    # the rifle is shouldered.  No long horizontal platform extends under it.
    p.append(create_box("Rifle_SupportGrip_Mount", (0.0, 0.205, 0.050),
        (0.055, 0.050, 0.018), material=materials["edge"],
        bevel=SMALL_BEVEL, collection=collection))
    p.append(create_tapered_box("Rifle_SupportGrip", (0.0, 0.205, -0.022),
        (0.052, 0.056, 0.120), front_scale=(0.82, 0.76),
        material=materials["grip"], rotation=(math.radians(-7), 0, 0),
        bevel=SECONDARY_BEVEL, collection=collection))

    p.append(create_cylinder("Rifle_Barrel_Shroud", (0, 0.55, 0.145),
        radius=BARREL_SHROUD_RADIUS, length=0.18, material=materials["body"],
        collection=collection))
    p.append(create_cylinder("Rifle_Barrel", (0, 0.77, 0.145),
        radius=BARREL_RADIUS, length=BARREL_LENGTH, material=materials["edge"],
        collection=collection))
    p.append(create_cylinder("Rifle_Barrel_EnergySleeve", (0, 0.63, 0.145),
        radius=0.027, length=0.14, material=materials["accent"],
        bevel=0.002, collection=collection))

    # Compact two-baffle muzzle brake. The long barrel remains readable and the
    # brake no longer ends in a single oversized unrelated cube.
    p.append(create_cylinder("Rifle_MuzzleBrake_Core", (0, 1.04, 0.145),
        radius=0.031, length=0.12, material=materials["body"],
        collection=collection))
    for index, y in enumerate((1.005, 1.055), start=1):
        p.append(create_tapered_box(
            f"Rifle_MuzzleBrake_Baffle_{index}", (0, y, 0.150),
            (MUZZLE_WIDTH, 0.026, MUZZLE_HEIGHT),
            front_scale=(0.86, 0.86), material=materials["armor"],
            bevel=SMALL_BEVEL, collection=collection))
    p.append(create_box("Rifle_MuzzleBrake_TopFin", (0, 1.04, 0.191),
        (0.035, 0.085, 0.018), material=materials["edge"],
        bevel=0.002, collection=collection))
    p.append(create_cylinder("Rifle_MuzzleBore", (0, 1.105, 0.145),
        radius=0.015, length=0.012, material=materials["grip"], bevel=0.001,
        collection=collection))

    # Pistol grip, trigger guard, and magazine.
    grip_angle = math.radians(-24.0)
    p.append(create_tapered_box("Rifle_PistolGrip", (0, -0.026, -0.064),
        (GRIP_WIDTH, GRIP_DEPTH, GRIP_LENGTH), front_scale=(0.78, 1.18),
        material=materials["grip"], rotation=(grip_angle, 0, 0),
        bevel=SECONDARY_BEVEL, collection=collection))
    p.append(create_box("Rifle_GripBackstrap", (0, -0.058, -0.058),
        (0.084, 0.018, 0.132), material=materials["armor"], rotation=(grip_angle, 0, 0),
        bevel=SMALL_BEVEL, collection=collection))
    p.append(create_box("Rifle_TriggerGuard", (0, 0.070, -0.010),
        (0.075, 0.105, 0.026), material=materials["body"], bevel=SMALL_BEVEL,
        collection=collection))
    p.append(create_box("Rifle_Trigger", (0, 0.035, 0.002),
        (0.022, 0.020, 0.065), material=materials["edge"],
        rotation=(math.radians(-18), 0, 0), bevel=0.002, collection=collection))
    p.append(create_tapered_box("Rifle_Magazine", (0, 0.155, -0.078),
        (MAGAZINE_WIDTH, MAGAZINE_DEPTH, MAGAZINE_LENGTH), front_scale=(0.66, 0.84),
        material=materials["body"], rotation=(math.radians(-11), 0, 0),
        bevel=SECONDARY_BEVEL, collection=collection))
    p.append(create_box("Rifle_Magazine_Base", (0, 0.178, -0.171),
        (0.094, 0.062, 0.018), material=materials["edge"],
        rotation=(math.radians(-11), 0, 0), bevel=SMALL_BEVEL, collection=collection))

    # Open skeletal stock with a deliberate powered-suit shoulder offset.
    # The receiver, bore and scope remain centred at X=0.  Only the rear stock
    # interface doglegs toward the right shoulder.  This is a one-time weapon
    # ergonomic design feature, not an animation-time scope/receiver offset.
    # It lets the centred sight line sit closer to the helmet while the buttpad
    # still reaches the armoured shoulder pocket.
    stock_side = -1.0 if STOCK_LATERAL_OFFSET < 0.0 else 1.0
    p.append(create_cylinder("Rifle_StockSpine", (stock_side * 0.018, -0.190, 0.135),
        radius=0.022, length=0.22, material=materials["body"], collection=collection))
    for side in (-1.0, 1.0):
        p.append(create_tapered_box(
            f"Rifle_StockStrut_{'R' if side < 0 else 'L'}",
            (stock_side * 0.042 + side * 0.045, -0.235, 0.095), (0.028, 0.22, 0.045),
            front_scale=(0.92, 0.70), material=materials["armor"],
            rotation=(math.radians(12), 0, 0), bevel=SECONDARY_BEVEL,
            collection=collection))
    p.append(create_tapered_box("Rifle_Stock_TopRail", (stock_side * 0.040, -0.235, 0.140),
        (0.105, 0.20, 0.042), front_scale=(0.78, 0.72),
        material=materials["grip"], rotation=(math.radians(20), 0, 0),
        bevel=SECONDARY_BEVEL, collection=collection))
    p.append(create_tapered_box("Rifle_Stock_BottomRail", (stock_side * 0.048, -0.250, 0.045),
        (0.090, 0.17, 0.032), front_scale=(0.72, 0.70),
        material=materials["armor"], rotation=(math.radians(5), 0, 0),
        bevel=SECONDARY_BEVEL, collection=collection))
    p.append(create_box("Rifle_Stock_RearBridge", (STOCK_LATERAL_OFFSET, -0.335, STOCK_CONTACT_Z),
        (0.112, 0.028, 0.142), material=materials["armor"],
        bevel=SECONDARY_BEVEL, collection=collection))
    p.append(create_tapered_box("Rifle_Stock_ButtPad", (STOCK_LATERAL_OFFSET, -0.350, STOCK_CONTACT_Z),
        (0.118, 0.030, 0.128), front_scale=(0.88, 0.90),
        material=materials["grip"], bevel=SECONDARY_BEVEL,
        collection=collection))

    # Scope, mounts, lens bells, and data-module accent. The complete optic is
    # rigid, centred on the receiver, and visually conventional. It is designed
    # here once; pose creation is forbidden from changing any child transform.
    for y_base in (-0.055, 0.125):
        y = y_base + SCOPE_LONGITUDINAL_SHIFT
        mount_name = "Rifle_ScopeMount_Rear" if y_base < 0 else "Rifle_ScopeMount_Front"
        p.append(create_box(mount_name,
            (scope_x, y, 0.265), (0.070, 0.050, 0.095),
            material=materials["edge"], bevel=SECONDARY_BEVEL,
            collection=collection))
        p.append(create_box(mount_name + "_Crossbar",
            (0.0, y, 0.222),
            (0.110, 0.042, 0.024),
            material=materials["armor"], bevel=SMALL_BEVEL,
            collection=collection))
    p.append(create_cylinder("Rifle_ScopeTube",
        (scope_x, 0.025 + SCOPE_LONGITUDINAL_SHIFT, SCOPE_CENTER_Z),
        radius=SCOPE_TUBE_RADIUS, length=SCOPE_LENGTH, material=materials["body"],
        collection=collection))
    p.append(create_cylinder("Rifle_ScopeObjective",
        (scope_x, 0.220 + SCOPE_LONGITUDINAL_SHIFT, SCOPE_CENTER_Z),
        radius=SCOPE_OBJECTIVE_RADIUS, length=0.095, material=materials["armor"],
        collection=collection))
    p.append(create_cylinder("Rifle_ScopeOcular",
        (scope_x, -0.180 + SCOPE_LONGITUDINAL_SHIFT, SCOPE_CENTER_Z),
        radius=SCOPE_OCULAR_RADIUS, length=0.090, material=materials["armor"],
        collection=collection))
    p.append(create_cylinder("Rifle_ScopeLensFront",
        (scope_x, 0.272 + SCOPE_LONGITUDINAL_SHIFT, SCOPE_CENTER_Z),
        radius=0.037, length=0.010, material=materials["glass"], bevel=0.001,
        collection=collection))
    p.append(create_cylinder("Rifle_ScopeLensRear",
        (scope_x, -0.233 + SCOPE_LONGITUDINAL_SHIFT, SCOPE_CENTER_Z),
        radius=0.029, length=0.010, material=materials["glass"], bevel=0.001,
        collection=collection))
    p.append(create_cylinder("Rifle_ScopeElevation",
        (scope_x, 0.020 + SCOPE_LONGITUDINAL_SHIFT, SCOPE_CENTER_Z + 0.040),
        radius=0.018, length=0.050, axis="Z", material=materials["edge"],
        collection=collection))
    p.append(create_cylinder("Rifle_ScopeWindage",
        (scope_x - 0.041, 0.020 + SCOPE_LONGITUDINAL_SHIFT, SCOPE_CENTER_Z),
        radius=0.016, length=0.050, axis="X", material=materials["edge"],
        collection=collection))
    p.append(create_tapered_box("Rifle_DataModule", (0.060, 0.115, 0.218),
        (0.044, 0.14, 0.058), front_scale=(0.78, 0.78), material=materials["accent"],
        bevel=SECONDARY_BEVEL, collection=collection))

    # Canonical hardpoints. Object names are human-readable only; animation
    # resolves their semantic roles through the reusable weapon contract.
    helpers = [
        create_helper("Rifle_PrimaryGrip", PRIMARY_GRIP,
            Vector((0.0, 0.20, -0.980)), Vector((1.0, 0.0, 0.0)), collection),
        create_helper("Rifle_SupportGripTarget", SUPPORT_GRIP,
            Vector((0.0, 0.20, -0.980)), Vector((1.0, 0.0, 0.0)), collection),
        create_helper("Rifle_StockContact", STOCK_CONTACT,
            Vector((0.0, -1.0, 0.0)), Vector((0.0, 0.0, 1.0)), collection),
        create_helper("Rifle_SightOcular", SCOPE_OCULAR,
            Vector((0.0, 1.0, 0.0)), Vector((0.0, 0.0, 1.0)), collection),
        create_helper("Rifle_Muzzle", MUZZLE_POINT,
            Vector((0.0, 1.0, 0.0)), Vector((0.0, 0.0, 1.0)), collection),
        create_helper("Rifle_SupportGripMin", SUPPORT_GRIP_MIN,
            Vector((0.0, 1.0, 0.0)), Vector((1.0, 0.0, 0.0)), collection),
        create_helper("Rifle_SupportGripMax", SUPPORT_GRIP_MAX,
            Vector((0.0, 1.0, 0.0)), Vector((1.0, 0.0, 0.0)), collection),
    ]
    for helper, role in zip(helpers, (
        ROLE_PRIMARY_GRIP, ROLE_SUPPORT_GRIP, ROLE_STOCK_CONTACT,
        ROLE_SIGHT_OCULAR, ROLE_MUZZLE, ROLE_SUPPORT_MIN, ROLE_SUPPORT_MAX,
    )):
        tag_helper(helper, role)

    grip_offsets = {
        ROLE_PRIMARY_GRIP: PRIMARY_WRIST_OFFSET,
        ROLE_SUPPORT_GRIP: SUPPORT_WRIST_OFFSET,
        ROLE_SUPPORT_MIN: SUPPORT_WRIST_OFFSET,
        ROLE_SUPPORT_MAX: SUPPORT_WRIST_OFFSET,
    }
    for helper, role in zip(helpers, (
        ROLE_PRIMARY_GRIP, ROLE_SUPPORT_GRIP, ROLE_STOCK_CONTACT,
        ROLE_SIGHT_OCULAR, ROLE_MUZZLE, ROLE_SUPPORT_MIN, ROLE_SUPPORT_MAX,
    )):
        if role in grip_offsets:
            helper["ps_weapon_target_semantic"] = "wrist_head"
            helper["ps_weapon_contact_offset_local"] = tuple(
                float(value) for value in grip_offsets[role]
            )

    _parent_local([*p, *helpers], root)
    for child in p:
        if child.name in {
            "Rifle_Magazine",
            "Rifle_Magazine_Base",
        }:
            tag_component(child, COMPONENT_MAGAZINE)
        elif child.name in {
            "Rifle_ChargingRail_R",
            "Rifle_BoltHandleStem_R",
            "Rifle_BoltHandleKnob_R",
        }:
            tag_component(child, COMPONENT_BOLT)
        elif child.name.startswith("Rifle_Scope"):
            tag_component(child, COMPONENT_OPTIC)
        elif child.name.startswith("Rifle_Stock"):
            tag_component(child, COMPONENT_STOCK)
        elif child.name in {"Rifle_PistolGrip", "Rifle_GripBackstrap", "Rifle_TriggerGuard", "Rifle_Trigger"}:
            tag_component(child, COMPONENT_PRIMARY_GRIP)
        elif child.name in {"Rifle_SupportGrip", "Rifle_SupportGrip_Mount"}:
            tag_component(child, COMPONENT_SUPPORT_GRIP)
    for child in p:
        if str(child.get("ps_weapon_component_role", "")) in {
            COMPONENT_MAGAZINE,
            COMPONENT_BOLT,
        }:
            tag_articulated_owner(root, child)
    tag_contact_surface(
        next(child for child in p if child.name == "Rifle_PistolGrip"),
        ROLE_PRIMARY_GRIP,
    )
    tag_contact_surface(
        next(child for child in p if child.name == "Rifle_SupportGrip"),
        ROLE_SUPPORT_GRIP,
    )
    tag_contact_surface(
        next(child for child in p if child.name == "Rifle_Stock_ButtPad"),
        ROLE_STOCK_CONTACT,
    )
    root.parent = None
    root.parent_type = "OBJECT"
    root.parent_bone = ""
    root.matrix_world = Matrix.Identity(4)
    root.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()
    # Canonical rigid representation: mesh-part placement is baked into mesh
    # vertices so every mesh child sits at identity under RifleRoot. This makes
    # rigidity independent of Blender's save/load parenting normalisation.
    normalize_rigid_weapon_children(root)
    signature = freeze_rigid_weapon(root)
    root["ps_weapon_asset_signature_short"] = signature[:16]
    validate_weapon_contract(root, require_independent=True)
    return root


def validate_hierarchy(root: bpy.types.Object) -> None:
    validate_weapon_contract(root)
    stray = [
        obj.name for obj in bpy.data.objects
        if (obj.name.startswith("Rifle_") or obj.get(GENERATED_TAG, False))
        and obj != root and obj.parent != root
    ]
    if stray:
        raise RuntimeError("Stray rifle objects outside RifleRoot: " + ", ".join(sorted(stray)))
    if abs(float(root.get("ps_scope_x_local", 999.0))) > 1.0e-6:
        raise RuntimeError("Rigid sniper optic is not centred on the receiver.")


def main() -> None:
    require_blender_52()
    get_armature()  # Ensures this is the intended suit file before destructive rifle cleanup.
    cleanup_previous_rifle()
    root = build_rifle()
    validate_hierarchy(root)
    saved = save_current_blend()
    print("\nPowered Suit rifle model rebuilt.")
    print(f"RifleRoot children: {len(root.children)}")
    print("Rigid weapon contract validated; scope remains centred on the rifle.")
    print("RifleRoot is independent and ready for stance-family pose solving.")
    print(f"Saved: {saved}")


if __name__ == "__main__":
    main()
