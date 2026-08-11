"""Pure-Python contract helpers for the isolated HeroV2 production lane.

This module deliberately has no Blender dependency.  It keeps profile parsing,
budget evaluation, report canonicalisation, and path safety testable from a
normal Python interpreter while the Blender adapter lives beside it.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any, Iterable


SCHEMA_VERSION = 1
VALID_ROLES = ("suit", "rifle", "optic")
VALID_LODS = (0, 1, 2, 3)


class ContractError(ValueError):
    """Raised when a production profile or handoff violates the contract."""


def load_profile(path: Path) -> dict[str, Any]:
    """Load and minimally validate a HeroV2 production profile."""

    with path.open("r", encoding="utf-8") as handle:
        profile = json.load(handle)

    if profile.get("schema_version") != SCHEMA_VERSION:
        raise ContractError(
            f"Unsupported profile schema {profile.get('schema_version')!r}; "
            f"expected {SCHEMA_VERSION}."
        )

    required_sections = (
        "selection",
        "topology",
        "uv",
        "materials",
        "lods",
    )
    missing = [name for name in required_sections if name not in profile]
    if missing:
        raise ContractError(f"Profile is missing sections: {', '.join(missing)}")

    lods = profile["lods"]
    for role in ("suit", "rifle"):
        if role not in lods.get("triangle_budgets", {}):
            raise ContractError(f"Missing triangle budgets for role {role!r}.")
        for lod in VALID_LODS:
            key = f"LOD{lod}"
            budget = lods["triangle_budgets"][role].get(key)
            if not budget or budget["target_min"] > budget["target_max"]:
                raise ContractError(f"Invalid {role} {key} triangle budget.")
            if budget["hard_max"] < budget["target_max"]:
                raise ContractError(f"{role} {key} hard max is below target max.")

    for lod in (1, 2, 3):
        key = f"LOD{lod}"
        ratio = float(lods["generation_ratios"][key])
        if not 0.0 < ratio < 1.0:
            raise ContractError(f"{key} generation ratio must be between 0 and 1.")

    for lod in VALID_LODS:
        key = f"LOD{lod}"
        combined_max = lods.get("combined_triangle_hard_max", {}).get(key)
        if not isinstance(combined_max, int) or combined_max <= 0:
            raise ContractError(f"Missing or invalid combined triangle hard max for {key}.")

    return profile


def infer_role(name: str, explicit_role: str | None = None) -> str:
    """Return a stable asset role from explicit metadata or conservative naming."""

    if explicit_role:
        role = explicit_role.strip().lower()
        if role not in VALID_ROLES:
            raise ContractError(f"Unsupported HeroV2 asset role {explicit_role!r}.")
        return role

    lowered = name.casefold()
    if any(token in lowered for token in ("rifle", "weapon", "magazine", "bolt")):
        return "rifle"
    if any(token in lowered for token in ("optic", "scope", "sight", "glass")):
        return "optic"
    return "suit"


def triangle_budget(
    profile: dict[str, Any], role: str, lod: int
) -> dict[str, int] | None:
    """Return the configured triangle budget, if the role owns geometry."""

    if role == "optic":
        return None
    return profile["lods"]["triangle_budgets"][role][f"LOD{lod}"]


def evaluate_triangle_budget(
    actual: int, budget: dict[str, int]
) -> dict[str, Any]:
    """Classify a triangle total without conflating targets and hard failures."""

    if actual > budget["hard_max"]:
        severity = "error"
        result = "ABOVE_HARD_MAX"
    elif actual < budget["target_min"]:
        severity = "warning"
        result = "BELOW_TARGET"
    elif actual > budget["target_max"]:
        severity = "warning"
        result = "ABOVE_TARGET"
    else:
        severity = "pass"
        result = "IN_TARGET"

    return {
        "actual": int(actual),
        "target_min": int(budget["target_min"]),
        "target_max": int(budget["target_max"]),
        "hard_max": int(budget["hard_max"]),
        "result": result,
        "severity": severity,
    }


def canonical_json_bytes(value: Any) -> bytes:
    """Return stable UTF-8 JSON bytes used for report fingerprints."""

    return (
        json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    ).encode("utf-8")


def write_canonical_json(path: Path, value: Any) -> str:
    """Write stable JSON and return its SHA-256 fingerprint."""

    payload = canonical_json_bytes(value)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def assert_derivative_path(source: Path, output: Path) -> None:
    """Refuse a source overwrite or a derivative outside the HeroV2 lane."""

    source_resolved = source.resolve()
    output_resolved = output.resolve()
    if source_resolved == output_resolved:
        raise ContractError("HeroV2 output must not overwrite its source blend.")

    lane_root = Path(__file__).resolve().parent
    try:
        output_resolved.relative_to(lane_root)
    except ValueError as exc:
        raise ContractError(
            f"HeroV2 derivatives must stay beneath {lane_root}; got {output_resolved}."
        ) from exc


def summarise_issues(issues: Iterable[dict[str, Any]]) -> dict[str, int]:
    counts = {"error": 0, "warning": 0, "pass": 0}
    for issue in issues:
        severity = issue["severity"]
        counts[severity] = counts.get(severity, 0) + 1
    return counts
