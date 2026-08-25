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
DRIVER = REPO / "tests/run_api36_career_weapon_fire_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-weapon-fire-e2e.chum5"

sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("career_weapon_fire_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(driver)


class Api36CareerWeaponFireDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_source_digest_revision_and_new_pid_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROL = "CharacterCareer.cmsAmmoShortBurst"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-weapon-fire"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertNotIn('"profile": "tablet"', source)
        for digest in (
            '"careerWeaponFireRequestSha256"',
            '"weaponFireRulesSha256"',
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
        self.assertIn('"build-section-tab-gear"', source)
        self.assertIn('"build-action-tab-gear-weapons"', source)
        self.assertIn("max_scrolls=24", source)
        self.assertIn(
            'evidence_prefix="career-weapon-fire-gear-section-route"',
            source,
        )
        self.assertIn(
            'evidence_prefix="career-weapon-fire-gear-section-entered"',
            source,
        )
        self.assertIn('evidence_prefix="career-weapon-fire-weapons-route"', source)
        self.assertIn(
            'evidence_prefix="career-weapon-fire-target-weapon-route"',
            source,
        )
        self.assertEqual(4, source.count("tap_exact_build_route("))
        self.assertLess(
            source.index('evidence_prefix="career-weapon-fire-gear-section-entered"'),
            source.index('evidence_prefix="career-weapon-fire-weapons-route"'),
        )
        self.assertLess(
            source.index('evidence_prefix="career-weapon-fire-weapons-route"'),
            source.index('evidence_prefix="career-weapon-fire-target-weapon-route"'),
        )
        self.assertIn("device.node_has_tappable_bounds(node)", source)
        self.assertNotIn(
            'device.tap("build-section-tab-gear", scroll=True',
            source,
        )
        self.assertNotIn(
            'device.tap("build-action-tab-gear-weapons", scroll=True',
            source,
        )
        self.assertNotIn(
            'device.tap(\n        f"collection-item-weapon-{WEAPON_ID}"',
            source,
        )

    def test_build_route_resets_then_taps_one_exact_bounded_node(self) -> None:
        node = driver.shared.UiNode(
            {
                "resource-id": "build-action-tab-gear-weapons",
                "clickable": "true",
                "bounds": "[98,400][984,560]",
            }
        )
        device = Mock(spec=driver.shared.Device)
        device.wait_for_single_exact_resource_id.return_value = node
        device.node_has_tappable_bounds.return_value = True

        with patch.object(driver.shared, "reset_scroll_to_top") as reset:
            driver.tap_exact_build_route(
                device,
                "build-action-tab-gear-weapons",
                evidence_prefix="career-weapon-fire-weapons-route",
                surface_name="Gear Weapons route accessibility node",
            )

        reset.assert_called_once_with(device, swipes=48)
        device.wait_for_single_exact_resource_id.assert_called_once_with(
            "build-action-tab-gear-weapons",
            timeout=120,
            scroll=True,
            max_scrolls=24,
            scroll_distance_ratio=0.22,
            evidence_prefix="career-weapon-fire-weapons-route",
            surface_name="Gear Weapons route accessibility node",
        )
        device.shell.assert_called_once_with("input", "tap", "541", "480")

    def test_build_route_fails_closed_when_exact_node_is_untappable(self) -> None:
        node = driver.shared.UiNode(
            {
                "resource-id": "build-action-tab-gear-weapons",
                "clickable": "true",
                "bounds": "[98,275][984,276]",
            }
        )
        device = Mock(spec=driver.shared.Device)
        device.wait_for_single_exact_resource_id.return_value = node
        device.node_has_tappable_bounds.return_value = False

        with (
            patch.object(driver.shared, "reset_scroll_to_top"),
            self.assertRaisesRegex(RuntimeError, "not tappable"),
        ):
            driver.tap_exact_build_route(
                device,
                "build-action-tab-gear-weapons",
                evidence_prefix="career-weapon-fire-weapons-route",
                surface_name="Gear Weapons route accessibility node",
            )

        device.capture.assert_called_once_with(
            "career-weapon-fire-weapons-route-untappable"
        )
        device.shell.assert_not_called()

    def test_open_page_binds_exact_gear_transition_before_weapons_route(self) -> None:
        device = Mock(spec=driver.shared.Device)
        events: list[tuple[str, str]] = []

        def record_route(
            _device: object,
            selector: str,
            *,
            evidence_prefix: str,
            surface_name: str,
        ) -> None:
            del evidence_prefix, surface_name
            events.append(("route", selector))

        def record_transition(selector: str, **_kwargs: object) -> Mock:
            events.append(("transition", selector))
            return Mock()

        device.wait_for_single_exact_resource_id.side_effect = record_transition
        with (
            patch.object(driver, "open_build_root"),
            patch.object(driver, "tap_exact_build_route", side_effect=record_route),
        ):
            driver.open_page(device)

        self.assertEqual(
            [
                ("route", "build-section-tab-gear"),
                ("transition", f"collection-item-gear-{driver.AMMO_GEAR_ID}"),
                ("route", "build-action-tab-gear-weapons"),
                ("route", f"collection-item-weapon-{driver.WEAPON_ID}"),
            ],
            events,
        )
        device.wait_for_single_exact_resource_id.assert_called_once_with(
            f"collection-item-gear-{driver.AMMO_GEAR_ID}",
            timeout=120,
            evidence_prefix="career-weapon-fire-gear-section-entered",
            surface_name="Exact fixture-linked Gear collection transition surface",
        )

    def test_open_build_root_unwinds_exact_navigation_until_root_toolbar(self) -> None:
        navigate_up = driver.shared.UiNode(
            {
                "content-desc": "Navigate up",
                "clickable": "true",
                "bounds": "[0,128][147,275]",
            }
        )
        root = driver.shared.UiNode(
            {
                "content-desc": "build-save-runner",
                "clickable": "true",
                "bounds": "[786,138][912,264]",
            }
        )
        device = Mock(spec=driver.shared.Device)
        device.hierarchy.side_effect = [[navigate_up], [root]]
        device.node_has_tappable_bounds.return_value = True

        with (
            patch.object(driver.shared, "open_build") as open_build,
            patch.object(driver.time, "sleep") as sleep,
        ):
            driver.open_build_root(device)

        open_build.assert_called_once_with(device, "phone")
        device.shell.assert_called_once_with("input", "tap", "73", "201")
        sleep.assert_called_once_with(1.25)
        device.capture.assert_not_called()

    def test_open_build_root_fails_closed_on_duplicate_root_toolbar(self) -> None:
        root = driver.shared.UiNode({"content-desc": "build-save-runner"})
        device = Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = [root, root]

        with (
            patch.object(driver.shared, "open_build"),
            self.assertRaisesRegex(RuntimeError, "toolbar cardinality was 2"),
        ):
            driver.open_build_root(device)

        device.capture.assert_called_once_with(
            "career-weapon-fire-build-root-cardinality-invalid"
        )
        device.shell.assert_not_called()

    def test_fixture_binds_exact_root_weapon_active_clip_linked_ammo_and_burst(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        driver.require_canonical_import_fixture(root)
        preserved = driver.assert_before(root)
        self.assertEqual(driver.WEAPON_ID, driver.target_weapon(root).findtext("guid"))
        self.assertEqual(driver.AMMO_GEAR_ID, driver.active_clip(root).findtext("id"))
        self.assertEqual(driver.AMMO_GEAR_ID, driver.linked_ammo(root).findtext("guid"))
        self.assertEqual("3", driver.target_weapon(root).findtext("shortburst"))
        self.assertEqual("11", driver.active_clip(root).findtext("count"))
        self.assertEqual("11", driver.linked_ammo(root).findtext("qty"))
        self.assertEqual("19", preserved["karma"])
        self.assertEqual("8765", preserved["nuyen"])
        for identity in (
            driver.WEAPON_ID,
            driver.AMMO_GEAR_ID,
            driver.UNRELATED_WEAPON_ID,
            driver.UNRELATED_GEAR_ID,
        ):
            uuid.UUID(identity)

    def test_after_contract_accepts_only_exact_three_round_delta_and_preserved_xml(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        preserved = driver.assert_before(root)
        driver.active_clip(root).find("count").text = str(driver.EXPECTED_AMMO)
        driver.linked_ammo(root).find("qty").text = str(driver.EXPECTED_AMMO)
        driver.assert_after(root, preserved)

        hostile = copy.deepcopy(root)
        driver.target_weapon(hostile).find("notes").text = "changed"
        with self.assertRaisesRegex(RuntimeError, "outside the exact clip/ammo quantities"):
            driver.assert_after(hostile, preserved)

    def test_fixture_preflight_rejects_every_missing_canonical_loader_field(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        for field in driver.CANONICAL_IMPORT_FIELDS:
            with self.subTest(field=field):
                hostile = copy.deepcopy(root)
                hostile.remove(hostile.find(field))
                with self.assertRaisesRegex(RuntimeError, rf"canonical SR5 loader: <{field}>"):
                    driver.require_canonical_import_fixture(hostile)


if __name__ == "__main__":
    unittest.main()
