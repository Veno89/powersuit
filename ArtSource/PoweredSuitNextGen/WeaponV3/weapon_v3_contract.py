"""Pure-Python contract helpers for the isolated Candidate007 weapon lane.

The Blender adapter intentionally lives in a separate module.  Keeping profile
validation, immutable hashes, exact rig/action contracts, deterministic JSON,
and fail-closed report semantics here makes the promotion rules testable from a
normal Python interpreter.
"""

from __future__ import annotations

import hashlib
import hmac
import json
import math
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence


SCHEMA_VERSION = 1
VALID_LODS = (0, 1, 2, 3)
VALID_RENDER_ROLES = ("rifle", "optic")
REQUIRED_PBR_MAPS = ("base_color", "normal", "mrao", "emission")
PROJECTION_EVIDENCE_SCHEMA_VERSION = 4
OCULAR_RENDER_NAME = "nextgen_precision_rifle_scope_ocular.png"
DRAW_RENDER_NAME = "nextgen_precision_rifle_pose_draw.png"
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
    "stow_authoring",
    "manipulation_authoring",
    "component_architecture",
    "authored_clearance",
    "all_frame_clearance",
    "dense_transition_clearance",
    "review_renders",
)

EXPECTED_ASSET_ID = "NextGen Precision Rifle 002"
EXPECTED_WEAPON_ID = "PS_NextGenPrecisionRifle002"
EXPECTED_SELECTION_PREFIX = "WeaponV3_LOD"
EXPECTED_ROLE_PROPERTY = "weapon_v3_role"
EXPECTED_LOD_PROPERTY = "weapon_v3_lod"
EXPECTED_OBJECT_PREFIX = "NGPR002_"
EXPECTED_REAUTHOR_VERSION = "CANDIDATE007_WEAPON_ACTIONS_V11"
EXPECTED_ACTION_SIGNATURE_SCHEMA = "CANDIDATE007_ACTION_SEMANTICS_V10"
EXPECTED_MANIPULATION = {
    "manipulation_solver_version": "CANDIDATE007_MANIPULATION_SOLVER_V3",
    "hand_contact_pad_center_local": {
        "L": [0.0005016, 0.2179851, 0.0639991],
        "R": [0.0005006, 0.2178152, 0.0640004],
    },
    "hand_contact_solve_tolerance_m": 0.000005,
    "reload_contact_mode": "seated_v2__detached_distal_pad_positive_x_face",
    "reload_seated_frames": [14, 25, 75],
    "reload_detached_frames": [36, 50, 64],
    "reload_hand_outward_m": 0.09,
    "reload_hand_to_mag_outward_delta_m": 0.04,
    "reload_palm_roll_deg": 25.0,
    "reload_pull_lug_object_name": "NGPR_MagazinePullLug_L",
    "reload_shared_target_outward_m": 0.035,
    "reload_magazine_outward_m": 0.05,
    "reload_magazine_half_width_m": 0.03,
    "reload_contact_inset_m": 0.001,
    "reload_detached_twist_deg": 60.0,
    "bolt_contact_mode": "tagged_knob_min_x_face_distal_pad",
    "bolt_contact_frames": [4, 8, 12, 16],
    "bolt_shared_target_outward_m": 0.035,
    "bolt_contact_inset_m": 0.001,
    "bolt_knob_object_name": "NGPR_BoltKnob",
    "bolt_palm_roll_deg": 30.0,
    "bolt_hand_outward_m": 0.04,
    "reload_path_mode": "identity_endpoints__outward_before_detached_delta",
    "bolt_target_mode": "tagged_knob_min_x_face_distal_pad",
    "bolt_target_classifier_mode": "exact_root_local_shared_bolt_call_corridor",
    "bolt_target_corridor_root_local_m": {
        "relative_to": "tagged_bolt_center",
        "x_offset_m": -0.035,
        "y_min_m": -0.095,
        "y_max_m": 0.0,
        "z_offset_m": 0.0,
        "axis_tolerance_m": 0.000001,
    },
    "bolt_measured_release_path_version": "CANDIDATE007_BOLT_RELEASE_PATH_V2",
    "bolt_measured_pose_substitutions": {
        "2.375": 3.0,
        "2.5": 3.0,
        "17.5": 17.0,
        "17.625": 17.0,
    },
    "bolt_measured_release_deltas_root_local_m": {
        "1.25": [0.000000008, 0.000053250, -0.002375834],
        "1.5": [0.000000015, 0.000106500, -0.004751668],
        "1.75": [0.000000008, 0.000053250, -0.002375834],
        "18.75": [-0.001610478, 0.000063438, -0.001026265],
        "19.0": [-0.003220956, 0.000126878, -0.002052529],
        "19.25": [0.000000064, 0.000091366, -0.004085246],
        "19.5": [-0.000000026, 0.000057798, -0.002586497],
        "19.75": [-0.000000013, 0.000028899, -0.001293249],
    },
    "bolt_measured_eighth_frame_clearances_m": {
        "3.875": 0.025,
        "6.125": 0.035,
        "6.875": 0.035,
        "13.875": 0.035,
        "16.125": 0.025,
    },
    "reload_measured_return_path_version": "CANDIDATE007_RELOAD_RETURN_PATH_V1",
    "reload_measured_return_blend_endpoint_frames": [79.0, 82.0],
    "reload_measured_return_anchor_frames": [79.75, 80.0],
    "reload_measured_return_deltas_root_local_m": {
        "79.875": [0.002, 0.0, 0.0],
    },
}
EXPECTED_MANIPULATION_DENSIFICATION = {
    "version": "CANDIDATE007_MANIPULATION_DENSIFICATION_V5",
    "actions": {
        "PS_Reload": {
            "sample_step_frames": 0.25,
            "contact_window": [25.0, 75.0],
            "approach_frames": [14.0, 16.0, 18.75, 20.0, 24.0, 25.0],
            "return_frames": [75.0, 79.0, 82.0, 84.0],
            "hover_mode": "grip_release__outboard_transit__face_normal_ramp",
            "hover_clearance_m": 0.025,
            "transit_clearance_m": 0.1,
            "grip_release_m": 0.06,
            "authored_frames": [1.0, 14.0, 25.0, 36.0, 50.0, 64.0, 75.0, 84.0],
            "co_solved_sample_count": 201,
            "yoke_clearance_m": 0.005,
            "measured_return_path_version": "CANDIDATE007_RELOAD_RETURN_PATH_V1",
            "measured_return_blend_endpoint_frames": [79.0, 82.0],
            "measured_return_anchor_frames": [79.75, 80.0],
            "measured_return_deltas_root_local_m": {
                "79.875": [0.002, 0.0, 0.0],
            },
            "interpolation": "LINEAR",
            "interpolation_counts": {
                "total_curve_count": 212,
                "affected_curve_count": 50,
                "affected_key_count": 10650,
            },
            "expected_result_frame_count": 213,
        },
        "PS_BoltCycle": {
            "sample_step_frames": 0.25,
            "contact_window": [4.0, 16.0],
            "approach_frames": [1.0, 1.75, 2.5, 3.0, 4.0],
            "return_frames": [16.0, 17.0, 17.5, 18.5, 20.0],
            "hover_mode": "grip_release__outboard_transit__face_normal_ramp",
            "hover_clearance_m": 0.025,
            "transit_clearance_m": 0.1,
            "grip_release_m": 0.072,
            "authored_frames": [1.0, 4.0, 8.0, 12.0, 16.0, 20.0],
            "co_solved_sample_count": 52,
            "approach_transit_clearance_m": 0.115,
            "transit_contact_clearance_m": 0.035,
            "measured_release_path_version": "CANDIDATE007_BOLT_RELEASE_PATH_V2",
            "measured_pose_substitutions": {
                "2.375": 3.0,
                "2.5": 3.0,
                "17.5": 17.0,
                "17.625": 17.0,
            },
            "measured_release_deltas_root_local_m": {
                "1.25": [0.000000008, 0.000053250, -0.002375834],
                "1.5": [0.000000015, 0.000106500, -0.004751668],
                "1.75": [0.000000008, 0.000053250, -0.002375834],
                "18.75": [-0.001610478, 0.000063438, -0.001026265],
                "19.0": [-0.003220956, 0.000126878, -0.002052529],
                "19.25": [0.000000064, 0.000091366, -0.004085246],
                "19.5": [-0.000000026, 0.000057798, -0.002586497],
                "19.75": [-0.000000013, 0.000028899, -0.001293249],
            },
            "measured_eighth_frame_clearances_m": {
                "3.875": 0.025,
                "6.125": 0.035,
                "6.875": 0.035,
                "13.875": 0.035,
                "16.125": 0.025,
            },
            "interpolation": "LINEAR",
            "interpolation_counts": {
                "total_curve_count": 212,
                "affected_curve_count": 50,
                "affected_key_count": 3800,
            },
            "expected_result_frame_count": 76,
        },
    },
}
EXPECTED_STOW = {
    "rearward_delta_m": 0.33,
    "outward_delta_m": 0.04,
    "transition_pose_mode": (
        "powered_back_mount_guided__measured_pregrasp__"
        "hand_r_owned_ready_dock_symmetric"
    ),
    "draw_extraction_back_clearance_m": 0.08,
    "draw_extraction_lateral_m": 0.04,
}
EXPECTED_TRANSITION_DRAW_FRAMES = [
    1.0, 6.0, 10.0, 16.0, 18.0, 20.0, 22.0, 24.0, 26.0, 27.0,
    28.0, 28.125, 28.25, 28.375, 28.5, 28.625, 28.75, 28.875,
    29.0, 29.125, 29.25, 29.375, 29.5, 29.625, 29.75, 29.875, 30.0,
]
EXPECTED_TRANSITION_SHEATHE_FRAMES = sorted(
    31.0 - frame for frame in EXPECTED_TRANSITION_DRAW_FRAMES
)
EXPECTED_TRANSITION = {
    "path_version": "CANDIDATE007_GUIDED_DEPLOY_LATE_CATCH_V3",
    "sample_step_frames": 0.125,
    "certification_step_frames": 0.125,
    "reversal_sample_count": 233,
    "ownership_bone": "Hand.R",
    "draw": {
        "key_frames": EXPECTED_TRANSITION_DRAW_FRAMES,
        "construction": (
            "powered_back_mount_guided__measured_early_acquisition__"
            "hand_r_no_slip_ready_dock"
        ),
        "deployment_mode": "powered_back_mount_guided",
        "guided_through_frame": 26.0,
        "early_acquisition_frame": 27.0,
        "early_acquisition_target_frame": 28.0,
        "early_acquisition_clearance_m": 0.012,
        "primary_contact_window": [26.75, 30.0],
        "ownership_start_frame": 28.0,
        "ownership_dense_end_frame": 29.875,
        "ownership_sample_step_frames": 0.125,
        "ownership_mode": (
            "full_ready_root_relative_hand_frame__cached_v9_root_restored"
        ),
        "support_contact_window": [29.0, 30.0],
        "interpolation": "LINEAR",
        "interpolation_counts": {
            "total_curve_count": 230,
            "affected_curve_count": 230,
            "affected_key_count": 6210,
        },
    },
    "sheathe": {
        "key_frames": EXPECTED_TRANSITION_SHEATHE_FRAMES,
        "construction": "exact_time_reverse_of_draw",
        "deployment_mode": "powered_back_mount_guided_exact_reverse",
        "guided_from_frame": 5.0,
        "early_acquisition_frame": 4.0,
        "early_acquisition_target_frame": 3.0,
        "early_acquisition_clearance_m": 0.012,
        "primary_contact_window": [1.0, 4.25],
        "ownership_end_frame": 3.0,
        "ownership_dense_start_frame": 1.125,
        "ownership_sample_step_frames": 0.125,
        "ownership_mode": (
            "exact_reverse_full_ready_root_relative_hand_frame__"
            "cached_v9_root_restored"
        ),
        "support_contact_window": [1.0, 2.0],
        "interpolation": "LINEAR",
        "interpolation_counts": {
            "total_curve_count": 230,
            "affected_curve_count": 230,
            "affected_key_count": 6210,
        },
    },
}
EXPECTED_CONTACT_WINDOW_POLICY_VERSION = (
    "PS_CLEARANCE_CONTACT_WINDOWS_CANDIDATE007_V3"
)
EXPECTED_TRANSITION_CONTACT_WINDOWS = {
    "primary_grip": [
        {"action": "PS_Weapon_Draw", "start": 26.75, "end": 30.0},
        {"action": "PS_Weapon_Sheathe", "start": 1.0, "end": 4.25},
    ],
    "support_grip": [
        {"action": "PS_Weapon_Draw", "start": 29.0, "end": 30.0},
        {"action": "PS_Weapon_Sheathe", "start": 1.0, "end": 2.0},
    ],
}
EXPECTED_DENSE_SAMPLE_COUNTS = {
    "PS_BoltCycle": 153,
    "PS_Reload": 665,
    "PS_Weapon_Draw": 233,
    "PS_Weapon_Sheathe": 233,
}
REQUIRED_CLEARANCE_KINDS = (
    "authored_clearance",
    "all_frame_clearance",
    "dense_transition_clearance",
)
REPORT_EVIDENCE_SHA256_FIELD = "report_evidence_sha256"


