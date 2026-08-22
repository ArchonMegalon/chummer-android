import copy
import hashlib
import json
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path

from scripts.verify_android_content_bundle import (
    CORE_REVISION,
    MANIFEST_ENTRY,
    PACKAGED_ROOT,
    SCHEMA,
    _bundle_digest,
    validate_manifest,
    verify_apk,
    verify_project_contract,
)


REPO_ROOT = Path(__file__).resolve().parents[1]
MANIFEST_PATH = (
    REPO_ROOT / "src/Chummer.Android/Content/chummer-content-manifest.json"
)


class AndroidContentBundleTests(unittest.TestCase):
    def make_manifest(self, files: dict[str, bytes]) -> tuple[dict, bytes]:
        entries = [
            {
                "path": path,
                "size": len(value),
                "sha256": hashlib.sha256(value).hexdigest(),
            }
            for path, value in sorted(files.items())
        ]
        manifest = {
            "schema": SCHEMA,
            "coreRevision": CORE_REVISION,
            "bundleDigest": _bundle_digest(entries),
            "files": entries,
        }
        return manifest, (json.dumps(manifest, indent=2) + "\n").encode()

    def make_catalogs(self) -> dict[str, bytes]:
        return {
            "data/lifemodules.xml": b"<chummer />",
            "data/nested/catalog.xml": b"<chummer />",
            "lang/en-us.xml": b"<chummer />",
            "lang/nested/de-de.xml": b"<chummer />",
        }

    def write_apk(
        self,
        apk_path: Path,
        files: dict[str, bytes],
        manifest_bytes: bytes,
        *,
        omit: str | None = None,
        tamper: str | None = None,
        duplicate: str | None = None,
    ) -> None:
        with zipfile.ZipFile(apk_path, "w") as archive:
            archive.writestr(MANIFEST_ENTRY, manifest_bytes)
            for relative, value in sorted(files.items()):
                if relative == omit:
                    continue
                entry = f"{PACKAGED_ROOT}/{relative}"
                archive.writestr(entry, b"tampered" if relative == tamper else value)
                if relative == duplicate:
                    archive.writestr(entry, value)

    def test_android_runtime_contract_is_atomic_reusable_and_before_di(self) -> None:
        self.assertEqual([], verify_project_contract(REPO_ROOT))
        materializer = (
            REPO_ROOT
            / "src/Chummer.Android/Platforms/Android/AndroidBundledContentMaterializer.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("VerifyMaterializedContent(destinationRoot, manifest);", materializer)
        self.assertIn("QuarantineInvalidGeneration", materializer)
        self.assertIn("CleanupInterruptedStaging(contentContainer);", materializer)
        self.assertIn('".staging-*"', materializer)
        self.assertIn("DeleteOwnedTree(stagingRoot);", materializer)
        self.assertNotIn("Directory.Delete(destinationRoot", materializer)
        self.assertNotIn("SearchOption.AllDirectories", materializer)

    def test_committed_manifest_is_exact_pinned_core_catalog_inventory(self) -> None:
        manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        self.assertEqual([], validate_manifest(manifest))
        self.assertEqual(CORE_REVISION, manifest["coreRevision"])
        self.assertEqual(110, len(manifest["files"]))
        self.assertEqual(17_371_170, sum(entry["size"] for entry in manifest["files"]))

    def test_verifier_receipt_is_self_contained_and_digest_bound(self) -> None:
        verifier = (
            REPO_ROOT / "scripts/verify_android_content_bundle.py"
        ).read_text(encoding="utf-8")
        for field in (
            '"apkSha256"',
            '"manifestSha256"',
            '"bundleDigest"',
            '"canonicalFileCount"',
            '"canonicalByteCount"',
            '"coreRevision"',
            '"status"',
            '"issues"',
        ):
            with self.subTest(field=field):
                self.assertIn(field, verifier)
        self.assertIn('parser.add_argument("--receipt", type=Path)', verifier)

    def test_signed_apk_verifier_hashes_every_canonical_file(self) -> None:
        files = self.make_catalogs()
        manifest, manifest_bytes = self.make_manifest(files)
        with tempfile.TemporaryDirectory() as temporary_directory:
            apk_path = Path(temporary_directory) / "candidate.apk"
            self.write_apk(apk_path, files, manifest_bytes)

            packaged_count, issues = verify_apk(apk_path, manifest, manifest_bytes)
            self.assertEqual(len(files), packaged_count)
            self.assertEqual([], issues)

    def test_signed_apk_verifier_reports_actual_count_for_incomplete_bundle(self) -> None:
        files = self.make_catalogs()
        manifest, manifest_bytes = self.make_manifest(files)
        missing = "data/lifemodules.xml"
        with tempfile.TemporaryDirectory() as temporary_directory:
            apk_path = Path(temporary_directory) / "candidate.apk"
            self.write_apk(apk_path, files, manifest_bytes, omit=missing)

            packaged_count, issues = verify_apk(apk_path, manifest, manifest_bytes)
            self.assertEqual(len(files) - 1, packaged_count)
            self.assertTrue(any("canonical-content-missing" in issue for issue in issues))

    def test_signed_apk_verifier_rejects_tampering_and_duplicate_members(self) -> None:
        files = self.make_catalogs()
        manifest, manifest_bytes = self.make_manifest(files)
        target = "data/lifemodules.xml"
        with tempfile.TemporaryDirectory() as temporary_directory:
            apk_path = Path(temporary_directory) / "candidate.apk"
            with warnings.catch_warnings():
                warnings.simplefilter("ignore", UserWarning)
                self.write_apk(
                    apk_path,
                    files,
                    manifest_bytes,
                    tamper=target,
                    duplicate="lang/en-us.xml",
                )

            _, issues = verify_apk(apk_path, manifest, manifest_bytes)
            self.assertTrue(any("duplicate-members" in issue for issue in issues))
            self.assertTrue(any("sha256-mismatch" in issue for issue in issues))

    def test_manifest_validation_rejects_traversal_tamper_and_wrong_revision(self) -> None:
        manifest, _ = self.make_manifest(self.make_catalogs())
        traversal = copy.deepcopy(manifest)
        traversal["files"][0]["path"] = "data/../lang/en-us.xml"
        self.assertTrue(any("path-unsafe" in issue for issue in validate_manifest(traversal)))

        tampered = copy.deepcopy(manifest)
        tampered["files"][0]["size"] += 1
        self.assertIn("content-manifest-bundle-digest-mismatch", validate_manifest(tampered))

        wrong_revision = copy.deepcopy(manifest)
        wrong_revision["coreRevision"] = "0" * 40
        self.assertIn("content-manifest-core-revision-invalid", validate_manifest(wrong_revision))


if __name__ == "__main__":
    unittest.main()
