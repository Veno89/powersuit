from __future__ import annotations

import ast
import hashlib
import json
import types
import unittest
from pathlib import Path


SCRIPT = (
    Path(__file__).resolve().parents[1]
    / "scripts"
    / "reauthor_candidate006_weapon_actions.py"
)
HELPERS = {
    "_signature_value",
    "_rna_properties",
    "_keyframe_document",
    "_modifier_document",
    "_curve_group_document",
}


def load_signature_helpers() -> dict[str, object]:
    """Load the pure evidence helpers without importing Blender-only modules."""
    parsed = ast.parse(SCRIPT.read_text(encoding="utf-8"), filename=str(SCRIPT))
    selected = [
        node
        for node in parsed.body
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
        and node.name in HELPERS
    ]
    if {node.name for node in selected} != HELPERS:
        raise AssertionError("Candidate006 action-signature helper set is incomplete")
    module = ast.Module(body=selected, type_ignores=[])
    ast.fix_missing_locations(module)
    namespace: dict[str, object] = {}
    exec(compile(module, str(SCRIPT), "exec"), namespace)
    return namespace


class VectorValue:
    def __init__(self, *values: float) -> None:
        self.values = values

    def to_tuple(self) -> tuple[float, ...]:
        return self.values


class PropertyDefinition:
    def __init__(self, identifier: str, property_type: str = "FLOAT") -> None:
        self.identifier = identifier
        self.type = property_type


class ReauthorActionSignatureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.helpers = load_signature_helpers()

    def key(self, **overrides: object) -> types.SimpleNamespace:
        values: dict[str, object] = {
            "co": VectorValue(1.0, 2.0),
            "handle_left": VectorValue(0.75, 1.8),
            "handle_right": VectorValue(1.25, 2.2),
            "interpolation": "BEZIER",
            "handle_left_type": "AUTO_CLAMPED",
            "handle_right_type": "AUTO_CLAMPED",
            "easing": "AUTO",
            "amplitude": 0.0,
            "back": 0.0,
            "period": 0.0,
            "type": "KEYFRAME",
        }
        values.update(overrides)
        return types.SimpleNamespace(**values)

    def digest(self, document: object) -> str:
        payload = json.dumps(document, sort_keys=True, separators=(",", ":"))
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()

    def test_keyframe_signature_binds_interpolation_handles_and_easing(self) -> None:
        document = self.helpers["_keyframe_document"](self.key())
        self.assertEqual(document["interpolation"], "BEZIER")
        self.assertEqual(document["handle_left"], [0.75, 1.8])
        self.assertEqual(document["handle_right_type"], "AUTO_CLAMPED")
        self.assertEqual(document["easing"], "AUTO")

        changed = self.helpers["_keyframe_document"](
            self.key(interpolation="LINEAR", handle_right=VectorValue(1.3, 2.4))
        )
        self.assertNotEqual(self.digest(document), self.digest(changed))

    def test_modifier_signature_binds_rna_properties_and_collections(self) -> None:
        point = types.SimpleNamespace(
            bl_rna=types.SimpleNamespace(
                properties=[
                    PropertyDefinition("frame"),
                    PropertyDefinition("min"),
                    PropertyDefinition("max"),
                ]
            ),
            frame=10.0,
            min=-0.2,
            max=0.4,
        )
        modifier = types.SimpleNamespace(
            type="ENVELOPE",
            bl_rna=types.SimpleNamespace(
                properties=[
                    PropertyDefinition("strength"),
                    PropertyDefinition("control_points", "COLLECTION"),
                ]
            ),
            strength=0.75,
            control_points=[point],
        )
        document = self.helpers["_modifier_document"](modifier)
        self.assertEqual(document["type"], "ENVELOPE")
        self.assertEqual(document["properties"]["strength"], 0.75)
        self.assertEqual(
            document["properties"]["control_points"],
            [{"frame": 10.0, "max": 0.4, "min": -0.2}],
        )

    def test_curve_group_signature_binds_evaluation_mute_state(self) -> None:
        curve = types.SimpleNamespace(
            group=types.SimpleNamespace(name="Chest", mute=False, lock=True)
        )
        document = self.helpers["_curve_group_document"](curve)
        self.assertEqual(document, {"name": "Chest", "mute": False, "lock": True})


if __name__ == "__main__":
    unittest.main()