class ContractError(ValueError):
    """Raised when a WeaponV3 profile or handoff violates its contract."""


def canonical_json_bytes(value: Any) -> bytes:
    """Return deterministic, human-readable UTF-8 JSON bytes."""

    return (
        json.dumps(
            value,
            indent=2,
            sort_keys=True,
            ensure_ascii=False,
            allow_nan=False,
        )
        + "\n"
    ).encode("utf-8")


def canonical_compact_json_bytes(value: Any) -> bytes:
    """Return the shared strict canonical byte representation for evidence."""

    return json.dumps(
        value,
        ensure_ascii=True,
        allow_nan=False,
        sort_keys=True,
        separators=(",", ":"),
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


def sha256_canonical_json_file(path: Path) -> str:
    """Hash JSON semantics, independent of key order, whitespace, and EOLs."""

    with path.open("r", encoding="utf-8") as handle:
        document = json.load(handle)
    return sha256_manifest(document)


def sha256_immutable_input(path: Path, hash_mode: str) -> str:
    """Hash an immutable input according to its explicit profile mode."""

    if hash_mode == "canonical_json":
        return sha256_canonical_json_file(path)
    if hash_mode == "raw_binary":
        return sha256_file(path)
    raise ContractError(f"Unsupported immutable input hash_mode {hash_mode!r}.")


def sha256_manifest(value: Any) -> str:
    return hashlib.sha256(canonical_compact_json_bytes(value)).hexdigest()


def report_evidence_sha256(report: Mapping[str, Any]) -> str:
    """Hash report semantics after removing its top-level self-hash field."""

    if not isinstance(report, Mapping):
        raise ContractError("Report evidence must be a JSON object.")
    unsigned = dict(report)
    unsigned.pop(REPORT_EVIDENCE_SHA256_FIELD, None)
    return sha256_manifest(unsigned)


def validate_report_evidence_sha256(
    report: Any, *, label: str = "report"
) -> list[str]:
    """Validate a report self-hash, returning fail-closed contract errors."""

    if not isinstance(report, Mapping):
        return [f"{label} report is missing or invalid"]
    supplied = report.get(REPORT_EVIDENCE_SHA256_FIELD)
    if (
        not isinstance(supplied, str)
        or len(supplied) != 64
        or any(character not in "0123456789abcdefABCDEF" for character in supplied)
    ):
        return [f"{label} report_evidence_sha256 is missing or invalid"]
    try:
        expected = report_evidence_sha256(report)
    except (TypeError, ValueError) as exc:
        return [f"{label} report is not strict canonical JSON: {exc}"]
    if not hmac.compare_digest(supplied.lower(), expected):
        return [f"{label} report_evidence_sha256 does not match report semantics"]
    return []


def seal_production_report(report: dict[str, Any]) -> dict[str, Any]:
    """Seal a fully finalised production report with its semantic self-hash."""

    required_final_fields = {
        "status",
        "summary",
        "promotion_authorized",
        "structural_gate_passed",
        "promotion_blockers_remaining",
    }
    missing = sorted(required_final_fields - set(report))
    if missing:
        raise ContractError(
            "Production report must be finalised before sealing; "
            f"missing={missing}."
        )
    report.pop(REPORT_EVIDENCE_SHA256_FIELD, None)
    report[REPORT_EVIDENCE_SHA256_FIELD] = report_evidence_sha256(report)
    return report


def validate_production_report_seal(report: Any) -> list[str]:
    """Validate the semantic self-hash on a written production report."""

    return validate_report_evidence_sha256(report, label="production")


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
    """Load and deeply validate the Candidate007 production profile."""

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
        "animation_authoring",
    )
    missing = [name for name in required_sections if name not in profile]
    if missing:
        raise ContractError(f"Profile is missing sections: {', '.join(missing)}")

    asset = _require_mapping(profile["asset"], "asset")
    if (
        asset.get("candidate_id") != EXPECTED_ASSET_ID
        or asset.get("candidate_number") != 7
        or asset.get("source_filename")
        != "nextgen_precision_rifle_candidate_v007.blend"
        or asset.get("manifest_filename")
        != "nextgen_precision_rifle_candidate_v007.json"
    ):
        raise ContractError("asset must identify Candidate007 / NextGen Precision Rifle 002.")

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
        hash_mode = entry.get("hash_mode")
        suffix = Path(entry["path"]).suffix.lower()
        if hash_mode == "canonical_json":
            if suffix != ".json":
                raise ContractError(
                    f"immutable_inputs.{name} canonical_json mode requires a .json path."
                )
        elif hash_mode == "raw_binary":
            if suffix not in {".blend", ".png"}:
                raise ContractError(
                    f"immutable_inputs.{name} raw_binary mode is restricted to binary/image inputs."
                )
        else:
            raise ContractError(
                f"immutable_inputs.{name}.hash_mode must be canonical_json or raw_binary."
            )
    if "candidate006_blend" in immutable_inputs:
        raise ContractError(
            "Candidate006 is rollback-comparison evidence, not a Candidate007 build input; "
            "its ignored blend must not be a mandatory immutable input."
        )
    candidate005_blend = immutable_inputs.get("candidate005_blend")
    if not isinstance(candidate005_blend, Mapping) or (
        candidate005_blend.get("path")
        != "ArtSource/PoweredSuitNextGen/candidates/aegis_vanguard_candidate_v005.blend"
        or candidate005_blend.get("hash_mode") != "raw_binary"
    ):
        raise ContractError(
            "Candidate007 must declare the pinned Candidate005 blend as its build source."
        )
    for evidence_name in (
        "candidate006_manifest",
        "candidate006_production_report",
    ):
        evidence = immutable_inputs.get(evidence_name)
        if not isinstance(evidence, Mapping) or evidence.get("hash_mode") != "canonical_json":
            raise ContractError(
                f"immutable_inputs.{evidence_name} must retain canonical predecessor evidence."
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
            "rig.weapon_control_deform_required must be true for Candidate007 render adapters."
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
    if weapon.get("weapon_id") != EXPECTED_WEAPON_ID:
        raise ContractError(f"weapon.weapon_id must be {EXPECTED_WEAPON_ID!r}.")
    if weapon.get("rigid_signature_version") != 6:
        raise ContractError(
            "weapon.rigid_signature_version must remain on the shared rigid-manifest schema 6."
        )
    if weapon.get("hardpoint_version") != "NGPR002_HARDPOINTS_V2":
        raise ContractError("weapon.hardpoint_version must identify the measured V2 fit.")
    if weapon.get("forward_axis") != "+Y" or weapon.get("up_axis") != "+Z":
        raise ContractError("Weapon axes must remain +Y forward and +Z up.")
    helpers = weapon.get("required_helper_roles")
    if not isinstance(helpers, list) or len(helpers) != len(set(helpers)):
        raise ContractError("weapon.required_helper_roles must be a unique list.")
    required_helper_set = {
        "primary_grip",
        "support_grip",
        "support_grip_min",
        "support_grip_max",
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
    if (
        selection.get("collection_prefix") != EXPECTED_SELECTION_PREFIX
        or selection.get("role_property") != EXPECTED_ROLE_PROPERTY
        or selection.get("lod_property") != EXPECTED_LOD_PROPERTY
        or selection.get("object_prefix") != EXPECTED_OBJECT_PREFIX
    ):
        raise ContractError("selection must use the isolated WeaponV3 identifiers.")
    if selection.get("required_roles") != ["rifle"]:
        raise ContractError("selection.required_roles must be exactly ['rifle'].")
    if selection.get("optional_roles") != ["optic"]:
        raise ContractError("selection.optional_roles must be exactly ['optic'].")

    architecture = _require_mapping(
        weapon.get("component_architecture"), "weapon.component_architecture"
    )
    fixed = _require_mapping(architecture.get("fixed"), "component_architecture.fixed")
    articulated = _require_mapping(
        architecture.get("articulated"), "component_architecture.articulated"
    )
    if fixed.get("control_bone") != "WeaponRoot":
        raise ContractError("Fixed weapon components must use WeaponRoot.")
    fixed_roles = fixed.get("roles")
    if (
        not isinstance(fixed_roles, list)
        or not fixed_roles
        or len(set(fixed_roles)) != len(fixed_roles)
        or any(role in fixed_roles for role in ("magazine", "bolt"))
    ):
        raise ContractError("Fixed component roles must be unique and exclude magazine/bolt.")
    if articulated != {"magazine": "WeaponMagazine", "bolt": "WeaponBolt"}:
        raise ContractError("Magazine and bolt must remain isolated on their articulated controls.")
    if architecture.get("require_role_isolation") is not True:
        raise ContractError("component_architecture.require_role_isolation must be true.")

    authoring = _require_mapping(profile["animation_authoring"], "animation_authoring")
    if authoring.get("reauthor_version") != EXPECTED_REAUTHOR_VERSION:
        raise ContractError("animation_authoring.reauthor_version differs from Candidate007.")
    if authoring.get("action_signature_schema") != EXPECTED_ACTION_SIGNATURE_SCHEMA:
        raise ContractError("animation_authoring.action_signature_schema differs from Candidate007.")
    manipulation = _require_mapping(
        authoring.get("manipulation"), "animation_authoring.manipulation"
    )
    if canonical_compact_json_bytes(manipulation) != canonical_compact_json_bytes(
        EXPECTED_MANIPULATION
    ):
        raise ContractError(
            "animation_authoring.manipulation differs from the measured "
            "Candidate007 pad-contact solver V3 contract."
        )
    densification = _require_mapping(
        authoring.get("manipulation_densification"),
        "animation_authoring.manipulation_densification",
    )
    if canonical_compact_json_bytes(densification) != canonical_compact_json_bytes(
        EXPECTED_MANIPULATION_DENSIFICATION
    ):
        raise ContractError(
            "animation_authoring.manipulation_densification differs from the "
            "measured Candidate007 V5 quarter/eighth-frame evidence contract."
        )
    stow = _require_mapping(authoring.get("stow"), "animation_authoring.stow")
    for field, expected in EXPECTED_STOW.items():
        if stow.get(field) != expected:
            raise ContractError(f"animation_authoring.stow.{field} must be {expected!r}.")
    for field in (
        "endpoint_max_matrix_error",
        "subframe_reversal_max_matrix_error",
    ):
        tolerance = stow.get(field)
        if (
            isinstance(tolerance, bool)
            or not isinstance(tolerance, (int, float))
            or not math.isfinite(float(tolerance))
            or float(tolerance) <= 0.0
        ):
            raise ContractError(f"animation_authoring.stow.{field} must be finite and positive.")
    transition = _require_mapping(
        authoring.get("transition"), "animation_authoring.transition"
    )
    if canonical_compact_json_bytes(transition) != canonical_compact_json_bytes(
        EXPECTED_TRANSITION
    ):
        raise ContractError(
            "animation_authoring.transition differs from the measured Candidate007 "
            "guided-deploy V3 ownership contract."
        )

    clearance = _require_mapping(profile["clearance"], "clearance")
    for field in (
        "authored_report_path",
        "all_frame_report_path",
        "dense_transition_report_path",
    ):
        if not isinstance(clearance.get(field), str) or not clearance[field]:
            raise ContractError(f"clearance.{field} is required.")
    if (
        clearance.get("required_status") != "PASS"
        or clearance.get("required_geometry_source") != "visible"
        or clearance.get("required_forbidden_instances") != 0
        or clearance.get("required_full_action_count") != 24
        or clearance.get("required_all_frame_sample_count") != 923
        or clearance.get("dense_transition_frame_step") != 0.125
        or clearance.get("dense_transition_sample_count") != 1284
    ):
        raise ContractError("clearance must require zero-contact visible PASS reports.")
    if clearance.get("policy_version") != "PS_CLEARANCE_FACE_POLICY_V1":
        raise ContractError("clearance.policy_version must remain on face policy V1.")
    if (
        clearance.get("contact_window_policy_version")
        != EXPECTED_CONTACT_WINDOW_POLICY_VERSION
    ):
        raise ContractError(
            "clearance.contact_window_policy_version must identify Candidate007."
        )
    expected_transition_actions = [
        "PS_BoltCycle",
        "PS_Reload",
        "PS_Weapon_Draw",
        "PS_Weapon_Sheathe",
    ]
    if clearance.get("dense_transition_actions") != expected_transition_actions:
        raise ContractError("clearance.dense_transition_actions differs from the required set.")
    if clearance.get("dense_transition_action_sample_counts") != EXPECTED_DENSE_SAMPLE_COUNTS:
        raise ContractError(
            "clearance.dense_transition_action_sample_counts differs from the exact "
            "inclusive 0.125-frame contract."
        )
    if (
        clearance.get("transition_contact_windows")
        != EXPECTED_TRANSITION_CONTACT_WINDOWS
    ):
        raise ContractError(
            "clearance.transition_contact_windows differs from Candidate007 V3."
        )
    expected_all_frame_count = sum(
        int(frame_range[1]) - int(frame_range[0]) + 1
        for frame_range in action_ranges.values()
    )
    if expected_all_frame_count != clearance["required_all_frame_sample_count"]:
        raise ContractError("clearance.required_all_frame_sample_count is inconsistent.")
    expected_dense_counts = {
        name: int(round((action_ranges[name][1] - action_ranges[name][0]) / 0.125)) + 1
        for name in expected_transition_actions
    }
    if expected_dense_counts != clearance["dense_transition_action_sample_counts"]:
        raise ContractError("Dense action sample counts do not cover inclusive endpoints.")
    if sum(expected_dense_counts.values()) != clearance["dense_transition_sample_count"]:
        raise ContractError("Dense total sample count is inconsistent.")

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
        raise ContractError("Candidate007 requires a 2048x2048 weapon texture set.")
    if pbr.get("required_maps") != list(REQUIRED_PBR_MAPS):
        raise ContractError("pbr.required_maps must use the canonical map order.")
    reuse = _require_mapping(pbr.get("reuse"), "pbr.reuse")
    if reuse.get("source_asset_id") != "PS_NextGenPrecisionRifle001":
        raise ContractError("pbr.reuse.source_asset_id must identify Candidate006.")
    if (
        reuse.get("reuse_policy")
        != "hash_pinned_candidate006_preview_maps_not_final_bake"
    ):
        raise ContractError("pbr.reuse.reuse_policy differs from Candidate007.")
    source_manifest_name = reuse.get("source_manifest_immutable_input")
    source_manifest_entry = immutable_inputs.get(source_manifest_name)
    if (
        source_manifest_name != "candidate006_texture_manifest"
        or not isinstance(source_manifest_entry, Mapping)
        or source_manifest_entry.get("hash_mode") != "canonical_json"
        or source_manifest_entry.get("path")
        != "ArtSource/PoweredSuitNextGen/textures/candidate006/manifest.json"
    ):
        raise ContractError(
            "pbr.reuse must bind the canonical Candidate006 texture manifest input."
        )
    map_inputs = _require_mapping(
        reuse.get("map_immutable_inputs"), "pbr.reuse.map_immutable_inputs"
    )
    _require_exact_keys(
        map_inputs, REQUIRED_PBR_MAPS, "pbr.reuse.map_immutable_inputs"
    )
    if len(set(map_inputs.values())) != len(REQUIRED_PBR_MAPS):
        raise ContractError("Each reused PBR map requires a distinct immutable input.")
    for role, input_name in map_inputs.items():
        entry = immutable_inputs.get(input_name)
        if (
            not isinstance(input_name, str)
            or not isinstance(entry, Mapping)
            or entry.get("hash_mode") != "raw_binary"
            or Path(str(entry.get("path", ""))).suffix.lower() != ".png"
        ):
            raise ContractError(
                f"pbr.reuse map {role!r} must reference a raw-binary PNG input."
            )

    renders = _require_mapping(profile["renders"], "renders")
    if renders.get("directory_name") != "nextgen_precision_rifle_candidate_v007":
        raise ContractError("renders.directory_name must identify Candidate007.")
    filenames = renders.get("required_filenames")
    if not isinstance(filenames, list) or len(filenames) != 13:
        raise ContractError("renders.required_filenames must contain exactly 13 views.")
    if len(set(filenames)) != 13 or any(not name.endswith(".png") for name in filenames):
        raise ContractError("Render filenames must be 13 unique PNG names.")

    report = _require_mapping(profile["report"], "report")
    if report.get("default_filename") != "candidate007_production.json":
        raise ContractError("report.default_filename must identify Candidate007.")
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
    Draw additionally proves meaningful sampled LOD0 suit context.  The ocular
    view deliberately uses a different contract: it proves a centered,
    circular, unobstructed sight picture with a reticle and distant target
    instead of trying to place the whole rifle inside the viewport.
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
        if name == DRAW_RENDER_NAME:
            if entry.get("context_evidence_kind") != "suit_lod0_samples_inside_2_98":
                errors.append(
                    f"{name} must use the suit_lod0_samples_inside_2_98 "
                    "context evidence rule"
                )
            context_fields = (
                "context_viewport_min_x",
                "context_viewport_max_x",
                "context_viewport_min_y",
                "context_viewport_max_y",
                "context_viewport_width",
                "context_viewport_height",
            )
            context_values: dict[str, float] = {}
            for field in context_fields:
                value = _finite_number(entry.get(field))
                if value is None:
                    errors.append(f"{name} has invalid {field}")
                else:
                    context_values[field] = value
            if len(context_values) == len(context_fields):
                context_min_x = context_values["context_viewport_min_x"]
                context_max_x = context_values["context_viewport_max_x"]
                context_min_y = context_values["context_viewport_min_y"]
                context_max_y = context_values["context_viewport_max_y"]
                context_width = context_values["context_viewport_width"]
                context_height = context_values["context_viewport_height"]
                if (
                    context_min_x < 0.02
                    or context_max_x > 0.98
                    or context_min_y < 0.02
                    or context_max_y > 0.98
                ):
                    errors.append(f"{name} suit context leaves the 2--98 percent frame")
                if (
                    context_width <= 0.0
                    or context_height <= 0.0
                    or abs((context_max_x - context_min_x) - context_width) > 0.001
                    or abs((context_max_y - context_min_y) - context_height) > 0.001
                ):
                    errors.append(f"{name} context projected width/height is inconsistent")
                if context_width < 0.20:
                    errors.append(f"{name} context viewport width is below 0.20")
                if context_height < 0.08:
                    errors.append(f"{name} context viewport height is below 0.08")
            visible_samples = entry.get("context_visible_sample_count")
            if (
                isinstance(visible_samples, bool)
                or not isinstance(visible_samples, int)
                or visible_samples < 24
            ):
                errors.append(f"{name} requires at least 24 visible context samples")
            projected_samples = entry.get("context_projected_sample_count")
            if (
                isinstance(projected_samples, bool)
                or not isinstance(projected_samples, int)
                or projected_samples < 24
                or (
                    isinstance(visible_samples, int)
                    and not isinstance(visible_samples, bool)
                    and projected_samples < visible_samples
                )
            ):
                errors.append(f"{name} has invalid projected context sample count")
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
    existing_missing_codes = {
        str(issue.get("code", ""))
        for issue in issues
        if isinstance(issue, Mapping)
    }
    for name in required_evidence:
        if evidence.get(name) is not True:
            code = f"EVIDENCE_{name.upper()}_MISSING"
            if code not in existing_missing_codes:
                issues.append({
                    "code": code,
                    "severity": "error",
                    "message": f"Required evidence {name!r} is absent or false.",
                })
    summary = summarise_issues(issues)
    report["summary"] = summary
    report["status"] = "PASS" if summary["error"] == 0 else "FAIL"
    # This lane never authorizes promotion by structural inference.  It may
    # report every machine gate as green, but owner review and an explicit
    # Unity-integration approval remain separate human decisions.
    report["promotion_authorized"] = False
    report["structural_gate_passed"] = report["status"] == "PASS"
    blockers: list[str] = []
    if evidence.get("authored_clearance") is not True:
        blockers.append("authored visible clearance sweep")
    if evidence.get("all_frame_clearance") is not True:
        blockers.append("canonical visible all-frame clearance sweep")
    if evidence.get("dense_transition_clearance") is not True:
        blockers.append("dense transition clearance sweep")
    blockers.extend(["owner visual approval", "separate Unity integration approval"])
    report["promotion_blockers_remaining"] = blockers
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
                    "message": "Candidate007 blend does not exist.",
                }
            ],
            "evidence": {name: False for name in required_evidence},
            "promotion_authorized": False,
        },
        required_evidence,
    )


