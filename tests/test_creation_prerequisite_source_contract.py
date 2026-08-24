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
        render = source[
            source.index("private void Render(") : source.index(
                "private static string Token("
            )
        ]
        commit = source[
            source.index("private async Task CommitPendingTextFieldsAsync(") : source.index(
                "private static bool RequiresStructuralRerender("
            )
        ]
        execute = source[
            source.index("private async Task ExecuteAsync(") : source.index(
                "private async Task CloseAsync("
            )
        ]
        unfocused_update = source[
            source.index("private async Task UpdateFieldAsync(") : source.index(
                "private async Task CommitPendingTextFieldsAsync("
            )
        ]

        self.assertIn("_pendingTextFields.Clear();", render)
        self.assertEqual(2, render.count("PendingTextField binding = new("))
        self.assertEqual(2, render.count("_pendingTextFields.Add(binding);"))
        self.assertEqual(2, render.count("if (!_executing)"))
        self.assertIn("() => editor.Text", render)
        self.assertIn("() => entry.Text", render)
        self.assertEqual(2, render.count("await UpdateFieldAsync(binding,"))
        for marker in (
            "await _fieldUpdateGate.WaitAsync();",
            "PendingTextField[] pending = _pendingTextFields.ToArray();",
            "foreach (PendingTextField binding in pending)",
            "TryResolveActiveTextField(binding, out DesktopDialogField field)",
            "DesktopDialogState? active = _coordinator.State.ActiveDialog",
            "string.Equals(active.Id, binding.DialogId, StringComparison.Ordinal)",
            ".Take(2)",
            "matches.Length != 1",
            "matches[0].IsReadOnly",
            "string.Equals(matches[0].InputType, binding.InputType, StringComparison.Ordinal)",
            "string.Equals(field.Value, value, StringComparison.Ordinal)",
            "await _coordinator.UpdateDialogFieldAsync(binding.FieldId, value);",
            "_fieldUpdateGate.Release();",
        ):
            self.assertIn(marker, commit)
        for forbidden in ("Task.Delay", "SaveAsync(", "ExecuteDialogActionAsync"):
            self.assertNotIn(forbidden, commit)
        for reordering in ("OrderBy", "Reverse", "Distinct"):
            self.assertNotIn(reordering, commit)

        self.assertIn("TryResolveActiveTextField(binding", unfocused_update)
        self.assertIn("return;", unfocused_update)
        self.assertLess(
            unfocused_update.index("TryResolveActiveTextField(binding"),
            unfocused_update.index(
                "await _coordinator.UpdateDialogFieldAsync(binding.FieldId, value);"
            ),
        )

        self.assertIn("if (_executing)", execute)
        self.assertIn("_executing = true;", execute)
        self.assertIn("await CommitPendingTextFieldsAsync();", execute)
        self.assertEqual(1, execute.count("await _coordinator.ExecuteDialogActionAsync(actionId);"))
        self.assertLess(
            execute.index("await CommitPendingTextFieldsAsync();"),
            execute.index("await _coordinator.ExecuteDialogActionAsync(actionId);"),
        )
        self.assertNotIn("Task.Delay", execute)

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
            "ICharacterCreationPrerequisiteService creationPrerequisiteService",
            "_creationPrerequisiteService.Load(",
            "new CharacterCreationPrerequisiteLoadRequest(workspaceId)",
            "_creationPrerequisiteService.Preview(",
            "new CharacterCreationPrerequisitePreviewRequest(",
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
            "AssignmentMatchesOption(assignment, option)",
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

    def test_phone_pages_show_budget_source_blockers_and_keep_attributes_closed(self) -> None:
        page = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        options = (NATIVE / "CreationPriorityCategoryPage.cs").read_text(encoding="utf-8")
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
            'automationId: "creation-prerequisite-attributes-disabled"',
            "halveattributepoints adjustment",
            "Coordinator.PreviewCreationPrerequisite(state.Binding, assignments)",
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
            'AutomationId = "creation-prerequisite-preview-page"',
            "_preview.PreviewDigest",
            "_preview.Assignments",
            "assignment.SourceId",
            "assignment.SourceNodeDigest",
            "assignment.SourceAnchorIds",
            "_preview.CreationKarmaBudget",
            "_preview.SumToTenUsed",
            "_preview.SumToTenTarget",
            "_preview.BaseNormalAttributePoints",
            "_preview.RequiresMetatypeAttributeAdjustment",
            "Coordinator.ConfirmCreationPrerequisiteAsync(",
            'AutomationId = "creation-prerequisite-confirm"',
            'AutomationId = "creation-prerequisite-confirm-receipt"',
            "receipt.DraftDigest",
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

        combined = page + options + preview
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
        self.assertIn("Back navigation did not restore", source)
        self.assertIn('"creation-prerequisite-attributes-disabled"', source)
        self.assertIn('"creation-prerequisite-prepare-preview"', source)
        self.assertIn('"creation-prerequisite-confirm"', source)
        self.assertIn('"creation-prerequisite-confirm-receipt"', source)
        self.assertIn('"creation-prerequisite-pending-draft"', source)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', source)
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
