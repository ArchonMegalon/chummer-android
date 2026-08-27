import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src" / "Chummer.Android" / "Native"


class NativeThemeSourceContractTests(unittest.TestCase):
    def test_downtime_success_state_uses_a_real_shared_semantic_color(self) -> None:
        theme = (NATIVE / "NativeTheme.cs").read_text(encoding="utf-8")
        downtime = (NATIVE / "Sr5DowntimeCalendarWizardPage.cs").read_text(
            encoding="utf-8"
        )
        stubs = (
            ROOT
            / "tests"
            / "Chummer.Android.Sr5DowntimeCalendar.NativeCompile.Tests"
            / "NativeCompileStubs.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("public static readonly Color Success", theme)
        self.assertIn("TextColor = Success", theme)
        self.assertIn("NativeTheme.Success", downtime)
        self.assertIn("Color Success", stubs)


if __name__ == "__main__":
    unittest.main()
