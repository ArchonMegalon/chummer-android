from __future__ import annotations

import ast
import importlib.util
import sys
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_origin_dossier_e2e.py"
SHARED_DRIVER = REPO / "tests" / "run_api36_editing_e2e.py"
SHARED_SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", SHARED_DRIVER)
assert SHARED_SPEC is not None and SHARED_SPEC.loader is not None
shared = importlib.util.module_from_spec(SHARED_SPEC)
sys.modules[SHARED_SPEC.name] = shared
SHARED_SPEC.loader.exec_module(shared)
DRIVER_SPEC = importlib.util.spec_from_file_location("run_api36_origin_dossier_e2e", DRIVER)
assert DRIVER_SPEC is not None and DRIVER_SPEC.loader is not None
driver = importlib.util.module_from_spec(DRIVER_SPEC)
sys.modules[DRIVER_SPEC.name] = driver
DRIVER_SPEC.loader.exec_module(driver)


class Api36OriginDossierE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_and_syntax_valid(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertNotIn('"profile": "tablet"', source)

    def test_driver_covers_every_origin_field_in_both_legacy_forms(self) -> None:
        self.assertEqual(13, len(driver.FIELDS))
        self.assertEqual({"CharacterCreate", "CharacterCareer"}, set(driver.CASE_VALUES))
        for values in driver.CASE_VALUES.values():
            self.assertEqual(set(driver.FIELDS), set(values))
        self.assertEqual(
            26,
            len(
                {
                    f"{form_name}.{control}"
                    for form_name in driver.CASE_VALUES
                    for control in driver.FIELDS
                }
            ),
        )

    def test_driver_proves_workspace_xml_and_restart_ui_readback(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "assert_workspace_origin",
            "assert_origin_ui_after_restart",
            'device.shell("am", "force-stop", shared.PACKAGE)',
            '"workspacePersisted"',
            '"processRestartUiReadback"',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartUiReadback": "pass"',
            '"careerProcessRestartUiReadback": "pass"',
            '"controls": control_proofs',
        ):
            self.assertIn(marker, source)

    def test_receipt_is_bound_to_the_complete_mutation_source_graph(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "sharedDriverSha256",
            "originDossierPageSha256",
            "runnerSessionCoordinatorSha256",
            "workspaceMutationsSha256",
            "careerFixtureSha256",
        ):
            self.assertIn(marker, source)


if __name__ == "__main__":
    unittest.main()
