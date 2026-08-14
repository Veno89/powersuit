"""Pure, deterministic face-semantic policy for weapon/suit clearance.

This module deliberately has no Blender dependency.  Candidate builders, the
Blender clearance audit, and ordinary Python unit tests import the same IDs,
window rules, manifest validation, and hashing functions so the contract
cannot silently drift between authoring and validation.
"""
from __future__ import annotations

import hashlib
import json
import math
import struct
from dataclasses import dataclass
from typing import Iterable, Mapping, Sequence


POLICY_VERSION = "PS_CLEARANCE_FACE_POLICY_V1"
SEMANTIC_SCHEMA = "PS_CLEARANCE_FACE_SEMANTICS_V1"
MANIFEST_SCHEMA = "PS_CLEARANCE_MANIFEST_V1"
MANIFEST_TEXT_NAME = "PS_CLEARANCE_MANIFEST.json"
CONTACT_WINDOW_POLICY_VERSION = "PS_CLEARANCE_CONTACT_WINDOWS_V1"
CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION = (
    "PS_CLEARANCE_CONTACT_WINDOWS_CANDIDATE007_V3"
)
CANDIDATE007_WEAPON_ASSET_ID = "PS_NextGenPrecisionRifle002"
SOURCE_CANDIDATE_SHA256 = (
    "0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55a39e79e1f0f3f84"
)

SUIT_ATTRIBUTE = "ps_clearance_suit_zone_id"
WEAPON_ATTRIBUTE = "ps_clearance_weapon_zone_id"

SUIT_ORDINARY = 0
SUIT_PRIMARY_HAND_RIGHT = 101
SUIT_SUPPORT_HAND_LEFT = 102
SUIT_STOCK_POCKET_RIGHT = 103
SUIT_MAGAZINE_HAND_LEFT = 104
SUIT_BOLT_HAND_RIGHT = 105

WEAPON_ORDINARY = 0
WEAPON_PRIMARY_GRIP = 201
WEAPON_SUPPORT_GRIP = 202
WEAPON_BUTTPAD = 203
WEAPON_MAGAZINE_GRASP = 204
WEAPON_BOLT_HANDLE = 205

SUIT_ZONE_NAMES = {
    SUIT_ORDINARY: "ordinary_forbidden",
    SUIT_PRIMARY_HAND_RIGHT: "primary_hand_right",
    SUIT_SUPPORT_HAND_LEFT: "support_hand_left",
    SUIT_STOCK_POCKET_RIGHT: "stock_pocket_right",
    SUIT_MAGAZINE_HAND_LEFT: "magazine_manipulation_left",
    SUIT_BOLT_HAND_RIGHT: "bolt_manipulation_right",
}
WEAPON_ZONE_NAMES = {
    WEAPON_ORDINARY: "ordinary_forbidden",
    WEAPON_PRIMARY_GRIP: "primary_grip",
    WEAPON_SUPPORT_GRIP: "support_grip",
    WEAPON_BUTTPAD: "buttpad",
    WEAPON_MAGAZINE_GRASP: "magazine_grasp",
    WEAPON_BOLT_HANDLE: "bolt_handle",
}

CONTACT_PAIR_BY_KEY = {
    "primary_grip": (SUIT_PRIMARY_HAND_RIGHT, WEAPON_PRIMARY_GRIP),
    "support_grip": (SUIT_SUPPORT_HAND_LEFT, WEAPON_SUPPORT_GRIP),
    "buttpad": (SUIT_STOCK_POCKET_RIGHT, WEAPON_BUTTPAD),
    "reload_mag": (SUIT_MAGAZINE_HAND_LEFT, WEAPON_MAGAZINE_GRASP),
    "bolt": (SUIT_BOLT_HAND_RIGHT, WEAPON_BOLT_HANDLE),
}
CONTACT_KEY_BY_PAIR = {value: key for key, value in CONTACT_PAIR_BY_KEY.items()}
REQUIRED_CONTACT_KEYS = tuple(CONTACT_PAIR_BY_KEY)

