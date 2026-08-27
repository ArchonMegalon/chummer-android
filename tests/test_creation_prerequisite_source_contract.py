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


class CreationPrerequisiteSourceContractTests(unittest.TestCase):
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
                            "08-27 12:00:00.000 I/ChummerBootstrap: "
                            + driver.CREATION_BOOTSTRAP_TIMING_PREFIX
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

    def test_creation_bootstrap_marker_poll_uses_only_exact_tagged_logcat(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            payload = self.bootstrap_timing_payload()

            class TimingDevice:
                evidence = Path(temporary)
                calls: list[tuple[str, ...]] = []

                @classmethod
                def run(cls, *arguments: str, **_options: object) -> subprocess.CompletedProcess:
                    cls.calls.append(arguments)
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
                [driver.shared.ADB_CREATION_BOOTSTRAP_LOGCAT_ARGUMENTS],
                TimingDevice.calls,
            )
            self.assertEqual("resolved", observation["status"])
            self.assertEqual(1, observation["logcatReadCount"])

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
            device.run.return_value = subprocess.CompletedProcess(
                driver.shared.ADB_CREATION_BOOTSTRAP_LOGCAT_ARGUMENTS,
                0,
                stdout="",
                stderr="",
            )
            observation: dict[str, object] = {}
            with mock.patch.object(
                driver.time,
                "monotonic",
                side_effect=[0.0, 0.0, 0.0, 2.0, 2.0],
            ), self.assertRaisesRegex(RuntimeError, "post-action creation bootstrap"):
                driver.wait_for_creation_bootstrap_timing_log(
                    device,
                    timeout=1.0,
                    observation_out=observation,
                )
            device.run.assert_called_once_with(
                *driver.shared.ADB_CREATION_BOOTSTRAP_LOGCAT_ARGUMENTS,
                timeout=30,
            )
            device.capture.assert_called_once_with(
                "creation-bootstrap-timing-log-timeout"
            )
            self.assertEqual("timeout", observation["status"])

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
                return nodes

            @staticmethod
            def hierarchy():
                raise AssertionError("stable scan used the two-command hierarchy path")

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

    def test_progress_recorder_writes_ordered_atomic_timing_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with mock.patch("builtins.print") as emit:
                progress = driver.ProgressRecorder(root)
                for phase_id in driver.PHASE_ORDER:
                    progress.advance(phase_id)
                    if phase_id == "initial-authority":
                        for milestone_id in driver.INITIAL_AUTHORITY_MILESTONE_ORDER:
                            progress.record_initial_authority_milestone(milestone_id)
                    if phase_id == "priority-ranks":
                        progress.record_scan(
                            {
                                "scanId": "rank-cardinality-heritage",
                                "status": "stable-end",
                                "screens": 4,
                                "swipes": 3,
                                "configuredMaxScrolls": 22,
                                "stableRepeats": 2,
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
            self.assertEqual("timing-complete", evidence["status"])
            self.assertEqual(list(driver.PHASE_ORDER), [
                phase["phaseId"] for phase in evidence["phases"]
            ])
            self.assertEqual(list(driver.PHASE_BUDGET_MS), [
                phase["phaseId"] for phase in evidence["phases"]
            ])
            self.assertEqual("rank-cardinality-heritage", evidence["scans"][0]["scanId"])
            self.assertEqual(
                list(driver.INITIAL_AUTHORITY_MILESTONE_ORDER),
                [milestone["milestoneId"] for milestone in evidence["milestones"]],
            )
            self.assertTrue(all(
                milestone["phaseId"] == "initial-authority"
                and milestone["segmentElapsedMs"] >= 0
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
                progress.advance("priority-ranks")
            progress.advance(driver.PHASE_ORDER[0])
            with self.assertRaisesRegex(RuntimeError, "progress is incomplete"):
                progress.finish()

    def test_progress_recorder_rejects_a_pass_phase_outside_its_budget(self) -> None:
        with tempfile.TemporaryDirectory() as temporary, mock.patch("builtins.print"):
            progress = driver.ProgressRecorder(Path(temporary))
            progress.advance("device-preflight-install")
            progress.advance("initial-authority")
            progress._active_started -= (
                driver.PHASE_BUDGET_MS["initial-authority"] / 1000
            ) + 1

            with self.assertRaisesRegex(RuntimeError, "explicit phase timing budget"):
                progress.advance("priority-ranks")

            self.assertTrue(any(
                phase["phaseId"] == "initial-authority"
                and phase["status"] == "pass"
                and phase["withinBudget"] is False
                for phase in progress.phases
            ))

    def test_progress_recorder_rejects_out_of_order_or_wrong_phase_milestones(self) -> None:
        with tempfile.TemporaryDirectory() as temporary, mock.patch("builtins.print"):
            progress = driver.ProgressRecorder(Path(temporary))
            progress.advance("device-preflight-install")
            with self.assertRaisesRegex(RuntimeError, "outside the active initial phase"):
                progress.record_initial_authority_milestone(
                    driver.INITIAL_AUTHORITY_MILESTONE_ORDER[0]
                )
            progress.advance("initial-authority")
            with self.assertRaisesRegex(RuntimeError, "Expected initial-authority milestone"):
                progress.record_initial_authority_milestone(
                    driver.INITIAL_AUTHORITY_MILESTONE_ORDER[1]
                )

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
        return [
            driver.shared.UiNode(
                {
                    "resource-id": "creation-prerequisite-talent-grant-authority",
                    "content-desc": f"Required. {selected_count} / 1 {kind}",
                    "bounds": "[0,0][100,100]",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": "creation-prerequisite-talent-grant-digest",
                    "text": "sha256:" + ("a" * 64),
                    "bounds": "[0,100][100,200]",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": prefix + option_id,
                    "content-desc": ("✓ " if selected else "") + "Arcana",
                    "enabled": "true",
                    "clickable": "true",
                    "bounds": "[0,200][100,300]",
                }
            ),
            driver.shared.UiNode(
                {
                    "resource-id": "creation-prerequisite-talent-grant-complete",
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
                return nodes[0]

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

    def test_talent_grant_surface_rejects_malformed_or_opposite_kind_ids(self) -> None:
        malformed = self.talent_grant_nodes(option_id="forged-")
        opposite = self.talent_grant_nodes()
        opposite.append(
            driver.shared.UiNode(
                {
                    "resource-id": (
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
            "resourceViewports": {
                "creation-prerequisite-talent-grant-authority": 0,
                "creation-prerequisite-talent-grant-digest": 0,
                option_id: 0,
                "creation-prerequisite-talent-grant-complete": 0,
            }
        }

        def nodes(
            *,
            selected: int = 1,
            required: int = 1,
            kind: str = "Active skills",
            observed_digest: str = digest,
            duplicate: bool = False,
            enabled: bool = True,
        ) -> list[driver.shared.UiNode]:
            option = driver.shared.UiNode(
                {
                    "resource-id": option_id,
                    "content-desc": "✓ Arcana" if selected else "Arcana",
                    "enabled": str(enabled).lower(),
                    "clickable": "true",
                    "bounds": "[10,100][900,300]",
                }
            )
            result = [
                driver.shared.UiNode(
                    {
                        "resource-id": "creation-prerequisite-talent-grant-authority",
                        "content-desc": f"Required. {selected} / {required} {kind}",
                    }
                ),
                driver.shared.UiNode(
                    {
                        "resource-id": "creation-prerequisite-talent-grant-digest",
                        "text": observed_digest,
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
            (nodes(observed_digest="sha256:" + ("b" * 64)), "immutable authority"),
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
        }
        talent_option_id = "creation-prerequisite-talent-option-adept"
        talent_navigation = {
            "endViewport": 2,
            "resourceViewports": {talent_option_id: 2},
        }
        chosen = option_ids[:2]
        complete = driver.TalentGrantMutableState(2, chosen, True)
        incomplete = driver.TalentGrantMutableState(1, (chosen[1],), False)
        grouped_states = iter(
            ((complete, 5), (complete, 5), (incomplete, 5), (complete, 5))
        )
        device = mock.Mock()

        def inventory(*_args, navigation_out=None, **_kwargs):
            navigation_out.update(grant_navigation)
            return baseline

        def measured(_device, resource_id, navigation, _current, **_kwargs):
            return int(navigation["resourceViewports"][resource_id])

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
            "read_talent_grant_grouped_state",
            side_effect=lambda *_args, **_kwargs: next(grouped_states),
        ) as grouped_scan:
            proof = driver.choose_and_prove_talent_grant(
                device,
                "Active skills",
                talent_option_id,
                talent_navigation,
                scan_id_prefix="active",
            )

        self.assertEqual(1, inventory_scan.call_count)
        self.assertEqual(4, grouped_scan.call_count)
        self.assertEqual(5, measured_tap.call_count)
        self.assertEqual(list(option_ids), proof.receipt["allOptionAutomationIds"])
        self.assertEqual(list(chosen), proof.receipt["selectedOptionAutomationIds"])
        self.assertEqual(5, proof.current_viewport)

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

    def test_prerequisite_navigation_uses_exact_bounded_bidirectional_search(self) -> None:
        device = mock.Mock()

        with mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            driver.open_prerequisite(device)

        device.tap_bidirectional.assert_called_once_with(
            "creation-stage-method",
            timeout=180,
            backward_scrolls=0,
            forward_scrolls=8,
            scroll_distance_ratio=0.68,
            exact_resource_id=True,
        )
        device.wait_exact_resource_id_bidirectional.assert_called_once_with(
            "creation-prerequisite-method",
            timeout=90,
            backward_scrolls=0,
            forward_scrolls=4,
            scroll_distance_ratio=0.18,
            evidence_prefix="creation-prerequisite-method",
            surface_name="Creation prerequisite build-method authority",
            require_tappable=False,
        )
        self.assertEqual(
            [mock.call(device, swipes=8), mock.call(device, swipes=4)],
            reset.call_args_list,
        )
        device.tap_until_visible.assert_not_called()

    def test_prerequisite_navigation_proves_route_before_reading_content(self) -> None:
        device = mock.Mock()
        events: list[tuple[str, tuple[object, ...], dict[str, object]]] = []
        device.wait.side_effect = lambda *args, **kwargs: events.append(
            ("wait", args, kwargs)
        )
        device.wait_exact_resource_id_bidirectional.side_effect = (
            lambda *args, **kwargs: events.append(("wait_bidirectional", args, kwargs))
        )

        with mock.patch.object(driver.shared, "reset_scroll_to_top") as reset:
            reset.side_effect = lambda *args, **kwargs: events.append(
                ("reset", args, kwargs)
            )
            driver.open_prerequisite(device)

        self.assertEqual(
            [
                ("wait", ("creation-prerequisite-page",), {"timeout": 60}),
                ("reset", (device,), {"swipes": 8}),
                (
                    "wait",
                    ("creation-prerequisite-karma-budget",),
                    {"timeout": 60, "scroll": True, "max_scrolls": 22},
                ),
                (
                    "wait_bidirectional",
                    ("creation-prerequisite-method",),
                    {
                        "timeout": 90,
                        "backward_scrolls": 0,
                        "forward_scrolls": 4,
                        "scroll_distance_ratio": 0.18,
                        "evidence_prefix": "creation-prerequisite-method",
                        "surface_name": "Creation prerequisite build-method authority",
                        "require_tappable": False,
                    },
                ),
                ("reset", (device,), {"swipes": 4}),
            ],
            events,
        )
        self.assertEqual(
            [mock.call(device, swipes=8), mock.call(device, swipes=4)],
            reset.call_args_list,
        )

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

    def test_route_and_persisted_authority_reads_reset_inherited_viewports(self) -> None:
        main_source = inspect.getsource(driver.execute)
        persisted_source = inspect.getsource(driver.require_exact_restored_authority_option)

        preview_route = main_source.index(
            'device.wait("creation-prerequisite-preview-page", timeout=60)'
        )
        preview_reset = main_source.index(
            "shared.reset_scroll_to_top(device, swipes=22)",
            preview_route,
        )
        preview_digest = main_source.index(
            '"creation-prerequisite-preview-digest"',
            preview_reset,
        )
        self.assertLess(preview_route, preview_reset)
        self.assertLess(preview_reset, preview_digest)

        receipt_route = main_source.index(
            'device.wait("creation-prerequisite-confirm-receipt"'
        )
        receipt_reset = main_source.index(
            "shared.reset_scroll_to_top(device, swipes=22)",
            receipt_route,
        )
        receipt_read = main_source.index(
            'node_text(device, "creation-prerequisite-confirm-receipt"',
            receipt_reset,
        )
        self.assertLess(receipt_route, receipt_reset)
        self.assertLess(receipt_reset, receipt_read)

        persisted_reset = persisted_source.index(
            "shared.reset_scroll_to_top(device, swipes=max_scrolls)"
        )
        persisted_selection = persisted_source.index(
            'f"creation-prerequisite-{category}-selection-id"'
        )
        self.assertLess(persisted_reset, persisted_selection)

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

        with mock.patch.object(
                 driver,
                 "tap_prescribed_exact_enabled_priority_rank",
                 side_effect=select_rank,
             ), \
             mock.patch.object(driver.time, "sleep"):
            selected = driver.select_priority_rank(device, "heritage")

        self.assertEqual("creation-prerequisite-rank-heritage-a", selected)
        self.assertEqual(0, sum(call[0] == "wait_bidirectional" for call in calls))
        self.assertEqual(1, sum(call[0] == "swipe_down" for call in calls))
        self.assertIn(("shell", ("input", "tap", "455", "200")), calls)
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
        self.assertEqual(1, sum(call[0] == "select" for call in calls))

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

        for nodes, expected in (
            ([duplicate, duplicate], "duplicateIds"),
            ([malformed], "invalidIds"),
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
            with self.subTest(selected_id=selected_id), \
                 mock.patch.object(
                     driver,
                     "rewind_to_exact_resource_id",
                     return_value=(initial_row, 0),
                 ), \
                 mock.patch.object(
                     driver,
                     "tap_prescribed_exact_enabled_priority_rank",
                     return_value=selected_id,
                 ), \
                 self.assertRaisesRegex(RuntimeError, expected_error):
                driver.select_priority_rank(device, "heritage")

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
        self.assertIn("distance_ratio=0.68", dashboard_source)
        self.assertIn("max_scrolls=18", dashboard_source)
        self.assertNotIn("reset_scroll_to_top", dashboard_source)

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

        prerequisite_source = inspect.getsource(driver.scan_prerequisite_authority)
        self.assertIn("scan_forward_with_receipt(", prerequisite_source)
        self.assertIn("distance_ratio=0.68", prerequisite_source)
        self.assertNotIn("reset_scroll_to_top", prerequisite_source)

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
        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([[binding], [method]], 4),
        ):
            proof = driver.assert_uncreated_advanced_editor_gated(device)
        self.assertEqual(
            driver.CreationDashboardScanProof("Revision 7", "Priority", 4, 1),
            proof,
        )

        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([[binding], [method, method]], 4),
        ), self.assertRaisesRegex(RuntimeError, "cardinality 2"):
            driver.assert_uncreated_advanced_editor_gated(device)

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
        device = mock.Mock()
        with mock.patch.object(
            driver,
            "scan_forward_with_receipt",
            return_value=driver.StableViewportScan([nodes], 6),
        ):
            proof = driver.scan_prerequisite_authority(device)
        self.assertEqual(values, proof.values)
        self.assertEqual(6, proof.swipes)

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
        ), self.assertRaisesRegex(RuntimeError, "changed while scrolling"):
            driver.scan_prerequisite_authority(device)

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

    def test_execute_emits_all_phase_and_scan_timing_into_digest_bound_receipt(self) -> None:
        source = inspect.getsource(driver.execute)
        offsets = [source.index(f'progress.advance("{phase_id}")') for phase_id in driver.PHASE_ORDER]
        self.assertEqual(sorted(offsets), offsets)
        for marker in (
            "scan_observer=progress.record_scan",
            'timing = progress.finish()',
            '"timing": timing',
            '"path": str(progress.evidence_path)',
            '"sha256": sha256(progress.evidence_path)',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertLess(source.index("timing = progress.finish()"), source.index("receipt = {"))

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
            "Coordinator.LoadCreationPrerequisite",
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
        self.assertNotIn('device.wait("dialog-action-complete-new-character-workflow"', source)
        self.assertNotIn('device.wait("Select Metatype Priority"', source)
        execute_source = inspect.getsource(driver.execute)
        initial_source = execute_source[: execute_source.index('progress.advance("priority-ranks")')]
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
        typed = typed[: typed.index("# A plain Back from a category route")]
        self.assertLess(
            typed.index("shared.reset_scroll_to_top(device, swipes=22)"),
            typed.index('"creation-prerequisite-heritage-selection"'),
        )

        preview = source[source.index('device.wait("creation-prerequisite-preview-page"') :]
        preview = preview[: preview.index('device.tap("creation-prerequisite-confirm"')]
        self.assertLess(
            preview.index('f"creation-prerequisite-preview-assignment-{category}"'),
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

        receipt = source[source.index("confirmed_revisions = {") :]
        receipt = receipt[: receipt.index('device.capture("creation-prerequisite-confirmed")')]
        self.assertLess(
            receipt.index('"creation-prerequisite-receipt-draft-revision"'),
            receipt.index('"creation-prerequisite-receipt-draft-digest"'),
        )
        self.assertLess(
            receipt.index('"creation-prerequisite-receipt-draft-digest"'),
            receipt.index('"creation-prerequisite-receipt-raw-character-xml-digest"'),
        )

        persisted = source[source.index("def read_persisted_prerequisite_authority(") :]
        persisted = persisted[: persisted.index("def assert_persisted_prerequisite_authority(")]
        self.assertLess(
            persisted.index("shared.reset_scroll_to_top(device, swipes=22)"),
            persisted.index('"creation-prerequisite-binding"'),
        )
        self.assertLess(
            persisted.index('"creation-prerequisite-authority-digest"'),
            persisted.index('"creation-prerequisite-pending-draft"'),
        )
        self.assertLess(
            persisted.index('"creation-prerequisite-pending-draft"'),
            persisted.index('"creation-prerequisite-pending-draft-digest"'),
        )

        resumed = source[source.index("resumed_authority =") :]
        resumed = resumed[: resumed.index("restart =")]
        self.assertLess(
            resumed.index("require_exact_restored_authority_option("),
            resumed.index('"creation-prerequisite-category-attributes"'),
        )

        restored = source[source.index("def require_exact_restored_authority_option(") :]
        restored = restored[: restored.index("def execute(")]
        self.assertIn("device.wait_exact_resource_id_bidirectional(", restored)
        self.assertNotIn("device.tap(", restored)

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
