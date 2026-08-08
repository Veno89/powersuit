# pyright: reportMissingImports=false
"""Rebuild the Powered Suit character shell as a more articulated V2 model.

This script deliberately preserves the existing armature object, bone names,
parenting relationships, and Action names.  It only replaces the visible suit
mesh geometry with a cleaner, less blocky shell that has:
- slimmer shoulder / forearm collision envelopes
- clearer elbow and knee articulation
- more functional hands with simple thumbs and finger groups
- slightly longer-looking arm and leg shells without changing the skeleton

The goal is to improve weapon-handling poses without breaking the rest of the
pipeline or Unity-facing naming.
"""
from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore
from mathutils.bvhtree import BVHTree  # type: ignore

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from powersuit_pipeline_common import (  # noqa: E402
    body_basis,
    ensure_object_mode,
    get_armature,
    orientation_with_y_axis,
    require_blender_52,
    save_current_blend,
)

GENERATED_TAG = "powersuit_suit_v2_generated"

SUIT_OBJECTS = {
    "Backpack_Core",
    "Backpack_Thruster.L", "Backpack_Thruster.R",
    "Thruster_Nozzle.L", "Thruster_Nozzle.R",
    "Chest_Core", "Chest_Plate", "Chest_Plate.L", "Chest_Plate.R", "Upper_Chest",
    "Boot_Toe.L", "Boot_Toe.R", "Heavy_Boot.L", "Heavy_Boot.R",
    "Hand.L", "Hand.R",
    "Helmet_Core", "Helmet_Crown", "Helmet_Jaw", "Helmet_Plate.L", "Helmet_Plate.R", "Helmet_Visor",
    "Hip_Guard.L", "Hip_Guard.R", "Pelvis",
    "Elbow.L", "Elbow.R", "Forearm.L", "Forearm.R", "Forearm_Plate.L", "Forearm_Plate.R",
    "Knee.L", "Knee.R", "Knee_Guard.L", "Knee_Guard.R", "Lower_Leg.L", "Lower_Leg.R", "Shin_Plate.L", "Shin_Plate.R",
    "Neck", "Waist",
    "Shoulder_Armour.L", "Shoulder_Armour.R", "Shoulder_Wing.L", "Shoulder_Wing.R", "Upper_Arm.L", "Upper_Arm.R",
    "Thigh_Plate.L", "Thigh_Plate.R", "Upper_Leg.L", "Upper_Leg.R",
}


def _material(name: str, color, metallic: float, roughness: float):
    material = bpy.data.materials.get(name)
    if material is None:
        material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
    return material


def _materials() -> dict[str, bpy.types.Material]:
    return {
        "armor": _material("PS_Suit_ArmorV2", (0.46, 0.52, 0.60, 1.0), 0.34, 0.42),
        "dark": _material("PS_Suit_DarkV2", (0.035, 0.050, 0.075, 1.0), 0.52, 0.40),
        "metal": _material("PS_Suit_MetalV2", (0.18, 0.24, 0.32, 1.0), 0.72, 0.28),
        "visor": _material("PS_Suit_VisorV2", (0.025, 0.38, 0.68, 1.0), 0.48, 0.12),
        "accent": _material("PS_Suit_AccentV2", (0.03, 0.48, 0.78, 1.0), 0.42, 0.18),
    }


def _clear_object_data(obj: bpy.types.Object) -> None:
    old = obj.data
    for modifier in list(obj.modifiers):
        obj.modifiers.remove(modifier)
    if obj.type == "MESH":
        obj.data = bpy.data.meshes.new(obj.name + "_Mesh")
        if old is not None and getattr(old, "users", 0) == 0:
            bpy.data.meshes.remove(old)
    obj.data.materials.clear()
    obj[GENERATED_TAG] = True


def _assign_material(obj: bpy.types.Object, material: bpy.types.Material) -> None:
    obj.data.materials.clear()
    obj.data.materials.append(material)


