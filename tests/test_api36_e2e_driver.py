import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, call, patch


REPO_ROOT = Path(__file__).resolve().parents[1]
DRIVER_PATH = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", DRIVER_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"could not load {DRIVER_PATH}")
DRIVER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = DRIVER
SPEC.loader.exec_module(DRIVER)


class FakeDevice(DRIVER.Device):
    def __init__(
        self,
        evidence: Path,
        hierarchy_output: str,
        dump_output: str = "UI hierarchy dumped",
    ) -> None:
        super().__init__(Path("/unused/adb"), "emulator-5554", evidence)
        self.hierarchy_output = hierarchy_output
        self.dump_output = dump_output

    def shell(self, *arguments: str, timeout: int = 120) -> str:
        return self.dump_output

    def run(
        self,
        *arguments: str,
        timeout: int = 120,
        text: bool = True,
        check: bool = True,
    ) -> subprocess.CompletedProcess:
        return subprocess.CompletedProcess(
            args=list(arguments),
            returncode=0,
            stdout=self.hierarchy_output,
            stderr="",
        )


class RecordingDevice(DRIVER.Device):
    def __init__(self, evidence: Path, display_size: str) -> None:
        super().__init__(Path("/unused/adb"), "emulator-5554", evidence)
        self.display_size_output = display_size
        self.commands: list[tuple[str, ...]] = []
        self.nodes: list[DRIVER.UiNode] = []
        self.input_method_output = ""
        self.hide_keyboard_on_nav_tap = False
        self.hide_keyboard_on_escape = False

    def shell(self, *arguments: str, timeout: int = 120) -> str:
        self.commands.append(arguments)
        if arguments == ("wm", "size"):
            return self.display_size_output
        if arguments == ("dumpsys", "input_method"):
            return self.input_method_output
        if (
            self.hide_keyboard_on_escape
            and arguments == ("input", "keyevent", "111")
        ):
            self.input_method_output = "mImeWindowVis=0\n      mInputShown=false"
        elif (
            self.hide_keyboard_on_nav_tap
            and arguments == ("input", "tap", "162", "2350")
        ):
            self.input_method_output = "mImeWindowVis=0\n      mInputShown=false"
        elif arguments[:2] == ("input", "tap") and self.nodes:
            for node in self.nodes:
                node.attributes["focused"] = "true"
        elif arguments[:2] == ("input", "text"):
            value = arguments[2].replace("%s", " ")
            for node in self.nodes:
                if node.attributes.get("focused") == "true":
                    node.attributes["text"] = value
        elif arguments == ("input", "keyevent", "67"):
            for node in self.nodes:
                if node.attributes.get("focused") == "true":
                    node.attributes["text"] = ""
        return ""

    def hierarchy(self) -> list[DRIVER.UiNode]:
        return self.nodes


