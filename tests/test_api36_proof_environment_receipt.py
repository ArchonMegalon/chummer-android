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
from unittest import mock
import subprocess


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
        emulator_raw = (
            b"INFO         | Android emulator version 36.2.11.0 "
            b"(build_id 15917651) (CL:N/A)\n"
        )
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
                    "available": True,
                    "version": "36.2.11.0",
                    "buildId": 15917651,
                    "versionOutputSha256": digest,
                    "rawObservationSha256": hashlib.sha256(emulator_raw).hexdigest(),
                    "rawObservationSizeBytes": len(emulator_raw),
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
            {
                "version": android["emulator"]["version"],
                "buildId": android["emulator"]["buildId"],
            }
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
        observation = self.environment()
        if role == "build":
            observation["androidSdk"]["emulator"] = {
                "available": False,
                "version": None,
                "buildId": None,
                "versionOutputSha256": MODULE.canonical_sha256(
                    {"available": False}
                ),
                "rawObservationSha256": MODULE.EMPTY_SHA256,
                "rawObservationSizeBytes": 0,
            }
        return MODULE.base_receipt(
            role=role,
            policy=self.policy,
            policy_snapshot=self.policy_snapshot,
            gate_authority=self.gate,
            subject_authority=subject,
            observation=observation,
        )

    def test_policy_and_receipt_are_exact_non_publishing_v2_authority(self) -> None:
        self.assertEqual(
            "chummer.android.api36-build-environment-receipt/v2",
            MODULE.BUILD_SCHEMA,
        )
        self.assertEqual(
            "chummer.android.api36-journey-environment-receipt/v2",
            MODULE.JOURNEY_SCHEMA,
        )
        self.assertEqual(MODULE.POLICY_SCHEMA, self.policy["schema"])
        self.assertEqual(17, self.policy["requiredJavaMajor"])
        self.assertEqual("10.0.110", self.policy["requiredDotnetSdkVersion"])
        self.assertNotIn(
            "emulator", self.policy["roles"]["build"]["requiredAndroidPackages"]
        )
        self.assertIn(
            "emulator", self.policy["roles"]["journey"]["requiredAndroidPackages"]
        )
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

        legacy = self.receipt("journey")
        legacy["schema"] = "chummer.android.api36-journey-environment-receipt/v1"
        legacy["receiptSha256"] = MODULE.canonical_sha256(
            {**legacy, "receiptSha256": None}
        )
        with self.assertRaisesRegex(ValueError, "receipt schema, role, or status differs"):
            MODULE.validate_receipt(legacy, self.policy)

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
        environment["androidSdk"]["emulator"] = {
            "available": False,
            "version": None,
            "buildId": None,
            "versionOutputSha256": MODULE.canonical_sha256({"available": False}),
            "rawObservationSha256": MODULE.EMPTY_SHA256,
            "rawObservationSizeBytes": 0,
        }
        MODULE.validate_environment(environment, self.policy, "build")
        with self.assertRaisesRegex(ValueError, "required Android packages"):
            MODULE.validate_environment(environment, self.policy, "journey")
        environment = self.environment()
        environment["kvm"]["writable"] = False
        build_environment = copy.deepcopy(environment)
        build_environment["androidSdk"]["emulator"] = {
            "available": False,
            "version": None,
            "buildId": None,
            "versionOutputSha256": MODULE.canonical_sha256({"available": False}),
            "rawObservationSha256": MODULE.EMPTY_SHA256,
            "rawObservationSizeBytes": 0,
        }
        MODULE.validate_environment(build_environment, self.policy, "build")
        with self.assertRaisesRegex(ValueError, "usable KVM"):
            MODULE.validate_environment(environment, self.policy, "journey")

    def test_build_allows_an_explicitly_unavailable_emulator_but_journey_rejects_it(self) -> None:
        present = self.environment()
        with self.assertRaisesRegex(ValueError, "record emulator as unavailable"):
            MODULE.validate_environment(present, self.policy, "build")
        recorded_only = copy.deepcopy(present)
        recorded_only["androidSdk"]["emulator"] = {
            "available": False,
            "version": None,
            "buildId": None,
            "versionOutputSha256": MODULE.canonical_sha256({"available": False}),
            "rawObservationSha256": MODULE.EMPTY_SHA256,
            "rawObservationSizeBytes": 0,
        }
        absent = copy.deepcopy(present)
        absent["androidSdk"]["installedPackages"] = [
            row
            for row in absent["androidSdk"]["installedPackages"]
            if row["package"] != "emulator"
        ]
        absent["androidSdk"]["inventoryOutputSha256"] = MODULE.canonical_sha256(
            absent["androidSdk"]["installedPackages"]
        )
        absent["androidSdk"]["emulator"] = {
            "available": False,
            "version": None,
            "buildId": None,
            "versionOutputSha256": MODULE.canonical_sha256({"available": False}),
            "rawObservationSha256": MODULE.EMPTY_SHA256,
            "rawObservationSizeBytes": 0,
        }
        absent["kvm"] = {field: False for field in absent["kvm"]}
        present_build = MODULE.compatibility_observation(
            recorded_only, self.policy, "build"
        )
        absent_build = MODULE.compatibility_observation(absent, self.policy, "build")
        self.assertEqual(present_build, absent_build)
        self.assertNotIn("emulator", present_build)
        self.assertNotIn("kvm", present_build)
        with self.assertRaisesRegex(ValueError, "required Android packages"):
            MODULE.compatibility_observation(absent, self.policy, "journey")

    def test_journey_compatibility_retains_emulator_and_kvm(self) -> None:
        first = self.environment()
        second = copy.deepcopy(first)
        for row in second["androidSdk"]["installedPackages"]:
            if row["package"] == "emulator":
                row["version"] = "36.2.12"
        second["androidSdk"]["inventoryOutputSha256"] = MODULE.canonical_sha256(
            second["androidSdk"]["installedPackages"]
        )
        second["androidSdk"]["emulator"]["version"] = "36.2.12.0"
        second["androidSdk"]["emulator"][
            "versionOutputSha256"
        ] = MODULE.canonical_sha256(
            {
                "version": "36.2.12.0",
                "buildId": second["androidSdk"]["emulator"]["buildId"],
            }
        )
        first_compatibility = MODULE.compatibility_observation(
            first, self.policy, "journey"
        )
        second_compatibility = MODULE.compatibility_observation(
            second, self.policy, "journey"
        )
        third = copy.deepcopy(first)
        third["androidSdk"]["emulator"]["buildId"] += 1
        third["androidSdk"]["emulator"][
            "versionOutputSha256"
        ] = MODULE.canonical_sha256(
            {
                "version": third["androidSdk"]["emulator"]["version"],
                "buildId": third["androidSdk"]["emulator"]["buildId"],
            }
        )
        third_compatibility = MODULE.compatibility_observation(
            third, self.policy, "journey"
        )
        self.assertIn("emulator", first_compatibility)
        self.assertIn("kvm", first_compatibility)
        self.assertNotEqual(first_compatibility, second_compatibility)
        self.assertNotEqual(first_compatibility, third_compatibility)

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
        emulator_mismatch = self.environment()
        for row in emulator_mismatch["androidSdk"]["installedPackages"]:
            if row["package"] == "emulator":
                row["version"] = "36.2.12"
        emulator_mismatch["androidSdk"][
            "inventoryOutputSha256"
        ] = MODULE.canonical_sha256(
            emulator_mismatch["androidSdk"]["installedPackages"]
        )
        cases.append((emulator_mismatch, "emulator observation authority"))
        invalid_build = self.environment()
        invalid_build["androidSdk"]["emulator"]["buildId"] = 0
        cases.append((invalid_build, "emulator observation authority"))
        oversized_raw_observation = self.environment()
        oversized_raw_observation["androidSdk"]["emulator"][
            "rawObservationSizeBytes"
        ] = 64 * 1024 + 1
        cases.append((oversized_raw_observation, "emulator observation authority"))
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

    def test_emulator_parser_requires_one_exact_official_header_and_numeric_build(self) -> None:
        valid = (
            b"INFO         | Android emulator version 36.2.11.0 "
            b"(build_id 15917651) (CL:N/A)\n"
        )
        path = self.root / "emulator-version.txt"
        path.write_bytes(valid)
        parsed = MODULE.parse_emulator_version_observation(
            MODULE.StableFile(path, "emulator version")
        )
        self.assertEqual("36.2.11.0", parsed["version"])
        self.assertEqual(15917651, parsed["buildId"])
        self.assertEqual(hashlib.sha256(valid).hexdigest(), parsed["rawObservationSha256"])
        self.assertEqual(len(valid), parsed["rawObservationSizeBytes"])

        hostile = (
            b"Android emulator version 36.2.11.0 (build_id nope) (CL:N/A)\n",
            b"prefix Android emulator version 36.2.11.0 (build_id 1) (CL:N/A)\n",
            valid.rstrip(b"\n") + b" trailing\n",
            valid + valid,
            valid
            + b"Android emulator version 99.0.0.0 (build_id missing) (CL:N/A)\n",
            b"\xffAndroid emulator version 36.2.11.0 (build_id 1) (CL:N/A)\n",
        )
        for index, payload in enumerate(hostile):
            with self.subTest(index=index):
                path.write_bytes(payload)
                with self.assertRaises(ValueError):
                    MODULE.parse_emulator_version_observation(
                        MODULE.StableFile(path, "emulator version")
                    )

    def test_emulator_version_normalization_only_allows_one_trailing_zero(self) -> None:
        accepted = (
            ("36.2.11", "36.2.11"),
            ("36.2.11", "36.2.11.0"),
            ("36.2.11.0", "36.2.11"),
            ("36.2.0", "36.2.0.0"),
            ("36.2.0.0", "36.2.0"),
        )
        rejected = (
            ("36.2.11", "36.2.12.0"),
            ("36.2.11", "36.2.11.1"),
            ("36.2.11", "36.2.11.0.0"),
            ("preview", "preview.0"),
        )
        for package, observed in accepted:
            self.assertTrue(MODULE.emulator_versions_match(package, observed))
        for package, observed in rejected:
            self.assertFalse(MODULE.emulator_versions_match(package, observed))

    def test_raw_emulator_bytes_and_build_id_are_independent_authority(self) -> None:
        first_path = self.root / "emulator-first.txt"
        second_path = self.root / "emulator-second.txt"
        third_path = self.root / "emulator-third.txt"
        header = "Android emulator version 36.2.11.0 (build_id 15917651) (CL:N/A)"
        first_path.write_text(header + "\n", encoding="utf-8")
        second_path.write_text(header + "\nCopyright notice\n", encoding="utf-8")
        third_path.write_text(
            "Android emulator version 36.2.11.0 (build_id 15917652) (CL:N/A)\n",
            encoding="utf-8",
        )
        first = MODULE.parse_emulator_version_observation(
            MODULE.StableFile(first_path, "first emulator version")
        )
        second = MODULE.parse_emulator_version_observation(
            MODULE.StableFile(second_path, "second emulator version")
        )
        third = MODULE.parse_emulator_version_observation(
            MODULE.StableFile(third_path, "third emulator version")
        )
        self.assertEqual(first["versionOutputSha256"], second["versionOutputSha256"])
        self.assertNotEqual(first["rawObservationSha256"], second["rawObservationSha256"])
        self.assertNotEqual(first["buildId"], third["buildId"])
        self.assertNotEqual(first["versionOutputSha256"], third["versionOutputSha256"])

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

        commands: list[tuple[str, ...]] = []

        def run(command: tuple[str, ...]) -> str:
            commands.append(command)
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
            raise AssertionError(command)

        proc_version = self.root / "proc-version"
        proc_version.write_text("Linux version hosted\n", encoding="utf-8")
        kvm_module = self.root / "kvm-module"
        kvm_module.mkdir()
        emulator_version_path = self.root / "emulator-version.txt"
        emulator_version_path.write_bytes(
            b"INFO         | Android emulator version 36.2.11.0 "
            b"(build_id 15917651) (CL:N/A)\n"
        )
        emulator_version_snapshot = MODULE.StableFile(
            emulator_version_path,
            "emulator version observation",
        )
        observation = MODULE.collect_environment(
            sdk,
            {
                "RUNNER_OS": "Linux",
                "RUNNER_ARCH": "X64",
                "ImageOS": "ubuntu24",
                "ImageVersion": "20260901.1.0",
            },
            emulator_version_observation=emulator_version_snapshot,
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
        self.assertEqual(15917651, observation["androidSdk"]["emulator"]["buildId"])
        self.assertEqual(
            emulator_version_snapshot.sha256,
            observation["androidSdk"]["emulator"]["rawObservationSha256"],
        )
        self.assertFalse(
            any(
                command[-1] == "-version"
                and command[0].endswith("/emulator/emulator")
                for command in commands
            )
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
        commands_before_precaptured_observation = len(commands)
        relocated = MODULE.collect_environment(
            relocated_sdk,
            {
                "RUNNER_OS": "Linux",
                "RUNNER_ARCH": "X64",
                "ImageOS": "ubuntu24",
                "ImageVersion": "20260901.1.0",
            },
            emulator_version_observation=emulator_version_snapshot,
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
        self.assertFalse(
            any(
                command[-1] == "-version"
                and command[0].endswith("/emulator/emulator")
                for command in commands[commands_before_precaptured_observation:]
            )
        )

    def test_exact_emulator_allows_only_an_sdk_internal_file_symlink_chain(self) -> None:
        sdk = self.root / "sdk-with-internal-emulator-link"
        emulator = sdk / "emulator/emulator"
        target = sdk / "emulator/qemu/linux-x86_64/qemu-system-x86_64"
        target.parent.mkdir(parents=True)
        target.write_text("emulator\n", encoding="utf-8")
        target.chmod(0o755)
        emulator.parent.mkdir(parents=True, exist_ok=True)
        emulator.symlink_to(Path("qemu/linux-x86_64/qemu-system-x86_64"))
        self.assertEqual(
            emulator,
            MODULE.sdk_executable(
                sdk,
                "emulator/emulator",
                "emulator",
                required=True,
                allow_internal_file_symlink=True,
            ),
        )

        second_target = sdk / "emulator/qemu/emulator-dispatch"
        second_target.symlink_to(Path("linux-x86_64/qemu-system-x86_64"))
        emulator.unlink()
        emulator.symlink_to(Path("qemu/emulator-dispatch"))
        self.assertEqual(
            emulator,
            MODULE.sdk_executable(
                sdk,
                "emulator/emulator",
                "emulator",
                required=True,
                allow_internal_file_symlink=True,
            ),
        )

    def test_command_failure_reports_bounded_tool_output_without_environment(self) -> None:
        stderr = "emulator loader failure\n" + ("x" * 5000)
        with mock.patch.object(
            MODULE.subprocess,
            "run",
            side_effect=subprocess.CalledProcessError(
                127,
                ["/sdk/emulator/emulator", "-version"],
                output="version probe stdout",
                stderr=stderr,
            ),
        ), self.assertRaisesRegex(
            RuntimeError,
            r"exit code 127; stdout='version probe stdout'; stderr='emulator loader failure",
        ) as failure:
            MODULE._run(("/sdk/emulator/emulator", "-version"))
        self.assertIn("...[truncated]", str(failure.exception))
        self.assertNotIn("PATH=", str(failure.exception))

    def test_emulator_symlink_escape_and_symlinked_sdk_directory_fail_closed(self) -> None:
        sdk = self.root / "sdk-with-hostile-emulator-link"
        emulator = sdk / "emulator/emulator"
        emulator.parent.mkdir(parents=True)
        external = self.root / "external-emulator"
        external.write_text("external\n", encoding="utf-8")
        external.chmod(0o755)
        emulator.symlink_to(external)
        with self.assertRaisesRegex(ValueError, "escapes the Android SDK root"):
            MODULE.sdk_executable(
                sdk,
                "emulator/emulator",
                "emulator",
                required=True,
                allow_internal_file_symlink=True,
            )

        emulator.unlink()
        real_directory = sdk / "real-emulator-directory"
        real_directory.mkdir()
        real_binary = real_directory / "emulator"
        real_binary.write_text("emulator\n", encoding="utf-8")
        real_binary.chmod(0o755)
        emulator.parent.rmdir()
        emulator.parent.symlink_to(real_directory, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "symlinked SDK directory component"):
            MODULE.sdk_executable(
                sdk,
                "emulator/emulator",
                "emulator",
                required=True,
                allow_internal_file_symlink=True,
            )

    def test_missing_build_emulator_is_not_replaced_by_ambiguous_path_binaries(self) -> None:
        sdk = self.root / "sdk-without-emulator"
        sdk.mkdir()
        rogue_directories: list[str] = []
        for name in ("rogue-a", "rogue-b"):
            binary = self.root / name / "emulator"
            binary.parent.mkdir()
            rogue_directories.append(str(binary.parent))
            binary.write_text("rogue\n", encoding="utf-8")
            binary.chmod(0o755)
        with mock.patch.dict("os.environ", {"PATH": ":".join(rogue_directories)}):
            self.assertIsNone(
                MODULE.sdk_executable(
                    sdk,
                    "emulator/emulator",
                    "emulator",
                    required=False,
                    allow_internal_file_symlink=True,
                )
            )
            with self.assertRaisesRegex(ValueError, "exact Android SDK path"):
                MODULE.sdk_executable(
                    sdk,
                    "emulator/emulator",
                    "emulator",
                    required=True,
                    allow_internal_file_symlink=True,
                )

    def test_collector_records_missing_build_emulator_without_invoking_path(self) -> None:
        sdk = self.root / "build-sdk-without-emulator"
        for relative in (
            "cmdline-tools/latest/bin/sdkmanager",
            "platform-tools/adb",
        ):
            path = sdk / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("tool\n", encoding="utf-8")
            path.chmod(0o755)
        commands: list[tuple[str, ...]] = []
        inventory = """Installed packages:
Path | Version | Description | Location
build-tools;36.0.0 | 36.0.0 | build tools | private/location
platform-tools | 36.0.0 | platform tools | private/location
platforms;android-36 | 2 | platform | private/location
Available Packages:
"""

        def run(command: tuple[str, ...]) -> str:
            commands.append(command)
            if command == ("java", "-version"):
                return 'openjdk version "17.0.16" 2026-07-15'
            if command == ("javac", "-version"):
                return "javac 17.0.16"
            if command == ("dotnet", "--version"):
                return "10.0.110"
            if command == ("dotnet", "--info"):
                return ".NET SDK:\n Version: 10.0.110\n RID: linux-x64"
            if command[-1] == "--list_installed":
                return inventory
            if command[-1] == "version":
                return (
                    "Android Debug Bridge version 1.0.41\n"
                    "Version 36.0.0-13206524"
                )
            raise AssertionError(command)

        proc_version = self.root / "build-proc-version"
        proc_version.write_text("Linux version hosted\n", encoding="utf-8")
        kvm_module = self.root / "build-kvm-module"
        kvm_module.mkdir()
        observation = MODULE.collect_environment(
            sdk,
            {
                "RUNNER_OS": "Linux",
                "RUNNER_ARCH": "X64",
                "ImageOS": "ubuntu24",
                "ImageVersion": "20260901.1.0",
            },
            emulator_required=False,
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
        MODULE.validate_environment(observation, self.policy, "build")
        self.assertEqual(
            {
                "available": False,
                "version": None,
                "buildId": None,
                "versionOutputSha256": MODULE.canonical_sha256(
                    {"available": False}
                ),
                "rawObservationSha256": MODULE.EMPTY_SHA256,
                "rawObservationSizeBytes": 0,
            },
            observation["androidSdk"]["emulator"],
        )
        self.assertFalse(
            any(
                command[-1] == "-version"
                and command[0].endswith("/emulator/emulator")
                for command in commands
            )
        )
        unexpected_observation = self.root / "unexpected-build-emulator.txt"
        unexpected_observation.write_text(
            "Android emulator version 36.2.11.0 (build_id 15917651) (CL:N/A)\n",
            encoding="utf-8",
        )
        with self.assertRaisesRegex(ValueError, "must not accept"):
            MODULE.collect_environment(
                sdk,
                {
                    "RUNNER_OS": "Linux",
                    "RUNNER_ARCH": "X64",
                    "ImageOS": "ubuntu24",
                    "ImageVersion": "20260901.1.0",
                },
                emulator_required=False,
                emulator_version_observation=MODULE.StableFile(
                    unexpected_observation,
                    "unexpected build emulator observation",
                ),
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
        self.assertIn(
            '--emulator-version-observation "$RUNNER_TEMP/chummer-api36-emulator-version.txt"',
            runner,
        )
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
        self.assertEqual(
            2,
            workflow.count(
                "DOTNET_INSTALL_DIR: ${{ runner.temp }}/chummer-api36-dotnet-10.0.110"
            ),
        )
        self.assertEqual(2, workflow.count('DOTNET_MULTILEVEL_LOOKUP: "0"'))
        self.assertEqual(
            2,
            workflow.count('test "$(command -v dotnet)" = "$DOTNET_ROOT/dotnet"'),
        )
        self.assertEqual(
            2,
            workflow.count('test "${installed_sdks[0]}" = "10.0.110"'),
        )
        self.assertEqual(
            2,
            workflow.count(
                'test "$("$DOTNET_ROOT/dotnet" --version)" = "10.0.110"'
            ),
        )
        self.assertIn(
            'run: \'"$DOTNET_ROOT/dotnet" workload restore '
            "chummer-android/Chummer.Android.slnx\'",
            workflow,
        )
        self.assertIn("build-environment-receipt.json", workflow)
        self.assertIn("--build-environment-receipt", workflow)
        self.assertEqual(1, workflow.count("pre-emulator-launch-script:"))
        self.assertEqual(
            1,
            workflow.count(
                '"$ANDROID_HOME/emulator/emulator" -version'
            ),
        )
        self.assertEqual(
            3,
            workflow.count("chummer-api36-emulator-version.txt"),
        )
        self.assertNotIn("emulator_version_observation=", workflow)
        self.assertIn(
            '"$ANDROID_HOME/emulator/emulator" -version >"$RUNNER_TEMP/chummer-api36-emulator-version.txt" 2>&1',
            workflow,
        )

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
