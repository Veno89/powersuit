"""Pure source-contract tests for Candidate007 measured manipulation paths."""

from __future__ import annotations

import ast
import unittest
from pathlib import Path


SCRIPT = (
    Path(__file__).resolve().parents[1]
    / "scripts"
    / "reauthor_candidate007_weapon_actions.py"
)


def parsed_source() -> tuple[str, ast.Module]:
    text = SCRIPT.read_text(encoding="utf-8")
    return text, ast.parse(text, filename=str(SCRIPT))


def literal_assignments(tree: ast.Module) -> dict[str, object]:
    values: dict[str, object] = {}
    for node in tree.body:
        if not isinstance(node, ast.Assign) or len(node.targets) != 1:
            continue
        target = node.targets[0]
        if not isinstance(target, ast.Name):
            continue
        try:
            values[target.id] = ast.literal_eval(node.value)
        except (TypeError, ValueError):
            continue
    return values


class Candidate007ManipulationOffsetTests(unittest.TestCase):
    def test_measured_v11_constants_are_exact(self) -> None:
        text, tree = parsed_source()
        values = literal_assignments(tree)
        self.assertEqual(values["RELOAD_HAND_OUTWARD_M"], 0.09)
        self.assertEqual(values["RELOAD_MAGAZINE_OUTWARD_M"], 0.05)
        self.assertAlmostEqual(
            values["RELOAD_HAND_OUTWARD_M"]
            - values["RELOAD_MAGAZINE_OUTWARD_M"],
            0.04,
        )
        self.assertEqual(values["RELOAD_PALM_ROLL_DEG"], 25.0)
        self.assertEqual(values["BOLT_HAND_OUTWARD_M"], 0.04)
        self.assertEqual(values["BOLT_PALM_ROLL_DEG"], 30.0)
        self.assertEqual(
            values["MANIPULATION_SOLVER_VERSION"],
            "CANDIDATE007_MANIPULATION_SOLVER_V3",
        )
        self.assertEqual(values["REAUTHOR_VERSION"], "CANDIDATE007_WEAPON_ACTIONS_V11")
        self.assertEqual(values["ACTION_SIGNATURE_SCHEMA"], "CANDIDATE007_ACTION_SEMANTICS_V10")
        self.assertEqual(values["RELOAD_MAGAZINE_HALF_WIDTH_M"], 0.030)
        self.assertEqual(values["RELOAD_CONTACT_INSET_M"], 0.001)
        self.assertEqual(values["RELOAD_DETACHED_TWIST_DEG"], 60.0)
        self.assertEqual(values["RELOAD_PULL_LUG_OBJECT_NAME"], "NGPR_MagazinePullLug_L")
        self.assertEqual(values["BOLT_CONTACT_INSET_M"], 0.001)
        self.assertEqual(values["SHARED_BOLT_TARGET_OUTWARD_M"], 0.035)
        self.assertEqual(values["BOLT_TARGET_TRAVEL_Y_RANGE_M"], (-0.095, 0.0))
        self.assertEqual(values["BOLT_TARGET_CORRIDOR_AXIS_TOLERANCE_M"], 1.0e-6)
        self.assertEqual(
            values["BOLT_TARGET_CLASSIFIER_MODE"],
            "exact_root_local_shared_bolt_call_corridor",
        )
        self.assertNotIn("BOLT_MANIPULATION_MATCH_RADIUS_M", text)
        self.assertEqual(values["HAND_CONTACT_SOLVE_TOLERANCE_M"], 5.0e-6)
        self.assertEqual(
            values["MANIPULATION_DENSIFICATION_VERSION"],
            "CANDIDATE007_MANIPULATION_DENSIFICATION_V5",
        )
        self.assertEqual(values["MANIPULATION_SAMPLE_STEP_FRAMES"], 0.25)
        self.assertEqual(values["RELOAD_CONTACT_WINDOW"], (25.0, 75.0))
        self.assertEqual(values["BOLT_CONTACT_WINDOW"], (4.0, 16.0))
        self.assertEqual(values["RELOAD_APPROACH_FRAMES"], (14.0, 16.0, 18.75, 20.0, 24.0, 25.0))
        self.assertEqual(values["BOLT_APPROACH_FRAMES"], (1.0, 1.75, 2.5, 3.0, 4.0))
        self.assertEqual(values["MANIPULATION_HOVER_CLEARANCE_M"], 0.025)
        self.assertEqual(
            values["BOLT_MEASURED_RELEASE_PATH_VERSION"],
            "CANDIDATE007_BOLT_RELEASE_PATH_V2",
        )
        self.assertEqual(
            values["BOLT_MEASURED_POSE_SUBSTITUTIONS"],
            {2.375: 3.0, 2.5: 3.0, 17.5: 17.0, 17.625: 17.0},
        )
        self.assertEqual(
            values["BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M"],
            {
                3.875: 0.025,
                6.125: 0.035,
                6.875: 0.035,
                13.875: 0.035,
                16.125: 0.025,
            },
        )
        self.assertEqual(
            values["BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M"],
            {
                1.25: (0.000000008, 0.000053250, -0.002375834),
                1.50: (0.000000015, 0.000106500, -0.004751668),
                1.75: (0.000000008, 0.000053250, -0.002375834),
                18.75: (-0.001610478, 0.000063438, -0.001026265),
                19.00: (-0.003220956, 0.000126878, -0.002052529),
                19.25: (0.000000064, 0.000091366, -0.004085246),
                19.50: (-0.000000026, 0.000057798, -0.002586497),
                19.75: (-0.000000013, 0.000028899, -0.001293249),
            },
        )
        self.assertEqual(
            values["RELOAD_MEASURED_RETURN_PATH_VERSION"],
            "CANDIDATE007_RELOAD_RETURN_PATH_V1",
        )
        self.assertEqual(
            values["RELOAD_MEASURED_RETURN_BLEND_ENDPOINT_FRAMES"],
            (79.0, 82.0),
        )
        self.assertEqual(
            values["RELOAD_MEASURED_RETURN_ANCHOR_FRAMES"],
            (79.75, 80.0),
        )
        self.assertEqual(
            values["RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M"],
            {79.875: (0.002, 0.0, 0.0)},
        )

    def test_bolt_target_classifier_accepts_only_the_shared_root_local_corridor(self) -> None:
        text, tree = parsed_source()
        values = literal_assignments(tree)
        function = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "_is_candidate007_bolt_target_offset_root_local"
        )
        namespace = {
            "SHARED_BOLT_TARGET_OUTWARD_M": values["SHARED_BOLT_TARGET_OUTWARD_M"],
            "BOLT_TARGET_TRAVEL_Y_RANGE_M": values["BOLT_TARGET_TRAVEL_Y_RANGE_M"],
            "BOLT_TARGET_CORRIDOR_AXIS_TOLERANCE_M": values[
                "BOLT_TARGET_CORRIDOR_AXIS_TOLERANCE_M"
            ],
        }
        extracted = ast.Module(body=[function], type_ignores=[])
        ast.fix_missing_locations(extracted)
        exec(compile(extracted, filename=str(SCRIPT), mode="exec"), namespace)
        classify = namespace["_is_candidate007_bolt_target_offset_root_local"]

        self.assertTrue(classify(-0.035, 0.0, 0.0))
        self.assertTrue(classify(-0.035, -0.0475, 0.0))
        self.assertTrue(classify(-0.035, -0.095, 0.0))
        self.assertFalse(classify(0.0, 0.0, 0.0))
        self.assertFalse(classify(-0.035, 0.001, 0.0))
        self.assertFalse(classify(-0.035, -0.096, 0.0))
        self.assertFalse(classify(-0.035, 0.0, 0.001))

        wrapper = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "_candidate007_single_arm_pose"
        )
        wrapper_source = ast.get_source_segment(text, wrapper)
        assert wrapper_source is not None
        self.assertIn(
            "_is_candidate007_bolt_target_offset_root_local(", wrapper_source
        )
        self.assertNotIn("BOLT_MANIPULATION_MATCH_RADIUS_M", wrapper_source)

    def test_distal_pad_solver_derives_wrist_from_contact(self) -> None:
        text, tree = parsed_source()
        function = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "_solve_hand_contact_frame"
        )
        function_source = ast.get_source_segment(text, function)
        assert function_source is not None
        self.assertIn("wrist_target = contact_world - (desired_rotation_world @ pad_local)", function_source)
        self.assertIn("pad_contact_error", function_source)
        self.assertIn("tolerance_m = HAND_CONTACT_SOLVE_TOLERANCE_M", function_source)

    def test_v3_contact_modes_and_measured_pads_are_persisted(self) -> None:
        text, _tree = parsed_source()
        self.assertIn('"ps_candidate007_hand_contact_pad_center_local_json"', text)
        self.assertIn('"ps_candidate007_reload_contact_mode"', text)
        self.assertIn('"ps_candidate007_bolt_contact_mode"', text)
        self.assertIn('"ps_candidate007_bolt_target_mode"', text)
        self.assertIn('"hand_contact_pad_center_local": HAND_CONTACT_PAD_CENTER_LOCAL', text)
        self.assertIn('"reload_detached_frames": [36, 50, 64]', text)
        self.assertIn('"bolt_contact_frames": [4, 8, 12, 16]', text)
        self.assertIn('"bolt_target_mode": BOLT_TARGET_MODE', text)
        self.assertIn('"ps_candidate007_bolt_target_classifier_mode"', text)
        self.assertIn('"ps_candidate007_bolt_target_corridor_root_local_json"', text)
        self.assertIn(
            '"bolt_target_classifier_mode": BOLT_TARGET_CLASSIFIER_MODE', text
        )
        self.assertIn(
            '"bolt_target_corridor_root_local_m": bolt_target_corridor_root_local',
            text,
        )
        self.assertNotIn('"bolt_manipulation_match_radius_m"', text)

    def test_transition_preserves_guided_corridor_then_uses_raw_late_catch(self) -> None:
        text, tree = parsed_source()
        function = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "_candidate007_transition_poses"
        )
        function_source = ast.get_source_segment(text, function)
        assert function_source is not None
        self.assertIn("1.0: weapon_stage._copy_pose(source[1])", function_source)
        self.assertIn("6.0: placed(source[1], stowed_root, far_back)", function_source)
        self.assertIn("10.0: placed(source[1], ready_root, far_back)", function_source)
        self.assertIn("16.0: placed(source[1], ready_root, far_front)", function_source)
        self.assertIn("24.0: placed(source[30], ready_root, far_front)", function_source)
        self.assertIn("29.0: placed(source[30], ready_root, dock_near)", function_source)
        self.assertIn("30.0: weapon_stage._copy_pose(source[30])", function_source)
        self.assertIn("original_single_arm_solver", function_source)
        self.assertIn("acquisition_body = weapon_stage._blend_pose(", function_source)
        self.assertIn("(-TRANSITION_PREGRASP_CLEARANCE_M, 0.0, 0.0)", function_source)
        self.assertIn("ownership_frames = _frame_samples(", function_source)
        self.assertIn("ownership_roots = {", function_source)
        self.assertIn("desired_hand = target_root @ ready_root_to_hand", function_source)
        self.assertNotIn("_candidate007_single_arm_pose", function_source)
        self.assertNotIn("DRAW_MID_READY_BLEND", function_source)

    def test_dense_v5_is_built_at_subframes_and_source_bound(self) -> None:
        text, tree = parsed_source()
        densify = next(
            node for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "_densify_manipulation_poses"
        )
        source = ast.get_source_segment(text, densify)
        assert source is not None
        self.assertIn("_reload_evaluated_contact_pose", source)
        self.assertIn("_bolt_evaluated_contact_pose", source)
        self.assertIn("for frame in _half_frame_samples(start, end)", source)
        self.assertIn('"co_solved_sample_count"', source)
        self.assertIn('"manipulation_densification_evidence"', text)
        self.assertIn('"transition_path_evidence"', text)
        self.assertIn("quaternion_bones=quaternion_bones", text)
        self.assertIn('"LINEAR"', text)

    def test_v11_measured_manipulation_paths_are_applied_and_emitted(self) -> None:
        text, tree = parsed_source()
        densify = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "_densify_manipulation_poses"
        )
        densify_source = ast.get_source_segment(text, densify)
        assert densify_source is not None
        self.assertIn(
            "for target_frame, source_frame in BOLT_MEASURED_POSE_SUBSTITUTIONS.items()",
            densify_source,
        )
        self.assertIn(
            "dense[target_frame] = weapon_stage._copy_pose(dense[source_frame])",
            densify_source,
        )
        self.assertIn(
            "for frame, delta in BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M.items()",
            densify_source,
        )
        self.assertIn("dense[frame] = _offset_hand_in_root_space(", densify_source)
        contact_loop = densify_source.index(
            "for frame in _half_frame_samples(start, end)"
        )
        eighth_frame_solves = densify_source.index(
            "for frame, clearance_m in BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M.items()"
        )
        self.assertGreater(eighth_frame_solves, contact_loop)
        self.assertIn(
            "interpolated(frame),\n                root,\n                side,\n                clearance_m",
            densify_source,
        )
        self.assertIn(
            "return_start, return_end = RELOAD_MEASURED_RETURN_BLEND_ENDPOINT_FRAMES",
            densify_source,
        )
        self.assertIn("*RELOAD_MEASURED_RETURN_ANCHOR_FRAMES", densify_source)
        self.assertIn(
            "*RELOAD_MEASURED_RETURN_DELTAS_ROOT_LOCAL_M", densify_source
        )
        self.assertIn(
            "dense[return_start], dense[return_end], factor", densify_source
        )

        offset = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "_offset_hand_in_root_space"
        )
        offset_source = ast.get_source_segment(text, offset)
        assert offset_source is not None
        self.assertIn(
            "delta_world = root_rotation @ Vector(root_local_delta_m)",
            offset_source,
        )

        # Per-action evidence records the substitutions and exact measured map.
        self.assertIn('"measured_release_path_version": BOLT_MEASURED_RELEASE_PATH_VERSION', text)
        self.assertIn('"measured_pose_substitutions": {', text)
        self.assertIn('for frame, source in sorted(BOLT_MEASURED_POSE_SUBSTITUTIONS.items())', text)
        self.assertIn('"measured_release_deltas_root_local_m": {', text)
        self.assertIn('BOLT_MEASURED_RELEASE_DELTAS_ROOT_LOCAL_M.items()', text)
        self.assertIn('"measured_eighth_frame_clearances_m": {', text)
        self.assertIn('BOLT_MEASURED_EIGHTH_FRAME_CLEARANCES_M.items()', text)
        self.assertIn(
            '"measured_return_path_version"] = (', text
        )
        self.assertIn('"measured_return_anchor_frames"] = list(', text)
        self.assertIn('"measured_return_deltas_root_local_m"] = {', text)

        # The blend root and returned build evidence expose the measured path.
        self.assertIn(
            'root["ps_candidate007_bolt_measured_release_path_version"] = (',
            text,
        )
        self.assertIn(
            'root["ps_candidate007_bolt_measured_release_deltas_root_local_json"] = (',
            text,
        )
        self.assertIn('"bolt_measured_release_path_version": BOLT_MEASURED_RELEASE_PATH_VERSION', text)
        self.assertIn('"bolt_measured_release_deltas_root_local_m": {', text)
        self.assertIn(
            'root["ps_candidate007_bolt_measured_pose_substitutions_json"]', text
        )
        self.assertIn(
            'root["ps_candidate007_bolt_measured_eighth_frame_clearances_json"]',
            text,
        )
        self.assertIn(
            'root["ps_candidate007_reload_measured_return_path_version"]', text
        )
        self.assertIn(
            'root["ps_candidate007_reload_measured_return_anchor_frames_json"]',
            text,
        )
        self.assertIn(
            'root["ps_candidate007_reload_measured_return_deltas_root_local_json"]',
            text,
        )
        self.assertIn('"bolt_measured_pose_substitutions": {', text)
        self.assertIn('"bolt_measured_eighth_frame_clearances_m": {', text)
        self.assertIn('"reload_measured_return_path_version":', text)
        self.assertIn('"reload_measured_return_anchor_frames": list(', text)
        self.assertIn('"reload_measured_return_deltas_root_local_m": {', text)

    def test_transition_path_is_exactly_mirrored_and_certified_between_keys(self) -> None:
        text, tree = parsed_source()
        values = literal_assignments(tree)
        self.assertIn('31.0 - frame: weapon_stage._copy_pose(pose)', text)
        self.assertEqual(
            values["TRANSITION_PATH_VERSION"],
            "CANDIDATE007_GUIDED_DEPLOY_LATE_CATCH_V3",
        )
        self.assertIn('"key_frames": list(TRANSITION_DRAW_KEY_FRAMES)', text)
        self.assertIn('"key_frames": list(poses)', text)
        self.assertNotIn("_retime_full_pose_key", text)
        self.assertIn("TRANSITION_SAMPLE_STEP_FRAMES = 0.125", text)
        self.assertIn("TRANSITION_CERTIFICATION_STEP_FRAMES = 0.125", text)
        self.assertEqual(values["TRANSITION_GUIDED_THROUGH_FRAME"], 26.0)
        self.assertEqual(values["TRANSITION_PREGRASP_FRAME"], 27.0)
        self.assertEqual(values["TRANSITION_PREGRASP_TARGET_FRAME"], 28.0)
        self.assertEqual(values["TRANSITION_PREGRASP_CLEARANCE_M"], 0.012)
        self.assertEqual(values["TRANSITION_OWNERSHIP_START_FRAME"], 28.0)
        self.assertEqual(values["TRANSITION_OWNERSHIP_DENSE_END_FRAME"], 29.875)
        self.assertEqual(
            values["TRANSITION_PRIMARY_CONTACT_DRAW_WINDOW"], (26.75, 30.0)
        )
        self.assertEqual(
            values["TRANSITION_PRIMARY_CONTACT_SHEATHE_WINDOW"], (1.0, 4.25)
        )
        self.assertEqual(len(values["TRANSITION_DRAW_KEY_FRAMES"]), 27)
        self.assertIn('"guided_through_frame": TRANSITION_GUIDED_THROUGH_FRAME', text)
        self.assertIn('"early_acquisition_frame": TRANSITION_PREGRASP_FRAME', text)
        self.assertIn('"ownership_start_frame": TRANSITION_OWNERSHIP_START_FRAME', text)
        self.assertIn('"primary_contact_window": list(', text)


if __name__ == "__main__":
    unittest.main()
