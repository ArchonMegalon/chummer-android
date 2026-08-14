import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, call


REPO_ROOT = Path(__file__).resolve().parents[1]
DRIVER_PATH = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", DRIVER_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"could not load {DRIVER_PATH}")
DRIVER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = DRIVER
SPEC.loader.exec_module(DRIVER)


class FakeDevice(DRIVER.Device):
    def __init__(self, evidence: Path, hierarchy_output: str) -> None:
        super().__init__(Path("/unused/adb"), "emulator-5554", evidence)
        self.hierarchy_output = hierarchy_output

    def shell(self, *arguments: str, timeout: int = 120) -> str:
        return "UI hierarchy dumped"

    def run(
        self,
        *arguments: str,
        timeout: int = 120,
        text: bool = True,
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

    def shell(self, *arguments: str, timeout: int = 120) -> str:
        self.commands.append(arguments)
        if arguments == ("wm", "size"):
            return self.display_size_output
        return ""

    def hierarchy(self) -> list[DRIVER.UiNode]:
        return self.nodes


class Api36EditingE2EDriverTests(unittest.TestCase):
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

    def test_invalid_hierarchy_is_retryable_and_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = FakeDevice(evidence, "ERROR: could not get idle state")
            self.assertEqual([], device.hierarchy())
            diagnostic = evidence / "last-invalid-hierarchy.txt"
            self.assertTrue(diagnostic.is_file())
            self.assertIn("could not get idle state", diagnostic.read_text(encoding="utf-8"))

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
            [
                ("swipe", "down"),
                ("swipe", "down"),
                ("tap", "tablet-build-tab-tab-attributes"),
                ("swipe", "down"),
                ("swipe", "down"),
                ("wait", "tablet-attribute-body"),
                ("tap", "tablet-attribute-body"),
                ("wait", "tablet-attribute-base-body"),
            ],
            device.calls,
        )

    def test_phone_gear_route_resets_preserved_action_scroll(self) -> None:
        class GearRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))

            def wait(self, selector: str, **_: object) -> None:
                self.calls.append(("wait", selector))

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

        device = GearRouteDevice()
        DRIVER.open_gear_section(device, "phone")

        self.assertEqual(
            [
                ("tap", "build-section-tab-gear"),
                ("swipe", "down"),
                ("swipe", "down"),
                ("tap", "build-action-tab-gear-gear"),
                ("wait", "section-quick-gear-add"),
            ],
            device.calls,
        )

    def test_gear_journey_edits_name_before_descending_to_add_action(self) -> None:
        device = Mock()
        device.assert_text = Mock()

        DRIVER.add_and_edit_gear(device, "phone")

        name_edit = device.method_calls.index(
            call.set_text(
                "dialog-field-uigearname",
                "Gear Name",
                "Armor Jacket",
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
            device.method_calls.index(call.tap("Armor Jacket", scroll=True)),
        )
        add_index = add_tap
        armor_index = device.method_calls.index(call.tap("Armor Jacket", scroll=True))
        self.assertIn(call.swipe_down(x_ratio=0.5), device.method_calls[add_index:armor_index])

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
            device.back()

        self.assertEqual(("input", "tap", "73", "201"), device.commands[-1])


if __name__ == "__main__":
    unittest.main()
