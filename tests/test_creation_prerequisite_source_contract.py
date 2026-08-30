import ast
import importlib.util
import inspect
import json
import os
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from unittest import mock
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"
DRIVER = REPO / "tests" / "run_api36_creation_prerequisite_e2e.py"

sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("creation_prerequisite_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(driver)


class CreationPrerequisiteSourceContractTests(unittest.TestCase):
    @staticmethod
    def priority_rank_origin(
        nodes: list[driver.shared.UiNode],
        *,
        reverse_swipes: int = 0,
        elapsed_ms: int = 7,
        hierarchy_durations_ms: tuple[int, ...] = (5,),
        empty_hierarchy_reads: int = 0,
    ) -> driver.PriorityRankOrigin:
        return driver.PriorityRankOrigin(
            nodes=nodes,
            reverse_swipes=reverse_swipes,
            elapsed_ms=elapsed_ms,
            hierarchy_durations_ms=hierarchy_durations_ms,
            empty_hierarchy_reads=empty_hierarchy_reads,
        )

    @staticmethod
    def bootstrap_timing_payload(**changes: object) -> dict[str, object]:
        payload: dict[str, object] = {
            "schema": "chummer.android.creation-bootstrap-timing/v1",
            "actionId": "create_character",
            "loadStartObserved": True,
            "workspaceStatePublished": True,
            "exactPublishedWorkspace": True,
            "reusedPresenterShellSync": True,
            "coreCreateMs": 12,
            "presenterLoadMs": 20,
            "presenterNavigationAndShellMs": 5,
            "activeSectionMs": 0,
            "androidRetainedRefreshMs": 1,
            "androidFullShellSyncMs": -1,
            "processPendingOutputsMs": 0,
            "totalMs": 38,
        }
        payload.update(changes)
        return payload

    def test_creation_bootstrap_timing_requires_exact_partition_and_reused_sync(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            payload = self.bootstrap_timing_payload()

            class TimingDevice:
                evidence = Path(temporary)

                @staticmethod
                def run(*_arguments: str, **_options: object) -> subprocess.CompletedProcess:
                    return subprocess.CompletedProcess(
                        [],
                        0,
                        stdout=(
                            driver.CREATION_BOOTSTRAP_TIMING_PREFIX
                            + json.dumps(payload, separators=(",", ":"))
                            + "\n"
                        ),
                        stderr="",
                    )

                @staticmethod
                def capture(name: str) -> None:
                    raise AssertionError(f"unexpected capture: {name}")

            timing = driver.capture_creation_bootstrap_timing(TimingDevice())
            self.assertEqual(payload, timing)
            self.assertEqual(
                payload,
                json.loads(
                    (Path(temporary) / driver.CREATION_BOOTSTRAP_TIMING_FILE_NAME)
                    .read_text(encoding="utf-8")
                ),
            )
            self.assertTrue(
                (Path(temporary) / driver.CREATION_BOOTSTRAP_LOGCAT_FILE_NAME).is_file()
            )

    def test_creation_bootstrap_timing_rejects_fallback_or_forged_totals(self) -> None:
        for changes, expected in (
            ({"reusedPresenterShellSync": False}, "reusedPresenterShellSync"),
            ({"totalMs": 400}, "did not partition"),
            ({"coreCreateMs": -1}, "nonnegative integer"),
        ):
            with self.subTest(changes=changes), tempfile.TemporaryDirectory() as temporary:
                payload = self.bootstrap_timing_payload(**changes)

                class TimingDevice:
                    evidence = Path(temporary)
                    captures: list[str] = []

                    @staticmethod
                    def run(*_arguments: str, **_options: object) -> subprocess.CompletedProcess:
                        return subprocess.CompletedProcess(
                            [],
                            0,
                            stdout=(
                                driver.CREATION_BOOTSTRAP_TIMING_PREFIX
                                + json.dumps(payload, separators=(",", ":"))
                            ),
                            stderr="",
                        )

                    @classmethod
                    def capture(cls, name: str) -> None:
                        cls.captures.append(name)

                with self.assertRaisesRegex(RuntimeError, expected):
                    driver.capture_creation_bootstrap_timing(TimingDevice())

    def test_creation_bootstrap_stream_is_local_exact_and_conservatively_non_replayable(self) -> None:
        wait_expected = (
            "logcat",
            "-b",
            "main",
            "-v",
            "raw",
            "-T",
            "1",
            "-m",
            "1",
            "-e",
            r"^CHUMMER_CREATION_BOOTSTRAP_TIMING \{",
            "-s",
            "ChummerBootstrap:I",
            "*:S",
        )
        snapshot_expected = (
            "logcat",
            "-d",
            "-b",
            "main",
            "-v",
            "raw",
            "-s",
            "ChummerBootstrap:I",
            "*:S",
        )
        self.assertEqual(
            wait_expected,
            driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
        )
        self.assertEqual(
            snapshot_expected,
            driver.ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS,
        )
        self.assertFalse(
            hasattr(driver.shared, "ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS")
        )
        self.assertFalse(
            hasattr(driver.shared, "ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS")
        )
        near_misses = (
            wait_expected[:7] + wait_expected[9:],
            tuple(
                "CHUMMER_CREATION_BOOTSTRAP_TIMING"
                if argument == r"^CHUMMER_CREATION_BOOTSTRAP_TIMING \{"
                else argument
                for argument in wait_expected
            ),
            tuple(
                "*:I" if argument == "ChummerBootstrap:I" else argument
                for argument in wait_expected
            ),
            snapshot_expected[:4] + snapshot_expected[6:],
            tuple(
                "system" if argument == "main" else argument
                for argument in wait_expected
            ),
            tuple(
                "system" if argument == "main" else argument
                for argument in snapshot_expected
            ),
        )
        for arguments in (wait_expected, snapshot_expected, *near_misses):
            with self.subTest(arguments=arguments):
                self.assertEqual(
                    "non-replayable",
                    driver.shared.adb_command_retry_policy(arguments)[0],
                )

    def test_creation_bootstrap_marker_poll_uses_only_exact_tagged_logcat(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            payload = self.bootstrap_timing_payload()

            class TimingDevice:
                evidence = Path(temporary)
                calls: list[tuple[str, ...]] = []
                options: list[dict[str, object]] = []

                @classmethod
                def run(cls, *arguments: str, **options: object) -> subprocess.CompletedProcess:
                    cls.calls.append(arguments)
                    cls.options.append(options)
                    return subprocess.CompletedProcess(
                        arguments,
                        0,
                        stdout=(
                            driver.CREATION_BOOTSTRAP_TIMING_PREFIX
                            + json.dumps(payload, separators=(",", ":"))
                        ),
                        stderr="",
                    )

                @staticmethod
                def hierarchy() -> None:
                    raise AssertionError("marker polling must not observe UI hierarchy")

                @staticmethod
                def capture(name: str) -> None:
                    raise AssertionError(f"unexpected capture: {name}")

            observation: dict[str, object] = {}
            logcat = driver.wait_for_creation_bootstrap_timing_log(
                TimingDevice(),
                observation_out=observation,
            )
            self.assertIn(driver.CREATION_BOOTSTRAP_TIMING_PREFIX, logcat)
            self.assertEqual(
                [
                    driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                    driver.ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS,
                ],
                TimingDevice.calls,
            )
            self.assertEqual(90.0, TimingDevice.options[0]["timeout"])
            self.assertEqual(30, TimingDevice.options[1]["timeout"])
            self.assertEqual(
                TimingDevice.options[0]["deadline"],
                TimingDevice.options[1]["deadline"],
            )
            self.assertEqual("resolved", observation["status"])
            self.assertEqual(2, observation["logcatReadCount"])
            self.assertEqual(
                "single-bounded-stream-plus-snapshot",
                observation["observationMode"],
            )
            self.assertEqual(1, observation["streamLogcatReadCount"])
            self.assertEqual(1, observation["snapshotLogcatReadCount"])
            self.assertNotIn(
                "time.sleep",
                inspect.getsource(driver.wait_for_creation_bootstrap_timing_log),
            )

    def test_creation_bootstrap_stream_and_snapshot_accept_one_exact_main_divider(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            payload = self.bootstrap_timing_payload()
            marker = driver.CREATION_BOOTSTRAP_TIMING_PREFIX + json.dumps(
                payload,
                separators=(",", ":"),
            )
            framed = f"{driver.CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER}\n{marker}\n"

            class TimingDevice:
                evidence = Path(temporary)
                calls: list[tuple[str, ...]] = []

                @classmethod
                def run(cls, *arguments: str, **_options: object) -> subprocess.CompletedProcess:
                    cls.calls.append(arguments)
                    return subprocess.CompletedProcess(
                        arguments,
                        0,
                        stdout=framed,
                        stderr="",
                    )

                @staticmethod
                def capture(name: str) -> None:
                    raise AssertionError(f"unexpected capture: {name}")

            logcat = driver.wait_for_creation_bootstrap_timing_log(TimingDevice())
            self.assertEqual(framed, logcat)
            self.assertEqual(payload, driver.capture_creation_bootstrap_timing(
                TimingDevice(),
                logcat=logcat,
            ))
            self.assertEqual(
                [
                    driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                    driver.ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS,
                ],
                TimingDevice.calls,
            )

    def test_creation_bootstrap_timing_rejects_illegal_logcat_framing(self) -> None:
        payload = self.bootstrap_timing_payload()
        marker = driver.CREATION_BOOTSTRAP_TIMING_PREFIX + json.dumps(
            payload,
            separators=(",", ":"),
        )
        divider = driver.CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER
        illegal_logs = (
            f"--------- beginning of system\n{marker}",
            f"{divider}\n{divider}\n{marker}",
            f"{marker}\n{divider}",
            f"-------- beginning of main\n{marker}",
            f"{divider} \n{marker}",
            f"unexpected raw log line\n{marker}",
        )

        for logcat in illegal_logs:
            with self.subTest(logcat=logcat), tempfile.TemporaryDirectory() as temporary:
                class TimingDevice:
                    evidence = Path(temporary)
                    captures: list[str] = []

                    @classmethod
                    def capture(cls, name: str) -> None:
                        cls.captures.append(name)

                with self.assertRaisesRegex(RuntimeError, "canonical main-buffer divider"):
                    driver.capture_creation_bootstrap_timing(
                        TimingDevice(),
                        logcat=logcat,
                    )
                self.assertEqual(
                    ["creation-bootstrap-timing-cardinality-invalid"],
                    TimingDevice.captures,
                )

    def test_creation_bootstrap_timing_rejects_malformed_exact_payload(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = mock.Mock()
            device.evidence = Path(temporary)
            logcat = (
                f"{driver.CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER}\n"
                f"{driver.CREATION_BOOTSTRAP_TIMING_PREFIX}{{not-json}}"
            )

            with self.assertRaisesRegex(RuntimeError, "invalid JSON"):
                driver.capture_creation_bootstrap_timing(device, logcat=logcat)

            device.capture.assert_called_once_with(
                "creation-bootstrap-timing-json-invalid"
            )

    def test_creation_bootstrap_wait_rejects_illegal_stream_or_snapshot_lines(self) -> None:
        payload = self.bootstrap_timing_payload()
        marker = driver.CREATION_BOOTSTRAP_TIMING_PREFIX + json.dumps(
            payload,
            separators=(",", ":"),
        )
        divider = driver.CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER
        cases = (
            (
                (f"{divider}\nnoise\n{marker}",),
                1,
            ),
            (
                (marker, f"{divider}\n{divider}\n{marker}"),
                2,
            ),
        )

        for outputs, expected_calls in cases:
            with self.subTest(outputs=outputs), tempfile.TemporaryDirectory() as temporary:
                device = mock.Mock()
                device.evidence = Path(temporary)
                device.run.side_effect = tuple(
                    subprocess.CompletedProcess([], 0, stdout=output, stderr="")
                    for output in outputs
                )

                with self.assertRaisesRegex(RuntimeError, "post-action creation bootstrap"):
                    driver.wait_for_creation_bootstrap_timing_log(device, timeout=1.0)

                self.assertEqual(expected_calls, device.run.call_count)
                device.capture.assert_called_once_with(
                    "creation-bootstrap-timing-log-timeout"
                )

    def test_creation_bootstrap_marker_stream_does_not_hide_snapshot_cardinality(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            first = self.bootstrap_timing_payload()
            second = self.bootstrap_timing_payload(coreCreateMs=13, totalMs=39)
            marker = driver.CREATION_BOOTSTRAP_TIMING_PREFIX

            class TimingDevice:
                evidence = Path(temporary)
                calls: list[tuple[str, ...]] = []
                captures: list[str] = []

                @classmethod
                def run(cls, *arguments: str, **_options: object) -> subprocess.CompletedProcess:
                    cls.calls.append(arguments)
                    payloads = [first] if len(cls.calls) == 1 else [first, second]
                    return subprocess.CompletedProcess(
                        arguments,
                        0,
                        stdout="\n".join(
                            marker + json.dumps(payload, separators=(",", ":"))
                            for payload in payloads
                        ),
                        stderr="",
                    )

                @classmethod
                def capture(cls, name: str) -> None:
                    cls.captures.append(name)

            logcat = driver.wait_for_creation_bootstrap_timing_log(TimingDevice())
            with self.assertRaisesRegex(RuntimeError, "exactly one exact"):
                driver.capture_creation_bootstrap_timing(
                    TimingDevice(),
                    logcat=logcat,
                )
            self.assertEqual(
                [
                    driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                    driver.ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS,
                ],
                TimingDevice.calls,
            )
            self.assertEqual(
                ["creation-bootstrap-timing-cardinality-invalid"],
                TimingDevice.captures,
            )

    def test_creation_bootstrap_timing_rejects_byte_identical_duplicate_lines(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            payload = self.bootstrap_timing_payload()
            line = driver.CREATION_BOOTSTRAP_TIMING_PREFIX + json.dumps(
                payload,
                separators=(",", ":"),
            )

            class TimingDevice:
                evidence = Path(temporary)
                captures: list[str] = []

                @classmethod
                def capture(cls, name: str) -> None:
                    cls.captures.append(name)

            with self.assertRaisesRegex(
                RuntimeError,
                r"exactly one exact.*raw=2, exact=2",
            ):
                driver.capture_creation_bootstrap_timing(
                    TimingDevice(),
                    logcat=f"{line}\n{line}",
                )
            self.assertEqual(
                ["creation-bootstrap-timing-cardinality-invalid"],
                TimingDevice.captures,
            )

    def test_creation_bootstrap_stream_rejects_prefixed_marker_line(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            payload = self.bootstrap_timing_payload()
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.return_value = subprocess.CompletedProcess(
                driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                0,
                stdout=(
                    "X"
                    + driver.CREATION_BOOTSTRAP_TIMING_PREFIX
                    + json.dumps(payload, separators=(",", ":"))
                ),
                stderr="",
            )

            with self.assertRaisesRegex(RuntimeError, "post-action creation bootstrap"):
                driver.wait_for_creation_bootstrap_timing_log(device, timeout=1.0)

            device.run.assert_called_once()
            self.assertEqual(
                driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                device.run.call_args.args,
            )
            device.capture.assert_called_once_with(
                "creation-bootstrap-timing-log-timeout"
            )

    def test_creation_bootstrap_stream_success_at_deadline_fails_without_snapshot(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            payload = self.bootstrap_timing_payload()
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.return_value = subprocess.CompletedProcess(
                driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                0,
                stdout=(
                    driver.CREATION_BOOTSTRAP_TIMING_PREFIX
                    + json.dumps(payload, separators=(",", ":"))
                ),
                stderr="",
            )
            observation: dict[str, object] = {}

            with mock.patch.object(
                driver.time,
                "monotonic",
                side_effect=[10.0, 10.0, 11.0, 11.0],
            ), self.assertRaisesRegex(RuntimeError, "post-action creation bootstrap"):
                driver.wait_for_creation_bootstrap_timing_log(
                    device,
                    timeout=1.0,
                    observation_out=observation,
                )

            device.run.assert_called_once_with(
                *driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                timeout=1.0,
                deadline=11.0,
            )
            self.assertEqual("timeout", observation["status"])
            self.assertEqual(0, observation["snapshotLogcatReadCount"])

    def test_creation_bootstrap_snapshot_cannot_extend_original_deadline(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            payload = self.bootstrap_timing_payload()
            marker = driver.CREATION_BOOTSTRAP_TIMING_PREFIX + json.dumps(
                payload,
                separators=(",", ":"),
            )
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.side_effect = (
                subprocess.CompletedProcess(
                    driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                    0,
                    stdout=marker,
                    stderr="",
                ),
                subprocess.CompletedProcess(
                    driver.ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS,
                    0,
                    stdout=marker,
                    stderr="",
                ),
            )
            observation: dict[str, object] = {}

            with mock.patch.object(
                driver.time,
                "monotonic",
                side_effect=[10.0, 10.0, 10.5, 11.0, 11.0],
            ), self.assertRaisesRegex(RuntimeError, "post-action creation bootstrap"):
                driver.wait_for_creation_bootstrap_timing_log(
                    device,
                    timeout=1.0,
                    observation_out=observation,
                )

            self.assertEqual(2, device.run.call_count)
            self.assertEqual(
                driver.ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS,
                device.run.call_args_list[1].args,
            )
            self.assertEqual(30, device.run.call_args_list[1].kwargs["timeout"])
            self.assertEqual(11.0, device.run.call_args_list[1].kwargs["deadline"])
            self.assertEqual("timeout", observation["status"])
            self.assertEqual(1, observation["snapshotLogcatReadCount"])

    def test_creation_bootstrap_log_is_cleared_once_without_retry_before_tap(self) -> None:
        device = mock.Mock()

        driver.clear_creation_bootstrap_timing_log(device)

        device.run.assert_called_once_with(
            *driver.shared.ADB_CREATION_BOOTSTRAP_LOGCAT_CLEAR_ARGUMENTS,
            timeout=30,
        )
        self.assertEqual(
            "non-replayable",
            driver.shared.adb_command_retry_policy(
                driver.shared.ADB_CREATION_BOOTSTRAP_LOGCAT_CLEAR_ARGUMENTS
            )[0],
        )

    def test_creation_bootstrap_marker_poll_times_out_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.side_effect = subprocess.TimeoutExpired(
                driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                1.0,
            )
            observation: dict[str, object] = {}
            with mock.patch.object(
                driver.time,
                "monotonic",
                side_effect=[10.0, 10.0, 11.0],
            ), self.assertRaisesRegex(RuntimeError, "post-action creation bootstrap"):
                driver.wait_for_creation_bootstrap_timing_log(
                    device,
                    timeout=1.0,
                    observation_out=observation,
                )
            device.run.assert_called_once_with(
                *driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                timeout=1.0,
                deadline=11.0,
            )
            device.capture.assert_called_once_with(
                "creation-bootstrap-timing-log-timeout"
            )
            self.assertEqual("timeout", observation["status"])
            self.assertEqual(1, observation["logcatReadCount"])
            self.assertEqual(1, observation["streamLogcatReadCount"])
            self.assertEqual(0, observation["snapshotLogcatReadCount"])

    def test_creation_bootstrap_transport_timeout_rejects_partial_marker_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = mock.Mock()
            device.evidence = evidence
            receipt = {
                "classification": "timeout-unknown-outcome",
                "commandPolicy": "non-replayable",
                "replay": {"performed": False, "suppressed": True},
                "failure": {
                    "stdout": driver.CREATION_BOOTSTRAP_TIMING_PREFIX + "{}",
                },
            }
            device.run.side_effect = driver.shared.AdbTransportError(
                receipt,
                evidence / "adb-transport-event-0001.json",
            )
            observation: dict[str, object] = {}

            with mock.patch.object(
                driver.time,
                "monotonic",
                side_effect=[10.0, 10.0, 11.0],
            ), self.assertRaisesRegex(RuntimeError, "post-action creation bootstrap"):
                driver.wait_for_creation_bootstrap_timing_log(
                    device,
                    timeout=1.0,
                    observation_out=observation,
                )

            device.run.assert_called_once_with(
                *driver.ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
                timeout=1.0,
                deadline=11.0,
            )
            device.capture.assert_called_once_with(
                "creation-bootstrap-timing-log-timeout"
            )
            self.assertEqual("timeout", observation["status"])
            self.assertIn(
                driver.CREATION_BOOTSTRAP_TIMING_PREFIX,
                (evidence / driver.CREATION_BOOTSTRAP_LOGCAT_FILE_NAME).read_text(
                    encoding="utf-8"
                ),
            )

    def test_artifact_binding_digest_uses_canonical_sorted_json(self) -> None:
        first = {"driver": "a", "apk": "b", "nested": {"events": "c"}}
        reordered = {"nested": {"events": "c"}, "apk": "b", "driver": "a"}
        changed = {"nested": {"events": "d"}, "apk": "b", "driver": "a"}
        self.assertEqual(
            driver.canonical_json_sha256(first),
            driver.canonical_json_sha256(reordered),
        )
        self.assertNotEqual(
            driver.canonical_json_sha256(first),
            driver.canonical_json_sha256(changed),
        )

    def test_accessibility_signature_is_order_independent_and_preserves_duplicates(self) -> None:
        first = driver.shared.UiNode(
            {
                "resource-id": "row-one",
                "class": "android.view.View",
                "text": "One",
                "enabled": "true",
                "clickable": "false",
                "bounds": "[0,0][100,100]",
            }
        )
        second = driver.shared.UiNode(
            {
                "resource-id": "row-two",
                "class": "android.widget.Button",
                "content-desc": "Two",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[0,100][100,200]",
            }
        )

        self.assertEqual(
            driver.accessibility_signature([first, second]),
            driver.accessibility_signature([second, first]),
        )
        duplicate_signature = driver.accessibility_signature([first, first])
        self.assertEqual(2, len(duplicate_signature))
        self.assertEqual(duplicate_signature[0], duplicate_signature[1])

    def test_stable_end_scan_stops_after_two_full_unchanged_viewports(self) -> None:
        nodes = [driver.shared.UiNode({"resource-id": "stable-row", "bounds": "[0,0][1,1]"})]

        class StableDevice:
            up = 0

            @staticmethod
            def read_only_hierarchy():
                raise AssertionError("scroll-dependent scan used the stale direct stream")

            @staticmethod
            def hierarchy():
                return nodes

            def swipe_up(self, **_options: object) -> None:
                self.up += 1

            @staticmethod
            def capture(name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = StableDevice()
        observations: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"):
            screens = driver.scan_forward_until_stable(
                device,
                scan_id="stable-proof",
                max_scrolls=40,
                distance_ratio=0.22,
                observer=observations.append,
            )

        self.assertEqual(3, len(screens))
        self.assertEqual(2, device.up)
        self.assertEqual(1, len(observations))
        self.assertEqual("stable-end", observations[0]["status"])
        self.assertEqual(2, observations[0]["swipes"])
        self.assertEqual(40, observations[0]["configuredMaxScrolls"])
        self.assertEqual(3, observations[0]["hierarchyReadCount"])
        self.assertGreaterEqual(observations[0]["hierarchyElapsedMs"], 0)
        self.assertGreaterEqual(observations[0]["maximumHierarchyReadMs"], 0)

    def test_stable_end_scan_reuses_one_fresh_origin_without_skipping_end_proof(self) -> None:
        origin = [
            driver.shared.UiNode(
                {"resource-id": "rank-a", "bounds": "[0,0][100,100]"}
            )
        ]
        end = [
            driver.shared.UiNode(
                {"resource-id": "rank-e", "bounds": "[0,0][100,100]"}
            )
        ]

        class ReusedOriginDevice:
            reads = 0
            swipes = 0

            def hierarchy(self):
                self.reads += 1
                return end

            def swipe_up(self, **_options: object) -> None:
                self.swipes += 1

            @staticmethod
            def capture(name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = ReusedOriginDevice()
        observations: list[dict[str, object]] = []
        origin_observation = self.priority_rank_origin(
            origin,
            reverse_swipes=2,
            elapsed_ms=2400,
            hierarchy_durations_ms=(500, 500, 500, 600),
            empty_hierarchy_reads=1,
        )
        with mock.patch.object(driver.time, "sleep"):
            screens = driver.scan_forward_until_stable(
                device,
                scan_id="reused-origin-proof",
                max_scrolls=8,
                distance_ratio=0.68,
                initial_observation=origin_observation,
                observer=observations.append,
            )

        self.assertEqual([origin, end, end, end], screens)
        self.assertEqual(3, device.reads)
        self.assertEqual(3, device.swipes)
        self.assertEqual("stable-end", observations[0]["status"])
        self.assertTrue(observations[0]["reusedInitialScreen"])
        self.assertEqual(4, observations[0]["screens"])
        self.assertEqual(7, observations[0]["hierarchyReadCount"])
        self.assertEqual(2100, observations[0]["hierarchyElapsedMs"])
        self.assertEqual(600, observations[0]["maximumHierarchyReadMs"])
        self.assertEqual(2400, observations[0]["originElapsedMs"])
        self.assertEqual(2, observations[0]["originReverseSwipes"])
        self.assertEqual(1, observations[0]["originEmptyHierarchyReads"])
        self.assertEqual(0, observations[0]["traversalEmptyHierarchyReads"])
        self.assertEqual(1, observations[0]["emptyHierarchyReads"])
        self.assertEqual(5, observations[0]["totalNavigationSwipes"])
        self.assertEqual(4, observations[0]["originHierarchyReadCount"])
        self.assertEqual(2100, observations[0]["originHierarchyElapsedMs"])
        self.assertEqual(600, observations[0]["originMaximumHierarchyReadMs"])
        self.assertEqual(
            observations[0]["originElapsedMs"] + observations[0]["traversalElapsedMs"],
            observations[0]["elapsedMs"],
        )

    def test_composed_scan_timing_reconciles_every_origin_and_traversal_partition(self) -> None:
        receipt: dict[str, object] = {
            "scanId": "rank-cardinality-heritage",
            "status": "stable-end",
            "reusedInitialScreen": True,
            "originElapsedMs": 2400,
            "originReverseSwipes": 2,
            "originEmptyHierarchyReads": 1,
            "originHierarchyReadCount": 4,
            "originHierarchyElapsedMs": 2100,
            "originMaximumHierarchyReadMs": 600,
            "traversalElapsedMs": 900,
            "traversalEmptyHierarchyReads": 0,
            "emptyHierarchyReads": 1,
            "totalNavigationSwipes": 5,
            "hierarchyReadCount": 7,
            "hierarchyElapsedMs": 2700,
            "maximumHierarchyReadMs": 600,
            "elapsedMs": 3300,
            "swipes": 3,
        }

        driver.require_composed_scan_timing(receipt)

    def test_composed_scan_timing_rejects_omission_types_and_partition_forgery(self) -> None:
        receipt: dict[str, object] = {
            "scanId": "rank-cardinality-heritage",
            "status": "stable-end",
            "reusedInitialScreen": True,
            "originElapsedMs": 2400,
            "originReverseSwipes": 2,
            "originEmptyHierarchyReads": 1,
            "originHierarchyReadCount": 4,
            "originHierarchyElapsedMs": 2100,
            "originMaximumHierarchyReadMs": 600,
            "traversalElapsedMs": 900,
            "traversalEmptyHierarchyReads": 0,
            "emptyHierarchyReads": 1,
            "totalNavigationSwipes": 5,
            "hierarchyReadCount": 7,
            "hierarchyElapsedMs": 2700,
            "maximumHierarchyReadMs": 600,
            "elapsedMs": 3300,
            "swipes": 3,
        }
        mutations = {
            "omitted": lambda value: value.pop("originHierarchyElapsedMs"),
            "aggregate-trigger-omission": lambda value: tuple(
                value.pop(field)
                for field in (
                    "originElapsedMs",
                    "traversalElapsedMs",
                    "originHierarchyReadCount",
                )
            ),
            "all-trigger-fields-omitted": lambda value: tuple(
                value.pop(field)
                for field in driver.COMPOSED_SCAN_TIMING_TRIGGER_FIELDS
            ),
            "bool-as-integer": lambda value: value.__setitem__("swipes", True),
            "elapsed-partition": lambda value: value.__setitem__("elapsedMs", 3299),
            "hierarchy-outside-clock": lambda value: value.__setitem__(
                "hierarchyElapsedMs", 4000
            ),
            "empty-read-partition": lambda value: value.__setitem__(
                "emptyHierarchyReads", 0
            ),
            "navigation-partition": lambda value: value.__setitem__(
                "totalNavigationSwipes", 4
            ),
            "false-reuse-with-origin": lambda value: value.__setitem__(
                "reusedInitialScreen", False
            ),
            "maximum-exceeds-total": lambda value: value.__setitem__(
                "maximumHierarchyReadMs", 2701
            ),
            "origin-maximum-exceeds-origin-sum": lambda value: (
                value.__setitem__("originMaximumHierarchyReadMs", 2101),
                value.__setitem__("maximumHierarchyReadMs", 2101),
            ),
            "traversal-maximum-exceeds-traversal-sum": lambda value: value.__setitem__(
                "maximumHierarchyReadMs", 700
            ),
            "origin-maximum-below-rounded-average": lambda value: value.__setitem__(
                "originMaximumHierarchyReadMs", 524
            ),
            "traversal-maximum-below-rounded-average": lambda value: value.update({
                "originHierarchyElapsedMs": 300,
                "originMaximumHierarchyReadMs": 100,
                "hierarchyElapsedMs": 900,
                "maximumHierarchyReadMs": 199,
            }),
            "zero-origin-reads-with-nonzero-sum": lambda value: value.update({
                "reusedInitialScreen": False,
                "originElapsedMs": 1,
                "originReverseSwipes": 0,
                "originEmptyHierarchyReads": 0,
                "originHierarchyReadCount": 0,
                "originHierarchyElapsedMs": 1,
                "originMaximumHierarchyReadMs": 1,
            }),
            "zero-traversal-reads-with-nonzero-sum": lambda value: value.update({
                "hierarchyReadCount": 4,
                "hierarchyElapsedMs": 2101,
            }),
        }
        for case, mutate in mutations.items():
            forged = dict(receipt)
            mutate(forged)
            with self.subTest(case=case), self.assertRaisesRegex(
                RuntimeError,
                "Composed accessibility scan timing",
            ):
                driver.require_composed_scan_timing(forged)

    def test_every_forward_terminal_status_requires_composed_timing(self) -> None:
        for status in sorted(driver.COMPOSED_SCAN_FORWARD_STATUSES):
            with self.subTest(status=status), self.assertRaisesRegex(
                RuntimeError,
                "Composed accessibility scan timing omitted fields",
            ):
                driver.require_composed_scan_timing({
                    "scanId": "rank-cardinality-heritage",
                    "status": status,
                })

    def test_realistic_hosted_rank_scan_timing_reconciles(self) -> None:
        receipt: dict[str, object] = {
            "scanId": "rank-cardinality-heritage",
            "status": "stable-end",
            "reusedInitialScreen": True,
            "originElapsedMs": 11000,
            "originReverseSwipes": 0,
            "originEmptyHierarchyReads": 3,
            "originHierarchyReadCount": 4,
            "originHierarchyElapsedMs": 8600,
            "originMaximumHierarchyReadMs": 2200,
            "traversalElapsedMs": 7100,
            "traversalEmptyHierarchyReads": 0,
            "emptyHierarchyReads": 3,
            "totalNavigationSwipes": 3,
            "hierarchyReadCount": 7,
            "hierarchyElapsedMs": 15000,
            "maximumHierarchyReadMs": 2250,
            "elapsedMs": 18100,
            "swipes": 3,
        }

        driver.require_composed_scan_timing(receipt)

    def test_noncomposed_poll_and_reverse_scan_observations_remain_separate(self) -> None:
        observations = (
            {
                "scanId": "dialog-transition-poll",
                "status": "resolved",
                "emptyHierarchyReads": 0,
                "hierarchyReadCount": 1,
                "hierarchyElapsedMs": 400,
                "maximumHierarchyReadMs": 400,
                "elapsedMs": 401,
            },
            {
                "scanId": "stable-start",
                "status": "stable-start",
                "screens": 3,
                "swipes": 2,
                "hierarchyReadCount": 3,
                "hierarchyElapsedMs": 1200,
                "maximumHierarchyReadMs": 400,
                "elapsedMs": 1600,
            },
            {
                "scanId": "authority-option-start-human",
                "status": "stable-start-bound-exhausted",
                "screens": 2,
                "swipes": 1,
                "hierarchyReadCount": 2,
                "hierarchyElapsedMs": 800,
                "maximumHierarchyReadMs": 400,
                "elapsedMs": 1000,
            },
        )

        for observation in observations:
            with self.subTest(scan_id=observation["scanId"]):
                driver.require_composed_scan_timing(observation)

    def test_reused_scan_origin_must_be_nonempty(self) -> None:
        node = driver.shared.UiNode({"resource-id": "rank-origin"})
        invalid_origins = (
            self.priority_rank_origin([], hierarchy_durations_ms=(1,)),
            self.priority_rank_origin([node], reverse_swipes=9),
            self.priority_rank_origin(
                [node], reverse_swipes=8, hierarchy_durations_ms=(1,)
            ),
            self.priority_rank_origin(
                [node], elapsed_ms=1, hierarchy_durations_ms=(20,)
            ),
            self.priority_rank_origin(
                [node], hierarchy_durations_ms=(1,), empty_hierarchy_reads=2
            ),
        )
        for invalid_origin in invalid_origins:
            with self.subTest(invalid_origin=invalid_origin), \
                 self.assertRaisesRegex(ValueError, "exact nonnegative timing"):
                driver.scan_forward_until_stable(
                    mock.Mock(),
                    scan_id="invalid-reused-origin",
                    max_scrolls=8,
                    distance_ratio=0.68,
                    initial_observation=invalid_origin,
                )

    def test_reused_scan_origin_rejects_bool_and_float_numeric_fields(self) -> None:
        node = driver.shared.UiNode({"resource-id": "rank-origin"})
        invalid_fields = (
            (
                "reverse_swipes-bool",
                self.priority_rank_origin([node], reverse_swipes=True),
            ),
            (
                "reverse_swipes-float",
                self.priority_rank_origin([node], reverse_swipes=1.0),
            ),
            (
                "elapsed_ms-bool",
                self.priority_rank_origin([node], elapsed_ms=True),
            ),
            (
                "elapsed_ms-float",
                self.priority_rank_origin([node], elapsed_ms=7.0),
            ),
            (
                "empty_hierarchy_reads-bool",
                self.priority_rank_origin([node], empty_hierarchy_reads=False),
            ),
            (
                "empty_hierarchy_reads-float",
                self.priority_rank_origin([node], empty_hierarchy_reads=0.0),
            ),
            (
                "hierarchy_duration-bool",
                self.priority_rank_origin(
                    [node], hierarchy_durations_ms=(True,)
                ),
            ),
            (
                "hierarchy_duration-float",
                self.priority_rank_origin(
                    [node], hierarchy_durations_ms=(5.0,)
                ),
            ),
        )
        for field, invalid_origin in invalid_fields:
            with self.subTest(field=field), self.assertRaisesRegex(
                ValueError,
                "exact nonnegative timing",
            ):
                driver.scan_forward_until_stable(
                    mock.Mock(),
                    scan_id="invalid-origin-numeric-type",
                    max_scrolls=8,
                    distance_ratio=0.68,
                    initial_observation=invalid_origin,
                )

    def test_stable_end_scan_fails_closed_when_the_bound_never_proves_an_end(self) -> None:
        class MovingDevice:
            reads = 0
            up = 0
            captures: list[str] = []

            def hierarchy(self):
                self.reads += 1
                return [
                    driver.shared.UiNode(
                        {
                            "resource-id": f"moving-row-{self.reads}",
                            "bounds": "[0,0][1,1]",
                        }
                    )
                ]

            def swipe_up(self, **_options: object) -> None:
                self.up += 1

            def capture(self, name: str) -> None:
                self.captures.append(name)

        device = MovingDevice()
        observations: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "did not prove a stable page end",
        ):
            driver.scan_forward_until_stable(
                device,
                scan_id="moving-proof",
                max_scrolls=2,
                distance_ratio=0.22,
                observer=observations.append,
            )

        self.assertEqual(3, device.reads)
        self.assertEqual(2, device.up)
        self.assertEqual(["moving-proof-stable-end-unproven"], device.captures)
        self.assertEqual("bound-exhausted", observations[0]["status"])

    def test_stable_end_scan_retries_empty_hierarchies_without_advancing(self) -> None:
        stable = [driver.shared.UiNode({"resource-id": "stable-row", "bounds": "[0,0][1,1]"})]

        class TransientDevice:
            reads = 0
            up = 0

            def hierarchy(self):
                self.reads += 1
                return [] if self.reads == 1 else stable

            def swipe_up(self, **_options: object) -> None:
                self.up += 1

            @staticmethod
            def capture(name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = TransientDevice()
        observations: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"):
            screens = driver.scan_forward_until_stable(
                device,
                scan_id="transient-empty",
                max_scrolls=2,
                distance_ratio=0.22,
                observer=observations.append,
            )

        self.assertEqual(3, len(screens))
        self.assertEqual(2, device.up)
        self.assertEqual(1, observations[0]["emptyHierarchyReads"])

    def test_stable_end_scan_fails_closed_on_repeated_empty_hierarchies(self) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = []
        observations: list[dict[str, object]] = []

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "exhausted transient empty hierarchy reads",
        ):
            driver.scan_forward_until_stable(
                device,
                scan_id="empty-proof",
                max_scrolls=2,
                distance_ratio=0.22,
                max_consecutive_empty_reads=1,
                observer=observations.append,
            )

        device.swipe_up.assert_not_called()
        device.capture.assert_called_once_with("empty-proof-empty-hierarchy-exhausted")
        self.assertEqual("empty-hierarchy-exhausted", observations[0]["status"])

    def test_progress_recorder_writes_ordered_atomic_timing_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with mock.patch("builtins.print") as emit:
                progress = driver.ProgressRecorder(root)
                for phase_id in driver.PHASE_ORDER:
                    progress.advance(phase_id)
                    if phase_id == "initial-navigation":
                        for milestone_id in driver.INITIAL_NAVIGATION_MILESTONE_ORDER:
                            progress.record_initial_milestone(milestone_id)
                    if phase_id == "initial-authority":
                        for milestone_id in driver.INITIAL_AUTHORITY_MILESTONE_ORDER:
                            progress.record_initial_milestone(milestone_id)
                    if phase_id == "dashboard-proof":
                        for milestone_id in driver.DASHBOARD_PROOF_MILESTONE_ORDER:
                            progress.record_initial_milestone(milestone_id)
                    if phase_id == "priority-ranks":
                        progress.record_scan(
                            {
                                "scanId": "rank-cardinality-heritage",
                                "status": "stable-end",
                                "screens": 4,
                                "swipes": 3,
                                "configuredMaxScrolls": 22,
                                "stableRepeats": 2,
                                "reusedInitialScreen": False,
                                "originElapsedMs": 0,
                                "originReverseSwipes": 0,
                                "originEmptyHierarchyReads": 0,
                                "originHierarchyReadCount": 0,
                                "originHierarchyElapsedMs": 0,
                                "originMaximumHierarchyReadMs": 0,
                                "traversalElapsedMs": 1200,
                                "traversalEmptyHierarchyReads": 0,
                                "emptyHierarchyReads": 0,
                                "totalNavigationSwipes": 3,
                                "hierarchyReadCount": 4,
                                "hierarchyElapsedMs": 800,
                                "maximumHierarchyReadMs": 200,
                                "elapsedMs": 1200,
                            }
                        )
                snapshot = progress.finish()

            evidence = json.loads(progress.evidence_path.read_text(encoding="utf-8"))
            event_log = [
                json.loads(line)
                for line in progress.events_path.read_text(encoding="utf-8").splitlines()
            ]
            self.assertEqual(snapshot, evidence)
            self.assertEqual(
                "chummer.android.creation-prerequisite-progress/v2",
                evidence["schema"],
            )
            self.assertEqual("timing-complete", evidence["status"])
            self.assertEqual(list(driver.PHASE_ORDER), [
                phase["phaseId"] for phase in evidence["phases"]
            ])
            self.assertEqual(list(driver.PHASE_BUDGET_MS), [
                phase["phaseId"] for phase in evidence["phases"]
            ])
            self.assertEqual("rank-cardinality-heritage", evidence["scans"][0]["scanId"])
            self.assertEqual(
                list(driver.INITIAL_MILESTONE_ORDER),
                [milestone["milestoneId"] for milestone in evidence["milestones"]],
            )
            self.assertEqual(
                [
                    *(["initial-navigation"] * len(driver.INITIAL_NAVIGATION_MILESTONE_ORDER)),
                    *(["initial-authority"] * len(driver.INITIAL_AUTHORITY_MILESTONE_ORDER)),
                    *(["dashboard-proof"] * len(driver.DASHBOARD_PROOF_MILESTONE_ORDER)),
                ],
                [milestone["phaseId"] for milestone in evidence["milestones"]],
            )
            self.assertTrue(all(
                milestone["segmentElapsedMs"] >= 0
                for milestone in evidence["milestones"]
            ))
            self.assertEqual(driver.TOTAL_PERFORMANCE_TARGET_MS, evidence["configuredTotalTargetMs"])
            self.assertFalse((root / f".{driver.PROGRESS_FILE_NAME}.tmp").exists())
            self.assertFalse((root / f".{driver.PROGRESS_EVENTS_FILE_NAME}.tmp").exists())
            self.assertEqual("phase-start", event_log[0]["event"])
            self.assertEqual("timing-complete", event_log[-1]["event"])
            self.assertEqual(len(progress.events), len(event_log))
            events = [call.args[0] for call in emit.call_args_list]
            self.assertTrue(any('"event": "phase-start"' in event for event in events))
            self.assertTrue(any('"event": "timing-complete"' in event for event in events))
            self.assertNotIn('"executionStatus": "pass"', progress.evidence_path.read_text())

    def test_progress_recorder_rejects_out_of_order_or_incomplete_phases(self) -> None:
        with tempfile.TemporaryDirectory() as temporary, mock.patch("builtins.print"):
            progress = driver.ProgressRecorder(Path(temporary))
            with self.assertRaisesRegex(RuntimeError, "Expected prerequisite progress phase"):
                progress.advance("authority-inventory")
            progress.advance(driver.PHASE_ORDER[0])
            with self.assertRaisesRegex(RuntimeError, "progress is incomplete"):
                progress.finish()

    def test_progress_recorder_rejects_a_pass_phase_outside_its_budget(self) -> None:
        with tempfile.TemporaryDirectory() as temporary, mock.patch("builtins.print"):
            progress = driver.ProgressRecorder(Path(temporary))
            progress.advance("device-preflight-install")
            progress.advance("initial-navigation")
            progress.advance("initial-authority")
            progress._active_started -= (
                driver.PHASE_BUDGET_MS["initial-authority"] / 1000
            ) + 1

            with self.assertRaisesRegex(RuntimeError, "explicit phase timing budget"):
                progress.advance("dashboard-proof")

            self.assertTrue(any(
                phase["phaseId"] == "initial-authority"
                and phase["status"] == "pass"
                and phase["withinBudget"] is False
                for phase in progress.phases
            ))

    def test_progress_recorder_rejects_out_of_order_or_wrong_phase_milestones(self) -> None:
        with tempfile.TemporaryDirectory() as temporary, mock.patch("builtins.print"):
            progress = driver.ProgressRecorder(Path(temporary))
            progress.advance("device-preflight-install")
            with self.assertRaisesRegex(RuntimeError, "outside its active phase"):
                progress.record_initial_milestone(
                    driver.INITIAL_NAVIGATION_MILESTONE_ORDER[0]
                )
            progress.advance("initial-navigation")
            with self.assertRaisesRegex(RuntimeError, "Expected initial milestone"):
                progress.record_initial_milestone(
                    driver.INITIAL_NAVIGATION_MILESTONE_ORDER[1]
                )

    def test_progress_finish_requires_exact_complete_milestone_evidence(self) -> None:
        cases = ("missing", "duplicate", "reordered", "wrongPhase", "wrongOrdinal")
        for case in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temporary, mock.patch(
                "builtins.print"
            ):
                progress = driver.ProgressRecorder(Path(temporary))
                for phase_id in driver.PHASE_ORDER:
                    progress.advance(phase_id)
                    for milestone_id, milestone_phase in zip(
                        driver.INITIAL_MILESTONE_ORDER,
                        driver.INITIAL_MILESTONE_PHASES,
                        strict=True,
                    ):
                        if milestone_phase == phase_id:
                            progress.record_initial_milestone(milestone_id)
                if case == "missing":
                    progress.milestones.pop()
                elif case == "duplicate":
                    progress.milestones.append(dict(progress.milestones[-1]))
                elif case == "reordered":
                    progress.milestones[0], progress.milestones[1] = (
                        progress.milestones[1],
                        progress.milestones[0],
                    )
                elif case == "wrongPhase":
                    progress.milestones[3]["phaseId"] = "dashboard-proof"
                else:
                    progress.milestones[3]["ordinal"] = 99

                with self.assertRaisesRegex(RuntimeError, "milestone evidence differs"):
                    progress.finish()

    def test_progress_finish_rejects_cross_field_timing_forgery(self) -> None:
        cases = (
            ("phaseOverSum", "phase elapsed time exceeds"),
            ("milestoneTotalZero", "milestone timing differs"),
            ("phaseBoolOrdinal", "phase evidence differs"),
            ("milestoneBoolOrdinal", "milestone evidence differs"),
        )
        for case, expected_error in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temporary, mock.patch(
                "builtins.print"
            ):
                progress = driver.ProgressRecorder(Path(temporary))
                for phase_id in driver.PHASE_ORDER:
                    progress.advance(phase_id)
                    for milestone_id, milestone_phase in zip(
                        driver.INITIAL_MILESTONE_ORDER,
                        driver.INITIAL_MILESTONE_PHASES,
                        strict=True,
                    ):
                        if milestone_phase == phase_id:
                            progress.record_initial_milestone(milestone_id)
                if case == "phaseOverSum":
                    for phase in progress.phases:
                        phase["elapsedMs"] = phase["budgetMs"]
                elif case == "milestoneTotalZero":
                    progress.started -= 1.0
                    progress.phases[0]["elapsedMs"] = 20
                    progress.phases[1]["elapsedMs"] = 20
                    first = progress.milestones[0]
                    first["phaseElapsedMs"] = 20
                    first["segmentElapsedMs"] = 20
                    first["totalElapsedMs"] = 0
                elif case == "phaseBoolOrdinal":
                    progress.phases[0]["ordinal"] = True
                else:
                    progress.milestones[0]["ordinal"] = True

                with self.assertRaisesRegex(RuntimeError, expected_error):
                    progress.finish()

    def test_creation_timing_uses_three_strict_nonoverlapping_phases(self) -> None:
        source = inspect.getsource(driver.execute)
        navigation_start = source.index('progress.advance("initial-navigation")')
        cold_launch = source.index("initial_launch = shared.launch_app(device)")
        dialog_ready = source.index(
            'progress.record_initial_milestone("dialog-acquisition-complete")'
        )
        authority_start = source.index('progress.advance("initial-authority")')
        explicit_tap = source.index(
            'device.shell(\n        "input",\n        "tap",',
            authority_start,
        )
        timing_capture = source.index("creation_bootstrap_timing = capture_creation_bootstrap_timing(")
        transaction_ready = source.index(
            'progress.record_initial_milestone("create-bootstrap-transaction-complete")'
        )
        dashboard_start = source.index('progress.advance("dashboard-proof")')
        visible_dashboard = source.index("require_initial_creation_dashboard_snapshot(")
        dashboard_ready = source.index(
            'progress.record_initial_milestone("dashboard-render-complete")'
        )
        inventory_start = source.index('progress.advance("authority-inventory")')

        self.assertEqual(60_000, driver.PHASE_BUDGET_MS["initial-navigation"])
        self.assertEqual(90_000, driver.PHASE_BUDGET_MS["initial-authority"])
        self.assertEqual(30_000, driver.PHASE_BUDGET_MS["dashboard-proof"])
        self.assertLess(navigation_start, cold_launch)
        self.assertLess(cold_launch, dialog_ready)
        self.assertLess(dialog_ready, authority_start)
        self.assertLess(authority_start, explicit_tap)
        self.assertLess(explicit_tap, timing_capture)
        self.assertLess(timing_capture, transaction_ready)
        self.assertLess(transaction_ready, dashboard_start)
        self.assertLess(dashboard_start, visible_dashboard)
        self.assertLess(visible_dashboard, dashboard_ready)
        self.assertLess(dashboard_ready, inventory_start)

    def test_creation_karma_budget_cards_expose_readable_semantic_totals(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationPrerequisitePreviewPage.cs").read_text(encoding="utf-8")
        for source, automation_id in (
            (page, "creation-prerequisite-karma-budget"),
            (preview, "creation-prerequisite-preview-karma-budget"),
        ):
            self.assertIn(f'border.AutomationId = "{automation_id}"', source)
            self.assertIn("SemanticProperties.SetDescription(", source)
            self.assertIn('"Priority.Karma.Semantic"', source)
            self.assertIn(
                '"Global Creation Karma. Total {0}. Used {1}. Remaining {2}."',
                source,
            )

    def test_source_authority_labels_keep_full_width_beside_long_digests(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")

        for key, label, automation_id in (
            ("Priority.Source.AuthorityDigest", "Authority digest", "creation-prerequisite-authority-digest"),
            ("Priority.Source.ProfileInputs", "Profile inputs", "creation-prerequisite-profile-inputs-digest"),
            ("Priority.Source.PrioritiesXml", "Priorities XML", "creation-prerequisite-priorities-xml-digest"),
        ):
            self.assertIn(
                f'WizardStrings.Get("{key}", "{label}"),',
                page,
            )
            self.assertIn(f'"{automation_id}"));', page)

        helper = page[page.index("private static VerticalStackLayout SourceAuthorityMetric") :]
        helper = helper[: helper.index("private void AddActions")]
        self.assertIn('labelView.AutomationId = $"{automationId}-label";', helper)
        self.assertIn("valueView.AutomationId = automationId;", helper)
        self.assertIn("valueView.LineBreakMode = LineBreakMode.CharacterWrap;", helper)
        self.assertNotIn("NativeTheme.Metric", helper)

    def test_readable_digest_prefix_is_canonical_and_twelve_hex_characters(self) -> None:
        helper = (NATIVE / "CreationPrerequisiteDigestText.cs").read_text(encoding="utf-8")
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationPrerequisitePreviewPage.cs").read_text(encoding="utf-8")
        for marker in (
            "CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(digest)",
            'private const string Sha256Prefix = "sha256:"',
            "private const int DisplayHexLength = 12",
            "digest![Sha256Prefix.Length..(Sha256Prefix.Length + DisplayHexLength)]",
            'return "unavailable"',
        ):
            self.assertIn(marker, helper)
        self.assertIn("CreationPrerequisiteDigestText.CanonicalPrefix(digest)", page)
        self.assertIn("CreationPrerequisiteDigestText.CanonicalPrefix(digest)", preview)
        self.assertNotIn("digest[..Math.Min(12, digest.Length)]", page)
        self.assertNotIn("digest[..Math.Min(12, digest.Length)]", preview)

    @staticmethod
    def authority_option_node(resource_id: str, label: str) -> driver.shared.UiNode:
        return driver.shared.UiNode(
            {
                "resource-id": f"com.myexternalbrain.chummer:id/{resource_id}",
                "text": "",
                "content-desc": f"{label}. Core-projected option",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[0,0][100,100]",
            }
        )

    @staticmethod
    def talent_grant_nodes(
        *,
        kind: str = "Active skills",
        option_id: str = "choice-0001",
        selected: bool = True,
        completion_enabled: bool = True,
    ) -> list[driver.shared.UiNode]:
        prefix = driver.TALENT_GRANT_OPTION_PREFIX[kind]
        selected_count = 1 if selected else 0
        return [
            driver.shared.UiNode(
                {
                    "resource-id": "creation-prerequisite-talent-grant-authority",
                    "content-desc": f"Required. {selected_count} / 1 {kind}",
                    "bounds": "[0,0][100,100]",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": "creation-prerequisite-talent-grant-digest",
                    "text": "sha256:" + ("a" * 64),
                    "bounds": "[0,100][100,200]",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": prefix + option_id,
                    "content-desc": ("✓ " if selected else "") + "Arcana",
                    "enabled": "true",
                    "clickable": "true",
                    "bounds": "[0,200][100,300]",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": "creation-prerequisite-talent-grant-complete",
                    "text": "Continue with exact grant" if completion_enabled else "Choose 1 more",
                    "enabled": "true" if completion_enabled else "false",
                    "clickable": "true",
                    "bounds": "[0,300][100,400]",
                }
            ),
        ]

    def test_talent_grant_surface_binds_exact_cardinality_digest_and_selected_ids(self) -> None:
        nodes = self.talent_grant_nodes()

        class GrantDevice:
            up = 0
            down = 0

            @staticmethod
            def wait_for_single_exact_resource_id(*_arguments, **_options):
                return nodes[0]

            @staticmethod
            def hierarchy():
                return nodes

            def swipe_up(self, **_options: object) -> None:
                self.up += 1

            def swipe_down(self, **_options: object) -> None:
                self.down += 1

            @staticmethod
            def node_has_tappable_bounds(node) -> bool:
                return bool(node.attributes.get("bounds"))

            @staticmethod
            def capture(name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = GrantDevice()
        navigation: dict[str, object] = {}
        with mock.patch.object(driver.time, "sleep"):
            surface = driver.read_talent_grant_surface(
                device,
                "Active skills",
                max_scrolls=2,
                navigation_out=navigation,
            )

        self.assertEqual("Active skills", surface.kind)
        self.assertEqual(1, surface.selected_count)
        self.assertEqual(1, surface.required_count)
        self.assertEqual("sha256:" + ("a" * 64), surface.grant_digest)
        self.assertEqual(
            ("creation-prerequisite-talent-active-skill-option-choice-0001",),
            surface.option_ids,
        )
        self.assertEqual(surface.option_ids, surface.selected_option_ids)
        self.assertTrue(surface.completion_enabled)
        self.assertEqual(2, device.up)
        self.assertEqual(2, device.down)
        self.assertEqual(0, navigation["endViewport"])
        self.assertEqual(
            0,
            navigation["resourceViewports"][
                "creation-prerequisite-talent-grant-complete"
            ],
        )
        self.assertEqual(
            ("Arcana",),
            navigation["resourceDetails"][
                "creation-prerequisite-talent-active-skill-option-choice-0001"
            ],
        )

    def test_talent_grant_surface_rejects_malformed_or_opposite_kind_ids(self) -> None:
        malformed = self.talent_grant_nodes(option_id="forged-")
        opposite = self.talent_grant_nodes()
        opposite.append(
            driver.shared.UiNode(
                {
                    "resource-id": (
                        "creation-prerequisite-talent-skill-group-option-forged"
                    ),
                    "enabled": "true",
                    "clickable": "true",
                    "bounds": "[0,400][100,500]",
                }
            )
        )

        for nodes, expected in ((malformed, "malformed"), (opposite, "opposite")):
            with self.subTest(expected=expected):
                device = mock.Mock()
                device.hierarchy.return_value = nodes
                with mock.patch.object(driver.shared, "reset_scroll_to_top"), \
                     mock.patch.object(driver.time, "sleep"), \
                     self.assertRaisesRegex(RuntimeError, expected):
                    driver.read_talent_grant_surface(
                        device,
                        "Active skills",
                        max_scrolls=2,
                    )
                device.capture.assert_called_once_with(
                    "creation-prerequisite-talent-grant-authority-invalid"
                )

    def test_talent_grant_surface_rejects_selected_but_disabled_choice(self) -> None:
        nodes = self.talent_grant_nodes()
        nodes[2].attributes["enabled"] = "false"
        device = mock.Mock()
        device.hierarchy.return_value = nodes
        with mock.patch.object(driver.shared, "reset_scroll_to_top"), \
             mock.patch.object(driver.time, "sleep"), \
             self.assertRaisesRegex(RuntimeError, "count did not match"):
            driver.read_talent_grant_surface(
                device,
                "Active skills",
                max_scrolls=2,
            )
        device.capture.assert_called_once_with(
            "creation-prerequisite-talent-grant-cardinality-invalid"
        )

    def test_grouped_talent_state_reacquires_the_hosted_artifact_topology(self) -> None:
        selected_id = (
            "creation-prerequisite-talent-active-skill-option-"
            "89ee1730-053a-400f-a13a-4fbadae015f0"
        )
        disabled_id = (
            "creation-prerequisite-talent-active-skill-option-"
            "cd9f6bf7-fa48-464b-9a8f-c7ce26713a72"
        )
        selected_detail = (
            "Survival. Physical Active · Group Outdoors · Attribute WIL · "
            "Source 89ee1730-053a-400f-a13a-4fbadae015f0 · Anchors "
            "skills.xml#skill:89ee1730-053a-400f-a13a-4fbadae015f0"
        )
        disabled_detail = (
            "Clubs. Combat Active · Group Close Combat · Attribute AGI · "
            "Source cd9f6bf7-fa48-464b-9a8f-c7ce26713a72 · Anchors "
            "skills.xml#skill:cd9f6bf7-fa48-464b-9a8f-c7ce26713a72"
        )
        digest = "sha256:" + ("a" * 64)
        baseline = driver.TalentGrantSurface(
            kind="Active skills",
            selected_count=0,
            required_count=1,
            grant_digest=digest,
            option_ids=(selected_id, disabled_id),
            enabled_option_ids=(selected_id, disabled_id),
            selected_option_ids=(),
            completion_enabled=False,
        )
        navigation = {
            "endViewport": 6,
            "resourceViewports": {
                "creation-prerequisite-talent-grant-authority": 0,
                "creation-prerequisite-talent-grant-digest": 0,
                selected_id: 4,
                disabled_id: 4,
                "creation-prerequisite-talent-grant-complete": 6,
            },
            "resourceDetails": {
                selected_id: (selected_detail,),
                disabled_id: (disabled_detail,),
            },
        }

        disabled = driver.shared.UiNode(
            {
                "resource-id": disabled_id,
                "content-desc": disabled_detail,
                "enabled": "false",
                "clickable": "true",
                "bounds": "[98,467][984,695]",
            }
        )
        selected = driver.shared.UiNode(
            {
                "resource-id": selected_id,
                "content-desc": "✓ " + selected_detail,
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,653][984,881]",
            }
        )
        authority = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-talent-grant-authority",
                "content-desc": "Required. 1 / 1 Active skills",
            }
        )
        digest_node = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-talent-grant-digest",
                "text": digest,
            }
        )
        completion = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-talent-grant-complete",
                "text": "Continue with exact grant",
                "enabled": "true",
                "clickable": "true",
            }
        )

        class ArtifactTopologyDevice:
            def __init__(self) -> None:
                # The hosted failure's first two fresh snapshots remain in the
                # middle of disabled catalog rows after the measured rewind.
                self.screens = [
                    [disabled],
                    [disabled],
                    [authority, digest_node],
                    [disabled, selected],
                    [completion],
                ]
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.forward_swipes = 0

            def hierarchy(self):
                nodes = self.screens[self.hierarchy_reads]
                self.hierarchy_reads += 1
                return nodes

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.reverse_swipes += 1

            def swipe_up(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.forward_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is selected

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = ArtifactTopologyDevice()
        state, viewport = driver.read_talent_grant_grouped_state(
            device,
            "Active skills",
            baseline,
            navigation,
            6,
            expected_selected_option_ids=(selected_id,),
            expected_completion_enabled=True,
            evidence_prefix="artifact-complete",
        )

        self.assertEqual(
            driver.TalentGrantMutableState(1, (selected_id,), True),
            state,
        )
        self.assertEqual(6, viewport)
        self.assertEqual(5, device.hierarchy_reads)
        self.assertEqual(8, device.reverse_swipes)
        self.assertEqual(6, device.forward_swipes)

    def test_grouped_talent_reacquisition_accepts_the_last_scan_bound(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        target = driver.shared.UiNode({"resource-id": resource_id})

        class LastBoundDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.reverse_swipes = 0

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [target] if self.hierarchy_reads == 4 else [driver.shared.UiNode({})]

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.reverse_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = LastBoundDevice()
        snapshot = driver.reacquire_exact_talent_state_group(
            device,
            (resource_id,),
            3,
            0,
            3,
            evidence_prefix="last-bound",
        )

        self.assertIs(target, snapshot.resources[resource_id])
        self.assertEqual(0, snapshot.logical_viewport)
        self.assertEqual(3, snapshot.reverse_reacquisition_swipes)
        self.assertEqual([target], snapshot.nodes)
        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual(6, device.reverse_swipes)

    def test_grouped_talent_reacquisition_rejects_one_beyond_scan_bound(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"

        class OneBeyondDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                if self.hierarchy_reads == 5:
                    return [driver.shared.UiNode({"resource-id": resource_id})]
                return [driver.shared.UiNode({})]

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.reverse_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = OneBeyondDevice()
        with self.assertRaisesRegex(
            RuntimeError,
            "scan-proven 3-swipe reverse compensation bound",
        ):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                3,
                0,
                3,
                evidence_prefix="one-beyond",
            )

        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual(6, device.reverse_swipes)
        self.assertEqual(["one-beyond-unavailable"], device.captures)

    def test_grouped_talent_reacquisition_rejects_duplicate_exact_id(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        duplicate = driver.shared.UiNode({"resource-id": resource_id})
        device = mock.Mock()
        device.hierarchy.return_value = [duplicate, duplicate]

        with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                1,
                0,
                1,
                evidence_prefix="duplicate",
            )

        device.swipe_down.assert_called_once_with(distance_ratio=0.68)
        device.capture.assert_called_once_with("duplicate-cardinality-invalid")

    def test_exact_measured_talent_tap_reacquires_after_reverse_geometry_drift(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-choice-0001"
        disabled = driver.shared.UiNode(
            {
                "resource-id": (
                    "creation-prerequisite-talent-active-skill-option-disabled"
                ),
                "enabled": "false",
                "clickable": "true",
            }
        )
        target = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "content-desc": "Arcana",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,467][984,695]",
            }
        )
        device = mock.Mock()
        device.hierarchy.side_effect = [[disabled], [target]]
        device.dismiss_system_ui_anr.return_value = False
        device.node_has_tappable_bounds.return_value = True
        navigation = {
            "endViewport": 3,
            "resourceViewports": {resource_id: 0},
        }

        viewport = driver.tap_exact_measured_talent_resource(
            device,
            resource_id,
            navigation,
            3,
            evidence_prefix="measured-tap",
        )

        self.assertEqual(0, viewport)
        self.assertEqual(2, device.hierarchy.call_count)
        self.assertEqual(
            [
                mock.call(distance_ratio=0.68),
                mock.call(distance_ratio=0.68),
                mock.call(distance_ratio=0.68),
                mock.call(distance_ratio=0.22),
            ],
            device.swipe_down.call_args_list,
        )
        device.shell.assert_called_once_with("input", "tap", "541", "581")
        device.capture.assert_not_called()

    def test_grouped_talent_reacquisition_keeps_retry_budgets_separate(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        target = driver.shared.UiNode({"resource-id": resource_id})
        overlay = driver.shared.UiNode({"content-desc": "system ui"})
        device = mock.Mock()
        device.hierarchy.side_effect = [[], [overlay], [target]]
        device.dismiss_system_ui_anr.side_effect = [True]

        with mock.patch.object(driver.time, "sleep"):
            snapshot = driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                1,
                0,
                1,
                evidence_prefix="separate-retries",
                max_empty_hierarchy_reads=1,
                max_system_ui_dismissals=1,
            )

        self.assertIs(target, snapshot.resources[resource_id])
        self.assertEqual(0, snapshot.logical_viewport)
        self.assertEqual(0, snapshot.reverse_reacquisition_swipes)
        self.assertEqual(3, device.hierarchy.call_count)
        device.swipe_down.assert_called_once_with(distance_ratio=0.68)
        device.capture.assert_not_called()

    def test_grouped_talent_state_rejects_unmeasured_navigation_topology(self) -> None:
        option_id = "creation-prerequisite-talent-active-skill-option-choice-0001"
        baseline = driver.TalentGrantSurface(
            kind="Active skills",
            selected_count=0,
            required_count=1,
            grant_digest="sha256:" + ("a" * 64),
            option_ids=(option_id,),
            enabled_option_ids=(option_id,),
            selected_option_ids=(),
            completion_enabled=False,
        )
        navigation = {
            "endViewport": 2,
            "resourceViewports": {
                "creation-prerequisite-talent-grant-authority": 0,
                "creation-prerequisite-talent-grant-digest": 0,
                option_id: 3,
                "creation-prerequisite-talent-grant-complete": 2,
            },
            "resourceDetails": {option_id: ("Arcana",)},
        }
        device = mock.Mock()

        with self.assertRaisesRegex(RuntimeError, "exact bounded scan topology"):
            driver.read_talent_grant_grouped_state(
                device,
                "Active skills",
                baseline,
                navigation,
                2,
                expected_selected_option_ids=(option_id,),
                expected_completion_enabled=True,
                evidence_prefix="invalid-topology",
            )

        device.hierarchy.assert_not_called()
        device.swipe_down.assert_not_called()

    def test_talent_navigation_topology_requires_exact_ints_within_zero_to_forty(
        self,
    ) -> None:
        self.assertEqual(
            40,
            driver._validated_talent_navigation_end(
                {"endViewport": 40, "resourceViewports": {"target": 40}},
                40,
            ),
        )
        cases = (
            ({"endViewport": 41, "resourceViewports": {"target": 40}}, 40),
            ({"endViewport": True, "resourceViewports": {"target": 0}}, 0),
            ({"endViewport": 1, "resourceViewports": {"target": True}}, 1),
            ({"endViewport": 1, "resourceViewports": {"target": -1}}, 1),
            ({"endViewport": 1, "resourceViewports": {"target": 2}}, 1),
            ({"endViewport": 1, "resourceViewports": {"target": 1}}, True),
        )
        for navigation, current_viewport in cases:
            with self.subTest(
                navigation=navigation,
                current_viewport=current_viewport,
            ), self.assertRaisesRegex(RuntimeError, "exact bounded scan topology"):
                driver._validated_talent_navigation_end(
                    navigation,
                    current_viewport,
                )

    def test_grouped_talent_state_reuses_catalog_and_fails_closed_on_drift(self) -> None:
        option_id = "creation-prerequisite-talent-active-skill-option-choice-0001"
        digest = "sha256:" + ("a" * 64)
        baseline = driver.TalentGrantSurface(
            kind="Active skills",
            selected_count=0,
            required_count=1,
            grant_digest=digest,
            option_ids=(option_id,),
            enabled_option_ids=(option_id,),
            selected_option_ids=(),
            completion_enabled=False,
        )
        navigation = {
            "endViewport": 0,
            "resourceViewports": {
                "creation-prerequisite-talent-grant-authority": 0,
                "creation-prerequisite-talent-grant-digest": 0,
                option_id: 0,
                "creation-prerequisite-talent-grant-complete": 0,
            },
            "resourceDetails": {option_id: ("Arcana",)},
        }

        def nodes(
            *,
            selected: int = 1,
            required: int = 1,
            kind: str = "Active skills",
            observed_digest: str = digest,
            duplicate: bool = False,
            enabled: bool = True,
            option_detail: str = "Arcana",
            digest_detail: str = "",
        ) -> list[driver.shared.UiNode]:
            option = driver.shared.UiNode(
                {
                    "resource-id": option_id,
                    "content-desc": (
                        ("✓ " if selected else "") + option_detail
                    ),
                    "enabled": str(enabled).lower(),
                    "clickable": "true",
                    "bounds": "[10,100][900,300]",
                }
            )
            result = [
                driver.shared.UiNode(
                    {
                        "resource-id": "creation-prerequisite-talent-grant-authority",
                        "content-desc": f"Required. {selected} / {required} {kind}",
                    }
                ),
                driver.shared.UiNode(
                    {
                        "resource-id": "creation-prerequisite-talent-grant-digest",
                        "text": observed_digest,
                        "content-desc": digest_detail,
                    }
                ),
                option,
                driver.shared.UiNode(
                    {
                        "resource-id": "creation-prerequisite-talent-grant-complete",
                        "enabled": str(selected == required).lower(),
                    }
                ),
            ]
            if duplicate:
                result.append(option)
            return result

        device = mock.Mock()
        device.hierarchy.return_value = nodes()
        device.node_has_tappable_bounds.return_value = True
        state, viewport = driver.read_talent_grant_grouped_state(
            device,
            "Active skills",
            baseline,
            navigation,
            0,
            expected_selected_option_ids=(option_id,),
            expected_completion_enabled=True,
            evidence_prefix="grouped",
        )
        self.assertEqual(driver.TalentGrantMutableState(1, (option_id,), True), state)
        self.assertEqual(0, viewport)

        failures = (
            (nodes(duplicate=True), "cardinality 2"),
            (nodes(observed_digest="sha256:" + ("b" * 64)), "immutable authority"),
            (nodes(digest_detail="not-the-exact-digest"), "immutable authority"),
            (nodes(option_detail="Arcana changed"), "changed exact option detail"),
            (nodes(kind="Skill groups"), "kind or required count"),
            (nodes(required=2), "kind or required count"),
            (nodes(enabled=False), "enabled exact selection"),
        )
        for hierarchy, message in failures:
            with self.subTest(message=message):
                failing = mock.Mock()
                failing.hierarchy.return_value = hierarchy
                failing.node_has_tappable_bounds.return_value = True
                with self.assertRaisesRegex(RuntimeError, message):
                    driver.read_talent_grant_grouped_state(
                        failing,
                        "Active skills",
                        baseline,
                        navigation,
                        0,
                        expected_selected_option_ids=(option_id,),
                        expected_completion_enabled=True,
                        evidence_prefix="grouped",
                    )

        with self.assertRaisesRegex(RuntimeError, "valid catalog partition"):
            driver.read_talent_grant_grouped_state(
                device,
                "Active skills",
                baseline,
                navigation,
                0,
                expected_selected_option_ids=(
                    "creation-prerequisite-talent-active-skill-option-unknown",
                ),
                expected_completion_enabled=True,
                evidence_prefix="grouped",
            )

    def test_talent_choice_uses_one_catalog_scan_and_no_fixed_reset_searches(self) -> None:
        source = inspect.getsource(driver.choose_and_prove_talent_grant)
        self.assertEqual(1, source.count("read_talent_grant_surface("))
        self.assertEqual(4, source.count("read_talent_grant_grouped_state("))
        self.assertNotIn("reset_scroll_to_top", source)
        self.assertNotIn("tap_exact_talent_grant_option", source)
        self.assertNotIn("tap_exact_talent_option", source)
        completion = inspect.getsource(driver.complete_talent_grant_to_prerequisite)
        self.assertIn("tap_exact_measured_talent_resource(", completion)
        self.assertNotIn("backward_scrolls=40", completion)
        self.assertNotIn("forward_scrolls=40", completion)

    def test_talent_choice_runs_one_inventory_then_four_fresh_grouped_states(self) -> None:
        prefix = driver.TALENT_GRANT_OPTION_PREFIX["Active skills"]
        option_ids = tuple(prefix + suffix for suffix in ("a", "b", "c"))
        baseline = driver.TalentGrantSurface(
            kind="Active skills",
            selected_count=0,
            required_count=2,
            grant_digest="sha256:" + ("a" * 64),
            option_ids=option_ids,
            enabled_option_ids=option_ids,
            selected_option_ids=(),
            completion_enabled=False,
        )
        grant_navigation = {
            "endViewport": 5,
            "resourceViewports": {
                **{resource_id: index + 1 for index, resource_id in enumerate(option_ids)},
                "creation-prerequisite-talent-grant-authority": 0,
                "creation-prerequisite-talent-grant-digest": 0,
                "creation-prerequisite-talent-grant-complete": 5,
            },
        }
        talent_option_id = "creation-prerequisite-talent-option-adept"
        talent_navigation = {
            "endViewport": 2,
            "resourceViewports": {talent_option_id: 2},
        }
        chosen = option_ids[:2]
        complete = driver.TalentGrantMutableState(2, chosen, True)
        incomplete = driver.TalentGrantMutableState(1, (chosen[1],), False)
        grouped_states = iter(
            ((complete, 5), (complete, 5), (incomplete, 5), (complete, 5))
        )
        device = mock.Mock()

        def inventory(*_args, navigation_out=None, **_kwargs):
            navigation_out.update(grant_navigation)
            return baseline

        def measured(_device, resource_id, navigation, _current, **_kwargs):
            return int(navigation["resourceViewports"][resource_id])

        with mock.patch.object(
            driver,
            "read_talent_grant_surface",
            side_effect=inventory,
        ) as inventory_scan, mock.patch.object(
            driver,
            "tap_exact_measured_talent_resource",
            side_effect=measured,
        ) as measured_tap, mock.patch.object(
            driver,
            "read_talent_grant_grouped_state",
            side_effect=lambda *_args, **_kwargs: next(grouped_states),
        ) as grouped_scan:
            proof = driver.choose_and_prove_talent_grant(
                device,
                "Active skills",
                talent_option_id,
                talent_navigation,
                scan_id_prefix="active",
            )

        self.assertEqual(1, inventory_scan.call_count)
        self.assertEqual(4, grouped_scan.call_count)
        self.assertEqual(5, measured_tap.call_count)
        self.assertEqual(list(option_ids), proof.receipt["allOptionAutomationIds"])
        self.assertEqual(list(chosen), proof.receipt["selectedOptionAutomationIds"])
        self.assertEqual(5, proof.current_viewport)

    def test_authority_option_collector_rejects_zero_candidates(self) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = [
            driver.shared.UiNode({"resource-id": "unrelated-visible-row"})
        ]
        with self.assertRaisesRegex(RuntimeError, "exactly one enabled authoritative option"):
            driver.tap_enabled_authority_option(
                device,
                "creation-prerequisite-heritage-option-",
                "Human",
                max_scrolls=2,
            )
        device.capture.assert_called_once()

    def test_authority_option_collector_exposes_duplicate_exact_labels(self) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = [
                self.authority_option_node(
                    "creation-prerequisite-heritage-option-one",
                    "Human",
                ),
                self.authority_option_node(
                    "creation-prerequisite-heritage-option-two",
                    "Human",
                ),
            ]
        device.node_has_tappable_bounds.return_value = True
        with self.assertRaisesRegex(RuntimeError, "found 2 unique candidates"):
            driver.tap_enabled_authority_option(
                device,
                "creation-prerequisite-heritage-option-",
                "Human",
                max_scrolls=2,
            )
        device.capture.assert_called_once()

    def test_authority_option_collector_rejects_substring_label(self) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = [
                self.authority_option_node(
                    "creation-prerequisite-heritage-option-metahuman",
                    "Metahuman",
                )
            ]
        device.node_has_tappable_bounds.return_value = True
        with self.assertRaisesRegex(RuntimeError, "found 0 unique candidates"):
            driver.tap_enabled_authority_option(
                device,
                "creation-prerequisite-heritage-option-",
                "Human",
                max_scrolls=2,
            )
        device.capture.assert_called_once()

    def test_authority_option_reacquires_fresh_measured_node_and_rejects_clipped_drift(self) -> None:
        resource_id = "creation-prerequisite-heritage-option-exact"
        visible = self.authority_option_node(resource_id, "Human")
        visible.attributes["bounds"] = "[100,500][900,700]"
        clipped_above = self.authority_option_node(resource_id, "Human")
        clipped_above.attributes["bounds"] = "[100,230][900,232]"

        class AuthorityDevice:
            fresh = visible

            def __init__(self) -> None:
                self.taps: list[tuple[str, ...]] = []
                self.captures: list[str] = []

            def hierarchy(self):
                return [self.fresh]

            @staticmethod
            def display_size():
                return 1080, 2400

            def node_has_tappable_bounds(self, node) -> bool:
                return driver.shared.Device.node_has_tappable_bounds(self, node)

            def shell(self, *arguments: str) -> str:
                self.taps.append(arguments)
                return ""

            def capture(self, name: str) -> None:
                self.captures.append(name)

        device = AuthorityDevice()
        scan = driver.StableViewportScan([[visible]], 0)
        with mock.patch.object(driver, "rewind_to_stable_start"), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=scan,
        ):
            selected = driver.tap_enabled_authority_option(
                device,
                "creation-prerequisite-heritage-option-",
                "Human",
                max_scrolls=2,
            )

        self.assertEqual(resource_id, selected)
        self.assertEqual([("input", "tap", "500", "600")], device.taps)
        self.assertEqual([], device.captures)

        drifted = AuthorityDevice()
        drifted.fresh = clipped_above
        with mock.patch.object(driver, "rewind_to_stable_start"), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=scan,
        ), self.assertRaisesRegex(RuntimeError, "not tappable"):
            driver.tap_enabled_authority_option(
                drifted,
                "creation-prerequisite-heritage-option-",
                "Human",
                max_scrolls=2,
            )
        self.assertEqual([], drifted.taps)
        self.assertEqual(
            ["creation-prerequisite-authority-option-not-tappable"],
            drifted.captures,
        )

    def test_restored_authority_option_rejects_mismatch_and_duplicate(self) -> None:
        with self.assertRaisesRegex(RuntimeError, "did not mark exactly"):
            driver.assert_exact_restored_authority_option_ids(
                {"creation-prerequisite-heritage-option-forged"},
                "creation-prerequisite-heritage-option-exact",
                duplicate_resource_id=False,
            )
        with self.assertRaisesRegex(RuntimeError, "duplicateResourceId=True"):
            driver.assert_exact_restored_authority_option_ids(
                {"creation-prerequisite-heritage-option-exact"},
                "creation-prerequisite-heritage-option-exact",
                duplicate_resource_id=True,
            )

    def test_persisted_authority_rejects_digest_and_revision_drift(self) -> None:
        digest = "sha256:" + "a" * 64
        auxiliary_digest = "a" * 64
        authority = {
            "binding": {"contentRevision": 7, "savedRevision": 3},
            "bindingDigests": {
                "rawCharacterXml": digest,
                "auxiliaryState": auxiliary_digest,
                "authority": digest,
            },
            "draftDigest": digest,
        }
        invalid = (
            ({**authority, "draftDigest": "sha256:" + "b" * 64}, "DraftDigest"),
            (
                {
                    **authority,
                    "bindingDigests": {
                        **authority["bindingDigests"],
                        "auxiliaryState": "b" * 64,
                    },
                },
                "binding digests",
            ),
            ({**authority, "binding": {"contentRevision": 8, "savedRevision": 3}}, "content revision"),
            ({**authority, "binding": {"contentRevision": 7, "savedRevision": 4}}, "saved revision"),
        )
        for actual, message in invalid:
            with self.subTest(message=message), self.assertRaisesRegex(RuntimeError, message):
                driver.assert_persisted_prerequisite_authority(
                    actual,
                    digest,
                    authority["bindingDigests"],
                    7,
                    3,
                )

    def test_auxiliary_state_digest_uses_its_exact_core_wire_grammar(self) -> None:
        device = mock.Mock()
        node = mock.Mock()
        device.wait.return_value = node
        canonical = "a" * 64
        node.attributes = {"text": canonical, "content-desc": ""}
        self.assertEqual(
            canonical,
            driver.canonical_auxiliary_state_digest(device, "auxiliary-digest"),
        )

        for invalid in (
            "sha256:" + canonical,
            "A" * 64,
            "a" * 63,
            "g" * 64,
        ):
            with self.subTest(invalid=invalid):
                node.attributes = {"text": invalid, "content-desc": ""}
                with self.assertRaisesRegex(RuntimeError, "canonical auxiliary-state digest"):
                    driver.canonical_auxiliary_state_digest(device, "auxiliary-digest")

    def test_priority_bootstrap_declares_the_canonical_core_settings_profile(self) -> None:
        self.assertEqual(
            "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
            driver.STANDARD_PRIORITY_SETTINGS_ID,
        )

    def test_prerequisite_method_card_exposes_canonical_build_method_semantics(self) -> None:
        source = (
            REPO / "src" / "Chummer.Android" / "Native" / "CreationPrerequisitePage.cs"
        ).read_text(encoding="utf-8")
        method = source[source.index("private void AddMethod") :]
        method = method[: method.index("private void AddPendingDraft")]

        self.assertIn('border.AutomationId = "creation-prerequisite-method";', method)
        self.assertIn("SemanticProperties.SetDescription(border, state.BuildMethod);", method)

    def test_prerequisite_navigation_uses_exact_bounded_bidirectional_search(self) -> None:
        device = mock.Mock()

        with mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            driver.open_prerequisite(device)

        device.tap_bidirectional.assert_called_once_with(
            "creation-stage-method",
            timeout=180,
            backward_scrolls=0,
            forward_scrolls=8,
            scroll_distance_ratio=0.68,
            exact_resource_id=True,
        )
        device.wait_exact_resource_id_bidirectional.assert_called_once_with(
            "creation-prerequisite-method",
            timeout=90,
            backward_scrolls=0,
            forward_scrolls=4,
            scroll_distance_ratio=0.18,
            evidence_prefix="creation-prerequisite-method",
            surface_name="Creation prerequisite build-method authority",
            require_tappable=False,
        )
        self.assertEqual(
            [mock.call(device, swipes=8), mock.call(device, swipes=4)],
            reset.call_args_list,
        )
        device.tap_until_visible.assert_not_called()

    def test_prerequisite_navigation_proves_route_before_reading_content(self) -> None:
        device = mock.Mock()
        events: list[tuple[str, tuple[object, ...], dict[str, object]]] = []
        device.wait.side_effect = lambda *args, **kwargs: events.append(
            ("wait", args, kwargs)
        )
        device.wait_exact_resource_id_bidirectional.side_effect = (
            lambda *args, **kwargs: events.append(("wait_bidirectional", args, kwargs))
        )

        with mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            reset.side_effect = lambda *args, **kwargs: events.append(
                ("reset", args, kwargs)
            )
            driver.open_prerequisite(device)

        self.assertEqual(
            [
                ("wait", ("creation-prerequisite-page",), {"timeout": 60}),
                ("reset", (device,), {"swipes": 8}),
                (
                    "wait",
                    ("creation-prerequisite-karma-budget",),
                    {"timeout": 60, "scroll": True, "max_scrolls": 22},
                ),
                (
                    "wait_bidirectional",
                    ("creation-prerequisite-method",),
                    {
                        "timeout": 90,
                        "backward_scrolls": 0,
                        "forward_scrolls": 4,
                        "scroll_distance_ratio": 0.18,
                        "evidence_prefix": "creation-prerequisite-method",
                        "surface_name": "Creation prerequisite build-method authority",
                        "require_tappable": False,
                    },
                ),
                ("reset", (device,), {"swipes": 4}),
            ],
            events,
        )
        self.assertEqual(
            [mock.call(device, swipes=8), mock.call(device, swipes=4)],
            reset.call_args_list,
        )

    def test_talent_grant_completion_taps_the_single_checked_exact_node(self) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = [
            driver.shared.UiNode(
                {
                    "resource-id": "creation-prerequisite-talent-grant-complete",
                    "enabled": "true",
                    "clickable": "true",
                    "bounds": "[10,100][900,300]",
                }
            )
        ]
        device.node_has_tappable_bounds.return_value = True
        navigation = {
            "endViewport": 7,
            "resourceViewports": {
                "creation-prerequisite-talent-grant-complete": 7,
            }
        }

        driver.complete_talent_grant_to_prerequisite(device, navigation, 7)

        device.shell.assert_called_once_with(
            "input",
            "tap",
            "455",
            "200",
        )
        device.tap_exact_resource_id_bidirectional.assert_not_called()
        self.assertEqual(
            [
                mock.call(
                    "creation-prerequisite-talent-page",
                    timeout=45,
                    evidence_prefix="creation-prerequisite-talent-after-grant",
                    surface_name="Talent detail route after exact grant completion",
                ),
                mock.call(
                    "creation-prerequisite-page",
                    timeout=45,
                    evidence_prefix="creation-prerequisite-after-talent-grant",
                    surface_name="Creation prerequisite route after Talent grant completion",
                ),
            ],
            device.wait_for_single_exact_resource_id.call_args_list,
        )
        device.back.assert_called_once_with()

    def test_bidirectional_exact_read_can_bind_a_noninteractive_authority_card(self) -> None:
        authority = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-method",
                "enabled": "true",
                "clickable": "false",
                "bounds": "[100,400][900,700]",
            }
        )
        device = mock.Mock()
        device._scroll_x_ratio.return_value = 0.5
        device.hierarchy.return_value = [authority]
        device.node_has_tappable_bounds.return_value = True

        result = driver.shared.Device.wait_exact_resource_id_bidirectional(
            device,
            "creation-prerequisite-method",
            backward_scrolls=0,
            forward_scrolls=0,
            require_tappable=False,
        )

        self.assertIs(authority, result)
        device.capture.assert_not_called()

    def test_wireless_swipe_gestures_fail_fast_instead_of_inheriting_shell_timeout(self) -> None:
        device = driver.shared.Device(
            Path("/tmp/adb"),
            "wireless-device:5555",
            Path("/tmp/evidence"),
        )
        device._display_size = (1080, 2400)
        device.shell = mock.Mock(return_value="")

        device.swipe_up(x_ratio=0.4, distance_ratio=0.22)
        device.swipe_down(x_ratio=0.6, distance_ratio=0.18)

        self.assertEqual(
            [
                mock.call(
                    "input",
                    "swipe",
                    "432",
                    "1968",
                    "432",
                    "1440",
                    "300",
                    timeout=15,
                ),
                mock.call(
                    "input",
                    "swipe",
                    "648",
                    "720",
                    "648",
                    "1152",
                    "300",
                    timeout=15,
                ),
            ],
            device.shell.call_args_list,
        )

    def test_route_and_persisted_authority_reads_reset_inherited_viewports(self) -> None:
        main_source = inspect.getsource(driver.execute)
        persisted_source = inspect.getsource(driver.require_exact_restored_authority_option)

        preview_route = main_source.index(
            'device.wait("creation-prerequisite-preview-page", timeout=60)'
        )
        preview_reset = main_source.index(
            "shared.reset_scroll_to_top(device, swipes=22)",
            preview_route,
        )
        preview_digest = main_source.index(
            '"creation-prerequisite-preview-digest"',
            preview_reset,
        )
        self.assertLess(preview_route, preview_reset)
        self.assertLess(preview_reset, preview_digest)

        receipt_route = main_source.index(
            'device.wait("creation-prerequisite-confirm-receipt"'
        )
        receipt_reset = main_source.index(
            "shared.reset_scroll_to_top(device, swipes=22)",
            receipt_route,
        )
        receipt_read = main_source.index(
            'node_text(device, "creation-prerequisite-confirm-receipt"',
            receipt_reset,
        )
        self.assertLess(receipt_route, receipt_reset)
        self.assertLess(receipt_reset, receipt_read)

        persisted_reset = persisted_source.index(
            "shared.reset_scroll_to_top(device, swipes=max_scrolls)"
        )
        persisted_selection = persisted_source.index(
            'f"creation-prerequisite-{category}-selection-id"'
        )
        self.assertLess(persisted_reset, persisted_selection)

    def test_rank_selection_rewinds_only_to_exact_row_and_proves_one_refreshed_snapshot(self) -> None:
        calls: list[tuple[str, object]] = []
        category_page = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-category-page"}
        )
        parent_page = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-page"}
        )
        initial_row = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "content-desc": "Heritage. 1. Select an authority-projected rank",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )
        selected_row = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "content-desc": "Heritage. 1. Rank A · Human or metatype · source SR5",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )

        class FakeDevice:
            route = "parent"
            viewport = "bottom"

            def hierarchy(self):
                calls.append(("hierarchy", self.route))
                if self.route == "parent":
                    return [parent_page] if self.viewport == "bottom" else [parent_page, initial_row]
                if self.route == "parent-refreshed":
                    return [parent_page, selected_row]
                return [category_page]

            def shell(self, *arguments: str) -> None:
                calls.append(("shell", arguments))
                if arguments[:2] == ("input", "tap"):
                    if self.route != "parent" or self.viewport != "top":
                        raise AssertionError(
                            "Category tap ran before exact bounded viewport acquisition"
                        )
                    self.route = "category"

            def dismiss_system_ui_anr(self, _nodes=None) -> bool:
                return False

            def wait_for_single_exact_resource_id(self, selector: str, **options: object):
                calls.append(("wait_exact", (selector, options)))
                if selector == "creation-prerequisite-category-page":
                    if self.route != "category":
                        raise AssertionError("Category route was not active")
                    return category_page
                raise AssertionError(f"unexpected exact wait: {selector}")

            def swipe_down(self, **options: object) -> None:
                calls.append(("swipe_down", options))
                self.viewport = "top"

            @staticmethod
            def node_has_tappable_bounds(node) -> bool:
                return bool(node.attributes.get("bounds"))

            def capture(self, name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = FakeDevice()

        def select_rank(_device, category: str, **_options: object) -> str:
            self.assertIs(device, _device)
            self.assertEqual("heritage", category)
            calls.append(("select", category))
            device.route = "parent-refreshed"
            device.viewport = "top"
            return "creation-prerequisite-rank-heritage-a"

        rank_origin_nodes = [
            category_page,
            driver.shared.UiNode(
                {
                    "resource-id": "creation-prerequisite-rank-heritage-a",
                    "bounds": "[10,100][900,300]",
                }
            ),
        ]

        category_navigation = {
            "viewportByCategory": {
                category: index for index, category in enumerate(driver.CATEGORIES)
            },
            "currentViewport": 1,
            "lastCategory": None,
        }
        with mock.patch.object(
                 driver,
                 "tap_prescribed_exact_enabled_priority_rank",
                 side_effect=select_rank,
             ) as select_rank_mock, \
             mock.patch.object(
                 driver,
                 "wait_for_priority_rank_origin",
                 return_value=self.priority_rank_origin(rank_origin_nodes),
             ), \
             mock.patch.object(driver.time, "sleep"):
            selected = driver.select_priority_rank(
                device,
                "heritage",
                category_navigation=category_navigation,
            )

        self.assertEqual("creation-prerequisite-rank-heritage-a", selected)
        self.assertEqual(0, sum(call[0] == "wait_bidirectional" for call in calls))
        self.assertEqual(1, sum(call[0] == "swipe_down" for call in calls))
        self.assertIn(("shell", ("input", "tap", "455", "200")), calls)
        select_rank_mock.assert_called_once_with(
            device,
            "heritage",
            expected_rank=None,
            initial_observation=self.priority_rank_origin(rank_origin_nodes),
            scan_observer=None,
        )
        self.assertEqual(1, sum(call[0] == "select" for call in calls))
        self.assertEqual("heritage", category_navigation["lastCategory"])

    def test_measured_priority_category_navigation_moves_forward_after_mutation(self) -> None:
        heritage = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )
        talent = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-talent",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,400][900,600]",
            }
        )

        class ForwardDevice:
            viewport = "heritage"
            up = 0
            down = 0

            def hierarchy(self):
                return [heritage] if self.viewport == "heritage" else [talent]

            def swipe_up(self, **_options: object) -> None:
                self.up += 1
                self.viewport = "talent"

            def swipe_down(self, **_options: object) -> None:
                self.down += 1

            @staticmethod
            def node_has_tappable_bounds(node) -> bool:
                return bool(node.attributes.get("bounds"))

            def capture(self, name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = ForwardDevice()
        navigation = {
            "viewportByCategory": {
                "heritage": 2,
                "talent": 3,
                "attributes": 4,
                "skills": 5,
                "resources": 6,
            },
            "currentViewport": 2,
            "lastCategory": "heritage",
        }
        with mock.patch.object(driver.time, "sleep"):
            row = driver.acquire_measured_priority_category_row(
                device,
                "talent",
                navigation,
            )

        self.assertIs(talent, row)
        self.assertEqual(1, device.up)
        self.assertEqual(0, device.down)

    def test_measured_priority_category_navigation_reuses_refreshed_parent_viewport(self) -> None:
        heritage = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )
        talent = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-talent",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,400][900,600]",
            }
        )
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True
        navigation = {
            "viewportByCategory": {
                "heritage": 2,
                "talent": 2,
                "attributes": 3,
                "skills": 4,
                "resources": 5,
            },
            "currentViewport": 2,
            "lastCategory": "heritage",
            "currentNodes": [heritage, talent],
        }

        row = driver.acquire_measured_priority_category_row(
            device,
            "talent",
            navigation,
        )

        self.assertIs(talent, row)
        device.hierarchy.assert_not_called()
        device.swipe_up.assert_not_called()
        self.assertEqual([heritage, talent], navigation["currentNodes"])

    def test_exact_rank_scan_cardinality_checks_then_taps_one_exact_enabled_option(self) -> None:
        enabled = driver.shared.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/"
                    "creation-prerequisite-rank-heritage-e"
                ),
                "enabled": "true",
                "clickable": "true",
                "bounds": "[100,400][900,600]",
            }
        )
        disabled = [
            driver.shared.UiNode(
                {
                    "resource-id": f"creation-prerequisite-rank-heritage-{rank}",
                    "enabled": "false",
                    "clickable": "true",
                    "bounds": "[100,650][900,850]",
                }
            )
            for rank in "abcd"
        ]

        class RankDevice:
            taps: list[tuple[str, ...]] = []
            down = 0
            up = 0

            def hierarchy(self):
                return [enabled, *disabled]

            def swipe_down(self, **_options: object) -> None:
                self.down += 1

            def swipe_up(self, **_options: object) -> None:
                self.up += 1

            @staticmethod
            def node_has_tappable_bounds(node) -> bool:
                return bool(node.attributes.get("bounds"))

            def wait_exact_resource_id_bidirectional(
                self,
                selector: str,
                **_options: object,
            ):
                self.assert_exact(selector)
                return enabled

            @staticmethod
            def assert_exact(selector: str) -> None:
                if selector != "creation-prerequisite-rank-heritage-e":
                    raise AssertionError(selector)

            def shell(self, *arguments: str) -> str:
                self.taps.append(arguments)
                return ""

            def capture(self, name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = RankDevice()
        with mock.patch.object(driver.time, "sleep"):
            selected = driver.tap_prescribed_exact_enabled_priority_rank(device, "heritage")

        self.assertEqual("creation-prerequisite-rank-heritage-e", selected)
        self.assertEqual(0, device.down)
        self.assertEqual(2, device.up)
        self.assertEqual([("input", "tap", "500", "500")], device.taps)

    def test_priority_rank_origin_coalesces_exact_route_and_rank_a_viewport(self) -> None:
        route = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-category-page"}
        )
        rank_a = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-rank-heritage-a",
                "enabled": "false",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )

        class OriginDevice:
            reads = 0

            def hierarchy(self):
                self.reads += 1
                return [route, rank_a]

            @staticmethod
            def node_has_tappable_bounds(node) -> bool:
                return bool(node.attributes.get("bounds"))

            @staticmethod
            def dismiss_system_ui_anr(_nodes=None) -> bool:
                return False

            @staticmethod
            def swipe_down(**_options: object) -> None:
                raise AssertionError("visible Rank A must not trigger reverse navigation")

            @staticmethod
            def capture(name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = OriginDevice()
        proof = driver.wait_for_priority_rank_origin(device, "heritage")

        self.assertEqual([route, rank_a], proof.nodes)
        self.assertEqual(0, proof.reverse_swipes)
        self.assertEqual(1, device.reads)
        self.assertGreaterEqual(proof.elapsed_ms, 0)
        self.assertEqual(1, len(proof.hierarchy_durations_ms))
        self.assertEqual(0, proof.empty_hierarchy_reads)

    def test_priority_rank_origin_never_carries_route_truth_across_snapshots(self) -> None:
        route = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-category-page"}
        )
        rank_a = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-rank-heritage-a",
                "bounds": "[10,100][900,300]",
            }
        )
        snapshots = iter(([route], [rank_a], [route, rank_a]))

        class TransitionDevice:
            reads = 0
            reverse_swipes = 0

            def hierarchy(self):
                self.reads += 1
                return next(snapshots)

            def swipe_down(self, **_options: object) -> None:
                self.reverse_swipes += 1

            @staticmethod
            def node_has_tappable_bounds(node) -> bool:
                return bool(node.attributes.get("bounds"))

            @staticmethod
            def dismiss_system_ui_anr(_nodes=None) -> bool:
                return False

            @staticmethod
            def capture(name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = TransitionDevice()
        with mock.patch.object(driver.time, "sleep"):
            proof = driver.wait_for_priority_rank_origin(device, "heritage")

        self.assertEqual([route, rank_a], proof.nodes)
        self.assertEqual(3, device.reads)
        self.assertEqual(1, device.reverse_swipes)
        self.assertEqual(1, proof.reverse_swipes)
        self.assertEqual(3, len(proof.hierarchy_durations_ms))

    def test_priority_rank_origin_fails_closed_on_duplicate_exact_rank_a(self) -> None:
        route = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-category-page"}
        )
        duplicate_rank = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-rank-heritage-a",
                "bounds": "[10,100][900,300]",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [route, duplicate_rank, duplicate_rank]

        with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.wait_for_priority_rank_origin(device, "heritage")

        device.capture.assert_called_once_with(
            "creation-prerequisite-heritage-rank-origin-cardinality-invalid"
        )
        device.swipe_down.assert_not_called()

    def test_priority_rank_origin_fails_closed_on_duplicate_exact_route(self) -> None:
        duplicate_route = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-category-page"}
        )
        rank_a = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-rank-heritage-a",
                "bounds": "[10,100][900,300]",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [duplicate_route, duplicate_route, rank_a]

        with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.wait_for_priority_rank_origin(device, "heritage")

        device.capture.assert_called_once_with(
            "creation-prerequisite-heritage-category-route-cardinality-invalid"
        )
        device.swipe_down.assert_not_called()

    def test_exact_rank_reacquisition_advances_past_clipped_same_id(self) -> None:
        def rank_node(rank: str, *, enabled: bool, bounds: str):
            return driver.shared.UiNode(
                {
                    "resource-id": (
                        "com.myexternalbrain.chummer:id/"
                        f"creation-prerequisite-rank-resources-{rank}"
                    ),
                    "enabled": str(enabled).lower(),
                    "clickable": "true",
                    "bounds": bounds,
                }
            )

        visible = rank_node("e", enabled=True, bounds="[100,1800][900,2000]")
        clipped = rank_node("e", enabled=True, bounds="[105,2138][977,2140]")
        projected = [
            rank_node(rank, enabled=False, bounds="[100,400][900,600]")
            for rank in "abcd"
        ] + [visible]

        class RankDevice:
            viewport = 0
            hierarchy_reads = 0
            up = 0
            down = 0
            taps: list[tuple[str, ...]] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                if self.viewport == 0:
                    return projected
                return [*projected[:-1], clipped]

            def swipe_up(self, **_options: object) -> None:
                self.up += 1
                self.viewport = 1

            def swipe_down(self, **_options: object) -> None:
                self.down += 1
                self.viewport = max(0, self.viewport - 1)

            @staticmethod
            def dismiss_system_ui_anr(_nodes=None) -> bool:
                return False

            def node_has_tappable_bounds(self, node) -> bool:
                return driver.shared.Device.node_has_tappable_bounds(self, node)

            @staticmethod
            def display_size():
                return 1080, 2400

            def shell(self, *arguments: str) -> str:
                self.taps.append(arguments)
                return ""

            def capture(self, name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = RankDevice()
        with mock.patch.object(driver.time, "sleep"):
            selected = driver.tap_prescribed_exact_enabled_priority_rank(
                device,
                "resources",
                expected_rank="e",
            )

        self.assertEqual("creation-prerequisite-rank-resources-e", selected)
        self.assertEqual(3, device.up)
        self.assertEqual(1, device.down)
        self.assertEqual([("input", "tap", "500", "1900")], device.taps)

    def test_exact_rank_scan_rejects_duplicate_or_malformed_resource_ids_before_tap(self) -> None:
        duplicate = self.authority_option_node(
            "creation-prerequisite-rank-heritage-a",
            "Rank A",
        )
        malformed = self.authority_option_node(
            "creation-prerequisite-rank-heritage-forged",
            "Forged rank",
        )
        wrong_category = self.authority_option_node(
            "creation-prerequisite-rank-talent-a",
            "Talent Rank A",
        )

        for nodes, expected in (
            ([duplicate, duplicate], "duplicateIds"),
            ([malformed], "invalidIds"),
            ([wrong_category], "invalidIds"),
        ):
            with self.subTest(expected=expected):
                origin = self.authority_option_node(
                    "creation-prerequisite-rank-heritage-a",
                    "Rank A",
                )

                class InvalidRankDevice:
                    reads = 0

                    def hierarchy(self):
                        self.reads += 1
                        return [origin] if self.reads == 1 else nodes

                    @staticmethod
                    def node_has_tappable_bounds(_node) -> bool:
                        return True

                    @staticmethod
                    def swipe_up(**_options: object) -> None:
                        return None

                    @staticmethod
                    def dismiss_system_ui_anr(_nodes=None) -> bool:
                        return False

                    def capture(self, _name: str) -> None:
                        return None

                    def shell(self, *_arguments: str) -> None:
                        raise AssertionError("invalid rank scan must not tap")

                device = InvalidRankDevice()
                with mock.patch.object(driver.time, "sleep"), \
                     self.assertRaisesRegex(RuntimeError, expected):
                    driver.tap_prescribed_exact_enabled_priority_rank(device, "heritage")

    def test_exact_rank_scan_requires_the_complete_a_to_e_projection(self) -> None:
        nodes = [
            self.authority_option_node(
                f"creation-prerequisite-rank-heritage-{rank}",
                f"Rank {rank.upper()}",
            )
            for rank in "abcd"
        ]
        device = mock.Mock()
        device.hierarchy.return_value = nodes
        device.node_has_tappable_bounds.return_value = True

        with mock.patch.object(driver.time, "sleep"), \
             self.assertRaisesRegex(RuntimeError, "expectedIds"):
            driver.tap_prescribed_exact_enabled_priority_rank(device, "heritage")

        device.shell.assert_not_called()

    def test_priority_physical_proof_uses_the_explicit_legal_rank_allocation(self) -> None:
        self.assertEqual(
            {
                "heritage": "e",
                "talent": "b",
                "attributes": "a",
                "skills": "c",
                "resources": "d",
            },
            driver.PRIORITY_PROOF_RANKS,
        )
        self.assertEqual(driver.CATEGORIES, tuple(driver.PRIORITY_PROOF_RANKS))

        source = inspect.getsource(driver.execute)
        self.assertIn("for category, expected_rank in PRIORITY_PROOF_RANKS.items():", source)
        self.assertIn("expected_rank=expected_rank", source)
        allocation = source[source.index("selected:") : source.index("typed_selections:")]
        self.assertNotIn("for category in CATEGORIES:", allocation)

    def test_prescribed_rank_must_be_exact_and_core_enabled(self) -> None:
        projected = [
            driver.shared.UiNode(
                {
                    "resource-id": f"creation-prerequisite-rank-talent-{rank}",
                    "enabled": "true" if rank == "d" else "false",
                    "clickable": "true",
                    "bounds": "[100,400][900,600]",
                }
            )
            for rank in "abcde"
        ]
        device = mock.Mock()
        device.hierarchy.return_value = projected
        device.node_has_tappable_bounds.return_value = True

        with mock.patch.object(driver.time, "sleep"), \
             self.assertRaisesRegex(RuntimeError, "candidates=.*talent-d"):
            driver.tap_prescribed_exact_enabled_priority_rank(
                device,
                "talent",
                expected_rank="e",
            )

        device.shell.assert_not_called()

    def test_rank_selection_fails_closed_on_unbound_or_unrefreshed_rank(self) -> None:
        device = mock.Mock()
        initial_row = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )
        parent = driver.shared.UiNode({"resource-id": "creation-prerequisite-page"})
        stale_row = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "content-desc": "Heritage. 1. Select an authority-projected rank",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )
        device.wait_for_single_exact_resource_id.return_value = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-category-page"}
        )
        device.hierarchy.return_value = [parent, stale_row]
        device.dismiss_system_ui_anr.return_value = False
        device.node_has_tappable_bounds.return_value = True

        for selected_id, expected_error in (
            ("creation-prerequisite-rank-talent-a", "exact resource ID"),
            ("creation-prerequisite-rank-heritage-z", "invalid SR5 rank"),
            ("creation-prerequisite-rank-heritage-a", "was not projected"),
        ):
            category_navigation = {
                "viewportByCategory": {
                    category: index
                    for index, category in enumerate(driver.CATEGORIES)
                },
                "currentViewport": 0,
                "lastCategory": None,
            }
            with self.subTest(selected_id=selected_id), \
                 mock.patch.object(
                     driver,
                     "tap_prescribed_exact_enabled_priority_rank",
                     return_value=selected_id,
                 ), \
                 mock.patch.object(
                     driver,
                     "wait_for_priority_rank_origin",
                     return_value=self.priority_rank_origin(
                         [driver.shared.UiNode({"resource-id": "rank-origin"})]
                     ),
                 ), \
                 self.assertRaisesRegex(RuntimeError, expected_error):
                driver.select_priority_rank(
                    device,
                    "heritage",
                    category_navigation=category_navigation,
                )

    def test_direct_priority_bootstrap_skips_legacy_continuation_and_public_save_detour(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        execute_source = inspect.getsource(driver.execute)
        self.assertIn(
            "create_character = device.tap_exact_resource_id_until_exact_resource_id(",
            execute_source,
        )
        self.assertIn(
            '*(str(value) for value in create_character.center)',
            execute_source,
        )
        self.assertNotIn('device.tap("dialog-action-create-character"', execute_source)
        self.assertIn("require_new_character_dialog_transition(", source)
        self.assertIn("observation_out=transition_observation", source)
        self.assertNotIn("provision_creation_karma_through_priority_creation", source)
        self.assertNotIn('device.wait("dialog-action-complete-new-character-workflow"', source)
        self.assertNotIn('device.wait("Select Metatype Priority"', source)

    def test_direct_priority_driver_owns_gating_and_accepts_compatibility_argv(
        self,
    ) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "def assert_uncreated_advanced_editor_gated(",
            "assert_uncreated_advanced_editor_gated(",
            "def main(argv: list[str] | None = None) -> int:",
            "args = parser.parse_args(argv)",
            (
                '"priorityCompatibilityDriverSha256": '
                "sha256(priority_compatibility_path)"
            ),
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertNotIn("run_api36_creation_wizard_foundation_e2e", source)
        self.assertNotIn("foundation.", source)

    def test_expensive_cardinality_scans_use_stable_end_proof_not_fixed_full_bounds(self) -> None:
        for function in (
            driver.assert_uncreated_advanced_editor_gated,
            driver.tap_prescribed_exact_enabled_priority_rank,
            driver.tap_enabled_authority_option,
            driver.require_exact_restored_authority_option,
        ):
            with self.subTest(function=function.__name__):
                source = inspect.getsource(function)
                self.assertTrue(
                    "scan_forward_until_stable(" in source
                    or "scan_forward_with_receipt(" in source
                )
                self.assertNotIn("for scroll_index in range(", source)

        dashboard_source = inspect.getsource(driver.assert_uncreated_advanced_editor_gated)
        self.assertIn("distance_ratio=0.68", dashboard_source)
        self.assertIn("max_scrolls=18", dashboard_source)
        self.assertIn("acquire_stable_start_origin(", dashboard_source)
        self.assertIn("max_reverse_swipes=8", dashboard_source)
        self.assertIn("stable_repeats=2", dashboard_source)
        self.assertIn("max_consecutive_empty_reads=3", dashboard_source)
        self.assertIn("initial_observation=scan_origin", dashboard_source)
        self.assertNotIn("reset_scroll_to_top", dashboard_source)

        stable_origin_source = inspect.getsource(driver.acquire_stable_start_origin)
        self.assertIn("fresh_hierarchy_timed(device, hierarchy_durations_ms)", stable_origin_source)
        self.assertIn("device.swipe_down(distance_ratio=distance_ratio)", stable_origin_source)
        self.assertNotIn("read_only_hierarchy", stable_origin_source)
        self.assertNotIn("observer", stable_origin_source)
        self.assertNotIn("COMPOSED_SCAN_TIMING_TRIGGER_FIELDS", stable_origin_source)
        self.assertEqual(90_000, driver.PHASE_BUDGET_MS["authority-inventory"])

        execute_source = inspect.getsource(driver.execute)
        self.assertIn(
            "require_initial_creation_dashboard_snapshot(device, transition_nodes)",
            execute_source,
        )
        self.assertIn("scan_prerequisite_authority(", execute_source)
        initial_source = execute_source[: execute_source.index('progress.advance("priority-ranks")')]
        self.assertNotIn("open_prerequisite(device)", initial_source)
        self.assertNotIn("shared.open_creation_dashboard(", initial_source)
        self.assertNotIn("shared.wait_for_phone_runner_route(", initial_source)
        self.assertNotIn("reset_swipes=48", execute_source)

        restore_move = initial_source.index("move_between_measured_viewports(")
        restore_bound = initial_source.index(
            "method_reverse_swipe_bound = measured_reverse_reacquisition_bound("
        )
        restore_reacquisition = initial_source.index(
            "reacquire_exact_ready_creation_method("
        )
        self.assertLess(restore_bound, restore_move)
        self.assertLess(restore_move, restore_reacquisition)
        restored_method_source = initial_source[restore_reacquisition:]
        self.assertIn(
            "max_swipes=method_reverse_swipe_bound",
            restored_method_source,
        )
        self.assertNotIn("max_swipes=1", restored_method_source)

        method_reacquisition_source = inspect.getsource(
            driver.reacquire_exact_ready_creation_method
        )
        self.assertIn('"creation-stage-method"', method_reacquisition_source)
        self.assertIn("distance_ratio=0.22", method_reacquisition_source)
        self.assertIn("require_tappable=True", method_reacquisition_source)
        self.assertIn("max_empty_hierarchy_reads=3", method_reacquisition_source)
        self.assertIn("max_system_ui_dismissals=3", method_reacquisition_source)
        self.assertIn("detail != expected_detail", method_reacquisition_source)

        rewind_source = inspect.getsource(driver.rewind_to_exact_resource_id)
        self.assertIn("fresh_hierarchy_timed(device, [])", rewind_source)
        self.assertIn("_exact_resource_id(node) == selector", rewind_source)
        self.assertIn("device.swipe_down(", rewind_source)
        self.assertNotIn("device.swipe_up(", rewind_source)
        self.assertNotIn("_matches(", rewind_source)

        prerequisite_source = inspect.getsource(driver.scan_prerequisite_authority)
        self.assertIn("scan_forward_with_receipt(", prerequisite_source)
        self.assertIn("max_scrolls=22", prerequisite_source)
        self.assertIn("distance_ratio=0.22", prerequisite_source)
        self.assertIn("initial_observation=initial_observation", prerequisite_source)
        self.assertNotIn("reset_scroll_to_top", prerequisite_source)

        prerequisite_origin_source = inspect.getsource(
            driver.wait_for_prerequisite_scan_origin
        )
        self.assertIn('route_selector = "creation-prerequisite-page"', prerequisite_origin_source)
        self.assertIn('"creation-prerequisite-method"', prerequisite_origin_source)
        self.assertIn('"creation-prerequisite-binding"', prerequisite_origin_source)
        self.assertIn("max_reverse_swipes: int = 8", prerequisite_origin_source)

        selection_source = inspect.getsource(driver.select_priority_rank)
        self.assertIn("acquire_measured_priority_category_row", selection_source)
        self.assertNotIn("rewind_to_exact_resource_id", selection_source)

        acquisition_source = inspect.getsource(driver.acquire_measured_priority_category_row)
        self.assertIn("distance_ratio=0.22", acquisition_source)

        rank_source = inspect.getsource(driver.tap_prescribed_exact_enabled_priority_rank)
        self.assertIn("reverse_swipes = max(0, scan.swipes - selected_viewport)", rank_source)
        self.assertNotIn("reset_scroll_to_top", rank_source)

        # The former fixed loops always spent 404 forward swipes before any
        # selector reacquisition. Stable scans now spend only the observed delta.
        legacy_fixed_forward_swipes = (18 * 3) + (22 * 5) + (40 * 2) + (40 * 4)
        self.assertEqual(404, legacy_fixed_forward_swipes)
        self.assertEqual(2, driver.scan_forward_until_stable.__kwdefaults__["stable_repeats"])

    def test_dashboard_scan_reuses_one_cardinality_checked_authority_snapshot(self) -> None:
        binding = driver.shared.UiNode(
            {
                "resource-id": "creation-wizard-binding",
                "content-desc": "Revision 7",
            }
        )
        method = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )
        device = mock.Mock()
        origin = self.priority_rank_origin([binding, method])
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=origin,
        ) as acquire, mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([[binding], [method]], 4),
        ) as scan:
            proof = driver.assert_uncreated_advanced_editor_gated(device)
        self.assertEqual(
            driver.CreationDashboardScanProof("Revision 7", "Priority", 4, 1),
            proof,
        )
        acquire.assert_called_once_with(
            device,
            scan_id="advanced-editor-gate-origin",
            max_reverse_swipes=8,
            distance_ratio=0.68,
            stable_repeats=2,
            max_consecutive_empty_reads=3,
            delay_seconds=0.0,
        )
        scan.assert_called_once_with(
            device,
            scan_id="advanced-editor-gate",
            max_scrolls=18,
            distance_ratio=0.68,
            initial_observation=origin,
            delay_seconds=0.0,
            observer=None,
        )

        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=origin,
        ), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([[binding], [method, method]], 4),
        ), self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.assert_uncreated_advanced_editor_gated(device)

    def test_dashboard_scan_selects_the_last_tappable_method_viewport(self) -> None:
        binding = driver.shared.UiNode(
            {
                "resource-id": "creation-wizard-binding",
                "content-desc": "Revision 7",
            }
        )

        def method(bounds: str) -> driver.shared.UiNode:
            return driver.shared.UiNode(
                {
                    "resource-id": "creation-stage-method",
                    "content-desc": "Priority",
                    "enabled": "true",
                    "clickable": "true",
                    "bounds": bounds,
                }
            )

        clipped_before = method("[53,273][1028,275]")
        tappable = method("[53,350][1028,550]")
        clipped_after = method("[53,2188][1028,2190]")
        origin = self.priority_rank_origin([binding, clipped_before])
        device = mock.Mock()
        device.node_has_tappable_bounds.side_effect = lambda node: node is tappable
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=origin,
        ), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan(
                [
                    [binding, clipped_before],
                    [tappable],
                    [clipped_after],
                ],
                6,
            ),
        ):
            proof = driver.assert_uncreated_advanced_editor_gated(device)

        self.assertEqual(
            driver.CreationDashboardScanProof("Revision 7", "Priority", 6, 1),
            proof,
        )

    def test_restored_creation_method_reacquisition_uses_fresh_exact_id_hierarchies(
        self,
    ) -> None:
        clipped_parent = driver.shared.UiNode(
            {
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,275][1028,292]",
            }
        )
        fresh_method = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,350][1028,550]",
            }
        )

        class RestoreDevice:
            def __init__(self, screens):
                self.screens = list(screens)
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                if self.hierarchy_reads != self.reverse_swipes:
                    raise AssertionError(
                        "Every post-restore hierarchy must follow its own reverse swipe"
                    )
                screen = self.screens[self.hierarchy_reads]
                self.hierarchy_reads += 1
                return screen

            def dismiss_system_ui_anr(self, _nodes):
                return False

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.reverse_swipes += 1

            def node_has_tappable_bounds(self, node):
                return node.attributes.get("bounds") == "[53,350][1028,550]"

            def capture(self, name):
                self.captures.append(name)

        device = RestoreDevice([[clipped_parent], [fresh_method]])
        with mock.patch.object(driver.time, "sleep"):
            node, reverse_swipes = driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=1,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
            )

        self.assertIs(fresh_method, node)
        self.assertEqual(1, reverse_swipes)
        self.assertEqual(2, device.hierarchy_reads)
        self.assertEqual(1, device.reverse_swipes)
        self.assertEqual(0.22, device.asserted_distance_ratio)
        self.assertEqual([], device.captures)

    def test_hosted_creation_method_restore_derives_compensation_from_scan_delta(
        self,
    ) -> None:
        clipped_parent = driver.shared.UiNode(
            {
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,275][1028,292]",
            }
        )
        exact_method = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,350][1028,550]",
            }
        )

        class HostedRestoreDevice:
            def __init__(self) -> None:
                # The hosted receipt made six real movements (eight swipes
                # minus two stable-end gestures) and observed the method at
                # viewport one. Five 0.68-height movements separate that exact
                # node from the end. Android's reverse gesture caps at 0.60
                # height, leaving 0.40 height after the measured five-swipe
                # return. Two 0.22 gestures recover the exact row; the former
                # one-swipe constant could not.
                self.offset = 340
                self.hierarchy_reads = 0
                self.swipe_ratios: list[float] = []
                self.captures: list[str] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [exact_method] if self.offset <= 4 else [clipped_parent]

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def swipe_down(self, *, distance_ratio):
                self.swipe_ratios.append(distance_ratio)
                movement = 60 if distance_ratio == 0.68 else 22
                self.offset = max(0, self.offset - movement)

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is exact_method

            def capture(self, name):
                self.captures.append(name)

        device = HostedRestoreDevice()
        movement_swipes = 6
        method_viewport = 1
        reverse_bound = driver.measured_reverse_reacquisition_bound(
            movement_swipes,
            method_viewport,
        )
        driver.move_between_measured_viewports(
            device,
            movement_swipes,
            method_viewport,
            distance_ratio=0.68,
            delay_seconds=0.0,
        )
        with mock.patch.object(driver.time, "sleep"):
            node, reverse_swipes = driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=reverse_bound,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
            )

        self.assertEqual(5, reverse_bound)
        self.assertIs(exact_method, node)
        self.assertEqual(2, reverse_swipes)
        self.assertEqual(3, device.hierarchy_reads)
        self.assertEqual(([0.68] * 5) + ([0.22] * 2), device.swipe_ratios)
        self.assertEqual([], device.captures)

    def test_measured_reverse_reacquisition_bound_is_exact_and_fail_closed(
        self,
    ) -> None:
        for current, target, expected in (
            (6, 0, 6),
            (6, 4, 2),
            (6, 6, 0),
            (18, 0, 18),
        ):
            with self.subTest(current=current, target=target):
                self.assertEqual(
                    expected,
                    driver.measured_reverse_reacquisition_bound(current, target),
                )

        for current, target in (
            (-1, 0),
            (0, -1),
            (1, 2),
            (19, 0),
            (1.0, 0),
            (1, 0.0),
            (True, 0),
            (1, False),
        ):
            with self.subTest(current=current, target=target), self.assertRaisesRegex(
                ValueError,
                "integer viewports ordered within 0..18",
            ):
                driver.measured_reverse_reacquisition_bound(current, target)

    def test_restored_creation_method_can_succeed_on_last_measured_gesture(
        self,
    ) -> None:
        method = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,350][1028,550]",
            }
        )

        class LastGestureDevice:
            def __init__(self) -> None:
                self.remaining = 3
                self.hierarchy_reads = 0
                self.reverse_swipes = 0

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [method] if self.remaining == 0 else [driver.shared.UiNode({})]

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.remaining -= 1
                self.reverse_swipes += 1

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is method

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = LastGestureDevice()
        with mock.patch.object(driver.time, "sleep"):
            node, reverse_swipes = driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=3,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
            )

        self.assertIs(method, node)
        self.assertEqual(3, reverse_swipes)
        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual(3, device.reverse_swipes)

    def test_restored_creation_method_one_beyond_bound_remains_fail_closed(
        self,
    ) -> None:
        method = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
            }
        )

        class OneBeyondDevice:
            def __init__(self) -> None:
                self.remaining = 4
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [method] if self.remaining == 0 else [driver.shared.UiNode({})]

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.remaining -= 1
                self.reverse_swipes += 1

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is method

            def capture(self, name):
                self.captures.append(name)

        device = OneBeyondDevice()

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "within the scan-proven 3-swipe bound",
        ):
            driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=3,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
            )

        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual(3, device.reverse_swipes)
        self.assertEqual(1, device.remaining)
        self.assertEqual(["creation-stage-method-ready-unavailable"], device.captures)

    def test_restored_creation_method_empty_reads_do_not_consume_gesture_bound(
        self,
    ) -> None:
        method = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
            }
        )
        device = mock.Mock()
        device.hierarchy.side_effect = [
            [],
            [],
            [driver.shared.UiNode({})],
            [],
            [method],
        ]
        device.dismiss_system_ui_anr.return_value = False
        device.node_has_tappable_bounds.return_value = True

        with mock.patch.object(driver.time, "sleep"):
            node, reverse_swipes = driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=1,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
                max_empty_hierarchy_reads=3,
            )

        self.assertIs(method, node)
        self.assertEqual(1, reverse_swipes)
        self.assertEqual(5, device.hierarchy.call_count)
        device.swipe_down.assert_called_once_with(distance_ratio=0.22)
        device.capture.assert_not_called()

    def test_ready_creation_method_reacquisition_rejects_invalid_state_without_tap(
        self,
    ) -> None:
        cases = (
            ("disabled", "Priority", "false", "true", "visible, enabled, and clickable"),
            ("nonclickable", "Priority", "true", "false", "visible, enabled, and clickable"),
            ("wrong-detail", "Sum-to-Ten", "true", "true", "authority changed"),
        )
        for case, detail, enabled, clickable, error in cases:
            node = driver.shared.UiNode(
                {
                    "resource-id": "creation-stage-method",
                    "content-desc": detail,
                    "enabled": enabled,
                    "clickable": clickable,
                }
            )
            device = mock.Mock()
            device.hierarchy.return_value = [node]
            device.node_has_tappable_bounds.return_value = True

            with self.subTest(case=case), self.assertRaisesRegex(RuntimeError, error):
                driver.reacquire_exact_ready_creation_method(
                    device,
                    expected_detail="Priority",
                    max_swipes=0,
                )

            device.shell.assert_not_called()
            device.swipe_down.assert_not_called()

    def test_restored_creation_method_reacquisition_accepts_immediate_fresh_match(
        self,
    ) -> None:
        method = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "enabled": "true",
                "clickable": "true",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [method]
        device.node_has_tappable_bounds.return_value = True

        node, reverse_swipes = driver.rewind_to_exact_resource_id(
            device,
            "creation-stage-method",
            max_swipes=1,
            distance_ratio=0.22,
            evidence_prefix="creation-stage-method-ready",
            surface_name="Measured ready creation method navigation",
            require_tappable=True,
        )

        self.assertIs(method, node)
        self.assertEqual(0, reverse_swipes)
        device.hierarchy.assert_called_once_with()
        device.swipe_down.assert_not_called()

    def test_restored_creation_method_reacquires_clipped_exact_id_within_bound(
        self,
    ) -> None:
        clipped = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,275][1028,292]",
            }
        )
        visible = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,350][1028,550]",
            }
        )
        device = mock.Mock()
        device.hierarchy.side_effect = [[clipped], [visible]]
        device.node_has_tappable_bounds.side_effect = lambda node: node is visible

        with mock.patch.object(driver.time, "sleep"):
            node, reverse_swipes = driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=1,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
            )

        self.assertIs(visible, node)
        self.assertEqual(1, reverse_swipes)
        self.assertEqual(2, device.hierarchy.call_count)
        device.swipe_down.assert_called_once_with(distance_ratio=0.22)
        device.capture.assert_not_called()

    def test_restored_creation_method_fails_when_exact_id_stays_clipped_at_bound(
        self,
    ) -> None:
        clipped = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,275][1028,292]",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [clipped]
        device.node_has_tappable_bounds.return_value = False

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "was not visible, enabled, and clickable",
        ):
            driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=1,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
            )

        self.assertEqual(2, device.hierarchy.call_count)
        device.swipe_down.assert_called_once_with(distance_ratio=0.22)
        device.capture.assert_called_once_with(
            "creation-stage-method-ready-not-tappable"
        )

    def test_restored_creation_method_reacquisition_fails_closed_on_duplicate(self) -> None:
        methods = [
            driver.shared.UiNode(
                {
                    "resource-id": "creation-stage-method",
                    "enabled": "true",
                    "clickable": "true",
                }
            )
            for _ in range(2)
        ]
        device = mock.Mock()
        device.hierarchy.return_value = methods

        with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=1,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
            )

        device.swipe_down.assert_not_called()
        device.capture.assert_called_once_with(
            "creation-stage-method-ready-cardinality-invalid"
        )

    def test_restored_creation_method_reacquisition_fails_closed_when_not_tappable(
        self,
    ) -> None:
        for attributes in (
            {"enabled": "false", "clickable": "true"},
            {"enabled": "true", "clickable": "false"},
        ):
            with self.subTest(attributes=attributes):
                method = driver.shared.UiNode(
                    {"resource-id": "creation-stage-method", **attributes}
                )
                device = mock.Mock()
                device.hierarchy.return_value = [method]
                device.node_has_tappable_bounds.return_value = False

                with self.assertRaisesRegex(RuntimeError, "visible, enabled, and clickable"):
                    driver.rewind_to_exact_resource_id(
                        device,
                        "creation-stage-method",
                        max_swipes=1,
                        distance_ratio=0.22,
                        evidence_prefix="creation-stage-method-ready",
                        surface_name="Measured ready creation method navigation",
                        require_tappable=True,
                    )

                device.swipe_down.assert_not_called()
                device.capture.assert_called_once_with(
                    "creation-stage-method-ready-not-tappable"
                )

    def test_restored_creation_method_reacquisition_stops_at_reverse_swipe_bound(
        self,
    ) -> None:
        clipped_parent = driver.shared.UiNode(
            {
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,275][1028,292]",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [clipped_parent]
        device.dismiss_system_ui_anr.return_value = False

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "within the scan-proven 1-swipe bound",
        ):
            driver.rewind_to_exact_resource_id(
                device,
                "creation-stage-method",
                max_swipes=1,
                distance_ratio=0.22,
                evidence_prefix="creation-stage-method-ready",
                surface_name="Measured ready creation method navigation",
                require_tappable=True,
            )

        self.assertEqual(2, device.hierarchy.call_count)
        device.swipe_down.assert_called_once_with(distance_ratio=0.22)
        device.swipe_up.assert_not_called()
        device.capture.assert_called_once_with("creation-stage-method-ready-unavailable")

    def test_dashboard_scan_does_not_reuse_read_only_authority_viewport_as_scroll_origin(
        self,
    ) -> None:
        nodes = [
            driver.shared.UiNode({"resource-id": "creation-wizard-dashboard"}),
            driver.shared.UiNode(
                {
                    "resource-id": "creation-wizard-binding",
                    "content-desc": "Revision 7",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": "creation-stage-method",
                    "content-desc": "Priority",
                    "enabled": "true",
                    "clickable": "true",
                }
            ),
        ]

        class ResolvedDevice:
            def read_only_hierarchy(self):
                raise AssertionError("Resolved transition viewport must be reused")

        transition_viewport = driver.PriorityRankOrigin(nodes, 0, 3, (3,), 0)
        viewport: list[driver.PriorityRankOrigin] = []
        waited = driver.wait_creation_dashboard_authority(
            ResolvedDevice(),
            initial_observation=transition_viewport,
            resolved_viewport_out=viewport,
        )
        self.assertFalse(waited)
        self.assertEqual(1, len(viewport))
        self.assertIs(nodes, viewport[0].nodes)
        self.assertEqual((3,), viewport[0].hierarchy_durations_ms)

        device = mock.Mock()
        fresh_origin = driver.PriorityRankOrigin(nodes, 2, 9, (3, 3, 3), 0)
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=fresh_origin,
        ) as acquire, mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes], 0),
        ) as scan:
            proof = driver.assert_uncreated_advanced_editor_gated(
                device,
            )
        self.assertEqual("Revision 7", proof.binding)
        acquire.assert_called_once_with(
            device,
            scan_id="advanced-editor-gate-origin",
            max_reverse_swipes=8,
            distance_ratio=0.68,
            stable_repeats=2,
            max_consecutive_empty_reads=3,
            delay_seconds=0.0,
        )
        scan.assert_called_once_with(
            device,
            scan_id="advanced-editor-gate",
            max_scrolls=18,
            distance_ratio=0.68,
            initial_observation=fresh_origin,
            delay_seconds=0.0,
            observer=None,
        )

    def test_dashboard_stable_origin_recovers_hosted_method_above_start(self) -> None:
        def marker(name: str) -> driver.shared.UiNode:
            return driver.shared.UiNode(
                {
                    "resource-id": "hosted-viewport-marker",
                    "content-desc": name,
                    "bounds": "[0,275][1080,2190]",
                }
            )

        method = driver.shared.UiNode(
            {
                "resource-id": "creation-stage-method",
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,350][1028,550]",
            }
        )
        top = [marker("top"), method]

        class HostedOffsetDevice:
            def __init__(self) -> None:
                # The failure artifact placed the method around y=-4266. Four
                # 0.68-height reverse movements recover it; two clamped repeats
                # then prove the stable start, all within the unchanged bound 8.
                self.screens = [
                    [marker("below-method-4")],
                    [marker("below-method-3")],
                    [marker("below-method-2")],
                    [marker("below-method-1")],
                    top,
                    top,
                    top,
                ]
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                if self.hierarchy_reads != self.reverse_swipes:
                    raise AssertionError("Each reverse gesture needs one fresh hierarchy")
                nodes = self.screens[self.hierarchy_reads]
                self.hierarchy_reads += 1
                return nodes

            def swipe_down(self, *, distance_ratio):
                if self.hierarchy_reads != self.reverse_swipes + 1:
                    raise AssertionError("A reverse gesture cannot outrun its fresh baseline")
                self.asserted_distance_ratio = distance_ratio
                self.reverse_swipes += 1

            def capture(self, name):
                self.captures.append(name)

        device = HostedOffsetDevice()
        perf_counter = [
            value
            for index in range(7)
            for value in (float(index), float(index) + 0.003)
        ]
        with mock.patch.object(
            driver.time,
            "perf_counter",
            side_effect=perf_counter,
        ), mock.patch.object(
            driver.time,
            "monotonic",
            side_effect=[10.0, 10.030],
        ):
            origin = driver.acquire_stable_start_origin(
                device,
                scan_id="advanced-editor-gate-initial-origin",
                max_reverse_swipes=8,
                distance_ratio=0.68,
                stable_repeats=2,
                max_consecutive_empty_reads=3,
                delay_seconds=0.0,
            )

        self.assertIs(top, origin.nodes)
        self.assertIn(method, origin.nodes)
        self.assertEqual(6, origin.reverse_swipes)
        self.assertEqual(30, origin.elapsed_ms)
        self.assertEqual((3, 3, 3, 3, 3, 3, 3), origin.hierarchy_durations_ms)
        self.assertEqual(0, origin.empty_hierarchy_reads)
        self.assertEqual(7, device.hierarchy_reads)
        self.assertEqual(6, device.reverse_swipes)
        self.assertEqual(0.68, device.asserted_distance_ratio)
        self.assertEqual([], device.captures)

    def test_dashboard_stable_origin_retries_empty_reads_without_extra_gestures(
        self,
    ) -> None:
        device = mock.Mock()
        device.hierarchy.side_effect = [
            [
                driver.shared.UiNode(
                    {
                        "resource-id": "moving-viewport-marker",
                        "content-desc": "below-method",
                    }
                )
            ],
            [],
            [],
            [],
            [],
        ]

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "exhausted transient empty hierarchy reads",
        ):
            driver.acquire_stable_start_origin(
                device,
                scan_id="advanced-editor-gate-initial-origin",
                max_reverse_swipes=8,
                distance_ratio=0.68,
                stable_repeats=2,
                max_consecutive_empty_reads=3,
                delay_seconds=0.0,
            )

        self.assertEqual(5, device.hierarchy.call_count)
        device.swipe_down.assert_called_once_with(distance_ratio=0.68)
        device.capture.assert_called_once_with(
            "advanced-editor-gate-initial-origin-empty-hierarchy-exhausted"
        )

    def test_dashboard_stable_origin_exhausts_exact_reverse_bound_without_guessing(
        self,
    ) -> None:
        device = mock.Mock()
        device.hierarchy.side_effect = [
            [
                driver.shared.UiNode(
                    {
                        "resource-id": "moving-viewport-marker",
                        "content-desc": f"viewport-{index}",
                    }
                )
            ]
            for index in range(9)
        ]

        with self.assertRaisesRegex(RuntimeError, "within 8 swipes"):
            driver.acquire_stable_start_origin(
                device,
                scan_id="advanced-editor-gate-initial-origin",
                max_reverse_swipes=8,
                distance_ratio=0.68,
                stable_repeats=2,
                max_consecutive_empty_reads=3,
                delay_seconds=0.0,
            )

        self.assertEqual(9, device.hierarchy.call_count)
        self.assertEqual(8, device.swipe_down.call_count)
        device.capture.assert_called_once_with(
            "advanced-editor-gate-initial-origin-stable-start-unproven"
        )

    def test_dashboard_origin_timing_is_carried_once_by_composed_forward_receipt(
        self,
    ) -> None:
        node = driver.shared.UiNode(
            {
                "resource-id": "stable-dashboard",
                "content-desc": "stable",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [node]
        observations: list[dict[str, object]] = []
        perf_counter = iter(
            value
            for started, duration_ms in (
                (0.0, 4),
                (1.0, 5),
                (2.0, 6),
                (3.0, 7),
                (4.0, 8),
            )
            for value in (started, started + duration_ms / 1000)
        )

        with mock.patch.object(
            driver.time,
            "perf_counter",
            side_effect=perf_counter,
        ), mock.patch.object(
            driver.time,
            "monotonic",
            side_effect=[0.0, 0.016, 1.0, 1.020],
        ):
            origin = driver.acquire_stable_start_origin(
                device,
                scan_id="advanced-editor-gate-initial-origin",
                max_reverse_swipes=8,
                distance_ratio=0.68,
                stable_repeats=2,
                max_consecutive_empty_reads=3,
                delay_seconds=0.0,
            )
            driver.scan_forward_until_stable(
                device,
                scan_id="advanced-editor-gate-initial",
                max_scrolls=18,
                distance_ratio=0.68,
                initial_observation=origin,
                stable_repeats=2,
                delay_seconds=0.0,
                observer=observations.append,
            )

        self.assertEqual(1, len(observations))
        receipt = observations[0]
        self.assertEqual("stable-end", receipt["status"])
        self.assertEqual(16, receipt["originElapsedMs"])
        self.assertEqual(2, receipt["originReverseSwipes"])
        self.assertEqual(3, receipt["originHierarchyReadCount"])
        self.assertEqual(15, receipt["originHierarchyElapsedMs"])
        self.assertEqual(6, receipt["originMaximumHierarchyReadMs"])
        self.assertEqual(20, receipt["traversalElapsedMs"])
        self.assertEqual(4, receipt["totalNavigationSwipes"])
        self.assertEqual(5, receipt["hierarchyReadCount"])
        self.assertEqual(30, receipt["hierarchyElapsedMs"])
        self.assertEqual(8, receipt["maximumHierarchyReadMs"])
        self.assertEqual(36, receipt["elapsedMs"])
        driver.require_composed_scan_timing(receipt)
        self.assertEqual(5, device.hierarchy.call_count)
        self.assertEqual(2, device.swipe_down.call_count)
        self.assertEqual(2, device.swipe_up.call_count)

    def test_composed_scan_timing_accepts_exact_maximum_lower_and_upper_bounds(
        self,
    ) -> None:
        for case, origin_maximum, combined_maximum in (
            ("lower", 5, 8),
            ("upper", 15, 15),
        ):
            with self.subTest(case=case):
                driver.require_composed_scan_timing(
                    {
                        "scanId": "advanced-editor-gate-initial",
                        "status": "stable-end",
                        "reusedInitialScreen": True,
                        "originElapsedMs": 13,
                        "originReverseSwipes": 2,
                        "originEmptyHierarchyReads": 0,
                        "originHierarchyReadCount": 3,
                        "originHierarchyElapsedMs": 15,
                        "originMaximumHierarchyReadMs": origin_maximum,
                        "traversalElapsedMs": 14,
                        "traversalEmptyHierarchyReads": 0,
                        "emptyHierarchyReads": 0,
                        "totalNavigationSwipes": 4,
                        "hierarchyReadCount": 5,
                        "hierarchyElapsedMs": 30,
                        "maximumHierarchyReadMs": combined_maximum,
                        "elapsedMs": 27,
                        "swipes": 2,
                    }
                )

    def test_dashboard_inventory_rejects_duplicate_missing_and_empty_authority(
        self,
    ) -> None:
        def authority(selector: str, value: str) -> driver.shared.UiNode:
            return driver.shared.UiNode(
                {
                    "resource-id": selector,
                    "content-desc": value,
                    "enabled": "true",
                    "clickable": "true",
                }
            )

        binding = authority("creation-wizard-binding", "Revision 7")
        method = authority("creation-stage-method", "Priority")
        cases = (
            ("duplicate-binding", [[binding, binding, method]], "cardinality 2"),
            ("duplicate-method", [[binding, method, method]], "cardinality 2"),
            ("missing-binding", [[method]], "did not expose one binding"),
            ("missing-method", [[binding]], "did not expose one binding"),
            (
                "empty-binding",
                [[authority("creation-wizard-binding", "  "), method]],
                "did not expose one binding",
            ),
            (
                "empty-method",
                [[binding, authority("creation-stage-method", "  ")]],
                "did not expose one binding",
            ),
        )
        origin = self.priority_rank_origin([binding, method])
        for case, screens, error in cases:
            device = mock.Mock()
            with self.subTest(case=case), mock.patch.object(
                driver,
                "acquire_stable_start_origin",
                return_value=origin,
            ), mock.patch.object(
                driver,
                "scan_forward_with_receipt",
                return_value=driver.StableViewportScan(screens, 0),
            ), self.assertRaisesRegex(RuntimeError, error):
                driver.assert_uncreated_advanced_editor_gated(device)

    def test_prerequisite_scan_origin_reuses_exact_top_viewport_after_bounded_rewind(
        self,
    ) -> None:
        route = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-page"}
        )
        method = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-method"}
        )
        binding = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-binding"}
        )

        class OriginDevice:
            def __init__(self):
                self.reads = [[route], [route, method, binding]]
                self.swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                return self.reads.pop(0)

            def dismiss_system_ui_anr(self, _nodes):
                return False

            def swipe_down(self, **_kwargs):
                self.swipes += 1

            def capture(self, name):
                self.captures.append(name)

        device = OriginDevice()
        with mock.patch.object(driver.time, "sleep"), mock.patch.object(
            driver.time,
            "perf_counter",
            side_effect=[1.0, 1.002, 2.0, 2.003],
        ):
            origin = driver.wait_for_prerequisite_scan_origin(device)
        self.assertEqual([route, method, binding], origin.nodes)
        self.assertEqual(1, origin.reverse_swipes)
        self.assertEqual((2, 3), origin.hierarchy_durations_ms)
        self.assertEqual(1, device.swipes)
        self.assertEqual([], device.captures)

    def test_prerequisite_scan_origin_rejects_duplicate_route_or_top_anchor(self) -> None:
        route = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-page"}
        )
        method = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-method"}
        )

        class AmbiguousOriginDevice:
            def __init__(self):
                self.captures: list[str] = []

            def hierarchy(self):
                return [route, route, method]

            def capture(self, name):
                self.captures.append(name)

        device = AmbiguousOriginDevice()
        with self.assertRaisesRegex(RuntimeError, "ambiguous"):
            driver.wait_for_prerequisite_scan_origin(device)
        self.assertEqual(
            ["creation-prerequisite-scan-origin-cardinality-invalid"],
            device.captures,
        )

    def test_prerequisite_authority_scan_collects_once_and_rejects_drift(self) -> None:
        digest_values = {
            "creation-prerequisite-snapshot-digest": "sha256:" + "1" * 64,
            "creation-prerequisite-raw-character-xml-digest": "sha256:" + "2" * 64,
            "creation-prerequisite-auxiliary-state-digest": "3" * 64,
            "creation-prerequisite-authority-digest": "sha256:" + "4" * 64,
            "creation-prerequisite-profile-inputs-digest": "sha256:" + "5" * 64,
            "creation-prerequisite-priorities-xml-digest": "sha256:" + "6" * 64,
        }
        values = {
            "creation-prerequisite-binding": "Revision 7",
            "creation-prerequisite-method": "Priority",
            "creation-prerequisite-karma-budget": "Total 25 · Used 0 · Remaining 25",
            **digest_values,
        }
        nodes = [
            driver.shared.UiNode({"resource-id": selector, "content-desc": value})
            for selector, value in values.items()
        ]
        nodes.extend(
            driver.shared.UiNode(
                {
                    "resource-id": f"creation-prerequisite-category-{category}",
                    "content-desc": category,
                    "enabled": "true",
                    "clickable": "true",
                    "bounds": "[10,100][900,300]",
                }
            )
            for category in driver.CATEGORIES
        )
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True
        origin = driver.PriorityRankOrigin(nodes, 0, 1, (1,), 0)
        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes], 6),
        ) as scan:
            proof = driver.scan_prerequisite_authority(
                device,
                initial_observation=origin,
            )
        self.assertEqual(values, proof.values)
        self.assertEqual(6, proof.swipes)
        self.assertEqual(
            {category: 0 for category in driver.CATEGORIES},
            proof.category_viewports,
        )
        scan.assert_called_once_with(
            device,
            scan_id="prerequisite-authority-initial",
            max_scrolls=22,
            distance_ratio=0.22,
            initial_observation=origin,
            delay_seconds=0.0,
            observer=None,
        )

        changed = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-binding",
                "content-desc": "Revision 8",
            }
        )
        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes, [changed]], 6),
        ), self.assertRaisesRegex(
            RuntimeError, "changed while scrolling"
        ):
            driver.scan_prerequisite_authority(
                device,
                initial_observation=origin,
            )

    def test_priority_category_inventory_captures_intermediate_only_rows_and_fails_closed(
        self,
    ) -> None:
        def category_node(category: str, *, enabled: str = "true"):
            return driver.shared.UiNode(
                {
                    "resource-id": f"creation-prerequisite-category-{category}",
                    "content-desc": category,
                    "enabled": enabled,
                    "clickable": "true",
                    "focusable": "true",
                    "bounds": "[10,100][900,300]",
                }
            )

        authority_values = {
            "creation-prerequisite-binding": "Revision 7",
            "creation-prerequisite-method": "Priority",
            "creation-prerequisite-karma-budget": "Total 25 · Used 0 · Remaining 25",
            "creation-prerequisite-snapshot-digest": "sha256:" + "1" * 64,
            "creation-prerequisite-raw-character-xml-digest": "sha256:" + "2" * 64,
            "creation-prerequisite-auxiliary-state-digest": "3" * 64,
            "creation-prerequisite-authority-digest": "sha256:" + "4" * 64,
            "creation-prerequisite-profile-inputs-digest": "sha256:" + "5" * 64,
            "creation-prerequisite-priorities-xml-digest": "sha256:" + "6" * 64,
        }
        authority_nodes = [
            driver.shared.UiNode({"resource-id": selector, "content-desc": value})
            for selector, value in authority_values.items()
        ]
        screens = [
            [*authority_nodes, category_node(driver.CATEGORIES[0])],
            *[[category_node(category)] for category in driver.CATEGORIES[1:]],
        ]
        device = mock.Mock()
        device.node_has_tappable_bounds.side_effect = (
            lambda node: bool(node.attributes.get("bounds"))
        )

        def scan(candidate_screens):
            origin = driver.PriorityRankOrigin(candidate_screens[0], 0, 1, (1,), 0)
            with mock.patch.object(
                driver,
                "scan_forward_with_receipt",
                return_value=driver.StableViewportScan(candidate_screens, 7),
            ):
                return driver.scan_prerequisite_authority(
                    device,
                    initial_observation=origin,
                )

        proof = scan(screens)
        self.assertEqual(
            {category: index for index, category in enumerate(driver.CATEGORIES)},
            proof.category_viewports,
        )

        missing_talent = [
            screen
            for category, screen in zip(driver.CATEGORIES, screens, strict=True)
            if category != "talent"
        ]
        with self.assertRaisesRegex(
            RuntimeError, "omitted an exact tappable priority category"
        ):
            scan(missing_talent)

        duplicate_heritage = [
            [*authority_nodes, category_node("heritage"), category_node("heritage")],
            *screens[1:],
        ]
        with self.assertRaisesRegex(
            RuntimeError, "cardinality 2"
        ):
            scan(duplicate_heritage)

        disabled_talent = [
            [*authority_nodes, category_node(driver.CATEGORIES[0])],
            *[
                [category_node(category, enabled="false" if category == "talent" else "true")]
                for category in driver.CATEGORIES[1:]
            ],
        ]
        with self.assertRaisesRegex(
            RuntimeError, "was not enabled and clickable"
        ):
            scan(disabled_talent)

    def test_prerequisite_page_pins_short_method_authority_before_tall_cards(self) -> None:
        source = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        refresh = source[source.index("protected override void Refresh()") :]
        refresh = refresh[: refresh.index("private void AddBinding(")]
        self.assertLess(refresh.index("AddMethod(state);"), refresh.index("AddBinding(state);"))
        self.assertLess(
            refresh.index("AddBinding(state);"),
            refresh.index("AddCreationKarma(state.CreationKarmaBudget);"),
        )

    def test_stable_scan_receipt_returns_the_observed_swipe_delta(self) -> None:
        node = driver.shared.UiNode({"resource-id": "stable"})
        device = mock.Mock()
        device.hierarchy.return_value = [node]
        receipt = driver.scan_forward_with_receipt(
            device,
            scan_id="stable-receipt",
            max_scrolls=5,
            distance_ratio=0.68,
        )
        self.assertEqual(0, receipt.swipes)
        self.assertEqual(3, len(receipt.screens))
        self.assertEqual(2, device.swipe_up.call_count)

    def test_exact_measured_viewport_restore_can_omit_redundant_fixed_delays(self) -> None:
        device = mock.Mock()
        with mock.patch.object(driver.time, "sleep") as sleep:
            restored = driver.move_between_measured_viewports(
                device,
                5,
                1,
                distance_ratio=0.68,
                delay_seconds=0.0,
            )
        self.assertEqual(1, restored)
        self.assertEqual(4, device.swipe_down.call_count)
        sleep.assert_not_called()

    def test_build_page_exposes_one_real_authority_gated_creation_method_route(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        dashboard_start = source.index("private void AddCreationWizardDashboard()")
        route_start = source.index("private void AddCreationMethodRoute(")
        dashboard = source[dashboard_start:route_start]
        route_end = source.index("private void AddFinalizationReviewAction(", route_start)
        route = source[route_start:route_end]
        stages_start = source.index("private void AddWizardStages(")
        stages_end = source.index("private void AddCompletionBlockers(", stages_start)
        stages = source[stages_start:stages_end]

        self.assertEqual(
            1,
            dashboard.count("AddCreationMethodRoute(snapshot, projection, prerequisite);"),
        )
        self.assertNotIn("VerticalStackLayout method =", dashboard)
        self.assertEqual(1, source.count('automationId: "creation-stage-method"'))
        for marker in (
            "NativeTheme.NavigationRow(",
            "HasAuthoritativePrerequisiteOptions(prerequisite)",
            "Coordinator.CanOpenSr5LifeModuleOrigin()",
            "OpenCreationPrerequisiteAsync",
            "OpenSr5LifeModuleOriginAsync",
            "BuildPageUiProjection.CreationKarmaAuthorityRequired",
            "static () => Task.CompletedTask",
            "enabled: canOpen",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, route)
        self.assertNotIn("enabled: true", route)
        method_guard = stages.index("CharacterCreationWizardStepIds.Method")
        self.assertLess(method_guard, stages.index("NativeTheme.NavigationRow("))
        self.assertIn(
            "continue;",
            stages[method_guard:stages.index("bool foundation", method_guard)],
        )
        self.assertNotIn("AddCreationMethodRoute", stages)

    def test_zero_or_repeated_projected_method_steps_keep_one_canonical_route(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        dashboard_start = source.index("private void AddCreationWizardDashboard()")
        dashboard_end = source.index("private void AddCreationMethodRoute(", dashboard_start)
        dashboard = source[dashboard_start:dashboard_end]
        stages_start = source.index("private void AddWizardStages(")
        stages_end = source.index("private void AddCompletionBlockers(", stages_start)
        stages = source[stages_start:stages_end]

        # Zero projected Method rows still gets the unconditional dedicated route.
        self.assertIn(
            "AddCreationMethodRoute(snapshot, projection, prerequisite);",
            dashboard,
        )
        # Repeated projected Method rows all hit this guard before the generic renderer.
        method_guard = stages.index("CharacterCreationWizardStepIds.Method")
        generic_renderer = stages.index(
            'automationId: $"creation-stage-{Token(stage.StepId)}"',
        )
        self.assertIn("continue;", stages[method_guard:generic_renderer])
        self.assertLess(method_guard, generic_renderer)
        self.assertEqual(1, source.count('automationId: "creation-stage-method"'))

    def test_execute_emits_all_phase_and_scan_timing_into_digest_bound_receipt(self) -> None:
        source = inspect.getsource(driver.execute)
        offsets = [source.index(f'progress.advance("{phase_id}")') for phase_id in driver.PHASE_ORDER]
        self.assertEqual(sorted(offsets), offsets)
        for marker in (
            "scan_observer=progress.record_scan",
            "initial_observation=transition_viewport[0]",
            "resolved_viewport_out=resolved_dashboard_viewport",
            "poll_delay_seconds=0.0",
            "prerequisite_origin = wait_for_prerequisite_scan_origin(device)",
            "initial_observation=prerequisite_origin",
            "delay_seconds=0.0",
            'timing = progress.finish()',
            '"timing": timing',
            '"path": str(progress.evidence_path)',
            '"sha256": sha256(progress.evidence_path)',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertLess(source.index("timing = progress.finish()"), source.index("receipt = {"))

    def test_talent_authority_phases_are_ordered_and_independently_bounded(self) -> None:
        source = inspect.getsource(driver.execute)
        phase_ids = (
            "typed-authority-options",
            "talent-active-skill-grant",
            "talent-active-preview",
            "talent-skill-group-grant",
            "preview-confirm",
        )
        for phase_id in phase_ids:
            with self.subTest(phase_id=phase_id):
                self.assertEqual(150_000, driver.PHASE_BUDGET_MS[phase_id])

        typed = source.index('progress.advance("typed-authority-options")')
        active = source.index('progress.advance("talent-active-skill-grant")')
        active_preview = source.index('progress.advance("talent-active-preview")')
        skill_group = source.index('progress.advance("talent-skill-group-grant")')
        final_preview = source.index('progress.advance("preview-confirm")')
        self.assertEqual(
            [typed, active, active_preview, skill_group, final_preview],
            sorted((typed, active, active_preview, skill_group, final_preview)),
        )
        self.assertLess(
            source.index("active_talent_option_id = tap_enabled_authority_option("),
            active,
        )
        self.assertLess(active, source.index("active_grant_proof = choose_and_prove_talent_grant("))
        self.assertLess(
            source.index("active_grant_proof.current_viewport"),
            active_preview,
        )
        self.assertLess(active_preview, source.index("active_preview_digest = canonical_digest("))
        self.assertLess(
            source.index('typed_selections["talent"] = tap_enabled_authority_option('),
            skill_group,
        )
        self.assertLess(
            skill_group,
            source.index("skill_group_grant_proof = choose_and_prove_talent_grant("),
        )
        self.assertLess(
            source.index("skill_group_grant_proof.current_viewport"),
            final_preview,
        )
        self.assertLess(final_preview, source.index("skill_group_plan_digest ="))
        self.assertEqual(15 * 60 * 1000, driver.TOTAL_PERFORMANCE_TARGET_MS)
        self.assertEqual(
            (len(driver.PHASE_ORDER) + 1) // 2,
            driver.TIMING_ROUNDING_TOLERANCE_MS,
        )

    def test_main_records_failure_evidence_without_converting_it_to_a_pass(self) -> None:
        source = inspect.getsource(driver.main)
        self.assertIn("progress = ProgressRecorder(args.evidence)", source)
        self.assertIn("return execute(args, progress)", source)
        self.assertIn("progress.fail(error)", source)
        self.assertNotIn('"status": "pass"', source)

    def test_build_toolbar_exposes_the_exact_durable_save_notice_in_view(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        base = (NATIVE / "NativePageBase.cs").read_text(encoding="utf-8")

        refresh = source[source.index("protected override void Refresh()") :]
        self.assertIn(
            "_save.Text = BuildPageUiProjection.SaveToolbarText(Coordinator.HasDurableSaveNotice)",
            refresh,
        )
        self.assertIn(
            'string.Equals(_notice, "Saved.", StringComparison.Ordinal)',
            coordinator,
        )
        self.assertIn("_durableSaveNotice?.Matches(State) == true", coordinator)
        save = coordinator[coordinator.index("public async Task SaveAsync") :]
        save = save[: save.index("public async Task ExportAsync")]
        self.assertLess(save.index("_durableSaveNotice = null"), save.index("await _presenter.SaveAsync"))
        self.assertIn("State.ContentRevision == State.SavedRevision", save)
        exception = base[base.index("catch (Exception ex)") :]
        exception = exception[: exception.index("finally")]
        self.assertLess(exception.index("Refresh();"), exception.index("DisplayAlertAsync"))
        self.assertLess(refresh.index("_save.Text ="), refresh.index("_save.IsEnabled ="))

    def test_new_character_dialog_transition_requires_exact_route_or_product_error(self) -> None:
        route = driver.shared.UiNode({"content-desc": "build-save-runner"})
        device = mock.Mock()
        device.hierarchy.return_value = [route]

        observed = driver.require_new_character_dialog_transition(device, timeout=1)

        self.assertEqual([route], observed)
        device.capture.assert_not_called()

    def test_new_character_dialog_transition_uses_one_fresh_post_marker_snapshot(self) -> None:
        route = driver.shared.UiNode({"content-desc": "build-save-runner"})
        device = mock.Mock()

        with mock.patch.object(
            driver,
            "fresh_hierarchy_timed",
            return_value=[route],
        ) as fresh, mock.patch.object(
            driver,
            "read_only_hierarchy_timed",
        ) as read_only:
            observed = driver.require_new_character_dialog_transition(
                device,
                timeout=1,
                fresh_first=True,
            )

        self.assertEqual([route], observed)
        fresh.assert_called_once()
        read_only.assert_not_called()

    def test_new_character_dialog_transition_retains_its_exact_resolved_viewport(self) -> None:
        route = driver.shared.UiNode({"content-desc": "build-save-runner"})

        class TransitionDevice:
            def hierarchy(self):
                return [route]

        retained: list[driver.PriorityRankOrigin] = []
        with mock.patch.object(
            driver.time,
            "perf_counter",
            side_effect=[1.0, 1.004],
        ):
            observed = driver.require_new_character_dialog_transition(
                TransitionDevice(),
                timeout=1,
                resolved_viewport_out=retained,
                fresh_first=True,
            )
        self.assertEqual([route], observed)
        self.assertEqual(1, len(retained))
        self.assertIs(observed, retained[0].nodes)
        self.assertEqual((4,), retained[0].hierarchy_durations_ms)
        self.assertEqual(0, retained[0].reverse_swipes)

    def test_creation_dashboard_handoff_reuses_one_exact_transition_snapshot(self) -> None:
        nodes = [
            driver.shared.UiNode(
                {
                    "resource-id": "phone-runner-page",
                    "class": "android.view.ViewGroup",
                    "enabled": "true",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": "phone-runner-create",
                    "class": "android.widget.TextView",
                    "text": "CREATION RUNNER",
                    "enabled": "true",
                    "clickable": "false",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": "creation-wizard-dashboard",
                    "enabled": "true",
                }
            ),
        ]
        device = mock.Mock()

        driver.require_initial_creation_dashboard_snapshot(device, nodes)

        device.hierarchy.assert_not_called()
        device.capture.assert_not_called()

        with self.assertRaisesRegex(RuntimeError, "one exact creation dashboard"):
            driver.require_initial_creation_dashboard_snapshot(device, nodes[:-1])
        device.capture.assert_called_once_with(
            "creation-priority-dashboard-handoff-cardinality-invalid"
        )

    def test_new_character_dialog_transition_surfaces_exact_product_error(self) -> None:
        surface = driver.shared.UiNode(
            {"resource-id": "com.myexternalbrain.chummer:id/dialog-surface"}
        )
        error = driver.shared.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/dialog-error",
                "text": "Canonical ruleset loader rejected the pending runner.",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [surface, error]

        with self.assertRaisesRegex(
            RuntimeError,
            "Canonical ruleset loader rejected the pending runner",
        ):
            driver.require_new_character_dialog_transition(device, timeout=1)

        device.capture.assert_called_once_with("creation-priority-dialog-product-error")

    def test_new_character_dialog_transition_rejects_ambiguous_error_nodes(self) -> None:
        error = driver.shared.UiNode(
            {"resource-id": "com.myexternalbrain.chummer:id/dialog-error"}
        )
        device = mock.Mock()
        device.hierarchy.return_value = [error, error]

        with self.assertRaisesRegex(RuntimeError, "transition was ambiguous"):
            driver.require_new_character_dialog_transition(device, timeout=1)

        device.capture.assert_called_once_with(
            "creation-priority-dialog-transition-cardinality-invalid"
        )

    def test_dialog_action_atomically_flushes_pending_text_before_creation(self) -> None:
        source = (NATIVE / "NativeDialogPage.cs").read_text(encoding="utf-8")
        gate = source[source.index("internal sealed class NativeDialogInteractionGate") :]
        render = source[
            source.index("private void Render(") : source.index(
                "private static string Token("
            )
        ]
        commit = source[
            source.index("private async Task CommitPendingTextFieldsCoreAsync(") : source.index(
                "private bool TryResolveActiveField("
            )
        ]
        execute = source[
            source.index("private async Task ExecuteAsync(") : source.index(
                "private async Task HandleInteractionFailureAsync("
            )
        ]
        dialog_shape = source[
            source.index("private static bool DialogShapeMatches(") : source.index(
                "private bool TryResolveActiveAction("
            )
        ]
        unfocused_update = source[
            source.index("private Task UpdateFieldAsync(") : source.index(
                "private async Task CommitPendingTextFieldsCoreAsync("
            )
        ]

        self.assertIn("_renderGeneration = _interactionGate.BeginRender();", render)
        self.assertIn("_pendingTextFields.Clear();", render)
        self.assertIn('AutomationId = "dialog-surface"', render)
        self.assertIn('errorLabel.AutomationId = "dialog-error";', render)
        self.assertEqual(2, render.count("PendingTextField pending = new(binding,"))
        self.assertEqual(2, render.count("_pendingTextFields.Add(pending);"))
        self.assertEqual(4, render.count("await UpdateFieldAsync(binding,"))
        self.assertIn("NativeDialogActionBinding binding = new(", render)
        self.assertIn("await ExecuteAsync(binding)", render)
        for marker in (
            "PendingTextField[] pending = _pendingTextFields.ToArray();",
            "foreach (PendingTextField pendingField in pending)",
            "NativeDialogFieldBinding binding = pendingField.Binding;",
            "TryResolveActiveField(binding, out DesktopDialogField field)",
            "string? value = pendingField.ReadValue();",
            "string.Equals(field.Value, value, StringComparison.Ordinal)",
            "await _coordinator.UpdateDialogFieldAsync(binding.FieldId, value);",
            "TryResolveActiveField(binding, out _)",
        ):
            self.assertIn(marker, commit)
        for forbidden in (
            "Task.Delay",
            "SaveAsync(",
            "ExecuteDialogActionAsync",
            "WaitAsync",
            "Release()",
        ):
            self.assertNotIn(forbidden, commit)
        for reordering in ("OrderBy", "Reverse", "Distinct"):
            self.assertNotIn(reordering, commit)

        self.assertIn(
            "_interactionGate.RunFieldUpdateAsync(binding.RenderGeneration",
            unfocused_update,
        )
        self.assertIn("TryResolveActiveField(binding", unfocused_update)

        self.assertIn("if (!_interactionGate.TryClaimAction())", execute)
        self.assertIn("await _interactionGate.RunClaimedActionAsync(", execute)
        self.assertIn("await CommitPendingTextFieldsCoreAsync();", execute)
        self.assertIn("TryResolveActiveAction(binding, out DesktopDialogAction action)", execute)
        self.assertEqual(1, execute.count("await _coordinator.ExecuteDialogActionAsync(action.Id);"))
        self.assertLess(
            execute.index("await CommitPendingTextFieldsCoreAsync();"),
            execute.index("await _coordinator.ExecuteDialogActionAsync(action.Id);"),
        )
        for forbidden in ("Task.Delay", "SaveAsync(", "_executing"):
            self.assertNotIn(forbidden, execute)

        self.assertIn("for (int index = 0; index < rendered.Fields.Count; index++)", dialog_shape)
        self.assertIn("DesktopDialogField activeField = active.Fields[index];", dialog_shape)
        self.assertIn(
            "string.Equals(renderedField.Id, activeField.Id, StringComparison.Ordinal)",
            dialog_shape,
        )

        for marker in (
            "Task _tail = Task.CompletedTask;",
            "TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);",
            "predecessor = _tail;",
            "_tail = completion.Task;",
            "await predecessor;",
            "if (!IsCurrentRender(renderGeneration))",
            "if (_closed || _closeRequested || _actionClaimed)",
            "await EnqueueAsync(async () =>",
        ):
            self.assertIn(marker, gate)
        for field_shape in (
            "RenderGeneration",
            "IsMultiline",
            "IsReadOnly",
            "LayoutSlot",
            "VisualKind",
            "OptionsSignature",
        ):
            self.assertIn(field_shape, gate)
        self.assertNotIn("Task.Delay", gate)

    def test_native_dialog_hostile_runtime_harness_is_wired_into_builds(self) -> None:
        test_root = REPO / "tests" / "Chummer.Android.Native.InteractionTests"
        project = (test_root / "Chummer.Android.Native.InteractionTests.csproj").read_text(
            encoding="utf-8"
        )
        runtime = (test_root / "Program.cs").read_text(encoding="utf-8")
        solution = (REPO / "Chummer.Android.slnx").read_text(encoding="utf-8")
        debug_build = (REPO / "scripts" / "build-debug.sh").read_text(encoding="utf-8")
        compile_build = (
            REPO / "scripts" / "compile-native-release-no-package.sh"
        ).read_text(encoding="utf-8")

        self.assertIn("../Chummer.Android.Native.CompileCheck", project)
        for hostile_test in (
            "QueuedOlderUnfocusedCannotOverwriteActionInputAsync",
            "StaleGenerationAndSameIdShapeChangesFailClosedAsync",
            "ReadOnlyTransitionFailsClosedAsync",
            "DoubleTapExecutesExactlyOnceAsync",
            "CloseWaitsForClaimedActionAsync",
            "FailureRerendersBeforeQueueAdvancesAsync",
        ):
            self.assertIn(hostile_test, runtime)
        self.assertNotIn("Task.Delay", runtime)
        self.assertIn("tests/Chummer.Android.Native.InteractionTests", solution)
        for script in (debug_build, compile_build):
            interaction_build = script.index(
                '"$dotnet_command" build "$interaction_tests_path"'
            )
            interaction_run = script.index('"$dotnet_command" run', interaction_build)
            serial_build = script[interaction_build:interaction_run]
            run_without_rebuild = script[interaction_run:]
            for authority in (
                "-m:1",
                "-p:BuildInParallel=false",
                "-p:ChummerDesktopRuntimeIdentifiers=",
                "-p:ChummerUseLocalCompatibilityTree=true",
            ):
                self.assertIn(authority, serial_build)
            self.assertIn("--no-build", run_without_rebuild)
            self.assertLess(interaction_build, interaction_run)

    def test_creation_karma_navigation_precondition_remains_fail_closed(self) -> None:
        blocked = driver.shared.UiNode(
            {
                "content-desc": "Creation method. creation-karma-authority-required",
                # Android UIAutomator exposes the installed MAUI Button's handler capability even
                # while IsEnabled=false. The driver separately taps and proves no navigation.
                "clickable": "true",
                "enabled": "false",
            }
        )
        ready = driver.shared.UiNode(
            {
                "content-desc": (
                    "Creation method. Choose five ordered Priority ranks from exact Core authority"
                ),
                "clickable": "true",
                "enabled": "true",
            }
        )
        self.assertIn(
            "creation-karma-authority-required",
            driver.require_creation_method_navigation(blocked, ready=False),
        )
        self.assertIn(
            "exact Core authority",
            driver.require_creation_method_navigation(ready, ready=True),
        )
        with self.assertRaisesRegex(RuntimeError, "did not enable"):
            driver.require_creation_method_navigation(blocked, ready=True)
        with self.assertRaisesRegex(RuntimeError, "did not remain fail-closed"):
            driver.require_creation_method_navigation(ready, ready=False)
        with self.assertRaisesRegex(RuntimeError, "did not remain fail-closed"):
            driver.require_creation_method_navigation(
                driver.shared.UiNode(
                    {
                        "content-desc": (
                            "Creation method. creation-karma-authority-required"
                        ),
                        "clickable": "false",
                        "enabled": "true",
                    }
                ),
                ready=False,
            )
        self.assertIn(
            "creation-karma-authority-required",
            driver.require_creation_method_navigation(
                driver.shared.UiNode(
                    {
                        "content-desc": (
                            "Creation method. creation-karma-authority-required"
                        ),
                        "clickable": "false",
                        "enabled": "false",
                    }
                ),
                ready=False,
            ),
        )

    def test_prerequisite_binding_requires_revision_and_both_digest_prefixes(self) -> None:
        authority = driver.require_prerequisite_binding(
            "Revision 7 · saved 7 · snapshot 0123456789ab · authority abcdef012345"
        )
        self.assertEqual(7, authority["contentRevision"])
        self.assertEqual("0123456789ab", authority["snapshotDigestPrefix"])
        self.assertEqual("abcdef012345", authority["authorityDigestPrefix"])
        with self.assertRaisesRegex(RuntimeError, "did not expose exact"):
            driver.require_prerequisite_binding(
                "Revision 7 · saved 7 · snapshot unavailable · authority unavailable"
            )

    def test_physical_blocked_tap_rechecks_same_row_before_scrolled_dashboard_marker(self) -> None:
        blocked = driver.shared.UiNode(
            {
                "content-desc": "Creation method. creation-karma-authority-required",
                "clickable": "true",
                "enabled": "false",
                "bounds": "[98,1510][984,1663]",
            }
        )

        class FakeScrolledDevice:
            def __init__(self) -> None:
                self.reset_count = 0
                self.taps: list[tuple[str, ...]] = []
                self.captures: list[str] = []

            def find(self, selector: str):
                if selector == "creation-stage-method":
                    return blocked
                if selector == "creation-prerequisite-page":
                    return None
                if selector == "creation-wizard-dashboard":
                    return None if self.reset_count < 2 else driver.shared.UiNode({})
                return None

            @staticmethod
            def hierarchy():
                return [driver.shared.UiNode({"resource-id": "creation-wizard-dashboard"})]

            def shell(self, *arguments: str) -> str:
                self.taps.append(arguments)
                return ""

            def capture(self, name: str) -> None:
                self.captures.append(name)

            def swipe_up(self, **_kwargs) -> None:
                raise AssertionError("The already-visible blocked row must not need another scroll")

            def wait(self, selector: str, *, timeout: int):
                self.assert_dashboard_was_reset(selector, timeout)
                return driver.shared.UiNode({})

            def assert_dashboard_was_reset(self, selector: str, timeout: int) -> None:
                if selector != "creation-wizard-dashboard" or timeout != 30:
                    raise AssertionError((selector, timeout))
                if self.reset_count < 2:
                    raise AssertionError("Dashboard marker was checked before resetting the viewport")

        device = FakeScrolledDevice()

        def reset_scroll(_device, *, swipes: int) -> None:
            self.assertIs(device, _device)
            self.assertEqual(22, swipes)
            device.reset_count += 1

        def open_dashboard(_device, **kwargs):
            self.assertIs(device, _device)
            self.assertEqual(
                {
                    "open_build_route": False,
                    "dashboard_timeout": 30,
                    "reset_swipes": 22,
                },
                kwargs,
            )
            if device.reset_count != 1:
                raise AssertionError("Blocked-row viewport was not reset before route proof")
            device.reset_count += 1
            return driver.shared.UiNode({})

        with mock.patch.object(driver.shared, "reset_scroll_to_top", side_effect=reset_scroll), \
             mock.patch.object(
                 driver.shared,
                 "open_creation_dashboard",
                 side_effect=open_dashboard,
             ), \
             mock.patch.object(driver.time, "sleep"):
            evidence = driver.wait_creation_method_navigation(device, ready=False)

        self.assertEqual(2, device.reset_count)
        self.assertEqual([("input", "tap", "541", "1586")], device.taps)
        self.assertEqual(["creation-method-navigation-remained-blocked"], device.captures)
        self.assertFalse(evidence["enabled"])
        self.assertTrue(evidence["clickable"])
        self.assertEqual(
            {
                "detail": "Creation method. creation-karma-authority-required",
                "clickable": True,
                "enabled": False,
            },
            evidence["afterTap"],
        )
        self.assertTrue(evidence["tapRemainedOnDashboard"])

    def test_method_navigation_waits_for_bound_async_authority_before_asserting_blocker(self) -> None:
        blocked = driver.shared.UiNode(
            {
                "content-desc": "Creation method. creation-karma-authority-required",
                "clickable": "true",
                "enabled": "false",
                "bounds": "[98,1510][984,1663]",
            }
        )

        class AsyncAuthorityDevice:
            def __init__(self) -> None:
                self.loading_reads = 0
                self.taps: list[tuple[str, ...]] = []
                self.captures: list[str] = []

            def hierarchy(self):
                self.loading_reads += 1
                if self.loading_reads < 3:
                    return [
                        driver.shared.UiNode(
                            {"resource-id": "creation-dashboard-authority-loading"}
                        )
                    ]
                return [driver.shared.UiNode({"resource-id": "creation-wizard-dashboard"})]

            def find(self, selector: str):
                if selector == "creation-stage-method":
                    return blocked
                if selector == "creation-prerequisite-page":
                    return None
                return None

            def shell(self, *arguments: str) -> str:
                self.taps.append(arguments)
                return ""

            def capture(self, name: str) -> None:
                self.captures.append(name)

            def swipe_up(self, **_kwargs) -> None:
                raise AssertionError("The ready blocked row must not need a scroll")

        device = AsyncAuthorityDevice()
        with mock.patch.object(driver.shared, "reset_scroll_to_top"), \
             mock.patch.object(driver.shared, "open_creation_dashboard"), \
             mock.patch.object(driver.time, "sleep") as sleep:
            evidence = driver.wait_creation_method_navigation(device, ready=False)

        self.assertEqual(3, device.loading_reads)
        self.assertEqual(
            [mock.call(0.5), mock.call(0.5), mock.call(1.25)],
            sleep.call_args_list,
        )
        self.assertTrue(evidence["authorityProjectionWaited"])
        self.assertEqual(
            "Creation method. creation-karma-authority-required",
            evidence["detail"],
        )
        self.assertEqual([("input", "tap", "541", "1586")], device.taps)

    def test_dashboard_never_labels_projection_bound_stage_complete_while_loading(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        self.assertIn('return "creation-authority-loading";', source)
        self.assertIn(
            'CreationDashboardAuthorityPhaseState.Loading => "creation-authority-loading"',
            source,
        )
        self.assertIn(
            "projectionBoundStage && !string.IsNullOrWhiteSpace(projectionBlocker)",
            source,
        )

    def test_dashboard_loads_and_merges_creation_authority_in_independent_phases(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        for marker in (
            "_creationPrerequisiteQueue",
            "_creationAttributesQueue",
            "_creationSkillsQueue",
            "Progress.Prerequisite: CreationDashboardAuthorityPhaseState.Ready",
            "Coordinator.LoadCreationPrerequisite",
            "Coordinator.LoadCreationAttributes",
            "Coordinator.LoadCreationSkills",
            "AcceptCreationPrerequisite",
            "AcceptCreationAttributes",
            "AcceptCreationSkills",
            "private static void ResolveCreationPhase<TResult>(",
            "private void ScheduleCreationPhaseAcceptance<TResult>(",
            "TResult result = loader();",
            "accept(request.Key, completed, error);",
            "request.Key.Matches(Coordinator.State, snapshot)",
            "_creationProjection?.Binding.Equals(request.Key) == true",
        ):
            self.assertIn(marker, source)

        global_loading = source[
            source.index("if (projection is null") : source.index("AddBudgetRibbon(")
        ]
        self.assertIn(
            "projection.Progress.Prerequisite == CreationDashboardAuthorityPhaseState.Loading",
            global_loading,
        )
        self.assertNotIn("Progress.Attributes == CreationDashboardAuthorityPhaseState.Loading", global_loading)
        self.assertNotIn("Progress.Skills == CreationDashboardAuthorityPhaseState.Loading", global_loading)

        retry = source[source.index("private void RetryCreationProjection()") :]
        retry = retry[: retry.index("private void AddBudgetRibbon(")]
        self.assertIn("CancelCreationProjectionQueues();", retry)
        self.assertIn("_creationProjection = null;", retry)
        self.assertIn("Refresh();", retry)

        resolver = source[source.index("private CreationDashboardAuthorityProjection? ResolveCreationProjection") :]
        resolver = resolver[: resolver.index("private bool CanAcceptCreationPhase(")]
        self.assertEqual(1, resolver.count("Coordinator.LoadCreationPrerequisite"))
        self.assertEqual(1, resolver.count("Coordinator.LoadCreationAttributes"))
        self.assertEqual(1, resolver.count("Coordinator.LoadCreationSkills"))
        self.assertLess(resolver.index("queue.TryRequest("), resolver.index("TResult result = loader();"))
        self.assertNotIn("Coordinator.LoadCreationPrerequisite()", resolver)
        self.assertNotIn("Coordinator.LoadCreationAttributes()", resolver)
        self.assertNotIn("Coordinator.LoadCreationSkills()", resolver)

    def test_dashboard_bootstrap_does_not_require_authority_before_loading_it(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        binding = source[
            source.index("public sealed record CreationDashboardProjectionBinding") :
            source.index("public enum CreationDashboardAuthorityPhaseState")
        ]

        self.assertIn("!IsBootstrapAuthorityBindingValue(snapshot.SourceDigest)", binding)
        self.assertIn("!IsBootstrapAuthorityBindingValue(snapshot.RuntimeFingerprint)", binding)
        self.assertIn("value.Length == 0 || !string.IsNullOrWhiteSpace(value)", binding)
        self.assertIn("snapshot.SourceDigest,", binding)
        self.assertIn("snapshot.RuntimeFingerprint,", binding)
        self.assertIn("snapshot.ContentDigest", binding)
        self.assertIn("snapshot.WorkspaceRevision != state.ContentRevision", binding)
        self.assertIn("string.IsNullOrWhiteSpace(snapshot.SnapshotDigest)", binding)

    def test_dashboard_recovers_terminal_projection_after_deferred_page_dispatch(self) -> None:
        page_source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        queue_source = (NATIVE / "LatestBackgroundProjectionQueue.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("queue.TryTake(request, out TResult completed", page_source)
        self.assertIn("_creationPrerequisiteQueue.Completed +=", page_source)
        self.assertIn("_creationAttributesQueue.Completed +=", page_source)
        self.assertIn("_creationSkillsQueue.Completed +=", page_source)
        self.assertIn("MainThread.BeginInvokeOnMainThread", page_source)
        self.assertIn("current.TryReadOutcome(out result, out error)", queue_source)
        self.assertIn("public bool TryTake(", queue_source)
        self.assertIn("work.MarkResultReady(result);", queue_source)
        self.assertIn("work.MarkFailureReady(exception);", queue_source)

    def test_async_authority_wait_fails_closed_for_explicit_failure_and_timeout(self) -> None:
        class ProjectionDevice:
            def __init__(self, *, failed: bool) -> None:
                self.failed = failed
                self.captures: list[str] = []

            def hierarchy(self):
                selector = (
                    "creation-dashboard-authority-failed"
                    if self.failed
                    else "creation-dashboard-authority-loading"
                )
                return [driver.shared.UiNode({"resource-id": selector})]

            def capture(self, name: str) -> None:
                self.captures.append(name)

        failed = ProjectionDevice(failed=True)
        with self.assertRaisesRegex(RuntimeError, "explicit authority projection failure"):
            driver.wait_creation_dashboard_authority(failed)
        self.assertEqual(["creation-dashboard-authority-failed"], failed.captures)

        pending = ProjectionDevice(failed=False)
        with mock.patch.object(
            driver.time,
            "monotonic",
            side_effect=[10.0, 10.0, 40.1, 40.1],
        ), \
             mock.patch.object(driver.time, "sleep"), \
             mock.patch.object(
                 driver,
                 "capture_creation_authority_pending_timeout_diagnostics",
             ) as capture_timeout:
            with self.assertRaisesRegex(RuntimeError, "remained pending"):
                driver.wait_creation_dashboard_authority(pending, timeout=30.0)
        capture_timeout.assert_called_once_with(pending, timeout=30.0)
        self.assertEqual([], pending.captures)

    def test_async_authority_wait_preserves_timeout_when_diagnostics_fail(self) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = [
            driver.shared.UiNode(
                {"resource-id": "creation-dashboard-authority-loading"}
            )
        ]
        with mock.patch.object(
            driver.time,
            "monotonic",
            side_effect=[10.0, 10.0, 40.1, 40.1],
        ), \
             mock.patch.object(
                 driver,
                 "capture_creation_authority_pending_timeout_diagnostics",
                 side_effect=RuntimeError("diagnostic transport failed"),
             ) as capture_timeout, \
             mock.patch.object(driver, "_write_pending_timeout_artifact") as write_error:
            with self.assertRaisesRegex(
                RuntimeError,
                "Creation dashboard authority projection remained pending past the bounded wait",
            ):
                driver.wait_creation_dashboard_authority(device, timeout=30.0)

        capture_timeout.assert_called_once_with(device, timeout=30.0)
        write_error.assert_called_once()
        self.assertTrue(
            write_error.call_args.args[1].endswith("-collection-error.txt")
        )

    def test_pending_authority_timeout_bundle_is_pid_bound_and_never_anr_named(self) -> None:
        class DiagnosticDevice:
            def __init__(self, evidence: Path) -> None:
                self.evidence = evidence
                self.shell_calls: list[tuple[tuple[str, ...], int]] = []
                self.run_calls: list[tuple[tuple[str, ...], int, bool]] = []

            def shell(self, *arguments: str, timeout: int = 120) -> str:
                self.shell_calls.append((arguments, timeout))
                if arguments == ("pidof", driver.shared.PACKAGE):
                    return "42 invalid 7 42"
                if arguments[:2] == ("kill", "-3"):
                    return ""
                if arguments[:2] == ("debuggerd", "-b"):
                    return f"native backtrace for {arguments[2]}"
                if arguments[:2] == ("uiautomator", "dump"):
                    return "UI hierarchy dumped"
                return "diagnostic output"

            def run(
                self,
                *arguments: str,
                timeout: int = 120,
                text: bool = True,
            ) -> subprocess.CompletedProcess:
                self.run_calls.append((arguments, timeout, text))
                if arguments[:2] == ("exec-out", "cat"):
                    return subprocess.CompletedProcess(
                        arguments,
                        0,
                        stdout='<hierarchy rotation="0"><node /></hierarchy>',
                        stderr="",
                    )
                if arguments == ("exec-out", "screencap", "-p"):
                    return subprocess.CompletedProcess(
                        arguments,
                        0,
                        stdout=b"\x89PNG\r\n\x1a\nproof",
                        stderr=b"",
                    )
                raise AssertionError(f"Unexpected diagnostic run: {arguments!r}")

        with tempfile.TemporaryDirectory() as directory:
            device = DiagnosticDevice(Path(directory))
            with mock.patch.object(driver.time, "sleep") as sleep:
                manifest = driver.capture_creation_authority_pending_timeout_diagnostics(
                    device,
                    timeout=30.0,
                )

            prefix = driver.CREATION_AUTHORITY_PENDING_TIMEOUT_PREFIX
            expected_artifacts = {
                f"{prefix}-process-ids.txt",
                f"{prefix}-managed-thread-signal.txt",
                f"{prefix}-native-backtrace.txt",
                f"{prefix}-activity-activities.txt",
                f"{prefix}-activity-processes.txt",
                f"{prefix}-window-windows.txt",
                f"{prefix}-hierarchy.xml",
                f"{prefix}-screenshot.png",
                f"{prefix}-logcat.txt",
            }
            artifact_names = {
                artifact["name"]
                for artifact in manifest["artifacts"]
            }
            self.assertEqual(expected_artifacts, artifact_names)
            self.assertEqual(["7", "42"], manifest["processIds"])
            self.assertEqual(
                "creation-dashboard-authority-pending-timeout",
                manifest["diagnosticKind"],
            )
            self.assertEqual(
                driver.CREATION_AUTHORITY_PENDING_TIMEOUT_HIERARCHY,
                manifest["hierarchySource"],
            )
            self.assertNotIn("anr", json.dumps(manifest).casefold())
            self.assertNotIn("anr", prefix.casefold())
            self.assertEqual(
                expected_artifacts
                | {driver.CREATION_AUTHORITY_PENDING_TIMEOUT_MANIFEST},
                {path.name for path in Path(directory).iterdir()},
            )
            stored_manifest = json.loads(
                (Path(directory) / driver.CREATION_AUTHORITY_PENDING_TIMEOUT_MANIFEST)
                .read_text(encoding="utf-8")
            )
            self.assertEqual(manifest, stored_manifest)
            self.assertTrue(
                all(
                    len(artifact["sha256"]) == 64
                    and artifact["sizeBytes"] > 0
                    for artifact in manifest["artifacts"]
                )
            )
            self.assertEqual([mock.call(0.75)], sleep.call_args_list)

        shell_commands = [call[0] for call in device.shell_calls]
        for process_id in ("7", "42"):
            self.assertIn(("kill", "-3", process_id), shell_commands)
            self.assertIn(("debuggerd", "-b", process_id), shell_commands)
        for command in (
            ("dumpsys", "activity", "activities"),
            ("dumpsys", "activity", "processes"),
            ("dumpsys", "window", "windows"),
            ("logcat", "-d", "-b", "all", "-v", "threadtime", "-t", "4000"),
        ):
            self.assertIn(command, shell_commands)
        self.assertIn(
            (
                "uiautomator",
                "dump",
                "--compressed",
                driver.CREATION_AUTHORITY_PENDING_TIMEOUT_HIERARCHY,
            ),
            shell_commands,
        )
        self.assertIn(
            (("exec-out", "screencap", "-p"), 15, False),
            device.run_calls,
        )

    def test_direct_priority_bootstrap_does_not_require_a_second_saved_workspace(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        self.assertNotIn("require_priority_created_workspace_authority", source)
        self.assertNotIn("freshRunnerWorkspaceAuthority", source)
        self.assertNotIn("preparedWorkspaceAuthority", source)
        self.assertIn('"dashboardBinding": dashboard_binding', source)

    def test_coordinator_uses_only_the_core_prerequisite_boundary_and_refreshes_receipt(self) -> None:
        source = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        for marker in (
            "internal CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>",
            "internal async Task<CreationPrerequisitePhoneConfirmResult>",
            "ICharacterCreationPrerequisiteService creationPrerequisiteService",
            "_creationPrerequisiteService.Load(",
            "new CharacterCreationPrerequisiteLoadRequest(workspaceId)",
            "_creationPrerequisiteService.Preview(",
            "new CharacterCreationPrerequisitePreviewRequest(",
            "HeritageSelectionId = selections.HeritageSelectionId",
            "TalentSelectionId = selections.TalentSelectionId",
            "TalentActiveSkillSelectionIds = selections.TalentActiveSkillSelectionIds.ToArray()",
            "TalentSkillGroupSelectionIds = selections.TalentSkillGroupSelectionIds.ToArray()",
            "_creationPrerequisiteService.Confirm(",
            "new CharacterCreationPrerequisiteConfirmRequest(",
            "preview.PreviewDigest",
            "ExplicitlyConfirmed: true",
            "await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken)",
            "CreationPrerequisitePhoneAuthority.ReceiptMatches(",
            "!preview.RequiresExplicitConfirmation",
            "!preview.CanConfirm",
            "CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(preview.PreviewDigest)",
            "CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision",
            "CharacterCreationPrerequisiteBlockers.PreviewDigestMismatch",
        ):
            self.assertIn(marker, source)

        prerequisite_region = source[
            source.index("LoadCreationPrerequisite()") : source.index(
                "public CharacterCreationFoundationInteractionLoadResult LoadCreationFoundation()"
            )
        ]
        for forbidden in (
            "AttributeEditRequest",
            "ApplyAttributeEditAsync",
            "System.Xml",
            "SaveAsync(",
            "UpdateMetadataAsync",
        ):
            self.assertNotIn(forbidden, prerequisite_region)

    def test_phone_draft_is_exact_bound_and_enforces_projected_profiles(self) -> None:
        source = (NATIVE / "CreationPrerequisitePhoneDraft.cs").read_text(encoding="utf-8")
        for marker in (
            "CharacterCreationPriorityCategoryIds.Ordered",
            "CreationPrerequisitePhoneAuthority.BindingEquals(_binding, state.Binding)",
            "state.SnapshotDigest",
            "state.Binding.RawCharacterXmlDigest",
            "state.Binding.AuxiliaryStateDigest",
            "state.Binding.AuthorityDigest",
            "state.Authority.Options",
            "ResolveUniqueOption(state, categoryId, rank)",
            "state.Authority.PriorityArray",
            "state.Authority.RankWeights",
            "PriorityRankExhausted",
            "CanReachSumToTenTarget(",
            "SumToTenTargetUnreachable",
            "state.Authority.SumToTenTarget",
            "RestorePendingDraft(state, overview)",
            "pending.DraftRevision",
            "pending.DraftDigest",
            "pending.Assignments",
            "pending.HeritageSelection",
            "pending.TalentSelection",
            "AssignmentMatchesOption(assignment, option)",
            "HeritageSelectionMatchesOption(",
            "TalentSelectionMatchesOption(",
            "PendingDraftMatchesAuthority(refreshed, pending)",
            "receipt.CharacterDocumentChanged",
            "refreshed.PendingDraft is { } pending",
        ):
            self.assertIn(marker, source)

        for forbidden in (
            '"A", "B", "C", "D", "E"',
            "AttributeEditRequest",
            "ApplyAttributeEditAsync",
            "System.Xml",
            "Preferences.Default",
            "HttpClient",
        ):
            self.assertNotIn(forbidden, source)

    def test_nested_authority_rejects_malformed_and_duplicate_projections(self) -> None:
        source = (NATIVE / "CreationPrerequisitePhoneDraft.cs").read_text(encoding="utf-8")
        for marker in (
            "HasExactNestedAuthority(state.Authority.Options)",
            "option.HeritageOptions is { Count: > 0 }",
            "option.TalentOptions is { Count: > 0 }",
            "option.HeritageOptions.All(IsExactHeritageOption)",
            "option.TalentOptions.All(IsExactTalentOption)",
            ".Distinct(StringComparer.Ordinal)",
            "Count() == option.HeritageOptions.Count",
            "Count() == option.TalentOptions.Count",
            "_ => option.HeritageOptions is { Count: 0 }",
            "&& option.TalentOptions is { Count: 0 }",
        ):
            self.assertIn(marker, source)
        self.assertNotIn(".Where(CreationPrerequisitePhoneAuthority.IsExactHeritageOption)", source)
        self.assertNotIn(".Where(CreationPrerequisitePhoneAuthority.IsExactTalentOption)", source)

    def test_heritage_identity_rejects_kind_specific_forgery(self) -> None:
        source = (NATIVE / "CreationPrerequisitePhoneDraft.cs").read_text(encoding="utf-8")
        heritage = source[
            source.index("public static bool IsExactHeritageOption(") :
            source.index("public static bool HasExactNestedAuthority(")
        ]
        for marker in (
            "CharacterCreationPriorityChildKinds.Metatype",
            "option.MetavariantSourceId is not null || option.MetavariantName is not null",
            "Guid.TryParseExact(",
            "option.MetavariantSourceId",
            "metavariantSourceId != Guid.Empty",
            "isMetavariant && string.IsNullOrWhiteSpace(option.MetavariantName)",
        ):
            self.assertIn(marker, heritage)

    def test_disabled_negative_metavariant_matches_core_authority_contract(self) -> None:
        source = (NATIVE / "CreationPrerequisitePhoneDraft.cs").read_text(encoding="utf-8")
        heritage = source[
            source.index("public static bool IsExactHeritageOption(") :
            source.index("public static bool HasExactNestedAuthority(")
        ]
        for marker in (
            "option.KarmaCost < 0",
            "isMetavariant",
            "!option.IsEnabled",
            "option.Blockers.Count > 0",
        ):
            self.assertIn(marker, heritage)
        self.assertNotIn("option.KarmaCost < 0 ||", heritage)

    def test_disabled_unresolved_heritage_matches_pinned_core_projection(self) -> None:
        chummer5 = Path(os.environ.get("CHUMMER5A_ROOT", "/docker/chummer5a"))
        priorities_path = chummer5 / "Chummer" / "data" / "priorities.xml"
        metatypes_path = chummer5 / "Chummer" / "data" / "metatypes.xml"
        self.assertTrue(priorities_path.is_file(), priorities_path)
        self.assertTrue(metatypes_path.is_file(), metatypes_path)

        priorities = ET.parse(priorities_path).getroot()
        metatypes = ET.parse(metatypes_path).getroot()
        source_metatypes: dict[str, list[ET.Element]] = {}
        for source in metatypes.find("metatypes").findall("metatype"):
            source_metatypes.setdefault(source.findtext("name", ""), []).append(source)

        unresolved: set[tuple[str, str, str]] = set()
        for rank in priorities.find("priorities").findall("priority"):
            if rank.findtext("category") != "Heritage":
                continue
            rank_id = rank.findtext("value", "")
            for child in rank.find("metatypes").findall("metatype"):
                metatype_name = child.findtext("name", "")
                matches = source_metatypes.get(metatype_name, [])
                if len(matches) != 1:
                    unresolved.add((rank_id, "metatype", metatype_name))
                    source_variants: list[ET.Element] = []
                else:
                    variants = matches[0].find("metavariants")
                    source_variants = [] if variants is None else variants.findall("metavariant")
                projected_variants = child.find("metavariants")
                for variant in ([] if projected_variants is None else projected_variants.findall("metavariant")):
                    variant_name = variant.findtext("name", "")
                    if sum(item.findtext("name") == variant_name for item in source_variants) != 1:
                        unresolved.add((rank_id, "metavariant", f"{metatype_name}/{variant_name}"))

        self.assertEqual(
            {
                ("A,4", "metatype", "E-Ghost"),
                ("A,4", "metavariant", "Troll/Cyclopean"),
                ("B,3", "metavariant", "Troll/Cyclopean"),
                ("C,2", "metavariant", "Dwarf/Goblin"),
            },
            unresolved,
        )

        source = (NATIVE / "CreationPrerequisitePhoneDraft.cs").read_text(encoding="utf-8")
        heritage = source[
            source.index("public static bool IsExactHeritageOption(") :
            source.index("public static bool HasExactNestedAuthority(")
        ]
        disabled_gate = heritage.index("if (!option.IsEnabled)")
        for enabled_only in (
            "Guid.TryParseExact(option.MetatypeSourceId",
            "option.MetatypeSourceNodeDigest",
            "option.Attributes.Count == s_AttributeIds.Length",
        ):
            self.assertGreater(heritage.index(enabled_only), disabled_gate)

    def test_enabled_heritage_requires_complete_identity_digest_and_attributes(self) -> None:
        source = (NATIVE / "CreationPrerequisitePhoneDraft.cs").read_text(encoding="utf-8")
        heritage = source[
            source.index("public static bool IsExactHeritageOption(") :
            source.index("public static bool HasExactNestedAuthority(")
        ]
        disabled_gate = heritage.index("if (!option.IsEnabled)")
        for marker in (
            'Guid.TryParseExact(option.MetatypeSourceId, "D", out Guid metatypeSourceId)',
            "metatypeSourceId != Guid.Empty",
            "Guid.TryParseExact(",
            "option.MetavariantSourceId",
            "metavariantSourceId != Guid.Empty",
            "option.MetatypeSourceNodeDigest",
            "option.Attributes.Count == s_AttributeIds.Length",
            ".SequenceEqual(s_AttributeIds, StringComparer.Ordinal)",
            "attribute.Minimum <= attribute.Maximum",
            "attribute.Maximum <= attribute.AugmentedMaximum",
        ):
            self.assertIn(marker, heritage[disabled_gate:])

    def test_disabled_heritage_still_requires_signed_shape_and_blockers(self) -> None:
        source = (NATIVE / "CreationPrerequisitePhoneDraft.cs").read_text(encoding="utf-8")
        heritage = source[
            source.index("public static bool IsExactHeritageOption(") :
            source.index("public static bool HasExactNestedAuthority(")
        ]
        disabled_gate = heritage.index("if (!option.IsEnabled)")
        for marker in (
            "option.PriorityChildNodeDigest",
            "option.IsEnabled != (option.Blockers.Count == 0)",
            "option.SourceAnchorIds.Count == 0",
            "option.SourceAnchorIds.Any(anchor => string.IsNullOrWhiteSpace(anchor))",
            "string.IsNullOrWhiteSpace(option.SelectionId)",
        ):
            self.assertIn(marker, heritage[:disabled_gate])

    def test_phone_pages_show_projected_typed_choices_and_core_attribute_gate(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        options = (NATIVE / "CreationPriorityCategoryPage.cs").read_text(encoding="utf-8")
        details = (NATIVE / "CreationPriorityDetailPage.cs").read_text(encoding="utf-8")
        grants = (NATIVE / "CreationTalentSkillGrantPage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationPrerequisitePreviewPage.cs").read_text(encoding="utf-8")
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        for marker in (
            'AutomationId = "creation-prerequisite-page"',
            "Coordinator.LoadCreationPrerequisite()",
            "budget.Total",
            "budget.Used",
            "budget.Remaining",
            "state.Authority.PriorityArray",
            "state.Authority.SumToTenTarget",
            "CharacterCreationPriorityCategoryIds.Ordered",
            "new CreationPriorityCategoryPage(",
            "selected.SourceId",
            "selected.BaseNormalAttributePoints",
            "state.Authority.SourceAnchorIds",
            "state.Authority.RawProfileInputsDigest",
            "state.Authority.RawPrioritiesXmlDigest",
            "state.PendingDraft is { } pending",
            '"creation-prerequisite-pending-draft-digest"',
            '"creation-prerequisite-snapshot-digest"',
            '"creation-prerequisite-raw-character-xml-digest"',
            '"creation-prerequisite-auxiliary-state-digest"',
            '"creation-prerequisite-authority-digest"',
            'automationId: "creation-prerequisite-heritage-selection"',
            'automationId: "creation-prerequisite-talent-selection"',
            "new CreationPriorityDetailPage(",
            '"creation-prerequisite-attributes-disabled"',
            "halveattributepoints adjustment",
            "Coordinator.PreviewCreationPrerequisite(state.Binding, assignments, selections)",
            "new CreationPrerequisitePreviewPage(",
        ):
            self.assertIn(marker, page)

        for marker in (
            'AutomationId = "creation-prerequisite-category-page"',
            "_draft.OptionsForCategory(_state, Coordinator.State, _categoryId)",
            "projection.Rank",
            "projection.SourceId",
            "projection.SourceNodeDigest",
            "projection.SourceAnchorIds",
            "projection.SumToTenValue",
            "projection.BaseNormalAttributePoints",
            "option.DisableReason",
            "_draft.TrySelect(_state, Coordinator.State, _categoryId, rank)",
            "Navigation.PopAsync(animated: false)",
        ):
            self.assertIn(marker, options)

        for marker in (
            'AutomationId = $"creation-prerequisite-{Token(_categoryId)}-page"',
            "_draft.HeritageOptions(state, Coordinator.State)",
            "_draft.TalentOptions(state, Coordinator.State)",
            "option.SelectionId",
            "option.SourceAnchorIds",
            "option.Blockers",
            "option.ActiveSkillGrant is not null",
            "option.SkillGroupGrant is { } groupGrant",
            "CreationPrerequisitePhoneAuthority.TalentGrantAuthorityBlockers(option)",
            "new CreationTalentSkillGrantPage(",
            "_draft.TrySelectHeritage(state, Coordinator.State, selectionId)",
            "_draft.TrySelectTalent(state, Coordinator.State, selectionId)",
        ):
            self.assertIn(marker, details)

        for marker in (
            'AutomationId = "creation-prerequisite-talent-grant-page"',
            "grant.Quantity",
            "grant.BaseRating",
            "grant.GrantDigest",
            "grant.SourceAnchorIds",
            "choice.IsExotic",
            "TalentExoticSkillSpecializationRequired",
            "_draft.TryToggleTalentActiveSkill(",
            "_draft.TryToggleTalentSkillGroup(",
            "TalentGrantSelectionsComplete(",
            '"creation-prerequisite-talent-grant-complete"',
            '"creation-prerequisite-talent-grant-recover"',
            '"creation-prerequisite-talent-active-skill-option-{Token(choice.SelectionId)}"',
            '"creation-prerequisite-talent-skill-group-option-{Token(choice.SelectionId)}"',
        ):
            self.assertIn(marker, grants)

        for marker in (
            'AutomationId = "creation-prerequisite-preview-page"',
            "_preview.PreviewDigest",
            '"creation-prerequisite-preview-digest"',
            '"creation-prerequisite-preview-auxiliary-state-digest"',
            "_preview.Assignments",
            "assignment.SourceId",
            "assignment.SourceNodeDigest",
            "assignment.SourceAnchorIds",
            "_preview.CreationKarmaBudget",
            "_preview.SumToTenUsed",
            "_preview.SumToTenTarget",
            "_preview.BaseNormalAttributePoints",
            "_preview.EffectiveNormalAttributePoints",
            "_preview.TotalSpecialAttributePoints",
            "_preview.HeritageSelection",
            "_preview.TalentSelection",
            "talent.GrantPlan",
            '"creation-prerequisite-preview-talent-grant-plan-digest"',
            "_preview.RequiresMetatypeAttributeAdjustment",
            "Coordinator.ConfirmCreationPrerequisiteAsync(",
            'AutomationId = "creation-prerequisite-confirm"',
            'AutomationId = "creation-prerequisite-confirm-receipt"',
            "receipt.DraftDigest",
            '"creation-prerequisite-receipt-draft-digest"',
            '"creation-prerequisite-receipt-auxiliary-state-digest"',
            '"creation-prerequisite-receipt-content-revision"',
            '"creation-prerequisite-receipt-saved-revision"',
            "receipt.CharacterDocumentChanged",
            "refreshed.RequiresMetatypeAttributeAdjustment",
        ):
            self.assertIn(marker, preview)

        for marker in (
            "Coordinator.LoadCreationPrerequisite",
            "IsPrerequisiteStage(stage.StepId, snapshot.BuildMethod)",
            "CreationPrerequisitePhoneAuthority.IsReady(state, Coordinator.State)",
            "new CreationPrerequisitePage(Coordinator)",
            "CharacterCreationWizardStepIds.Method",
            "CharacterCreationBuildMethods.Priority",
            "CharacterCreationBuildMethods.SumToTen",
            "AttributeEditRequest path must",
            "Attributes remain disabled",
        ):
            self.assertIn(marker, dashboard)

        combined = page + options + details + grants + preview
        for forbidden in (
            "AttributeEditRequest",
            "ApplyAttributeEditAsync",
            "NativeCommandPage",
            "TabletBuildPage",
            "System.Xml",
            "Picker",
            "SelectedIndex = 0",
            "SaveAsync(",
            "Skill-grant prompts are not yet available on phone",
        ):
            self.assertNotIn(forbidden, combined)

    def test_priority_child_reuses_exact_parent_authority_without_hiding_fresh_parent_reload(
        self,
    ) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        options = (NATIVE / "CreationPriorityCategoryPage.cs").read_text(
            encoding="utf-8"
        )
        category_refresh = options[
            options.index("protected override void Refresh()") : options.index(
                "private async Task SelectAsync("
            )
        ]

        self.assertEqual(1, page.count("Coordinator.LoadCreationPrerequisite()"))
        self.assertIn("_draft.Bind(state, Coordinator.State);", page)
        self.assertIn("_draft,\n                    state,\n                    category", page)

        self.assertNotIn("Coordinator.LoadCreationPrerequisite", options)
        self.assertIn("CharacterCreationPrerequisiteState state", options)
        self.assertIn("_state = state ?? throw", options)
        self.assertIn("if (!_draft.Matches(_state, Coordinator.State))", category_refresh)
        self.assertLess(
            category_refresh.index("if (!_draft.Matches(_state, Coordinator.State))"),
            category_refresh.index("_draft.OptionsForCategory("),
        )
        self.assertIn(
            "AddBlockers([CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision])",
            category_refresh,
        )

    def test_build_ghost_is_dormant_and_has_no_phone_prerequisite_launch(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationPrerequisitePreviewPage.cs").read_text(encoding="utf-8")
        rook = (NATIVE / "RookConversation.cs").read_text(encoding="utf-8")
        combined = page + preview
        self.assertNotIn("new RookConversationPage(Coordinator)", combined)
        self.assertNotIn("Build Ghost", combined)
        self.assertTrue((NATIVE / "RookConversationPage.cs").is_file())
        for forbidden in (
            "AskRook(",
            "PreviewCreationPrerequisite(",
            "ConfirmCreationPrerequisiteAsync(",
            "ICharacterCreationPrerequisiteService",
        ):
            self.assertNotIn(forbidden, rook)

    def test_api36_driver_covers_phone_back_resume_and_receipt_without_running(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertNotIn('"status": "scripted_not_executed"', source)
        self.assertIn('"status": "pass"', source)
        self.assertIn('"executionStatus": "pass"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"creation-stage-method"', source)
        self.assertIn("require_creation_method_navigation", source)
        self.assertIn("shared.open_creation_dashboard(", source)
        self.assertIn("require_new_character_dialog_transition(", source)
        self.assertIn("observation_out=transition_observation", source)
        self.assertIn("fresh_first=True", source)
        self.assertNotIn('device.wait("dialog-action-complete-new-character-workflow"', source)
        self.assertNotIn('device.wait("Select Metatype Priority"', source)
        execute_source = inspect.getsource(driver.execute)
        initial_source = execute_source[: execute_source.index('progress.advance("priority-ranks")')]
        self.assertNotIn("shared.wait_for_phone_runners(device)", initial_source)
        self.assertIn('required_route_resource_id="phone-runners"', initial_source)
        self.assertIn(
            "require_initial_creation_dashboard_snapshot(device, transition_nodes)",
            initial_source,
        )
        self.assertNotIn("toolbar_timeout=120", initial_source)
        self.assertNotIn("dashboard_timeout=30", initial_source)
        self.assertNotIn("reset_swipes=0", initial_source)
        self.assertNotIn("reset_swipes=48", source)
        self.assertNotIn('device.wait("creation-wizard-dashboard"', source)
        self.assertNotIn("shared.select_android_document", source)
        self.assertNotIn("shared.require_import_authority", source)
        self.assertNotIn("--creation-karma-runner", source)
        self.assertIn("scan_prerequisite_authority", source)
        self.assertIn('"publicPriorityRunnerBootstrappedByCore": "pass"', source)
        self.assertIn('"legacyPriorityContinuationSkipped": "pass"', source)
        self.assertIn('"canonicalPrioritySettingsProfileBound": "pass"', source)
        self.assertIn('"method": "typed-core-bootstrap-from-production-dialog"', source)
        self.assertIn('"settingsProfileId": STANDARD_PRIORITY_SETTINGS_ID', source)
        self.assertIn('"creation-prerequisite-karma-budget"', source)
        self.assertNotIn('"creation-prerequisite-rook"', source)
        self.assertIn('"buildGhostLaunchPostponedAndAbsent": "pass"', source)
        self.assertIn("for category in CATEGORIES:", source)
        self.assertIn('f"creation-prerequisite-category-{category}"', source)
        self.assertIn('f"creation-prerequisite-rank-{category}-"', source)
        self.assertIn('f"creation-prerequisite-{category}-selection"', source)
        self.assertIn('f"creation-prerequisite-{category}-option-"', source)
        self.assertIn('"creation-prerequisite-preview-heritage"', source)
        self.assertIn('"creation-prerequisite-preview-talent"', source)
        self.assertIn('"creation-prerequisite-preview-attributes-ready"', source)
        self.assertIn("Back navigation did not restore", source)
        self.assertIn('"creation-prerequisite-attributes-disabled"', source)
        self.assertIn('"creation-prerequisite-prepare-preview"', source)
        self.assertIn('"creation-prerequisite-confirm"', source)
        self.assertIn('"creation-prerequisite-confirm-receipt"', source)
        self.assertIn('"creation-prerequisite-pending-draft"', source)
        self.assertIn('"creation-prerequisite-attributes-ready"', source)
        self.assertIn("shared.force_stop_and_launch_new_process(device, initial_launch)", source)
        self.assertIn('"beforeForceStop": list(restart.before_force_stop.process_ids)', source)
        self.assertIn('"afterForceStop": list(restart.after_force_stop.process_ids)', source)
        self.assertIn('"restarted": list(restart.restarted.process_ids)', source)
        self.assertIn("require_exact_restored_authority_option(", source)
        self.assertIn('"selectedAuthoritySelectionIds": typed_selection_ids', source)
        self.assertIn('"confirmedDraftDigest": confirmed_draft_digest', source)
        self.assertIn('"previewDigest": preview_digest', source)
        self.assertIn('"previewBindingDigests": preview_binding_digests', source)
        self.assertIn('"confirmedBindingDigests": confirmed_binding_digests', source)
        self.assertIn('"prerequisiteSnapshotDigest": prerequisite_snapshot_digest', source)
        self.assertIn('"sameSessionPersistedAuthority": resumed_authority', source)
        self.assertIn('"restartedPersistedAuthority": restarted_authority', source)
        self.assertIn("read_persisted_prerequisite_authority(device)", source)
        self.assertIn('"characterDocumentChangedFalse": "pass"', source)
        self.assertIn('"buildGhostLaunchPostponedAndAbsent": "pass"', source)
        self.assertIn('"advancedEditorNeverExposedWhileCreatedFalse": "pass"', source)
        self.assertIn("require_binding_matches_canonical_digests(", source)
        for marker in (
            "ACTIVE_SKILL_TALENT_LABEL",
            "SKILL_GROUP_TALENT_LABEL",
            "choose_and_prove_talent_grant(",
            '"Active skills"',
            '"Skill groups"',
            "require_exact_preview_talent_grant_plan(",
            "require_restored_talent_grant(",
            '"talentChangeClearsActiveSkillGrantSlots": "pass"',
            '"skillGroupGrantProcessRestartResume": "pass"',
            '"atomicJsonl"',
            "sha256(progress.events_path)",
            '"artifactBinding": artifact_binding',
            '"artifactBindingSha256": canonical_json_sha256(artifact_binding)',
            '"digestDomain": "raw-file-bytes"',
            '"writeProtocol": "same-directory temporary file then os.replace-compatible Path.replace"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)

    def test_api36_driver_reads_scroll_surfaces_in_native_page_order(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")

        typed = source[source.index("selected: dict[str, str] = {}") :]
        typed = typed[: typed.index("# A plain Back from a category route")]
        self.assertLess(
            typed.index("shared.reset_scroll_to_top(device, swipes=22)"),
            typed.index('"creation-prerequisite-heritage-selection"'),
        )

        preview = source[source.index('device.wait("creation-prerequisite-preview-page"') :]
        preview = preview[: preview.index('device.tap("creation-prerequisite-confirm"')]
        self.assertLess(
            preview.index('f"creation-prerequisite-preview-assignment-{category}"'),
            preview.index('"creation-prerequisite-preview-heritage"'),
        )
        self.assertLess(
            preview.index('"creation-prerequisite-preview-heritage"'),
            preview.index('"creation-prerequisite-preview-talent"'),
        )
        self.assertLess(
            preview.index('"creation-prerequisite-preview-talent"'),
            preview.index('"creation-prerequisite-preview-karma-budget"'),
        )
        self.assertLess(
            preview.index('"creation-prerequisite-preview-karma-budget"'),
            preview.index('"creation-prerequisite-preview-attributes-ready"'),
        )

        receipt = source[source.index("confirmed_revisions = {") :]
        receipt = receipt[: receipt.index('device.capture("creation-prerequisite-confirmed")')]
        self.assertLess(
            receipt.index('"creation-prerequisite-receipt-draft-revision"'),
            receipt.index('"creation-prerequisite-receipt-draft-digest"'),
        )
        self.assertLess(
            receipt.index('"creation-prerequisite-receipt-draft-digest"'),
            receipt.index('"creation-prerequisite-receipt-raw-character-xml-digest"'),
        )

        persisted = source[source.index("def read_persisted_prerequisite_authority(") :]
        persisted = persisted[: persisted.index("def assert_persisted_prerequisite_authority(")]
        self.assertLess(
            persisted.index("shared.reset_scroll_to_top(device, swipes=22)"),
            persisted.index('"creation-prerequisite-binding"'),
        )
        self.assertLess(
            persisted.index('"creation-prerequisite-authority-digest"'),
            persisted.index('"creation-prerequisite-pending-draft"'),
        )
        self.assertLess(
            persisted.index('"creation-prerequisite-pending-draft"'),
            persisted.index('"creation-prerequisite-pending-draft-digest"'),
        )

        resumed = source[source.index("resumed_authority =") :]
        resumed = resumed[: resumed.index("restart =")]
        self.assertLess(
            resumed.index("require_exact_restored_authority_option("),
            resumed.index('"creation-prerequisite-category-attributes"'),
        )

        restored = source[source.index("def require_exact_restored_authority_option(") :]
        restored = restored[: restored.index("def execute(")]
        self.assertIn("device.wait_exact_resource_id_bidirectional(", restored)
        self.assertNotIn("device.tap(", restored)

    def test_api36_phone_only_ci_selects_the_isolated_prerequisite_journey(self) -> None:
        runner = (
            REPO / "scripts" / "run-api36-editing-e2e-ci.sh"
        ).read_text(encoding="utf-8")
        generic = "python3 chummer-android/tests/run_api36_editing_e2e.py"
        prerequisite = "python3 chummer-android/tests/run_api36_creation_prerequisite_e2e.py"
        self.assertIn('if [[ "$profile" != "phone" ]]; then', runner)
        self.assertIn("tablet beta proof is deferred", runner)
        self.assertIn(generic, runner)
        self.assertIn(prerequisite, runner)
        guard = runner.index('if [[ "$profile" != "phone" ]]; then')
        self.assertLess(guard, runner.index(generic))
        self.assertIn(
            'journey="${CHUMMER_E2E_JOURNEY:?CHUMMER_E2E_JOURNEY is required}"',
            runner,
        )
        self.assertIn("  creation-prerequisite)", runner)
        self.assertEqual(1, runner.count(prerequisite))
        self.assertIn(
            'evidence_root="$RUNNER_TEMP/chummer-api36-evidence/$profile/$journey"',
            runner,
        )
        prerequisite_case = runner[
            runner.index("  creation-prerequisite)") :
            runner.index("  career-active-skill-advance)")
        ]
        self.assertIn('--evidence "$evidence_root/screenshots"', prerequisite_case)
        self.assertIn('--receipt "$evidence_root/receipt.json"', prerequisite_case)
        self.assertNotIn(generic, prerequisite_case)
        self.assertNotIn('--creation-karma-runner', runner)
        self.assertNotIn('creation-group-membership-e2e.chum5', runner)


if __name__ == "__main__":
    unittest.main()
