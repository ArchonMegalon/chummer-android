import ast
import importlib.util
import os
import sys
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
    def test_creation_karma_budget_cards_expose_readable_semantic_totals(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationPrerequisitePreviewPage.cs").read_text(encoding="utf-8")
        expected = (
            '$"Global Creation Karma. Total {total}. Used {used}. '
            'Remaining {remaining}."'
        )
        for source, automation_id in (
            (page, "creation-prerequisite-karma-budget"),
            (preview, "creation-prerequisite-preview-karma-budget"),
        ):
            self.assertIn(f'border.AutomationId = "{automation_id}"', source)
            self.assertIn("SemanticProperties.SetDescription(", source)
            self.assertIn(expected, source)

    def test_source_authority_labels_keep_full_width_beside_long_digests(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")

        for label, automation_id in (
            ("Authority digest", "creation-prerequisite-authority-digest"),
            ("Profile inputs", "creation-prerequisite-profile-inputs-digest"),
            ("Priorities XML", "creation-prerequisite-priorities-xml-digest"),
        ):
            self.assertIn(
                f'card.Add(SourceAuthorityMetric(\n            "{label}",',
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

    def test_authority_option_collector_rejects_zero_candidates(self) -> None:
        device = mock.Mock()
        device.hierarchy.return_value = []
        with self.assertRaisesRegex(RuntimeError, "exactly one enabled authoritative option"):
            driver.tap_enabled_authority_option(
                device,
                "creation-prerequisite-heritage-option-",
                "Human",
                max_scrolls=0,
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
                max_scrolls=0,
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
                max_scrolls=0,
            )
        device.capture.assert_called_once()

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

    def test_priority_provisioning_declares_every_explicit_production_selection(self) -> None:
        self.assertEqual(
            ("dialog-field-newcharacterbuildmethod", "Priority"),
            driver.PRIORITY_BUILD_METHOD_SELECTION,
        )
        self.assertEqual(
            (
                "dialog-field-newcharactersetting",
                "Character Setting",
                "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
            ),
            driver.PRIORITY_SETTINGS_SELECTION,
        )
        self.assertEqual(
            {
                "dialog-field-newcharactermetatypecategory": "Non-human choices",
                "dialog-field-newcharactermetatype": "Elf",
                "dialog-field-newcharacterpriorityheritage": "A",
                "dialog-field-newcharactermetavariant": "Dryad",
                "dialog-field-newcharacterpriorityattributes": "C",
                "dialog-field-newcharacterprioritytalent": "B",
                "dialog-field-newcharacterpriorityskills": "D",
                "dialog-field-newcharacterpriorityresources": "E",
                "dialog-field-newcharacterprioritytalentchoice": "Mystic Adept",
                "dialog-field-newcharacterpriorityskillchoice1": "Summoning",
                "dialog-field-newcharacterpriorityskillchoice2": "Binding",
                "dialog-field-newcharacterpriorityskillchoice3": "Gymnastics",
            },
            dict(driver.PRIORITY_CREATION_SELECTIONS),
        )

    def test_prerequisite_navigation_uses_exact_bounded_bidirectional_search(self) -> None:
        device = mock.Mock()

        with mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            driver.open_prerequisite(device)

        device.tap_bidirectional.assert_called_once_with(
            "creation-stage-method",
            timeout=180,
            backward_scrolls=22,
            forward_scrolls=22,
            scroll_distance_ratio=0.22,
            exact_resource_id=True,
        )
        reset.assert_called_once_with(device, swipes=22)
        device.tap_until_visible.assert_not_called()

    def test_prerequisite_navigation_proves_route_before_reading_content(self) -> None:
        device = mock.Mock()

        with mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            driver.open_prerequisite(device)

        self.assertEqual(
            [
                mock.call("creation-prerequisite-page", timeout=60),
                mock.call(
                    "creation-prerequisite-karma-budget",
                    timeout=60,
                    scroll=True,
                    max_scrolls=22,
                ),
                mock.call(
                    "creation-prerequisite-method",
                    timeout=45,
                    scroll=True,
                    max_scrolls=22,
                ),
            ],
            device.wait.call_args_list,
        )
        reset.assert_called_once_with(device, swipes=22)

    def test_rank_selection_resets_bottom_viewport_and_proves_refreshed_draft_row(self) -> None:
        calls: list[tuple[str, object]] = []
        category_page = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-category-page"}
        )
        parent_page = driver.shared.UiNode(
            {"resource-id": "creation-prerequisite-page"}
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
            category_poll_count = 0

            def tap(self, selector: str, **options: object) -> None:
                if self.route != "parent" or self.viewport != "top":
                    raise AssertionError("Category tap ran before resetting the parent viewport")
                calls.append(("tap", (selector, options)))
                self.route = "category"

            def find_exact_resource_id(self, selector: str):
                calls.append(("find_exact", selector))
                if selector == "creation-prerequisite-category-page" and self.route == "category":
                    self.category_poll_count += 1
                    if self.category_poll_count == 1:
                        return category_page
                    self.route = "parent"
                    self.viewport = "bottom"
                    return category_page
                return None

            def dismiss_system_ui_anr(self) -> bool:
                return False

            def wait_for_single_exact_resource_id(self, selector: str, **options: object):
                calls.append(("wait_exact", (selector, options)))
                if selector == "creation-prerequisite-category-page":
                    if self.route != "category":
                        raise AssertionError("Category route was not active")
                    return category_page
                if selector == "creation-prerequisite-page":
                    if self.route != "parent":
                        raise AssertionError("Parent route was checked before the category pop")
                    return parent_page
                if self.route != "parent" or self.viewport != "top":
                    raise AssertionError("Selected row was checked before resetting the parent viewport")
                return selected_row

            def swipe_down(self, **options: object) -> None:
                calls.append(("swipe_down", options))
                self.viewport = "top"

            @staticmethod
            def node_has_tappable_bounds(node) -> bool:
                return bool(node.attributes.get("bounds"))

            def capture(self, name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = FakeDevice()

        def select_rank(_device, category: str) -> str:
            self.assertIs(device, _device)
            self.assertEqual("heritage", category)
            calls.append(("select", category))
            return "creation-prerequisite-rank-heritage-a"

        with mock.patch.object(
                 driver,
                 "tap_first_exact_enabled_priority_rank",
                 side_effect=select_rank,
             ), \
             mock.patch.object(driver.time, "sleep"):
            selected = driver.select_priority_rank(device, "heritage")

        self.assertEqual("creation-prerequisite-rank-heritage-a", selected)
        self.assertEqual(44, sum(call[0] == "swipe_down" for call in calls))
        self.assertIn(
            (
                "tap",
                (
                    "creation-prerequisite-category-heritage",
                    {
                        "scroll": True,
                        "max_scrolls": 22,
                        "exact_resource_id": True,
                    },
                ),
            ),
            calls,
        )
        self.assertIn(
            (
                "wait_exact",
                (
                    "creation-prerequisite-category-page",
                    {
                        "timeout": 45,
                        "evidence_prefix": "creation-prerequisite-heritage-category-route",
                        "surface_name": "heritage priority category route",
                    },
                ),
            ),
            calls,
        )
        self.assertIn(
            (
                "wait_exact",
                (
                    "creation-prerequisite-page",
                    {
                        "timeout": 45,
                        "evidence_prefix": "creation-prerequisite-heritage-parent-route",
                        "surface_name": "Creation prerequisite parent route",
                    },
                ),
            ),
            calls,
        )
        self.assertIn(
            (
                "wait_exact",
                (
                    "creation-prerequisite-category-heritage",
                    {
                        "timeout": 45,
                        "scroll": True,
                        "max_scrolls": 22,
                        "scroll_distance_ratio": 0.22,
                        "evidence_prefix": "creation-prerequisite-heritage-selected-row",
                        "surface_name": "Selected heritage category row",
                    },
                ),
            ),
            calls,
        )

    def test_exact_rank_scan_cardinality_checks_then_taps_one_exact_enabled_option(self) -> None:
        enabled = driver.shared.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/"
                    "creation-prerequisite-rank-heritage-a"
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
            for rank in "bcde"
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

            def wait_for_single_exact_resource_id(self, selector: str, **_options: object):
                self.assert_exact(selector)
                return enabled

            @staticmethod
            def assert_exact(selector: str) -> None:
                if selector != "creation-prerequisite-rank-heritage-a":
                    raise AssertionError(selector)

            def shell(self, *arguments: str) -> str:
                self.taps.append(arguments)
                return ""

            def capture(self, name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = RankDevice()
        with mock.patch.object(driver.time, "sleep"):
            selected = driver.tap_first_exact_enabled_priority_rank(device, "heritage")

        self.assertEqual("creation-prerequisite-rank-heritage-a", selected)
        self.assertEqual(44, device.down)
        self.assertEqual(22, device.up)
        self.assertEqual([("input", "tap", "500", "500")], device.taps)

    def test_exact_rank_scan_rejects_duplicate_or_malformed_resource_ids_before_tap(self) -> None:
        duplicate = self.authority_option_node(
            "creation-prerequisite-rank-heritage-a",
            "Rank A",
        )
        malformed = self.authority_option_node(
            "creation-prerequisite-rank-heritage-forged",
            "Forged rank",
        )

        for nodes, expected in (
            ([duplicate, duplicate], "duplicateIds"),
            ([malformed], "invalidIds"),
        ):
            with self.subTest(expected=expected):
                device = mock.Mock()
                device.hierarchy.return_value = nodes
                device.node_has_tappable_bounds.return_value = True
                with mock.patch.object(driver.shared, "reset_scroll_to_top"), \
                     mock.patch.object(driver.time, "sleep"), \
                     self.assertRaisesRegex(RuntimeError, expected):
                    driver.tap_first_exact_enabled_priority_rank(device, "heritage")
                device.wait_for_single_exact_resource_id.assert_not_called()
                device.shell.assert_not_called()

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

        with mock.patch.object(driver.shared, "reset_scroll_to_top"), \
             mock.patch.object(driver.time, "sleep"), \
             self.assertRaisesRegex(RuntimeError, "expectedIds"):
            driver.tap_first_exact_enabled_priority_rank(device, "heritage")

        device.wait_for_single_exact_resource_id.assert_not_called()
        device.shell.assert_not_called()

    def test_rank_selection_fails_closed_on_unbound_or_unrefreshed_rank(self) -> None:
        device = mock.Mock()
        device.wait.return_value = driver.shared.UiNode({})
        device.find_exact_resource_id.return_value = None
        parent = driver.shared.UiNode({"resource-id": "creation-prerequisite-page"})
        stale_row = driver.shared.UiNode(
            {
                "content-desc": "Heritage. 1. Select an authority-projected rank",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[10,100][900,300]",
            }
        )
        device.wait_for_single_exact_resource_id.side_effect = (
            lambda selector, **_options: parent
            if selector == "creation-prerequisite-page"
            else stale_row
        )
        device.node_has_tappable_bounds.return_value = True

        for selected_id, expected_error in (
            ("creation-prerequisite-rank-talent-a", "exact resource ID"),
            ("creation-prerequisite-rank-heritage-z", "invalid SR5 rank"),
            ("creation-prerequisite-rank-heritage-a", "was not projected"),
        ):
            with self.subTest(selected_id=selected_id), \
                 mock.patch.object(driver.shared, "reset_scroll_to_top"), \
                 mock.patch.object(
                     driver,
                     "tap_first_exact_enabled_priority_rank",
                     return_value=selected_id,
                 ), \
                 self.assertRaisesRegex(RuntimeError, expected_error):
                driver.select_priority_rank(device, "heritage")

    def test_priority_provisioning_follows_build_route_and_public_save_before_home(self) -> None:
        calls: list[tuple[str, tuple[object, ...], dict[str, object]]] = []
        structural_snapshots: list[list[driver.shared.UiNode]] = []

        class FakeDevice:
            viewport_reset = False
            selected_phone_destination = "Runner"

            @staticmethod
            def _phone_destination_node(
                label: str,
                bounds: str,
                *,
                selected: bool,
            ):
                return driver.shared.UiNode(
                    {
                        "resource-id": "",
                        "class": "android.widget.FrameLayout",
                        "package": driver.shared.PACKAGE,
                        "content-desc": label,
                        "enabled": "true",
                        "focusable": "true",
                        "selected": str(selected).lower(),
                        "clickable": str(not selected).lower(),
                        "bounds": bounds,
                    }
                )

            def hierarchy(self):
                calls.append(("hierarchy", (), {}))
                nodes = [
                    self._phone_destination_node(
                        "Runners",
                        "[0,2190][360,2337]",
                        selected=self.selected_phone_destination == "Runners",
                    ),
                    self._phone_destination_node(
                        "Runner",
                        "[360,2190][720,2337]",
                        selected=self.selected_phone_destination == "Runner",
                    ),
                    self._phone_destination_node(
                        "More",
                        "[720,2190][1080,2337]",
                        selected=self.selected_phone_destination == "More",
                    ),
                ]
                structural_snapshots.append(nodes)
                return nodes

            def display_size(self):
                calls.append(("display_size", (), {}))
                return 1080, 2400

            def dismiss_system_ui_anr(self) -> bool:
                calls.append(("dismiss_system_ui_anr", (), {}))
                return False

            def shell(self, *args) -> None:
                calls.append(("shell", args, {}))
                if args == ("input", "tap", "180", "2263"):
                    self.selected_phone_destination = "Runners"

            def tap_until_visible(self, *args, **kwargs) -> None:
                calls.append(("tap_until_visible", args, kwargs))

            def tap(self, *args, **kwargs) -> None:
                calls.append(("tap", args, kwargs))

            def tap_single_exact_resource_id(self, *args, **kwargs) -> None:
                calls.append(("tap_single_exact_resource_id", args, kwargs))

            def set_text(self, *args, **kwargs) -> None:
                calls.append(("set_text", args, kwargs))

            def wait(self, *args, **kwargs):
                if args == ("creation-wizard-dashboard",) and not self.viewport_reset:
                    raise AssertionError("Scrolled dashboard marker was checked before reset")
                calls.append(("wait", args, kwargs))
                return driver.shared.UiNode({})

            def capture(self, *args, **kwargs) -> None:
                calls.append(("capture", args, kwargs))

        selected_options: list[tuple[str, str]] = []

        def select_option(_device, selector: str, value: str) -> None:
            self.assertIs(device, _device)
            selected_options.append((selector, value))

        def open_dashboard(_device, **kwargs):
            self.assertIs(device, _device)
            device.viewport_reset = True
            calls.append(("open_creation_dashboard", (_device,), kwargs))
            return driver.shared.UiNode({})

        def require_dialog_transition(_device, **kwargs) -> None:
            self.assertIs(device, _device)
            calls.append(("require_new_character_dialog_transition", (_device,), kwargs))

        device = FakeDevice()
        with mock.patch.object(driver.priority, "select_option", side_effect=select_option), \
             mock.patch.object(
                 driver.shared,
                 "open_creation_dashboard",
                 side_effect=open_dashboard,
             ), \
             mock.patch.object(
                 driver,
                 "require_new_character_dialog_transition",
                 side_effect=require_dialog_transition,
             ):
            selected = driver.provision_creation_karma_through_priority_creation(device)

        self.assertEqual(
            [driver.PRIORITY_BUILD_METHOD_SELECTION, *driver.PRIORITY_CREATION_SELECTIONS],
            selected_options,
        )
        expected_selected = dict(selected_options)
        expected_selected[driver.PRIORITY_SETTINGS_SELECTION[0]] = (
            driver.PRIORITY_SETTINGS_SELECTION[2]
        )
        self.assertEqual(expected_selected, selected)
        self.assertIn(
            (
                "set_text",
                driver.PRIORITY_SETTINGS_SELECTION,
                {
                    "scroll": True,
                    "max_scrolls": 16,
                    "scroll_distance_ratio": 0.22,
                },
            ),
            calls,
        )
        setting_index = calls.index(
            (
                "set_text",
                driver.PRIORITY_SETTINGS_SELECTION,
                {
                    "scroll": True,
                    "max_scrolls": 16,
                    "scroll_distance_ratio": 0.22,
                },
            )
        )
        self.assertEqual(
            (
                "tap",
                ("dialog-action-create-character",),
                {"scroll": True, "max_scrolls": 16},
            ),
            calls[setting_index + 1],
            "The phone proof must exercise the action boundary without an artificial blur.",
        )
        route_index = calls.index(
            (
                "open_creation_dashboard",
                (device,),
                {
                    "open_build_route": False,
                    "toolbar_timeout": 120,
                    "dashboard_timeout": 30,
                    "reset_swipes": 48,
                },
            )
        )
        transition_index = calls.index(
            ("require_new_character_dialog_transition", (device,), {})
        )
        capture_index = calls.index(("capture", ("creation-karma-priority-runner-created",), {}))
        save_index = calls.index(
            (
                "tap",
                ("build-save-runner",),
                {
                    "scroll": True,
                    "max_scrolls": 48,
                    "scroll_distance_ratio": 0.22,
                },
            )
        )
        saved_index = calls.index(
            (
                "wait",
                ("Saved.",),
                {
                    "timeout": 90,
                    "scroll": True,
                    "max_scrolls": 48,
                    "scroll_distance_ratio": 0.22,
                },
            )
        )
        runners_index = calls.index(("shell", ("input", "tap", "180", "2263"), {}))
        hierarchy_indexes = [
            index for index, observed in enumerate(calls) if observed == ("hierarchy", (), {})
        ]
        display_indexes = [
            index for index, observed in enumerate(calls) if observed == ("display_size", (), {})
        ]
        authority_surface_index = calls.index(("wait", ("home-open-file",), {"timeout": 90}))
        self.assertLess(transition_index, route_index)
        self.assertLess(route_index, capture_index)
        self.assertLess(capture_index, save_index)
        self.assertLess(save_index, saved_index)
        self.assertGreaterEqual(len(hierarchy_indexes), 4)
        self.assertGreaterEqual(len(display_indexes), 4)
        self.assertTrue(all(index < runners_index for index in hierarchy_indexes[:3]))
        self.assertTrue(all(index < runners_index for index in display_indexes[:3]))
        self.assertLess(saved_index, runners_index)
        self.assertLess(runners_index, authority_surface_index)
        self.assertNotIn(("wait", ("Continue building",), {"timeout": 120}), calls)
        self.assertGreaterEqual(len(structural_snapshots), 5)
        bound_snapshots = [
            driver.shared.bind_phone_shell_destinations(device, snapshot)
            for snapshot in structural_snapshots
        ]
        for bound in bound_snapshots:
            self.assertEqual(
                driver.shared.PHONE_SHELL_DESTINATION_IDS,
                tuple(resource_id for resource_id, _ in bound),
            )
            self.assertEqual(
                driver.shared.PHONE_SHELL_DESTINATION_LABELS,
                tuple(node.attributes["content-desc"] for _, node in bound),
            )
            self.assertTrue(
                all(node.attributes["resource-id"] == "" for _, node in bound),
                "The pinned API-36 MAUI tabs use structural identities, not synthetic Android IDs.",
            )
        self.assertEqual(
            driver.shared._phone_shell_destination_signature(bound_snapshots[0]),
            driver.shared._phone_shell_destination_signature(bound_snapshots[1]),
            "The pre-tap structural binding must be stable across consecutive dumps.",
        )
        self.assertEqual(
            ["Runner"],
            [
                node.attributes["content-desc"]
                for _, node in bound_snapshots[2]
                if node.attributes["selected"] == "true"
            ],
        )
        self.assertEqual(
            ["Runners"],
            [
                node.attributes["content-desc"]
                for _, node in bound_snapshots[-1]
                if node.attributes["selected"] == "true"
            ],
        )

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

        driver.require_new_character_dialog_transition(device, timeout=1)

        device.capture.assert_not_called()

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

    def test_priority_created_authority_is_distinct_saved_and_digest_bound(self) -> None:
        fresh = driver.shared.WorkspaceAuthority("fresh", 2, 2, "a" * 64, "b" * 64)
        prepared = driver.shared.WorkspaceAuthority("prepared", 1, 1, "c" * 64, "d" * 64)
        driver.require_priority_created_workspace_authority(fresh, prepared)

        invalid = (
            (
                driver.shared.WorkspaceAuthority("fresh", 1, 1, "c" * 64, "d" * 64),
                "distinct runner workspace identity",
            ),
            (
                driver.shared.WorkspaceAuthority("prepared", 2, 1, "c" * 64, "d" * 64),
                "not durably checkpointed",
            ),
            (
                driver.shared.WorkspaceAuthority("prepared", 1, 1, "a" * 64, "d" * 64),
                "distinct character payload digest",
            ),
            (
                driver.shared.WorkspaceAuthority("prepared", 1, 1, "c" * 64, "b" * 64),
                "distinct document authority digest",
            ),
        )
        for authority, message in invalid:
            with self.subTest(message=message), self.assertRaisesRegex(RuntimeError, message):
                driver.require_priority_created_workspace_authority(fresh, authority)

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
            "_draft.OptionsForCategory(state, Coordinator.State, _categoryId)",
            "projection.Rank",
            "projection.SourceId",
            "projection.SourceNodeDigest",
            "projection.SourceAnchorIds",
            "projection.SumToTenValue",
            "projection.BaseNormalAttributePoints",
            "option.DisableReason",
            "_draft.TrySelect(state, Coordinator.State, _categoryId, rank)",
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
            "option.SkillGroupGrant is not null",
            "_draft.TrySelectHeritage(state, Coordinator.State, selectionId)",
            "_draft.TrySelectTalent(state, Coordinator.State, selectionId)",
        ):
            self.assertIn(marker, details)

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
            "Coordinator.LoadCreationPrerequisite()",
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

        combined = page + options + details + preview
        for forbidden in (
            "AttributeEditRequest",
            "ApplyAttributeEditAsync",
            "NativeCommandPage",
            "TabletBuildPage",
            "System.Xml",
            "Picker",
            "SelectedIndex = 0",
            "SaveAsync(",
        ):
            self.assertNotIn(forbidden, combined)

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
        self.assertIn('device.find("creation-prerequisite-page") is not None', source)
        self.assertIn('blocked_after = device.find("creation-stage-method")', source)
        self.assertIn("require_creation_method_navigation(blocked_after, ready=False)", source)
        self.assertIn("if after_tap != before_tap:", source)
        self.assertIn('device.capture("creation-method-navigation-remained-blocked")', source)
        self.assertIn("shared.open_creation_dashboard(", source)
        self.assertIn('"clickable": node.attributes.get("clickable") == "true"', source)
        self.assertIn('"tapRemainedOnDashboard": True', source)
        self.assertIn('"freshNavigation": fresh_navigation', source)
        self.assertIn("provision_creation_karma_through_priority_creation", source)
        self.assertIn("priority.select_option(device, selector, option)", source)
        self.assertIn('device.wait("Select Metatype Priority"', source)
        self.assertIn("toolbar_timeout=120", source)
        self.assertIn("dashboard_timeout=30", source)
        self.assertIn("reset_swipes=48", source)
        self.assertNotIn('device.wait("creation-wizard-dashboard"', source)
        self.assertIn('"build-save-runner",', source)
        self.assertIn('device.wait("home-open-file", timeout=90)', source)
        self.assertNotIn('device.wait("Continue building", timeout=120)', source)
        self.assertIn("require_priority_created_workspace_authority", source)
        self.assertIn("prepared.workspace_id == fresh.workspace_id", source)
        self.assertIn("prepared.payload_sha256 == fresh.payload_sha256", source)
        self.assertIn("prepared.document_sha256 == fresh.document_sha256", source)
        self.assertNotIn("shared.select_android_document", source)
        self.assertNotIn("shared.require_import_authority", source)
        self.assertNotIn("--creation-karma-runner", source)
        self.assertIn("read_source_authority_digests", source)
        self.assertIn('"freshRunnerCreationKarmaAuthorityBlocked": "pass"', source)
        self.assertIn('"publicRulesValidPriorityRunnerCreated": "pass"', source)
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
