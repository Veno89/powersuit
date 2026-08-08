# pyright: reportMissingImports=false
"""Reusable rigid-weapon handling contract for the Powered Suit prototype.

Blender 5.2 pipeline responsibilities:
- declare weapon helper roles independent of object names
- declare reusable stance-family limits
- validate a weapon's hardpoint contract
- freeze and verify rigid weapon-child transforms

This module does not model, pose, render, or export anything by itself.
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore

CONTRACT_VERSION = 1
RIGID_SIGNATURE_VERSION = 4

ROLE_PRIMARY_GRIP = "primary_grip"
ROLE_SUPPORT_GRIP = "support_grip"
ROLE_STOCK_CONTACT = "stock_contact"
ROLE_SIGHT_OCULAR = "sight_ocular"
ROLE_MUZZLE = "muzzle"
ROLE_SUPPORT_MIN = "support_grip_min"
ROLE_SUPPORT_MAX = "support_grip_max"

REQUIRED_ROLES = (
    ROLE_PRIMARY_GRIP,
    ROLE_SUPPORT_GRIP,
    ROLE_STOCK_CONTACT,
    ROLE_SIGHT_OCULAR,
    ROLE_MUZZLE,
)

COMPONENT_OPTIC = "optic"
COMPONENT_STOCK = "stock"
COMPONENT_PRIMARY_GRIP = "primary_grip"
COMPONENT_SUPPORT_GRIP = "support_grip"


@dataclass(frozen=True)
class StanceProfile:
    name: str
    weapon_family: str
    spine_pitch_deg: float
    chest_pitch_deg: float
    chest_yaw_deg: float
    trigger_shoulder_forward_deg: float
    support_shoulder_forward_deg: float
    weapon_pitch_deg: float
    stock_inward_m: float
    stock_forward_m: float
    stock_up_m: float
    preferred_right_reach: float
    preferred_left_reach: float
    max_reach: float
    head_yaw_limit_deg: float
    head_pitch_limit_deg: float
    head_roll_limit_deg: float
    sight_lateral_tolerance_m: float
    sight_vertical_tolerance_m: float
    sight_front_min_m: float
    sight_front_max_m: float


STANCE_PROFILES = {
    "shouldered_precision": StanceProfile(
        name="shouldered_precision",
        weapon_family="long_gun",
        spine_pitch_deg=-2.0,
        chest_pitch_deg=-4.0,
        chest_yaw_deg=4.0,
        trigger_shoulder_forward_deg=11.0,
        support_shoulder_forward_deg=21.0,
        weapon_pitch_deg=2.0,
        # The buttpad seats slightly inboard of the outer shoulder armour. This is
        # a character-stance decision, not a per-weapon deformation search.
        stock_inward_m=0.080,
        stock_forward_m=0.010,
        stock_up_m=0.008,
        preferred_right_reach=0.72,
        preferred_left_reach=0.82,
        max_reach=1.000,
        # Powered-suit helmet only settles toward the optic. The weapon never moves
        # to chase an exact eye point.
        head_yaw_limit_deg=3.0,
        head_pitch_limit_deg=1.5,
        head_roll_limit_deg=12.0,
        # A real sighting envelope: the helmet may settle toward the optic, but
        # a visibly side-by-side scope/visor relationship remains a blocker.
        sight_lateral_tolerance_m=0.075,
        sight_vertical_tolerance_m=0.120,
        sight_front_min_m=0.015,
        sight_front_max_m=0.200,
    ),
}


def get_stance_profile(name: str) -> StanceProfile:
    profile = STANCE_PROFILES.get(name)
    if profile is None:
        raise RuntimeError(f"Unknown weapon stance family '{name}'.")
    return profile


def tag_weapon_root(root: bpy.types.Object, *, weapon_id: str, stance_family: str) -> None:
    profile = get_stance_profile(stance_family)
    root["ps_weapon_contract_version"] = CONTRACT_VERSION
    root["ps_weapon_id"] = weapon_id
    root["ps_weapon_family"] = profile.weapon_family
    root["ps_weapon_stance_family"] = profile.name
    root["ps_weapon_rigid"] = True
    root["ps_weapon_active"] = True
    root["ps_weapon_forward_axis"] = "+Y"
    root["ps_weapon_up_axis"] = "+Z"



def weapon_roots() -> list[bpy.types.Object]:
    """Return every object that declares this weapon contract version."""
    return sorted(
        [
            obj for obj in bpy.data.objects
            if int(obj.get("ps_weapon_contract_version", 0)) == CONTRACT_VERSION
        ],
        key=lambda obj: obj.name,
    )


def get_active_weapon_root() -> bpy.types.Object:
    """Resolve the one active weapon root for future multi-weapon scenes.

    Current builds contain only RifleRoot, but future weapon generators can use
    the same contract. Exactly one root should carry ps_weapon_active=True.
    """
    roots = weapon_roots()
    active = [obj for obj in roots if bool(obj.get("ps_weapon_active", False))]
    if len(active) == 1:
        return active[0]
    if not active and len(roots) == 1:
        return roots[0]
    if not roots:
        raise RuntimeError("No weapon root declares the Powered Suit weapon contract.")
    names = active if active else roots
    raise RuntimeError(
        "Exactly one weapon root must be active; candidates: "
        + ", ".join(obj.name for obj in names)
    )


def set_active_weapon(root: bpy.types.Object) -> None:
    if int(root.get("ps_weapon_contract_version", 0)) != CONTRACT_VERSION:
        raise RuntimeError(f"'{root.name}' does not use weapon contract v{CONTRACT_VERSION}.")
    for candidate in weapon_roots():
        candidate["ps_weapon_active"] = candidate == root


def tag_helper(obj: bpy.types.Object, role: str) -> None:
    if role not in {
        ROLE_PRIMARY_GRIP,
        ROLE_SUPPORT_GRIP,
        ROLE_STOCK_CONTACT,
        ROLE_SIGHT_OCULAR,
        ROLE_MUZZLE,
        ROLE_SUPPORT_MIN,
        ROLE_SUPPORT_MAX,
    }:
        raise ValueError(f"Unknown weapon-helper role '{role}'.")
    obj["ps_weapon_helper_role"] = role


def tag_component(obj: bpy.types.Object, role: str) -> None:
    obj["ps_weapon_component_role"] = role


def weapon_helpers(root: bpy.types.Object) -> dict[str, bpy.types.Object]:
    helpers: dict[str, bpy.types.Object] = {}
    for child in root.children:
        role = str(child.get("ps_weapon_helper_role", ""))
        if not role:
            continue
        if role in helpers:
            raise RuntimeError(
                f"Weapon '{root.name}' has duplicate helper role '{role}': "
                f"'{helpers[role].name}' and '{child.name}'."
            )
        helpers[role] = child
    return helpers


def require_weapon_helper(root: bpy.types.Object, role: str) -> bpy.types.Object:
    helper = weapon_helpers(root).get(role)
    if helper is None:
        raise RuntimeError(
            f"Weapon '{root.name}' is missing required helper role '{role}'."
        )
    return helper


def weapon_components(root: bpy.types.Object, role: str) -> list[bpy.types.Object]:
    return [
        child for child in root.children
        if child.type == "MESH" and str(child.get("ps_weapon_component_role", "")) == role
    ]


RIGID_TRANSFORM_DECIMALS = 6
RIGID_VERTEX_DECIMALS = 6
RIGID_RUNTIME_TOLERANCE = 1.0e-4
RIGID_MANIFEST_PROPERTY = "ps_weapon_rigid_manifest_json"
RIGID_AUTHORED_MATRIX_PROPERTY = "ps_weapon_authored_matrix_v4"
RIGID_MESH_BAKED_PROPERTY = "ps_weapon_mesh_transform_baked_v4"


def _canonical_float(value: float, decimals: int) -> float:
    """Round a float and collapse signed zero for deterministic serialisation."""
    result = round(float(value), decimals)
    return 0.0 if result == 0.0 else result


def _matrix_values(matrix: Matrix) -> tuple[float, ...]:
    return tuple(
        _canonical_float(matrix[row][column], RIGID_TRANSFORM_DECIMALS)
        for row in range(4)
        for column in range(4)
    )


def _matrix_from_values(values) -> Matrix:
    raw = [float(value) for value in values]
    if len(raw) != 16:
        raise ValueError(f"Expected 16 matrix values, got {len(raw)}.")
    return Matrix((
        raw[0:4],
        raw[4:8],
        raw[8:12],
        raw[12:16],
    ))


def _effective_local_matrix(
    root: bpy.types.Object,
    child: bpy.types.Object,
) -> Matrix:
    """Return the child's visible transform in weapon-root space."""
    return root.matrix_world.inverted_safe() @ child.matrix_world


