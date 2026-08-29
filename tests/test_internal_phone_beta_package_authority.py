from __future__ import annotations

import copy
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


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

    def validate_presentation_checkout(
        self,
        *,
        origin: str,
        commit: str | None = None,
        tree: str | None = None,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            authority = root / self.module.EXPECTED_LOCK_PATH
            authority.parent.mkdir(parents=True)
            authority.write_bytes(b"x" * self.module.EXPECTED_LOCK_SIZE)
            answers = {
                ("status", "--porcelain", "--untracked-files=all"): "",
                ("rev-parse", "HEAD"): commit or self.module.EXPECTED_PRESENTATION_COMMIT,
                ("rev-parse", "HEAD^{tree}"): tree or self.module.EXPECTED_PRESENTATION_TREE,
                ("remote", "get-url", "origin"): origin,
                (
                    "rev-parse",
                    f"HEAD:{self.module.EXPECTED_LOCK_PATH}",
                ): self.module.EXPECTED_LOCK_BLOB,
            }

            def fake_git(_root: Path, *arguments: str) -> str:
                return answers[arguments]

            with (
                patch.object(self.module, "git", side_effect=fake_git),
                patch.object(
                    self.module,
                    "sha256",
                    return_value=self.module.EXPECTED_LOCK_SHA256,
                ),
                patch.object(
                    self.module,
                    "validate_package_plane_lock",
                    return_value={},
                ),
                patch.object(self.module, "strict_json", return_value={}),
            ):
                self.module.validate_presentation_repository(root)

    def bound_authority_fixture(self) -> tuple[dict[str, object], dict[str, object]]:
        source_graph = self.module.EXPECTED_SOURCE_GRAPH

        def package_row(package_id: str, commit: str, version: str) -> dict[str, object]:
            return {
                "commit": commit,
                "fileName": f"{package_id}.{version}.nupkg",
                "packageId": package_id,
                "project": f"{package_id}/{package_id}.csproj",
                "repository": "https://github.com/ArchonMegalon/example.git",
                "sha256": "1" * 64,
                "sizeBytes": 100,
                "version": version,
            }

        core_row = package_row(
            "Chummer.Application",
            source_graph["coreRuntimeSourceCommit"],
            self.module.CORE_VERSION,
        )
        hub_rows = [
            package_row(package_id, source_graph["hubProducerCommit"], self.module.HUB_VERSION)
            for package_id in ("Chummer.Play.Contracts", "Chummer.Run.Contracts")
        ]
        hub_rows.extend(
            package_row(package_id, source_graph["registryCommit"], self.module.HUB_VERSION)
            for package_id in ("Chummer.Hub.Registry.Contracts", "Chummer.Run.Registry")
        )
        ui_kit_row = package_row(
            "Chummer.Ui.Kit",
            source_graph["uiKitCommit"],
            self.module.UI_KIT_VERSION,
        )
        campaign_row = package_row(
            "Chummer.Campaign.Contracts",
            source_graph["hubProducerCommit"],
            self.module.CAMPAIGN_VERSION,
        )
        for row in (ui_kit_row, campaign_row):
            row.update({
                "ownerDirectory": "owner",
                "projectSha256": "2" * 64,
                "sourceTree": "3" * 40,
            })

        lock = {
            "approvedPackageSources": [],
            "canonicalOwnerFeed": {
                "inventoryContract": "hub-inventory",
                "inventoryFileName": "hub-inventory.json",
                "inventorySha256": "4" * 64,
                "lockContract": "hub-lock",
                "lockPath": "hub-lock.json",
                "lockSha256": "5" * 64,
                "packageVersion": self.module.HUB_VERSION,
                "packages": hub_rows,
                "producerCommit": source_graph["hubProducerCommit"],
                "producerDirectory": "hub",
                "producerPath": "producer.py",
                "producerRepository": "https://github.com/ArchonMegalon/chummer6-hub.git",
                "producerSha256": "6" * 64,
                "receiptContract": "hub-receipt",
                "receiptFileName": "hub-receipt.json",
                "receiptSha256": "7" * 64,
            },
            "consumer": {},
            "contractName": self.module.LOCK_CONTRACT,
            "contractVersion": 11,
            "coreRuntimeFeed": {
                "inventoryContract": "core-inventory",
                "inventoryFileName": "core-inventory.json",
                "inventorySha256": "8" * 64,
                "lockContract": "core-lock",
                "lockFileName": "core-lock.json",
                "lockSha256": "9" * 64,
                "packageRecipeCommit": source_graph["corePackageRecipeCommit"],
                "packageVersion": self.module.CORE_VERSION,
                "packages": [core_row],
                "receiptContract": "core-receipt",
                "receiptFileName": "core-receipt.json",
                "receiptSha256": "a" * 64,
                "repository": "https://github.com/ArchonMegalon/chummer6-core.git",
                "runtimeSourceCommit": source_graph["coreRuntimeSourceCommit"],
            },
            "currentOwnerContractFeed": {},
            "externalPackages": [],
            "owners": [],
            "packages": [],
            "sdkArchive": {
                "fileName": "dotnet-sdk.tar.gz",
                "rid": "linux-x64",
                "sha512": "b" * 128,
                "source": "https://example.invalid/dotnet-sdk.tar.gz",
                "version": "10.0.103",
            },
            "sdkVersion": "10.0.103",
            "uiOwnerFeed": {
                "dependencyAuthorityCacheKey": "c" * 64,
                "inventoryContract": "ui-inventory",
                "inventoryFileName": "ui-inventory.json",
                "inventorySha256": "d" * 64,
                "packageRecipeCommit": self.module.EXPECTED_PRESENTATION_COMMIT,
                "packageRecipeSha256": "e" * 64,
                "packages": [campaign_row, ui_kit_row],
                "producerLockFileName": "ui-owner-lock.json",
                "producerLockPath": "config/ui-owner-lock.json",
                "producerLockSha256": "f" * 64,
                "receiptContract": "ui-receipt",
                "receiptFileName": "ui-receipt.json",
                "receiptSha256": "0" * 64,
                "sdkVersion": "10.0.103",
            },
        }
        package_authority = self.module.validate_package_plane_lock(lock)

        def receipt_feed(projection: dict[str, object], **extra: object) -> dict[str, object]:
            return {**copy.deepcopy(projection), **extra}

        receipt = {
            "sdkVersion": package_authority["packageProofSdkVersion"],
            "coreRuntimeFeed": receipt_feed(
                package_authority["coreRuntimeFeed"],
                packageCount=len(package_authority["coreRuntimeFeed"]["packages"]),
                selectedForCanonicalFullFeed=True,
                status="passed",
            ),
            "canonicalOwnerFeed": receipt_feed(
                package_authority["canonicalOwnerFeed"],
                packageCount=len(package_authority["canonicalOwnerFeed"]["packages"]),
                projectLockFilesEnforced=True,
                status="passed",
            ),
            "uiOwnerFeed": receipt_feed(
                package_authority["uiOwnerFeed"],
                packageCount=len(package_authority["uiOwnerFeed"]["packages"]),
                status="passed",
            ),
        }
        return package_authority, receipt

    def test_exact_current_graph_is_valid_and_never_public_ready(self) -> None:
        validated = self.module.validate_manifest(MANIFEST)
        binding = self.module.build_binding(validated)
        self.assertEqual(self.module.CONTRACT, binding["contractName"])
        self.assertEqual("current_graph_verified", binding["authorityState"])
        self.assertFalse(binding["publicationAuthorized"])
        self.assertIn("public_release_readiness", binding["doesNotAssert"])
        self.assertEqual(18, binding["artifactCache"]["packageCount"])

    def test_exact_core_hub_ui_and_cache_authorities_are_bound(self) -> None:
        self.assertEqual(self.module.EXPECTED_PRESENTATION_COMMIT, self.payload["presentationSource"]["commit"])
        self.assertEqual(self.module.EXPECTED_PRESENTATION_TREE, self.payload["presentationSource"]["tree"])
        self.assertEqual(self.module.EXPECTED_SOURCE_GRAPH, self.payload["sourceGraph"])
        self.assertEqual(self.module.EXPECTED_CACHE_KEY, self.payload["artifactCache"]["cacheKey"])
        self.assertEqual(self.module.EXPECTED_CACHE_MANIFEST_SHA256, self.payload["artifactCache"]["manifestSha256"])
        workflow = (REPO / ".github/workflows/api36-editing-e2e.yml").read_text(encoding="utf-8")
        self.assertIn(self.module.EXPECTED_PRESENTATION_COMMIT, workflow)
        self.assertIn(self.module.EXPECTED_SOURCE_GRAPH["coreRuntimeSourceCommit"], workflow)
        self.assertIn(self.module.EXPECTED_SOURCE_GRAPH["hubProducerCommit"], workflow)

    def test_canonical_presentation_origin_is_accepted(self) -> None:
        self.validate_presentation_checkout(
            origin=self.module.EXPECTED_PRESENTATION_REPOSITORY,
        )

    def test_ui_kit_compatibility_origin_is_not_presentation_authority(self) -> None:
        self.assertIn("uiKitCommit", self.payload["sourceGraph"])
        self.assertNotIn("compatibilityCheckoutRepository", self.payload["presentationSource"])
        with self.assertRaisesRegex(ValueError, "repository authority drifted"):
            self.validate_presentation_checkout(
                origin="https://github.com/ArchonMegalon/chummer6-ui-kit.git",
            )

    def test_fork_origin_is_not_presentation_authority(self) -> None:
        with self.assertRaisesRegex(ValueError, "repository authority drifted"):
            self.validate_presentation_checkout(
                origin="https://github.com/example/chummer6-ui.git",
            )

    def test_presentation_commit_and_tree_remain_exact(self) -> None:
        with self.assertRaisesRegex(ValueError, "commit drifted"):
            self.validate_presentation_checkout(
                origin=self.module.EXPECTED_PRESENTATION_REPOSITORY,
                commit="0" * 40,
            )
        with self.assertRaisesRegex(ValueError, "tree drifted"):
            self.validate_presentation_checkout(
                origin=self.module.EXPECTED_PRESENTATION_REPOSITORY,
                tree="0" * 40,
            )

    def test_current_receipt_lock_cache_and_source_tamper_fail_closed(self) -> None:
        mutations: list[tuple[dict[str, object], str]] = []
        for section, field, value, message in (
            ("presentationSource", "commit", "0" * 40, "Presentation current graph"),
            ("packagePlaneLock", "sha256", "0" * 64, "package-plane lock"),
            ("verificationReceipt", "sha256", "0" * 64, "verification receipt"),
            ("artifactCache", "cacheKey", "0" * 64, "artifact-cache"),
            ("sourceGraph", "coreRuntimeSourceCommit", "0" * 40, "source graph"),
        ):
            payload = copy.deepcopy(self.payload)
            payload[section][field] = value
            mutations.append((payload, message))
        for payload, message in mutations:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_copy(payload)

    def test_bound_ui_lock_schema_and_duplicate_json_fail_closed(self) -> None:
        package_authority, _ = self.bound_authority_fixture()
        self.assertEqual(self.module.EXPECTED_SOURCE_GRAPH, package_authority["sourceGraph"])

        with tempfile.TemporaryDirectory() as temporary:
            duplicate = Path(temporary) / "duplicate-lock.json"
            duplicate.write_text(
                '{"contractName":"first","contractName":"second"}\n',
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "duplicate key"):
                self.module.strict_json(duplicate, "Presentation package-plane lock")

        lock = {
            key: None
            for key in self.module.LOCK_TOP_LEVEL_KEYS
        }
        lock.update({"contractName": self.module.LOCK_CONTRACT, "contractVersion": 11})
        lock["unexpectedAuthority"] = "must-fail"
        with self.assertRaisesRegex(ValueError, "schema is not exact"):
            self.module.validate_package_plane_lock(lock)

    def test_coordinated_source_graph_and_receipt_tamper_fail_closed(self) -> None:
        package_authority, receipt = self.bound_authority_fixture()
        manifest = copy.deepcopy(self.payload)
        tampered_commit = "f" * 40
        manifest["sourceGraph"]["coreRuntimeSourceCommit"] = tampered_commit
        receipt["coreRuntimeFeed"]["runtimeSourceCommit"] = tampered_commit

        with self.assertRaisesRegex(ValueError, "not derived from the bound UI package lock"):
            self.module.validate_bound_authority_claims(
                manifest,
                package_authority,
                receipt,
            )

    def test_coordinated_sdk_manifest_and_receipt_tamper_fail_closed(self) -> None:
        package_authority, receipt = self.bound_authority_fixture()
        manifest = copy.deepcopy(self.payload)
        manifest["sdkAuthority"]["packageProofSdkVersion"] = "10.0.999"
        receipt["sdkVersion"] = "10.0.999"
        receipt["uiOwnerFeed"]["sdkVersion"] = "10.0.999"

        with self.assertRaisesRegex(ValueError, "not derived from the bound UI package lock"):
            self.module.validate_bound_authority_claims(
                manifest,
                package_authority,
                receipt,
            )

    def test_bound_receipt_feed_tamper_fails_closed_even_when_manifest_is_unchanged(self) -> None:
        package_authority, receipt = self.bound_authority_fixture()
        receipt["canonicalOwnerFeed"]["producerCommit"] = "e" * 40

        with self.assertRaisesRegex(ValueError, "receipt Hub authority disagrees"):
            self.module.validate_bound_authority_claims(
                self.payload,
                package_authority,
                receipt,
            )

    def test_lock_missing_extra_reordered_and_byte_tamper_fail_closed(self) -> None:
        mutations: list[tuple[dict[str, object], str]] = []
        missing = copy.deepcopy(self.payload)
        missing["androidConsumerLocks"].pop()
        mutations.append((missing, "consumer lock"))
        extra = copy.deepcopy(self.payload)
        extra["androidConsumerLocks"].append(copy.deepcopy(extra["androidConsumerLocks"][-1]))
        mutations.append((extra, "consumer lock"))
        reordered = copy.deepcopy(self.payload)
        reordered["androidConsumerLocks"].reverse()
        mutations.append((reordered, "consumer lock"))
        digest = copy.deepcopy(self.payload)
        digest["androidConsumerLocks"][0]["sha256"] = "0" * 64
        mutations.append((digest, "consumer lock"))
        for payload, message in mutations:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    self.validate_copy(payload)

    def test_source_fallback_and_publication_fail_closed(self) -> None:
        for field, value in (
            ("packageOnly", False),
            ("restoreLockedMode", False),
            ("sourceCheckoutsPresent", True),
            ("siblingsAllowed", True),
        ):
            payload = copy.deepcopy(self.payload)
            payload["dependencyMode"][field] = value
            with self.subTest(field=field):
                with self.assertRaisesRegex(ValueError, "dependency mode"):
                    self.validate_copy(payload)
        publication = copy.deepcopy(self.payload)
        publication["publicationAuthorized"] = True
        with self.assertRaisesRegex(ValueError, "cannot authorize publication"):
            self.validate_copy(publication)

    def test_android_sdk_and_both_consumer_locks_are_current(self) -> None:
        manifest = self.module.validate_manifest(MANIFEST)
        self.module.validate_android_sdk_authority(REPO, manifest)
        self.assertEqual(
            [row[1] for row in self.module.EXPECTED_ANDROID_LOCKS],
            [row["path"] for row in self.payload["androidConsumerLocks"]],
        )
        self.assertEqual("10.0.103", self.payload["sdkAuthority"]["packageProofSdkVersion"])
        self.assertEqual("10.0.111", self.payload["sdkAuthority"]["selectedAndroidConsumerSdkVersion"])

    def test_compile_closure_uses_current_versions_only(self) -> None:
        self.assertEqual(12, len(self.module.EXPECTED_COMPILE_PACKAGES))
        self.assertEqual(
            self.module.CORE_VERSION,
            self.module.EXPECTED_COMPILE_PACKAGES["Chummer.Application"],
        )
        self.assertEqual(
            self.module.HUB_VERSION,
            self.module.EXPECTED_COMPILE_PACKAGES["Chummer.Run.Contracts"],
        )
        self.assertNotIn("Chummer.Run.Hub", self.module.EXPECTED_COMPILE_PACKAGES)

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
