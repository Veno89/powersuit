from __future__ import annotations

import ast
import unittest
from pathlib import Path


SCRIPT = (
    Path(__file__).resolve().parents[1]
    / "scripts"
    / "validate_weapon_clearance.py"
)


class ClearanceWeaponLaneSelectionTests(unittest.TestCase):
    def test_validator_discovers_both_weapon_v2_and_weapon_v3_lod0(self) -> None:
        module = ast.parse(SCRIPT.read_text(encoding="utf-8"), filename=str(SCRIPT))
        assignments = {
            target.id: ast.literal_eval(node.value)
            for node in module.body
            if isinstance(node, ast.Assign)
            for target in node.targets
            if isinstance(target, ast.Name)
            and target.id
            in {
                "WEAPON_V2_ROLE_PROPERTY",
                "WEAPON_V2_LOD_PROPERTY",
                "WEAPON_V3_ROLE_PROPERTY",
                "WEAPON_V3_LOD_PROPERTY",
            }
        }
        self.assertEqual(
            assignments,
            {
                "WEAPON_V2_ROLE_PROPERTY": "weapon_v2_role",
                "WEAPON_V2_LOD_PROPERTY": "weapon_v2_lod",
                "WEAPON_V3_ROLE_PROPERTY": "weapon_v3_role",
                "WEAPON_V3_LOD_PROPERTY": "weapon_v3_lod",
            },
        )

        selector = next(
            node
            for node in module.body
            if isinstance(node, ast.FunctionDef) and node.name == "rifle_objects"
        )
        referenced_names = {
            node.id for node in ast.walk(selector) if isinstance(node, ast.Name)
        }
        self.assertTrue(
            {
                "WEAPON_V2_ROLE_PROPERTY",
                "WEAPON_V2_LOD_PROPERTY",
                "WEAPON_V3_ROLE_PROPERTY",
                "WEAPON_V3_LOD_PROPERTY",
            }
            <= referenced_names
        )

    def test_report_policy_describes_candidate007_transition_windows_truthfully(self) -> None:
        text = SCRIPT.read_text(encoding="utf-8")
        self.assertIn(
            "all armor contact while stowed, plus draw/sheathe contact not "
            "explicitly windowed by compatible face semantics",
            text,
        )
        self.assertNotIn(
            "all armor contact while stowed or during draw/sheathe",
            text,
        )


if __name__ == "__main__":
    unittest.main()