def _bevel(obj: bpy.types.Object, width: float = 0.015, segments: int = 2) -> None:
    modifier = obj.modifiers.new("PS_Bevel", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"


class MeshBuilder:
    def __init__(self):
        self.verts: list[tuple[float, float, float]] = []
        self.faces: list[tuple[int, int, int, int]] = []

    def add_box(self, center, size):
        cx, cy, cz = center
        sx, sy, sz = (size[0] * 0.5, size[1] * 0.5, size[2] * 0.5)
        base = len(self.verts)
        self.verts.extend([
            (cx - sx, cy - sy, cz - sz), (cx + sx, cy - sy, cz - sz),
            (cx + sx, cy + sy, cz - sz), (cx - sx, cy + sy, cz - sz),
            (cx - sx, cy - sy, cz + sz), (cx + sx, cy - sy, cz + sz),
            (cx + sx, cy + sy, cz + sz), (cx - sx, cy + sy, cz + sz),
        ])
        self.faces.extend([
            (base + 0, base + 1, base + 2, base + 3),
            (base + 4, base + 7, base + 6, base + 5),
            (base + 0, base + 4, base + 5, base + 1),
            (base + 1, base + 5, base + 6, base + 2),
            (base + 2, base + 6, base + 7, base + 3),
            (base + 4, base + 0, base + 3, base + 7),
        ])

    def add_cylinder(self, center, radius: float, length: float, *, axis: str = "Z", sides: int = 12):
        cx, cy, cz = center
        half = length * 0.5
        base = len(self.verts)
        for index in range(sides):
            angle = 2.0 * math.pi * index / sides
            a = radius * math.cos(angle)
            b = radius * math.sin(angle)
            if axis == "Z":
                self.verts.extend([(cx + a, cy + b, cz - half), (cx + a, cy + b, cz + half)])
            elif axis == "Y":
                self.verts.extend([(cx + a, cy - half, cz + b), (cx + a, cy + half, cz + b)])
            elif axis == "X":
                self.verts.extend([(cx - half, cy + a, cz + b), (cx + half, cy + a, cz + b)])
            else:
                raise ValueError(axis)
        for index in range(sides):
            nxt = (index + 1) % sides
            self.faces.append((base + index * 2, base + nxt * 2, base + nxt * 2 + 1, base + index * 2 + 1))
        self.faces.append(tuple(base + index * 2 for index in reversed(range(sides))))
        self.faces.append(tuple(base + index * 2 + 1 for index in range(sides)))

    def apply(self, obj: bpy.types.Object) -> None:
        mesh = obj.data
        mesh.clear_geometry()
        mesh.from_pydata(self.verts, [], self.faces)
        mesh.update()
        for poly in mesh.polygons:
            poly.use_smooth = False


# ---------------------------------------------------------------------------
# Piece builders
# ---------------------------------------------------------------------------


def build_helmet_core(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.38, 0.30, 0.27))
    builder.add_box((0.0, -0.06, 0.11), (0.28, 0.15, 0.065))
    builder.add_box((0.0, 0.05, -0.10), (0.27, 0.14, 0.08))


def build_helmet_crown(builder: MeshBuilder):
    builder.add_box((0.0, -0.01, 0.0), (0.25, 0.17, 0.065))
    builder.add_box((0.0, -0.02, 0.05), (0.18, 0.12, 0.035))


def build_helmet_jaw(builder: MeshBuilder):
    builder.add_box((0.0, 0.02, 0.0), (0.25, 0.16, 0.08))


def build_helmet_plate(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.075, 0.18, 0.22))
    builder.add_box((0.0, -0.02, -0.09), (0.06, 0.16, 0.06))


def build_helmet_visor(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.27, 0.04, 0.105))


def build_neck(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.20, 0.18, 0.12))
    builder.add_box((0.0, -0.01, -0.06), (0.16, 0.14, 0.05))


def build_chest_core(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.02), (0.60, 0.34, 0.30))
    builder.add_box((0.0, -0.01, -0.16), (0.52, 0.29, 0.10))
    builder.add_box((0.0, 0.10, 0.10), (0.48, 0.08, 0.13))


