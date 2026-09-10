"""Source recognition only: these tests grant no package, CI or device authority.

Before UI adoption, an explicit CHUMMER_OWNER_EDITOR_FORWARD_ROOT may point to
the forthcoming reviewed source. Never substitute that path for the generator's
declared presentation_root. The historical negative uses exact hash-bound c86
interface bytes offline, without assuming Git history in a shallow checkout.
"""
import ast
import hashlib
import importlib.util
import inspect
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


REPO = Path(__file__).resolve().parents[1]
ANDROID = Path(os.environ.get("CHUMMER_OWNER_EDITOR_ANDROID_ROOT", REPO))
WORKSPACE = Path(os.environ.get("CHUMMER_COMPLETE_ROOT", ANDROID.parent))
FORWARD = Path(os.environ.get("CHUMMER_OWNER_EDITOR_FORWARD_ROOT", WORKSPACE / "chummer-presentation"))
SCRIPT = REPO / "scripts/materialize_chummer5_editability_inventory.py"
SPEC = importlib.util.spec_from_file_location("owner_editor_inventory", SCRIPT)
inventory = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(inventory)
UI_FILES = (
    "Chummer.Presentation/IOwnerBoundWorkspaceMutationClient.cs",
    "Chummer.Presentation/IOwnerBoundWorkspacePersistenceClient.cs",
    "Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
    "Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
)


