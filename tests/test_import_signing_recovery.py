from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "import-signing-recovery.py"
SPEC = importlib.util.spec_from_file_location("import_signing_recovery", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
recovery = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(recovery)


def release_environment() -> str:
    return "\n".join(
        (
            "AndroidSigningKeyStore=/private/chummer-upload.p12",
            "ChummerAndroidSigningStorePass=store-pass",
            "ChummerAndroidSigningKeyAlias=chummer-upload",
            "ChummerAndroidSigningKeyPass=key-pass",
            "CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH=/private/chummer-upload-cert.pem",
        )
    )


class SigningRecoveryImporterTests(unittest.TestCase):
    def test_fingerprint_normalization_is_exact(self) -> None:
        compact = "d9c4b635121544d5522abf1ec2dfda3c1938aab93d6726bb93c9871ec9ed1d15"
        self.assertEqual(
            "D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15",
            recovery._normalize_fingerprint(compact),
        )
        with self.assertRaisesRegex(ValueError, "exactly 32 bytes"):
            recovery._normalize_fingerprint("D9:C4")

    def test_release_environment_requires_only_the_exact_keys(self) -> None:
        parsed = recovery._parse_release_environment(release_environment())
        self.assertEqual("chummer-upload", parsed["ChummerAndroidSigningKeyAlias"])

        with self.assertRaisesRegex(ValueError, "unexpected or duplicate key"):
            recovery._parse_release_environment(release_environment() + "\nEXTRA=value")
        with self.assertRaisesRegex(ValueError, "incomplete"):
            recovery._parse_release_environment("AndroidSigningKeyStore=/private/key.p12")

    def test_recovery_bundle_must_be_owner_only(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            bundle = Path(raw_directory) / "recovery.json"
            bundle.write_text("{}", encoding="utf-8")
            bundle.chmod(0o644)
            with self.assertRaisesRegex(ValueError, "owner-only"):
                recovery._load_bundle(bundle)

    def test_import_refuses_a_target_inside_the_repository(self) -> None:
        with self.assertRaisesRegex(ValueError, "outside the Chummer Android repository"):
            recovery.import_bundle(
                REPO / "missing-recovery.json",
                REPO / "private-signing",
                "D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15",
                Path("/bin/true"),
            )


if __name__ == "__main__":
    unittest.main()