def _store_authored_matrix(child: bpy.types.Object, matrix: Matrix) -> None:
    child[RIGID_AUTHORED_MATRIX_PROPERTY] = list(_matrix_values(matrix))


def _authored_matrix(child: bpy.types.Object) -> Matrix | None:
    values = child.get(RIGID_AUTHORED_MATRIX_PROPERTY)
    if values is None:
        return None
    try:
        return _matrix_from_values(values)
    except Exception as error:
        raise RuntimeError(
            f"Weapon child '{child.name}' has an invalid authored transform property."
        ) from error


def weapon_local_matrix(
    root: bpy.types.Object,
    child: bpy.types.Object,
) -> Matrix:
    """Return the authored semantic transform in weapon-root space.

    Mesh children in rigid-framework v4 have their authored placement baked into
    mesh vertices and therefore sit at object-space identity under RifleRoot.
    Their semantic centre/orientation is retained in a custom authored matrix so
    stance and validation code can still query the intended grip/scope/stock
    location. Helper empties keep their authored transform directly.
    """
    authored = _authored_matrix(child)
    if authored is not None:
        return authored.copy()
    bpy.context.view_layer.update()
    return _effective_local_matrix(root, child)


def weapon_local_position(
    root: bpy.types.Object,
    child: bpy.types.Object,
) -> Vector:
    """Public semantic child position in weapon-root space."""
    return weapon_local_matrix(root, child).translation.copy()


