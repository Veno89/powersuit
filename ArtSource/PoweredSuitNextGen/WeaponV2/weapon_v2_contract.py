"""Pure-Python contract helpers for the isolated Candidate006 weapon lane.

The Blender adapter intentionally lives in a separate module.  Keeping profile
validation, immutable hashes, exact rig/action contracts, deterministic JSON,
and fail-closed report semantics here makes the promotion rules testable from a
normal Python interpreter.
"""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence


SCHEMA_VERSION = 1
VALID_LODS = (0, 1, 2, 3)
VALID_RENDER_ROLES = ("rifle", "optic")
REQUIRED_PBR_MAPS = ("base_color", "normal", "mrao", "emission")
PROJECTION_EVIDENCE_SCHEMA_VERSION = 3
OCULAR_RENDER_NAME = "nextgen_precision_rifle_scope_ocular.png"
REQUIRED_EVIDENCE = (
    "immutable_inputs",
    "source_immutability",
    "rig_and_actions",
    "weapon_contract",
    "rigid_geometry",
    "weapon_skin_motion",
    "topology_and_uv",
    "pbr_materials",
    "lod_and_render_budget",
    "sight_and_ocular",
    "clearance_semantics",
    "review_renders",
)


class ContractError(ValueError):
    """Raised when a WeaponV2 profile or handoff violates its contract."""


def canonical_json_bytes(value: Any) -> bytes:
    """Return deterministic, human-readable UTF-8 JSON bytes."""

    return (
        json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    ).encode("utf-8")


