from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts/verify_release_artifact_hygiene.py"
SPEC = importlib.util.spec_from_file_location("release_artifact_hygiene", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


class ReleaseArtifactHygieneTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.aab = self.root / "candidate.aab"
        self.receipt = self.root / "ANDROID_API36_TWO_GREEN_ELIGIBILITY.generated.json"
        self.approval = self.root / "ANDROID_API36_TWO_GREEN_RELEASE_APPROVAL.generated.json"

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def write_bundle(self, payload: bytes) -> None:
        with zipfile.ZipFile(self.aab, "w") as archive:
            archive.writestr("BundleConfig.pb", b"bundle")
            archive.writestr("base/root/assets/payload.bin", payload)

    def test_clean_bundle_passes(self) -> None:
        self.write_bundle(b"ordinary application bytes")
        module.verify(self.aab, [self.receipt, self.approval])

    def test_protected_path_environment_and_filename_leaks_fail_closed(self) -> None:
        for marker in (
            str(self.receipt).encode(),
            self.approval.name.encode(),
            b"CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT",
            b"CHUMMER_ANDROID_TWO_GREEN_RELEASE_APPROVAL",
        ):
            with self.subTest(marker=marker):
                self.aab.unlink(missing_ok=True)
                self.write_bundle(b"prefix:" + marker + b":suffix")
                with self.assertRaisesRegex(ValueError, "leaked into AAB"):
                    module.verify(self.aab, [self.receipt, self.approval])


if __name__ == "__main__":
    unittest.main()