class Api36EditingE2EDriverTests(unittest.TestCase):
    @staticmethod
    def successful_am_start(component: str) -> subprocess.CompletedProcess:
        return subprocess.CompletedProcess(
            args=["adb", "shell", "am", "start"],
            returncode=0,
            stdout=(
                "Starting: Intent { act=android.intent.action.MAIN "
                f"cat=[android.intent.category.LAUNCHER] cmp={component} }}\n"
                "Status: ok\n"
                "LaunchState: COLD\n"
                f"Activity: {component}\n"
                "Complete\n"
            ),
            stderr="",
        )

    def test_launch_app_uses_exact_main_launcher_and_requires_resumed_activity(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.shell.side_effect = [
                "package:/data/app/exact/base.apk",
                component,
                "7225",
                f"mResumedActivity: ActivityRecord{{123 u0 {component} t9}}",
            ]
            device.run.side_effect = [
                subprocess.CompletedProcess(args=["logcat", "-c"], returncode=0, stdout="", stderr=""),
                self.successful_am_start(component),
            ]

            state = DRIVER.launch_app(device, resume_timeout=0)

            verified = Path(temporary) / "launch-attempt-1-verified.txt"
            self.assertTrue(verified.is_file())
            self.assertIn("process_ids=7225", verified.read_text(encoding="utf-8"))
            self.assertIn(f"resumed_component={component}", verified.read_text(encoding="utf-8"))
            self.assertEqual(("7225",), state.process_ids)
            self.assertEqual(component, state.resumed_component)

        self.assertEqual(
            call(
                "shell",
                "am",
                "start",
                "--user",
                "current",
                "-W",
                "-a",
                DRIVER.MAIN_ACTION,
                "-c",
                DRIVER.LAUNCHER_CATEGORY,
                "--ez",
                DRIVER.E2E_AUTHORITY_EXTRA,
                "true",
                "-n",
                component,
                timeout=60,
                check=False,
            ),
            device.run.call_args_list[1],
        )
        self.assertNotIn("-S", device.run.call_args_list[1].args)

    def test_launch_app_accepts_am_wait_timeout_only_with_exact_process_and_resume(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        timeout = subprocess.TimeoutExpired(
            ["adb", "shell", "am", "start", "-W"],
            60,
            output="Status: ok\n",
            stderr="",
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.shell.side_effect = [
                "package:/data/app/exact/base.apk",
                component,
                "7225",
                f"topResumedActivity=ActivityRecord{{123 u0 {component} t9}}",
            ]
            device.run.side_effect = [
                subprocess.CompletedProcess(args=["logcat", "-c"], returncode=0, stdout="", stderr=""),
                timeout,
            ]

            DRIVER.launch_app(device, resume_timeout=0)

        self.assertEqual(2, device.run.call_count)

    def test_launch_failure_captures_command_activity_window_and_all_log_buffers(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        launcher = "com.google.android.apps.nexuslauncher/.NexusLauncherActivity"
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.capture.return_value = None
            device.shell.side_effect = [
                "package:/data/app/exact/base.apk",
                component,
                "7225",
                f"mResumedActivity: ActivityRecord{{456 u0 {launcher} t2}}",
                f"mCurrentFocus=Window{{456 u0 {launcher}}}",
                "ApplicationExitInfo(timestamp=1, reason=CRASH)",
            ]
            device.run.side_effect = [
                subprocess.CompletedProcess(args=["logcat", "-c"], returncode=0, stdout="", stderr=""),
                self.successful_am_start(component),
                subprocess.CompletedProcess(
                    args=["logcat", "-d"],
                    returncode=0,
                    stdout=(
                        "AndroidRuntime: FATAL EXCEPTION: main\n"
                        "AndroidRuntime: Process: com.myexternalbrain.chummer, PID: 7225\n"
                    ),
                    stderr="",
                ),
                subprocess.CompletedProcess(
                    args=["logcat", "-d", "-b", "events"],
                    returncode=0,
                    stdout="am_crash: com.myexternalbrain.chummer\n",
                    stderr="",
                ),
                subprocess.CompletedProcess(
                    args=["logcat", "-d", "-b", "crash"],
                    returncode=0,
                    stdout="AndroidRuntime: Process: com.myexternalbrain.chummer\n",
                    stderr="",
                ),
            ]

            with self.assertRaisesRegex(RuntimeError, "did not remain the exact resumed activity"):
                DRIVER.launch_app(device, resume_timeout=0)

            evidence = Path(temporary)
            self.assertIn("Status: ok", (evidence / "launch-attempt-1-am-start.stdout.txt").read_text())
            self.assertIn(launcher, (evidence / "launch-attempt-1-activity.txt").read_text())
            self.assertIn("FATAL EXCEPTION", (evidence / "launch-attempt-1-logcat.txt").read_text())
            self.assertIn("reason=CRASH", (evidence / "launch-attempt-1-exit-info.txt").read_text())
            self.assertIn("am_crash", (evidence / "launch-attempt-1-logcat-events.txt").read_text())
            self.assertIn("AndroidRuntime", (evidence / "launch-attempt-1-logcat-crash.txt").read_text())
            device.capture.assert_called_once_with("launch-attempt-1-failure")

        self.assertIn(
            call(
                "logcat",
                "-d",
                "-b",
                "all",
                "-v",
                "threadtime",
                "-t",
                "4000",
                timeout=60,
                check=False,
            ),
            device.run.call_args_list,
        )
        for buffer_name in ("events", "crash"):
            self.assertIn(
                call(
                    "logcat",
                    "-d",
                    "-b",
                    buffer_name,
                    "-v",
                    "threadtime",
                    timeout=60,
                    check=False,
                ),
                device.run.call_args_list,
            )

    def test_launcher_component_uses_current_package_manager_result(self) -> None:
        device = Mock()
        device.shell.side_effect = [
            "package:/data/app/exact/base.apk",
            "priority=0\ncom.myexternalbrain.chummer/crccurrent.MainActivity\n",
        ]

        self.assertEqual(
            "com.myexternalbrain.chummer/crccurrent.MainActivity",
            DRIVER.launcher_component(device),
        )
        self.assertEqual(
            call(
                "cmd",
                "package",
                "resolve-activity",
                "--brief",
                "--user",
                "current",
                "-a",
                DRIVER.MAIN_ACTION,
                "-c",
                DRIVER.LAUNCHER_CATEGORY,
                "-p",
                DRIVER.PACKAGE,
            ),
            device.shell.call_args_list[1],
        )

    def test_process_restart_requires_a_new_exact_launch_pid(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        initial = DRIVER.LaunchState(("7225",), component, "initial")
        stopped = DRIVER.LaunchState((), None, "stopped")
        restarted = DRIVER.LaunchState(("7351",), component, "restart")
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            with patch.object(
                DRIVER,
                "current_launch_state",
                side_effect=[initial, stopped],
            ), patch.object(DRIVER, "launch_app", return_value=restarted):
                actual = DRIVER.force_stop_and_launch_new_process(device, initial)

            proof = Path(temporary) / "process-restart-verified.txt"
            self.assertTrue(proof.is_file())
            self.assertIn("pre_force_stop_process_ids=7225", proof.read_text(encoding="utf-8"))
            self.assertIn("post_force_stop_process_ids=", proof.read_text(encoding="utf-8"))
            self.assertIn("restart_process_ids=7351", proof.read_text(encoding="utf-8"))

        self.assertIs(initial, actual.before_force_stop)
        self.assertIs(stopped, actual.after_force_stop)
        self.assertIs(restarted, actual.restarted)
        device.shell.assert_called_once_with("am", "force-stop", DRIVER.PACKAGE)

    def test_process_restart_rejects_a_reused_pid(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        initial = DRIVER.LaunchState(("7225",), component, "initial")
        stopped = DRIVER.LaunchState((), None, "stopped")
        restarted = DRIVER.LaunchState(("7225",), component, "restart")
        device = Mock(spec=DRIVER.Device)
        with patch.object(
            DRIVER,
            "current_launch_state",
            side_effect=[initial, stopped],
        ), patch.object(DRIVER, "launch_app", return_value=restarted):
            with self.assertRaisesRegex(RuntimeError, "reused an existing PID"):
                DRIVER.force_stop_and_launch_new_process(device, initial)

        device.capture.assert_called_once_with("process-restart-pid-reused")

    def test_process_restart_rejects_a_nonempty_post_force_stop_pid_set(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        initial = DRIVER.LaunchState(("7225",), component, "initial")
        still_running = DRIVER.LaunchState(("7225",), None, "stopping")
        device = Mock(spec=DRIVER.Device)
        with patch.object(
            DRIVER,
            "current_launch_state",
            side_effect=[initial, still_running],
        ), patch.object(DRIVER, "launch_app") as launch:
            with self.assertRaisesRegex(RuntimeError, "remained non-empty"):
                DRIVER.force_stop_and_launch_new_process(device, initial)

        launch.assert_not_called()
        device.capture.assert_called_once_with("process-restart-force-stop-not-empty")

    def test_process_restart_rejects_a_changed_live_launch_identity(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        initial = DRIVER.LaunchState(("7225",), component, "initial")
        changed = DRIVER.LaunchState(("7300",), component, "changed")
        device = Mock(spec=DRIVER.Device)
        with patch.object(DRIVER, "current_launch_state", return_value=changed), patch.object(
            DRIVER,
            "launch_app",
        ) as launch:
            with self.assertRaisesRegex(RuntimeError, "changed before the owned force-stop"):
                DRIVER.force_stop_and_launch_new_process(device, initial)

        launch.assert_not_called()
        device.shell.assert_not_called()
        device.capture.assert_called_once_with("process-restart-precondition-changed")

    def test_launcher_component_rejects_missing_package_or_ambiguous_component(self) -> None:
        missing = Mock()
        missing.shell.return_value = ""
        with self.assertRaisesRegex(RuntimeError, "not installed"):
            DRIVER.launcher_component(missing)

        ambiguous = Mock()
        ambiguous.shell.side_effect = [
            "package:/data/app/exact/base.apk",
            (
                "com.myexternalbrain.chummer/crc.one.MainActivity\n"
                "com.myexternalbrain.chummer/crc.two.MainActivity\n"
            ),
        ]
        with self.assertRaisesRegex(RuntimeError, "exactly one"):
            DRIVER.launcher_component(ambiguous)

    def test_resumed_activity_parser_normalizes_short_activity_and_ignores_focus(self) -> None:
        dump = (
            "mCurrentFocus=Window{123 u0 com.example/.Other}\n"
            "topResumedActivity=ActivityRecord{456 u0 "
            "com.myexternalbrain.chummer/.MainActivity t4}\n"
        )
        self.assertEqual(
            "com.myexternalbrain.chummer/com.myexternalbrain.chummer.MainActivity",
            DRIVER.resumed_activity(dump),
        )

    def test_hierarchy_ignores_uiautomator_preamble(self) -> None:
        output = (
            "UI hierarchy dumped to: /sdcard/window.xml\n"
            "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>"
            "<hierarchy><node text='Your runners' /></hierarchy>"
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = FakeDevice(Path(temporary), output)
            nodes = device.hierarchy()

        self.assertEqual("Your runners", nodes[0].attributes["text"])

    def test_hierarchy_uses_compressed_android_dump(self) -> None:
        xml = "<hierarchy><node text='Your runners' /></hierarchy>"
        device = Mock(spec=DRIVER.Device)
        device.shell.return_value = "UI hierarchy dumped"
        device.run.return_value = subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout=xml,
            stderr="",
        )
        with tempfile.TemporaryDirectory() as temporary:
            device.evidence = Path(temporary)
            nodes = DRIVER.Device.hierarchy(device)

        self.assertEqual("Your runners", nodes[0].attributes["text"])
        device.shell.assert_called_once_with(
            "uiautomator",
            "dump",
            "--compressed",
            "/sdcard/chummer-editing-window.xml",
        )

    def test_invalid_hierarchy_is_retryable_and_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = FakeDevice(evidence, "ERROR: could not get idle state")
            self.assertEqual([], device.hierarchy())
            diagnostic = evidence / "last-invalid-hierarchy.txt"
            self.assertTrue(diagnostic.is_file())
            self.assertIn("could not get idle state", diagnostic.read_text(encoding="utf-8"))

    def test_failed_dump_status_does_not_reuse_stale_hierarchy_xml(self) -> None:
        stale = "<hierarchy><node text='Previous screen' /></hierarchy>"
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = FakeDevice(
                evidence,
                stale,
                dump_output="ERROR: could not get idle state.",
            )

            self.assertEqual([], device.hierarchy())
            diagnostic = evidence / "last-invalid-hierarchy.txt"
            self.assertIn(
                "could not get idle state",
                diagnostic.read_text(encoding="utf-8"),
            )

    def test_android_hierchary_success_typo_is_accepted(self) -> None:
        xml = "<hierarchy><node text='Current screen' /></hierarchy>"
        with tempfile.TemporaryDirectory() as temporary:
            device = FakeDevice(
                Path(temporary),
                xml,
                dump_output="UI hierchary dumped to: /sdcard/window.xml",
            )

            nodes = device.hierarchy()

        self.assertEqual("Current screen", nodes[0].attributes["text"])

    def test_swipe_up_stays_inside_tablet_display(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 2560x1800")
            device.swipe_up()

        self.assertEqual(
            ("input", "swipe", "1280", "1476", "1280", "540", "300"),
            device.commands[-1],
        )

    def test_small_swipe_keeps_overlap_for_long_dialogs(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.swipe_up(distance_ratio=0.28)

        self.assertEqual(
            ("input", "swipe", "540", "1968", "540", "1296", "300"),
            device.commands[-1],
        )

    def test_bidirectional_tap_resets_without_hierarchy_probes(self) -> None:
        target = DRIVER.UiNode(
            {
                "resource-id": "build-section-tab-attributes",
                "clickable": "true",
                "bounds": "[100,400][900,520]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device.find.return_value = target
        device.dismiss_system_ui_anr.return_value = False
        device._scroll_x_ratio.return_value = 0.5
        device.node_has_tappable_bounds.return_value = True

        with patch.object(DRIVER.time, "sleep"):
            DRIVER.Device.tap_bidirectional(
                device,
                "build-section-tab-attributes",
                backward_scrolls=4,
                forward_scrolls=4,
            )

        self.assertEqual(
            [call(x_ratio=0.5, distance_ratio=0.22)] * 4,
            device.swipe_down.call_args_list,
        )
        device.find.assert_called_once_with("build-section-tab-attributes")
        device.swipe_up.assert_not_called()
        device.shell.assert_called_once_with("input", "tap", "500", "460")

    def test_bidirectional_tap_has_bounded_forward_fallback(self) -> None:
        target = DRIVER.UiNode(
            {
                "resource-id": "build-section-tab-attributes",
                "clickable": "true",
                "bounds": "[100,400][900,520]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device.find.side_effect = [None, None, target]
        device.dismiss_system_ui_anr.return_value = False
        device._scroll_x_ratio.return_value = 0.5
        device.node_has_tappable_bounds.return_value = True

        with patch.object(DRIVER.time, "sleep"):
            DRIVER.Device.tap_bidirectional(
                device,
                "build-section-tab-attributes",
                backward_scrolls=1,
                forward_scrolls=2,
            )

        device.swipe_down.assert_called_once_with(x_ratio=0.5, distance_ratio=0.22)
        self.assertEqual(
            [call(x_ratio=0.5, distance_ratio=0.22)] * 2,
            device.swipe_up.call_args_list,
        )

    def test_bidirectional_tap_reaches_hidden_target_with_expensive_hierarchy_dumps(self) -> None:
        target = DRIVER.UiNode(
            {
                "resource-id": "build-section-tab-attributes",
                "clickable": "true",
                "bounds": "[100,400][900,520]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device._scroll_x_ratio.return_value = 0.5
        device.node_has_tappable_bounds.return_value = True
        elapsed = [0.0]
        down_find_counts: list[int] = []

        def expensive_find(selector: str) -> DRIVER.UiNode | None:
            elapsed[0] += 4.0
            if (
                selector == "build-section-tab-attributes"
                and device.swipe_up.call_count >= 12
            ):
                return target
            return None

        device.find.side_effect = expensive_find
        device.swipe_down.side_effect = lambda **_kwargs: down_find_counts.append(
            device.find.call_count
        )
        device.dismiss_system_ui_anr.side_effect = (
            lambda: DRIVER.Device.dismiss_system_ui_anr(device)
        )

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=lambda: elapsed[0]),
            patch.object(DRIVER.time, "sleep"),
        ):
            DRIVER.Device.tap_bidirectional(
                device,
                "build-section-tab-attributes",
                timeout=120,
                backward_scrolls=24,
                forward_scrolls=24,
            )

        self.assertEqual([0] * 24, down_find_counts)
        self.assertEqual(25, device.find.call_count)
        self.assertEqual(12, device.dismiss_system_ui_anr.call_count)
        self.assertEqual(24, device.swipe_down.call_count)
        self.assertEqual(12, device.swipe_up.call_count)
        device.shell.assert_called_once_with("input", "tap", "500", "460")

    def test_bidirectional_tap_keeps_forward_search_bounded_when_target_is_absent(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.find.return_value = None
        device.dismiss_system_ui_anr.return_value = False
        device._scroll_x_ratio.return_value = 0.5

        with patch.object(DRIVER.time, "sleep"):
            with self.assertRaisesRegex(RuntimeError, "bounded bidirectional search"):
                DRIVER.Device.tap_bidirectional(
                    device,
                    "build-section-tab-attributes",
                    timeout=120,
                    backward_scrolls=3,
                    forward_scrolls=2,
                )

        self.assertEqual(3, device.swipe_down.call_count)
        self.assertEqual(2, device.swipe_up.call_count)
        self.assertEqual(3, device.find.call_count)
        device.capture.assert_called_once_with("failure")

    def test_exact_bidirectional_tap_recovers_when_twelve_swipes_leave_gear_clipped(self) -> None:
        clipped = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/build-section-tab-gear",
                "clickable": "true",
                "bounds": "[98,275][984,276]",
            }
        )
        target = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/build-section-tab-gear",
                "clickable": "true",
                "bounds": "[98,400][984,560]",
            }
        )
        prefix_decoy = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/build-section-tab-gear-locations"
                ),
                "clickable": "true",
                "bounds": "[98,700][984,860]",
            }
        )
        legacy = Mock(spec=DRIVER.Device)
        legacy_position = {"forward_swipes": 0}
        legacy_clock = {"seconds": 0}
        legacy._scroll_x_ratio.return_value = 0.5
        legacy.display_size.return_value = (1080, 2400)
        legacy.hierarchy.side_effect = lambda: [
            prefix_decoy,
            *([clipped] if legacy_position["forward_swipes"] == 1 else []),
        ]
        legacy.find_exact_resource_id.side_effect = (
            lambda selector: DRIVER.Device.find_exact_resource_id(legacy, selector)
        )
        legacy.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(legacy, node)
        )
        legacy.dismiss_system_ui_anr.return_value = False
        legacy.swipe_up.side_effect = lambda **_: legacy_position.update(
            forward_swipes=legacy_position["forward_swipes"] + 1
        )

        def advance_legacy_clock() -> int:
            legacy_clock["seconds"] += 1
            return legacy_clock["seconds"]

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=advance_legacy_clock),
            patch.object(DRIVER.time, "sleep"),
            self.assertRaisesRegex(RuntimeError, "Timed out waiting for tappable UI node"),
        ):
            DRIVER.Device.tap(
                legacy,
                "build-section-tab-gear",
                timeout=8,
                scroll=True,
                max_scrolls=6,
                scroll_distance_ratio=0.52,
                exact_resource_id=True,
            )

        self.assertEqual(
            [call(x_ratio=0.5, distance_ratio=0.52)] * 6,
            legacy.swipe_up.call_args_list,
        )
        legacy.capture.assert_called_once_with("failure")
        legacy.node_has_tappable_bounds.assert_called_once_with(clipped)
        legacy.shell.assert_not_called()

        device = Mock(spec=DRIVER.Device)
        position = {"rows_below_top": 24}
        device._scroll_x_ratio.return_value = 0.5
        device.display_size.return_value = (1080, 2400)
        device.swipe_down.side_effect = lambda **_: position.update(
            rows_below_top=max(0, position["rows_below_top"] - 1)
        )
        device.swipe_up.side_effect = lambda **_: position.update(
            rows_below_top=position["rows_below_top"] + 1
        )
        device.hierarchy.side_effect = lambda: [
            prefix_decoy,
            *(
                [clipped]
                if position["rows_below_top"] == 12
                else [target]
                if position["rows_below_top"] == 11
                else []
            ),
        ]
        device.find_exact_resource_id.side_effect = (
            lambda selector: DRIVER.Device.find_exact_resource_id(device, selector)
        )
        device.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(device, node)
        )
        device.dismiss_system_ui_anr.return_value = False

        for _ in range(12):
            device.swipe_down(x_ratio=0.5, distance_ratio=0.22)
        twelve_swipe_candidate = DRIVER.Device.find_exact_resource_id(
            device,
            "build-section-tab-gear",
        )
        self.assertIs(clipped, twelve_swipe_candidate)
        self.assertFalse(DRIVER.Device.node_has_tappable_bounds(device, clipped))
        self.assertTrue(DRIVER.Device._matches(prefix_decoy, "build-section-tab-gear"))

        position["rows_below_top"] = 24
        device.reset_mock()
        device._scroll_x_ratio.return_value = 0.5
        device.display_size.return_value = (1080, 2400)
        device.find_exact_resource_id.side_effect = (
            lambda selector: DRIVER.Device.find_exact_resource_id(device, selector)
        )
        device.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(device, node)
        )
        device.dismiss_system_ui_anr.return_value = False

        with patch.object(DRIVER.time, "sleep"):
            DRIVER.Device.tap_bidirectional(
                device,
                "build-section-tab-gear",
                timeout=120,
                backward_scrolls=24,
                forward_scrolls=24,
                scroll_distance_ratio=0.22,
                exact_resource_id=True,
            )

        self.assertEqual(24, device.swipe_down.call_count)
        self.assertEqual(11, device.swipe_up.call_count)
        self.assertEqual(11, position["rows_below_top"])
        device.find.assert_not_called()
        device.shell.assert_called_once_with("input", "tap", "541", "480")

    def test_exact_bidirectional_tap_exhaustion_never_taps_clipped_or_prefix_decoy(self) -> None:
        clipped = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/build-section-tab-gear",
                "clickable": "true",
                "bounds": "[98,275][984,276]",
            }
        )
        prefix_decoy = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/build-section-tab-gear-locations"
                ),
                "clickable": "true",
                "bounds": "[98,700][984,860]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device._scroll_x_ratio.return_value = 0.5
        device.display_size.return_value = (1080, 2400)
        device.hierarchy.return_value = [prefix_decoy, clipped]
        device.find_exact_resource_id.side_effect = (
            lambda selector: DRIVER.Device.find_exact_resource_id(device, selector)
        )
        device.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(device, node)
        )
        device.dismiss_system_ui_anr.return_value = False

        with patch.object(DRIVER.time, "sleep"):
            with self.assertRaisesRegex(RuntimeError, "bounded bidirectional search"):
                DRIVER.Device.tap_bidirectional(
                    device,
                    "build-section-tab-gear",
                    backward_scrolls=2,
                    forward_scrolls=2,
                    exact_resource_id=True,
                )

        self.assertEqual(2, device.swipe_down.call_count)
        self.assertEqual(2, device.swipe_up.call_count)
        device.find.assert_not_called()
        device.capture.assert_called_once_with("failure")
        device.shell.assert_not_called()

    def test_fixture_transport_verifies_the_exact_remote_bytes(self) -> None:
        expected = "a" * 64
        device = Mock(spec=DRIVER.Device)
        device.shell.return_value = f"{expected}  /sdcard/Download/runner.chum5"
        local = Path("runner.chum5")

        actual = DRIVER.Device.push_verified(
            device,
            local,
            "/sdcard/Download/runner.chum5",
            expected,
        )

        self.assertEqual(expected, actual)
        device.push.assert_called_once_with(local, "/sdcard/Download/runner.chum5")
        device.shell.assert_called_once_with(
            "sha256sum",
            "/sdcard/Download/runner.chum5",
        )

    def test_fixture_transport_rejects_changed_remote_bytes(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.shell.return_value = f"{'b' * 64}  /sdcard/Download/runner.chum5"

        with self.assertRaisesRegex(RuntimeError, "transport digest mismatch"):
            DRIVER.Device.push_verified(
                device,
                Path("runner.chum5"),
                "/sdcard/Download/runner.chum5",
                "a" * 64,
            )

    def test_collection_tap_uses_card_gutter_and_overlapping_scrolls(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "text": "Ares Predator V",
                        "clickable": "false",
                        "bounds": "[100,500][700,560]",
                    }
                )
            ]
            DRIVER.tap_collection_item(device, "Ares Predator V")

        self.assertEqual(("input", "tap", "82", "530"), device.commands[-1])

    def test_system_ui_anr_wait_action_is_dismissed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "resource-id": "android:id/aerr_wait",
                        "text": "Wait",
                        "clickable": "true",
                        "bounds": "[100,1200][900,1400]",
                    }
                )
            ]

            self.assertTrue(device.dismiss_system_ui_anr())

        self.assertEqual(("input", "tap", "500", "1300"), device.commands[-1])

    def test_wait_scrolls_after_anr_recovery_before_dumping_again(self) -> None:
        device = Mock(spec=DRIVER.Device)
        expected = DRIVER.UiNode({"text": "ContactE2E"})
        device.find.side_effect = [None, expected]
        device.dismiss_system_ui_anr.return_value = True
        device._scroll_x_ratio.return_value = 0.5

        with patch.object(DRIVER.time, "sleep") as sleep:
            actual = DRIVER.Device.wait(
                device,
                "ContactE2E",
                scroll=True,
                max_scrolls=8,
                scroll_distance_ratio=0.22,
            )

        self.assertIs(expected, actual)
        device.swipe_up.assert_called_once_with(
            x_ratio=0.5,
            distance_ratio=0.22,
        )
        self.assertEqual([call(5), call(1)], sleep.call_args_list)

    def test_new_runner_launch_retries_until_the_build_method_dialog_is_visible(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        self.assertIn(
            'device.tap_until_visible("home-new-runner", "Select Build Method")',
            source,
        )
        self.assertIn("time.sleep(1.25)", source)

    def test_full_phone_journey_proves_wizard_then_imports_completed_runner(self) -> None:
        device = Mock()
        fixture_sha256 = "a" * 64
        creation = DRIVER.WorkspaceAuthority("creation", 1, 1, "b" * 64, "c" * 64)
        imported = DRIVER.WorkspaceAuthority("imported", 2, 2, fixture_sha256, "d" * 64)

        with patch.object(DRIVER, "select_android_document") as select_document, patch.object(
            DRIVER,
            "read_workspace_authority",
            side_effect=[creation, imported],
        ):
            result = DRIVER.prepare_full_editing_runner(
                device,
                "phone",
                "career-full-editing-e2e.chum5",
                "FullEditingE2E",
                fixture_sha256,
            )

        device.assert_has_calls(
            [
                call.tap_until_visible("home-new-runner", "Select Build Method"),
                call.tap("dialog-action-create-character", scroll=True),
                call.wait(
                    "dialog-action-complete-new-character-workflow",
                    timeout=45,
                    scroll=True,
                ),
                call.tap("dialog-action-complete-new-character-workflow", scroll=True),
                call.wait("creation-wizard-dashboard", timeout=90),
                call.capture("new-runner-creation-wizard"),
                call.tap("build-save-runner"),
                call.wait(
                    "Saved.",
                    timeout=90,
                    scroll=True,
                    max_scrolls=48,
                    scroll_distance_ratio=0.22,
                ),
                call.tap("Home"),
                call.wait("home-open-file", timeout=90),
                call.tap("home-open-file"),
                call.wait("FullEditingE2E", timeout=90),
                call.wait("Continue building", timeout=90),
            ]
        )
        select_document.assert_called_once_with(
            device,
            "career-full-editing-e2e.chum5",
        )
        self.assertEqual(imported, result)

    def test_full_tablet_journey_remains_available_outside_the_phone_beta_lane(self) -> None:
        device = Mock()

        with patch.object(DRIVER, "select_android_document") as select_document:
            result = DRIVER.prepare_full_editing_runner(
                device,
                "tablet",
                "career-full-editing-e2e.chum5",
                "FullEditingE2E",
                "a" * 64,
            )

        device.assert_has_calls(
            [
                call.tap_until_visible("home-new-runner", "Select Build Method"),
                call.tap("dialog-action-create-character", scroll=True),
                call.wait(
                    "dialog-action-complete-new-character-workflow",
                    timeout=45,
                    scroll=True,
                ),
                call.tap("dialog-action-complete-new-character-workflow", scroll=True),
                call.wait("Continue building", timeout=90),
                call.wait("home-open-file", timeout=90),
                call.tap("home-open-file"),
                call.wait("FullEditingE2E", timeout=90),
                call.wait("Continue building", timeout=90),
            ]
        )
        self.assertIsNone(result)
        device.capture.assert_not_called()
        self.assertNotIn(call.tap("Home"), device.mock_calls)
        select_document.assert_called_once_with(
            device,
            "career-full-editing-e2e.chum5",
        )

    def test_generic_driver_keeps_the_deferred_tablet_profile(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        self.assertIn('choices=("phone", "tablet")', source)

    def test_full_receipt_binds_completed_fixture_and_exact_restart_identity(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        receipt = source[source.rindex("    receipt = {") :]

        for marker in (
            '"inputFixture": str(args.full_editing_runner.resolve())',
            '"inputFixtureSha256": full_editing_runner_sha256',
            '"verifiedRemoteInputFixtureSha256": verified_remote_sha256[',
            '"importAuthority": optional_workspace_authority_json(imported_authority)',
            '"preRestartAuthority": optional_workspace_authority_json(persisted_authority)',
            '"postRestartAuthority": optional_workspace_authority_json(restored_authority)',
            '"frozenFixtureSha256": full_editing_runner_sha256',
            '"initialLaunchProcessIds": list(initial_launch_state.process_ids)',
            '"initialLaunchResumedComponent": initial_launch_state.resumed_component',
            '"preForceStopProcessIds": list(restart_proof.before_force_stop.process_ids)',
            '"preForceStopResumedComponent": restart_proof.before_force_stop.resumed_component',
            '"postForceStopProcessIds": list(restart_proof.after_force_stop.process_ids)',
            '"restartProcessIds": list(restart_proof.restarted.process_ids)',
            '"restartResumedComponent": restart_proof.restarted.resumed_component',
            '"newRunnerCreationCompletion": "not-claimed"',
            '"newRunnerCreationDraftSaved": (',
            '"pass" if args.profile == "phone" else "not-claimed-tablet-deferred"',
            '"phoneCreationWizardDashboard": (',
            '"pass" if args.profile == "phone" else "not-applicable-tablet-deferred"',
            '"careerRunnerImport": "pass"',
            '"careerRunnerAliasActivated": "FullEditingE2E"',
            '"careerAttributeImprovePersisted": "pass"',
            '"careerAttributeTransition": {',
            '"initialTotal": full_editing_contract.initial_body_total',
            '"improvedTotal": full_editing_contract.improved_body_total',
            '"improvementCost": full_editing_contract.improvement_cost',
            '"remainingKarma": full_editing_contract.remaining_karma',
            '"processRestartPersistence": "pass"',
        ):
            self.assertIn(marker, receipt)

    def test_post_restart_attribute_section_returns_to_build_before_gear(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        marker = (
            "assert_body_total(device, args.profile, "
            "full_editing_contract.improved_body_total)"
        )
        post_restart = source[source.rindex(marker) :]
        route = post_restart[: post_restart.index("open_gear_section(device, args.profile)")]
        self.assertIn('if args.profile == "phone":\n        device.back()', route)

    def test_full_editing_fixture_has_valid_career_body_improvement(self) -> None:
        fixture = REPO_ROOT / "tests" / "fixtures" / "career-full-editing-e2e.chum5"
        self.assertEqual(
            DRIVER.FullEditingFixtureContract(
                initial_body_total=1,
                improved_body_total=2,
                improvement_cost=10,
                initial_karma=35,
                remaining_karma=25,
                next_improvement_cost=15,
            ),
            DRIVER.validate_full_editing_fixture(fixture),
        )

    def test_sparse_condition_fixture_is_rejected_for_full_editing(self) -> None:
        sparse_fixture = (
            REPO_ROOT / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5"
        )
        with self.assertRaises(RuntimeError):
            DRIVER.validate_full_editing_fixture(sparse_fixture)

    def test_full_fixture_is_validated_before_transport_or_app_install(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        validation = source.index(
            "validate_full_editing_fixture(args.full_editing_runner.resolve())"
        )
        fixture_transport = source.index("    fixture_inputs = (")
        app_install = source.index('        [str(args.adb), "-s", args.serial, "install"')
        self.assertLess(validation, fixture_transport)
        self.assertLess(fixture_transport, app_install)

        generator = (
            REPO_ROOT / "scripts" / "materialize_chummer5_editability_inventory.py"
        ).read_text(encoding="utf-8")
        self.assertIn(
            'REPO_ROOT / "tests" / "fixtures" / "career-full-editing-e2e.chum5"',
            generator,
        )

    def test_exact_career_attribute_text_rejects_prefix_only_match(self) -> None:
        device = Mock()
        device.wait.return_value = DRIVER.UiNode(
            {"text": "Improve · 10 Karma unexpected"}
        )
        with self.assertRaisesRegex(RuntimeError, "Expected exact career attribute text"):
            DRIVER.wait_exact_text(device, "Improve · 10 Karma", timeout=45)
        device.capture.assert_called_once_with("career-attribute-text-mismatch")

    def test_local_import_does_not_claim_success_for_a_guarded_workspace(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        block = source[source.index("public async Task OpenLocalAsync") :]
        block = block[: block.index("public async Task OpenOnlineAsync")]

        self.assertIn("CharacterOverviewState previousState = State;", block)
        self.assertIn("_notice = null;", block)
        self.assertIn("ComputeExactImportPayloadSha256(document.Content)", block)
        self.assertIn(
            "if (ActivatedNewWorkspace(previousState, State)",
            block,
        )
        guarded = block[block.index("if (ActivatedNewWorkspace") :]
        self.assertIn("TryRefreshWorkspaceAuthorityAsync", guarded)
        self.assertIn("if (authority is not null)", guarded)
        self.assertIn("RememberRosterLocator", guarded)
        self.assertIn('_notice = $"Opened {document.DisplayName}.";', guarded)
        self.assertNotIn(
            'await _presenter.ImportAsync(\n'
            '                WorkspaceImportDocument.FromUtf8Bytes(document.Content, string.Empty, WorkspaceDocumentFormat.NativeXml),\n'
            '                cancellationToken);\n'
            '            RememberRosterLocator',
            block,
        )

        predicate = block[block.index("private static bool ActivatedNewWorkspace") :]
        self.assertIn("current.WorkspaceId is { } currentWorkspace", predicate)
        self.assertIn("!string.Equals(previousWorkspace.Value, currentWorkspace.Value", predicate)
        self.assertNotIn("ContentRevision", predicate)
        self.assertNotIn("SavedRevision", predicate)

    def test_authoritative_import_and_restart_contracts_fail_closed(self) -> None:
        imported = DRIVER.WorkspaceAuthority("new", 4, 4, "a" * 64, "b" * 64)
        DRIVER.require_import_authority(imported, "a" * 64, "old")
        DRIVER.require_saved_authority(imported)
        DRIVER.require_restored_authority(imported, imported)

        with self.assertRaisesRegex(RuntimeError, "exact verified fixture bytes"):
            DRIVER.require_import_authority(imported, "c" * 64, "old")
        with self.assertRaisesRegex(RuntimeError, "new target workspace"):
            DRIVER.require_import_authority(imported, "a" * 64, "new")
        with self.assertRaisesRegex(RuntimeError, "durably checkpointed"):
            DRIVER.require_saved_authority(imported.__class__("new", 5, 4, "a" * 64, "b" * 64))
        with self.assertRaisesRegex(RuntimeError, "does not match"):
            DRIVER.require_restored_authority(
                imported,
                imported.__class__("new", 4, 4, "a" * 64, "c" * 64),
            )

    def test_workspace_authority_surface_requires_two_identical_reads(self) -> None:
        authority = DRIVER.WorkspaceAuthority("new", 4, 4, "a" * 64, "b" * 64)
        device = Mock()
        with patch.object(
            DRIVER,
            "_read_workspace_authority_once",
            side_effect=[authority, authority],
        ), patch.object(DRIVER, "reset_scroll_to_top") as reset:
            self.assertEqual(authority, DRIVER.read_workspace_authority(device))
        self.assertEqual(2, reset.call_count)

        changed = authority.__class__("new", 5, 5, "c" * 64, "d" * 64)
        with patch.object(
            DRIVER,
            "_read_workspace_authority_once",
            side_effect=[authority, changed],
        ), patch.object(DRIVER, "reset_scroll_to_top"):
            with self.assertRaisesRegex(RuntimeError, "changed during verification"):
                DRIVER.read_workspace_authority(device)
        device.capture.assert_called_with("workspace-authority-surface-changed")

    def test_workspace_authority_is_runtime_opt_in_and_epoch_bound(self) -> None:
        coordinator = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        home = (
            REPO_ROOT / "src" / "Chummer.Android" / "Native" / "HomePage.cs"
        ).read_text(encoding="utf-8")
        activity = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Platforms"
            / "Android"
            / "MainActivity.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("IChummerClient client", coordinator)
        self.assertIn("using Chummer.Presentation;", coordinator)
        self.assertIn("_client = client;", coordinator)
        self.assertIn("await _client.GetWorkspaceAsync(workspaceId, cancellationToken)", coordinator)
        self.assertEqual(2, coordinator.count("await _client.GetWorkspaceAsync(workspaceId, cancellationToken)"))
        self.assertIn("AuthoritySnapshotsMatch(first, verified)", coordinator)
        self.assertIn("authorityEpoch != _workspaceAuthorityEpoch", coordinator)
        self.assertIn("!authority.Matches(State)", coordinator)
        self.assertIn("public static long Generation", coordinator)
        self.assertIn("_workspaceAuthorityOptInGeneration == AndroidE2EAuthority.Generation", coordinator)
        self.assertIn("optInGeneration == AndroidE2EAuthority.Generation", coordinator)
        self.assertIn("ClearWorkspaceAuthority();\n            return null;", coordinator)
        self.assertIn("WriteInt64BigEndian", coordinator)
        self.assertNotIn("WriteInt32BigEndian", coordinator)
        self.assertIn("if (!AndroidE2EAuthority.Enabled", coordinator)
        self.assertIn("public static event EventHandler? Changed;", coordinator)
        self.assertIn("AndroidE2EAuthority.Changed += OnE2EAuthorityChanged;", coordinator)
        self.assertIn("AndroidE2EAuthority.Changed -= OnE2EAuthorityChanged;", coordinator)
        for resource in (
            "_workspaceActivationGate",
            "_initializeGate",
            "_outputGate",
            "_shellSyncGate",
            "_lifetime",
        ):
            self.assertNotIn(f"{resource}.Dispose();", coordinator)
        self.assertIn("turn an otherwise safe shutdown race into ObjectDisposedException", coordinator)
        self.assertIn("#if DEBUG\n    public NativeWorkspaceAuthoritySnapshot?", coordinator)
        self.assertIn("#if DEBUG\n        AddDebugWorkspaceAuthority();", home)
        debug_surface = home[home.index("private void AddDebugWorkspaceAuthority") :]
        debug_surface = debug_surface[: debug_surface.index("private void AddOnlineSection")]
        self.assertNotIn("ContentUri", debug_surface)
        self.assertNotIn("document.Content", debug_surface)
        self.assertNotIn(".Alias", debug_surface)
        self.assertIn("#if DEBUG", activity)
        self.assertIn(DRIVER.E2E_AUTHORITY_EXTRA, activity)

    def test_proof_refresh_failure_cannot_create_a_false_open_notice_or_locator(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        local = source[source.index("public async Task OpenLocalAsync") :]
        local = local[: local.index("public async Task OpenOnlineAsync")]
        save = source[source.index("public async Task SaveAsync") :]
        save = save[: save.index("public async Task ExportAsync")]
        refresh = source[source.index("private async Task<NativeWorkspaceAuthoritySnapshot?>") :]
        refresh = refresh[: refresh.index("private void ClearWorkspaceAuthority")]

        self.assertLess(local.index("await _presenter.ImportAsync"), local.index("TryRefreshWorkspaceAuthorityAsync"))
        guarded = local[local.index("if (authority is not null)") :]
        self.assertLess(guarded.index("if (authority is not null)"), guarded.index("RememberRosterLocator"))
        self.assertLess(guarded.index("if (authority is not null)"), guarded.index('_notice = $"Opened'))
        self.assertIn("else\n                {\n                    _notice = WorkspaceVerificationUnavailableNotice;", guarded)
        self.assertIn("public string? Notice => _notice ?? State.Notice", source)
        self.assertLess(save.index("await _presenter.SaveAsync"), save.index("TryRefreshWorkspaceAuthorityAsync"))
        self.assertIn("catch (Exception exception) when (exception is not OutOfMemoryException)", refresh)
        self.assertIn("ClearWorkspaceAuthority();", refresh)
        self.assertIn("return null;", refresh)

    def test_online_import_uses_the_same_guarded_activation_contract(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        block = source[source.index("public async Task OpenOnlineAsync") :]
        block = block[: block.index("public async Task CreateRunnerAsync")]

        self.assertIn("CharacterOverviewState previousState = State;", block)
        self.assertIn("_notice = null;", block)
        self.assertIn("await _workspaceActivationGate.WaitAsync(cancellationToken);", block)
        self.assertIn("string expectedPayloadSha256 = Sha256Hex(payload);", block)
        self.assertIn(
            "if (ActivatedNewWorkspace(previousState, State)",
            block,
        )
        guarded = block[block.index("if (ActivatedNewWorkspace") :]
        self.assertIn("TryRefreshWorkspaceAuthorityAsync", guarded)
        self.assertIn("if (authority is not null)", guarded)
        self.assertIn("RememberRosterLocator", guarded)
        self.assertIn(
            '_notice = $"Opened {DisplayName(character.Name, character.Alias)}.";',
            guarded,
        )
        self.assertIn(
            "else\n                {\n                    _notice = WorkspaceVerificationUnavailableNotice;",
            guarded,
        )
        self.assertEqual(2, source.count("_notice = WorkspaceVerificationUnavailableNotice;"))
        self.assertNotIn(
            'cancellationToken);\n'
            '            RememberRosterLocator(',
            block,
        )

    def test_import_buffers_are_allocated_only_after_the_shared_activation_gate(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        local = source[source.index("public async Task OpenLocalAsync") :]
        local = local[: local.index("private static bool ActivatedNewWorkspace")]
        online = source[source.index("public async Task OpenOnlineAsync") :]
        online = online[: online.index("public async Task CreateRunnerAsync")]

        self.assertLess(local.index("_workspaceActivationGate.WaitAsync"), local.index("_documents.OpenAsync"))
        self.assertLess(local.index("_documents.OpenAsync"), local.index("ComputeExactImportPayloadSha256"))
        self.assertLess(local.index("CryptographicOperations.ZeroMemory"), local.index("_workspaceActivationGate.Release"))
        self.assertLess(online.index("_workspaceActivationGate.WaitAsync"), online.index("StrictUtf8.GetBytes"))
        self.assertLess(online.index("StrictUtf8.GetBytes"), online.index("Sha256Hex(payload)"))
        self.assertLess(online.index("CryptographicOperations.ZeroMemory"), online.index("_workspaceActivationGate.Release"))

    def test_explicit_android_activation_entrypoints_share_one_gate(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")

        for method_name, start_marker, next_method in (
            ("ConfirmCreationPrerequisiteAsync", "ConfirmCreationPrerequisiteAsync(", "ConfirmCreationPrerequisiteCoreAsync"),
            ("ConfirmCreationFoundationAsync", "public async Task<CharacterCreationFoundationInteractionConfirmResult> ConfirmCreationFoundationAsync(", "ConfirmCreationFoundationCoreAsync"),
            ("SwitchWorkspaceAsync", "public async Task SwitchWorkspaceAsync", "CloseWorkspaceAsync"),
            ("CloseWorkspaceAsync", "public async Task CloseWorkspaceAsync", "private async Task WithWorkspaceActivationGateAsync"),
            ("EraseAccountAsync", "public async Task<NativeAccountErasureResult> EraseAccountAsync", "EraseAccountCoreAsync"),
            ("ExecuteCommandAsync", "public async Task ExecuteCommandAsync", "ExecuteCommandCoreAsync"),
            ("ExecuteDialogActionAsync", "public async Task ExecuteDialogActionAsync", "ExecuteDialogActionCoreAsync"),
            ("ExecuteWorkspaceActionAsync", "public async Task ExecuteWorkspaceActionAsync", "ExecuteWorkspaceActionCoreAsync"),
            ("ApplyAttributeEditAsync", "public async Task ApplyAttributeEditAsync", "ApplyAttributeEditCoreAsync"),
            ("ApplyOriginDossierEditAsync", "public async Task ApplyOriginDossierEditAsync", "ApplyOriginDossierEditCoreAsync"),
            ("ApplyCollectionMutationAsync", "public async Task ApplyCollectionMutationAsync", "ApplyCollectionMutationCoreAsync"),
            ("ApplyConditionMonitorEditAsync", "public async Task ApplyConditionMonitorEditAsync", "ApplyConditionMonitorEditCoreAsync"),
            ("ApplyPrimaryArmEditAsync", "public async Task ApplyPrimaryArmEditAsync", "ApplyPrimaryArmEditCoreAsync"),
        ):
            block = source[source.index(start_marker) :]
            block = block[: block.index(next_method, len(method_name))]
            self.assertIn("WithWorkspaceActivationGateAsync", block, method_name)

        create = source[source.index("public async Task CreateRunnerAsync") :]
        create = create[: create.index("public async Task SwitchWorkspaceAsync")]
        self.assertIn('ExecuteCommandAsync("new_character", cancellationToken)', create)
        self.assertNotIn("WithWorkspaceActivationGateAsync", create)

        initialize = source[source.index("public async Task InitializeAsync") :]
        initialize = initialize[: initialize.index("public async Task OpenLocalAsync")]
        self.assertLess(
            initialize.index("_workspaceActivationGate.WaitAsync"),
            initialize.index("_presenter.InitializeAsync"),
        )
        helper = source[source.index("private async Task WithWorkspaceActivationGateAsync") :]
        helper = helper[: helper.index("public bool IsRosterFavorite")]
        self.assertIn("finally", helper)
        self.assertIn("_workspaceActivationGate.Release();", helper)

    def test_authority_capture_uses_the_shared_presentation_operation_coordinator(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        maui = (
            REPO_ROOT / "src" / "Chummer.Android" / "MauiProgram.cs"
        ).read_text(encoding="utf-8")
        refresh = source[source.index("private async Task<NativeWorkspaceAuthoritySnapshot?>") :]
        refresh = refresh[: refresh.index("private void ClearWorkspaceAuthority")]

        registration = (
            "builder.Services.AddSingleton<IWorkspaceOperationCoordinator, "
            "WorkspaceOperationCoordinator>();"
        )
        self.assertIn(registration, maui)
        self.assertLess(
            maui.index(registration),
            maui.index("AddSingleton<ICharacterOverviewPresenter, CharacterOverviewPresenter>()"),
        )
        self.assertLess(
            maui.index(registration),
            maui.index("AddSingleton<RunnerSessionCoordinator>()"),
        )
        self.assertIn("IWorkspaceOperationCoordinator workspaceOperationCoordinator", source)
        self.assertIn("_workspaceOperationCoordinator = workspaceOperationCoordinator;", source)
        self.assertIn("_workspaceOperationCoordinator.RunCurrentAsync(", refresh)
        run_current = refresh[refresh.index("_workspaceOperationCoordinator.RunCurrentAsync(") :]
        self.assertLess(run_current.index("ReadWorkspaceAuthorityAsync"), run_current.index("RequirePayloadDigest"))
        self.assertLess(run_current.index("RequirePayloadDigest"), run_current.index("linked.Token"))
        self.assertIn("|| !execution.HasValue", run_current)
        self.assertIn("|| execution.Value is not { } authority", run_current)
        self.assertLess(run_current.index("!execution.CanPublish"), run_current.index("execution.Value"))
        self.assertIn("ClearWorkspaceAuthority();\n                return null;", run_current)

        for method_name, presenter_operation in (
            ("InitializeAsync", "_presenter.InitializeAsync"),
            ("OpenLocalAsync", "_presenter.ImportAsync"),
            ("OpenOnlineAsync", "_presenter.ImportAsync"),
            ("SwitchWorkspaceAsync", "_presenter.SwitchWorkspaceAsync"),
            ("CloseWorkspaceAsync", "_presenter.CloseWorkspaceAsync"),
            ("SaveAsync", "_presenter.SaveAsync"),
        ):
            block = source[source.index(f"public async Task {method_name}") :]
            next_public = block.find("\n    public ", 10)
            if next_public >= 0:
                block = block[:next_public]
            self.assertLess(
                block.index(presenter_operation),
                block.index("TryRefreshWorkspaceAuthorityAsync"),
                method_name,
            )

    def test_opt_in_changes_clear_then_rerender_and_refresh_without_static_leaks(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        handler = source[source.index("private void OnE2EAuthorityChanged") :]
        handler = handler[: handler.index("private void NotifyChanged")]

        self.assertLess(handler.index("ClearWorkspaceAuthority();"), handler.index("NotifyChanged();"))
        self.assertIn("if (AndroidE2EAuthority.Enabled && !_disposed)", handler)
        self.assertIn("RefreshWorkspaceAuthorityForOptInAsync", handler)
        self.assertIn("expectedWorkspaceId: State.WorkspaceId", handler)
        self.assertIn("_lifetime.Token", handler)
        self.assertIn("catch (Exception exception) when (exception is not OutOfMemoryException)", handler)

    def test_native_dialog_rebuilds_from_state_shape_changes_not_a_dialog_allowlist(self) -> None:
        source = (
            REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs"
        ).read_text(encoding="utf-8")
        start = source.index("private async Task UpdateFieldAsync")
        end = source.index("private async Task ExecuteAsync", start)
        block = source[start:end]

        self.assertIn("DesktopDialogState? previous", block)
        self.assertIn("RequiresStructuralRerender(previous, next, fieldId)", block)
        self.assertIn("FieldShapeMatches", block)
        self.assertIn("OptionsMatch", block)
        self.assertNotIn("dialog.new_character.priority_workflow", block)
        self.assertNotIn("dialog.new_character.karma_workflow", block)

    def test_collection_openers_use_overlapping_search_below_the_action_list(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name, action, quick_add in (
            ("open_contact_section", "build-action-tab-relationships-contacts", "section-quick-contact-add"),
            ("open_pet_section", "build-action-tab-relationships-pets", "section-quick-contact-add"),
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertIn("_open_phone_relationship_collection(", block)
            self.assertIn(f'"{action}"', block)
            self.assertIn(f'"{quick_add}"', block)

        helper = source[source.index("def _open_phone_relationship_collection") :]
        helper = helper[: helper.index("\ndef ", 5)]
        self.assertEqual(2, helper.count("device.tap_bidirectional("))
        self.assertEqual(2, helper.count("exact_resource_id=True"))
        self.assertIn('"build-section-tab-relationships"', helper)
        self.assertIn("forward_scrolls=48", helper)
        self.assertIn("device.wait_exact_resource_id_bidirectional(", helper)
        self.assertIn("reset_scroll_to_top(device, swipes=24)", helper)
        self.assertIn("max_scrolls=24", helper)
        empty_marker = "No entries yet. Use an action above to add one."
        self.assertIn(empty_marker, helper)
        self.assertLess(
            helper.index(empty_marker),
            helper.index("device.wait_exact_resource_id_bidirectional("),
        )

    def test_relationship_openers_wait_for_fixture_items_instead_of_quick_add(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name, phone_action, tablet_action in (
            (
                "open_contact_section",
                "build-action-tab-relationships-contacts",
                "tablet-build-action-tab-relationships-contacts",
            ),
            (
                "open_pet_section",
                "build-action-tab-relationships-pets",
                "tablet-build-action-tab-relationships-pets",
            ),
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertIn("expected_item: str | None = None", block)
            self.assertIn("_open_phone_relationship_collection(", block)
            self.assertIn(f'"{phone_action}"', block)
            self.assertIn(f'"{tablet_action}"', block)
            self.assertIn("expected_item=expected_item", block)
            self.assertNotIn("device.swipe_up(", block)
            self.assertIn("max_scrolls=8", block)
            self.assertIn("time.sleep(2)", block)
            self.assertIn("time.sleep(5)", block)
            self.assertIn("timeout=180", block)

    def test_tablet_section_openers_fully_reset_the_long_build_rail(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name in (
            "open_attribute_section",
            "open_gear_section",
            "open_contact_section",
            "open_pet_section",
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertIn(
                "reset_scroll_to_top(device, x_ratio=0.15, swipes=24)",
                block,
            )

    def test_phone_gear_save_resets_the_editor_before_persistence_assertion(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        phone_save = source[source.index('device.set_text("collection-field-customname"') :]
        phone_save = phone_save[: phone_save.index("device.back()")]

        self.assertIn("reset_scroll_to_top(device, swipes=6)", phone_save)
        self.assertLess(
            phone_save.index("reset_scroll_to_top(device, swipes=6)"),
            phone_save.index('device.assert_text("GearProofE2E")'),
        )

    def test_contact_and_pet_editors_reset_before_field_mutations(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name in (
            "add_and_edit_contact",
            "assert_contact_persisted",
            "add_and_edit_pet",
            "assert_pet_persisted",
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertLess(
                block.index("tap_collection_item(device,"),
                block.index("reset_collection_editor_to_top(device, profile)"),
            )

    def test_contact_journey_uses_fixture_bounds_and_saves_ratings_before_toggles(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        block = source[source.index("def add_and_edit_contact") :]
        block = block[: block.index("\ndef ", 5)]
        self.assertIn("connection_maximum: int = 6", block)
        self.assertIn('f"Connection · 1–{connection_maximum}",', block)
        self.assertIn("str(connection_maximum + 1)", block)
        self.assertIn("str(connection_maximum)", block)
        ratings_save = block.index("device.tap(save, scroll=True)")
        toggle_batch = block.index(
            'for toggle in ("group", "free", "family", "blackmail")'
        )
        self.assertLess(
            ratings_save,
            toggle_batch,
        )

    def test_contact_connection_bound_matches_creation_and_career_semantics(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_pet = source[source.index('if args.journey == "contact-pet":') :]
        contact_pet = contact_pet[: contact_pet.index('if args.journey == "condition-monitor":')]
        full_journey = source[source.index("    add_and_edit_gear(device, args.profile)") :]

        self.assertIn(
            "add_and_edit_contact(device, args.profile, create_items=False)",
            contact_pet,
        )
        self.assertNotIn("connection_maximum=12", contact_pet)
        self.assertIn("assert_contact_persisted(device, args.profile)", contact_pet)
        self.assertIn(
            "add_and_edit_contact(device, args.profile, connection_maximum=12)",
            full_journey,
        )
        self.assertIn(
            "assert_contact_persisted(device, args.profile, connection_maximum=12)",
            full_journey,
        )

    def test_contact_connection_invalid_probe_save_and_readback_use_the_active_runner_bound(self) -> None:
        persisted_fields = [
            "ContactPersistedE2E",
            "ContactNotesE2E",
            "FixerE2E",
            "ViennaE2E",
            "ElfE2E",
            "NonbinaryE2E",
            "42",
            "ProfessionalE2E",
            "CredstickE2E",
            "UrbanExplorerE2E",
            "PrivateE2E",
        ]
        for maximum, invalid in ((6, "7"), (12, "13")):
            with self.subTest(maximum=maximum):
                device = Mock(spec=DRIVER.Device)
                device.find.return_value = None
                device.wait.return_value = DRIVER.UiNode({"checked": "true"})
                with (
                    patch.object(DRIVER, "open_contact_section"),
                    patch.object(DRIVER, "tap_collection_item"),
                    patch.object(DRIVER, "reset_collection_editor_to_top"),
                    patch.object(DRIVER, "ensure_checked"),
                    patch.object(DRIVER.time, "sleep"),
                ):
                    DRIVER.add_and_edit_contact(
                        device,
                        "phone",
                        create_items=False,
                        connection_maximum=maximum,
                    )

                connection_calls = [
                    observed
                    for observed in device.set_text.call_args_list
                    if observed.args[0] == "collection-contact-connection-"
                ]
                self.assertEqual(2, len(connection_calls))
                self.assertEqual(
                    (
                        "collection-contact-connection-",
                        f"Connection · 1–{maximum}",
                        invalid,
                    ),
                    connection_calls[0].args,
                )
                self.assertEqual(str(maximum), connection_calls[1].args[2])

                with (
                    patch.object(DRIVER, "open_contact_section"),
                    patch.object(DRIVER, "tap_collection_item"),
                    patch.object(DRIVER, "reset_collection_editor_to_top"),
                    patch.object(
                        DRIVER,
                        "selected_text",
                        side_effect=[*persisted_fields, str(maximum)],
                    ) as selected_text,
                ):
                    DRIVER.assert_contact_persisted(
                        device,
                        "phone",
                        connection_maximum=maximum,
                    )

                self.assertEqual(
                    f"Connection · 1–{maximum}",
                    selected_text.call_args_list[-1].args[2],
                )

                if maximum == 12:
                    with (
                        patch.object(DRIVER, "open_contact_section"),
                        patch.object(DRIVER, "tap_collection_item"),
                        patch.object(DRIVER, "reset_collection_editor_to_top"),
                        patch.object(
                            DRIVER,
                            "selected_text",
                            side_effect=[*persisted_fields, "6"],
                        ),
                    ):
                        with self.assertRaisesRegex(
                            RuntimeError,
                            "expected '12', got '6'",
                        ):
                            DRIVER.assert_contact_persisted(
                                device,
                                "phone",
                                connection_maximum=12,
                            )
                    device.capture.assert_called_with(
                        "phone-contact-connection-12-not-persisted"
                    )

    def test_restart_gear_proof_reads_the_persisted_custom_name_field(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        restart = source[source.index('device.shell("am", "force-stop", PACKAGE)') :]
        self.assertIn('tap_collection_item(device, "Ares Predator V")', restart)
        self.assertIn('selected_text(device, gear_field, "Custom Name", scroll=True)', restart)
        self.assertIn('persisted_custom_name != "GearProofE2E"', restart)
        self.assertNotIn('tap_collection_item(device, "GearProofE2E")', restart)

    def test_unlink_restore_uses_the_waited_name_node_without_recapturing(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        unlink = source[source.index("def assert_link_persisted_then_remove") :]
        unlink = unlink[: unlink.index("def add_and_edit_gear")]

        self.assertIn('restored = name_node.attributes.get("text", "")', unlink)
        self.assertNotIn(
            'selected_text(device, name_selector, "Name", scroll=True)',
            unlink,
        )

    def test_contact_dialog_resets_after_locating_its_bottom_action(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_add = source[source.index("def add_contact_from_dialog") :]
        contact_add = contact_add[: contact_add.index("def add_and_edit_contact")]

        action_wait = contact_add.index('"dialog-action-add",')
        reset = contact_add.index("reset_scroll_to_top(device")
        name_edit = contact_add.index('device.set_text("dialog-field-uicontactname"')
        self.assertLess(action_wait, reset)
        self.assertLess(reset, name_edit)
        self.assertIn("max_scrolls=48", contact_add)
        self.assertIn("scroll_distance_ratio=0.28", contact_add)
        self.assertIn("swipes=24", contact_add)

    def test_tablet_gear_editor_resets_inspector_before_custom_name(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        gear_edit = source[source.index("def add_and_edit_gear") :]
        gear_edit = gear_edit[: gear_edit.index("def add_contact_from_dialog")]

        save_wait = gear_edit.index('device.wait("tablet-inspector-save"')
        reset = gear_edit.index("reset_scroll_to_top(device, x_ratio=0.82", save_wait)
        custom_name = gear_edit.index('device.set_text("tablet-field-customname"')
        self.assertLess(save_wait, reset)
        self.assertLess(reset, custom_name)

    def test_tablet_gear_save_reads_back_exact_custom_name_field(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        gear_edit = source[source.index("def add_and_edit_gear") :]
        gear_edit = gear_edit[: gear_edit.index("def add_contact_from_dialog")]

        self.assertIn(
            'selected_text(\n            device,\n            "tablet-field-customname",',
            gear_edit,
        )
        self.assertIn('saved_custom_name != "GearProofE2E"', gear_edit)
        tablet_branch = gear_edit[: gear_edit.index("return")]
        self.assertNotIn('device.assert_text("GearProofE2E")', tablet_branch)

    def test_keyboard_visibility_requires_an_active_ime(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.input_method_output = "mImeWindowVis=0\n      mInputShown=false"
            self.assertFalse(device.keyboard_visible())

            device.input_method_output = "mImeWindowVis=0x3\n      mInputShown=true"
            self.assertTrue(device.keyboard_visible())

    def test_text_entry_uses_escape_to_hide_ime_without_navigating_back(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.input_method_output = "mImeWindowVis=0x3\n      mInputShown=true"
            device.hide_keyboard_on_escape = True
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "resource-id": "app:id/collection-field-name-id",
                        "text": "Old",
                        "clickable": "true",
                        "bounds": "[100,400][900,520]",
                    }
                )
            ]

            device.set_text("collection-field-name", "Name", "New")

        self.assertIn(("input", "keyevent", "111"), device.commands)
        self.assertNotIn(("input", "tap", "162", "2350"), device.commands)
        self.assertNotIn(("input", "keyevent", "4"), device.commands)

    def test_input_nodes_in_the_system_navigation_zone_are_not_tappable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            safe = DRIVER.UiNode({"bounds": "[53,1727][1028,1831]"})
            navigation_zone = DRIVER.UiNode({"bounds": "[53,2190][1028,2400]"})
            collapsed = DRIVER.UiNode({"bounds": "[53,2400][1028,2400]"})

            self.assertTrue(device.input_node_is_tappable(safe))
            self.assertFalse(device.input_node_is_tappable(navigation_zone))
            self.assertFalse(device.input_node_is_tappable(collapsed))

    def test_clipped_or_inverted_action_bounds_are_not_tappable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 2560x1800")
            safe = DRIVER.UiNode({"bounds": "[88,1500][504,1600]"})
            clipped = DRIVER.UiNode({"bounds": "[88,1695][504,1688]"})
            system_zone = DRIVER.UiNode({"bounds": "[88,1740][504,1800]"})

            self.assertTrue(device.node_has_tappable_bounds(safe))
            self.assertFalse(device.node_has_tappable_bounds(clipped))
            self.assertFalse(device.node_has_tappable_bounds(system_zone))

    def test_empty_text_entry_clears_selection_without_invalid_adb_text_call(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "resource-id": "app:id/collection-field-name-id",
                        "text": "Old",
                        "clickable": "true",
                        "bounds": "[100,400][900,520]",
                    }
                )
            ]

            device.set_text("collection-field-name", "Name", "")

        self.assertIn(("input", "keyevent", "67"), device.commands)
        self.assertNotIn(("input", "text", ""), device.commands)

    def test_dense_contact_edits_use_overlapping_scrolls(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_edit = source[source.index("def add_and_edit_contact") :]
        contact_edit = contact_edit[: contact_edit.index("def assert_contact_persisted")]

        self.assertIn("scroll_distance_ratio=0.22", contact_edit)
        self.assertIn("max_scrolls=20", contact_edit)
        rating_block = contact_edit[contact_edit.index("connection_selector =") :]
        self.assertLess(
            rating_block.index("reset_scroll_to_top("),
            rating_block.index("device.set_text("),
        )

    def test_toggle_search_uses_overlapping_scrolls(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        ensure_checked = source[source.index("def ensure_checked") :]
        ensure_checked = ensure_checked[: ensure_checked.index("def selected_text")]

        self.assertIn("max_scrolls=20", ensure_checked)
        self.assertIn("scroll_distance_ratio=0.22", ensure_checked)
        self.assertIn("device.tap(", ensure_checked)
        self.assertIn('device.capture("toggle-state-failed")', ensure_checked)
        self.assertNotIn('device.shell("input", "tap"', ensure_checked)

    def test_text_field_scrolling_waits_for_android_ui_tree_to_settle(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        set_text = source[source.index("    def set_text(") :]
        set_text = set_text[: set_text.index("    def input_node_is_tappable(")]

        swipe = set_text.index("self.swipe_up(")
        settle = set_text.index("time.sleep(0.75)", swipe)
        self.assertLess(swipe, settle)

    def test_contact_connection_correction_resets_after_validation_dialog(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_edit = source[source.index("def add_and_edit_contact") :]
        contact_edit = contact_edit[: contact_edit.index("def assert_contact_persisted")]
        correction = contact_edit[contact_edit.index('device.tap("OK")') :]

        self.assertLess(
            correction.index("reset_scroll_to_top("),
            correction.index("device.set_text("),
        )

    def test_contact_group_toggle_resets_after_first_successful_save(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_edit = source[source.index("def add_and_edit_contact") :]
        contact_edit = contact_edit[: contact_edit.index("def assert_contact_persisted")]
        first_save = contact_edit.index("device.tap(save, scroll=True)")
        reset = contact_edit.index("reset_scroll_to_top(", first_save)
        toggle_batch = contact_edit.index(
            'for toggle in ("group", "free", "family", "blackmail")',
            reset,
        )

        self.assertLess(first_save, reset)
        self.assertLess(reset, toggle_batch)

    def test_phone_contact_and_pet_wait_for_collection_after_save(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name, persisted_name in (
            ("add_and_edit_contact", "ContactPersistedE2E"),
            ("add_and_edit_pet", "PetPersistedE2E"),
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            phone_return = block[block.index('if profile == "phone":') :]
            self.assertIn("device.back()", phone_return)
            self.assertIn("reset_scroll_to_top(device, swipes=12)", phone_return)
            self.assertIn(f'device.wait("{persisted_name}", timeout=60, scroll=True)', phone_return)

    def test_restart_contact_toggle_checks_reset_and_scroll_in_document_order(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        block = source[source.index("def assert_contact_persisted") :]
        block = block[: block.index("\ndef ", 5)]
        toggle_loop = block.index('for toggle in ("group", "free", "family", "blackmail")')
        self.assertLess(
            block.index("reset_collection_editor_to_top(device, profile)"),
            toggle_loop,
        )
        self.assertIn("max_scrolls=24", block[toggle_loop:])
        self.assertIn("scroll_distance_ratio=0.22", block[toggle_loop:])

    def test_document_picker_opens_downloads_when_fixture_is_not_recent(self) -> None:
        device = Mock()
        device.find.return_value = None

        DRIVER.select_android_document(device, "linked-runner-e2e.chum5")

        self.assertEqual(
            [
                call("Show roots", timeout=45),
                call("Downloads", timeout=45),
                call("Files in Downloads", timeout=45),
                call("linked-runner-e2e.chum5", timeout=45, scroll=True),
            ],
            device.wait.call_args_list,
        )
        self.assertEqual(
            [
                call("Show roots"),
                call("Downloads"),
                call("linked-runner-e2e.chum5", scroll=True),
            ],
            device.tap.call_args_list,
        )

    def test_document_picker_uses_visible_fixture_without_changing_root(self) -> None:
        device = Mock()
        device.find.side_effect = lambda selector: (
            object() if selector == "linked-runner-e2e.chum5" else None
        )

        DRIVER.select_android_document(device, "linked-runner-e2e.chum5")

        device.wait.assert_not_called()
        device.tap.assert_called_once_with("linked-runner-e2e.chum5", scroll=True)

    def test_document_picker_closes_tablet_roots_drawer_before_fixture_tap(self) -> None:
        device = Mock()
        device.find.side_effect = lambda selector: (
            object()
            if selector in {
                "Recent",
                "Documents",
                "invalid-linked-runner-e2e.chum5",
            }
            else None
        )
        device.display_size.return_value = (2560, 1800)

        with patch.object(DRIVER.time, "sleep") as sleep:
            DRIVER.select_android_document(device, "invalid-linked-runner-e2e.chum5")

        device.shell.assert_called_once_with("input", "tap", "1920", "900")
        sleep.assert_called_once_with(0.75)
        device.tap.assert_called_once_with(
            "invalid-linked-runner-e2e.chum5",
            scroll=True,
        )

    def test_linked_identity_resets_editor_before_reading_top_fields(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        linked_identity = source[source.index("def assert_linked_identity") :]
        linked_identity = linked_identity[: linked_identity.index("def select_android_document")]

        self.assertLess(
            linked_identity.index("reset_scroll_to_top("),
            linked_identity.index("for selector, label, value in expected:"),
        )
        self.assertNotIn("device.wait(selector, scroll=True)", linked_identity)

    def test_persisted_field_reader_requires_exact_selector(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        selected_text = source[source.index("def selected_text") :]
        selected_text = selected_text[: selected_text.index("def assert_linked_identity")]

        self.assertIn("node = device.find(selector)", selected_text)
        self.assertNotIn("field_after_label=label", selected_text)

    def test_swipe_down_stays_inside_phone_display(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.swipe_down()

        self.assertEqual(
            ("input", "swipe", "540", "720", "540", "1968", "300"),
            device.commands[-1],
        )

    def test_tablet_selectors_scroll_their_own_panes(self) -> None:
        self.assertEqual(
            0.15,
            DRIVER.Device._scroll_x_ratio("tablet-build-tab-tab-relationships"),
        )
        self.assertEqual(
            0.15,
            DRIVER.Device._scroll_x_ratio("tablet-build-action-tab-gear-gear"),
        )
        self.assertEqual(
            0.15,
            DRIVER.Device._scroll_x_ratio("tablet-quick-contact-add"),
        )
        self.assertEqual(
            0.82,
            DRIVER.Device._scroll_x_ratio("tablet-inspector-save"),
        )
        self.assertEqual(
            0.82,
            DRIVER.Device._scroll_x_ratio("tablet-attribute-base-body"),
        )

    def test_tablet_attribute_route_uses_visible_inspector(self) -> None:
        class AttributeRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))

            def wait(self, selector: str, **_: object) -> None:
                self.calls.append(("wait", selector))

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

        device = AttributeRouteDevice()
        DRIVER.open_attribute_section(device, "tablet")

        self.assertEqual(
            ([
                ("swipe", "down"),
            ] * 24) + [
                ("tap", "tablet-build-tab-tab-attributes"),
                ("swipe", "down"),
                ("swipe", "down"),
                ("wait", "tablet-attribute-body"),
                ("tap", "tablet-attribute-body"),
            ],
            device.calls,
        )

    def test_phone_attribute_route_uses_auto_loaded_default_action(self) -> None:
        class AttributeRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []
                self.row_wait_options: dict[str, object] = {}

            def tap_bidirectional(self, selector: str, **_: object) -> None:
                self.calls.append(("tap_bidirectional", selector))

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

            def wait(self, selector: str, **options: object) -> None:
                self.calls.append(("wait", selector))
                if selector == "attribute-body":
                    self.row_wait_options = options

        device = AttributeRouteDevice()
        with patch.object(DRIVER.time, "sleep"):
            DRIVER.open_attribute_section(device, "phone")

        self.assertEqual(
            [
                ("tap_bidirectional", "build-section-tab-attributes"),
                ("swipe", "down"),
                ("swipe", "down"),
                ("wait", "attribute-body"),
            ],
            device.calls,
        )
        self.assertEqual(
            {
                "timeout": 120,
                "scroll": True,
                "max_scrolls": 24,
                "scroll_distance_ratio": 0.22,
            },
            device.row_wait_options,
        )

    def test_career_body_improvement_binds_rendered_total_and_karma(self) -> None:
        class CareerAttributeDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []
                self.total = 1

            def tap_bidirectional(self, selector: str, **_: object) -> None:
                self.calls.append(("tap_bidirectional", selector))

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

            def wait(self, selector: str, **_: object) -> DRIVER.UiNode:
                self.calls.append(("wait", selector))
                if selector == "attribute-bod":
                    return DRIVER.UiNode(
                        {"content-desc": f"Body. {self.total} · 1-6 · Aug 10"}
                    )
                return DRIVER.UiNode({"text": selector})

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))
                if selector == "attribute-improve-bod":
                    self.total = 2

            def back(self) -> None:
                self.calls.append(("back", ""))

            def capture(self, name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = CareerAttributeDevice()
        contract = DRIVER.FullEditingFixtureContract(1, 2, 10, 35, 25, 15)
        with patch.object(DRIVER.time, "sleep"):
            DRIVER.improve_body_in_career(device, "phone", contract)

        self.assertIn(("tap", "attribute-improve-bod"), device.calls)
        self.assertIn(("wait", "Available Karma: 35"), device.calls)
        self.assertIn(("wait", "Improve · 10 Karma"), device.calls)
        self.assertIn(("wait", "Available Karma: 25"), device.calls)
        self.assertIn(("wait", "Improve · 15 Karma"), device.calls)
        self.assertEqual(2, device.total)

    def test_phone_gear_route_resets_preserved_action_scroll(self) -> None:
        class GearRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str, dict[str, object]]] = []

            def tap_bidirectional(self, selector: str, **options: object) -> None:
                self.calls.append(("tap_bidirectional", selector, options))

            def wait_exact_resource_id_bidirectional(
                self,
                selector: str,
                **options: object,
            ) -> None:
                self.calls.append(("wait_exact", selector, options))

        device = GearRouteDevice()
        DRIVER.open_gear_section(device, "phone")

        self.assertEqual(
            (
                "tap_bidirectional",
                "build-section-tab-gear",
                {
                    "timeout": 120,
                    "backward_scrolls": 24,
                    "forward_scrolls": 24,
                    "scroll_distance_ratio": 0.22,
                    "exact_resource_id": True,
                },
            ),
            device.calls[0],
        )
        self.assertEqual(
            [
                (
                    "wait_exact",
                    "section-quick-gear-add",
                    {
                        "timeout": 180,
                        "backward_scrolls": 24,
                        "forward_scrolls": 48,
                        "scroll_distance_ratio": 0.22,
                    },
                ),
            ],
            device.calls[1:],
        )
        self.assertNotIn(
            "build-action-tab-gear-gear",
            [selector for _, selector, _ in device.calls],
        )

    def test_phone_relationship_routes_use_exact_bounded_activation_and_quick_add_reset(self) -> None:
        class RelationshipRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str, dict[str, object]]] = []

            def tap_bidirectional(self, selector: str, **options: object) -> None:
                self.calls.append(("tap_bidirectional", selector, options))

            def wait_exact_resource_id_bidirectional(
                self,
                selector: str,
                **options: object,
            ) -> None:
                self.calls.append(("wait_exact", selector, options))

            def swipe_down(self, **options: object) -> None:
                self.calls.append(("swipe_down", "", options))

            def wait(self, selector: str, **options: object) -> DRIVER.UiNode:
                self.calls.append(("wait", selector, options))
                return DRIVER.UiNode({"text": selector})

        for opener, action_selector in (
            (DRIVER.open_contact_section, "build-action-tab-relationships-contacts"),
            (DRIVER.open_pet_section, "build-action-tab-relationships-pets"),
        ):
            with self.subTest(action_selector=action_selector):
                device = RelationshipRouteDevice()
                with patch.object(DRIVER.time, "sleep"):
                    opener(device, "phone")

                self.assertEqual(
                    [
                        (
                            "tap_bidirectional",
                            "build-section-tab-relationships",
                            {
                                "timeout": 120,
                                "backward_scrolls": 24,
                                "forward_scrolls": 24,
                                "scroll_distance_ratio": 0.22,
                                "exact_resource_id": True,
                            },
                        ),
                        (
                            "tap_bidirectional",
                            action_selector,
                            {
                                "timeout": 180,
                                "backward_scrolls": 24,
                                "forward_scrolls": 48,
                                "scroll_distance_ratio": 0.22,
                                "exact_resource_id": True,
                            },
                        ),
                        (
                            "wait",
                            "No entries yet. Use an action above to add one.",
                            {
                                "timeout": 60,
                                "scroll": True,
                                "max_scrolls": 24,
                                "scroll_distance_ratio": 0.22,
                            },
                        ),
                        (
                            "wait_exact",
                            "section-quick-contact-add",
                            {
                                "timeout": 180,
                                "backward_scrolls": 24,
                                "forward_scrolls": 48,
                                "scroll_distance_ratio": 0.22,
                            },
                        ),
                    ],
                    [
                        call_value
                        for call_value in device.calls
                        if call_value[0] != "swipe_down"
                    ],
                )
                marker_index = next(
                    index
                    for index, call_value in enumerate(device.calls)
                    if call_value[0] == "wait"
                )
                quick_add_index = next(
                    index
                    for index, call_value in enumerate(device.calls)
                    if call_value[0] == "wait_exact"
                )
                self.assertEqual(
                    24,
                    sum(
                        call_value[0] == "swipe_down"
                        for call_value in device.calls[1:marker_index]
                    ),
                )
                self.assertLess(marker_index, quick_add_index)

    def test_stale_relationships_quick_add_cannot_satisfy_collection_activation(self) -> None:
        class StaleRelationshipsDevice:
            def __init__(self) -> None:
                self.stale_quick_add_visible = True
                self.quick_add_waited = False

            def tap_bidirectional(self, _: str, **__: object) -> None:
                return None

            def swipe_down(self, **_: object) -> None:
                return None

            def wait(self, selector: str, **_: object) -> DRIVER.UiNode:
                if selector == "No entries yet. Use an action above to add one.":
                    raise RuntimeError("target collection empty marker absent on stale Relationships")
                raise AssertionError(f"unexpected selector {selector!r}")

            def wait_exact_resource_id_bidirectional(
                self,
                selector: str,
                **_: object,
            ) -> None:
                if selector == "section-quick-contact-add" and self.stale_quick_add_visible:
                    self.quick_add_waited = True
                    return None
                raise AssertionError(f"unexpected selector {selector!r}")

        device = StaleRelationshipsDevice()
        with (
            patch.object(DRIVER.time, "sleep"),
            self.assertRaisesRegex(RuntimeError, "empty marker absent on stale Relationships"),
        ):
            DRIVER._open_phone_relationship_collection(
                device,
                action_selector="build-action-tab-relationships-contacts",
                quick_add_selector="section-quick-contact-add",
                expected_item=None,
            )

        self.assertFalse(device.quick_add_waited)

    def test_phone_relationship_fixture_route_resets_before_bounded_item_search(self) -> None:
        class RelationshipFixtureDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str, dict[str, object]]] = []

            def tap_bidirectional(self, selector: str, **options: object) -> None:
                self.calls.append(("tap_bidirectional", selector, options))

            def swipe_down(self, **options: object) -> None:
                self.calls.append(("swipe_down", "", options))

            def wait(self, selector: str, **options: object) -> None:
                self.calls.append(("wait", selector, options))

        for opener, action_selector, expected_item in (
            (
                DRIVER.open_contact_section,
                "build-action-tab-relationships-contacts",
                "ContactPersistedE2E",
            ),
            (
                DRIVER.open_pet_section,
                "build-action-tab-relationships-pets",
                "PetPersistedE2E",
            ),
        ):
            with self.subTest(action_selector=action_selector):
                device = RelationshipFixtureDevice()
                with patch.object(DRIVER.time, "sleep"):
                    opener(device, "phone", expected_item=expected_item)

                self.assertEqual(
                    [
                        "build-section-tab-relationships",
                        action_selector,
                    ],
                    [
                        selector
                        for kind, selector, _ in device.calls
                        if kind == "tap_bidirectional"
                    ],
                )
                action_index = next(
                    index
                    for index, call_value in enumerate(device.calls)
                    if call_value[0:2] == ("tap_bidirectional", action_selector)
                )
                wait_index = next(
                    index
                    for index, call_value in enumerate(device.calls)
                    if call_value[0:2] == ("wait", expected_item)
                )
                between = device.calls[action_index + 1 : wait_index]
                self.assertEqual(24, sum(call_value[0] == "swipe_down" for call_value in between))
                self.assertEqual(
                    (
                        "wait",
                        expected_item,
                        {
                            "timeout": 60,
                            "scroll": True,
                            "max_scrolls": 24,
                            "scroll_distance_ratio": 0.22,
                        },
                    ),
                    device.calls[wait_index],
                )

    def test_exact_bidirectional_wait_recovers_quick_add_above_preserved_scroll(self) -> None:
        wrong_prefix = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/"
                    "build-action-tab-gear-gearlocations"
                ),
                "clickable": "true",
                "bounds": "[100,100][300,300]",
            }
        )
        finder = Mock(spec=DRIVER.Device)
        finder.hierarchy.return_value = [wrong_prefix]
        self.assertTrue(
            DRIVER.Device._matches(wrong_prefix, "build-action-tab-gear-gear")
        )
        self.assertIsNone(
            DRIVER.Device.find_exact_resource_id(
                finder,
                "build-action-tab-gear-gear",
            )
        )

        target = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/section-quick-gear-add",
                "clickable": "true",
                "bounds": "[100,100][300,300]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        preserved_offset = {"rows_below_top": 24}
        device._scroll_x_ratio.return_value = 0.5
        device.swipe_down.side_effect = lambda **_: preserved_offset.update(
            rows_below_top=max(0, preserved_offset["rows_below_top"] - 1)
        )
        device.swipe_up.side_effect = lambda **_: preserved_offset.update(
            rows_below_top=preserved_offset["rows_below_top"] + 1
        )
        device.find_exact_resource_id.side_effect = lambda _: (
            target if preserved_offset["rows_below_top"] == 0 else None
        )
        device.node_has_tappable_bounds.return_value = True
        with patch.object(DRIVER.time, "sleep"):
            actual = DRIVER.Device.wait_exact_resource_id_bidirectional(
                device,
                "section-quick-gear-add",
                timeout=120,
                backward_scrolls=24,
                forward_scrolls=24,
                scroll_distance_ratio=0.22,
            )

        self.assertIs(target, actual)
        self.assertEqual(0, preserved_offset["rows_below_top"])
        self.assertEqual(24, device.swipe_down.call_count)
        device.swipe_up.assert_not_called()
        device.node_has_tappable_bounds.assert_called_once_with(target)

        old_offset = {"rows_below_top": 24}
        old_device = Mock(spec=DRIVER.Device)
        old_device.find.side_effect = lambda _: (
            target if old_offset["rows_below_top"] == 0 else None
        )
        old_device.dismiss_system_ui_anr.return_value = False
        old_device._scroll_x_ratio.return_value = 0.5
        old_device.swipe_up.side_effect = lambda **_: old_offset.update(
            rows_below_top=old_offset["rows_below_top"] + 1
        )
        clock = {"now": 0.0}

        def advance_clock() -> float:
            clock["now"] += 1.0
            return clock["now"]

        with patch.object(DRIVER.time, "monotonic", side_effect=advance_clock), patch.object(
            DRIVER.time,
            "sleep",
        ):
            with self.assertRaisesRegex(RuntimeError, "Timed out waiting"):
                DRIVER.Device.wait(
                    old_device,
                    "section-quick-gear-add",
                    timeout=4,
                    scroll=True,
                    max_scrolls=4,
                    scroll_distance_ratio=0.22,
                )
        self.assertGreater(old_offset["rows_below_top"], 24)

    def test_phone_condition_route_uses_overlapping_scrolls(self) -> None:
        class ConditionRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))

            def wait(self, selector: str, **_: object) -> None:
                self.calls.append(("wait", selector))

        device = ConditionRouteDevice()
        DRIVER.open_condition_monitor_section(device, "phone")

        self.assertEqual(
            [
                ("tap", "build-section-tab-combat"),
                ("tap", "build-action-tab-combat-conditionmonitor"),
                ("wait", "condition-monitor-physical"),
            ],
            device.calls,
        )

    def test_tablet_condition_route_resets_the_persistent_navigation_rail(self) -> None:
        class ConditionRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))

            def wait(self, selector: str, **_: object) -> None:
                self.calls.append(("wait", selector))

        device = ConditionRouteDevice()
        with patch.object(DRIVER.time, "sleep"):
            DRIVER.open_condition_monitor_section(device, "tablet")

        self.assertEqual([("swipe", "down")] * 24, device.calls[:24])
        self.assertEqual(
            [
                ("tap", "tablet-build-tab-tab-combat"),
                ("tap", "tablet-build-action-tab-combat-conditionmonitor"),
                ("wait", "tablet-condition-track-physical"),
            ],
            device.calls[24:],
        )

    def test_condition_journey_imports_a_career_fixture_through_the_document_picker(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        condition_branch = source[
            source.index('if args.journey in {"condition-monitor", "contact-pet"}:') :
        ]
        condition_branch = condition_branch[: condition_branch.index("else:")]

        self.assertIn('device.tap("home-open-file")', condition_branch)
        self.assertIn(
            '"career-condition-monitor-e2e.chum5"',
            condition_branch,
        )
        self.assertIn("select_android_document(device, fixture_name)", condition_branch)
        self.assertNotIn("home-new-runner", condition_branch)

        fixture = REPO_ROOT / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5"
        fixture_xml = fixture.read_text(encoding="utf-8")
        for marker in (
            "<created>True</created>",
            "<karma>35</karma>",
            "<nuyen>8500</nuyen>",
            "<physicalcm>10</physicalcm>",
            "<physicalcmoverflow>3</physicalcmoverflow>",
            "<stuncm>10</stuncm>",
        ):
            self.assertIn(marker, fixture_xml)

    def test_contact_pet_journey_is_focused_and_process_restart_bound(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        branch = source[source.index('if args.journey == "contact-pet":') :]
        branch = branch[: branch.index('if args.journey == "condition-monitor":')]

        for marker in (
            "add_and_edit_contact(device, args.profile, create_items=False)",
            "add_and_edit_pet(device, args.profile, create_items=False)",
            "force_stop_and_launch_new_process(",
            "assert_contact_persisted(device, args.profile)",
            "assert_pet_persisted(device, args.profile)",
            '"inputFixtureSha256": contact_pet_runner_sha256',
            '"verifiedRemoteInputFixtureSha256": verified_remote_sha256[',
            '"preRestartAuthority": optional_workspace_authority_json(persisted_authority)',
            '"postRestartAuthority": optional_workspace_authority_json(restored_authority)',
            '"processRestartContactPersistence": "pass"',
            '"processRestartPetPersistence": "pass"',
        ):
            self.assertIn(marker, branch)
        self.assertNotIn("attach_linked_runner(", branch)

        fixture = REPO_ROOT / "tests" / "fixtures" / "creation-contact-pet-e2e.chum5"
        fixture_xml = fixture.read_text(encoding="utf-8")
        for marker in (
            "<created>False</created>",
            "<name>ContactE2E</name>",
            "<name>ContactDeleteE2E</name>",
            "<name>PetE2E</name>",
            "<name>PetDeleteE2E</name>",
            "<type>Contact</type>",
            "<type>Pet</type>",
        ):
            self.assertIn(marker, fixture_xml)

    def test_phone_origin_dossier_resets_before_overlapping_search(self) -> None:
        device = Mock()

        with patch.object(DRIVER.time, "sleep"):
            DRIVER.open_origin_dossier(device, "phone")

        self.assertEqual(12, device.swipe_down.call_count)
        device.tap.assert_called_once_with(
            "build-origin-dossier",
            scroll=True,
            timeout=60,
            max_scrolls=16,
            scroll_distance_ratio=0.22,
        )

    def test_condition_damage_uses_distinct_phone_and_tablet_editors(self) -> None:
        with patch.object(DRIVER, "open_condition_monitor_section"), patch.object(
            DRIVER, "selected_text", return_value="2"
        ), patch.object(DRIVER.time, "sleep"):
            phone = Mock()
            DRIVER.edit_condition_damage(phone, "phone", "physical", 2)
            tablet = Mock()
            DRIVER.edit_condition_damage(tablet, "tablet", "physical", 2)

        phone.assert_has_calls(
            [
                call.tap("condition-monitor-physical", scroll=True),
                call.wait("condition-monitor-editor-physical", timeout=45),
                call.tap("condition-monitor-filled-physical", scroll=True),
                call.tap("2", scroll=True),
                call.tap("condition-monitor-save-physical", scroll=True),
                call.back(),
                call.back(),
            ]
        )
        tablet.assert_has_calls(
            [
                call.tap("tablet-condition-track-physical", scroll=True),
                call.tap("tablet-condition-filled-physical", scroll=True),
                call.tap("2", scroll=True),
                call.tap("tablet-condition-save-physical", scroll=True),
            ]
        )

    def test_gear_journey_edits_name_before_descending_to_add_action(self) -> None:
        device = Mock()
        device.assert_text = Mock()

        DRIVER.add_and_edit_gear(device, "phone")

        name_edit = device.method_calls.index(
            call.set_text(
                "dialog-field-uigearname",
                "Gear Name",
                "Ares Predator V",
                scroll=True,
                max_scrolls=32,
                scroll_distance_ratio=0.28,
            )
        )
        add_tap = device.method_calls.index(
            call.tap(
                "dialog-action-add",
                scroll=True,
                timeout=180,
                max_scrolls=48,
                scroll_distance_ratio=0.28,
            )
        )
        self.assertLess(name_edit, add_tap)
        self.assertLess(
            add_tap,
            device.method_calls.index(
                call.tap(
                    "Ares Predator V",
                    scroll=True,
                    timeout=60,
                    max_scrolls=24,
                    scroll_distance_ratio=0.22,
                    text_leading_offset=18,
                )
            ),
        )
        add_index = add_tap
        gear_index = device.method_calls.index(
            call.tap(
                "Ares Predator V",
                scroll=True,
                timeout=60,
                max_scrolls=24,
                scroll_distance_ratio=0.22,
                text_leading_offset=18,
            )
        )
        self.assertEqual(
            6,
            device.method_calls[add_index:gear_index].count(call.swipe_down(x_ratio=0.5)),
        )

    def test_back_uses_explicit_app_navigation_when_available(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "content-desc": "Navigate up",
                        "clickable": "true",
                        "bounds": "[0,128][147,275]",
                    }
                )
            ]
            with patch.object(DRIVER.time, "sleep") as sleep:
                device.back()

        self.assertEqual(("input", "tap", "73", "201"), device.commands[-1])
        sleep.assert_called_once_with(1)

    def test_assert_text_retries_a_transient_empty_hierarchy(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            rendered = DRIVER.UiNode({"text": "NativeE2E"})
            with patch.object(
                device,
                "hierarchy",
                side_effect=[[], [rendered]],
            ) as hierarchy, patch.object(
                DRIVER.time,
                "monotonic",
                side_effect=[0, 0, 1],
            ), patch.object(DRIVER.time, "sleep") as sleep:
                device.assert_text("NativeE2E")

        self.assertEqual(2, hierarchy.call_count)
        sleep.assert_called_once_with(0.5)


if __name__ == "__main__":
    unittest.main()
