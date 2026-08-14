from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from clearance_face_policy import (  # noqa: E402
    CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION,
    CANDIDATE007_WEAPON_ASSET_ID,
    MANIFEST_SCHEMA,
    POLICY_VERSION,
    SEMANTIC_SCHEMA,
    SOURCE_CANDIDATE_SHA256,
    SUIT_ATTRIBUTE,
    SUIT_MAGAZINE_HAND_LEFT,
    SUIT_PRIMARY_HAND_RIGHT,
    SUIT_STOCK_POCKET_RIGHT,
    SUIT_SUPPORT_HAND_LEFT,
    WEAPON_ATTRIBUTE,
    WEAPON_BUTTPAD,
    WEAPON_MAGAZINE_GRASP,
    WEAPON_PRIMARY_GRIP,
    WEAPON_SUPPORT_GRIP,
    canonical_json_bytes,
    canonical_sha256,
    classify_face_contact,
    topology_semantics_sha256,
    validate_manifest,
)


def valid_windows() -> dict[str, list[dict[str, int | float | str]]]:
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


def enable_candidate007_transition_contacts(manifest: dict[str, object]) -> None:
    manifest["weapon_asset_id"] = CANDIDATE007_WEAPON_ASSET_ID
    manifest["contact_window_policy_version"] = (
        CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION
    )
    windows = manifest["contact_windows"]
    assert isinstance(windows, dict)
    primary_transition_contacts = [
        {"action": "PS_Weapon_Draw", "start": 26.75, "end": 30},
        {"action": "PS_Weapon_Sheathe", "start": 1, "end": 4.25},
    ]
    support_transition_contacts = [
        {"action": "PS_Weapon_Draw", "start": 29, "end": 30},
        {"action": "PS_Weapon_Sheathe", "start": 1, "end": 2},
    ]
    for contact_key, transition_contacts in (
        ("primary_grip", primary_transition_contacts),
        ("support_grip", support_transition_contacts),
    ):
        contact_windows = windows[contact_key]
        assert isinstance(contact_windows, list)
        contact_windows.extend(dict(window) for window in transition_contacts)