def validate_stow_evidence(
    evidence: Any,
    requirements: Mapping[str, Any],
) -> list[str]:
    """Validate the source-bound Candidate007 stow and transition proof."""

    if not isinstance(evidence, Mapping):
        return ["actions.reauthor_evidence is missing or invalid"]
    errors: list[str] = []
    if evidence.get("reauthor_version") != EXPECTED_REAUTHOR_VERSION:
        errors.append("reauthor_version differs from Candidate007")
    if evidence.get("action_signature_schema") != EXPECTED_ACTION_SIGNATURE_SCHEMA:
        errors.append("action_signature_schema differs from Candidate007")
    stow = requirements["stow"]
    fields = {
        "stow_rearward_delta_m": stow["rearward_delta_m"],
        "stow_outward_delta_m": stow["outward_delta_m"],
        "draw_extraction_back_clearance_m": stow["draw_extraction_back_clearance_m"],
        "draw_extraction_lateral_m": stow["draw_extraction_lateral_m"],
    }
    for field, expected in fields.items():
        actual = evidence.get(field)
        if (
            isinstance(actual, bool)
            or not isinstance(actual, (int, float))
            or not math.isfinite(float(actual))
            or abs(float(actual) - float(expected)) > 1.0e-9
        ):
            errors.append(f"{field} differs: actual={actual!r}, expected={expected!r}")
    if evidence.get("transition_pose_mode") != stow["transition_pose_mode"]:
        errors.append("transition_pose_mode differs")
    transition = evidence.get("transition_evidence")
    if not isinstance(transition, Mapping):
        errors.append("transition_evidence is missing")
    else:
        limits = (
            ("endpoint_max_matrix_error", stow["endpoint_max_matrix_error"]),
            (
                "subframe_reversal_max_matrix_error",
                stow["subframe_reversal_max_matrix_error"],
            ),
        )
        for field, maximum in limits:
            actual = transition.get(field)
            if (
                isinstance(actual, bool)
                or not isinstance(actual, (int, float))
                or not math.isfinite(float(actual))
                or float(actual) > float(maximum)
            ):
                errors.append(f"transition {field} exceeds {maximum}")
        transition_requirements = requirements["transition"]
        for field, expected in (
            (
                "reversal_certification_step_frames",
                transition_requirements["certification_step_frames"],
            ),
            ("reversal_sample_count", transition_requirements["reversal_sample_count"]),
        ):
            if transition.get(field) != expected:
                errors.append(
                    f"transition {field} differs: actual={transition.get(field)!r}, "
                    f"expected={expected!r}"
                )

    transition_requirements = requirements["transition"]
    if evidence.get("transition_path_version") != transition_requirements["path_version"]:
        errors.append("transition_path_version differs")
    actual_paths = evidence.get("transition_path_evidence")
    expected_paths: dict[str, Any] = {}
    for action_name, leg_name in (
        ("PS_Weapon_Draw", "draw"),
        ("PS_Weapon_Sheathe", "sheathe"),
    ):
        leg = transition_requirements[leg_name]
        expected_paths[action_name] = {
            "version": transition_requirements["path_version"],
            "sample_step_frames": transition_requirements["sample_step_frames"],
            "certification_step_frames": transition_requirements[
                "certification_step_frames"
            ],
            "key_frames": leg["key_frames"],
            "result_frames": leg["key_frames"],
            **{key: value for key, value in leg.items() if key != "key_frames"},
            "ownership_bone": transition_requirements["ownership_bone"],
        }
    try:
        paths_match = canonical_compact_json_bytes(
            actual_paths
        ) == canonical_compact_json_bytes(expected_paths)
    except (TypeError, ValueError):
        paths_match = False
    if not paths_match:
        errors.append("transition_path_evidence differs from guided-deploy V3")
    return errors


