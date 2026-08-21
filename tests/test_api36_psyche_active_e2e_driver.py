from __future__ import annotations

import hashlib
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_psyche_active_e2e.py"
FIXTURE = REPO / "tests" / "fixtures" / "career-psyche-active-e2e.chum5"


class Api36PsycheActiveDriverTests(unittest.TestCase):
    def test_driver_binds_both_legacy_surfaces_to_restart_safe_shared_state(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            '"CharacterCareer.chkPsycheActiveMagician"',
            '"CharacterCareer.chkPsycheActiveTechnomancer"',
            'MAGICIAN = "sustained-psyche-active-magician"',
            'TECHNOMANCER = "sustained-psyche-active-technomancer"',
            'api != "36"',
            '"journey": "psyche-active"',
            'device.shell("am", "force-stop"',
            '"sharedSavedPsycheBoolean"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
        ):
            self.assertIn(marker, source)

    def test_fixture_has_exact_career_spell_complex_form_and_preservation_state(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("False", root.findtext("psyche"))
        self.assertEqual(
            {"Spell", "ComplexForm"},
            {item.findtext("linkedobjecttype") for item in root.findall("./sustainedobjects/sustainedobject")},
        )
        self.assertEqual("Preserve unrelated Psyche fixture state", root.findtext("./customstate/psyche"))
        self.assertEqual(64, len(hashlib.sha256(DRIVER.read_bytes()).hexdigest()))


if __name__ == "__main__":
    unittest.main()