class ClearanceFacePolicyTests(unittest.TestCase):
    def test_candidate007_policy_id_is_v3(self) -> None:
        self.assertEqual(
            CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION,
            "PS_CLEARANCE_CONTACT_WINDOWS_CANDIDATE007_V3",
        )

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

    def test_baseline_policy_rejects_draw_and_sheathe_grip_windows(self) -> None:
        manifest = valid_manifest()
        windows = manifest["contact_windows"]
        assert isinstance(windows, dict)
        primary = windows["primary_grip"]
        assert isinstance(primary, list)
        primary.extend([
            {"action": "PS_Weapon_Draw", "start": 29, "end": 30},
            {"action": "PS_Weapon_Sheathe", "start": 1, "end": 2},
        ])
        errors = validate_manifest(manifest)
        self.assertEqual(
            sum("is not an active ready-family action" in error for error in errors),
            2,
        )

    def test_candidate007_policy_allows_only_exact_transition_contacts(self) -> None:
        manifest = valid_manifest()
        enable_candidate007_transition_contacts(manifest)
        self.assertEqual(validate_manifest(manifest), [])

        windows = manifest["contact_windows"]
        assert isinstance(windows, dict)
        primary = windows["primary_grip"]
        assert isinstance(primary, list)
        acquisition = classify_face_contact(
            "PS_Weapon_Draw",
            26.75,
            SUIT_PRIMARY_HAND_RIGHT,
            WEAPON_PRIMARY_GRIP,
            windows,
        )
        adjacent = classify_face_contact(
            "PS_Weapon_Draw",
            26.5,
            SUIT_PRIMARY_HAND_RIGHT,
            WEAPON_PRIMARY_GRIP,
            windows,
        )
        self.assertTrue(acquisition.allowed)
        self.assertFalse(adjacent.allowed)

        primary_release = classify_face_contact(
            "PS_Weapon_Sheathe",
            4.25,
            SUIT_PRIMARY_HAND_RIGHT,
            WEAPON_PRIMARY_GRIP,
            windows,
        )
        after_primary_release = classify_face_contact(
            "PS_Weapon_Sheathe",
            4.5,
            SUIT_PRIMARY_HAND_RIGHT,
            WEAPON_PRIMARY_GRIP,
            windows,
        )
        support_acquisition = classify_face_contact(
            "PS_Weapon_Draw",
            29,
            SUIT_SUPPORT_HAND_LEFT,
            WEAPON_SUPPORT_GRIP,
            windows,
        )
        before_support_acquisition = classify_face_contact(
            "PS_Weapon_Draw",
            28.75,
            SUIT_SUPPORT_HAND_LEFT,
            WEAPON_SUPPORT_GRIP,
            windows,
        )
        support_release = classify_face_contact(
            "PS_Weapon_Sheathe",
            2,
            SUIT_SUPPORT_HAND_LEFT,
            WEAPON_SUPPORT_GRIP,
            windows,
        )
        after_support_release = classify_face_contact(
            "PS_Weapon_Sheathe",
            2.25,
            SUIT_SUPPORT_HAND_LEFT,
            WEAPON_SUPPORT_GRIP,
            windows,
        )
        self.assertTrue(primary_release.allowed)
        self.assertFalse(after_primary_release.allowed)
        self.assertTrue(support_acquisition.allowed)
        self.assertFalse(before_support_acquisition.allowed)
        self.assertTrue(support_release.allowed)
        self.assertFalse(after_support_release.allowed)

        for invalid_window in (
            {"action": "PS_Weapon_Draw", "start": 26.5, "end": 26.5},
            {"action": "PS_Weapon_Draw", "start": 26.5, "end": 30},
            {"action": "PS_Weapon_Sheathe", "start": 4.5, "end": 4.5},
            {"action": "PS_Weapon_Sheathe", "start": 1, "end": 4.5},
        ):
            with self.subTest(invalid_window=invalid_window):
                primary.append(invalid_window)
                errors = validate_manifest(manifest)
                self.assertTrue(
                    any(
                        "is not an active ready-family action" in error
                        for error in errors
                    )
                )
                primary.pop()

    def test_candidate007_policy_is_asset_bound_and_requires_all_windows(self) -> None:
        manifest = valid_manifest()
        enable_candidate007_transition_contacts(manifest)
        manifest["weapon_asset_id"] = "PS_NextGenPrecisionRifle001"
        errors = validate_manifest(manifest)
        self.assertTrue(any("restricted" in error for error in errors))

        manifest["weapon_asset_id"] = CANDIDATE007_WEAPON_ASSET_ID
        windows = manifest["contact_windows"]
        assert isinstance(windows, dict)
        support = windows["support_grip"]
        assert isinstance(support, list)
        support.remove(
            {"action": "PS_Weapon_Sheathe", "start": 1, "end": 2}
        )
        errors = validate_manifest(manifest)
        self.assertTrue(any("missing exact transition contact" in error for error in errors))

    def test_candidate007_rejects_stowed_legacy_contact_windows_only(self) -> None:
        for action in ("PS_Idle", "PS_Walk", "PS_Hover"):
            baseline = valid_manifest()
            baseline_windows = baseline["contact_windows"]
            assert isinstance(baseline_windows, dict)
            baseline_primary = baseline_windows["primary_grip"]
            assert isinstance(baseline_primary, list)
            baseline_primary.append({"action": action, "start": 1, "end": 1})
            self.assertEqual(validate_manifest(baseline), [], action)

            for contact_key in ("primary_grip", "support_grip", "buttpad"):
                with self.subTest(action=action, contact_key=contact_key):
                    candidate = valid_manifest()
                    enable_candidate007_transition_contacts(candidate)
                    candidate_windows = candidate["contact_windows"]
                    assert isinstance(candidate_windows, dict)
                    contact_windows = candidate_windows[contact_key]
                    assert isinstance(contact_windows, list)
                    contact_windows.append(
                        {"action": action, "start": 1, "end": 1}
                    )
                    errors = validate_manifest(candidate)
                    self.assertTrue(
                        any(
                            "carries Candidate007 stowed and cannot authorize contact"
                            in error
                            for error in errors
                        ),
                        errors,
                    )

    def test_candidate007_malformed_windows_return_errors_instead_of_raising(self) -> None:
        malformed_values = (
            5,
            None,
            [{"action": [], "start": 26.75, "end": 30}],
            [{"action": "PS_Weapon_Draw", "start": float("inf"), "end": 30}],
        )
        for malformed in malformed_values:
            with self.subTest(malformed=malformed):
                manifest = valid_manifest()
                enable_candidate007_transition_contacts(manifest)
                windows = manifest["contact_windows"]
                assert isinstance(windows, dict)
                windows["primary_grip"] = malformed
                errors = validate_manifest(manifest)
                self.assertIsInstance(errors, list)
                self.assertTrue(errors)
                self.assertTrue(
                    any("contact_windows.primary_grip" in error for error in errors),
                    errors,
                )


if __name__ == "__main__":
    unittest.main()