def validate_manipulation_evidence(
    evidence: Any,
    requirements: Mapping[str, Any],
    densification_requirements: Mapping[str, Any],
) -> list[str]:
    """Validate the source-bound Candidate007 semantic-pad solver proof.

    Manipulation authoring is deliberately separate from stow authoring: a
    source can retain perfect draw/sheath reversal while silently regressing to
    the old wrist/component-centre targets.  Every field in the profile is an
    exact emitted evidence key so missing modes, frames, pad centres, or
    contact-face measurements fail closed.
    """

    if not isinstance(evidence, Mapping):
        return ["actions.reauthor_evidence is missing or invalid"]
    errors: list[str] = []
    if evidence.get("reauthor_version") != EXPECTED_REAUTHOR_VERSION:
        errors.append("reauthor_version differs from Candidate007 V11")
    if evidence.get("action_signature_schema") != EXPECTED_ACTION_SIGNATURE_SCHEMA:
        errors.append("action_signature_schema differs from Candidate007 schema V10")
    for field, expected in requirements.items():
        if field not in evidence:
            errors.append(f"{field} is missing")
            continue
        actual = evidence[field]
        if field == "reload_hand_to_mag_outward_delta_m":
            matches = (
                not isinstance(actual, bool)
                and isinstance(actual, (int, float))
                and math.isfinite(float(actual))
                and abs(float(actual) - float(expected)) <= 1.0e-12
            )
        else:
            try:
                matches = canonical_compact_json_bytes(
                    actual
                ) == canonical_compact_json_bytes(expected)
            except (TypeError, ValueError):
                matches = False
        if not matches:
            errors.append(
                f"{field} differs: actual={actual!r}, expected={expected!r}"
            )
    expected_version = densification_requirements.get("version")
    if evidence.get("manipulation_densification_version") != expected_version:
        errors.append("manipulation_densification_version differs")
    actual_actions = evidence.get("manipulation_densification_evidence")
    expected_actions = densification_requirements.get("actions")
    if not isinstance(actual_actions, Mapping):
        errors.append("manipulation_densification_evidence is missing")
        return errors
    if not isinstance(expected_actions, Mapping):
        errors.append("manipulation densification requirements are invalid")
        return errors
    if set(actual_actions) != set(expected_actions):
        errors.append("manipulation densification action set differs")
    for action_name, expected_value in expected_actions.items():
        actual_value = actual_actions.get(action_name)
        if not isinstance(actual_value, Mapping):
            errors.append(f"{action_name} densification evidence is missing")
            continue
        expected_entry = dict(expected_value)
        expected_count = expected_entry.pop("expected_result_frame_count")
        expected_frames = _expected_manipulation_result_frames(action_name)
        if len(expected_frames) != expected_count:
            raise ContractError(
                f"Internal {action_name} manipulation frame-count contract drifted."
            )
        expected_entry["result_frames"] = expected_frames
        try:
            matches = canonical_compact_json_bytes(
                actual_value
            ) == canonical_compact_json_bytes(expected_entry)
        except (TypeError, ValueError):
            matches = False
        if not matches:
            errors.append(f"{action_name} densification evidence differs")
    return errors


