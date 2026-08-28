from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
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
            raw = self.root / f"{journey}-raw.json"
            write_json(raw, self.raw_payload(journey, restart_pid))
            restart = self.root / f"{journey}-restart.txt"
            restart.write_text(
                "pre_force_stop_process_ids=101\n"
                f"pre_force_stop_resumed_component={contract.PACKAGE}/crc.MainActivity\n"
                "post_force_stop_process_ids=\n"
                f"restart_process_ids={restart_pid}\n"
                f"restart_resumed_component={contract.PACKAGE}/crc.MainActivity\n",
                encoding="utf-8",
            )
            self.raw[journey] = raw
            self.restart[journey] = restart
            self.seal[journey] = self.root / f"{journey}-seal.json"

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def graph_payload() -> dict[str, object]:
        return {
            "contractName": contract.SOURCE_GRAPH_SCHEMA,
            "generatedAtUtc": "2026-08-28T00:00:00Z",
            "authorityState": "local_review_required",
            "publicationAuthorized": False,
            "generator": {"path": "scripts/verify_release_source_graph.py", "sha256": "1" * 64, "size_bytes": 1},
            "repositories": [{"name": f"repo-{index}"} for index in range(8)],
            "packagePins": [{"package_id": f"Core-{index}"} for index in range(6)],
            "ownerPackagePins": [{"package_id": f"Owner-{index}"} for index in range(7)],
            "dependencyClosure": [],
            "presentationSource": {},
            "doesNotAssert": [],
        }

    def provenance_payload(self) -> dict[str, object]:
        graph_bytes = self.graph.read_bytes()
        apk_bytes = self.apk.read_bytes()
        authority: dict[str, object] = {
            "schema": contract.BUILD_PROVENANCE_SCHEMA,
            "status": "pass",
            "authorityClass": "internal_phone_beta_physical_candidate_only",
            "publicationAuthorized": False,
            "proofScope": "full_maui_arm64_apk_build_only",
            "dependencyMode": "locked_w5_packages_no_owner_siblings",
            "sourceGraph": {
                "sha256": hashlib.sha256(graph_bytes).hexdigest(),
                "sizeBytes": len(graph_bytes),
                "contractName": contract.SOURCE_GRAPH_SCHEMA,
                "repositories": self.graph_payload()["repositories"],
            },
            "w5CompileProof": {},
            "presentationBuildSource": {"productionSource": False, "publicationAuthorized": False},
            "packageAuthority": {},
            "content": {},
            "restore": {"lockedMode": True, "networkSourcesAllowed": False},
            "executionEvidence": {},
            "toolchain": {},
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
            },
            "doesNotAssert": ["api36_device_execution", "publication_authority"],
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

    def raw_payload(self, journey: str, restart_pid: str) -> dict[str, object]:
        schema, raw_journey = contract.JOURNEY_CONTRACTS[journey]
        observation = {
            "serial": self.device_payload()["serial"],
            "apiLevel": 36,
            "abi": contract.ABI,
            "abiList": self.device_payload()["abiList"],
            "qemu": "",
            "hardware": "tensor",
            "buildFingerprint": self.device_payload()["properties"]["ro.build.fingerprint"],
        }
        proof: dict[str, object]
        if journey == "priority":
            proof = {
                "processRestart": {
                    "beforeProcessIds": ["101"], "afterForceStopProcessIds": [],
                    "restartedProcessIds": [restart_pid], "newPidVerified": True,
                }
            }
        else:
            proof = {"restartProcessIds": [["111"], ["112"], [restart_pid]]}
        common: dict[str, object] = {
            "schema": schema,
            "status": "device-pass-source-bound",
            "executionStatus": "pass",
            "releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested",
            "generatedAtUtc": "2026-08-28T00:00:03+00:00",
            "profile": "phone",
            "journey": raw_journey,
            "apiLevel": 36,
            "abi": contract.ABI,
            "deviceObservation": observation,
            "buildProvenance": self.provenance_payload(),
            "apkSha256": hashlib.sha256(self.apk.read_bytes()).hexdigest(),
            "adbTransport": {"status": "pass"},
            "authorityProofStages": proof,
        }
        if journey == "priority":
            common.update({
                "releaseAttested": False,
                "publicationAuthorized": False,
                "buildMethod": "Priority",
                "serial": self.device_payload()["serial"],
                "package": contract.PACKAGE,
                "apk": str(self.apk),
                "buildProvenanceFile": {
                    "sha256": hashlib.sha256(self.provenance.read_bytes()).hexdigest(),
                    "size": self.provenance.stat().st_size,
                },
                "buildProvenanceRecheckedAfterRun": True,
                "buildProvenanceFileRecheckedAfterRun": True,
                "disposableDeviceAuthorization": {
                    "authorized": True,
                    "flag": "--allow-destructive-disposable-device",
                    "serial": self.device_payload()["serial"],
                    "scope": "install-apk-and-atomically-finalize-one-pending-runner",
                },
                "physicalDeviceProof": True,
                "installedArtifactBound": True,
                "draftStateFabricated": False,
                "identityContractStatus": "typed-contract-unavailable",
            })
        elif journey == "career":
            common.update({
                "serial": self.device_payload()["serial"],
                "package": contract.PACKAGE,
                "apk": str(self.apk),
                "expectedApkSha256": common["apkSha256"],
                "apkAbis": [contract.ABI],
                "androidSourceRevision": "1" * 40,
                "expectedAndroidSourceRevision": "1" * 40,
                "presentationSourceRevision": "2" * 40,
                "coreSourceRevision": "3" * 40,
                "sourceGraphAuthority": {"authoritySha256": "4" * 64},
                "postRunSourceGraphAuthoritySha256": "4" * 64,
                "sourceGraphRecheckedAfterRun": True,
                "verifiedRemoteCareerFixtureSha256": "5" * 64,
                "remoteTemporaryFiles": [],
                "journeys": {"career": "pass"},
                **{field: "6" * 64 for field in contract.CAREER_SOURCE_FIELDS},
            })
        elif journey in {"before-run", "playtime"}:
            common.update({
                "sourceGraphAuthority": {"authoritySha256": "4" * 64},
                "sourceGraphRecheckedAfterRun": True,
                "careerFixtureSha256": "5" * 64,
                "verifiedRemoteCareerFixtureSha256": "5" * 64,
                "remoteTemporaryFilesDeleted": [],
                "scope": {"claim": "one representative typed action only"},
                "journeys": {journey: "pass"},
            })
        elif journey == "after-run":
            common.pop("adbTransport")
            common.update({
                "serial": self.device_payload()["serial"],
                "sourceGraphAuthority": {"authoritySha256": "4" * 64},
                "postRunSourceGraphAuthoritySha256": "4" * 64,
                "sourceGraphRecheckedAfterRun": True,
                "apkAbis": [contract.ABI],
                "governedFixtureSha256": "5" * 64,
                "materializedRunnerSha256": "6" * 64,
                "verifiedRemoteRunnerSha256": "6" * 64,
                "remoteTemporaryFiles": [],
                "journeys": {journey: "pass"},
            })
        else:
            common.pop("adbTransport")
            common.update({
                "serial": self.device_payload()["serial"],
                "sourceGraphAuthority": {"authoritySha256": "4" * 64},
                "postRunSourceGraphAuthoritySha256": "4" * 64,
                "sourceGraphRecheckedAfterRun": True,
                "apkAbis": [contract.ABI],
                "governedFixtureSha256": "5" * 64,
                "careerRunnerSha256": "6" * 64,
                "verifiedRemoteRunnerSha256": "6" * 64,
                "remoteTemporaryFiles": [],
                "journeys": {journey: "pass"},
            })
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
        for journey in contract.JOURNEY_ORDER:
            result = finalizer.main([
                "--journey-id", journey,
                "--raw-receipt", str(self.raw[journey]),
                "--restart-evidence", str(self.restart[journey]),
                "--apk", str(self.apk),
                "--source-graph", str(self.graph),
                "--build-provenance", str(self.provenance),
                "--device-observation", str(self.device),
                "--output", str(self.seal[journey]),
            ])
            self.assertEqual(0, result)
        common = [
            "--apk", str(self.apk), "--source-graph", str(self.graph),
            "--build-provenance", str(self.provenance),
            "--device-observation", str(self.device),
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
        with self.assertRaisesRegex(ValueError, "reused a restarted PID"):
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
                with self.assertRaisesRegex(ValueError, "forbidden claim"):
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


class CaptureAndOrchestratorContractTests(unittest.TestCase):
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
