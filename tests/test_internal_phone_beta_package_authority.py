from __future__ import annotations

import copy
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts/verify_internal_phone_beta_package_authority.py"
MANIFEST = REPO / "eng/internal-phone-beta-package-authority.json"


def load_module():
    spec = importlib.util.spec_from_file_location("internal_phone_beta_authority", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_private(path: Path, payload: object) -> None:
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    os.chmod(path, 0o600)


class InternalPhoneBetaPackageAuthorityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.module = load_module()
        cls.payload = json.loads(MANIFEST.read_text(encoding="utf-8"))

    def validate_copy(self, payload: dict[str, object]) -> dict[str, object]:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "authority.json"
            write_private(path, payload)
            return self.module.validate_manifest(path)

    def test_exact_internal_binding_is_valid_and_never_public_ready(self) -> None:
        validated = self.module.validate_manifest(MANIFEST)
        binding = self.module.build_binding(validated)
        self.assertEqual(self.module.CONTRACT, binding["contractName"])
        self.assertEqual("internal_phone_beta_only", binding["authorityClass"])
        self.assertEqual("independently_audited", binding["authorityState"])
        self.assertFalse(binding["publicationAuthorized"])
        self.assertIn("public_release_readiness", binding["doesNotAssert"])
        self.assertIn("api36_device_execution", binding["doesNotAssert"])

    def test_production_presentation_source_pin_is_preserved_separately(self) -> None:
        source = self.payload["presentationSource"]
        self.assertEqual(self.module.EXPECTED_PRODUCTION_COMMIT, source["productionCommit"])
        self.assertEqual(self.module.EXPECTED_PRODUCTION_TREE, source["productionTree"])
        self.assertEqual(self.module.EXPECTED_PRESENTATION_COMMIT, source["packageAuthorityCommit"])
        self.assertEqual(self.module.EXPECTED_PRESENTATION_TREE, source["packageAuthorityTree"])
        release_source = (REPO / "scripts/verify_release_source_graph.py").read_text(encoding="utf-8")
        self.assertIn(f'PRESENTATION_SOURCE_COMMIT = "{self.module.EXPECTED_PRODUCTION_COMMIT}"', release_source)
        self.assertIn(f'PRESENTATION_SOURCE_TREE = "{self.module.EXPECTED_PRODUCTION_TREE}"', release_source)
        self.assertNotIn(self.module.EXPECTED_PRESENTATION_COMMIT, release_source)

    def test_preserves_exact_six_core_and_seven_owner_pin_graph(self) -> None:
        self.assertEqual(
            list(self.module.EXPECTED_CORE_IDS),
            [row["package_id"] for row in self.payload["packagePins"]],
        )
        self.assertEqual(
            list(self.module.EXPECTED_OWNER_IDS),
            [row["package_id"] for row in self.payload["ownerPackagePins"]],
        )
        expected = {row[0]: row for row in self.module.EXPECTED_PACKAGES}
        for row in [*self.payload["packagePins"], *self.payload["ownerPackagePins"]]:
            package = expected[row["package_id"]]
            self.assertEqual((package[1], package[2], package[3]), (row["version"], row["sha256"], row["size_bytes"]))

    def test_missing_extra_duplicate_reordered_and_misowned_pins_fail_closed(self) -> None:
        mutations: list[tuple[dict[str, object], str]] = []
        missing = copy.deepcopy(self.payload)
        missing["packagePins"].pop()
        mutations.append((missing, "exact ordered six"))
        extra = copy.deepcopy(self.payload)
        extra["ownerPackagePins"].append(copy.deepcopy(extra["ownerPackagePins"][-1]))
        mutations.append((extra, "exact ordered seven"))
        duplicate = copy.deepcopy(self.payload)
        duplicate["ownerPackagePins"][1] = copy.deepcopy(duplicate["ownerPackagePins"][0])
        mutations.append((duplicate, "exact ordered seven"))
        reordered = copy.deepcopy(self.payload)
        reordered["packagePins"][0], reordered["packagePins"][1] = reordered["packagePins"][1], reordered["packagePins"][0]
        mutations.append((reordered, "exact ordered six"))
        misowned = copy.deepcopy(self.payload)
        misowned["ownerPackagePins"][0]["owner"] = "registry"
        mutations.append((misowned, "owner is not exact"))
        for payload, message in mutations:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_copy(payload)

    def test_package_hash_version_size_and_receipt_tamper_fail_closed(self) -> None:
        mutations: list[tuple[dict[str, object], str]] = []
        for field, value in (("sha256", "0" * 64), ("version", "9.9.9"), ("size_bytes", 1)):
            payload = copy.deepcopy(self.payload)
            payload["packagePins"][0][field] = value
            mutations.append((payload, "package pin bytes are not exact"))
        receipt = copy.deepcopy(self.payload)
        receipt["verificationReceipt"]["sha256"] = "0" * 64
        mutations.append((receipt, "receipt binding is not exact"))
        authority = copy.deepcopy(self.payload)
        authority["authority"]["sha256"] = "0" * 64
        mutations.append((authority, "authority file binding is not exact"))
        for payload, message in mutations:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_copy(payload)

    def test_source_fallback_siblings_and_publication_fail_closed(self) -> None:
        mutations: list[tuple[dict[str, object], str]] = []
        for field, value in (
            ("packageOnly", False),
            ("restoreLockedMode", False),
            ("sourceCheckoutsPresent", True),
            ("siblingsAllowed", True),
        ):
            payload = copy.deepcopy(self.payload)
            payload["dependencyMode"][field] = value
            mutations.append((payload, "dependency mode"))
        publication = copy.deepcopy(self.payload)
        publication["publicationAuthorized"] = True
        mutations.append((publication, "cannot authorize publication"))
        for payload, message in mutations:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_copy(payload)

    def test_sdk_selection_separates_w41_producer_from_android_consumer_authority(self) -> None:
        sdk = self.payload["sdkAuthority"]
        self.assertEqual("10.0.103", sdk["packageProofSdkVersion"])
        self.assertEqual("10.0.110", sdk["androidGlobalPolicy"]["version"])
        self.assertEqual("latestPatch", sdk["androidGlobalPolicy"]["rollForward"])
        self.assertEqual("10.0.111", sdk["releaseWorkflow"]["dotnetVersion"])
        self.assertEqual("10.0.111", sdk["selectedAndroidConsumerSdkVersion"])
        self.module.validate_android_sdk_authority(REPO, self.module.validate_manifest(MANIFEST))
        for field, value in (
            ("packageProofSdkVersion", "10.0.111"),
            ("selectedAndroidConsumerSdkVersion", "10.0.103"),
        ):
            payload = copy.deepcopy(self.payload)
            payload["sdkAuthority"][field] = value
            with self.subTest(field=field):
                with self.assertRaisesRegex(ValueError, "SDK authority"):
                    self.validate_copy(payload)

    def test_stale_commit_tree_lock_and_production_source_tamper_fail_closed(self) -> None:
        mutations: list[tuple[dict[str, object], str]] = []
        for field in ("packageAuthorityCommit", "packageAuthorityTree", "productionCommit", "productionTree"):
            payload = copy.deepcopy(self.payload)
            payload["presentationSource"][field] = "0" * 40
            mutations.append((payload, "source and internal package authority pins"))
        lock = copy.deepcopy(self.payload)
        lock["lockFiles"][1]["sha256"] = "0" * 64
        mutations.append((lock, "exact five W4.1 locks"))
        consumer_lock = copy.deepcopy(self.payload)
        consumer_lock["androidConsumerLock"]["sha256"] = "0" * 64
        mutations.append((consumer_lock, "consumer lock binding is not exact"))
        for payload, message in mutations:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_copy(payload)

    def test_receipt_commands_require_bounded_locked_serialized_proof(self) -> None:
        restore = {
            "command": [
                "dotnet", "restore", "project.csproj", "--locked-mode",
                "--ignore-failed-sources", "-p:RestoreLockedMode=true",
                "-p:RestorePackagesWithLockFile=true",
                "-p:ChummerUseLocalCompatibilityTree=false",
                "-p:ChummerUseLockedOwnerContractPackages=true",
            ],
            "exitCode": 0, "outputSha256": "0" * 64, "outputTail": "",
        }
        build = {
            "command": ["dotnet", "build", "project.csproj", "--no-restore", "-m:1"],
            "exitCode": 0, "outputSha256": "0" * 64, "outputTail": "",
        }
        executable = {
            "command": ["test-binary"], "exitCode": 0,
            "outputSha256": "0" * 64, "outputTail": "",
        }
        commands = [copy.deepcopy(restore) for _ in range(5)] + [copy.deepcopy(build) for _ in range(5)] + [copy.deepcopy(executable) for _ in range(3)]
        self.module._validate_commands(commands)
        commands[0]["command"].remove("--locked-mode")
        with self.assertRaisesRegex(ValueError, "locked package-only"):
            self.module._validate_commands(commands)

    def test_exclusive_output_cannot_be_replaced(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "binding.json"
            binding = self.module.build_binding(self.module.validate_manifest(MANIFEST))
            self.module.write_exclusive(output, binding)
            self.assertEqual(0o600, output.stat().st_mode & 0o777)
            with self.assertRaises(FileExistsError):
                self.module.write_exclusive(output, binding)


if __name__ == "__main__":
    unittest.main()