def _inclusive_frames(start: float, end: float, step: float) -> list[float]:
    """Return one exact, inclusive frame sequence for contract comparisons."""

    first_tick = int(round(start / step))
    last_tick = int(round(end / step))
    if (
        first_tick > last_tick
        or abs(first_tick * step - start) > 1.0e-9
        or abs(last_tick * step - end) > 1.0e-9
    ):
        raise ContractError(f"Invalid inclusive frame contract {start}..{end}@{step}.")
    return [tick * step for tick in range(first_tick, last_tick + 1)]


def _expected_manipulation_result_frames(action_name: str) -> list[float]:
    if action_name == "PS_Reload":
        return sorted(
            {
                1.0,
                14.0,
                16.0,
                18.75,
                20.0,
                24.0,
                79.0,
                79.75,
                79.875,
                80.0,
                82.0,
                84.0,
                *_inclusive_frames(25.0, 75.0, 0.25),
            }
        )
    if action_name == "PS_BoltCycle":
        return sorted(
            {
                1.0,
                1.75,
                2.5,
                2.375,
                3.0,
                3.875,
                6.125,
                6.875,
                13.875,
                16.125,
                17.0,
                17.5,
                17.625,
                18.5,
                20.0,
                *_inclusive_frames(1.25, 2.25, 0.25),
                *_inclusive_frames(4.0, 16.0, 0.25),
                *_inclusive_frames(17.75, 19.75, 0.25),
            }
        )
    raise ContractError(f"Unknown manipulation action {action_name!r}.")


