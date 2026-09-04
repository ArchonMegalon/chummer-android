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


def download_missing(
    root_identity: str = "/sdcard:43:106499:45f8\n",
) -> subprocess.CalledProcessError:
    return subprocess.CalledProcessError(
        1,
        (
            "adb",
            "-s",
            "SERIAL-API36",
            *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
        ),
        output=root_identity,
        stderr="stat: '/sdcard/Download': No such file or directory\n",
    )


def shared_storage_unavailable() -> subprocess.CalledProcessError:
    return subprocess.CalledProcessError(
        1,
        (
            "adb",
            "-s",
            "SERIAL-API36",
            *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
        ),
        output="",
        stderr="stat: '/sdcard': Transport endpoint is not connected\n",
    )


def shared_storage_roots_not_mounted(
    *,
    command: tuple[str, ...] | None = None,
    stdout: str = "",
    stderr: str = (
        "stat: '/sdcard': No such file or directory\n"
        "stat: '/sdcard/Download': No such file or directory\n"
    ),
) -> subprocess.CalledProcessError:
    return subprocess.CalledProcessError(
        1,
        command
        or (
            "/trusted/adb",
            "-s",
            "SERIAL-API36",
            *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
        ),
        output=stdout,
        stderr=stderr,
    )


def hosted_storage_authority() -> dict[str, object]:
    return {
        "hosted_api_level": "36",
        "hosted_abi": "x86_64",
        "hosted_emulator": "1",
        "hosted_proof_attempt": True,
    }


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

    def test_shared_storage_preflight_waits_for_three_stable_read_only_identities(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            unavailable = subprocess.CalledProcessError(
                1,
                (
                    "adb",
                    "-s",
                    "SERIAL-API36",
                    *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                ),
                output="",
                stderr="stat: '/sdcard': Transport endpoint is not connected\n",
            )
            identity = (
                "/sdcard:42:100:41ed\n"
                "/sdcard/Download:42:101:41ed\n"
            )
            responses = [
                unavailable,
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
            ]
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=responses) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                )

            self.assertEqual(4, run.call_count)
            self.assertEqual(3, sleep.call_count)
            self.assertEqual("pass", receipt["status"])
            self.assertEqual(4, receipt["observationsPerformed"])
            self.assertEqual(3, receipt["consecutiveStableObservations"])
            self.assertEqual(4, receipt["readOnlyCommandsIssued"])
            self.assertEqual(0, receipt["mutationCommandsIssued"])
            self.assertEqual(
                0,
                receipt["environmentInitializationMutationCommandsIssued"],
            )
            self.assertEqual("not-required", receipt["environmentInitialization"]["status"])
            self.assertFalse(
                (
                    evidence
                    / driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_FILENAME
                ).exists()
            )
            self.assertTrue(receipt["observationRetryOnly"])
            self.assertFalse(receipt["adbReconnectAttempted"])
            self.assertFalse(receipt["applicationRelaunchAttempted"])
            self.assertEqual(
                "shared-storage-unavailable",
                receipt["observations"][0]["classification"],
            )
            self.assertEqual(
                ["storage-unavailable", "stable", "stable", "stable"],
                [entry["status"] for entry in receipt["observations"]],
            )
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(
                [driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS] * 4,
                issued,
            )
            self.assertTrue(
                all(
                    0 < call.kwargs["timeout"]
                    <= driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS
                    for call in run.call_args_list
                )
            )
            self.assertTrue(receipt["followSymlinks"])
            self.assertIn("-L", receipt["statArguments"])
            self.assertTrue(receipt["callerDeadlineEnforced"])
            self.assertEqual(
                driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                receipt["maximumDurationSeconds"],
            )
            self.assertFalse(any("reconnect" in arguments for arguments in issued))
            stored = json.loads(
                (evidence / "adb-shared-storage-readiness.json").read_text(
                    encoding="utf-8"
                )
            )
            self.assertEqual(receipt, stored)
            self.assertEqual(
                0o600,
                (evidence / "adb-shared-storage-readiness.json").stat().st_mode
                & 0o777,
            )
            self.assertEqual(
                receipt,
                device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                ),
            )
            self.assertEqual(4, run.call_count)

    def test_shared_storage_preflight_persistent_failure_blocks_without_mutation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            unavailable = subprocess.CalledProcessError(
                1,
                ("adb", "-s", "SERIAL-API36", *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS),
                output="",
                stderr=(
                    "stat: /sdcard/Download: Transport endpoint is not connected\n"
                ),
            )
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[unavailable]
                    * driver.ADB_SHARED_STORAGE_MAX_OBSERVATIONS,
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                    )
                with self.assertRaises(driver.AdbSharedStoragePreflightError):
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                    )

            self.assertEqual(driver.ADB_SHARED_STORAGE_MAX_OBSERVATIONS, run.call_count)
            self.assertEqual(
                driver.ADB_SHARED_STORAGE_MAX_OBSERVATIONS - 1,
                sleep.call_count,
            )
            receipt = raised.exception.receipt
            self.assertEqual("fail", receipt["status"])
            self.assertEqual(0, receipt["mutationCommandsIssued"])
            self.assertEqual(
                driver.ADB_SHARED_STORAGE_MAX_OBSERVATIONS,
                receipt["observationsPerformed"],
            )
            self.assertTrue(
                all(
                    entry["classification"] == "shared-storage-unavailable"
                    for entry in receipt["observations"]
                )
            )
            summary = device.transport_summary()
            self.assertEqual("fail", summary["status"])
            self.assertEqual(receipt, summary["sharedStorageReadiness"])

    def test_shared_storage_preflight_policy_cannot_be_reduced_to_two_of_two(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with mock.patch.object(driver.subprocess, "run") as run:
                with self.assertRaises(TypeError):
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        required_consecutive=2,
                        max_observations=2,
                    )
            run.assert_not_called()

    def test_shared_storage_preflight_rejects_expired_caller_deadline(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with mock.patch.object(driver.subprocess, "run") as run:
                with self.assertRaises(driver.AdbOperationDeadlineExceeded):
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic() - 1.0
                    )
            run.assert_not_called()

    def test_shared_storage_retry_marker_is_fully_anchored_to_requested_paths(self) -> None:
        command = (
            "adb",
            "-s",
            "SERIAL-API36",
            *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
        )
        accepted = (
            "stat: /sdcard: Transport endpoint is not connected\n",
            "stat: '/sdcard/Download': Transport endpoint is not connected\n",
            (
                "stat: \"/sdcard\": Transport endpoint is not connected\n"
                "stat: /sdcard/Download: Transport endpoint is not connected\n"
            ),
        )
        for stderr in accepted:
            with self.subTest(stderr=stderr):
                error = subprocess.CalledProcessError(
                    1,
                    command,
                    output="",
                    stderr=stderr,
                )
                self.assertEqual(
                    ("shared-storage-unavailable", True),
                    driver.classify_shared_storage_readiness_failure(error),
                )

        rejected = (
            "Transport endpoint is not connected\n",
            "stat: /sdcard/Other: Transport endpoint is not connected\n",
            "stat: /sdcard: Transport endpoint is not connected; ignored\n",
            "prefix stat: /sdcard: Transport endpoint is not connected\n",
            (
                "stat: /sdcard: Transport endpoint is not connected\n"
                "unexpected diagnostic\n"
            ),
            (
                "stat: /sdcard: Transport endpoint is not connected\n"
                "stat: /sdcard: Transport endpoint is not connected\n"
            ),
        )
        for stderr in rejected:
            with self.subTest(stderr=stderr):
                error = subprocess.CalledProcessError(
                    1,
                    command,
                    output="",
                    stderr=stderr,
                )
                classification, retryable = (
                    driver.classify_shared_storage_readiness_failure(error)
                )
                self.assertNotEqual("shared-storage-unavailable", classification)
                self.assertFalse(retryable)

        wrong_command = subprocess.CalledProcessError(
            1,
            ("adb", "-s", "SERIAL-API36", "shell", "rm", "/sdcard"),
            output="",
            stderr="stat: /sdcard: Transport endpoint is not connected\n",
        )
        self.assertFalse(
            driver.classify_shared_storage_readiness_failure(wrong_command)[1]
        )

    def test_exact_preintent_two_root_enoent_gets_one_fresh_observation(
        self,
    ) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw_download = "/sdcard/Download:43:106500:41ed\n"
        stable = root + raw_download
        responses = [
            shared_storage_roots_not_mounted(),
            download_missing(root),
            download_missing(root),
            download_missing(root),
            completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
            completed(
                driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS,
                raw_download,
            ),
            completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
            completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
            completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
            completed(
                driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS,
                raw_download,
            ),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=responses,
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                    **hosted_storage_authority(),
                )

        self.assertEqual("pass", receipt["status"])
        self.assertEqual(10, run.call_count)
        self.assertEqual(6, sleep.call_count)
        sleep.assert_any_call(driver.ADB_SHARED_STORAGE_OBSERVATION_DELAY_SECONDS)
        self.assertEqual(7, receipt["observationsPerformed"])
        self.assertEqual(3, receipt["consecutiveStableObservations"])
        self.assertEqual(1, receipt["mutationCommandsIssued"])
        self.assertEqual(
            [
                "pre-intent-shared-storage-roots-not-mounted",
                "storage-not-initialized",
                "storage-not-initialized",
                "storage-not-initialized",
                "stable",
                "stable",
                "stable",
            ],
            [entry["status"] for entry in receipt["observations"]],
        )
        first = receipt["observations"][0]
        self.assertEqual("shared-storage-roots-not-mounted", first["classification"])
        self.assertEqual(
            "exact-pre-intent-one-fresh-root-observation",
            first["classificationAuthority"],
        )
        self.assertTrue(first["retryableReadOnlyObservation"])
        self.assertTrue(first["freshObservationScheduled"])
        self.assertNotIn("roots", first)
        recovery = receipt["preIntentSharedStorageRootEnoentRecovery"]
        self.assertEqual(1, recovery["maximumRetries"])
        self.assertEqual(1, recovery["retriesPerformed"])
        self.assertTrue(recovery["freshReadOnlyInvocationRequired"])
        self.assertFalse(recovery["mutationCommandReplayAuthorized"])
        self.assertFalse(recovery["observationBoundWidened"])
        self.assertFalse(recovery["deadlineWidened"])
        self.assertEqual(7, driver.ADB_SHARED_STORAGE_MAX_OBSERVATIONS)
        self.assertEqual(30.0, driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS)

    def test_preintent_two_root_enoent_recovery_allows_only_one_retry(self) -> None:
        stable = "/sdcard:42:100:41ed\n/sdcard/Download:42:101:41ed\n"
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        shared_storage_roots_not_mounted(),
                        shared_storage_roots_not_mounted(),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )

        self.assertEqual(2, run.call_count)
        self.assertEqual(1, sleep.call_count)
        first, second = raised.exception.receipt["observations"]
        self.assertTrue(first["retryableReadOnlyObservation"])
        self.assertTrue(first["freshObservationScheduled"])
        self.assertFalse(second["retryableReadOnlyObservation"])
        self.assertFalse(second["freshObservationScheduled"])
        self.assertEqual(0, raised.exception.receipt["mutationCommandsIssued"])
        self.assertEqual(
            1,
            raised.exception.receipt[
                "preIntentSharedStorageRootEnoentRecovery"
            ]["retriesPerformed"],
        )

    def test_two_root_enoent_recovery_rejects_nonexact_output_and_argv(self) -> None:
        exact_stderr = (
            "stat: '/sdcard': No such file or directory\n"
            "stat: '/sdcard/Download': No such file or directory\n"
        )
        cases = (
            shared_storage_roots_not_mounted(
                stderr=(
                    "stat: '/sdcard/Download': No such file or directory\n"
                    "stat: '/sdcard': No such file or directory\n"
                )
            ),
            shared_storage_roots_not_mounted(stderr=exact_stderr + "extra\n"),
            shared_storage_roots_not_mounted(stdout="unexpected\n"),
            shared_storage_roots_not_mounted(
                command=(
                    "/trusted/adb",
                    "-s",
                    "WRONG-SERIAL",
                    *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                )
            ),
            shared_storage_roots_not_mounted(
                command=(
                    "/trusted/adb",
                    "-s",
                    "SERIAL-API36",
                    "shell",
                    "stat",
                    "-c",
                    driver.ADB_SHARED_STORAGE_STAT_FORMAT,
                    *driver.ADB_SHARED_STORAGE_ROOTS,
                )
            ),
        )
        for failure in cases:
            with (
                self.subTest(command=failure.cmd, output=failure.output),
                tempfile.TemporaryDirectory() as temporary,
            ):
                device = self.make_device(Path(temporary))
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=failure,
                    ) as run,
                    mock.patch.object(driver.time, "sleep") as sleep,
                ):
                    with self.assertRaises(
                        driver.AdbSharedStoragePreflightError
                    ) as raised:
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                            **hosted_storage_authority(),
                        )
                self.assertEqual(1, run.call_count)
                sleep.assert_not_called()
                self.assertFalse(
                    raised.exception.receipt["observations"][0][
                        "retryableReadOnlyObservation"
                    ]
                )
                self.assertEqual(
                    0,
                    raised.exception.receipt[
                        "preIntentSharedStorageRootEnoentRecovery"
                    ]["retriesPerformed"],
                )

    def test_two_root_enoent_recovery_requires_lease_and_observation_slots(
        self,
    ) -> None:
        cases = (
            (
                [shared_storage_roots_not_mounted()],
                driver.ADB_SHARED_STORAGE_PREINTENT_ROOT_ENOENT_RETRY_MINIMUM_LEASE_SECONDS
                - 0.5,
                1,
                0,
            ),
            (
                [
                    shared_storage_unavailable(),
                    shared_storage_unavailable(),
                    shared_storage_unavailable(),
                    shared_storage_unavailable(),
                    shared_storage_roots_not_mounted(),
                ],
                driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                5,
                4,
            ),
        )
        for responses, lease, expected_calls, expected_sleeps in cases:
            with (
                self.subTest(responses=len(responses), lease=lease),
                tempfile.TemporaryDirectory() as temporary,
            ):
                device = self.make_device(Path(temporary))
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=responses,
                    ) as run,
                    mock.patch.object(driver.time, "sleep") as sleep,
                ):
                    with self.assertRaises(
                        driver.AdbSharedStoragePreflightError
                    ) as raised:
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic() + lease,
                            **hosted_storage_authority(),
                        )
                self.assertEqual(expected_calls, run.call_count)
                self.assertEqual(expected_sleeps, sleep.call_count)
                self.assertEqual(0, raised.exception.receipt["mutationCommandsIssued"])
                self.assertFalse(
                    raised.exception.receipt["observations"][-1][
                        "retryableReadOnlyObservation"
                    ]
                )
                self.assertEqual(
                    0,
                    raised.exception.receipt[
                        "preIntentSharedStorageRootEnoentRecovery"
                    ]["retriesPerformed"],
                )

    def test_two_root_enoent_recovery_requires_exact_hosted_authority(self) -> None:
        authorities = (
            {},
            {**hosted_storage_authority(), "hosted_api_level": "35"},
            {**hosted_storage_authority(), "hosted_abi": "arm64-v8a"},
            {**hosted_storage_authority(), "hosted_emulator": "0"},
            {**hosted_storage_authority(), "hosted_proof_attempt": False},
        )
        for authority in authorities:
            with (
                self.subTest(authority=authority),
                tempfile.TemporaryDirectory() as temporary,
            ):
                device = self.make_device(Path(temporary))
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=shared_storage_roots_not_mounted(),
                    ) as run,
                    mock.patch.object(driver.time, "sleep") as sleep,
                ):
                    with self.assertRaises(
                        driver.AdbSharedStoragePreflightError
                    ) as raised:
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                            **authority,
                        )
                self.assertEqual(1, run.call_count)
                sleep.assert_not_called()
                observation = raised.exception.receipt["observations"][0]
                self.assertFalse(observation["retryableReadOnlyObservation"])
                self.assertFalse(observation["freshObservationScheduled"])
                self.assertEqual(0, raised.exception.receipt["mutationCommandsIssued"])

    def test_two_root_enoent_after_initialization_intent_is_terminal(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw_download = "/sdcard/Download:43:106500:41ed\n"
        responses = [
            download_missing(root),
            download_missing(root),
            download_missing(root),
            completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
            completed(
                driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS,
                raw_download,
            ),
            shared_storage_roots_not_mounted(),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=responses,
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )

            self.assertTrue(
                (
                    evidence
                    / driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_FILENAME
                ).is_file()
            )

        self.assertEqual(6, run.call_count)
        self.assertEqual(3, sleep.call_count)
        receipt = raised.exception.receipt
        self.assertEqual(1, receipt["mutationCommandsIssued"])
        terminal = receipt["terminalFailure"]
        self.assertEqual(
            "shared-storage-post-initialization-observation-failed",
            terminal["classification"],
        )
        self.assertEqual(
            "shared-storage-roots-not-mounted",
            terminal["underlyingClassification"],
        )
        self.assertFalse(terminal["retryableReadOnlyObservation"])
        self.assertEqual(
            0,
            receipt["preIntentSharedStorageRootEnoentRecovery"][
                "retriesPerformed"
            ],
        )

    def test_missing_download_directory_retries_only_from_exact_partial_stat_observation(
        self,
    ) -> None:
        captured_failure = subprocess.CalledProcessError(
            1,
            (
                "adb",
                "-s",
                "SERIAL-API36",
                *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            ),
            output="/sdcard:43:106499:45f8\n",
            stderr="stat: '/sdcard/Download': No such file or directory\n",
        )
        stable = "/sdcard:43:106499:45f8\n/sdcard/Download:43:106500:41ed\n"
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        captured_failure,
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                )

        self.assertEqual(4, run.call_count)
        self.assertEqual(3, sleep.call_count)
        self.assertEqual("pass", receipt["status"])
        first = receipt["observations"][0]
        self.assertEqual("storage-not-initialized", first["status"])
        self.assertEqual(
            "shared-storage-download-not-initialized",
            first["classification"],
        )
        self.assertEqual(
            "recognized-transient-shared-storage-marker",
            first["classificationAuthority"],
        )
        self.assertTrue(first["retryableReadOnlyObservation"])
        self.assertEqual(0, receipt["mutationCommandsIssued"])
        self.assertEqual(0, receipt["environmentInitializationMutationCommandsIssued"])
        self.assertEqual("not-required", receipt["environmentInitialization"]["status"])

    def test_exact_persistent_missing_download_is_initialized_once_then_reproved(
        self,
    ) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw_download = "/sdcard/Download:43:106500:41ed\n"
        stable = root + raw_download
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            intent_path = (
                evidence / driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_FILENAME
            )
            responses = iter(
                [
                    download_missing(root),
                    download_missing(root),
                    download_missing(root),
                    completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
                    completed(
                        driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS,
                        raw_download,
                    ),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(
                        driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS,
                        raw_download,
                    ),
                ]
            )

            def invoke(command: list[str], **_kwargs: object) -> object:
                arguments = tuple(command[3:])
                if arguments == driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS:
                    self.assertTrue(intent_path.is_file())
                    intent = json.loads(intent_path.read_text(encoding="utf-8"))
                    self.assertEqual(
                        driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_SCHEMA,
                        intent["schema"],
                    )
                response = next(responses)
                if isinstance(response, BaseException):
                    raise response
                return response

            device = self.make_device(evidence)
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                    **hosted_storage_authority(),
                )

            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(9, run.call_count)
            self.assertEqual(5, sleep.call_count)
            self.assertEqual(
                1,
                issued.count(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS),
            )
            self.assertEqual(
                2,
                issued.count(
                    driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS
                ),
            )
            self.assertNotIn(
                ("shell", "mkdir", "-p", "/sdcard/Download"),
                issued,
            )
            self.assertEqual("pass", receipt["status"])
            self.assertEqual(driver.ADB_SHARED_STORAGE_PREFLIGHT_SCHEMA, receipt["schema"])
            self.assertEqual(1, receipt["mutationCommandsIssued"])
            self.assertEqual(
                1,
                receipt["environmentInitializationMutationCommandsIssued"],
            )
            self.assertEqual(0, receipt["applicationMutationCommandsIssued"])
            self.assertFalse(receipt["publicationAuthorized"])
            self.assertIsNone(device._mutation_blocker)
            initialization = receipt["environmentInitialization"]
            self.assertEqual("verified", initialization["status"])
            self.assertEqual(3, initialization["precondition"]["consecutiveIdenticalObservations"])
            self.assertEqual(1, initialization["attempts"])
            self.assertFalse(initialization["replayAttempted"])
            self.assertFalse(initialization["publicationAuthorized"])
            intent = json.loads(intent_path.read_text(encoding="utf-8"))
            outcome = json.loads(
                (
                    evidence
                    / driver.ADB_SHARED_STORAGE_INITIALIZATION_OUTCOME_FILENAME
                ).read_text(encoding="utf-8")
            )
            bootstrap = json.loads(
                (evidence / driver.ADB_SHARED_STORAGE_BOOTSTRAP_FILENAME).read_text(
                    encoding="utf-8"
                )
            )
            self.assertEqual(
                driver.ADB_SHARED_STORAGE_INITIALIZATION_OUTCOME_SCHEMA,
                outcome["schema"],
            )
            self.assertEqual(
                driver.ADB_SHARED_STORAGE_BOOTSTRAP_SCHEMA,
                bootstrap["schema"],
            )
            self.assertEqual("pass-exact-rc0-silent", outcome["status"])
            self.assertEqual("pass", bootstrap["status"])
            outcome_path = (
                evidence / driver.ADB_SHARED_STORAGE_INITIALIZATION_OUTCOME_FILENAME
            )
            self.assertEqual(
                driver._canonical_json_receipt_bytes(outcome),
                outcome_path.read_bytes(),
            )
            self.assertEqual(
                driver.hashlib.sha256(intent_path.read_bytes()).hexdigest(),
                outcome["intentSha256"],
            )
            self.assertEqual(outcome["intentSha256"], bootstrap["intentSha256"])
            outcome_sha256 = driver.hashlib.sha256(
                outcome_path.read_bytes()
            ).hexdigest()
            self.assertEqual(outcome_sha256, bootstrap["outcomeSha256"])
            bootstrap_path = evidence / driver.ADB_SHARED_STORAGE_BOOTSTRAP_FILENAME
            bootstrap_sha256 = driver.hashlib.sha256(
                bootstrap_path.read_bytes()
            ).hexdigest()
            self.assertEqual(
                bootstrap_sha256,
                receipt["environmentInitializationBootstrapSha256"],
            )
            self.assertEqual(
                bootstrap_sha256,
                receipt["environmentInitialization"]["bootstrapSha256"],
            )
            self.assertEqual(
                outcome_sha256,
                receipt["environmentInitializationOutcomeSha256"],
            )
            self.assertEqual(
                outcome_sha256,
                receipt["environmentInitialization"]["outcomeSha256"],
            )
            self.assertFalse(intent["applicationMutation"])
            self.assertFalse(outcome["applicationMutation"])
            self.assertFalse(bootstrap["publicationAuthorized"])

    def test_missing_download_directory_retry_rejects_every_non_exact_variant(
        self,
    ) -> None:
        command = (
            "adb",
            "-s",
            "SERIAL-API36",
            *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
        )
        hostile_outputs = (
            ("", "stat: '/sdcard/Download': No such file or directory\n"),
            (
                "/sdcard:43:106499:45f8\nextra\n",
                "stat: '/sdcard/Download': No such file or directory\n",
            ),
            (
                "/sdcard/Download:43:106499:45f8\n",
                "stat: '/sdcard/Download': No such file or directory\n",
            ),
            (
                "/sdcard:43:106499:a1ff\n",
                "stat: '/sdcard/Download': No such file or directory\n",
            ),
            (
                "/sdcard:43:106499:45f8\n",
                "stat: '/sdcard': No such file or directory\n",
            ),
            (
                "/sdcard:43:106499:45f8\n",
                "stat: '/sdcard/Other': No such file or directory\n",
            ),
            (
                "/sdcard:43:106499:45f8\n",
                "stat: /sdcard/Download: No such file or directory\n",
            ),
            (
                "/sdcard:43:106499:45f8\n",
                "stat: '/sdcard/Download': No such file or directory\nextra\n",
            ),
        )
        for stdout, stderr in hostile_outputs:
            with self.subTest(stdout=stdout, stderr=stderr):
                failure = subprocess.CalledProcessError(
                    1,
                    command,
                    output=stdout,
                    stderr=stderr,
                )
                classification, retryable = (
                    driver.classify_shared_storage_readiness_failure(failure)
                )
                self.assertNotEqual(
                    "shared-storage-download-not-initialized",
                    classification,
                )
                self.assertFalse(retryable)

    def test_download_initialization_requires_three_identical_roots_and_post_slots(
        self,
    ) -> None:
        first = "/sdcard:43:100:45f8\n"
        second = "/sdcard:84:200:45f8\n"
        for failures in (
            [
                download_missing(first),
                download_missing(first),
                offline(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS),
            ],
            [
                download_missing(first),
                download_missing(second),
                download_missing(first),
                download_missing(second),
                download_missing(first),
                download_missing(second),
                download_missing(first),
            ],
            [
                shared_storage_unavailable(),
                shared_storage_unavailable(),
                shared_storage_unavailable(),
                shared_storage_unavailable(),
                download_missing(first),
                download_missing(first),
                download_missing(first),
            ],
        ):
            with self.subTest(observations=len(failures)), tempfile.TemporaryDirectory() as temporary:
                device = self.make_device(Path(temporary))
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=failures,
                    ) as run,
                    mock.patch.object(driver.time, "sleep"),
                ):
                    with self.assertRaises(driver.AdbSharedStoragePreflightError):
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                            **hosted_storage_authority(),
                        )
                issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
                self.assertNotIn(
                    driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
                    issued,
                )

    def test_download_initialization_requires_hosted_authority_and_deadline_lease(
        self,
    ) -> None:
        missing = [download_missing()] * 3
        cases = (
            ({}, driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS),
            (
                hosted_storage_authority(),
                driver.ADB_SHARED_STORAGE_INITIALIZATION_MINIMUM_LEASE_SECONDS
                - 1.0,
            ),
        )
        for authority, lease in cases:
            with self.subTest(authority=authority, lease=lease), tempfile.TemporaryDirectory() as temporary:
                device = self.make_device(Path(temporary))
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=missing,
                    ) as run,
                    mock.patch.object(driver.time, "sleep"),
                ):
                    with self.assertRaises(driver.AdbSharedStoragePreflightError):
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic() + lease,
                            **authority,
                        )
                issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
                self.assertNotIn(
                    driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
                    issued,
                )
        self.assertEqual(
            20.0,
            driver.ADB_SHARED_STORAGE_INITIALIZATION_MINIMUM_LEASE_SECONDS,
        )

    def test_download_bootstrap_exact_budget_fits_unchanged_thirty_second_cap(
        self,
    ) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw = "/sdcard/Download:43:106500:41ed\n"
        stable = root + raw
        responses = iter(
            [
                download_missing(root),
                download_missing(root),
                download_missing(root),
                completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
                completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
            ]
        )
        clock = [0.0]
        observed_timeouts: list[float] = []

        def invoke(_command: list[str], **kwargs: object) -> object:
            invocation_index = len(observed_timeouts)
            timeout = float(kwargs["timeout"])
            observed_timeouts.append(timeout)
            clock[0] += 2.0 if invocation_index < 3 else timeout
            response = next(responses)
            if isinstance(response, BaseException):
                raise response
            return response

        def sleep(seconds: float) -> None:
            clock[0] += seconds

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.time, "monotonic", side_effect=lambda: clock[0]),
                mock.patch.object(driver.time, "sleep", side_effect=sleep) as sleep_mock,
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                    **hosted_storage_authority(),
                )

        self.assertEqual("pass", receipt["status"])
        self.assertEqual(9, run.call_count)
        self.assertEqual(5, sleep_mock.call_count)
        self.assertEqual([5.0] * 3, observed_timeouts[:3])
        self.assertTrue(
            all(
                driver.ADB_SHARED_STORAGE_BOOTSTRAP_ATTEMPT_MINIMUM_SECONDS
                <= timeout
                <= driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS
                for timeout in observed_timeouts[3:]
            )
        )
        self.assertAlmostEqual(28.0, clock[0])
        self.assertEqual(
            driver.ADB_SHARED_STORAGE_BOOTSTRAP_ATTEMPT_MINIMUM_SECONDS,
            receipt["bootstrapAttemptMinimumSeconds"],
        )
        self.assertEqual(
            driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
            receipt["bootstrapAttemptMaximumSeconds"],
        )
        self.assertEqual(
            driver.ADB_SHARED_STORAGE_REQUIRED_CONSECUTIVE + 3,
            receipt["bootstrapDeviceCalls"],
        )
        self.assertEqual(
            driver.ADB_SHARED_STORAGE_REQUIRED_CONSECUTIVE,
            receipt["bootstrapObservationDelays"],
        )
        self.assertEqual(
            driver.ADB_SHARED_STORAGE_BOOTSTRAP_RECEIPT_HEADROOM_SECONDS,
            receipt["bootstrapReceiptHeadroomSeconds"],
        )
        allocations = receipt["environmentInitialization"][
            "attemptTimeoutAllocations"
        ]
        self.assertEqual(
            [6, 5, 4, 3, 2, 1],
            [entry["remainingDeviceCallsBefore"] for entry in allocations],
        )
        self.assertEqual(
            [3, 3, 2, 1, 0, 0],
            [entry["remainingObservationDelaysBefore"] for entry in allocations],
        )
        self.assertEqual(
            [
                "mkdir",
                "initial-raw-download-observation",
                "followed-root-observation",
                "followed-root-observation",
                "followed-root-observation",
                "final-raw-download-observation",
            ],
            [entry["commandKind"] for entry in allocations],
        )

    def test_download_bootstrap_at_or_below_minimum_budget_never_mutates(
        self,
    ) -> None:
        root = "/sdcard:43:106499:45f8\n"
        for remaining_lease in (20.0, 19.999):
            with self.subTest(remaining_lease=remaining_lease):
                clock = [0.0]
                issued: list[tuple[str, ...]] = []

                def invoke(command: list[str], **_kwargs: object) -> object:
                    arguments = tuple(command[3:])
                    issued.append(arguments)
                    clock[0] += 2.0
                    raise download_missing(root)

                def sleep(seconds: float) -> None:
                    clock[0] += seconds

                exact_deadline = 8.0 + remaining_lease
                with tempfile.TemporaryDirectory() as temporary:
                    device = self.make_device(Path(temporary))
                    with (
                        mock.patch.object(
                            driver.time,
                            "monotonic",
                            side_effect=lambda: clock[0],
                        ),
                        mock.patch.object(driver.time, "sleep", side_effect=sleep),
                        mock.patch.object(
                            driver.subprocess,
                            "run",
                            side_effect=invoke,
                        ) as run,
                    ):
                        with self.assertRaises(
                            driver.AdbSharedStoragePreflightError
                        ) as raised:
                            device.require_shared_storage_readiness(
                                deadline=exact_deadline,
                                **hosted_storage_authority(),
                            )

                self.assertEqual(3, run.call_count)
                self.assertNotIn(
                    driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
                    issued,
                )
                self.assertEqual(
                    "shared-storage-bootstrap-deadline-lease-missing",
                    raised.exception.receipt["terminalFailure"]["classification"],
                )
                self.assertEqual(0, raised.exception.receipt["mutationCommandsIssued"])

    def test_intent_write_time_can_exhaust_lease_before_mkdir(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        clock = [0.0]
        issued: list[tuple[str, ...]] = []
        original_write = driver._write_durable_new_json_receipt

        def invoke(command: list[str], **_kwargs: object) -> object:
            arguments = tuple(command[3:])
            issued.append(arguments)
            raise download_missing(root)

        def sleep(seconds: float) -> None:
            clock[0] += seconds

        def delayed_intent_write(path: Path, receipt: dict[str, object]) -> str:
            digest = original_write(path, receipt)
            if path.name == driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_FILENAME:
                clock[0] += 10.0
            return digest

        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(driver.time, "monotonic", side_effect=lambda: clock[0]),
                mock.patch.object(driver.time, "sleep", side_effect=sleep),
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(
                    driver,
                    "_write_durable_new_json_receipt",
                    side_effect=delayed_intent_write,
                ),
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )
            outcome = json.loads(
                (
                    Path(temporary)
                    / driver.ADB_SHARED_STORAGE_INITIALIZATION_OUTCOME_FILENAME
                ).read_text(encoding="utf-8")
            )

        self.assertEqual(3, run.call_count)
        self.assertNotIn(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, issued)
        self.assertEqual(
            "shared-storage-bootstrap-deadline-lease-missing-after-intent",
            raised.exception.receipt["terminalFailure"]["classification"],
        )
        self.assertEqual(0, raised.exception.receipt["mutationCommandsIssued"])
        self.assertEqual("fail-closed-before-invocation", outcome["status"])
        self.assertEqual(0, outcome["attempts"])
        self.assertFalse(outcome["commandInvocationPerformed"])
        self.assertIsNotNone(device._mutation_blocker)

    def test_post_initialization_fuse_failure_is_terminal_without_further_io(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw = "/sdcard/Download:43:106500:41ed\n"
        unconsumed = completed(
            driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            root + raw,
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            responses = [
                download_missing(root),
                download_missing(root),
                download_missing(root),
                completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
                completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                shared_storage_unavailable(),
                unconsumed,
            ]
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=responses,
                ) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )
            self.assertEqual(6, run.call_count)
            terminal = raised.exception.receipt["terminalFailure"]
            self.assertEqual(
                "shared-storage-post-initialization-observation-failed",
                terminal["classification"],
            )
            self.assertEqual(
                "shared-storage-unavailable",
                terminal["underlyingClassification"],
            )
            self.assertFalse(terminal["retryableReadOnlyObservation"])

    def test_bootstrap_cancellation_after_intent_keeps_mutation_fence_armed(
        self,
    ) -> None:
        class SimulatedCancellation(BaseException):
            pass

        root = "/sdcard:43:106499:45f8\n"
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            responses = iter(
                [
                    download_missing(root),
                    download_missing(root),
                    download_missing(root),
                    SimulatedCancellation("cancelled during mkdir"),
                ]
            )

            def invoke(_command: list[str], **_kwargs: object) -> object:
                response = next(responses)
                if isinstance(response, BaseException):
                    raise response
                return response

            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(SimulatedCancellation):
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )
                calls_after_cancellation = run.call_count
                with self.assertRaises(driver.AdbTransportError):
                    device.shell("rm", "-f", "/sdcard/Download/runner.chum5")

            self.assertEqual(4, calls_after_cancellation)
            self.assertEqual(calls_after_cancellation, run.call_count)
            self.assertIsNotNone(device._mutation_blocker)
            self.assertTrue(
                (
                    evidence
                    / driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_FILENAME
                ).is_file()
            )
            self.assertFalse(
                (
                    evidence
                    / driver.ADB_SHARED_STORAGE_INITIALIZATION_OUTCOME_FILENAME
                ).exists()
            )

    def test_bootstrap_rejects_tampered_or_deleted_outcome_before_final_receipt(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw = "/sdcard/Download:43:106500:41ed\n"
        stable = root + raw
        for alteration in ("tamper", "delete"):
            with self.subTest(alteration=alteration), tempfile.TemporaryDirectory() as temporary:
                evidence = Path(temporary)
                outcome_path = (
                    evidence
                    / driver.ADB_SHARED_STORAGE_INITIALIZATION_OUTCOME_FILENAME
                )
                raw_observations = 0
                responses = iter(
                    [
                        download_missing(root),
                        download_missing(root),
                        download_missing(root),
                        completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
                        completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                        completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                    ]
                )

                def invoke(command: list[str], **_kwargs: object) -> object:
                    nonlocal raw_observations
                    arguments = tuple(command[3:])
                    response = next(responses)
                    if isinstance(response, BaseException):
                        raise response
                    if arguments == driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS:
                        raw_observations += 1
                        if raw_observations == 2:
                            if alteration == "tamper":
                                outcome_path.write_bytes(b"tampered\n")
                            else:
                                outcome_path.unlink()
                    return response

                device = self.make_device(evidence)
                with (
                    mock.patch.object(driver.subprocess, "run", side_effect=invoke) as run,
                    mock.patch.object(driver.time, "sleep"),
                ):
                    with self.assertRaisesRegex(
                        RuntimeError,
                        "bootstrap outcome receipt",
                    ):
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                            **hosted_storage_authority(),
                        )
                self.assertEqual(9, run.call_count)
                self.assertFalse(
                    (evidence / "adb-shared-storage-readiness.json").exists()
                )

    def test_bootstrap_digest_is_verified_before_final_readiness_receipt(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw = "/sdcard/Download:43:106500:41ed\n"
        stable = root + raw
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            responses = iter(
                [
                    download_missing(root),
                    download_missing(root),
                    download_missing(root),
                    completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
                    completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                ]
            )
            original_write = driver._write_durable_new_json_receipt

            def write_then_tamper(path: Path, receipt: dict[str, object]) -> str:
                digest = original_write(path, receipt)
                if path.name == driver.ADB_SHARED_STORAGE_BOOTSTRAP_FILENAME:
                    path.write_bytes(b"tampered\n")
                return digest

            def invoke(_command: list[str], **_kwargs: object) -> object:
                response = next(responses)
                if isinstance(response, BaseException):
                    raise response
                return response

            device = self.make_device(evidence)
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke),
                mock.patch.object(driver.time, "sleep"),
                mock.patch.object(
                    driver,
                    "_write_durable_new_json_receipt",
                    side_effect=write_then_tamper,
                ),
            ):
                with self.assertRaisesRegex(
                    RuntimeError,
                    "bootstrap authority receipt bytes changed",
                ):
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )

            self.assertIsNotNone(device._mutation_blocker)
            self.assertFalse(
                (evidence / "adb-shared-storage-readiness.json").exists()
            )

    def test_intent_digest_uses_immutable_serialized_bytes_not_replaced_path(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw = "/sdcard/Download:43:106500:41ed\n"
        stable = root + raw
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            intent_path = (
                evidence / driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_FILENAME
            )
            original_intent_sha256 = ""
            responses = iter(
                [
                    download_missing(root),
                    download_missing(root),
                    download_missing(root),
                    completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
                    completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                    completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                ]
            )

            def invoke(command: list[str], **_kwargs: object) -> object:
                nonlocal original_intent_sha256
                arguments = tuple(command[3:])
                if arguments == driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS:
                    original = intent_path.read_bytes()
                    original_intent_sha256 = driver.hashlib.sha256(original).hexdigest()
                    intent_path.write_bytes(b"replacement\n")
                response = next(responses)
                if isinstance(response, BaseException):
                    raise response
                return response

            device = self.make_device(evidence)
            with (
                mock.patch.object(driver.subprocess, "run", side_effect=invoke),
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaisesRegex(
                    RuntimeError,
                    "bootstrap intent receipt bytes changed",
                ):
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )
            outcome = json.loads(
                (
                    evidence
                    / driver.ADB_SHARED_STORAGE_INITIALIZATION_OUTCOME_FILENAME
                ).read_text(encoding="utf-8")
            )
            self.assertEqual(original_intent_sha256, outcome["intentSha256"])

    def test_download_initialization_command_failure_is_one_shot_and_blocks_app_mutation(
        self,
    ) -> None:
        command_failures = (
            subprocess.CalledProcessError(
                1,
                (
                    "adb",
                    "-s",
                    "SERIAL-API36",
                    *driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
                ),
                output="",
                stderr="mkdir: '/sdcard/Download': File exists\n",
            ),
            subprocess.TimeoutExpired(
                driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
                5,
            ),
            completed(
                driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
                "unexpected output\n",
            ),
            subprocess.CompletedProcess(
                driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
                0,
                stdout="",
                stderr="unexpected diagnostic\n",
            ),
        )
        for command_failure in command_failures:
            with self.subTest(failure=type(command_failure).__name__), tempfile.TemporaryDirectory() as temporary:
                evidence = Path(temporary)
                device = self.make_device(evidence)
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=[
                            download_missing(),
                            download_missing(),
                            download_missing(),
                            command_failure,
                        ],
                    ) as run,
                    mock.patch.object(driver.time, "sleep"),
                ):
                    with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                            **hosted_storage_authority(),
                        )
                    calls_after_failure = run.call_count
                    with self.assertRaises(driver.AdbTransportError):
                        device.shell("rm", "-f", "/sdcard/Download/runner.chum5")
                self.assertEqual(4, calls_after_failure)
                self.assertEqual(calls_after_failure, run.call_count)
                self.assertEqual(
                    "shared-storage-bootstrap-mkdir-failed",
                    raised.exception.receipt["terminalFailure"]["classification"],
                )
                outcome = json.loads(
                    (
                        evidence
                        / driver.ADB_SHARED_STORAGE_INITIALIZATION_OUTCOME_FILENAME
                    ).read_text(encoding="utf-8")
                )
                self.assertEqual("fail-closed-no-retry", outcome["status"])
                self.assertEqual(1, outcome["attempts"])
                self.assertTrue(outcome["commandInvocationPerformed"])
                if isinstance(command_failure, subprocess.CalledProcessError):
                    self.assertEqual(1, outcome["returnCode"])
                    self.assertEqual("", outcome["stdout"])
                    self.assertEqual(
                        "mkdir: '/sdcard/Download': File exists\n",
                        outcome["stderr"],
                    )
                self.assertFalse(outcome["replayAttempted"])
                self.assertFalse(outcome["reconciliationAttempted"])

    def test_download_initialization_rejects_symlink_and_identity_races(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw = "/sdcard/Download:43:106500:41ed\n"
        cases = (
            [
                completed(
                    driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS,
                    "/sdcard/Download:43:106500:a1ff\n",
                )
            ],
            [
                completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                completed(
                    driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                    "/sdcard:99:999:45f8\n" + raw,
                ),
            ],
            [
                completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                completed(
                    driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                    root + "/sdcard/Download:43:106501:41ed\n",
                ),
            ],
        )
        for proof_responses in cases:
            with self.subTest(proof=proof_responses), tempfile.TemporaryDirectory() as temporary:
                device = self.make_device(Path(temporary))
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=[
                            download_missing(root),
                            download_missing(root),
                            download_missing(root),
                            completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
                            *proof_responses,
                        ],
                    ) as run,
                    mock.patch.object(driver.time, "sleep"),
                ):
                    with self.assertRaises(driver.AdbSharedStoragePreflightError):
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                            **hosted_storage_authority(),
                        )
                issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
                self.assertEqual(
                    1,
                    issued.count(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS),
                )

    def test_download_initialization_final_raw_stat_blocks_symlink_swap(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        raw = "/sdcard/Download:43:106500:41ed\n"
        stable = root + raw
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        download_missing(root),
                        download_missing(root),
                        download_missing(root),
                        completed(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS, ""),
                        completed(driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS, raw),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, stable),
                        completed(
                            driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS,
                            "/sdcard/Download:43:106501:41ed\n",
                        ),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep"),
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError):
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(
                1,
                issued.count(driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS),
            )

    def test_stale_download_initialization_intent_blocks_before_adb(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            (
                evidence / driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_FILENAME
            ).write_text("{}\n", encoding="utf-8")
            with mock.patch.object(driver.subprocess, "run") as run:
                with self.assertRaisesRegex(RuntimeError, "contains stale receipts"):
                    self.make_device(evidence)
            run.assert_not_called()

    def test_durable_initialization_receipt_fsyncs_file_and_parent_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            target = Path(temporary) / "intent.json"
            with mock.patch.object(
                driver.os,
                "fsync",
                wraps=driver.os.fsync,
            ) as fsync:
                digest = driver._write_durable_new_json_receipt(
                    target,
                    {"schema": driver.ADB_SHARED_STORAGE_INITIALIZATION_INTENT_SCHEMA},
                )
            self.assertEqual(2, fsync.call_count)
            self.assertTrue(target.is_file())
            self.assertEqual(
                driver.hashlib.sha256(target.read_bytes()).hexdigest(),
                digest,
            )

    def test_shared_storage_preflight_never_retries_generic_adb_failures(self) -> None:
        failures = (
            offline(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS),
            subprocess.CalledProcessError(
                1,
                ("adb", "-s", "SERIAL-API36", *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS),
                output="",
                stderr="cannot connect to daemon",
            ),
            subprocess.CalledProcessError(
                1,
                ("adb", "-s", "SERIAL-API36", *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS),
                output="",
                stderr="transport is closed",
            ),
            subprocess.CalledProcessError(
                1,
                (
                    "adb",
                    "-s",
                    "SERIAL-API36",
                    *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                ),
                output="",
                stderr="stat: '/sdcard/Download': No such file or directory\n",
            ),
        )
        for failure in failures:
            with (
                self.subTest(failure=type(failure).__name__),
                tempfile.TemporaryDirectory() as temporary,
            ):
                device = self.make_device(Path(temporary))
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=failure,
                    ) as run,
                    mock.patch.object(driver.time, "sleep") as sleep,
                ):
                    with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                        )
                self.assertEqual(1, run.call_count)
                sleep.assert_not_called()
                self.assertFalse(
                    raised.exception.receipt["observations"][0][
                        "retryableReadOnlyObservation"
                    ]
                )

    def test_exact_preintent_storage_stat_timeout_gets_one_fresh_observation(
        self,
    ) -> None:
        identity = "/sdcard:42:100:41ed\n/sdcard/Download:42:101:41ed\n"
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = self.make_device(evidence)
            exact_command = (
                str(device.adb),
                "-s",
                device.serial,
                *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            )
            responses = [
                subprocess.TimeoutExpired(
                    exact_command,
                    driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                    output="",
                    stderr="",
                ),
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
            ]
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=responses,
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                )

            self.assertEqual("pass", receipt["status"])
            self.assertEqual(4, run.call_count)
            self.assertEqual(3, sleep.call_count)
            self.assertEqual(4, receipt["observationsPerformed"])
            self.assertEqual(3, receipt["consecutiveStableObservations"])
            self.assertEqual(4, receipt["readOnlyCommandsIssued"])
            self.assertEqual(0, receipt["mutationCommandsIssued"])
            self.assertEqual(
                [
                    "pre-intent-read-only-stat-timeout",
                    "stable",
                    "stable",
                    "stable",
                ],
                [entry["status"] for entry in receipt["observations"]],
            )
            first = receipt["observations"][0]
            self.assertEqual("timeout-unknown-outcome", first["classification"])
            self.assertTrue(first["retryableReadOnlyObservation"])
            self.assertTrue(first["freshObservationScheduled"])
            self.assertFalse(first["timedOutProcessReused"])
            self.assertFalse(first["mutationCommandReplayAuthorized"])
            recovery = receipt["preIntentReadOnlyStatTimeoutRecovery"]
            self.assertEqual(
                1,
                driver.ADB_SHARED_STORAGE_PREINTENT_STAT_TIMEOUT_MAX_RETRIES,
            )
            self.assertEqual(7, driver.ADB_SHARED_STORAGE_MAX_OBSERVATIONS)
            self.assertEqual(30.0, driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS)
            self.assertEqual(1, recovery["maximumRetries"])
            self.assertEqual(2, recovery["maximumCommandInvocations"])
            self.assertEqual(1, recovery["retriesPerformed"])
            self.assertTrue(recovery["freshReadOnlyInvocationRequired"])
            self.assertFalse(recovery["mutationCommandReplayAuthorized"])
            self.assertFalse(recovery["observationBoundWidened"])
            self.assertFalse(recovery["deadlineWidened"])
            self.assertFalse(receipt["adbReconnectAttempted"])
            self.assertFalse(receipt["applicationRelaunchAttempted"])
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertEqual(
                [driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS] * 4,
                issued,
            )

    def test_storage_stat_timeout_recovery_allows_only_one_timeout(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            exact_command = (
                str(device.adb),
                "-s",
                device.serial,
                *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            )
            timeout = subprocess.TimeoutExpired(
                exact_command,
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            )
            unconsumed = completed(
                driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                "/sdcard:42:100:41ed\n/sdcard/Download:42:101:41ed\n",
            )
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[timeout, timeout, unconsumed],
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                    )

            self.assertEqual(2, run.call_count)
            self.assertEqual(1, sleep.call_count)
            self.assertEqual(0, raised.exception.receipt["mutationCommandsIssued"])
            first, second = raised.exception.receipt["observations"]
            self.assertTrue(first["retryableReadOnlyObservation"])
            self.assertFalse(second["retryableReadOnlyObservation"])
            self.assertFalse(second["freshObservationScheduled"])
            self.assertEqual(
                1,
                raised.exception.receipt["preIntentReadOnlyStatTimeoutRecovery"][
                    "retriesPerformed"
                ],
            )

    def test_preintent_stat_timeout_resets_missing_download_quorum(self) -> None:
        root = "/sdcard:43:106499:45f8\n"
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            exact_command = (
                str(device.adb),
                "-s",
                device.serial,
                *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            )
            timeout = subprocess.TimeoutExpired(
                exact_command,
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            )
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        download_missing(root),
                        timeout,
                        download_missing(root),
                        download_missing(root),
                        download_missing(root),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
                        **hosted_storage_authority(),
                    )

            self.assertEqual(5, run.call_count)
            self.assertEqual(4, sleep.call_count)
            self.assertEqual(0, raised.exception.receipt["mutationCommandsIssued"])
            observations = raised.exception.receipt["observations"]
            self.assertEqual(
                [
                    "storage-not-initialized",
                    "pre-intent-read-only-stat-timeout",
                    "storage-not-initialized",
                    "storage-not-initialized",
                    "storage-not-initialized",
                ],
                [entry["status"] for entry in observations],
            )
            self.assertEqual(
                [1, 1, 2, 3],
                [
                    entry["initializationPreconditionConsecutive"]
                    for entry in observations
                    if entry["status"] == "storage-not-initialized"
                ],
            )
            self.assertEqual(
                "shared-storage-bootstrap-slot-authority-missing",
                raised.exception.receipt["terminalFailure"]["classification"],
            )
            issued = [tuple(call.args[0][3:]) for call in run.call_args_list]
            self.assertNotIn(
                driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
                issued,
            )

    def test_preintent_stat_timeout_resets_stable_identity_quorum(self) -> None:
        identity = "/sdcard:42:100:41ed\n/sdcard/Download:42:101:41ed\n"
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            exact_command = (
                str(device.adb),
                "-s",
                device.serial,
                *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            )
            timeout = subprocess.TimeoutExpired(
                exact_command,
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            )
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                        timeout,
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                )

            self.assertEqual("pass", receipt["status"])
            self.assertEqual(5, run.call_count)
            self.assertEqual(4, sleep.call_count)
            self.assertEqual(5, receipt["observationsPerformed"])
            self.assertEqual(3, receipt["consecutiveStableObservations"])
            self.assertEqual(
                [
                    "stable",
                    "pre-intent-read-only-stat-timeout",
                    "stable",
                    "stable",
                    "stable",
                ],
                [entry["status"] for entry in receipt["observations"]],
            )
            self.assertEqual(0, receipt["mutationCommandsIssued"])

    def test_storage_stat_timeout_recovery_rejects_nonexact_command(
        self,
    ) -> None:
        cases = (
            subprocess.TimeoutExpired(
                driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            ),
            subprocess.TimeoutExpired(
                (
                    "/trusted/adb",
                    "-s",
                    "WRONG-SERIAL",
                    *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                ),
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            ),
            subprocess.TimeoutExpired(
                (
                    "/trusted/adb",
                    "-s",
                    "SERIAL-API36",
                    "shell",
                    "stat",
                    "-c",
                    driver.ADB_SHARED_STORAGE_STAT_FORMAT,
                    *driver.ADB_SHARED_STORAGE_ROOTS,
                ),
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            ),
            subprocess.TimeoutExpired(
                (
                    "wrapper",
                    "/trusted/adb",
                    "-s",
                    "SERIAL-API36",
                    *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                ),
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            ),
        )
        for timeout in cases:
            with (
                self.subTest(command=timeout.cmd, output=timeout.output),
                tempfile.TemporaryDirectory() as temporary,
            ):
                device = self.make_device(Path(temporary))
                with (
                    mock.patch.object(
                        driver.subprocess,
                        "run",
                        side_effect=timeout,
                    ) as run,
                    mock.patch.object(driver.time, "sleep") as sleep,
                ):
                    with self.assertRaises(
                        driver.AdbSharedStoragePreflightError
                    ) as raised:
                        device.require_shared_storage_readiness(
                            deadline=driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                        )
                self.assertEqual(1, run.call_count)
                sleep.assert_not_called()
                self.assertFalse(
                    raised.exception.receipt["observations"][0][
                        "retryableReadOnlyObservation"
                    ]
                )
                self.assertEqual(
                    0,
                    raised.exception.receipt["preIntentReadOnlyStatTimeoutRecovery"][
                        "retriesPerformed"
                    ],
                )

    def test_fuse_marker_after_timeout_keeps_only_its_own_retry_authority(self) -> None:
        identity = "/sdcard:42:100:41ed\n/sdcard/Download:42:101:41ed\n"
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            exact_command = (
                str(device.adb),
                "-s",
                device.serial,
                *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            )
            timeout = subprocess.TimeoutExpired(
                exact_command,
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            )
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        timeout,
                        shared_storage_unavailable(),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                )

            self.assertEqual("pass", receipt["status"])
            self.assertEqual(5, run.call_count)
            self.assertEqual(4, sleep.call_count)
            fuse = receipt["observations"][1]
            self.assertEqual("shared-storage-unavailable", fuse["classification"])
            self.assertEqual(
                "recognized-transient-shared-storage-marker",
                fuse["classificationAuthority"],
            )
            self.assertFalse(fuse["freshObservationScheduled"])
            self.assertEqual(
                1,
                receipt["preIntentReadOnlyStatTimeoutRecovery"]["retriesPerformed"],
            )

    def test_initial_stat_timeout_partial_output_is_discarded_before_retry(self) -> None:
        identity = "/sdcard:42:100:41ed\n/sdcard/Download:42:101:41ed\n"
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            exact_command = (
                str(device.adb),
                "-s",
                device.serial,
                *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            )
            timeout = subprocess.TimeoutExpired(
                exact_command,
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="/sdcard:999:999:41ed\n",
                stderr="partial diagnostic",
            )
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        timeout,
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, identity),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                )

            self.assertEqual("pass", receipt["status"])
            self.assertEqual(4, run.call_count)
            self.assertEqual(3, sleep.call_count)
            first = receipt["observations"][0]
            self.assertTrue(first["retryableReadOnlyObservation"])
            self.assertFalse(first["timeoutOutputAcceptedAsAuthority"])
            self.assertNotIn("roots", first)
            self.assertEqual(
                "/sdcard:999:999:41ed\n",
                first["failure"]["stdout"],
            )
            self.assertEqual(3, receipt["consecutiveStableObservations"])

    def test_storage_stat_timeout_recovery_preserves_full_retry_lease(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            exact_command = (
                str(device.adb),
                "-s",
                device.serial,
                *driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
            )
            timeout = subprocess.TimeoutExpired(
                exact_command,
                driver.ADB_SHARED_STORAGE_STAT_ATTEMPT_MAX_SECONDS,
                output="",
                stderr="",
            )
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=timeout,
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=(
                            driver.time.monotonic()
                            + driver.ADB_SHARED_STORAGE_PREINTENT_STAT_TIMEOUT_RETRY_MINIMUM_LEASE_SECONDS
                            - 0.5
                        )
                    )

            self.assertEqual(1, run.call_count)
            sleep.assert_not_called()
            self.assertFalse(
                raised.exception.receipt["observations"][0][
                    "retryableReadOnlyObservation"
                ]
            )
            self.assertEqual(0, raised.exception.receipt["mutationCommandsIssued"])

    def test_shared_storage_identity_drift_resets_consecutive_authority(self) -> None:
        first = "/sdcard:42:100:41ed\n/sdcard/Download:42:101:41ed\n"
        second = "/sdcard:84:200:41ed\n/sdcard/Download:84:201:41ed\n"
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    side_effect=[
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, first),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, second),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, second),
                        completed(driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS, second),
                    ],
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                receipt = device.require_shared_storage_readiness(
                    deadline=driver.time.monotonic()
                    + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                )
        self.assertEqual(4, run.call_count)
        self.assertEqual(3, sleep.call_count)
        self.assertEqual("pass", receipt["status"])
        self.assertEqual(3, receipt["consecutiveStableObservations"])
        self.assertEqual(
            ["stable", "identity-drift", "stable", "stable"],
            [entry["status"] for entry in receipt["observations"]],
        )

    def test_shared_storage_parser_rejects_malformed_or_reordered_identity(self) -> None:
        malformed = (
            "/sdcard:42:100:41ed\n",
            "/sdcard/Download:42:101:41ed\n/sdcard:42:100:41ed\n",
            "/sdcard:42:100:41ed\n/sdcard:42:100:41ed\n",
            "/sdcard:device:100:41ed\n/sdcard/Download:42:101:41ed\n",
        )
        for output in malformed:
            with self.subTest(output=output):
                with self.assertRaises(RuntimeError):
                    driver._parse_shared_storage_stat_output(output)

    def test_follow_mode_observes_a_genuine_symlink_as_its_directory_target(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            target = root / "emulated-storage"
            target.mkdir()
            (target / "Download").mkdir()
            target_inode = target.stat().st_ino
            sdcard = root / "sdcard"
            sdcard.symlink_to(target, target_is_directory=True)
            followed = subprocess.run(
                ["stat", "-L", "-c", "%d:%i:%f", str(sdcard)],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip().split(":")
            raw = subprocess.run(
                ["stat", "-c", "%d:%i:%f", str(sdcard)],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip().split(":")
        self.assertEqual(target_inode, int(followed[1]))
        self.assertEqual(0o040000, int(followed[2], 16) & 0o170000)
        self.assertEqual(0o120000, int(raw[2], 16) & 0o170000)

    def test_shared_storage_preflight_rejects_non_directory_without_retry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            with (
                mock.patch.object(
                    driver.subprocess,
                    "run",
                    return_value=completed(
                        driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS,
                        "/sdcard:42:100:a1ff\n"
                        "/sdcard/Download:42:101:41ed\n",
                    ),
                ) as run,
                mock.patch.object(driver.time, "sleep") as sleep,
            ):
                with self.assertRaises(driver.AdbSharedStoragePreflightError) as raised:
                    device.require_shared_storage_readiness(
                        deadline=driver.time.monotonic()
                        + driver.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
                    )

            self.assertEqual(1, run.call_count)
            sleep.assert_not_called()
            receipt = raised.exception.receipt
            self.assertEqual("fail", receipt["status"])
            self.assertEqual("invalid-observation", receipt["observations"][0]["status"])
            self.assertEqual(0, receipt["mutationCommandsIssued"])

    def test_stale_shared_storage_receipt_is_rejected_before_any_adb_command(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            stale = evidence / "adb-shared-storage-readiness.json"
            stale.write_text('{"status":"pass"}\n', encoding="utf-8")
            with mock.patch.object(driver.subprocess, "run") as run:
                with self.assertRaisesRegex(RuntimeError, "contains stale receipts"):
                    self.make_device(evidence)
            run.assert_not_called()

    def test_shared_storage_stat_policy_is_exact_and_read_only(self) -> None:
        self.assertEqual(
            (
                "read-only-retryable",
                "exact follow-mode shared-storage directory identity observation",
            ),
            driver.adb_command_retry_policy(
                driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS
            ),
        )
        self.assertEqual("-L", driver.ADB_SHARED_STORAGE_STAT_ARGUMENTS[2])
        self.assertEqual(
            "read-only-retryable",
            driver.adb_command_retry_policy(
                driver.ADB_SHARED_STORAGE_DOWNLOAD_RAW_STAT_ARGUMENTS
            )[0],
        )
        self.assertEqual(
            "non-replayable",
            driver.adb_command_retry_policy(
                driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS
            )[0],
        )
        self.assertEqual(
            ("shell", "mkdir", "/sdcard/Download"),
            driver.ADB_SHARED_STORAGE_INITIALIZE_ARGUMENTS,
        )
        self.assertEqual(
            "non-replayable",
            driver.adb_command_retry_policy(
                (
                    "shell",
                    "stat",
                    "-c",
                    driver.ADB_SHARED_STORAGE_STAT_FORMAT,
                    *driver.ADB_SHARED_STORAGE_ROOTS,
                )
            )[0],
        )

    def test_all_seven_wizard_drivers_preflight_storage_before_mutation(self) -> None:
        first_mutation_markers = {
            "run_api36_creation_prerequisite_e2e.py": "subprocess.run(",
            "run_api36_career_active_skill_advance_e2e.py": "subprocess.run(",
            "run_api36_career_weapon_fire_e2e.py": "subprocess.run(",
            "run_api36_sr5_before_run_edge_e2e.py": "device.install_verified(",
            "run_api36_sr5_playtime_short_burst_e2e.py": "device.install_verified(",
            "run_api36_sr5_downtime_calendar_hosted_e2e.py": "device.install_verified(",
            "run_api36_sr5_after_run_settlement_hosted_e2e.py": (
                "for remote in remote_temporary_files:"
            ),
        }
        for filename, mutation_marker in first_mutation_markers.items():
            with self.subTest(filename=filename):
                source = (ROOT / "tests" / filename).read_text(encoding="utf-8")
                readiness_index = source.index("device.require_shared_storage_readiness(")
                deadline_index = source.index("deadline=", readiness_index)
                mutation_index = source.index(mutation_marker, readiness_index)
                self.assertLess(readiness_index, mutation_index)
                self.assertLess(deadline_index, mutation_index)
                for authority_name in (
                    "hosted_api_level=",
                    "hosted_abi=",
                    "hosted_emulator=",
                    "hosted_proof_attempt=True",
                ):
                    self.assertLess(
                        source.index(authority_name, readiness_index),
                        mutation_index,
                    )

    def test_shared_storage_marker_never_authorizes_mutation_replay(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = self.make_device(Path(temporary))
            unavailable = subprocess.CalledProcessError(
                1,
                ("shell", "rm"),
                output="",
                stderr="Transport endpoint is not connected",
            )
            with mock.patch.object(
                driver.subprocess,
                "run",
                side_effect=unavailable,
            ) as run:
                with self.assertRaises(driver.AdbTransportError) as raised:
                    device.shell(
                        "rm",
                        "-f",
                        "/sdcard/Download/runner.chum5",
                    )

            self.assertEqual(1, run.call_count)
            receipt = raised.exception.receipt
            self.assertEqual("unclassified-adb-failure", receipt["classification"])
            self.assertFalse(receipt["retryableTransportClassification"])
            self.assertEqual("non-replayable", receipt["commandPolicy"])
            self.assertFalse(receipt["replay"]["eligible"])
            self.assertFalse(receipt["replay"]["performed"])
            self.assertFalse(receipt["replay"]["scheduled"])
            self.assertTrue(receipt["replay"]["suppressed"])

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

    def test_only_exact_hierarchy_observers_are_retryable(self) -> None:
        self.assertEqual(
            "read-only-retryable",
            driver.adb_command_retry_policy(
                driver.ADB_READ_ONLY_HIERARCHY_ARGUMENTS
            )[0],
        )
        self.assertEqual(
            "read-only-retryable",
            driver.adb_command_retry_policy(
                ("shell", *driver.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS)
            )[0],
        )
        self.assertEqual(
            "non-replayable",
            driver.adb_command_retry_policy(
                ("shell", *driver.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS)
            )[0],
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