class OwnerBoundEditorInventoryTests(unittest.TestCase):
    @staticmethod
    def declaring_type(path):
        if path.name.startswith("CharacterOverviewPresenter."):
            return "public sealed partial class CharacterOverviewPresenter"
        return {
            "RunnerSessionCoordinator.cs": "public sealed partial class RunnerSessionCoordinator",
            "BuildPage.cs": "public sealed class BuildPage",
            "ConditionMonitorEditPage.cs": "public sealed class ConditionMonitorEditPage",
            "PrimaryArmPage.cs": "public sealed class PrimaryArmPage",
            "TabletBuildPage.cs": "public sealed partial class TabletBuildPage",
            "Sr5PlaytimeDamageWizardPage.cs": "public sealed class Sr5PlaytimeDamageReviewPage",
        }[path.name]

    def setUp(self):
        self.native = ANDROID / "src/Chummer.Android/Native"
        self.coordinator = self.native / "RunnerSessionCoordinator.cs"
        self.presenter = FORWARD / UI_FILES[2]
        self.original_read = inventory._read_text
        # A hostile case must start from a complete source-positive chain.
        # This is explicitly not an installed-package or successful-run claim.
        self.assertTrue(inventory._native_primary_arm_owner_guarded(self.native, FORWARD))
        self.assertTrue(inventory._native_condition_owner_guarded(self.native, FORWARD))
        self.assertTrue(inventory._native_condition_owner_guarded(self.native, FORWARD, tablet=True))

    def reject_change(self, path, method, declaration, old, new, check):
        source = self.original_read(path)
        body = inventory._csharp_method_source(path, method, declaration, declaring_type=self.declaring_type(path))
        self.assertIsNotNone(body)
        self.assertIn(old, body)
        changed = source.replace(body, body.replace(old, new, 1), 1)
        with patch.object(inventory, "_read_text", side_effect=lambda p:
                          changed if p == path else self.original_read(p)):
            self.assertFalse(check())

    def primary(self):
        return inventory._native_primary_arm_owner_guarded(self.native, FORWARD)

    def condition(self):
        return inventory._native_condition_owner_guarded(self.native, FORWARD)

    def test_exact_c86_interface_rejects_forthcoming_mutation_chain(self):
        fixture = json.loads((REPO / "tests/fixtures/ui-c86-owner-mutation-interface.json").read_text())
        self.assertEqual({"repository", "commit", "path", "sha256", "source"}, set(fixture))
        self.assertEqual("https://github.com/ArchonMegalon/chummer6-ui", fixture["repository"])
        self.assertEqual("c86fabcb3168615d95f3732cfc6d544eb7fa1d0d", fixture["commit"])
        self.assertEqual(UI_FILES[0], fixture["path"])
        self.assertEqual("3d4e9c92b607b31423e882ba06cffbd0b30b118153489168a6901d9f433c761f", fixture["sha256"])
        self.assertEqual(fixture["sha256"], hashlib.sha256(fixture["source"].encode("utf-8")).hexdigest())
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in UI_FILES:
                destination = root / relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes((FORWARD / relative).read_bytes())
            (root / UI_FILES[0]).write_text(fixture["source"], encoding="utf-8")
            # The real historical declared interface must fail even with the
            # forthcoming implementation present; this is not a c86 build.
            self.assertIn("ApplyCareerReputationEditAsync(", (root / UI_FILES[0]).read_text())
            for kind in ("PrimaryArm", "ConditionMonitor"):
                self.assertFalse(inventory._owner_bound_editor_presenter_guarded(root, kind))
            self.assertFalse(inventory._native_primary_arm_owner_guarded(self.native, root))
            self.assertFalse(inventory._native_condition_owner_guarded(self.native, root))
            self.assertFalse(inventory._native_condition_owner_guarded(self.native, root, tablet=True))

    def test_missing_or_invalid_declared_root_does_not_use_forward_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertFalse(inventory._native_primary_arm_owner_guarded(self.native, Path(directory) / "missing"))
            self.assertFalse(inventory._native_condition_owner_guarded(self.native, Path(directory)))

    def test_interface_defaults_or_adjacent_concrete_methods_are_insufficient(self):
        source = self.original_read(self.presenter)
        for kind in ("PrimaryArm", "ConditionMonitor"):
            method = f"Apply{kind}EditAsync"
            declaration = f"public Task<CommandResult<WorkspaceRevisionReceipt>> {method}("
            body = inventory._csharp_method_source(self.presenter, method, declaration,
                declaring_type=self.declaring_type(self.presenter))
            self.assertIsNotNone(body)
            variants = {
                "default-only": source.replace(body, "", 1),
                "duplicate": source.replace(body, body + "\n" + body, 1),
                "unbound": source.replace(body, body.replace("expectedOwner", "ignoredOwner"), 1),
                "adjacent-type": source.replace(body, "", 1) + "\npublic sealed class Unrelated\n{\n" + body + "\n}\n",
                "adjacent-method": source.replace(body, body.replace("ApplyOriginalWorkspaceXmlMutationAsync", "UnboundMutation"), 1)
                    + "\npublic sealed class Other\n{\n" + body.replace(method, "UnrelatedMutation") + "\n}\n",
            }
            for name, changed in variants.items():
                with self.subTest(kind=kind, corruption=name), patch.object(inventory, "_read_text",
                        side_effect=lambda p: changed if p == self.presenter else self.original_read(p)):
                    self.assertFalse(inventory._owner_bound_editor_presenter_guarded(FORWARD, kind))
            interface = FORWARD / UI_FILES[0]
            original = self.original_read(interface)
            changed = original.replace(f"> {method}(", f"> Missing{method}(", 1)
            self.assertNotEqual(original, changed)
            with patch.object(inventory, "_read_text", side_effect=lambda p:
                              changed if p == interface else self.original_read(p)):
                self.assertFalse(inventory._owner_bound_editor_presenter_guarded(FORWARD, kind))

    def test_common_ui_observer_and_original_owner_cannot_be_removed(self):
        interface = FORWARD / UI_FILES[1]
        original = self.original_read(interface)
        head, body = original.split("public interface IOwnerBoundWorkspacePersistencePresenter", 1)
        changed = head + "public interface IOwnerBoundWorkspacePersistencePresenter" + body.replace(" SaveAsync(", " MissingSaveAsync(", 1)
        # The identical Save signature still present on the client is not the
        # required presenter capability and cannot satisfy its missing member.
        with patch.object(inventory, "_read_text", side_effect=lambda p:
                          changed if p == interface else self.original_read(p)):
            self.assertTrue(self.condition())
            self.assertFalse(self.primary())
            with self.assertRaises(AssertionError):
                self.assert_damage_chain()
        persistence = FORWARD / UI_FILES[3]
        original = self.original_read(persistence)
        core = inventory._csharp_method_source(persistence, "SaveCoreAsync", "private async Task SaveCoreAsync(",
            declaring_type=self.declaring_type(persistence))
        self.assertIsNotNone(core)
        changed = original.replace(core, "", 1)
        with patch.object(inventory, "_read_text", side_effect=lambda p:
                          changed if p == persistence else self.original_read(p)):
            self.assertTrue(self.condition())
            self.assertFalse(self.primary())
            with self.assertRaises(AssertionError):
                self.assert_damage_chain()
        cases = (
            ("ApplyOriginalWorkspaceXmlMutationAsync", "private async Task ApplyOriginalWorkspaceXmlMutationAsync(",
             "expectedOwner: originalOwner", "expectedOwner: null"),
            ("ApplyOriginalWorkspaceXmlMutationAsync", "private async Task ApplyOriginalWorkspaceXmlMutationAsync(",
             "observeCanonical: observe", "observeCanonical: null"),
            ("ApplyWorkspaceXmlMutationAsync", "private async Task ApplyWorkspaceXmlMutationAsync(",
             "if (expectedOwner.HasValue && boundClient is null)", "if (false)"),
            ("ApplyWorkspaceXmlMutationAsync", "private async Task ApplyWorkspaceXmlMutationAsync(",
             "read.Value.ContentRevision != expectedContentRevision", "false"),
            ("ApplyWorkspaceXmlMutationAsync", "private async Task ApplyWorkspaceXmlMutationAsync(",
             "observeCanonical?.Invoke(replacementResult);", ""),
        )
        for method, declaration, old, new in cases:
            with self.subTest(guard=old):
                self.reject_change(self.presenter, method, declaration, old, new, self.condition)

    def test_primary_arm_original_capture_gate_receipts_and_save_are_required(self):
        cases = (
            ("BuildPage.cs", "AddDossier", "private void AddDossier(",
             "Coordinator.PreparePrimaryArmEditAsync(original)", "Coordinator.PreparePrimaryArmEditAsync()"),
            ("PrimaryArmPage.cs", "SaveAsync", "private async Task SaveAsync(",
             "_editor, value, () => IsCurrentAppearanceGeneration(appearance)", "_editor, value, () => true"),
            ("RunnerSessionCoordinator.cs", "PreparePrimaryArmEditAsync", "internal async Task<PrimaryArmEditorState?> PreparePrimaryArmEditAsync(",
             "_primaryArmEditors.GetValue(editor, _ => original);", "_primaryArmEditors.GetValue(editor, _ => State);"),
            ("RunnerSessionCoordinator.cs", "TryApplyBoundPrimaryArmEditAsync", "internal Task<bool> TryApplyBoundPrimaryArmEditAsync(",
             "return WithWorkspaceActivationGateAsync(", "return RunWithoutGate("),
            ("RunnerSessionCoordinator.cs", "ApplyPrimaryArmEditCoreAsync", "private async Task<bool> ApplyPrimaryArmEditCoreAsync(",
             "original.ContentRevision != request.ExpectedContentRevision", "false"),
            ("RunnerSessionCoordinator.cs", "ApplyPrimaryArmEditCoreAsync", "private async Task<bool> ApplyPrimaryArmEditCoreAsync(",
             "committed.Id != request.WorkspaceId", "false"),
            ("RunnerSessionCoordinator.cs", "ApplyPrimaryArmEditCoreAsync", "private async Task<bool> ApplyPrimaryArmEditCoreAsync(",
             "committed.SavedRevision != original.SavedRevision", "false"),
            ("RunnerSessionCoordinator.cs", "ApplyPrimaryArmEditCoreAsync", "private async Task<bool> ApplyPrimaryArmEditCoreAsync(",
             "owner, committed.Id, committed.ContentRevision, cancellationToken", "State.DisplayOwnerContext.Value, committed.Id, committed.ContentRevision, cancellationToken"),
            ("RunnerSessionCoordinator.cs", "ApplyPrimaryArmEditCoreAsync", "private async Task<bool> ApplyPrimaryArmEditCoreAsync(",
             "receipt.SavedRevision != committed.ContentRevision", "false"),
        )
        for relative, method, declaration, old, new in cases:
            with self.subTest(guard=old):
                self.reject_change(self.native / relative, method, declaration, old, new, self.primary)
        self.reject_change(self.presenter, "PreparePrimaryArmEditAsync",
            "public async Task<PrimaryArmEditorState?> PreparePrimaryArmEditAsync(",
            "read.Value.SavedRevision != expectedSavedRevision", "false", self.primary)

    def test_condition_original_render_gate_receipts_and_dirty_semantics_are_required(self):
        for old, new in (
            ("CharacterOverviewState original = Coordinator.State;", "CharacterOverviewState original = LaterState;"),
            ("original, () => generation == _renderGeneration", "Coordinator.State, () => generation == _renderGeneration"),
            ("Coordinator.TryApplyBoundConditionMonitorEditAsync(", "Coordinator.ApplyConditionMonitorEditAsync("),
        ):
            with self.subTest(guard=old):
                self.reject_change(self.native / "ConditionMonitorEditPage.cs", "Refresh",
                    "protected override void Refresh(", old, new, self.condition)
        for old in ("!IsNativeEditDisplayCurrent(expected)", "current.SavedRevision != expected.SavedRevision",
                    "!ReferenceEquals(current.ActiveConditionMonitor, expected.ActiveConditionMonitor)",
                    "monitor.Tracks.Count(track => track.Track == request.Track) != 1", "!isCurrentInspector()"):
            with self.subTest(guard=old):
                self.reject_change(self.coordinator, "TryApplyBoundConditionMonitorEditAsync",
                    "internal Task<bool> TryApplyBoundConditionMonitorEditAsync(", old, "false", self.condition)
        for old, new in (
            ("request, owner, workspaceId, original.ContentRevision", "request, owner, workspaceId, State.ContentRevision"),
            ("receipt.Id != workspaceId", "false"),
            ("receipt.ContentRevision != original.ContentRevision + 1", "false"),
            ("receipt.SavedRevision != original.SavedRevision", "false"),
            ("await SyncShellAsync(cancellationToken);", "await _presenter.SaveAsync(cancellationToken);\n        await SyncShellAsync(cancellationToken);"),
        ):
            with self.subTest(guard=old):
                self.reject_change(self.coordinator, "ApplyConditionMonitorEditCoreAsync",
                    "private async Task<bool> ApplyConditionMonitorEditCoreAsync(", old, new, self.condition)

    def test_live_owner_frame_guard_cannot_borrow_adjacent_member(self):
        for old in ("State.DisplayOwnerContext == original.DisplayOwnerContext",
                    "State.Session.OwnerContext == original.DisplayOwnerContext",
                    "_shellPresenter.State.OwnerContext == original.DisplayOwnerContext",
                    "IsNativePersistenceOwnerCurrent(original.DisplayOwnerContext)"):
            with self.subTest(guard=old):
                self.reject_change(self.coordinator, "IsNativeEditDisplayCurrent",
                    "private bool IsNativeEditDisplayCurrent(", old, "true", self.condition)
        source = self.original_read(self.coordinator)
        body = inventory._csharp_method_source(self.coordinator, "ApplyConditionMonitorEditCoreAsync",
            "private async Task<bool> ApplyConditionMonitorEditCoreAsync(", declaring_type=self.declaring_type(self.coordinator))
        missing = body.replace("receipt.SavedRevision != original.SavedRevision", "false")
        changed = source.replace(body, missing + "\n" + body.replace("ApplyConditionMonitorEditCoreAsync", "UnrelatedCoreAsync"), 1)
        with patch.object(inventory, "_read_text", side_effect=lambda p:
                          changed if p == self.coordinator else self.original_read(p)):
            self.assertFalse(self.condition())

    def test_type_scoped_selector_rejects_duplicate_and_neighbouring_refresh(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Page.cs"
            wanted = "public sealed class Wanted\n{\n    protected override void Refresh() { Missing(); }\n}\n"
            other = "public sealed class Other\n{\n    protected override void Refresh() { Bound(); }\n}\n"
            path.write_text(wanted + other)
            self.assertFalse(inventory._csharp_method_contains(path, "Refresh", "protected override void Refresh(",
                "Bound();", declaring_type="public sealed class Wanted"))
            path.write_text(wanted + wanted + other)
            self.assertIsNone(inventory._csharp_method_source(path, "Refresh", "protected override void Refresh(",
                declaring_type="public sealed class Wanted"))

    def assert_damage_chain(self):
        self.assertTrue(inventory._owner_bound_editor_presenter_guarded(FORWARD, "ConditionMonitor"))
        self.assertTrue(inventory._owner_bound_editor_save_guarded(FORWARD))
        page = self.native / "Sr5PlaytimeDamageWizardPage.cs"
        declaring = "public sealed class Sr5PlaytimeDamageReviewPage"
        self.assertTrue(inventory._csharp_method_contains(page, "Sr5PlaytimeDamageReviewPage",
            "public Sr5PlaytimeDamageReviewPage(", "_reviewFrame = coordinator.State;",
            "_originalOwner = _reviewFrame.DisplayOwnerContext;", declaring_type=declaring))
        self.assertTrue(inventory._csharp_method_contains(page, "Refresh", "protected override void Refresh(",
            "long renderGeneration = ++_renderGeneration;", "long appearanceGeneration = CaptureAppearanceGeneration();",
            "ConfirmAsync(renderGeneration, appearanceGeneration)", declaring_type=declaring))
        confirm = inventory._csharp_method_source(page, "ConfirmAsync", "private async Task ConfirmAsync(", declaring_type=declaring)
        self.assertIsNotNone(confirm)
        self.assertNotIn("TryReturnToReview(", confirm)
        self.assertNotIn("Coordinator.SaveAsync(", confirm)
        self.assertEqual(1, confirm.count("Coordinator.TryApplyAndSaveBoundConditionMonitorEditAsync("))
        self.assertTrue(inventory._csharp_method_contains(page, "ConfirmAsync", "private async Task ConfirmAsync(",
            "IsCurrentAppearanceGeneration(appearanceGeneration)", "_store.TryBeginApplying(", "_journal = applying;",
            "Coordinator.TryApplyAndSaveBoundConditionMonitorEditAsync(", "_reviewFrame,", "IsCurrentReview,",
            "if (saved is null || observed is null", "saved.Id != applying.Quote.Original.WorkspaceId",
            "saved.ContentRevision != observed.WorkspaceRevision", "saved.SavedRevision != observed.WorkspaceRevision",
            "_store.TryComplete(", 'Text("Damage saved")',
            "if (IsCurrentReview() && _journal == applied && ProjectCurrent() == observed)", "await Navigation.PopAsync();",
            declaring_type=declaring))
        helper = inventory._csharp_method_source(self.coordinator, "TryApplyAndSaveBoundConditionMonitorEditAsync",
            "internal Task<WorkspaceSaveReceipt?> TryApplyAndSaveBoundConditionMonitorEditAsync(",
            declaring_type=self.declaring_type(self.coordinator))
        self.assertIsNotNone(helper)
        self.assertEqual(1, helper.count("await bound.ApplyConditionMonitorEditAsync("))
        self.assertEqual(1, helper.count("await persistence.SaveAsync("))
        self.assertNotIn("_presenter.SaveAsync(", helper)
        self.assertTrue(inventory._csharp_method_contains(self.coordinator, "TryApplyAndSaveBoundConditionMonitorEditAsync",
            "internal Task<WorkspaceSaveReceipt?> TryApplyAndSaveBoundConditionMonitorEditAsync(",
            "WithWorkspaceActivationGateAsync<WorkspaceSaveReceipt?>", "!IsNativeEditDisplayCurrent(expected)",
            "expected.ContentRevision != expected.SavedRevision", "expected.DisplayOwnerContext is not { IsValid: true } owner",
            "!isCurrentReview()", "await bound.ApplyConditionMonitorEditAsync(",
            "request, owner, workspaceId, expected.ContentRevision", "committed.Id != workspaceId",
            "committed.ContentRevision != expected.ContentRevision + 1", "committed.SavedRevision != expected.SavedRevision",
            "await persistence.SaveAsync(", "owner, workspaceId, committed.ContentRevision",
            "receipt.Id != workspaceId", "receipt.ContentRevision != committed.ContentRevision",
            "receipt.SavedRevision != committed.ContentRevision", "return receipt;",
            declaring_type=self.declaring_type(self.coordinator)))
        self.assertEqual(1, helper.count("!isCurrentReview()"))  # No new page cancellation after dispatch.

    def test_same_signature_sibling_or_nested_members_cannot_supply_actual_authority(self):
        persistence = FORWARD / UI_FILES[3]
        cases = (
            (self.coordinator, "IsNativeEditDisplayCurrent", "private bool IsNativeEditDisplayCurrent(", self.condition),
            (self.coordinator, "ApplyConditionMonitorEditCoreAsync", "private async Task<bool> ApplyConditionMonitorEditCoreAsync(", self.condition),
            (self.coordinator, "TryApplyBoundConditionMonitorEditAsync", "internal Task<bool> TryApplyBoundConditionMonitorEditAsync(", self.condition),
            (self.coordinator, "ApplyPrimaryArmEditCoreAsync", "private async Task<bool> ApplyPrimaryArmEditCoreAsync(", self.primary),
            (self.coordinator, "PreparePrimaryArmEditAsync", "internal async Task<PrimaryArmEditorState?> PreparePrimaryArmEditAsync(", self.primary),
            (self.coordinator, "TryApplyBoundPrimaryArmEditAsync", "internal Task<bool> TryApplyBoundPrimaryArmEditAsync(", self.primary),
            (persistence, "RunOriginalPersistenceGestureAsync<T>", "private async Task<CommandResult<T>> RunOriginalPersistenceGestureAsync<T>(", self.condition),
            (persistence, "IsOriginalPersistenceOwnerCurrent", "private bool IsOriginalPersistenceOwnerCurrent(", self.condition),
            (persistence, "SaveAsync", "public Task<CommandResult<WorkspaceSaveReceipt>> SaveAsync(", self.primary),
            (persistence, "SaveCoreAsync", "private async Task SaveCoreAsync(", self.primary),
            (self.presenter, "ApplyConditionMonitorEditAsync", "public Task<CommandResult<WorkspaceRevisionReceipt>> ApplyConditionMonitorEditAsync(", self.condition),
            (self.native / "ConditionMonitorEditPage.cs", "Refresh", "protected override void Refresh(", self.condition),
            (self.native / "PrimaryArmPage.cs", "SaveAsync", "private async Task SaveAsync(", self.primary),
        )
        for path, method, declaration, check in cases:
            original = self.original_read(path)
            declaring = self.declaring_type(path)
            body = inventory._csharp_method_source(path, method, declaration, declaring_type=declaring)
            self.assertIsNotNone(body)
            missing = original.replace(body, "", 1)
            opening = missing.index("{", missing.index(declaring) + len(declaring)) + 1
            variants = {
                "sibling": missing + "\npublic sealed class Foreign\n{\n" + body + "\n}\n",
                # Deliberately retain the outer member's indentation. Structural
                # direct-member depth, not a formatting convention, must reject it.
                "nested-flat": missing[:opening] + "\n    private sealed class Foreign\n    {\n" + body
                    + "\n    }\n" + missing[opening:],
            }
            for variant, changed in variants.items():
                with self.subTest(method=method, variant=variant), patch.object(inventory, "_read_text",
                        side_effect=lambda p: changed if p == path else self.original_read(p)):
                    self.assertFalse(check())

    def test_direct_member_body_cannot_borrow_nested_guards_or_nested_declaring_type(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Member.cs"
            actual = "public sealed class Wanted\n{\n    public void Apply() { Missing(); }\n"
            nested = "    private sealed class Foreign\n    {\n    public void Apply() { Bound(); }\n    }\n}\n"
            path.write_text(actual + nested)
            self.assertFalse(inventory._csharp_method_contains(path, "Apply", "public void Apply(", "Bound();",
                declaring_type="public sealed class Wanted"))
            # Even the requested type name is insufficient when it is nested.
            path.write_text("public sealed class Outer\n{\n" + actual + "}\n}\n")
            self.assertIsNone(inventory._csharp_method_source(path, "Apply", "public void Apply(",
                declaring_type="public sealed class Wanted"))
            # Comments and literal braces cannot change structural membership.
            path.write_text('public sealed class Wanted\n{\n    public void Apply() { var x = "}"; /* } */ Bound(); }\n}\n')
            self.assertTrue(inventory._csharp_method_contains(path, "Apply", "public void Apply(", "Bound();",
                declaring_type="public sealed class Wanted"))
            path.write_text('public sealed class Wanted\n{\n    public string Apply() => "}";\n    private void Other() { Bound(); }\n}\n')
            self.assertFalse(inventory._csharp_method_contains(path, "Apply", "public string Apply(", "Bound();",
                declaring_type="public sealed class Wanted"))

    def test_damage_confirmation_save_is_distinct_from_generic_edit_and_e2e_claim(self):
        self.assert_damage_chain()
        recognition = inventory._sr5_contextual_mutation_api36_recognition({"sourceAuthorityStatus": "ready", "blockers": []})
        self.assertIn("playtime.damage-conditions", recognition["explicitBlockers"])
        self.assertNotIn("playtime.damage-conditions", recognition["supportedTypedMutations"])
        self.assertEqual("not_executed", recognition["executionStatus"])
        self.assertFalse(recognition["releaseClaim"])
        self.assertEqual(0, recognition["completionCountContribution"])

    def test_damage_confirmation_rejects_owner_receipt_save_and_navigation_drift(self):
        cases = (
            (self.coordinator, "TryApplyAndSaveBoundConditionMonitorEditAsync", "internal Task<WorkspaceSaveReceipt?> TryApplyAndSaveBoundConditionMonitorEditAsync(",
             "owner, workspaceId, committed.ContentRevision", "State.DisplayOwnerContext.Value, workspaceId, committed.ContentRevision"),
            (self.coordinator, "TryApplyAndSaveBoundConditionMonitorEditAsync", "internal Task<WorkspaceSaveReceipt?> TryApplyAndSaveBoundConditionMonitorEditAsync(",
             "receipt.SavedRevision != committed.ContentRevision", "false"),
            (self.native / "Sr5PlaytimeDamageWizardPage.cs", "ConfirmAsync", "private async Task ConfirmAsync(",
             "_reviewFrame,", "Coordinator.State,"),
            (self.native / "Sr5PlaytimeDamageWizardPage.cs", "ConfirmAsync", "private async Task ConfirmAsync(",
             "if (IsCurrentReview() && _journal == applied && ProjectCurrent() == observed)", "if (true)"),
        )
        for path, method, declaration, old, new in cases:
            with self.subTest(guard=old):
                source = self.original_read(path)
                body = inventory._csharp_method_source(path, method, declaration,
                    declaring_type=self.declaring_type(path))
                self.assertIsNotNone(body)
                self.assertIn(old, body)
                changed = source.replace(body, body.replace(old, new, 1), 1)
                with patch.object(inventory, "_read_text", side_effect=lambda p:
                                  changed if p == path else self.original_read(p)):
                    with self.assertRaises(AssertionError):
                        self.assert_damage_chain()

    def test_new_interface_inputs_are_hash_bound_and_existing_rows_keep_evidence_boundary(self):
        # Inspect the real list, not unrelated source markers; do not generate an inventory.
        tree = ast.parse(SCRIPT.read_text())
        build = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "build_inventory")
        assignment = next(node for node in ast.walk(build) if isinstance(node, ast.Assign)
                          and any(isinstance(target, ast.Name) and target.id == "android_inputs" for target in node.targets))
        for filename in ("IOwnerBoundWorkspaceMutationClient.cs", "IOwnerBoundWorkspacePersistenceClient.cs"):
            self.assertEqual(1, sum(isinstance(node, ast.Constant) and node.value == filename for node in ast.walk(assignment.value)))
        payload = json.loads((ANDROID / "docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text())
        rows = [row for row in payload["rows"] if row["phone"]["surface"] in {"PrimaryArmPage", "ConditionMonitorEditPage"}]
        self.assertEqual(55, len(rows))
        arguments = {name: {} if name in {"condition_e2e_receipts", "contact_pet_e2e_receipts"} else None
                     for name in list(inspect.signature(inventory._known_phone_mapping).parameters)[4:]}
        # Test-only forward input: this never writes generated status or substitutes
        # for the declared c86 graph in the checked-in authority.
        with patch.object(inventory, "REPO_ROOT", ANDROID):
            for row in rows:
                with self.subTest(row=row["id"]):
                    mapped = inventory._known_phone_mapping(row, WORKSPACE / "chummer5a", FORWARD,
                        WORKSPACE / "chummer-core-engine", **arguments)
                    self.assertEqual("implemented_pending_emulator", mapped["status"])
                    self.assertEqual("scripted_not_executed", mapped["e2e"]["status"])
                    self.assertFalse(mapped.get("completionProven", False))
                    self.assertIn("chummer-presentation/" + UI_FILES[0], mapped["sourceRefs"])


if __name__ == "__main__":
    unittest.main()
