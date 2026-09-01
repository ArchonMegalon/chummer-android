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


def strict_hierarchy_xml(label: str = "Priorities") -> str:
    return (
        "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>"
        "<hierarchy rotation='0'><node index='0' "
        f"text='{label}' resource-id='com.myexternalbrain.chummer:id/"
        "creation-prerequisite-page' class='android.view.ViewGroup' "
        "package='com.myexternalbrain.chummer' content-desc='' "
        "checkable='false' checked='false' clickable='false' enabled='true' "
        "focusable='false' focused='false' scrollable='false' "
        "long-clickable='false' password='false' selected='false' "
        "bounds='[0,0][1080,2400]' /></hierarchy>"
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

    def test_read_only_retry_is_not_scheduled_without_deadline_headroom(self) -> None:
        now = [90.0]
        arguments = (
            "exec-out",
            "cat",
            driver.ADB_FILE_HIERARCHY_REMOTE_PATH,
        )

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            self.assertTrue(check)
            self.assertTrue(capture_output)
            self.assertTrue(text)
            self.assertEqual(arguments, tuple(command[3:]))
            now[0] += timeout
            raise subprocess.TimeoutExpired(command, timeout)

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "monotonic", side_effect=lambda: now[0]),
                self.assertRaises(driver.AdbTransportError) as raised,
            ):
                device.run(*arguments, timeout=1.0, deadline=91.5)

            summary = device.transport_summary()

        self.assertEqual(1, run.call_count)
        self.assertEqual("fail", raised.exception.receipt["status"])
        self.assertFalse(raised.exception.receipt["replay"]["scheduled"])
        self.assertEqual(1, summary["terminalFailureCount"])
        self.assertEqual(["fail"], [event["status"] for event in summary["events"]])

    def test_read_only_retry_deadline_overrun_gets_terminal_receipt(self) -> None:
        now = [90.0]
        arguments = (
            "exec-out",
            "cat",
            driver.ADB_FILE_HIERARCHY_REMOTE_PATH,
        )

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            self.assertEqual(arguments, tuple(command[3:]))
            now[0] += timeout
            raise subprocess.TimeoutExpired(command, timeout)

        def delayed_retry(seconds: float) -> None:
            self.assertEqual(driver.ADB_READ_ONLY_RETRY_DELAY_SECONDS, seconds)
            now[0] += seconds + 0.2

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "monotonic", side_effect=lambda: now[0]),
                mock.patch.object(driver.time, "sleep", side_effect=delayed_retry),
                self.assertRaises(driver.AdbTransportError) as raised,
            ):
                device.run(*arguments, timeout=1.0, deadline=92.1)

            summary = device.transport_summary()

        self.assertEqual(1, run.call_count)
        self.assertEqual(
            ["retrying-read-only", "fail"],
            [event["status"] for event in summary["events"]],
        )
        terminal = raised.exception.receipt
        self.assertEqual("caller-deadline-exhausted-before-retry", terminal["classification"])
        self.assertEqual("caller-owned-deadline-before-command", terminal["classificationAuthority"])
        self.assertEqual(2, terminal["attempt"])
        self.assertFalse(terminal["commandInvocationPerformed"])
        self.assertFalse(terminal["replay"]["scheduled"])
        self.assertEqual(1, summary["terminalFailureCount"])

    def test_read_only_hierarchy_deadline_reserves_headroom_for_retry(self) -> None:
        now = [90.0]
        xml = (
            "<?xml version='1.0' encoding='UTF-8'?>"
            "<hierarchy><node text='Creation dashboard' "
            "resource-id='com.chummer6.android:id/creation-wizard-dashboard' "
            "bounds='[0,0][100,100]' /></hierarchy>"
            "UI hierarchy dumped to: /dev/tty"
        )
        observed_timeouts: list[float] = []

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            self.assertTrue(check)
            self.assertTrue(capture_output)
            self.assertTrue(text)
            self.assertEqual(
                driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS,
                tuple(command[3:]),
            )
            observed_timeouts.append(timeout)
            if len(observed_timeouts) == 1:
                now[0] += timeout
                raise subprocess.TimeoutExpired(command, timeout)
            return completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml)

        def delayed_retry(seconds: float) -> None:
            self.assertEqual(driver.ADB_READ_ONLY_RETRY_DELAY_SECONDS, seconds)
            now[0] += seconds

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            caller_deadline = now[0] + 30.0
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "monotonic", side_effect=lambda: now[0]),
                mock.patch.object(driver.time, "sleep", side_effect=delayed_retry),
            ):
                nodes = device.read_only_hierarchy(deadline=caller_deadline)
                summary = device.transport_summary()

        self.assertEqual("Creation dashboard", nodes[0].attributes["text"])
        self.assertEqual(2, run.call_count)
        self.assertEqual(
            [driver.ADB_READ_ONLY_HIERARCHY_ATTEMPT_MAX_SECONDS] * 2,
            observed_timeouts,
        )
        self.assertLess(now[0], caller_deadline)
        self.assertEqual(
            ["retrying-read-only", "recovered-read-only"],
            [event["status"] for event in summary["events"]],
        )
        self.assertEqual(0, summary["terminalFailureCount"])
        self.assertTrue(summary["events"][0]["replay"]["scheduled"])
        self.assertTrue(summary["events"][1]["replay"]["performed"])

    def test_complete_hierarchy_stdout_becomes_authority_after_process_timeout(self) -> None:
        xml = strict_hierarchy_xml()

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            self.assertEqual(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, tuple(command[3:]))
            raise subprocess.TimeoutExpired(
                command,
                timeout,
                output=xml.encode("utf-8"),
                stderr=b"",
            )

        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            with mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run:
                nodes = device.read_only_hierarchy(
                    deadline=driver.time.monotonic() + 30.0
                )
                summary = device.transport_summary()

            self.assertEqual(1, run.call_count)
            self.assertEqual("Priorities", nodes[0].attributes["text"])
            self.assertEqual("not-started", summary["status"])
            self.assertEqual(0, summary["terminalFailureCount"])
            self.assertEqual(1, summary["eventCount"])
            recovery = summary["events"][0]
            self.assertEqual("recovered-read-only", recovery["status"])
            self.assertEqual("timeout-output-complete", recovery["classification"])
            self.assertEqual(
                "complete-well-formed-read-only-timeout-stdout",
                recovery["classificationAuthority"],
            )
            self.assertEqual(1, recovery["attempt"])
            self.assertFalse(recovery["replay"]["performed"])
            self.assertTrue(recovery["replay"]["suppressed"])
            timeout_output = recovery["timeoutOutput"]
            self.assertEqual(len(xml.encode("utf-8")), timeout_output["stdoutBytes"])
            self.assertEqual(1, timeout_output["hierarchyNodeCount"])
            self.assertEqual(xml, timeout_output["stdout"])

    def test_timeout_hierarchy_recovery_rejects_incomplete_or_ambiguous_output(self) -> None:
        invalid_outputs = (
            "<hierarchy><node text='partial' />",
            "<hierarchy></hierarchy>",
            "<hierarchy><node /></hierarchy><hierarchy><node /></hierarchy>",
            "unexpected<hierarchy><node /></hierarchy>",
            "<hierarchy><node /></hierarchy>unexpected",
            "<hierarchy rotation='0'><foreign><node /></foreign></hierarchy>",
            "<hierarchy rotation='0'><node /></hierarchy>",
            strict_hierarchy_xml("x" * driver.ADB_TIMEOUT_HIERARCHY_MAX_BYTES),
            strict_hierarchy_xml("\udcff"),
            b"<hierarchy><node text='\xff' /></hierarchy>",
        )
        for output in invalid_outputs:
            with self.subTest(output=output):
                error = subprocess.TimeoutExpired(
                    driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS,
                    8,
                    output=output,
                )
                self.assertIsNone(driver._complete_timed_out_hierarchy_output(error))
        for stderr in (
            "error: device unauthorized",
            b"error: device offline",
            "cannot connect to daemon",
        ):
            with self.subTest(stderr=stderr):
                error = subprocess.TimeoutExpired(
                    driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS,
                    8,
                    output=strict_hierarchy_xml().encode("utf-8"),
                    stderr=stderr,
                )
                self.assertIsNone(driver._complete_timed_out_hierarchy_output(error))

    def test_complete_timeout_output_after_retry_preserves_attempt_chain(self) -> None:
        xml = strict_hierarchy_xml("Recovered priorities")
        attempts = [
            subprocess.TimeoutExpired(
                driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS,
                8,
                output=b"<hierarchy>",
            ),
            subprocess.TimeoutExpired(
                driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS,
                8,
                output=xml.encode("utf-8"),
            ),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=attempts) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                nodes = device.read_only_hierarchy(
                    deadline=driver.time.monotonic() + 30.0
                )
                summary = device.transport_summary()

        self.assertEqual(2, run.call_count)
        self.assertEqual("Recovered priorities", nodes[0].attributes["text"])
        self.assertEqual(
            ["retrying-read-only", "recovered-read-only"],
            [event["status"] for event in summary["events"]],
        )
        recovery = summary["events"][1]
        self.assertEqual(2, recovery["attempt"])
        self.assertTrue(recovery["replay"]["performed"])
        self.assertEqual(0, summary["terminalFailureCount"])

    def test_late_read_only_hierarchy_shares_caller_lease_across_all_retries(self) -> None:
        now = [90.0]
        xml = (
            "<hierarchy><node text='Post-confirm dashboard' "
            "resource-id='com.chummer6.android:id/creation-wizard-dashboard' "
            "bounds='[0,0][100,100]' /></hierarchy>"
        )
        observed_timeouts: list[float] = []

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            self.assertTrue(check)
            self.assertTrue(capture_output)
            self.assertTrue(text)
            self.assertEqual(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, tuple(command[3:]))
            observed_timeouts.append(timeout)
            if len(observed_timeouts) < 3:
                now[0] += timeout
                raise subprocess.TimeoutExpired(command, timeout)
            return completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml)

        def delayed_retry(seconds: float) -> None:
            self.assertEqual(driver.ADB_READ_ONLY_RETRY_DELAY_SECONDS, seconds)
            now[0] += seconds

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            caller_deadline = now[0] + 10.0
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "monotonic", side_effect=lambda: now[0]),
                mock.patch.object(driver.time, "sleep", side_effect=delayed_retry),
            ):
                nodes = device.read_only_hierarchy(deadline=caller_deadline)
                summary = device.transport_summary()

        self.assertEqual("Post-confirm dashboard", nodes[0].attributes["text"])
        self.assertEqual(3, run.call_count)
        self.assertEqual([2.5, 2.5, 2.5], observed_timeouts)
        self.assertLessEqual(now[0], caller_deadline)
        self.assertEqual(
            ["retrying-read-only", "retrying-read-only", "recovered-read-only"],
            [event["status"] for event in summary["events"]],
        )
        self.assertEqual(0, summary["terminalFailureCount"])

    def test_full_read_only_hierarchy_lease_preserves_third_attempt(self) -> None:
        now = [90.0]
        xml = "<hierarchy><node text='Third attempt dashboard' /></hierarchy>"
        observed_timeouts: list[float] = []

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            arguments = tuple(command[3:])
            self.assertEqual(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, arguments)
            observed_timeouts.append(timeout)
            if len(observed_timeouts) < 3:
                now[0] += timeout
                raise subprocess.TimeoutExpired(command, timeout)
            return completed(arguments, xml)

        def delayed_retry(seconds: float) -> None:
            now[0] += seconds

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            caller_deadline = now[0] + 30.0
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "monotonic", side_effect=lambda: now[0]),
                mock.patch.object(driver.time, "sleep", side_effect=delayed_retry),
            ):
                nodes = device.read_only_hierarchy(deadline=caller_deadline)
                summary = device.transport_summary()

        self.assertEqual("Third attempt dashboard", nodes[0].attributes["text"])
        self.assertEqual(3, run.call_count)
        self.assertEqual(
            [driver.ADB_READ_ONLY_HIERARCHY_ATTEMPT_MAX_SECONDS] * 3,
            observed_timeouts,
        )
        self.assertLess(now[0], caller_deadline)
        self.assertEqual(
            ["retrying-read-only", "retrying-read-only", "recovered-read-only"],
            [event["status"] for event in summary["events"]],
        )
        self.assertEqual(0, summary["terminalFailureCount"])

    def test_partitioned_read_only_hierarchy_exhausts_exactly_three_attempts(self) -> None:
        now = [90.0]
        observed_timeouts: list[float] = []

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            self.assertEqual(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, tuple(command[3:]))
            observed_timeouts.append(timeout)
            now[0] += timeout
            raise subprocess.TimeoutExpired(command, timeout)

        def delayed_retry(seconds: float) -> None:
            now[0] += seconds

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            caller_deadline = now[0] + 10.0
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "monotonic", side_effect=lambda: now[0]),
                mock.patch.object(driver.time, "sleep", side_effect=delayed_retry),
                self.assertRaises(driver.AdbTransportError),
            ):
                device.read_only_hierarchy(deadline=caller_deadline)
            summary = device.transport_summary()

        self.assertEqual(3, run.call_count)
        self.assertEqual([2.5, 2.5, 2.5], observed_timeouts)
        self.assertLessEqual(now[0], caller_deadline)
        self.assertEqual(
            ["retrying-read-only", "retrying-read-only", "fail"],
            [event["status"] for event in summary["events"]],
        )
        self.assertEqual(1, summary["terminalFailureCount"])
        self.assertIsNone(device._mutation_blocker)

    def test_read_only_hierarchy_refuses_to_spend_reserved_retry_delays(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.time, "monotonic", return_value=90.0),
                mock.patch.object(driver.subprocess, "run") as run,
                self.assertRaisesRegex(
                    driver.AdbOperationDeadlineExceeded,
                    "retry lease expired",
                ),
            ):
                device.read_only_hierarchy(deadline=92.5)

        run.assert_not_called()

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

    def test_exact_package_pidof_exit_one_without_output_means_no_process(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            absent = subprocess.CompletedProcess(
                ("shell", "pidof", driver.PACKAGE),
                1,
                stdout="",
                stderr="",
            )
            with mock.patch.object(
                driver.subprocess,
                "run",
                return_value=absent,
            ) as run:
                observed = device.shell("pidof", driver.PACKAGE, timeout=15)

            self.assertEqual("", observed)
            self.assertFalse(run.call_args.kwargs["check"])
            self.assertEqual("not-started", device.transport_summary()["status"])
            self.assertEqual(0, device.transport_summary()["eventCount"])

    def test_package_pidof_nonempty_failure_remains_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            denied = subprocess.CompletedProcess(
                ("shell", "pidof", driver.PACKAGE),
                1,
                stdout="",
                stderr="permission denied by device policy",
            )
            with mock.patch.object(
                driver.subprocess,
                "run",
                return_value=denied,
            ) as run:
                with self.assertRaises(driver.AdbTransportError) as raised:
                    device.shell("pidof", driver.PACKAGE, timeout=15)

            self.assertEqual(1, run.call_count)
            self.assertEqual(
                "unclassified-adb-failure",
                raised.exception.receipt["classification"],
            )
            self.assertEqual("fail", device.transport_summary()["status"])
            self.assertEqual(1, device.transport_summary()["terminalFailureCount"])

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
            ("shell", *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS),
            ("shell", *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS),
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
            self.assertEqual(
                ["shell", "input", "swipe", "<5 redacted argument(s)>"],
                original["adbArguments"],
            )
            self.assertEqual(original["adbArguments"], reconciliation["adbArguments"])
            self.assertFalse(reconciliation["replay"]["performed"])
            self.assertTrue(reconciliation["replay"]["suppressed"])
            self.assertEqual(
                "current-viewport-observed-no-replay",
                reconciliation["outcomeMutationAuthority"],
            )

    def test_owned_file_only_swipe_fails_closed_without_direct_reconciliation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            device._display_size = (1080, 2400)
            timeout = subprocess.TimeoutExpired(
                ("shell", "input", "swipe"),
                15,
            )
            with mock.patch.object(
                driver.subprocess,
                "run",
                side_effect=timeout,
            ) as run:
                with self.assertRaises(driver.AdbTransportError):
                    device.swipe_up(
                        deadline=driver.time.monotonic() + 60.0,
                        allow_direct_reconciliation=False,
                    )

            self.assertEqual(1, run.call_count)
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(1, len(issued))
            self.assertEqual(("shell", "input", "swipe"), issued[0][:3])
            self.assertNotIn(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, issued)
            summary = device.transport_summary()
            self.assertEqual(1, summary["terminalFailureCount"])
            self.assertNotIn(
                "reconciled-unknown-swipe",
                [event["status"] for event in summary["events"]],
            )

    def test_swipe_reconciliation_transport_recovery_preserves_mutation_blocker(self) -> None:
        xml = (
            "<?xml version='1.0' encoding='UTF-8'?>"
            "<hierarchy><node text='Current viewport' bounds='[0,0][100,100]' />"
            "</hierarchy>UI hierarchy dumped to: /dev/tty"
        )
        swipe_timeout = subprocess.TimeoutExpired(
            ("shell", "input", "swipe"),
            15,
        )
        hierarchy_timeout = subprocess.TimeoutExpired(
            driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS,
            driver.ADB_READ_ONLY_HIERARCHY_ATTEMPT_MAX_SECONDS,
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            device._display_size = (1080, 2400)
            responses = [
                swipe_timeout,
                hierarchy_timeout,
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(driver.AdbTransportError) as swipe_failure:
                    device.swipe_up(deadline=driver.time.monotonic() + 60.0)
                with self.assertRaises(driver.AdbTransportError) as blocked_mutation:
                    device.shell("input", "keyevent", "4")

            self.assertEqual(3, run.call_count)
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(1, issued.count(issued[0]))
            self.assertEqual(
                [driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS] * 2,
                issued[1:],
            )
            summary = device.transport_summary()

        self.assertEqual(
            ["fail", "retrying-read-only", "recovered-read-only", "fail"],
            [event["status"] for event in summary["events"]],
        )
        self.assertNotIn(
            "reconciled-unknown-swipe",
            [event["status"] for event in summary["events"]],
        )
        original, _retry, _recovery, suppression = summary["events"]
        self.assertEqual(original, swipe_failure.exception.receipt)
        self.assertEqual(
            "prior-mutation-outcome-unknown",
            blocked_mutation.exception.receipt["classification"],
        )
        self.assertEqual(suppression, blocked_mutation.exception.receipt)
        self.assertFalse(suppression["commandInvocationPerformed"])
        self.assertEqual(0, suppression["attempt"])
        self.assertEqual(
            original["evidenceFile"],
            suppression["blockedBy"]["evidenceFile"],
        )
        self.assertEqual(2, summary["terminalFailureCount"])

    def test_timed_out_hierarchy_dump_is_reconciled_without_dump_replay(self) -> None:
        xml = (
            "<?xml version='1.0' encoding='UTF-8'?>"
            "<hierarchy><node text='Stable preview' bounds='[0,0][100,100]' />"
            "</hierarchy>UI hierarchy dumped to: /dev/tty"
        )
        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            responses = [
                completed(remove_arguments, ""),
                subprocess.TimeoutExpired(dump_arguments, 52),
                completed(
                    ("exec-out", "cat", driver.ADB_FILE_HIERARCHY_REMOTE_PATH),
                    xml,
                ),
                completed(
                    ("exec-out", "cat", driver.ADB_FILE_HIERARCHY_REMOTE_PATH),
                    xml,
                ),
                completed(("shell", "input", "keyevent", "4"), ""),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                nodes = device.hierarchy(deadline=driver.time.monotonic() + 150)
                device.shell("input", "keyevent", "4")

            self.assertEqual("Stable preview", nodes[0].attributes["text"])
            self.assertEqual(5, run.call_count)
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(1, issued.count(dump_arguments))
            self.assertEqual(
                [
                    ("exec-out", "cat", driver.ADB_FILE_HIERARCHY_REMOTE_PATH),
                ] * 2,
                issued[2:4],
            )
            summary = device.transport_summary()
            self.assertEqual(0, summary["terminalFailureCount"])
            original, reconciliation = summary["events"]
            self.assertEqual("fail", original["status"])
            self.assertEqual(
                "reconciled-unknown-hierarchy-dump",
                reconciliation["status"],
            )
            self.assertEqual(
                original["evidenceFile"],
                reconciliation["reconcilesEvidenceFile"],
            )
            self.assertFalse(reconciliation["replay"]["performed"])
            self.assertTrue(reconciliation["replay"]["suppressed"])
            self.assertEqual(
                "current-hierarchy-observed-no-dump-replay",
                reconciliation["outcomeMutationAuthority"],
            )
            self.assertEqual(
                2,
                reconciliation["readOnlyObservation"]["consecutiveMatching"],
            )
            self.assertEqual(
                "fresh-owned-file",
                reconciliation["readOnlyObservation"]["mode"],
            )
            self.assertEqual(
                ["shell", *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS],
                reconciliation["readOnlyObservation"][
                    "freshnessBarrierArguments"
                ],
            )
            self.assertEqual(
                "exact-observation-bytes",
                reconciliation["readOnlyObservation"]["matchingAuthority"],
            )
            self.assertEqual(
                driver.ADB_HIERARCHY_DUMP_RECONCILIATION_READ_ATTEMPT_MAX_SECONDS,
                reconciliation["readOnlyObservation"][
                    "readAttemptMaximumSeconds"
                ],
            )
            self.assertEqual(
                driver.ADB_HIERARCHY_DUMP_RECONCILIATION_MAX_SECONDS,
                reconciliation["readOnlyObservation"][
                    "maximumObservationSeconds"
                ],
            )
            self.assertTrue(
                all(
                    call.kwargs["timeout"]
                    <= driver.ADB_HIERARCHY_DUMP_RECONCILIATION_READ_ATTEMPT_MAX_SECONDS
                    for call in run.call_args_list[2:4]
                )
            )

    def test_hierarchy_dump_attempt_preserves_owned_file_reconciliation_lease(self) -> None:
        reserved = (
            driver.ADB_HIERARCHY_DUMP_RECONCILIATION_REQUIRED_CONSECUTIVE
            * driver.ADB_HIERARCHY_DUMP_RECONCILIATION_READ_ATTEMPT_MAX_SECONDS
            + (
                driver.ADB_HIERARCHY_DUMP_RECONCILIATION_REQUIRED_CONSECUTIVE - 1
            )
            * driver.ADB_HIERARCHY_DUMP_RECONCILIATION_DELAY_SECONDS
            + driver.ADB_HIERARCHY_DUMP_RECONCILIATION_HEADROOM_SECONDS
        )
        with mock.patch.object(driver.time, "monotonic", return_value=100.0):
            self.assertEqual(
                1.0,
                driver._hierarchy_dump_attempt_timeout(
                    deadline=101.0 + reserved,
                    maximum=driver.ADB_FILE_HIERARCHY_DUMP_ATTEMPT_MAX_SECONDS,
                ),
            )
            with self.assertRaises(driver.AdbOperationDeadlineExceeded):
                driver._hierarchy_dump_attempt_timeout(
                    deadline=100.0 + reserved,
                    maximum=driver.ADB_FILE_HIERARCHY_DUMP_ATTEMPT_MAX_SECONDS,
                )

    def test_missing_owned_dump_uses_two_stable_direct_reads_without_replay(self) -> None:
        xml = (
            "<hierarchy><node text='Stable direct preview' "
            "bounds='[0,0][100,100]' /></hierarchy>"
        )
        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        owned_arguments = (
            "exec-out",
            "cat",
            driver.ADB_FILE_HIERARCHY_REMOTE_PATH,
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            responses = [
                completed(remove_arguments, ""),
                subprocess.TimeoutExpired(dump_arguments, 37),
                *[
                    completed(owned_arguments, "")
                    for _ in range(
                        driver.ADB_HIERARCHY_DUMP_RECONCILIATION_MAX_OBSERVATIONS
                    )
                ],
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
                completed(("shell", "input", "keyevent", "4"), ""),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                nodes = device.hierarchy(deadline=driver.time.monotonic() + 150)
                device.shell("input", "keyevent", "4")

            self.assertEqual("Stable direct preview", nodes[0].attributes["text"])
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(1, issued.count(dump_arguments))
            self.assertEqual(
                driver.ADB_HIERARCHY_DUMP_RECONCILIATION_MAX_OBSERVATIONS,
                issued.count(owned_arguments),
            )
            self.assertEqual(
                driver.ADB_HIERARCHY_DUMP_RECONCILIATION_REQUIRED_CONSECUTIVE,
                issued.count(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS),
            )
            summary = device.transport_summary()
            self.assertEqual(0, summary["terminalFailureCount"])
            original, reconciliation = summary["events"]
            self.assertEqual(
                original["evidenceFile"],
                reconciliation["reconcilesEvidenceFile"],
            )
            self.assertEqual(
                "direct-current-hierarchy",
                reconciliation["readOnlyObservation"]["mode"],
            )
            self.assertEqual(
                list(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS),
                reconciliation["readOnlyObservation"]["arguments"],
            )
            self.assertFalse(reconciliation["replay"]["performed"])

    def test_owned_file_local_deadline_hands_off_to_direct_reads(self) -> None:
        xml = "<hierarchy><node text='Deadline handoff' /></hierarchy>"
        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        owned_arguments = (
            "exec-out",
            "cat",
            driver.ADB_FILE_HIERARCHY_REMOTE_PATH,
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with mock.patch.object(
                driver.subprocess,
                "run",
                side_effect=[
                    completed(remove_arguments, ""),
                    subprocess.TimeoutExpired(dump_arguments, 37),
                ],
            ):
                device.run(*remove_arguments)
                with self.assertRaises(driver.AdbTransportError) as raised:
                    device.run(*dump_arguments)

            remaining_calls = 0

            def remaining(*, deadline: float | None, maximum: float) -> float:
                nonlocal remaining_calls
                remaining_calls += 1
                if remaining_calls == 3:
                    raise driver.AdbOperationDeadlineExceeded("owned file local deadline")
                return maximum

            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        completed(owned_arguments, ""),
                        completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
                        completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
                    ],
                ),
                mock.patch.object(
                    driver,
                    "_remaining_operation_timeout",
                    side_effect=remaining,
                ),
                mock.patch.object(driver.time, "sleep"),
            ):
                nodes = device._reconcile_unknown_hierarchy_dump(
                    raised.exception,
                    dump_arguments,
                    deadline=driver.time.monotonic() + 150,
                )

            self.assertIsNotNone(nodes)
            self.assertEqual("Deadline handoff", nodes[0].attributes["text"])
            reconciliation = device.transport_summary()["events"][1]
            self.assertEqual(
                "direct-current-hierarchy",
                reconciliation["readOnlyObservation"]["mode"],
            )
            self.assertFalse(reconciliation["replay"]["performed"])

    def test_expired_caller_deadline_never_starts_direct_observation(self) -> None:
        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with mock.patch.object(
                driver.subprocess,
                "run",
                side_effect=[
                    completed(remove_arguments, ""),
                    subprocess.TimeoutExpired(dump_arguments, 37),
                ],
            ):
                device.run(*remove_arguments)
                with self.assertRaises(driver.AdbTransportError) as raised:
                    device.run(*dump_arguments)

            with mock.patch.object(driver.subprocess, "run") as run:
                nodes = device._reconcile_unknown_hierarchy_dump(
                    raised.exception,
                    dump_arguments,
                    deadline=driver.time.monotonic() - 1,
                )

            self.assertIsNone(nodes)
            run.assert_not_called()
            self.assertEqual(1, device.transport_summary()["terminalFailureCount"])

    def test_direct_transport_recovery_preserves_original_blocker(self) -> None:
        xml = "<hierarchy><node text='Recovered transport' /></hierarchy>"
        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        owned_arguments = (
            "exec-out",
            "cat",
            driver.ADB_FILE_HIERARCHY_REMOTE_PATH,
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            responses = [
                completed(remove_arguments, ""),
                subprocess.TimeoutExpired(dump_arguments, 37),
                *[
                    completed(owned_arguments, "")
                    for _ in range(
                        driver.ADB_HIERARCHY_DUMP_RECONCILIATION_MAX_OBSERVATIONS
                    )
                ],
                offline(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS),
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses),
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(driver.AdbTransportError):
                    device.hierarchy(deadline=driver.time.monotonic() + 150)
                with self.assertRaises(driver.AdbTransportError) as blocked:
                    device.shell("input", "keyevent", "4")

            statuses = [
                event["status"] for event in device.transport_summary()["events"]
            ]
            self.assertEqual(
                ["fail", "retrying-read-only", "recovered-read-only", "fail"],
                statuses,
            )
            self.assertEqual(
                "prior-mutation-outcome-unknown",
                blocked.exception.receipt["classification"],
            )

    def test_direct_reconciliation_requires_raw_byte_identity(self) -> None:
        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        owned_arguments = (
            "exec-out",
            "cat",
            driver.ADB_FILE_HIERARCHY_REMOTE_PATH,
        )
        semantically_equal = (
            "<hierarchy><node text='Same' /></hierarchy>",
            "<hierarchy> <node text='Same' /></hierarchy>",
            "<hierarchy><node text='Same'/> </hierarchy>",
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            responses = [
                completed(remove_arguments, ""),
                subprocess.TimeoutExpired(dump_arguments, 37),
                *[
                    completed(owned_arguments, "")
                    for _ in range(
                        driver.ADB_HIERARCHY_DUMP_RECONCILIATION_MAX_OBSERVATIONS
                    )
                ],
                *[
                    completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml)
                    for xml in semantically_equal
                ],
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses),
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(driver.AdbTransportError):
                    device.hierarchy(deadline=driver.time.monotonic() + 150)
                with self.assertRaises(driver.AdbTransportError):
                    device.shell("input", "keyevent", "4")

            self.assertNotIn(
                "reconciled-unknown-hierarchy-dump",
                [event["status"] for event in device.transport_summary()["events"]],
            )

    def test_direct_reconciliation_accepts_invalid_then_two_identical_complete_reads(self) -> None:
        xml = "<hierarchy><node text='Third observation' /></hierarchy>"
        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        owned_arguments = (
            "exec-out",
            "cat",
            driver.ADB_FILE_HIERARCHY_REMOTE_PATH,
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            responses = [
                completed(remove_arguments, ""),
                subprocess.TimeoutExpired(dump_arguments, 37),
                *[
                    completed(owned_arguments, "")
                    for _ in range(
                        driver.ADB_HIERARCHY_DUMP_RECONCILIATION_MAX_OBSERVATIONS
                    )
                ],
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, "<hierarchy>"),
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
                completed(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, xml),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses),
                mock.patch.object(driver.time, "sleep"),
            ):
                nodes = device.hierarchy(deadline=driver.time.monotonic() + 150)

            self.assertEqual("Third observation", nodes[0].attributes["text"])
            reconciliation = device.transport_summary()["events"][1]
            self.assertEqual(
                3,
                reconciliation["readOnlyObservation"]["observationsPerformed"],
            )

    def test_slow_dump_reserves_exact_direct_reconciliation_lease(self) -> None:
        xml = "<hierarchy><node text='Recovered preview' /></hierarchy>"
        null_root = "ERROR: null root node returned by UiTestAutomationBridge."
        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        owned_arguments = (
            "exec-out",
            "cat",
            driver.ADB_FILE_HIERARCHY_REMOTE_PATH,
        )
        now = [90.0]
        dump_timeouts: list[float] = []
        direct_timeouts: list[float] = []
        direct_outputs = [null_root, xml, xml]
        direct_started_at: list[float] = []

        def monotonic() -> float:
            return now[0]

        def sleep(seconds: float) -> None:
            now[0] += seconds

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            self.assertTrue(check)
            self.assertTrue(capture_output)
            self.assertTrue(text)
            arguments = tuple(command[3:])
            if arguments == remove_arguments:
                return completed(arguments, "")
            if arguments == dump_arguments:
                dump_timeouts.append(timeout)
                now[0] += timeout
                raise subprocess.TimeoutExpired(command, timeout)
            if arguments == owned_arguments:
                return completed(arguments, "")
            self.assertEqual(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS, arguments)
            if not direct_started_at:
                direct_started_at.append(now[0])
            direct_timeouts.append(timeout)
            now[0] += timeout
            return completed(arguments, direct_outputs[len(direct_timeouts) - 1])

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            caller_deadline = now[0] + 75.0
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "monotonic", side_effect=monotonic),
                mock.patch.object(driver.time, "sleep", side_effect=sleep),
            ):
                nodes = device.hierarchy(deadline=caller_deadline)
                summary = device.transport_summary()

        self.assertEqual("Recovered preview", nodes[0].attributes["text"])
        self.assertEqual(
            [driver.ADB_FILE_HIERARCHY_DUMP_ATTEMPT_MAX_SECONDS],
            dump_timeouts,
        )
        self.assertEqual(
            [driver.ADB_HIERARCHY_DUMP_DIRECT_RECONCILIATION_READ_ATTEMPT_MAX_SECONDS] * 3,
            direct_timeouts,
        )
        self.assertLessEqual(
            now[0] - direct_started_at[0],
            driver.ADB_HIERARCHY_DUMP_DIRECT_RECONCILIATION_MAX_SECONDS,
        )
        self.assertLessEqual(now[0], caller_deadline)
        issued = [tuple(invocation.args[0][3:]) for invocation in run.call_args_list]
        self.assertEqual(1, issued.count(remove_arguments))
        self.assertEqual(1, issued.count(dump_arguments))
        self.assertEqual(
            driver.ADB_HIERARCHY_DUMP_RECONCILIATION_MAX_OBSERVATIONS,
            issued.count(owned_arguments),
        )
        self.assertEqual(3, issued.count(driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS))
        self.assertEqual(0, summary["terminalFailureCount"])
        original, reconciliation = summary["events"]
        self.assertEqual("fail", original["status"])
        self.assertEqual("reconciled-unknown-hierarchy-dump", reconciliation["status"])
        self.assertEqual(
            original["evidenceFile"],
            reconciliation["reconcilesEvidenceFile"],
        )
        self.assertEqual(
            "direct-current-hierarchy",
            reconciliation["readOnlyObservation"]["mode"],
        )
        self.assertEqual(3, reconciliation["readOnlyObservation"]["observationsPerformed"])

    def test_divergent_hierarchy_dump_reconciliation_stays_blocked(self) -> None:
        def hierarchy(label: str) -> str:
            return f"<hierarchy><node text='{label}' /></hierarchy>"

        remove_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
        )
        dump_arguments = (
            "shell",
            *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            responses = [
                completed(remove_arguments, ""),
                subprocess.TimeoutExpired(dump_arguments, 52),
                *[
                    completed(
                        ("exec-out", "cat", driver.ADB_FILE_HIERARCHY_REMOTE_PATH),
                        hierarchy(f"observation-{index}"),
                    )
                    for index in range(
                        1,
                        driver.ADB_HIERARCHY_DUMP_RECONCILIATION_MAX_OBSERVATIONS
                        + 1,
                    )
                ],
                *[
                    completed(
                        driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS,
                        hierarchy(f"direct-observation-{index}"),
                    )
                    for index in range(
                        1,
                        driver.ADB_HIERARCHY_DUMP_DIRECT_RECONCILIATION_MAX_OBSERVATIONS
                        + 1,
                    )
                ],
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(driver.AdbTransportError) as original:
                    device.hierarchy(deadline=driver.time.monotonic() + 150)
                with self.assertRaises(driver.AdbTransportError) as blocked:
                    device.shell("input", "keyevent", "4")

            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(1, issued.count(dump_arguments))
            self.assertEqual(
                "timeout-unknown-outcome",
                original.exception.receipt["classification"],
            )
            self.assertEqual(
                "prior-mutation-outcome-unknown",
                blocked.exception.receipt["classification"],
            )
            self.assertEqual(2, device.transport_summary()["terminalFailureCount"])

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

    def test_only_dev_tty_hierarchy_observation_is_retryable(self) -> None:
        self.assertEqual(
            "read-only-retryable",
            driver.adb_command_retry_policy(
                driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS
            )[0],
        )
        for arguments in (
            ("shell", *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS),
            ("shell", *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS),
        ):
            with self.subTest(arguments=arguments):
                self.assertEqual(
                    "non-replayable",
                    driver.adb_command_retry_policy(arguments)[0],
                )

    def test_only_exact_bounded_creation_bootstrap_log_filter_is_retryable(self) -> None:
        self.assertEqual(
            "read-only-retryable",
            driver.adb_command_retry_policy(
                driver.ADB_CREATION_BOOTSTRAP_LOGCAT_ARGUMENTS
            )[0],
        )
        for arguments in (
            ("logcat", "-d", "-s", "ChummerBootstrap:I", "*:S"),
            ("logcat", "-d", "-t", "500", "-s", "ChummerBootstrap:I", "*:S"),
            ("logcat", "-d", "-t", "50", "-s", "*:I"),
        ):
            with self.subTest(arguments=arguments):
                self.assertEqual(
                    "non-replayable",
                    driver.adb_command_retry_policy(arguments)[0],
                )
        self.assertEqual(
            "non-replayable",
            driver.adb_command_retry_policy(
                driver.ADB_CREATION_BOOTSTRAP_LOGCAT_CLEAR_ARGUMENTS
            )[0],
        )

    def test_creation_dashboard_ready_snapshot_is_exactly_read_only_retryable(self) -> None:
        self.assertEqual(
            (
                "read-only-retryable",
                "bounded exact-tag creation-dashboard route-ready snapshot observation",
            ),
            driver.adb_command_retry_policy(
                driver.ADB_CREATION_DASHBOARD_READY_LOGCAT_ARGUMENTS
            ),
        )
        for arguments in (
            driver.ADB_CREATION_DASHBOARD_READY_LOGCAT_ARGUMENTS[:-2],
            ("logcat", "-d", "-s", "ChummerRoute:I", "*:S"),
            ("logcat", "-b", "main", "-v", "raw", "-T", "1"),
        ):
            with self.subTest(arguments=arguments):
                self.assertEqual("non-replayable", driver.adb_command_retry_policy(tuple(arguments))[0])

    def test_creation_dashboard_ready_snapshot_retries_transport_failure_only(self) -> None:
        arguments = driver.ADB_CREATION_DASHBOARD_READY_LOGCAT_ARGUMENTS
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            responses = [
                offline(arguments),
                completed(arguments, "must-not-be-read"),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                result = device.run(*arguments, timeout=20.0)

        self.assertEqual(2, run.call_count)
        self.assertEqual("must-not-be-read", result.stdout)

    def test_hierarchy_rejects_nonfinite_dump_bound_without_adb_command(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.subprocess, "run") as run,
                self.assertRaisesRegex(ValueError, "within"),
            ):
                device.hierarchy(dump_attempt_max_seconds=float("nan"))
        run.assert_not_called()

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