def validate_reused_pbr_provenance(
    wrapper: Any,
    source_manifest: Any,
    reuse: Mapping[str, Any],
    immutable_inputs: Mapping[str, Any],
) -> list[str]:
    """Bind Candidate007's preview-map wrapper to the exact pinned C006 set."""

    if not isinstance(wrapper, Mapping):
        return ["Candidate007 texture wrapper is missing or invalid"]
    if not isinstance(source_manifest, Mapping):
        return ["Candidate006 source texture manifest is missing or invalid"]
    errors: list[str] = []
    manifest_input_name = reuse.get("source_manifest_immutable_input")
    manifest_input = immutable_inputs.get(manifest_input_name)
    if not isinstance(manifest_input, Mapping):
        return ["Pinned Candidate006 source texture manifest input is missing"]
    expected_manifest_path = manifest_input.get("path")
    expected_manifest_hash = str(manifest_input.get("sha256", "")).lower()
    actual_manifest_hash = sha256_manifest(source_manifest).lower()
    if actual_manifest_hash != expected_manifest_hash:
        errors.append("Candidate006 source texture manifest canonical hash differs")
    if (
        str(wrapper.get("source_texture_manifest_canonical_sha256", "")).lower()
        != expected_manifest_hash
    ):
        errors.append("Candidate007 wrapper source texture manifest hash differs")
    if wrapper.get("source_manifest_path") != expected_manifest_path:
        errors.append("Candidate007 wrapper source texture manifest path differs")
    expected_source_asset = reuse.get("source_asset_id")
    if source_manifest.get("asset_id") != expected_source_asset:
        errors.append("Candidate006 source texture asset_id differs")
    if wrapper.get("source_asset_id") != expected_source_asset:
        errors.append("Candidate007 wrapper source_asset_id differs")
    if wrapper.get("reuse_policy") != reuse.get("reuse_policy"):
        errors.append("Candidate007 wrapper reuse_policy differs")
    if wrapper.get("resolution") != source_manifest.get("resolution"):
        errors.append("Candidate007 wrapper resolution differs from its source manifest")

    map_inputs = reuse.get("map_immutable_inputs")
    source_maps = source_manifest.get("maps")
    wrapper_maps = wrapper.get("maps")
    if not isinstance(map_inputs, Mapping):
        return [*errors, "Pinned Candidate006 map input mapping is missing"]
    if not isinstance(source_maps, Mapping):
        return [*errors, "Candidate006 source texture maps are missing"]
    if not isinstance(wrapper_maps, Mapping):
        return [*errors, "Candidate007 wrapper texture maps are missing"]
    expected_roles = set(REQUIRED_PBR_MAPS)
    if set(source_maps) != expected_roles:
        errors.append("Candidate006 source texture map roles differ")
    if set(wrapper_maps) != expected_roles:
        errors.append("Candidate007 wrapper texture map roles differ")
    for role in REQUIRED_PBR_MAPS:
        input_name = map_inputs.get(role)
        pinned = immutable_inputs.get(input_name)
        source_entry = source_maps.get(role)
        wrapper_entry = wrapper_maps.get(role)
        if not isinstance(pinned, Mapping):
            errors.append(f"Pinned {role} texture input is missing")
            continue
        expected_identity = {
            "path": pinned.get("path"),
            "sha256": str(pinned.get("sha256", "")).lower(),
        }
        for label, entry in (
            ("Candidate006 source", source_entry),
            ("Candidate007 wrapper", wrapper_entry),
        ):
            if not isinstance(entry, Mapping):
                errors.append(f"{label} {role} map entry is missing")
                continue
            identity = {
                "path": entry.get("path"),
                "sha256": str(entry.get("sha256", "")).lower(),
            }
            if identity != expected_identity:
                errors.append(f"{label} {role} map identity differs from its pin")
    return errors


