import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

from tests.api36_physical_build_provenance import (
    AUTHORITY_CLASS,
    SCHEMA,
    create_manifest,
    load_and_verify_manifest,
    write_manifest,
)


class Api36PhysicalBuildProvenanceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.repositories = {
            name: self.make_repository(name)
            for name in ("android", "core", "presentation")
        }
        self.apk = self.root / "chummer-android-arm64-debug.apk"
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("lib/arm64-v8a/libmonodroid.so", b"arm64-proof")
            archive.writestr("AndroidManifest.xml", b"binary-manifest-placeholder")
        self.manifest_path = self.root / "build-provenance.json"

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def make_repository(self, name: str) -> Path:
        repository = self.root / name
        repository.mkdir()
        subprocess.run(["git", "init", "-q"], cwd=repository, check=True)
        subprocess.run(
            ["git", "config", "user.email", "proof@example.invalid"],
            cwd=repository,
            check=True,
        )
        subprocess.run(
            ["git", "config", "user.name", "Proof Test"],
            cwd=repository,
            check=True,
        )
        (repository / "tracked.txt").write_text(name, encoding="utf-8")
        subprocess.run(["git", "add", "tracked.txt"], cwd=repository, check=True)
        subprocess.run(["git", "commit", "-qm", "fixture"], cwd=repository, check=True)
        return repository

    def mint(self) -> dict[str, object]:
        manifest = create_manifest(
            android_root=self.repositories["android"],
            core_root=self.repositories["core"],
            presentation_root=self.repositories["presentation"],
            apk=self.apk,
        )
        write_manifest(self.manifest_path, manifest)
        return manifest

    def verify(self) -> dict[str, object]:
        return load_and_verify_manifest(
            self.manifest_path,
            android_root=self.repositories["android"],
            core_root=self.repositories["core"],
            presentation_root=self.repositories["presentation"],
            apk=self.apk,
        )

    def test_exact_clean_trees_and_arm64_apk_round_trip(self) -> None:
        minted = self.mint()
        self.assertEqual(minted, self.verify())
        self.assertEqual(SCHEMA, minted["schema"])
        self.assertEqual(AUTHORITY_CLASS, minted["authorityClass"])
        self.assertFalse(minted["releaseAttested"])
        self.assertEqual("android-arm64", minted["artifact"]["runtimeIdentifier"])

    def test_changed_apk_fails_closed(self) -> None:
        self.mint()
        with zipfile.ZipFile(self.apk, "a") as archive:
            archive.writestr("tampered", b"changed")
        with self.assertRaisesRegex(RuntimeError, "APK differs"):
            self.verify()

    def test_changed_or_dirty_source_fails_closed(self) -> None:
        self.mint()
        (self.repositories["core"] / "tracked.txt").write_text("dirty", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "dirty"):
            self.verify()

    def test_manifest_digest_and_duplicate_keys_fail_closed(self) -> None:
        self.mint()
        payload = json.loads(self.manifest_path.read_text(encoding="utf-8"))
        payload["artifact"]["sha256"] = "0" * 64
        self.manifest_path.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "authority digest"):
            self.verify()

        self.manifest_path.write_text(
            '{"schema":"a","schema":"b"}', encoding="utf-8"
        )
        with self.assertRaisesRegex(RuntimeError, "Duplicate JSON key"):
            self.verify()


if __name__ == "__main__":
    unittest.main()
