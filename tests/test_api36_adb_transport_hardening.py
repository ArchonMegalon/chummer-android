from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests" / "run_api36_editing_e2e.py"
SPEC = importlib.util.spec_from_file_location("api36_transport_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


def completed(arguments: tuple[str, ...], stdout: str) -> subprocess.CompletedProcess:
    return subprocess.CompletedProcess(arguments, 0, stdout=stdout, stderr="")


def offline(arguments: tuple[str, ...]) -> subprocess.CalledProcessError:
    return subprocess.CalledProcessError(
        1,
        arguments,
        output="",
        stderr="error: device offline",
    )


class Api36AdbTransportHardeningTests(unittest.TestCase):
    def make_device(self, evidence: Path) -> object:
        return driver.Device(Path("/trusted/adb"), "SERIAL-API36", evidence)

    def test_preflight_requires_consecutive_api36_observations_and_recovers_read_only(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            responses = [
                offline(("get-state",)),
                completed(("get-state",), "device\n"),
                completed(("getprop",), "36\n"),
                completed(("get-state",), "device\n"),
                completed(("getprop",), "36\n"),
                completed(("get-state",), "device\n"),
                completed(("getprop",), "36\n"),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                receipt = device.require_transport_stability(
                    required_consecutive=3,
                    max_observations=5,
                )

            self.assertEqual(7, run.call_count)
            self.assertEqual("pass", receipt["status"])
            self.assertEqual(4, receipt["observationsPerformed"])
            self.assertEqual(3, receipt["consecutiveStableObservations"])
            self.assertEqual(0, receipt["mutationCommandsIssued"])
            self.assertEqual(
                "fresh-adb-invocation-no-reconnect-command",
                receipt["recoveryMechanism"],
            )
            self.assertEqual(
                ["transport-failure", "stable", "stable", "stable"],
                [entry["status"] for entry in receipt["observations"]],
            )
            stored = json.loads(
                (evidence / "adb-transport-preflight.json").read_text(encoding="utf-8")
            )
            self.assertEqual(receipt, stored)
            self.assertEqual(
                0o600,
                (evidence / "adb-transport-preflight.json").stat().st_mode & 0o777,
            )
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(
                [
                    ("get-state",),
                    ("get-state",),
                    ("shell", "getprop", "ro.build.version.sdk"),
                    ("get-state",),
                    ("shell", "getprop", "ro.build.version.sdk"),
                    ("get-state",),
                    ("shell", "getprop", "ro.build.version.sdk"),
                ],
                issued,
            )

    def test_stale_transport_receipt_is_rejected_before_any_adb_command(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            stale = evidence / "adb-transport-preflight.json"
            stale.write_text('{"status":"pass"}\n', encoding="utf-8")
            with mock.patch.object(driver.subprocess, "run") as run:
                with self.assertRaisesRegex(RuntimeError, "contains stale receipts"):
                    self.make_device(evidence)
            run.assert_not_called()

    def test_preflight_fails_immediately_for_unauthorized_transport(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            unauthorized = subprocess.CalledProcessError(
                1,
                ("get-state",),
                stderr="error: device unauthorized",
            )
            with mock.patch.object(
                driver.subprocess,
                "run",
                side_effect=unauthorized,
            ) as run:
                with self.assertRaises(driver.AdbTransportPreflightError) as raised:
                    device.require_transport_stability(max_observations=7)

            self.assertEqual(1, run.call_count)
            self.assertEqual("fail", raised.exception.receipt["status"])
            self.assertEqual(
                "device-unauthorized",
                raised.exception.receipt["observations"][0]["classification"],
            )
            self.assertFalse(
                raised.exception.receipt["observations"][0][
                    "retryableReadOnlyObservation"
                ]
            )

    def test_read_only_transport_failure_is_retried_with_a_recovery_receipt(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            responses = [
                offline(("shell", "getprop", "ro.hardware")),
                completed(("shell", "getprop", "ro.hardware"), "tensor\n"),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                observed = device.shell("getprop", "ro.hardware")

            self.assertEqual("tensor", observed)
            self.assertEqual(2, run.call_count)
            summary = device.transport_summary()
            self.assertFalse(summary["explicitAdbReconnectCommandAllowed"])
            self.assertEqual(2, summary["eventCount"])
            failure, recovery = summary["events"]
            self.assertEqual("retrying-read-only", failure["status"])
            self.assertEqual("device-offline", failure["classification"])
            self.assertEqual("read-only-retryable", failure["commandPolicy"])
            self.assertEqual(
                "none-read-only-command", failure["outcomeMutationAuthority"]
            )
            self.assertFalse(failure["replay"]["performed"])
            self.assertTrue(failure["replay"]["scheduled"])
            self.assertEqual("recovered-read-only", recovery["status"])
            self.assertTrue(recovery["replay"]["performed"])
            self.assertEqual(
                failure,
                json.loads(
                    (evidence / "adb-transport-event-0001.json").read_text(
                        encoding="utf-8"
                    )
                ),
            )
            self.assertEqual(
                0o600,
                (evidence / "adb-transport-event-0001.json").stat().st_mode & 0o777,
            )

    def test_get_state_offline_success_exit_is_still_retried_as_transport(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        completed(("get-state",), "offline\n"),
                        completed(("get-state",), "device\n"),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                state = device.run("get-state").stdout.strip()

            self.assertEqual("device", state)
            self.assertEqual(2, run.call_count)
            failure = device.transport_summary()["events"][0]
            self.assertEqual("device-offline", failure["classification"])
            self.assertEqual("retrying-read-only", failure["status"])

    def test_read_only_retry_is_bounded_and_final_failure_is_exact(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[offline(("get-state",)) for _ in range(3)],
                ) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(driver.AdbTransportError) as raised:
                    device.run("get-state")

            self.assertEqual(driver.ADB_READ_ONLY_MAX_ATTEMPTS, run.call_count)
            receipt = raised.exception.receipt
            self.assertEqual("fail", receipt["status"])
            self.assertEqual(3, receipt["attempt"])
            self.assertEqual(3, receipt["maximumAttempts"])
            self.assertTrue(receipt["replay"]["performed"])
            self.assertFalse(receipt["replay"]["scheduled"])
            self.assertTrue(receipt["replay"]["suppressed"])

    def test_mutating_and_ambiguous_commands_are_never_replayed(self) -> None:
        commands = (
            ("install", "--no-streaming", "-r", "/tmp/app.apk"),
            ("push", "/tmp/runner.chum5", "/sdcard/Download/runner.chum5"),
            ("shell", "input", "tap", "10", "20"),
            ("shell", "pm", "clear", driver.PACKAGE),
            ("shell", "rm", "-f", "/sdcard/Download/runner.chum5"),
            ("shell", "am", "start", "-W", driver.PACKAGE),
            (
                "shell",
                "uiautomator",
                "dump",
                "--compressed",
                "/sdcard/chummer-editing-window.xml",
            ),
            ("exec-out", "run-as", driver.PACKAGE, "cat", "shared_prefs/a.xml"),
        )
        for arguments in commands:
            with self.subTest(arguments=arguments), tempfile.TemporaryDirectory() as temporary:
                device = self.make_device(Path(temporary))
                timeout = subprocess.TimeoutExpired(arguments, 10)
                with mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=timeout,
                ) as run:
                    with self.assertRaises(driver.AdbTransportError) as raised:
                        device.run(*arguments)

                self.assertEqual(1, run.call_count)
                receipt = raised.exception.receipt
                self.assertEqual("timeout-unknown-outcome", receipt["classification"])
                self.assertEqual("non-replayable", receipt["commandPolicy"])
                self.assertEqual(1, receipt["maximumAttempts"])
                self.assertEqual("unknown-fail-closed", receipt["outcomeMutationAuthority"])
                self.assertFalse(receipt["replay"]["performed"])
                self.assertFalse(receipt["replay"]["scheduled"])
                self.assertTrue(receipt["replay"]["suppressed"])
                visible = 2 if arguments[0] == "shell" else 1
                self.assertEqual(
                    [
                        *arguments[:visible],
                        f"<{len(arguments) - visible} redacted argument(s)>",
                    ],
                    receipt["adbArguments"],
                )
                self.assertEqual(
                    driver._adb_arguments_sha256(arguments),
                    receipt["adbArgumentsSha256"],
                )

    def test_timed_out_swipe_is_reconciled_by_stable_read_only_hierarchy_without_replay(self) -> None:
        xml = (
            "<?xml version='1.0' encoding='UTF-8'?>"
            "<hierarchy><node text='Current viewport' bounds='[0,0][100,100]' />"
            "</hierarchy>UI hierarchy dumped to: /dev/tty"
        )
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            device._display_size = (1080, 2400)
            timeout = subprocess.TimeoutExpired(("shell", "input", "swipe"), 15)
            responses = [
                timeout,
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
                completed(("shell", "input", "keyevent", "4"), ""),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                device.swipe_up()
                device.shell("input", "keyevent", "4")

            self.assertEqual(4, run.call_count)
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(1, sum(arguments[:3] == ("shell", "input", "swipe") for arguments in issued))
            self.assertEqual(
                [driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS] * 2,
                issued[1:3],
            )
            summary = device.transport_summary()
            self.assertEqual(0, summary["terminalFailureCount"])
            original, reconciliation = summary["events"]
            self.assertEqual("fail", original["status"])
            self.assertEqual("reconciled-unknown-swipe", reconciliation["status"])
            self.assertEqual(
                original["evidenceFile"],
                reconciliation["reconcilesEvidenceFile"],
            )
            self.assertFalse(reconciliation["replay"]["performed"])
            self.assertTrue(reconciliation["replay"]["suppressed"])
            self.assertEqual(
                "current-viewport-observed-no-replay",
                reconciliation["outcomeMutationAuthority"],
            )

    def test_divergent_read_only_hierarchies_leave_swipe_outcome_blocked(self) -> None:
        def hierarchy(label: str) -> str:
            return f"<hierarchy><node text='{label}' /></hierarchy>"

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            device._display_size = (1080, 2400)
            responses = [
                subprocess.TimeoutExpired(("shell", "input", "swipe"), 15),
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, hierarchy("one")),
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, hierarchy("two")),
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, hierarchy("three")),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(driver.AdbTransportError):
                    device.swipe_down()
                with self.assertRaises(driver.AdbTransportError) as blocked:
                    device.shell("input", "keyevent", "4")

            self.assertEqual(4, run.call_count)
            self.assertEqual(
                "prior-mutation-outcome-unknown",
                blocked.exception.receipt["classification"],
            )
            self.assertEqual(2, device.transport_summary()["terminalFailureCount"])

    def test_direct_hierarchy_observation_is_the_only_retryable_uiautomator_dump(self) -> None:
        self.assertEqual(
            "read-only-retryable",
            driver.adb_command_retry_policy(
                driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS
            )[0],
        )
        self.assertEqual(
            "non-replayable",
            driver.adb_command_retry_policy(
                (
                    "shell",
                    "uiautomator",
                    "dump",
                    "--compressed",
                    "/sdcard/chummer-editing-window.xml",
                )
            )[0],
        )

    def test_verified_install_and_push_never_replay_unknown_outcomes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            apk = root / "app.apk"
            fixture = root / "runner.chum5"
            apk.write_bytes(b"apk")
            fixture.write_bytes(b"fixture")
            for name, invoke in (
                (
                    "install",
                    lambda device: device.install_verified(
                        apk,
                        driver.sha256(apk),
                        "--no-streaming",
                        "-r",
                    ),
                ),
                (
                    "push",
                    lambda device: device.push_verified(
                        fixture,
                        "/sdcard/Download/runner.chum5",
                        driver.sha256(fixture),
                    ),
                ),
            ):
                with self.subTest(name=name):
                    device = self.make_device(root / name)
                    timeout = subprocess.TimeoutExpired((name,), 10)
                    with mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=timeout,
                    ) as run:
                        with self.assertRaises(driver.AdbTransportError):
                            invoke(device)
                    self.assertEqual(1, run.call_count)

    def test_unknown_mutation_outcome_blocks_every_later_mutation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            timeout = subprocess.TimeoutExpired(("shell", "input", "tap"), 10)
            with mock.patch.object(
                driver.subprocess,
                "run",
                side_effect=timeout,
            ) as run:
                with self.assertRaises(driver.AdbTransportError) as first:
                    device.shell("input", "tap", "10", "20")
                with self.assertRaises(driver.AdbTransportError) as suppressed:
                    device.shell("rm", "-f", "/sdcard/Download/runner.chum5")

            self.assertEqual(1, run.call_count)
            self.assertEqual(
                "timeout-unknown-outcome",
                first.exception.receipt["classification"],
            )
            receipt = suppressed.exception.receipt
            self.assertEqual("prior-mutation-outcome-unknown", receipt["classification"])
            self.assertFalse(receipt["commandInvocationPerformed"])
            self.assertTrue(receipt["replay"]["suppressed"])
            self.assertEqual(
                first.exception.receipt["evidenceFile"],
                receipt["blockedBy"]["evidenceFile"],
            )
            self.assertEqual(
                receipt,
                json.loads(
                    (evidence / "adb-transport-event-0002.json").read_text(
                        encoding="utf-8"
                    )
                ),
            )

    def test_install_toctou_drift_fails_after_exactly_one_install(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            apk = root / "app.apk"
            apk.write_bytes(b"apk")
            expected = "a" * 64
            changed = "b" * 64
            device = self.make_device(root / "evidence")
            with (
                mock.patch.object(driver, "sha256", side_effect=[expected, changed]),
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    return_value=completed(("install",), "Success\n"),
                ) as run,
            ):
                with self.assertRaisesRegex(
                    RuntimeError,
                    "APK digest changed across the one-shot install",
                ):
                    device.install_verified(apk, expected, "--no-streaming", "-r")

            self.assertEqual(1, run.call_count)
            self.assertEqual(
                ("install", "--no-streaming", "-r", str(apk.resolve())),
                tuple(run.call_args.args[0][3:]),
            )

    def test_unrecognized_adb_failure_is_classified_and_not_retried(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            error = subprocess.CalledProcessError(
                17,
                ("shell", "getprop", "ro.hardware"),
                stderr="permission denied by device policy",
            )
            with mock.patch.object(
                driver.subprocess,
                "run",
                side_effect=error,
            ) as run:
                with self.assertRaises(driver.AdbTransportError) as raised:
                    device.shell("getprop", "ro.hardware")

            self.assertEqual(1, run.call_count)
            self.assertEqual("unclassified-adb-failure", raised.exception.receipt["classification"])
            self.assertEqual(
                "unclassified-fail-closed",
                raised.exception.receipt["classificationAuthority"],
            )
            self.assertEqual("read-only-retryable", raised.exception.receipt["commandPolicy"])
            self.assertFalse(raised.exception.receipt["replay"]["eligible"])
            self.assertFalse(raised.exception.receipt["replay"]["performed"])
            self.assertTrue(raised.exception.receipt["replay"]["suppressed"])
            self.assertEqual(1, device.transport_summary()["eventCount"])

    def test_retry_whitelist_rejects_logcat_clear_and_quoted_device_is_classified(self) -> None:
        self.assertEqual(
            "non-replayable",
            driver.adb_command_retry_policy(("logcat", "-d", "-c"))[0],
        )
        missing = subprocess.CalledProcessError(
            1,
            ("get-state",),
            stderr="error: device '10.0.0.5:5555' not found",
        )
        self.assertEqual(("device-missing", True), driver.classify_adb_failure(missing))

    def test_cleanup_authority_suppresses_replay_after_ambiguous_preclean(self) -> None:
        remote: dict[str, object] = {
            "precleanAttempted": True,
            "precleaned": False,
            "cleanupAttempted": False,
            "cleanupReplaySuppressed": False,
        }
        self.assertFalse(driver.authorize_remote_cleanup_once(remote))
        self.assertFalse(remote["cleanupAttempted"])
        self.assertTrue(remote["cleanupReplaySuppressed"])

        clean_remote: dict[str, object] = {
            "precleanAttempted": True,
            "precleaned": True,
            "cleanupAttempted": False,
            "cleanupReplaySuppressed": False,
        }
        self.assertTrue(driver.authorize_remote_cleanup_once(clean_remote))
        self.assertTrue(clean_remote["cleanupAttempted"])
        self.assertFalse(driver.authorize_remote_cleanup_once(clean_remote))
        self.assertTrue(clean_remote["cleanupReplaySuppressed"])


if __name__ == "__main__":
    unittest.main()
