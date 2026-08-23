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
            def tap_until_visible(self, *args, **kwargs) -> None:
                calls.append(("tap_until_visible", args, kwargs))

            def tap(self, *args, **kwargs) -> None:
                calls.append(("tap", args, kwargs))

            def wait(self, *args, **kwargs):
                calls.append(("wait", args, kwargs))
                return driver.shared.UiNode({})

            def capture(self, *args, **kwargs) -> None:
                calls.append(("capture", args, kwargs))

        selected_options: list[tuple[str, str]] = []

        def select_option(_device, selector: str, value: str) -> None:
            self.assertIs(device, _device)
            selected_options.append((selector, value))

        device = FakeDevice()
        with mock.patch.object(driver.priority, "select_option", side_effect=select_option):
            selected = driver.provision_creation_karma_through_priority_creation(device)

        self.assertEqual(
            [driver.PRIORITY_BUILD_METHOD_SELECTION, *driver.PRIORITY_CREATION_SELECTIONS],
            selected_options,
        )
        self.assertEqual(dict(selected_options), selected)
        route_index = calls.index(("wait", ("creation-wizard-dashboard",), {"timeout": 120}))
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

        with mock.patch.object(driver.shared, "reset_scroll_to_top", side_effect=reset_scroll), \
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
        self.assertIn('device.wait("creation-wizard-dashboard", timeout=30)', source)
        self.assertIn('"clickable": node.attributes.get("clickable") == "true"', source)
        self.assertIn('"tapRemainedOnDashboard": True', source)
        self.assertIn('"freshNavigation": fresh_navigation', source)
        self.assertIn("provision_creation_karma_through_priority_creation", source)
        self.assertIn("priority.select_option(device, selector, option)", source)
        self.assertIn('device.wait("Select Metatype Priority"', source)
        self.assertIn('device.wait("creation-wizard-dashboard", timeout=120)', source)
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

    def test_api36_phone_only_ci_runs_the_prerequisite_after_generic_e2e(self) -> None:
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
        self.assertLess(runner.index(generic), runner.index(prerequisite))
        self.assertNotIn(
            'if [[ "$profile"',
            runner[runner.index(generic) : runner.index(prerequisite)],
        )
        self.assertIn('prerequisite_root="$evidence_root/creation-prerequisite"', runner)
        self.assertIn('--evidence "$prerequisite_root/screenshots"', runner)
        self.assertIn('--receipt "$prerequisite_root/receipt.json"', runner)
        self.assertNotIn('--creation-karma-runner', runner)
        self.assertNotIn('creation-group-membership-e2e.chum5', runner)


if __name__ == "__main__":
    unittest.main()