def _matrix_max_abs_delta(first: Matrix, second: Matrix) -> float:
    return max(
        abs(float(first[row][column]) - float(second[row][column]))
        for row in range(4)
        for column in range(4)
    )


def normalize_rigid_weapon_children(root: bpy.types.Object) -> None:
    """Canonicalise a generated rigid weapon before freezing it.

    Blender may rewrite object parenting matrices while saving/loading. That is
    harmless visually but made earlier rigidity hashes unstable. Framework v4
    removes that representation from rifle mesh parts altogether:

    * each mesh child's authored root-space matrix is baked into its mesh data;
    * the mesh object's transform becomes identity beneath RifleRoot;
    * each helper keeps an authored root-space matrix;
    * semantic authored matrices are stored as stable custom data.

    The weapon therefore remains editable as separate named mesh objects, but
    pose/animation code cannot move those objects without the runtime identity
    check failing.
    """
    if root.parent is not None:
        raise RuntimeError(
            "Rigid weapon children must be normalised while RifleRoot is independent."
        )
    bpy.context.view_layer.update()

    for child in sorted(root.children, key=lambda item: item.name):
        authored = _effective_local_matrix(root, child).copy()
        _store_authored_matrix(child, authored)

        if child.type == "MESH" and child.data is not None:
            # Generated rifle meshes are unique, but copy defensively if a future
            # asset shares mesh data so baking cannot affect unrelated objects.
            if child.data.users > 1:
                child.data = child.data.copy()
            child.data.transform(authored)
            child.data.update()
            child.matrix_parent_inverse = Matrix.Identity(4)
            child.matrix_basis = Matrix.Identity(4)
            child[RIGID_MESH_BAKED_PROPERTY] = True
        else:
            # Helper empties remain visible at their authored hardpoint transform.
            child.matrix_parent_inverse = Matrix.Identity(4)
            child.matrix_basis = authored
            child[RIGID_MESH_BAKED_PROPERTY] = False

    bpy.context.view_layer.update()

    identity = Matrix.Identity(4)
    failures: list[str] = []
    for child in sorted(root.children, key=lambda item: item.name):
        actual = _effective_local_matrix(root, child)
        if child.type == "MESH":
            delta = _matrix_max_abs_delta(actual, identity)
            if delta > RIGID_RUNTIME_TOLERANCE:
                failures.append(f"{child.name}: mesh identity delta={delta:.3e}")
        else:
            authored = _authored_matrix(child)
            if authored is None:
                failures.append(f"{child.name}: missing authored helper transform")
                continue
            delta = _matrix_max_abs_delta(actual, authored)
            if delta > RIGID_RUNTIME_TOLERANCE:
                failures.append(f"{child.name}: helper delta={delta:.3e}")
    if failures:
        raise RuntimeError(
            "Weapon canonicalisation failed: " + "; ".join(failures[:6])
        )


