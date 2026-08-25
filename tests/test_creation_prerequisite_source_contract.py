import ast
import importlib.util
import sys
import unittest
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
        authority = {
            "binding": {"contentRevision": 7, "savedRevision": 3},
            "bindingDigests": {
                "rawCharacterXml": digest,
                "auxiliaryState": digest,
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
                        "auxiliaryState": "sha256:" + "b" * 64,
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

    def test_priority_provisioning_follows_build_route_and_public_save_before_home(self) -> None:
        calls: list[tuple[str, tuple[object, ...], dict[str, object]]] = []

        class FakeDevice:
            viewport_reset = False

            def tap_until_visible(self, *args, **kwargs) -> None:
                calls.append(("tap_until_visible", args, kwargs))

            def tap(self, *args, **kwargs) -> None:
                calls.append(("tap", args, kwargs))

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

        device = FakeDevice()
        with mock.patch.object(driver.priority, "select_option", side_effect=select_option), \
             mock.patch.object(
                 driver.shared,
                 "open_creation_dashboard",
                 side_effect=open_dashboard,
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
        home_index = calls.index(("tap", ("Home",), {}))
        authority_surface_index = calls.index(("wait", ("home-open-file",), {"timeout": 90}))
        self.assertLess(route_index, capture_index)
        self.assertLess(capture_index, save_index)
        self.assertLess(save_index, saved_index)
        self.assertLess(saved_index, home_index)
        self.assertLess(home_index, authority_surface_index)
        self.assertNotIn(("wait", ("Continue building",), {"timeout": 120}), calls)

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
            "option.MetavariantSourceId is null && option.MetavariantName is null",
            "Guid.TryParseExact(",
            "option.MetavariantSourceId",
            "metavariantSourceId != Guid.Empty",
            "!string.IsNullOrWhiteSpace(option.MetavariantName)",
        ):
            self.assertIn(marker, heritage)

    def test_phone_pages_show_projected_typed_choices_and_core_attribute_gate(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        options = (NATIVE / "CreationPriorityCategoryPage.cs").read_text(encoding="utf-8")
        details = (NATIVE / "CreationPriorityDetailPage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationPrerequisitePreviewPage.cs").read_text(encoding="utf-8")
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        for marker in (
            'AutomationId = "creation-prerequisite-page"',
            '"Ask Build Ghost"',
            'automationId: "creation-prerequisite-rook"',
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

    def test_build_ghost_remains_navigation_only_without_prerequisite_mutation(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationPrerequisitePreviewPage.cs").read_text(encoding="utf-8")
        rook = (NATIVE / "RookConversation.cs").read_text(encoding="utf-8")
        combined = page + preview
        self.assertIn("new RookConversationPage(Coordinator)", page)
        self.assertIn("new RookConversationPage(Coordinator)", preview)
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
        self.assertIn('"creation-prerequisite-rook"', source)
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
        self.assertIn('"sameSessionPersistedAuthority": resumed_authority', source)
        self.assertIn('"restartedPersistedAuthority": restarted_authority', source)
        self.assertIn("read_persisted_prerequisite_authority(device)", source)
        self.assertIn('"characterDocumentChangedFalse": "pass"', source)
        self.assertIn('"buildGhostCurrentAndNonMutating": "pass"', source)
        self.assertIn('"advancedEditorNeverExposedWhileCreatedFalse": "pass"', source)

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
