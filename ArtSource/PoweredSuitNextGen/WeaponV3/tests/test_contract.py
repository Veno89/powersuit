from __future__ import annotations

import ast
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


LANE_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = LANE_ROOT.parents[2]
sys.path.insert(0, str(LANE_ROOT))

from weapon_v3_contract import (  # noqa: E402
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
    report_evidence_sha256,
    safe_repository_path,
    seal_production_report,
    sha256_manifest,
    sha256_file,
    sha256_immutable_input,
    validate_pbr_manifest,
    validate_bound_render_manifest,
    validate_clearance_report,
    validate_component_architecture_evidence,
    validate_manipulation_evidence,
    validate_projection_evidence,
    validate_production_report_seal,
    validate_report_evidence_sha256,
    validate_render_set,
    validate_stow_evidence,
    validate_topology_provenance,
    _expected_manipulation_result_frames,
)


class WeaponV3ContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.profile = load_profile(LANE_ROOT / "production_profile.json")

    def valid_reauthor_evidence(self) -> dict[str, object]:
        authoring = self.profile["animation_authoring"]
        transition = authoring["transition"]
        paths = {}
        for action_name, leg_name in (
            ("PS_Weapon_Draw", "draw"),
            ("PS_Weapon_Sheathe", "sheathe"),
        ):
            leg = transition[leg_name]
            paths[action_name] = {
                "version": transition["path_version"],
                "sample_step_frames": transition["sample_step_frames"],
                "certification_step_frames": transition[
                    "certification_step_frames"
                ],
                "key_frames": leg["key_frames"],
                "result_frames": leg["key_frames"],
                **{key: value for key, value in leg.items() if key != "key_frames"},
                "ownership_bone": transition["ownership_bone"],
            }
        densification = authoring["manipulation_densification"]
        dense_actions = {}
        for action_name, expected in densification["actions"].items():
            entry = json.loads(json.dumps(expected))
            entry.pop("expected_result_frame_count")
            entry["result_frames"] = _expected_manipulation_result_frames(action_name)
            dense_actions[action_name] = entry
        return {
            "reauthor_version": authoring["reauthor_version"],
            "action_signature_schema": authoring["action_signature_schema"],
            "stow_rearward_delta_m": authoring["stow"]["rearward_delta_m"],
            "stow_outward_delta_m": authoring["stow"]["outward_delta_m"],
            "transition_pose_mode": authoring["stow"]["transition_pose_mode"],
            "draw_extraction_back_clearance_m": authoring["stow"][
                "draw_extraction_back_clearance_m"
            ],
            "draw_extraction_lateral_m": authoring["stow"][
                "draw_extraction_lateral_m"
            ],
            "transition_evidence": {
                "endpoint_max_matrix_error": 0.0,
                "subframe_reversal_max_matrix_error": 5.96e-7,
                "reversal_certification_step_frames": transition[
                    "certification_step_frames"
                ],
                "reversal_sample_count": transition["reversal_sample_count"],
            },
            "transition_path_version": transition["path_version"],
            "transition_path_evidence": paths,
            **json.loads(json.dumps(authoring["manipulation"])),
            "manipulation_densification_version": densification["version"],
            "manipulation_densification_evidence": dense_actions,
        }

    def valid_projection_evidence(self) -> dict[str, object]:
        views = {}
        for name in self.profile["renders"]["required_filenames"]:
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
                continue
            entry = {
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
            if name == "nextgen_precision_rifle_pose_draw.png":
                entry.update({
                    "context_evidence_kind": "suit_lod0_samples_inside_2_98",
                    "context_viewport_min_x": 0.10,
                    "context_viewport_max_x": 0.40,
                    "context_viewport_min_y": 0.25,
                    "context_viewport_max_y": 0.45,
                    "context_viewport_width": 0.30,
                    "context_viewport_height": 0.20,
                    "context_visible_sample_count": 48,
                    "context_projected_sample_count": 128,
                })
            views[name] = entry
        return {
            "schema_version": 4,
            "render_resolution": [1280, 960],
            "views": views,
        }

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

    def test_profile_uses_only_candidate007_asset_and_lane_identifiers(self) -> None:
        self.assertEqual(self.profile["asset"]["candidate_number"], 7)
        self.assertEqual(
            self.profile["asset"]["candidate_id"], "NextGen Precision Rifle 002"
        )
        self.assertEqual(
            self.profile["weapon"]["weapon_id"], "PS_NextGenPrecisionRifle002"
        )
        self.assertEqual(self.profile["weapon"]["rigid_signature_version"], 6)
        self.assertEqual(
            self.profile["weapon"]["hardpoint_version"],
            "NGPR002_HARDPOINTS_V2",
        )
        self.assertEqual(self.profile["selection"]["collection_prefix"], "WeaponV3_LOD")
        self.assertEqual(self.profile["selection"]["role_property"], "weapon_v3_role")
        self.assertEqual(self.profile["selection"]["lod_property"], "weapon_v3_lod")
        self.assertEqual(self.profile["selection"]["object_prefix"], "NGPR002_")
        self.assertEqual(
            self.profile["asset"]["source_filename"],
            "nextgen_precision_rifle_candidate_v007.blend",
        )
        self.assertEqual(
            self.profile["renders"]["directory_name"],
            "nextgen_precision_rifle_candidate_v007",
        )
        self.assertEqual(
            self.profile["report"]["default_filename"],
            "candidate007_production.json",
        )

    def test_profile_freezes_measured_candidate007_stow(self) -> None:
        authoring = self.profile["animation_authoring"]
        self.assertEqual(authoring["reauthor_version"], "CANDIDATE007_WEAPON_ACTIONS_V11")
        self.assertEqual(
            authoring["action_signature_schema"],
            "CANDIDATE007_ACTION_SEMANTICS_V10",
        )
        self.assertEqual(authoring["stow"]["rearward_delta_m"], 0.33)
        self.assertEqual(authoring["stow"]["outward_delta_m"], 0.04)
        self.assertEqual(
            authoring["transition"]["path_version"],
            "CANDIDATE007_GUIDED_DEPLOY_LATE_CATCH_V3",
        )
        evidence = self.valid_reauthor_evidence()
        self.assertEqual(validate_stow_evidence(evidence, authoring), [])
        evidence["stow_rearward_delta_m"] = 0.23
        self.assertTrue(validate_stow_evidence(evidence, authoring))
        evidence["stow_rearward_delta_m"] = 0.33
        evidence["transition_path_evidence"]["PS_Weapon_Draw"][
            "ownership_start_frame"
        ] = 29.0
        self.assertTrue(validate_stow_evidence(evidence, authoring))

    def test_profile_freezes_measured_candidate007_manipulation_v5(self) -> None:
        manipulation = self.profile["animation_authoring"]["manipulation"]
        self.assertEqual(
            manipulation["manipulation_solver_version"],
            "CANDIDATE007_MANIPULATION_SOLVER_V3",
        )
        self.assertEqual(
            manipulation["hand_contact_pad_center_local"],
            {
                "L": [0.0005016, 0.2179851, 0.0639991],
                "R": [0.0005006, 0.2178152, 0.0640004],
            },
        )
        self.assertEqual(
            manipulation["reload_contact_mode"],
            "seated_v2__detached_distal_pad_positive_x_face",
        )
        self.assertEqual(manipulation["reload_seated_frames"], [14, 25, 75])
        self.assertEqual(manipulation["reload_detached_frames"], [36, 50, 64])
        self.assertEqual(manipulation["reload_hand_outward_m"], 0.09)
        self.assertEqual(manipulation["reload_palm_roll_deg"], 25.0)
        self.assertEqual(manipulation["reload_shared_target_outward_m"], 0.035)
        self.assertEqual(manipulation["reload_magazine_outward_m"], 0.05)
        self.assertEqual(manipulation["reload_magazine_half_width_m"], 0.03)
        self.assertEqual(manipulation["reload_contact_inset_m"], 0.001)
        self.assertEqual(manipulation["reload_detached_twist_deg"], 60.0)
        self.assertEqual(
            manipulation["bolt_contact_mode"],
            "tagged_knob_min_x_face_distal_pad",
        )
        self.assertEqual(manipulation["bolt_contact_frames"], [4, 8, 12, 16])
        self.assertEqual(manipulation["bolt_shared_target_outward_m"], 0.035)
        self.assertEqual(manipulation["bolt_contact_inset_m"], 0.001)
        self.assertEqual(manipulation["bolt_knob_object_name"], "NGPR_BoltKnob")
        self.assertEqual(
            manipulation["bolt_target_classifier_mode"],
            "exact_root_local_shared_bolt_call_corridor",
        )
        self.assertEqual(
            self.profile["animation_authoring"]["manipulation_densification"][
                "version"
            ],
            "CANDIDATE007_MANIPULATION_DENSIFICATION_V5",
        )
        self.assertEqual(
            self.profile["animation_authoring"]["manipulation_densification"][
                "actions"
            ]["PS_BoltCycle"]["co_solved_sample_count"],
            52,
        )

    def test_profile_rejects_manipulation_solver_or_pad_drift(self) -> None:
        mutations = (
            ("solver_version", "CANDIDATE007_MANIPULATION_SOLVER_V2"),
            ("left_pad_x", 0.0105016),
            ("reload_mode", "wrist_target_only"),
            ("bolt_mode", "component_center"),
        )
        for mutation, value in mutations:
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                tampered = json.loads(json.dumps(self.profile))
                manipulation = tampered["animation_authoring"]["manipulation"]
                if mutation == "solver_version":
                    manipulation["manipulation_solver_version"] = value
                elif mutation == "left_pad_x":
                    manipulation["hand_contact_pad_center_local"]["L"][0] = value
                elif mutation == "reload_mode":
                    manipulation["reload_contact_mode"] = value
                else:
                    manipulation["bolt_contact_mode"] = value
                path = Path(temporary) / "profile.json"
                path.write_text(json.dumps(tampered), encoding="utf-8")
                with self.assertRaises(ContractError):
                    load_profile(path)

    def test_manipulation_evidence_requires_every_exact_v5_field(self) -> None:
        authoring = self.profile["animation_authoring"]
        requirements = authoring["manipulation"]
        densification = authoring["manipulation_densification"]
        evidence = self.valid_reauthor_evidence()
        self.assertEqual(
            validate_manipulation_evidence(evidence, requirements, densification), []
        )
        for field in requirements:
            with self.subTest(missing=field):
                tampered = json.loads(json.dumps(evidence))
                tampered.pop(field)
                errors = validate_manipulation_evidence(
                    tampered, requirements, densification
                )
                self.assertTrue(any(field in error and "missing" in error for error in errors))
        tampered = json.loads(json.dumps(evidence))
        tampered["reload_detached_frames"] = [36, 50]
        self.assertTrue(any(
            "reload_detached_frames differs" in error
            for error in validate_manipulation_evidence(
                tampered, requirements, densification
            )
        ))
        tampered = json.loads(json.dumps(evidence))
        tampered["bolt_knob_object_name"] = "NGPR_BoltStem"
        self.assertTrue(any(
            "bolt_knob_object_name differs" in error
            for error in validate_manipulation_evidence(
                tampered, requirements, densification
            )
        ))
        tampered = json.loads(json.dumps(evidence))
        tampered["manipulation_densification_evidence"]["PS_BoltCycle"][
            "measured_pose_substitutions"
        ]["2.5"] = 2.5
        self.assertTrue(any(
            "PS_BoltCycle densification evidence differs" in error
            for error in validate_manipulation_evidence(
                tampered, requirements, densification
            )
        ))
        for field, invalid in (
            ("reauthor_version", "CANDIDATE007_WEAPON_ACTIONS_V3"),
            ("action_signature_schema", "CANDIDATE007_ACTION_SEMANTICS_V2"),
        ):
            with self.subTest(field=field):
                tampered = json.loads(json.dumps(evidence))
                tampered[field] = invalid
                self.assertTrue(any(
                    field in error
                    for error in validate_manipulation_evidence(
                        tampered, requirements, densification
                    )
                ))

    def test_reload_derived_delta_uses_only_tight_finite_numeric_tolerance(self) -> None:
        authoring = self.profile["animation_authoring"]
        requirements = authoring["manipulation"]
        densification = authoring["manipulation_densification"]

        canonical_noise = self.valid_reauthor_evidence()
        canonical_noise["reload_hand_to_mag_outward_delta_m"] = (
            0.039999999999999994
        )
        self.assertEqual(
            validate_manipulation_evidence(
                canonical_noise, requirements, densification
            ),
            [],
        )

        for invalid in (True, "0.04", float("nan"), float("inf"), 0.040000000002):
            with self.subTest(invalid=invalid):
                tampered = self.valid_reauthor_evidence()
                tampered["reload_hand_to_mag_outward_delta_m"] = invalid
                errors = validate_manipulation_evidence(
                    tampered, requirements, densification
                )
                self.assertTrue(any(
                    "reload_hand_to_mag_outward_delta_m differs" in error
                    for error in errors
                ))

        exact_field = self.valid_reauthor_evidence()
        exact_field["reload_hand_outward_m"] = 0.09000000000000001
        self.assertTrue(any(
            "reload_hand_outward_m differs" in error
            for error in validate_manipulation_evidence(
                exact_field, requirements, densification
            )
        ))

    def test_profile_rejects_stale_bolt_quarter_only_co_solved_count(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            tampered = json.loads(json.dumps(self.profile))
            tampered["animation_authoring"]["manipulation_densification"][
                "actions"
            ]["PS_BoltCycle"]["co_solved_sample_count"] = 49
            path = Path(temporary) / "profile.json"
            path.write_text(json.dumps(tampered), encoding="utf-8")
            with self.assertRaises(ContractError):
                load_profile(path)

    def test_profile_rejects_v11_measured_waypoint_tampering(self) -> None:
        mutations = (
            ("bolt_substitution", "bolt_measured_pose_substitutions", "2.375", 2.875),
            (
                "bolt_eighth_clearance",
                "bolt_measured_eighth_frame_clearances_m",
                "6.125",
                0.0,
            ),
            (
                "reload_return_anchor",
                "reload_measured_return_anchor_frames",
                0,
                79.625,
            ),
            (
                "reload_return_delta",
                "reload_measured_return_deltas_root_local_m",
                "79.875",
                [0.0, 0.0, 0.0],
            ),
        )
        for name, field, key, value in mutations:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                tampered = json.loads(json.dumps(self.profile))
                target = tampered["animation_authoring"]["manipulation"][field]
                target[key] = value
                path = Path(temporary) / "profile.json"
                path.write_text(json.dumps(tampered), encoding="utf-8")
                with self.assertRaises(ContractError):
                    load_profile(path)

    def test_v11_result_frames_pin_all_measured_eighth_waypoints(self) -> None:
        reload_frames = _expected_manipulation_result_frames("PS_Reload")
        bolt_frames = _expected_manipulation_result_frames("PS_BoltCycle")
        self.assertEqual(len(reload_frames), 213)
        self.assertEqual(len(bolt_frames), 76)
        self.assertTrue({79.75, 79.875, 80.0} <= set(reload_frames))
        self.assertTrue(
            {2.375, 3.875, 6.125, 6.875, 13.875, 16.125, 17.625}
            <= set(bolt_frames)
        )
        self.assertEqual(
            self.profile["clearance"]["dense_transition_action_sample_counts"],
            {
                "PS_BoltCycle": 153,
                "PS_Reload": 665,
                "PS_Weapon_Draw": 233,
                "PS_Weapon_Sheathe": 233,
            },
        )
        self.assertEqual(self.profile["clearance"]["dense_transition_sample_count"], 1284)

    def test_v11_evidence_rejects_measured_waypoint_tampering(self) -> None:
        authoring = self.profile["animation_authoring"]
        requirements = authoring["manipulation"]
        densification = authoring["manipulation_densification"]

        top_level = self.valid_reauthor_evidence()
        top_level["bolt_measured_eighth_frame_clearances_m"]["3.875"] = 0.0
        self.assertTrue(any(
            "bolt_measured_eighth_frame_clearances_m differs" in error
            for error in validate_manipulation_evidence(
                top_level, requirements, densification
            )
        ))

        bolt_dense = self.valid_reauthor_evidence()
        bolt_dense["manipulation_densification_evidence"]["PS_BoltCycle"][
            "measured_pose_substitutions"
        ]["17.625"] = 17.625
        self.assertIn(
            "PS_BoltCycle densification evidence differs",
            validate_manipulation_evidence(bolt_dense, requirements, densification),
        )

        reload_dense = self.valid_reauthor_evidence()
        reload_dense["manipulation_densification_evidence"]["PS_Reload"][
            "measured_return_deltas_root_local_m"
        ]["79.875"] = [0.0, 0.0, 0.0]
        self.assertIn(
            "PS_Reload densification evidence differs",
            validate_manipulation_evidence(reload_dense, requirements, densification),
        )

    def test_blender_adapter_exposes_separate_manipulation_gate(self) -> None:
        source = (LANE_ROOT / "validate_candidate007.py").read_text(
            encoding="utf-8"
        )
        self.assertIn("validate_manipulation_evidence", source)
        self.assertIn("CANDIDATE007_MANIPULATION_AUTHORING", source)
        self.assertIn(
            'audit.report["evidence"]["manipulation_authoring"]', source
        )
        self.assertIn(
            'report["manipulation_authoring"] = '
            "validate_candidate007_manipulation_authoring(",
            source,
        )

    def test_reauthor_source_matches_v11_transition_evidence_schema(self) -> None:
        path = (
            LANE_ROOT.parent
            / "scripts"
            / "reauthor_candidate007_weapon_actions.py"
        )
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        constants = {}
        target_function = None
        for node in tree.body:
            if isinstance(node, ast.Assign) and len(node.targets) == 1:
                target = node.targets[0]
                if isinstance(target, ast.Name) and target.id in {
                    "REAUTHOR_VERSION",
                    "ACTION_SIGNATURE_SCHEMA",
                    "STOW_REARWARD_DELTA_M",
                    "STOW_OUTWARD_DELTA_M",
                    "DRAW_EXTRACTION_BACK_CLEARANCE_M",
                    "DRAW_EXTRACTION_LATERAL_M",
                    "TRANSITION_POSE_MODE",
                    "TRANSITION_PATH_VERSION",
                    "TRANSITION_SAMPLE_STEP_FRAMES",
                    "TRANSITION_CERTIFICATION_STEP_FRAMES",
                    "TRANSITION_GUIDED_THROUGH_FRAME",
                    "TRANSITION_PREGRASP_FRAME",
                    "TRANSITION_PREGRASP_TARGET_FRAME",
                    "TRANSITION_OWNERSHIP_START_FRAME",
                }:
                    constants[target.id] = ast.literal_eval(node.value)
            if (
                isinstance(node, ast.FunctionDef)
                and node.name == "reauthor_candidate007_weapon_actions"
            ):
                target_function = node
        authoring = self.profile["animation_authoring"]
        stow = authoring["stow"]
        self.assertEqual(constants["REAUTHOR_VERSION"], authoring["reauthor_version"])
        self.assertEqual(
            constants["ACTION_SIGNATURE_SCHEMA"],
            authoring["action_signature_schema"],
        )
        self.assertEqual(constants["STOW_REARWARD_DELTA_M"], stow["rearward_delta_m"])
        self.assertEqual(constants["STOW_OUTWARD_DELTA_M"], stow["outward_delta_m"])
        self.assertEqual(
            constants["DRAW_EXTRACTION_BACK_CLEARANCE_M"],
            stow["draw_extraction_back_clearance_m"],
        )
        self.assertEqual(
            constants["DRAW_EXTRACTION_LATERAL_M"],
            stow["draw_extraction_lateral_m"],
        )
        self.assertEqual(constants["TRANSITION_POSE_MODE"], stow["transition_pose_mode"])
        transition = authoring["transition"]
        self.assertEqual(constants["TRANSITION_PATH_VERSION"], transition["path_version"])
        self.assertEqual(
            constants["TRANSITION_SAMPLE_STEP_FRAMES"],
            transition["sample_step_frames"],
        )
        self.assertEqual(
            constants["TRANSITION_CERTIFICATION_STEP_FRAMES"],
            transition["certification_step_frames"],
        )
        self.assertEqual(
            constants["TRANSITION_GUIDED_THROUGH_FRAME"],
            transition["draw"]["guided_through_frame"],
        )
        self.assertEqual(
            constants["TRANSITION_PREGRASP_FRAME"],
            transition["draw"]["early_acquisition_frame"],
        )
        self.assertEqual(
            constants["TRANSITION_PREGRASP_TARGET_FRAME"],
            transition["draw"]["early_acquisition_target_frame"],
        )
        self.assertEqual(
            constants["TRANSITION_OWNERSHIP_START_FRAME"],
            transition["draw"]["ownership_start_frame"],
        )
        self.assertIsNotNone(target_function)
        emitted_keys = {
            node.value
            for node in ast.walk(target_function)
            if isinstance(node, ast.Constant) and isinstance(node.value, str)
        }
        self.assertTrue({
            "reauthor_version",
            "action_signature_schema",
            "transition_pose_mode",
            "transition_evidence",
            "transition_path_version",
            "transition_path_evidence",
            "stow_rearward_delta_m",
            "stow_outward_delta_m",
            "draw_extraction_back_clearance_m",
            "draw_extraction_lateral_m",
        } <= emitted_keys)
        self.assertEqual(
            set(stow),
            {
                "rearward_delta_m",
                "outward_delta_m",
                "transition_pose_mode",
                "draw_extraction_back_clearance_m",
                "draw_extraction_lateral_m",
                "endpoint_max_matrix_error",
                "subframe_reversal_max_matrix_error",
            },
        )

    def test_reauthor_source_matches_v5_manipulation_evidence_schema(self) -> None:
        path = (
            LANE_ROOT.parent
            / "scripts"
            / "reauthor_candidate007_weapon_actions.py"
        )
        text = path.read_text(encoding="utf-8")
        tree = ast.parse(text, filename=str(path))
        expected_constant_names = {
            "MANIPULATION_SOLVER_VERSION",
            "MANIPULATION_DENSIFICATION_VERSION",
            "HAND_CONTACT_PAD_CENTER_LOCAL",
            "HAND_CONTACT_SOLVE_TOLERANCE_M",
            "RELOAD_CONTACT_MODE",
            "SHARED_RELOAD_TARGET_OUTWARD_M",
            "RELOAD_MAGAZINE_HALF_WIDTH_M",
            "RELOAD_CONTACT_INSET_M",
            "RELOAD_DETACHED_TWIST_DEG",
            "RELOAD_HAND_OUTWARD_M",
            "RELOAD_MAGAZINE_OUTWARD_M",
            "RELOAD_PALM_ROLL_DEG",
            "RELOAD_PULL_LUG_OBJECT_NAME",
            "BOLT_TARGET_MODE",
            "SHARED_BOLT_TARGET_OUTWARD_M",
            "BOLT_CONTACT_INSET_M",
            "BOLT_KNOB_OBJECT_NAME",
            "BOLT_PALM_ROLL_DEG",
            "BOLT_HAND_OUTWARD_M",
            "RELOAD_PATH_MODE",
            "BOLT_TARGET_CLASSIFIER_MODE",
            "BOLT_MEASURED_RELEASE_PATH_VERSION",
            "BOLT_MEASURED_POSE_SUBSTITUTIONS",
            "BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M",
            "BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M",
            "RELOAD_MEASURED_RETURN_PATH_VERSION",
            "RELOAD_MEASURED_RETURN_BLEND_ENDPOINT_FRAMES",
            "RELOAD_MEASURED_RETURN_ANCHOR_FRAMES",
            "RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M",
        }
        constants = {}
        target_function = None
        for node in tree.body:
            if isinstance(node, ast.Assign) and len(node.targets) == 1:
                target = node.targets[0]
                if isinstance(target, ast.Name) and target.id in expected_constant_names:
                    constants[target.id] = ast.literal_eval(node.value)
            if (
                isinstance(node, ast.FunctionDef)
                and node.name == "reauthor_candidate007_weapon_actions"
            ):
                target_function = node
        self.assertEqual(set(constants), expected_constant_names)
        manipulation = self.profile["animation_authoring"]["manipulation"]
        field_to_constant = {
            "manipulation_solver_version": "MANIPULATION_SOLVER_VERSION",
            "hand_contact_pad_center_local": "HAND_CONTACT_PAD_CENTER_LOCAL",
            "hand_contact_solve_tolerance_m": "HAND_CONTACT_SOLVE_TOLERANCE_M",
            "reload_contact_mode": "RELOAD_CONTACT_MODE",
            "reload_shared_target_outward_m": "SHARED_RELOAD_TARGET_OUTWARD_M",
            "reload_magazine_half_width_m": "RELOAD_MAGAZINE_HALF_WIDTH_M",
            "reload_contact_inset_m": "RELOAD_CONTACT_INSET_M",
            "reload_detached_twist_deg": "RELOAD_DETACHED_TWIST_DEG",
            "reload_hand_outward_m": "RELOAD_HAND_OUTWARD_M",
            "reload_magazine_outward_m": "RELOAD_MAGAZINE_OUTWARD_M",
            "reload_palm_roll_deg": "RELOAD_PALM_ROLL_DEG",
            "reload_pull_lug_object_name": "RELOAD_PULL_LUG_OBJECT_NAME",
            "bolt_contact_mode": "BOLT_TARGET_MODE",
            "bolt_target_mode": "BOLT_TARGET_MODE",
            "bolt_shared_target_outward_m": "SHARED_BOLT_TARGET_OUTWARD_M",
            "bolt_contact_inset_m": "BOLT_CONTACT_INSET_M",
            "bolt_knob_object_name": "BOLT_KNOB_OBJECT_NAME",
            "bolt_palm_roll_deg": "BOLT_PALM_ROLL_DEG",
            "bolt_hand_outward_m": "BOLT_HAND_OUTWARD_M",
            "reload_path_mode": "RELOAD_PATH_MODE",
            "bolt_target_classifier_mode": "BOLT_TARGET_CLASSIFIER_MODE",
            "bolt_measured_release_path_version": "BOLT_MEASURED_RELEASE_PATH_VERSION",
            "bolt_measured_pose_substitutions": "BOLT_MEASURED_POSE_SUBSTITUTIONS",
            "bolt_measured_release_deltas_root_local_m": (
                "BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M"
            ),
            "bolt_measured_eighth_frame_clearances_m": (
                "BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M"
            ),
            "reload_measured_return_path_version": (
                "RELOAD_MEASURED_RETURN_PATH_VERSION"
            ),
            "reload_measured_return_blend_endpoint_frames": (
                "RELOAD_MEASURED_RETURN_BLEND_ENDPOINT_FRAMES"
            ),
            "reload_measured_return_anchor_frames": (
                "RELOAD_MEASURED_RETURN_ANCHOR_FRAMES"
            ),
            "reload_measured_return_deltas_root_local_m": (
                "RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M"
            ),
        }
        for field, constant_name in field_to_constant.items():
            with self.subTest(field=field):
                actual = json.loads(json.dumps(constants[constant_name]))
                self.assertEqual(actual, manipulation[field])
        self.assertIn('"reload_detached_frames": [36, 50, 64]', text)
        self.assertIn('"reload_seated_frames": [14, 25, 75]', text)
        self.assertIn('"bolt_contact_frames": [4, 8, 12, 16]', text)
        self.assertEqual(
            constants["MANIPULATION_DENSIFICATION_VERSION"],
            self.profile["animation_authoring"]["manipulation_densification"][
                "version"
            ],
        )
        self.assertEqual(
            manipulation["bolt_target_corridor_root_local_m"],
            {
                "relative_to": "tagged_bolt_center",
                "x_offset_m": -0.035,
                "y_min_m": -0.095,
                "y_max_m": 0.0,
                "z_offset_m": 0.0,
                "axis_tolerance_m": 0.000001,
            },
        )
        self.assertIsNotNone(target_function)
        emitted_keys = {
            node.value
            for node in ast.walk(target_function)
            if isinstance(node, ast.Constant) and isinstance(node.value, str)
        }
        self.assertTrue(set(manipulation) <= emitted_keys)

    def test_candidate007_contact_window_policy_matches_shared_source(self) -> None:
        policy_path = LANE_ROOT.parent / "scripts" / "clearance_face_policy.py"
        builder_path = (
            LANE_ROOT.parent
            / "scripts"
            / "build_nextgen_precision_rifle_candidate007.py"
        )
        policy_tree = ast.parse(
            policy_path.read_text(encoding="utf-8"), filename=str(policy_path)
        )
        constants = {}
        for node in policy_tree.body:
            if isinstance(node, ast.Assign) and len(node.targets) == 1:
                target = node.targets[0]
                if isinstance(target, ast.Name) and target.id in {
                    "POLICY_VERSION",
                    "CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION",
                }:
                    constants[target.id] = ast.literal_eval(node.value)
        clearance = self.profile["clearance"]
        self.assertEqual(clearance["policy_version"], constants["POLICY_VERSION"])
        self.assertEqual(
            clearance["contact_window_policy_version"],
            constants["CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION"],
        )
        builder_source = builder_path.read_text(encoding="utf-8")
        self.assertIn(
            "face_policy.CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION",
            builder_source,
        )
        self.assertIn('"contact_window_policy_version"', builder_source)

    def test_profile_rejects_legacy_or_changed_clearance_policy(self) -> None:
        for field, invalid in (
            ("policy_version", "PS_CLEARANCE_FACE_POLICY_V2"),
            (
                "contact_window_policy_version",
                "PS_CLEARANCE_CONTACT_WINDOWS_V1",
            ),
        ):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "profile.json"
                tampered = json.loads(json.dumps(self.profile))
                tampered["clearance"][field] = invalid
                path.write_text(json.dumps(tampered), encoding="utf-8")
                with self.assertRaises(ContractError):
                    load_profile(path)

    def test_component_architecture_separates_fixed_magazine_and_bolt(self) -> None:
        architecture = self.profile["weapon"]["component_architecture"]
        assignments = {
            role: ["WeaponRoot"] for role in architecture["fixed"]["roles"]
        }
        assignments.update({"magazine": ["WeaponMagazine"], "bolt": ["WeaponBolt"]})
        evidence = {"role_control_assignments": assignments}
        self.assertEqual(
            validate_component_architecture_evidence(evidence, architecture), []
        )
        evidence["role_control_assignments"]["magazine"] = [
            "WeaponRoot",
            "WeaponMagazine",
        ]
        errors = validate_component_architecture_evidence(evidence, architecture)
        self.assertTrue(any("magazine" in error for error in errors))

    def test_topology_provenance_binds_every_visible_renderer(self) -> None:
        visible = {
            "NGPR002_Rifle_LOD0": {
                "vertices": 12000,
                "triangles": 24000,
                "topology": {
                    "boundary_edges": 0,
                    "non_manifold_edges": 0,
                    "zero_area_faces": 0,
                    "duplicate_vertex_pairs": 0,
                },
            },
            "NGPR002_Optic_LOD0": {
                "vertices": 1000,
                "triangles": 2000,
                "topology": {
                    "boundary_edges": 0,
                    "non_manifold_edges": 0,
                    "zero_area_faces": 0,
                    "duplicate_vertex_pairs": 0,
                },
            },
        }
        manifest = {
            name: {
                "vertices": metric["vertices"],
                "triangles": metric["triangles"],
                **metric["topology"],
            }
            for name, metric in visible.items()
        }
        triangle_counts = {
            name: metric["triangles"] for name, metric in manifest.items()
        }
        digest = "a" * 64
        self.assertEqual(
            validate_topology_provenance(
                visible, manifest, triangle_counts, digest, digest
            ), []
        )
        tampered = json.loads(json.dumps(manifest))
        tampered["NGPR002_Rifle_LOD0"]["vertices"] += 1
        self.assertTrue(any(
            "vertices differs" in error
            for error in validate_topology_provenance(
                visible, tampered, triangle_counts, digest, digest
            )
        ))
        tampered = json.loads(json.dumps(manifest))
        tampered.pop("NGPR002_Optic_LOD0")
        self.assertTrue(any(
            "renderer set differs" in error
            for error in validate_topology_provenance(
                visible, tampered, triangle_counts, digest, digest
            )
        ))
        self.assertTrue(any(
            "not bound" in error
            for error in validate_topology_provenance(
                visible, manifest, triangle_counts, digest, "b" * 64
            )
        ))

    def test_clearance_reports_require_source_bound_visible_zero_contact_pass(self) -> None:
        requirements = self.profile["clearance"]
        action_ranges = self.profile["rig"]["action_ranges"]
        source = "ArtSource/PoweredSuitNextGen/candidates/nextgen_precision_rifle_candidate_v007.blend"
        digest = "a" * 64

        def build_report(kind: str) -> dict[str, object]:
            if kind == "dense_transition_clearance":
                names = requirements["dense_transition_actions"]
                mode = "uniform_dense_frames"
                step = requirements["dense_transition_frame_step"]
                filters = list(names)
                inclusive = True
            else:
                names = sorted(action_ranges)
                mode = (
                    "authored_keyframes"
                    if kind == "authored_clearance"
                    else "all_integer_frames"
                )
                step = None
                filters = []
                inclusive = False
            actions = []
            for name in names:
                start, end = action_ranges[name]
                if kind == "dense_transition_clearance":
                    frames = [
                        tick * step
                        for tick in range(
                            int(round(start / step)),
                            int(round(end / step)) + 1,
                        )
                    ]
                elif kind == "all_frame_clearance":
                    frames = list(range(start, end + 1))
                else:
                    frames = [start, end]
                actions.append({
                    "action": name,
                    "allowed_contact_instances": 0,
                    "forbidden_intersection_instances": 0,
                    "sample_count": len(frames),
                    "sample_frames": frames,
                    "status": "PASS",
                })
            total = sum(entry["sample_count"] for entry in actions)
            report = {
                "candidate_blend": source,
                "candidate_blend_sha256_before": digest,
                "candidate_blend_sha256_after": digest,
                "candidate_blend_preserved": True,
                "status": "PASS",
                "collision_geometry_source": "visible",
                "promotion_eligible_geometry_source": True,
                "forbidden_intersection_instances": 0,
                "action_count": len(names),
                "available_action_count": 24,
                "sampled_frame_count": total,
                "actions": actions,
                "clearance_metadata": {
                    "status": "PASS",
                    "policy_version": requirements["policy_version"],
                    "manifest": {
                        "policy_version": requirements["policy_version"],
                        "contact_window_policy_version": requirements[
                            "contact_window_policy_version"
                        ],
                        "contact_windows": {
                            **requirements["transition_contact_windows"],
                            "buttpad": [],
                            "reload_mag": [],
                            "bolt": [],
                        },
                    },
                },
                "sampling": {
                    "mode": mode,
                    "action_filters": filters,
                    "frame_step": step,
                    "inclusive_action_endpoints": inclusive,
                    "sampled_frame_count": total,
                    "selected_action_names": names,
                },
            }
            report["report_evidence_sha256"] = report_evidence_sha256(report)
            return report

        for kind in (
            "authored_clearance",
            "all_frame_clearance",
            "dense_transition_clearance",
        ):
            with self.subTest(kind=kind):
                report = build_report(kind)
                self.assertEqual(validate_clearance_report(
                    report,
                    kind=kind,
                    source_path=source,
                    source_sha256=digest,
                    requirements=requirements,
                    action_ranges=action_ranges,
                ), [])

        valid_hash = build_report("dense_transition_clearance")
        self.assertEqual(
            validate_report_evidence_sha256(
                valid_hash, label="dense_transition_clearance"
            ),
            [],
        )
        missing_hash = json.loads(json.dumps(valid_hash))
        missing_hash.pop("report_evidence_sha256")
        zero_hash = json.loads(json.dumps(valid_hash))
        zero_hash["report_evidence_sha256"] = "0" * 64
        mutated_hash = json.loads(json.dumps(valid_hash))
        mutated_hash["post_hash_mutation"] = True
        for label, report in (
            ("missing", missing_hash),
            ("zero", zero_hash),
            ("mutated", mutated_hash),
        ):
            with self.subTest(clearance_self_hash=label):
                errors = validate_clearance_report(
                    report,
                    kind="dense_transition_clearance",
                    source_path=source,
                    source_sha256=digest,
                    requirements=requirements,
                    action_ranges=action_ranges,
                )
                self.assertTrue(
                    any("report_evidence_sha256" in error for error in errors),
                    errors,
                )

        dense = build_report("dense_transition_clearance")
        self.assertEqual(dense["sampled_frame_count"], 1284)
        self.assertEqual(
            [entry["sample_count"] for entry in dense["actions"]],
            [153, 665, 233, 233],
        )
        corruptions = []
        missing_sample = json.loads(json.dumps(dense))
        missing_sample["actions"][0]["sample_frames"].pop(1)
        corruptions.append(missing_sample)
        bad_filter = json.loads(json.dumps(dense))
        bad_filter["sampling"]["action_filters"].reverse()
        corruptions.append(bad_filter)
        no_endpoint_contract = json.loads(json.dumps(dense))
        no_endpoint_contract["sampling"]["inclusive_action_endpoints"] = False
        corruptions.append(no_endpoint_contract)
        bad_total = json.loads(json.dumps(dense))
        bad_total["sampling"]["sampled_frame_count"] = 1283
        corruptions.append(bad_total)
        bad_contact = json.loads(json.dumps(dense))
        bad_contact["clearance_metadata"]["manifest"]["contact_windows"][
            "primary_grip"
        ][0]["start"] = 27.0
        corruptions.append(bad_contact)
        for index, report in enumerate(corruptions):
            with self.subTest(corruption=index):
                self.assertTrue(validate_clearance_report(
                    report,
                    kind="dense_transition_clearance",
                    source_path=source,
                    source_sha256=digest,
                    requirements=requirements,
                    action_ranges=action_ranges,
                ))

    def test_profile_requires_candidate007_deforming_weapon_controls(self) -> None:
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
                self.assertEqual(
                    sha256_immutable_input(path, entry["hash_mode"]).lower(),
                    entry["sha256"].lower(),
                )

    def test_canonical_json_input_hash_ignores_eol_whitespace_and_key_order(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            left = directory / "left.json"
            right = directory / "right.json"
            left.write_bytes(b'{\n  "b": 2,\n  "a": {"z": 3, "y": 4}\n}\n')
            right.write_bytes(b'{\r\n"a":{"y":4,"z":3},\r\n"b":2\r\n}\r\n')
            self.assertNotEqual(sha256_file(left), sha256_file(right))
            self.assertEqual(
                sha256_immutable_input(left, "canonical_json"),
                sha256_immutable_input(right, "canonical_json"),
            )

    def test_profile_rejects_raw_byte_mode_for_json_inputs(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "profile.json"
            tampered = json.loads(json.dumps(self.profile))
            tampered["immutable_inputs"]["candidate006_manifest"][
                "hash_mode"
            ] = "raw_binary"
            path.write_text(json.dumps(tampered), encoding="utf-8")
            with self.assertRaises(ContractError):
                load_profile(path)

    def test_candidate005_is_build_source_and_candidate006_is_tracked_evidence(self) -> None:
        immutable = self.profile["immutable_inputs"]
        self.assertNotIn("candidate006_blend", immutable)
        self.assertEqual(
            immutable["candidate005_blend"],
            {
                "hash_mode": "raw_binary",
                "path": (
                    "ArtSource/PoweredSuitNextGen/candidates/"
                    "aegis_vanguard_candidate_v005.blend"
                ),
                "sha256": (
                    "0e800bbfaabdd320415d530a69d0efc7ef67716a0da33cd55"
                    "a39e79e1f0f3f84"
                ),
            },
        )
        self.assertEqual(
            immutable["candidate006_manifest"]["hash_mode"], "canonical_json"
        )
        self.assertEqual(
            immutable["candidate006_production_report"]["hash_mode"],
            "canonical_json",
        )

    def test_profile_rejects_candidate006_blend_as_mandatory_build_input(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "profile.json"
            tampered = json.loads(json.dumps(self.profile))
            tampered["immutable_inputs"]["candidate006_blend"] = {
                "hash_mode": "raw_binary",
                "path": (
                    "ArtSource/PoweredSuitNextGen/candidates/"
                    "nextgen_precision_rifle_candidate_v006.blend"
                ),
                "sha256": "0" * 64,
            }
            path.write_text(json.dumps(tampered), encoding="utf-8")
            with self.assertRaises(ContractError):
                load_profile(path)

    def test_hardpoint_envelopes_accept_candidate007_design_and_reject_drift(self) -> None:
        self.assertEqual(
            set(self.profile["weapon"]["required_helper_roles"]),
            {
                "primary_grip",
                "support_grip",
                "support_grip_min",
                "support_grip_max",
                "stock_contact",
                "sight_ocular",
                "muzzle",
            },
        )
        hardpoints = {
            "primary_grip": [-0.085, -0.070, 0.025],
            "support_grip": [0.120, 0.280, 0.015],
            "support_grip_min": [0.097, 0.250, 0.015],
            "support_grip_max": [0.137, 0.315, 0.015],
            "stock_contact": [-0.112, -0.448, 0.132],
            "sight_ocular": [0.0, -0.28, 0.315],
            "muzzle": [0.0, 1.175, 0.145],
        }
        envelopes = self.profile["weapon"]["hardpoint_envelopes_m"]
        self.assertEqual(
            envelopes["primary_grip"],
            {"x": [-0.095, -0.075], "y": [-0.08, -0.06], "z": [0.015, 0.035]},
        )
        self.assertEqual(
            envelopes["support_grip"],
            {"x": [0.115, 0.125], "y": [0.27, 0.29], "z": [0.005, 0.025]},
        )
        self.assertEqual(
            envelopes["support_grip_min"],
            {"x": [0.096, 0.098], "y": [0.249, 0.251], "z": [0.014, 0.016]},
        )
        self.assertEqual(
            envelopes["support_grip_max"],
            {"x": [0.136, 0.138], "y": [0.314, 0.316], "z": [0.014, 0.016]},
        )
        self.assertEqual(evaluate_hardpoint_envelopes(hardpoints, envelopes), [])
        hardpoints["support_grip"] = [0.2, 0.280, 0.015]
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

    def test_evidence_hash_matches_shared_strict_canonical_algorithm(self) -> None:
        document = {"unicode": "V\u00e4ktare", "nested": {"z": 2, "a": 1}}
        expected_payload = json.dumps(
            document,
            ensure_ascii=True,
            allow_nan=False,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
        self.assertEqual(
            sha256_manifest(document),
            hashlib.sha256(expected_payload).hexdigest(),
        )

    def test_report_evidence_hash_ignores_eol_whitespace_and_key_order(self) -> None:
        lf = json.loads('{\n  "b": 2,\n  "a": {"y": 4, "z": 3}\n}\n')
        crlf = json.loads(
            '{\r\n"a":{"z":3,"y":4},\r\n"b":2\r\n}\r\n'
        )
        self.assertEqual(report_evidence_sha256(lf), report_evidence_sha256(crlf))

        lf["report_evidence_sha256"] = report_evidence_sha256(lf)
        crlf["report_evidence_sha256"] = report_evidence_sha256(crlf)
        self.assertEqual(validate_report_evidence_sha256(lf), [])
        self.assertEqual(validate_report_evidence_sha256(crlf), [])

    def test_report_evidence_hash_fails_closed_on_nan(self) -> None:
        report = {
            "value": float("nan"),
            "report_evidence_sha256": "0" * 64,
        }
        errors = validate_report_evidence_sha256(report)
        self.assertTrue(any("strict canonical JSON" in error for error in errors))
        with self.assertRaises(ValueError):
            report_evidence_sha256(report)

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
        evidence = self.valid_projection_evidence()
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

    def test_projection_schema4_binds_draw_suit_context_and_weapon_occupancy(self) -> None:
        names = self.profile["renders"]["required_filenames"]
        draw_name = "nextgen_precision_rifle_pose_draw.png"

        boundary = self.valid_projection_evidence()
        draw = boundary["views"][draw_name]
        draw.update({
            "context_viewport_max_x": 0.30,
            "context_viewport_width": 0.20,
            "context_viewport_max_y": 0.33,
            "context_viewport_height": 0.08,
            "context_visible_sample_count": 24,
        })
        self.assertEqual(validate_projection_evidence(boundary, names), [])

        cases = (
            ("schema", "schema_version", 3, "schema differs"),
            (
                "context_kind",
                "context_evidence_kind",
                "weapon_bounds_5_95",
                "context evidence rule",
            ),
            (
                "visible_samples",
                "context_visible_sample_count",
                23,
                "at least 24 visible context samples",
            ),
            (
                "projected_samples",
                "context_projected_sample_count",
                23,
                "invalid projected context sample count",
            ),
        )
        for case, field, value, message in cases:
            with self.subTest(case=case):
                tampered = self.valid_projection_evidence()
                target = tampered if field == "schema_version" else tampered["views"][draw_name]
                target[field] = value
                self.assertTrue(any(
                    message in error
                    for error in validate_projection_evidence(tampered, names)
                ))

        narrow = self.valid_projection_evidence()
        narrow_draw = narrow["views"][draw_name]
        narrow_draw["context_viewport_max_x"] = 0.29
        narrow_draw["context_viewport_width"] = 0.19
        self.assertTrue(any(
            "context viewport width is below 0.20" in error
            for error in validate_projection_evidence(narrow, names)
        ))

        short = self.valid_projection_evidence()
        short_draw = short["views"][draw_name]
        short_draw["context_viewport_max_y"] = 0.32
        short_draw["context_viewport_height"] = 0.07
        self.assertTrue(any(
            "context viewport height is below 0.08" in error
            for error in validate_projection_evidence(short, names)
        ))

        small_weapon = self.valid_projection_evidence()
        weapon_draw = small_weapon["views"][draw_name]
        weapon_draw.update({
            "viewport_min_x": 0.25,
            "viewport_max_x": 0.74,
            "viewport_min_y": 0.30,
            "viewport_max_y": 0.70,
            "viewport_width": 0.49,
            "viewport_height": 0.40,
        })
        self.assertTrue(any(
            "weapon occupancy is below 0.50" in error
            for error in validate_projection_evidence(small_weapon, names)
        ))

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

    def test_production_report_seal_recomputes_and_detects_mutation(self) -> None:
        report = {
            "issues": [],
            "evidence": {name: True for name in REQUIRED_EVIDENCE},
        }
        finalise_report(report)
        seal_production_report(report)
        original = report["report_evidence_sha256"]
        self.assertEqual(original, report_evidence_sha256(report))
        self.assertEqual(validate_production_report_seal(report), [])

        report["status"] = "FAIL"
        self.assertTrue(validate_production_report_seal(report))
        seal_production_report(report)
        self.assertNotEqual(report["report_evidence_sha256"], original)
        self.assertEqual(validate_production_report_seal(report), [])

    def test_production_report_seal_requires_finalise_and_finite_json(self) -> None:
        with self.assertRaises(ContractError):
            seal_production_report({"issues": [], "evidence": {}})

        report = {
            "issues": [],
            "evidence": {name: True for name in REQUIRED_EVIDENCE},
        }
        finalise_report(report)
        report["non_finite"] = float("nan")
        with self.assertRaises(ValueError):
            seal_production_report(report)

    def test_blender_adapter_seals_every_exit_after_finalise_before_write(self) -> None:
        source = (LANE_ROOT / "validate_candidate007.py").read_text(
            encoding="utf-8"
        )
        expected_sequences = (
            (
                "finalise_report(missing)\n"
                "        seal_production_report(missing)\n"
                "        write_canonical_json(report_output, missing)"
            ),
            (
                "finalise_report(report)\n"
                "    seal_production_report(report)\n"
                "    write_canonical_json(report_output, report)"
            ),
            (
                "finalise_report(report)\n"
                "        seal_production_report(report)\n"
                "        write_canonical_json(report_path_value, report)"
            ),
        )
        for sequence in expected_sequences:
            with self.subTest(sequence=sequence.splitlines()[-1]):
                self.assertIn(sequence, source)
        self.assertIn('"hash_mode": "canonical_json"', source)
        self.assertIn('"sha256": sha256_manifest(profile)', source)
        self.assertIn('semantic_sha256 = sha256_manifest(document)', source)

    def test_missing_source_report_is_written_as_a_blocking_result(self) -> None:
        report = missing_source_report("missing_candidate007.blend")
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
