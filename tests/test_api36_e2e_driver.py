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
    def test_launch_app_retries_transient_android_launcher_failure(self) -> None:
        device = Mock()
        device.shell.side_effect = [
            subprocess.CalledProcessError(137, ["adb", "shell", "monkey"]),
            "",
        ]

        with patch.object(DRIVER.time, "sleep") as sleep:
            DRIVER.launch_app(device)

        self.assertEqual(2, device.shell.call_count)
        sleep.assert_called_once_with(3)

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

    def test_new_runner_launch_retries_until_the_build_method_dialog_is_visible(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        self.assertIn(
            'device.tap_until_visible("home-new-runner", "Select Build Method")',
            source,
        )
        self.assertIn("time.sleep(1.25)", source)

    def test_collection_openers_use_overlapping_search_below_the_action_list(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name, action, quick_add in (
            ("open_gear_section", "build-action-tab-gear-gear", "section-quick-gear-add"),
            ("open_contact_section", "build-action-tab-relationships-contacts", "section-quick-contact-add"),
            ("open_pet_section", "build-action-tab-relationships-pets", "section-quick-contact-add"),
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertIn("device.tap(", block)
            self.assertIn("device.wait(", block)
            self.assertIn(f'"{action}"', block)
            self.assertIn(f'"{quick_add}"', block)
            self.assertIn("max_scrolls=48", block)
            self.assertIn("scroll_distance_ratio=0.22", block)

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

        action_wait = contact_add.index('device.wait("dialog-action-add"')
        reset = contact_add.index("reset_scroll_to_top(device")
        name_edit = contact_add.index('device.set_text("dialog-field-uicontactname"')
        self.assertLess(action_wait, reset)
        self.assertLess(reset, name_edit)

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
        group_toggle = contact_edit[: contact_edit.index('f"{toggle_prefix}-group"')]
        first_save = group_toggle.rindex("device.tap(save, scroll=True)")
        reset = group_toggle.rindex("reset_scroll_to_top(")

        self.assertLess(first_save, reset)

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

        self.assertEqual([("swipe", "down")] * 12, device.calls[:12])
        self.assertEqual(("tap", "build-section-tab-gear"), device.calls[12])
        self.assertEqual([("swipe", "down")] * 12, device.calls[13:25])
        self.assertEqual(
            [
                ("tap", "build-action-tab-gear-gear"),
                ("wait", "section-quick-gear-add"),
            ],
            device.calls[25:],
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
            device.back()

        self.assertEqual(("input", "tap", "73", "201"), device.commands[-1])


if __name__ == "__main__":
    unittest.main()
