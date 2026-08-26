from __future__ import annotations

import ast
import importlib.util
import inspect
import json
from pathlib import Path
from types import SimpleNamespace
import sys
import unittest
from unittest import mock


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_new_character_priority_e2e.py"
KARMA_DRIVER = REPO / "tests/run_api36_new_character_karma_e2e.py"
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("priority_compatibility_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


class Api36NewCharacterPriorityE2EDriverTests(unittest.TestCase):
    def test_driver_is_a_syntax_valid_typed_core_prerequisite_entrypoint(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            "import run_api36_creation_prerequisite_e2e as prerequisite",
            "def main(argv: list[str] | None = None) -> int:",
            "return prerequisite.main(argv)",
            "canonical direct-bootstrap prerequisite physical journey",
        ):
            self.assertIn(marker, source)

    def test_legacy_priority_continuation_spirit_and_foundation_are_absent(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        forbidden = (
            "dialog-field-newcharacterpriority",
            "dialog-action-complete-new-character-workflow",
            "Select Metatype Priority",
            "EXPECTED_XML",
            "EXPECTED_PRIORITY_SKILLS",
            "EXPECTED_SPIRIT_XML",
            "assert_persisted_priority",
            "assert_persisted_spirit",
            "creation-stage-foundation",
            "creation-foundation-page",
            "run_api36_creation_wizard_foundation_e2e",
        )
        for marker in forbidden:
            with self.subTest(marker=marker):
                self.assertNotIn(marker, source)

    def test_select_option_public_api_remains_exact_for_karma_driver(self) -> None:
        signature = inspect.signature(driver.select_option)
        self.assertEqual(
            ("device", "selector", "option_label", "scroll"),
            tuple(signature.parameters),
        )
        scroll = signature.parameters["scroll"]
        self.assertEqual(inspect.Parameter.KEYWORD_ONLY, scroll.kind)
        self.assertIs(True, scroll.default)

        karma = KARMA_DRIVER.read_text(encoding="utf-8")
        self.assertIn(
            "import run_api36_new_character_priority_e2e as priority_helpers",
            karma,
        )
        self.assertIn("priority_helpers.select_option(", karma)
        self.assertIn("priority_helpers.workspace_payloads(device)", karma)

    def test_priority_picker_keeps_cardinality_checked_bidirectional_lookup(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        field_tap = source[source.index("def tap_exact_field(") : source.index("def tap_exact_option(")]
        for marker in (
            "device.wait_exact_resource_id_bidirectional(",
            "backward_scrolls=18",
            "forward_scrolls=18",
            'evidence_prefix=f"priority-field-{selector}"',
            'surface_name="Priority dialog field"',
        ):
            self.assertIn(marker, field_tap)
        self.assertNotIn("device.swipe_up", field_tap)

    def test_workspace_payload_helper_keeps_only_xml_envelopes(self) -> None:
        class Device:
            @staticmethod
            def shell(*_arguments: str) -> str:
                return "/state/one\n/state/two\n/state/three\n"

            @staticmethod
            def run(*arguments: str) -> SimpleNamespace:
                path = arguments[-1]
                if path.endswith("one"):
                    payload = {"Envelope": {"Payload": "<character />"}}
                elif path.endswith("two"):
                    payload = {"Envelope": {"Payload": "not xml"}}
                else:
                    payload = {"Other": True}
                return SimpleNamespace(stdout=json.dumps(payload))

        self.assertEqual(["<character />"], driver.workspace_payloads(Device()))

    def test_main_forwards_exact_argv_to_prerequisite_driver(self) -> None:
        arguments = ["--serial", "physical-phone", "--receipt", "/tmp/receipt.json"]
        with mock.patch.object(driver.prerequisite, "main", return_value=17) as delegated:
            self.assertEqual(17, driver.main(arguments))
        delegated.assert_called_once_with(arguments)


if __name__ == "__main__":
    unittest.main()
