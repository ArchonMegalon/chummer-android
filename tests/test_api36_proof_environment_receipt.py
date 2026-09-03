#!/usr/bin/env python3
"""Hostile tests for API-36 build/journey environment authority."""

from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts/api36_proof_environment_authority.py"
SPEC = importlib.util.spec_from_file_location("api36_proof_environment_authority", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class Api36ProofEnvironmentAuthorityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.policy_path = self.root / "policy.json"
        self.policy_path.write_bytes(
            (REPO / "eng/api36-proof-environment-authority.json").read_bytes()
        )
        self.policy_snapshot = MODULE.StableFile(self.policy_path, "policy")
        self.policy = MODULE.load_policy(self.policy_snapshot)
        self.gate = {
            "schema": "chummer.android.api36-sr5-wizard-gate-binding/v1",
            "contractSha256": "b" * 64,
            "publicationAuthorized": False,
        }

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def environment() -> dict[str, object]:
        digest = "a" * 64
        observation = {
            "runnerImage": {
                "runnerOs": "Linux",
                "runnerArch": "X64",
                "imageOs": "ubuntu24",
                "imageVersion": "20260901.1.0",
            },
            "java": {
                "runtimeVersion": "17.0.16",
                "compilerVersion": "17.0.16",
                "versionOutputSha256": digest,
                "compilerOutputSha256": digest,
            },
            "dotnet": {
                "sdkVersion": "10.0.110",
                "runtimeIdentifier": "linux-x64",
                "infoOutputSha256": digest,
            },
            "androidSdk": {
                "installedPackages": [
                    {"package": "build-tools;36.0.0", "version": "36.0.0"},
                    {"package": "emulator", "version": "36.2.11"},
                    {"package": "platform-tools", "version": "36.0.0"},
                    {"package": "platforms;android-36", "version": "2"},
                    {
                        "package": "system-images;android-36;google_apis;x86_64",
                        "version": "10",
                    },
                ],
                "inventoryOutputSha256": digest,
                "adb": {
                    "protocolVersion": "1.0.41",
                    "packageVersion": "36.0.0-13206524",
                    "versionOutputSha256": digest,
                },
                "emulator": {
                    "version": "36.2.11.0",
                    "versionOutputSha256": digest,
                },
            },
            "kernel": {
                "system": "Linux",
                "release": "6.11.0-hosted",
                "machine": "x86_64",
                "procVersionSha256": digest,
            },
            "kvm": {
                "devicePresent": True,
                "characterDevice": True,
                "readable": True,
                "writable": True,
                "kernelModulePresent": True,
            },
        }
        java = observation["java"]
        dotnet = observation["dotnet"]
        android = observation["androidSdk"]
        java["versionOutputSha256"] = MODULE.canonical_sha256(
            {"runtimeVersion": java["runtimeVersion"]}
        )
        java["compilerOutputSha256"] = MODULE.canonical_sha256(
            {"compilerVersion": java["compilerVersion"]}
        )
        dotnet["infoOutputSha256"] = MODULE.canonical_sha256(
            {
                "sdkVersion": dotnet["sdkVersion"],
                "runtimeIdentifier": dotnet["runtimeIdentifier"],
            }
        )
        android["inventoryOutputSha256"] = MODULE.canonical_sha256(
            android["installedPackages"]
        )
        android["adb"]["versionOutputSha256"] = MODULE.canonical_sha256(
            {
                "protocolVersion": android["adb"]["protocolVersion"],
                "packageVersion": android["adb"]["packageVersion"],
            }
        )
        android["emulator"]["versionOutputSha256"] = MODULE.canonical_sha256(
            {"version": android["emulator"]["version"]}
        )
        return observation

    def receipt(self, role: str = "journey") -> dict[str, object]:
        if role == "journey":
            subject = {
                "matrixJourney": "career-active-skill-advance",
                "driverJourney": "career-active-skill-advance",
                "receiptSchema": "chummer.android.editing-e2e/v1",
                "journeyReceiptSha256": "1" * 64,
                "journeyReceiptSizeBytes": 100,
                "apkSha256": "2" * 64,
                "apkSizeBytes": 200,
                "artifactAuthoritySha256": "3" * 64,
            }
        else:
            subject = {
                "x64Apk": {"sha256": "1" * 64, "sizeBytes": 100},
                "arm64Apk": {"sha256": "2" * 64, "sizeBytes": 200},
                "hostedCandidate": {
                    "schema": "chummer.android.api36-arm64-hosted-debug-candidate/v1",
                    "sha256": "3" * 64,
                    "sizeBytes": 300,
                },
                "workflow": {"sha256": "4" * 64, "sizeBytes": 400},
            }
        return MODULE.base_receipt(
            role=role,
            policy=self.policy,
            policy_snapshot=self.policy_snapshot,
            gate_authority=self.gate,
            subject_authority=subject,
            observation=self.environment(),
        )

    def test_policy_and_receipt_are_exact_non_publishing_v2_authority(self) -> None:
        self.assertEqual(MODULE.POLICY_SCHEMA, self.policy["schema"])
        self.assertEqual(17, self.policy["requiredJavaMajor"])
        self.assertEqual("10.0.110", self.policy["requiredDotnetSdkVersion"])
        for role, schema in (
            ("build", MODULE.BUILD_SCHEMA),
            ("journey", MODULE.JOURNEY_SCHEMA),
        ):
            with self.subTest(role=role):
                receipt = self.receipt(role)
                self.assertEqual(schema, receipt["schema"])
                self.assertFalse(receipt["publicationAuthorized"])
                self.assertEqual(
                    MODULE.canonical_sha256(receipt["environment"]),
                    receipt["environmentSha256"],
                )
                self.assertEqual(
                    MODULE.canonical_sha256(receipt["compatibility"]),
                    receipt["compatibilitySha256"],
                )
                MODULE.validate_receipt(receipt, self.policy)

    def test_compatibility_excludes_volatile_image_and_kernel_patch_but_records_them(self) -> None:
        first = self.environment()
        second = copy.deepcopy(first)
        second["runnerImage"]["imageVersion"] = "20260902.2.0"
        second["kernel"]["release"] = "6.11.1-hosted"
        second["kernel"]["procVersionSha256"] = "c" * 64
        self.assertNotEqual(
            MODULE.canonical_sha256(first),
            MODULE.canonical_sha256(second),
        )
        self.assertEqual(
            MODULE.compatibility_observation(first, self.policy, "journey"),
            MODULE.compatibility_observation(second, self.policy, "journey"),
        )

    def test_journey_requires_api36_image_and_usable_kvm_build_only_records_kvm(self) -> None:
        environment = self.environment()
        environment["androidSdk"]["installedPackages"] = [
            row
            for row in environment["androidSdk"]["installedPackages"]
            if not row["package"].startswith("system-images;")
        ]
        environment["androidSdk"]["inventoryOutputSha256"] = MODULE.canonical_sha256(
            environment["androidSdk"]["installedPackages"]
        )
        MODULE.validate_environment(environment, self.policy, "build")
        with self.assertRaisesRegex(ValueError, "required Android packages"):
            MODULE.validate_environment(environment, self.policy, "journey")
        environment = self.environment()
        environment["kvm"]["writable"] = False
        MODULE.validate_environment(environment, self.policy, "build")
        with self.assertRaisesRegex(ValueError, "usable KVM"):
            MODULE.validate_environment(environment, self.policy, "journey")

    def test_toolchain_and_sdk_drift_fail_closed(self) -> None:
        cases = []
        java = self.environment()
        java["java"]["compilerVersion"] = "21.0.1"
        cases.append((java, "Java"))
        dotnet = self.environment()
        dotnet["dotnet"]["sdkVersion"] = "10.0.111"
        cases.append((dotnet, "dotnet"))
        runner = self.environment()
        runner["runnerImage"]["imageOs"] = "ubuntu26"
        cases.append((runner, "runner image"))
        missing = self.environment()
        missing["androidSdk"]["installedPackages"] = missing["androidSdk"]["installedPackages"][1:]
        missing["androidSdk"]["inventoryOutputSha256"] = MODULE.canonical_sha256(
            missing["androidSdk"]["installedPackages"]
        )
        cases.append((missing, "package family"))
        duplicate = self.environment()
        duplicate["androidSdk"]["installedPackages"].append(
            copy.deepcopy(duplicate["androidSdk"]["installedPackages"][0])
        )
        cases.append((duplicate, "ambiguous"))
        for observation, message in cases:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    MODULE.validate_environment(observation, self.policy, "journey")

    def test_receipt_tampering_and_publication_escalation_fail_closed(self) -> None:
        receipt = self.receipt()
        receipt["environment"]["kernel"]["release"] = "tampered"
        with self.assertRaisesRegex(ValueError, "environment digest"):
            MODULE.validate_receipt(receipt, self.policy)
        receipt = self.receipt()
        receipt["publicationAuthorized"] = True
        receipt["receiptSha256"] = MODULE.canonical_sha256(
            {**receipt, "receiptSha256": None}
        )
        with self.assertRaisesRegex(ValueError, "boundary"):
            MODULE.validate_receipt(receipt, self.policy)

    def test_build_subject_binds_both_apks_candidate_and_workflow(self) -> None:
        x64 = self.root / "x64.apk"
        arm64 = self.root / "arm64.apk"
        workflow = self.root / "workflow.yml"
        candidate_path = self.root / "candidate.json"
        x64.write_bytes(b"x64")
        arm64.write_bytes(b"arm64")
        workflow.write_text("name: proof\n", encoding="utf-8")
        candidate = {
            "contractName": "chummer.android.api36-arm64-hosted-debug-candidate/v1",
            "status": "candidate",
            "publicationAuthorized": False,
            "releaseEligible": False,
            "releaseAttested": False,
            "artifact": {
                "sha256": hashlib.sha256(arm64.read_bytes()).hexdigest(),
            },
        }
        candidate_path.write_text(json.dumps(candidate) + "\n", encoding="utf-8")
        subject = MODULE.build_subject(
            x64_apk=MODULE.StableFile(x64, "x64"),
            arm64_apk=MODULE.StableFile(arm64, "arm64"),
            hosted_candidate=MODULE.StableFile(candidate_path, "candidate"),
            workflow=MODULE.StableFile(workflow, "workflow"),
        )
        self.assertEqual(
            hashlib.sha256(x64.read_bytes()).hexdigest(),
            subject["x64Apk"]["sha256"],
        )
        candidate["artifact"]["sha256"] = "f" * 64
        candidate_path.write_text(json.dumps(candidate) + "\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "exact ARM64 APK"):
            MODULE.build_subject(
                x64_apk=MODULE.StableFile(x64, "x64"),
                arm64_apk=MODULE.StableFile(arm64, "arm64"),
                hosted_candidate=MODULE.StableFile(candidate_path, "candidate"),
                workflow=MODULE.StableFile(workflow, "workflow"),
            )

    def test_sdkmanager_parser_omits_description_and_host_location(self) -> None:
        output = """Installed packages:
Path | Version | Description | Location
build-tools;36.0.0 | 36.0.0 | Android SDK Build-Tools | private/location
platform-tools | 36.0.0 | Android SDK Platform-Tools | private/location
Available Packages:
Path | Version | Description
"""
        rows = MODULE.parse_sdkmanager_inventory(output)
        self.assertEqual(
            [
                {"package": "build-tools;36.0.0", "version": "36.0.0"},
                {"package": "platform-tools", "version": "36.0.0"},
            ],
            rows,
        )
        serialized = json.dumps(rows)
        self.assertNotIn("Description", serialized)
        self.assertNotIn("private/location", serialized)
        duplicate = output.replace(
            "Available Packages:",
            "platform-tools | 36.0.0 | duplicate | private/location\nAvailable Packages:",
        )
        with self.assertRaisesRegex(ValueError, "duplicate installed package"):
            MODULE.parse_sdkmanager_inventory(duplicate)

    def test_collector_records_versions_and_digests_without_sdk_path(self) -> None:
        sdk = self.root / "private-sdk"
        for relative in (
            "cmdline-tools/latest/bin/sdkmanager",
            "platform-tools/adb",
            "emulator/emulator",
        ):
            path = sdk / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("tool\n", encoding="utf-8")
            path.chmod(0o755)
        inventory = """Installed packages:
Path | Version | Description | Location
build-tools;36.0.0 | 36.0.0 | build tools | secret/location
emulator | 36.2.11 | emulator | secret/location
platform-tools | 36.0.0 | platform tools | secret/location
platforms;android-36 | 2 | platform | secret/location
system-images;android-36;google_apis;x86_64 | 10 | image | secret/location
Available Packages:
"""

        def run(command: tuple[str, ...]) -> str:
            if command == ("java", "-version"):
                return 'openjdk version "17.0.16" 2026-07-15'
            if command == ("javac", "-version"):
                return "javac 17.0.16"
            if command == ("dotnet", "--version"):
                return "10.0.110"
            if command == ("dotnet", "--info"):
                return ".NET SDK:\n Version: 10.0.110\n RID: linux-x64"
            if command[-1] == "--list_installed":
                return inventory.replace("secret/location", command[0])
            if command[-1] == "version":
                return (
                    "Android Debug Bridge version 1.0.41\n"
                    "Version 36.0.0-13206524\n"
                    f"Installed as {command[0]}"
                )
            if command[-1] == "-version":
                return (
                    "Android emulator version 36.2.11.0\n"
                    f"Found emulator at {command[0]}"
                )
            raise AssertionError(command)

        proc_version = self.root / "proc-version"
        proc_version.write_text("Linux version hosted\n", encoding="utf-8")
        kvm_module = self.root / "kvm-module"
        kvm_module.mkdir()
        observation = MODULE.collect_environment(
            sdk,
            {
                "RUNNER_OS": "Linux",
                "RUNNER_ARCH": "X64",
                "ImageOS": "ubuntu24",
                "ImageVersion": "20260901.1.0",
            },
            command_runner=run,
            kvm_path=Path("/dev/null"),
            kvm_module_path=kvm_module,
            proc_version_path=proc_version,
            uname_provider=lambda: SimpleNamespace(
                system="Linux",
                release="6.11.0-hosted",
                machine="x86_64",
            ),
        )
        MODULE.validate_environment(observation, self.policy, "journey")
        serialized = json.dumps(observation)
        self.assertNotIn(str(sdk), serialized)
        self.assertNotIn("secret/location", serialized)
        self.assertEqual(
            "36.0.0-13206524",
            observation["androidSdk"]["adb"]["packageVersion"],
        )
        relocated_sdk = self.root / "relocated-private-sdk"
        for relative in (
            "cmdline-tools/latest/bin/sdkmanager",
            "platform-tools/adb",
            "emulator/emulator",
        ):
            path = relocated_sdk / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("tool\n", encoding="utf-8")
            path.chmod(0o755)
        relocated = MODULE.collect_environment(
            relocated_sdk,
            {
                "RUNNER_OS": "Linux",
                "RUNNER_ARCH": "X64",
                "ImageOS": "ubuntu24",
                "ImageVersion": "20260901.1.0",
            },
            command_runner=run,
            kvm_path=Path("/dev/null"),
            kvm_module_path=kvm_module,
            proc_version_path=proc_version,
            uname_provider=lambda: SimpleNamespace(
                system="Linux",
                release="6.11.0-hosted",
                machine="x86_64",
            ),
        )
        self.assertEqual(observation, relocated)

    @unittest.skipUnless(Path("/proc/version").is_file(), "Linux procfs is unavailable")
    def test_real_proc_version_zero_stat_size_is_bounded_and_repeatable(self) -> None:
        proc_version = Path("/proc/version")
        self.assertEqual(0, proc_version.stat().st_size)
        first = MODULE.stable_virtual_file_bytes(proc_version, "real kernel version")
        second = MODULE.stable_virtual_file_bytes(proc_version, "real kernel version")
        self.assertTrue(first.startswith(b"Linux version"))
        self.assertEqual(first, second)
        self.assertLessEqual(len(first), 64 * 1024)

    def test_duplicate_json_and_input_drift_fail_closed(self) -> None:
        duplicate = self.root / "duplicate.json"
        duplicate.write_text('{"schema":"one","schema":"two"}\n', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
            MODULE.StableFile(duplicate, "duplicate").json()
        snapshot = MODULE.StableFile(self.policy_path, "policy")
        self.policy_path.write_bytes(self.policy_path.read_bytes() + b" ")
        with self.assertRaisesRegex(ValueError, "changed before receipt seal"):
            snapshot.recheck()


class Api36ProofEnvironmentSourceContractTests(unittest.TestCase):
    def test_runner_materializes_and_seals_journey_environment_after_finalizer(self) -> None:
        runner = (REPO / "scripts/run-api36-editing-e2e-ci.sh").read_text()
        self.assertLess(
            runner.index("finalize-api36-e2e-journey-receipt.py"),
            runner.index("materialize-api36-proof-environment-receipt.py"),
        )
        self.assertIn("journey \\", runner)
        self.assertIn("environment-receipt.json.sha256", runner)

    def test_workflow_captures_build_environment_with_pinned_java_and_dotnet(self) -> None:
        workflow = (REPO / ".github/workflows/api36-editing-e2e.yml").read_text()
        self.assertEqual(
            2,
            workflow.count(
                "actions/setup-java@cf277c60eb25467037889841efdb72551f06f6c3"
            ),
        )
        self.assertEqual(2, workflow.count('java-version: "17"'))
        self.assertEqual(2, workflow.count("dotnet-version: 10.0.110"))
        self.assertIn("build-environment-receipt.json", workflow)
        self.assertIn("--build-environment-receipt", workflow)

    def test_aggregate_is_v2_and_environment_is_not_an_eighth_journey(self) -> None:
        gate = json.loads(
            (REPO / "eng/api36-sr5-wizard-gate-authority.json").read_text()
        )
        aggregate = (
            REPO / "scripts/verify-api36-editing-e2e-aggregate.py"
        ).read_text()
        self.assertEqual(7, gate["requiredJourneyCount"])
        self.assertIn(
            'AGGREGATE_SCHEMA = "chummer.android.api36-sr5-wizard-e2e-aggregate/v2"',
            (REPO / "scripts/api36_wizard_gate_contract.py").read_text(),
        )
        self.assertIn('"environmentAuthority"', aggregate)
        self.assertNotIn(
            "proof-environment",
            [row["matrixJourney"] for row in gate["requiredJourneys"]],
        )


if __name__ == "__main__":
    unittest.main()
