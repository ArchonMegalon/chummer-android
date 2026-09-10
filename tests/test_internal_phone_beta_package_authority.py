from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import os
import tempfile
import unittest
from contextlib import contextmanager
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
        # The copied-cache producer emits the complete Hub authority binding,
        # including the authenticated receipt contract and digest.
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
        """Synthetic next-schema receipt, not historical or runtime evidence."""
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
            "stubPackagesAllowed": False,
            "consumerPackagePlaneLock": {
                "path": self.module.EXPECTED_LOCK_PATH,
                "sha256": self.module.EXPECTED_LOCK_SHA256,
                "sizeBytes": self.module.EXPECTED_LOCK_SIZE,
            },
            "ownerPackageArtifactCache": {
                "authorityArtifacts": [
                    {
                        "path": f"authority/{name}",
                        "sha256": hashlib.sha256(name.encode()).hexdigest(),
                        "sizeBytes": len(name.encode()),
                    }
                    for name in (
                        "core-inventory.json", "core-lock.json", "core-receipt.json",
                        "hub-inventory.json", "hub-lock.json", "hub-producer.py",
                        "hub-receipt.json", "legacy-inventory.json", "legacy-lock.json",
                        "legacy-producer.py", "ui-owner-package-plane.lock.json",
                        "ui-owner-packages.inventory.json", "ui-owner-packages.receipt.json",
                    )
                ],
                "cacheKey": self.module.EXPECTED_CACHE_KEY,
                "coldProducerFallbackOnCacheMiss": True,
                "contract": self.module.CACHE_CONTRACT,
                "importedByCopy": True,
                "manifest": {
                    "path": "owner-package-cache.json",
                    "sha256": self.module.EXPECTED_CACHE_MANIFEST_SHA256,
                    "sizeBytes": self.module.EXPECTED_CACHE_MANIFEST_SIZE,
                },
                "packageCount": 18,
                "packages": [
                    {
                        "path": f"packages/{row['fileName']}",
                        "sha256": row["sha256"],
                        "sizeBytes": row["sizeBytes"],
                    }
                    for row in receipt["packageInventory"]
                ],
                # Provenance is deliberately unavailable locally: never follow it.
                "sourcePath": "/provenance-only/absent/cold-owner-cache",
                "status": "passed",
                "used": True,
            },
        })
        self.add_synthetic_owner_test_executions(receipt)
        return receipt

    def add_synthetic_owner_test_executions(self, receipt: dict[str, object]) -> None:
        project = "Chummer.Product.UnitTests/Chummer.Product.UnitTests.csproj"
        assembly = {
            "path": "Chummer.Product.UnitTests/bin/Release/net10.0/Chummer.Product.UnitTests.dll",
            "sha256": hashlib.sha256(b"synthetic Product assembly, not compiled").hexdigest(),
            "sizeBytes": 12345,
        }
        content = {
            "repository": "https://github.com/ArchonMegalon/chummer6-core.git",
            "checkoutCommit": self.module.EXPECTED_SOURCE_GRAPH["corePackageRecipeCommit"],
            "runtimeSourceCommit": self.module.EXPECTED_SOURCE_GRAPH["coreRuntimeSourceCommit"],
            "packageRecipeCommit": self.module.EXPECTED_SOURCE_GRAPH["corePackageRecipeCommit"],
            "sourceRoot": "/provenance-only/absent/core-rule-data",
            "usage": "read-only-rule-data-not-project-reference",
            "contentDirectories": ["Chummer/data", "Chummer/lang", "Chummer/customdata"],
            "fileCount": 123,
            "contentInventorySha256": hashlib.sha256(b"synthetic rule data inventory").hexdigest(),
        }
        receipt["testProjects"] = [project]
        receipt["testExecutions"] = [{
            "coreProjectionContent": copy.deepcopy(content),
            "buildInParallel": False,
            "compileRunner": "serialized-package-plane-build",
            "disableBuildServers": True,
            "maxCpuCount": 1,
            "minimumExpectedTests": 747,
            "project": project,
            "runner": "direct-exact-assembly",
            "sdkVersion": receipt["sdkVersion"],
            "testAssembly": copy.deepcopy(assembly),
            "useSharedCompilation": False,
        }]
        specs = (
            ("InProcessWorkspaceContinuationTests", "Chummer.Tests/InProcessWorkspaceContinuationTests.cs", 19),
            ("InProcessShellOwnerContextTests", "Chummer.Tests/InProcessShellOwnerContextTests.cs", 26),
            ("InProcessChummerClientRulesetPluginTests", "Chummer.Tests/InProcessChummerClientRulesetPluginTests.cs", 74),
            ("ShellBootstrapDataProviderTests", "Chummer.Tests/Presentation/ShellBootstrapDataProviderTests.cs", 24),
            ("ShellPresenterTests", "Chummer.Tests/Presentation/ShellPresenterTests.cs", 80),
            ("WorkspaceSessionActivationServiceTests", "Chummer.Tests/Presentation/WorkspaceSessionActivationServiceTests.cs", 5),
            ("WorkspaceSessionPresenterTests", "Chummer.Tests/Presentation/WorkspaceSessionPresenterTests.cs", 23),
            ("WorkspaceViewStateStoreTests", "Chummer.Tests/Presentation/WorkspaceViewStateStoreTests.cs", 6),
            ("RestartSafeWorkspacePersistenceTests", "Chummer.Tests/RestartSafeWorkspacePersistenceTests.cs", 1),
        )
        rows = [{
            "coreProjectionContent": copy.deepcopy(content),
            "filter": f"FullyQualifiedName~{test_class}",
            "minimumExpectedTests": minimum,
            "project": project,
            "reuseFullSuiteBuild": True,
            "runner": "direct-exact-assembly",
            "sdkVersion": receipt["sdkVersion"],
            "sourceFiles": [source_file],
            "testAssembly": copy.deepcopy(assembly),
        } for test_class, source_file, minimum in specs]
        receipt["focusedContinuationTestExecution"] = rows[0]
        receipt["focusedOwnerShellTestExecution"] = rows[1]
        receipt["focusedExistingOwnerRegressionTestExecutions"] = rows[2:]
        receipt["sourceInventory"] = [{
            "path": path,
            "sha256": hashlib.sha256(f"synthetic source:{path}".encode()).hexdigest(),
            "sizeBytes": len(path),
        } for path in sorted([project, *(spec[1] for spec in specs)])]

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
            "authorityArtifacts": [
                {"fileName": row["path"].removeprefix("authority/"), "sha256": row["sha256"]}
                for row in receipt["ownerPackageArtifactCache"]["authorityArtifacts"]
            ],
            "cacheKey": self.module.EXPECTED_CACHE_KEY,
            "contract": self.module.CACHE_CONTRACT,
            "packages": packages,
        }

    @contextmanager
    def materialized_cache_fixture(self):
        """Synthetic unit-test files, never a retained production receipt."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve() / "cache"
            feed = root / "packages"
            feed.mkdir(parents=True)
            (root / "authority").mkdir()
            receipt = self.current_main_receipt_fixture()
            for row in receipt["packageInventory"]:
                raw = f"fixture-package:{row['fileName']}".encode()
                (feed / row["fileName"]).write_bytes(raw)
                row.update(sha256=hashlib.sha256(raw).hexdigest(), sizeBytes=len(raw))
            packages = {row["fileName"]: row for row in receipt["packageInventory"]}
            for field in ("coreRuntimeFeed", "canonicalOwnerFeed", "currentOwnerContractFeed", "uiOwnerFeed"):
                for row in receipt[field]["packages"]:
                    row.update(packages[row["fileName"]])
            copied = receipt["ownerPackageArtifactCache"]
            copied["sourcePath"] = str(root / "provenance-only-not-created")
            copied["packages"] = [
                {"path": f"packages/{row['fileName']}", "sha256": row["sha256"], "sizeBytes": row["sizeBytes"]}
                for row in receipt["packageInventory"]
            ]
            for row in copied["authorityArtifacts"]:
                name = row["path"].removeprefix("authority/")
                (root / row["path"]).write_bytes(name.encode())
            cache = self.retained_cache_fixture(receipt)
            manifest = root / "owner-package-cache.json"
            write_private(manifest, cache)
            copied["manifest"].update(sha256=self.module.sha256(manifest), sizeBytes=manifest.stat().st_size)
            with (
                patch.object(self.module, "EXPECTED_CACHE_MANIFEST_SHA256", self.module.sha256(manifest)),
                patch.object(self.module, "EXPECTED_CACHE_MANIFEST_SIZE", manifest.stat().st_size),
            ):
                yield receipt, cache, feed

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
        self.assertEqual({"runtimeSourceCommit", "packageProducerCommit"}, set(runtime_hub))

    def test_independently_pinned_runtime_and_producer_can_share_a_commit(self) -> None:
        shared_commit = self.module.EXPECTED_RUNTIME_HUB_COMMIT
        with patch.dict(self.module.EXPECTED_SOURCE_GRAPH, hubProducerCommit=shared_commit):
            binding = self.module.validate_runtime_hub_source_checkout(
                REPO / self.module.RUNTIME_SOURCE_WORKFLOW_PATH
            )
        self.assertEqual(
            {"runtimeSourceCommit": shared_commit, "packageProducerCommit": shared_commit},
            binding,
        )

    def test_runtime_hub_checkout_rejects_producer_substitution_and_duplicate_source(self) -> None:
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
                    f"ref: {'e' * 40}",
                    1,
                ),
                encoding="utf-8",
            )
            with patch.dict(self.module.EXPECTED_SOURCE_GRAPH, hubProducerCommit="e" * 40):
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

            missing = root / "missing-runtime-hub.yml"
            missing.write_text(canonical.replace(repository_line, "repository: unrelated"), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "must occur exactly once"):
                self.module.validate_runtime_hub_source_checkout(missing)

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

    def test_warm_hub_receipt_binds_complete_authenticated_projection(self) -> None:
        package_authority, receipt = self.bound_authority_fixture()
        self.module.validate_bound_authority_claims(self.payload, package_authority, receipt)
        for field in ("receiptContract", "receiptSha256"):
            self.assertEqual(package_authority["canonicalOwnerFeed"][field], receipt["canonicalOwnerFeed"][field])

    def test_warm_hub_receipt_rejects_cold_missing_extra_or_substituted_authority(self) -> None:
        package_authority, original = self.bound_authority_fixture()
        for field in original["canonicalOwnerFeed"]:
            receipt = copy.deepcopy(original)
            receipt["canonicalOwnerFeed"].pop(field)
            with self.subTest(missing=field), self.assertRaisesRegex(ValueError, "Hub feed schema is not exact"):
                self.module.validate_bound_authority_claims(self.payload, package_authority, receipt)
        for mutation in ("cold", "extra"):
            receipt = copy.deepcopy(original)
            if mutation == "cold":
                receipt["canonicalOwnerFeed"].pop("receiptContract")
                receipt["canonicalOwnerFeed"].pop("receiptSha256")
            else:
                receipt["canonicalOwnerFeed"]["unboundAuthority"] = "not-authorized"
            with self.subTest(mutation=mutation), self.assertRaisesRegex(ValueError, "Hub feed schema is not exact"):
                self.module.validate_bound_authority_claims(self.payload, package_authority, receipt)
        for field in ("receiptContract", "receiptSha256"):
            for value in ("substituted-contract", "0" * 64, "", None, False, 1, [], {}):
                receipt = copy.deepcopy(original)
                receipt["canonicalOwnerFeed"][field] = value
                with self.subTest(field=field, value=value), self.assertRaisesRegex(ValueError, "receipt Hub authority disagrees"):
                    self.module.validate_bound_authority_claims(self.payload, package_authority, receipt)

    def test_exact_copied_cache_receipt_posture_is_accepted(self) -> None:
        receipt = self.current_main_receipt_fixture()
        validated = self.validate_receipt_copy(receipt)
        self.assertEqual(receipt["ownerPackageArtifactCache"], validated["ownerPackageArtifactCache"])
        self.assertTrue(validated["ownerPackageArtifactCache"]["used"])
        self.assertTrue(validated["ownerPackageArtifactCache"]["importedByCopy"])

    def owner_execution_rows(self, receipt):
        return [receipt["focusedContinuationTestExecution"], receipt["focusedOwnerShellTestExecution"],
                *receipt["focusedExistingOwnerRegressionTestExecutions"]]

    def test_new_owner_receipt_invocations_bind_exact_shared_assembly_and_data(self) -> None:
        receipt = self.current_main_receipt_fixture()
        original = copy.deepcopy(receipt)
        self.assertEqual(receipt, self.validate_receipt_copy(receipt))
        self.assertEqual(original, receipt)
        self.assertEqual(747, receipt["testExecutions"][0]["minimumExpectedTests"])
        self.assertEqual([19, 26, 74, 24, 80, 5, 23, 6, 1],
                         [row["minimumExpectedTests"] for row in self.owner_execution_rows(receipt)])
        self.assertFalse(Path(receipt["testExecutions"][0]["coreProjectionContent"]["sourceRoot"]).exists())
        # The producer can validate the content checkout at either exact commit.
        for row in [*receipt["testExecutions"], *self.owner_execution_rows(receipt)]:
            row["coreProjectionContent"]["checkoutCommit"] = self.module.EXPECTED_SOURCE_GRAPH["coreRuntimeSourceCommit"]
        self.validate_receipt_copy(receipt)

    def test_new_owner_receipt_fields_are_required_and_unknown_fields_stay_denied(self) -> None:
        for field in ("focusedContinuationTestExecution", "focusedOwnerShellTestExecution",
                      "focusedExistingOwnerRegressionTestExecutions"):
            for change in ("missing", "null", "empty"):
                receipt = self.current_main_receipt_fixture()
                if change == "missing":
                    receipt.pop(field)
                else:
                    receipt[field] = None if change == "null" else {}
                with self.subTest(field=field, change=change), self.assertRaises(ValueError):
                    self.validate_receipt_copy(receipt)
        receipt = self.current_main_receipt_fixture()
        receipt["unreviewedTestExecution"] = {}
        with self.assertRaises(ValueError):
            self.validate_receipt_copy(receipt)

    def test_every_owner_class_rejects_missing_extra_or_changed_invocation_fields(self) -> None:
        mutations = {
            "filter": "FullyQualifiedName~SomethingElse",
            "minimumExpectedTests": 0,
            "project": "Other.Tests/Other.Tests.csproj",
            "reuseFullSuiteBuild": 1,
            "runner": "dotnet-test-may-rebuild",
            "sdkVersion": "10.0.999",
            "sourceFiles": ["Chummer.Tests/UnrelatedTests.cs"],
            "testAssembly": None,
            "coreProjectionContent": None,
        }
        for index in range(9):
            for field, value in mutations.items():
                for change in ("missing", "changed"):
                    receipt = self.current_main_receipt_fixture()
                    row = self.owner_execution_rows(receipt)[index]
                    if change == "missing":
                        row.pop(field)
                    else:
                        row[field] = value
                    with self.subTest(index=index, field=field, change=change), self.assertRaises(ValueError):
                        self.validate_receipt_copy(receipt)
            receipt = self.current_main_receipt_fixture()
            self.owner_execution_rows(receipt)[index]["observedPassingTotal"] = 999
            with self.subTest(index=index, extra=True), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)

    def test_existing_owner_classes_cannot_be_dropped_duplicated_reordered_or_extended(self) -> None:
        for change in ("drop", "duplicate", "reverse", "extra"):
            receipt = self.current_main_receipt_fixture()
            rows = receipt["focusedExistingOwnerRegressionTestExecutions"]
            if change == "drop":
                rows.pop()
            elif change == "duplicate":
                rows[-1] = copy.deepcopy(rows[0])
            elif change == "reverse":
                rows.reverse()
            else:
                rows.append(copy.deepcopy(rows[0]))
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)

    def test_configured_test_floors_require_exact_integers_not_observed_totals(self) -> None:
        for index in range(10):
            for change in ("float", "boolean", "lower", "higher"):
                receipt = self.current_main_receipt_fixture()
                row = [*receipt["testExecutions"], *self.owner_execution_rows(receipt)][index]
                minimum = row["minimumExpectedTests"]
                row["minimumExpectedTests"] = {"float": float(minimum), "boolean": True,
                                               "lower": minimum - 1, "higher": minimum + 1}[change]
                with self.subTest(index=index, change=change), self.assertRaises(ValueError):
                    self.validate_receipt_copy(receipt)

    def test_owner_tests_cannot_use_different_assembly_or_core_projection_bytes(self) -> None:
        for index in range(9):
            for field, key, value in (
                ("testAssembly", "path", "Other.dll"),
                ("testAssembly", "sha256", "e" * 64),
                ("testAssembly", "sizeBytes", 9),
                ("testAssembly", "sizeBytes", 12345.0),
                ("testAssembly", "extra", "not admitted"),
                ("coreProjectionContent", "contentInventorySha256", "e" * 64),
                ("coreProjectionContent", "sourceRoot", "/another/provenance/root"),
                ("coreProjectionContent", "fileCount", 123.0),
                ("coreProjectionContent", "extra", "not admitted"),
            ):
                receipt = self.current_main_receipt_fixture()
                self.owner_execution_rows(receipt)[index][field][key] = value
                with self.subTest(index=index, field=field, key=key, value=value), self.assertRaises(ValueError):
                    self.validate_receipt_copy(receipt)

    def test_coordinated_core_projection_forgery_is_not_runtime_feed_authority(self) -> None:
        for key, value in (
            ("repository", "https://github.com/example/fork.git"),
            ("runtimeSourceCommit", "e" * 40),
            ("packageRecipeCommit", "e" * 40),
            ("checkoutCommit", "e" * 40),
            ("usage", "source-project-reference"),
            ("contentDirectories", ["Chummer/data", "Chummer/lang"]),
            ("sourceRoot", "relative/core"),
            ("sourceRoot", "/data/../redirected"),
            ("contentInventorySha256", "G" * 64),
            ("fileCount", True),
            ("fileCount", 0),
        ):
            receipt = self.current_main_receipt_fixture()
            for row in [*receipt["testExecutions"], *self.owner_execution_rows(receipt)]:
                row["coreProjectionContent"][key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)

    def test_owner_sources_require_canonical_inventory_membership_and_exact_source_sets(self) -> None:
        for change in ("missing", "duplicate", "reverse", "bad-digest", "boolean-size", "unknown-field"):
            receipt = self.current_main_receipt_fixture()
            rows = receipt["sourceInventory"]
            if change == "missing":
                rows.pop()
            elif change == "duplicate":
                rows.append(copy.deepcopy(rows[0]))
            elif change == "reverse":
                rows.reverse()
            elif change == "bad-digest":
                rows[0]["sha256"] = "not-a-digest"
            elif change == "boolean-size":
                rows[0]["sizeBytes"] = True
            else:
                rows[0]["extra"] = "not admitted"
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)
        receipt = self.current_main_receipt_fixture()
        receipt["focusedContinuationTestExecution"]["sourceFiles"].append(
            receipt["focusedOwnerShellTestExecution"]["sourceFiles"][0])
        with self.assertRaises(ValueError):
            self.validate_receipt_copy(receipt)

    def test_full_execution_cannot_be_filtered_rebuilt_duplicated_or_misreported(self) -> None:
        for field, value in (
            ("filter", "FullyQualifiedName~Subset"),
            ("project", "Other.Tests/Other.Tests.csproj"),
            ("runner", "different-runner"),
            ("compileRunner", "unbound-build"),
            ("sdkVersion", "10.0.999"),
            ("buildInParallel", True),
            ("disableBuildServers", False),
            ("useSharedCompilation", True),
            ("maxCpuCount", True),
        ):
            receipt = self.current_main_receipt_fixture()
            receipt["testExecutions"][0][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)
        for change in ("missing", "duplicate", "projects"):
            receipt = self.current_main_receipt_fixture()
            if change == "missing":
                receipt["testExecutions"] = []
            elif change == "duplicate":
                receipt["testExecutions"].append(copy.deepcopy(receipt["testExecutions"][0]))
            else:
                receipt["testProjects"].append("Other.Tests/Other.Tests.csproj")
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)
    def test_cold_non_use_and_incomplete_warm_postures_fail_closed(self) -> None:
        mutations = (
            {"status": "not_supplied", "used": False},
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
                with self.assertRaisesRegex(ValueError, "copied-cache"):
                    self.validate_receipt_copy(payload)

    def test_copied_cache_flags_types_and_exact_shape_fail_closed(self) -> None:
        mutations = [
            (field, value)
            for field in ("used", "importedByCopy", "coldProducerFallbackOnCacheMiss")
            for value in (False, 1, "true", None)
        ] + [
            ("status", "not_supplied"), ("contract", "other"), ("cacheKey", "0" * 64),
            ("packageCount", True), ("packageCount", 18.0), ("packageCount", "18"),
            ("sourcePath", None), ("sourcePath", ""),
        ]
        for field, value in mutations:
            receipt = self.current_main_receipt_fixture()
            receipt["ownerPackageArtifactCache"][field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)
        for field in self.current_main_receipt_fixture()["ownerPackageArtifactCache"]:
            receipt = self.current_main_receipt_fixture()
            receipt["ownerPackageArtifactCache"].pop(field)
            with self.subTest(missing=field), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)
        for field, value in (
            ("extra", False),
            ("manifest", {"path": "../owner-package-cache.json", "sha256": self.module.EXPECTED_CACHE_MANIFEST_SHA256,
                          "sizeBytes": self.module.EXPECTED_CACHE_MANIFEST_SIZE}),
        ):
            receipt = self.current_main_receipt_fixture()
            receipt["ownerPackageArtifactCache"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)
        for field in ("packageCacheWasFresh", "localCompatibilityTree", "stubPackagesAllowed"):
            for value in (None, 0, 1, "false"):
                receipt = self.current_main_receipt_fixture()
                receipt[field] = value
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    self.validate_receipt_copy(receipt)
        for mutation in ("float-size", "extra", "missing", "digest"):
            receipt = self.current_main_receipt_fixture()
            manifest = receipt["ownerPackageArtifactCache"]["manifest"]
            if mutation == "float-size": manifest["sizeBytes"] = float(manifest["sizeBytes"])
            elif mutation == "extra": manifest["extra"] = False
            elif mutation == "missing": manifest.pop("path")
            else: manifest["sha256"] = "0" * 64
            with self.subTest(manifest=mutation), self.assertRaises(ValueError):
                self.validate_receipt_copy(receipt)

    def test_copied_cache_row_counts_paths_types_and_membership_are_closed(self) -> None:
        for field in ("packages", "authorityArtifacts"):
            for mutation in ("missing", "extra", "duplicate", "reordered", "missing-field", "extra-field"):
                receipt = self.current_main_receipt_fixture()
                rows = receipt["ownerPackageArtifactCache"][field]
                if mutation == "missing": rows.pop()
                elif mutation == "extra": rows.append(copy.deepcopy(rows[-1]))
                elif mutation == "duplicate": rows[-1] = copy.deepcopy(rows[0])
                elif mutation == "reordered": rows.reverse()
                elif mutation == "missing-field": rows[0].pop("sha256")
                else: rows[0]["extra"] = False
                with self.subTest(field=field, mutation=mutation), self.assertRaises(ValueError):
                    self.validate_receipt_copy(receipt)
            prefix = "packages" if field == "packages" else "authority"
            for key, value in (
                ("path", f"{prefix}/../outside"), ("path", f"{prefix}/nested/file"),
                ("path", f"{prefix}/..\\outside"), ("path", "/absolute"),
                ("path", f"{prefix}/."), ("path", f"{prefix}/bad\x00name"),
                ("sha256", "X" * 64), ("sha256", "a" * 63),
                ("sizeBytes", True), ("sizeBytes", 1.0), ("sizeBytes", "1"), ("sizeBytes", 0),
            ):
                receipt = self.current_main_receipt_fixture()
                receipt["ownerPackageArtifactCache"][field][0][key] = value
                with self.subTest(field=field, key=key, value=value), self.assertRaises(ValueError):
                    self.validate_receipt_copy(receipt)

    def test_final_receipt_and_retained_cache_are_byte_equivalent(self) -> None:
        with self.materialized_cache_fixture() as (receipt, cache, feed):
            self.assertEqual(cache, self.module.validate_package_feed(feed))
            self.validate_receipt_copy(receipt)
            self.module.validate_receipt_cache_equivalence(receipt, cache, package_feed=feed)
            self.assertFalse(Path(receipt["ownerPackageArtifactCache"]["sourcePath"]).exists())

    def test_final_receipt_and_retained_cache_divergence_fails_closed(self) -> None:
        with self.materialized_cache_fixture() as (base_receipt, base_cache, feed):
            inventory_tamper = copy.deepcopy(base_receipt)
            inventory_tamper["packageInventory"][0]["sha256"] = "b" * 64
            owner_feed_tamper = copy.deepcopy(base_receipt)
            owner_feed_tamper["coreRuntimeFeed"]["packages"][0]["sizeBytes"] += 1
            cache_tamper = copy.deepcopy(base_cache)
            cache_tamper["packages"][0]["sha256"] = "c" * 64
            for receipt, cache, message in (
                (inventory_tamper, base_cache, "package inventory diverges"),
                (owner_feed_tamper, base_cache, "owner feeds diverge"),
                (base_receipt, cache_tamper, "differs from authenticated files"),
            ):
                with self.subTest(message=message), self.assertRaisesRegex(ValueError, message):
                    self.module.validate_receipt_cache_equivalence(receipt, cache, package_feed=feed)

    def test_copied_receipt_rows_match_actual_files_not_only_manifest_digests(self) -> None:
        with self.materialized_cache_fixture() as (base, cache, feed):
            for field in ("packages", "authorityArtifacts"):
                for key, value in (("sha256", "0" * 64), ("sizeBytes", 999), ("path", None)):
                    receipt = copy.deepcopy(base)
                    row = receipt["ownerPackageArtifactCache"][field][0]
                    row[key] = value if value is not None else row["path"] + ".substituted"
                    receipt["ownerPackageArtifactCache"][field].sort(key=lambda item: item["path"])
                    # Still well-shaped; must fail actual-file equivalence.
                    self.validate_receipt_copy(receipt)
                    with self.subTest(field=field, key=key), self.assertRaisesRegex(ValueError, "rows differ"):
                        self.module.validate_receipt_cache_equivalence(receipt, cache, package_feed=feed)

    def test_cache_files_reject_tamper_missing_extras_links_and_special_entries(self) -> None:
        for directory in ("packages", "authority"):
            for mutation in ("bytes", "missing", "extra", "extra-directory", "symlink", "directory", "fifo"):
                with self.materialized_cache_fixture() as (receipt, cache, feed):
                    folder = feed if directory == "packages" else feed.parent / "authority"
                    path = next(folder.iterdir())
                    if mutation == "bytes":
                        raw = path.read_bytes()
                        path.write_bytes(bytes([raw[0] ^ 1]) + raw[1:])
                    elif mutation == "extra": (folder / "extra.bin").write_bytes(b"extra")
                    elif mutation == "extra-directory": (folder / "extra-dir").mkdir()
                    else:
                        raw = path.read_bytes()
                        path.unlink()
                        if mutation == "symlink":
                            target = feed.parent.parent / "fixture-target"
                            target.write_bytes(raw)
                            path.symlink_to(target)
                        elif mutation == "directory": path.mkdir()
                        elif mutation == "fifo": os.mkfifo(path)
                    with self.subTest(directory=directory, mutation=mutation), self.assertRaises(ValueError):
                        self.module.validate_receipt_cache_equivalence(receipt, cache, package_feed=feed)

    def test_cache_file_reads_are_bounded_by_opened_size(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary).resolve() / "fixture.bin"
            path.write_bytes(b"1234")
            for capture in (False, True):
                for chunks, message, expected_requests in (
                    ([b"1234", b"x"], "grew during read", [5, 1]),
                    ([b"12345"], "grew during read", [5]),
                    ([b"123", b""], "shrank during read", [5, 2]),
                ):
                    with (
                        self.subTest(capture=capture, chunks=chunks),
                        patch.object(self.module.os, "read", side_effect=chunks) as read,
                        self.assertRaisesRegex(ValueError, message),
                    ):
                        self.module.cache_file_inventory(path, "fixture", capture=capture)
                    self.assertEqual(expected_requests, [call.args[1] for call in read.call_args_list])

    def test_manifest_and_directory_substitution_fail_closed(self) -> None:
        for mutation in ("manifest-bytes", "manifest-symlink", "authority-directory-symlink", "feed-symlink", "root-extra"):
            with self.materialized_cache_fixture() as (receipt, cache, feed):
                root = feed.parent
                if mutation == "manifest-bytes":
                    with (root / "owner-package-cache.json").open("ab") as stream: stream.write(b" ")
                elif mutation == "root-extra": (root / "unbound").mkdir()
                else:
                    path = root / "owner-package-cache.json" if mutation == "manifest-symlink" else (
                        root / "authority" if mutation == "authority-directory-symlink" else feed)
                    moved = root.parent / "fixture-original"
                    path.rename(moved)
                    path.symlink_to(moved, target_is_directory=moved.is_dir())
                with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                    self.module.validate_receipt_cache_equivalence(receipt, cache, package_feed=feed)

    def test_authenticated_manifest_still_requires_closed_safe_rows(self) -> None:
        for field in ("packages", "authorityArtifacts"):
            for mutation in ("missing", "extra", "duplicate", "traversal", "bad-hash", "extra-field"):
                with self.materialized_cache_fixture() as (_receipt, cache, feed):
                    rows = cache[field]
                    if mutation == "missing": rows.pop()
                    elif mutation == "extra": rows.append(copy.deepcopy(rows[-1]))
                    elif mutation == "duplicate": rows[-1] = copy.deepcopy(rows[0])
                    elif mutation == "traversal": rows[0]["fileName"] = "../outside"
                    elif mutation == "bad-hash": rows[0]["sha256"] = "X" * 64
                    else: rows[0]["extra"] = False
                    manifest = feed.parent / "owner-package-cache.json"
                    write_private(manifest, cache)
                    with (
                        patch.object(self.module, "EXPECTED_CACHE_MANIFEST_SHA256", self.module.sha256(manifest)),
                        patch.object(self.module, "EXPECTED_CACHE_MANIFEST_SIZE", manifest.stat().st_size),
                        self.subTest(field=field, mutation=mutation), self.assertRaises(ValueError),
                    ):
                        self.module.validate_package_feed(feed)

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
