from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


LANE_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(LANE_ROOT))

from hero_v2_contract import (  # noqa: E402
    ContractError,
    assert_derivative_path,
    canonical_json_bytes,
    evaluate_triangle_budget,
    infer_role,
    load_profile,
    triangle_budget,
)


class HeroV2ContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.profile = load_profile(LANE_ROOT / "production_profile.json")

    def test_profile_contains_all_suit_and_rifle_lod_budgets(self) -> None:
        for role in ("suit", "rifle"):
            for lod in range(4):
                self.assertIsNotNone(triangle_budget(self.profile, role, lod))
        self.assertEqual(
            self.profile["lods"]["combined_triangle_hard_max"],
            {"LOD0": 130000, "LOD1": 65000, "LOD2": 26000, "LOD3": 9000},
        )

    def test_budget_targets_are_warnings_but_hard_max_is_error(self) -> None:
        budget = {"target_min": 80, "target_max": 100, "hard_max": 110}
        self.assertEqual(evaluate_triangle_budget(79, budget)["severity"], "warning")
        self.assertEqual(evaluate_triangle_budget(90, budget)["severity"], "pass")
        self.assertEqual(evaluate_triangle_budget(105, budget)["severity"], "warning")
        self.assertEqual(evaluate_triangle_budget(111, budget)["severity"], "error")

    def test_explicit_roles_are_strict_and_names_have_conservative_fallbacks(self) -> None:
        self.assertEqual(infer_role("AV_Chest"), "suit")
        self.assertEqual(infer_role("Precision_Rifle_Bolt"), "rifle")
        self.assertEqual(infer_role("Scope_Glass"), "optic")
        self.assertEqual(infer_role("Anything", "SUIT"), "suit")
        with self.assertRaises(ContractError):
            infer_role("Anything", "backpack")

    def test_canonical_json_is_order_independent(self) -> None:
        left = canonical_json_bytes({"b": 2, "a": 1})
        right = canonical_json_bytes({"a": 1, "b": 2})
        self.assertEqual(left, right)
        self.assertEqual(json.loads(left), {"a": 1, "b": 2})

    def test_source_overwrite_is_rejected(self) -> None:
        source = LANE_ROOT / "fixtures" / "candidate.blend"
        with self.assertRaises(ContractError):
            assert_derivative_path(source, source)

    def test_derivatives_outside_lane_are_rejected(self) -> None:
        source = LANE_ROOT / "fixtures" / "candidate.blend"
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaises(ContractError):
                assert_derivative_path(source, Path(temporary) / "candidate_lods.blend")


if __name__ == "__main__":
    unittest.main()