def _mesh_signature(mesh: bpy.types.Mesh) -> str:
    digest = hashlib.sha256()
    digest.update(f"v={len(mesh.vertices)}|p={len(mesh.polygons)}|".encode("ascii"))
    for vertex in mesh.vertices:
        co = vertex.co
        digest.update(
            (
                f"{_canonical_float(co.x, RIGID_VERTEX_DECIMALS):.{RIGID_VERTEX_DECIMALS}f},"
                f"{_canonical_float(co.y, RIGID_VERTEX_DECIMALS):.{RIGID_VERTEX_DECIMALS}f},"
                f"{_canonical_float(co.z, RIGID_VERTEX_DECIMALS):.{RIGID_VERTEX_DECIMALS}f};"
            ).encode("ascii")
        )
    return digest.hexdigest()


def _modifier_signature(obj: bpy.types.Object) -> tuple[tuple[object, ...], ...]:
    """Capture generated modifier settings that can change visible weapon shape."""
    result: list[tuple[object, ...]] = []
    for modifier in obj.modifiers:
        if modifier.type == "BEVEL":
            result.append((
                modifier.name,
                modifier.type,
                bool(modifier.show_viewport),
                _canonical_float(modifier.width, RIGID_TRANSFORM_DECIMALS),
                int(modifier.segments),
                str(modifier.limit_method),
            ))
        else:
            result.append((
                modifier.name,
                modifier.type,
                bool(modifier.show_viewport),
            ))
    return tuple(result)


def compute_rigid_manifest(root: bpy.types.Object) -> dict[str, object]:
    """Create the stable authored rigid-asset manifest.

    The signature intentionally contains authored semantic transforms, mesh data,
    and modifiers—not Blender's mutable parenting matrix representation. Runtime
    child transforms are checked separately by assert_weapon_rigid().
    """
    bpy.context.view_layer.update()
    children: list[dict[str, object]] = []
    for child in sorted(root.children, key=lambda item: item.name):
        authored = _authored_matrix(child)
        if authored is None:
            raise RuntimeError(
                f"Weapon child '{child.name}' has not been canonicalised/frozen."
            )
        entry: dict[str, object] = {
            "name": child.name,
            "type": child.type,
            "authored_matrix": list(_matrix_values(authored)),
            "mesh_transform_baked": bool(child.get(RIGID_MESH_BAKED_PROPERTY, False)),
            "modifiers": [list(values) for values in _modifier_signature(child)],
        }
        if child.type == "MESH" and child.data is not None:
            mesh = child.data
            entry["mesh_vertices"] = len(mesh.vertices)
            entry["mesh_polygons"] = len(mesh.polygons)
            entry["mesh_signature"] = _mesh_signature(mesh)
        children.append(entry)
    return {
        "version": RIGID_SIGNATURE_VERSION,
        "transform_decimals": RIGID_TRANSFORM_DECIMALS,
        "vertex_decimals": RIGID_VERTEX_DECIMALS,
        "children": children,
    }


