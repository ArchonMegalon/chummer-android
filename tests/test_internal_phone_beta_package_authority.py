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

        core_rows = [
            package_row(
                package_id,
                source_graph["coreRuntimeSourceCommit"],
                self.module.CORE_VERSION,
            )
            for package_id in (
                "Chummer.Engine.Contracts", "Chummer.Application",
                "Chummer.Rulesets.Hosting", "Chummer.Rulesets.Sr4",
                "Chummer.Rulesets.Sr5", "Chummer.Rulesets.Sr6",
                "Chummer.Infrastructure", "Chummer.Engine.GmCharacterEdits",
            )
        ]
        hub_rows = [
            package_row(package_id, source_graph["hubProducerCommit"], self.module.HUB_VERSION)
            for package_id in ("Chummer.Play.Contracts", "Chummer.Run.Contracts")
        ]
        hub_rows.extend(
            package_row(package_id, source_graph["registryCommit"], self.module.HUB_VERSION)
            for package_id in ("Chummer.Hub.Registry.Contracts", "Chummer.Run.Registry")
        )
        legacy_version = "0.0.0-packageplane.20260721.1"
        legacy_rows = [
            package_row(package_id, "4" * 40, legacy_version)
            for package_id in (
                "Chummer.Engine.Contracts", "Chummer.Hub.Registry.Contracts",
                "Chummer.Play.Contracts", "Chummer.Run.Contracts",
            )
        ]
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
                "packages": core_rows,
                "receiptContract": "core-receipt",
                "receiptFileName": "core-receipt.json",
                "receiptSha256": "a" * 64,
                "repository": "https://github.com/ArchonMegalon/chummer6-core.git",
                "runtimeSourceCommit": source_graph["coreRuntimeSourceCommit"],
            },
            "currentOwnerContractFeed": {
                "inventoryContract": "legacy-inventory",
                "inventoryFileName": "legacy-inventory.json",
                "inventorySha256": "5" * 64,
                "lockContract": "legacy-lock",
                "lockPath": "legacy-lock.json",
                "lockSha256": "6" * 64,
                "ownerDirectory": "legacy",
                "packageFeedInventorySha256": "7" * 64,
                "packageVersion": legacy_version,
                "packages": legacy_rows,
                "producerCommit": "8" * 40,
                "producerPath": "legacy-producer.py",
                "producerRepository": "https://github.com/ArchonMegalon/chummer6-core.git",
                "producerSha256": "9" * 64,
                "selectedForCoreRuntimeCompatibility": True,
            },
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
            "currentOwnerContractFeed": receipt_feed(
                package_authority["currentOwnerContractFeed"],
                compatibilityPurpose="exact-core-runtime-transitive-dependencies",
                materializedFeedValidated=True,
                packageCount=len(package_authority["currentOwnerContractFeed"]["packages"]),
                selectedForCanonicalFullFeed=True,
                selectedForCoreRuntimeCompatibility=True,
                status="passed",
            ),
            "uiOwnerFeed": receipt_feed(
                package_authority["uiOwnerFeed"],
                packageCount=len(package_authority["uiOwnerFeed"]["packages"]),
                status="passed",
            ),
        }
        receipt["canonicalOwnerFeed"].pop("receiptContract")
        receipt["canonicalOwnerFeed"].pop("receiptSha256")
        receipt["packageInventory"] = sorted(
            [
                copy.deepcopy(row)
                for field in (
                    "coreRuntimeFeed", "canonicalOwnerFeed",
                    "currentOwnerContractFeed", "uiOwnerFeed",
                )
                for row in receipt[field]["packages"]
            ],
            key=lambda row: row["fileName"],
        )
        return package_authority, receipt

    def current_main_receipt_fixture(self) -> dict[str, object]:
        _, authority_receipt = self.bound_authority_fixture()
        receipt = {key: None for key in self.module.RECEIPT_TOP_LEVEL_KEYS}
        receipt.update(authority_receipt)
        receipt.update({
            "contractName": self.module.RECEIPT_CONTRACT,
            "contractVersion": 11,
            "status": "passed",
            "mode": "integration",
            "consumerCommit": self.module.EXPECTED_PRESENTATION_COMMIT,
            "localCompatibilityTree": False,
            "packageCacheWasFresh": True,
            "consumerPackagePlaneLock": {
                "path": self.module.EXPECTED_LOCK_PATH,
                "sha256": self.module.EXPECTED_LOCK_SHA256,
                "sizeBytes": self.module.EXPECTED_LOCK_SIZE,
            },
            "ownerPackageArtifactCache": {
                "coldProducerFallbackOnCacheMiss": True,
                "contract": self.module.CACHE_CONTRACT,
                "status": "not_supplied",
                "used": False,
            },
        })
        return receipt

    def validate_receipt_copy(self, payload: dict[str, object]) -> dict[str, object]:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary).resolve() / "current-main-receipt.json"
            write_private(path, payload)
            with (
                patch.object(self.module, "EXPECTED_RECEIPT_SIZE", path.stat().st_size),
                patch.object(self.module, "EXPECTED_RECEIPT_SHA256", self.module.sha256(path)),
            ):
                return self.module.validate_receipt(path)

    def retained_cache_fixture(self, receipt: dict[str, object]) -> dict[str, object]:
        packages = []
        for index, row in enumerate(receipt["packageInventory"]):
            packages.append({
                "commit": f"{index:040x}",
                "fileName": row["fileName"],
                "packageId": f"Package.{index}",
                "plane": "test-owner",
                "repository": "https://github.com/ArchonMegalon/example.git",
                "sha256": row["sha256"],
                "sizeBytes": row["sizeBytes"],
                "version": "1.0.0",
            })
        return {
            "authorities": {},
            "authorityArtifacts": [{"fileName": "authority.json", "sha256": "a" * 64}],
            "cacheKey": self.module.EXPECTED_CACHE_KEY,
            "contract": self.module.CACHE_CONTRACT,
            "packages": packages,
        }

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
        runtime_hub = self.module.validate_runtime_hub_source_checkout(
            REPO / self.module.RUNTIME_SOURCE_WORKFLOW_PATH
        )
        self.assertEqual(
            self.module.EXPECTED_RUNTIME_HUB_COMMIT,
            runtime_hub["runtimeSourceCommit"],
        )
        self.assertEqual(
            self.module.EXPECTED_SOURCE_GRAPH["hubProducerCommit"],
            runtime_hub["packageProducerCommit"],
        )
        self.assertNotEqual(
            runtime_hub["runtimeSourceCommit"],
            runtime_hub["packageProducerCommit"],
        )

    def test_runtime_hub_checkout_rejects_package_producer_and_duplicate_source(self) -> None:
        canonical = (
            REPO / self.module.RUNTIME_SOURCE_WORKFLOW_PATH
        ).read_text(encoding="utf-8")
        repository_line = "repository: ArchonMegalon/chummer6-hub"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            producer_as_runtime = root / "producer-as-runtime.yml"
            producer_as_runtime.write_text(
                canonical.replace(
                    f"ref: {self.module.EXPECTED_RUNTIME_HUB_COMMIT}",
                    f"ref: {self.module.EXPECTED_SOURCE_GRAPH['hubProducerCommit']}",
                    1,
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "runtime Hub checkout commit drifted"):
                self.module.validate_runtime_hub_source_checkout(producer_as_runtime)

            duplicate = root / "duplicate-runtime-hub.yml"
            duplicate.write_text(
                canonical + "\n  repository: ArchonMegalon/chummer6-hub\n"
                + f"  ref: {self.module.EXPECTED_RUNTIME_HUB_COMMIT}\n",
                encoding="utf-8",
            )
            self.assertEqual(
                1,
                sum(
                    line.strip() == repository_line
                    for line in canonical.splitlines()
                ),
            )
            with self.assertRaisesRegex(ValueError, "must occur exactly once"):
                self.module.validate_runtime_hub_source_checkout(duplicate)

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

    def test_exact_final_current_main_receipt_non_use_posture_is_accepted(self) -> None:
        receipt = self.current_main_receipt_fixture()
        validated = self.validate_receipt_copy(receipt)
        self.assertEqual(
            {
                "coldProducerFallbackOnCacheMiss": True,
                "contract": self.module.CACHE_CONTRACT,
                "status": "not_supplied",
                "used": False,
            },
            validated["ownerPackageArtifactCache"],
        )

    def test_current_main_receipt_cache_status_and_used_combinations_fail_closed(self) -> None:
        mutations = (
            {"status": "not_supplied", "used": True},
            {"status": "passed", "used": False},
            {"status": "passed", "used": True},
            {"status": "not_supplied"},
        )
        for cache in mutations:
            payload = self.current_main_receipt_fixture()
            payload["ownerPackageArtifactCache"] = {
                "coldProducerFallbackOnCacheMiss": True,
                "contract": self.module.CACHE_CONTRACT,
                **cache,
            }
            with self.subTest(cache=cache):
                with self.assertRaisesRegex(ValueError, "cache non-use posture is not exact"):
                    self.validate_receipt_copy(payload)

    def test_final_receipt_and_retained_cache_are_byte_equivalent(self) -> None:
        receipt = self.current_main_receipt_fixture()
        cache = self.retained_cache_fixture(receipt)
        self.module.validate_receipt_cache_equivalence(receipt, cache)

    def test_final_receipt_and_retained_cache_divergence_fails_closed(self) -> None:
        base_receipt = self.current_main_receipt_fixture()
        base_cache = self.retained_cache_fixture(base_receipt)

        inventory_tamper = copy.deepcopy(base_receipt)
        inventory_tamper["packageInventory"][0]["sha256"] = "b" * 64
        owner_feed_tamper = copy.deepcopy(base_receipt)
        owner_feed_tamper["coreRuntimeFeed"]["packages"][0]["sizeBytes"] += 1
        cache_tamper = copy.deepcopy(base_cache)
        cache_tamper["packages"][0]["sha256"] = "c" * 64
        for receipt, cache, message in (
            (inventory_tamper, base_cache, "package inventory diverges"),
            (owner_feed_tamper, base_cache, "owner feeds diverge"),
            (base_receipt, cache_tamper, "owner feeds diverge"),
        ):
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    self.module.validate_receipt_cache_equivalence(receipt, cache)

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
