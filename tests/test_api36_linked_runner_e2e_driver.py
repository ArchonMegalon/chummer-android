from __future__ import annotations

import ast
import importlib.util
import sys
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_linked_runner_e2e.py"
SHARED_DRIVER = REPO / "tests" / "run_api36_editing_e2e.py"
SHARED_SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", SHARED_DRIVER)
assert SHARED_SPEC is not None and SHARED_SPEC.loader is not None
shared = importlib.util.module_from_spec(SHARED_SPEC)
sys.modules[SHARED_SPEC.name] = shared
SHARED_SPEC.loader.exec_module(shared)
DRIVER_SPEC = importlib.util.spec_from_file_location("run_api36_linked_runner_e2e", DRIVER)
assert DRIVER_SPEC is not None and DRIVER_SPEC.loader is not None
driver = importlib.util.module_from_spec(DRIVER_SPEC)
sys.modules[DRIVER_SPEC.name] = driver
DRIVER_SPEC.loader.exec_module(driver)


class Api36LinkedRunnerE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_and_syntax_valid(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertNotIn('"profile": "tablet"', source)

    def test_driver_covers_exact_contact_and_pet_attach_remove_controls(self) -> None:
        self.assertEqual(
            {
                "ContactControl.tsAttachCharacter",
                "ContactControl.tsRemoveCharacter",
                "PetControl.tsAttachCharacter",
                "PetControl.tsRemoveCharacter",
            },
            set(driver.CONTROLS),
        )

    def test_driver_proves_attach_and_remove_across_separate_restarts(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "attach_linked_runner",
            "assert_link_persisted_then_remove",
            "assert_unlinked_after_restart",
            '"processRestartAttachPersistence": "pass"',
            '"processRestartRemovePersistence": "pass"',
            '"controls": control_proofs',
            'device.shell("am", "force-stop", shared.PACKAGE)',
        ):
            self.assertIn(marker, source)

    def test_receipt_is_bound_to_link_storage_and_workspace_mutation_sources(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "linkedCharacterFileServiceSha256",
            "linkedDocumentCodecSha256",
            "workspaceCollectionEditorProjectorSha256",
            "workspaceCollectionMutationRequestSha256",
            "workspaceXmlMutationCatalogSha256",
            "workspaceMutationsSha256",
            "linkedFixtureSha256",
            "invalidLinkedFixtureSha256",
        ):
            self.assertIn(marker, source)


if __name__ == "__main__":
    unittest.main()
