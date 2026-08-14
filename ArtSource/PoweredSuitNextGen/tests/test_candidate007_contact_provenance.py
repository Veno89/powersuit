"""Pure source-contract tests for Candidate007 grip-contact provenance."""

from __future__ import annotations

import ast
import re
import unittest
from pathlib import Path


BUILDER = (
    Path(__file__).resolve().parents[1]
    / "scripts"
    / "build_nextgen_precision_rifle_candidate007.py"
)
REAUTHOR = BUILDER.parent / "reauthor_candidate007_weapon_actions.py"


def source() -> str:
    return BUILDER.read_text(encoding="utf-8")


class Candidate007ContactProvenanceTests(unittest.TestCase):
    def test_builder_parses(self) -> None:
        ast.parse(source(), filename=str(BUILDER))

    def test_builder_paths_are_repository_and_blend_relative(self) -> None:
        text = source()
        tree = ast.parse(text, filename=str(BUILDER))
        assignments = {
            node.targets[0].id: ast.literal_eval(node.value)
            for node in tree.body
            if isinstance(node, ast.Assign)
            and len(node.targets) == 1
            and isinstance(node.targets[0], ast.Name)
            and node.targets[0].id == "EXTERNAL_DATABLOCK_COLLECTIONS"
        }
        self.assertEqual(
            assignments["EXTERNAL_DATABLOCK_COLLECTIONS"],
            ("images", "libraries", "movieclips", "sounds", "fonts"),
        )

        functions = {
            node.name: node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
        }
        classifier = functions["_is_absolute_or_drive_qualified_local_path"]
        manifest_assertion = functions["assert_manifest_has_no_local_absolute_paths"]
        extracted = ast.Module(
            body=[classifier, manifest_assertion], type_ignores=[]
        )
        ast.fix_missing_locations(extracted)
        namespace = {
            "ROOT": BUILDER.resolve().parents[3],
            "WINDOWS_DRIVE_PATH": re.compile(r"^[A-Za-z]:[\\/]")
        }
        exec(compile(extracted, filename=str(BUILDER), mode="exec"), namespace)
        classify = namespace["_is_absolute_or_drive_qualified_local_path"]
        assert_manifest = namespace["assert_manifest_has_no_local_absolute_paths"]
        self.assertTrue(classify(r"C:\build\candidate.blend"))
        self.assertTrue(classify("/tmp/candidate.blend"))
        self.assertTrue(classify(r"\build\candidate.blend"))
        self.assertFalse(classify("ArtSource/PoweredSuitNextGen/candidate.blend"))
        self.assertFalse(classify("//../textures/candidate006/base_color.png"))
        assert_manifest(
            {
                "candidate": "ArtSource/PoweredSuitNextGen/candidate.blend",
                "external": "//../textures/candidate006/base_color.png",
            }
        )
        with self.assertRaises(RuntimeError):
            assert_manifest({"nested": [{"path": r"C:\build\candidate.blend"}]})

        relpath_source = ast.get_source_segment(
            text, functions["_blender_relative_to_output"]
        )
        normalize_source = ast.get_source_segment(
            text, functions["normalize_external_blender_paths_for_output"]
        )
        validate_source = ast.get_source_segment(
            text, functions["assert_external_blender_paths_portable"]
        )
        main_source = ast.get_source_segment(text, functions["main"])
        assert relpath_source is not None
        assert normalize_source is not None
        assert validate_source is not None
        assert main_source is not None
        self.assertIn("bpy.path.relpath(", relpath_source)
        self.assertIn('start=str(OUTPUT_BLEND.resolve().parent)', relpath_source)
        self.assertIn('scene.render.filepath = _blender_relative_to_output', normalize_source)
        self.assertIn("_external_datablock_paths()", normalize_source)
        self.assertIn("_external_datablock_paths()", validate_source)
        self.assertIn("not filepath.startswith(\"//\")", validate_source)
        self.assertIn("repository_relative_posix(absolute)", validate_source)

        for exact in (
            '"source_candidate005": repository_relative_posix(SOURCE_BLEND)',
            '"candidate_blend": repository_relative_posix(OUTPUT_BLEND)',
            '"render_paths": [repository_relative_posix(path) for path in render_paths]',
            '"path_portability": path_portability',
        ):
            self.assertIn(exact, main_source)
        render_index = main_source.index("render_paths = render_reviews(")
        normalize_index = main_source.index(
            "normalized_path_count = normalize_external_blender_paths_for_output()"
        )
        save_index = main_source.index("bpy.ops.wm.save_as_mainfile(")
        report_index = main_source.index("report = {")
        manifest_index = main_source.index(
            "assert_manifest_has_no_local_absolute_paths(report)"
        )
        write_index = main_source.index("OUTPUT_REPORT.write_bytes(")
        self.assertLess(render_index, normalize_index)
        self.assertLess(normalize_index, save_index)
        self.assertIn("relative_remap=False", main_source)
        self.assertIn(
            "saved_path_portability = assert_external_blender_paths_portable(",
            main_source,
        )
        self.assertIn(
            "if saved_path_portability != path_portability:", main_source
        )
        self.assertLess(report_index, manifest_index)
        self.assertLess(manifest_index, write_index)
        self.assertIn('"ngpr_path_portability_evidence_json"', main_source)

        reauthor_text = REAUTHOR.read_text(encoding="utf-8")
        self.assertIn(
            '"pinned_pipeline_blend": '
            "PINNED_PIPELINE_BLEND.relative_to(ROOT).as_posix()",
            reauthor_text,
        )
        self.assertNotIn(
            '"pinned_pipeline_blend": str(PINNED_PIPELINE_BLEND)',
            reauthor_text,
        )

    def test_contact_envelope_parts_have_exact_face_semantics(self) -> None:
        text = source()
        for name, semantic in (
            ("NGPR_TriggerGuard", "face_policy.WEAPON_PRIMARY_GRIP"),
            ("NGPR_SupportYoke_Mount", "face_policy.WEAPON_SUPPORT_GRIP"),
            ("NGPR_SupportYoke_Upper", "face_policy.WEAPON_SUPPORT_GRIP"),
            ("NGPR_SupportYoke_Lower", "face_policy.WEAPON_SUPPORT_GRIP"),
            ("NGPR_SupportYoke_HingeCap", "face_policy.WEAPON_SUPPORT_GRIP"),
            ("NGPR_SupportYoke_Guard", "face_policy.WEAPON_SUPPORT_GRIP"),
        ):
            line = next(line for line in text.splitlines() if f'"{name}"' in line)
            self.assertIn(semantic, line, name)

    def test_lower_keel_uses_measured_hand_clearance_height(self) -> None:
        line = next(
            line for line in source().splitlines() if '"NGPR_Lower_Keel"' in line
        )
        self.assertIn("(0.0, 0.070, 0.061)", line)
        self.assertIn("(0.086, 0.275, 0.044)", line)

    def test_primary_grip_uses_measured_cuff_relief(self) -> None:
        text = source()
        pistol = next(line for line in text.splitlines() if '"NGPR_PistolGrip"' in line)
        backstrap = next(
            line for line in text.splitlines() if '"NGPR_GripBackstrap"' in line
        )
        self.assertIn("cap_z_m=-0.040", pistol)
        self.assertIn("backstrap_shift_x_m=0.020", backstrap)
        tree = ast.parse(text, filename=str(BUILDER))
        relief = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "apply_primary_grip_hand_relief"
        )
        relief_source = ast.get_source_segment(text, relief)
        assert relief_source is not None
        self.assertIn("object_to_root = obj.matrix_basis.copy()", relief_source)
        self.assertNotIn("object_to_root = obj.matrix_world.copy()", relief_source)
        self.assertIn("root_position = object_to_root @ vertex.co", relief_source)
        self.assertIn("root_position.z = cap_z_m", relief_source)
        self.assertIn("vertex.co = root_to_object @ root_position", relief_source)
        self.assertNotIn("vertex.co.z = cap_z_m", relief_source)

    def test_stock_contact_relief_is_local_and_measured(self) -> None:
        text = source()
        tree = ast.parse(text, filename=str(BUILDER))
        function = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "apply_stock_contact_perimeter_relief"
        )
        function_source = ast.get_source_segment(text, function)
        assert function_source is not None
        self.assertIn("STOCK_CONTACT_PERIMETER_RELIEF_M = 0.0035", text)
        self.assertIn("maximum_x = max", function_source)
        self.assertIn("minimum_y = min", function_source)
        self.assertIn("minimum_z = min", function_source)
        self.assertIn("target.co += Vector((-relief_m, relief_m, relief_m))", function_source)
        self.assertNotIn("bmesh.ops.bevel", function_source)
        self.assertIn('"topology_preserving_corner_inset"', function_source)
        self.assertIn("transform_before = obj.matrix_world.copy()", function_source)

        for name, center in (
            ("NGPR_Buttpad", "(-0.112, -0.442, 0.132)"),
            ("NGPR_ButtHeel", "(-0.112, -0.454, 0.070)"),
        ):
            line = next(line for line in text.splitlines() if f'"{name}"' in line)
            self.assertIn("apply_stock_contact_perimeter_relief", line, name)
            self.assertIn(center, line, name)

        stock_helper = next(
            line for line in text.splitlines() if '"Rifle_StockContact"' in line
        )
        self.assertIn("(-0.112, -0.448, 0.132)", stock_helper)

    def test_magazine_pull_lug_is_moving_and_semantic(self) -> None:
        text = source()
        tree = ast.parse(text, filename=str(BUILDER))
        build_components = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef) and node.name == "build_components"
        )
        function_source = ast.get_source_segment(text, build_components)
        assert function_source is not None
        lug_start = function_source.index('"NGPR_MagazinePullLug_L"')
        lug_source = function_source[lug_start : lug_start + 500]
        self.assertIn("(0.054, 0.138, -0.105)", lug_source)
        self.assertIn("face_policy.WEAPON_MAGAZINE_GRASP", lug_source)
        self.assertIn("COMPONENT_MAGAZINE", lug_source)
        self.assertIn(
            "(magazine, magazine_base, *magazine_ribs, magazine_pull_lug)",
            function_source,
        )
        tag_loop_start = function_source.index(
            "for component in (magazine, magazine_base, *magazine_ribs, magazine_pull_lug):"
        )
        tag_loop_source = function_source[tag_loop_start : tag_loop_start + 300]
        self.assertIn("tag_component(component, COMPONENT_MAGAZINE)", tag_loop_source)
        self.assertIn("component[WEAPON_OWNER_PROPERTY] = ASSET_ID", tag_loop_source)

    def test_magazine_geometry_is_deferred_until_frame_50_remeasurement(self) -> None:
        text = source()
        magazine = next(
            line for line in text.splitlines() if '"NGPR_Magazine"' in line
        )
        self.assertIn("frame-50 undersuit strike", text)
        self.assertIn("(0.0, 0.135, -0.082)", magazine)
        self.assertIn("(0.058, 0.074, 0.174)", magazine)
        self.assertNotIn("apply_stock_contact_perimeter_relief", magazine)

    def test_clearance_manifest_binds_exact_transition_contact_policy(self) -> None:
        text = source()
        tree = ast.parse(text, filename=str(BUILDER))
        function = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "add_clearance_manifest"
        )
        function_source = ast.get_source_segment(text, function)
        assert function_source is not None
        assignments = {
            node.targets[0].id: ast.literal_eval(node.value)
            for node in function.body
            if isinstance(node, ast.Assign)
            and len(node.targets) == 1
            and isinstance(node.targets[0], ast.Name)
            and node.targets[0].id
            in {
                "primary_transition_contact_windows",
                "support_transition_contact_windows",
            }
        }
        self.assertEqual(
            assignments["primary_transition_contact_windows"],
            [
                {"action": "PS_Weapon_Draw", "start": 26.75, "end": 30},
                {"action": "PS_Weapon_Sheathe", "start": 1, "end": 4.25},
            ],
        )
        self.assertEqual(
            assignments["support_transition_contact_windows"],
            [
                {"action": "PS_Weapon_Draw", "start": 29, "end": 30},
                {"action": "PS_Weapon_Sheathe", "start": 1, "end": 2},
            ],
        )
        self.assertIn(
            "face_policy.CANDIDATE007_CONTACT_WINDOW_POLICY_VERSION",
            function_source,
        )
        self.assertIn(
            '{"action": "PS_Weapon_Draw", "start": 26.75, "end": 30}',
            function_source,
        )
        self.assertIn(
            '{"action": "PS_Weapon_Sheathe", "start": 1, "end": 4.25}',
            function_source,
        )
        self.assertIn(
            "dict(window) for window in primary_transition_contact_windows",
            function_source,
        )
        self.assertIn(
            '{"action": "PS_Weapon_Draw", "start": 29, "end": 30}',
            function_source,
        )
        self.assertIn(
            '{"action": "PS_Weapon_Sheathe", "start": 1, "end": 2}',
            function_source,
        )
        self.assertIn(
            "dict(window) for window in support_transition_contact_windows",
            function_source,
        )
        self.assertIn(
            'candidate007_stowed_legacy_actions = {"PS_Idle", "PS_Walk", "PS_Hover"}',
            function_source,
        )
        self.assertIn(
            "face_policy.READY_ACTIONS - candidate007_stowed_legacy_actions",
            function_source,
        )

    def test_review_set_exposes_guided_and_owned_transition_phases(self) -> None:
        text = source()
        self.assertIn(
            '(RENDER_NAMES[11], "PS_Weapon_Draw", 18,',
            text,
        )
        self.assertIn(
            '(RENDER_NAMES[12], "PS_Weapon_Sheathe", 3,',
            text,
        )
        self.assertNotIn(
            '(RENDER_NAMES[12], "PS_Weapon_Sheathe", 21,',
            text,
        )

    def test_guided_draw_camera_proves_weapon_and_suit_context(self) -> None:
        text = source()
        tree = ast.parse(text, filename=str(BUILDER))
        context_fit = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "fit_context_review_camera"
        )
        context_fit_source = ast.get_source_segment(text, context_fit)
        assert context_fit_source is not None
        self.assertIn("target_weapon_occupancy: float = 0.54", context_fit_source)
        self.assertIn("minimum_weapon_occupancy: float = 0.50", context_fit_source)
        self.assertIn("weapon_center - context_center", context_fit_source)
        self.assertIn("projected_context_metrics", context_fit_source)
        self.assertIn("assert_projected_weapon_visible", context_fit_source)
        self.assertIn("assert_projected_suit_context_visible", context_fit_source)

        render_reviews = next(
            node
            for node in tree.body
            if isinstance(node, ast.FunctionDef) and node.name == "render_reviews"
        )
        render_source = ast.get_source_segment(text, render_reviews)
        assert render_source is not None
        self.assertIn("is_guided_draw = filename == RENDER_NAMES[11]", render_source)
        self.assertIn("fit_context_review_camera", render_source)
        self.assertIn("tuple(suit_renderers)", render_source)
        self.assertIn("target_weapon_occupancy=0.54", render_source)
        self.assertIn("minimum_weapon_occupancy=0.50", render_source)
        self.assertIn("assert_projected_suit_context_visible", render_source)
        self.assertIn('"schema_version": 4', render_source)


if __name__ == "__main__":
    unittest.main()
