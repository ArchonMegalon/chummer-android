from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile


ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ROOT / "scripts"
sys.path.insert(0, str(SCRIPTS))
import api36_arm64_physical_contract as contract


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


class Api36Arm64PhysicalContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.apk = self.root / "com.myexternalbrain.chummer-Signed.apk"
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("lib/arm64-v8a/libmonodroid.so", b"arm64")
        self.graph = self.root / "release-source-graph.json"
        write_json(self.graph, self.graph_payload())
        self.provenance = self.root / "build-provenance.json"
        write_json(self.provenance, self.provenance_payload())
        self.device = self.root / "device.json"
        write_json(self.device, self.device_payload())
        self.raw: dict[str, Path] = {}
        self.restart: dict[str, Path] = {}
        self.seal: dict[str, Path] = {}
        for index, journey in enumerate(contract.JOURNEY_ORDER):
            restart_pid = str(200 + index)
            before_pid = str(100 + index)
            raw = self.root / f"{journey}-raw.json"
            write_json(raw, self.raw_payload(journey, restart_pid))
            restart = self.root / f"{journey}-restart.txt"
            restart.write_text(
                f"pre_force_stop_process_ids={before_pid}\n"
                f"pre_force_stop_resumed_component={contract.PACKAGE}/crc.MainActivity\n"
                "post_force_stop_process_ids=\n"
                f"restart_process_ids={restart_pid}\n"
                f"restart_resumed_component={contract.PACKAGE}/crc.MainActivity\n",
                encoding="utf-8",
            )
            self.raw[journey] = raw
            self.restart[journey] = restart
            self.seal[journey] = self.root / f"{journey}-seal.json"
        self.driver_authority = {
            "schema": contract.DRIVER_AUTHORITY_SCHEMA,
            "integrationBaseCommit": contract.INTEGRATION_BASE_COMMIT,
            "integrationBaseTree": contract.INTEGRATION_BASE_TREE,
            "repositoryCommit": "a" * 40, "repositoryTree": "b" * 40,
            "publicationAuthorized": False, "drivers": [],
            "authoritySha256": "c" * 64,
        }
        self.driver_patch = mock.patch.object(
            contract, "capture_driver_authority", return_value=self.driver_authority,
        )
        self.driver_patch.start()
        dummy = contract.bind_regular(self.provenance, "fixture provenance reference")
        self.reference_patches = [
            mock.patch.object(
                contract, "capture_build_provenance_references",
                return_value=contract.BuildProvenanceReferences(
                    dummy, dummy, dummy, dummy, dummy, dummy, dummy, dummy,
                    tuple(
                        (field, dummy)
                        for field in sorted(contract.WP1_REFERENCE_EVIDENCE_FILES)
                    ),
                ),
            ),
            mock.patch.object(contract, "_validate_package_authority_references"),
            mock.patch.object(contract, "_validate_content_references"),
            mock.patch.object(contract, "_validate_referenced_provenance_bytes"),
            mock.patch.object(
                contract, "TRUSTED_TOOLCHAIN_SIZE_BYTES",
                {field: 8 for field in contract.TRUSTED_TOOLCHAIN_SIZE_BYTES},
            ),
            mock.patch.object(contract, "TRUSTED_JAVA_VERSION_LINES", {
                "java": 'openjdk version "17.0.14"', "javac": "javac 17.0.14",
            }),
            mock.patch.object(contract, "TRUSTED_JDK_RELEASE_VALUES", {
                "IMPLEMENTOR": "Microsoft", "IMPLEMENTOR_VERSION": "Microsoft-10800290",
                "JAVA_RUNTIME_VERSION": "17.0.14+7-LTS", "JAVA_VERSION": "17.0.14",
                "JAVA_VERSION_DATE": "2025-01-21", "LIBC": "gnu",
                "MODULES": "java.base", "OS_ARCH": "x86_64", "OS_NAME": "Linux",
                "SOURCE": ".:git:fixture",
            }),
            mock.patch.object(
                contract, "TRUSTED_CORE_CONTENT_TREE",
                next(
                    row["tree"] for row in self.graph_payload()["repositories"]
                    if row["name"] == "chummer6-core"
                ),
            ),
        ]
        for patcher in self.reference_patches:
            patcher.start()

    def tearDown(self) -> None:
        for patcher in reversed(self.reference_patches):
            patcher.stop()
        self.driver_patch.stop()
        self.temporary.cleanup()

    @staticmethod
    def graph_payload() -> dict[str, object]:
        repositories = []
        for index, (name, role, repository) in enumerate(zip(
            contract.REPOSITORY_NAMES, contract.REPOSITORY_ROLES,
            contract.REPOSITORY_URLS, strict=True,
        ), start=1):
            repositories.append({
                "name": name, "role": role, "commit": f"{index:040x}",
                "tree": f"{index + 20:040x}", "tree_sha256": f"{index + 40:064x}",
                "repository": repository,
            })
        repository_map = {row["name"]: row for row in repositories}
        package_pins = [
            {
                "package_id": package_id, "version": "1.0.0",
                "sha256": f"{index + 100:064x}", "repository": "chummer6-core",
                "commit": repository_map["chummer6-core"]["commit"],
            }
            for index, package_id in enumerate(contract.CORE_PACKAGE_IDS)
        ]
        owner_pins = [
            {
                "package_id": package_id, "version": "1.0.0",
                "sha256": f"{index + 200:064x}", "size_bytes": index + 1,
                "owner_repository": owner,
                "source_commit": repository_map[owner]["commit"],
                "source_tree": repository_map[owner]["tree"],
                "authority_receipt_sha256": f"{index + 300:064x}",
                "package_inventory_sha256": f"{index + 400:064x}",
                "package_plane_lock_sha256": f"{index + 500:064x}",
                "dependency_mode": "locked_package",
            }
            for index, (package_id, owner) in enumerate(contract.OWNER_PACKAGE_SPECS)
        ]
        return {
            "contractName": contract.SOURCE_GRAPH_SCHEMA,
            "generatedAtUtc": "2026-08-28T00:00:00Z",
            "authorityState": "local_review_required",
            "publicationAuthorized": False,
            "generator": {"path": "scripts/verify_release_source_graph.py", "sha256": "1" * 64, "size_bytes": 1},
            "repositories": repositories, "packagePins": package_pins,
            "ownerPackagePins": owner_pins,
            "dependencyClosure": [
                {"package_id": package_id, "dependencies": []}
                for package_id, _owner in contract.OWNER_PACKAGE_SPECS
            ],
            "presentationSource": {
                "repository": "chummer6-ui", "commit": repository_map["chummer6-ui"]["commit"],
                "tree": repository_map["chummer6-ui"]["tree"],
                "source_path": "chummer-presentation", "authority_state": "local_review_required",
                "publication_authorized": False, "dependency_mode": "source_compatibility",
            },
            "doesNotAssert": list(contract.SOURCE_GRAPH_DOES_NOT_ASSERT),
        }

    def provenance_payload(self) -> dict[str, object]:
        apk_bytes = self.apk.read_bytes()
        binding = {"sha256": "8" * 64, "sizeBytes": 8}
        trusted = contract.TRUSTED_TOOLCHAIN_SHA256
        package_binding = {"sha256": "7" * 64, "sizeBytes": 7}
        repository_map = {
            row["name"]: row for row in self.graph_payload()["repositories"]
        }
        execution_evidence = {
            field: dict(binding)
            for field in contract.WP1_EXECUTION_EVIDENCE_FIELDS
            if field not in {"boundedProcessGroups", "warnings", "errors"}
        }
        execution_evidence.update({"boundedProcessGroups": True, "warnings": 0, "errors": 0})
        toolchain = {
            "dotnetSdkVersion": "10.0.111", "dotnetRuntimeVersion": "10.0.11",
            "workloadSetVersion": "10.0.110.1", "dotnetHost": {
                **binding, "sha256": trusted["dotnet"],
            },
            "dotnetWorkloads": {
                **binding, "installed": ["maui-android"], "updateAvailable": [],
                "workloadSetVersion": "10.0.110.1",
                "manifestVersions": {
                    "maui-android": "10.0.20/10.0.100",
                    "microsoft.net.sdk.android": "36.1.69",
                },
                "runtimeVersion": "10.0.11",
            },
            "workloadManifests": {
                "android": {**binding, "sha256": trusted["android_workload_manifest"], "version": "36.1.69"},
                "maui": {**binding, "sha256": trusted["maui_workload_manifest"], "version": "10.0.20"},
            },
            "java": {**binding, "sha256": trusted["java"], "version": "17.0.14", "versionLine": 'openjdk version "17.0.14"'},
            "javac": {**binding, "sha256": trusted["javac"], "version": "17.0.14", "versionLine": "javac 17.0.14"},
            "jarsigner": {**binding, "sha256": trusted["jarsigner"]},
            "keytool": {**binding, "sha256": trusted["keytool"]},
            "jdkRelease": {**binding, "sha256": trusted["jdk_release"], "fields": {
                "IMPLEMENTOR": "Microsoft", "IMPLEMENTOR_VERSION": "Microsoft-10800290",
                "JAVA_RUNTIME_VERSION": "17.0.14+7-LTS", "JAVA_VERSION": "17.0.14",
                "JAVA_VERSION_DATE": "2025-01-21", "LIBC": "gnu", "MODULES": "java.base",
                "OS_ARCH": "x86_64", "OS_NAME": "Linux", "SOURCE": ".:git:fixture",
            }},
            "androidSdk": {
                "root": "/home/tibor/.cache/chummer-android-toolchain/android-sdk",
                "selectedInventory": dict(binding),
                "installedPackages": {
                    "platforms;android-36": {**binding, "sha256": trusted["platform_package"], "revision": "2.0.0"},
                    "build-tools;36.0.0": {**binding, "sha256": trusted["build_tools_package"], "revision": "36.0.0"},
                    "platform-tools": {**binding, "sha256": trusted["platform_tools_package"], "revision": "36.0.0"},
                },
                "androidJar": {**binding, "sha256": trusted["android_jar"]},
                "aapt2": {**binding, "sha256": trusted["aapt2"]},
                "zipalign": {**binding, "sha256": trusted["zipalign"]},
                "adb": {**binding, "sha256": trusted["adb"]},
                "apksigner": {**binding, "sha256": trusted["apksigner"]},
                "apksignerJar": {**binding, "sha256": trusted["apksigner_jar"]},
            },
            "androidBuildToolsVersion": "36.0.0", "androidPlatformLabel": "Android 16",
            "targetFramework": contract.TARGET_FRAMEWORK, "targetSdkVersion": 36,
            "runtimeIdentifier": contract.RUNTIME_IDENTIFIER, "configuration": "Debug",
            "serializedBuild": True,
        }
        authority: dict[str, object] = {
            "schema": contract.BUILD_PROVENANCE_SCHEMA,
            "status": "pass",
            "authorityClass": "internal_phone_beta_physical_candidate_only",
            "publicationAuthorized": False,
            "proofScope": "full_maui_arm64_apk_build_only",
            "dependencyMode": "locked_current_packages_no_owner_siblings",
            "sourceHead": {
                "commit": repository_map["chummer-android"]["commit"],
                "tree": repository_map["chummer-android"]["tree"],
                "repository": "https://github.com/ArchonMegalon/chummer-android.git",
                "publicationAuthorized": False,
            },
            "presentationBuildSource": {
                "commit": repository_map["chummer6-ui"]["commit"],
                "tree": repository_map["chummer6-ui"]["tree"],
                "authorityClass": "verified_current_ui_source",
                "productionSource": False, "publicationAuthorized": False,
                "packagePlaneLock": dict(binding), "producerLock": dict(binding),
                "remoteRef": "refs/remotes/origin/main",
            },
            "packageAuthority": {
                **package_binding,
                "contractName": "chummer.android.internal-phone-beta-package-authority/v2",
                "authorityState": "current_graph_verified",
                "sourceGraph": {
                    "corePackageRecipeCommit": "6" * 40,
                    "coreRuntimeSourceCommit": repository_map["chummer6-core"]["commit"],
                    "hubProducerCommit": repository_map["chummer6-hub"]["commit"],
                    "registryCommit": repository_map["chummer6-hub-registry"]["commit"],
                    "uiKitCommit": repository_map["chummer6-ui-kit"]["commit"],
                },
                "uiReceipt": dict(binding), "cacheManifest": dict(binding),
                "intakeBinding": dict(package_binding),
                "postBuildBinding": dict(package_binding),
            },
            "content": {
                "sourceReceipt": dict(binding), "apkReceipt": dict(binding),
                "coreRevision": "6" * 40,
                "bundleDigest": "5" * 64, "manifestSha256": "4" * 64,
                "canonicalFileCount": 1, "canonicalByteCount": 1,
                "sourceRepository": {
                    "commit": "6" * 40,
                    "tree": repository_map["chummer6-core"]["tree"],
                },
            },
            "restore": {
                "lockedMode": True, "networkSourcesAllowed": False,
                "ownerSourceFallbackAllowed": False,
                "fullProjectLock": dict(binding), "projectAssets": dict(binding),
            },
            "executionEvidence": execution_evidence,
            "toolchain": toolchain,
            "artifact": {
                "basename": self.apk.name,
                "sha256": hashlib.sha256(apk_bytes).hexdigest(),
                "sizeBytes": len(apk_bytes),
                "package": contract.PACKAGE,
                "abis": [contract.ABI],
                "apiLevel": 36,
                "configuration": "Debug",
                "runtimeIdentifier": contract.RUNTIME_IDENTIFIER,
                "targetFramework": contract.TARGET_FRAMEWORK,
                "fullMauiArtifact": True,
                "installed": False,
                "signing": {
                    "certificateSha256": "9" * 64,
                    "verifiedSchemes": [2, 3],
                    "receipt": dict(binding),
                },
            },
            "doesNotAssert": list(contract.WP1_DOES_NOT_ASSERT),
        }
        return {
            **authority,
            "authoritySha256": contract.canonical_sha256(authority),
            "generatedAtUtc": "2026-08-28T00:00:01Z",
        }

    @staticmethod
    def device_payload() -> dict[str, object]:
        serial = "R5CT30PHYSICAL"
        properties = {
            "ro.boot.qemu": "",
            "ro.boot.verifiedbootstate": "green",
            "ro.build.fingerprint": "google/tokay/tokay:16/BP2A/release:user/release-keys",
            "ro.build.id": "BP2A",
            "ro.build.version.security_patch": "2026-08-05",
            "ro.build.version.sdk": "36",
            "ro.hardware": "tensor",
            "ro.kernel.qemu": "",
            "ro.product.cpu.abi": contract.ABI,
            "ro.product.cpu.abilist": "arm64-v8a,armeabi-v7a",
            "ro.product.device": "tokay",
            "ro.product.manufacturer": "Google",
            "ro.product.model": "Pixel 9",
            "ro.product.name": "tokay",
        }
        return {
            "schema": contract.DEVICE_SCHEMA,
            "status": "pass",
            "classification": "physical_api36_arm64_non_emulator",
            "publicationAuthorized": False,
            "serial": serial,
            "serialSha256": hashlib.sha256(serial.encode()).hexdigest(),
            "apiLevel": 36,
            "abi": contract.ABI,
            "abiList": ["arm64-v8a", "armeabi-v7a"],
            "properties": properties,
            "observationNature": "stable-twice non-cryptographic adb/getprop observation",
            "capturedAtUtc": "2026-08-28T00:00:02Z",
        }

    @staticmethod
    def workspace_payload() -> dict[str, object]:
        return {
            "workspaceId": "workspace-1", "contentRevision": 2, "savedRevision": 2,
            "payloadSha256": "1" * 64, "documentSha256": "2" * 64,
        }

    def raw_device_payload(self, journey: str) -> dict[str, object]:
        device = self.device_payload()
        properties = device["properties"]
        value = {
            "classification": "non-emulator-arm64-api36",
            "evidenceNature": "non-cryptographic getprop and adb serial observations",
            "serial": device["serial"], "apiLevel": 36, "abi": contract.ABI,
            "abiList": properties["ro.product.cpu.abilist"],
            "qemu": properties["ro.kernel.qemu"],
            "manufacturer": properties["ro.product.manufacturer"],
            "model": properties["ro.product.model"], "hardware": properties["ro.hardware"],
            "buildFingerprint": properties["ro.build.fingerprint"],
            "buildId": properties["ro.build.id"],
            "securityPatch": properties["ro.build.version.security_patch"],
            "verifiedBootState": properties["ro.boot.verifiedbootstate"],
        }
        if journey == "priority":
            value.update({
                "bootQemu": properties["ro.boot.qemu"],
                "productDevice": properties["ro.product.device"],
                "productName": properties["ro.product.name"],
            })
        return value

    def adb_transport_payload(self) -> dict[str, object]:
        observations = [
            {"index": index, "status": "stable", "getState": "device", "apiLevel": "36"}
            for index in range(1, 4)
        ]
        return {
            "schema": "chummer.android.adb-transport-summary/v1", "status": "pass",
            "preflight": {
                "schema": "chummer.android.adb-transport-preflight/v1", "status": "pass",
                "serial": self.device_payload()["serial"], "expectedApiLevel": "36",
                "requiredConsecutiveObservations": 3, "maximumObservations": 7,
                "observationDelaySeconds": 1.0, "observationsPerformed": 3,
                "consecutiveStableObservations": 3, "mutationCommandsIssued": 0,
                "recoveryPolicy": "bounded-read-only-observation-retry",
                "recoveryMechanism": "fresh-adb-invocation-no-reconnect-command",
                "observations": observations,
            },
            "eventCount": 0, "terminalFailureCount": 0, "events": [],
            "readOnlyMaximumAttempts": 3, "readOnlyRetryDelaySeconds": 1.0,
            "preflightObservationDelaySeconds": 1.0,
            "explicitAdbReconnectCommandAllowed": False,
            "nonReplayableCommandMaximumAttempts": 1,
        }

    def recovered_read_only_transport_payload(self) -> dict[str, object]:
        payload = self.adb_transport_payload()
        serial = self.device_payload()["serial"]
        arguments = contract.ADB_FILE_HIERARCHY_OBSERVATION_ARGUMENTS
        arguments_sha256 = hashlib.sha256(
            "\0".join(arguments).encode("utf-8")
        ).hexdigest()

        def retrying(attempt: int) -> dict[str, object]:
            return {
                "schema": "chummer.android.adb-transport-event/v1",
                "status": "retrying-read-only",
                "serial": serial,
                "classification": "timeout-unknown-outcome",
                "classificationAuthority": "timeout-with-unknown-command-outcome",
                "retryableTransportClassification": True,
                "commandPolicy": "read-only-retryable",
                "policyReason": "exact remote-file byte observation",
                "adbArguments": list(arguments),
                "adbArgumentsSha256": arguments_sha256,
                "attempt": attempt,
                "maximumAttempts": 3,
                "commandInvocationPerformed": True,
                "outcomeMutationAuthority": "none-read-only-command",
                "replay": {
                    "eligible": True, "performed": attempt > 1,
                    "scheduled": True, "suppressed": False,
                },
                "failure": {
                    "type": "TimeoutExpired", "returnCode": None,
                    "stdout": "", "stderr": "",
                },
                "evidenceFile": f"adb-transport-event-{attempt:04d}.json",
            }

        recovered = {
            "schema": "chummer.android.adb-transport-event/v1",
            "status": "recovered-read-only",
            "serial": serial,
            "classification": "transport-recovered",
            "classificationAuthority": "fresh-read-only-command-succeeded",
            "retryableTransportClassification": True,
            "commandPolicy": "read-only-retryable",
            "policyReason": "exact remote-file byte observation",
            "adbArguments": list(arguments),
            "adbArgumentsSha256": arguments_sha256,
            "attempt": 3,
            "maximumAttempts": 3,
            "commandInvocationPerformed": True,
            "outcomeMutationAuthority": "none-read-only-command",
            "replay": {
                "eligible": True, "performed": True,
                "scheduled": False, "suppressed": False,
            },
            "failure": None,
            "evidenceFile": "adb-transport-event-0003.json",
        }
        events = [retrying(1), retrying(2), recovered]
        payload.update({
            "eventCount": len(events),
            "terminalFailureCount": 0,
            "events": events,
        })
        return payload

    def reconciled_hierarchy_dump_transport_payload(
        self,
        *,
        observation_mode: str = "fresh-owned-file",
    ) -> dict[str, object]:
        payload = self.adb_transport_payload()
        serial = self.device_payload()["serial"]
        original = {
            "schema": "chummer.android.adb-transport-event/v1",
            "status": "fail",
            "serial": serial,
            "classification": "timeout-unknown-outcome",
            "classificationAuthority": "timeout-with-unknown-command-outcome",
            "retryableTransportClassification": True,
            "commandPolicy": "non-replayable",
            "policyReason": "shell mutation or ambiguous shell command is never replayed",
            "adbArguments": list(
                contract.ADB_FILE_HIERARCHY_DUMP_REDACTED_ARGUMENTS
            ),
            "adbArgumentsSha256": (
                contract.ADB_FILE_HIERARCHY_DUMP_ARGUMENTS_SHA256
            ),
            "attempt": 1,
            "maximumAttempts": 1,
            "commandInvocationPerformed": True,
            "outcomeMutationAuthority": "unknown-fail-closed",
            "replay": {
                "eligible": False, "performed": False,
                "scheduled": False, "suppressed": True,
            },
            "failure": {
                "type": "TimeoutExpired", "returnCode": None,
                "stdout": "", "stderr": "",
            },
            "evidenceFile": "adb-transport-event-0001.json",
        }
        reconciliation = {
            "schema": "chummer.android.adb-transport-event/v1",
            "status": "reconciled-unknown-hierarchy-dump",
            "serial": serial,
            "classification": "timeout-unknown-outcome",
            "classificationAuthority": (
                "bounded-consecutive-read-only-hierarchy-observations"
            ),
            "retryableTransportClassification": True,
            "commandPolicy": "non-replayable",
            "policyReason": (
                "file-backed dump was never replayed; bounded stable current "
                "hierarchy became observation authority"
            ),
            "adbArguments": list(
                contract.ADB_FILE_HIERARCHY_DUMP_REDACTED_ARGUMENTS
            ),
            "adbArgumentsSha256": (
                contract.ADB_FILE_HIERARCHY_DUMP_ARGUMENTS_SHA256
            ),
            "attempt": 1,
            "maximumAttempts": 1,
            "commandInvocationPerformed": False,
            "outcomeMutationAuthority": (
                "current-hierarchy-observed-no-dump-replay"
            ),
            "replay": {
                "eligible": False, "performed": False,
                "scheduled": False, "suppressed": True,
            },
            "failure": None,
            "reconcilesEvidenceFile": original["evidenceFile"],
            "readOnlyObservation": {
                "mode": observation_mode,
                "arguments": list(
                    contract.ADB_FILE_HIERARCHY_OBSERVATION_ARGUMENTS
                    if observation_mode == "fresh-owned-file"
                    else contract.ADB_READ_ONLY_HIERARCHY_ARGUMENTS
                ),
                "freshnessBarrierArguments": list(
                    contract.ADB_FILE_HIERARCHY_REMOVE_ARGUMENTS
                ),
                "consecutiveMatching": 2,
                "observationsPerformed": 2,
                "hierarchySha256": "1" * 64,
                "observationBytesSha256": "2" * 64,
            },
            "evidenceFile": "adb-transport-event-0002.json",
        }
        payload.update({
            "eventCount": 2,
            "terminalFailureCount": 0,
            "events": [original, reconciliation],
        })
        return payload

    def reconciled_swipe_transport_payload(self) -> dict[str, object]:
        payload = self.adb_transport_payload()
        serial = self.device_payload()["serial"]
        swipe_arguments = (
            "shell", "input", "swipe", "540", "1968", "540", "720", "300",
        )
        swipe_digest = hashlib.sha256(
            "\0".join(swipe_arguments).encode("utf-8")
        ).hexdigest()
        original = {
            "schema": "chummer.android.adb-transport-event/v1",
            "status": "fail",
            "serial": serial,
            "classification": "timeout-unknown-outcome",
            "classificationAuthority": "timeout-with-unknown-command-outcome",
            "retryableTransportClassification": True,
            "commandPolicy": "non-replayable",
            "policyReason": "shell mutation or ambiguous shell command is never replayed",
            "adbArguments": list(contract.ADB_SWIPE_REDACTED_ARGUMENTS),
            "adbArgumentsSha256": swipe_digest,
            "attempt": 1,
            "maximumAttempts": 1,
            "commandInvocationPerformed": True,
            "outcomeMutationAuthority": "unknown-fail-closed",
            "replay": {
                "eligible": False, "performed": False,
                "scheduled": False, "suppressed": True,
            },
            "failure": {
                "type": "TimeoutExpired", "returnCode": None,
                "stdout": "", "stderr": "",
            },
            "evidenceFile": "adb-transport-event-0001.json",
        }
        reconciliation = {
            "schema": "chummer.android.adb-transport-event/v1",
            "status": "reconciled-unknown-swipe",
            "serial": serial,
            "classification": "timeout-unknown-outcome",
            "classificationAuthority": (
                "bounded-consecutive-read-only-hierarchy-observations"
            ),
            "retryableTransportClassification": True,
            "commandPolicy": "non-replayable",
            "policyReason": (
                "swipe was never replayed; current viewport became authority"
            ),
            "adbArguments": list(contract.ADB_SWIPE_REDACTED_ARGUMENTS),
            "adbArgumentsSha256": swipe_digest,
            "attempt": 1,
            "maximumAttempts": 1,
            "commandInvocationPerformed": False,
            "outcomeMutationAuthority": "current-viewport-observed-no-replay",
            "replay": {
                "eligible": False, "performed": False,
                "scheduled": False, "suppressed": True,
            },
            "failure": None,
            "reconcilesEvidenceFile": original["evidenceFile"],
            "readOnlyObservation": {
                "arguments": list(contract.ADB_READ_ONLY_HIERARCHY_ARGUMENTS),
                "consecutiveMatching": 2,
                "observationsPerformed": 2,
                "hierarchySha256": "3" * 64,
            },
            "evidenceFile": "adb-transport-event-0002.json",
        }
        payload.update({
            "eventCount": 2,
            "terminalFailureCount": 0,
            "events": [original, reconciliation],
        })
        return payload

    def source_authority_payload(self, journey: str) -> dict[str, object]:
        authority = {
            "expectedAndroidSourceRevision": "1" * 40,
            "androidSourceRevision": "1" * 40,
            "expectedPresentationSourceRevision": "2" * 40,
            "presentationSourceRevision": "2" * 40,
            "expectedCoreSourceRevision": "3" * 40,
            "coreSourceRevision": "3" * 40,
            "expectedApkSha256": hashlib.sha256(self.apk.read_bytes()).hexdigest(),
            "apkSha256": hashlib.sha256(self.apk.read_bytes()).hexdigest(),
            "apkAbis": [contract.ABI],
            "sourceFileSha256": {
                field: hashlib.sha256(field.encode()).hexdigest()
                for field in contract.SOURCE_FILE_FIELDS[journey]
            },
        }
        return {**authority, "authoritySha256": contract.canonical_sha256(authority)}

    def priority_proof(self, restart_pid: str) -> dict[str, object]:
        stages = []
        for step in (
            "basics", "method", "foundation", "attributes", "qualities", "skills",
            "magic-resonance", "resources", "contacts-lifestyles", "identity-story",
        ):
            stage = {
                "stepId": step, "routeId": f"creation-stage-{step}",
                "requiredByCurrentFinalizer": step in {"method", "attributes", "qualities", "skills", "magic-resonance", "resources"},
                "routeStatus": "typed-authority-visible", "authorityVisible": True,
                "draftFabricated": False,
            }
            if step == "identity-story":
                stage.update({
                    "routeStatus": "typed-contract-unavailable", "authorityVisible": False,
                    "blocker": "creation-identity-draft-contract-unavailable",
                })
            else:
                stage.update({"pageId": f"page-{step}", "authorityId": f"authority-{step}"})
                extra = {
                    "basics": ("sourcebookMutation", "typed-contract-unavailable"),
                    "method": ("buildMethod", "Priority"),
                    "resources": ("gearDraft", "persisted-typed-authority"),
                    "contacts-lifestyles": ("lifestylesAuthority", "visible"),
                }.get(step)
                if extra:
                    stage[extra[0]] = extra[1]
            stages.append(stage)
        receipt_digest = "9" * 64
        before_pid = str(100 + contract.JOURNEY_ORDER.index("priority"))
        return {
            "stages": stages, "identityGap": stages[-1],
            "draftStateAuthority": "typed-phone-pages-preexisting-no-seed-or-fabrication",
            "finalization": {
                "review": "sealed-core-whole-build-plan",
                "visibleReviewEvidence": {
                    "creation-finalization-binding": "binding",
                    "creation-finalization-costs": "costs",
                    "creation-finalization-atomic-boundary": "atomic",
                },
                "sealedPlanAuthority": {"contentRevision": 1, "planDigest": "3" * 64, "previewDigest": "4" * 64},
                "receiptAuthority": {
                    "previousContentRevision": 1, "contentRevision": 2, "savedRevision": 2,
                    "buildMethod": "Priority", "planDigest": "3" * 64,
                    "previewDigest": "4" * 64, "receiptDigest": receipt_digest,
                },
                "confirmation": "explicit-atomic-once", "receipt": "durable", "careerReopen": "verified",
            },
            "savedCareerWorkspace": self.workspace_payload(),
            "restoredCareerWorkspace": self.workspace_payload(),
            "persistedCreationReceiptDigest": receipt_digest,
            "restoredCreationReceiptDigest": receipt_digest,
            "processRestart": {
                "beforeProcessIds": [before_pid], "afterForceStopProcessIds": [],
                "restartedProcessIds": [restart_pid], "newPidVerified": True,
            },
        }

    def career_checkpoint(self) -> dict[str, object]:
        integer_fields = {
            "SchemaVersion", "Version", "Kind", "ExpectedContentRevision", "BasePoints",
            "PreviousKarmaPoints", "RatingMaximum", "ExpenseAmount", "UndoQuantity",
            "PreviousRating", "TargetRating", "SavedKarma", "Phase",
        }
        boolean_fields = {"ExpenseRefund", "ExpenseForceCareerVisible"}
        return {
            field: (1 if field in integer_fields else False if field in boolean_fields else "value")
            for field in contract.CAREER_CHECKPOINT_FIELDS
        }

    def career_proof(self, restart_pid: str) -> dict[str, object]:
        index = contract.JOURNEY_ORDER.index("career")
        return {
            "import": self.workspace_payload(), "restoredBeforeApply": self.workspace_payload(),
            "restoredAfterApply": self.workspace_payload(),
            "finalRestoredAfterAcknowledgement": self.workspace_payload(),
            "reviewedCheckpoint": self.career_checkpoint(), "reviewedCheckpointSha256": "3" * 64,
            "appliedCheckpoint": self.career_checkpoint(), "appliedCheckpointSha256": "4" * 64,
            "receiptProjection": {
                key: "5" * 64 for key in (
                    "skill", "source", "source_digest", "reviewed_rule", "loaded_rule",
                    "loaded_quote", "owner", "action",
                )
            },
            "generatedExpenseGuid": "expense-guid",
            "restartProcessIds": [[str(300 + index * 3)], [str(301 + index * 3)], [restart_pid]],
        }

    def lane_proof(self, journey: str, restart_pid: str) -> dict[str, object]:
        index = contract.JOURNEY_ORDER.index(journey)
        scope = {"representativeAction": "one exact action", "excluded": ["tablet"], "claim": "one representative typed action only"}
        if journey == "before-run":
            ids = ("before-run.edge.spend", "before-run.edge.regain")
            contracts = {
                action_id: {
                    "actionId": action_id, "kind": "SpendEdge", "edgeUsedBefore": 1,
                    "edgeUsedAfter": 2, "totalEdge": 4, "targetRevision": "6" * 64,
                    "actionDigest": "7" * 64, "automationId": f"auto-{offset}",
                }
                for offset, action_id in enumerate(ids)
            }
        else:
            ids = ("playtime.weapon.fire",)
            contracts = {
                ids[0]: {
                    "actionId": ids[0], "kind": "FireWeapon", "weaponId": "weapon",
                    "ammoSlot": 1, "ammoGearId": "ammo", "fireMode": "ShortBurst",
                    "roundsConsumed": 3, "ammoBefore": 8, "ammoAfter": 5,
                    "targetRevision": "6" * 64, "actionDigest": "7" * 64,
                    "automationId": "auto-0",
                }
            }
        return {
            "scope": scope, "import": self.workspace_payload(),
            "restoredBeforeApply": self.workspace_payload(), "savedSuccessor": self.workspace_payload(),
            "finalRestoredSuccessor": self.workspace_payload(), "actionAutomationId": "auto-current",
            "successorActionAutomationIds": [row["automationId"] for row in contracts.values()],
            "successorActionAuthority": contracts, "reviewedTransactionSha256": "8" * 64,
            "appliedTransactionSha256": "9" * 64,
            "receipt": {
                **{field: "a" for field in contract.LANE_RECEIPT_FIELDS},
                "ExpectedWorkspaceRevision": 1, "AppliedWorkspaceRevision": 2,
                "ActionKind": 0,
                "ActionDigest": "a" * 64, "ExpectedPostconditionDigest": "b" * 64,
                "ObservedPostconditionDigest": "b" * 64, "ReceiptDigest": "c" * 64,
            },
            "restartProcessIds": [[str(300 + index * 3)], [str(301 + index * 3)], [restart_pid]],
        }

    @staticmethod
    def after_checkpoint(*, applied: bool) -> dict[str, object]:
        identity = {field: "id" for field in contract.AFTER_IDENTITY_FIELDS}
        quote = {field: "value" for field in contract.AFTER_QUOTE_FIELDS}
        for field in {
            "HeatBefore", "HeatDelta", "HeatAfter", "StreetCredBefore", "StreetCredDelta",
            "StreetCredAfter", "NotorietyBefore", "NotorietyDelta", "NotorietyAfter",
            "PublicAwarenessBefore", "RequestedPublicAwarenessDelta", "PublicAwarenessAfter",
            "KarmaBefore", "ContactKarmaCost", "KarmaAfter", "Blocker",
        }:
            quote[field] = 0
        quote.update({"Identity": identity, "Contacts": [], "Prerequisites": [], "CanSettle": True})
        reward = {field: "value" for field in contract.AFTER_REWARD_CONTEXT_FIELDS}
        reward.update({"Identity": identity, "KarmaAward": 1, "NuyenAward": 1})
        binding = {field: "value" for field in contract.AFTER_BINDING_FIELDS}
        binding.update({"WorkspaceId": {"Value": "workspace-1"}, "WorkspaceRevision": 1, "Identity": identity, "Quote": quote})
        plan = {field: "value" for field in contract.AFTER_PLAN_FIELDS}
        for field in {
            "TargetHeat", "TargetStreetCred", "TargetNotoriety", "TargetPublicAwareness",
            "TargetKarma", "ContactKarmaCost", "ExpenseAmount",
        }:
            plan[field] = 0
        plan.update({"Identity": identity, "ContactsToAdd": []})
        draft = {
            "OwnerId": "owner", "Candidate": {"RewardContext": reward, "Binding": binding},
            "Plan": plan, "Acknowledgements": {field: True for field in contract.AFTER_ACK_FIELDS},
        }
        receipt = None
        if applied:
            receipt = {field: "value" for field in contract.AFTER_RECEIPT_FIELDS}
            for field in {
                "HeatBefore", "HeatAfter", "StreetCredBefore", "StreetCredAfter",
                "NotorietyBefore", "NotorietyAfter", "PublicAwarenessBefore",
                "PublicAwarenessAfter", "KarmaBefore", "KarmaAfter", "ContactKarmaCost",
                "ExpenseAmount",
            }:
                receipt[field] = 0
            receipt.update({"Identity": identity, "AddedContacts": []})
        checkpoint = {field: "value" for field in contract.AFTER_CHECKPOINT_FIELDS}
        checkpoint.update({"SchemaVersion": 1, "Version": 1, "Phase": 2 if applied else 0, "Draft": draft, "Receipt": receipt})
        return checkpoint

    def after_proof(self, restart_pid: str) -> dict[str, object]:
        index = contract.JOURNEY_ORDER.index("after-run")
        return {
            "import": self.workspace_payload(), "restoredBeforeApply": self.workspace_payload(),
            "savedSuccessor": self.workspace_payload(), "finalRestartSuccessor": self.workspace_payload(),
            "reviewedCheckpoint": self.after_checkpoint(applied=False), "reviewedCheckpointSha256": "1" * 64,
            "appliedCheckpoint": self.after_checkpoint(applied=True), "appliedCheckpointSha256": "2" * 64,
            "transactionAndReviewAuthority": {
                "transactionId": "transaction", "gmReviewDigest": "3" * 64,
                "ownerReviewDigest": "4" * 64, "receiptDigest": "5" * 64,
            },
            "restartProcessIds": [[str(300 + index * 3)], [str(301 + index * 3)], [restart_pid]],
        }

    @staticmethod
    def downtime_journal(*, applied: bool) -> dict[str, object]:
        preview = {field: "value" for field in contract.DOWNTIME_PREVIEW_FIELDS}
        preview.update({"Year": 2080, "Week": 1, "Operation": 1})
        review = {field: "value" for field in contract.DOWNTIME_REVIEW_FIELDS}
        review.update({"WorkspaceRevision": 1, "Preview": preview})
        receipt = None if not applied else {field: "value" for field in contract.DOWNTIME_RECEIPT_FIELDS}
        if receipt is not None:
            receipt.update({"ExpectedWorkspaceRevision": 1, "AppliedWorkspaceRevision": 2, "Operation": 1})
        journal = {field: "value" for field in contract.DOWNTIME_JOURNAL_FIELDS}
        journal.update({"SchemaVersion": 1, "Version": 1, "Phase": 2 if applied else 0, "Review": review, "Receipt": receipt})
        return journal

    def downtime_proof(self, restart_pid: str) -> dict[str, object]:
        index = contract.JOURNEY_ORDER.index("downtime")
        return {
            "import": self.workspace_payload(), "restoredBeforeApply": self.workspace_payload(),
            "savedSuccessor": self.workspace_payload(), "finalRestartSuccessor": self.workspace_payload(),
            "reviewedJournal": self.downtime_journal(applied=False), "reviewedJournalSha256": "1" * 64,
            "appliedJournal": self.downtime_journal(applied=True), "appliedJournalSha256": "2" * 64,
            "receiptAuthority": {
                "actionId": "action", "previewDigest": "3" * 64,
                "expectedPostconditionDigest": "4" * 64, "receiptDigest": "5" * 64,
            },
            "restartProcessIds": [[str(300 + index * 3)], [str(301 + index * 3)], [restart_pid]],
        }

    def proof_payload(self, journey: str, restart_pid: str) -> dict[str, object]:
        if journey == "priority":
            return self.priority_proof(restart_pid)
        if journey == "career":
            return self.career_proof(restart_pid)
        if journey in {"before-run", "playtime"}:
            return self.lane_proof(journey, restart_pid)
        if journey == "after-run":
            return self.after_proof(restart_pid)
        return self.downtime_proof(restart_pid)

    def remote_cleanup_payload(self, journey: str) -> object:
        fixture = f"/sdcard/Download/{journey}.chum5"
        hierarchy = "/sdcard/chummer-editing-window.xml"
        if journey in {"before-run", "playtime"}:
            return {fixture: True, hierarchy: True}
        return [
            {
                "path": path, "purpose": "temporary proof input",
                "precleanAttempted": True, "precleaned": True,
                "cleanupAttempted": True, "cleanupReplaySuppressed": False,
                "deletedAndVerified": True,
            }
            for path in (fixture, hierarchy)
        ]

    def raw_payload(self, journey: str, restart_pid: str) -> dict[str, object]:
        schema, raw_journey = contract.JOURNEY_CONTRACTS[journey]
        proof = self.proof_payload(journey, restart_pid)
        common: dict[str, object] = {
            "schema": schema, "status": "device-pass-source-bound", "executionStatus": "pass",
            "releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested",
            "generatedAtUtc": "2026-08-28T00:00:03+00:00", "profile": "phone",
            "journey": raw_journey, "apiLevel": 36, "abi": contract.ABI,
            "deviceObservation": self.raw_device_payload(journey),
            "buildProvenance": self.provenance_payload(),
            "apkSha256": hashlib.sha256(self.apk.read_bytes()).hexdigest(),
            "adbTransport": self.adb_transport_payload(), "authorityProofStages": proof,
        }
        if journey == "priority":
            common.update({
                "releaseAttested": False, "publicationAuthorized": False, "buildMethod": "Priority",
                "serial": self.device_payload()["serial"], "package": contract.PACKAGE,
                "apk": str(self.apk),
                "buildProvenanceFile": {
                    "sha256": hashlib.sha256(self.provenance.read_bytes()).hexdigest(),
                    "size": self.provenance.stat().st_size,
                },
                "buildProvenanceRecheckedAfterRun": True,
                "buildProvenanceFileRecheckedAfterRun": True,
                "disposableDeviceAuthorization": {
                    "authorized": True, "flag": "--allow-destructive-disposable-device",
                    "serial": self.device_payload()["serial"],
                    "scope": "install-apk-and-atomically-finalize-one-pending-runner",
                },
                "physicalDeviceProof": True, "installedArtifactBound": True,
                "draftStateFabricated": False, "identityContractStatus": "typed-contract-unavailable",
            })
        else:
            source = self.source_authority_payload(journey)
            common.update({
                "sourceGraphAuthority": source, "sourceGraphRecheckedAfterRun": True,
                "journeys": {field: "pass" for field in contract.SUBJOURNEY_FIELDS[journey]},
            })
            if journey == "career":
                common.update({
                    "serial": self.device_payload()["serial"], "package": contract.PACKAGE,
                    "apk": str(self.apk), "expectedApkSha256": common["apkSha256"],
                    "apkAbis": [contract.ABI], "androidSourceRevision": source["androidSourceRevision"],
                    "expectedAndroidSourceRevision": source["expectedAndroidSourceRevision"],
                    "presentationSourceRevision": source["presentationSourceRevision"],
                    "coreSourceRevision": source["coreSourceRevision"],
                    "postRunSourceGraphAuthoritySha256": source["authoritySha256"],
                    "careerFixtureSha256": source["sourceFileSha256"]["careerFixtureSha256"],
                    "verifiedRemoteCareerFixtureSha256": source["sourceFileSha256"]["careerFixtureSha256"],
                    "remoteTemporaryFiles": self.remote_cleanup_payload(journey),
                    **source["sourceFileSha256"],
                })
            elif journey in {"before-run", "playtime"}:
                common.update({
                    "careerFixtureSha256": source["sourceFileSha256"]["fixtureSha256"],
                    "verifiedRemoteCareerFixtureSha256": source["sourceFileSha256"]["fixtureSha256"],
                    "remoteTemporaryFilesDeleted": self.remote_cleanup_payload(journey),
                    "scope": proof["scope"],
                })
            else:
                common.pop("adbTransport")
                common.update({
                    "serial": self.device_payload()["serial"],
                    "postRunSourceGraphAuthoritySha256": source["authoritySha256"],
                    "apkAbis": [contract.ABI],
                    "governedFixtureSha256": source["sourceFileSha256"]["fixtureSha256"],
                    "verifiedRemoteRunnerSha256": "6" * 64,
                    "remoteTemporaryFiles": self.remote_cleanup_payload(journey),
                })
                if journey == "after-run":
                    common["materializedRunnerSha256"] = "6" * 64
                else:
                    common["careerRunnerSha256"] = "6" * 64
        self.assertEqual(contract.RAW_FIELDS[journey], set(common))
        return common

    def seal_all(self) -> list[tuple[str, Path, Path, Path]]:
        rows = []
        for journey in contract.JOURNEY_ORDER:
            seal = contract.create_journey_seal(
                journey_id=journey, raw_receipt_path=self.raw[journey],
                restart_evidence_path=self.restart[journey], apk_path=self.apk,
                source_graph_path=self.graph, build_provenance_path=self.provenance,
                device_observation_path=self.device,
                generated_at_utc="2026-08-28T00:00:04Z",
            )
            write_json(self.seal[journey], seal)
            rows.append((journey, self.raw[journey], self.restart[journey], self.seal[journey]))
        return rows

    def test_six_exact_raw_schemas_seal_and_aggregate_round_trip(self) -> None:
        rows = self.seal_all()
        aggregate = contract.create_aggregate(
            journey_inputs=rows, apk_path=self.apk, source_graph_path=self.graph,
            build_provenance_path=self.provenance, device_observation_path=self.device,
            generated_at_utc="2026-08-28T00:00:05Z",
        )
        aggregate_path = self.root / "aggregate.json"
        write_json(aggregate_path, aggregate)
        verified = contract.load_and_verify_aggregate(
            aggregate_path, journey_inputs=rows, apk_path=self.apk,
            source_graph_path=self.graph, build_provenance_path=self.provenance,
            device_observation_path=self.device,
        )
        self.assertEqual(list(contract.JOURNEY_ORDER), verified["journeyOrder"])
        self.assertEqual(6, len(verified["journeys"]))
        self.assertFalse(verified["publicationAuthorized"])
        self.assertEqual(contract.PACKAGE, verified["artifact"]["package"])

    def test_owned_file_hierarchy_dump_reconciliation_is_accepted(self) -> None:
        contract.validate_adb_transport(
            self.reconciled_hierarchy_dump_transport_payload(),
            serial=str(self.device_payload()["serial"]),
            label="priority",
        )

    def test_canonical_read_only_retry_chain_is_accepted(self) -> None:
        contract.validate_adb_transport(
            self.recovered_read_only_transport_payload(),
            serial=str(self.device_payload()["serial"]),
            label="priority",
        )

    def test_read_only_retry_chain_rejects_dangling_and_forged_links(self) -> None:
        def dangling(value: dict[str, object]) -> None:
            value["events"].pop()
            value["eventCount"] = len(value["events"])

        def wrong_command(value: dict[str, object]) -> None:
            recovered = value["events"][-1]
            recovered["adbArguments"] = ["get-state"]
            recovered["adbArgumentsSha256"] = hashlib.sha256(
                b"get-state"
            ).hexdigest()

        def terminal_failure(value: dict[str, object]) -> None:
            terminal = value["events"][-1]
            terminal.update({
                "status": "fail",
                "classification": "timeout-unknown-outcome",
                "classificationAuthority": "timeout-with-unknown-command-outcome",
                "retryableTransportClassification": True,
                "replay": {
                    "eligible": True, "performed": True,
                    "scheduled": False, "suppressed": True,
                },
                "failure": {
                    "type": "TimeoutExpired", "returnCode": None,
                    "stdout": "", "stderr": "",
                },
            })

        def mutating_whole_chain(value: dict[str, object]) -> None:
            arguments = ("shell", "input", "tap", "10", "20")
            arguments_sha256 = hashlib.sha256(
                "\0".join(arguments).encode("utf-8")
            ).hexdigest()
            for event in value["events"]:
                event.update({
                    "policyReason": "forged read-only viewport observation",
                    "adbArguments": list(arguments),
                    "adbArgumentsSha256": arguments_sha256,
                })

        mutations = (
            ("dangling", dangling),
            (
                "wrong-digest",
                lambda value: value["events"][-1].update({
                    "adbArgumentsSha256": "0" * 64,
                }),
            ),
            ("wrong-command", wrong_command),
            (
                "attempt-skip",
                lambda value: value["events"][-1].update({"attempt": 2}),
            ),
            (
                "maximum-attempt-drift",
                lambda value: value["events"][1].update({"maximumAttempts": 2}),
            ),
            (
                "serial-drift",
                lambda value: value["events"][-1].update({"serial": "other"}),
            ),
            (
                "retry-not-scheduled",
                lambda value: value["events"][0]["replay"].update({
                    "scheduled": False,
                }),
            ),
            (
                "recovery-not-performed",
                lambda value: value["events"][-1]["replay"].update({
                    "performed": False,
                }),
            ),
            ("mutating-whole-chain", mutating_whole_chain),
            ("terminal-failure-in-pass", terminal_failure),
        )
        for label, mutate in mutations:
            with self.subTest(label=label):
                payload = copy.deepcopy(
                    self.recovered_read_only_transport_payload()
                )
                mutate(payload)
                with self.assertRaises(ValueError):
                    contract.validate_adb_transport(
                        payload,
                        serial=str(self.device_payload()["serial"]),
                        label="priority",
                    )

    def test_direct_hierarchy_dump_reconciliation_is_accepted(self) -> None:
        contract.validate_adb_transport(
            self.reconciled_hierarchy_dump_transport_payload(
                observation_mode="direct-current-hierarchy",
            ),
            serial=str(self.device_payload()["serial"]),
            label="priority",
        )

        too_many = self.reconciled_hierarchy_dump_transport_payload(
            observation_mode="direct-current-hierarchy",
        )
        too_many["events"][1]["readOnlyObservation"]["observationsPerformed"] = 4
        with self.assertRaises(ValueError):
            contract.validate_adb_transport(
                too_many,
                serial=str(self.device_payload()["serial"]),
                label="priority",
            )

    def test_exact_swipe_reconciliation_is_accepted(self) -> None:
        contract.validate_adb_transport(
            self.reconciled_swipe_transport_payload(),
            serial=str(self.device_payload()["serial"]),
            label="priority",
        )

    def test_swipe_reconciliation_rejects_cross_type_and_metadata_forgery(self) -> None:
        def relabel_dump(value: dict[str, object]) -> None:
            value["events"][1].update({
                "status": "reconciled-unknown-swipe",
                "readOnlyObservation": {
                    "arguments": list(contract.ADB_READ_ONLY_HIERARCHY_ARGUMENTS),
                    "consecutiveMatching": 2,
                    "observationsPerformed": 2,
                    "hierarchySha256": "3" * 64,
                },
            })

        mutations = (
            (
                "cross-type-relabel",
                self.reconciled_hierarchy_dump_transport_payload,
                relabel_dump,
            ),
            (
                "original-replay",
                self.reconciled_swipe_transport_payload,
                lambda value: value["events"][0]["replay"].update({
                    "performed": True,
                }),
            ),
            (
                "reconciliation-invocation",
                self.reconciled_swipe_transport_payload,
                lambda value: value["events"][1].update({
                    "commandInvocationPerformed": True,
                }),
            ),
            (
                "wrong-original-classification",
                self.reconciled_swipe_transport_payload,
                lambda value: value["events"][0].update({
                    "classification": "transport-recovered",
                }),
            ),
        )
        for label, factory, mutate in mutations:
            with self.subTest(label=label):
                payload = factory()
                mutate(payload)
                with self.assertRaises(ValueError):
                    contract.validate_adb_transport(
                        payload,
                        serial=str(self.device_payload()["serial"]),
                        label="priority",
                    )

    def test_owned_file_hierarchy_dump_reconciliation_rejects_forgery(self) -> None:
        def orphan(value: dict[str, object]) -> None:
            reconciliation = value["events"][1]
            reconciliation["evidenceFile"] = "adb-transport-event-0001.json"
            value.update({"eventCount": 1, "events": [reconciliation]})

        def nonadjacent(value: dict[str, object]) -> None:
            recovered = {
                "schema": "chummer.android.adb-transport-event/v1",
                "status": "recovered-read-only",
                "serial": self.device_payload()["serial"],
                "classification": "transport-recovered",
                "classificationAuthority": "fresh-read-only-command-succeeded",
                "retryableTransportClassification": True,
                "commandPolicy": "read-only-retryable",
                "policyReason": "exact remote-file byte observation",
                "adbArguments": list(
                    contract.ADB_FILE_HIERARCHY_OBSERVATION_ARGUMENTS
                ),
                "adbArgumentsSha256": "3" * 64,
                "attempt": 2,
                "maximumAttempts": 3,
                "commandInvocationPerformed": True,
                "outcomeMutationAuthority": "none-read-only-command",
                "replay": {
                    "eligible": True, "performed": True,
                    "scheduled": False, "suppressed": False,
                },
                "failure": None,
                "evidenceFile": "adb-transport-event-0002.json",
            }
            value["events"].insert(1, recovered)
            value["events"][2]["evidenceFile"] = "adb-transport-event-0003.json"
            value["eventCount"] = 3

        mutations = (
            ("orphan", orphan),
            ("nonadjacent", nonadjacent),
            (
                "wrong-reference",
                lambda value: value["events"][1].update({
                    "reconcilesEvidenceFile": "adb-transport-event-9999.json",
                }),
            ),
            (
                "different-digest",
                lambda value: value["events"][1].update({
                    "adbArgumentsSha256": "0" * 64,
                }),
            ),
            (
                "forged-original-command",
                lambda value: value["events"][0].update({
                    "adbArguments": ["shell", "input", "<3 redacted argument(s)>"],
                }),
            ),
            (
                "forged-freshness-barrier",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "freshnessBarrierArguments": [
                        "shell", "rm", "-f", "/sdcard/other.xml",
                    ],
                }),
            ),
            (
                "forged-owned-file",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "arguments": ["exec-out", "cat", "/sdcard/other.xml"],
                }),
            ),
            (
                "invalid-observation-bytes-digest",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "observationBytesSha256": "not-a-digest",
                }),
            ),
            (
                "unknown-observation-mode",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "mode": "unbounded-fallback",
                }),
            ),
            (
                "direct-mode-with-owned-file-command",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "mode": "direct-current-hierarchy",
                }),
            ),
            (
                "owned-mode-with-direct-command",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "arguments": list(contract.ADB_READ_ONLY_HIERARCHY_ARGUMENTS),
                }),
            ),
            (
                "insufficient-observations",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "observationsPerformed": 1,
                }),
            ),
            (
                "too-many-observations",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "observationsPerformed": 9,
                }),
            ),
            (
                "wrong-consecutive-count",
                lambda value: value["events"][1]["readOnlyObservation"].update({
                    "consecutiveMatching": 1,
                }),
            ),
            (
                "replayed-dump",
                lambda value: value["events"][1]["replay"].update({
                    "performed": True,
                }),
            ),
            (
                "not-a-timeout",
                lambda value: value["events"][0]["failure"].update({
                    "type": "CalledProcessError",
                }),
            ),
            (
                "original-not-invoked",
                lambda value: value["events"][0].update({
                    "commandInvocationPerformed": False,
                }),
            ),
            (
                "reconciliation-claims-invocation",
                lambda value: value["events"][1].update({
                    "commandInvocationPerformed": True,
                }),
            ),
        )
        for label, mutate in mutations:
            with self.subTest(label=label):
                payload = copy.deepcopy(
                    self.reconciled_hierarchy_dump_transport_payload()
                )
                mutate(payload)
                with self.assertRaises(ValueError):
                    contract.validate_adb_transport(
                        payload,
                        serial=str(self.device_payload()["serial"]),
                        label="priority",
                    )

    def test_finalizer_and_aggregate_cli_materialize_then_verify(self) -> None:
        def load(name: str, filename: str):
            spec = importlib.util.spec_from_file_location(name, SCRIPTS / filename)
            assert spec is not None and spec.loader is not None
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            return module

        finalizer = load(
            "physical_journey_finalizer",
            "finalize-api36-arm64-physical-journey-receipt.py",
        )
        aggregate_cli = load(
            "physical_aggregate_verifier",
            "verify-api36-arm64-physical-aggregate.py",
        )
        driver_args = ["--android-repository", str(ROOT)]
        for journey, (relative, _blob) in contract.DRIVER_SPECS.items():
            driver_args.extend(["--driver", f"{journey}={ROOT / relative}"])
        for journey in contract.JOURNEY_ORDER:
            result = finalizer.main([
                "--journey-id", journey,
                "--raw-receipt", str(self.raw[journey]),
                "--restart-evidence", str(self.restart[journey]),
                "--apk", str(self.apk),
                "--source-graph", str(self.graph),
                "--build-provenance", str(self.provenance),
                "--device-observation", str(self.device),
                *driver_args,
                "--output", str(self.seal[journey]),
            ])
            self.assertEqual(0, result)
        common = [
            "--apk", str(self.apk), "--source-graph", str(self.graph),
            "--build-provenance", str(self.provenance),
            "--device-observation", str(self.device),
            *driver_args,
        ]
        for journey in contract.JOURNEY_ORDER:
            common.extend([
                "--raw-receipt", f"{journey}={self.raw[journey]}",
                "--restart-evidence", f"{journey}={self.restart[journey]}",
                "--journey-seal", f"{journey}={self.seal[journey]}",
            ])
        output = self.root / "cli-aggregate.json"
        self.assertEqual(0, aggregate_cli.main(["materialize", *common, "--output", str(output)]))
        self.assertEqual(0, aggregate_cli.main(["verify", *common, "--aggregate", str(output)]))

    def test_raw_unknown_duplicate_status_apk_device_and_publication_tamper_fail_closed(self) -> None:
        cases = []
        for field, value in (
            ("unknownClaim", True), ("status", "pass"),
            ("apkSha256", "0" * 64), ("publicationAuthorized", True),
        ):
            payload = self.raw_payload("priority", "200")
            payload[field] = value
            cases.append((field, payload))
        payload = self.raw_payload("priority", "200")
        payload["deviceObservation"]["serial"] = "OTHER"
        cases.append(("device", payload))
        payload = self.raw_payload("priority", "200")
        payload["buildProvenanceRecheckedAfterRun"] = False
        cases.append(("provenance-recheck", payload))
        payload = self.raw_payload("priority", "200")
        payload["disposableDeviceAuthorization"]["serial"] = "OTHER"
        cases.append(("disposable-authorization", payload))
        for label, payload in cases:
            with self.subTest(label=label):
                write_json(self.raw["priority"], payload)
                with self.assertRaises(ValueError):
                    contract.create_journey_seal(
                        journey_id="priority", raw_receipt_path=self.raw["priority"],
                        restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                        source_graph_path=self.graph, build_provenance_path=self.provenance,
                        device_observation_path=self.device,
                    )
        self.raw["priority"].write_text('{"schema":"a","schema":"b"}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
            contract.create_journey_seal(
                journey_id="priority", raw_receipt_path=self.raw["priority"],
                restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                source_graph_path=self.graph, build_provenance_path=self.provenance,
                device_observation_path=self.device,
            )

    def test_restart_requires_empty_post_stop_disjoint_pid_resumed_component_and_raw_cross_binding(self) -> None:
        templates = (
            ("post_force_stop_process_ids=999", "empty post-stop"),
            ("restart_process_ids=101", "reused"),
            ("restart_resumed_component=foreign/.MainActivity", "component"),
            ("restart_process_ids=999", "raw final"),
        )
        original = self.restart["priority"].read_text()
        for replacement, _label in templates:
            with self.subTest(replacement=replacement):
                lines = original.splitlines()
                key = replacement.split("=", 1)[0]
                lines = [replacement if line.startswith(key + "=") else line for line in lines]
                self.restart["priority"].write_text("\n".join(lines) + "\n")
                with self.assertRaises(ValueError):
                    contract.create_journey_seal(
                        journey_id="priority", raw_receipt_path=self.raw["priority"],
                        restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                        source_graph_path=self.graph, build_provenance_path=self.provenance,
                        device_observation_path=self.device,
                    )

    def test_symlink_and_aggregate_order_extra_seal_and_authority_tamper_fail_closed(self) -> None:
        link = self.root / "raw-link.json"
        link.symlink_to(self.raw["priority"])
        with self.assertRaisesRegex(ValueError, "canonical|symlink"):
            contract.create_journey_seal(
                journey_id="priority", raw_receipt_path=link,
                restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                source_graph_path=self.graph, build_provenance_path=self.provenance,
                device_observation_path=self.device,
            )
        rows = self.seal_all()
        with self.assertRaisesRegex(ValueError, "cardinality/order"):
            contract.create_aggregate(
                journey_inputs=list(reversed(rows)), apk_path=self.apk,
                source_graph_path=self.graph, build_provenance_path=self.provenance,
                device_observation_path=self.device,
            )
        seal_payload = json.loads(self.seal["career"].read_text())
        seal_payload["publicationAuthorized"] = True
        write_json(self.seal["career"], seal_payload)
        with self.assertRaises(ValueError):
            contract.create_aggregate(
                journey_inputs=rows, apk_path=self.apk, source_graph_path=self.graph,
                build_provenance_path=self.provenance, device_observation_path=self.device,
            )

    def test_aggregate_unknown_duplicate_and_authentication_boundary_mutations_fail_closed(self) -> None:
        rows = self.seal_all()
        aggregate = contract.create_aggregate(
            journey_inputs=rows, apk_path=self.apk, source_graph_path=self.graph,
            build_provenance_path=self.provenance, device_observation_path=self.device,
            generated_at_utc="2026-08-28T00:00:05Z",
        )
        aggregate_path = self.root / "aggregate.json"
        forged = copy.deepcopy(aggregate)
        forged["unknownClaim"] = True
        write_json(aggregate_path, forged)
        with self.assertRaisesRegex(ValueError, "keys are not exact"):
            contract.load_and_verify_aggregate(
                aggregate_path, journey_inputs=rows, apk_path=self.apk,
                source_graph_path=self.graph, build_provenance_path=self.provenance,
                device_observation_path=self.device,
            )
        aggregate_path.write_text('{"schema":"a","schema":"b"}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
            contract.load_and_verify_aggregate(
                aggregate_path, journey_inputs=rows, apk_path=self.apk,
                source_graph_path=self.graph, build_provenance_path=self.provenance,
                device_observation_path=self.device,
            )

        write_json(aggregate_path, aggregate)
        original_create = contract.create_aggregate

        def mutate_aggregate_after_replay(**arguments: object) -> dict[str, object]:
            result = original_create(**arguments)
            aggregate_path.write_bytes(aggregate_path.read_bytes() + b" ")
            return result

        with mock.patch.object(contract, "create_aggregate", side_effect=mutate_aggregate_after_replay):
            with self.assertRaisesRegex(ValueError, "authentication boundary"):
                contract.load_and_verify_aggregate(
                    aggregate_path, journey_inputs=rows, apk_path=self.apk,
                    source_graph_path=self.graph, build_provenance_path=self.provenance,
                    device_observation_path=self.device,
                )

    def test_seal_rejects_input_replacement_across_authentication_boundary(self) -> None:
        original_graph = self.graph.read_bytes()
        original_validate = contract.validate_raw_receipt

        def mutate_graph_after_raw_validation(*arguments: object, **keywords: object) -> dict[str, object]:
            result = original_validate(*arguments, **keywords)
            self.graph.write_bytes(original_graph + b" ")
            return result

        try:
            with mock.patch.object(contract, "validate_raw_receipt", side_effect=mutate_graph_after_raw_validation):
                with self.assertRaisesRegex(ValueError, "authentication boundary"):
                    contract.create_journey_seal(
                        journey_id="priority", raw_receipt_path=self.raw["priority"],
                        restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                        source_graph_path=self.graph, build_provenance_path=self.provenance,
                        device_observation_path=self.device,
                    )
        finally:
            self.graph.write_bytes(original_graph)

    def test_aggregate_rejects_restarted_pid_reuse_across_journeys(self) -> None:
        career_payload = self.raw_payload("career", "200")
        write_json(self.raw["career"], career_payload)
        self.restart["career"].write_text(
            "pre_force_stop_process_ids=101\n"
            f"pre_force_stop_resumed_component={contract.PACKAGE}/crc.MainActivity\n"
            "post_force_stop_process_ids=\n"
            "restart_process_ids=200\n"
            f"restart_resumed_component={contract.PACKAGE}/crc.MainActivity\n",
            encoding="utf-8",
        )
        rows = self.seal_all()
        with self.assertRaisesRegex(ValueError, "reused a before/restarted PID"):
            contract.create_aggregate(
                journey_inputs=rows, apk_path=self.apk, source_graph_path=self.graph,
                build_provenance_path=self.provenance, device_observation_path=self.device,
            )

    def test_nested_release_play_tablet_and_production_claims_fail_closed(self) -> None:
        for key in (
            "googlePlayUpload", "productionRollout", "tabletJourney",
            "publicationAuthorized", "publicReleaseReadiness",
        ):
            with self.subTest(key=key):
                payload = self.raw_payload("priority", "200")
                payload["authorityProofStages"][key] = True
                write_json(self.raw["priority"], payload)
                with self.assertRaises(ValueError):
                    contract.create_journey_seal(
                        journey_id="priority", raw_receipt_path=self.raw["priority"],
                        restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                        source_graph_path=self.graph, build_provenance_path=self.provenance,
                        device_observation_path=self.device,
                    )

    def test_apk_source_graph_provenance_and_device_divergence_fail_closed(self) -> None:
        for target, mutate in (
            (self.apk, lambda: self.apk.write_bytes(b"not-apk")),
            (self.graph, lambda: self.graph.write_text('{"contractName":"wrong"}\n')),
            (self.provenance, lambda: self.provenance.write_text('{"status":"pass"}\n')),
            (self.device, lambda: self.device.write_text('{"status":"pass"}\n')),
        ):
            with self.subTest(target=target.name):
                original = target.read_bytes()
                mutate()
                with self.assertRaises(ValueError):
                    contract.create_journey_seal(
                        journey_id="priority", raw_receipt_path=self.raw["priority"],
                        restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                        source_graph_path=self.graph, build_provenance_path=self.provenance,
                        device_observation_path=self.device,
                    )
                target.write_bytes(original)

    def test_nested_unknown_fields_types_and_full_restart_cross_binding_fail_closed(self) -> None:
        mutations = (
            ("device", "priority", lambda value: value["deviceObservation"].update({"unknown": True})),
            ("adb", "career", lambda value: value["adbTransport"]["preflight"].update({"unknown": True})),
            ("adb-bool", "career", lambda value: value["adbTransport"].update({"nonReplayableCommandMaximumAttempts": True})),
            ("source", "career", lambda value: value["sourceGraphAuthority"].update({"unknown": True})),
            ("subjourney", "career", lambda value: value["journeys"].update({"unknown": "pass"})),
            ("proof", "after-run", lambda value: value["authorityProofStages"].update({"unknown": True})),
            ("career-bool", "career", lambda value: value["authorityProofStages"]["reviewedCheckpoint"].update({"Version": True})),
            ("lane-bool", "before-run", lambda value: value["authorityProofStages"]["receipt"].update({"ExpectedWorkspaceRevision": True})),
            ("after-type", "after-run", lambda value: value["authorityProofStages"]["reviewedCheckpoint"]["Draft"]["Candidate"]["Binding"].update({"WorkspaceId": "workspace-1"})),
            ("downtime-bool", "downtime", lambda value: value["authorityProofStages"]["reviewedJournal"]["Review"]["Preview"].update({"Year": True})),
            (
                "bool-pid", "priority",
                lambda value: value["authorityProofStages"]["processRestart"].update({"beforeProcessIds": [True]}),
            ),
            (
                "integer-pid", "priority",
                lambda value: value["authorityProofStages"]["processRestart"].update({"restartedProcessIds": [200]}),
            ),
            (
                "duplicate-pid", "priority",
                lambda value: value["authorityProofStages"]["processRestart"].update({"restartedProcessIds": ["200", "200"]}),
            ),
            (
                "new-pid-false", "priority",
                lambda value: value["authorityProofStages"]["processRestart"].update({"newPidVerified": False}),
            ),
            (
                "post-stop-nonempty", "priority",
                lambda value: value["authorityProofStages"]["processRestart"].update({"afterForceStopProcessIds": ["999"]}),
            ),
        )
        for label, journey, mutate in mutations:
            with self.subTest(label=label):
                original = self.raw[journey].read_bytes()
                payload = self.raw_payload(journey, str(200 + contract.JOURNEY_ORDER.index(journey)))
                mutate(payload)
                write_json(self.raw[journey], payload)
                with self.assertRaises(ValueError):
                    contract.create_journey_seal(
                        journey_id=journey, raw_receipt_path=self.raw[journey],
                        restart_evidence_path=self.restart[journey], apk_path=self.apk,
                        source_graph_path=self.graph, build_provenance_path=self.provenance,
                        device_observation_path=self.device,
                    )
                self.raw[journey].write_bytes(original)

    def test_wp1_successor_adapter_and_nonclaim_boundaries_fail_closed(self) -> None:
        def reseal(payload: dict[str, object]) -> None:
            authority = copy.deepcopy(payload)
            authority.pop("authoritySha256", None)
            authority.pop("generatedAtUtc", None)
            payload["authoritySha256"] = contract.canonical_sha256(authority)
            write_json(self.provenance, payload)

        pristine = self.provenance_payload()
        mutations = (
            ("source-head-unknown", lambda value: value["sourceHead"].update({"unknown": True})),
            ("source-publication", lambda value: value["sourceHead"].update({"publicationAuthorized": True})),
            ("package-authority-unknown", lambda value: value["packageAuthority"].update({"unknown": True})),
            ("package-source-drift", lambda value: value["packageAuthority"]["sourceGraph"].update({"hubProducerCommit": "0" * 40})),
            ("artifact-no-signing", lambda value: value["artifact"].pop("signing")),
            ("signing-unknown", lambda value: value["artifact"]["signing"].update({"unknown": True})),
            ("signing-bool-scheme", lambda value: value["artifact"]["signing"].update({"verifiedSchemes": [True, 2]})),
            ("execution-missing", lambda value: value["executionEvidence"].pop("delegateCommandJournal")),
            ("toolchain-unknown", lambda value: value["toolchain"].update({"unknown": True})),
            ("toolchain-untrusted-dotnet", lambda value: value["toolchain"]["dotnetHost"].update({"sha256": "0" * 64})),
            ("toolchain-arbitrary-sdk-root", lambda value: value["toolchain"]["androidSdk"].update({"root": "/tmp/android-sdk"})),
            ("toolchain-incomplete-jdk-map", lambda value: value["toolchain"]["jdkRelease"].update({"fields": {"IMPLEMENTOR": "Microsoft", "JAVA_VERSION": "17.0.14"}})),
            ("toolchain-extended-jdk-map", lambda value: value["toolchain"]["jdkRelease"]["fields"].update({"UNTRUSTED": "value"})),
            ("wp1-nonclaim", lambda value: value.update({"doesNotAssert": []})),
        )
        for label, mutate in mutations:
            with self.subTest(label=label):
                payload = copy.deepcopy(pristine)
                mutate(payload)
                reseal(payload)
                with self.assertRaises(ValueError):
                    contract.create_journey_seal(
                        journey_id="priority", raw_receipt_path=self.raw["priority"],
                        restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                        source_graph_path=self.graph, build_provenance_path=self.provenance,
                        device_observation_path=self.device,
                    )
        write_json(self.provenance, pristine)

        for label, mutate in (
            ("authority-state", lambda value: value.update({"authorityState": "production"})),
            ("source-nonclaim", lambda value: value.update({"doesNotAssert": []})),
        ):
            with self.subTest(label=label):
                graph = self.graph_payload()
                mutate(graph)
                write_json(self.graph, graph)
                with self.assertRaises(ValueError):
                    contract.create_journey_seal(
                        journey_id="priority", raw_receipt_path=self.raw["priority"],
                        restart_evidence_path=self.restart["priority"], apk_path=self.apk,
                        source_graph_path=self.graph, build_provenance_path=self.provenance,
                        device_observation_path=self.device,
                    )
        write_json(self.graph, self.graph_payload())

    def test_aggregate_rejects_before_pid_reuse_across_journeys(self) -> None:
        self.restart["career"].write_text(
            "pre_force_stop_process_ids=100\n"
            f"pre_force_stop_resumed_component={contract.PACKAGE}/crc.MainActivity\n"
            "post_force_stop_process_ids=\n"
            "restart_process_ids=201\n"
            f"restart_resumed_component={contract.PACKAGE}/crc.MainActivity\n",
            encoding="utf-8",
        )
        rows = self.seal_all()
        with self.assertRaisesRegex(ValueError, "reused a before/restarted PID"):
            contract.create_aggregate(
                journey_inputs=rows, apk_path=self.apk, source_graph_path=self.graph,
                build_provenance_path=self.provenance, device_observation_path=self.device,
            )


class CaptureAndOrchestratorContractTests(unittest.TestCase):
    def test_exact_integrated_driver_git_authority_and_cli_contracts(self) -> None:
        head = "a" * 40
        tree = "b" * 40
        graph = Api36Arm64PhysicalContractTests.graph_payload()
        android = next(row for row in graph["repositories"] if row["name"] == "chummer-android")
        android.update({"commit": head, "tree": tree})
        paths = {
            journey: ROOT / relative
            for journey, (relative, _blob) in contract.DRIVER_SPECS.items()
        }

        def runner(_root: Path, arguments: tuple[str, ...]) -> str:
            if arguments == ("rev-parse", "HEAD"):
                return head + "\n"
            if arguments == ("rev-parse", "HEAD^{tree}"):
                return tree + "\n"
            if arguments[0] == "merge-base":
                return contract.INTEGRATION_BASE_COMMIT + "\n"
            if arguments[0] == "status":
                return ""
            if arguments[0] == "ls-tree":
                relative = arguments[-1]
                journey = next(key for key, value in contract.DRIVER_SPECS.items() if value[0] == relative)
                blob = contract.DRIVER_SPECS[journey][1]
                return f"100644 blob {blob}\t{relative}\n"
            raise AssertionError(arguments)

        self.assertEqual(
            contract.DRIVER_AUTHORITY_SCHEMA,
            contract.capture_driver_authority(
                repository_root=ROOT, driver_paths=paths,
                source_graph=graph, git_runner=runner,
            )["schema"],
        )
        for journey, (relative, blob) in contract.DRIVER_SPECS.items():
            line = subprocess.run(
                ["git", "ls-tree", contract.INTEGRATION_BASE_COMMIT, "--", relative],
                cwd=ROOT, check=True, capture_output=True, text=True,
            ).stdout.rstrip("\n")
            self.assertEqual(f"100644 blob {blob}\t{relative}", line)
            help_text = subprocess.run(
                [sys.executable, str(ROOT / relative), "--help"], cwd=ROOT,
                check=True, capture_output=True, text=True,
            ).stdout
            for option in (
                "--adb", "--apk", "--build-provenance-manifest", "--serial",
                "--evidence", "--receipt", "--workspace-root",
                "--allow-destructive-disposable-device",
            ):
                self.assertIn(option, help_text, f"{journey} omitted {option}")

        with self.assertRaisesRegex(ValueError, "must be clean"):
            contract.capture_driver_authority(
                repository_root=ROOT, driver_paths=paths, source_graph=graph,
                git_runner=lambda root, args: " M tests/dirty.py\n" if args[0] == "status" else runner(root, args),
            )
        wrong_paths = dict(paths)
        wrong_paths["career"] = paths["priority"]
        with self.assertRaisesRegex(ValueError, "exact integrated repository path"):
            contract.capture_driver_authority(
                repository_root=ROOT, driver_paths=wrong_paths,
                source_graph=graph, git_runner=runner,
            )
        with self.assertRaisesRegex(ValueError, "Git blob/mode/path"):
            contract.capture_driver_authority(
                repository_root=ROOT, driver_paths=paths, source_graph=graph,
                git_runner=lambda root, args: (
                    f"100644 blob {'0' * 40}\t{args[-1]}\n"
                    if args[0] == "ls-tree" else runner(root, args)
                ),
            )
        wrong_graph = copy.deepcopy(graph)
        next(row for row in wrong_graph["repositories"] if row["name"] == "chummer-android")["commit"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "does not bind"):
            contract.capture_driver_authority(
                repository_root=ROOT, driver_paths=paths,
                source_graph=wrong_graph, git_runner=runner,
            )

    def test_capture_requires_two_stable_physical_observations_and_rejects_emulator(self) -> None:
        path = SCRIPTS / "capture-api36-arm64-physical-device.py"
        spec = importlib.util.spec_from_file_location("capture_api36_arm64", path)
        assert spec is not None and spec.loader is not None
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as temporary:
            adb = Path(temporary).resolve() / "adb"
            adb.write_bytes(b"executable fixture")
            adb.chmod(0o700)
            properties = Api36Arm64PhysicalContractTests.device_payload()["properties"]

            def runner(_adb: Path, _serial: str, *arguments: str) -> str:
                if arguments == ("get-state",):
                    return "device"
                return properties[arguments[-1]]

            payload = module.capture_payload(
                adb, "R5CT30PHYSICAL", runner=runner,
                captured_at_utc="2026-08-28T00:00:00Z",
            )
            self.assertEqual(36, payload["apiLevel"])
            forged = dict(properties)
            forged["ro.hardware"] = "vbox86"

            def emulator(_adb: Path, _serial: str, *arguments: str) -> str:
                return "device" if arguments == ("get-state",) else forged[arguments[-1]]

            with self.assertRaisesRegex(ValueError, "emulator"):
                module.capture_payload(adb, "R5CT30PHYSICAL", runner=emulator)

    def test_orchestrator_is_bounded_external_and_inactive(self) -> None:
        script_path = SCRIPTS / "run-api36-arm64-physical-e2e.sh"
        source = script_path.read_text(encoding="utf-8")
        for marker in (
            "build-authority-preflight", "physical-device-capture",
            "--deadline-epoch", "--timeout-seconds 3600",
            "process-restart-verified.txt", "aggregate-materialize", "aggregate-verify",
            "CHUMMER_API36_ARM64_OUTPUT_ROOT", "publication_authorized=false",
        ):
            self.assertIn(marker, source)
        for forbidden in ("dotnet build", "dotnet publish", "https://", "google play"):
            self.assertNotIn(forbidden, source.lower())
        self.assertLess(source.index("build-authority-preflight"), source.index("physical-device-capture"))
        self.assertLess(
            source.index("physical-device-capture"),
            source.index('run_bounded "journey-$journey"'),
        )
        relative = script_path.relative_to(ROOT).as_posix()
        matches = []
        for activation in (ROOT / ".github", ROOT / "scripts/release"):
            if not activation.exists():
                continue
            for path in activation.rglob("*"):
                if path.is_file() and relative in path.read_text(encoding="utf-8", errors="replace"):
                    matches.append(path.relative_to(ROOT).as_posix())
        self.assertEqual([], matches)


if __name__ == "__main__":
    unittest.main()
