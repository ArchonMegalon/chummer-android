import importlib
import sys
import unittest
from pathlib import Path
from unittest import mock
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
TESTS = REPO / "tests"
sys.path.insert(0, str(TESTS))
driver = importlib.import_module("run_api36_sr5_before_run_edge_e2e")


class BeforeRunHostedDriverTests(unittest.TestCase):
    def test_hosted_identity_is_distinct_and_narrow(self) -> None:
        self.assertEqual(
            "chummer.android.sr5-before-run-edge-e2e/v1",
            driver.RECEIPT_SCHEMA,
        )
        self.assertEqual("before-run-edge", driver.JOURNEY)
        self.assertEqual("before-run", driver.HOSTED_SPEC.lane)
        self.assertEqual("before-run.edge.spend", driver.HOSTED_SPEC.expected_action_id)
        self.assertIn("tablet", driver.HOSTED_SPEC.excluded_scope)
        self.assertIn("full editing", driver.HOSTED_SPEC.excluded_scope)
        self.assertIn("publication", driver.HOSTED_SPEC.excluded_scope)
        self.assertNotEqual(driver.lane.RECEIPT_SCHEMA, driver.RECEIPT_SCHEMA)
        self.assertNotEqual(driver.lane.JOURNEY, driver.JOURNEY)

    def test_device_authority_requires_api36_x86_64_emulator(self) -> None:
        device = mock.Mock(spec=driver.shared.Device)
        device.shell.side_effect = ("36", "x86_64", "1")
        self.assertEqual(
            {"apiLevel": 36, "abi": "x86_64", "emulator": True},
            driver.require_hosted_device(device),
        )
        for label, observations, message in (
            ("api", ("35",), "requires API 36"),
            ("abi", ("36", "arm64-v8a"), "x86_64 phone lane"),
            ("physical", ("36", "x86_64", "0"), "emulator authority"),
        ):
            with self.subTest(label=label):
                hostile = mock.Mock(spec=driver.shared.Device)
                hostile.shell.side_effect = observations
                with self.assertRaisesRegex(RuntimeError, message):
                    driver.require_hosted_device(hostile)

    def test_exact_fixture_and_unrelated_xml_are_bound(self) -> None:
        fixture = driver.HOSTED_SPEC.fixture
        root = ET.parse(fixture).getroot()
        before = driver.lane.assert_before_state(root)
        changed = ET.parse(fixture).getroot()
        changed.find("edgeused").text = "1"
        state = driver.lane.assert_after_state(changed, before)
        self.assertEqual(
            {"lane": "before-run", "edgeUsed": 1, "totalEdge": 4},
            state,
        )
        hostile = ET.parse(fixture).getroot()
        hostile.find("edgeused").text = "1"
        hostile.find("./customstate/sentinel").text = "changed"
        with self.assertRaisesRegex(RuntimeError, "changed unrelated fixture XML"):
            driver.lane.assert_after_state(hostile, before)

    def test_source_graph_binds_typed_app_presentation_core_and_fixture(self) -> None:
        paths = driver.source_paths(
            driver=Path(driver.__file__).resolve(),
            android_root=REPO,
            workspace_root=REPO.parent,
            fixture=driver.HOSTED_SPEC.fixture,
        )
        self.assertTrue(paths)
        self.assertTrue(all(path.is_file() for path in paths.values()))
        self.assertIn("sharedBeforeRunLaneDriverSha256", paths)
        self.assertIn("tableWizardTransactionSha256", paths)
        self.assertIn("careerEdgeRequestSha256", paths)
        self.assertIn("careerEdgeRulesSha256", paths)
        self.assertIn("workspaceStoreSha256", paths)
        self.assertIn("fixtureSha256", paths)

    def test_main_uses_shared_exact_lane_and_emits_no_physical_authority(self) -> None:
        source = Path(driver.__file__).read_text(encoding="utf-8")
        self.assertIn("lane.prove_lane(", source)
        self.assertIn('device.require_transport_stability(expected_api_level="36")', source)
        self.assertIn('device.install_verified(apk, apk_sha256, "--no-streaming", "-r")', source)
        self.assertIn('"publicationAuthorized": False', source)
        self.assertNotIn("build-provenance-manifest", source)
        self.assertNotIn("allow-destructive-disposable-device", source)
        self.assertNotIn('"arm64-v8a"', source)


if __name__ == "__main__":
    unittest.main()
