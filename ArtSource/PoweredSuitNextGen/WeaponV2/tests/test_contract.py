from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


LANE_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = LANE_ROOT.parents[2]
sys.path.insert(0, str(LANE_ROOT))

from weapon_v2_contract import (  # noqa: E402
    ContractError,
    REQUIRED_EVIDENCE,
    assert_exact_action_contract,
    canonical_json_bytes,
    evaluate_skin_motion_metrics,
    evaluate_triangle_budget,
    evaluate_hardpoint_envelopes,
    finalise_report,
    issue_code_scope_passed,
    load_profile,
    missing_source_report,
    safe_repository_path,
    sha256_manifest,
    sha256_file,
    validate_pbr_manifest,
    validate_bound_render_manifest,
    validate_projection_evidence,
    validate_render_set,
)


class WeaponV2ContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.profile = load_profile(LANE_ROOT / "production_profile.json")

    def test_profile_freezes_exact_generator114_rig_contract(self) -> None:
        rig = self.profile["rig"]
        self.assertEqual(len(rig["bone_names"]), 23)
        self.assertEqual(len(rig["action_ranges"]), 24)
        self.assertEqual(
            rig["weapon_control_bones"],
            ["WeaponRoot", "WeaponMagazine", "WeaponBolt"],
        )
        self.assertIs(rig["weapon_control_deform_required"], True)
        self.assertEqual(rig["action_ranges"]["PS_Reload"], [1, 84])
        self.assertEqual(rig["action_ranges"]["PS_BoltCycle"], [1, 20])

    def test_profile_requires_candidate006_deforming_weapon_controls(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "profile.json"
            tampered = json.loads(json.dumps(self.profile))
            tampered["rig"]["weapon_control_deform_required"] = False
            path.write_text(json.dumps(tampered), encoding="utf-8")
            with self.assertRaises(ContractError):
                load_profile(path)

    def test_profile_pins_independent_skin_motion_samples(self) -> None:
        self.assertEqual(
            self.profile["skin_motion"]["required_samples"],
            [
                {"action": "PS_Aim", "frame": 1},
                {"action": "PS_WeaponStowed_Idle", "frame": 1},
                {"action": "PS_Reload", "frame": 50},
                {"action": "PS_BoltCycle", "frame": 12},
            ],
        )
        self.assertIn("weapon_skin_motion", REQUIRED_EVIDENCE)

    def test_skin_motion_metrics_fail_closed_on_static_or_bad_bind(self) -> None:
        requirements = self.profile["skin_motion"]
        samples = {
            f"{item['action']}@{item['frame']}": {
                "maximum_manual_skin_error_m": 0.00001,
            }
            for item in requirements["required_samples"]
        }
        passing = {
            "samples": samples,
            "root_ready_to_stowed_travel_m": 0.4,
            "root_transition_return_matrix_error": 0.00001,
            "magazine_travel_m": 0.3,
            "magazine_return_matrix_error": 0.00001,
            "bolt_travel_m": 0.09,
            "bolt_return_matrix_error": 0.00001,
        }
        self.assertEqual(evaluate_skin_motion_metrics(passing, requirements), [])

        frozen = json.loads(json.dumps(passing))
        frozen["root_ready_to_stowed_travel_m"] = 0.0
        frozen["magazine_travel_m"] = 0.0
        frozen["bolt_travel_m"] = 0.0
        errors = evaluate_skin_motion_metrics(frozen, requirements)
        self.assertTrue(any("root_ready_to_stowed" in error for error in errors))
        self.assertTrue(any("magazine_travel" in error for error in errors))
        self.assertTrue(any("bolt_travel" in error for error in errors))

        bad_bind = json.loads(json.dumps(passing))
        bad_bind["samples"]["PS_Aim@1"]["maximum_manual_skin_error_m"] = 0.01
        self.assertTrue(
            any(
                "PS_Aim@1 skin error" in error
                for error in evaluate_skin_motion_metrics(bad_bind, requirements)
            )
        )

    def test_profile_freezes_rifle_lod_targets(self) -> None:
        budgets = self.profile["lods"]["rifle_triangle_budgets"]
        self.assertEqual(budgets["LOD0"], {
            "hard_max": 30000,
            "target_max": 30000,
            "target_min": 20000,
        })
        self.assertEqual(budgets["LOD3"], {
            "hard_max": 2000,
            "target_max": 2000,
            "target_min": 1000,
        })

    def test_all_immutable_profile_hashes_match_disk(self) -> None:
        for name, entry in self.profile["immutable_inputs"].items():
            with self.subTest(name=name):
                path = safe_repository_path(REPOSITORY_ROOT, entry["path"])
                self.assertTrue(path.is_file())
                self.assertEqual(sha256_file(path).lower(), entry["sha256"].lower())

    def test_hardpoint_envelopes_accept_candidate006_design_and_reject_drift(self) -> None:
        hardpoints = {
            "primary_grip": [-0.07, -0.05, -0.04],
            "support_grip": [0.108, 0.3, -0.035],
            "stock_contact": [-0.112, -0.448, 0.132],
            "sight_ocular": [0.0, -0.28, 0.315],
            "muzzle": [0.0, 1.175, 0.145],
        }
        envelopes = self.profile["weapon"]["hardpoint_envelopes_m"]
        self.assertEqual(evaluate_hardpoint_envelopes(hardpoints, envelopes), [])
        hardpoints["support_grip"] = [0.2, 0.3, -0.035]
        errors = evaluate_hardpoint_envelopes(hardpoints, envelopes)
        self.assertTrue(any("support_grip.x" in error for error in errors))

    def test_budget_target_misses_warn_but_hard_max_fails(self) -> None:
        budget = {"target_min": 100, "target_max": 200, "hard_max": 220}
        self.assertEqual(evaluate_triangle_budget(99, budget)["severity"], "warning")
        self.assertEqual(evaluate_triangle_budget(150, budget)["severity"], "pass")
        self.assertEqual(evaluate_triangle_budget(210, budget)["severity"], "warning")
        self.assertEqual(evaluate_triangle_budget(221, budget)["severity"], "error")

    def test_action_contract_rejects_missing_extra_changed_and_bad_slots(self) -> None:
        expected = self.profile["rig"]["action_ranges"]
        slots = {name: 1 for name in expected}
        assert_exact_action_contract(expected, slots, expected)

        missing = dict(expected)
        missing.pop("PS_Aim")
        with self.assertRaises(ContractError):
            assert_exact_action_contract(missing, slots, expected)

        changed = dict(expected)
        changed["PS_Reload"] = [1, 83]
        with self.assertRaises(ContractError):
            assert_exact_action_contract(changed, slots, expected)

        bad_slots = dict(slots)
        bad_slots["PS_Aim"] = 2
        with self.assertRaises(ContractError):
            assert_exact_action_contract(expected, bad_slots, expected)

    def test_canonical_report_and_manifest_hashes_are_order_independent(self) -> None:
        left = {"b": 2, "a": {"z": 3, "y": 4}}
        right = {"a": {"y": 4, "z": 3}, "b": 2}
        self.assertEqual(canonical_json_bytes(left), canonical_json_bytes(right))
        self.assertEqual(sha256_manifest(left), sha256_manifest(right))

    def test_manifest_hash_ignores_json_line_endings_and_whitespace(self) -> None:
        lf_document = json.loads('{\n  "b": 2,\n  "a": 1\n}\n')
        crlf_document = json.loads('{\r\n"a":1,\r\n"b":2\r\n}\r\n')
        self.assertEqual(sha256_manifest(lf_document), sha256_manifest(crlf_document))

    def test_repository_paths_fail_closed_on_escape(self) -> None:
        inside = safe_repository_path(REPOSITORY_ROOT, "ArtSource")
        self.assertEqual(inside, REPOSITORY_ROOT / "ArtSource")
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaises(ContractError):
                safe_repository_path(REPOSITORY_ROOT, Path(temporary) / "escape.png")

    def test_pbr_manifest_requires_all_four_hash_verified_maps(self) -> None:
        with tempfile.TemporaryDirectory(dir=REPOSITORY_ROOT) as temporary:
            directory = Path(temporary)
            maps = {}
            for role in ("base_color", "normal", "mrao", "emission"):
                path = directory / f"{role}.png"
                path.write_bytes(f"map:{role}".encode("ascii"))
                maps[role] = {
                    "path": path.relative_to(REPOSITORY_ROOT).as_posix(),
                    "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                }
            manifest = {"schema_version": 1, "resolution": [2048, 2048], "maps": maps}
            self.assertEqual(
                validate_pbr_manifest(manifest, REPOSITORY_ROOT, [2048, 2048]), []
            )
            manifest["maps"]["normal"]["sha256"] = "0" * 64
            self.assertTrue(
                any("normal map hash mismatch" in error for error in validate_pbr_manifest(
                    manifest, REPOSITORY_ROOT, [2048, 2048]
                ))
            )

    def test_pbr_manifest_rejects_missing_map_and_wrong_resolution(self) -> None:
        manifest = {
            "schema_version": 1,
            "resolution": [1024, 1024],
            "maps": {
                "base_color": {},
                "normal": {},
                "mrao": {},
            },
        }
        errors = validate_pbr_manifest(manifest, REPOSITORY_ROOT, [2048, 2048])
        self.assertTrue(any("resolution" in error for error in errors))
        self.assertTrue(any("map roles" in error for error in errors))
        self.assertTrue(any("emission map entry" in error for error in errors))

    def test_render_set_requires_exact_thirteen_nontrivial_pngs(self) -> None:
        names = self.profile["renders"]["required_filenames"]
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for name in names:
                (directory / name).write_bytes(name.encode("ascii") + b"P" * 4096)
            self.assertEqual(validate_render_set(directory, names), [])
            (directory / names[0]).unlink()
            (directory / "unexpected.png").write_bytes(b"P" * 4096)
            errors = validate_render_set(directory, names)
            self.assertTrue(errors)
            self.assertIn(names[0], errors[0])

    def test_render_set_rejects_byte_identical_images_independently(self) -> None:
        names = self.profile["renders"]["required_filenames"]
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for name in names:
                (directory / name).write_bytes(b"same review evidence" * 300)
            errors = validate_render_set(directory, names)
            self.assertTrue(any("byte-identical" in error for error in errors))

    def test_projection_evidence_enforces_safe_frames_and_usable_ocular(self) -> None:
        names = self.profile["renders"]["required_filenames"]
        views = {}
        for name in names:
            if name == "nextgen_precision_rifle_scope_ocular.png":
                views[name] = {
                    "evidence_kind": "ocular_corridor",
                    "camera_to_ocular_rear_m": 0.010,
                    "aperture_center_x": 0.5,
                    "aperture_center_y": 0.5,
                    "aperture_radius_x": 0.21,
                    "aperture_radius_y": 0.28,
                    "reticle_center_x": 0.5,
                    "reticle_center_y": 0.5,
                    "target_center_x": 0.5,
                    "target_center_y": 0.5,
                    "target_distance_m": 12.0,
                    "target_viewport_width": 0.52,
                    "target_viewport_height": 0.52,
                    "sight_picture_viewport_width": 0.48,
                    "sight_picture_viewport_height": 0.48,
                    "aperture_object": "NGPR_OpticOcular",
                    "aperture_geometry_source": "exact_source_proxy_inner_rim",
                    "aperture_proxy_max_distance_m": 0.0,
                    "aperture_sample_count": 128,
                    "sight_picture_sample_count": 256,
                    "corridor_clear": True,
                    "objective_visible": True,
                    "reticle_visible": True,
                    "target_visible": True,
                    "studio_ground_visible": False,
                    "nested_occluder_count": 0,
                    "reticle_line_count": 4,
                    "range_tick_count": 6,
                }
            else:
                views[name] = {
                    "evidence_kind": "weapon_bounds_5_95",
                    "viewport_min_x": 0.14,
                    "viewport_max_x": 0.86,
                    "viewport_min_y": 0.20,
                    "viewport_max_y": 0.80,
                    "viewport_width": 0.72,
                    "viewport_height": 0.60,
                    "weapon_bounds_sample_count": 128,
                    "studio_ground_visible": False,
                }
        evidence = {
            "schema_version": 3,
            "render_resolution": [1280, 960],
            "views": views,
        }
        self.assertEqual(validate_projection_evidence(evidence, names), [])

        tampered = json.loads(json.dumps(evidence))
        tampered["views"]["nextgen_precision_rifle_pose_bolt.png"][
            "viewport_max_y"
        ] = 1.04
        tampered["views"]["nextgen_precision_rifle_neutral_side.png"][
            "viewport_width"
        ] = 0.70
        ocular = tampered["views"]["nextgen_precision_rifle_scope_ocular.png"]
        ocular["camera_to_ocular_rear_m"] = 0.12
        ocular["nested_occluder_count"] = 1
        ocular["target_visible"] = False
        ocular["aperture_object"] = "SyntheticOcular"
        ocular["sight_picture_viewport_width"] = 0.04
        errors = validate_projection_evidence(tampered, names)
        self.assertTrue(any("safe frame" in error for error in errors))
        self.assertTrue(any("0.72 side-view target" in error for error in errors))
        self.assertTrue(any("rear aperture" in error for error in errors))
        self.assertTrue(any("nested ocular occluders" in error for error in errors))
        self.assertTrue(any("target_visible=true" in error for error in errors))
        self.assertTrue(any("bind aperture evidence" in error for error in errors))
        self.assertTrue(any("sight_picture_viewport_width" in error for error in errors))

    def test_issue_code_scopes_do_not_cross_contaminate_evidence(self) -> None:
        issues = [
            {"code": "LOD2_RIFLE_UV0", "severity": "error"},
            {"code": "LOD2_RIFLE_TRIANGLES", "severity": "pass"},
            {"code": "LOD2_COMBINED_RUNTIME_BUDGET", "severity": "pass"},
        ]
        self.assertFalse(issue_code_scope_passed(issues, suffixes=("_UV0",)))
        self.assertTrue(issue_code_scope_passed(
            issues,
            suffixes=("_RIFLE_TRIANGLES", "_COMBINED_RUNTIME_BUDGET"),
        ))
        issues.append({
            "code": "LOD2_COMBINED_RUNTIME_BUDGET",
            "severity": "error",
        })
        self.assertFalse(issue_code_scope_passed(
            issues,
            suffixes=("_RIFLE_TRIANGLES", "_COMBINED_RUNTIME_BUDGET"),
        ))
        self.assertFalse(issue_code_scope_passed(issues, suffixes=("_UV0",)))
        budget_only = [
            {"code": "LOD3_RIFLE_UV0", "severity": "pass"},
            {"code": "LOD3_COMBINED_RUNTIME_BUDGET", "severity": "error"},
        ]
        self.assertTrue(issue_code_scope_passed(
            budget_only, suffixes=("_UV0",)
        ))

    def test_bound_render_manifest_rejects_stale_source_or_image_hash(self) -> None:
        names = self.profile["renders"]["required_filenames"]
        with tempfile.TemporaryDirectory(dir=REPOSITORY_ROOT) as temporary:
            directory = Path(temporary)
            entries = []
            for name in names:
                path = directory / name
                path.write_bytes((name.encode("ascii") + b"P" * 4096))
                entries.append({
                    "filename": name,
                    "path": path.relative_to(REPOSITORY_ROOT).as_posix(),
                    "sha256": sha256_file(path),
                    "size_bytes": path.stat().st_size,
                })
            source_hash = "a" * 64
            manifest = {
                "candidate_blend_sha256": source_hash,
                "render_manifest": {
                    "candidate_blend_sha256": source_hash,
                    "files": entries,
                },
            }
            self.assertEqual(validate_bound_render_manifest(
                manifest, REPOSITORY_ROOT, directory, names, source_hash
            ), [])
            manifest["render_manifest"]["candidate_blend_sha256"] = "b" * 64
            manifest["render_manifest"]["files"][0]["sha256"] = "c" * 64
            errors = validate_bound_render_manifest(
                manifest, REPOSITORY_ROOT, directory, names, source_hash
            )
            self.assertTrue(any("audited source" in error for error in errors))
            self.assertTrue(any("bound SHA-256 differs" in error for error in errors))

    def test_report_fails_closed_when_required_evidence_is_missing(self) -> None:
        report = {"issues": [], "evidence": {name: True for name in REQUIRED_EVIDENCE}}
        report["evidence"].pop("sight_and_ocular")
        finalise_report(report)
        self.assertEqual(report["status"], "FAIL")
        self.assertFalse(report["promotion_authorized"])
        self.assertEqual(report["summary"]["error"], 1)

    def test_report_fails_when_explicit_gate_error_exists(self) -> None:
        report = {
            "issues": [{"code": "BROKEN", "severity": "error", "message": "no"}],
            "evidence": {name: True for name in REQUIRED_EVIDENCE},
        }
        finalise_report(report)
        self.assertEqual(report["status"], "FAIL")
        self.assertFalse(report["promotion_authorized"])

    def test_report_passes_only_with_every_true_evidence_and_no_errors(self) -> None:
        report = {
            "issues": [{"code": "QUALITY", "severity": "warning", "message": "review"}],
            "evidence": {name: True for name in REQUIRED_EVIDENCE},
        }
        finalise_report(report)
        self.assertEqual(report["status"], "PASS")
        self.assertTrue(report["structural_gate_passed"])
        self.assertFalse(report["promotion_authorized"])
        self.assertIn("owner visual approval", report["promotion_blockers_remaining"])

    def test_missing_source_report_is_written_as_a_blocking_result(self) -> None:
        report = missing_source_report("missing_candidate006.blend")
        self.assertEqual(report["status"], "FAIL")
        self.assertFalse(report["promotion_authorized"])
        self.assertTrue(all(value is False for value in report["evidence"].values()))
        self.assertGreater(report["summary"]["error"], 1)

    def test_profile_rejects_tampered_action_count(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "profile.json"
            tampered = json.loads(json.dumps(self.profile))
            tampered["rig"]["action_ranges"].pop("PS_Aim")
            path.write_text(json.dumps(tampered), encoding="utf-8")
            with self.assertRaises(ContractError):
                load_profile(path)


if __name__ == "__main__":
    unittest.main()
