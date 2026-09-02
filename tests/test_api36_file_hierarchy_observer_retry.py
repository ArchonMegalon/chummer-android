from __future__ import annotations

import hashlib
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
SPEC = importlib.util.spec_from_file_location("api36_file_hierarchy_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)

REMOVE = ("shell", *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS)
DUMP = ("shell", *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS)
STAT = ("shell", *driver.ADB_FILE_HIERARCHY_STAT_SHELL_ARGUMENTS)
CONTENT = ("exec-out", "cat", driver.ADB_FILE_HIERARCHY_REMOTE_PATH)


def complete(arguments: tuple[str, ...], stdout: str = "") -> subprocess.CompletedProcess:
    return subprocess.CompletedProcess(arguments, 0, stdout=stdout, stderr="")


def complete_bytes(
    arguments: tuple[str, ...],
    stdout: bytes,
) -> subprocess.CompletedProcess:
    return subprocess.CompletedProcess(arguments, 0, stdout=stdout, stderr=b"")


def strict_hierarchy(label: str = "Priorities") -> bytes:
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
    ).encode("utf-8")


def metadata(content: bytes, *, inode: int = 101) -> subprocess.CompletedProcess:
    return complete(STAT, f"1:{inode}:{len(content)}:1788336000:81a4\n")


def issued(run: mock.Mock) -> list[tuple[str, ...]]:
    return [tuple(call.args[0][3:]) for call in run.call_args_list]