def validate_component_architecture_evidence(
    evidence: Any,
    architecture: Mapping[str, Any],
) -> list[str]:
    """Prove fixed structure and articulated magazine/bolt never share controls."""

    if not isinstance(evidence, Mapping):
        return ["component architecture evidence is missing or invalid"]
    errors: list[str] = []
    fixed_control = architecture["fixed"]["control_bone"]
    fixed_roles = set(architecture["fixed"]["roles"])
    articulated = dict(architecture["articulated"])
    assignments = evidence.get("role_control_assignments")
    if not isinstance(assignments, Mapping):
        return ["role_control_assignments is missing"]
    normalized_assignments: dict[str, list[str]] = {}
    for role, controls in assignments.items():
        if (
            isinstance(controls, Sequence)
            and not isinstance(controls, (str, bytes))
            and all(isinstance(control, str) for control in controls)
        ):
            normalized_assignments[str(role)] = sorted(set(controls))
        else:
            errors.append(f"role {role!r} has invalid control assignments")
    for role in sorted(fixed_roles):
        controls = normalized_assignments.get(role)
        if controls != [fixed_control]:
            errors.append(f"fixed role {role!r} must use only {fixed_control}")
    for role, expected_control in sorted(articulated.items()):
        controls = normalized_assignments.get(role)
        if controls != [expected_control]:
            errors.append(f"articulated role {role!r} must use only {expected_control}")
    unexpected = sorted(set(normalized_assignments) - fixed_roles - set(articulated))
    if unexpected:
        errors.append(f"unexpected component roles: {unexpected}")
    return errors


def validate_topology_provenance(
    visible_metrics: Any,
    manifest_metrics: Any,
    manifest_triangle_counts: Any,
    source_sha256: str,
    manifest_source_sha256: Any,
) -> list[str]:
    """Bind every audited renderer's immutable topology counts to its handoff."""

    if not isinstance(visible_metrics, Mapping):
        return ["visible topology metrics are missing or invalid"]
    if not isinstance(manifest_metrics, Mapping):
        return ["builder topology metrics are missing or invalid"]
    if not isinstance(manifest_triangle_counts, Mapping):
        return ["builder triangle-count provenance is missing or invalid"]
    errors: list[str] = []
    if (
        not isinstance(manifest_source_sha256, str)
        or manifest_source_sha256.lower() != source_sha256.lower()
    ):
        errors.append("builder topology provenance is not bound to the audited source")
    visible_names = set(visible_metrics)
    manifest_names = set(manifest_metrics)
    if visible_names != manifest_names:
        errors.append(
            "topology renderer set differs: "
            f"visible={sorted(visible_names)}, manifest={sorted(manifest_names)}"
        )
    if set(manifest_triangle_counts) != manifest_names:
        errors.append("builder triangle-count renderer set differs")
    fields = (
        "vertices",
        "triangles",
        "boundary_edges",
        "non_manifold_edges",
        "zero_area_faces",
        "duplicate_vertex_pairs",
    )
    for name in sorted(visible_names | manifest_names):
        visible = visible_metrics.get(name)
        manifest = manifest_metrics.get(name)
        if not isinstance(visible, Mapping) or not isinstance(manifest, Mapping):
            errors.append(f"{name} topology evidence is missing or invalid")
            continue
        visible_topology = visible.get("topology")
        if not isinstance(visible_topology, Mapping):
            errors.append(f"{name} visible topology detail is missing")
            continue
        normalized_visible = {
            "vertices": visible.get("vertices"),
            "triangles": visible.get("triangles"),
            **{field: visible_topology.get(field) for field in fields[2:]},
        }
        for field in fields:
            actual = normalized_visible.get(field)
            expected = manifest.get(field)
            if (
                isinstance(actual, bool)
                or isinstance(expected, bool)
                or not isinstance(actual, int)
                or not isinstance(expected, int)
                or actual < 0
                or expected < 0
            ):
                errors.append(f"{name}.{field} provenance is not a non-negative integer")
            elif actual != expected:
                errors.append(
                    f"{name}.{field} differs: visible={actual}, manifest={expected}"
                )
        triangle_count = manifest_triangle_counts.get(name)
        if triangle_count != manifest.get("triangles"):
            errors.append(f"{name} duplicated triangle-count provenance differs")
    return errors


