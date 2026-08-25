import ast
import copy
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock, patch
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_notoriety_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-notoriety-e2e.chum5"

sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("career_notoriety_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(driver)


class Api36CareerNotorietyDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_source_digest_revision_and_new_pid_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROL = "CharacterCareer.nudNotoriety"', source)
        self.assertIn('SELECTOR = "career-reputation-notoriety"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-notoriety"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertNotIn('"profile": "tablet"', source)
        for digest in (
            '"careerReputationRequestSha256"',
            '"mutationCatalogSha256"',
            '"presenterMutationSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
        ):
            self.assertIn(digest, source)
        self.assertIn("saved.content_revision != imported.content_revision + 1", source)
        self.assertIn("saved.payload_sha256 == imported.payload_sha256", source)
        self.assertIn("saved.document_sha256 == imported.document_sha256", source)
        self.assertEqual(1, source.count("shared.force_stop_and_launch_new_process"))
        self.assertIn("shared.require_restored_authority(saved, restored)", source)
        self.assertIn('"afterForceStop": list(restart.after_force_stop.process_ids)', source)
        self.assertIn("shared.reset_scroll_to_top(device, swipes=48)", source)
        self.assertIn('"build-save-runner"', source)
        self.assertIn('== "Navigate up"', source)
        self.assertIn("max_back_steps: int = 6", source)
        self.assertIn("device.wait_for_single_exact_resource_id(", source)
        self.assertIn('"build-career-reputation"', source)
        self.assertIn("wait_for_single_exact_picker_text(device, value)", source)
        self.assertIn("device.node_has_tappable_bounds", source)

    def test_coordinator_requires_exact_revision_dirty_save_and_durable_authority(self) -> None:
        source = (
            REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        method = source[
            source.index("public async Task ApplyCareerReputationEditAsync"):
            source.index("public async Task ApplyBurnStreetCredAsync")
        ]
        for token in (
            "request.ExpectedContentRevision < long.MaxValue",
            "State.ContentRevision == request.ExpectedContentRevision + 1",
            "State.IsDirty",
            "State.SavedRevision == appliedContentRevision",
            "!State.IsDirty",
            "TryRefreshWorkspaceAuthorityAsync(",
            "authority is not null && authority.Matches(State)",
            '_notice = persisted ? "Reputation saved." : null;',
        ):
            self.assertIn(token, method)
        self.assertLess(method.index("exactMutationApplied"), method.index("SaveAsync"))
        self.assertLess(method.index("durableState"), method.index("TryRefreshWorkspaceAuthorityAsync"))

    def test_fixture_binds_exact_notoriety_and_unrelated_xml_authority(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        driver.require_canonical_import_fixture(root)
        preserved = driver.assert_before(root)
        self.assertEqual(7, driver.read_notoriety(root))
        self.assertEqual("19", preserved["karma"])
        self.assertEqual("8765", preserved["nuyen"])
        self.assertEqual(
            driver.UNRELATED_GEAR_ID,
            driver._unique_by_guid(
                root,
                "./gears/gear",
                driver.UNRELATED_GEAR_ID,
                "unrelated Gear",
            ).findtext("guid"),
        )
        uuid.UUID(driver.UNRELATED_GEAR_ID)

    def test_after_contract_accepts_only_exact_delta_and_preserved_xml(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        preserved = driver.assert_before(root)
        driver._unique_child(root, "notoriety").text = str(driver.EXPECTED_NOTORIETY)
        driver.assert_after(root, preserved)

        hostile = copy.deepcopy(root)
        driver._unique_child(hostile, "publicawareness").text = "99"
        with self.assertRaisesRegex(RuntimeError, "outside the exact <notoriety> field"):
            driver.assert_after(hostile, preserved)

        wrong_delta = copy.deepcopy(root)
        driver._unique_child(wrong_delta, "notoriety").text = "9"
        with self.assertRaisesRegex(RuntimeError, "exact one-point delta"):
            driver.assert_after(wrong_delta, preserved)

    def test_fixture_preflight_rejects_every_missing_canonical_loader_field(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        for field in driver.CANONICAL_IMPORT_FIELDS:
            with self.subTest(field=field):
                hostile = copy.deepcopy(root)
                hostile.remove(hostile.find(field))
                with self.assertRaisesRegex(RuntimeError, rf"canonical SR5 loader: <{field}>"):
                    driver.require_canonical_import_fixture(hostile)

    def test_exact_tap_fails_closed_when_target_is_untappable(self) -> None:
        node = driver.shared.UiNode(
            {
                "resource-id": "career-reputation-notoriety",
                "clickable": "true",
                "bounds": "[98,275][984,276]",
            }
        )
        device = Mock(spec=driver.shared.Device)
        device.wait_for_single_exact_resource_id.return_value = node
        device.node_has_tappable_bounds.return_value = False

        with self.assertRaisesRegex(RuntimeError, "not tappable"):
            driver.tap_exact(
                device,
                "career-reputation-notoriety",
                evidence_prefix="career-notoriety-picker-open",
                surface_name="Career Notoriety picker accessibility node",
                scroll=True,
                max_scrolls=12,
            )

        device.capture.assert_called_once_with("career-notoriety-picker-open-untappable")
        device.shell.assert_not_called()

    def test_picker_value_binds_one_exact_android_checked_text_row(self) -> None:
        value = driver.shared.UiNode(
            {
                "text": "8",
                "resource-id": "vendor:id/not-the-picker-row",
                "content-desc": "not-8",
                "class": "android.widget.CheckedTextView",
                "bounds": "[91,589][989,715]",
            }
        )
        unrelated = driver.shared.UiNode(
            {
                "text": "8 rounds",
                "resource-id": "android:id/text1",
                "class": "android.widget.CheckedTextView",
            }
        )
        device = Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [unrelated, value]

        self.assertIs(value, driver.wait_for_single_exact_picker_text(device, 8))
        device.capture.assert_not_called()

    def test_picker_value_fails_closed_when_exact_text_is_missing(self) -> None:
        device = Mock(spec=driver.shared.Device)

        with self.assertRaisesRegex(RuntimeError, "one exact Career Notoriety"):
            driver.wait_for_single_exact_picker_text(device, 8, timeout=0)

        device.capture.assert_called_once_with("career-notoriety-value-unavailable")

    def test_picker_value_fails_closed_on_duplicate_exact_rows(self) -> None:
        value = driver.shared.UiNode(
            {
                "text": "8",
                "resource-id": "android:id/text1",
                "class": "android.widget.CheckedTextView",
            }
        )
        device = Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [value, value]

        with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.wait_for_single_exact_picker_text(device, 8)

        device.capture.assert_called_once_with(
            "career-notoriety-value-cardinality-invalid"
        )

    def test_open_page_resets_build_viewport_then_binds_exact_route_and_picker(self) -> None:
        device = Mock(spec=driver.shared.Device)
        events: list[tuple[str, str]] = []

        def record_tap(
            _device: object,
            selector: str,
            **_kwargs: object,
        ) -> None:
            events.append(("tap", selector))

        def record_wait(selector: str, **_kwargs: object) -> Mock:
            events.append(("wait", selector))
            return Mock()

        device.wait_for_single_exact_resource_id.side_effect = record_wait
        device.wait_for_single_exact_accessibility_value.side_effect = record_wait
        with (
            patch.object(driver, "open_build_root"),
            patch.object(driver.shared, "reset_scroll_to_top") as reset,
            patch.object(driver, "tap_exact", side_effect=record_tap),
        ):
            driver.open_page(device)

        reset.assert_called_once_with(device, swipes=48)
        self.assertEqual(
            [
                ("tap", "build-career-reputation"),
                ("wait", "career-reputation"),
                ("wait", "career-reputation-notoriety"),
            ],
            events,
        )


if __name__ == "__main__":
    unittest.main()