def canonical_compact_json_bytes(value: Any) -> bytes:
    """Return deterministic compact JSON bytes for embedded manifest hashes."""

    return json.dumps(
        value, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")


def write_canonical_json(path: Path, value: Any) -> str:
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


def sha256_manifest(value: Any) -> str:
    return hashlib.sha256(canonical_compact_json_bytes(value)).hexdigest()


def _require_mapping(value: Any, name: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        raise ContractError(f"{name} must be a JSON object.")
    return value


def _require_exact_keys(
    value: Mapping[str, Any], expected: Iterable[str], name: str
) -> None:
    expected_set = set(expected)
    actual_set = set(value)
    missing = sorted(expected_set - actual_set)
    extra = sorted(actual_set - expected_set)
    if missing or extra:
        raise ContractError(
            f"{name} keys differ; missing={missing}, unexpected={extra}."
        )


def load_profile(path: Path) -> dict[str, Any]:
    """Load and deeply validate the Candidate006 production profile."""

    with path.open("r", encoding="utf-8") as handle:
        profile = json.load(handle)
    if profile.get("schema_version") != SCHEMA_VERSION:
        raise ContractError(
            f"Unsupported profile schema {profile.get('schema_version')!r}; "
            f"expected {SCHEMA_VERSION}."
        )

    required_sections = (
        "asset",
        "immutable_inputs",
        "rig",
        "weapon",
        "selection",
        "topology",
        "uv",
        "pbr",
        "lods",
        "runtime_budget",
        "skin_motion",
        "sighting",
        "clearance",
        "renders",
        "report",
    )
    missing = [name for name in required_sections if name not in profile]
    if missing:
        raise ContractError(f"Profile is missing sections: {', '.join(missing)}")

    immutable_inputs = _require_mapping(
        profile["immutable_inputs"], "immutable_inputs"
    )
    if not immutable_inputs:
        raise ContractError("immutable_inputs cannot be empty.")
    for name, entry_value in immutable_inputs.items():
        entry = _require_mapping(entry_value, f"immutable_inputs.{name}")
        if not isinstance(entry.get("path"), str) or not entry["path"]:
            raise ContractError(f"immutable_inputs.{name}.path is required.")
        digest = entry.get("sha256")
        if not isinstance(digest, str) or len(digest) != 64:
            raise ContractError(
                f"immutable_inputs.{name}.sha256 must be a 64-character digest."
            )

    rig = _require_mapping(profile["rig"], "rig")
    bone_names = rig.get("bone_names")
    if not isinstance(bone_names, list) or len(bone_names) != 23:
        raise ContractError("rig.bone_names must contain exactly 23 names.")
    if len(set(bone_names)) != 23:
        raise ContractError("rig.bone_names contains duplicates.")
    control_bones = rig.get("weapon_control_bones")
    if control_bones != ["WeaponRoot", "WeaponMagazine", "WeaponBolt"]:
        raise ContractError("rig.weapon_control_bones is not the Generator114 contract.")
    if rig.get("weapon_control_deform_required") is not True:
        raise ContractError(
            "rig.weapon_control_deform_required must be true for Candidate006 render adapters."
        )
    action_ranges = _require_mapping(rig.get("action_ranges"), "rig.action_ranges")
    if len(action_ranges) != 24:
        raise ContractError("rig.action_ranges must contain exactly 24 actions.")
    for action, frame_range in action_ranges.items():
        if not action.startswith("PS_"):
            raise ContractError(f"Unexpected non-PS action {action!r}.")
        if (
            not isinstance(frame_range, list)
            or len(frame_range) != 2
            or not all(isinstance(frame, int) for frame in frame_range)
            or frame_range[0] > frame_range[1]
        ):
            raise ContractError(f"Invalid frame range for {action!r}.")
    if rig.get("action_slot_count") != 1 or rig.get("action_slot_id_type") != "OBJECT":
        raise ContractError("Each action must own exactly one OBJECT Action Slot.")

    skin_motion = _require_mapping(profile["skin_motion"], "skin_motion")
    expected_motion_samples = [
        {"action": "PS_Aim", "frame": 1},
        {"action": "PS_WeaponStowed_Idle", "frame": 1},
        {"action": "PS_Reload", "frame": 50},
        {"action": "PS_BoltCycle", "frame": 12},
    ]
    if skin_motion.get("required_samples") != expected_motion_samples:
        raise ContractError(
            "skin_motion.required_samples must pin Aim1, Stowed1, Reload50 and Bolt12."
        )
    for sample in expected_motion_samples:
        action = sample["action"]
        frame = sample["frame"]
        frame_range = action_ranges.get(action)
        if frame_range is None or not frame_range[0] <= frame <= frame_range[1]:
            raise ContractError(f"Skin-motion sample {action}@{frame} is outside its action.")
    for field in (
        "manual_skin_tolerance_m",
        "return_matrix_tolerance",
        "root_ready_to_stowed_min_m",
        "magazine_travel_min_m",
        "bolt_travel_min_m",
    ):
        value = skin_motion.get(field)
        if not isinstance(value, (int, float)) or not math.isfinite(float(value)) or value <= 0:
            raise ContractError(f"skin_motion.{field} must be finite and positive.")

    weapon = _require_mapping(profile["weapon"], "weapon")
    if weapon.get("forward_axis") != "+Y" or weapon.get("up_axis") != "+Z":
        raise ContractError("Weapon axes must remain +Y forward and +Z up.")
    helpers = weapon.get("required_helper_roles")
    if not isinstance(helpers, list) or len(helpers) != len(set(helpers)):
        raise ContractError("weapon.required_helper_roles must be a unique list.")
    required_helper_set = {
        "primary_grip",
        "support_grip",
        "stock_contact",
        "sight_ocular",
        "muzzle",
    }
    if set(helpers) != required_helper_set:
        raise ContractError("weapon.required_helper_roles is incomplete.")
    envelopes = _require_mapping(
        weapon.get("hardpoint_envelopes_m"), "weapon.hardpoint_envelopes_m"
    )
    _require_exact_keys(envelopes, required_helper_set, "hardpoint envelopes")
    for role, axes_value in envelopes.items():
        axes = _require_mapping(axes_value, f"hardpoint envelope {role}")
        _require_exact_keys(axes, ("x", "y", "z"), f"hardpoint envelope {role}")
        for axis, bounds in axes.items():
            if (
                not isinstance(bounds, list)
                or len(bounds) != 2
                or not all(isinstance(value, (int, float)) for value in bounds)
                or not all(math.isfinite(float(value)) for value in bounds)
                or bounds[0] > bounds[1]
            ):
                raise ContractError(f"Invalid {role}.{axis} hardpoint envelope.")

    selection = _require_mapping(profile["selection"], "selection")
    if selection.get("required_roles") != ["rifle"]:
        raise ContractError("selection.required_roles must be exactly ['rifle'].")
    if selection.get("optional_roles") != ["optic"]:
        raise ContractError("selection.optional_roles must be exactly ['optic'].")

    lods = _require_mapping(profile["lods"], "lods")
    budgets = _require_mapping(lods.get("rifle_triangle_budgets"), "lods budgets")
    _require_exact_keys(budgets, (f"LOD{lod}" for lod in VALID_LODS), "LOD budgets")
    previous_target_max: int | None = None
    for lod in VALID_LODS:
        key = f"LOD{lod}"
        budget = _require_mapping(budgets[key], f"lods.{key}")
        for field in ("target_min", "target_max", "hard_max"):
            if not isinstance(budget.get(field), int) or budget[field] <= 0:
                raise ContractError(f"{key}.{field} must be a positive integer.")
        if budget["target_min"] > budget["target_max"]:
            raise ContractError(f"{key} target_min exceeds target_max.")
        if budget["hard_max"] < budget["target_max"]:
            raise ContractError(f"{key} hard_max is below target_max.")
        if previous_target_max is not None and budget["target_max"] >= previous_target_max:
            raise ContractError("Rifle LOD triangle targets must decrease monotonically.")
        previous_target_max = budget["target_max"]

    runtime = _require_mapping(profile["runtime_budget"], "runtime_budget")
    combined = _require_mapping(
        runtime.get("combined_triangle_hard_max"),
        "runtime_budget.combined_triangle_hard_max",
    )
    _require_exact_keys(combined, (f"LOD{lod}" for lod in VALID_LODS), "combined budgets")
    for key, value in combined.items():
        if not isinstance(value, int) or value <= 0:
            raise ContractError(f"Invalid combined triangle budget for {key}.")

    pbr = _require_mapping(profile["pbr"], "pbr")
    if pbr.get("texture_resolution") != [2048, 2048]:
        raise ContractError("Candidate006 requires a 2048x2048 weapon texture set.")
    if pbr.get("required_maps") != list(REQUIRED_PBR_MAPS):
        raise ContractError("pbr.required_maps must use the canonical map order.")

    renders = _require_mapping(profile["renders"], "renders")
    filenames = renders.get("required_filenames")
    if not isinstance(filenames, list) or len(filenames) != 13:
        raise ContractError("renders.required_filenames must contain exactly 13 views.")
    if len(set(filenames)) != 13 or any(not name.endswith(".png") for name in filenames):
        raise ContractError("Render filenames must be 13 unique PNG names.")

    report = _require_mapping(profile["report"], "report")
    if report.get("required_evidence") != list(REQUIRED_EVIDENCE):
        raise ContractError("report.required_evidence must match the fail-closed contract.")
    return profile


def evaluate_triangle_budget(actual: int, budget: Mapping[str, int]) -> dict[str, Any]:
    if actual > budget["hard_max"]:
        result, severity = "ABOVE_HARD_MAX", "error"
    elif actual < budget["target_min"]:
        result, severity = "BELOW_TARGET", "warning"
    elif actual > budget["target_max"]:
        result, severity = "ABOVE_TARGET", "warning"
    else:
        result, severity = "IN_TARGET", "pass"
    return {
        "actual": int(actual),
        "target_min": int(budget["target_min"]),
        "target_max": int(budget["target_max"]),
        "hard_max": int(budget["hard_max"]),
        "result": result,
        "severity": severity,
    }


def exact_mapping_difference(
    actual: Mapping[str, Any], expected: Mapping[str, Any]
) -> dict[str, Any]:
    """Describe missing, unexpected, and changed exact contract entries."""

    actual_keys = set(actual)
    expected_keys = set(expected)
    return {
        "missing": sorted(expected_keys - actual_keys),
        "unexpected": sorted(actual_keys - expected_keys),
        "changed": sorted(
            key
            for key in actual_keys & expected_keys
            if actual[key] != expected[key]
        ),
    }


def assert_exact_action_contract(
    actual_ranges: Mapping[str, Sequence[int]],
    slot_counts: Mapping[str, int],
    expected_ranges: Mapping[str, Sequence[int]],
) -> None:
    normalized_actual = {
        name: [int(frame_range[0]), int(frame_range[1])]
        for name, frame_range in actual_ranges.items()
    }
    normalized_expected = {
        name: [int(frame_range[0]), int(frame_range[1])]
        for name, frame_range in expected_ranges.items()
    }
    difference = exact_mapping_difference(normalized_actual, normalized_expected)
    if any(difference.values()):
        raise ContractError(f"Action names/ranges differ: {difference}")
    slot_difference = exact_mapping_difference(
        {name: int(count) for name, count in slot_counts.items()},
        {name: 1 for name in normalized_expected},
    )
    if any(slot_difference.values()):
        raise ContractError(f"Action Slot contract differs: {slot_difference}")


def validate_pbr_manifest(
    manifest: Mapping[str, Any], repository_root: Path, expected_resolution: Sequence[int]
) -> list[str]:
    """Return fail-closed PBR manifest errors without depending on Pillow."""

    errors: list[str] = []
    if manifest.get("schema_version") != 1:
        errors.append("texture manifest schema_version must be 1")
    if list(manifest.get("resolution", ())) != list(expected_resolution):
        errors.append("texture manifest resolution differs from the profile")
    maps = manifest.get("maps")
    if not isinstance(maps, Mapping):
        return [*errors, "texture manifest maps are missing"]
    if set(maps) != set(REQUIRED_PBR_MAPS):
        errors.append("texture manifest map roles are incomplete or unexpected")
    for role in REQUIRED_PBR_MAPS:
        entry = maps.get(role)
        if not isinstance(entry, Mapping):
            errors.append(f"{role} map entry is missing")
            continue
        raw_path = entry.get("path")
        expected_hash = str(entry.get("sha256", "")).lower()
        if not isinstance(raw_path, str) or not raw_path:
            errors.append(f"{role} map path is missing")
            continue
        try:
            path = safe_repository_path(repository_root, raw_path)
        except ContractError as exc:
            errors.append(str(exc))
            continue
        if not path.is_file():
            errors.append(f"{role} map does not exist: {raw_path}")
        elif len(expected_hash) != 64 or sha256_file(path).lower() != expected_hash:
            errors.append(f"{role} map hash mismatch: {raw_path}")
    return errors


def safe_repository_path(repository_root: Path, raw_path: str | Path) -> Path:
    root = repository_root.resolve()
    candidate = Path(raw_path)
    resolved = (candidate if candidate.is_absolute() else root / candidate).resolve()
    try:
        resolved.relative_to(root)
    except ValueError as exc:
        raise ContractError(f"Path escapes the repository: {raw_path}") from exc
    return resolved


def validate_render_set(render_dir: Path, expected_filenames: Sequence[str]) -> list[str]:
    errors: list[str] = []
    if not render_dir.is_dir():
        return [f"Render directory does not exist: {render_dir}"]
    actual = sorted(path.name for path in render_dir.glob("*.png"))
    expected = sorted(expected_filenames)
    if actual != expected:
        missing = sorted(set(expected) - set(actual))
        unexpected = sorted(set(actual) - set(expected))
        errors.append(f"Render set differs; missing={missing}, unexpected={unexpected}")
    for name in expected:
        path = render_dir / name
        if path.is_file() and path.stat().st_size < 4096:
            errors.append(f"Render is too small to be review evidence: {name}")
    hashes: dict[str, list[str]] = {}
    for name in expected:
        path = render_dir / name
        if path.is_file():
            hashes.setdefault(sha256_file(path), []).append(name)
    for digest, names in sorted(hashes.items()):
        if len(names) > 1:
            errors.append(
                "Review renders are byte-identical; "
                f"sha256={digest}, files={sorted(names)}"
            )
    return errors


def _finite_number(value: Any) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    result = float(value)
    return result if math.isfinite(result) else None


def validate_projection_evidence(
    evidence: Any,
    expected_filenames: Sequence[str],
) -> list[str]:
    """Validate source-bound framing evidence for all review renders.

    Twelve views use evaluated weapon bounds and a 5--95 percent safe frame.
    The ocular view deliberately uses a different contract: it proves a
    centered, circular, unobstructed sight picture with a reticle and distant
    target instead of trying to place the whole rifle inside the viewport.
    """

    errors: list[str] = []
    if not isinstance(evidence, Mapping):
        return ["projection_evidence must be an object"]
    if evidence.get("schema_version") != PROJECTION_EVIDENCE_SCHEMA_VERSION:
        errors.append(
            "projection_evidence schema differs; "
            f"expected {PROJECTION_EVIDENCE_SCHEMA_VERSION}"
        )
    views = evidence.get("views")
    if not isinstance(views, Mapping):
        return [*errors, "projection_evidence.views must be an object"]
    resolution = evidence.get("render_resolution")
    if (
        not isinstance(resolution, Sequence)
        or isinstance(resolution, (str, bytes))
        or len(resolution) != 2
        or any(isinstance(value, bool) or not isinstance(value, int) for value in resolution)
        or list(resolution) != [1280, 960]
    ):
        errors.append("projection_evidence.render_resolution must be [1280, 960]")
        render_width, render_height = 1280, 960
    else:
        render_width, render_height = int(resolution[0]), int(resolution[1])
    expected = set(expected_filenames)
    actual = set(views)
    if actual != expected:
        errors.append(
            "projection evidence filenames differ; "
            f"missing={sorted(expected - actual)}, "
            f"unexpected={sorted(actual - expected)}"
        )

    for name in sorted(expected & actual):
        entry = views[name]
        if not isinstance(entry, Mapping):
            errors.append(f"{name} projection evidence must be an object")
            continue
        if name == OCULAR_RENDER_NAME:
            if entry.get("evidence_kind") != "ocular_corridor":
                errors.append(f"{name} must use the ocular_corridor evidence rule")
            numeric_fields = (
                "camera_to_ocular_rear_m",
                "aperture_center_x",
                "aperture_center_y",
                "aperture_radius_x",
                "aperture_radius_y",
                "reticle_center_x",
                "reticle_center_y",
                "target_center_x",
                "target_center_y",
                "target_distance_m",
                "target_viewport_width",
                "target_viewport_height",
                "sight_picture_viewport_width",
                "sight_picture_viewport_height",
                "aperture_proxy_max_distance_m",
            )
            values: dict[str, float] = {}
            for field in numeric_fields:
                value = _finite_number(entry.get(field))
                if value is None:
                    errors.append(f"{name} has invalid {field}")
                else:
                    values[field] = value
            camera_offset = values.get("camera_to_ocular_rear_m")
            if camera_offset is not None and not 0.002 <= camera_offset <= 0.022:
                errors.append(
                    f"{name} camera must be 0.002..0.022 m behind the rear aperture"
                )
            for prefix, tolerance in (
                ("aperture", 0.04),
                ("reticle", 0.03),
                ("target", 0.08),
            ):
                for axis in ("x", "y"):
                    field = f"{prefix}_center_{axis}"
                    value = values.get(field)
                    if value is not None and abs(value - 0.5) > tolerance:
                        errors.append(f"{name} {field} is not centered")
            radius_x = values.get("aperture_radius_x")
            radius_y = values.get("aperture_radius_y")
            for field, radius in (
                ("aperture_radius_x", radius_x),
                ("aperture_radius_y", radius_y),
            ):
                if radius is not None and not 0.15 <= radius <= 0.42:
                    errors.append(f"{name} {field} must remain within 0.15..0.42")
            if radius_x is not None and radius_y is not None:
                # world_to_camera_view returns normalized viewport units.  A
                # true pixel-circle in a non-square 4:3 render therefore has
                # unequal normalized x/y radii; compare in pixel space.
                ratio = (
                    radius_x * render_width / (radius_y * render_height)
                    if radius_y > 0.0
                    else math.inf
                )
                if not 0.88 <= ratio <= 1.12:
                    errors.append(f"{name} aperture is not circular enough")
            target_distance = values.get("target_distance_m")
            if target_distance is not None and target_distance < 5.0:
                errors.append(f"{name} target must be at least 5 m beyond the ocular")
            for field in ("target_viewport_width", "target_viewport_height"):
                value = values.get(field)
                if value is not None and not 0.24 <= value <= 0.84:
                    errors.append(
                        f"{name} {field} must remain readable within 0.24..0.84"
                    )
            for field in (
                "sight_picture_viewport_width",
                "sight_picture_viewport_height",
            ):
                value = values.get(field)
                if value is not None and not 0.22 <= value <= 0.80:
                    errors.append(
                        f"{name} {field} must remain readable within 0.22..0.80"
                    )
            aperture_object = entry.get("aperture_object")
            if aperture_object != "NGPR_OpticOcular":
                errors.append(f"{name} must bind aperture evidence to NGPR_OpticOcular")
            if entry.get("aperture_geometry_source") != "exact_source_proxy_inner_rim":
                errors.append(f"{name} must identify the exact inner-rim proxy source")
            proxy_distance = values.get("aperture_proxy_max_distance_m")
            if proxy_distance is not None and not 0.0 <= proxy_distance <= 1.0e-5:
                errors.append(f"{name} ocular proxy differs from visible LOD0")
            aperture_samples = entry.get("aperture_sample_count")
            if (
                isinstance(aperture_samples, bool)
                or not isinstance(aperture_samples, int)
                or aperture_samples < 32
            ):
                errors.append(f"{name} has insufficient real ocular samples")
            sight_samples = entry.get("sight_picture_sample_count")
            if (
                isinstance(sight_samples, bool)
                or not isinstance(sight_samples, int)
                or sight_samples < 32
            ):
                errors.append(f"{name} has insufficient sight-picture samples")
            for field in (
                "corridor_clear",
                "objective_visible",
                "reticle_visible",
                "target_visible",
            ):
                if entry.get(field) is not True:
                    errors.append(f"{name} requires {field}=true")
            if entry.get("studio_ground_visible") is not False:
                errors.append(f"{name} must hide the studio ground")
            if entry.get("nested_occluder_count") != 0:
                errors.append(f"{name} has nested ocular occluders")
            for field, minimum in (("reticle_line_count", 4), ("range_tick_count", 4)):
                value = entry.get(field)
                if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
                    errors.append(f"{name} requires {field}>={minimum}")
            continue

        if entry.get("evidence_kind") != "weapon_bounds_5_95":
            errors.append(f"{name} must use the weapon_bounds_5_95 evidence rule")
        fields = (
            "viewport_min_x",
            "viewport_max_x",
            "viewport_min_y",
            "viewport_max_y",
            "viewport_width",
            "viewport_height",
        )
        values = {}
        for field in fields:
            value = _finite_number(entry.get(field))
            if value is None:
                errors.append(f"{name} has invalid {field}")
            else:
                values[field] = value
        if len(values) == len(fields):
            min_x = values["viewport_min_x"]
            max_x = values["viewport_max_x"]
            min_y = values["viewport_min_y"]
            max_y = values["viewport_max_y"]
            width = values["viewport_width"]
            height = values["viewport_height"]
            if min_x < 0.05 or max_x > 0.95 or min_y < 0.05 or max_y > 0.95:
                errors.append(f"{name} weapon bounds leave the 5--95 percent safe frame")
            if (
                width <= 0.0
                or height <= 0.0
                or abs((max_x - min_x) - width) > 0.001
                or abs((max_y - min_y) - height) > 0.001
            ):
                errors.append(f"{name} projected width/height is inconsistent")
            if max(width, height) < 0.50:
                errors.append(f"{name} weapon occupancy is below 0.50")
            if name == "nextgen_precision_rifle_neutral_side.png" and width < 0.72:
                errors.append(f"{name} weapon width is below the 0.72 side-view target")
        sample_count = entry.get("weapon_bounds_sample_count")
        if isinstance(sample_count, bool) or not isinstance(sample_count, int) or sample_count < 8:
            errors.append(f"{name} has insufficient evaluated weapon-bound samples")
        if name.startswith("nextgen_precision_rifle_neutral_"):
            if entry.get("studio_ground_visible") is not False:
                errors.append(f"{name} must hide the studio ground")
    return errors


def issue_code_scope_passed(
    issues: Sequence[Mapping[str, Any]],
    *,
    start: int = 0,
    exact: Iterable[str] = (),
    suffixes: Iterable[str] = (),
) -> bool:
    """Return true when a specific issue-code scope has no errors."""

    exact_codes = set(exact)
    code_suffixes = tuple(suffixes)
    return not any(
        issue.get("severity") == "error"
        and (
            str(issue.get("code", "")) in exact_codes
            or str(issue.get("code", "")).endswith(code_suffixes)
        )
        for issue in issues[start:]
    )


def validate_bound_render_manifest(
    manifest: Mapping[str, Any],
    repository_root: Path,
    render_dir: Path,
    expected_filenames: Sequence[str],
    source_sha256: str,
) -> list[str]:
    """Verify review image hashes are bound to the exact audited source blend."""

    errors: list[str] = []
    bound = manifest.get("render_manifest")
    if not isinstance(bound, Mapping):
        return ["builder report has no render_manifest object"]
    top_source_hash = str(manifest.get("candidate_blend_sha256", "")).lower()
    bound_source_hash = str(bound.get("candidate_blend_sha256", "")).lower()
    expected_source_hash = source_sha256.lower()
    if top_source_hash != expected_source_hash:
        errors.append("builder report candidate_blend_sha256 differs from audited source")
    if bound_source_hash != expected_source_hash:
        errors.append("render_manifest candidate_blend_sha256 differs from audited source")
    entries = bound.get("files")
    if not isinstance(entries, list):
        return [*errors, "render_manifest.files must be a list"]
    by_name: dict[str, Mapping[str, Any]] = {}
    for entry in entries:
        if not isinstance(entry, Mapping):
            errors.append("render_manifest.files contains a non-object entry")
            continue
        name = entry.get("filename")
        if not isinstance(name, str) or not name:
            errors.append("render manifest entry has no filename")
        elif name in by_name:
            errors.append(f"render manifest contains duplicate filename {name}")
        else:
            by_name[name] = entry
    if set(by_name) != set(expected_filenames):
        errors.append(
            "bound render filenames differ; "
            f"missing={sorted(set(expected_filenames) - set(by_name))}, "
            f"unexpected={sorted(set(by_name) - set(expected_filenames))}"
        )
    for name in sorted(set(by_name) & set(expected_filenames)):
        entry = by_name[name]
        raw_path = entry.get("path")
        if not isinstance(raw_path, str) or not raw_path:
            errors.append(f"{name} has no bound path")
            continue
        try:
            path = safe_repository_path(repository_root, raw_path)
        except ContractError as exc:
            errors.append(str(exc))
            continue
        if path != (render_dir / name).resolve():
            errors.append(f"{name} bound path does not match its review directory")
        if not path.is_file():
            errors.append(f"{name} bound render is missing")
            continue
        actual_hash = sha256_file(path)
        if str(entry.get("sha256", "")).lower() != actual_hash.lower():
            errors.append(f"{name} bound SHA-256 differs")
        if entry.get("size_bytes") != path.stat().st_size:
            errors.append(f"{name} bound size differs")
    return errors


def finite_vector(values: Sequence[Any], *, length: int = 3) -> bool:
    return len(values) == length and all(math.isfinite(float(value)) for value in values)


def evaluate_hardpoint_envelopes(
    hardpoints: Mapping[str, Sequence[Any]],
    envelopes: Mapping[str, Mapping[str, Sequence[float]]],
) -> list[str]:
    """Return exact finite/envelope violations for canonical local hardpoints."""

    errors: list[str] = []
    if set(hardpoints) != set(envelopes):
        difference = exact_mapping_difference(hardpoints, envelopes)
        errors.append(f"Hardpoint roles differ: {difference}")
    for role in sorted(set(hardpoints) & set(envelopes)):
        values = hardpoints[role]
        if not finite_vector(values):
            errors.append(f"{role} must contain three finite local axes")
            continue
        for index, axis in enumerate(("x", "y", "z")):
            lower, upper = envelopes[role][axis]
            value = float(values[index])
            if value < float(lower) or value > float(upper):
                errors.append(
                    f"{role}.{axis}={value:.6f} is outside [{lower}, {upper}]"
                )
    return errors


def evaluate_skin_motion_metrics(
    metrics: Mapping[str, Any], requirements: Mapping[str, Any]
) -> list[str]:
    """Return fail-closed violations for independently evaluated rigid skin motion."""

    errors: list[str] = []
    required_labels = {
        f"{sample['action']}@{sample['frame']}"
        for sample in requirements["required_samples"]
    }
    samples = metrics.get("samples")
    if not isinstance(samples, Mapping):
        return ["Skin-motion samples are missing or invalid"]
    actual_labels = set(samples)
    if actual_labels != required_labels:
        errors.append(
            "Skin-motion sample labels differ: "
            f"missing={sorted(required_labels - actual_labels)}, "
            f"unexpected={sorted(actual_labels - required_labels)}"
        )
    tolerance = float(requirements["manual_skin_tolerance_m"])
    for label in sorted(required_labels & actual_labels):
        sample = samples[label]
        error = sample.get("maximum_manual_skin_error_m") if isinstance(sample, Mapping) else None
        if not isinstance(error, (int, float)) or not math.isfinite(float(error)):
            errors.append(f"{label} has no finite manual skin error")
        elif float(error) > tolerance:
            errors.append(f"{label} skin error {float(error):.9f} m exceeds {tolerance:.9f} m")

    minimum_fields = (
        ("root_ready_to_stowed_travel_m", "root_ready_to_stowed_min_m"),
        ("magazine_travel_m", "magazine_travel_min_m"),
        ("bolt_travel_m", "bolt_travel_min_m"),
    )
    for metric_name, requirement_name in minimum_fields:
        value = metrics.get(metric_name)
        minimum = float(requirements[requirement_name])
        if not isinstance(value, (int, float)) or not math.isfinite(float(value)):
            errors.append(f"{metric_name} is missing or non-finite")
        elif float(value) < minimum:
            errors.append(f"{metric_name}={float(value):.9f} is below {minimum:.9f}")

    return_tolerance = float(requirements["return_matrix_tolerance"])
    for metric_name in (
        "root_transition_return_matrix_error",
        "magazine_return_matrix_error",
        "bolt_return_matrix_error",
    ):
        value = metrics.get(metric_name)
        if not isinstance(value, (int, float)) or not math.isfinite(float(value)):
            errors.append(f"{metric_name} is missing or non-finite")
        elif float(value) > return_tolerance:
            errors.append(
                f"{metric_name}={float(value):.9f} exceeds {return_tolerance:.9f}"
            )
    return errors


def summarise_issues(issues: Iterable[Mapping[str, Any]]) -> dict[str, int]:
    counts = {"error": 0, "warning": 0, "pass": 0}
    for issue in issues:
        severity = str(issue.get("severity", "error"))
        if severity not in counts:
            severity = "error"
        counts[severity] += 1
    return counts


def finalise_report(
    report: dict[str, Any], required_evidence: Sequence[str] = REQUIRED_EVIDENCE
) -> dict[str, Any]:
    """Set status from explicit evidence and issues, failing closed on omissions."""

    evidence = report.get("evidence")
    issues = report.setdefault("issues", [])
    if not isinstance(evidence, Mapping):
        evidence = {}
        report["evidence"] = evidence
    for name in required_evidence:
        if evidence.get(name) is not True:
            issues.append(
                {
                    "code": f"EVIDENCE_{name.upper()}_MISSING",
                    "severity": "error",
                    "message": f"Required evidence {name!r} is absent or false.",
                }
            )
    summary = summarise_issues(issues)
    report["summary"] = summary
    report["status"] = "PASS" if summary["error"] == 0 else "FAIL"
    # This lane is a production-structure gate only.  Even its PASS cannot
    # authorize promotion: the separate 923-frame/324-transition visible
    # clearance sweeps and explicit owner visual approval remain mandatory.
    report["promotion_authorized"] = False
    report["structural_gate_passed"] = report["status"] == "PASS"
    report["promotion_blockers_remaining"] = (
        []
        if report["status"] != "PASS"
        else [
            "canonical visible all-frame clearance sweep",
            "dense transition clearance sweep",
            "owner visual approval",
            "separate Unity integration approval",
        ]
    )
    return report


def missing_source_report(
    source_path: str, required_evidence: Sequence[str] = REQUIRED_EVIDENCE
) -> dict[str, Any]:
    """Build the canonical fail-closed result for a source that does not exist."""

    return finalise_report(
        {
            "schema_version": SCHEMA_VERSION,
            "source": {"path": source_path, "immutable": False},
            "issues": [
                {
                    "code": "SOURCE_MISSING",
                    "severity": "error",
                    "message": "Candidate006 blend does not exist.",
                }
            ],
            "evidence": {name: False for name in required_evidence},
            "promotion_authorized": False,
        },
        required_evidence,
    )
