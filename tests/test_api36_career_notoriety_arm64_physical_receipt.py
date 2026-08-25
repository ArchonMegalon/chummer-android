import hashlib
import json
from pathlib import Path
import unittest


REPO_ROOT = Path(__file__).resolve().parents[1]
RECEIPT = (
    REPO_ROOT
    / "docs/editability-evidence/api36-phone-career-notoriety-arm64-physical/receipt.json"
)
DRIVER = REPO_ROOT / "tests/run_api36_career_notoriety_arm64_physical_e2e.py"
SHARED_DRIVER = REPO_ROOT / "tests/run_api36_editing_e2e.py"
JOURNEY_DRIVER = REPO_ROOT / "tests/run_api36_career_notoriety_e2e.py"
FIXTURE = REPO_ROOT / "tests/fixtures/career-notoriety-e2e.chum5"
APK_SHA256 = "4c28a73b0e4dbb5da49ecc0b454f39e1d0d0a8cbebea09a696bca047d2a2ddc0"
PROVENANCE_SHA256 = "2365729a38e8eae9cdec395df67b1120cb4601f1ab3765cde1c17150e5dbf12e"
PACKAGE = "com.myexternalbrain.chummer.codexproof.arm64"
PRESERVED_PACKAGES = {
    "com.myexternalbrain.chummer",
    "com.myexternalbrain.chummer.codexproof",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class PhysicalArm64ReceiptTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.receipt = json.loads(RECEIPT.read_text(encoding="utf-8"))

    def test_receipt_is_bound_to_current_drivers_and_exact_artifact(self):
        receipt = self.receipt
        self.assertEqual("chummer.android.physical-editing-e2e/v1", receipt["schema"])
        self.assertEqual("pass", receipt["status"])
        self.assertEqual("phone", receipt["profile"])
        self.assertEqual("career-notoriety", receipt["journey"])
        self.assertEqual(36, receipt["apiLevel"])
        self.assertEqual("arm64-v8a", receipt["abi"])
        self.assertEqual(PACKAGE, receipt["package"])
        self.assertEqual(APK_SHA256, receipt["apkSha256"])
        self.assertEqual(PROVENANCE_SHA256, receipt["provenanceSha256"])
        self.assertEqual(sha256(DRIVER), receipt["driverSha256"])
        self.assertEqual(sha256(SHARED_DRIVER), receipt["sharedDriverSha256"])
        self.assertEqual(sha256(JOURNEY_DRIVER), receipt["journeyDriverSha256"])
        self.assertEqual(sha256(FIXTURE), receipt["careerFixtureSha256"])
        self.assertEqual(
            receipt["careerFixtureSha256"],
            receipt["verifiedRemoteCareerFixtureSha256"],
        )

    def test_installed_apk_and_package_boundaries_are_unchanged(self):
        receipt = self.receipt
        self.assertEqual(receipt["installedPackagesBefore"], receipt["installedPackagesAfter"])
        self.assertEqual(
            PRESERVED_PACKAGES | {PACKAGE},
            set(receipt["installedPackagesBefore"]),
        )
        self.assertEqual(receipt["installedApkBefore"], receipt["installedApkAfter"])
        self.assertEqual(PACKAGE, receipt["installedApkBefore"]["package"])
        self.assertEqual(APK_SHA256, receipt["installedApkBefore"]["sha256"])
        self.assertTrue(receipt["installedApkBefore"]["path"].startswith("/data/app/"))
        self.assertTrue(receipt["installedApkBefore"]["path"].endswith("/base.apk"))

    def test_exact_mutation_and_restart_authority_passed(self):
        receipt = self.receipt
        self.assertEqual(1, receipt["controlCount"])
        control = receipt["controls"]["CharacterCareer.nudNotoriety"]
        self.assertTrue(control)
        self.assertEqual({"pass"}, set(control.values()))
        stages = receipt["authorityProofStages"]
        self.assertEqual(7, stages["initialValue"])
        self.assertEqual(8, stages["savedValue"])
        self.assertEqual(1, stages["import"]["contentRevision"])
        self.assertEqual(0, stages["import"]["savedRevision"])
        self.assertEqual(2, stages["saved"]["contentRevision"])
        self.assertEqual(2, stages["saved"]["savedRevision"])
        self.assertEqual(stages["saved"], stages["restored"])
        self.assertNotEqual(stages["import"]["payloadSha256"], stages["saved"]["payloadSha256"])
        self.assertNotEqual(stages["import"]["documentSha256"], stages["saved"]["documentSha256"])
        pids = stages["restartProcessIds"]
        self.assertTrue(pids["beforeForceStop"])
        self.assertEqual([], pids["afterForceStop"])
        self.assertTrue(pids["restarted"])
        self.assertTrue(set(pids["beforeForceStop"]).isdisjoint(pids["restarted"]))
        self.assertEqual({"pass"}, set(receipt["journeys"].values()))

    def test_provenance_is_fail_closed_about_distribution_scope(self):
        provenance = self.receipt["provenance"]
        self.assertEqual("chummer.android.physical-apk/v1", provenance["schema"])
        self.assertEqual("pass", provenance["status"])
        self.assertEqual(PACKAGE, provenance["packageId"])
        self.assertEqual(APK_SHA256, provenance["apkSha256"])
        self.assertEqual("isolated-physical-proof", provenance["distributionLane"])
        self.assertEqual("ephemeral-hosted-debug-one-shot", provenance["signingPosture"])
        self.assertFalse(provenance["stableUpdateSigningClaim"])
        self.assertFalse(provenance["playBetaSigningClaim"])


if __name__ == "__main__":
    unittest.main()