# Contacts are possible only in active weapon actions under the baseline
# policy. Candidate007 has one separately versioned exception for each measured
# grip acquisition/release corridor beside the Ready endpoints of draw and
# sheathe. The firing hand catches/releases before the support hand. Every other
# transition frame and all stowed actions remain absent and therefore fail
# closed.
READY_ACTIONS = frozenset({
    "PS_Aim",
    "PS_Aim_Walk_Backward",
    "PS_Aim_Walk_Forward",
    "PS_Aim_Walk_Left",
    "PS_Aim_Walk_Right",
    "PS_BoltCycle",
    "PS_Hover",
    "PS_Idle",
    "PS_Reload",
    "PS_Run_Forward",
    "PS_Walk",
    "PS_Walk_Backward",
    "PS_Walk_Forward",
    "PS_Walk_Left",
    "PS_Walk_Right",
    "PS_WeaponReady_Idle",
})

CANDIDATE007_TRANSITION_CONTACT_WINDOWS = frozenset({
    ("primary_grip", "PS_Weapon_Draw", 26.75, 30.0),
    ("primary_grip", "PS_Weapon_Sheathe", 1.0, 4.25),
    ("support_grip", "PS_Weapon_Draw", 29.0, 30.0),
    ("support_grip", "PS_Weapon_Sheathe", 1.0, 2.0),
})
CANDIDATE007_STOWED_LEGACY_ACTIONS = frozenset({
    "PS_Idle",
    "PS_Walk",
    "PS_Hover",
})


@dataclass(frozen=True)
class ContactDecision:
    classification: str
    allowed: bool
    reason: str
    contact_key: str | None
    matched_window: dict[str, int | float | str] | None


