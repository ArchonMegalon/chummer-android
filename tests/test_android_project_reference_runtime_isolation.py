from __future__ import annotations

import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


PROJECT = Path(__file__).resolve().parents[1] / "src" / "Chummer.Android" / "Chummer.Android.csproj"


class AndroidProjectReferenceRuntimeIsolationTests(unittest.TestCase):
    def test_android_runtime_identifier_is_not_forwarded_to_portable_projects(self) -> None:
        root = ET.parse(PROJECT).getroot()
        values = [
            (element.text or "").strip()
            for element in root.iter("_GlobalPropertiesToRemoveFromProjectReferences")
        ]

        self.assertEqual(1, len(values))
        properties = {value for value in values[0].split(";") if value}
        self.assertTrue(
            {"RuntimeIdentifier", "RuntimeIdentifiers", "SelfContained"}.issubset(properties)
        )
        self.assertIn("$(_GlobalPropertiesToRemoveFromProjectReferences)", values[0])


if __name__ == "__main__":
    unittest.main()