def _manifest_json(manifest: dict[str, object]) -> str:
    return json.dumps(manifest, sort_keys=True, separators=(",", ":"))


def compute_rigid_signature(root: bpy.types.Object) -> str:
    manifest = compute_rigid_manifest(root)
    return hashlib.sha256(_manifest_json(manifest).encode("utf-8")).hexdigest()


def freeze_rigid_weapon(root: bpy.types.Object) -> str:
    manifest = compute_rigid_manifest(root)
    signature = hashlib.sha256(_manifest_json(manifest).encode("utf-8")).hexdigest()
    root["ps_weapon_rigid_signature_version"] = RIGID_SIGNATURE_VERSION
    root["ps_weapon_rigid_signature"] = signature
    root["ps_weapon_rigid_signature_precision"] = RIGID_TRANSFORM_DECIMALS
    root[RIGID_MANIFEST_PROPERTY] = _manifest_json(manifest)
    return signature


def _manifest_difference_summary(
    expected: dict[str, object],
    actual: dict[str, object],
) -> str:
    expected_children = {
        str(entry.get("name")): entry
        for entry in expected.get("children", [])
        if isinstance(entry, dict)
    }
    actual_children = {
        str(entry.get("name")): entry
        for entry in actual.get("children", [])
        if isinstance(entry, dict)
    }
    messages: list[str] = []
    missing = sorted(set(expected_children) - set(actual_children))
    added = sorted(set(actual_children) - set(expected_children))
    if missing:
        messages.append("missing children=" + ", ".join(missing[:4]))
    if added:
        messages.append("added children=" + ", ".join(added[:4]))

    for name in sorted(set(expected_children) & set(actual_children)):
        before = expected_children[name]
        after = actual_children[name]
        changed: list[str] = []
        if before.get("type") != after.get("type"):
            changed.append("type")
        if before.get("authored_matrix") != after.get("authored_matrix"):
            changed.append("authored transform")
        if before.get("mesh_transform_baked") != after.get("mesh_transform_baked"):
            changed.append("mesh transform mode")
        if before.get("mesh_signature") != after.get("mesh_signature"):
            changed.append("mesh geometry")
        if before.get("modifiers") != after.get("modifiers"):
            changed.append("modifiers")
        if changed:
            messages.append(f"{name}: " + ", ".join(changed))
        if len(messages) >= 6:
            break
    return "; ".join(messages) if messages else "manifest differs"


def _runtime_transform_difference(root: bpy.types.Object) -> str | None:
    identity = Matrix.Identity(4)
    for child in sorted(root.children, key=lambda item: item.name):
        authored = _authored_matrix(child)
        if authored is None:
            return f"{child.name}: missing authored transform"
        actual = _effective_local_matrix(root, child)
        if child.type == "MESH":
            if not bool(child.get(RIGID_MESH_BAKED_PROPERTY, False)):
                return f"{child.name}: mesh is not marked transform-baked"
            delta = _matrix_max_abs_delta(actual, identity)
            if delta > RIGID_RUNTIME_TOLERANCE:
                return f"{child.name}: mesh child moved (identity delta={delta:.3e})"
        else:
            delta = _matrix_max_abs_delta(actual, authored)
            if delta > RIGID_RUNTIME_TOLERANCE:
                return f"{child.name}: helper moved (delta={delta:.3e})"
    return None


