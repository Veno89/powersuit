from __future__ import annotations

import math
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from clearance_sampling import (  # noqa: E402
    SamplingError,
    inclusive_frame_samples,
    sampling_mode,
    select_action_names,
    total_dense_sample_count,
    validate_frame_step,
)


class ClearanceSamplingTests(unittest.TestCase):
    def test_candidate006_dense_transition_set_is_exactly_324_samples(self) -> None:
        ranges = {
            "PS_Reload": (1, 84),
            "PS_BoltCycle": (1, 20),
            "PS_Weapon_Draw": (1, 30),
            "PS_Weapon_Sheathe": (1, 30),
        }
        selected = select_action_names(
            sorted(ranges),
            [
                "PS_Reload",
                "PS_BoltCycle",
                "PS_Weapon_Draw",
                "PS_Weapon_Sheathe",
            ],
        )
        counts = {
            name: len(inclusive_frame_samples(*ranges[name], 0.5))
            for name in selected
        }
        self.assertEqual(counts["PS_Reload"], 167)
        self.assertEqual(counts["PS_BoltCycle"], 39)
        self.assertEqual(counts["PS_Weapon_Draw"], 59)
        self.assertEqual(counts["PS_Weapon_Sheathe"], 59)
        self.assertEqual(
            total_dense_sample_count((ranges[name] for name in selected), 0.5),
            324,
        )

    def test_candidate007_certification_samples_between_dense_authored_keys(self) -> None:
        ranges = {
            "PS_Reload": (1, 84),
            "PS_BoltCycle": (1, 20),
            "PS_Weapon_Draw": (1, 30),
            "PS_Weapon_Sheathe": (1, 30),
        }
        counts = {
            name: len(inclusive_frame_samples(*frame_range, 0.25))
            for name, frame_range in ranges.items()
        }
        self.assertEqual(counts, {
            "PS_Reload": 333,
            "PS_BoltCycle": 77,
            "PS_Weapon_Draw": 117,
            "PS_Weapon_Sheathe": 117,
        })
        self.assertEqual(total_dense_sample_count(ranges.values(), 0.25), 644)

    def test_dense_samples_include_both_action_endpoints(self) -> None:
        frames = inclusive_frame_samples(1, 2, 0.4)
        self.assertEqual(frames, [1.0, 1.4, 1.8, 2.0])
        self.assertTrue(all(1.0 <= frame <= 2.0 for frame in frames))

    def test_action_filter_is_exact_deterministic_and_fail_closed(self) -> None:
        available = ["PS_Aim", "PS_Reload", "PS_Weapon_Draw"]
        self.assertEqual(
            select_action_names(available, ["PS_Weapon_Draw", "PS_Aim"]),
            ["PS_Aim", "PS_Weapon_Draw"],
        )
        self.assertEqual(select_action_names(available, None), available)
        with self.assertRaises(SamplingError):
            select_action_names(available, ["PS_Missing"])
        with self.assertRaises(SamplingError):
            select_action_names(available, ["PS_Aim", "PS_Aim"])

    def test_frame_step_rejects_nonfinite_nonpositive_and_out_of_dense_range(self) -> None:
        for value in (0, -0.5, math.nan, math.inf, -math.inf, 1.01, True):
            with self.subTest(value=value), self.assertRaises(SamplingError):
                validate_frame_step(value)
        self.assertEqual(validate_frame_step(0.5), 0.5)
        self.assertEqual(validate_frame_step(1), 1.0)

    def test_sampling_modes_preserve_legacy_behavior_and_reject_conflict(self) -> None:
        self.assertEqual(
            sampling_mode(all_frames=False, frame_step=None),
            "authored_keyframes",
        )
        self.assertEqual(
            sampling_mode(all_frames=True, frame_step=None),
            "all_integer_frames",
        )
        self.assertEqual(
            sampling_mode(all_frames=False, frame_step=0.5),
            "uniform_dense_frames",
        )
        with self.assertRaises(SamplingError):
            sampling_mode(all_frames=True, frame_step=0.5)

    def test_action_range_must_be_finite_and_ordered(self) -> None:
        for start, end in ((2, 1), (math.nan, 2), (1, math.inf)):
            with self.subTest(start=start, end=end), self.assertRaises(SamplingError):
                inclusive_frame_samples(start, end, 0.5)


if __name__ == "__main__":
    unittest.main()
