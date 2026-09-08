import os
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch


TESTS = Path(__file__).resolve().parent


class CanonicalTestEnvironmentTests(unittest.TestCase):
    def test_canonical_source_tests_honor_explicit_chummer5_root_without_fallback(self) -> None:
        modules = (
            "test_api36_application_index_visibility_settings_e2e_driver.py",
            "test_api36_application_selection_behavior_settings_e2e_driver.py",
            "test_api36_application_update_settings_e2e_driver.py",
            "test_api36_career_create_expense_e2e_driver.py",
        )
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "explicit-checkout"
            for exists in (False, True):
                if exists:
                    root.mkdir()
                with patch.dict(os.environ, {"CHUMMER5A_ROOT": str(root)}):
                    for module in modules:
                        with self.subTest(module=module, exists=exists):
                            namespace = runpy.run_path(str(TESTS / module))
                            self.assertEqual(root, namespace["CHUMMER5"])