def build_upper_chest(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.62, 0.18, 0.08))
    builder.add_box((0.0, -0.02, 0.05), (0.42, 0.12, 0.035))


def build_chest_plate_center(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.24, 0.08, 0.20))


def build_chest_plate_side(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.16, 0.08, 0.18))


def build_waist(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.52, 0.28, 0.14))
    builder.add_box((0.0, 0.0, -0.08), (0.46, 0.24, 0.05))


def build_pelvis(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.02), (0.54, 0.30, 0.20))
    builder.add_box((0.0, 0.08, -0.03), (0.42, 0.08, 0.10))


def build_hip_guard(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.10, 0.06, 0.22))


def build_backpack_core(builder: MeshBuilder):
    builder.add_box((0.0, -0.02, 0.0), (0.34, 0.18, 0.28))
    builder.add_box((0.0, 0.04, 0.08), (0.26, 0.08, 0.10))


def build_backpack_thruster(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.12, 0.10, 0.34))
    builder.add_box((0.0, 0.02, 0.15), (0.08, 0.07, 0.06))


def build_thruster_nozzle(builder: MeshBuilder):
    # A compact rear-facing nozzle centred on its object origin.  Earlier
    # source geometry had an internal offset, which left cyan blocks floating
    # near the waist after the V2 backpack shell was rebuilt.
    builder.add_cylinder((0.0, 0.0, 0.0), 0.050, 0.085, axis="Y")
    builder.add_cylinder((0.0, -0.048, 0.0), 0.036, 0.020, axis="Y")


def build_shoulder_armour(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.21, 0.15, 0.115))
    builder.add_box((0.0, 0.03, -0.07), (0.15, 0.10, 0.055))
    builder.add_box((0.0, -0.05, 0.05), (0.13, 0.05, 0.04))


def build_shoulder_wing(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.15, 0.08, 0.10))


def build_upper_arm(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, -0.035), (0.13, 0.135, 0.39))
    builder.add_box((0.0, 0.0, 0.145), (0.155, 0.145, 0.055))


def build_elbow(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.13, 0.13, 0.12))
    builder.add_cylinder((0.0, 0.0, 0.0), 0.03, 0.16, axis="X")


def build_forearm(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, -0.025), (0.125, 0.13, 0.38))
    builder.add_box((0.0, 0.0, -0.155), (0.145, 0.145, 0.045))
    builder.add_box((0.0, -0.02, 0.135), (0.100, 0.100, 0.045))


def build_forearm_plate(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.10, 0.08, 0.22))


def build_hand(builder: MeshBuilder):
    # Palm / gauntlet.  Keep the same overall hand envelope used by the rig.
    builder.add_box((0.0, 0.0, 0.02), (0.105, 0.11, 0.11))
    builder.add_box((0.0, 0.0, 0.10), (0.12, 0.12, 0.04))
    # Connected tapered finger cluster.  The previous distal block had a visible
    # 7.5 mm air gap, which read as a detached fingertip in the grip close-ups.
    # These two blocks overlap slightly so the hand remains one continuous glove
    # silhouette while still showing a simple articulated step.
    builder.add_box((0.0, 0.00, -0.045), (0.085, 0.088, 0.052))
    builder.add_box((0.0, 0.00, -0.086), (0.070, 0.076, 0.040))
    # Thumb pad: pull it a little toward the palm so it reads as a gripping thumb
    # instead of a separate side cube.
    builder.add_box((0.054, 0.0, -0.018), (0.034, 0.058, 0.050))


def build_upper_leg(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.20, 0.20, 0.38))
    builder.add_box((0.0, -0.01, 0.14), (0.22, 0.22, 0.08))


def build_thigh_plate(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.14, 0.07, 0.22))


def build_knee(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.14, 0.14, 0.12))
    builder.add_cylinder((0.0, 0.0, 0.0), 0.028, 0.15, axis="X")


def build_knee_guard(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.12, 0.07, 0.12))


def build_lower_leg(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.18, 0.18, 0.34))
    builder.add_box((0.0, -0.01, -0.12), (0.20, 0.20, 0.05))