def canonical_json_bytes(document: object) -> bytes:
    """Return the one canonical byte representation used for evidence hashes."""

    return json.dumps(
        document,
        ensure_ascii=True,
        allow_nan=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def canonical_sha256(document: object) -> str:
    return hashlib.sha256(canonical_json_bytes(document)).hexdigest()


def topology_semantics_sha256(
    triangles: Iterable[Sequence[int]],
    semantic_ids: Iterable[int],
) -> str:
    """Hash triangle connectivity and its matching face semantic IDs.

    Vertex coordinates are deliberately excluded because skinned evaluated
    coordinates change each frame.  Geometry coordinates receive a separate
    hash in the Blender report.
    """

    triangle_list = [tuple(int(index) for index in triangle) for triangle in triangles]
    semantic_list = [int(value) for value in semantic_ids]
    if len(triangle_list) != len(semantic_list):
        raise ValueError("Triangle and semantic-ID counts must match.")
    digest = hashlib.sha256()
    digest.update(struct.pack("<Q", len(triangle_list)))
    for triangle, semantic_id in zip(triangle_list, semantic_list):
        if len(triangle) != 3:
            raise ValueError("Clearance topology must be triangulated.")
        digest.update(struct.pack("<3q", *triangle))
        digest.update(struct.pack("<q", semantic_id))
    return digest.hexdigest()


def evaluated_geometry_sha256(
    vertices: Iterable[Sequence[float]],
    triangles: Iterable[Sequence[int]],
    semantic_ids: Iterable[int],
) -> str:
    """Hash evaluated object-space positions plus topology and semantics."""

    vertex_list = [tuple(float(axis) for axis in vertex) for vertex in vertices]
    triangle_list = [tuple(int(index) for index in triangle) for triangle in triangles]
    semantic_list = [int(value) for value in semantic_ids]
    if len(triangle_list) != len(semantic_list):
        raise ValueError("Triangle and semantic-ID counts must match.")
    digest = hashlib.sha256()
    digest.update(struct.pack("<Q", len(vertex_list)))
    for vertex in vertex_list:
        if len(vertex) != 3 or not all(math.isfinite(axis) for axis in vertex):
            raise ValueError("Evaluated vertices must contain three finite axes.")
        digest.update(struct.pack("<3d", *vertex))
    digest.update(bytes.fromhex(topology_semantics_sha256(triangle_list, semantic_list)))
    return digest.hexdigest()


def semantic_counts(values: Iterable[int]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for value in values:
        key = str(int(value))
        counts[key] = counts.get(key, 0) + 1
    return dict(sorted(counts.items(), key=lambda item: int(item[0])))


def _window_errors(
    contact_key: str,
    windows: object,
    contact_window_policy_version: str = CONTACT_WINDOW_POLICY_VERSION,
) -> list[str]:
    errors: list[str] = []
    if not isinstance(windows, list) or not windows:
        return [f"contact_windows.{contact_key} must be a non-empty list"]
    for index, window in enumerate(windows):
        label = f"contact_windows.{contact_key}[{index}]"
        if not isinstance(window, Mapping):
            errors.append(f"{label} must be an object")
            continue
        if set(window) != {"action", "start", "end"}:
            errors.append(f"{label} must contain exactly action/start/end")
            continue
        action = window["action"]
        start = window["start"]
        end = window["end"]
        if not isinstance(action, str) or not action:
            errors.append(f"{label}.action must be a non-empty string")
            continue
        if (
            not isinstance(start, (int, float))
            or isinstance(start, bool)
            or not isinstance(end, (int, float))
            or isinstance(end, bool)
            or not math.isfinite(float(start))
            or not math.isfinite(float(end))
            or float(start) > float(end)
        ):
            errors.append(f"{label} must have finite start <= end")
            continue
        if (
            contact_window_policy_version
            == CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION
            and action in CANDIDATE007_STOWED_LEGACY_ACTIONS
        ):
            errors.append(
                f"{label}.action carries Candidate007 stowed and cannot authorize contact"
            )
        elif contact_key == "reload_mag":
            if action != "PS_Reload" or float(start) < 25.0 or float(end) > 75.0:
                errors.append(f"{label} must stay within PS_Reload frames 25-75")
        elif contact_key == "bolt":
            if action != "PS_BoltCycle" or float(start) < 4.0 or float(end) > 16.0:
                errors.append(f"{label} must stay within PS_BoltCycle frames 4-16")
        elif action not in READY_ACTIONS:
            candidate007_transition_contact = (
                contact_window_policy_version
                == CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION
                and (
                    contact_key,
                    action,
                    float(start),
                    float(end),
                )
                in CANDIDATE007_TRANSITION_CONTACT_WINDOWS
            )
            if not candidate007_transition_contact:
                errors.append(f"{label}.action is not an active ready-family action")
        elif (
            contact_key == "primary_grip"
            and action == "PS_BoltCycle"
            and float(start) <= 16.0
            and float(end) >= 4.0
        ):
            errors.append(
                f"{label} overlaps PS_BoltCycle frames 4-16 while the right hand manipulates the bolt"
            )
        elif (
            contact_key == "support_grip"
            and action == "PS_Reload"
            and float(start) <= 75.0
            and float(end) >= 25.0
        ):
            errors.append(
                f"{label} overlaps PS_Reload frames 25-75 while the left hand manipulates the magazine"
            )
    return errors


def validate_manifest(manifest: object) -> list[str]:
    """Return every structural/policy error; an empty list means valid."""

    if not isinstance(manifest, Mapping):
        return ["clearance manifest must be a JSON object"]
    errors: list[str] = []
    expected_scalars = {
        "schema_version": MANIFEST_SCHEMA,
        "policy_version": POLICY_VERSION,
        "semantic_schema": SEMANTIC_SCHEMA,
    }
    for key, expected in expected_scalars.items():
        if manifest.get(key) != expected:
            errors.append(f"{key} must equal {expected}")
    for key in ("suit_asset_id", "weapon_asset_id", "source_candidate_sha256"):
        value = manifest.get(key)
        if not isinstance(value, str) or not value:
            errors.append(f"{key} must be a non-empty string")
    source_hash = manifest.get("source_candidate_sha256")
    if source_hash != SOURCE_CANDIDATE_SHA256:
        errors.append(
            "source_candidate_sha256 must match the hash-pinned Candidate005 source"
        )

    declared_contact_window_policy = manifest.get("contact_window_policy_version")
    if declared_contact_window_policy is None:
        contact_window_policy_version = CONTACT_WINDOW_POLICY_VERSION
    elif declared_contact_window_policy not in {
        CONTACT_WINDOW_POLICY_VERSION,
        CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION,
    }:
        contact_window_policy_version = CONTACT_WINDOW_POLICY_VERSION
        errors.append(
            "contact_window_policy_version must name a supported contact-window policy"
        )
    else:
        contact_window_policy_version = str(declared_contact_window_policy)
    if (
        contact_window_policy_version
        == CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION
        and manifest.get("weapon_asset_id") != CANDIDATE007_WEAPON_ASSET_ID
    ):
        errors.append(
            "Candidate007 contact-window policy is restricted to "
            f"{CANDIDATE007_WEAPON_ASSET_ID}"
        )

    windows = manifest.get("contact_windows")
    if not isinstance(windows, Mapping):
        errors.append("contact_windows must be an object")
    else:
        unknown = sorted(set(windows) - set(REQUIRED_CONTACT_KEYS))
        if unknown:
            errors.append(f"contact_windows has unknown keys: {', '.join(unknown)}")
        for contact_key in REQUIRED_CONTACT_KEYS:
            errors.extend(
                _window_errors(
                    contact_key,
                    windows.get(contact_key),
                    contact_window_policy_version,
                )
            )
        if (
            contact_window_policy_version
            == CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION
        ):
            actual_windows: set[tuple[str, str, float, float]] = set()
            for contact_key in ("primary_grip", "support_grip"):
                contact_windows = windows.get(contact_key)
                if not isinstance(contact_windows, list):
                    continue
                for window in contact_windows:
                    if not isinstance(window, Mapping):
                        continue
                    action = window.get("action")
                    start = window.get("start")
                    end = window.get("end")
                    if not isinstance(action, str) or not action:
                        continue
                    if (
                        not isinstance(start, (int, float))
                        or isinstance(start, bool)
                        or not isinstance(end, (int, float))
                        or isinstance(end, bool)
                        or not math.isfinite(float(start))
                        or not math.isfinite(float(end))
                    ):
                        continue
                    actual_windows.add(
                        (contact_key, action, float(start), float(end))
                    )
            missing_transition_contacts = sorted(
                CANDIDATE007_TRANSITION_CONTACT_WINDOWS - actual_windows
            )
            if missing_transition_contacts:
                errors.append(
                    "Candidate007 contact-window policy is missing exact transition "
                    f"contact windows: {missing_transition_contacts}"
                )

    objects = manifest.get("objects")
    if not isinstance(objects, list) or not objects:
        errors.append("objects must be a non-empty list")
    else:
        names: set[str] = set()
        roles: set[str] = set()
        required_object_keys = {
            "name",
            "asset_role",
            "semantic_attribute",
            "face_count",
            "topology_sha256",
            "semantic_counts",
        }
        for index, entry in enumerate(objects):
            label = f"objects[{index}]"
            if not isinstance(entry, Mapping):
                errors.append(f"{label} must be an object")
                continue
            missing = sorted(required_object_keys - set(entry))
            if missing:
                errors.append(f"{label} missing: {', '.join(missing)}")
                continue
            name = entry["name"]
            role = entry["asset_role"]
            attribute = entry["semantic_attribute"]
            if not isinstance(name, str) or not name:
                errors.append(f"{label}.name must be a non-empty string")
            elif name in names:
                errors.append(f"duplicate manifest object name: {name}")
            else:
                names.add(name)
            if role not in {"suit", "weapon"}:
                errors.append(f"{label}.asset_role must be suit or weapon")
            else:
                roles.add(role)
            expected_attribute = SUIT_ATTRIBUTE if role == "suit" else WEAPON_ATTRIBUTE
            if attribute != expected_attribute:
                errors.append(f"{label}.semantic_attribute must equal {expected_attribute}")
            face_count = entry["face_count"]
            if not isinstance(face_count, int) or isinstance(face_count, bool) or face_count <= 0:
                errors.append(f"{label}.face_count must be a positive integer")
            topology_hash = entry["topology_sha256"]
            if (
                not isinstance(topology_hash, str)
                or len(topology_hash) != 64
                or any(char not in "0123456789abcdef" for char in topology_hash)
            ):
                errors.append(f"{label}.topology_sha256 must be SHA-256 hex")
            counts = entry["semantic_counts"]
            if not isinstance(counts, Mapping) or not counts:
                errors.append(f"{label}.semantic_counts must be a non-empty object")
            elif any(not isinstance(value, int) or value < 0 for value in counts.values()):
                errors.append(f"{label}.semantic_counts values must be non-negative integers")
            elif isinstance(face_count, int) and sum(counts.values()) != face_count:
                errors.append(f"{label}.semantic_counts must sum to face_count")
            if isinstance(counts, Mapping) and role in {"suit", "weapon"}:
                allowed_ids = SUIT_ZONE_NAMES if role == "suit" else WEAPON_ZONE_NAMES
                try:
                    count_ids = {int(key) for key in counts}
                except (TypeError, ValueError):
                    errors.append(f"{label}.semantic_counts keys must be integer strings")
                else:
                    if any(str(value) not in counts for value in count_ids):
                        errors.append(
                            f"{label}.semantic_counts keys must be canonical integer strings"
                        )
                    unknown_ids = sorted(count_ids - set(allowed_ids))
                    if unknown_ids:
                        errors.append(
                            f"{label}.semantic_counts has unknown IDs: {unknown_ids}"
                        )
        missing_roles = sorted({"suit", "weapon"} - roles)
        if missing_roles:
            errors.append(f"objects lacks required asset roles: {', '.join(missing_roles)}")
    return errors


def find_matching_window(
    contact_key: str,
    action_name: str,
    frame: float,
    contact_windows: Mapping[str, object] | None,
) -> dict[str, int | float | str] | None:
    if not isinstance(contact_windows, Mapping):
        return None
    windows = contact_windows.get(contact_key)
    if not isinstance(windows, list):
        return None
    for window in windows:
        if not isinstance(window, Mapping):
            continue
        action = window.get("action")
        start = window.get("start")
        end = window.get("end")
        if (
            action == action_name
            and isinstance(start, (int, float))
            and not isinstance(start, bool)
            and isinstance(end, (int, float))
            and not isinstance(end, bool)
            and float(start) <= float(frame) <= float(end)
        ):
            return {"action": action_name, "start": start, "end": end}
    return None


def classify_face_contact(
    action_name: str,
    frame: float,
    suit_zone_id: int,
    weapon_zone_id: int,
    contact_windows: Mapping[str, object] | None,
    *,
    containment: bool = False,
    metadata_valid: bool = True,
) -> ContactDecision:
    """Classify one actual intersecting triangle-face semantic pair."""

    if containment:
        return ContactDecision(
            "forbidden_containment",
            False,
            "Containment is always forbidden, including compatible contact zones.",
            None,
            None,
        )
    if not metadata_valid:
        return ContactDecision(
            "forbidden_invalid_clearance_metadata",
            False,
            "Clearance metadata or its source manifest is missing or inconsistent.",
            None,
            None,
        )
    if suit_zone_id not in SUIT_ZONE_NAMES or weapon_zone_id not in WEAPON_ZONE_NAMES:
        return ContactDecision(
            "forbidden_unknown_face_semantic",
            False,
            "An intersecting face carries an unknown or missing semantic ID.",
            None,
            None,
        )
    contact_key = CONTACT_KEY_BY_PAIR.get((suit_zone_id, weapon_zone_id))
    if contact_key is None:
        return ContactDecision(
            "forbidden_incompatible_face_semantics",
            False,
            "Intersecting suit and weapon faces are not a compatible contact pair.",
            None,
            None,
        )
    window = find_matching_window(contact_key, action_name, frame, contact_windows)
    if window is None:
        return ContactDecision(
            f"forbidden_{contact_key}_outside_authored_window",
            False,
            "Compatible contact faces intersect outside their explicit action/frame window.",
            contact_key,
            None,
        )
    return ContactDecision(
        f"allowed_{contact_key}_contact",
        True,
        "Compatible face semantics intersect inside their explicit authored contact window.",
        contact_key,
        window,
    )
