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
AGGREGATE_SPEC = importlib.util.spec_from_file_location(
    "creation_prerequisite_aggregate",
    REPO / "scripts" / "verify-api36-editing-e2e-aggregate.py",
)
assert AGGREGATE_SPEC is not None and AGGREGATE_SPEC.loader is not None
aggregate = importlib.util.module_from_spec(AGGREGATE_SPEC)
AGGREGATE_SPEC.loader.exec_module(aggregate)


class CreationPrerequisiteSourceContractTests(unittest.TestCase):
    @staticmethod
    def canonical_node(
        selector: str,
        **attributes: str,
    ) -> driver.shared.UiNode:
        values = {
            "package": driver.shared.PACKAGE,
            "resource-id": f"{driver.shared.PACKAGE}:id/{selector}",
            "text": selector,
            "enabled": "true",
            "clickable": "true",
            "bounds": "[100,300][900,500]",
        }
        values.update(attributes)
        return driver.shared.UiNode(values)

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

    @classmethod
    def resources_binding_nodes(
        cls,
        option: driver.shared.UiNode,
    ) -> dict[str, driver.shared.UiNode]:
        digest = "sha256:" + "a" * 64
        auxiliary = "b" * 64
        nodes = {
            selector: cls.canonical_node(selector)
            for selector in driver.RESOURCES_BINDING_AUTHORITY_SELECTORS
        }
        nodes.update({
            "creation-resources-binding-content-revision": cls.canonical_node(
                "creation-resources-binding-content-revision", text="2"
            ),
            "creation-resources-binding-saved-revision": cls.canonical_node(
                "creation-resources-binding-saved-revision", text="1"
            ),
            "creation-resources-binding-snapshot-digest": cls.canonical_node(
                "creation-resources-binding-snapshot-digest", text=digest
            ),
            "creation-resources-binding-raw-character-xml-digest": cls.canonical_node(
                "creation-resources-binding-raw-character-xml-digest", text=digest
            ),
            "creation-resources-binding-auxiliary-state-digest": cls.canonical_node(
                "creation-resources-binding-auxiliary-state-digest", text=auxiliary
            ),
            "creation-resources-binding-prerequisite-draft-digest": cls.canonical_node(
                "creation-resources-binding-prerequisite-draft-digest", text=digest
            ),
            "creation-resources-authority-digest": cls.canonical_node(
                "creation-resources-authority-digest", text=digest
            ),
            "creation-resources-budget-priority-nuyen": cls.canonical_node(
                "creation-resources-budget-priority-nuyen", text="50000"
            ),
            "creation-resources-budget-total-starting-nuyen": cls.canonical_node(
                "creation-resources-budget-total-starting-nuyen", text="50000"
            ),
            driver.RESOURCES_ZERO_CONVERSION_OPTION_ID: option,
        })
        return nodes

    @classmethod
    def dashboard_route_nodes(cls) -> list[driver.shared.UiNode]:
        return [
            cls.canonical_node("phone-runner-create"),
            cls.canonical_node("creation-wizard-dashboard"),
            driver.shared.UiNode(
                {
                    "resource-id": "",
                    "package": driver.shared.PACKAGE,
                    "class": "android.widget.Button",
                    "content-desc": "build-save-runner",
                    "enabled": "true",
                    "clickable": "true",
                    "focusable": "true",
                    "bounds": "[954,138][1080,264]",
                }
            ),
        ]

    @staticmethod
    def record_required_method_reacquisition(
        progress: driver.ProgressRecorder,
    ) -> None:
        progress.record_scan({
            "scanId": driver.CREATION_METHOD_REACQUISITION_SCAN_ID,
            "status": "resolved",
            "direction": driver.CREATION_METHOD_REACQUISITION_DIRECTION,
            "distanceRatio": driver.DASHBOARD_SCAN_GESTURE_RATIO,
            "screens": 1,
            "swipes": 0,
            "configuredMaxScrolls": driver.DASHBOARD_SCAN_MAX_SCROLLS,
            "stableRepeats": 2,
            "emptyHierarchyReads": 0,
            "maximumEmptyHierarchyReads": 3,
            "systemUiDismissals": 0,
            "maximumSystemUiDismissals": 3,
            "deadlineEnforced": True,
            "phaseBudgetMs": driver.PHASE_BUDGET_MS[
                "advanced-editor-gate-inventory"
            ],
            "hierarchyReadCount": 1,
            "hierarchyElapsedMs": 0,
            "maximumHierarchyReadMs": 0,
            "elapsedMs": 0,
        })

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

    def test_stable_end_scan_can_keep_every_post_origin_dump_owned_file_only(
        self,
    ) -> None:
        stable = [
            driver.shared.UiNode(
                {"resource-id": "stable-row", "bounds": "[0,0][100,100]"}
            )
        ]

        class OwnedFileOnlyDevice:
            def __init__(self) -> None:
                self.hierarchy_options: list[dict[str, object]] = []
                self.swipe_options: list[dict[str, object]] = []

            def hierarchy(self, **options: object) -> list[driver.shared.UiNode]:
                self.hierarchy_options.append(options)
                return stable

            def swipe_up(self, **options: object) -> None:
                self.swipe_options.append(options)

            @staticmethod
            def capture(_name: str, **_options: object) -> None:
                raise AssertionError("stable owned-file scan unexpectedly captured")

        device = OwnedFileOnlyDevice()
        deadline = driver.time.monotonic() + 30.0
        origin = self.priority_rank_origin(stable)
        scan = driver.scan_forward_with_receipt(
            device,
            scan_id="post-back-owned-file-only",
            max_scrolls=8,
            distance_ratio=0.68,
            initial_observation=origin,
            delay_seconds=0.0,
            deadline=deadline,
            hierarchy_dump_attempt_max_seconds=30.0,
            allow_direct_hierarchy_reconciliation=False,
            allow_direct_swipe_reconciliation=False,
        )

        self.assertEqual(0, scan.swipes)
        self.assertEqual(
            [
                {
                    "distance_ratio": 0.68,
                    "deadline": deadline,
                    "allow_direct_reconciliation": False,
                }
            ]
            * 2,
            device.swipe_options,
        )
        self.assertEqual(2, len(device.hierarchy_options))
        self.assertEqual(
            [
                {
                    "deadline": deadline,
                    "dump_attempt_max_seconds": 30.0,
                    "allow_direct_reconciliation": False,
                }
            ]
            * 2,
            device.hierarchy_options,
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

    def test_stable_end_scan_accepts_explicitly_bounded_empty_reads(
        self,
    ) -> None:
        stable = [
            driver.shared.UiNode(
                {"resource-id": "stable-row", "bounds": "[0,0][1,1]"}
            )
        ]
        device = mock.Mock()
        device.hierarchy.side_effect = [
            [], [], [], [], [], [], stable, stable, stable,
        ]
        observations: list[dict[str, object]] = []

        with mock.patch.object(driver.time, "sleep"):
            screens = driver.scan_forward_until_stable(
                device,
                scan_id="explicit-six-empty-read-bound",
                max_scrolls=2,
                distance_ratio=0.22,
                max_consecutive_empty_reads=6,
                observer=observations.append,
            )

        self.assertEqual(3, len(screens))
        self.assertEqual(9, device.hierarchy.call_count)
        self.assertEqual(2, device.swipe_up.call_count)
        device.capture.assert_not_called()
        self.assertEqual("stable-end", observations[0]["status"])
        self.assertEqual(6, observations[0]["emptyHierarchyReads"])
        self.assertEqual(
            6,
            observations[0]["maximumConsecutiveEmptyReads"],
        )

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

    def test_stable_end_empty_hierarchy_failure_capture_shares_phase_deadline(
        self,
    ) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = []
        deadline = driver.time.monotonic() + 30

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "exhausted transient empty hierarchy reads",
        ):
            driver.scan_forward_until_stable(
                device,
                scan_id="deadline-empty-proof",
                max_scrolls=2,
                distance_ratio=0.22,
                max_consecutive_empty_reads=1,
                deadline=deadline,
            )

        device.hierarchy.assert_called_with(deadline=deadline)
        device.capture.assert_called_once_with(
            "deadline-empty-proof-empty-hierarchy-exhausted",
            deadline=deadline,
        )

    def test_progress_recorder_writes_ordered_atomic_timing_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with mock.patch("builtins.print") as emit:
                progress = driver.ProgressRecorder(root)
                for phase_id in driver.PHASE_ORDER:
                    progress.advance(phase_id)
                    if phase_id == "advanced-editor-gate-inventory":
                        self.record_required_method_reacquisition(progress)
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
                "chummer.android.creation-prerequisite-progress/v5",
                evidence["schema"],
            )
            self.assertEqual("timing-complete", evidence["status"])
            self.assertEqual(list(driver.PHASE_ORDER), [
                phase["phaseId"] for phase in evidence["phases"]
            ])
            self.assertEqual(list(driver.PHASE_BUDGET_MS), [
                phase["phaseId"] for phase in evidence["phases"]
            ])
            self.assertEqual(
                "rank-cardinality-heritage",
                next(
                    scan["scanId"]
                    for scan in evidence["scans"]
                    if scan["scanId"] == "rank-cardinality-heritage"
                ),
            )
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
                progress.advance("dashboard-authority-inventory")
            progress.advance(driver.PHASE_ORDER[0])
            with self.assertRaisesRegex(RuntimeError, "progress is incomplete"):
                progress.finish()

    def test_phase_deadline_is_clipped_by_the_whole_journey_cap(self) -> None:
        with tempfile.TemporaryDirectory() as temporary, mock.patch("builtins.print"):
            progress = driver.ProgressRecorder(Path(temporary))
            progress.advance(driver.PHASE_ORDER[0])
            progress._active_started = (
                progress.started
                + (driver.TOTAL_PERFORMANCE_TARGET_MS / 1000)
                - 10
            )

            self.assertEqual(
                progress.started + (driver.TOTAL_PERFORMANCE_TARGET_MS / 1000),
                progress.active_phase_deadline(driver.PHASE_ORDER[0]),
            )

    def test_advanced_editor_phase_requires_one_resolved_method_receipt(self) -> None:
        for case in ("omitted", "duplicated"):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temporary, mock.patch(
                "builtins.print"
            ):
                progress = driver.ProgressRecorder(Path(temporary))
                for phase_id in driver.PHASE_ORDER:
                    progress.advance(phase_id)
                    if phase_id != "advanced-editor-gate-inventory":
                        continue
                    if case == "duplicated":
                        self.record_required_method_reacquisition(progress)
                        self.record_required_method_reacquisition(progress)
                    with self.assertRaisesRegex(
                        RuntimeError,
                        "exactly one creation method reacquisition receipt",
                    ):
                        progress.advance("prerequisite-authority-inventory")
                    break

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

    def test_same_process_restored_grant_budget_accepts_exact_limit_and_rejects_plus_one(
        self,
    ) -> None:
        phase_id = "same-process-restored-talent-grant"
        budget_ms = driver.PHASE_BUDGET_MS[phase_id]
        self.assertEqual(90_000, budget_ms)
        for elapsed_ms, should_pass in (
            (budget_ms, True),
            (budget_ms + 1, False),
        ):
            with self.subTest(elapsed_ms=elapsed_ms), tempfile.TemporaryDirectory() as temporary, mock.patch(
                "builtins.print"
            ):
                progress = driver.ProgressRecorder(Path(temporary))
                progress._active_id = phase_id
                progress._active_started = 10.0
                ended_at = progress._active_started + (elapsed_ms / 1000)

                if should_pass:
                    progress._close_active("pass", ended_at=ended_at)
                    self.assertTrue(progress.phases[-1]["withinBudget"])
                    self.assertEqual(budget_ms, progress.phases[-1]["elapsedMs"])
                else:
                    with self.assertRaisesRegex(
                        RuntimeError,
                        "explicit phase timing budget",
                    ):
                        progress._close_active("pass", ended_at=ended_at)
                    self.assertFalse(progress.phases[-1]["withinBudget"])
                    self.assertEqual(budget_ms + 1, progress.phases[-1]["elapsedMs"])

    def test_reselection_and_grant_completion_have_independent_hard_budgets(
        self,
    ) -> None:
        def start_phase(progress: driver.ProgressRecorder, target: str) -> None:
            for phase_id in driver.PHASE_ORDER:
                progress.advance(phase_id)
                if phase_id == "advanced-editor-gate-inventory":
                    self.record_required_method_reacquisition(progress)
                if phase_id == target:
                    return
            raise AssertionError(f"unknown phase: {target}")

        cases = (
            (
                "talent-active-skill-reselection",
                "talent-active-grant-completion",
            ),
            (
                "talent-active-grant-completion",
                "talent-active-preview",
            ),
            (
                "talent-skill-group-reselection",
                "talent-skill-group-grant-completion",
            ),
            (
                "talent-skill-group-grant-completion",
                "preview-confirm",
            ),
        )
        for active_phase, next_phase in cases:
            with self.subTest(active_phase=active_phase), tempfile.TemporaryDirectory() as temporary, mock.patch(
                "builtins.print"
            ):
                progress = driver.ProgressRecorder(Path(temporary))
                start_phase(progress, active_phase)
                progress._active_started -= (
                    driver.PHASE_BUDGET_MS[active_phase] / 1000
                ) + 1

                with self.assertRaisesRegex(
                    RuntimeError,
                    "explicit phase timing budget",
                ):
                    progress.advance(next_phase)

                failed = progress.phases[-1]
                self.assertEqual(active_phase, failed["phaseId"])
                self.assertFalse(failed["withinBudget"])
                self.assertNotEqual(next_phase, progress._active_id)

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
                    if phase_id == "advanced-editor-gate-inventory":
                        self.record_required_method_reacquisition(progress)
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
            ("phaseOverSum", "does not reconcile"),
            ("totalOverPhaseSum", "does not reconcile"),
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
                    if phase_id == "advanced-editor-gate-inventory":
                        self.record_required_method_reacquisition(progress)
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
                elif case == "totalOverPhaseSum":
                    progress.started -= 1.0
                elif case == "milestoneTotalZero":
                    progress.phases[0]["elapsedMs"] = 20
                    progress.phases[1]["elapsedMs"] = 20
                    completed_phase_sum = sum(
                        int(phase["elapsedMs"])
                        for phase in progress.phases
                    )
                    progress.started = (
                        driver.time.monotonic() - (completed_phase_sum / 1000)
                    )
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

    def test_creation_timing_uses_strict_nonoverlapping_semantic_phases(self) -> None:
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
        dashboard_authority_start = source.index(
            'progress.advance("dashboard-authority-inventory")'
        )
        dashboard_authority_wait = source.index(
            "authority_projection_waited = wait_creation_dashboard_authority("
        )
        dashboard_authority_record = source.index(
            "progress.record_scan(dashboard_authority_observation)"
        )
        advanced_editor_start = source.index(
            'progress.advance("advanced-editor-gate-inventory")'
        )
        advanced_editor_scan = source.index(
            "dashboard_scan = assert_uncreated_advanced_editor_gated("
        )
        method_positioning = source.index(
            "_positioned_method_node, method_detail, _ = reacquire_exact_ready_creation_method("
        )
        ready_capture = source.index('"creation-priority-core-bootstrap-ready"')
        prerequisite_authority_start = source.index(
            'progress.advance("prerequisite-authority-inventory")'
        )
        final_method_selection = source.index(
            "reacquire_creation_method_one_shot_target(",
            prerequisite_authority_start,
        )
        prerequisite_tap = source.index("device.shell(", prerequisite_authority_start)
        prerequisite_scan = source.index("prerequisite_scan = scan_prerequisite_authority(")
        ready_navigation = source.index("ready_navigation = {", prerequisite_tap)
        prerequisite_binding = source.index(
            "prerequisite_binding_authority = require_prerequisite_binding("
        )
        prerequisite_digest_validation = source.index(
            "require_binding_matches_canonical_digests("
        )
        prerequisite_karma_validation = source.index(
            'karma = prerequisite_values["creation-prerequisite-karma-budget"]'
        )
        prerequisite_source_authority = source.index(
            "source_authority_digests = sorted("
        )
        priority_start = source.index('progress.advance("priority-ranks")')

        self.assertEqual(60_000, driver.PHASE_BUDGET_MS["initial-navigation"])
        self.assertEqual(90_000, driver.PHASE_BUDGET_MS["initial-authority"])
        self.assertEqual(30_000, driver.PHASE_BUDGET_MS["dashboard-proof"])
        self.assertEqual(
            30_000,
            driver.PHASE_BUDGET_MS["dashboard-authority-inventory"],
        )
        self.assertEqual(
            90_000,
            driver.PHASE_BUDGET_MS["advanced-editor-gate-inventory"],
        )
        self.assertEqual(
            120_000,
            driver.PHASE_BUDGET_MS["prerequisite-authority-inventory"],
        )
        self.assertEqual(2_700_000, driver.TOTAL_PERFORMANCE_TARGET_MS)
        self.assertEqual(33, len(driver.PHASE_ORDER))
        self.assertEqual(17, driver.TIMING_ROUNDING_TOLERANCE_MS)
        self.assertLess(navigation_start, cold_launch)
        self.assertLess(cold_launch, dialog_ready)
        self.assertLess(dialog_ready, authority_start)
        self.assertLess(authority_start, explicit_tap)
        self.assertLess(explicit_tap, timing_capture)
        self.assertLess(timing_capture, transaction_ready)
        self.assertLess(transaction_ready, dashboard_start)
        self.assertLess(dashboard_start, visible_dashboard)
        self.assertLess(visible_dashboard, dashboard_ready)
        self.assertLess(dashboard_ready, dashboard_authority_start)
        self.assertLess(dashboard_authority_start, dashboard_authority_wait)
        self.assertLess(dashboard_authority_wait, dashboard_authority_record)
        self.assertLess(dashboard_authority_record, advanced_editor_start)
        self.assertLess(advanced_editor_start, advanced_editor_scan)
        self.assertLess(advanced_editor_scan, method_positioning)
        self.assertLess(method_positioning, ready_capture)
        self.assertLess(ready_capture, prerequisite_authority_start)
        self.assertIn(
            "deadline=advanced_editor_deadline",
            source[ready_capture:prerequisite_authority_start],
        )
        self.assertLess(prerequisite_authority_start, final_method_selection)
        self.assertLess(final_method_selection, prerequisite_tap)
        self.assertLess(prerequisite_tap, ready_navigation)
        self.assertLess(ready_navigation, prerequisite_scan)
        self.assertNotIn(
            "device.capture(",
            source[final_method_selection:prerequisite_tap],
        )
        self.assertEqual(
            1,
            source[final_method_selection:prerequisite_scan].count(
                '"input",\n        "tap",'
            ),
        )
        self.assertLess(prerequisite_scan, prerequisite_binding)
        self.assertLess(prerequisite_binding, prerequisite_digest_validation)
        self.assertLess(prerequisite_digest_validation, prerequisite_karma_validation)
        self.assertLess(prerequisite_karma_validation, prerequisite_source_authority)
        self.assertLess(prerequisite_source_authority, priority_start)

    def test_driver_and_aggregate_share_one_exact_timing_contract(self) -> None:
        self.assertEqual(driver.PROGRESS_SCHEMA, aggregate.CREATION_PROGRESS_SCHEMA)
        self.assertEqual(
            list(driver.PHASE_BUDGET_MS.items()),
            list(aggregate.CREATION_PHASE_BUDGETS_MS.items()),
        )
        self.assertEqual(
            driver.TOTAL_PERFORMANCE_TARGET_MS,
            aggregate.CREATION_TOTAL_TARGET_MS,
        )
        self.assertEqual(
            tuple(zip(
                driver.INITIAL_MILESTONE_ORDER,
                driver.INITIAL_MILESTONE_PHASES,
                strict=True,
            )),
            aggregate.CREATION_MILESTONES,
        )
        self.assertEqual(
            driver.TIMING_ROUNDING_TOLERANCE_MS,
            aggregate.CREATION_TIMING_ROUNDING_TOLERANCE_MS,
        )
        self.assertEqual(17, driver.TIMING_ROUNDING_TOLERANCE_MS)
        for phase_id in (
            "talent-active-skill-grant",
            "talent-active-grant-completion",
            "talent-active-preview",
            "talent-skill-group-selection",
            "talent-skill-group-grant",
            "talent-skill-group-preservation",
            "talent-skill-group-reset",
            "talent-skill-group-reselection",
            "talent-skill-group-grant-completion",
            "same-process-restored-talent-grant",
            "process-restart-restored-talent-grant",
        ):
            with self.subTest(phase_id=phase_id):
                self.assertIn(phase_id, aggregate.CREATION_PHASE_BUDGETS_MS)

    def test_resume_and_restart_work_are_charged_to_their_exact_split_phases(self) -> None:
        source = inspect.getsource(driver.execute)

        def phase_slice(phase: str, next_phase: str | None) -> str:
            start = source.index(f'progress.advance("{phase}")')
            if next_phase is None:
                return source[start:]
            end = source.index(f'progress.advance("{next_phase}")', start)
            return source[start:end]

        same_reopen = phase_slice(
            "same-process-reopen",
            "same-process-authority-options",
        )
        same_options = phase_slice(
            "same-process-authority-options",
            "same-process-restored-talent-grant",
        )
        same_grant = phase_slice(
            "same-process-restored-talent-grant",
            "resources-initial-authority",
        )
        resources_initial = phase_slice(
            "resources-initial-authority",
            "resources-preview-confirm",
        )
        resources_confirm = phase_slice(
            "resources-preview-confirm",
            "resources-same-process-reopen",
        )
        resources_reopen = phase_slice(
            "resources-same-process-reopen",
            "resources-prerequisite-rebind",
        )
        resources_rebind = phase_slice(
            "resources-prerequisite-rebind",
            "process-restart-reopen",
        )
        restart_reopen = phase_slice(
            "process-restart-reopen",
            "process-restart-authority-options",
        )
        restart_options = phase_slice(
            "process-restart-authority-options",
            "process-restart-restored-talent-grant",
        )
        restart_grant = phase_slice(
            "process-restart-restored-talent-grant",
            "process-restart-resources",
        )
        restart_resources = phase_slice("process-restart-resources", None)

        self.assertIn("read_persisted_prerequisite_authority(", same_reopen)
        self.assertIn("deadline=same_process_deadline", same_reopen)
        self.assertNotIn("require_exact_restored_authority_option(", same_reopen)
        self.assertEqual(2, same_options.count("require_exact_restored_authority_option("))
        self.assertNotIn("require_restored_talent_grant(", same_options)
        self.assertIn("require_restored_talent_grant(", same_grant)
        self.assertIn("deadline=same_process_grant_deadline", same_grant)

        self.assertIn("open_creation_dashboard(", resources_initial)
        self.assertIn("open_resources(", resources_initial)
        self.assertIn("resources_dashboard = shared.open_creation_dashboard(", resources_initial)
        self.assertIn("reset_swipes=0", resources_initial)
        self.assertIn("observed_dashboard=resources_dashboard", resources_initial)
        self.assertIn("authority_scan_owns_origin=True", resources_initial)
        self.assertIn("read_resources_binding_with_zero_option(", resources_initial)
        self.assertNotIn("select_and_confirm_resources(", resources_initial)
        self.assertIn("select_and_confirm_resources(", resources_confirm)
        self.assertIn("prelocated_option=resources_zero_option", resources_confirm)
        self.assertIn("resources_confirmation[\"receipt\"]", resources_confirm)
        self.assertNotIn("reopen_resources(", resources_confirm)
        self.assertIn("reopen_resources(", resources_reopen)
        self.assertIn("read_persisted_resources_authority(", resources_reopen)
        self.assertNotIn("open_creation_dashboard(", resources_reopen)
        self.assertIn("open_creation_dashboard(", resources_rebind)
        self.assertIn("reacquire_exact_ready_creation_method(", resources_rebind)
        self.assertIn("read_persisted_prerequisite_authority(", resources_rebind)
        self.assertIn("assert_persisted_prerequisite_authority(", resources_rebind)
        self.assertNotIn("force_stop_and_launch_new_process", resources_rebind)

        self.assertIn("shared.force_stop_and_launch_new_process", restart_reopen)
        self.assertIn("read_persisted_prerequisite_authority(", restart_reopen)
        self.assertIn("deadline=process_restart_deadline", restart_reopen)
        self.assertIn(
            "max_consecutive_empty_reads=(\n            PROCESS_RESTART_PERSISTED_PREREQUISITE_MAX_CONSECUTIVE_EMPTY_READS\n        )",
            restart_reopen,
        )
        self.assertNotIn("require_exact_restored_authority_option(", restart_reopen)
        self.assertEqual(
            2,
            restart_options.count("require_exact_restored_authority_option("),
        )
        self.assertNotIn("require_restored_talent_grant(", restart_options)
        self.assertIn("require_restored_talent_grant(", restart_grant)
        self.assertIn("deadline=process_restart_grant_deadline", restart_grant)

        self.assertIn(
            "process_restart_resources_dashboard = shared.open_creation_dashboard(",
            restart_resources,
        )
        self.assertIn("reset_swipes=0", restart_resources)
        self.assertIn("open_resources(", restart_resources)
        self.assertIn(
            "observed_dashboard=process_restart_resources_dashboard",
            restart_resources,
        )
        self.assertIn("authority_scan_owns_origin=True", restart_resources)
        self.assertIn("read_process_restart_resources_proof_state(", restart_resources)
        self.assertNotIn("read_persisted_resources_authority(", restart_resources)
        self.assertIn(
            "deadline=process_restart_resources_deadline",
            restart_resources,
        )

    def test_resources_expired_deadline_performs_no_device_mutation(self) -> None:
        expired = driver.time.monotonic() - 1

        open_device = mock.Mock()
        open_device.wait_exact_resource_id_bidirectional.return_value = (
            self.canonical_node(
                "creation-stage-resources",
                **{"content-desc": "Resources"},
            )
        )
        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.open_resources(open_device, deadline=expired)
        open_device.shell.assert_not_called()
        open_device.wait_for_single_exact_resource_id.assert_not_called()

        confirm_device = mock.Mock()
        confirm_device.wait_exact_resource_id_bidirectional.return_value = (
            self.canonical_node(
                "creation-resources-option-karma-0",
                **{"content-desc": "0 Karma · 50,000 nuyen"},
            )
        )
        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.select_and_confirm_resources(
                confirm_device,
                {"contentRevision": 1, "savedRevision": 1},
                deadline=expired,
            )
        confirm_device.shell.assert_not_called()
        confirm_device.wait_for_single_exact_resource_id.assert_not_called()

    def test_resources_open_reuses_observed_dashboard_and_defers_origin_to_scan(
        self,
    ) -> None:
        dashboard = self.canonical_node(
            "creation-wizard-dashboard",
            clickable="false",
        )
        resources_row = self.canonical_node(
            "creation-stage-resources",
            **{"content-desc": "Resources"},
        )
        resources_route = self.canonical_node(
            "creation-resources-page",
            clickable="false",
        )
        deadline = driver.time.monotonic() + 30
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True
        device.wait_exact_resource_id_bidirectional.return_value = resources_row
        device.wait_for_single_exact_resource_id.return_value = resources_route

        with mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            driver.open_resources(
                device,
                deadline=deadline,
                observed_dashboard=dashboard,
                authority_scan_owns_origin=True,
            )

        device.wait_exact_resource_id_bidirectional.assert_called_once_with(
            "creation-stage-resources",
            timeout=180,
            backward_scrolls=0,
            forward_scrolls=22,
            scroll_distance_ratio=0.22,
            evidence_prefix="creation-resources-stage",
            surface_name="Core-authoritative Resources stage",
            deadline=deadline,
        )
        device.shell.assert_called_once_with(
            "input",
            "tap",
            *(str(value) for value in resources_row.center),
            timeout=15,
            deadline=deadline,
        )
        device.wait_for_single_exact_resource_id.assert_called_once_with(
            "creation-resources-page",
            timeout=60,
            evidence_prefix="creation-resources-route",
            surface_name="Creation Resources route",
            deadline=deadline,
        )
        reset.assert_not_called()

    def test_resources_authority_scan_origin_requires_observed_dashboard(self) -> None:
        device = mock.Mock()

        with self.assertRaisesRegex(
            ValueError,
            "requires an observed dashboard",
        ):
            driver.open_resources(
                device,
                deadline=driver.time.monotonic() + 30,
                authority_scan_owns_origin=True,
            )

        device.wait_exact_resource_id_bidirectional.assert_not_called()
        device.shell.assert_not_called()

    def test_resources_legacy_calls_do_not_inject_none_deadlines(self) -> None:
        device = mock.Mock()
        device.wait_exact_resource_id_bidirectional.return_value = self.canonical_node(
            "creation-resources-option-karma-0",
            **{"content-desc": "invalid option"},
        )
        with self.assertRaisesRegex(RuntimeError, "zero-conversion"):
            driver.select_and_confirm_resources(
                device,
                {"contentRevision": 1, "savedRevision": 1},
            )

        _, wait_options = device.wait_exact_resource_id_bidirectional.call_args
        self.assertNotIn("deadline", wait_options)
        device.capture.assert_called_once_with(
            "creation-resources-option-karma-0-invalid"
        )
        device.shell.assert_not_called()

        digest = "sha256:" + "a" * 64
        with mock.patch.object(
            driver,
            "nonnegative_integer",
            side_effect=(1, 1, 50_000, 50_000),
        ) as integer, mock.patch.object(
            driver,
            "canonical_digest",
            return_value=digest,
        ) as canonical, mock.patch.object(
            driver,
            "canonical_auxiliary_state_digest",
            return_value="b" * 64,
        ) as auxiliary:
            driver.read_resources_binding(device)

        for helper in (integer, canonical, auxiliary):
            for call in helper.call_args_list:
                self.assertNotIn("deadline", call.kwargs)

    def test_deadline_resources_surface_uses_one_complete_authority_scan(self) -> None:
        selectors = (
            "creation-resources-page",
            "creation-resources-binding-content-revision",
        )
        nodes = [
            self.canonical_node(selectors[0]),
            self.canonical_node(selectors[1], text="2"),
        ]
        origin = self.priority_rank_origin(nodes)
        deadline = driver.time.monotonic() + 30
        observer = mock.Mock()
        device = mock.Mock()

        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=origin,
        ) as acquire, mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            autospec=True,
            return_value=driver.StableViewportScan([nodes], 3),
        ) as scan:
            actual = driver.scan_deadline_bound_resources_surface(
                device,
                selectors,
                scan_id="resources-one-scan",
                deadline=deadline,
                scan_observer=observer,
            )

        self.assertEqual(set(selectors), set(actual))
        acquire.assert_called_once_with(
            device,
            scan_id="resources-one-scan-start",
            max_reverse_swipes=22,
            distance_ratio=0.68,
            deadline=deadline,
        )
        scan.assert_called_once_with(
            device,
            scan_id="resources-one-scan",
            max_scrolls=22,
            distance_ratio=0.22,
            initial_observation=origin,
            initial_observation_max_reverse_swipes=22,
            delay_seconds=0.0,
            max_consecutive_empty_reads=(
                driver.RESOURCES_SURFACE_MAX_CONSECUTIVE_EMPTY_READS
            ),
            observer=observer,
            deadline=deadline,
        )
        device.capture.assert_not_called()

    def test_process_restart_resources_reads_exact_typed_state_without_ui_tree(self) -> None:
        digest = lambda character: "sha256:" + character * 64
        binding = {
            "contentRevision": 3,
            "savedRevision": 3,
            "snapshotDigest": digest("1"),
            "rawCharacterXmlDigest": digest("2"),
            "auxiliaryStateDigest": "3" * 64,
            "prerequisiteDraftDigest": digest("4"),
            "authorityDigest": digest("5"),
            "priorityNuyen": 50_000,
            "totalStartingNuyen": 50_000,
        }
        saved = {
            "optionId": "karma:0",
            "draftRevision": 1,
            "draftDigest": digest("6"),
        }
        resources = {
            "pageIdentity": "creation-resources-page",
            "workspaceId": "workspace-resources",
            "workspaceRevision": 3,
            **binding,
            "sourceDigest": digest("7"),
            "rulesDigest": digest("8"),
            "runtimeDigest": digest("9"),
            "prerequisiteDraftRevision": 1,
            "pendingOptionId": saved["optionId"],
            "pendingDraftRevision": saved["draftRevision"],
            "pendingDraftDigest": saved["draftDigest"],
        }
        snapshot = driver.proof_state.ProofStateSnapshot(
            {
                "schema": driver.proof_state.SCHEMA,
                "sequence": 7,
                "processId": 4242,
                "processInstanceId": "44444444-4444-4444-4444-444444444444",
                "e2eAuthorityGeneration": 2,
                "surface": {
                    "pageAutomationId": "creation-resources-page",
                    "navigationDepth": 2,
                    "wizardLane": "creation-resources",
                    "stage": "authority-ready",
                    "settled": True,
                },
                "workspace": {
                    "workspaceId": resources["workspaceId"],
                    "contentRevision": resources["contentRevision"],
                    "savedRevision": resources["savedRevision"],
                    "payloadSha256": "c" * 64,
                    "documentSha256": "d" * 64,
                    "snapshotDigest": resources["snapshotDigest"],
                },
                "stateDigest": digest("a"),
                "creationResources": resources,
            },
            "b" * 64,
        )
        device = mock.Mock()
        deadline = driver.time.monotonic() + 30
        with mock.patch.object(
            driver.shared,
            "_remaining_operation_timeout",
            return_value=30,
        ), mock.patch.object(
            driver.proof_state,
            "wait_for_state",
            return_value=snapshot,
        ) as wait:
            observed, evidence = driver.read_process_restart_resources_proof_state(
                device,
                {
                    "workspaceRevision": 3,
                    "savedRevision": 3,
                    **saved,
                },
                {"binding": binding, "savedDraft": saved},
                resources,
                mock.Mock(),
                deadline=deadline,
            )

        self.assertEqual({"binding": binding, "savedDraft": saved}, observed)
        self.assertEqual(digest("7"), evidence["typedResources"]["sourceDigest"])
        wait.assert_called_once_with(
            device,
            expected=mock.ANY,
            page_automation_id="creation-resources-page",
            stage="authority-ready",
            wizard_lane="creation-resources",
            timeout=30,
        )
        device.hierarchy.assert_not_called()
        device.swipe_up.assert_not_called()
        device.shell.assert_not_called()
        device.capture.assert_not_called()

    def test_process_restart_resources_state_absence_and_binding_drift_fail_closed(
        self,
    ) -> None:
        device = mock.Mock()
        deadline = driver.time.monotonic() + 30
        absent = driver.proof_state.ProofStateSnapshot(
            {"creationResources": None},
            "a" * 64,
        )
        with mock.patch.object(
            driver.shared, "_remaining_operation_timeout", return_value=30
        ), mock.patch.object(
            driver.proof_state, "wait_for_state", return_value=absent
        ), self.assertRaisesRegex(RuntimeError, "no Creation Resources authority"):
            driver.read_process_restart_resources_proof_state(
                device, {}, {}, {}, mock.Mock(), deadline=deadline
            )
        device.hierarchy.assert_not_called()
        device.shell.assert_not_called()

    def test_process_restart_resources_rejects_canonical_typed_digest_drift(self) -> None:
        digest = lambda character: "sha256:" + character * 64
        binding = {
            "contentRevision": 3,
            "savedRevision": 3,
            "snapshotDigest": digest("1"),
            "rawCharacterXmlDigest": digest("2"),
            "auxiliaryStateDigest": "3" * 64,
            "prerequisiteDraftDigest": digest("4"),
            "authorityDigest": digest("5"),
            "priorityNuyen": 50_000,
            "totalStartingNuyen": 50_000,
        }
        saved = {
            "optionId": "karma:0",
            "draftRevision": 1,
            "draftDigest": digest("6"),
        }
        same_process = {
            "pageIdentity": "creation-resources-page",
            "workspaceId": "workspace-resources",
            "workspaceRevision": 3,
            **binding,
            "sourceDigest": digest("7"),
            "rulesDigest": digest("8"),
            "runtimeDigest": digest("9"),
            "prerequisiteDraftRevision": 1,
            "pendingOptionId": saved["optionId"],
            "pendingDraftRevision": saved["draftRevision"],
            "pendingDraftDigest": saved["draftDigest"],
        }
        hostile_restart = dict(same_process)
        hostile_restart["sourceDigest"] = digest("a")
        device = mock.Mock()
        with mock.patch.object(
            driver,
            "read_creation_resources_proof_state",
            return_value=(hostile_restart, {"stateDigest": digest("b")}),
        ), self.assertRaisesRegex(RuntimeError, "Typed Resources authority changed"):
            driver.read_process_restart_resources_proof_state(
                device,
                {"workspaceRevision": 3, "savedRevision": 3, **saved},
                {"binding": binding, "savedDraft": saved},
                same_process,
                mock.Mock(),
                deadline=driver.time.monotonic() + 30,
            )
        device.capture.assert_called_once()
        device.hierarchy.assert_not_called()
        device.shell.assert_not_called()

    def test_deadline_resources_surface_rejects_missing_duplicate_drift_and_identity(
        self,
    ) -> None:
        selectors = (
            "creation-resources-page",
            "creation-resources-binding-content-revision",
        )
        base = [
            self.canonical_node(selectors[0]),
            self.canonical_node(selectors[1], text="2"),
        ]
        drift = self.canonical_node(selectors[1], text="3")
        wrong_identity = self.canonical_node(selectors[1], text="2")
        wrong_identity.attributes["resource-id"] = (
            f"com.example.forged:id/{selectors[1]}"
        )
        cases = (
            ("missing", [[base[0]]], "incomplete or ambiguous"),
            ("duplicate", [[*base, base[1]]], "incomplete or ambiguous"),
            ("drift", [base, [base[0], drift]], "incomplete or ambiguous"),
            ("identity", [[base[0], wrong_identity]], "canonical Chummer"),
        )
        for name, screens, expected in cases:
            with self.subTest(name=name):
                device = mock.Mock()
                with mock.patch.object(
                    driver,
                    "acquire_stable_start_origin",
                    return_value=self.priority_rank_origin(base),
                ), mock.patch.object(
                    driver,
                    "scan_forward_with_receipt",
                    return_value=driver.StableViewportScan(screens, 1),
                ), self.assertRaisesRegex(RuntimeError, expected):
                    driver.scan_deadline_bound_resources_surface(
                        device,
                        selectors,
                        scan_id=f"resources-{name}",
                        deadline=driver.time.monotonic() + 30,
                    )

    def test_resources_binding_reacquires_zero_option_from_measured_scan_only(
        self,
    ) -> None:
        option = self.canonical_node(
            driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,
            **{"content-desc": "0 Karma · 50,000 nuyen"},
        )
        nodes = self.resources_binding_nodes(option)
        proof = driver.ResourcesSurfaceScanProof(
            nodes=nodes,
            swipes=10,
            selector_viewports={
                selector: 10 for selector in driver.RESOURCES_BINDING_AUTHORITY_SELECTORS
            } | {driver.RESOURCES_ZERO_CONVERSION_OPTION_ID: 6},
            terminal_tappable_nodes={},
        )
        deadline = driver.time.monotonic() + 30
        device = mock.Mock()
        observer = mock.Mock()
        with mock.patch.object(
            driver,
            "scan_deadline_bound_resources_surface",
            return_value=proof,
        ) as scan, mock.patch.object(
            driver,
            "measured_reverse_reacquisition_bound",
            return_value=4,
        ) as measured, mock.patch.object(
            driver,
            "rewind_to_exact_resource_id",
            return_value=(option, 4),
        ) as rewind:
            authority, actual_option = driver.read_resources_binding_with_zero_option(
                device,
                deadline=deadline,
                scan_observer=observer,
                scan_id="resources-measured-option",
            )

        self.assertIs(option, actual_option)
        self.assertEqual(50_000, authority["totalStartingNuyen"])
        scan.assert_called_once_with(
            device,
            (
                *driver.RESOURCES_BINDING_AUTHORITY_SELECTORS,
                driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,
            ),
            scan_id="resources-measured-option",
            deadline=deadline,
            scan_observer=observer,
            tappable_selectors=(driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,),
            return_scan_proof=True,
        )
        measured.assert_called_once_with(10, 6, maximum_viewport=22)
        self.assertEqual(6, rewind.call_args.kwargs["max_swipes"])
        self.assertTrue(rewind.call_args.kwargs["require_tappable"])
        device.wait_exact_resource_id_bidirectional.assert_not_called()

        drifted_option = self.canonical_node(
            driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,
            **{"content-desc": "0 Karma · forged grant"},
        )
        device.reset_mock()
        with mock.patch.object(
            driver,
            "scan_deadline_bound_resources_surface",
            return_value=proof,
        ), mock.patch.object(
            driver,
            "measured_reverse_reacquisition_bound",
            return_value=4,
        ), mock.patch.object(
            driver,
            "rewind_to_exact_resource_id",
            return_value=(drifted_option, 4),
        ), self.assertRaisesRegex(RuntimeError, "changed between scan and tap"):
            driver.read_resources_binding_with_zero_option(
                device,
                deadline=deadline,
            )
        device.capture.assert_called_once_with(
            "creation-resources-option-karma-0-measured-reacquisition-drift",
            deadline=deadline,
        )

    def test_resources_binding_reuses_fresh_terminal_option_without_rewind(self) -> None:
        option = self.canonical_node(
            driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,
            **{"content-desc": "0 Karma · 50,000 nuyen"},
        )
        nodes = self.resources_binding_nodes(option)
        proof = driver.ResourcesSurfaceScanProof(
            nodes=nodes,
            swipes=8,
            selector_viewports={
                selector: 8 for selector in driver.RESOURCES_BINDING_AUTHORITY_SELECTORS
            } | {driver.RESOURCES_ZERO_CONVERSION_OPTION_ID: 8},
            terminal_tappable_nodes={
                driver.RESOURCES_ZERO_CONVERSION_OPTION_ID: option,
            },
        )
        deadline = driver.time.monotonic() + 30
        device = mock.Mock()
        with mock.patch.object(
            driver,
            "scan_deadline_bound_resources_surface",
            return_value=proof,
        ), mock.patch.object(
            driver,
            "measured_reverse_reacquisition_bound",
        ) as measured, mock.patch.object(
            driver,
            "rewind_to_exact_resource_id",
        ) as rewind:
            authority, actual_option = driver.read_resources_binding_with_zero_option(
                device,
                deadline=deadline,
            )

        self.assertEqual(50_000, authority["priorityNuyen"])
        self.assertIs(option, actual_option)
        measured.assert_not_called()
        rewind.assert_not_called()
        device.capture.assert_not_called()
        device.shell.assert_not_called()

    def test_resources_terminal_reuse_rejects_identity_and_viewport_drift(self) -> None:
        option = self.canonical_node(
            driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,
            **{"content-desc": "0 Karma · 50,000 nuyen"},
        )
        drifted = self.canonical_node(
            driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,
            **{"content-desc": "0 Karma · forged grant"},
        )
        base = driver.ResourcesSurfaceScanProof(
            nodes=self.resources_binding_nodes(option),
            swipes=8,
            selector_viewports={
                selector: 8 for selector in driver.RESOURCES_BINDING_AUTHORITY_SELECTORS
            } | {driver.RESOURCES_ZERO_CONVERSION_OPTION_ID: 8},
            terminal_tappable_nodes={
                driver.RESOURCES_ZERO_CONVERSION_OPTION_ID: drifted,
            },
        )
        cases = (
            ("identity", base),
            (
                "viewport",
                base._replace(
                    terminal_tappable_nodes={
                        driver.RESOURCES_ZERO_CONVERSION_OPTION_ID: option,
                    },
                    selector_viewports={
                        **base.selector_viewports,
                        driver.RESOURCES_ZERO_CONVERSION_OPTION_ID: 7,
                    },
                ),
            ),
        )
        for name, proof in cases:
            with self.subTest(name=name):
                deadline = driver.time.monotonic() + 30
                device = mock.Mock()
                with mock.patch.object(
                    driver,
                    "scan_deadline_bound_resources_surface",
                    return_value=proof,
                ), mock.patch.object(
                    driver,
                    "measured_reverse_reacquisition_bound",
                ) as measured, mock.patch.object(
                    driver,
                    "rewind_to_exact_resource_id",
                ) as rewind, self.assertRaisesRegex(
                    RuntimeError,
                    "changed between scan and terminal reuse",
                ):
                    driver.read_resources_binding_with_zero_option(
                        device,
                        deadline=deadline,
                    )
                measured.assert_not_called()
                rewind.assert_not_called()
                device.capture.assert_called_once_with(
                    "creation-resources-option-karma-0-terminal-reuse-drift",
                    deadline=deadline,
                )

    def test_deadline_resources_surface_requires_action_on_terminal_viewport(self) -> None:
        selectors = (
            "creation-resources-preview-page",
            "creation-resources-confirm",
        )
        route = self.canonical_node(selectors[0])
        confirm = self.canonical_node(selectors[1])
        device = mock.Mock()
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=self.priority_rank_origin([route, confirm]),
        ), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan(
                [[route, confirm], [route]],
                1,
            ),
        ), self.assertRaisesRegex(RuntimeError, "terminalMissing"):
            driver.scan_deadline_bound_resources_surface(
                device,
                selectors,
                scan_id="resources-terminal-action",
                deadline=driver.time.monotonic() + 30,
                required_terminal_selectors=("creation-resources-confirm",),
            )

    def test_resources_scan_measures_only_tappable_action_viewports(self) -> None:
        route = self.canonical_node("creation-resources-page")
        clipped = self.canonical_node(driver.RESOURCES_ZERO_CONVERSION_OPTION_ID)
        visible = self.canonical_node(driver.RESOURCES_ZERO_CONVERSION_OPTION_ID)
        device = mock.Mock()
        device.node_has_tappable_bounds.side_effect = (True, False)
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=self.priority_rank_origin([route, visible]),
        ), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan(
                [[route, visible], [route, clipped]],
                1,
            ),
        ):
            proof = driver.scan_deadline_bound_resources_surface(
                device,
                ("creation-resources-page", driver.RESOURCES_ZERO_CONVERSION_OPTION_ID),
                scan_id="resources-tappable-viewport",
                deadline=driver.time.monotonic() + 30,
                tappable_selectors=(driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,),
                return_scan_proof=True,
            )

        self.assertIsInstance(proof, driver.ResourcesSurfaceScanProof)
        self.assertEqual(
            0,
            proof.selector_viewports[driver.RESOURCES_ZERO_CONVERSION_OPTION_ID],
        )
        self.assertIs(
            visible,
            proof.nodes[driver.RESOURCES_ZERO_CONVERSION_OPTION_ID],
        )
        self.assertNotIn(
            driver.RESOURCES_ZERO_CONVERSION_OPTION_ID,
            proof.terminal_tappable_nodes,
        )

    def test_deadline_resources_preview_and_receipt_each_use_one_scan(self) -> None:
        digest = "sha256:" + "a" * 64
        option = self.canonical_node(
            "creation-resources-option-karma-0",
            **{"content-desc": "0 Karma · 50,000 nuyen"},
        )
        confirm = self.canonical_node("creation-resources-confirm")
        preview_nodes = {
            "creation-resources-preview-page": self.canonical_node(
                "creation-resources-preview-page"
            ),
            "creation-resources-preview-option-id": self.canonical_node(
                "creation-resources-preview-option-id",
                text="karma:0",
            ),
            "creation-resources-preview-priority-grant": self.canonical_node(
                "creation-resources-preview-priority-grant",
                text="50000",
            ),
            "creation-resources-preview-total-starting-nuyen": self.canonical_node(
                "creation-resources-preview-total-starting-nuyen",
                text="50000",
            ),
            "creation-resources-preview-digest": self.canonical_node(
                "creation-resources-preview-digest",
                text=digest,
            ),
            "creation-resources-confirm": confirm,
        }
        receipt_nodes = {
            "creation-resources-confirm-receipt": self.canonical_node(
                "creation-resources-confirm-receipt"
            ),
            "creation-resources-receipt-option-id": self.canonical_node(
                "creation-resources-receipt-option-id",
                text="karma:0",
            ),
            "creation-resources-receipt-workspace-revision": self.canonical_node(
                "creation-resources-receipt-workspace-revision",
                text="2",
            ),
            "creation-resources-receipt-saved-revision": self.canonical_node(
                "creation-resources-receipt-saved-revision",
                text="2",
            ),
            "creation-resources-receipt-draft-revision": self.canonical_node(
                "creation-resources-receipt-draft-revision",
                text="1",
            ),
            "creation-resources-receipt-total-starting-nuyen": self.canonical_node(
                "creation-resources-receipt-total-starting-nuyen",
                text="50000",
            ),
            "creation-resources-receipt-preview-digest": self.canonical_node(
                "creation-resources-receipt-preview-digest",
                text=digest,
            ),
            "creation-resources-receipt-draft-digest": self.canonical_node(
                "creation-resources-receipt-draft-digest",
                text=digest,
            ),
            "creation-resources-receipt-digest": self.canonical_node(
                "creation-resources-receipt-digest",
                text=digest,
            ),
        }
        device = mock.Mock()
        device.wait_exact_resource_id_bidirectional.return_value = option
        device.wait_for_single_exact_resource_id.side_effect = (
            preview_nodes["creation-resources-preview-page"],
            receipt_nodes["creation-resources-confirm-receipt"],
        )
        deadline = driver.time.monotonic() + 30
        with mock.patch.object(
            driver,
            "scan_deadline_bound_resources_surface",
            side_effect=(preview_nodes, receipt_nodes),
        ) as scan:
            result = driver.select_and_confirm_resources(
                device,
                {"contentRevision": 1, "savedRevision": 1},
                prelocated_option=option,
                deadline=deadline,
            )

        self.assertEqual("karma:0", result["receipt"]["optionId"])
        self.assertEqual(2, scan.call_count)
        self.assertEqual(
            ("creation-resources-confirm",),
            scan.call_args_list[0].kwargs["required_terminal_selectors"],
        )
        self.assertNotIn("required_terminal_selectors", scan.call_args_list[1].kwargs)
        device.wait_exact_resource_id_bidirectional.assert_not_called()
        self.assertEqual(2, device.shell.call_count)

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

    def test_talent_grant_count_is_bound_to_the_exact_authority_semantics(self) -> None:
        source = (NATIVE / "CreationTalentSkillGrantPage.cs").read_text(
            encoding="utf-8"
        )
        authority = source[source.index("private void AddGrantAuthority") :]
        authority = authority[: authority.index("private async Task ToggleActiveAsync")]

        self.assertIn("string requiredAuthority =", authority)
        self.assertIn(
            '$"{selectedCount.ToString(CultureInfo.InvariantCulture)} / "',
            authority,
        )
        self.assertIn(
            '$"{quantity.ToString(CultureInfo.InvariantCulture)} {kind}"',
            authority,
        )
        self.assertIn(
            'border.AutomationId = "creation-prerequisite-talent-grant-authority";',
            authority,
        )
        self.assertIn(
            "SemanticProperties.SetDescription(border, requiredAuthority);",
            authority,
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
        option_detail = (
            "✓ Arcana. Selected slot 1 · Magical Active"
            if selected
            else "Arcana. Magical Active"
        )
        return [
            driver.shared.UiNode(
                {
                    "package": driver.shared.PACKAGE,
                    "resource-id": (
                        f"{driver.shared.PACKAGE}:id/"
                        "creation-prerequisite-talent-grant-authority"
                    ),
                    "content-desc": f"Required. {selected_count} / 1 {kind}",
                    "bounds": "[0,0][100,100]",
                }
            ),
            driver.shared.UiNode(
                {
                    "package": driver.shared.PACKAGE,
                    "resource-id": (
                        f"{driver.shared.PACKAGE}:id/"
                        "creation-prerequisite-talent-grant-digest"
                    ),
                    "text": "sha256:" + ("a" * 64),
                    "bounds": "[0,100][100,200]",
                }
            ),
            driver.shared.UiNode(
                {
                    "package": driver.shared.PACKAGE,
                    "resource-id": f"{driver.shared.PACKAGE}:id/{prefix}{option_id}",
                    "content-desc": option_detail,
                    "enabled": "true",
                    "clickable": "true",
                    "bounds": "[0,200][100,300]",
                }
            ),
            driver.shared.UiNode(
                {
                    "package": driver.shared.PACKAGE,
                    "resource-id": (
                        f"{driver.shared.PACKAGE}:id/"
                        "creation-prerequisite-talent-grant-complete"
                    ),
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
                return CreationPrerequisiteSourceContractTests.canonical_node(
                    "creation-prerequisite-talent-grant-page"
                )

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
            ("Arcana. Magical Active",),
            navigation["resourceDetails"][
                "creation-prerequisite-talent-active-skill-option-choice-0001"
            ],
        )

    def test_talent_grant_surface_rejects_noncanonical_route_before_scan(self) -> None:
        for name, package, resource_id in (
            (
                "wrong-package",
                "com.example.forged",
                f"{driver.shared.PACKAGE}:id/creation-prerequisite-talent-grant-page",
            ),
            (
                "wrong-full-id",
                driver.shared.PACKAGE,
                "com.example.forged:id/creation-prerequisite-talent-grant-page",
            ),
        ):
            with self.subTest(name=name):
                device = mock.Mock()
                device.wait_for_single_exact_resource_id.return_value = (
                    driver.shared.UiNode(
                        {
                            "package": package,
                            "resource-id": resource_id,
                        }
                    )
                )
                with self.assertRaisesRegex(RuntimeError, "canonical Chummer"):
                    driver.read_talent_grant_surface(device, "Active skills")

                device.hierarchy.assert_not_called()
                device.capture.assert_called_once_with(
                    "creation-prerequisite-talent-grant-route-identity-invalid"
                )

    def test_talent_grant_surface_rejects_noncanonical_child_authority(self) -> None:
        selectors = (
            "creation-prerequisite-talent-grant-authority",
            "creation-prerequisite-talent-grant-digest",
            "creation-prerequisite-talent-grant-complete",
            "creation-prerequisite-talent-active-skill-option-choice-0001",
            "creation-prerequisite-talent-skill-group-option-choice-0001",
        )
        for selector in selectors:
            for identity_field in ("package", "resource-id"):
                with self.subTest(
                    selector=selector,
                    identity_field=identity_field,
                ):
                    nodes = self.talent_grant_nodes()
                    child = next(
                        (
                            node
                            for node in nodes
                            if driver._exact_resource_id(node) == selector
                        ),
                        None,
                    )
                    if child is None:
                        child = self.canonical_node(selector)
                        nodes.append(child)
                    if identity_field == "package":
                        child.attributes["package"] = "com.example.forged"
                    else:
                        child.attributes["resource-id"] = (
                            f"com.example.forged:id/{selector}"
                        )
                    device = mock.Mock()
                    device.wait_for_single_exact_resource_id.return_value = (
                        self.canonical_node(
                            "creation-prerequisite-talent-grant-page"
                        )
                    )
                    with mock.patch.object(
                        driver,
                        "acquire_stable_start_origin",
                        return_value=self.priority_rank_origin(nodes),
                    ), mock.patch.object(
                        driver,
                        "scan_forward_with_receipt",
                        return_value=driver.StableViewportScan([nodes], 0),
                    ), self.assertRaisesRegex(RuntimeError, "canonical Chummer"):
                        driver.read_talent_grant_surface(device, "Active skills")

                    device.capture.assert_called_once()

    def test_talent_grant_counts_are_bound_to_exact_singleton_authority(self) -> None:
        nodes = self.talent_grant_nodes()
        nodes[0].attributes["content-desc"] = "Required"
        nodes.append(
            driver.shared.UiNode(
                {
                    "package": "com.example.forged",
                    "resource-id": "com.example.forged:id/unrelated-count",
                    "content-desc": "1 / 1 Active skills",
                }
            )
        )
        device = mock.Mock()
        device.wait_for_single_exact_resource_id.return_value = self.canonical_node(
            "creation-prerequisite-talent-grant-page"
        )
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=self.priority_rank_origin(nodes),
        ), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes], 0),
        ), self.assertRaisesRegex(RuntimeError, "counts=\\[\\]"):
            driver.read_talent_grant_surface(device, "Active skills")

    def test_talent_grant_rejects_duplicate_singletons_in_one_viewport(self) -> None:
        for index in (0, 1, 3):
            with self.subTest(index=index):
                nodes = self.talent_grant_nodes()
                nodes.append(driver.shared.UiNode(dict(nodes[index].attributes)))
                device = mock.Mock()
                device.wait_for_single_exact_resource_id.return_value = (
                    self.canonical_node("creation-prerequisite-talent-grant-page")
                )
                with mock.patch.object(
                    driver,
                    "acquire_stable_start_origin",
                    return_value=self.priority_rank_origin(nodes),
                ), mock.patch.object(
                    driver,
                    "scan_forward_with_receipt",
                    return_value=driver.StableViewportScan([nodes], 0),
                ), self.assertRaisesRegex(RuntimeError, "singletonDuplicates"):
                    driver.read_talent_grant_surface(device, "Active skills")

    def test_talent_grant_surface_rejects_duplicate_and_misplaced_selected_slots(
        self,
    ) -> None:
        cases = (
            (
                "duplicate",
                "✓ Arcana. Selected slot 1 · Magical Active. "
                "Selected slot 1 · Attribute MAG",
                (1, 1),
                (True, False),
            ),
            (
                "misplaced",
                "✓ Arcana. Magical Active. Selected slot 1 · Attribute MAG",
                (1,),
                (False,),
            ),
        )
        for name, projected_detail, expected_slots, expected_placements in cases:
            with self.subTest(name=name):
                nodes = self.talent_grant_nodes()
                option = nodes[2]
                option.attributes["content-desc"] = projected_detail
                self.assertEqual(
                    (
                        ("Arcana. Magical Active. Attribute MAG",),
                        expected_slots,
                        expected_placements,
                    ),
                    driver._talent_option_identity_and_slots(option),
                )
                self.assertFalse(driver._talent_option_has_exact_dynamic_slot(option))

                device = mock.Mock()
                device.hierarchy.return_value = nodes
                device.node_has_tappable_bounds.return_value = True
                device.wait_for_single_exact_resource_id.return_value = (
                    self.canonical_node("creation-prerequisite-talent-grant-page")
                )
                with mock.patch.object(driver.shared, "reset_scroll_to_top"), \
                     mock.patch.object(driver.time, "sleep"), \
                     self.assertRaisesRegex(RuntimeError, "invalidSlotIds"):
                    driver.read_talent_grant_surface(
                        device,
                        "Active skills",
                        max_scrolls=2,
                    )
                device.capture.assert_called_once_with(
                    "creation-prerequisite-talent-grant-cardinality-invalid"
                )

    def test_talent_grant_surface_rejects_malformed_or_opposite_kind_ids(self) -> None:
        malformed = self.talent_grant_nodes(option_id="forged-")
        opposite = self.talent_grant_nodes()
        opposite.append(
            driver.shared.UiNode(
                {
                    "package": driver.shared.PACKAGE,
                    "resource-id": (
                        f"{driver.shared.PACKAGE}:id/"
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
                device.wait_for_single_exact_resource_id.return_value = (
                    self.canonical_node("creation-prerequisite-talent-grant-page")
                )
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
        device.wait_for_single_exact_resource_id.return_value = self.canonical_node(
            "creation-prerequisite-talent-grant-page"
        )
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
            "04e1eb3e-e82d-485b-a7fd-1e677df2a070"
        )
        disabled_id = (
            "creation-prerequisite-talent-active-skill-option-"
            "cd9f6bf7-fa48-464b-9a8f-c7ce26713a72"
        )
        selected_detail = (
            "Perception. Physical Active · Attribute INT · "
            "Source 04e1eb3e-e82d-485b-a7fd-1e677df2a070 · Anchors "
            "skills.xml#skill:04e1eb3e-e82d-485b-a7fd-1e677df2a070"
        )
        selected_projected_detail = (
            "✓ Perception. Selected slot 1 · Physical Active · Attribute INT · "
            "Source 04e1eb3e-e82d-485b-a7fd-1e677df2a070 · Anchors "
            "skills.xml#skill:04e1eb3e-e82d-485b-a7fd-1e677df2a070"
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
                "content-desc": selected_projected_detail,
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,653][984,881]",
            }
        )
        authority = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-talent-grant-authority",
                "content-desc": "Required. 1 / 1 Active skills",
                "bounds": "[53,350][1028,550]",
            }
        )
        digest_node = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-talent-grant-digest",
                "text": digest,
                "bounds": "[53,560][1028,700]",
            }
        )
        completion = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-talent-grant-complete",
                "text": "Continue with exact grant",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,1953][1028,2085]",
            }
        )

        class ArtifactTopologyDevice:
            def __init__(self) -> None:
                self.viewport = 6
                self.screens = {
                    0: [authority, digest_node],
                    4: [disabled, selected],
                    6: [completion],
                }
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.forward_swipes = 0
                self.reverse_distances: list[float] = []
                self.forward_distances: list[float] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                return self.screens.get(
                    self.viewport,
                    [driver.shared.UiNode({"resource-id": f"viewport-{self.viewport}"})],
                )

            def swipe_down(self, *, distance_ratio):
                if distance_ratio not in {
                    driver.TALENT_GRANT_SCAN_GESTURE_RATIO,
                    driver.TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO,
                }:
                    raise AssertionError(f"unexpected reverse ratio: {distance_ratio!r}")
                self.reverse_swipes += 1
                self.reverse_distances.append(distance_ratio)
                self.viewport = max(0, self.viewport - 1)

            def swipe_up(self, *, distance_ratio):
                if distance_ratio not in {
                    driver.TALENT_GRANT_SCAN_GESTURE_RATIO,
                    driver.TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO,
                }:
                    raise AssertionError(f"unexpected forward ratio: {distance_ratio!r}")
                self.forward_swipes += 1
                self.forward_distances.append(distance_ratio)
                self.viewport = min(6, self.viewport + 1)

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node in (authority, digest_node, disabled, selected, completion)

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
        self.assertEqual(0, viewport)
        self.assertEqual(9, device.hierarchy_reads)
        self.assertEqual(6, device.reverse_swipes)
        self.assertEqual(0, device.forward_swipes)
        self.assertEqual(
            [driver.TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO] * 2
            + [driver.TALENT_GRANT_SCAN_GESTURE_RATIO] * 4,
            device.reverse_distances,
        )
        self.assertEqual([], device.forward_distances)

        preferred_device = ArtifactTopologyDevice()
        preferred_state, preferred_viewport = driver.read_talent_grant_grouped_state(
            preferred_device,
            "Active skills",
            baseline,
            navigation,
            6,
            expected_selected_option_ids=(selected_id,),
            expected_completion_enabled=True,
            preferred_final_resource_id=selected_id,
            evidence_prefix="artifact-complete-preferred-final",
        )
        self.assertEqual(state, preferred_state)
        self.assertEqual(4, preferred_viewport)
        self.assertEqual(13, preferred_device.hierarchy_reads)
        self.assertEqual(6, preferred_device.reverse_swipes)
        self.assertEqual(4, preferred_device.forward_swipes)
        self.assertEqual(
            [driver.TALENT_GRANT_SCAN_GESTURE_RATIO] * 6,
            preferred_device.reverse_distances,
        )
        self.assertEqual(
            [driver.TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO] * 4,
            preferred_device.forward_distances,
        )

    def test_grouped_talent_reacquisition_accepts_the_last_scan_bound(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        target = driver.shared.UiNode(
            {"resource-id": resource_id, "bounds": "[53,350][1028,550]"}
        )

        class LastBoundDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.reverse_distances: list[float] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                return (
                    [target]
                    if self.hierarchy_reads == 4
                    else [driver.shared.UiNode({"resource-id": f"viewport-{self.hierarchy_reads}"})]
                )

            def swipe_down(self, *, distance_ratio):
                self.reverse_swipes += 1
                self.reverse_distances.append(distance_ratio)

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is target

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
        self.assertEqual("reverse", snapshot.reacquisition_direction)
        self.assertEqual(3, snapshot.reacquisition_swipes)
        self.assertEqual([target], snapshot.nodes)
        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual(3, device.reverse_swipes)
        self.assertEqual(
            [driver.TALENT_GRANT_SCAN_GESTURE_RATIO] * 3,
            device.reverse_distances,
        )

    def test_grouped_talent_reacquisition_continues_past_clipped_exact_id(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        clipped = driver.shared.UiNode(
            {"resource-id": resource_id, "bounds": "[53,-200][1028,-20]"}
        )
        visible = driver.shared.UiNode(
            {"resource-id": resource_id, "bounds": "[53,350][1028,550]"}
        )

        class ClippedThenVisibleDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.reverse_swipes = 0

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [clipped] if self.reverse_swipes == 0 else [visible]

            def swipe_down(self, *, distance_ratio):
                if distance_ratio != driver.TALENT_GRANT_SCAN_GESTURE_RATIO:
                    raise AssertionError(f"unexpected reverse ratio: {distance_ratio!r}")
                self.reverse_swipes += 1

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is visible

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = ClippedThenVisibleDevice()
        snapshot = driver.reacquire_exact_talent_state_group(
            device,
            (resource_id,),
            2,
            0,
            2,
            evidence_prefix="clipped-then-visible",
        )

        self.assertIs(visible, snapshot.resources[resource_id])
        self.assertEqual(0, snapshot.logical_viewport)
        self.assertEqual(1, snapshot.reacquisition_swipes)
        self.assertEqual(2, device.hierarchy_reads)
        self.assertEqual(1, device.reverse_swipes)

    def test_grouped_talent_overlap_normalizes_to_inventory_target_viewport(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        target = driver.shared.UiNode(
            {"resource-id": resource_id, "bounds": "[53,350][1028,550]"}
        )
        device = mock.Mock()
        device.hierarchy.return_value = [target]
        device.node_has_tappable_bounds.return_value = True

        snapshot = driver.reacquire_exact_talent_state_group(
            device,
            (resource_id,),
            4,
            0,
            6,
            evidence_prefix="overlap",
        )

        self.assertEqual(0, snapshot.logical_viewport)
        self.assertEqual(0, snapshot.reacquisition_swipes)
        device.swipe_down.assert_not_called()
        device.swipe_up.assert_not_called()

    def test_grouped_talent_reacquisition_handles_noninvertible_scroll_geometry(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-perception"
        target = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "bounds": "[53,350][1028,550]",
            }
        )

        class NoninvertibleScrollDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.forward_swipes = 0
                self.forward_distances: list[float] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                return (
                    [target]
                    if self.forward_swipes == 10
                    else [driver.shared.UiNode({"resource-id": f"viewport-{self.forward_swipes}"})]
                )

            def swipe_up(self, *, distance_ratio):
                self.forward_swipes += 1
                self.forward_distances.append(distance_ratio)

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is target

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = NoninvertibleScrollDevice()
        receipts: list[dict[str, object]] = []
        snapshot = driver.reacquire_exact_talent_state_group(
            device,
            (resource_id,),
            2,
            8,
            11,
            evidence_prefix="mutated-row-height",
            scan_observer=receipts.append,
        )

        self.assertIs(target, snapshot.resources[resource_id])
        self.assertEqual(8, snapshot.logical_viewport)
        self.assertEqual("forward", snapshot.reacquisition_direction)
        self.assertEqual(10, snapshot.reacquisition_swipes)
        self.assertEqual(11, device.hierarchy_reads)
        self.assertEqual(
            [driver.TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO] * 10,
            device.forward_distances,
        )
        self.assertEqual(1, len(receipts))
        self.assertEqual(
            {
                "scanId": "mutated-row-height-reacquisition",
                "status": "resolved",
                "direction": "forward",
                "distanceRatio": driver.TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO,
                "startingViewport": 2,
                "targetViewport": 8,
                "normalizedTargetViewport": 8,
                "measuredDelta": 6,
                "configuredMaxScrolls": 40,
                "catalogMovementExtent": 11,
                "navigationMode": (
                    "measured-direction-stable-boundary-overlap-recovery"
                ),
                "stableRepeats": 2,
                "stableBoundaryProven": False,
                "deadlineEnforced": False,
                "exactResourceIds": [resource_id],
                "screens": 11,
                "swipes": 10,
                "emptyHierarchyReads": 0,
                "systemUiDismissals": 0,
                "maximumEmptyHierarchyReads": 3,
                "maximumSystemUiDismissals": 3,
                "hierarchyReadCount": 11,
            },
            {
                key: receipts[0][key]
                for key in (
                    "scanId",
                    "status",
                    "direction",
                    "distanceRatio",
                    "startingViewport",
                    "targetViewport",
                    "normalizedTargetViewport",
                    "measuredDelta",
                    "configuredMaxScrolls",
                    "catalogMovementExtent",
                    "navigationMode",
                    "stableRepeats",
                    "stableBoundaryProven",
                    "deadlineEnforced",
                    "exactResourceIds",
                    "screens",
                    "swipes",
                    "emptyHierarchyReads",
                    "systemUiDismissals",
                    "maximumEmptyHierarchyReads",
                    "maximumSystemUiDismissals",
                    "hierarchyReadCount",
                )
            },
        )
        self.assertGreaterEqual(receipts[0]["hierarchyElapsedMs"], 0)
        self.assertGreaterEqual(receipts[0]["maximumHierarchyReadMs"], 0)
        self.assertGreaterEqual(receipts[0]["elapsedMs"], 0)

    def test_grouped_talent_order_is_greedy_nearest_with_lower_tie_break(self) -> None:
        remaining = {0, 7, 9}
        current = 7
        order: list[int] = []
        while remaining:
            current = driver._nearest_talent_group_viewport(current, remaining)
            remaining.remove(current)
            order.append(current)
        self.assertEqual([7, 9, 0], order)
        self.assertEqual(0, driver._nearest_talent_group_viewport(3, {0, 6}))
        for current_viewport, candidates in ((True, {0}), (0, set()), (0, {False})):
            with self.subTest(current=current_viewport, candidates=candidates), self.assertRaises(
                ValueError
            ):
                driver._nearest_talent_group_viewport(current_viewport, candidates)

    def test_grouped_talent_option_recovers_c7f_selected_perception_after_stable_boundary(
        self,
    ) -> None:
        resource_id = (
            "creation-prerequisite-talent-active-skill-option-"
            "04e1eb3e-e82d-485b-a7fd-1e677df2a070"
        )
        target = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "content-desc": (
                    "✓ Perception. Selected slot 1 · Physical Active · Attribute INT"
                ),
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,467][984,725]",
            }
        )

        class CoarseSkipDevice:
            def __init__(self) -> None:
                self.coarse_swipes = 0
                self.recovery_swipes = 0
                self.forward_ratios: list[float] = []
                self.reverse_ratios: list[float] = []

            def hierarchy(self):
                if self.recovery_swipes >= 2:
                    return [target]
                if self.recovery_swipes:
                    return [driver.shared.UiNode({
                        "resource-id": "recovery-moving",
                        "bounds": "[53,-900][1028,-700]",
                    })]
                if self.coarse_swipes:
                    return [driver.shared.UiNode({
                        "resource-id": "stable-bottom",
                        "bounds": "[53,1800][1028,2050]",
                    })]
                return [driver.shared.UiNode({
                    "resource-id": "top",
                    "bounds": "[53,350][1028,550]",
                })]

            def swipe_up(self, *, distance_ratio):
                self.forward_ratios.append(distance_ratio)
                self.coarse_swipes += 1

            def swipe_down(self, *, distance_ratio):
                self.reverse_ratios.append(distance_ratio)
                self.recovery_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is target

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = CoarseSkipDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"):
            snapshot = driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                0,
                7,
                9,
                evidence_prefix="c7f-selected-perception",
                scan_observer=receipts.append,
            )

        self.assertIs(target, snapshot.resources[resource_id])
        self.assertEqual(7, snapshot.logical_viewport)
        self.assertEqual(5, snapshot.reacquisition_swipes)
        self.assertEqual([0.22] * 3, device.forward_ratios)
        self.assertEqual([0.22] * 2, device.reverse_ratios)
        self.assertEqual("resolved", receipts[0]["status"])
        self.assertEqual(0.22, receipts[0]["distanceRatio"])
        self.assertEqual(0.22, receipts[0]["primaryDistanceRatio"])
        self.assertIs(receipts[0]["primaryStableBoundaryProven"], True)
        self.assertIs(receipts[0]["recoveryEligible"], True)
        self.assertIs(receipts[0]["recoveryUsed"], True)
        self.assertEqual("reverse", receipts[0]["recoveryDirection"])
        self.assertEqual(3, receipts[0]["primarySwipes"])
        self.assertEqual(2, receipts[0]["recoverySwipes"])
        self.assertIs(receipts[0]["recoveryStableBoundaryProven"], False)

    def test_exact_unselected_perception_reselect_uses_overlap_recovery_then_taps(
        self,
    ) -> None:
        resource_id = (
            "creation-prerequisite-talent-active-skill-option-"
            "04e1eb3e-e82d-485b-a7fd-1e677df2a070"
        )
        target = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "content-desc": "Perception. Physical Active · Attribute INT",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,467][984,724]",
            }
        )

        class ReselectDevice:
            def __init__(self) -> None:
                self.coarse_swipes = 0
                self.recovery_swipes = 0
                self.taps: list[tuple[str, ...]] = []

            def hierarchy(self):
                if self.recovery_swipes >= 1:
                    return [target]
                if self.coarse_swipes:
                    return [driver.shared.UiNode({
                        "resource-id": "stable-bottom",
                        "bounds": "[53,1800][1028,2050]",
                    })]
                return [driver.shared.UiNode({"resource-id": "top"})]

            def swipe_up(self, *, distance_ratio):
                self.asserted_primary_ratio = distance_ratio
                self.coarse_swipes += 1

            def swipe_down(self, *, distance_ratio):
                self.asserted_recovery_ratio = distance_ratio
                self.recovery_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is target

            def shell(self, *args):
                self.taps.append(args)

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        navigation = {
            "endViewport": 9,
            "resourceViewports": {resource_id: 7},
            "resourceDetails": {
                resource_id: ("Perception. Physical Active · Attribute INT",),
            },
        }
        device = ReselectDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"):
            viewport = driver.tap_exact_measured_talent_resource(
                device,
                resource_id,
                navigation,
                0,
                evidence_prefix="explicit-unselected-reselect",
                scan_observer=receipts.append,
            )

        self.assertEqual(7, viewport)
        self.assertEqual(3, device.coarse_swipes)
        self.assertEqual(1, device.recovery_swipes)
        self.assertEqual(0.22, device.asserted_primary_ratio)
        self.assertEqual(0.22, device.asserted_recovery_ratio)
        self.assertEqual([("input", "tap", "541", "595")], device.taps)
        self.assertIs(receipts[0]["recoveryUsed"], True)

    def test_talent_option_recovery_rejects_duplicate_exact_id(self) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-perception"
        target = driver.shared.UiNode(
            {"resource-id": resource_id, "bounds": "[53,350][1028,550]"}
        )

        class DuplicateRecoveryDevice:
            def __init__(self) -> None:
                self.coarse_swipes = 0
                self.recovery_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                if self.recovery_swipes:
                    return [target, target]
                return [driver.shared.UiNode({
                    "resource-id": "top" if self.coarse_swipes == 0 else "bottom"
                })]

            def swipe_up(self, *, distance_ratio):
                self.asserted_primary_ratio = distance_ratio
                self.coarse_swipes += 1

            def swipe_down(self, *, distance_ratio):
                self.asserted_recovery_ratio = distance_ratio
                self.recovery_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = DuplicateRecoveryDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "cardinality 2",
        ):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                0,
                7,
                9,
                evidence_prefix="duplicate-recovery",
                scan_observer=receipts.append,
            )

        self.assertEqual(3, device.coarse_swipes)
        self.assertEqual(1, device.recovery_swipes)
        self.assertEqual(["duplicate-recovery-cardinality-invalid"], device.captures)
        self.assertEqual("cardinality-invalid", receipts[0]["status"])
        self.assertIs(receipts[0]["primaryStableBoundaryProven"], True)
        self.assertIs(receipts[0]["recoveryUsed"], True)

    def test_talent_option_recovery_fails_at_opposite_stable_boundary(self) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-perception"

        class OppositeBoundaryDevice:
            def __init__(self) -> None:
                self.coarse_swipes = 0
                self.recovery_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                resource = (
                    "recovery-boundary"
                    if self.recovery_swipes
                    else "top"
                    if self.coarse_swipes == 0
                    else "primary-boundary"
                )
                return [driver.shared.UiNode({"resource-id": resource})]

            def swipe_up(self, *, distance_ratio):
                self.coarse_swipes += 1

            def swipe_down(self, *, distance_ratio):
                self.recovery_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = OppositeBoundaryDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "opposite stable physical boundary",
        ):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                0,
                7,
                9,
                evidence_prefix="opposite-boundary",
                scan_observer=receipts.append,
            )

        self.assertEqual(3, device.coarse_swipes)
        self.assertEqual(3, device.recovery_swipes)
        self.assertEqual(
            ["opposite-boundary-recovery-stable-boundary-unresolved"],
            device.captures,
        )
        self.assertEqual("recovery-stable-boundary-unresolved", receipts[0]["status"])
        self.assertIs(receipts[0]["recoveryStableBoundaryProven"], True)

    def test_talent_option_recovery_enforces_separate_hard_forty_bound(self) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-perception"

        class MovingRecoveryDevice:
            def __init__(self) -> None:
                self.coarse_swipes = 0
                self.recovery_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                resource = (
                    f"recovery-{self.recovery_swipes}"
                    if self.recovery_swipes
                    else "top"
                    if self.coarse_swipes == 0
                    else "primary-boundary"
                )
                return [driver.shared.UiNode({"resource-id": resource})]

            def swipe_up(self, *, distance_ratio):
                self.coarse_swipes += 1

            def swipe_down(self, *, distance_ratio):
                self.recovery_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = MovingRecoveryDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "40-swipe reverse recovery hard bound",
        ):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                0,
                7,
                9,
                evidence_prefix="recovery-hard-bound",
                scan_observer=receipts.append,
            )

        self.assertEqual(3, device.coarse_swipes)
        self.assertEqual(40, device.recovery_swipes)
        self.assertEqual(["recovery-hard-bound-unavailable"], device.captures)
        self.assertEqual("recovery-hard-bound-unresolved", receipts[0]["status"])
        self.assertEqual(40, receipts[0]["recoveryConfiguredMaxScrolls"])

    def test_talent_option_recovery_threads_same_deadline_to_both_directions(self) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-perception"
        target = driver.shared.UiNode(
            {"resource-id": resource_id, "bounds": "[53,350][1028,550]"}
        )

        class DeadlineRecoveryDevice:
            def __init__(self) -> None:
                self.coarse_swipes = 0
                self.recovery_swipes = 0
                self.hierarchy_deadlines: list[float] = []
                self.forward_deadlines: list[float] = []
                self.reverse_deadlines: list[float] = []
                self.system_ui_deadlines: list[float] = []
                self.bounds_deadlines: list[float] = []

            def hierarchy(self, *, deadline):
                self.hierarchy_deadlines.append(deadline)
                if self.recovery_swipes:
                    return [target]
                return [driver.shared.UiNode({
                    "resource-id": "top" if self.coarse_swipes == 0 else "bottom"
                })]

            def swipe_up(self, *, distance_ratio, deadline):
                self.forward_deadlines.append(deadline)
                self.coarse_swipes += 1

            def swipe_down(self, *, distance_ratio, deadline):
                self.reverse_deadlines.append(deadline)
                self.recovery_swipes += 1

            def dismiss_system_ui_anr(self, _nodes, *, deadline):
                self.system_ui_deadlines.append(deadline)
                return False

            def node_has_tappable_bounds(self, node, *, deadline):
                self.bounds_deadlines.append(deadline)
                return node is target

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        deadline = driver.time.monotonic() + 30
        device = DeadlineRecoveryDevice()
        with mock.patch.object(driver.time, "sleep"):
            snapshot = driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                0,
                7,
                9,
                evidence_prefix="recovery-deadline",
                deadline=deadline,
            )

        self.assertIs(target, snapshot.resources[resource_id])
        self.assertTrue(device.hierarchy_deadlines)
        self.assertEqual({deadline}, set(device.hierarchy_deadlines))
        self.assertEqual([deadline] * 3, device.forward_deadlines)
        self.assertEqual([deadline], device.reverse_deadlines)
        self.assertEqual({deadline}, set(device.system_ui_deadlines))
        self.assertEqual([deadline], device.bounds_deadlines)

    def test_talent_option_recovery_has_fresh_transient_retry_budgets(self) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-perception"
        target = driver.shared.UiNode(
            {"resource-id": resource_id, "bounds": "[53,350][1028,550]"}
        )
        overlay = driver.shared.UiNode({"resource-id": "system-overlay"})

        class SeparateStageRetryDevice:
            def __init__(self) -> None:
                self.coarse_swipes = 0
                self.recovery_swipes = 0
                self.primary_prelude = 0
                self.recovery_prelude = 0

            def hierarchy(self):
                if self.recovery_swipes:
                    self.recovery_prelude += 1
                    if self.recovery_prelude == 1:
                        return []
                    if self.recovery_prelude == 2:
                        return [overlay]
                    return [target]
                if self.coarse_swipes == 0:
                    self.primary_prelude += 1
                    if self.primary_prelude == 1:
                        return []
                    if self.primary_prelude == 2:
                        return [overlay]
                    return [driver.shared.UiNode({"resource-id": "top"})]
                return [driver.shared.UiNode({"resource-id": "bottom"})]

            def swipe_up(self, *, distance_ratio):
                self.coarse_swipes += 1

            def swipe_down(self, *, distance_ratio):
                self.recovery_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(nodes):
                return nodes == [overlay]

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is target

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = SeparateStageRetryDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"):
            snapshot = driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                0,
                7,
                9,
                evidence_prefix="separate-stage-retries",
                max_empty_hierarchy_reads=1,
                max_system_ui_dismissals=1,
                scan_observer=receipts.append,
            )

        self.assertIs(target, snapshot.resources[resource_id])
        self.assertEqual(1, receipts[0]["primaryEmptyHierarchyReads"])
        self.assertEqual(1, receipts[0]["recoveryEmptyHierarchyReads"])
        self.assertEqual(1, receipts[0]["primarySystemUiDismissals"])
        self.assertEqual(1, receipts[0]["recoverySystemUiDismissals"])
        self.assertEqual(2, receipts[0]["emptyHierarchyReads"])
        self.assertEqual(2, receipts[0]["systemUiDismissals"])

    def test_grouped_talent_reacquisition_closes_cfb_artifact_asymmetric_reverse_geometry(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        clipped = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "content-desc": "1 / 1 Active skills",
                "bounds": "[53,-2128][1028,-1468]",
            }
        )
        visible = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "content-desc": "1 / 1 Active skills",
                "bounds": "[53,350][1028,1010]",
            }
        )

        class AsymmetricArtifactDevice:
            def __init__(self) -> None:
                self.reverse_swipes = 0
                self.hierarchy_reads = 0

            def hierarchy(self):
                self.hierarchy_reads += 1
                if self.reverse_swipes >= 13:
                    return [visible]
                if self.reverse_swipes == 10:
                    return [clipped]
                return [driver.shared.UiNode({
                    "resource-id": f"physical-reverse-{self.reverse_swipes}",
                    "bounds": f"[53,{-4000 + self.reverse_swipes * 180}][1028,{-3800 + self.reverse_swipes * 180}]",
                })]

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.reverse_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is visible

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = AsymmetricArtifactDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"):
            snapshot = driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                10,
                0,
                10,
                evidence_prefix="cfb-artifact",
                scan_observer=receipts.append,
            )

        self.assertIs(visible, snapshot.resources[resource_id])
        self.assertEqual(0, snapshot.logical_viewport)
        self.assertEqual(13, snapshot.reacquisition_swipes)
        self.assertEqual(14, device.hierarchy_reads)
        self.assertEqual(40, receipts[0]["configuredMaxScrolls"])
        self.assertEqual(10, receipts[0]["catalogMovementExtent"])
        self.assertEqual(
            "measured-direction-stable-boundary-overlap-recovery",
            receipts[0]["navigationMode"],
        )
        self.assertIs(receipts[0]["stableBoundaryProven"], False)

    def test_grouped_talent_reacquisition_rejects_hard_bound_without_boundary_proof(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"

        class MovingBeyondHardBoundDevice:
            def __init__(self) -> None:
                self.reverse_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                return [driver.shared.UiNode({
                    "resource-id": f"moving-{self.reverse_swipes}",
                    "bounds": f"[53,{-8000 + self.reverse_swipes * 100}][1028,{-7800 + self.reverse_swipes * 100}]",
                })]

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.reverse_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = MovingBeyondHardBoundDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "boundary-checked 40-swipe reverse primary hard bound",
        ):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                10,
                0,
                10,
                evidence_prefix="hard-bound",
                scan_observer=receipts.append,
            )

        self.assertEqual(40, device.reverse_swipes)
        self.assertEqual(["hard-bound-unavailable"], device.captures)
        self.assertEqual("primary-hard-bound-unresolved", receipts[0]["status"])
        self.assertIs(receipts[0]["stableBoundaryProven"], False)

    def test_grouped_talent_reacquisition_enforces_absolute_phase_deadline(
        self,
    ) -> None:
        device = mock.Mock()
        receipts: list[dict[str, object]] = []
        with self.assertRaisesRegex(RuntimeError, "phase deadline expired"):
            driver.reacquire_exact_talent_state_group(
                device,
                ("creation-prerequisite-talent-grant-authority",),
                10,
                0,
                10,
                evidence_prefix="expired",
                deadline=driver.time.monotonic() - 1,
                scan_observer=receipts.append,
            )
        device.hierarchy.assert_not_called()
        self.assertEqual("deadline-unresolved", receipts[0]["status"])
        self.assertIs(receipts[0]["deadlineEnforced"], True)

    def test_grouped_talent_reacquisition_passes_deadline_to_hierarchy_and_gesture(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        target = driver.shared.UiNode({
            "resource-id": resource_id,
            "bounds": "[53,350][1028,550]",
        })

        class DeadlineDevice:
            def __init__(self) -> None:
                self.swipes = 0
                self.hierarchy_deadlines: list[float] = []
                self.swipe_deadlines: list[float] = []
                self.system_ui_deadlines: list[float] = []
                self.bounds_deadlines: list[float] = []

            def hierarchy(self, *, deadline):
                self.hierarchy_deadlines.append(deadline)
                return [target] if self.swipes == 1 else [driver.shared.UiNode({
                    "resource-id": "moving",
                    "bounds": "[53,-500][1028,-300]",
                })]

            def swipe_down(self, *, distance_ratio, deadline):
                self.asserted_distance_ratio = distance_ratio
                self.swipe_deadlines.append(deadline)
                self.swipes += 1

            def dismiss_system_ui_anr(self, _nodes, *, deadline):
                self.system_ui_deadlines.append(deadline)
                return False

            def node_has_tappable_bounds(self, node, *, deadline):
                self.bounds_deadlines.append(deadline)
                return node is target

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        deadline = driver.time.monotonic() + 30
        device = DeadlineDevice()
        with mock.patch.object(driver.time, "sleep"):
            snapshot = driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                10,
                0,
                10,
                evidence_prefix="deadline-thread",
                deadline=deadline,
            )

        self.assertIs(target, snapshot.resources[resource_id])
        self.assertEqual([deadline, deadline], device.hierarchy_deadlines)
        self.assertEqual([deadline], device.swipe_deadlines)
        self.assertEqual([deadline], device.system_ui_deadlines)
        self.assertEqual([deadline], device.bounds_deadlines)

    def test_grouped_talent_bounds_deadline_expiry_authorizes_no_gesture_or_action(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        resource_id = "creation-prerequisite-talent-grant-authority"
        target = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "bounds": "[53,350][1028,550]",
            }
        )
        error = driver.shared.AdbOperationDeadlineExceeded(
            "phase deadline expired while resolving display bounds"
        )
        device = mock.Mock()
        device.hierarchy.return_value = [target]
        device.node_has_tappable_bounds.side_effect = error

        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                0,
                0,
                0,
                evidence_prefix="bounds-deadline",
                deadline=deadline,
            )

        device.hierarchy.assert_called_once_with(deadline=deadline)
        device.node_has_tappable_bounds.assert_called_once_with(
            target,
            deadline=deadline,
        )
        device.swipe_down.assert_not_called()
        device.swipe_up.assert_not_called()
        device.shell.assert_not_called()

    def test_measured_talent_tap_recheck_uses_deadline_and_never_taps_after_expiry(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        resource_id = "creation-prerequisite-talent-grant-complete"
        target = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,350][1028,550]",
            }
        )
        snapshot = driver.TalentStateGroupSnapshot(
            nodes=[target],
            resources={resource_id: target},
            logical_viewport=0,
            reacquisition_direction="none",
            reacquisition_swipes=0,
        )
        device = mock.Mock()
        device.node_has_tappable_bounds.side_effect = (
            driver.shared.AdbOperationDeadlineExceeded(
                "phase deadline expired while rechecking tap bounds"
            )
        )
        navigation = {
            "endViewport": 0,
            "resourceViewports": {resource_id: 0},
        }

        with mock.patch.object(
            driver,
            "reacquire_exact_talent_state_group",
            return_value=snapshot,
        ) as reacquire, self.assertRaises(
            driver.shared.AdbOperationDeadlineExceeded
        ):
            driver.tap_exact_measured_talent_resource(
                device,
                resource_id,
                navigation,
                0,
                evidence_prefix="tap-bounds-deadline",
                deadline=deadline,
            )

        reacquire.assert_called_once()
        self.assertEqual(deadline, reacquire.call_args.kwargs["deadline"])
        device.node_has_tappable_bounds.assert_called_once_with(
            target,
            deadline=deadline,
        )
        device.shell.assert_not_called()

    def test_grouped_talent_boundary_uses_full_accessibility_signature(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        observations = (
            {"text": "A", "checked": "false", "bounds": "[53,-900][1028,-700]"},
            {"text": "A", "checked": "false", "bounds": "[53,-700][1028,-500]"},
            {"text": "B", "checked": "true", "bounds": "[53,-700][1028,-500]"},
            {"text": "B", "checked": "true", "bounds": "[53,-700][1028,-500]"},
            {"text": "B", "checked": "true", "bounds": "[53,-700][1028,-500]"},
        )

        class SignatureDevice:
            def __init__(self) -> None:
                self.index = 0
                self.swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                value = observations[min(self.index, len(observations) - 1)]
                return [driver.shared.UiNode({"resource-id": "same-id", **value})]

            def swipe_down(self, *, distance_ratio):
                self.asserted_distance_ratio = distance_ratio
                self.swipes += 1
                self.index += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = SignatureDevice()
        receipts: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "stable physical boundary",
        ):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                10,
                0,
                10,
                evidence_prefix="signature-boundary",
                scan_observer=receipts.append,
            )

        self.assertEqual(4, device.swipes)
        self.assertEqual(
            ["signature-boundary-stable-boundary-unresolved"],
            device.captures,
        )
        self.assertEqual("stable-boundary-unresolved", receipts[0]["status"])
        self.assertIs(receipts[0]["stableBoundaryProven"], True)

    def test_grouped_talent_reacquisition_fails_closed_on_zero_delta_geometry_drift(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-grant-complete"
        device = mock.Mock()
        device.hierarchy.return_value = [driver.shared.UiNode({})]
        device.dismiss_system_ui_anr.return_value = False
        receipts: list[dict[str, object]] = []

        with self.assertRaisesRegex(RuntimeError, "without a safe directional hint"):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                5,
                5,
                10,
                evidence_prefix="zero-delta",
                scan_observer=receipts.append,
            )

        device.swipe_down.assert_not_called()
        device.swipe_up.assert_not_called()
        device.capture.assert_called_once_with("zero-delta-zero-delta-unresolved")
        self.assertEqual("zero-delta-unresolved", receipts[0]["status"])
        self.assertEqual(0, receipts[0]["configuredMaxScrolls"])

    def test_grouped_talent_reacquisition_rejects_proven_reverse_boundary(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"

        class OneBeyondDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
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
            "stable physical boundary",
        ):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                3,
                0,
                3,
                evidence_prefix="one-beyond",
            )

        self.assertEqual(3, device.hierarchy_reads)
        self.assertEqual(2, device.reverse_swipes)
        self.assertEqual(
            ["one-beyond-stable-boundary-unresolved"],
            device.captures,
        )

    def test_non_option_talent_groups_never_enter_overlap_recovery(self) -> None:
        for resource_id in (
            "creation-prerequisite-talent-grant-authority",
            "creation-prerequisite-talent-grant-digest",
            "creation-prerequisite-talent-grant-complete",
        ):
            with self.subTest(resource_id=resource_id):
                class StableBoundaryDevice:
                    def __init__(self) -> None:
                        self.reverse_ratios: list[float] = []
                        self.forward_ratios: list[float] = []

                    @staticmethod
                    def hierarchy():
                        return [driver.shared.UiNode({"resource-id": "boundary"})]

                    def swipe_down(self, *, distance_ratio):
                        self.reverse_ratios.append(distance_ratio)

                    def swipe_up(self, *, distance_ratio):
                        self.forward_ratios.append(distance_ratio)

                    @staticmethod
                    def dismiss_system_ui_anr(_nodes):
                        return False

                    @staticmethod
                    def capture(_name):
                        return None

                device = StableBoundaryDevice()
                receipts: list[dict[str, object]] = []
                with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
                    RuntimeError,
                    "without an authorized option recovery",
                ):
                    driver.reacquire_exact_talent_state_group(
                        device,
                        (resource_id,),
                        3,
                        0,
                        3,
                        evidence_prefix="non-option",
                        scan_observer=receipts.append,
                    )
                self.assertEqual([0.60, 0.60], device.reverse_ratios)
                self.assertEqual([], device.forward_ratios)
                self.assertIs(receipts[0]["recoveryEligible"], False)
                self.assertIs(receipts[0]["recoveryUsed"], False)
                self.assertEqual(0, receipts[0]["recoveryConfiguredMaxScrolls"])

    def test_exact_talent_option_detail_drift_after_recovery_still_fails_closed(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-perception"
        changed = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "content-desc": "Perception changed. Physical Active",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,467][984,724]",
            }
        )

        class DriftAfterRecoveryDevice:
            def __init__(self) -> None:
                self.coarse_swipes = 0
                self.recovery_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                if self.recovery_swipes:
                    return [changed]
                return [driver.shared.UiNode({
                    "resource-id": "top" if self.coarse_swipes == 0 else "bottom"
                })]

            def swipe_up(self, *, distance_ratio):
                self.coarse_swipes += 1

            def swipe_down(self, *, distance_ratio):
                self.recovery_swipes += 1

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is changed

            def capture(self, name):
                self.captures.append(name)

            @staticmethod
            def shell(*_args):
                raise AssertionError("detail-drift option must not be tapped")

        navigation = {
            "endViewport": 9,
            "resourceViewports": {resource_id: 7},
            "resourceDetails": {resource_id: ("Perception. Physical Active",)},
        }
        device = DriftAfterRecoveryDevice()
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "changed exact option detail",
        ):
            driver.tap_exact_measured_talent_resource(
                device,
                resource_id,
                navigation,
                0,
                evidence_prefix="recovered-detail-drift",
            )
        self.assertEqual(3, device.coarse_swipes)
        self.assertEqual(1, device.recovery_swipes)
        self.assertEqual(["recovered-detail-drift-detail-drift"], device.captures)

    def test_grouped_talent_forward_reacquisition_accepts_the_last_scan_bound(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-grant-complete"
        target = driver.shared.UiNode(
            {"resource-id": resource_id, "bounds": "[53,350][1028,550]"}
        )

        class LastForwardBoundDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.forward_distances: list[float] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                return (
                    [target]
                    if self.hierarchy_reads == 4
                    else [driver.shared.UiNode({"resource-id": f"viewport-{self.hierarchy_reads}"})]
                )

            def swipe_up(self, *, distance_ratio):
                self.forward_distances.append(distance_ratio)

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is target

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = LastForwardBoundDevice()
        snapshot = driver.reacquire_exact_talent_state_group(
            device,
            (resource_id,),
            0,
            3,
            3,
            evidence_prefix="forward-last-bound",
        )

        self.assertIs(target, snapshot.resources[resource_id])
        self.assertEqual(3, snapshot.logical_viewport)
        self.assertEqual("forward", snapshot.reacquisition_direction)
        self.assertEqual(3, snapshot.reacquisition_swipes)
        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual(
            [driver.TALENT_GRANT_SCAN_GESTURE_RATIO] * 3,
            device.forward_distances,
        )

    def test_grouped_talent_forward_reacquisition_rejects_proven_boundary(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-grant-complete"

        class OneBeyondForwardDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.forward_distances: list[float] = []
                self.captures: list[str] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [driver.shared.UiNode({})]

            def swipe_up(self, *, distance_ratio):
                self.forward_distances.append(distance_ratio)

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = OneBeyondForwardDevice()
        with self.assertRaisesRegex(
            RuntimeError,
            "stable physical boundary",
        ):
            driver.reacquire_exact_talent_state_group(
                device,
                (resource_id,),
                0,
                3,
                3,
                evidence_prefix="forward-one-beyond",
            )

        self.assertEqual(3, device.hierarchy_reads)
        self.assertEqual(
            [driver.TALENT_GRANT_SCAN_GESTURE_RATIO] * 2,
            device.forward_distances,
        )
        self.assertEqual(
            ["forward-one-beyond-stable-boundary-unresolved"],
            device.captures,
        )

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

        device.swipe_down.assert_not_called()
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
                "content-desc": "Arcana. Magical Active",
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
            "resourceDetails": {resource_id: ("Arcana. Magical Active",)},
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
        device.swipe_down.assert_called_once_with(
            distance_ratio=driver.TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO,
        )
        device.shell.assert_called_once_with("input", "tap", "541", "581")
        device.capture.assert_not_called()

    def test_exact_measured_talent_tap_binds_immutable_detail_before_tap(self) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-choice-0001"
        changed = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "content-desc": "Arcana changed. Magical Active",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,467][984,695]",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [changed]
        navigation = {
            "endViewport": 0,
            "resourceViewports": {resource_id: 0},
            "resourceDetails": {resource_id: ("Arcana. Magical Active",)},
        }

        with self.assertRaisesRegex(RuntimeError, "changed exact option detail"):
            driver.tap_exact_measured_talent_resource(
                device,
                resource_id,
                navigation,
                0,
                evidence_prefix="measured-tap-detail",
            )

        device.capture.assert_called_once_with("measured-tap-detail-detail-drift")
        device.shell.assert_not_called()

    def test_exact_measured_talent_tap_rejects_forged_duplicate_slot_before_tap(
        self,
    ) -> None:
        resource_id = "creation-prerequisite-talent-active-skill-option-choice-0001"
        forged = driver.shared.UiNode(
            {
                "resource-id": resource_id,
                "content-desc": (
                    "✓ Arcana. Selected slot 1 · Magical Active. "
                    "Selected slot 1 · Attribute MAG"
                ),
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,467][984,695]",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [forged]
        navigation = {
            "endViewport": 0,
            "resourceViewports": {resource_id: 0},
            "resourceDetails": {
                resource_id: ("Arcana. Magical Active. Attribute MAG",),
            },
        }

        with self.assertRaisesRegex(RuntimeError, "invalid exact slot decorator"):
            driver.tap_exact_measured_talent_resource(
                device,
                resource_id,
                navigation,
                0,
                evidence_prefix="measured-tap-duplicate-slot",
            )

        device.capture.assert_called_once_with(
            "measured-tap-duplicate-slot-slot-state-invalid"
        )
        device.shell.assert_not_called()

    def test_grouped_talent_reacquisition_keeps_retry_budgets_separate(self) -> None:
        resource_id = "creation-prerequisite-talent-grant-authority"
        target = driver.shared.UiNode({"resource-id": resource_id})
        overlay = driver.shared.UiNode({"content-desc": "system ui"})
        device = mock.Mock()
        device.hierarchy.side_effect = [[], [overlay], [target]]
        device.dismiss_system_ui_anr.side_effect = [True]
        device.node_has_tappable_bounds.return_value = True

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
        self.assertEqual("reverse", snapshot.reacquisition_direction)
        self.assertEqual(0, snapshot.reacquisition_swipes)
        self.assertEqual(3, device.hierarchy.call_count)
        device.swipe_down.assert_not_called()
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
            "resourceDetails": {option_id: ("Arcana. Magical Active",)},
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
            "resourceDetails": {option_id: ("Arcana. Magical Active",)},
        }

        def nodes(
            *,
            selected: int = 1,
            required: int = 1,
            kind: str = "Active skills",
            observed_digest: str = digest,
            duplicate: bool = False,
            enabled: bool = True,
            option_detail: str = "Arcana. Magical Active",
            digest_detail: str = "",
            slot_ordinal: int = 1,
            decorate_unselected_slot: bool = False,
            authority_detail: str | None = None,
        ) -> list[driver.shared.UiNode]:
            projected_option_detail = (
                ("✓ " if selected else "")
                + option_detail.replace(
                    ". ",
                    f". Selected slot {slot_ordinal} · ",
                    1,
                )
                if selected or decorate_unselected_slot
                else option_detail
            )
            option = driver.shared.UiNode(
                {
                    "resource-id": option_id,
                    "content-desc": projected_option_detail,
                    "enabled": str(enabled).lower(),
                    "clickable": "true",
                    "bounds": "[10,100][900,300]",
                }
            )
            result = [
                driver.shared.UiNode(
                    {
                        "resource-id": "creation-prerequisite-talent-grant-authority",
                        "content-desc": (
                            f"{selected} / {required} {kind}"
                            if authority_detail is None
                            else authority_detail
                        ),
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
            # Model the native Metric child explicitly. The immutable count
            # must still be bound to the exact authority AutomationId; a
            # matching anonymous descendant is not sufficient authority.
            result.append(
                driver.shared.UiNode(
                    {"text": f"{selected} / {required} {kind}"}
                )
            )
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
            (nodes(authority_detail=""), "immutable authority"),
            (nodes(observed_digest="sha256:" + ("b" * 64)), "immutable authority"),
            (nodes(digest_detail="not-the-exact-digest"), "immutable authority"),
            (nodes(option_detail="Arcana changed"), "changed exact option detail"),
            (nodes(slot_ordinal=2), "enabled exact selection"),
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

        forged_selected_details = (
            (
                "duplicate-selected-slot",
                "✓ Arcana. Selected slot 1 · Magical Active. "
                "Selected slot 1 · Attribute MAG",
            ),
            (
                "misplaced-selected-slot",
                "✓ Arcana. Magical Active. Selected slot 1 · Attribute MAG",
            ),
        )
        for name, projected_detail in forged_selected_details:
            with self.subTest(name=name):
                hierarchy = nodes()
                hierarchy[2].attributes["content-desc"] = projected_detail
                forged_navigation = {
                    **navigation,
                    "resourceDetails": {
                        option_id: ("Arcana. Magical Active. Attribute MAG",),
                    },
                }
                failing = mock.Mock()
                failing.hierarchy.return_value = hierarchy
                failing.node_has_tappable_bounds.return_value = True
                with self.assertRaisesRegex(RuntimeError, "enabled exact selection"):
                    driver.read_talent_grant_grouped_state(
                        failing,
                        "Active skills",
                        baseline,
                        forged_navigation,
                        0,
                        expected_selected_option_ids=(option_id,),
                        expected_completion_enabled=True,
                        evidence_prefix=f"grouped-{name}",
                    )

        unselected_with_slot = mock.Mock()
        unselected_with_slot.hierarchy.return_value = nodes(
            selected=0,
            decorate_unselected_slot=True,
        )
        unselected_with_slot.node_has_tappable_bounds.return_value = True
        with self.assertRaisesRegex(RuntimeError, "enabled exact unselection"):
            driver.read_talent_grant_grouped_state(
                unselected_with_slot,
                "Active skills",
                baseline,
                navigation,
                0,
                expected_selected_option_ids=(),
                expected_unselected_option_ids=(option_id,),
                expected_completion_enabled=False,
                evidence_prefix="grouped-unselected-slot",
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
        with self.assertRaisesRegex(RuntimeError, "preferred final resource"):
            driver.read_talent_grant_grouped_state(
                device,
                "Active skills",
                baseline,
                navigation,
                0,
                expected_selected_option_ids=(option_id,),
                expected_completion_enabled=True,
                preferred_final_resource_id="creation-prerequisite-unrelated",
                evidence_prefix="grouped-invalid-final-resource",
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

        catalog = inspect.getsource(driver.read_talent_grant_surface)
        reacquisition = inspect.getsource(driver.reacquire_exact_talent_state_group)
        self.assertEqual(0.60, driver.TALENT_GRANT_SCAN_GESTURE_RATIO)
        self.assertEqual(
            driver.DASHBOARD_SCAN_GESTURE_RATIO,
            driver.TALENT_GRANT_SCAN_GESTURE_RATIO,
        )
        self.assertEqual(
            2,
            catalog.count("distance_ratio=TALENT_GRANT_SCAN_GESTURE_RATIO"),
        )
        self.assertEqual(0.22, driver.TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO)
        self.assertEqual(40, driver.TALENT_GRANT_OPTION_RECOVERY_MAX_SCROLLS)
        self.assertIn("active_distance_ratio", reacquisition)
        self.assertIn("primary_distance_ratio", reacquisition)
        self.assertIn(
            "TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO",
            reacquisition,
        )
        self.assertIn("recovery_eligible", reacquisition)
        self.assertNotIn("move_between_measured_viewports(", reacquisition)
        self.assertNotIn("reacquisition_distance_ratio", reacquisition)
        self.assertNotIn("measured_distance_ratio", reacquisition)

    def test_talent_choice_prefers_completion_local_options_and_tap_order(self) -> None:
        prefix = driver.TALENT_GRANT_OPTION_PREFIX["Active skills"]
        option_ids = tuple(prefix + suffix for suffix in ("a", "b", "c"))
        selected, tap_order = driver.choose_navigation_local_talent_options(
            option_ids,
            2,
            {
                "endViewport": 5,
                "resourceViewports": {
                    option_ids[0]: 1,
                    option_ids[1]: 2,
                    option_ids[2]: 3,
                    "creation-prerequisite-talent-grant-complete": 5,
                },
            },
        )

        self.assertEqual(option_ids[1:], selected)
        self.assertEqual((option_ids[2], option_ids[1]), tap_order)

    def test_talent_choice_excludes_enabled_options_seen_only_clipped(self) -> None:
        prefix = driver.TALENT_GRANT_OPTION_PREFIX["Active skills"]
        clipped_id, measured_id = tuple(prefix + suffix for suffix in ("a", "b"))

        selected, tap_order = driver.choose_navigation_local_talent_options(
            (clipped_id, measured_id),
            1,
            {
                "endViewport": 4,
                "resourceViewports": {
                    measured_id: 3,
                    "creation-prerequisite-talent-grant-complete": 4,
                },
            },
        )

        self.assertEqual((measured_id,), selected)
        self.assertEqual((measured_id,), tap_order)

    def test_talent_choice_fails_closed_when_tappable_subset_is_insufficient(self) -> None:
        prefix = driver.TALENT_GRANT_OPTION_PREFIX["Active skills"]
        clipped_id, measured_id = tuple(prefix + suffix for suffix in ("a", "b"))

        with self.assertRaisesRegex(RuntimeError, "too few enabled options"):
            driver.choose_navigation_local_talent_options(
                (clipped_id, measured_id),
                2,
                {
                    "endViewport": 4,
                    "resourceViewports": {
                        measured_id: 3,
                        "creation-prerequisite-talent-grant-complete": 4,
                    },
                },
            )

    def test_talent_transition_waits_for_mutated_exact_option_and_same_route(self) -> None:
        option_id = (
            driver.TALENT_GRANT_OPTION_PREFIX["Active skills"] + "arcana"
        )
        route = self.canonical_node("creation-prerequisite-talent-grant-page")
        stale = self.canonical_node(
            option_id,
            text="",
            **{"content-desc": "Arcana. Magical Active"},
        )
        ready = self.canonical_node(
            option_id,
            text="",
            **{"content-desc": "✓ Arcana. Selected slot 1 · Magical Active"},
        )
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True

        with mock.patch.object(
            driver,
            "fresh_hierarchy_timed",
            side_effect=([route, stale], [route, ready]),
        ) as hierarchy, mock.patch.object(
            driver,
            "sleep_before_phase_deadline",
        ) as sleep:
            observed = driver.wait_for_exact_talent_option_transition(
                device,
                option_id,
                ("Arcana. Magical Active",),
                1,
                evidence_prefix="transition",
            )

        self.assertIs(ready, observed)
        self.assertEqual(2, hierarchy.call_count)
        sleep.assert_called_once()

    def test_talent_transition_rejects_forged_option_identity(self) -> None:
        option_id = (
            driver.TALENT_GRANT_OPTION_PREFIX["Active skills"] + "arcana"
        )
        route = self.canonical_node("creation-prerequisite-talent-grant-page")
        forged = self.canonical_node(
            option_id,
            package="org.example.forged",
            text="",
            **{"content-desc": "✓ Arcana. Selected slot 1 · Magical Active"},
        )
        device = mock.Mock()

        with mock.patch.object(
            driver,
            "fresh_hierarchy_timed",
            return_value=[route, forged],
        ):
            with self.assertRaisesRegex(RuntimeError, "canonical Chummer resource identity"):
                driver.wait_for_exact_talent_option_transition(
                    device,
                    option_id,
                    ("Arcana. Magical Active",),
                    1,
                    evidence_prefix="transition-forged",
                )

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
            "resourceDetails": {
                resource_id: (f"Option {index + 1}",)
                for index, resource_id in enumerate(option_ids)
            },
        }
        talent_option_id = "creation-prerequisite-talent-option-adept"
        talent_navigation = {
            "endViewport": 2,
            "resourceViewports": {talent_option_id: 2},
        }
        chosen = option_ids[1:]
        complete = driver.TalentGrantMutableState(2, chosen, True)
        incomplete = driver.TalentGrantMutableState(1, (chosen[1],), False)
        grouped_states = iter(
            ((complete, 5), (complete, 5), (incomplete, 5), (complete, 5))
        )
        events: list[str] = []
        tapped_resource_ids: list[str] = []
        preferred_final_resources: list[str | None] = []
        grouped_start_viewports: list[int] = []
        propagated_deadlines: list[float] = []
        deadline_values = iter(float(value) for value in range(1, 16))
        device = mock.Mock()
        device.wait_for_single_exact_resource_id.side_effect = (
            self.canonical_node("creation-prerequisite-talent-page"),
            self.canonical_node("creation-prerequisite-talent-grant-page"),
        )

        def inventory(*_args, navigation_out=None, **_kwargs):
            propagated_deadlines.append(_kwargs["deadline"])
            navigation_out.update(grant_navigation)
            return baseline

        def measured(_device, resource_id, navigation, _current, **_kwargs):
            propagated_deadlines.append(_kwargs["deadline"])
            tapped_resource_ids.append(resource_id)
            return int(navigation["resourceViewports"][resource_id])

        def grouped(*_args, **_kwargs):
            propagated_deadlines.append(_kwargs["deadline"])
            grouped_start_viewports.append(_args[4])
            preferred_final_resources.append(
                _kwargs.get("preferred_final_resource_id")
            )
            ordinal = sum(event.startswith("grouped-") for event in events) + 1
            events.append(f"grouped-{ordinal}")
            return next(grouped_states)

        def transition(*_args, **_kwargs):
            propagated_deadlines.append(_kwargs["deadline"])
            return mock.Mock()

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
            "wait_for_exact_talent_option_transition",
            side_effect=transition,
        ) as transition_wait, mock.patch.object(
            driver,
            "read_talent_grant_grouped_state",
            side_effect=grouped,
        ) as grouped_scan:
            proof = driver.choose_and_prove_talent_grant(
                device,
                "Active skills",
                talent_option_id,
                talent_navigation,
                scan_id_prefix="active",
                phase_deadline_provider=lambda: next(deadline_values),
                continuation_phase_advances=(
                    lambda: events.append("phase-preservation"),
                    lambda: events.append("phase-reset"),
                    lambda: events.append("phase-reselection"),
                ),
            )

        self.assertEqual(1, inventory_scan.call_count)
        self.assertEqual(4, grouped_scan.call_count)
        self.assertEqual(4, transition_wait.call_count)
        self.assertEqual(5, measured_tap.call_count)
        self.assertEqual(
            [
                float(value)
                for value in range(1, 16)
                if value != 7
            ],
            propagated_deadlines,
        )
        device.back.assert_called_once_with(deadline=7.0)
        back_route_call = next(
            call
            for call in device.wait_for_single_exact_resource_id.call_args_list
            if call.args == ("creation-prerequisite-talent-page",)
        )
        self.assertEqual(7.0, back_route_call.kwargs["deadline"])
        preserved_route_call = next(
            call
            for call in device.wait_for_single_exact_resource_id.call_args_list
            if call.args == ("creation-prerequisite-talent-grant-page",)
        )
        self.assertEqual(7.0, preserved_route_call.kwargs["deadline"])
        self.assertEqual(list(option_ids), proof.receipt["allOptionAutomationIds"])
        self.assertEqual(list(chosen), proof.receipt["selectedOptionAutomationIds"])
        self.assertEqual([chosen[1], chosen[0]], tapped_resource_ids[:2])
        self.assertEqual(5, proof.current_viewport)
        self.assertEqual(
            [
                "grouped-1",
                "phase-preservation",
                "grouped-2",
                "phase-reset",
                "grouped-3",
                "phase-reselection",
                "grouped-4",
            ],
            events,
        )
        self.assertEqual(
            [None, chosen[0], chosen[0], None],
            preferred_final_resources,
        )
        self.assertEqual([2, 0, 2, 2], grouped_start_viewports)

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

    def test_restored_talent_grant_reuses_the_retained_selected_node_once(self) -> None:
        talent_option_id = "creation-prerequisite-talent-option-exact"
        talent_option = self.canonical_node(
            talent_option_id,
            text="Exact Talent",
            **{"content-desc": "Exact Talent; current typed draft selection"},
        )
        grant_digest = "sha256:" + "a" * 64
        selected_option_ids = ("active-skill-pistols",)
        surface = driver.TalentGrantSurface(
            kind="active-skill",
            selected_count=1,
            required_count=1,
            grant_digest=grant_digest,
            option_ids=selected_option_ids,
            enabled_option_ids=selected_option_ids,
            selected_option_ids=selected_option_ids,
            completion_enabled=True,
        )

        class DirectReuseDevice:
            def __init__(self) -> None:
                self.taps: list[tuple[str, ...]] = []
                self.routes: list[str] = []
                self.backs: list[float] = []

            @staticmethod
            def node_has_tappable_bounds(_node, *, deadline=None) -> bool:
                if deadline is None:
                    raise AssertionError("Direct Talent reuse lost its active deadline")
                return True

            def shell(self, *arguments: str, timeout=None, deadline=None) -> str:
                if timeout is None or deadline is None:
                    raise AssertionError("Direct Talent reuse lost its action deadline")
                self.taps.append(arguments)
                return ""

            def wait_for_single_exact_resource_id(
                self,
                resource_id: str,
                *,
                timeout: int,
                evidence_prefix: str,
                surface_name: str,
                deadline: float,
            ):
                del timeout, evidence_prefix, surface_name
                if deadline is None:
                    raise AssertionError("Direct Talent reuse lost its route deadline")
                self.routes.append(resource_id)
                return CreationPrerequisiteSourceContractTests.canonical_node(resource_id)

            def back(self, *, deadline: float) -> None:
                self.backs.append(deadline)

            @staticmethod
            def capture(*_args, **_kwargs) -> None:
                raise AssertionError("The exact direct-reuse success path must not capture")

            @staticmethod
            def wait_exact_resource_id_bidirectional(*_args, **_kwargs):
                raise AssertionError("The retained Talent option must not be reacquired")

            @staticmethod
            def tap_exact_resource_id_bidirectional(*_args, **_kwargs):
                raise AssertionError("The retained Talent option must not be tapped indirectly")

        device = DirectReuseDevice()
        deadline = driver.time.monotonic() + 60
        with mock.patch.object(
            driver,
            "read_talent_grant_surface",
            return_value=surface,
        ) as read_surface:
            actual = driver.require_restored_talent_grant(
                device,
                talent_option_id,
                talent_option,
                "active-skill",
                grant_digest,
                selected_option_ids,
                scan_id="direct-selected-talent-reuse",
                deadline=deadline,
            )

        self.assertEqual(surface, actual)
        self.assertEqual([("input", "tap", "500", "400")], device.taps)
        self.assertEqual(
            [
                "creation-prerequisite-talent-grant-page",
                "creation-prerequisite-talent-page",
                "creation-prerequisite-page",
            ],
            device.routes,
        )
        self.assertEqual([deadline, deadline], device.backs)
        read_surface.assert_called_once_with(
            device,
            "active-skill",
            scan_observer=None,
            scan_id="direct-selected-talent-reuse",
            deadline=deadline,
            route_node=mock.ANY,
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
        deadline = 999.0
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True
        device.wait_exact_resource_id_bidirectional.return_value = driver.shared.UiNode(
            {
                "package": driver.shared.PACKAGE,
                "resource-id": (
                    f"{driver.shared.PACKAGE}:id/creation-stage-method"
                ),
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[40,300][1040,520]",
            }
        )
        origin = driver.PriorityRankOrigin([], 0, 0, (), 0)
        scan_observer = mock.Mock()
        with mock.patch.object(
            driver,
            "wait_for_prerequisite_scan_origin",
            return_value=origin,
        ) as acquire, mock.patch.object(
            driver.shared,
            "_remaining_operation_timeout",
            return_value=15,
        ), mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            actual = driver.open_prerequisite(
                device,
                deadline=deadline,
                scan_observer=scan_observer,
            )

        self.assertIs(origin, actual)
        device.wait_exact_resource_id_bidirectional.assert_called_once_with(
            "creation-stage-method",
            timeout=180,
            backward_scrolls=driver.DASHBOARD_SCAN_MAX_SCROLLS,
            forward_scrolls=driver.DASHBOARD_SCAN_MAX_SCROLLS,
            scroll_distance_ratio=driver.DASHBOARD_SCAN_GESTURE_RATIO,
            evidence_prefix="creation-stage-method-open",
            surface_name="Creation method navigation",
            require_tappable=True,
            deadline=deadline,
        )
        reset.assert_not_called()
        device.shell.assert_called_once_with(
            "input", "tap", "540", "410", timeout=15, deadline=deadline
        )
        acquire.assert_called_once_with(
            device,
            deadline=deadline,
            immediately_after_opening_tap=True,
            scan_observer=scan_observer,
        )
        device.wait.assert_not_called()
        device.tap_until_visible.assert_not_called()
        device.tap_bidirectional.assert_not_called()

    def test_prerequisite_navigation_waits_for_exact_attachment_after_one_tap(self) -> None:
        node = self.canonical_node(
            "creation-stage-method",
            **{
                "content-desc": "Priority",
                "bounds": "[40,300][1040,520]",
            },
        )
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True
        origin = self.priority_rank_origin([])
        expectation = mock.Mock(spec=driver.proof_state.ProofBuildExpectation)
        prior = {"sequence": 7}
        attachment = {"sequence": 8}
        retained: list[dict[str, object]] = []
        order: list[str] = []
        deadline = driver.time.monotonic() + 30

        device.shell.side_effect = lambda *_args, **_kwargs: order.append("tap")
        with mock.patch.object(
            driver,
            "read_creation_prerequisite_attachment_proof_state",
            side_effect=lambda *_args, **_kwargs: (
                order.append("attachment") or attachment
            ),
        ) as read_attachment, mock.patch.object(
            driver,
            "wait_for_prerequisite_scan_origin",
            side_effect=lambda *_args, **_kwargs: (
                order.append("accessibility") or origin
            ),
        ):
            actual = driver.open_prerequisite(
                device,
                ready_method_node=node,
                deadline=deadline,
                proof_expectation=expectation,
                expected_prior_proof=prior,
                attachment_proof_out=retained,
            )

        self.assertIs(origin, actual)
        self.assertEqual(["tap", "attachment", "accessibility"], order)
        self.assertEqual([attachment], retained)
        self.assertEqual(1, device.shell.call_count)
        read_attachment.assert_called_once_with(
            device,
            expectation,
            expected_prior_proof=prior,
            deadline=deadline,
        )

    def test_prerequisite_navigation_rejects_partial_proof_contract_before_tap(self) -> None:
        device = mock.Mock()
        with self.assertRaisesRegex(ValueError, "supplied together"):
            driver.open_prerequisite(
                device,
                proof_expectation=mock.Mock(
                    spec=driver.proof_state.ProofBuildExpectation
                ),
            )
        device.shell.assert_not_called()

    def test_prerequisite_attachment_proof_binds_later_same_process_workspace(self) -> None:
        workspace = {
            "workspaceId": "workspace-1",
            "contentRevision": 7,
            "savedRevision": 7,
            "payloadSha256": "a" * 64,
            "documentSha256": "b" * 64,
            "snapshotDigest": "sha256:" + "c" * 64,
        }
        prior = {
            "sequence": 10,
            "processId": 4242,
            "processInstanceId": "44444444-4444-4444-4444-444444444444",
            "e2eAuthorityGeneration": 2,
            "workspace": {**workspace, "snapshotDigest": "sha256:" + "d" * 64},
        }
        payload = {
            "schema": driver.proof_state.SCHEMA,
            "sequence": 11,
            "processId": prior["processId"],
            "processInstanceId": prior["processInstanceId"],
            "e2eAuthorityGeneration": prior["e2eAuthorityGeneration"],
            "surface": {
                "pageAutomationId": "creation-prerequisite-page",
                "navigationDepth": 2,
                "wizardLane": "creation-prerequisite",
                "stage": "attachment-authority-ready",
                "settled": True,
            },
            "workspace": workspace,
            "transaction": None,
            "creationResources": None,
            "stateDigest": "sha256:" + "e" * 64,
        }
        snapshot = driver.proof_state.ProofStateSnapshot(
            payload,
            "f" * 64,
            {"attempt": 1},
        )
        device = mock.Mock()
        expectation = mock.Mock(spec=driver.proof_state.ProofBuildExpectation)
        with mock.patch.object(
            driver.proof_state,
            "wait_for_state",
            return_value=snapshot,
        ), mock.patch.object(
            driver.shared,
            "_remaining_operation_timeout",
            return_value=17,
        ):
            result = driver.read_creation_prerequisite_attachment_proof_state(
                device,
                expectation,
                expected_prior_proof=prior,
                deadline=999.0,
            )

        self.assertEqual("route-attachment-and-core-snapshot-only", result["claimScope"])
        self.assertEqual(0, result["mutationCommandsRetried"])
        self.assertEqual(workspace, result["workspace"])
        device.capture.assert_not_called()

    def test_prerequisite_attachment_proof_rejects_identity_revision_and_scope_drift(
        self,
    ) -> None:
        workspace = {
            "workspaceId": "workspace-1",
            "contentRevision": 7,
            "savedRevision": 7,
            "payloadSha256": "a" * 64,
            "documentSha256": "b" * 64,
            "snapshotDigest": "sha256:" + "c" * 64,
        }
        prior = {
            "sequence": 10,
            "processId": 4242,
            "processInstanceId": "44444444-4444-4444-4444-444444444444",
            "e2eAuthorityGeneration": 2,
            "workspace": {**workspace, "snapshotDigest": "sha256:" + "d" * 64},
        }
        base_payload = {
            "schema": driver.proof_state.SCHEMA,
            "sequence": 11,
            "processId": 4242,
            "processInstanceId": prior["processInstanceId"],
            "e2eAuthorityGeneration": 2,
            "surface": {"navigationDepth": 2},
            "workspace": workspace,
            "transaction": None,
            "creationResources": None,
            "stateDigest": "sha256:" + "e" * 64,
        }
        cases = (
            ("sequence", {"sequence": 10}),
            ("process", {"processId": 4243}),
            ("instance", {"processInstanceId": "55555555-5555-5555-5555-555555555555"}),
            ("generation", {"e2eAuthorityGeneration": 3}),
            ("workspace", {"workspace": {**workspace, "workspaceId": "workspace-2"}}),
            ("revision", {"workspace": {**workspace, "contentRevision": 8}}),
            ("payload", {"workspace": {**workspace, "payloadSha256": "f" * 64}}),
            ("not-pushed", {"surface": {"navigationDepth": 1}}),
            ("transaction", {"transaction": {"unexpected": True}}),
            ("resources", {"creationResources": {"unexpected": True}}),
        )
        expectation = mock.Mock(spec=driver.proof_state.ProofBuildExpectation)
        for label, replacement in cases:
            payload = {**base_payload, **replacement}
            snapshot = driver.proof_state.ProofStateSnapshot(payload, "f" * 64, None)
            device = mock.Mock()
            with self.subTest(label=label), mock.patch.object(
                driver.proof_state,
                "wait_for_state",
                return_value=snapshot,
            ), mock.patch.object(
                driver.shared,
                "_remaining_operation_timeout",
                return_value=17,
            ), self.assertRaisesRegex(RuntimeError, "later same-process"):
                driver.read_creation_prerequisite_attachment_proof_state(
                    device,
                    expectation,
                    expected_prior_proof=prior,
                    deadline=999.0,
                )
            device.capture.assert_called_once_with(
                "creation-prerequisite-attachment-proof-state-mismatch",
                deadline=999.0,
            )

    def test_prerequisite_resume_taps_only_the_exact_reacquired_method_node(self) -> None:
        node = driver.shared.UiNode(
            {
                "package": driver.shared.PACKAGE,
                "resource-id": (
                    f"{driver.shared.PACKAGE}:id/creation-stage-method"
                ),
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[40,300][1040,520]",
            }
        )
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True
        origin = driver.PriorityRankOrigin([], 0, 0, (), 0)

        with mock.patch.object(
            driver,
            "wait_for_prerequisite_scan_origin",
            return_value=origin,
        ) as acquire, mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            actual = driver.open_prerequisite(device, ready_method_node=node)

        self.assertIs(origin, actual)
        reset.assert_not_called()
        device.tap_bidirectional.assert_not_called()
        device.shell.assert_called_once_with("input", "tap", "540", "410")
        acquire.assert_called_once_with(
            device,
            deadline=None,
            immediately_after_opening_tap=True,
            scan_observer=None,
        )
        device.wait.assert_not_called()

    def test_initial_method_opening_reacquires_one_fresh_exact_target_after_diagnostics(
        self,
    ) -> None:
        node = self.canonical_node(
            "creation-stage-method",
            text="",
            **{
                "content-desc": "Build method · Priority",
                "bounds": "[98,275][984,355]",
            },
        )
        unrelated = self.canonical_node("creation-wizard-binding")
        device = mock.Mock(spec=driver.shared.Device)
        device_events: list[str] = []
        device.display_size.side_effect = lambda **_kwargs: (
            device_events.append("display-size") or (1080, 1920)
        )
        hierarchy_durations: list[int] = []

        def fresh(_device, durations, **_kwargs):
            device_events.append("fresh-hierarchy")
            hierarchy_durations.append(len(durations))
            durations.append(7)
            return [unrelated, node]

        with mock.patch.object(driver, "fresh_hierarchy_timed", side_effect=fresh) as read:
            selected, proof = driver.reacquire_creation_method_one_shot_target(
                device,
                expected_detail="Build method · Priority",
                diagnostic_capture="creation-priority-core-bootstrap-ready",
                deadline=driver.time.monotonic() + 30,
            )

        self.assertIs(node, selected)
        read.assert_called_once()
        self.assertEqual(["display-size", "fresh-hierarchy"], device_events)
        self.assertEqual([0], hierarchy_durations)
        self.assertEqual("target-acquired", proof["status"])
        self.assertEqual("[98,275][984,355]", proof["preTap"]["bounds"])
        self.assertEqual({"x": 541, "y": 315}, proof["preTap"]["center"])
        self.assertEqual(1, proof["preTap"]["hierarchyReadCount"])
        self.assertRegex(proof["preTap"]["hierarchyDigest"], r"^sha256:[0-9a-f]{64}$")
        device.shell.assert_not_called()
        device.swipe_up.assert_not_called()
        device.swipe_down.assert_not_called()
        device.capture.assert_not_called()

    def test_initial_method_opening_fails_closed_before_tap_on_stale_or_ambiguous_target(
        self,
    ) -> None:
        exact = self.canonical_node(
            "creation-stage-method",
            text="",
            **{"content-desc": "Build method · Priority"},
        )
        cases = (
            ("missing", [], "cardinality 0"),
            ("duplicate", [exact, exact], "cardinality 2"),
            (
                "disabled",
                [self.canonical_node(
                    "creation-stage-method",
                    text="",
                    enabled="false",
                    **{"content-desc": "Build method · Priority"},
                )],
                "not visible, enabled, and clickable",
            ),
            (
                "forged-package",
                [self.canonical_node(
                    "creation-stage-method",
                    package="com.example.forged",
                    text="",
                    **{"content-desc": "Build method · Priority"},
                )],
                "canonical Chummer",
            ),
            (
                "detail-drift",
                [self.canonical_node(
                    "creation-stage-method",
                    text="",
                    **{"content-desc": "Build method · Sum to Ten"},
                )],
                "changed after diagnostics",
            ),
        )
        for label, nodes, error in cases:
            device = mock.Mock(spec=driver.shared.Device)
            device.display_size.return_value = (1080, 1920)
            with self.subTest(label=label), mock.patch.object(
                driver,
                "fresh_hierarchy_timed",
                return_value=nodes,
            ), self.assertRaisesRegex(RuntimeError, error):
                driver.reacquire_creation_method_one_shot_target(
                    device,
                    expected_detail="Build method · Priority",
                    diagnostic_capture="creation-priority-core-bootstrap-ready",
                    deadline=driver.time.monotonic() + 30,
                )
            device.shell.assert_not_called()
            device.swipe_up.assert_not_called()
            device.swipe_down.assert_not_called()

    def test_initial_method_opening_binds_the_first_post_tap_route_without_replay(
        self,
    ) -> None:
        method = self.canonical_node(
            "creation-stage-method",
            text="",
            **{"content-desc": "Build method · Priority"},
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.display_size.return_value = (1080, 1920)
        with mock.patch.object(
            driver,
            "fresh_hierarchy_timed",
            side_effect=lambda _device, durations, **_kwargs: (
                durations.append(3) or [method]
            ),
        ):
            _, action = driver.reacquire_creation_method_one_shot_target(
                device,
                expected_detail="Build method · Priority",
                diagnostic_capture="creation-priority-core-bootstrap-ready",
                deadline=driver.time.monotonic() + 30,
            )
        action["status"] = "tap-issued"
        action["tap"] = {
            "command": "input tap",
            "count": 1,
            "coordinates": dict(action["preTap"]["center"]),
            "issuedAtUtc": "2026-09-03T17:48:30+00:00",
        }
        old_dashboard = [self.canonical_node("creation-wizard-dashboard")]
        route = self.canonical_node("creation-prerequisite-page")
        top_method = self.canonical_node("creation-prerequisite-method")
        binding = self.canonical_node("creation-prerequisite-binding")
        device.hierarchy.side_effect = [old_dashboard, [route, top_method, binding]]
        device.dismiss_system_ui_anr.return_value = False
        observed: list[dict[str, object]] = []

        with mock.patch.object(driver.time, "sleep"):
            origin = driver.wait_for_prerequisite_scan_origin(
                device,
                immediately_after_opening_tap=True,
                scan_observer=observed.append,
                opening_action=action,
            )

        self.assertEqual([route, top_method, binding], origin.nodes)
        self.assertEqual("first-post-tap-observed", action["status"])
        first = action["firstPostTap"]
        self.assertEqual(
            driver.accessibility_signature_sha256(old_dashboard),
            first["hierarchyDigest"],
        )
        self.assertEqual(0, first["routeCardinality"])
        self.assertIs(False, first["routeResolved"])
        self.assertEqual(action, observed[0]["openingAction"])
        driver.require_creation_method_one_shot_proof(
            action,
            require_first_post_tap=True,
        )
        device.shell.assert_not_called()

    def test_initial_method_opening_proof_rejects_replay_fallback_or_second_tap(self) -> None:
        base = {
            "schema": driver.CREATION_METHOD_ONE_SHOT_SCHEMA,
            "status": "tap-issued",
            "selector": "creation-stage-method",
            "fullResourceId": f"{driver.shared.PACKAGE}:id/creation-stage-method",
            "diagnosticCapture": "creation-priority-core-bootstrap-ready",
            "preTap": {
                "observedAtUtc": "2026-09-03T17:48:29+00:00",
                "hierarchyDigest": "sha256:" + "1" * 64,
                "hierarchyDigestDomain": driver.CREATION_METHOD_ONE_SHOT_DIGEST_DOMAIN,
                "nodeCount": 39,
                "hierarchyReadCount": 1,
                "hierarchyElapsedMs": 3,
                "bounds": "[98,275][984,355]",
                "center": {"x": 541, "y": 315},
                "enabled": True,
                "clickable": True,
                "detail": "Build method · Priority",
            },
            "tap": {
                "command": "input tap",
                "count": 1,
                "coordinates": {"x": 541, "y": 315},
                "issuedAtUtc": "2026-09-03T17:48:30+00:00",
            },
            "tapReplayPerformed": False,
            "fallbackTapPerformed": False,
        }
        for field, value in (
            ("tapReplayPerformed", True),
            ("fallbackTapPerformed", True),
            ("tap.count", 2),
        ):
            proof = json.loads(json.dumps(base))
            if field == "tap.count":
                proof["tap"]["count"] = value
            else:
                proof[field] = value
            with self.subTest(field=field), self.assertRaisesRegex(
                RuntimeError,
                "authority differs|target, digest, or tap differs",
            ):
                driver.require_creation_method_one_shot_proof(
                    proof,
                    require_first_post_tap=False,
                )

    def test_prerequisite_resume_rejects_a_stale_or_non_tappable_method_node(self) -> None:
        for attributes in (
            {
                "resource-id": "creation-finalization-step-method",
                "content-desc": "Method · complete",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[40,300][1040,520]",
            },
            {
                "package": driver.shared.PACKAGE,
                "resource-id": (
                    f"{driver.shared.PACKAGE}:id/creation-stage-method"
                ),
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[40,300][1040,520]",
            },
        ):
            with self.subTest(resource_id=attributes["resource-id"]):
                node = driver.shared.UiNode(attributes)
                device = mock.Mock()
                device.node_has_tappable_bounds.return_value = (
                    attributes["resource-id"] == "creation-finalization-step-method"
                )

                with self.assertRaisesRegex(
                    RuntimeError,
                    "exact tappable creation-stage-method",
                ):
                    driver.open_prerequisite(device, ready_method_node=node)

                device.capture.assert_called_once_with(
                    "creation-stage-method-resume-node-invalid"
                )
                device.shell.assert_not_called()
                device.tap_bidirectional.assert_not_called()

    def test_prerequisite_resume_rejects_noncanonical_method_identity_before_tap(
        self,
    ) -> None:
        for name, package, resource_id in (
            (
                "wrong-package",
                "com.example.forged",
                f"{driver.shared.PACKAGE}:id/creation-stage-method",
            ),
            (
                "wrong-full-id",
                driver.shared.PACKAGE,
                "com.example.forged:id/creation-stage-method",
            ),
        ):
            with self.subTest(name=name):
                node = driver.shared.UiNode(
                    {
                        "package": package,
                        "resource-id": resource_id,
                        "content-desc": "Priority",
                        "enabled": "true",
                        "clickable": "true",
                        "bounds": "[40,300][1040,520]",
                    }
                )
                device = mock.Mock()
                device.node_has_tappable_bounds.return_value = True

                with self.assertRaisesRegex(RuntimeError, "canonical Chummer"):
                    driver.open_prerequisite(device, ready_method_node=node)

                device.capture.assert_called_once_with(
                    "creation-stage-method-resume-identity-invalid"
                )
                device.shell.assert_not_called()
                device.wait.assert_not_called()

    def test_same_process_resume_reacquires_method_upward_from_the_proven_bottom(self) -> None:
        source = inspect.getsource(driver.execute)
        start = source.index('progress.advance("same-process-reopen")')
        end = source.index('progress.advance("resources-initial-authority")', start)
        resume_source = source[start:end]
        navigation_source = resume_source[: resume_source.index("resumed_authority =")]

        self.assertIn(
            'progress.active_phase_deadline("same-process-reopen")',
            resume_source,
        )
        self.assertIn("reacquire_exact_ready_creation_method(", resume_source)
        self.assertIn(
            "expected_detail=post_confirm_dashboard.method_detail",
            resume_source,
        )
        self.assertIn("max_swipes=DASHBOARD_SCAN_MAX_SCROLLS", resume_source)
        self.assertIn("deadline=same_process_deadline", resume_source)
        self.assertIn(
            "ready_method_node=resumed_method_node",
            resume_source,
        )
        self.assertIn("deadline=same_process_deadline", resume_source)
        self.assertNotIn("shared.open_creation_dashboard(", navigation_source)

    def test_process_restart_reacquires_method_from_its_fresh_dashboard_proof(self) -> None:
        source = inspect.getsource(driver.execute)
        start = source.index('progress.advance("process-restart-reopen")')
        restart_source = source[start:]

        self.assertIn(
            'progress.active_phase_deadline(\n        "process-restart-reopen"',
            restart_source,
        )
        self.assertIn(
            "process_restart_dashboard = assert_uncreated_advanced_editor_gated(",
            restart_source,
        )
        self.assertIn("deadline=process_restart_deadline", restart_source)
        self.assertIn(
            "expected_detail=process_restart_dashboard.method_detail",
            restart_source,
        )
        self.assertIn("max_swipes=DASHBOARD_SCAN_MAX_SCROLLS", restart_source)
        self.assertIn('phase_id="process-restart-reopen"', restart_source)
        self.assertIn(
            "ready_method_node=restarted_method_node",
            restart_source,
        )
        self.assertIn("deadline=process_restart_deadline", restart_source)

    def test_prerequisite_navigation_proves_route_before_reading_content(self) -> None:
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True
        method = driver.shared.UiNode(
            {
                "package": driver.shared.PACKAGE,
                "resource-id": (
                    f"{driver.shared.PACKAGE}:id/creation-stage-method"
                ),
                "content-desc": "Priority",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[40,300][1040,520]",
            }
        )
        device.wait_exact_resource_id_bidirectional.return_value = method
        origin = driver.PriorityRankOrigin([], 0, 0, (), 0)
        with mock.patch.object(
            driver,
            "wait_for_prerequisite_scan_origin",
            return_value=origin,
        ) as acquire, mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            driver.open_prerequisite(device)

        self.assertEqual(1, device.wait_exact_resource_id_bidirectional.call_count)
        device.shell.assert_called_once_with("input", "tap", "540", "410")
        acquire.assert_called_once_with(
            device,
            deadline=None,
            immediately_after_opening_tap=True,
            scan_observer=None,
        )
        reset.assert_not_called()
        device.wait.assert_not_called()

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

    def test_talent_grant_completion_threads_one_deadline_through_all_operations(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        completion = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-talent-grant-complete",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )
        device = mock.Mock()
        device.hierarchy.return_value = [completion]
        device.node_has_tappable_bounds.return_value = True
        navigation = {
            "endViewport": 7,
            "resourceViewports": {
                "creation-prerequisite-talent-grant-complete": 7,
            },
        }

        driver.complete_talent_grant_to_prerequisite(
            device,
            navigation,
            7,
            deadline=deadline,
        )

        device.hierarchy.assert_called_once_with(deadline=deadline)
        device.shell.assert_called_once_with(
            "input",
            "tap",
            "455",
            "200",
            deadline=deadline,
        )
        self.assertEqual(
            [
                mock.call(
                    "creation-prerequisite-talent-page",
                    timeout=45,
                    evidence_prefix="creation-prerequisite-talent-after-grant",
                    surface_name="Talent detail route after exact grant completion",
                    deadline=deadline,
                ),
                mock.call(
                    "creation-prerequisite-page",
                    timeout=45,
                    evidence_prefix="creation-prerequisite-after-talent-grant",
                    surface_name="Creation prerequisite route after Talent grant completion",
                    deadline=deadline,
                ),
            ],
            device.wait_for_single_exact_resource_id.call_args_list,
        )
        device.back.assert_called_once_with(deadline=deadline)

    def test_deadline_bound_exact_route_wait_rejects_duplicate_cardinality(
        self,
    ) -> None:
        selector = "creation-prerequisite-talent-page"
        deadline = driver.shared.time.monotonic() + 30
        duplicate = driver.shared.UiNode({"resource-id": selector})
        device = mock.Mock()
        device.hierarchy.return_value = [duplicate, duplicate]

        with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.shared.Device.wait_for_single_exact_resource_id(
                device,
                selector,
                evidence_prefix="deadline-route",
                surface_name="Deadline-bound Talent route",
                deadline=deadline,
            )

        device.hierarchy.assert_called_once_with(deadline=deadline)
        device.capture.assert_called_once_with(
            "deadline-route-cardinality-invalid",
            deadline=deadline,
        )
        device.swipe_up.assert_not_called()

    def test_expired_exact_route_deadline_performs_no_hierarchy_or_mutation(
        self,
    ) -> None:
        deadline = driver.shared.time.monotonic() - 1
        device = mock.Mock()

        with self.assertRaisesRegex(RuntimeError, "Timed out waiting"):
            driver.shared.Device.wait_for_single_exact_resource_id(
                device,
                "creation-prerequisite-talent-page",
                evidence_prefix="expired-route",
                deadline=deadline,
            )

        device.hierarchy.assert_not_called()
        device.swipe_up.assert_not_called()
        device.capture.assert_called_once_with(
            "expired-route-unavailable",
            deadline=deadline,
        )

    def test_deadline_bound_back_issues_one_action_and_one_bounded_sleep(self) -> None:
        deadline = driver.shared.time.monotonic() + 30
        navigate_up = driver.shared.UiNode({"bounds": "[10,20][110,120]"})
        device = mock.Mock()
        device.find.return_value = navigate_up

        with mock.patch.object(
            driver.shared,
            "_sleep_before_operation_deadline",
        ) as bounded_sleep:
            driver.shared.Device.back(device, deadline=deadline)

        device.find.assert_called_once_with("Navigate up", deadline=deadline)
        device.shell.assert_called_once()
        action = device.shell.call_args
        self.assertEqual(("input", "tap", "60", "70"), action.args)
        self.assertEqual(deadline, action.kwargs["deadline"])
        self.assertGreater(action.kwargs["timeout"], 0)
        self.assertLessEqual(action.kwargs["timeout"], 120)
        bounded_sleep.assert_called_once_with(1, deadline=deadline)

    def test_expired_back_deadline_issues_no_hierarchy_or_action(self) -> None:
        device = mock.Mock()
        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.shared.Device.back(
                device,
                deadline=driver.shared.time.monotonic() - 1,
            )

        device.find.assert_not_called()
        device.shell.assert_not_called()

    def test_unknown_deadline_bound_back_action_is_issued_only_once(self) -> None:
        deadline = driver.shared.time.monotonic() + 30
        receipt = {
            "classification": "timeout-unknown-outcome",
            "commandPolicy": "non-replayable",
            "replay": {"performed": False, "suppressed": True},
        }
        error = driver.shared.AdbTransportError(
            receipt,
            Path("back-action-timeout-unknown-outcome.json"),
        )
        device = mock.Mock()
        device.find.return_value = None
        device.shell.side_effect = error

        with mock.patch.object(
            driver.shared,
            "_sleep_before_operation_deadline",
        ) as bounded_sleep, self.assertRaises(driver.shared.AdbTransportError):
            driver.shared.Device.back(device, deadline=deadline)

        device.find.assert_called_once_with("Navigate up", deadline=deadline)
        device.shell.assert_called_once()
        action = device.shell.call_args
        self.assertEqual(("input", "keyevent", "4"), action.args)
        self.assertEqual(deadline, action.kwargs["deadline"])
        bounded_sleep.assert_not_called()

    def test_unknown_back_outcome_is_never_replayed_or_followed_by_route_wait(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        receipt = {
            "classification": "timeout-unknown-outcome",
            "commandPolicy": "non-replayable",
            "replay": {"performed": False, "suppressed": True},
        }
        error = driver.shared.AdbTransportError(
            receipt,
            Path("back-timeout-unknown-outcome.json"),
        )
        device = mock.Mock()
        device.wait_for_single_exact_resource_id.return_value = mock.Mock()
        device.back.side_effect = error
        navigation = {
            "endViewport": 7,
            "resourceViewports": {
                "creation-prerequisite-talent-grant-complete": 7,
            },
        }

        with mock.patch.object(
            driver,
            "tap_exact_measured_talent_resource",
        ) as completion_tap, self.assertRaises(driver.shared.AdbTransportError):
            driver.complete_talent_grant_to_prerequisite(
                device,
                navigation,
                7,
                deadline=deadline,
            )

        completion_tap.assert_called_once()
        device.back.assert_called_once_with(deadline=deadline)
        self.assertEqual(
            1,
            device.wait_for_single_exact_resource_id.call_count,
        )

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

    def test_preview_and_resume_use_bounded_single_pass_authority_scans(self) -> None:
        main_source = inspect.getsource(driver.execute)
        root_source = inspect.getsource(driver.scan_persisted_prerequisite_authority)
        option_source = inspect.getsource(driver.require_exact_restored_authority_option)

        self.assertIn("open_exact_prerequisite_preview(", main_source)
        self.assertIn("require_exact_preview_talent_grant_plan(", main_source)
        self.assertIn("read_exact_confirmed_receipt(", main_source)
        preview_slice = main_source[
            main_source.index('progress.advance("preview-confirm")') :
            main_source.index('progress.advance("same-process-reopen")')
        ]
        self.assertNotIn("shared.reset_scroll_to_top", preview_slice)
        self.assertNotIn("node_text(", preview_slice)

        self.assertIn("scan_forward_with_receipt(", root_source)
        self.assertIn("initial_observation=initial_observation", root_source)
        self.assertIn("deadline=deadline", root_source)
        self.assertNotIn("shared.reset_scroll_to_top", root_source)
        self.assertNotIn("node_text(", root_source)
        self.assertIn("acquire_stable_start_origin(", option_source)
        self.assertIn("scan_forward_with_receipt(", option_source)
        self.assertNotIn("shared.reset_scroll_to_top", option_source)
        self.assertNotIn("node_text(", option_source)

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
        self.assertIn(("swipe_down", {"distance_ratio": 0.22}), calls)
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

    def test_hosted_clipped_first_category_uses_scan_equivalent_small_gestures(self) -> None:
        clipped = driver.shared.UiNode(
            {
                "content-desc": (
                    "Heritage choice. Select an exact Core-projected metatype "
                    "or metavariant"
                ),
                "enabled": "true",
                "clickable": "false",
                "bounds": "[53,275][1028,278]",
            }
        )
        talent = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-talent",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,350][984,503]",
            }
        )
        visible = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,300][1028,500]",
            }
        )

        class HostedClippedDevice:
            def __init__(self) -> None:
                self.swipe_ratios: list[float] = []
                self.small_swipes = 0
                self.hierarchy_reads = 0
                self.captures: list[str] = []

            def swipe_down(self, *, distance_ratio):
                self.swipe_ratios.append(distance_ratio)
                if distance_ratio != 0.22:
                    raise AssertionError(
                        "first category used a large reverse gesture that can skip the row"
                    )
                self.small_swipes += 1

            @staticmethod
            def swipe_up(**_options):
                raise AssertionError("first category used an unmeasured forward search")

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [visible, talent] if self.small_swipes == 3 else [clipped, talent]

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is visible

            def capture(self, name):
                self.captures.append(name)

        device = HostedClippedDevice()
        navigation = {
            "viewportByCategory": {
                "heritage": 4,
                "talent": 5,
                "attributes": 6,
                "skills": 7,
                "resources": 7,
            },
            "currentViewport": 7,
            "lastCategory": None,
        }
        with mock.patch.object(driver.time, "sleep"):
            row = driver.acquire_measured_priority_category_row(
                device,
                "heritage",
                navigation,
            )

        self.assertIs(visible, row)
        self.assertEqual([0.22] * 3, device.swipe_ratios)
        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual(4, navigation["currentViewport"])
        self.assertEqual([], device.captures)

    def test_first_category_reacquisition_accepts_one_overlap_beyond_scan_delta(self) -> None:
        self.assertEqual(1, driver.PRIORITY_CATEGORY_REACQUISITION_OVERLAP_SWIPES)
        visible = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,300][1028,500]",
            }
        )

        class LastBoundDevice:
            def __init__(self) -> None:
                self.swipe_ratios: list[float] = []
                self.small_swipes = 0
                self.hierarchy_reads = 0

            def swipe_down(self, *, distance_ratio):
                self.swipe_ratios.append(distance_ratio)
                if distance_ratio == 0.22:
                    self.small_swipes += 1

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [visible] if self.small_swipes == 4 else [driver.shared.UiNode({})]

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is visible

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = LastBoundDevice()
        navigation = {
            "viewportByCategory": {
                "heritage": 0,
                "talent": 1,
                "attributes": 2,
                "skills": 3,
                "resources": 3,
            },
            "currentViewport": 3,
            "lastCategory": None,
        }
        with mock.patch.object(driver.time, "sleep"):
            row = driver.acquire_measured_priority_category_row(
                device,
                "heritage",
                navigation,
            )

        self.assertIs(visible, row)
        self.assertEqual([0.22] * 4, device.swipe_ratios)
        self.assertEqual(5, device.hierarchy_reads)

    def test_first_category_reacquisition_rejects_two_beyond_scan_delta(self) -> None:
        class TwoBeyondDevice:
            def __init__(self) -> None:
                self.swipe_ratios: list[float] = []
                self.small_swipes = 0
                self.hierarchy_reads = 0
                self.captures: list[str] = []

            def swipe_down(self, *, distance_ratio):
                self.swipe_ratios.append(distance_ratio)
                if distance_ratio == 0.22:
                    self.small_swipes += 1

            def hierarchy(self):
                self.hierarchy_reads += 1
                if self.small_swipes == 5:
                    return [driver.shared.UiNode({
                        "resource-id": "creation-prerequisite-category-heritage",
                        "enabled": "true",
                        "clickable": "true",
                        "bounds": "[53,300][1028,500]",
                    })]
                return [driver.shared.UiNode({})]

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = TwoBeyondDevice()
        navigation = {
            "viewportByCategory": {category: 0 for category in driver.CATEGORIES},
            "currentViewport": 3,
            "lastCategory": None,
        }
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "within the scan-proven 4-swipe bound",
        ):
            driver.acquire_measured_priority_category_row(
                device,
                "heritage",
                navigation,
            )

        self.assertEqual([0.22] * 4, device.swipe_ratios)
        self.assertEqual(5, device.hierarchy_reads)
        self.assertEqual(
            ["creation-prerequisite-heritage-category-row-unavailable"],
            device.captures,
        )

    def test_first_category_reacquisition_rejects_duplicate_and_disabled_rows(self) -> None:
        cases = (
            (
                "duplicate",
                [
                    driver.shared.UiNode({
                        "resource-id": "creation-prerequisite-category-heritage",
                    }),
                    driver.shared.UiNode({
                        "resource-id": "creation-prerequisite-category-heritage",
                    }),
                ],
                "cardinality 2",
                "creation-prerequisite-heritage-category-row-cardinality-invalid",
            ),
            (
                "disabled",
                [driver.shared.UiNode({
                    "resource-id": "creation-prerequisite-category-heritage",
                    "enabled": "false",
                    "clickable": "true",
                    "bounds": "[53,300][1028,500]",
                })],
                "visible, enabled, and clickable",
                "creation-prerequisite-heritage-category-row-not-tappable",
            ),
        )
        for name, nodes, error, capture in cases:
            with self.subTest(name=name):
                device = mock.Mock()
                device.hierarchy.return_value = nodes
                device.node_has_tappable_bounds.return_value = True
                navigation = {
                    "viewportByCategory": {
                        category: 0 for category in driver.CATEGORIES
                    },
                    "currentViewport": 0,
                    "lastCategory": None,
                }
                with self.assertRaisesRegex(RuntimeError, error):
                    driver.acquire_measured_priority_category_row(
                        device,
                        "heritage",
                        navigation,
                    )

                device.swipe_down.assert_not_called()
                device.capture.assert_called_once_with(capture)

    def test_first_category_reacquisition_keeps_empty_and_system_budgets_separate(
        self,
    ) -> None:
        overlay = driver.shared.UiNode({"content-desc": "system ui"})
        visible = driver.shared.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[53,300][1028,500]",
            }
        )
        device = mock.Mock()
        device.hierarchy.side_effect = [
            [],
            [],
            [],
            [overlay],
            [overlay],
            [overlay],
            [driver.shared.UiNode({})],
            [visible],
        ]
        device.dismiss_system_ui_anr.side_effect = [True, True, True, False]
        device.node_has_tappable_bounds.return_value = True
        navigation = {
            "viewportByCategory": {category: 0 for category in driver.CATEGORIES},
            "currentViewport": 1,
            "lastCategory": None,
        }
        with mock.patch.object(driver.time, "sleep"):
            row = driver.acquire_measured_priority_category_row(
                device,
                "heritage",
                navigation,
            )

        self.assertIs(visible, row)
        self.assertEqual(8, device.hierarchy.call_count)
        self.assertEqual(
            [mock.call(distance_ratio=0.22)],
            device.swipe_down.call_args_list,
        )
        device.capture.assert_not_called()

    def test_first_category_reacquisition_exhausts_each_transient_budget(self) -> None:
        cases = (
            (
                "empty",
                [[], [], [], []],
                False,
                "empty-hierarchy",
                "creation-prerequisite-heritage-category-row-empty-hierarchy-exhausted",
            ),
            (
                "system",
                [[driver.shared.UiNode({"content-desc": "system ui"})]] * 4,
                True,
                "system-UI",
                "creation-prerequisite-heritage-category-row-system-ui-exhausted",
            ),
        )
        for name, screens, dismisses, error, capture in cases:
            with self.subTest(name=name):
                device = mock.Mock()
                device.hierarchy.side_effect = screens
                device.dismiss_system_ui_anr.return_value = dismisses
                navigation = {
                    "viewportByCategory": {
                        category: 0 for category in driver.CATEGORIES
                    },
                    "currentViewport": 0,
                    "lastCategory": None,
                }
                with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
                    RuntimeError,
                    error,
                ):
                    driver.acquire_measured_priority_category_row(
                        device,
                        "heritage",
                        navigation,
                    )

                self.assertEqual(4, device.hierarchy.call_count)
                device.swipe_down.assert_not_called()
                device.capture.assert_called_once_with(capture)

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
        self.assertEqual(0.60, driver.DASHBOARD_SCAN_GESTURE_RATIO)
        self.assertEqual(18, driver.DASHBOARD_SCAN_MAX_SCROLLS)
        self.assertEqual(
            2,
            dashboard_source.count("distance_ratio=DASHBOARD_SCAN_GESTURE_RATIO"),
        )
        self.assertIn("max_scrolls = DASHBOARD_SCAN_MAX_SCROLLS", dashboard_source)
        self.assertIn("max_scrolls=max_scrolls", dashboard_source)
        self.assertIn("acquire_stable_start_origin(", dashboard_source)
        self.assertIn("max_reverse_swipes=8", dashboard_source)
        self.assertIn("stable_repeats=2", dashboard_source)
        self.assertIn("max_consecutive_empty_reads=3", dashboard_source)
        self.assertIn("initial_observation=scan_origin", dashboard_source)
        self.assertNotIn("reset_scroll_to_top", dashboard_source)

        stable_origin_source = inspect.getsource(driver.acquire_stable_start_origin)
        self.assertIn("fresh_hierarchy_timed(", stable_origin_source)
        self.assertIn("deadline=deadline", stable_origin_source)
        self.assertIn("device.swipe_down(", stable_origin_source)
        self.assertNotIn("read_only_hierarchy", stable_origin_source)
        self.assertNotIn("observer", stable_origin_source)
        self.assertNotIn("COMPOSED_SCAN_TIMING_TRIGGER_FIELDS", stable_origin_source)
        self.assertEqual(
            30_000,
            driver.PHASE_BUDGET_MS["dashboard-authority-inventory"],
        )
        self.assertEqual(
            90_000,
            driver.PHASE_BUDGET_MS["advanced-editor-gate-inventory"],
        )
        self.assertEqual(
            120_000,
            driver.PHASE_BUDGET_MS["prerequisite-authority-inventory"],
        )

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

        restore_reacquisition = initial_source.index(
            "reacquire_exact_ready_creation_method("
        )
        self.assertNotIn("measured_reverse_reacquisition_bound(", initial_source)
        self.assertNotIn("move_between_measured_viewports(", initial_source)
        restored_method_source = initial_source[restore_reacquisition:]
        self.assertIn(
            "advanced_editor_deadline = progress.active_phase_deadline(",
            initial_source,
        )
        self.assertGreaterEqual(
            initial_source.count("deadline=advanced_editor_deadline"),
            2,
        )
        self.assertIn(
            "max_swipes=DASHBOARD_SCAN_MAX_SCROLLS",
            restored_method_source,
        )
        self.assertIn("scan_observer=progress.record_scan", restored_method_source)
        self.assertNotIn("max_swipes=1", restored_method_source)

        method_reacquisition_source = inspect.getsource(
            driver.reacquire_exact_ready_creation_method
        )
        self.assertIn('"creation-stage-method"', method_reacquisition_source)
        self.assertIn(
            "distance_ratio=DASHBOARD_SCAN_GESTURE_RATIO",
            method_reacquisition_source,
        )
        self.assertIn("fresh_hierarchy_timed(", method_reacquisition_source)
        self.assertIn("deadline=deadline", method_reacquisition_source)
        self.assertIn("stable-start-without-method", method_reacquisition_source)
        self.assertIn("max_empty_hierarchy_reads", method_reacquisition_source)
        self.assertIn("max_system_ui_dismissals", method_reacquisition_source)
        self.assertIn("max_swipes > DASHBOARD_SCAN_MAX_SCROLLS", method_reacquisition_source)
        self.assertIn('"direction": CREATION_METHOD_REACQUISITION_DIRECTION', method_reacquisition_source)
        self.assertIn('"distanceRatio": DASHBOARD_SCAN_GESTURE_RATIO', method_reacquisition_source)
        self.assertIn('"deadlineEnforced": deadline is not None', method_reacquisition_source)
        self.assertIn("detail != expected_detail", method_reacquisition_source)
        self.assertIn(
            'capture("creation-stage-method-ready-not-ready")',
            method_reacquisition_source,
        )

        rewind_source = inspect.getsource(driver.rewind_to_exact_resource_id)
        self.assertIn(
            "fresh_hierarchy_timed(device, [], deadline=deadline)",
            rewind_source,
        )
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
            distance_ratio=driver.DASHBOARD_SCAN_GESTURE_RATIO,
            stable_repeats=2,
            max_consecutive_empty_reads=3,
            delay_seconds=0.0,
            deadline=None,
        )
        scan.assert_called_once_with(
            device,
            scan_id="advanced-editor-gate",
            max_scrolls=18,
            distance_ratio=driver.DASHBOARD_SCAN_GESTURE_RATIO,
            initial_observation=origin,
            delay_seconds=0.0,
            observer=None,
            deadline=None,
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

    def test_dashboard_origin_and_forward_scan_share_one_phase_deadline(self) -> None:
        binding = driver.shared.UiNode({
            "resource-id": "creation-wizard-binding",
            "content-desc": "Revision 7",
        })
        method = driver.shared.UiNode({
            "resource-id": "creation-stage-method",
            "content-desc": "Priority",
            "enabled": "true",
            "clickable": "true",
            "bounds": "[10,100][900,300]",
        })
        device = mock.Mock()
        route_nodes = self.dashboard_route_nodes()
        origin = self.priority_rank_origin([*route_nodes, binding, method])
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=origin,
        ) as acquire, mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan(
                [[*route_nodes, binding, method]], 2
            ),
        ) as scan:
            driver.assert_uncreated_advanced_editor_gated(
                device,
                deadline=123.0,
            )

        self.assertEqual(123.0, acquire.call_args.kwargs["deadline"])
        self.assertEqual(123.0, scan.call_args.kwargs["deadline"])

    def test_compact_dashboard_origin_uses_one_exact_post_marker_snapshot(
        self,
    ) -> None:
        dashboard = self.dashboard_route_nodes()
        binding = self.canonical_node(
            "creation-wizard-binding",
            **{"text": "", "content-desc": "Revision 7"},
        )
        method = self.canonical_node(
            "creation-stage-method",
            **{"text": "", "content-desc": "Priority"},
        )
        exact_dashboard = [*dashboard, binding, method]

        class TransitionDevice:
            def __init__(self) -> None:
                self.responses = [exact_dashboard]
                self.deadlines: list[float] = []
                self.captures: list[str] = []

            def hierarchy(
                self,
                *,
                deadline: float,
                dump_attempt_max_seconds: float,
                allow_direct_reconciliation: bool,
            ) -> list[driver.shared.UiNode]:
                self.assert_hierarchy_policy(
                    dump_attempt_max_seconds,
                    allow_direct_reconciliation,
                )
                self.deadlines.append(deadline)
                return self.responses.pop(0)

            @staticmethod
            def assert_hierarchy_policy(
                dump_attempt_max_seconds: float,
                allow_direct_reconciliation: bool,
            ) -> None:
                if (
                    dump_attempt_max_seconds
                    != driver.POST_CONFIRM_DASHBOARD_DUMP_ATTEMPT_MAX_SECONDS
                    or allow_direct_reconciliation is not False
                ):
                    raise AssertionError("compact dashboard used the wrong dump policy")

            def read_only_hierarchy(
                self,
                *,
                deadline: float,
            ) -> list[driver.shared.UiNode]:
                raise AssertionError(
                    "compact transition must avoid the hanging direct stream"
                )

            def capture(self, name: str, *, deadline: float) -> None:
                self.captures.append(name)

            def node_has_tappable_bounds(
                self,
                _node: driver.shared.UiNode,
                *,
                deadline: float,
            ) -> bool:
                return True

        device = TransitionDevice()
        deadline = driver.time.monotonic() + 30.0
        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([exact_dashboard], 0),
        ) as scan:
            proof = driver.assert_uncreated_advanced_editor_gated(
                device,
                scan_id="advanced-editor-gate-post-confirm",
                deadline=deadline,
                compact_current=True,
            )

        self.assertEqual(
            driver.CreationDashboardScanProof("Revision 7", "Priority", 0, 0),
            proof,
        )
        self.assertEqual([deadline], device.deadlines)
        self.assertEqual([], device.responses)
        self.assertEqual([], device.captures)
        origin = scan.call_args.kwargs["initial_observation"]
        self.assertIs(exact_dashboard, origin.nodes)
        self.assertEqual(
            driver.POST_CONFIRM_DASHBOARD_SCAN_MAX_SCROLLS,
            scan.call_args.kwargs["max_scrolls"],
        )
        self.assertEqual(10, driver.POST_CONFIRM_DASHBOARD_SCAN_MAX_SCROLLS)
        self.assertEqual(
            driver.POST_CONFIRM_DASHBOARD_DUMP_ATTEMPT_MAX_SECONDS,
            scan.call_args.kwargs["hierarchy_dump_attempt_max_seconds"],
        )
        self.assertIs(
            False,
            scan.call_args.kwargs["allow_direct_hierarchy_reconciliation"],
        )
        self.assertIs(
            False,
            scan.call_args.kwargs["allow_direct_swipe_reconciliation"],
        )

    def test_compact_dashboard_origin_rejects_duplicate_route_without_retry(
        self,
    ) -> None:
        dashboard = self.dashboard_route_nodes()

        class DuplicateDevice:
            def __init__(self) -> None:
                self.reads = 0
                self.captures: list[tuple[str, float]] = []

            def hierarchy(
                self,
                *,
                deadline: float,
                dump_attempt_max_seconds: float,
                allow_direct_reconciliation: bool,
            ) -> list[driver.shared.UiNode]:
                self.assertEqualPolicy(
                    dump_attempt_max_seconds,
                    allow_direct_reconciliation,
                )
                self.reads += 1
                return [dashboard[0], dashboard[0], dashboard[1]]

            @staticmethod
            def assertEqualPolicy(
                dump_attempt_max_seconds: float,
                allow_direct_reconciliation: bool,
            ) -> None:
                if (
                    dump_attempt_max_seconds != 30.0
                    or allow_direct_reconciliation is not False
                ):
                    raise AssertionError("compact dashboard used the wrong dump policy")

            def capture(self, name: str, *, deadline: float) -> None:
                self.captures.append((name, deadline))

        device = DuplicateDevice()
        deadline = driver.time.monotonic() + 30.0
        with self.assertRaisesRegex(RuntimeError, "exposed 2 exact"), mock.patch.object(
            driver,
            "sleep_before_phase_deadline",
        ) as sleep:
            driver.wait_for_compact_dashboard_origin(
                device,
                scan_id="advanced-editor-gate-post-confirm",
                deadline=deadline,
            )

        self.assertEqual(1, device.reads)
        self.assertEqual(
            [
                (
                    "advanced-editor-gate-post-confirm-phone-runner-create-"
                    "current-cardinality-invalid",
                    deadline,
                )
            ],
            device.captures,
        )
        sleep.assert_not_called()

    def test_compact_dashboard_origin_never_unions_split_route_snapshots(
        self,
    ) -> None:
        dashboard = self.dashboard_route_nodes()

        class SplitDevice:
            def __init__(self) -> None:
                self.responses = [[dashboard[0]], [dashboard[1]]]
                self.captures: list[tuple[str, float]] = []

            def hierarchy(
                self,
                *,
                deadline: float,
                dump_attempt_max_seconds: float,
                allow_direct_reconciliation: bool,
            ) -> list[driver.shared.UiNode]:
                if (
                    dump_attempt_max_seconds != 30.0
                    or allow_direct_reconciliation is not False
                ):
                    raise AssertionError("compact dashboard used the wrong dump policy")
                return self.responses.pop(0)

            def capture(self, name: str, *, deadline: float) -> None:
                self.captures.append((name, deadline))

        device = SplitDevice()
        deadline = driver.time.monotonic() + 10.0
        with self.assertRaisesRegex(
            RuntimeError,
            "single post-marker dashboard snapshot",
        ):
            driver.wait_for_compact_dashboard_origin(
                device,
                scan_id="advanced-editor-gate-post-confirm",
                deadline=deadline,
            )

        self.assertEqual([[dashboard[1]]], device.responses)
        self.assertEqual(
            [
                (
                    "advanced-editor-gate-post-confirm-"
                    "current-transition-unavailable",
                    deadline,
                )
            ],
            device.captures,
        )

    def test_compact_dashboard_origin_empty_until_deadline_fails_without_action(
        self,
    ) -> None:
        class EmptyDevice:
            def __init__(self) -> None:
                self.reads = 0
                self.captures: list[str] = []

            def hierarchy(
                self,
                *,
                deadline: float,
                dump_attempt_max_seconds: float,
                allow_direct_reconciliation: bool,
            ) -> list[driver.shared.UiNode]:
                if (
                    dump_attempt_max_seconds != 30.0
                    or allow_direct_reconciliation is not False
                ):
                    raise AssertionError("compact dashboard used the wrong dump policy")
                self.reads += 1
                return []

            def capture(self, name: str, *, deadline: float) -> None:
                self.captures.append(name)

        device = EmptyDevice()
        deadline = driver.time.monotonic() + 10.0
        with self.assertRaisesRegex(
            RuntimeError,
            "single post-marker dashboard snapshot",
        ):
            driver.wait_for_compact_dashboard_origin(
                device,
                scan_id="advanced-editor-gate-post-confirm",
                deadline=deadline,
            )

        self.assertEqual(1, device.reads)
        self.assertEqual(
            ["advanced-editor-gate-post-confirm-current-transition-unavailable"],
            device.captures,
        )

    def test_compact_dashboard_origin_propagates_ambiguous_dump_without_replay(
        self,
    ) -> None:
        receipt = {
            "classification": "timeout-unknown-outcome",
            "commandPolicy": "non-replayable",
            "replay": {"performed": False, "suppressed": True},
        }
        error = driver.shared.AdbTransportError(
            receipt,
            Path("compact-dashboard-dump-timeout-unknown-outcome.json"),
        )

        class AmbiguousDevice:
            def __init__(self) -> None:
                self.reads = 0

            def hierarchy(
                self,
                *,
                deadline: float,
                dump_attempt_max_seconds: float,
                allow_direct_reconciliation: bool,
            ) -> list[driver.shared.UiNode]:
                if (
                    dump_attempt_max_seconds != 30.0
                    or allow_direct_reconciliation is not False
                ):
                    raise AssertionError("compact dashboard used the wrong dump policy")
                self.reads += 1
                raise error

            def capture(self, name: str, *, deadline: float) -> None:
                raise AssertionError("ambiguous hierarchy must propagate directly")

        device = AmbiguousDevice()
        deadline = driver.time.monotonic() + 30.0
        with mock.patch.object(
            driver,
            "sleep_before_phase_deadline",
        ) as sleep, self.assertRaises(driver.shared.AdbTransportError) as raised:
            driver.wait_for_compact_dashboard_origin(
                device,
                scan_id="advanced-editor-gate-post-confirm",
                deadline=deadline,
            )

        self.assertIs(error, raised.exception)
        self.assertEqual(1, device.reads)
        sleep.assert_not_called()

    def test_compact_dashboard_origin_rejects_noncanonical_suffix_match(self) -> None:
        dashboard = self.dashboard_route_nodes()
        malformed = driver.shared.UiNode(
            {
                **dashboard[0].attributes,
                "package": "invalid.package",
                "resource-id": "invalid.package:id/phone-runner-create",
            }
        )

        class MalformedDevice:
            def __init__(self) -> None:
                self.captures: list[str] = []

            def hierarchy(
                self,
                *,
                deadline: float,
                dump_attempt_max_seconds: float,
                allow_direct_reconciliation: bool,
            ) -> list[driver.shared.UiNode]:
                if (
                    dump_attempt_max_seconds != 30.0
                    or allow_direct_reconciliation is not False
                ):
                    raise AssertionError("compact dashboard used the wrong dump policy")
                return [malformed, dashboard[1]]

            def capture(self, name: str, *, deadline: float) -> None:
                self.captures.append(name)

        device = MalformedDevice()
        with self.assertRaisesRegex(
            RuntimeError,
            "did not expose the canonical Chummer resource identity",
        ):
            driver.wait_for_compact_dashboard_origin(
                device,
                scan_id="advanced-editor-gate-post-confirm",
                deadline=driver.time.monotonic() + 30.0,
            )

        self.assertEqual(
            [
                "advanced-editor-gate-post-confirm-phone-runner-create-"
                "current-identity-invalid"
            ],
            device.captures,
        )

    def test_post_confirm_dashboard_observation_cannot_replay_navigation(self) -> None:
        helper_source = inspect.getsource(driver.wait_for_compact_dashboard_origin)
        for forbidden in (
            "device.shell(",
            "device.tap(",
            "device.swipe_",
            "dismiss_system_ui",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, helper_source)
        self.assertIn("dump_attempt_max_seconds=", helper_source)
        self.assertIn("allow_direct_reconciliation=False", helper_source)
        self.assertNotIn("device.read_only_hierarchy", helper_source)
        execute_source = inspect.getsource(driver.execute)
        self.assertEqual(1, execute_source.count("tap_exact_confirmed_receipt_back("))
        self.assertEqual(
            1,
            execute_source.count(
                "dashboard_ready_marker = wait_for_creation_dashboard_ready_log("
            ),
        )
        self.assertEqual(
            1,
            execute_source.count(
                "require_marker_bound_post_confirm_dashboard("
            ),
        )

    def test_post_back_dashboard_ready_marker_is_exact_and_revision_bound(self) -> None:
        digest = "sha256:" + "a" * 64
        payload = {
            "schema": driver.CREATION_DASHBOARD_READY_SCHEMA,
            "routeAutomationId": "phone-runner-create",
            "dashboardAutomationId": "creation-wizard-dashboard",
            "workspaceId": "workspace-route-ready",
            "contentRevision": 12,
            "savedRevision": 11,
            "contentDigest": digest,
            "sourceDigest": digest,
            "runtimeFingerprint": "",
            "buildMethod": "Priority",
            "snapshotDigest": digest,
            "characterCreated": False,
            "authorityReady": True,
        }
        line = driver.CREATION_DASHBOARD_READY_PREFIX + json.dumps(
            payload,
            separators=(",", ":"),
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.return_value = subprocess.CompletedProcess(
                args=[],
                returncode=0,
                stdout=driver.CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER + "\n" + line,
                stderr="",
            )
            scans: list[dict[str, object]] = []
            observed = driver.wait_for_creation_dashboard_ready_log(
                device,
                expected_content_revision=12,
                expected_saved_revision=11,
                deadline=driver.time.monotonic() + 30.0,
                scan_observer=scans.append,
            )

            self.assertEqual(payload, observed)
            self.assertEqual(1, len(scans))
            self.assertEqual(
                "post-confirm-dashboard-route-ready-log",
                scans[0]["scanId"],
            )
            self.assertEqual(12, scans[0]["observedContentRevision"])
            self.assertEqual(11, scans[0]["observedSavedRevision"])
            self.assertEqual(
                "fresh-cleared-main-log-snapshot-poll",
                scans[0]["observationMode"],
            )
            self.assertEqual(1, scans[0]["logcatReadCount"])
            self.assertEqual(0, scans[0]["emptySnapshotCount"])
            self.assertEqual(
                driver.shared.ADB_CREATION_DASHBOARD_READY_LOGCAT_ARGUMENTS,
                device.run.call_args.args,
            )
            device.hierarchy.assert_not_called()
            device.shell.assert_not_called()

    def test_post_back_dashboard_ready_snapshot_poll_resolves_after_pending_read(
        self,
    ) -> None:
        digest = "sha256:" + "c" * 64
        payload = {
            "schema": driver.CREATION_DASHBOARD_READY_SCHEMA,
            "routeAutomationId": "phone-runner-create",
            "dashboardAutomationId": "creation-wizard-dashboard",
            "workspaceId": "workspace-route-ready",
            "contentRevision": 12,
            "savedRevision": 11,
            "contentDigest": digest,
            "sourceDigest": digest,
            "runtimeFingerprint": "",
            "buildMethod": "Priority",
            "snapshotDigest": digest,
            "characterCreated": False,
            "authorityReady": True,
        }
        marker = driver.CREATION_DASHBOARD_READY_PREFIX + json.dumps(
            payload,
            separators=(",", ":"),
        )
        pending = subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout=driver.CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER,
            stderr="",
        )
        resolved = subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout=driver.CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER + "\n" + marker,
            stderr="",
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.side_effect = (pending, resolved)
            scans: list[dict[str, object]] = []
            with mock.patch.object(driver.time, "sleep"):
                observed = driver.wait_for_creation_dashboard_ready_log(
                    device,
                    expected_content_revision=12,
                    expected_saved_revision=11,
                    deadline=driver.time.monotonic() + 30.0,
                    scan_observer=scans.append,
                )

            self.assertEqual(payload, observed)
            self.assertEqual(2, device.run.call_count)
            self.assertEqual(2, scans[0]["logcatReadCount"])
            self.assertEqual(1, scans[0]["emptySnapshotCount"])
            device.shell.assert_not_called()
            device.hierarchy.assert_not_called()

    def test_post_back_dashboard_ready_snapshot_poll_fails_closed_at_deadline(
        self,
    ) -> None:
        pending = subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout=driver.CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER,
            stderr="",
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.return_value = pending
            with (
                mock.patch.object(
                    driver,
                    "POST_CONFIRM_DASHBOARD_READY_TIMEOUT_SECONDS",
                    0.02,
                ),
                mock.patch.object(
                    driver,
                    "POST_CONFIRM_DASHBOARD_READY_POLL_DELAY_SECONDS",
                    0.005,
                ),
                self.assertRaisesRegex(RuntimeError, "Timed out waiting"),
            ):
                driver.wait_for_creation_dashboard_ready_log(
                    device,
                    expected_content_revision=12,
                    expected_saved_revision=11,
                    deadline=driver.time.monotonic() + 1.0,
                )

            self.assertGreaterEqual(device.run.call_count, 1)
            device.shell.assert_not_called()
            device.hierarchy.assert_not_called()

    def test_post_confirm_dashboard_binding_is_executable_and_fail_closed(
        self,
    ) -> None:
        digest = "sha256:" + "d" * 64
        marker: dict[str, object] = {
            "contentRevision": 12,
            "snapshotDigest": digest,
        }
        dashboard = driver.CreationDashboardScanProof(
            binding=f"Revision 12 · snapshot {digest[:12]}",
            method_detail="Priority",
            swipes=0,
            method_viewport=0,
        )
        driver.require_marker_bound_post_confirm_dashboard(marker, dashboard)

        with self.assertRaisesRegex(RuntimeError, "marker-bound revision"):
            driver.require_marker_bound_post_confirm_dashboard(
                marker,
                dashboard._replace(binding="Revision 11 · snapshot sha256:bad00"),
            )
        with self.assertRaisesRegex(RuntimeError, "canonical binding authority"):
            driver.require_marker_bound_post_confirm_dashboard(
                {"contentRevision": True, "snapshotDigest": digest},
                dashboard,
            )

    def test_dashboard_ready_marker_uses_the_shell_presented_root_page(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        callback = source[
            source.index("private async Task EmitCreationDashboardRouteReadyAsync(") :
            source.index("private void AddCreationMethodRoute(")
        ]

        self.assertIn(
            "!ReferenceEquals(Shell.Current?.CurrentPage, this)",
            callback,
        )
        self.assertNotIn("Navigation.NavigationStack", callback)

    def test_confirmed_prerequisite_back_resets_to_authored_phone_runner_route(self) -> None:
        source = (
            NATIVE / "CreationPrerequisitePreviewPage.cs"
        ).read_text(encoding="utf-8")
        callback = source[
            source.index("private async Task BackToBuildAsync()") :
            source.index("private static string FormatBudget(")
        ]

        self.assertEqual(
            1,
            callback.count(
                "await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute, animate: false);"
            ),
        )
        self.assertIn(
            "Shell.Current is not MainShell { UsesTabletComposition: false } shell",
            callback,
        )
        self.assertIn("throw new InvalidOperationException(", callback)
        self.assertNotIn("ScheduleCreationDashboardRouteReady", callback)
        self.assertNotIn("EmitCreationDashboardRouteReady", callback)
        self.assertNotIn("Navigation.PopToRootAsync", callback)
        self.assertNotIn("Navigation.PopAsync", callback)
        self.assertNotIn("Navigation.NavigationStack", callback)

    def test_dashboard_ready_marker_is_armed_on_loaded_and_every_refresh(self) -> None:
        source = inspect.getsource(driver.execute)
        self.assertEqual(1, source.count("tap_exact_confirmed_receipt_back("))

        build_page = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        dashboard = build_page[
            build_page.index("private void AddCreationWizardDashboard()") :
            build_page.index("private void ScheduleCreationDashboardRouteReady(")
        ]
        attached = dashboard.index("header.Loaded +=")
        added = dashboard.index("_body.Add(header);")
        immediate = dashboard.index(
            "ScheduleCreationDashboardRouteReady(",
            added,
        )
        self.assertLess(attached, added)
        self.assertLess(added, immediate)
        self.assertEqual(2, dashboard.count("ScheduleCreationDashboardRouteReady("))

    def test_dashboard_ready_marker_poll_is_bounded_and_generation_idempotent(
        self,
    ) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        self.assertIn(
            "CreationDashboardRouteReadySettleDelay =\n"
            "        TimeSpan.FromMilliseconds(750);",
            source,
        )
        self.assertIn(
            "CreationDashboardRouteReadyPollDelay =\n"
            "        TimeSpan.FromMilliseconds(250);",
            source,
        )
        self.assertIn(
            "CreationDashboardRouteReadyMaximumWait =\n"
            "        TimeSpan.FromSeconds(25);",
            source,
        )
        callback = source[
            source.index("private async Task EmitCreationDashboardRouteReadyAsync(") :
            source.index("private void AddCreationMethodRoute(")
        ]
        self.assertIn("Stopwatch.GetElapsedTime(waitStarted)", callback)
        self.assertIn("< CreationDashboardRouteReadyMaximumWait", callback)
        self.assertIn(
            "remaining < CreationDashboardRouteReadyPollDelay",
            callback,
        )
        self.assertIn(
            "_creationDashboardRouteReadyEmittedGeneration == appearanceGeneration",
            callback,
        )
        self.assertIn(
            "_creationDashboardRouteReadyEmittedGeneration = appearanceGeneration;",
            callback,
        )
        self.assertLess(
            callback.index(
                "_creationDashboardRouteReadyEmittedGeneration = appearanceGeneration;"
            ),
            callback.index("global::Android.Util.Log.Info("),
        )
        for terminal_guard in (
            "cancellationToken.IsCancellationRequested",
            "appearanceGeneration != _creationDashboardAppearanceGeneration",
            "!_body.Children.Contains(header)",
            "Coordinator.State.CreationWizard is not { } currentSnapshot",
        ):
            with self.subTest(terminal_guard=terminal_guard):
                self.assertIn(terminal_guard, callback)

    def test_post_back_dashboard_ready_marker_rejects_stale_duplicate_and_malformed(
        self,
    ) -> None:
        digest = "sha256:" + "b" * 64
        valid = {
            "schema": driver.CREATION_DASHBOARD_READY_SCHEMA,
            "routeAutomationId": "phone-runner-create",
            "dashboardAutomationId": "creation-wizard-dashboard",
            "workspaceId": "workspace-route-ready",
            "contentRevision": 12,
            "savedRevision": 11,
            "contentDigest": digest,
            "sourceDigest": digest,
            "runtimeFingerprint": "",
            "buildMethod": "Priority",
            "snapshotDigest": digest,
            "characterCreated": False,
            "authorityReady": True,
        }
        cases = {
            "stale-revision": {
                **valid,
                "contentRevision": 11,
            },
            "wrong-schema": {
                **valid,
                "schema": "chummer.android.creation-dashboard-route-ready/v0",
            },
            "malformed-digest": {
                **valid,
                "snapshotDigest": "not-a-digest",
            },
            "normalized-build-method": {
                **valid,
                "buildMethod": "priority",
            },
            "invented-runtime-fingerprint": {
                **valid,
                "runtimeFingerprint": digest,
            },
            "null-runtime-fingerprint": {
                **valid,
                "runtimeFingerprint": None,
            },
            "boolean-content-revision": {
                **valid,
                "contentRevision": True,
            },
            "float-content-revision": {
                **valid,
                "contentRevision": 12.0,
            },
            "boolean-saved-revision": {
                **valid,
                "savedRevision": False,
            },
            "integer-character-created": {
                **valid,
                "characterCreated": 0,
            },
            "integer-authority-ready": {
                **valid,
                "authorityReady": 1,
            },
        }
        for name, payload in cases.items():
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                device = mock.Mock()
                device.evidence = Path(temporary)
                device.run.return_value = subprocess.CompletedProcess(
                    args=[],
                    returncode=0,
                    stdout=(
                        driver.CREATION_DASHBOARD_READY_PREFIX
                        + json.dumps(payload, separators=(",", ":"))
                    ),
                    stderr="",
                )
                with self.assertRaisesRegex(
                    RuntimeError,
                    "stale or malformed|scalar types differed",
                ):
                    driver.wait_for_creation_dashboard_ready_log(
                        device,
                        expected_content_revision=12,
                        expected_saved_revision=11,
                        deadline=driver.time.monotonic() + 30.0,
                    )
                device.hierarchy.assert_not_called()
                device.shell.assert_not_called()

        with tempfile.TemporaryDirectory() as temporary:
            device = mock.Mock()
            device.evidence = Path(temporary)
            marker = driver.CREATION_DASHBOARD_READY_PREFIX + json.dumps(
                valid,
                separators=(",", ":"),
            )
            device.run.return_value = subprocess.CompletedProcess(
                args=[],
                returncode=0,
                stdout=marker + "\n" + marker,
                stderr="",
            )
            with self.assertRaisesRegex(RuntimeError, "Expected one exact"):
                driver.wait_for_creation_dashboard_ready_log(
                    device,
                    expected_content_revision=12,
                    expected_saved_revision=11,
                    deadline=driver.time.monotonic() + 30.0,
                )
            device.hierarchy.assert_not_called()
            device.shell.assert_not_called()

        missing_runtime = dict(valid)
        del missing_runtime["runtimeFingerprint"]
        with tempfile.TemporaryDirectory() as temporary:
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.return_value = subprocess.CompletedProcess(
                args=[],
                returncode=0,
                stdout=(
                    driver.CREATION_DASHBOARD_READY_PREFIX
                    + json.dumps(missing_runtime, separators=(",", ":"))
                ),
                stderr="",
            )
            with self.assertRaisesRegex(RuntimeError, "field set differed"):
                driver.wait_for_creation_dashboard_ready_log(
                    device,
                    expected_content_revision=12,
                    expected_saved_revision=11,
                    deadline=driver.time.monotonic() + 30.0,
                )
            device.hierarchy.assert_not_called()
            device.shell.assert_not_called()

    def test_post_back_dashboard_ready_transport_ambiguity_cannot_reach_hierarchy(
        self,
    ) -> None:
        receipt = {
            "classification": "timeout-unknown-outcome",
            "commandPolicy": "read-only-retryable",
            "replay": {"performed": True, "suppressed": False},
        }
        transport = driver.shared.AdbTransportError(
            receipt,
            Path("route-ready-timeout.json"),
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = mock.Mock()
            device.evidence = Path(temporary)
            device.run.side_effect = transport
            with self.assertRaises(driver.shared.AdbTransportError):
                driver.wait_for_creation_dashboard_ready_log(
                    device,
                    expected_content_revision=12,
                    expected_saved_revision=11,
                    deadline=driver.time.monotonic() + 30.0,
                )
            device.run.assert_called_once()
            device.hierarchy.assert_not_called()
            device.shell.assert_not_called()

    def test_dashboard_deadline_scan_rejects_noncanonical_native_toolbar_identity(
        self,
    ) -> None:
        binding = self.canonical_node(
            "creation-wizard-binding",
            **{"content-desc": "Revision 7"},
        )
        method = self.canonical_node(
            "creation-stage-method",
            **{"content-desc": "Priority"},
        )
        canonical_routes = self.dashboard_route_nodes()
        toolbar_attributes = dict(canonical_routes[-1].attributes)
        cases = (
            (
                "resource-backed-toolbar",
                {
                    **toolbar_attributes,
                    "resource-id": (
                        f"{driver.shared.PACKAGE}:id/build-save-runner"
                    ),
                },
                "canonical native toolbar accessibility node",
            ),
            (
                "wrong-toolbar-class",
                {**toolbar_attributes, "class": "android.widget.TextView"},
                "canonical native toolbar accessibility node",
            ),
            (
                "wrong-toolbar-package",
                {**toolbar_attributes, "package": "com.example.other"},
                "canonical native toolbar accessibility node",
            ),
            (
                "non-focusable-toolbar",
                {**toolbar_attributes, "focusable": "false"},
                "canonical native toolbar accessibility node",
            ),
            (
                "disabled-toolbar",
                {**toolbar_attributes, "enabled": "false"},
                "canonical route, root, toolbar",
            ),
            (
                "non-clickable-toolbar",
                {**toolbar_attributes, "clickable": "false"},
                "canonical route, root, toolbar",
            ),
            (
                "prefix-lookalike-toolbar",
                {
                    **toolbar_attributes,
                    "content-desc": "build-save-runner-lookalike",
                },
                "canonical route, root, toolbar",
            ),
        )
        for case, invalid_attributes, error in cases:
            nodes = [
                *canonical_routes[:-1],
                driver.shared.UiNode(invalid_attributes),
                binding,
                method,
            ]
            origin = self.priority_rank_origin(nodes)
            device = mock.Mock()
            device.node_has_tappable_bounds.return_value = True
            with self.subTest(case=case), mock.patch.object(
                driver,
                "acquire_stable_start_origin",
                return_value=origin,
            ), mock.patch.object(
                driver,
                "scan_forward_with_receipt",
                return_value=driver.StableViewportScan([nodes], 0),
            ), self.assertRaisesRegex(RuntimeError, error):
                driver.assert_uncreated_advanced_editor_gated(
                    device,
                    deadline=123.0,
                )

        duplicate_nodes = [
            *canonical_routes,
            canonical_routes[-1],
            binding,
            method,
        ]
        duplicate_origin = self.priority_rank_origin(duplicate_nodes)
        duplicate_device = mock.Mock()
        duplicate_device.node_has_tappable_bounds.return_value = True
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=duplicate_origin,
        ), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([duplicate_nodes], 0),
        ), self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.assert_uncreated_advanced_editor_gated(
                duplicate_device,
                deadline=123.0,
            )

        clipped_toolbar = driver.shared.UiNode(toolbar_attributes)
        clipped_nodes = [
            *canonical_routes[:-1],
            clipped_toolbar,
            binding,
            method,
        ]
        clipped_origin = self.priority_rank_origin(clipped_nodes)
        clipped_device = mock.Mock()
        clipped_device.node_has_tappable_bounds.side_effect = (
            lambda node, **_kwargs: node is not clipped_toolbar
        )
        with mock.patch.object(
            driver,
            "acquire_stable_start_origin",
            return_value=clipped_origin,
        ), mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([clipped_nodes], 0),
        ), self.assertRaisesRegex(RuntimeError, "canonical route, root, toolbar"):
            driver.assert_uncreated_advanced_editor_gated(
                clipped_device,
                deadline=123.0,
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

    def test_hosted_creation_method_restore_observes_each_scan_ratio_gesture(
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
                # Run 33323001738 exported five forward movement gestures, but
                # its exact ready method remained above the viewport after five
                # same-geometry reverse gestures. The observed reverse search
                # must continue without treating that count as a coordinate.
                self.remaining = 7
                self.hierarchy_reads = 0
                self.swipe_ratios: list[float] = []
                self.captures: list[str] = []

            def hierarchy(self):
                if self.hierarchy_reads != len(self.swipe_ratios):
                    raise AssertionError(
                        "Every reverse gesture must be followed by its own fresh hierarchy"
                    )
                self.hierarchy_reads += 1
                if self.remaining == 0:
                    return [exact_method]
                return [driver.shared.UiNode({
                    **clipped_parent.attributes,
                    "content-desc": f"remaining-{self.remaining}",
                })]

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def swipe_down(self, *, distance_ratio):
                if self.hierarchy_reads != len(self.swipe_ratios) + 1:
                    raise AssertionError("A reverse gesture requires a fresh prior hierarchy")
                self.swipe_ratios.append(distance_ratio)
                if distance_ratio != 0.60:
                    raise AssertionError(f"unexpected reverse ratio: {distance_ratio!r}")
                self.remaining -= 1

            @staticmethod
            def node_has_tappable_bounds(node):
                return node is exact_method

            def capture(self, name):
                self.captures.append(name)

        device = HostedRestoreDevice()
        observations: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"):
            node, detail, reverse_swipes = driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS,
                scan_observer=observations.append,
            )

        self.assertIs(exact_method, node)
        self.assertEqual("Priority", detail)
        self.assertEqual(7, reverse_swipes)
        self.assertEqual(8, device.hierarchy_reads)
        self.assertEqual([0.60] * 7, device.swipe_ratios)
        self.assertEqual([], device.captures)
        self.assertEqual(1, len(observations))
        self.assertEqual("resolved", observations[0]["status"])
        self.assertEqual(7, observations[0]["swipes"])
        self.assertEqual(
            driver.DASHBOARD_SCAN_MAX_SCROLLS,
            observations[0]["configuredMaxScrolls"],
        )
        self.assertEqual("down", observations[0]["direction"])
        self.assertEqual(0.60, observations[0]["distanceRatio"])
        self.assertFalse(observations[0]["deadlineEnforced"])
        driver.require_creation_method_reacquisition_receipt(observations[0])

    def test_dashboard_method_reacquisition_rejects_a_bound_above_shared_cap(
        self,
    ) -> None:
        device = mock.Mock()
        with self.assertRaisesRegex(ValueError, "exact gesture"):
            driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS + 1,
            )
        device.hierarchy.assert_not_called()
        device.swipe_down.assert_not_called()

        for kwargs in ({"phase_id": "unknown-phase"}, {"phase_id": []}):
            with self.subTest(kwargs=kwargs), self.assertRaisesRegex(
                ValueError,
                "exact gesture",
            ):
                driver.reacquire_exact_ready_creation_method(
                    device,
                    expected_detail="Priority",
                    max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS,
                    **kwargs,
                )

    def test_dashboard_method_reacquisition_threads_one_active_deadline(
        self,
    ) -> None:
        method = driver.shared.UiNode({
            "resource-id": "creation-stage-method",
            "content-desc": "Priority",
            "enabled": "true",
            "clickable": "true",
            "bounds": "[53,350][1028,550]",
        })

        class DeadlineDevice:
            def __init__(self) -> None:
                self.deadlines: list[float] = []
                self.swipe_deadlines: list[float] = []

            def hierarchy(self, *, deadline: float):
                self.deadlines.append(deadline)
                return (
                    [method]
                    if self.swipe_deadlines
                    else [driver.shared.UiNode({"text": "before method"})]
                )

            def swipe_down(self, *, distance_ratio: float, deadline: float):
                if distance_ratio != driver.DASHBOARD_SCAN_GESTURE_RATIO:
                    raise AssertionError(f"unexpected ratio: {distance_ratio!r}")
                self.swipe_deadlines.append(deadline)

            @staticmethod
            def dismiss_system_ui_anr(_nodes, *, deadline: float):
                if deadline != 20.0:
                    raise AssertionError(f"unexpected deadline: {deadline!r}")
                return False

            @staticmethod
            def node_has_tappable_bounds(_node, *, deadline: float):
                if deadline != 20.0:
                    raise AssertionError(f"unexpected deadline: {deadline!r}")
                return True

            @staticmethod
            def capture(name):
                raise AssertionError(f"unexpected capture: {name}")

        device = DeadlineDevice()
        observations: list[dict[str, object]] = []
        now = [10.0]

        def advance(seconds: float) -> None:
            now[0] += seconds

        with mock.patch.object(
            driver.time,
            "monotonic",
            side_effect=lambda: now[0],
        ), mock.patch.object(driver.time, "sleep", side_effect=advance):
            node, detail, swipes = driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS,
                scan_observer=observations.append,
                deadline=20.0,
            )

        self.assertIs(method, node)
        self.assertEqual("Priority", detail)
        self.assertEqual(1, swipes)
        self.assertEqual([20.0, 20.0], device.deadlines)
        self.assertEqual([20.0], device.swipe_deadlines)
        self.assertTrue(observations[0]["deadlineEnforced"])
        driver.require_creation_method_reacquisition_receipt(observations[0])

    def test_fresh_hierarchy_rejects_completion_after_shared_deadline(self) -> None:
        class LateDevice:
            @staticmethod
            def hierarchy(*, deadline: float):
                if deadline != 1.0:
                    raise AssertionError(f"unexpected deadline: {deadline!r}")
                return [driver.shared.UiNode({"text": "late"})]

        with mock.patch.object(
            driver.time,
            "monotonic",
            side_effect=(0.0, 1.0),
        ), self.assertRaisesRegex(RuntimeError, "phase deadline expired"):
            driver.fresh_hierarchy_timed(LateDevice(), [], deadline=1.0)

    def test_method_receipt_rejects_nonboolean_deadline_and_omitted_waits(
        self,
    ) -> None:
        receipt: dict[str, object] = {
            "scanId": driver.CREATION_METHOD_REACQUISITION_SCAN_ID,
            "status": "resolved",
            "direction": driver.CREATION_METHOD_REACQUISITION_DIRECTION,
            "distanceRatio": driver.DASHBOARD_SCAN_GESTURE_RATIO,
            "screens": 8,
            "swipes": 7,
            "configuredMaxScrolls": driver.DASHBOARD_SCAN_MAX_SCROLLS,
            "stableRepeats": 2,
            "emptyHierarchyReads": 0,
            "maximumEmptyHierarchyReads": 3,
            "systemUiDismissals": 0,
            "maximumSystemUiDismissals": 3,
            "deadlineEnforced": True,
            "phaseBudgetMs": driver.PHASE_BUDGET_MS[
                "advanced-editor-gate-inventory"
            ],
            "hierarchyReadCount": 8,
            "hierarchyElapsedMs": 4_000,
            "maximumHierarchyReadMs": 500,
            "elapsedMs": 5_400,
        }
        driver.require_creation_method_reacquisition_receipt(
            receipt,
            require_deadline=True,
        )

        for field, forged in (
            ("deadlineEnforced", 1),
            ("elapsedMs", 4_000),
        ):
            with self.subTest(field=field):
                candidate = dict(receipt)
                candidate[field] = forged
                with self.assertRaisesRegex(
                    RuntimeError,
                    "deadline authority|did not reconcile",
                ):
                    driver.require_creation_method_reacquisition_receipt(
                        candidate,
                        require_deadline=True,
                    )

    def test_post_swipe_wait_cannot_cross_shared_phase_deadline(self) -> None:
        with mock.patch.object(
            driver.time,
            "monotonic",
            return_value=0.9,
        ), mock.patch.object(driver.time, "sleep") as sleep, self.assertRaisesRegex(
            RuntimeError,
            "cannot accommodate method-reacquisition post-swipe wait",
        ):
            driver.sleep_before_phase_deadline(
                0.2,
                deadline=1.0,
                operation="method-reacquisition post-swipe wait",
            )
        sleep.assert_not_called()

    def test_dashboard_scan_ratio_has_symmetric_physical_swipe_geometry(self) -> None:
        device = mock.Mock()
        device.display_size.return_value = (1080, 2400)

        driver.shared.Device.swipe_up(
            device,
            distance_ratio=driver.DASHBOARD_SCAN_GESTURE_RATIO,
        )
        driver.shared.Device.swipe_down(
            device,
            distance_ratio=driver.DASHBOARD_SCAN_GESTURE_RATIO,
        )

        self.assertEqual(
            [
                mock.call(
                    "input", "swipe", "540", "1968", "540", "528", "300", timeout=15
                ),
                mock.call(
                    "input", "swipe", "540", "720", "540", "2160", "300", timeout=15
                ),
            ],
            device.shell.call_args_list,
        )
        up = device.shell.call_args_list[0].args
        down = device.shell.call_args_list[1].args
        self.assertEqual(
            abs(int(up[3]) - int(up[5])),
            abs(int(down[3]) - int(down[5])),
        )

    def test_dashboard_method_reacquisition_fails_at_stable_start_without_method(
        self,
    ) -> None:
        stable_start = driver.shared.UiNode({
            "resource-id": "creation-wizard-dashboard",
            "content-desc": "stable start without method",
            "bounds": "[0,275][1080,2190]",
        })

        class NoOpDevice:
            def __init__(self) -> None:
                self.hierarchy_reads = 0
                self.reverse_swipes = 0
                self.captures: list[str] = []

            def hierarchy(self):
                self.hierarchy_reads += 1
                return [stable_start]

            @staticmethod
            def dismiss_system_ui_anr(_nodes):
                return False

            def swipe_down(self, *, distance_ratio):
                if distance_ratio != driver.DASHBOARD_SCAN_GESTURE_RATIO:
                    raise AssertionError(f"unexpected reverse ratio: {distance_ratio!r}")
                self.reverse_swipes += 1

            @staticmethod
            def node_has_tappable_bounds(_node):
                return False

            def capture(self, name):
                self.captures.append(name)

        device = NoOpDevice()
        observations: list[dict[str, object]] = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "stable start without the exact ready creation-stage-method",
        ):
            driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS,
                scan_observer=observations.append,
            )

        self.assertEqual(3, device.hierarchy_reads)
        self.assertEqual(2, device.reverse_swipes)
        self.assertEqual(
            ["creation-stage-method-ready-stable-start-without-method"],
            device.captures,
        )
        self.assertEqual("stable-start-without-method", observations[0]["status"])
        self.assertEqual(2, observations[0]["swipes"])

    def test_dashboard_method_reacquisition_bounds_empty_hierarchies_separately(
        self,
    ) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = []
        observations: list[dict[str, object]] = []

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "transient empty-hierarchy budget of 3 reads",
        ):
            driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS,
                scan_observer=observations.append,
            )

        self.assertEqual(4, device.hierarchy.call_count)
        device.swipe_down.assert_not_called()
        device.capture.assert_called_once_with(
            "creation-stage-method-ready-empty-hierarchy-exhausted"
        )
        self.assertEqual("reverse-empty-hierarchy-exhausted", observations[0]["status"])
        self.assertEqual(0, observations[0]["swipes"])

    def test_cold_restart_method_reacquisition_accepts_four_transient_empty_roots(
        self,
    ) -> None:
        method = driver.shared.UiNode({
            "resource-id": "creation-stage-method",
            "content-desc": "Priority",
            "enabled": "true",
            "clickable": "true",
            "bounds": "[53,350][1028,550]",
        })
        device = mock.Mock()
        device.hierarchy.side_effect = [[], [], [], [], [method]]
        device.node_has_tappable_bounds.return_value = True
        observations: list[dict[str, object]] = []

        with mock.patch.object(driver.time, "sleep"):
            node, detail, swipes = driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS,
                phase_id="process-restart-reopen",
                scan_observer=observations.append,
            )

        self.assertIs(method, node)
        self.assertEqual("Priority", detail)
        self.assertEqual(0, swipes)
        self.assertEqual(5, device.hierarchy.call_count)
        device.swipe_down.assert_not_called()
        device.capture.assert_not_called()
        self.assertEqual("resolved", observations[0]["status"])
        self.assertEqual(4, observations[0]["emptyHierarchyReads"])
        self.assertEqual(
            driver.PROCESS_RESTART_METHOD_MAX_EMPTY_HIERARCHY_READS,
            observations[0]["maximumEmptyHierarchyReads"],
        )
        self.assertEqual("process-restart-reopen", observations[0]["phaseId"])
        self.assertEqual(
            driver.PHASE_BUDGET_MS["process-restart-reopen"],
            observations[0]["phaseBudgetMs"],
        )

    def test_non_restart_method_phases_reject_a_fourth_transient_empty_root(
        self,
    ) -> None:
        for phase_id in (
            "advanced-editor-gate-inventory",
            "same-process-reopen",
            "resources-prerequisite-rebind",
        ):
            with self.subTest(phase_id=phase_id):
                device = mock.Mock()
                device.hierarchy.return_value = []
                observations: list[dict[str, object]] = []
                with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
                    RuntimeError,
                    "transient empty-hierarchy budget of 3 reads",
                ):
                    driver.reacquire_exact_ready_creation_method(
                        device,
                        expected_detail="Priority",
                        max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS,
                        phase_id=phase_id,
                        scan_observer=observations.append,
                    )
                self.assertEqual(4, device.hierarchy.call_count)
                self.assertEqual(
                    "reverse-empty-hierarchy-exhausted",
                    observations[0]["status"],
                )

    def test_dashboard_method_reacquisition_bounds_system_ui_separately(
        self,
    ) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = [driver.shared.UiNode({"text": "system ui"})]
        device.dismiss_system_ui_anr.return_value = True
        observations: list[dict[str, object]] = []

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "system-UI dismissal budget of 3",
        ):
            driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=driver.DASHBOARD_SCAN_MAX_SCROLLS,
                scan_observer=observations.append,
            )

        self.assertEqual(4, device.hierarchy.call_count)
        device.swipe_down.assert_not_called()
        device.capture.assert_called_once_with(
            "creation-stage-method-ready-system-ui-exhausted"
        )
        self.assertEqual("reverse-system-ui-exhausted", observations[0]["status"])
        self.assertEqual(4, observations[0]["systemUiDismissals"])

    def test_dashboard_method_reacquisition_exhausts_changing_viewport_bound(
        self,
    ) -> None:
        device = mock.Mock()
        device.hierarchy.side_effect = [
            [driver.shared.UiNode({"text": f"viewport-{index}"})]
            for index in range(3)
        ]
        device.dismiss_system_ui_anr.return_value = False
        observations: list[dict[str, object]] = []

        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "dashboard scan bound of 2 swipes",
        ):
            driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=2,
                scan_observer=observations.append,
            )

        self.assertEqual(3, device.hierarchy.call_count)
        self.assertEqual(2, device.swipe_down.call_count)
        device.capture.assert_called_once_with("creation-stage-method-ready-unavailable")
        self.assertEqual("reverse-bound-exhausted", observations[0]["status"])
        self.assertEqual(2, observations[0]["swipes"])

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

    def test_ready_creation_method_not_ready_has_distinct_evidence(self) -> None:
        node = driver.shared.UiNode({
            "resource-id": "creation-stage-method",
            "content-desc": (
                "Priority · " + driver.CREATION_KARMA_AUTHORITY_BLOCKER
            ),
            "enabled": "true",
            "clickable": "true",
        })
        device = mock.Mock()
        device.hierarchy.return_value = [node]
        device.node_has_tappable_bounds.return_value = True

        with self.assertRaisesRegex(RuntimeError, "did not enable"):
            driver.reacquire_exact_ready_creation_method(
                device,
                expected_detail="Priority",
                max_swipes=0,
            )

        device.capture.assert_called_once_with(
            "creation-stage-method-ready-not-ready"
        )
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
            distance_ratio=driver.DASHBOARD_SCAN_GESTURE_RATIO,
            stable_repeats=2,
            max_consecutive_empty_reads=3,
            delay_seconds=0.0,
            deadline=None,
        )
        scan.assert_called_once_with(
            device,
            scan_id="advanced-editor-gate",
            max_scrolls=18,
            distance_ratio=driver.DASHBOARD_SCAN_GESTURE_RATIO,
            initial_observation=fresh_origin,
            delay_seconds=0.0,
            observer=None,
            deadline=None,
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
        def node(selector: str) -> driver.shared.UiNode:
            return driver.shared.UiNode(
                {
                    "package": driver.shared.PACKAGE,
                    "resource-id": f"{driver.shared.PACKAGE}:id/{selector}",
                }
            )

        route = node("creation-prerequisite-page")
        method = node("creation-prerequisite-method")
        binding = node("creation-prerequisite-binding")

        class OriginDevice:
            def __init__(self):
                self.reads = [[route], [route, method, binding]]
                self.swipes = 0
                self.captures: list[str] = []

            def hierarchy(self, **_kwargs):
                return self.reads.pop(0)

            def dismiss_system_ui_anr(self, _nodes, **_kwargs):
                return False

            def swipe_down(self, **_kwargs):
                self.swipes += 1

            def capture(self, name, **_kwargs):
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
        def node(selector: str) -> driver.shared.UiNode:
            return driver.shared.UiNode(
                {
                    "package": driver.shared.PACKAGE,
                    "resource-id": f"{driver.shared.PACKAGE}:id/{selector}",
                }
            )

        route = node("creation-prerequisite-page")
        method = node("creation-prerequisite-method")

        class AmbiguousOriginDevice:
            def __init__(self):
                self.captures: list[str] = []

            def hierarchy(self, **_kwargs):
                return [route, route, method]

            def capture(self, name, **_kwargs):
                self.captures.append(name)

        device = AmbiguousOriginDevice()
        with self.assertRaisesRegex(RuntimeError, "ambiguous"):
            driver.wait_for_prerequisite_scan_origin(device)
        self.assertEqual(
            ["creation-prerequisite-scan-origin-cardinality-invalid"],
            device.captures,
        )

    def test_prerequisite_scan_origin_uses_one_direct_read_for_exact_post_tap_lease_exhaustion(
        self,
    ) -> None:
        route = self.canonical_node("creation-prerequisite-page")
        method = self.canonical_node("creation-prerequisite-method")
        binding = self.canonical_node("creation-prerequisite-binding")
        lease_error = driver.shared.AdbHierarchyLeaseReserveExceeded(
            "owned-file retry and reconciliation reserve"
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.side_effect = lease_error
        device.read_only_hierarchy_once.return_value = [route, method, binding]
        observed_scans: list[dict[str, object]] = []

        origin = driver.wait_for_prerequisite_scan_origin(
            device,
            deadline=driver.time.monotonic() + 30,
            immediately_after_opening_tap=True,
            scan_observer=observed_scans.append,
        )

        self.assertEqual([route, method, binding], origin.nodes)
        self.assertEqual(0, origin.reverse_swipes)
        self.assertEqual(2, len(origin.hierarchy_durations_ms))
        self.assertEqual(0, origin.empty_hierarchy_reads)
        self.assertEqual(1, device.hierarchy.call_count)
        self.assertTrue(
            device.hierarchy.call_args.kwargs[
                "raise_on_lease_reserve_exhaustion"
            ]
        )
        device.read_only_hierarchy_once.assert_called_once()
        direct = device.read_only_hierarchy_once.call_args.kwargs
        self.assertGreater(direct["attempt_max_seconds"], 0)
        self.assertLessEqual(
            direct["attempt_max_seconds"],
            driver.shared.ADB_HIERARCHY_DUMP_DIRECT_RECONCILIATION_READ_ATTEMPT_MAX_SECONDS,
        )
        self.assertEqual(
            "creation-prerequisite-scan-origin-direct-invalid.xml",
            direct["diagnostic_name"],
        )
        self.assertEqual(1, len(observed_scans))
        self.assertEqual("resolved", observed_scans[0]["status"])
        self.assertEqual(
            "resolved-exact-top-origin",
            observed_scans[0]["directFallbackResult"],
        )
        self.assertEqual(1, observed_scans[0]["directFallbackReadCount"])
        self.assertEqual(1, observed_scans[0]["fileBackedObservationAttempts"])
        self.assertIs(True, observed_scans[0]["fileBackedLeaseReserveExhausted"])
        device.swipe_down.assert_not_called()
        device.shell.assert_not_called()

    def test_prerequisite_scan_origin_direct_fallback_rejects_lower_or_ambiguous_snapshot(
        self,
    ) -> None:
        route = self.canonical_node("creation-prerequisite-page")
        method = self.canonical_node("creation-prerequisite-method")
        binding = self.canonical_node("creation-prerequisite-binding")
        cases = (
            ("lower", [route, method], "did not expose the route and both"),
            ("ambiguous", [route, method, binding, binding], "was ambiguous"),
        )
        for name, direct_nodes, message in cases:
            with self.subTest(name=name):
                device = mock.Mock(spec=driver.shared.Device)
                device.hierarchy.side_effect = (
                    driver.shared.AdbHierarchyLeaseReserveExceeded("lease reserve")
                )
                device.read_only_hierarchy_once.return_value = direct_nodes
                observed_scans: list[dict[str, object]] = []

                with self.assertRaisesRegex(RuntimeError, message):
                    driver.wait_for_prerequisite_scan_origin(
                        device,
                        deadline=driver.time.monotonic() + 30,
                        immediately_after_opening_tap=True,
                        scan_observer=observed_scans.append,
                    )

                device.read_only_hierarchy_once.assert_called_once()
                device.swipe_down.assert_not_called()
                device.shell.assert_not_called()
                self.assertEqual(1, observed_scans[0]["directFallbackReadCount"])
                self.assertIn(
                    observed_scans[0]["status"],
                    {
                        "direct-fallback-origin-incomplete",
                        "direct-fallback-cardinality-invalid",
                    },
                )

    def test_prerequisite_scan_origin_direct_fallback_rejects_noncanonical_identity_or_read_failure(
        self,
    ) -> None:
        route = self.canonical_node("creation-prerequisite-page")
        method = self.canonical_node("creation-prerequisite-method")
        forged_binding = driver.shared.UiNode(
            {
                "package": "com.example.forged",
                "resource-id": (
                    f"{driver.shared.PACKAGE}:id/creation-prerequisite-binding"
                ),
            }
        )
        cases = (
            (
                "noncanonical",
                [route, method, forged_binding],
                "canonical Chummer resource identities",
            ),
            (
                "read-failure",
                subprocess.TimeoutExpired("adb", 1),
                "direct hierarchy observation failed",
            ),
        )
        for name, direct_result, message in cases:
            with self.subTest(name=name):
                device = mock.Mock(spec=driver.shared.Device)
                device.hierarchy.side_effect = (
                    driver.shared.AdbHierarchyLeaseReserveExceeded("lease reserve")
                )
                if isinstance(direct_result, BaseException):
                    device.read_only_hierarchy_once.side_effect = direct_result
                else:
                    device.read_only_hierarchy_once.return_value = direct_result

                with self.assertRaisesRegex(RuntimeError, message):
                    driver.wait_for_prerequisite_scan_origin(
                        device,
                        deadline=driver.time.monotonic() + 30,
                        immediately_after_opening_tap=True,
                        scan_observer=lambda _scan: None,
                    )

                device.read_only_hierarchy_once.assert_called_once()
                device.swipe_down.assert_not_called()
                device.shell.assert_not_called()

    def test_prerequisite_scan_origin_never_direct_falls_back_after_reverse_swipe(
        self,
    ) -> None:
        route = self.canonical_node("creation-prerequisite-page")
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.side_effect = (
            [route],
            driver.shared.AdbHierarchyLeaseReserveExceeded("lease reserve"),
        )
        device.dismiss_system_ui_anr.return_value = False
        observed_scans: list[dict[str, object]] = []

        with self.assertRaisesRegex(RuntimeError, "untouched post-opening viewport"):
            driver.wait_for_prerequisite_scan_origin(
                device,
                deadline=driver.time.monotonic() + 30,
                immediately_after_opening_tap=True,
                scan_observer=observed_scans.append,
            )

        device.swipe_down.assert_called_once()
        device.read_only_hierarchy_once.assert_not_called()
        device.shell.assert_not_called()
        self.assertEqual(
            "lease-reserve-exhausted-ineligible",
            observed_scans[0]["status"],
        )
        self.assertEqual(1, observed_scans[0]["reverseSwipes"])

    def test_prerequisite_scan_origin_does_not_direct_fallback_for_an_unclassified_empty_read(
        self,
    ) -> None:
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = []

        with mock.patch.object(
            driver,
            "sleep_before_phase_deadline",
            side_effect=RuntimeError("empty hierarchy wait cannot fit"),
        ), self.assertRaisesRegex(RuntimeError, "empty hierarchy wait cannot fit"):
            driver.wait_for_prerequisite_scan_origin(
                device,
                deadline=driver.time.monotonic() + 30,
                immediately_after_opening_tap=True,
                scan_observer=lambda _scan: None,
            )

        device.read_only_hierarchy_once.assert_not_called()
        device.swipe_down.assert_not_called()
        device.shell.assert_not_called()

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
            deadline=None,
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

    @staticmethod
    def persisted_prerequisite_authority_nodes() -> list[driver.shared.UiNode]:
        snapshot = "sha256:" + "1" * 64
        authority = "sha256:" + "4" * 64
        values = {
            "creation-prerequisite-binding": (
                "Revision 7 · saved 7 · snapshot "
                + snapshot.removeprefix("sha256:")[:12]
                + " · authority "
                + authority.removeprefix("sha256:")[:12]
            ),
            "creation-prerequisite-method": "Priority",
            "creation-prerequisite-karma-budget": (
                "Global Creation Karma. Total 25. Used 0. Remaining 25."
            ),
            "creation-prerequisite-snapshot-digest": snapshot,
            "creation-prerequisite-raw-character-xml-digest": (
                "sha256:" + "2" * 64
            ),
            "creation-prerequisite-auxiliary-state-digest": "3" * 64,
            "creation-prerequisite-authority-digest": authority,
            "creation-prerequisite-pending-draft-digest": (
                "sha256:" + "5" * 64
            ),
            "creation-prerequisite-heritage-selection-id": "heritage:human",
            "creation-prerequisite-talent-selection-id": "talent:aspected",
            "creation-prerequisite-category-attributes": (
                "Attributes. Rank A. Raw normal Attribute grant: 24"
            ),
        }
        nodes = [
            driver.shared.UiNode(
                {
                    "package": driver.shared.PACKAGE,
                    "resource-id": f"{driver.shared.PACKAGE}:id/{selector}",
                    "content-desc": value,
                    "class": "android.view.View",
                }
            )
            for selector, value in values.items()
        ]
        for selector in (
            "creation-prerequisite-pending-draft",
            "creation-prerequisite-attributes-ready",
        ):
            nodes.append(
                driver.shared.UiNode(
                    {
                        "package": driver.shared.PACKAGE,
                        "resource-id": f"{driver.shared.PACKAGE}:id/{selector}",
                        "class": "android.view.View",
                    }
                )
            )
        for category in ("heritage", "talent"):
            nodes.append(
                driver.shared.UiNode(
                    {
                        "package": driver.shared.PACKAGE,
                        "resource-id": (
                            f"{driver.shared.PACKAGE}:id/"
                            f"creation-prerequisite-{category}-selection"
                        ),
                        "content-desc": f"{category} selected",
                        "class": "android.view.View",
                        "enabled": "true",
                        "clickable": "true",
                        "bounds": "[10,100][900,300]",
                    }
                )
            )
        return nodes

    def test_persisted_authority_scan_collects_one_deadline_bound_traversal(self) -> None:
        nodes = self.persisted_prerequisite_authority_nodes()
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = True
        origin = driver.PriorityRankOrigin(nodes[:3], 0, 1, (1,), 0)
        deadline = 1234.0
        observer = mock.Mock()
        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes], 6),
        ) as scan:
            proof = driver.scan_persisted_prerequisite_authority(
                device,
                initial_observation=origin,
                deadline=deadline,
                scan_observer=observer,
                scan_id="persisted-authority",
            )

        self.assertEqual(6, proof.swipes)
        self.assertEqual({"heritage": 0, "talent": 0}, proof.selection_viewports)
        self.assertEqual("present", proof.values["creation-prerequisite-pending-draft"])
        self.assertEqual("present", proof.values["creation-prerequisite-attributes-ready"])
        scan.assert_called_once_with(
            device,
            scan_id="persisted-authority",
            max_scrolls=22,
            distance_ratio=0.22,
            initial_observation=origin,
            delay_seconds=0.0,
            observer=observer,
            deadline=deadline,
            max_consecutive_empty_reads=3,
        )
        device.capture.assert_not_called()

        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes], 6),
        ) as exact_scan:
            driver.scan_persisted_prerequisite_authority(
                device,
                initial_observation=origin,
                deadline=deadline,
                scan_observer=observer,
                scan_id=driver.PROCESS_RESTART_PERSISTED_PREREQUISITE_SCAN_ID,
                max_consecutive_empty_reads=(
                    driver.PROCESS_RESTART_PERSISTED_PREREQUISITE_MAX_CONSECUTIVE_EMPTY_READS
                ),
            )
        self.assertEqual(
            driver.PROCESS_RESTART_PERSISTED_PREREQUISITE_MAX_CONSECUTIVE_EMPTY_READS,
            exact_scan.call_args.kwargs["max_consecutive_empty_reads"],
        )

        with mock.patch.object(
            driver,
            "scan_persisted_prerequisite_authority",
            return_value=driver.PersistedPrerequisiteAuthorityScanProof(
                proof.values,
                6,
                {"heritage": 0, "talent": 0},
            ),
        ) as persisted_scan:
            driver.read_persisted_prerequisite_authority(
                device,
                initial_observation=origin,
                deadline=deadline,
                scan_observer=observer,
                scan_id=driver.PROCESS_RESTART_PERSISTED_PREREQUISITE_SCAN_ID,
                max_consecutive_empty_reads=(
                    driver.PROCESS_RESTART_PERSISTED_PREREQUISITE_MAX_CONSECUTIVE_EMPTY_READS
                ),
            )
        self.assertEqual(
            driver.PROCESS_RESTART_PERSISTED_PREREQUISITE_MAX_CONSECUTIVE_EMPTY_READS,
            persisted_scan.call_args.kwargs["max_consecutive_empty_reads"],
        )

    def test_persisted_authority_scan_skips_clipped_selection_until_tappable_viewport(self) -> None:
        nodes = self.persisted_prerequisite_authority_nodes()
        later_nodes = [
            driver.shared.UiNode(dict(node.attributes))
            for node in nodes
        ]
        device = mock.Mock()
        # The first overlap viewport sees both selection rows clipped; the
        # later viewport exposes the same semantic rows with tappable bounds.
        device.node_has_tappable_bounds.side_effect = (False, False, True, True)
        origin = driver.PriorityRankOrigin(nodes[:3], 0, 1, (1,), 0)
        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes, later_nodes], 6),
        ):
            proof = driver.scan_persisted_prerequisite_authority(
                device,
                initial_observation=origin,
                deadline=1234.0,
                scan_observer=None,
                scan_id="persisted-clipped-selection",
            )

        self.assertEqual({"heritage": 1, "talent": 1}, proof.selection_viewports)
        device.capture.assert_not_called()

    def test_persisted_authority_scan_fails_at_end_when_selection_never_tappable(self) -> None:
        nodes = self.persisted_prerequisite_authority_nodes()
        device = mock.Mock()
        device.node_has_tappable_bounds.return_value = False
        origin = driver.PriorityRankOrigin(nodes[:3], 0, 1, (1,), 0)
        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes], 6),
        ), self.assertRaisesRegex(RuntimeError, "navigation"):
            driver.scan_persisted_prerequisite_authority(
                device,
                initial_observation=origin,
                deadline=1234.0,
                scan_observer=None,
                scan_id="persisted-never-tappable-selection",
            )
        self.assertNotIn(
            mock.call("persisted-never-tappable-selection-heritage-selection-not-tappable", deadline=1234.0),
            device.capture.call_args_list,
        )

    def test_persisted_authority_scan_rejects_missing_duplicate_drift_and_bad_identity(
        self,
    ) -> None:
        base = self.persisted_prerequisite_authority_nodes()
        cases: list[tuple[str, list[list[driver.shared.UiNode]], str]] = []

        missing = [
            node
            for node in base
            if driver._exact_resource_id(node)
            != "creation-prerequisite-pending-draft-digest"
        ]
        cases.append(("missing", [missing], "incomplete or changed"))

        duplicate = [*base, base[-1]]
        cases.append(("duplicate", [duplicate], "cardinality"))

        changed_method = driver.shared.UiNode(dict(base[1].attributes))
        changed_method.attributes["content-desc"] = "SumToTen"
        cases.append(("drift", [base, [changed_method]], "incomplete or changed"))

        wrong_package = [driver.shared.UiNode(dict(node.attributes)) for node in base]
        wrong_package[0].attributes["package"] = "com.example.forged"
        cases.append(("wrong-package", [wrong_package], "canonical Chummer"))

        malformed_aux = [driver.shared.UiNode(dict(node.attributes)) for node in base]
        next(
            node
            for node in malformed_aux
            if driver._exact_resource_id(node)
            == "creation-prerequisite-auxiliary-state-digest"
        ).attributes["content-desc"] = "not-a-digest"
        cases.append(("malformed-digest", [malformed_aux], "not canonical"))

        for name, screens, expected in cases:
            with self.subTest(name=name):
                device = mock.Mock()
                device.node_has_tappable_bounds.return_value = True
                with mock.patch.object(
                    driver,
                    "scan_forward_with_receipt",
                    return_value=driver.StableViewportScan(screens, 6),
                ), self.assertRaisesRegex(RuntimeError, expected):
                    driver.scan_persisted_prerequisite_authority(
                        device,
                        initial_observation=driver.PriorityRankOrigin(
                            base[:3], 0, 1, (1,), 0
                        ),
                        deadline=1234.0,
                        scan_observer=None,
                        scan_id=f"persisted-{name}",
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
            "prerequisite_origin = wait_for_prerequisite_scan_origin(",
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
            "talent-active-skill-preservation",
            "talent-active-skill-reset",
            "talent-active-skill-reselection",
            "talent-active-grant-completion",
            "talent-active-preview",
            "talent-skill-group-grant",
            "talent-skill-group-preservation",
            "talent-skill-group-reset",
            "talent-skill-group-reselection",
            "talent-skill-group-grant-completion",
            "preview-confirm",
        )
        for phase_id in tuple(
            phase_id
            for phase_id in phase_ids
            if phase_id
            not in {
                "talent-active-skill-grant",
                "talent-active-grant-completion",
                "talent-skill-group-grant",
                "talent-skill-group-grant-completion",
                "preview-confirm",
            }
        ):
            with self.subTest(phase_id=phase_id):
                self.assertEqual(150_000, driver.PHASE_BUDGET_MS[phase_id])
        self.assertEqual(
            180_000,
            driver.PHASE_BUDGET_MS["talent-active-skill-grant"],
        )
        self.assertEqual(
            180_000,
            driver.PHASE_BUDGET_MS["talent-active-grant-completion"],
        )
        self.assertEqual(
            180_000,
            driver.PHASE_BUDGET_MS["talent-skill-group-grant"],
        )
        self.assertEqual(
            180_000,
            driver.PHASE_BUDGET_MS["talent-skill-group-grant-completion"],
        )
        self.assertEqual(360_000, driver.PHASE_BUDGET_MS["preview-confirm"])

        typed = source.index('progress.advance("typed-authority-options")')
        active = source.index('progress.advance("talent-active-skill-grant")')
        active_preservation = source.index(
            'progress.advance("talent-active-skill-preservation")'
        )
        active_reset = source.index('progress.advance("talent-active-skill-reset")')
        active_reselection = source.index(
            'progress.advance("talent-active-skill-reselection")'
        )
        active_completion = source.index(
            'progress.advance("talent-active-grant-completion")'
        )
        active_preview = source.index('progress.advance("talent-active-preview")')
        skill_group_selection = source.index(
            'progress.advance("talent-skill-group-selection")'
        )
        skill_group = source.index('progress.advance("talent-skill-group-grant")')
        skill_group_preservation = source.index(
            'progress.advance("talent-skill-group-preservation")'
        )
        skill_group_reset = source.index(
            'progress.advance("talent-skill-group-reset")'
        )
        skill_group_reselection = source.index(
            'progress.advance("talent-skill-group-reselection")'
        )
        skill_group_completion = source.index(
            'progress.advance("talent-skill-group-grant-completion")'
        )
        final_preview = source.index('progress.advance("preview-confirm")')
        self.assertEqual(
            [
                typed,
                active,
                active_preservation,
                active_reset,
                active_reselection,
                active_completion,
                active_preview,
                skill_group_selection,
                skill_group,
                skill_group_preservation,
                skill_group_reset,
                skill_group_reselection,
                skill_group_completion,
                final_preview,
            ],
            sorted(
                (
                    typed,
                    active,
                    active_preservation,
                    active_reset,
                    active_reselection,
                    active_completion,
                    active_preview,
                    skill_group_selection,
                    skill_group,
                    skill_group_preservation,
                    skill_group_reset,
                    skill_group_reselection,
                    skill_group_completion,
                    final_preview,
                )
            ),
        )
        active_choice = source.index("active_grant_proof = choose_and_prove_talent_grant(")
        active_deadline = source.index(
            "active_grant_completion_deadline = progress.active_phase_deadline("
        )
        active_close = source.index("complete_talent_grant_to_prerequisite(")
        selection_reacquisition = source.index(
            "active_talent_selection_node = device.wait_exact_resource_id_bidirectional("
        )
        selection_read = source.index("active_talent_selection_id = (")
        selection_guard = source.index("if not active_talent_selection_id:")
        self.assertLess(active_choice, active_completion)
        self.assertLess(active_completion, active_deadline)
        self.assertLess(active_deadline, active_close)
        self.assertLess(active_close, selection_reacquisition)
        self.assertLess(selection_reacquisition, selection_read)
        self.assertLess(selection_read, selection_guard)
        self.assertLess(selection_guard, active_preview)
        active_selection_reacquisition = source[
            selection_reacquisition:selection_read
        ]
        active_deadline_block = source[active_deadline:selection_read]
        self.assertEqual(
            2,
            active_deadline_block.count("deadline=active_grant_completion_deadline"),
        )
        for required_authority in (
            '"creation-prerequisite-talent-selection-id"',
            "timeout=60",
            "backward_scrolls=22",
            "forward_scrolls=22",
            "scroll_distance_ratio=0.22",
            'evidence_prefix="creation-prerequisite-active-talent-selection-id"',
            'surface_name="Active-skill Talent SelectionId authority"',
            "require_tappable=False",
            "deadline=active_grant_completion_deadline",
        ):
            self.assertIn(required_authority, active_selection_reacquisition)
        self.assertNotIn("node_text(", active_selection_reacquisition)
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
        preview_back = source.index(
            'evidence_prefix="talent-active-skill-preview-back"'
        )
        exact_selection_open = source.index(
            "open_talent_selection_after_preview(device)"
        )
        self.assertLess(preview_back, skill_group_selection)
        self.assertLess(skill_group_selection, exact_selection_open)
        self.assertLess(
            exact_selection_open,
            source.index('typed_selections["talent"] = tap_enabled_authority_option('),
        )
        self.assertLess(
            source.index('typed_selections["talent"] = tap_enabled_authority_option('),
            skill_group,
        )
        self.assertLess(
            skill_group,
            source.index("skill_group_grant_proof = choose_and_prove_talent_grant("),
        )
        skill_group_choice_block = source[
            source.index("skill_group_grant_proof = choose_and_prove_talent_grant("):
            skill_group_completion
        ]
        for continuation_phase in (
            "talent-skill-group-preservation",
            "talent-skill-group-reset",
            "talent-skill-group-reselection",
        ):
            with self.subTest(continuation_phase=continuation_phase):
                self.assertEqual(
                    1,
                    skill_group_choice_block.count(
                        f'lambda: progress.advance("{continuation_phase}")'
                    ),
                )
        skill_group_completion_deadline = source.index(
            "skill_group_grant_completion_deadline = progress.active_phase_deadline("
        )
        skill_group_close = source.index(
            "complete_talent_grant_to_prerequisite(",
            skill_group_completion,
        )
        skill_group_current_viewport = source.index(
            "skill_group_grant_proof.current_viewport",
            skill_group_close,
        )
        skill_group_selection_read = source.index(
            'typed_selection_ids["talent"] = read_exact_skill_group_talent_selection_id('
        )
        self.assertLess(skill_group_completion, skill_group_completion_deadline)
        self.assertLess(skill_group_completion_deadline, skill_group_close)
        self.assertLess(skill_group_close, skill_group_current_viewport)
        self.assertLess(skill_group_current_viewport, skill_group_selection_read)
        self.assertLess(skill_group_selection_read, final_preview)
        self.assertLess(
            skill_group_completion,
            final_preview,
        )
        self.assertLess(final_preview, source.index("skill_group_plan_digest ="))
        self.assertEqual(45 * 60 * 1000, driver.TOTAL_PERFORMANCE_TARGET_MS)
        self.assertEqual(
            (len(driver.PHASE_ORDER) + 1) // 2,
            driver.TIMING_ROUNDING_TOLERANCE_MS,
        )

    def test_skill_group_post_grant_selection_id_is_one_deadline_bound_exact_read(
        self,
    ) -> None:
        source = inspect.getsource(driver.execute)
        start = source.index(
            "skill_group_grant_completion_deadline = progress.active_phase_deadline("
        )
        end = source.index(
            'if (\n        not typed_selection_ids["talent"]',
            start,
        )
        block = source[start:end]

        self.assertEqual(
            180_000,
            driver.PHASE_BUDGET_MS["talent-skill-group-grant-completion"],
        )
        self.assertIn('"talent-skill-group-grant-completion"', block)
        self.assertEqual(1, block.count("complete_talent_grant_to_prerequisite("))
        self.assertEqual(
            1,
            block.count("read_exact_skill_group_talent_selection_id("),
        )
        self.assertEqual(
            2,
            block.count("deadline=skill_group_grant_completion_deadline"),
        )
        self.assertNotIn("node_text(", block)
        self.assertNotIn("device.wait(", block)
        self.assertNotIn("device.wait_for_single_exact_resource_id(", block)
        self.assertNotIn("device.tap(", block)
        self.assertNotIn("device.shell(", block)

    def test_skill_group_selection_id_read_delegates_one_exact_read_only_scan(
        self,
    ) -> None:
        deadline = 321.5
        device = mock.Mock()
        device.wait_exact_resource_id_bidirectional.return_value = (
            driver.shared.UiNode(
                {
                    "resource-id": (
                        "com.myexternalbrain.chummer:id/"
                        "creation-prerequisite-talent-selection-id"
                    ),
                    "text": "774fd89a-ecea-4f5f-ad2b-c4476ca46f70:talent:3",
                    "bounds": "[103,420][979,473]",
                }
            )
        )

        actual = driver.read_exact_skill_group_talent_selection_id(
            device,
            deadline=deadline,
        )

        self.assertEqual(
            "774fd89a-ecea-4f5f-ad2b-c4476ca46f70:talent:3",
            actual,
        )
        device.wait_exact_resource_id_bidirectional.assert_called_once_with(
            "creation-prerequisite-talent-selection-id",
            timeout=60,
            backward_scrolls=22,
            forward_scrolls=22,
            scroll_distance_ratio=0.22,
            evidence_prefix=(
                "creation-prerequisite-skill-group-talent-selection-id"
            ),
            surface_name="Skill-group Talent SelectionId authority",
            require_tappable=False,
            deadline=deadline,
        )
        device.wait_for_single_exact_resource_id.assert_not_called()
        device.wait.assert_not_called()
        device.tap.assert_not_called()
        device.shell.assert_not_called()

    def test_skill_group_selection_id_read_fails_closed_without_fallback_or_action(
        self,
    ) -> None:
        failures = (
            RuntimeError("exact SelectionId cardinality 2"),
            RuntimeError("exact SelectionId unavailable after prefix decoy"),
            driver.shared.AdbOperationDeadlineExceeded(
                "phase deadline expired before reverse acquisition"
            ),
        )
        for failure in failures:
            with self.subTest(failure=str(failure)):
                device = mock.Mock()
                device.wait_exact_resource_id_bidirectional.side_effect = failure
                with self.assertRaises(type(failure)) as raised:
                    driver.read_exact_skill_group_talent_selection_id(
                        device,
                        deadline=9.0,
                    )
                self.assertIs(failure, raised.exception)
                device.wait_exact_resource_id_bidirectional.assert_called_once()
                device.wait_for_single_exact_resource_id.assert_not_called()
                device.wait.assert_not_called()
                device.tap.assert_not_called()
                device.shell.assert_not_called()

        blank = mock.Mock()
        blank.wait_exact_resource_id_bidirectional.return_value = (
            driver.shared.UiNode(
                {
                    "resource-id": (
                        "com.myexternalbrain.chummer:id/"
                        "creation-prerequisite-talent-selection-id"
                    ),
                    "text": "   ",
                    "content-desc": "",
                    "bounds": "[103,420][979,473]",
                }
            )
        )
        with self.assertRaisesRegex(RuntimeError, "did not expose an exact value"):
            driver.read_exact_skill_group_talent_selection_id(
                blank,
                deadline=9.0,
            )
        blank.wait_exact_resource_id_bidirectional.assert_called_once()
        blank.wait_for_single_exact_resource_id.assert_not_called()
        blank.wait.assert_not_called()
        blank.tap.assert_not_called()
        blank.shell.assert_not_called()

    def test_preview_confirm_delegates_one_exact_deadline_bound_attributes_round_trip(
        self,
    ) -> None:
        source = inspect.getsource(driver.execute)
        start = source.index('progress.advance("preview-confirm")')
        end = source.index("preview_proof: dict[str, object] = {}", start)
        block = source[start:end]
        phase_end = source.index('progress.advance("same-process-reopen")', start)
        phase_block = source[start:phase_end]

        self.assertEqual(360_000, driver.PHASE_BUDGET_MS["preview-confirm"])
        self.assertEqual(
            1,
            block.count(
                'preview_confirm_deadline = progress.active_phase_deadline("preview-confirm")'
            ),
        )
        self.assertEqual(
            1,
            block.count(
                "attributes_before = require_exact_attributes_category_round_trip("
            ),
        )
        self.assertEqual(1, phase_block.count("tap_exact_current_preview_confirm("))
        self.assertEqual(1, phase_block.count("read_exact_confirmed_receipt("))
        self.assertEqual(2, block.count("deadline=preview_confirm_deadline"))
        for forbidden in (
            "node_text(",
            "device.tap(",
            "device.wait(",
            "device.back(",
        ):
            self.assertNotIn(forbidden, block)

        round_trip = inspect.getsource(
            driver.require_exact_attributes_category_round_trip
        )
        self.assertEqual(
            1,
            round_trip.count("acquire_exact_attributes_category_authority("),
        )
        self.assertEqual(1, round_trip.count("require_exact_zero_gesture_route("))
        self.assertEqual(1, round_trip.count("device.back(deadline=deadline)"))
        self.assertEqual(
            1,
            round_trip.count("require_exact_attributes_post_back_observation("),
        )
        self.assertEqual(1, round_trip.count("device.shell("))
        for forbidden in (
            "node_text(",
            "device.tap(",
            "device.wait(",
        ):
            self.assertNotIn(forbidden, round_trip)

    def test_preview_confirm_uses_one_360_second_phase_slo(
        self,
    ) -> None:
        source = inspect.getsource(driver.execute)
        confirm = source.index('progress.advance("preview-confirm")')
        receipt_validation = source.index(
            'if confirmed_revisions["contentRevision"] <= 0'
        )
        back = source.index("dashboard_deadline = tap_exact_confirmed_receipt_back(")
        self.assertEqual(360_000, driver.PHASE_BUDGET_MS["preview-confirm"])
        self.assertLess(confirm, receipt_validation)
        self.assertLess(receipt_validation, back)
        post_receipt = source.index(
            'receipt_text = str(confirmed_receipt["receiptText"])'
        )
        self.assertNotIn("device.", source[post_receipt:back])
        self.assertNotIn("preview-authority", driver.PHASE_BUDGET_MS)
        self.assertNotIn("post-confirm-dashboard", driver.PHASE_BUDGET_MS)
        self.assertEqual(3.0, driver.PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS)
        self.assertEqual(90.0, driver.CONFIRMED_STATE_TRANSITION_TIMEOUT_SECONDS)
        self.assertEqual(
            15.0,
            driver.CONFIRMED_RECEIPT_BACK_ORIGIN_TIMEOUT_SECONDS,
        )
        self.assertEqual(
            60.0,
            driver.CONFIRMED_RECEIPT_TRAVERSAL_RESERVE_SECONDS,
        )
        self.assertEqual(150.0, driver.CONFIRMED_RECEIPT_PROOF_TIMEOUT_SECONDS)
        self.assertEqual(
            driver.CONFIRMED_STATE_TRANSITION_TIMEOUT_SECONDS
            + driver.CONFIRMED_RECEIPT_TRAVERSAL_RESERVE_SECONDS,
            driver.CONFIRMED_RECEIPT_PROOF_TIMEOUT_SECONDS,
        )
        self.assertEqual(3.0, driver.PRE_BACK_ROUTE_LOG_CLEAR_TIMEOUT_SECONDS)
        self.assertEqual(30.0, driver.POST_CONFIRM_DASHBOARD_READY_TIMEOUT_SECONDS)
        self.assertEqual(
            5.0,
            driver.POST_CONFIRM_DASHBOARD_READY_READ_ATTEMPT_MAX_SECONDS,
        )
        self.assertEqual(
            0.25,
            driver.POST_CONFIRM_DASHBOARD_READY_POLL_DELAY_SECONDS,
        )
        self.assertEqual(
            30.0,
            driver.POST_CONFIRM_DASHBOARD_DUMP_ATTEMPT_MAX_SECONDS,
        )
        self.assertEqual(75.0, driver.POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS)
        self.assertEqual(
            231.0,
            driver.CONFIRM_DOWNSTREAM_RESERVE_SECONDS,
        )
        self.assertEqual(
            driver.CONFIRMED_RECEIPT_PROOF_TIMEOUT_SECONDS
            + driver.PRE_BACK_ROUTE_LOG_CLEAR_TIMEOUT_SECONDS
            + driver.PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
            + driver.POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS,
            driver.CONFIRM_DOWNSTREAM_RESERVE_SECONDS,
        )
        receipt_source = inspect.getsource(driver.read_exact_confirmed_receipt)
        self.assertIn(
            "timeout=CONFIRMED_STATE_TRANSITION_TIMEOUT_SECONDS",
            receipt_source,
        )
        self.assertIn("scroll=False", receipt_source)
        self.assertIn("max_scrolls=0", receipt_source)
        self.assertIn(
            "timeout=CONFIRMED_RECEIPT_BACK_ORIGIN_TIMEOUT_SECONDS",
            receipt_source,
        )
        self.assertEqual(2_700_000, driver.TOTAL_PERFORMANCE_TARGET_MS)

    def test_exact_attributes_round_trip_uses_one_observed_node_and_preserves_raw_bytes(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        authority = "Rank A · Attributes 24 "
        before = self.canonical_node(
            "creation-prerequisite-category-attributes",
            text=authority,
        )
        # The post-Back authority is deliberately noninteractive. The
        # fine read-only scan compares its bytes without authorizing a tap.
        after = self.canonical_node(
            "creation-prerequisite-category-attributes",
            text=authority,
            enabled="false",
            clickable="false",
        )
        category_route = self.canonical_node(
            "creation-prerequisite-category-page"
        )
        prerequisite_route = self.canonical_node("creation-prerequisite-page")
        disabled = self.canonical_node(
            "creation-prerequisite-attributes-disabled",
            enabled="false",
            clickable="false",
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [before]
        device.wait_for_single_exact_resource_id.return_value = category_route
        device.node_has_tappable_bounds.return_value = True
        device.dismiss_system_ui_anr.return_value = False

        with mock.patch.object(
            driver,
            "scan_forward_until_stable",
            return_value=[
                [prerequisite_route, after],
                [disabled],
                [disabled],
            ],
        ) as scan:
            actual = driver.require_exact_attributes_category_round_trip(
                device,
                deadline=deadline,
            )

        self.assertEqual(authority, actual)
        device.hierarchy.assert_called_once_with(deadline=deadline)
        device.swipe_down.assert_not_called()
        device.swipe_up.assert_not_called()
        device.node_has_tappable_bounds.assert_any_call(before, deadline=deadline)
        scan.assert_called_once_with(
            device,
            scan_id="creation-prerequisite-attributes-post-back",
            max_scrolls=12,
            distance_ratio=0.22,
            stable_repeats=2,
            max_consecutive_empty_reads=3,
            delay_seconds=0.0,
            deadline=deadline,
        )
        self.assertEqual(1, device.shell.call_count)
        self.assertEqual(
            ("input", "tap", "500", "400"),
            device.shell.call_args_list[0].args,
        )
        for call in device.shell.call_args_list:
            self.assertEqual(deadline, call.kwargs["deadline"])
        self.assertEqual(1, device.wait_for_single_exact_resource_id.call_count)
        for call in device.wait_for_single_exact_resource_id.call_args_list:
            self.assertIs(call.kwargs["scroll"], False)
            self.assertEqual(0, call.kwargs["max_scrolls"])
            self.assertEqual(deadline, call.kwargs["deadline"])
        device.tap.assert_not_called()
        device.wait.assert_not_called()
        device.back.assert_called_once_with(deadline=deadline)

    def test_exact_attributes_pre_tap_authority_fails_closed_without_action(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        exact = self.canonical_node("creation-prerequisite-category-attributes")
        failures = (
            ("cardinality", [exact, exact], RuntimeError),
            ("missing", [[], [], [], [], []], RuntimeError),
            (
                "deadline",
                driver.shared.AdbOperationDeadlineExceeded(
                    "phase deadline expired during Attributes acquisition"
                ),
                driver.shared.AdbOperationDeadlineExceeded,
            ),
        )
        for name, observations, error_type in failures:
            with self.subTest(failure=name):
                device = mock.Mock(spec=driver.shared.Device)
                if isinstance(observations, BaseException):
                    device.hierarchy.side_effect = observations
                elif observations and isinstance(observations[0], list):
                    device.hierarchy.side_effect = observations
                else:
                    device.hierarchy.return_value = observations
                with self.assertRaises(error_type):
                    driver.require_exact_attributes_category_round_trip(
                        device,
                        deadline=deadline,
                    )
                device.shell.assert_not_called()
                device.wait_for_single_exact_resource_id.assert_not_called()

        cases = (
            (
                "foreign-package-decoy",
                self.canonical_node(
                    "creation-prerequisite-category-attributes",
                    package="decoy.package",
                ),
                True,
                "canonical Chummer resource identity",
            ),
            (
                "disabled",
                self.canonical_node(
                    "creation-prerequisite-category-attributes",
                    enabled="false",
                ),
                True,
                "not enabled and clickable",
            ),
            (
                "nonclickable",
                self.canonical_node(
                    "creation-prerequisite-category-attributes",
                    clickable="false",
                ),
                True,
                "not enabled and clickable",
            ),
            (
                "out-of-bounds",
                self.canonical_node(
                    "creation-prerequisite-category-attributes",
                    bounds="[100,-300][900,-100]",
                ),
                False,
                "not tappable within",
            ),
            (
                "blank",
                self.canonical_node(
                    "creation-prerequisite-category-attributes",
                    text="   ",
                    **{"content-desc": ""},
                ),
                True,
                "blank authority",
            ),
        )
        for name, node, tappable, expected in cases:
            with self.subTest(name=name):
                device = mock.Mock(spec=driver.shared.Device)
                device.hierarchy.return_value = [node]
                device.node_has_tappable_bounds.return_value = tappable
                with self.assertRaisesRegex(RuntimeError, expected):
                    driver.require_exact_attributes_category_round_trip(
                        device,
                        deadline=deadline,
                    )
                device.shell.assert_not_called()
                device.wait_for_single_exact_resource_id.assert_not_called()
                self.assertGreaterEqual(device.hierarchy.call_count, 1)
                for call in device.hierarchy.call_args_list:
                    self.assertEqual(deadline, call.kwargs["deadline"])

    def test_attributes_unknown_open_outcome_is_not_replayed_or_followed_by_navigation(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        node = self.canonical_node("creation-prerequisite-category-attributes")
        receipt = {
            "classification": "timeout-unknown-outcome",
            "commandPolicy": "non-replayable",
            "replay": {"performed": False, "suppressed": True},
        }
        error = driver.shared.AdbTransportError(
            receipt,
            Path("attributes-open-timeout-unknown-outcome.json"),
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [node]
        device.node_has_tappable_bounds.return_value = True
        device.shell.side_effect = error

        with self.assertRaises(driver.shared.AdbTransportError):
            driver.require_exact_attributes_category_round_trip(
                device,
                deadline=deadline,
            )

        device.shell.assert_called_once()
        device.wait_for_single_exact_resource_id.assert_not_called()
        device.hierarchy.assert_called_once_with(deadline=deadline)
        device.back.assert_not_called()

    def test_exact_attributes_routes_reject_decoys_without_later_mutation(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        attributes = self.canonical_node(
            "creation-prerequisite-category-attributes"
        )
        route_decoy = self.canonical_node(
            "creation-prerequisite-category-page",
            package="decoy.package",
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [attributes]
        device.wait_for_single_exact_resource_id.return_value = route_decoy
        device.node_has_tappable_bounds.return_value = True

        with self.assertRaisesRegex(RuntimeError, "canonical Chummer resource identity"):
            driver.require_exact_attributes_category_round_trip(
                device,
                deadline=deadline,
            )

        device.shell.assert_called_once()
        device.hierarchy.assert_called_once_with(deadline=deadline)
        device.back.assert_not_called()

    def test_post_back_attributes_scan_rejects_duplicate_and_decoy_authority(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        route = self.canonical_node("creation-prerequisite-page")
        attributes = self.canonical_node(
            "creation-prerequisite-category-attributes",
            text="Rank A · Attributes 24",
        )
        disabled = self.canonical_node("creation-prerequisite-attributes-disabled")
        cases = (
            (
                "duplicate",
                [[route, attributes, attributes], [disabled], [disabled]],
                "cardinality 2",
            ),
            (
                "decoy",
                [[route, driver.shared.UiNode({
                    **attributes.attributes,
                    "package": "decoy.package",
                })], [disabled], [disabled]],
                "canonical Chummer resource identity",
            ),
        )
        for name, screens, expected in cases:
            with self.subTest(name=name):
                device = mock.Mock(spec=driver.shared.Device)
                with mock.patch.object(
                    driver,
                    "scan_forward_until_stable",
                    return_value=screens,
                ), self.assertRaisesRegex(RuntimeError, expected):
                    driver.require_exact_attributes_post_back_observation(
                        device,
                        "Rank A · Attributes 24",
                        deadline=deadline,
                    )
                device.shell.assert_not_called()
                device.back.assert_not_called()

    def test_attributes_post_back_read_is_read_only_and_byte_exact(self) -> None:
        deadline = driver.time.monotonic() + 30
        after = self.canonical_node(
            "creation-prerequisite-category-attributes",
            text="Rank A · Attributes 24 ",
            enabled="false",
            clickable="false",
        )
        route = self.canonical_node("creation-prerequisite-page")
        disabled = self.canonical_node(
            "creation-prerequisite-attributes-disabled",
            enabled="false",
            clickable="false",
        )
        device = mock.Mock(spec=driver.shared.Device)

        with mock.patch.object(
            driver,
            "scan_forward_until_stable",
            return_value=[[route, after], [disabled], [disabled]],
        ), self.assertRaisesRegex(RuntimeError, "byte-for-byte"):
            driver.require_exact_attributes_post_back_observation(
                device,
                "Rank A · Attributes 24",
                deadline=deadline,
            )

        device.shell.assert_not_called()
        device.wait_exact_resource_id_bidirectional.assert_not_called()
        device.back.assert_not_called()

    def test_bounds_deadline_expiry_stops_attributes_before_tap_route_or_back(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 30
        node = self.canonical_node("creation-prerequisite-category-attributes")
        error = driver.shared.AdbOperationDeadlineExceeded(
            "phase deadline expired during bounds validation"
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [node]
        device.node_has_tappable_bounds.side_effect = error

        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.require_exact_attributes_category_round_trip(
                device,
                deadline=deadline,
            )

        device.node_has_tappable_bounds.assert_called_once_with(
            node,
            deadline=deadline,
        )
        device.shell.assert_not_called()
        device.wait_for_single_exact_resource_id.assert_not_called()
        device.hierarchy.assert_called_once_with(deadline=deadline)
        device.back.assert_not_called()

    def test_persistent_action_lease_is_exact_and_never_clamps_permission(self) -> None:
        cases = (
            (
                "prepare-preview",
                driver.open_exact_prerequisite_preview,
                "proof_timeout_seconds=PREVIEW_ROUTE_PROOF_TIMEOUT_SECONDS",
                driver.PREVIEW_ROUTE_PROOF_TIMEOUT_SECONDS,
                78.0,
            ),
            (
                "confirm",
                driver.tap_exact_current_preview_confirm,
                "proof_timeout_seconds=CONFIRM_DOWNSTREAM_RESERVE_SECONDS",
                driver.CONFIRM_DOWNSTREAM_RESERVE_SECONDS,
                234.0,
            ),
            (
                "back",
                driver.tap_exact_confirmed_receipt_back,
                "proof_timeout_seconds=POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS",
                driver.POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS,
                78.0,
            ),
        )
        with mock.patch.object(driver.time, "monotonic", return_value=100.0):
            for label, function, binding, proof_seconds, required_seconds in cases:
                with self.subTest(label=label):
                    source = inspect.getsource(function)
                    self.assertIn(
                        "action_timeout_seconds="
                        "PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS",
                        source,
                    )
                    self.assertIn(binding, source)
                    self.assertEqual(
                        driver.PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
                        + proof_seconds,
                        required_seconds,
                    )
                    with self.assertRaisesRegex(
                        RuntimeError,
                        "action-plus-proof lease",
                    ):
                        driver.persistent_action_deadline(
                            100.0 + required_seconds - 0.001,
                            action_timeout_seconds=(
                                driver.PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
                            ),
                            proof_timeout_seconds=proof_seconds,
                            operation=label,
                        )
                    self.assertEqual(
                        103.0,
                        driver.persistent_action_deadline(
                            100.0 + required_seconds,
                            action_timeout_seconds=(
                                driver.PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
                            ),
                            proof_timeout_seconds=proof_seconds,
                            operation=label,
                        ),
                    )
        for invalid in (float("nan"), float("inf"), 0.0, -1.0):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                driver.persistent_action_deadline(
                    200.0,
                    action_timeout_seconds=invalid,
                    proof_timeout_seconds=60.0,
                    operation="Confirm",
                )

    def test_run_334524_preview_checkpoint_fits_only_the_360_second_phase_cap(
        self,
    ) -> None:
        observed_phase_elapsed_seconds = 99.052
        with mock.patch.object(
            driver.time,
            "monotonic",
            return_value=observed_phase_elapsed_seconds,
        ):
            with self.assertRaisesRegex(RuntimeError, "action-plus-proof lease"):
                driver.persistent_action_deadline(
                    330.0,
                    action_timeout_seconds=(
                        driver.PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
                    ),
                    proof_timeout_seconds=driver.CONFIRM_DOWNSTREAM_RESERVE_SECONDS,
                    operation="run 334524 Preview Confirm",
                )
            self.assertAlmostEqual(
                102.052,
                driver.persistent_action_deadline(
                    360.0,
                    action_timeout_seconds=(
                        driver.PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
                    ),
                    proof_timeout_seconds=driver.CONFIRM_DOWNSTREAM_RESERVE_SECONDS,
                    operation="run 334524 Preview Confirm",
                ),
            )

    def test_attributes_current_first_recovers_artifact_clipped_row_in_one_reverse(self) -> None:
        deadline = driver.time.monotonic() + 30
        clipped = self.canonical_node(
            "creation-prerequisite-category-attributes",
            text="Rank A · Attributes 24",
            bounds="[100,-270][900,-70]",
        )
        visible = self.canonical_node(
            "creation-prerequisite-category-attributes",
            text="Rank A · Attributes 24",
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.side_effect = ([clipped], [visible])
        device.node_has_tappable_bounds.side_effect = (False, True)
        with mock.patch.object(driver, "sleep_before_phase_deadline"):
            node, authority = driver.acquire_exact_attributes_category_authority(
                device,
                evidence_prefix="artifact-clipped-attributes",
                deadline=deadline,
            )
        self.assertIs(visible, node)
        self.assertEqual("Rank A · Attributes 24", authority)
        device.swipe_down.assert_called_once_with(
            distance_ratio=0.22,
            deadline=deadline,
        )
        device.swipe_up.assert_not_called()
        device.shell.assert_not_called()

    def test_prepare_preview_unknown_outcome_is_never_replayed_or_proved(self) -> None:
        node = self.canonical_node("creation-prerequisite-prepare-preview")
        device = mock.Mock(spec=driver.shared.Device)
        device.wait_exact_resource_id_bidirectional.return_value = node
        device.node_has_tappable_bounds.return_value = True
        error = driver.shared.AdbTransportError(
            {
                "classification": "timeout-unknown-outcome",
                "commandPolicy": "non-replayable",
                "replay": {"performed": False, "suppressed": True},
            },
            Path("prepare-preview-unknown.json"),
        )
        device.shell.side_effect = error
        with self.assertRaises(driver.shared.AdbTransportError):
            driver.open_exact_prerequisite_preview(
                device,
                deadline=driver.time.monotonic() + 90,
            )
        device.shell.assert_called_once()
        device.wait_for_single_exact_resource_id.assert_not_called()

    def test_confirm_fresh_authority_unknown_outcome_is_never_replayed(self) -> None:
        retained = self.canonical_node(
            "creation-prerequisite-confirm",
            text="Confirm exact preview",
        )
        current = self.canonical_node(
            "creation-prerequisite-confirm",
            text="Confirm exact preview",
        )
        proof = {
            "confirmNode": retained,
            "confirmAuthority": "Confirm exact preview",
            "confirmViewport": 4,
            "terminalViewport": 4,
        }
        device = mock.Mock(spec=driver.shared.Device)
        device.wait_for_single_exact_resource_id.return_value = current
        device.node_has_tappable_bounds.return_value = True
        error = driver.shared.AdbTransportError(
            {
                "classification": "timeout-unknown-outcome",
                "commandPolicy": "non-replayable",
                "replay": {"performed": False, "suppressed": True},
            },
            Path("preview-confirm-unknown.json"),
        )
        device.shell.side_effect = error
        with self.assertRaises(driver.shared.AdbTransportError):
            driver.tap_exact_current_preview_confirm(
                device,
                proof,
                deadline=driver.time.monotonic() + 300,
            )
        device.shell.assert_called_once()
        device.wait_for_single_exact_resource_id.assert_called_once()

    def test_confirmed_receipt_waits_read_only_for_transition_before_traversal(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 200
        transition_timeout = RuntimeError(
            "confirmed state transition remained unavailable"
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.wait_for_single_exact_resource_id.side_effect = transition_timeout
        preview_proof = {
            "immutableAuthorities": {
                "creation-prerequisite-preview-binding": "Revision 1",
            },
            "immutableStates": {
                "creation-prerequisite-preview-binding": ("true", "false"),
            },
            "assignmentIds": (),
            "grantIds": (),
            "absentPreviewIds": (
                "creation-prerequisite-preview-sum-to-ten",
                "creation-prerequisite-preview-blockers",
                "creation-prerequisite-preview-attributes-disabled",
            ),
        }

        with self.assertRaises(RuntimeError) as raised:
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )

        self.assertIs(transition_timeout, raised.exception)
        device.wait_for_single_exact_resource_id.assert_called_once_with(
            "creation-prerequisite-confirmed",
            timeout=driver.CONFIRMED_STATE_TRANSITION_TIMEOUT_SECONDS,
            scroll=False,
            max_scrolls=0,
            evidence_prefix="creation-prerequisite-confirmed-receipt-transition",
            surface_name="Confirmed prerequisite state transition",
            deadline=deadline,
        )
        device.wait_exact_resource_id_bidirectional.assert_not_called()
        device.hierarchy.assert_not_called()
        device.shell.assert_not_called()

    def test_confirmed_receipt_delayed_transition_precedes_back_traversal(
        self,
    ) -> None:
        preview = self.canonical_node("creation-prerequisite-confirm")
        confirmed = self.canonical_node("creation-prerequisite-confirmed")
        observations = iter(([preview], [preview], [confirmed]))
        events: list[str] = []
        device = mock.Mock(spec=driver.shared.Device)

        def hierarchy(*, deadline: float | None = None) -> list[driver.shared.UiNode]:
            self.assertEqual(190.0, deadline)
            events.append("poll")
            return next(observations)

        def real_transition_wait(selector: str, **kwargs: object) -> driver.shared.UiNode:
            return driver.shared.Device.wait_for_single_exact_resource_id(
                device,
                selector,
                **kwargs,
            )

        back_blocker = RuntimeError("ordered Back traversal marker")

        def begin_back(*args: object, **kwargs: object) -> driver.shared.UiNode:
            self.assertEqual(["poll", "poll", "poll"], events)
            events.append("back")
            raise back_blocker

        device.hierarchy.side_effect = hierarchy
        device.dismiss_system_ui_anr.return_value = False
        device.wait_for_single_exact_resource_id.side_effect = real_transition_wait
        device.wait_exact_resource_id_bidirectional.side_effect = begin_back
        preview_proof = {
            "immutableAuthorities": {
                "creation-prerequisite-preview-binding": "Revision 1",
            },
            "immutableStates": {
                "creation-prerequisite-preview-binding": ("true", "false"),
            },
            "assignmentIds": (),
            "grantIds": (),
            "absentPreviewIds": (
                "creation-prerequisite-preview-sum-to-ten",
                "creation-prerequisite-preview-blockers",
                "creation-prerequisite-preview-attributes-disabled",
            ),
        }

        with mock.patch.object(
            driver.shared.time,
            "monotonic",
            return_value=100.0,
        ), mock.patch.object(driver.shared.time, "sleep") as sleep:
            with self.assertRaises(RuntimeError) as raised:
                driver.read_exact_confirmed_receipt(
                    device,
                    preview_proof=preview_proof,
                    scan_observer=None,
                    deadline=250.0,
                )

        self.assertIs(back_blocker, raised.exception)
        self.assertEqual(["poll", "poll", "poll", "back"], events)
        self.assertEqual(3, device.hierarchy.call_count)
        self.assertEqual(2, sleep.call_count)
        device.swipe_up.assert_not_called()
        device.swipe_down.assert_not_called()
        device.shell.assert_not_called()

    def test_confirmed_receipt_allows_immutable_preview_evidence_but_rejects_confirm_action(
        self,
    ) -> None:
        sha_a = "sha256:" + ("a" * 64)
        sha_b = "sha256:" + ("b" * 64)
        aux = "c" * 64
        required = [
            self.canonical_node("creation-prerequisite-preview-page"),
            self.canonical_node("creation-prerequisite-confirmed"),
            self.canonical_node(
                "creation-prerequisite-confirm-receipt",
                text="",
                **{"content-desc": "Character document changed false"},
            ),
            self.canonical_node(
                "creation-prerequisite-receipt-content-revision", text="2"
            ),
            self.canonical_node(
                "creation-prerequisite-receipt-saved-revision", text="2"
            ),
            self.canonical_node(
                "creation-prerequisite-receipt-draft-revision", text="1"
            ),
            self.canonical_node(
                "creation-prerequisite-receipt-draft-digest", text=sha_b
            ),
            self.canonical_node(
                "creation-prerequisite-receipt-raw-character-xml-digest", text=sha_a
            ),
            self.canonical_node(
                "creation-prerequisite-receipt-auxiliary-state-digest", text=aux
            ),
            self.canonical_node(
                "creation-prerequisite-receipt-authority-digest", text=sha_a
            ),
            self.canonical_node(
                "creation-prerequisite-back-to-build", text="Back to build"
            ),
            # The native confirmed page intentionally retains immutable Preview
            # evidence. It is evidence, not a stale actionable Confirm control.
            self.canonical_node(
                "creation-prerequisite-preview-digest", text=sha_a
            ),
            self.canonical_node(
                "creation-prerequisite-preview-binding",
                text="Revision 1 · saved 0 · preview abcdef012345",
            ),
            self.canonical_node(
                "creation-prerequisite-preview-assignment-heritage",
                text="Heritage assignment",
            ),
        ]
        device = mock.Mock(spec=driver.shared.Device)
        deadline = driver.time.monotonic() + 30
        confirmed_node = next(
            node
            for node in required
            if node.attributes["resource-id"].endswith(
                "/creation-prerequisite-confirmed"
            )
        )
        device.wait_for_single_exact_resource_id.return_value = confirmed_node
        device.wait_exact_resource_id_bidirectional.return_value = next(
            node
            for node in required
            if node.attributes["resource-id"].endswith(
                "/creation-prerequisite-back-to-build"
            )
        )
        device.hierarchy.return_value = required
        immutable_ids = (
            "creation-prerequisite-preview-page",
            "creation-prerequisite-preview-binding",
            "creation-prerequisite-preview-digest",
            "creation-prerequisite-preview-assignment-heritage",
        )
        immutable_nodes = {
            node.attributes["resource-id"].rsplit("/", 1)[-1]: node
            for node in required
            if node.attributes["resource-id"].rsplit("/", 1)[-1]
            in immutable_ids
        }
        preview_proof = {
            "immutableAuthorities": {
                selector: (
                    immutable_nodes[selector].attributes.get("text")
                    or immutable_nodes[selector].attributes.get("content-desc")
                    or ""
                )
                for selector in immutable_ids
            },
            "immutableStates": {
                selector: (
                    immutable_nodes[selector].attributes.get("enabled", ""),
                    immutable_nodes[selector].attributes.get("clickable", ""),
                )
                for selector in immutable_ids
            },
            "assignmentIds": (
                "creation-prerequisite-preview-assignment-heritage",
            ),
            "grantIds": (),
            "absentPreviewIds": (
                "creation-prerequisite-preview-sum-to-ten",
                "creation-prerequisite-preview-blockers",
                "creation-prerequisite-preview-attributes-disabled",
            ),
        }
        receipt = driver.read_exact_confirmed_receipt(
            device,
            preview_proof=preview_proof,
            scan_observer=None,
            deadline=deadline,
        )
        self.assertEqual(sha_b, receipt["draftDigest"])
        self.assertEqual(0, receipt["backRecoveryMaxForwardScrolls"])
        stale = [*required, self.canonical_node("creation-prerequisite-confirm")]
        device.hierarchy.return_value = stale
        with self.assertRaisesRegex(RuntimeError, "stale"):
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )
        unknown = [
            *required,
            self.canonical_node(
                "creation-prerequisite-preview-assignment-unknown"
            ),
        ]
        device.hierarchy.return_value = unknown
        with self.assertRaisesRegex(RuntimeError, "unknownPreviewIds"):
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )
        conditional = [
            *required,
            self.canonical_node("creation-prerequisite-preview-blockers"),
        ]
        device.hierarchy.return_value = conditional
        with self.assertRaisesRegex(RuntimeError, "unexpectedConditional"):
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )
        drifted = [
            self.canonical_node(
                "creation-prerequisite-preview-digest",
                text="sha256:" + ("d" * 64),
            )
            if node.attributes["resource-id"].endswith(
                "/creation-prerequisite-preview-digest"
            )
            else node
            for node in required
        ]
        device.hierarchy.return_value = drifted
        with self.assertRaisesRegex(RuntimeError, "immutableDrift"):
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )

        assignment = immutable_nodes[
            "creation-prerequisite-preview-assignment-heritage"
        ]
        authority_digest = next(
            node
            for node in required
            if node.attributes["resource-id"].endswith(
                "/creation-prerequisite-receipt-authority-digest"
            )
        )
        bottom = [
            node
            for node in required
            if node is not assignment and node is not authority_digest
        ]
        device.hierarchy.side_effect = (
            bottom,
            [assignment],
            [self.canonical_node("unrelated-receipt-spacer")],
            [assignment, authority_digest],
        )
        with self.assertRaisesRegex(RuntimeError, "reappeared"):
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )
        device.shell.assert_not_called()

        expired = driver.shared.AdbOperationDeadlineExceeded(
            "receipt proof deadline expired"
        )
        device.reset_mock(side_effect=True, return_value=True)
        device.wait_for_single_exact_resource_id.return_value = confirmed_node
        device.wait_exact_resource_id_bidirectional.side_effect = expired
        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )
        device.hierarchy.assert_not_called()
        device.swipe_down.assert_not_called()
        device.shell.assert_not_called()

        back_node = next(
            node
            for node in required
            if node.attributes["resource-id"].endswith(
                "/creation-prerequisite-back-to-build"
            )
        )
        device.reset_mock(side_effect=True, return_value=True)
        device.wait_for_single_exact_resource_id.return_value = confirmed_node
        device.wait_exact_resource_id_bidirectional.return_value = back_node
        device.hierarchy.return_value = bottom
        device.swipe_down.side_effect = expired
        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )
        device.swipe_down.assert_called_once()
        device.swipe_up.assert_not_called()
        device.shell.assert_not_called()

        device.reset_mock(side_effect=True, return_value=True)
        device.wait_for_single_exact_resource_id.return_value = confirmed_node
        device.wait_exact_resource_id_bidirectional.return_value = back_node
        device.hierarchy.side_effect = (
            bottom,
            [assignment, authority_digest],
        )
        device.swipe_up.side_effect = expired
        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.read_exact_confirmed_receipt(
                device,
                preview_proof=preview_proof,
                scan_observer=None,
                deadline=deadline,
            )
        device.swipe_up.assert_called_once()
        device.shell.assert_not_called()

        device.reset_mock(side_effect=True, return_value=True)
        device.hierarchy.side_effect = expired
        with self.assertRaises(driver.shared.AdbOperationDeadlineExceeded):
            driver.tap_exact_confirmed_receipt_back(
                device,
                {
                    "backViewport": 0,
                    "currentViewport": 0,
                    "backAuthority": "Back to build",
                    "backRecoveryMaxForwardScrolls": 0,
                },
                scan_observer=None,
                deadline=driver.time.monotonic() + 120,
            )
        device.shell.assert_not_called()

    def test_confirmed_receipt_back_reacquisition_uses_only_measured_forward_bound(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 120
        back = self.canonical_node(
            "creation-prerequisite-back-to-build",
            text="Back to build",
        )
        unrelated = self.canonical_node("receipt-restoration-spacer")
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.side_effect = ([unrelated], [back])
        device.dismiss_system_ui_anr.return_value = False
        device.node_has_tappable_bounds.return_value = True
        observer = mock.Mock()

        with mock.patch.object(driver.time, "sleep"):
            dashboard_deadline = driver.tap_exact_confirmed_receipt_back(
                device,
                {
                    "backViewport": 0,
                    "currentViewport": 0,
                    "backAuthority": "Back to build",
                    "backRecoveryMaxForwardScrolls": 1,
                },
                scan_observer=observer,
                deadline=deadline,
            )

        self.assertGreater(dashboard_deadline, driver.time.monotonic())
        self.assertEqual(2, device.hierarchy.call_count)
        device.swipe_up.assert_called_once()
        self.assertEqual(0.30, device.swipe_up.call_args.kwargs["distance_ratio"])
        self.assertLessEqual(device.swipe_up.call_args.kwargs["deadline"], deadline)
        device.shell.assert_called_once()
        self.assertEqual("input", device.shell.call_args.args[0])
        self.assertEqual("tap", device.shell.call_args.args[1])
        device.run.assert_called_once()
        self.assertEqual(
            driver.shared.ADB_CREATION_BOOTSTRAP_LOGCAT_CLEAR_ARGUMENTS,
            device.run.call_args.args,
        )
        method_names = [call[0] for call in device.method_calls]
        self.assertLess(method_names.index("run"), method_names.index("shell"))
        observer.assert_called_once()
        reacquisition = observer.call_args.args[0]
        self.assertEqual("resolved", reacquisition["status"])
        self.assertEqual(1, reacquisition["swipes"])
        self.assertEqual(1, reacquisition["configuredMaxScrolls"])
        self.assertGreater(reacquisition["maximumElapsedMs"], 0)
        self.assertLessEqual(reacquisition["maximumElapsedMs"], 39_000)
        self.assertEqual(81_000, reacquisition["downstreamReserveMs"])
        self.assertEqual(
            "forward-from-measured-restored-bottom",
            reacquisition["direction"],
        )

    def test_confirmed_receipt_back_reacquisition_exhaustion_never_taps(
        self,
    ) -> None:
        deadline = driver.time.monotonic() + 120
        unrelated = self.canonical_node("receipt-restoration-spacer")
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [unrelated]
        device.dismiss_system_ui_anr.return_value = False
        observer = mock.Mock()

        with self.assertRaisesRegex(RuntimeError, "scan-proven 0-swipe"):
            driver.tap_exact_confirmed_receipt_back(
                device,
                {
                    "backViewport": 0,
                    "currentViewport": 0,
                    "backAuthority": "Back to build",
                    "backRecoveryMaxForwardScrolls": 0,
                },
                scan_observer=observer,
                deadline=deadline,
            )

        device.swipe_up.assert_not_called()
        device.shell.assert_not_called()
        observer.assert_called_once()
        failure = observer.call_args.args[0]
        self.assertEqual("failed", failure["status"])
        self.assertEqual("RuntimeError", failure["failureReason"])
        self.assertEqual(0, failure["configuredMaxScrolls"])

    def test_pre_back_route_log_clear_ambiguity_prevents_the_one_shot_back(
        self,
    ) -> None:
        back = self.canonical_node(
            "creation-prerequisite-back-to-build",
            text="Back to build",
        )
        receipt = {
            "classification": "timeout-unknown-outcome",
            "commandPolicy": "non-replayable",
            "replay": {"performed": False, "suppressed": True},
        }
        transport = driver.shared.AdbTransportError(
            receipt,
            Path("pre-back-log-clear-timeout.json"),
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [back]
        device.node_has_tappable_bounds.return_value = True
        device.run.side_effect = transport

        with self.assertRaises(driver.shared.AdbTransportError):
            driver.tap_exact_confirmed_receipt_back(
                device,
                {
                    "backViewport": 0,
                    "currentViewport": 0,
                    "backAuthority": "Back to build",
                    "backRecoveryMaxForwardScrolls": 0,
                },
                scan_observer=None,
                deadline=driver.time.monotonic() + 130,
            )

        device.run.assert_called_once()
        self.assertEqual(
            driver.shared.ADB_CREATION_BOOTSTRAP_LOGCAT_CLEAR_ARGUMENTS,
            device.run.call_args.args,
        )
        device.shell.assert_not_called()

    def test_confirmed_receipt_back_reacquisition_observer_failure_is_terminal(
        self,
    ) -> None:
        back = self.canonical_node(
            "creation-prerequisite-back-to-build",
            text="Back to build",
        )
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [back]
        device.node_has_tappable_bounds.return_value = True
        observer = mock.Mock(side_effect=RuntimeError("evidence write failed"))

        with self.assertRaisesRegex(RuntimeError, "evidence write failed"):
            driver.tap_exact_confirmed_receipt_back(
                device,
                {
                    "backViewport": 0,
                    "currentViewport": 0,
                    "backAuthority": "Back to build",
                    "backRecoveryMaxForwardScrolls": 0,
                },
                scan_observer=observer,
                deadline=driver.time.monotonic() + 130,
            )

        observer.assert_called_once()
        self.assertEqual("resolved", observer.call_args.args[0]["status"])
        device.shell.assert_not_called()

    def test_confirmed_receipt_back_reacquisition_preserves_action_and_proof_lease(
        self,
    ) -> None:
        device = mock.Mock(spec=driver.shared.Device)
        observer = mock.Mock()

        with self.assertRaisesRegex(RuntimeError, "action-plus-dashboard reserve"):
            driver.tap_exact_confirmed_receipt_back(
                device,
                {
                    "backViewport": 0,
                    "currentViewport": 0,
                    "backAuthority": "Back to build",
                    "backRecoveryMaxForwardScrolls": 1,
                },
                scan_observer=observer,
                deadline=(
                    driver.time.monotonic()
                    + driver.CONFIRMED_RECEIPT_BACK_DOWNSTREAM_RESERVE_SECONDS
                    - 0.001
                ),
            )

        device.hierarchy.assert_not_called()
        device.swipe_up.assert_not_called()
        device.shell.assert_not_called()
        observer.assert_called_once()
        failure = observer.call_args.args[0]
        self.assertEqual("failed", failure["status"])
        self.assertEqual(
            "InsufficientDownstreamReserve",
            failure["failureReason"],
        )
        self.assertEqual(0, failure["maximumElapsedMs"])
        self.assertEqual(81_000, failure["downstreamReserveMs"])

    def test_confirmed_receipt_back_reacquisition_rejects_unmeasured_bound(
        self,
    ) -> None:
        device = mock.Mock(spec=driver.shared.Device)
        for bound in (-1, 13, 1.0, True, "1"):
            with self.subTest(bound=bound), self.assertRaises(ValueError):
                driver.reacquire_exact_confirmed_receipt_back(
                    device,
                    max_forward_scrolls=bound,
                    expected_authority="Back to build",
                    scan_observer=None,
                    deadline=driver.time.monotonic() + 60,
                )
        device.hierarchy.assert_not_called()
        device.shell.assert_not_called()

    def test_rich_preview_rejects_unknown_or_reordered_assignments(self) -> None:
        option_id = (
            "creation-prerequisite-talent-skill-group-option-"
            "0dbcb9cd-f824-4b5d-a387-90d33318b04c"
        )
        preview_id = option_id.replace(
            "talent-skill-group-option-",
            "preview-talent-skill-group-",
        )
        sha = "sha256:" + ("a" * 64)
        assignments = [
            self.canonical_node(
                f"creation-prerequisite-preview-assignment-{category}"
            )
            for category in driver.CATEGORIES
        ]
        fixed = [
            self.canonical_node("creation-prerequisite-preview-page"),
            self.canonical_node(
                "creation-prerequisite-preview-binding",
                text="Revision 1 · saved 0 · preview abcdef012345",
            ),
            self.canonical_node("creation-prerequisite-preview-digest", text=sha),
            self.canonical_node(
                "creation-prerequisite-preview-raw-character-xml-digest", text=sha
            ),
            self.canonical_node(
                "creation-prerequisite-preview-auxiliary-state-digest",
                text="b" * 64,
            ),
            self.canonical_node(
                "creation-prerequisite-preview-authority-digest", text=sha
            ),
        ]
        tail = [
            self.canonical_node("creation-prerequisite-preview-heritage"),
            self.canonical_node("creation-prerequisite-preview-talent"),
            self.canonical_node(
                "creation-prerequisite-preview-karma-budget", text="Karma 25"
            ),
            self.canonical_node("creation-prerequisite-preview-attributes-ready"),
            self.canonical_node(
                driver.TALENT_GRANT_PREVIEW_PLAN_DIGEST_ID,
                text=sha,
            ),
            self.canonical_node(preview_id),
            self.canonical_node(
                "creation-prerequisite-confirm", text="Confirm exact preview"
            ),
        ]
        device = mock.Mock(spec=driver.shared.Device)
        device.node_has_tappable_bounds.return_value = True
        deadline = driver.time.monotonic() + 30

        def evaluate(screen: list[driver.shared.UiNode]) -> None:
            origin = self.priority_rank_origin(screen)
            with mock.patch.object(
                driver,
                "acquire_stable_start_origin",
                return_value=origin,
            ), mock.patch.object(
                driver,
                "scan_forward_until_stable",
                return_value=[screen],
            ):
                driver.require_exact_preview_talent_grant_plan(
                    device,
                    "Skill groups",
                    (option_id,),
                    max_scrolls=22,
                    scan_id="rich-preview-order",
                    deadline=deadline,
                    proof_out={},
                )

        evaluate([*fixed, *assignments, *tail])
        unknown = self.canonical_node(
            "creation-prerequisite-preview-assignment-unknown"
        )
        with self.assertRaisesRegex(RuntimeError, "assignmentOrder"):
            evaluate([*fixed, *assignments, unknown, *tail])
        with self.assertRaisesRegex(RuntimeError, "assignmentOrder"):
            evaluate([*fixed, assignments[1], assignments[0], *assignments[2:], *tail])
        with self.assertRaisesRegex(RuntimeError, "unexpectedConditional"):
            evaluate([
                *fixed,
                *assignments,
                self.canonical_node("creation-prerequisite-preview-sum-to-ten"),
                *tail,
            ])

    def test_priority_ready_preview_product_shape_has_binding_and_no_conditional_cards(
        self,
    ) -> None:
        source = (
            NATIVE / "CreationPrerequisitePreviewPage.cs"
        ).read_text(encoding="utf-8")
        refresh = source[
            source.index("protected override void Refresh()") :
            source.index("private void AddHeritageAndTalent()")
        ]
        self.assertLess(
            refresh.index('binding.AutomationId = "creation-prerequisite-preview-binding"'),
            refresh.index("AddAssignments();"),
        )
        sum_to_ten = source[
            source.index("private void AddSumToTen()") :
            source.index("private void AddAttributeGrant()")
        ]
        self.assertIn("CharacterCreationBuildMethods.SumToTen", sum_to_ten)
        self.assertIn("return;", sum_to_ten)
        attributes = source[
            source.index("private void AddAttributeGrant()") :
            source.index("private void AddBlockers()")
        ]
        self.assertIn(
            '"creation-prerequisite-preview-attributes-disabled"',
            attributes,
        )
        self.assertIn(
            ': "creation-prerequisite-preview-attributes-ready"',
            attributes,
        )
        blockers = source[
            source.index("private void AddBlockers()") :
            source.index("private void AddConfirmation()")
        ]
        self.assertIn("if (blockers.Length == 0)", blockers)
        self.assertIn("return;", blockers)

    def test_post_preview_talent_transition_is_exact_single_tap_and_fail_closed(
        self,
    ) -> None:
        device = mock.Mock()

        driver.open_talent_selection_after_preview(device)

        device.tap_exact_resource_id_bidirectional.assert_called_once_with(
            "creation-prerequisite-talent-selection",
            timeout=90,
            backward_scrolls=22,
            forward_scrolls=22,
            scroll_distance_ratio=0.22,
            evidence_prefix="creation-prerequisite-talent-selection-after-preview",
            surface_name="Talent selection row after active-skill preview",
        )
        device.wait_for_single_exact_resource_id.assert_called_once_with(
            "creation-prerequisite-talent-page",
            timeout=45,
            evidence_prefix="creation-prerequisite-talent-route-after-preview",
            surface_name="Talent route after active-skill preview",
        )
        device.tap.assert_not_called()

        ambiguous = mock.Mock()
        ambiguous.tap_exact_resource_id_bidirectional.side_effect = RuntimeError(
            "exact Talent selection cardinality was ambiguous"
        )
        with self.assertRaisesRegex(RuntimeError, "cardinality was ambiguous"):
            driver.open_talent_selection_after_preview(ambiguous)
        ambiguous.tap_exact_resource_id_bidirectional.assert_called_once()
        ambiguous.wait_for_single_exact_resource_id.assert_not_called()
        ambiguous.tap.assert_not_called()

        missing_route = mock.Mock()
        missing_route.wait_for_single_exact_resource_id.side_effect = RuntimeError(
            "exact Talent route did not appear"
        )
        with self.assertRaisesRegex(RuntimeError, "route did not appear"):
            driver.open_talent_selection_after_preview(missing_route)
        missing_route.tap_exact_resource_id_bidirectional.assert_called_once()
        missing_route.wait_for_single_exact_resource_id.assert_called_once()
        missing_route.tap.assert_not_called()

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

    def test_resources_transition_preserves_each_typed_authority_domain(self) -> None:
        prerequisite = {
            "rawCharacterXml": "sha256:" + "a" * 64,
            "auxiliaryState": "b" * 64,
            "authority": "sha256:" + "c" * 64,
        }
        prerequisite_revisions = {"contentRevision": 2, "savedRevision": 2}
        resources_before = {
            "contentRevision": prerequisite_revisions["contentRevision"],
            "savedRevision": prerequisite_revisions["savedRevision"],
            "rawCharacterXmlDigest": prerequisite["rawCharacterXml"],
            "auxiliaryStateDigest": prerequisite["auxiliaryState"],
            "authorityDigest": "sha256:" + "d" * 64,
            "prerequisiteDraftDigest": "sha256:" + "f" * 64,
        }
        resources_after = {
            **resources_before,
            "auxiliaryStateDigest": "e" * 64,
        }

        expected_prerequisite_binding = (
            driver.require_resources_confirmation_authority_transition(
                prerequisite,
                prerequisite_revisions,
                resources_before["prerequisiteDraftDigest"],
                resources_before,
                resources_after,
            )
        )

        self.assertEqual(
            {
                "rawCharacterXml": prerequisite["rawCharacterXml"],
                "auxiliaryState": "e" * 64,
                "authority": prerequisite["authority"],
            },
            expected_prerequisite_binding,
        )
        self.assertNotEqual(
            prerequisite["authority"],
            resources_before["authorityDigest"],
            "Prerequisite and Resources authority digests are domain-specific",
        )

    def test_resources_transition_rejects_cross_domain_workspace_drift(self) -> None:
        prerequisite = {
            "rawCharacterXml": "sha256:" + "a" * 64,
            "auxiliaryState": "b" * 64,
            "authority": "sha256:" + "c" * 64,
        }
        prerequisite_revisions = {"contentRevision": 2, "savedRevision": 2}
        resources_before = {
            "contentRevision": prerequisite_revisions["contentRevision"],
            "savedRevision": prerequisite_revisions["savedRevision"],
            "rawCharacterXmlDigest": prerequisite["rawCharacterXml"],
            "auxiliaryStateDigest": prerequisite["auxiliaryState"],
            "authorityDigest": "sha256:" + "d" * 64,
            "prerequisiteDraftDigest": "sha256:" + "f" * 64,
        }
        valid_after = {
            **resources_before,
            "auxiliaryStateDigest": "e" * 64,
        }

        invalid_cases = (
            (
                {**resources_before, "contentRevision": 3},
                valid_after,
                "did not start",
            ),
            (
                {**resources_before, "savedRevision": 3},
                valid_after,
                "did not start",
            ),
            (
                {**resources_before, "rawCharacterXmlDigest": "sha256:" + "f" * 64},
                valid_after,
                "did not start",
            ),
            (
                {**resources_before, "auxiliaryStateDigest": "0" * 64},
                valid_after,
                "did not start",
            ),
            (
                {**resources_before, "prerequisiteDraftDigest": "sha256:" + "0" * 64},
                valid_after,
                "did not start",
            ),
            (
                resources_before,
                {**valid_after, "rawCharacterXmlDigest": "sha256:" + "f" * 64},
                "changed raw XML, Resources authority",
            ),
            (
                resources_before,
                {**valid_after, "authorityDigest": "sha256:" + "f" * 64},
                "changed raw XML, Resources authority",
            ),
            (
                resources_before,
                {**valid_after, "prerequisiteDraftDigest": "sha256:" + "0" * 64},
                "prerequisite draft",
            ),
            (
                resources_before,
                {**valid_after, "auxiliaryStateDigest": prerequisite["auxiliaryState"]},
                "failed to advance auxiliary state",
            ),
        )
        for before, after, message in invalid_cases:
            with self.subTest(message=message), self.assertRaisesRegex(
                RuntimeError,
                message,
            ):
                driver.require_resources_confirmation_authority_transition(
                    prerequisite,
                    prerequisite_revisions,
                    resources_before["prerequisiteDraftDigest"],
                    before,
                    after,
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

    def test_dashboard_does_not_retain_typed_authority_across_auxiliary_route_mutations(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        disappearing = source[source.index("protected override void OnDisappearing()") :]
        disappearing = disappearing[: disappearing.index("protected override void Refresh()")]

        self.assertLess(
            disappearing.index("CancelCreationProjectionQueues();"),
            disappearing.index("_creationProjection = null;"),
        )
        self.assertLess(
            disappearing.index("_creationProjection = null;"),
            disappearing.index("base.OnDisappearing();"),
        )
        self.assertIn("auxiliary-state-stale", disappearing)

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
            "ResolveCurrentAuthority()",
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
            'documentChangedMetric.AutomationId =\n            "creation-prerequisite-confirm-receipt";',
            "string documentChangedLabel = WizardStrings.Get(",
            "string documentChanged = receipt.CharacterDocumentChanged",
            'SemanticProperties.SetDescription(\n            documentChangedMetric,\n            $"{documentChangedLabel} {documentChanged}");',
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
            "new CreationPrerequisitePage(Coordinator, authority)",
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
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        category_refresh = options[
            options.index("protected override void Refresh()") : options.index(
                "private async Task SelectAsync("
            )
        ]

        refresh = page[
            page.index("protected override void Refresh()") :
            page.index("private void TryPublishApi36AttachmentProof()")
        ]
        self.assertEqual(1, refresh.count("            ResolveCurrentAuthority();"))
        self.assertEqual(1, page.count("Coordinator.LoadCreationPrerequisite()"))
        self.assertIn("CharacterCreationPrerequisiteState dashboardAuthority", page)
        self.assertIn("_dashboardAuthority = dashboardAuthority", page)
        self.assertIn(
            "CreationPrerequisitePhoneAuthority.IsReady(authority, Coordinator.State)",
            page,
        )
        self.assertLess(
            page.index("_dashboardAuthority = null;"),
            page.index("return Coordinator.LoadCreationPrerequisite();"),
        )
        self.assertIn(
            "OpenCreationPrerequisiteAsync(prerequisite!.Value!)",
            dashboard,
        )
        self.assertIn("_latestApi36ProofReadyState = null;", refresh)
        self.assertIn("_latestApi36ProofReadyState = state;", refresh)
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
        self.assertIn(
            "Back navigation did not restore the byte-for-byte typed Attribute ",
            source,
        )
        self.assertIn('"creation-prerequisite-attributes-disabled"', source)
        self.assertIn('"creation-prerequisite-prepare-preview"', source)
        self.assertIn('"creation-prerequisite-confirm"', source)
        self.assertIn('"creation-prerequisite-confirm-receipt"', source)
        self.assertIn('"creation-prerequisite-pending-draft"', source)
        self.assertIn('"creation-prerequisite-attributes-ready"', source)
        self.assertIn("shared.force_stop_and_launch_new_process(\n        device,", source)
        self.assertIn("deadline=process_restart_deadline", source)
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
        self.assertIn(
            '"sameSessionPersistedAuthority": resumed_authority.authority',
            source,
        )
        self.assertIn(
            '"restartedPersistedAuthority": restarted_authority.authority',
            source,
        )
        self.assertIn("read_persisted_prerequisite_authority(", source)
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
        typed = typed[: typed.index('progress.advance("preview-confirm")')]
        self.assertLess(
            typed.index("shared.reset_scroll_to_top(device, swipes=22)"),
            typed.index('"creation-prerequisite-heritage-selection"'),
        )

        preview = inspect.getsource(driver.require_exact_preview_talent_grant_plan)
        self.assertLess(
            preview.index('assignment_selectors = tuple('),
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

        receipt = inspect.getsource(driver.read_exact_confirmed_receipt)
        self.assertLess(
            receipt.index('"creation-prerequisite-receipt-draft-revision"'),
            receipt.index('"creation-prerequisite-receipt-draft-digest"'),
        )
        self.assertLess(
            receipt.index('"creation-prerequisite-receipt-draft-digest"'),
            receipt.index('"creation-prerequisite-receipt-raw-character-xml-digest"'),
        )

        persisted = inspect.getsource(driver.scan_persisted_prerequisite_authority)
        self.assertIn("PERSISTED_PREREQUISITE_AUTHORITY_SELECTORS", persisted)
        self.assertIn("scan_forward_with_receipt(", persisted)
        self.assertNotIn("shared.reset_scroll_to_top", persisted)
        self.assertNotIn("node_text(", persisted)
        self.assertLess(
            persisted.index('"creation-prerequisite-authority-digest"'),
            persisted.index('"creation-prerequisite-pending-draft-digest"'),
        )

        resumed = source[source.index("resumed_authority =") :]
        resumed = resumed[: resumed.index("restart =")]
        self.assertIn(
            "resumed_attributes = resumed_authority.attributes_authority",
            resumed,
        )
        self.assertNotIn("node_text(", resumed)

        restored = source[source.index("def require_exact_restored_authority_option(") :]
        restored = restored[: restored.index("def open_resources(")]
        self.assertIn("device.wait_exact_resource_id_bidirectional(", restored)
        self.assertIn("backward_scrolls=0", restored)
        self.assertNotIn("device.tap(", restored)
        self.assertNotIn("shared.reset_scroll_to_top", restored)

    def test_api36_phone_only_ci_selects_the_isolated_prerequisite_journey(self) -> None:
        runner = (
            REPO / "scripts" / "run-api36-editing-e2e-ci.sh"
        ).read_text(encoding="utf-8")
        generic = "python3 chummer-android/tests/run_api36_editing_e2e.py"
        prerequisite = "python3 chummer-android/tests/run_api36_creation_prerequisite_e2e.py"
        self.assertIn('if [[ "$profile" != "phone" ]]; then', runner)
        self.assertIn("tablet beta proof is deferred", runner)
        self.assertNotIn(generic, runner)
        self.assertIn(prerequisite, runner)
        guard = runner.index('if [[ "$profile" != "phone" ]]; then')
        self.assertLess(guard, runner.index(prerequisite))
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