def build_shin_plate(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.12, 0.07, 0.22))


def build_boot(builder: MeshBuilder):
    builder.add_box((0.0, 0.02, 0.0), (0.22, 0.30, 0.18))
    builder.add_box((0.0, -0.05, 0.08), (0.18, 0.16, 0.06))
    builder.add_box((0.0, 0.00, -0.09), (0.24, 0.32, 0.04))


def build_boot_toe(builder: MeshBuilder):
    builder.add_box((0.0, 0.0, 0.0), (0.18, 0.18, 0.10))


BUILDERS = {
    "Helmet_Core": build_helmet_core,
    "Helmet_Crown": build_helmet_crown,
    "Helmet_Jaw": build_helmet_jaw,
    "Helmet_Visor": build_helmet_visor,
    "Neck": build_neck,
    "Chest_Core": build_chest_core,
    "Upper_Chest": build_upper_chest,
    "Chest_Plate": build_chest_plate_center,
    "Waist": build_waist,
    "Pelvis": build_pelvis,
    "Backpack_Core": build_backpack_core,
    "Shoulder_Armour": build_shoulder_armour,
    "Shoulder_Wing": build_shoulder_wing,
    "Upper_Arm": build_upper_arm,
    "Elbow": build_elbow,
    "Forearm": build_forearm,
    "Hand": build_hand,
    "Upper_Leg": build_upper_leg,
    "Lower_Leg": build_lower_leg,
    "Heavy_Boot": build_boot,
}


EXACT_BUILDERS = {
    "Helmet_Plate.L": build_helmet_plate,
    "Helmet_Plate.R": build_helmet_plate,
    "Chest_Plate.L": build_chest_plate_side,
    "Chest_Plate.R": build_chest_plate_side,
    "Hip_Guard.L": build_hip_guard,
    "Hip_Guard.R": build_hip_guard,
    "Backpack_Thruster.L": build_backpack_thruster,
    "Backpack_Thruster.R": build_backpack_thruster,
    "Thruster_Nozzle.L": build_thruster_nozzle,
    "Thruster_Nozzle.R": build_thruster_nozzle,
    "Forearm_Plate.L": build_forearm_plate,
    "Forearm_Plate.R": build_forearm_plate,
    "Thigh_Plate.L": build_thigh_plate,
    "Thigh_Plate.R": build_thigh_plate,
    "Knee.L": build_knee,
    "Knee.R": build_knee,
    "Knee_Guard.L": build_knee_guard,
    "Knee_Guard.R": build_knee_guard,
    "Shin_Plate.L": build_shin_plate,
    "Shin_Plate.R": build_shin_plate,
    "Boot_Toe.L": build_boot_toe,
    "Boot_Toe.R": build_boot_toe,
}


def _builder_for_name(name: str):
    if name in EXACT_BUILDERS:
        return EXACT_BUILDERS[name]
    stem = name
    if stem.endswith(".L") or stem.endswith(".R"):
        stem = stem[:-2]
    return BUILDERS.get(stem)


def _material_for_name(name: str, mats):
    if name == "Helmet_Visor":
        return mats["visor"]
    if name.startswith("Thruster_Nozzle"):
        return mats["accent"]
    if name in {"Hand.L", "Hand.R"}:
        return mats["metal"]
    if name.startswith("Backpack") or name in {"Elbow.L", "Elbow.R", "Knee.L", "Knee.R"}:
        return mats["metal"]
    if name in {"Neck", "Waist"}:
        return mats["dark"]
    if name.startswith("Boot_Toe"):
        return mats["metal"]
    return mats["armor"]


def rebuild_object(obj: bpy.types.Object, mats) -> None:
    builder_fn = _builder_for_name(obj.name)
    if builder_fn is None:
        return
    _clear_object_data(obj)
    builder = MeshBuilder()
    builder_fn(builder)
    builder.apply(obj)
    _assign_material(obj, _material_for_name(obj.name, mats))
    bevel_width = 0.012
    if obj.name.startswith("Heavy_Boot") or obj.name.startswith("Chest") or obj.name.startswith("Pelvis"):
        bevel_width = 0.015
    elif obj.name in {"Helmet_Visor", "Chest_Plate", "Chest_Plate.L", "Chest_Plate.R", "Forearm_Plate.L", "Forearm_Plate.R", "Thigh_Plate.L", "Thigh_Plate.R"}:
        bevel_width = 0.008
    _bevel(obj, bevel_width)


