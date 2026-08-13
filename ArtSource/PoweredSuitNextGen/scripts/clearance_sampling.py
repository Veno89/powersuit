"""Pure sampling contract for the Blender weapon-clearance audit.

The Blender adapter imports these helpers, while ordinary unit tests prove the
action-filter and inclusive subframe-count rules without opening Blender.
"""
from __future__ import annotations

import math
from decimal import Decimal, InvalidOperation, ROUND_FLOOR
from typing import Iterable, Sequence


MAX_DENSE_FRAME_STEP = 1.0
MAX_SAMPLES_PER_ACTION = 100_000


class SamplingError(ValueError):
    """Raised when a requested clearance sample set is invalid or unsafe."""


def validate_frame_step(value: object) -> float:
    """Return a finite dense step in ``(0, 1]`` or fail closed.

    Steps above one frame are deliberately rejected: ``--frame-step`` is the
    dense/subframe lane, while the existing ``--all-frames`` switch owns the
    canonical integer-frame sweep.
    """

    if isinstance(value, bool):
        raise SamplingError("frame step must be a finite number, not a boolean")
    try:
        result = float(value)
    except (TypeError, ValueError) as exc:
        raise SamplingError("frame step must be a finite number") from exc
    if not math.isfinite(result) or result <= 0.0:
        raise SamplingError("frame step must be finite and greater than zero")
    if result > MAX_DENSE_FRAME_STEP:
        raise SamplingError(
            f"frame step {result:g} is outside the dense range (0, "
            f"{MAX_DENSE_FRAME_STEP:g}]"
        )
    return result


def select_action_names(
    available_names: Sequence[str], requested_names: Sequence[str] | None
) -> list[str]:
    """Select exact, unique actions in canonical available-name order."""

    available = list(available_names)
    if not available or len(set(available)) != len(available):
        raise SamplingError("available action names must be non-empty and unique")
    if not requested_names:
        return available
    requested = list(requested_names)
    if any(not isinstance(name, str) or not name for name in requested):
        raise SamplingError("action filters must be non-empty exact action names")
    duplicates = sorted({name for name in requested if requested.count(name) > 1})
    if duplicates:
        raise SamplingError(f"duplicate action filters: {', '.join(duplicates)}")
    unknown = sorted(set(requested) - set(available))
    if unknown:
        raise SamplingError(f"unknown action filters: {', '.join(unknown)}")
    requested_set = set(requested)
    selected = [name for name in available if name in requested_set]
    if not selected:
        raise SamplingError("action filters selected no actions")
    return selected


def sampling_mode(*, all_frames: bool, frame_step: object | None) -> str:
    """Resolve compatible legacy/integer/dense sampling modes."""

    if all_frames and frame_step is not None:
        raise SamplingError("--all-frames and --frame-step are mutually exclusive")
    if frame_step is not None:
        validate_frame_step(frame_step)
        return "uniform_dense_frames"
    return "all_integer_frames" if all_frames else "authored_keyframes"


def inclusive_frame_samples(start: object, end: object, step: object) -> list[float]:
    """Return deterministic ``start..end`` samples including both endpoints."""

    step_value = validate_frame_step(step)
    try:
        start_decimal = Decimal(str(start))
        end_decimal = Decimal(str(end))
        step_decimal = Decimal(str(step_value))
    except (InvalidOperation, ValueError) as exc:
        raise SamplingError("action range must contain finite numeric endpoints") from exc
    if not start_decimal.is_finite() or not end_decimal.is_finite():
        raise SamplingError("action range must contain finite numeric endpoints")
    if start_decimal > end_decimal:
        raise SamplingError(
            f"action range is out of order: {start_decimal} > {end_decimal}"
        )
    span = end_decimal - start_decimal
    interval_count = int((span / step_decimal).to_integral_value(rounding=ROUND_FLOOR))
    sample_count_without_tail = interval_count + 1
    if sample_count_without_tail > MAX_SAMPLES_PER_ACTION:
        raise SamplingError(
            f"frame step would exceed {MAX_SAMPLES_PER_ACTION} samples for one action"
        )
    values = [start_decimal + step_decimal * index for index in range(sample_count_without_tail)]
    if not values or values[-1] != end_decimal:
        values.append(end_decimal)
    if len(values) > MAX_SAMPLES_PER_ACTION:
        raise SamplingError(
            f"frame step would exceed {MAX_SAMPLES_PER_ACTION} samples for one action"
        )
    if any(value < start_decimal or value > end_decimal for value in values):
        raise SamplingError("generated sample escaped the authored action range")
    return [float(value) for value in values]


def total_dense_sample_count(
    action_ranges: Iterable[tuple[object, object]], step: object
) -> int:
    """Return the combined inclusive sample count for pure contract tests."""

    return sum(len(inclusive_frame_samples(start, end, step)) for start, end in action_ranges)
