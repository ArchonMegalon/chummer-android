import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
DRIVER_PATH = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", DRIVER_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"could not load {DRIVER_PATH}")
DRIVER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = DRIVER
SPEC.loader.exec_module(DRIVER)


class FakeDevice(DRIVER.Device):
    def __init__(self, evidence: Path, hierarchy_output: str) -> None:
        super().__init__(Path("/unused/adb"), "emulator-5554", evidence)
        self.hierarchy_output = hierarchy_output

    def shell(self, *arguments: str, timeout: int = 120) -> str:
        return "UI hierarchy dumped"

    def run(
        self,
        *arguments: str,
        timeout: int = 120,
        text: bool = True,
    ) -> subprocess.CompletedProcess:
        return subprocess.CompletedProcess(
            args=list(arguments),
            returncode=0,
            stdout=self.hierarchy_output,
            stderr="",
        )


class Api36EditingE2EDriverTests(unittest.TestCase):
    def test_hierarchy_ignores_uiautomator_preamble(self) -> None:
        output = (
            "UI hierarchy dumped to: /sdcard/window.xml\n"
            "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>"
            "<hierarchy><node text='Your runners' /></hierarchy>"
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = FakeDevice(Path(temporary), output)
            nodes = device.hierarchy()

        self.assertEqual("Your runners", nodes[0].attributes["text"])

    def test_invalid_hierarchy_is_retryable_and_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = FakeDevice(evidence, "ERROR: could not get idle state")
            self.assertEqual([], device.hierarchy())
            diagnostic = evidence / "last-invalid-hierarchy.txt"
            self.assertTrue(diagnostic.is_file())
            self.assertIn("could not get idle state", diagnostic.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
