"""Captured Talent layout reproduction; synthetic movement is not API-36 proof."""

import json
from pathlib import Path
import unittest
from unittest import mock

import run_api36_creation_prerequisite_e2e as driver


FIXTURE = json.loads((Path(__file__).parent / "fixtures" /
    "talent-completion-reflow-34049451151.json").read_text())
OPTION = FIXTURE["optionId"]
COMPLETE = "creation-prerequisite-talent-grant-complete"
ROUTE = "creation-prerequisite-talent-grant-page"
AUTHORITY = "creation-prerequisite-talent-grant-authority"
DIGEST = "creation-prerequisite-talent-grant-digest"


def node(selector, **attributes):
    return driver.shared.UiNode({
        "resource-id": f"{driver.shared.PACKAGE}:id/{selector}",
        "package": driver.shared.PACKAGE,
        "enabled": "true", "clickable": "true", **attributes,
    })


def bottom_frame(*, recovered=False, selected=True):
    result = [
        node(ROUTE, bounds=FIXTURE["viewportBounds"]),
        node("", bounds=FIXTURE["viewportBounds"],
             **{"class": "android.widget.ScrollView", "scrollable": "true"}),
        node(OPTION, bounds=FIXTURE["recoveredOptionBounds"] if recovered
             else FIXTURE["reflowOptionBounds"],
             **{"content-desc": FIXTURE["selectedDetail"] if selected
                else FIXTURE["optionDetail"]}),
    ]
    if recovered:
        result.append(node(COMPLETE, bounds=FIXTURE["initialCompletionBounds"],
                           text="Continue with exact grant"))
    return result


class ReflowDevice:
    node_has_tappable_bounds = driver.shared.Device.node_has_tappable_bounds

    def __init__(self, frames=None):
        self.frames = frames or [bottom_frame(), bottom_frame(recovered=True)]
        self.forward_swipes = []
        self.reverse_swipes = []
        self.hierarchy_reads = 0
        self.captures = []
        self.deadlines = []

    def hierarchy(self, *, deadline=None):
        self.hierarchy_reads += 1
        self.deadlines.append(deadline)
        if self.reverse_swipes:
            return [node(AUTHORITY, bounds="[0,300][100,400]",
                         **{"content-desc": "1 / 1 Active skills"}),
                    node(DIGEST, bounds="[0,400][100,500]",
                         text="sha256:" + "a" * 64)]
        return self.frames[min(len(self.forward_swipes), len(self.frames) - 1)]

    def display_size(self, *, deadline=None):
        self.deadlines.append(deadline)
        return tuple(FIXTURE["displaySize"])

    def swipe_up(self, *, distance_ratio, deadline=None, allow_direct_reconciliation=True):
        if allow_direct_reconciliation:
            raise AssertionError("Content reflow must not replay an uncertain gesture")
        self.deadlines.append(deadline)
        self.forward_swipes.append(distance_ratio)

    def swipe_down(self, *, distance_ratio, deadline=None):
        self.deadlines.append(deadline)
        self.reverse_swipes.append(distance_ratio)

    def dismiss_system_ui_anr(self, nodes, *, deadline=None):
        self.deadlines.append(deadline)
        return False

    def capture(self, name, *, deadline=None):
        self.captures.append(name)

    def shell(self, *args, **kwargs):
        raise AssertionError("Observation must never replay a mutation")


