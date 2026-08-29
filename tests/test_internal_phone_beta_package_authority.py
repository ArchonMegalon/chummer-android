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
            ):
                self.module.validate_presentation_repository(root)

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
