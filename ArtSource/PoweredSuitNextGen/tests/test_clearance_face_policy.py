from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from clearance_face_policy import (  # noqa: E402
    MANIFEST_SCHEMA,
    POLICY_VERSION,
    SEMANTIC_SCHEMA,
    SOURCE_CANDIDATE_SHA256,
    SUIT_ATTRIBUTE,
    SUIT_MAGAZINE_HAND_LEFT,
    SUIT_PRIMARY_HAND_RIGHT,
    SUIT_STOCK_POCKET_RIGHT,
    WEAPON_ATTRIBUTE,
    WEAPON_BUTTPAD,
    WEAPON_MAGAZINE_GRASP,
    WEAPON_PRIMARY_GRIP,
    canonical_json_bytes,
    canonical_sha256,
    classify_face_contact,
    topology_semantics_sha256,
    validate_manifest,
)


def valid_windows() -> dict[str, list[dict[str, int | str]]]:
    return {
        "primary_grip": [{"action": "PS_Aim", "start": 1, "end": 30}],
        "support_grip": [{"action": "PS_Aim", "start": 1, "end": 30}],
        "buttpad": [{"action": "PS_Aim", "start": 1, "end": 30}],
        "reload_mag": [{"action": "PS_Reload", "start": 25, "end": 75}],
        "bolt": [{"action": "PS_BoltCycle", "start": 4, "end": 16}],
    }


def valid_manifest() -> dict[str, object]:
    triangles = [(0, 1, 2), (2, 3, 0)]
    ids = [0, 101]
    return {
        "schema_version": MANIFEST_SCHEMA,
        "policy_version": POLICY_VERSION,
        "semantic_schema": SEMANTIC_SCHEMA,
        "suit_asset_id": "AegisVanguardCandidate005",
        "weapon_asset_id": "PS_NextGenPrecisionRifle001",
        "source_candidate_sha256": SOURCE_CANDIDATE_SHA256,
        "contact_windows": valid_windows(),
        "objects": [{
            "name": "H2_Undersuit_LOD0",
            "asset_role": "suit",
            "semantic_attribute": SUIT_ATTRIBUTE,
            "face_count": 2,
            "topology_sha256": topology_semantics_sha256(triangles, ids),
            "semantic_counts": {"0": 1, "101": 1},
        }, {
            "name": "WeaponV2_Rifle_LOD0",
            "asset_role": "weapon",
            "semantic_attribute": WEAPON_ATTRIBUTE,
            "face_count": 2,
            "topology_sha256": topology_semantics_sha256(triangles, [0, 201]),
            "semantic_counts": {"0": 1, "201": 1},
        }],
    }


class ClearanceFacePolicyTests(unittest.TestCase):
    def test_canonical_json_and_hash_ignore_mapping_insertion_order(self) -> None:
        first = {"b": 2, "a": {"d": 4, "c": 3}}
        second = {"a": {"c": 3, "d": 4}, "b": 2}
        self.assertEqual(canonical_json_bytes(first), canonical_json_bytes(second))
        self.assertEqual(canonical_sha256(first), canonical_sha256(second))
        self.assertEqual(json.loads(canonical_json_bytes(first)), first)

    def test_topology_hash_binds_face_semantics(self) -> None:
        triangles = [(0, 1, 2), (2, 3, 0)]
        first = topology_semantics_sha256(triangles, [0, 101])
        self.assertEqual(first, topology_semantics_sha256(triangles, [0, 101]))
        self.assertNotEqual(first, topology_semantics_sha256(triangles, [0, 102]))

    def test_matching_pair_is_allowed_only_inside_explicit_window(self) -> None:
        inside = classify_face_contact(
            "PS_Aim", 15, SUIT_PRIMARY_HAND_RIGHT, WEAPON_PRIMARY_GRIP, valid_windows()
        )
        outside = classify_face_contact(
            "PS_Aim", 31, SUIT_PRIMARY_HAND_RIGHT, WEAPON_PRIMARY_GRIP, valid_windows()
        )
        self.assertTrue(inside.allowed)
        self.assertEqual(inside.contact_key, "primary_grip")
        self.assertFalse(outside.allowed)
        self.assertIn("outside_authored_window", outside.classification)

    def test_reload_contact_has_exact_pair_and_bounded_window(self) -> None:
        inside = classify_face_contact(
            "PS_Reload", 25, SUIT_MAGAZINE_HAND_LEFT,
            WEAPON_MAGAZINE_GRASP, valid_windows()
        )
        before = classify_face_contact(
            "PS_Reload", 24.5, SUIT_MAGAZINE_HAND_LEFT,
            WEAPON_MAGAZINE_GRASP, valid_windows()
        )
        self.assertTrue(inside.allowed)
        self.assertFalse(before.allowed)

    def test_incompatible_faces_and_missing_metadata_fail_closed(self) -> None:
        mismatch = classify_face_contact(
            "PS_Aim", 10, SUIT_STOCK_POCKET_RIGHT, WEAPON_PRIMARY_GRIP, valid_windows()
        )
        invalid = classify_face_contact(
            "PS_Aim", 10, SUIT_PRIMARY_HAND_RIGHT, WEAPON_PRIMARY_GRIP,
            valid_windows(), metadata_valid=False
        )
        self.assertFalse(mismatch.allowed)
        self.assertFalse(invalid.allowed)

    def test_containment_is_forbidden_even_for_compatible_faces(self) -> None:
        decision = classify_face_contact(
            "PS_Aim", 15, SUIT_STOCK_POCKET_RIGHT, WEAPON_BUTTPAD,
            valid_windows(), containment=True
        )
        self.assertFalse(decision.allowed)
        self.assertEqual(decision.classification, "forbidden_containment")

    def test_manifest_requires_all_windows_and_consistent_evidence(self) -> None:
        manifest = valid_manifest()
        self.assertEqual(validate_manifest(manifest), [])
        del manifest["contact_windows"]["bolt"]
        manifest["objects"][0]["semantic_counts"] = {"0": 1}
        errors = validate_manifest(manifest)
        self.assertTrue(any("contact_windows.bolt" in error for error in errors))
        self.assertTrue(any("sum to face_count" in error for error in errors))

    def test_manifest_rejects_unknown_semantic_evidence(self) -> None:
        manifest = valid_manifest()
        manifest["objects"][1]["semantic_counts"] = {"999": 2}
        errors = validate_manifest(manifest)
        self.assertTrue(any("unknown IDs" in error for error in errors))

    def test_reload_and_bolt_windows_cannot_escape_hard_policy_bounds(self) -> None:
        manifest = valid_manifest()
        manifest["contact_windows"]["reload_mag"][0]["start"] = 24
        manifest["contact_windows"]["bolt"][0]["end"] = 17
        errors = validate_manifest(manifest)
        self.assertTrue(any("PS_Reload frames 25-75" in error for error in errors))
        self.assertTrue(any("PS_BoltCycle frames 4-16" in error for error in errors))

    def test_grip_windows_exclude_authored_manipulation_intervals(self) -> None:
        manifest = valid_manifest()
        manifest["contact_windows"]["primary_grip"] = [
            {"action": "PS_BoltCycle", "start": 1, "end": 20}
        ]
        manifest["contact_windows"]["support_grip"] = [
            {"action": "PS_Reload", "start": 1, "end": 84}
        ]
        errors = validate_manifest(manifest)
        self.assertTrue(any("right hand manipulates the bolt" in error for error in errors))
        self.assertTrue(any("left hand manipulates the magazine" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
