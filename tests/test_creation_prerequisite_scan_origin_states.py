"""Deterministic observer-state tests; these do not execute Android lifecycle code."""

import copy
from contextlib import contextmanager
from datetime import datetime
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest import mock


DRIVER = Path(__file__).resolve().parent / "run_api36_creation_prerequisite_e2e.py"
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("creation_scan_origin_state_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(driver)


def node(selector: str, **attributes: str) -> driver.shared.UiNode:
    values = {
        "package": driver.shared.PACKAGE,
        "resource-id": f"{driver.shared.PACKAGE}:id/{selector}",
        "text": selector,
        "enabled": "true",
        "clickable": "false",
        "bounds": "[10,100][900,180]",
    }
    values.update(attributes)
    return driver.shared.UiNode(values)


def ready_nodes() -> list[driver.shared.UiNode]:
    return [
        node("creation-prerequisite-page"),
        node("creation-prerequisite-method", text="Priority"),
        node("creation-prerequisite-binding", text="Revision 3"),
    ]


class Clock:
    def __init__(self):
        self.now = 100.0
        self.sleeps: list[float] = []

    def read(self):
        return self.now

    def sleep(self, seconds):
        if seconds < 0:
            raise AssertionError("Negative wait")
        self.sleeps.append(seconds)
        self.now += seconds


class ObservedDevice:
    """Only transport is modeled; unplanned reads fail instead of hanging."""

    def __init__(self, clock, frames, *, direct=None):
        self.clock = clock
        self.frames = list(frames)
        self.direct = direct
        self.file_calls: list[dict] = []
        self.direct_calls: list[dict] = []
        self.swipes: list[dict] = []
        self.captures: list[str] = []
        self.mutations: list[tuple] = []

    def hierarchy(self, **kwargs):
        self.file_calls.append(kwargs)
        self.clock.now += 0.1
        if not self.frames:
            raise AssertionError("Scanner requested an unplanned hierarchy")
        frame = self.frames.pop(0)
        if callable(frame):
            frame = frame()
        if isinstance(frame, BaseException):
            raise frame
        return frame

    def read_only_hierarchy_once(self, **kwargs):
        self.direct_calls.append(kwargs)
        self.clock.now += 0.1
        if len(self.direct_calls) != 1:
            raise AssertionError("Direct observation was replayed")
        if isinstance(self.direct, BaseException):
            raise self.direct
        if self.direct is None:
            raise AssertionError("Unplanned direct observation")
        return self.direct

    def dismiss_system_ui_anr(self, _nodes, **_kwargs):
        return False

    def swipe_down(self, **kwargs):
        self.swipes.append(kwargs)

    def capture(self, name, **_kwargs):
        self.captures.append(name)

    def shell(self, *args, **_kwargs):
        self.mutations.append(args)
        raise AssertionError("Origin acquisition must not replay an opening action")


@contextmanager
def deterministic_clock(clock):
    with mock.patch.object(driver.time, "monotonic", clock.read), mock.patch.object(
        driver.time, "perf_counter", clock.read
    ), mock.patch.object(driver.time, "sleep", clock.sleep):
        yield


class CreationPrerequisiteScanOriginStateTests(unittest.TestCase):
    def assert_no_diagnostic_or_mutation_commands(self, device):
        self.assertEqual([], device.captures)
        self.assertEqual([], device.mutations)

    def assert_observation(self, scan, expected_nodes, mode):
        observation = scan["lastObservation"]
        self.assertEqual(
            "chummer.android.prerequisite-origin-observation/v1",
            observation["schema"],
        )
        self.assertEqual(mode, observation["observationMode"])
        self.assertEqual(
            driver.CREATION_METHOD_ONE_SHOT_DIGEST_DOMAIN,
            observation["hierarchyDigestDomain"],
        )
        self.assertEqual(
            driver.accessibility_signature_sha256(expected_nodes),
            observation["hierarchyDigest"],
        )
        self.assertEqual(len(expected_nodes), observation["nodeCount"])
        self.assertEqual(
            [dict(item.attributes) for item in expected_nodes], observation["nodes"]
        )
        self.assertIsNotNone(datetime.fromisoformat(observation["observedAtUtc"]).tzinfo)
        return observation

    def test_loading_waits_then_reuses_only_fresh_ready_frame(self):
        clock = Clock()
        ready = ready_nodes()
        loading = [node("creation-prerequisite-page"), node("creation-prerequisite-loading")]
        device = ObservedDevice(clock, [loading, ready])
        scans = []
        with deterministic_clock(clock):
            origin = driver.wait_for_prerequisite_scan_origin(
                device, deadline=130.0, immediately_after_opening_tap=True,
                scan_observer=scans.append,
            )
        self.assertIs(ready, origin.nodes)
        self.assertEqual(2, len(device.file_calls))
        self.assertEqual(0, origin.reverse_swipes)
        self.assertEqual([], device.swipes)
        self.assertEqual([], device.direct_calls)
        self.assertTrue(clock.sleeps)
        self.assertEqual(1, len(scans))
        self.assertEqual("resolved", scans[0]["status"])
        self.assertNotIn("lastObservation", scans[0])
        self.assert_no_diagnostic_or_mutation_commands(device)

    def test_loading_precedes_retained_anchors_and_retained_unavailable_cards(self):
        for terminal in (None, "creation-prerequisite-unavailable", "creation-prerequisite-blockers"):
            with self.subTest(terminal=terminal):
                clock = Clock()
                stale = ready_nodes()
                stale[2].attributes["text"] = "Revision 2"
                stale.append(node("creation-prerequisite-loading"))
                if terminal:
                    stale.append(node(terminal))
                ready = ready_nodes()
                device = ObservedDevice(clock, [stale, ready])
                with deterministic_clock(clock):
                    origin = driver.wait_for_prerequisite_scan_origin(
                        device, deadline=130.0, immediately_after_opening_tap=True
                    )
                self.assertIs(ready, origin.nodes)
                self.assertEqual(2, len(device.file_calls))
                self.assertEqual([], device.swipes)
                self.assertEqual([], device.direct_calls)
                self.assert_no_diagnostic_or_mutation_commands(device)

    def test_terminal_authority_markers_override_otherwise_valid_top_anchors(self):
        for selector in ("creation-prerequisite-unavailable", "creation-prerequisite-blockers"):
            with self.subTest(selector=selector):
                clock = Clock()
                frame = ready_nodes() + [node(selector, text="stale-workspace-revision")]
                device = ObservedDevice(clock, [frame])
                scans = []
                with deterministic_clock(clock), self.assertRaisesRegex(
                    RuntimeError, "authority unavailable"
                ):
                    driver.wait_for_prerequisite_scan_origin(
                        device, deadline=130.0, immediately_after_opening_tap=True,
                        scan_observer=scans.append,
                    )
                self.assertEqual(1, len(device.file_calls))
                self.assertEqual(1, len(scans))
                self.assert_observation(scans[0], frame, "fresh-file-backed")
                self.assertEqual([], device.swipes)
                self.assertEqual([], device.direct_calls)
                self.assert_no_diagnostic_or_mutation_commands(device)

    def test_duplicate_state_markers_are_rejected_even_while_loading(self):
        for selector in (
            "creation-prerequisite-loading",
            "creation-prerequisite-unavailable",
            "creation-prerequisite-blockers",
        ):
            with self.subTest(selector=selector):
                clock = Clock()
                frame = ready_nodes() + [node(selector), node(selector)]
                if selector != "creation-prerequisite-loading":
                    frame.append(node("creation-prerequisite-loading"))
                device = ObservedDevice(clock, [frame])
                with deterministic_clock(clock), self.assertRaisesRegex(
                    RuntimeError, "state cardinality"
                ):
                    driver.wait_for_prerequisite_scan_origin(device, deadline=130.0)
                self.assertEqual(1, len(device.file_calls))
                self.assertEqual([], device.swipes)
                self.assertEqual([], device.direct_calls)
                self.assert_no_diagnostic_or_mutation_commands(device)

    def test_state_marker_identity_is_exact_before_loading_or_anchor_admission(self):
        for selector in (
            "creation-prerequisite-loading",
            "creation-prerequisite-unavailable",
            "creation-prerequisite-blockers",
        ):
            for forged in (
                {"package": "com.example.forged"},
                {"resource-id": selector},
                {"resource-id": f"com.example.forged:id/{selector}"},
            ):
                with self.subTest(selector=selector, forged=forged):
                    clock = Clock()
                    frame = ready_nodes() + [node(selector, **forged)]
                    if selector != "creation-prerequisite-loading":
                        frame.append(node("creation-prerequisite-loading"))
                    device = ObservedDevice(clock, [frame])
                    with deterministic_clock(clock), self.assertRaisesRegex(
                        RuntimeError, "state identity"
                    ):
                        driver.wait_for_prerequisite_scan_origin(device, deadline=130.0)
                    self.assertEqual(1, len(device.file_calls))
                    self.assertEqual([], device.swipes)
                    self.assertEqual([], device.direct_calls)
                    self.assert_no_diagnostic_or_mutation_commands(device)

    def test_direct_loading_frame_is_not_retried_or_accepted_with_stale_anchors(self):
        clock = Clock()
        frame = ready_nodes() + [node("creation-prerequisite-loading")]
        device = ObservedDevice(
            clock, [driver.shared.AdbHierarchyLeaseReserveExceeded("lease reserve")],
            direct=frame,
        )
        scans = []
        with deterministic_clock(clock), self.assertRaisesRegex(
            RuntimeError, "did not expose a ready prerequisite surface"
        ):
            driver.wait_for_prerequisite_scan_origin(
                device, deadline=130.0, immediately_after_opening_tap=True,
                scan_observer=scans.append,
            )
        self.assertEqual(1, len(device.file_calls))
        self.assertEqual(1, len(device.direct_calls))
        self.assertEqual([], device.swipes)
        self.assertEqual(1, len(scans))
        self.assert_observation(scans[0], frame, "single-direct-read-only")
        self.assertEqual(1, scans[0]["directFallbackReadCount"])
        self.assert_no_diagnostic_or_mutation_commands(device)

    def test_direct_terminal_frame_overrides_valid_anchors_without_another_read(self):
        for selector in ("creation-prerequisite-unavailable", "creation-prerequisite-blockers"):
            with self.subTest(selector=selector):
                clock = Clock()
                frame = ready_nodes() + [node(selector)]
                device = ObservedDevice(
                    clock, [driver.shared.AdbHierarchyLeaseReserveExceeded("lease reserve")],
                    direct=frame,
                )
                scans = []
                with deterministic_clock(clock), self.assertRaisesRegex(
                    RuntimeError, "authority unavailable"
                ):
                    driver.wait_for_prerequisite_scan_origin(
                        device, deadline=130.0, immediately_after_opening_tap=True,
                        scan_observer=scans.append,
                    )
                self.assertEqual(1, len(device.file_calls))
                self.assertEqual(1, len(device.direct_calls))
                self.assertEqual([], device.swipes)
                self.assert_observation(scans[0], frame, "single-direct-read-only")
                self.assert_no_diagnostic_or_mutation_commands(device)

    def test_loading_respects_existing_caller_deadline_without_navigation(self):
        clock = Clock()
        frame = [node("creation-prerequisite-page"), node("creation-prerequisite-loading")]
        device = ObservedDevice(clock, [frame, frame, frame])
        scans = []
        with deterministic_clock(clock), self.assertRaisesRegex(RuntimeError, "deadline"):
            driver.wait_for_prerequisite_scan_origin(
                device, timeout=60.0, deadline=101.0, immediately_after_opening_tap=True,
                scan_observer=scans.append,
            )
        self.assertEqual(3, len(device.file_calls))
        self.assertEqual([0.25, 0.25], clock.sleeps)
        self.assertAlmostEqual(100.8, clock.now)
        self.assertTrue(all(call["deadline"] == 101.0 for call in device.file_calls))
        self.assertEqual([], device.swipes)
        self.assertEqual([], device.direct_calls)
        self.assertEqual(1, len(scans))
        self.assertNotEqual("resolved", scans[0]["status"])
        self.assert_observation(scans[0], frame, "fresh-file-backed")
        self.assertEqual(3, scans[0]["fileBackedObservationAttempts"])
        self.assertEqual(3, scans[0]["hierarchyReadCount"])
        self.assert_no_diagnostic_or_mutation_commands(device)

    def test_generic_reader_failure_retains_last_frame_without_diagnostic_commands(self):
        for selector, expected_swipes in (
            ("creation-prerequisite-loading", 0),
            ("creation-prerequisite-source-authority", 1),
        ):
            with self.subTest(selector=selector):
                clock = Clock()
                frame = [node("creation-prerequisite-page"), node(selector)]
                failure = RuntimeError("retained-test-reader-failure")
                device = ObservedDevice(clock, [frame, failure])
                scans = []
                with deterministic_clock(clock), self.assertRaisesRegex(
                    RuntimeError, "retained-test-reader-failure"
                ) as raised:
                    driver.wait_for_prerequisite_scan_origin(
                        device, deadline=130.0, immediately_after_opening_tap=True,
                        scan_observer=scans.append,
                    )
                self.assertIs(failure, raised.exception)
                self.assertEqual(2, len(device.file_calls))
                self.assertEqual([], device.frames)
                self.assertEqual(expected_swipes, len(device.swipes))
                self.assertEqual([], device.direct_calls)
                self.assertEqual(1, len(scans))
                self.assertNotEqual("resolved", scans[0]["status"])
                self.assert_observation(scans[0], frame, "fresh-file-backed")
                self.assertEqual(2, scans[0]["fileBackedObservationAttempts"])
                self.assertEqual(2, scans[0]["hierarchyReadCount"])
                self.assert_no_diagnostic_or_mutation_commands(device)

    def test_last_nonempty_observation_survives_mutation_empty_read_and_lease_failure(self):
        clock = Clock()
        lower = [node("creation-prerequisite-page"), node("creation-prerequisite-source-authority")]
        expected = [driver.shared.UiNode(dict(item.attributes)) for item in lower]
        expected_attributes = copy.deepcopy([item.attributes for item in lower])

        def changed_then_empty():
            lower[0].attributes["text"] = "mutated after the earlier observation"
            lower[1].attributes.clear()
            lower.append(node("creation-prerequisite-method"))
            return []

        device = ObservedDevice(
            clock, [lower, changed_then_empty,
                    driver.shared.AdbHierarchyLeaseReserveExceeded("lease reserve")]
        )
        scans = []
        with deterministic_clock(clock), self.assertRaisesRegex(
            RuntimeError, "untouched post-opening viewport"
        ):
            driver.wait_for_prerequisite_scan_origin(
                device, deadline=130.0, immediately_after_opening_tap=True,
                scan_observer=scans.append,
            )
        self.assertEqual(3, len(device.file_calls))
        self.assertEqual(1, len(device.swipes))
        self.assertEqual([], device.direct_calls)
        self.assertEqual(1, len(scans))
        observation = self.assert_observation(scans[0], expected, "fresh-file-backed")
        self.assertEqual(expected_attributes, observation["nodes"])
        self.assertEqual(1, scans[0]["emptyHierarchyReads"])
        self.assertEqual(3, scans[0]["fileBackedObservationAttempts"])
        self.assertEqual(3, scans[0]["hierarchyReadCount"])
        self.assertEqual(0, scans[0]["directFallbackReadCount"])
        self.assertEqual("rejected-after-navigation", scans[0]["directFallbackResult"])
        observation["nodes"][0]["text"] = "diagnostic consumer mutation"
        self.assertEqual("mutated after the earlier observation", lower[0].attributes["text"])
        self.assert_no_diagnostic_or_mutation_commands(device)

    def test_lower_ready_surface_keeps_existing_bounded_reverse_navigation(self):
        clock = Clock()
        lower = [node("creation-prerequisite-page"), node("creation-prerequisite-source-authority")]
        ready = ready_nodes()
        device = ObservedDevice(clock, [lower, ready])
        with deterministic_clock(clock):
            origin = driver.wait_for_prerequisite_scan_origin(
                device, deadline=130.0, immediately_after_opening_tap=True
            )
        self.assertIs(ready, origin.nodes)
        self.assertEqual(2, len(device.file_calls))
        self.assertEqual(1, origin.reverse_swipes)
        self.assertEqual([{"distance_ratio": 0.68, "deadline": 130.0}], device.swipes)
        self.assertEqual([], device.direct_calls)
        self.assert_no_diagnostic_or_mutation_commands(device)

    def test_preview_blocker_is_not_misclassified_as_terminal_load_authority(self):
        clock = Clock()
        ready = ready_nodes() + [node("creation-prerequisite-preview-blockers")]
        device = ObservedDevice(clock, [ready])
        with deterministic_clock(clock):
            origin = driver.wait_for_prerequisite_scan_origin(device, deadline=130.0)
        self.assertIs(ready, origin.nodes)
        self.assertEqual(1, len(device.file_calls))
        self.assertEqual([], device.swipes)
        self.assertEqual([], device.direct_calls)
        self.assert_no_diagnostic_or_mutation_commands(device)


if __name__ == "__main__":
    unittest.main()
