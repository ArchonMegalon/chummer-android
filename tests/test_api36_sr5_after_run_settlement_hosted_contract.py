import argparse
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import run_api36_editing_e2e as shared
import run_api36_sr5_after_run_settlement_e2e as settlement
import run_api36_sr5_after_run_settlement_hosted_e2e as hosted


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_after_run_settlement_hosted_e2e.py"


class FakeDevice:
    def __init__(self, adb: Path, serial: str, evidence: Path) -> None:
        self.adb = adb
        self.serial = serial
        self.evidence = evidence
        self.preflight_api = ""
        self.shared_storage_ready = False
        self.shared_storage_deadline = 0.0
        self.pushed: tuple[Path, str, str] | None = None

    def require_transport_stability(self, *, expected_api_level: str) -> None:
        self.preflight_api = expected_api_level

    def require_shared_storage_readiness(
        self,
        *,
        deadline: float,
        hosted_api_level: str,
        hosted_abi: str,
        hosted_emulator: str,
        hosted_proof_attempt: bool,
    ) -> None:
        self.shared_storage_ready = True
        self.shared_storage_deadline = deadline
        if (
            hosted_api_level != "36"
            or hosted_abi != "x86_64"
            or hosted_emulator != "1"
            or hosted_proof_attempt is not True
        ):
            raise AssertionError("Hosted shared-storage authority was not exact")

    def shell(self, *arguments: str) -> str:
        if arguments == ("getprop", "ro.build.version.sdk"):
            return "36"
        if arguments == ("getprop", "ro.product.cpu.abi"):
            return "x86_64"
        if arguments == ("getprop", "ro.kernel.qemu"):
            return "1"
        raise AssertionError(f"unexpected shell call: {arguments!r}")

    def push_verified(self, source: Path, remote: str, digest: str) -> str:
        self.pushed = (source, remote, digest)
        return digest

    def transport_summary(self) -> dict[str, object]:
        return {"status": "stable", "apiLevel": self.preflight_api}


class Api36Sr5AfterRunSettlementHostedContractTests(unittest.TestCase):
    def test_driver_reuses_the_exact_typed_settlement_authority(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        compile(source, str(DRIVER), "exec")
        for marker in (
            "settlement.load_fixture",
            "settlement.materialize_runner",
            "settlement.prove_after_run",
            'expected_api_level="36"',
            'HOSTED_ABI = "x86_64"',
            '"fullEditing": "excluded"',
            '"tablet": "deferred"',
            '"publicationAuthorized": False',
        ):
            self.assertIn(marker, source)
        self.assertNotIn("device-pass-source-bound", source)
        self.assertNotIn("releaseAttested", source)

    def test_source_graph_is_regular_complete_and_fixture_bound(self) -> None:
        paths = hosted.source_paths()
        self.assertEqual(set(paths), set(hosted.source_snapshot(paths)))
        self.assertEqual(
            settlement.DEFAULT_FIXTURE.resolve(), paths["fixtureSha256"]
        )
        self.assertEqual(DRIVER, paths["driverSha256"])
        self.assertGreaterEqual(len(paths), 14)
        for path in paths.values():
            with self.subTest(path=path):
                self.assertTrue(path.is_file())
                self.assertFalse(path.is_symlink())

    def test_hosted_execution_is_phone_api36_x64_source_and_cleanup_bound(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            apk = root / "candidate.apk"
            apk.write_bytes(b"exact-hosted-apk")
            evidence = root / "evidence"
            args = argparse.Namespace(
                adb=root / "adb",
                apk=apk,
                serial="emulator-5554",
                evidence=evidence,
                receipt=root / "receipt.json",
                fixture=settlement.DEFAULT_FIXTURE,
            )
            proof = {"typedSettlement": "pass", "restartCount": 3}
            devices: list[FakeDevice] = []

            def create_device(adb: Path, serial: str, output: Path) -> FakeDevice:
                device = FakeDevice(adb, serial, output)
                devices.append(device)
                return device

            with (
                mock.patch.object(hosted.shared, "Device", side_effect=create_device),
                mock.patch.object(hosted.subprocess, "run") as install,
                mock.patch.object(
                    hosted.settlement.physical,
                    "remove_remote_temporary_file",
                    side_effect=lambda *_args: self.assertTrue(
                        devices[0].shared_storage_ready
                    ),
                ) as remove_remote,
                mock.patch.object(
                    hosted.settlement,
                    "prove_after_run",
                    return_value=proof,
                ) as prove_after_run,
            ):
                receipt = hosted.execute(args)

            self.assertEqual(1, len(devices))
            self.assertEqual("36", devices[0].preflight_api)
            self.assertTrue(devices[0].shared_storage_ready)
            self.assertGreater(devices[0].shared_storage_deadline, 0.0)
            self.assertIsNotNone(devices[0].pushed)
            self.assertEqual(4, remove_remote.call_count)
            install.assert_called_once()
            prove_after_run.assert_called_once()
            self.assertEqual(hosted.SCHEMA, receipt["schema"])
            self.assertEqual("pass", receipt["status"])
            self.assertEqual("pass", receipt["executionStatus"])
            self.assertEqual(hosted.JOURNEY, receipt["journey"])
            self.assertNotIn("matrixJourney", receipt)
            self.assertNotIn("driverJourney", receipt)
            self.assertNotIn("gateAuthority", receipt)
            self.assertNotIn("artifactAuthority", receipt)
            self.assertEqual("phone", receipt["profile"])
            self.assertEqual(36, receipt["apiLevel"])
            self.assertEqual("x86_64", receipt["abi"])
            self.assertEqual(shared.PACKAGE, receipt["package"])
            self.assertEqual(proof, receipt["authorityProofStages"])
            self.assertEqual("excluded", receipt["scope"]["fullEditing"])
            self.assertEqual("deferred", receipt["scope"]["tablet"])
            self.assertIs(False, receipt["publicationAuthorized"])
            fixture = settlement.load_fixture()
            self.assertEqual(
                fixture["runner"]["expectedSha256"],
                receipt["materializedRunnerSha256"],
            )
            self.assertEqual(
                receipt["materializedRunnerSha256"],
                receipt["verifiedRemoteRunnerSha256"],
            )
            self.assertTrue(all(
                item["precleaned"] and item["deletedAndVerified"]
                for item in receipt["remoteTemporaryFiles"]
            ))
            json.dumps(receipt)

    def test_foreign_fixture_fails_before_device_access(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            fixture = root / "fixture.json"
            fixture.write_text("{}\n", encoding="utf-8")
            args = argparse.Namespace(
                adb=root / "adb",
                apk=root / "candidate.apk",
                serial="emulator-5554",
                evidence=root / "evidence",
                receipt=root / "receipt.json",
                fixture=fixture,
            )
            with mock.patch.object(hosted.shared, "Device") as device:
                with self.assertRaisesRegex(RuntimeError, "exact committed governed fixture"):
                    hosted.execute(args)
                device.assert_not_called()


if __name__ == "__main__":
    unittest.main()