class TalentCompletionReflowTests(unittest.TestCase):
    def acquire(self, device, **kwargs):
        return driver.reacquire_exact_talent_state_group(
            device, (OPTION, COMPLETE), 9, 9, 9,
            evidence_prefix="reflow",
            completion_reflow_selected_options={OPTION: ((FIXTURE["optionDetail"],), 1)},
            **kwargs,
        )

    @staticmethod
    def replace(frame, selector, **changes):
        return [driver.shared.UiNode({**item.attributes, **changes})
                if driver._exact_resource_id(item) == selector else item
                for item in frame]

    def test_captured_selected_swimming_reflow_recovers_grouped_state(self):
        device = ReflowDevice()
        receipts = []
        baseline = driver.TalentGrantSurface(
            "Active skills", 0, 1, "sha256:" + "a" * 64,
            (OPTION,), (OPTION,), (), False,
        )
        navigation = {
            "endViewport": 9,
            "resourceViewports": {OPTION: 9, COMPLETE: 9, AUTHORITY: 0, DIGEST: 0},
            "resourceDetails": {OPTION: (FIXTURE["optionDetail"],)},
        }
        with mock.patch.object(driver.time, "sleep"):
            state, viewport = driver.read_talent_grant_grouped_state(
                device, "Active skills", baseline, navigation, 9,
                expected_selected_option_ids=(OPTION,),
                expected_completion_enabled=True,
                evidence_prefix="captured-reflow", scan_observer=receipts.append,
            )
        self.assertEqual(driver.TalentGrantMutableState(1, (OPTION,), True), state)
        self.assertEqual(0, viewport)
        self.assertEqual([0.22], device.forward_swipes)
        self.assertEqual([0.60], device.reverse_swipes)
        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual([], device.captures)
        self.assertTrue(receipts[0]["completionReflowUsed"])
        self.assertEqual(0, receipts[0]["measuredDelta"])
        self.assertEqual(9, receipts[0]["normalizedTargetViewport"])

    def test_resolved_group_uses_fresh_post_scroll_nodes(self):
        device = ReflowDevice()
        with mock.patch.object(driver.time, "sleep"):
            result = self.acquire(device)
        self.assertIs(result.resources[OPTION], device.frames[1][2])
        self.assertIs(result.resources[COMPLETE], device.frames[1][3])
        self.assertEqual(9, result.logical_viewport)
        self.assertEqual(1, result.reacquisition_swipes)
        self.assertEqual(2, device.hierarchy_reads)
        self.assertEqual([], device.reverse_swipes)

    def test_invalid_initial_or_terminal_semantics_stop_without_more_movement(self):
        for recovered in (False, True):
            frame = bottom_frame(recovered=recovered)
            invalid = {
                "stale-selection": self.replace(frame, OPTION,
                    **{"content-desc": FIXTURE["optionDetail"]}),
                "wrong-slot": self.replace(frame, OPTION,
                    **{"content-desc": FIXTURE["selectedDetail"].replace("slot 1", "slot 2")}),
                "forged-detail": self.replace(frame, OPTION,
                    **{"content-desc": FIXTURE["selectedDetail"] + " forged"}),
                "disabled": self.replace(frame, OPTION, enabled="false"),
                "unclickable": self.replace(frame, OPTION, clickable="false"),
                "wrong-package": self.replace(frame, OPTION, package="other.app"),
                "route-missing": [n for n in frame if driver._exact_resource_id(n) != ROUTE],
                "conflicting-route": [*frame, node("creation-prerequisite-page")],
                "route-duplicate": [*frame, frame[0]],
                "route-package": self.replace(frame, ROUTE, package="other.app"),
                "duplicate-option": [*frame, frame[2]],
                "missing-option": [n for n in frame if driver._exact_resource_id(n) != OPTION],
                "option-outside-page": self.replace(frame, OPTION, bounds="[98,100][984,350]"),
                "extra-scroll-surface": [*frame, frame[1]],
                "stale-grant": [*frame, node("creation-prerequisite-talent-grant-stale")],
                "blocked-grant": [*frame, node("creation-prerequisite-talent-grant-blockers")],
            }
            if recovered:
                invalid.update({
                    "completion-duplicate": [*frame, frame[3]],
                    "completion-stale": self.replace(frame, COMPLETE, enabled="false"),
                    "completion-forged": self.replace(frame, COMPLETE, text="Choose 1 more"),
                    "completion-outside-page": self.replace(frame, COMPLETE, bounds="[53,2200][1028,2300]"),
                    "completion-before-option": self.replace(frame, COMPLETE, bounds="[53,300][1028,400]"),
                    "completion-package": self.replace(frame, COMPLETE, package="other.app"),
                })
            for label, bad_frame in invalid.items():
                with self.subTest(frame="terminal" if recovered else "initial", defect=label):
                    device = ReflowDevice([bottom_frame(), bad_frame] if recovered else [bad_frame])
                    receipts = []
                    with mock.patch.object(driver.time, "sleep"), self.assertRaises(RuntimeError):
                        self.acquire(device, scan_observer=receipts.append)
                    self.assertEqual([0.22] if recovered else [], device.forward_swipes)
                    self.assertEqual([], device.reverse_swipes)
                    self.assertEqual(1, len(receipts))
                    self.assertNotEqual("resolved", receipts[0]["status"])

    def test_changed_page_viewport_and_unsafe_swipe_segment_fail_closed(self):
        for bounds in ("[0,275][1080,2100]", "[0,275][1080,1900]"):
            with self.subTest(bounds=bounds):
                frame = bottom_frame(recovered=True)
                frame[0] = node(ROUTE, bounds=bounds)
                frame[1] = node("", bounds=bounds,
                    **{"class": "android.widget.ScrollView", "scrollable": "true"})
                device = ReflowDevice([bottom_frame(), frame])
                with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(RuntimeError, "page viewport"):
                    self.acquire(device)
                self.assertEqual([0.22], device.forward_swipes)
        frame = bottom_frame()
        frame[0] = node(ROUTE, bounds="[0,275][1080,1900]")
        frame[1] = node("", bounds="[0,275][1080,1900]",
            **{"class": "android.widget.ScrollView", "scrollable": "true"})
        device = ReflowDevice([frame])
        with self.assertRaisesRegex(RuntimeError, "page viewport"):
            self.acquire(device)
        self.assertEqual([], device.forward_swipes)

    def test_non_scrollable_outer_shell_does_not_look_like_second_content_viewport(self):
        frames = []
        for recovered in (False, True):
            frame = bottom_frame(recovered=recovered)
            frame.append(node("shell", bounds="[0,0][1080,2190]",
                **{"class": "android.widget.ScrollView", "scrollable": "false"}))
            frames.append(frame)
        with mock.patch.object(driver.time, "sleep"):
            result = self.acquire(ReflowDevice(frames))
        self.assertEqual(1, result.reacquisition_swipes)

    def test_stable_missing_footer_stops_after_two_corrections(self):
        device = ReflowDevice([bottom_frame()])
        receipts = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(RuntimeError, "stable physical boundary"):
            self.acquire(device, scan_observer=receipts.append)
        self.assertEqual([0.22, 0.22], device.forward_swipes)
        self.assertEqual([], device.reverse_swipes)
        self.assertEqual("stable-boundary-unresolved", receipts[0]["status"])

    def test_moving_missing_footer_cannot_exceed_four_corrections(self):
        frames = [bottom_frame() + [node("other", text=str(i))] for i in range(5)]
        device = ReflowDevice(frames)
        receipts = []
        with mock.patch.object(driver.time, "sleep"), self.assertRaisesRegex(RuntimeError, "4-swipe forward primary hard bound"):
            self.acquire(device, scan_observer=receipts.append)
        self.assertEqual([0.22] * 4, device.forward_swipes)
        self.assertEqual(5, device.hierarchy_reads)
        self.assertEqual([], device.reverse_swipes)
        self.assertEqual(4, receipts[0]["completionReflowMaxScrolls"])
        self.assertEqual("primary-hard-bound-unresolved", receipts[0]["status"])

    def test_footer_only_or_no_expected_selected_authority_cannot_recover(self):
        for resources, expected in (
            ((COMPLETE,), {OPTION: ((FIXTURE["optionDetail"],), 1)}),
            ((OPTION, COMPLETE), None),
            ((OPTION, COMPLETE), {OPTION: ((FIXTURE["optionDetail"],), 0)}),
        ):
            with self.subTest(resources=resources, expected=expected):
                device = ReflowDevice()
                with self.assertRaisesRegex(RuntimeError, "without a safe directional hint"):
                    driver.reacquire_exact_talent_state_group(
                        device, resources, 9, 9, 9, evidence_prefix="no-authority",
                        completion_reflow_selected_options=expected,
                    )
                self.assertEqual([], device.forward_swipes)

    def test_reflow_observations_and_gesture_share_original_deadline(self):
        deadline = driver.time.monotonic() + 30
        device = ReflowDevice()
        with mock.patch.object(driver.time, "sleep"):
            self.acquire(device, deadline=deadline)
        self.assertEqual({deadline}, set(device.deadlines))

    def test_reflow_keeps_empty_and_system_ui_retry_budgets_separate(self):
        overlay = node("system-ui-overlay")

        class TransientDevice(ReflowDevice):
            def hierarchy(self, *, deadline=None):
                frames = [bottom_frame(), [], [overlay], bottom_frame(recovered=True)]
                frame = frames[min(self.hierarchy_reads, len(frames) - 1)]
                self.hierarchy_reads += 1
                return frame

            def dismiss_system_ui_anr(self, nodes, *, deadline=None):
                return nodes == [overlay]

        device = TransientDevice()
        receipts = []
        with mock.patch.object(driver.time, "sleep"):
            result = self.acquire(device, scan_observer=receipts.append,
                max_empty_hierarchy_reads=1, max_system_ui_dismissals=1)
        self.assertEqual(1, result.reacquisition_swipes)
        self.assertEqual(4, device.hierarchy_reads)
        self.assertEqual(1, receipts[0]["emptyHierarchyReads"])
        self.assertEqual(1, receipts[0]["systemUiDismissals"])
        for empty_budget, ui_budget, status in (
            (0, 1, "empty-hierarchy-exhausted"),
            (1, 0, "system-ui-exhausted"),
        ):
            with self.subTest(status=status):
                device = TransientDevice()
                receipts = []
                with mock.patch.object(driver.time, "sleep"), self.assertRaises(RuntimeError):
                    self.acquire(device, scan_observer=receipts.append,
                        max_empty_hierarchy_reads=empty_budget,
                        max_system_ui_dismissals=ui_budget)
                self.assertEqual([0.22], device.forward_swipes)
                self.assertEqual(status, receipts[0]["status"])

    def test_expired_deadline_after_one_correction_cannot_read_or_act_again(self):
        device = ReflowDevice()
        receipts = []
        with mock.patch.object(driver.time, "monotonic", side_effect=lambda: 2 if device.forward_swipes else 0), \
                mock.patch.object(driver.time, "sleep"), \
                self.assertRaisesRegex(RuntimeError, "phase deadline"):
            self.acquire(device, deadline=1, scan_observer=receipts.append)
        self.assertEqual([0.22], device.forward_swipes)
        self.assertEqual(1, device.hierarchy_reads)
        self.assertEqual("deadline-unresolved", receipts[0]["status"])

    def test_uncertain_reflow_gesture_is_never_reconciled_or_replayed(self):
        error = driver.shared.AdbTransportError({
            "classification": "unknown-outcome", "commandPolicy": "mutation",
            "replay": {"performed": False, "suppressed": True},
        }, Path("synthetic-transport-receipt.json"))

        class UncertainGestureDevice(ReflowDevice):
            swipe_up = driver.shared.Device.swipe_up

            def shell(self, *args, **kwargs):
                self.forward_swipes.append(args)
                raise error

            def _reconcile_unknown_swipe(self, *args, **kwargs):
                raise AssertionError("Uncertain reflow must not reconcile or replay")

        device = UncertainGestureDevice()
        receipts = []
        with self.assertRaises(driver.shared.AdbTransportError):
            self.acquire(device, scan_observer=receipts.append)
        self.assertEqual(1, len(device.forward_swipes))
        self.assertEqual(("input", "swipe", "540", "1968", "540", "1440", "300"),
                         device.forward_swipes[0])
        self.assertEqual(1, device.hierarchy_reads)
        self.assertEqual("gesture-or-transport-unresolved", receipts[0]["status"])

    def test_emitted_reflow_receipt_survives_json_and_strict_aggregate_validation(self):
        from test_api36_e2e_artifact_authority import AGGREGATE, talent_reacquisition_scan

        device = ReflowDevice()
        receipts = []
        with mock.patch.object(driver.time, "monotonic", side_effect=lambda: len(device.forward_swipes) * 0.2), \
                mock.patch.object(driver.time, "perf_counter", return_value=0), \
                mock.patch.object(driver.time, "sleep"):
            self.acquire(device, deadline=30, scan_observer=receipts.append)
        reflow = json.loads(json.dumps(receipts[0]))
        reflow["phaseId"] = "talent-active-skill-reselection"
        scans = [talent_reacquisition_scan(phase) for phase in AGGREGATE.TALENT_REACQUISITION_PHASES
                 if phase != reflow["phaseId"]]
        scans.append(reflow)
        AGGREGATE.require_talent_reacquisition_scans(
            {"scans": scans}, phase_elapsed_by_id=AGGREGATE.CREATION_PHASE_BUDGETS_MS,
        )


if __name__ == "__main__":
    unittest.main()