def validate_clearance_report(
    report: Any,
    *,
    kind: str,
    source_path: str,
    source_sha256: str,
    requirements: Mapping[str, Any],
    action_ranges: Mapping[str, Sequence[int]],
) -> list[str]:
    """Validate one source-bound clearance report; omissions fail closed."""

    if kind not in REQUIRED_CLEARANCE_KINDS:
        raise ContractError(f"Unknown clearance kind {kind!r}.")
    if not isinstance(report, Mapping):
        return [f"{kind} report is missing or invalid"]
    errors = validate_report_evidence_sha256(report, label=kind)
    expected_path = Path(source_path)
    actual_path = Path(str(report.get("candidate_blend", "")))
    # Reports normally store repository-relative paths; accept an absolute
    # path only when it resolves to the same audited source.
    if actual_path.is_absolute() or expected_path.is_absolute():
        same_path = actual_path.resolve() == expected_path.resolve()
    else:
        same_path = actual_path.as_posix() == expected_path.as_posix()
    if not same_path:
        errors.append(f"{kind} candidate_blend differs from audited source")
    before = str(report.get("candidate_blend_sha256_before", "")).lower()
    after = str(report.get("candidate_blend_sha256_after", "")).lower()
    expected_hash = source_sha256.lower()
    if before != expected_hash or after != expected_hash:
        errors.append(f"{kind} source hash differs from audited source")
    if report.get("candidate_blend_preserved") is not True:
        errors.append(f"{kind} did not preserve its source")
    if report.get("status") != requirements["required_status"]:
        errors.append(f"{kind} status is not PASS")
    if report.get("collision_geometry_source") != requirements["required_geometry_source"]:
        errors.append(f"{kind} did not audit visible geometry")
    if report.get("promotion_eligible_geometry_source") is not True:
        errors.append(f"{kind} geometry source is not promotion eligible")
    if report.get("forbidden_intersection_instances") != requirements["required_forbidden_instances"]:
        errors.append(f"{kind} contains forbidden intersection instances")
    clearance_metadata = report.get("clearance_metadata")
    if not isinstance(clearance_metadata, Mapping):
        errors.append(f"{kind} clearance metadata is missing")
    else:
        if clearance_metadata.get("status") != "PASS":
            errors.append(f"{kind} clearance metadata status is not PASS")
        if clearance_metadata.get("policy_version") != requirements["policy_version"]:
            errors.append(f"{kind} face policy version differs")
        manifest = clearance_metadata.get("manifest")
        if not isinstance(manifest, Mapping):
            errors.append(f"{kind} embedded clearance manifest is missing")
        else:
            if manifest.get("policy_version") != requirements["policy_version"]:
                errors.append(f"{kind} manifest face policy version differs")
            if (
                manifest.get("contact_window_policy_version")
                != requirements["contact_window_policy_version"]
            ):
                errors.append(f"{kind} contact-window policy version differs")
            contact_windows = manifest.get("contact_windows")
            if not isinstance(contact_windows, Mapping):
                errors.append(f"{kind} contact-window evidence is missing")
            else:
                actual_transition_windows: dict[str, list[Any]] = {}
                for contact_key in ("primary_grip", "support_grip"):
                    windows = contact_windows.get(contact_key)
                    if not isinstance(windows, Sequence) or isinstance(
                        windows, (str, bytes)
                    ):
                        errors.append(f"{kind} {contact_key} windows are invalid")
                        continue
                    actual_transition_windows[contact_key] = [
                        dict(window)
                        for window in windows
                        if isinstance(window, Mapping)
                        and window.get("action")
                        in {"PS_Weapon_Draw", "PS_Weapon_Sheathe"}
                    ]
                if (
                    actual_transition_windows
                    != requirements["transition_contact_windows"]
                ):
                    errors.append(f"{kind} transition contact windows differ")
    sampling = report.get("sampling")
    if not isinstance(sampling, Mapping):
        errors.append(f"{kind} sampling evidence is missing")
        return errors
    mode = sampling.get("mode")
    full_action_names = sorted(action_ranges)
    if len(full_action_names) != requirements["required_full_action_count"]:
        raise ContractError("Clearance action-range contract is incomplete.")
    if kind == "authored_clearance" and mode != "authored_keyframes":
        errors.append("authored_clearance must use authored_keyframes")
    if kind == "all_frame_clearance" and mode != "all_integer_frames":
        errors.append("all_frame_clearance must use all_integer_frames")
    if kind == "dense_transition_clearance":
        if mode != "uniform_dense_frames":
            errors.append("dense_transition_clearance must use uniform_dense_frames")
        if sampling.get("frame_step") != requirements["dense_transition_frame_step"]:
            errors.append("dense_transition_clearance frame_step differs")
        selected_names = requirements["dense_transition_actions"]
        if sampling.get("selected_action_names") != selected_names:
            errors.append("dense_transition_clearance actions differ")
        if sampling.get("action_filters") != selected_names:
            errors.append("dense_transition_clearance action filters differ")
        if sampling.get("inclusive_action_endpoints") is not True:
            errors.append("dense_transition_clearance must include action endpoints")
        if report.get("action_count") != len(selected_names):
            errors.append("dense_transition_clearance action_count differs")
        expected_total = requirements["dense_transition_sample_count"]
    else:
        selected_names = full_action_names
        required_count = requirements["required_full_action_count"]
        if report.get("action_count") != required_count:
            errors.append(f"{kind} action_count differs")
        if report.get("available_action_count") != required_count:
            errors.append(f"{kind} available_action_count differs")
        if sampling.get("selected_action_names") != selected_names:
            errors.append(f"{kind} selected action set differs")
        if sampling.get("action_filters") != []:
            errors.append(f"{kind} must not filter actions")
        if sampling.get("frame_step") is not None:
            errors.append(f"{kind} frame_step must be null")
        if sampling.get("inclusive_action_endpoints") is not False:
            errors.append(f"{kind} inclusive endpoint mode differs")
        expected_total = (
            requirements["required_all_frame_sample_count"]
            if kind == "all_frame_clearance"
            else None
        )
    if report.get("available_action_count") != requirements["required_full_action_count"]:
        errors.append(f"{kind} available_action_count differs")

    actions = report.get("actions")
    if not isinstance(actions, Sequence) or isinstance(actions, (str, bytes)):
        errors.append(f"{kind} per-action sampling evidence is missing")
        return errors
    reported_names = [
        entry.get("action") if isinstance(entry, Mapping) else None
        for entry in actions
    ]
    if reported_names != selected_names:
        errors.append(f"{kind} per-action report set or order differs")
    sampled_total = 0
    for entry in actions:
        if not isinstance(entry, Mapping):
            errors.append(f"{kind} contains an invalid action entry")
            continue
        action_name = entry.get("action")
        frame_range = action_ranges.get(action_name)
        if frame_range is None:
            errors.append(f"{kind} contains unknown action {action_name!r}")
            continue
        sample_frames = entry.get("sample_frames")
        if not isinstance(sample_frames, Sequence) or isinstance(
            sample_frames, (str, bytes)
        ):
            errors.append(f"{kind} {action_name} sample_frames are missing")
            continue
        frames = list(sample_frames)
        if any(
            isinstance(frame, bool)
            or not isinstance(frame, (int, float))
            or not math.isfinite(float(frame))
            for frame in frames
        ):
            errors.append(f"{kind} {action_name} sample_frames are invalid")
            continue
        if frames != sorted(set(frames)):
            errors.append(f"{kind} {action_name} sample_frames are not unique and ordered")
        if entry.get("sample_count") != len(frames):
            errors.append(f"{kind} {action_name} sample_count differs")
        sampled_total += len(frames)
        if entry.get("status") != "PASS":
            errors.append(f"{kind} {action_name} status is not PASS")
        if entry.get("forbidden_intersection_instances") != 0:
            errors.append(f"{kind} {action_name} contains forbidden intersections")
        start, end = frame_range
        if kind == "all_frame_clearance":
            expected_frames = list(range(int(start), int(end) + 1))
        elif kind == "dense_transition_clearance":
            expected_frames = _inclusive_frames(
                float(start),
                float(end),
                float(requirements["dense_transition_frame_step"]),
            )
            expected_count = requirements["dense_transition_action_sample_counts"].get(
                action_name
            )
            if len(expected_frames) != expected_count:
                raise ContractError(
                    f"Internal dense count for {action_name} is inconsistent."
                )
        else:
            expected_frames = None
            if not frames or frames[0] != start or frames[-1] != end:
                errors.append(
                    f"{kind} {action_name} does not cover both authored endpoints"
                )
            if any(frame < start or frame > end for frame in frames):
                errors.append(f"{kind} {action_name} sample escaped its action range")
        if expected_frames is not None and frames != expected_frames:
            errors.append(f"{kind} {action_name} sampled frame coverage differs")
    if report.get("sampled_frame_count") != sampled_total:
        errors.append(f"{kind} top-level sampled_frame_count differs")
    if sampling.get("sampled_frame_count") != sampled_total:
        errors.append(f"{kind} sampling sampled_frame_count differs")
    if expected_total is not None and sampled_total != expected_total:
        errors.append(f"{kind} exact sampled frame total differs")
    return errors