class Api36FileHierarchyObserverRetryTests(unittest.TestCase):
    def make_device(self, evidence: Path) -> object:
        return driver.Device(Path("/trusted/adb"), "SERIAL-API36", evidence)

    def retry_receipt(self, evidence: Path) -> dict[str, object]:
        paths = sorted(evidence.glob("adb-file-hierarchy-retry-*.json"))
        self.assertEqual(1, len(paths))
        return json.loads(paths[0].read_text(encoding="utf-8"))

    def test_first_dump_reserves_a_fresh_retry_and_reconciliation(self) -> None:
        reconciliation = (
            3 * driver.ADB_FILE_HIERARCHY_IDENTITY_READ_ATTEMPT_MAX_SECONDS
            + driver.ADB_FILE_HIERARCHY_IDENTITY_HEADROOM_SECONDS
        )
        fresh_retry = driver._file_hierarchy_retry_headroom(
            dump_attempt_max_seconds=20.0,
        )
        with mock.patch.object(driver.time, "monotonic", return_value=0.0):
            timeout = driver._hierarchy_dump_attempt_timeout(
                deadline=(fresh_retry - 20.0)
                + driver.ADB_FILE_HIERARCHY_FRESHNESS_BARRIER_ATTEMPT_MAX_SECONDS
                + driver.ADB_READ_ONLY_DEADLINE_HEADROOM_SECONDS
                + 14.0,
                maximum=20.0,
            )
            later_timeout = driver._hierarchy_dump_attempt_timeout(
                deadline=(
                    reconciliation
                    + driver.ADB_FILE_HIERARCHY_FRESHNESS_BARRIER_ATTEMPT_MAX_SECONDS
                    + 7.0
                ),
                maximum=20.0,
                reserve_fresh_retry=False,
            )

        self.assertEqual(7.0, timeout)
        self.assertEqual(7.0, later_timeout)
        self.assertEqual(
            driver.ADB_FILE_HIERARCHY_FRESHNESS_BARRIER_ATTEMPT_MAX_SECONDS
            + driver.ADB_FILE_HIERARCHY_RETRY_DELAY_SECONDS
            + 20.0
            + reconciliation,
            fresh_retry,
        )

    def test_too_little_budget_fails_closed_before_freshness_or_dump(self) -> None:
        reserve = (
            2 * driver.ADB_FILE_HIERARCHY_FRESHNESS_BARRIER_ATTEMPT_MAX_SECONDS
            + driver.ADB_FILE_HIERARCHY_RETRY_DELAY_SECONDS
            + 3 * driver.ADB_FILE_HIERARCHY_IDENTITY_READ_ATTEMPT_MAX_SECONDS
            + driver.ADB_FILE_HIERARCHY_IDENTITY_HEADROOM_SECONDS
            + driver.ADB_READ_ONLY_DEADLINE_HEADROOM_SECONDS
        )
        with (
            mock.patch.object(driver.time, "monotonic", return_value=0.0),
            self.assertRaises(driver.AdbOperationDeadlineExceeded),
        ):
            driver._hierarchy_dump_attempt_timeout(
                deadline=reserve,
                maximum=20.0,
            )

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.time, "monotonic", return_value=0.0),
                mock.patch.object(driver.subprocess, "run") as run,
            ):
                self.assertEqual([], device.hierarchy(deadline=reserve))
            run.assert_not_called()

    def assert_failure_then_success(
        self,
        first_failure: BaseException,
        expected_classification: str,
    ) -> None:
        content = strict_hierarchy("Fresh after retry")
        responses = [
            complete(REMOVE),
            first_failure,
            complete(REMOVE),
            complete(DUMP, "UI hierarchy dumped"),
            metadata(content),
            complete_bytes(CONTENT, content),
            metadata(content),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                nodes = device.hierarchy(deadline=driver.time.monotonic() + 90)

            self.assertEqual("Fresh after retry", nodes[0].attributes["text"])
            self.assertEqual(
                [REMOVE, DUMP, REMOVE, DUMP, STAT, CONTENT, STAT],
                issued(run),
            )
            receipt = self.retry_receipt(evidence)
            self.assertEqual("pass", receipt["status"])
            self.assertEqual("retrying-read-only", receipt["attempts"][0]["status"])
            self.assertEqual(0, receipt["mutationCommandsRetried"])
            self.assertEqual(
                ["retrying-read-only", "pass"],
                [attempt["status"] for attempt in receipt["attempts"]],
            )
            self.assertEqual(
                expected_classification,
                receipt["attempts"][0]["classification"],
            )
            accepted = receipt["acceptedObservation"]
            self.assertEqual(accepted["metadataBefore"], accepted["metadataAfter"])
            self.assertEqual(len(content), accepted["contentBytes"])
            self.assertEqual(hashlib.sha256(content).hexdigest(), accepted["contentSha256"])
            self.assertEqual("hierarchy", accepted["root"])
            self.assertEqual("metadata-content-metadata-identity", accepted["reconciliation"])
            summary = device.transport_summary()
            self.assertEqual(0, summary["terminalFailureCount"])
            self.assertEqual(
                ["retrying-read-only", "recovered-read-only"],
                [event["status"] for event in summary["events"]],
            )
            self.assertEqual(list(DUMP), summary["events"][0]["adbArguments"])

    def test_timeout_then_success_uses_a_new_fenced_observer_invocation(self) -> None:
        self.assert_failure_then_success(
            subprocess.TimeoutExpired(DUMP, 20),
            "timeout-unknown-outcome",
        )

    def test_short_remaining_lease_slices_first_dump_to_keep_exact_retry(self) -> None:
        content = strict_hierarchy("Short lease retry")
        now = [0.0]
        dump_timeouts: list[float] = []
        dump_count = 0

        def monotonic() -> float:
            return now[0]

        def invoke(
            command: list[str],
            *,
            check: bool,
            capture_output: bool,
            text: bool,
            timeout: float,
        ) -> subprocess.CompletedProcess:
            nonlocal dump_count
            arguments = tuple(command[3:])
            if arguments == REMOVE:
                now[0] += timeout
                return complete(REMOVE)
            if arguments == DUMP:
                dump_count += 1
                dump_timeouts.append(timeout)
                now[0] += timeout
                if dump_count == 1:
                    raise subprocess.TimeoutExpired(command, timeout)
                return complete(DUMP, "UI hierarchy dumped")
            if arguments == STAT:
                return metadata(content)
            if arguments == CONTENT:
                return complete_bytes(CONTENT, content)
            raise AssertionError(f"unexpected ADB arguments: {arguments!r}")

        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke),
                mock.patch.object(driver.time, "monotonic", side_effect=monotonic),
                mock.patch.object(
                    driver.time,
                    "sleep",
                    side_effect=lambda seconds: now.__setitem__(0, now[0] + seconds),
                ),
            ):
                nodes = device.hierarchy(deadline=26.5)

            self.assertEqual("Short lease retry", nodes[0].attributes["text"])
            self.assertEqual(2, dump_count)
            self.assertEqual(7.25, dump_timeouts[0])
            retry_headroom = driver._file_hierarchy_retry_headroom(
                dump_attempt_max_seconds=dump_timeouts[0],
            )
            self.assertGreater(26.5 - (2.0 + dump_timeouts[0]) - retry_headroom, 0.49)
            self.assertLessEqual(now[0], 26.5)
            receipt = self.retry_receipt(evidence)
            self.assertEqual("pass", receipt["status"])
            self.assertEqual(
                "metadata-content-metadata-identity",
                receipt["acceptedObservation"]["reconciliation"],
            )

    def test_exit_137_then_success_uses_a_new_fenced_observer_invocation(self) -> None:
        self.assert_failure_then_success(
            subprocess.CalledProcessError(137, DUMP, output="", stderr=""),
            "observer-process-killed",
        )

    def test_repeated_observer_failure_is_bounded_and_fails_closed(self) -> None:
        failures = [
            subprocess.TimeoutExpired(DUMP, 20),
            subprocess.CalledProcessError(137, DUMP, output="", stderr=""),
            subprocess.TimeoutExpired(DUMP, 20),
        ]
        responses: list[object] = []
        for failure in failures:
            responses.extend((complete(REMOVE), failure))
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep"),
                self.assertRaises(driver.AdbTransportError) as raised,
            ):
                device.hierarchy(deadline=driver.time.monotonic() + 120)

            commands = issued(run)
            self.assertEqual(3, commands.count(DUMP))
            self.assertEqual(3, commands.count(REMOVE))
            self.assertFalse(
                any(command[:3] == ("shell", "input", "tap") for command in commands)
            )
            self.assertEqual("fail", raised.exception.receipt["status"])
            self.assertEqual(3, raised.exception.receipt["attempt"])
            receipt = self.retry_receipt(evidence)
            self.assertEqual("fail", receipt["status"])
            self.assertIsNone(receipt["acceptedObservation"])
            self.assertEqual(0, receipt["mutationCommandsRetried"])
            self.assertEqual(
                ["retrying-read-only", "retrying-read-only", "fail"],
                [attempt["status"] for attempt in receipt["attempts"]],
            )

    def test_stale_file_cannot_survive_a_failed_freshness_fence(self) -> None:
        remove_failure = subprocess.CalledProcessError(
            1,
            REMOVE,
            output="",
            stderr="owned hierarchy path could not be removed",
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=[remove_failure]) as run,
                self.assertRaises(driver.AdbTransportError),
            ):
                device.hierarchy()

            self.assertEqual([REMOVE], issued(run))
            self.assertNotIn(DUMP, issued(run))

    def test_changing_file_identity_and_valid_xml_are_not_accepted(self) -> None:
        content = strict_hierarchy("Identity changed")
        responses = [
            complete(REMOVE),
            subprocess.TimeoutExpired(DUMP, 20),
            complete(REMOVE),
            complete(DUMP, "UI hierarchy dumped"),
            metadata(content, inode=101),
            complete_bytes(CONTENT, content),
            metadata(content, inode=202),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses),
                mock.patch.object(driver.time, "sleep"),
            ):
                self.assertEqual([], device.hierarchy())

            receipt = self.retry_receipt(evidence)
            self.assertEqual("fail", receipt["status"])
            observation = receipt["acceptedObservation"]
            self.assertEqual("fail", observation["status"])
            self.assertEqual("hierarchy", observation["root"])
            self.assertNotEqual(
                observation["metadataBefore"],
                observation["metadataAfter"],
            )

    def test_retried_file_requires_one_complete_hierarchy_root(self) -> None:
        content = b"<not-hierarchy><node /></not-hierarchy>"
        responses = [
            complete(REMOVE),
            subprocess.TimeoutExpired(DUMP, 20),
            complete(REMOVE),
            complete(DUMP, "UI hierarchy dumped"),
            metadata(content),
            complete_bytes(CONTENT, content),
            metadata(content),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses),
                mock.patch.object(driver.time, "sleep"),
            ):
                self.assertEqual([], device.hierarchy())

            receipt = self.retry_receipt(evidence)
            self.assertEqual("fail", receipt["status"])
            self.assertIsNone(receipt["acceptedObservation"]["root"])
            self.assertEqual(0, receipt["acceptedObservation"]["nodeCount"])

    def test_only_the_exact_dump_argv_and_output_path_are_authorized(self) -> None:
        timeout = subprocess.TimeoutExpired(DUMP, 20)
        killed = subprocess.CalledProcessError(137, DUMP)
        self.assertEqual("read-only-retryable", driver.adb_command_retry_policy(DUMP)[0])
        self.assertEqual(
            ("timeout-unknown-outcome", True),
            driver.classify_file_hierarchy_dump_failure(DUMP, timeout),
        )
        self.assertEqual(
            ("observer-process-killed", True),
            driver.classify_file_hierarchy_dump_failure(DUMP, killed),
        )
        deviations = (
            DUMP + ("extra",),
            ("shell", "uiautomator", "dump", driver.ADB_FILE_HIERARCHY_REMOTE_PATH),
            ("shell", "uiautomator", "dump", "--compressed", "/sdcard/other.xml"),
            (
                "shell",
                "sh",
                "-c",
                "uiautomator dump --compressed /sdcard/chummer-editing-window.xml",
            ),
        )
        for arguments in deviations:
            with self.subTest(arguments=arguments):
                self.assertEqual(
                    "non-replayable",
                    driver.adb_command_retry_policy(arguments)[0],
                )
                self.assertEqual(
                    ("unclassified-adb-failure", False),
                    driver.classify_file_hierarchy_dump_failure(arguments, timeout),
                )
        for mutation in (
            ("shell", "input", "tap", "100", "200"),
            ("shell", "input", "swipe", "100", "200", "100", "50", "300"),
            ("shell", "am", "start", "-W", driver.PACKAGE),
            ("shell", "am", "force-stop", driver.PACKAGE),
        ):
            with self.subTest(mutation=mutation):
                self.assertEqual(
                    "non-replayable",
                    driver.adb_command_retry_policy(mutation)[0],
                )

    def test_generic_runner_cannot_bypass_the_freshness_fence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.subprocess, "run") as run,
                self.assertRaisesRegex(ValueError, "require Device.hierarchy"),
            ):
                device.run(*DUMP)
            run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
