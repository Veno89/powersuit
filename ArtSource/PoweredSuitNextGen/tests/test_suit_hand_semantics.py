from __future__ import annotations

import math
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from suit_hand_semantics import (  # noqa: E402
    ZONE_GRIP,
    ZONE_MANIPULATION,
    ZONE_ORDINARY,
    classify_hand_surface,
    normalized_chain_influence,
    project_to_bone_segment,
)


HEAD = (0.0, 0.0, 0.7602)
TAIL = (0.0, 0.0, 0.6057)
HAND_WEIGHTS = [
    {"Hand.R": 0.45, "LowerArm.R": 0.55},
    {"Hand.R": 0.60, "LowerArm.R": 0.40},
    {"Hand.R": 0.80, "LowerArm.R": 0.20},
]


def point_at(t: float, radial: float = 0.0) -> tuple[float, float, float]:
    return (radial, 0.0, HEAD[2] + (TAIL[2] - HEAD[2]) * t)


def decision(
    t: float,
    *,
    radial: float = 0.0,
    weights: list[dict[str, float]] | None = None,
    armor_distance: float | None = None,
):
    return classify_hand_surface(
        point=point_at(t, radial),
        bone_head=HEAD,
        bone_tail=TAIL,
        vertex_weights=HAND_WEIGHTS if weights is None else weights,
        hand_group="Hand.R",
        lower_arm_group="LowerArm.R",
        matching_armor_distance_m=armor_distance,
    )


class SuitHandSemanticsTests(unittest.TestCase):
    def test_projection_preserves_distal_t_instead_of_clamping_to_bone_tail(self) -> None:
        projection = project_to_bone_segment(point_at(1.6, 0.02), HEAD, TAIL)
        self.assertAlmostEqual(projection.axial_t, 1.6)
        self.assertAlmostEqual(projection.axis_distance_m, 0.02)
        self.assertGreater(projection.segment_distance_m, projection.axis_distance_m)

    def test_influence_is_normalized_per_vertex(self) -> None:
        mean, minimum = normalized_chain_influence(
            [
                {"Hand.R": 4.0, "Chest": 1.0},
                {"LowerArm.R": 0.9, "Chest": 0.1},
            ],
            ("Hand.R", "LowerArm.R"),
        )
        self.assertAlmostEqual(mean, 0.85)
        self.assertAlmostEqual(minimum, 0.80)

    def test_wrist_proximal_sleeve_fails_closed_even_with_full_chain_weight(self) -> None:
        result = decision(-0.20)
        self.assertEqual(result.zone, ZONE_ORDINARY)
        self.assertEqual(result.reason, "outside_hand_axial_range")

    def test_palm_and_proximal_fingers_are_grip_surfaces(self) -> None:
        result = decision(0.75, radial=0.05)
        self.assertEqual(result.zone, ZONE_GRIP)
        self.assertEqual(result.reason, "palm_or_proximal_finger_surface")

    def test_distal_finger_faces_are_manipulation_surfaces(self) -> None:
        result = decision(1.60, radial=0.04)
        self.assertEqual(result.zone, ZONE_MANIPULATION)
        self.assertEqual(result.reason, "distal_finger_surface")

    def test_remote_or_weak_faces_remain_ordinary(self) -> None:
        self.assertEqual(decision(0.70, radial=0.30).zone, ZONE_ORDINARY)
        weak = [
            {"Hand.R": 0.20, "Chest": 0.80},
            {"LowerArm.R": 0.30, "Chest": 0.70},
            {"Hand.R": 0.25, "Chest": 0.75},
        ]
        self.assertEqual(decision(0.70, weights=weak).zone, ZONE_ORDINARY)

    def test_matching_armor_can_confirm_hidden_surface_but_not_wrong_anatomy(self) -> None:
        self.assertEqual(
            decision(0.70, radial=0.30, armor_distance=0.005).zone,
            ZONE_GRIP,
        )
        self.assertEqual(
            decision(-0.20, radial=0.30, armor_distance=0.005).zone,
            ZONE_ORDINARY,
        )

    def test_invalid_geometry_and_weight_evidence_raises(self) -> None:
        with self.assertRaises(ValueError):
            project_to_bone_segment((math.nan, 0.0, 0.0), HEAD, TAIL)
        with self.assertRaises(ValueError):
            decision(0.5, weights=[{"Hand.R": -0.1}])


if __name__ == "__main__":
    unittest.main()