def _reattach_helmet_visor(armature: bpy.types.Object) -> None:
    """Seat Helmet_Visor from an actual Helmet_Core surface hit.

    Test Fix 22 measured only projected bounding boxes.  That could report a
    small numerical gap while the cyan plate still looked detached in profile.
    Here the visor's thin local-Y axis defines its surface normal, and a ray is
    cast from outside the visor back into the evaluated Helmet_Core mesh.  The
    rear visor face is then placed 1.5 mm above that real hit surface.

    Parenting remains unchanged (Head bone); only the visor world translation is
    corrected.  Horizontal/vertical placement is deliberately preserved.
    """
    helmet = bpy.data.objects.get("Helmet_Core")
    visor = bpy.data.objects.get("Helmet_Visor")
    if helmet is None or visor is None or helmet.type != "MESH" or visor.type != "MESH":
        return

    depsgraph = bpy.context.evaluated_depsgraph_get()
    helmet_eval = helmet.evaluated_get(depsgraph)
    visor_eval = visor.evaluated_get(depsgraph)

    visor_points = [visor_eval.matrix_world @ Vector(corner) for corner in visor_eval.bound_box]
    helmet_points = [helmet_eval.matrix_world @ Vector(corner) for corner in helmet_eval.bound_box]
    if not visor_points or not helmet_points:
        return

    visor_center = sum(visor_points, Vector((0.0, 0.0, 0.0))) / len(visor_points)
    helmet_center = sum(helmet_points, Vector((0.0, 0.0, 0.0))) / len(helmet_points)
    normal = (visor_eval.matrix_world.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
    if (visor_center - helmet_center).dot(normal) < 0.0:
        normal = -normal

    # Half-depth measured from the actual evaluated visor bounds along the thin axis.
    half_depth = max(abs((point - visor_center).dot(normal)) for point in visor_points)
    rear_center = visor_center - normal * half_depth

    def helmet_world_bvh() -> BVHTree:
        evaluated = helmet.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
        if mesh is None:
            raise RuntimeError("Could not evaluate Helmet_Core mesh for visor attachment.")
        try:
            world = evaluated.matrix_world
            vertices = [world @ vertex.co for vertex in mesh.vertices]
            polygons = [tuple(int(i) for i in poly.vertices) for poly in mesh.polygons if len(poly.vertices) >= 3]
        finally:
            evaluated.to_mesh_clear()
        if not vertices or not polygons:
            raise RuntimeError("Helmet_Core evaluated mesh is empty during visor attachment.")
        return BVHTree.FromPolygons(vertices, polygons, all_triangles=False, epsilon=1.0e-7)

    bvh = helmet_world_bvh()

    # Start safely outside the visor and cast inward through the visor centreline.
    ray_origin = visor_center + normal * 0.20
    hit, _hit_normal, _face_index, _distance = bvh.ray_cast(ray_origin, -normal, 1.0)
    if hit is None:
        # Fallback to the nearest actual helmet surface to the rear-face centre.
        nearest = bvh.find_nearest(rear_center)
        if nearest is None or nearest[0] is None:
            raise RuntimeError("Helmet visor attachment could not find Helmet_Core surface.")
        hit = nearest[0]

    desired_gap = 0.0015
    target_rear_center = Vector(hit) + normal * desired_gap
    shift = target_rear_center - rear_center

    # The correction should be almost entirely normal to the visor.  Preserve the
    # already-good front-view centering; reject a suspicious lateral solution.
    normal_shift = shift.dot(normal)
    tangent_shift = shift - normal * normal_shift
    if abs(normal_shift) > 0.25 or tangent_shift.length > 0.020:
        raise RuntimeError(
            "Refusing implausible helmet-visor surface correction: "
            f"normal={normal_shift:.3f} m, tangent={tangent_shift.length:.3f} m."
        )

    world = visor.matrix_world.copy()
    world.translation += normal * normal_shift
    visor.matrix_world = world
    visor.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()

    # Re-measure using a fresh ray from the corrected rear-face centre.
    depsgraph = bpy.context.evaluated_depsgraph_get()
    visor_eval = visor.evaluated_get(depsgraph)
    visor_points = [visor_eval.matrix_world @ Vector(corner) for corner in visor_eval.bound_box]
    visor_center = sum(visor_points, Vector((0.0, 0.0, 0.0))) / len(visor_points)
    half_depth = max(abs((point - visor_center).dot(normal)) for point in visor_points)
    rear_center = visor_center - normal * half_depth
    bvh = helmet_world_bvh()
    hit2, _n2, _i2, _d2 = bvh.ray_cast(rear_center + normal * 0.05, -normal, 0.20)
    if hit2 is None:
        nearest = bvh.find_nearest(rear_center)
        if nearest is None or nearest[0] is None:
            raise RuntimeError("Helmet visor post-correction surface measurement failed.")
        hit2 = nearest[0]
    final_gap = (rear_center - Vector(hit2)).dot(normal)
    visor["ps_visor_surface_gap_m"] = float(final_gap)
    visor["ps_visor_attachment_version"] = 3
    if final_gap < -0.001 or final_gap > 0.004:
        raise RuntimeError(
            "Helmet visor did not seat against actual Helmet_Core surface: "
            f"surface gap={final_gap:.4f} m."
        )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main() -> None:
    require_blender_52()
    ensure_object_mode()
    armature = get_armature()
    mats = _materials()

    rebuilt = []
    missing = []
    for name in sorted(SUIT_OBJECTS):
        obj = bpy.data.objects.get(name)
        if obj is None:
            missing.append(name)
            continue
        if obj.type != "MESH":
            continue
        # Preserve all transforms, parent relationships, and object names.
        if obj.parent != armature and name != "RifleRoot":
            pass
        rebuild_object(obj, mats)
        rebuilt.append(name)

    # Seat the cyan visor against the rebuilt helmet before any later code uses
    # it as the authoritative visual-forward reference.
    _reattach_helmet_visor(armature)

    # Reattach each accent nozzle from the evaluated world bounds of its own
    # thruster housing.  Hard-coded local coordinates were unreliable because
    # these objects are bone-parented and inherit the source file's parent
    # inverse.  The front cap of the nozzle now touches the rear housing face.
    _right, forward, up = body_basis(armature)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for side in ("L", "R"):
        thruster = bpy.data.objects.get(f"Backpack_Thruster.{side}")
        nozzle = bpy.data.objects.get(f"Thruster_Nozzle.{side}")
        if thruster is None or nozzle is None:
            continue
        evaluated = thruster.evaluated_get(depsgraph)
        corners = [evaluated.matrix_world @ Vector(corner) for corner in evaluated.bound_box]
        if not corners:
            continue
        center = sum(corners, Vector((0.0, 0.0, 0.0))) / len(corners)
        rear_surface = min(point.dot(forward) for point in corners)
        nozzle_center = center + forward * (rear_surface - center.dot(forward) - 0.043)
        nozzle.matrix_world = (
            Matrix.Translation(nozzle_center)
            @ orientation_with_y_axis(forward, up)
        )
        nozzle.scale = (1.0, 1.0, 1.0)

    bpy.context.view_layer.update()
    path = save_current_blend()

    print("Powered Suit V2 shell rebuilt.")
    print(f"Rebuilt objects: {len(rebuilt)}")
    if missing:
        print("Missing expected suit objects (left untouched because absent in source):")
        for name in missing:
            print(f"  - {name}")
    print("Skeleton, bone names, parenting, and Action names preserved.")
    print("Changes emphasize slimmer shoulders/forearms, clearer joints, and better hands.")
    print(f"Saved: {path}")


if __name__ == "__main__":
    main()