def assert_weapon_rigid(root: bpy.types.Object) -> str:
    if not bool(root.get("ps_weapon_rigid", False)):
        raise RuntimeError(f"Weapon '{root.name}' is not marked rigid.")
    expected = str(root.get("ps_weapon_rigid_signature", ""))
    if not expected:
        raise RuntimeError(
            f"Weapon '{root.name}' has no frozen rigid-child signature."
        )

    actual_manifest = compute_rigid_manifest(root)
    actual_json = _manifest_json(actual_manifest)
    actual = hashlib.sha256(actual_json.encode("utf-8")).hexdigest()
    if actual != expected:
        detail = ""
        expected_manifest_json = str(root.get(RIGID_MANIFEST_PROPERTY, ""))
        if expected_manifest_json:
            try:
                expected_manifest = json.loads(expected_manifest_json)
                detail = _manifest_difference_summary(expected_manifest, actual_manifest)
            except Exception:
                detail = "stored manifest could not be decoded"
        raise RuntimeError(
            "Rigid weapon asset definition changed after construction. Animation may "
            "only move RifleRoot; weapon child geometry/authored hardpoints are frozen. "
            f"Expected signature {expected[:16]}, got {actual[:16]}. "
            f"Difference: {detail or 'unavailable'}."
        )

    runtime_difference = _runtime_transform_difference(root)
    if runtime_difference is not None:
        raise RuntimeError(
            "Rigid weapon runtime transform changed after construction. Animation may "
            "only move RifleRoot. Difference: " + runtime_difference + "."
        )
    return actual

def validate_weapon_contract(
    root: bpy.types.Object,
    *,
    require_independent: bool = False,
) -> dict[str, object]:
    version = int(root.get("ps_weapon_contract_version", 0))
    if version != CONTRACT_VERSION:
        raise RuntimeError(
            f"Weapon contract version {version} is unsupported; expected {CONTRACT_VERSION}."
        )
    stance_name = str(root.get("ps_weapon_stance_family", ""))
    profile = get_stance_profile(stance_name)
    helpers = weapon_helpers(root)
    missing = [role for role in REQUIRED_ROLES if role not in helpers]
    if missing:
        raise RuntimeError("Weapon contract is incomplete: " + ", ".join(missing))
    if require_independent and root.parent is not None:
        raise RuntimeError("Weapon root must remain independent before pose solving.")
    if root.matrix_world.to_3x3().determinant() <= 0.0:
        raise RuntimeError("Weapon root has a reflected transform.")
    if any(float(value) <= 0.0 for value in root.scale):
        raise RuntimeError(f"Weapon root has non-positive scale: {tuple(root.scale)}")

    # Helpers must be direct children so their local transforms are part of the
    # rigid asset contract and cannot become hidden animation controls.
    for role in REQUIRED_ROLES:
        helper = helpers[role]
        if helper.parent != root:
            raise RuntimeError(
                f"Helper '{helper.name}' ({role}) is not a direct child of '{root.name}'."
            )

    primary = weapon_local_position(root, helpers[ROLE_PRIMARY_GRIP])
    support = weapon_local_position(root, helpers[ROLE_SUPPORT_GRIP])
    stock = weapon_local_position(root, helpers[ROLE_STOCK_CONTACT])
    sight = weapon_local_position(root, helpers[ROLE_SIGHT_OCULAR])
    muzzle = weapon_local_position(root, helpers[ROLE_MUZZLE])
    if muzzle.y <= primary.y:
        raise RuntimeError("Weapon muzzle helper is not forward of the primary grip.")
    if stock.y >= primary.y:
        raise RuntimeError("Weapon stock-contact helper is not behind the primary grip.")
    if sight.z <= primary.z:
        raise RuntimeError("Weapon sight helper is not above the primary grip.")

    assert_weapon_rigid(root)
    return {
        "contract_version": version,
        "weapon_id": str(root.get("ps_weapon_id", "")),
        "weapon_family": str(root.get("ps_weapon_family", "")),
        "stance_family": profile.name,
        "primary_grip_local": tuple(float(v) for v in primary),
        "support_grip_local": tuple(float(v) for v in support),
        "stock_contact_local": tuple(float(v) for v in stock),
        "sight_ocular_local": tuple(float(v) for v in sight),
        "muzzle_local": tuple(float(v) for v in muzzle),
    }
