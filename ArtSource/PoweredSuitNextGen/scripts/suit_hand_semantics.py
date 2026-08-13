"""Pure anatomical hand-surface classifier for clearance semantics.

Candidate005 has a deliberately simple hand rig: a Hand bone runs from the
wrist through the palm while the procedural fingers extend beyond its tail.
Face semantics therefore cannot be inferred from a dominant vertex group (the
undersuit sleeve never becomes Hand-dominant), nor from arbitrary mesh order.
This module combines normalized Hand+LowerArm influence with measurements in
that rest-bone coordinate system.  Anything not supported by both kinds of
evidence remains ordinary/forbidden.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Iterable, Mapping, Sequence


SCHEMA_VERSION = "PS_SUIT_HAND_SURFACE_TAGGING_V1"

ZONE_ORDINARY = "ordinary"
ZONE_GRIP = "grip"
ZONE_MANIPULATION = "manipulation"

# Normalized per vertex before aggregation so unusually large or unnormalized
# raw weights cannot make a remote body face look hand-influenced.
MIN_FACE_CHAIN_INFLUENCE = 0.80
MIN_VERTEX_CHAIN_INFLUENCE = 0.65

# Bone-local t=0 is the wrist and t=1 is the Hand-bone tail.  The C005 fingers
# continue beyond the tail; t>=1.40 isolates their distal pads/caps for the
# magazine/bolt manipulation semantic.  The small positive lower bound rejects
# the lower-arm cuff and every undersuit sleeve face in the pinned source.
MIN_HAND_T = 0.02
DISTAL_FINGER_START_T = 1.40
MAX_HAND_T = 2.20

# These are strict upper bounds around the pinned C005 hand envelope.  A close
# match to an already-qualified visible armor-hand face can substitute for the
# segment-distance test (for a hidden undersuit skin), but never for influence
# or the anatomical t range.
MAX_AXIS_DISTANCE_M = 0.145
MAX_SEGMENT_DISTANCE_M = 0.235
MAX_MATCHING_ARMOR_DISTANCE_M = 0.012


@dataclass(frozen=True)
class BoneProjection:
    axial_t: float
    axis_distance_m: float
    segment_distance_m: float


@dataclass(frozen=True)
class HandSurfaceDecision:
    zone: str
    reason: str
    face_chain_influence: float
    min_vertex_chain_influence: float
    projection: BoneProjection
    matching_armor_distance_m: float | None


def _point3(value: Sequence[float], label: str) -> tuple[float, float, float]:
    if len(value) != 3:
        raise ValueError(f"{label} must contain exactly three axes")
    result = tuple(float(axis) for axis in value)
    if not all(math.isfinite(axis) for axis in result):
        raise ValueError(f"{label} must contain finite axes")
    return result  # type: ignore[return-value]


def project_to_bone_segment(
    point: Sequence[float],
    head: Sequence[float],
    tail: Sequence[float],
) -> BoneProjection:
    """Measure a point against a bone without clamping away distal anatomy."""

    p = _point3(point, "point")
    a = _point3(head, "head")
    b = _point3(tail, "tail")
    delta = tuple(b[index] - a[index] for index in range(3))
    length_sq = sum(axis * axis for axis in delta)
    if length_sq <= 1.0e-12:
        raise ValueError("hand bone segment must have nonzero length")
    relative = tuple(p[index] - a[index] for index in range(3))
    axial_t = sum(relative[index] * delta[index] for index in range(3)) / length_sq
    axis_point = tuple(a[index] + axial_t * delta[index] for index in range(3))
    axis_distance = math.sqrt(
        sum((p[index] - axis_point[index]) ** 2 for index in range(3))
    )
    clamped_t = min(1.0, max(0.0, axial_t))
    segment_point = tuple(a[index] + clamped_t * delta[index] for index in range(3))
    segment_distance = math.sqrt(
        sum((p[index] - segment_point[index]) ** 2 for index in range(3))
    )
    return BoneProjection(axial_t, axis_distance, segment_distance)


def normalized_chain_influence(
    vertex_weights: Iterable[Mapping[str, float]],
    chain_groups: Iterable[str],
) -> tuple[float, float]:
    """Return mean/min normalized Hand+LowerArm influence across a face."""

    groups = frozenset(chain_groups)
    if not groups:
        raise ValueError("chain_groups must not be empty")
    normalized: list[float] = []
    for weights in vertex_weights:
        values: dict[str, float] = {}
        for name, raw_value in weights.items():
            value = float(raw_value)
            if not math.isfinite(value) or value < 0.0:
                raise ValueError("vertex weights must be finite and non-negative")
            values[str(name)] = value
        total = sum(values.values())
        normalized.append(
            sum(values.get(group, 0.0) for group in groups) / total
            if total > 1.0e-12
            else 0.0
        )
    if not normalized:
        return 0.0, 0.0
    return sum(normalized) / len(normalized), min(normalized)


def classify_hand_surface(
    *,
    point: Sequence[float],
    bone_head: Sequence[float],
    bone_tail: Sequence[float],
    vertex_weights: Iterable[Mapping[str, float]],
    hand_group: str,
    lower_arm_group: str,
    matching_armor_distance_m: float | None = None,
) -> HandSurfaceDecision:
    """Classify one face; uncertain evidence always returns ordinary."""

    projection = project_to_bone_segment(point, bone_head, bone_tail)
    face_influence, minimum_influence = normalized_chain_influence(
        vertex_weights,
        (hand_group, lower_arm_group),
    )
    if matching_armor_distance_m is not None:
        matching_armor_distance_m = float(matching_armor_distance_m)
        if not math.isfinite(matching_armor_distance_m) or matching_armor_distance_m < 0.0:
            raise ValueError("matching armor distance must be finite and non-negative")

    common = {
        "face_chain_influence": face_influence,
        "min_vertex_chain_influence": minimum_influence,
        "projection": projection,
        "matching_armor_distance_m": matching_armor_distance_m,
    }
    if (
        face_influence < MIN_FACE_CHAIN_INFLUENCE
        or minimum_influence < MIN_VERTEX_CHAIN_INFLUENCE
    ):
        return HandSurfaceDecision(ZONE_ORDINARY, "insufficient_chain_influence", **common)
    if not MIN_HAND_T <= projection.axial_t <= MAX_HAND_T:
        return HandSurfaceDecision(ZONE_ORDINARY, "outside_hand_axial_range", **common)

    near_segment = (
        projection.axis_distance_m <= MAX_AXIS_DISTANCE_M
        and projection.segment_distance_m <= MAX_SEGMENT_DISTANCE_M
    )
    matches_armor = (
        matching_armor_distance_m is not None
        and matching_armor_distance_m <= MAX_MATCHING_ARMOR_DISTANCE_M
    )
    if not (near_segment or matches_armor):
        return HandSurfaceDecision(ZONE_ORDINARY, "outside_hand_surface_envelope", **common)

    if projection.axial_t >= DISTAL_FINGER_START_T:
        return HandSurfaceDecision(
            ZONE_MANIPULATION,
            "distal_finger_surface",
            **common,
        )
    return HandSurfaceDecision(ZONE_GRIP, "palm_or_proximal_finger_surface", **common)
