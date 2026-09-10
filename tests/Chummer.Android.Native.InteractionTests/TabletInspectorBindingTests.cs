using System.Reflection;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Maui.Controls;

internal static class TabletInspectorBindingTests
{
    private const string ItemAId = "11111111-1111-4111-8111-111111111111";
    private const string ItemBId = "22222222-2222-4222-8222-222222222222";
    private static readonly OwnerContextStamp LinkedDisplayOwner = new(OwnerScope.LocalSingleUser, "controlled-tablet-host", 0);
    public static async Task RunAsync()
    {
        UnsavedCollectionDraftSurvivesSelectionAndRefresh();
        UnsavedConditionDraftSurvivesTrackSelectionAndRefresh();
        EquivalentProjectionAndRecreatedPagePreserveSelection();
        IncompleteTypedFieldsSurviveSelectionAndRefresh();
        RevisionAndFieldDriftRetainButNeverApplyDraft();
        InPlaceFieldAuthorityDriftCannotApplyRetainedInput();
        WorkspaceSwitchCannotLeakDrafts();
        AccountSwitchCannotLeakDraftsOrReviveOldGrantInput();
        AccountOwnedConditionAndAttributeDraftsRemainSeparate();
        NewAuthorityInstanceRequiresDraftReview();
        RetiredPageCannotOverwriteOrDeleteNewerDraft();
        CompleteNestedIdentityAndCaseRemainBound();
        ObservedSuccessCleanupRequiresExactSuccessorProjection();
        AmbiguousCollectionReadbackCannotClearDraft();
        await ExplicitDiscardIsScopedAndCancelSafeAsync();
        await DelayedDiscardCannotEraseNewerInputAsync();
        await DelayedDiscardCannotEraseEqualNewerDraftAsync();
        DelayedDamageActionCannotMixTracksAndPickers();
        DetachedMoveCannotReorderThePreviousItem();
        DelayedApplyCannotMixTargetsAndControls();
        DetachedSelectionCannotRestoreAnOldEditor();
        RefreshAndDepartureInvalidateApply();
        await QueuedApplyRechecksAuthorityInsideGateAsync();
        await DamageWaitRechecksTrackAndWorkspaceAsync();
        await DeleteConfirmationStaysBoundToTheReviewedItemAsync();
        await DeleteWaitRechecksAuthorityAfterConfirmationAsync();
        NestedChooserKeepsTheCurrentParent();
        await RejectedNestedAddRetainsDraftAsync();
        await LinkedPickerAndUnknownOutcomeRemainSafeAsync();
        await LinkedCharacterBindingTests.RunAsync();
        Console.WriteLine("PASS tablet inspector action binding (managed native page/coordinator, not device persistence)");
    }

    private static void UnsavedCollectionDraftSurvivesSelectionAndRefresh()
    {
        using var fixture = new Fixture();
        fixture.Notes.Text = "unsaved A notes";
        fixture.Click($"tablet-collection-item-{ItemBId}");
        fixture.Notes.Text = "unsaved B notes";
        fixture.Click($"tablet-collection-item-{ItemAId}");
        Require(fixture.Notes.Text == "unsaved A notes", "Switching back to A discarded its unsaved notes.");
        fixture.Refresh();
        Require(fixture.Notes.Text == "unsaved A notes", "Unchanged coordinator refresh discarded A's unsaved notes.");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        Require(fixture.Notes.Text == "unsaved B notes", "Selecting A overwrote B's separate draft.");
        Require(fixture.Requests.Count == 0, "Draft preservation changed the runner without Apply.");
    }

    private static void UnsavedConditionDraftSurvivesTrackSelectionAndRefresh()
    {
        using var fixture = new Fixture(condition: true);
        fixture.Picker("tablet-condition-filled-physical").SelectedIndex = 7;
        fixture.Click("tablet-condition-track-stun");
        fixture.Picker("tablet-condition-filled-stun").SelectedIndex = 6;
        fixture.Click("tablet-condition-track-physical");
        Require(fixture.Picker("tablet-condition-filled-physical").SelectedIndex == 7,
            "Track selection discarded unsubmitted Physical damage.");
        fixture.Refresh();
        Require(fixture.Picker("tablet-condition-filled-physical").SelectedIndex == 7,
            "Unchanged refresh discarded the Physical draft.");
        fixture.Click("tablet-condition-track-stun");
        Require(fixture.Picker("tablet-condition-filled-stun").SelectedIndex == 6,
            "Physical selection overwrote the separate Stun draft.");
        Require(fixture.ConditionRequests.Count == 0, "Draft preservation applied damage without confirmation.");
    }

    private static void EquivalentProjectionAndRecreatedPagePreserveSelection()
    {
        using var fixture = new Fixture();
        fixture.Click($"tablet-collection-item-{ItemBId}");
        fixture.Notes.Text = "B across new page";
        fixture.State = fixture.State with { ActiveCollectionEditor = Editor("saved A", "saved B") };
        fixture.Refresh();
        Require(fixture.Notes.Text == "B across new page", "Equivalent projection objects invalidated the draft.");
        fixture.Recreate();
        Require(fixture.Notes.Text == "B across new page", "Page recreation lost B's selection/input.");
        Require(!fixture.Has("tablet-inspector-draft-conflict"), "Equivalent authority falsely conflicted.");
        fixture.Click("tablet-inspector-save");
        Require(fixture.Requests.Single() is WorkspacePatchCollectionItemRequest
            { Target.ItemId: ItemBId, TextValues: { } values }
            && values[WorkspaceCollectionTextField.Notes] == "B across new page",
            "Restored current input did not use B's typed mutation boundary.");
        fixture.Recreate();
        Require(fixture.Notes.Text == "B across new page", "Canceled mutation erased B's retained input.");
    }

    private static void IncompleteTypedFieldsSurviveSelectionAndRefresh()
    {
        using var fixture = new Fixture(rich: true);
        fixture.Input("tablet-rating").Text = "-";
        fixture.Input("tablet-quantity").Text = "0.";
        fixture.Input("tablet-contact-connection").Text = "";
        fixture.Input("tablet-contact-loyalty").Text = "7x";
        fixture.Toggle("tablet-toggle-equipped").IsToggled = true;
        fixture.Picker("tablet-vehicle-physical-damage").SelectedIndex = 4;
        fixture.Picker("tablet-gear-matrix-damage").SelectedIndex = -1;
        fixture.Click($"tablet-collection-item-{ItemBId}");
        fixture.Click($"tablet-collection-item-{ItemAId}");
        fixture.Refresh();
        fixture.Recreate();
        Require(fixture.Input("tablet-rating").Text == "-" && fixture.Input("tablet-quantity").Text == "0."
            && fixture.Input("tablet-contact-connection").Text == ""
            && fixture.Input("tablet-contact-loyalty").Text == "7x",
            "Unfinished numeric input was parsed, normalized, or lost before Apply.");
        Require(fixture.Toggle("tablet-toggle-equipped").IsToggled
            && fixture.Picker("tablet-vehicle-physical-damage").SelectedIndex == 4
            && fixture.Picker("tablet-gear-matrix-damage").SelectedIndex == -1,
            "Toggle/damage choices were lost or silently defaulted.");
        Require(fixture.Requests.Count == 0, "Restoring raw input changed the runner.");
    }

    private static void RevisionAndFieldDriftRetainButNeverApplyDraft()
    {
        foreach (bool revision in new[] { true, false })
        {
            using var fixture = new Fixture();
            fixture.Notes.Text = "retain the entire old note";
            if (revision) fixture.AdvanceRevision();
            else fixture.State = fixture.State with { ActiveCollectionEditor = fixture.State.ActiveCollectionEditor! with
            {
                Items = fixture.State.ActiveCollectionEditor!.Items.Select(item => item with
                { TextValues = [new(WorkspaceCollectionTextField.Notes, "new", MaximumLength: 3)] }).ToArray()
            }};
            fixture.Refresh();
            Require(fixture.Has("tablet-inspector-draft-conflict"), "Changed authority silently rebased retained input.");
            Require(fixture.Label("tablet-retained-tablet-field-notes").Text.Contains("retain the entire old note",
                StringComparison.Ordinal), "Conflicted input was truncated or lost.");
            fixture.Click("tablet-inspector-save");
            Require(fixture.Requests.Count == 0, "Conflicted draft was applied through a disabled button callback.");
            fixture.Recreate();
            Require(fixture.Has("tablet-inspector-draft-conflict")
                && fixture.Label("tablet-retained-tablet-field-notes").Text.Contains("retain the entire old note",
                    StringComparison.Ordinal), "Conflict was lost when the page was recreated.");
        }
        using var condition = new Fixture(condition: true);
        condition.Picker("tablet-condition-filled-physical").SelectedIndex = 7;
        condition.AdvanceRevision();
        condition.Refresh();
        condition.Click("tablet-condition-save-physical");
        condition.Click("tablet-condition-clear-physical");
        Require(condition.ConditionRequests.Count == 0 && condition.Has("tablet-inspector-draft-conflict"),
            "Revision-conflicted damage draft was silently applied or cleared.");
    }

    private static void WorkspaceSwitchCannotLeakDrafts()
    {
        using var fixture = new Fixture();
        CharacterOverviewState first = fixture.State;
        fixture.Click($"tablet-collection-item-{ItemBId}");
        fixture.Notes.Text = "private workspace B draft";
        fixture.State = Program.NewCreationOverview(new("another-tablet-runner"), 5, 5) with
        {
            ActiveSectionId = "gear", ActiveCollectionEditor = Editor("other A", "other B")
        };
        fixture.Refresh();
        Require(fixture.Notes.Text == "other A" && !fixture.Has("tablet-inspector-draft-conflict"),
            "Same item IDs leaked a draft into another workspace.");
        fixture.State = first;
        fixture.Refresh();
        Require(fixture.Notes.Text == "private workspace B draft",
            "Returning to the original workspace lost its selected draft.");
    }

    private static void AccountSwitchCannotLeakDraftsOrReviveOldGrantInput()
    {
        using var fixture = new Fixture();
        OwnerContextStamp ownerA = new(new OwnerScope("tablet-owner-a"), "tablet-account-authority", 1);
        OwnerContextStamp ownerB = ownerA with { Owner = new OwnerScope("tablet-owner-b"), TransitionRevision = 2 };
        fixture.State = fixture.State with { DisplayOwnerContext = ownerA };
        fixture.Refresh();
        fixture.Click($"tablet-collection-item-{ItemBId}");
        fixture.Notes.Text = "account A private draft";
        CharacterOverviewState original = fixture.State;
        fixture.State = original with { DisplayOwnerContext = ownerB };
        fixture.Refresh();
        Require(fixture.Notes.Text == "saved A" && !fixture.Has("tablet-inspector-draft-conflict"),
            "Identical workspace/item IDs leaked A's selection or draft into account B.");
        fixture.Notes.Text = "account B private draft";
        fixture.Recreate();
        Require(fixture.Notes.Text == "account B private draft", "Account B lost its independently retained input.");

        fixture.State = original with { DisplayOwnerContext = ownerA with { TransitionRevision = 3 } };
        fixture.Refresh();
        Require(fixture.Has("tablet-inspector-draft-conflict")
            && fixture.Label("tablet-retained-tablet-field-notes").Text.Contains("account A private draft", StringComparison.Ordinal),
            "Returning to A either lost its draft or silently rebound it to the new grant epoch.");
        fixture.Click("tablet-inspector-save");
        Require(fixture.Requests.Count == 0, "Old-grant draft input was applied without a new explicit review.");
        fixture.State = original with { DisplayOwnerContext = ownerB };
        fixture.Refresh();
        Require(fixture.Notes.Text == "account B private draft" && !fixture.Has("tablet-inspector-draft-conflict"),
            "A's conflict overwrote B's separately owned draft.");
        Console.WriteLine("PASS tablet retained input: same IDs across accounts and same-account new-grant conflict");
    }

    private static void AccountOwnedConditionAndAttributeDraftsRemainSeparate()
    {
        foreach (bool attribute in new[] { false, true })
        {
            using var fixture = new Fixture(condition: !attribute);
            OwnerContextStamp ownerA = new(new OwnerScope("tablet-owner-a"), "tablet-account-authority", 1);
            OwnerContextStamp ownerB = ownerA with { Owner = new OwnerScope("tablet-owner-b"), TransitionRevision = 2 };
            fixture.State = fixture.State with { DisplayOwnerContext = ownerA };
            if (attribute)
                fixture.State = fixture.State with { ActiveCollectionEditor = null, ActiveSectionId = "attributes",
                    ActiveSectionJson = AttributeJson(2, 0) };
            fixture.Refresh();
            string picker = attribute ? "tablet-attribute-base-body" : "tablet-condition-filled-physical";
            int original = fixture.Picker(picker).SelectedIndex;
            int draftA = attribute ? 3 : 7;
            int draftB = attribute ? 4 : 6;
            fixture.Picker(picker).SelectedIndex = draftA;
            CharacterOverviewState stateA = fixture.State;
            fixture.State = stateA with { DisplayOwnerContext = ownerB };
            fixture.Refresh();
            Require(fixture.Picker(picker).SelectedIndex == original && !fixture.Has("tablet-inspector-draft-conflict"),
                $"{picker}: A's private draft crossed the account boundary.");
            fixture.Picker(picker).SelectedIndex = draftB;
            fixture.State = stateA with { DisplayOwnerContext = ownerA with { TransitionRevision = 3 } };
            fixture.Refresh();
            Require(fixture.Has("tablet-inspector-draft-conflict") && fixture.Has($"tablet-retained-{picker}"),
                $"{picker}: old-grant input was lost or automatically rebound.");
            fixture.State = stateA with { DisplayOwnerContext = ownerB };
            fixture.Refresh();
            Require(fixture.Picker(picker).SelectedIndex == draftB && !fixture.Has("tablet-inspector-draft-conflict"),
                $"{picker}: A's retained conflict corrupted B's draft.");
            Require(fixture.Requests.Count == 0 && fixture.ConditionRequests.Count == 0,
                "Changing accounts applied a retained draft.");
        }
    }

    private static void NewAuthorityInstanceRequiresDraftReview()
    {
        using var fixture = new Fixture();
        OwnerContextStamp owner = new(new OwnerScope("tablet-owner-a"), "first-authority-instance", 1);
        fixture.State = fixture.State with { DisplayOwnerContext = owner };
        fixture.Refresh();
        fixture.Notes.Text = "input from the retired authority";
        fixture.State = fixture.State with
        {
            DisplayOwnerContext = new OwnerContextStamp(owner.Owner, "replacement-authority-instance", 1)
        };
        fixture.Refresh();
        Require(fixture.Has("tablet-inspector-draft-conflict")
            && fixture.Label("tablet-retained-tablet-field-notes").Text.Contains("retired authority", StringComparison.Ordinal),
            "An equal revision in a different authority instance revived stale input.");
        fixture.Click("tablet-inspector-save");
        Require(fixture.Requests.Count == 0, "Replacement authority applied an unreviewed old-authority draft.");
    }

    private static void InPlaceFieldAuthorityDriftCannotApplyRetainedInput()
    {
        using var fixture = new Fixture();
        WorkspaceCollectionTextValueState[] values = fixture.State.ActiveCollectionEditor!.Items[0].TextValues.ToArray();
        fixture.State = fixture.State with { ActiveCollectionEditor = fixture.State.ActiveCollectionEditor with
        {
            Items = fixture.State.ActiveCollectionEditor.Items.Select((item, index) =>
                index == 0 ? item with { TextValues = values } : item).ToArray()
        }};
        fixture.Refresh();
        fixture.Notes.Text = "not allowed after in-place authority drift";
        values[0] = values[0] with { IsEnabled = false };
        fixture.Click("tablet-inspector-save");
        Require(fixture.Requests.Count == 0 && fixture.Notes.Text == "not allowed after in-place authority drift",
            "Reference-equal editor mutation bypassed the captured field authority or erased input.");
    }

    private static async Task ExplicitDiscardIsScopedAndCancelSafeAsync()
    {
        using var fixture = new Fixture();
        fixture.Notes.Text = "keep A until confirmed";
        fixture.Click($"tablet-collection-item-{ItemBId}");
        fixture.Notes.Text = "always keep B";
        fixture.Click($"tablet-collection-item-{ItemAId}");
        await fixture.DiscardAsync();
        Require(fixture.Notes.Text == "keep A until confirmed", "Canceled discard destroyed input.");
        fixture.Confirmation = Task.FromResult(true);
        await fixture.DiscardAsync();
        fixture.Refresh();
        Require(fixture.Notes.Text == "saved A", "Confirmed discard did not restore A's current values.");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        Require(fixture.Notes.Text == "always keep B" && fixture.Requests.Count == 0,
            "Discarding A altered B or the saved runner.");
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Confirmation = pending.Task;
        Task discard = fixture.DiscardAsync();
        fixture.Click($"tablet-collection-item-{ItemAId}");
        pending.SetResult(true);
        await discard;
        fixture.Click($"tablet-collection-item-{ItemBId}");
        Require(fixture.Notes.Text == "always keep B", "Delayed confirmation discarded a different render's draft.");
    }

    private static async Task DelayedDiscardCannotEraseNewerInputAsync()
    {
        using var fixture = new Fixture();
        fixture.Notes.Text = "reviewed for discard";
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Confirmation = pending.Task;
        Task discard = fixture.DiscardAsync();
        fixture.Notes.Text = "typed after discard review";
        pending.SetResult(true);
        await discard;
        fixture.Recreate();
        Require(fixture.Notes.Text == "typed after discard review",
            "Delayed discard erased newer input from the same render.");
    }

    private static async Task DelayedDiscardCannotEraseEqualNewerDraftAsync()
    {
        using var fixture = new Fixture();
        fixture.Notes.Text = "reviewed value";
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Confirmation = pending.Task;
        Task discard = fixture.DiscardAsync();
        fixture.Notes.Text = "different newly typed value";
        fixture.Notes.Text = "reviewed value";
        pending.SetResult(true);
        await discard;
        fixture.Recreate();
        Require(fixture.Notes.Text == "reviewed value",
            "A stale discard confirmation erased a newer equal-valued draft incarnation.");
    }

    private static void RetiredPageCannotOverwriteOrDeleteNewerDraft()
    {
        using var fixture = new Fixture();
        TabletBuildPage retired = fixture.Page;
        InputView retiredNotes = fixture.Notes;
        fixture.Recreate();
        fixture.Notes.Text = "new page owns this draft";
        retiredNotes.Text = "late callback on detached control";
        typeof(TabletBuildPage).GetMethod("OnDisappearing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(retired, null);
        fixture.Recreate();
        Require(fixture.Notes.Text == "new page owns this draft",
            "A departed page's later capture overwrote the current page's input.");

        TabletBuildPage pristine = new(fixture.Coordinator);
        typeof(TabletBuildPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(pristine, null);
        fixture.Refresh(); // reclaims the writer lease
        fixture.Notes.Text = "newer than the other page's binding";
        typeof(TabletBuildPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(pristine, null);
        InputView restored = Elements(pristine).OfType<InputView>()
            .Single(input => input.AutomationId == "tablet-field-notes");
        Require(restored.Text == "newer than the other page's binding",
            "An older page recaptured its pristine/stale controls over a newer same-key draft.");
    }

    private static void CompleteNestedIdentityAndCaseRemainBound()
    {
        using var fixture = new Fixture();
        const string childId = "abcdefab-1234-4234-8234-123456789abc";
        WorkspaceCollectionItemEditorState first = Item(ItemAId, 0, "parent A saved") with
        { Target = new(WorkspaceCollectionKind.Gear, ItemAId, WorkspaceNestedCollectionKind.Gear, childId) };
        fixture.State = fixture.State with { ActiveCollectionEditor = new("gear", WorkspaceCollectionKind.Gear,
            WorkspaceNestedCollectionKind.Gear, [first]) };
        fixture.Refresh();
        fixture.Notes.Text = "parent A retained";
        CharacterOverviewState original = fixture.State;
        fixture.State = fixture.State with { ActiveCollectionEditor = fixture.State.ActiveCollectionEditor with
        { Items = [first with { Target = first.Target with { ItemId = ItemBId }, TextValues =
            [new(WorkspaceCollectionTextField.Notes, "parent B saved")] }] }};
        fixture.Refresh();
        Require(fixture.Notes.Text == "parent B saved", "A child ID alone selected another parent's draft.");
        fixture.Notes.Text = "parent B retained";
        fixture.State = original with { ActiveCollectionEditor = original.ActiveCollectionEditor! with
        { Items = [first with { Target = first.Target with { NestedItemId = childId.ToUpperInvariant() } }] }};
        fixture.Refresh();
        Require(fixture.Notes.Text == "parent A retained" && !fixture.Has("tablet-inspector-draft-conflict"),
            "Case-only typed ID formatting lost or invalidated the same nested target.");
        fixture.State = fixture.State with { ActiveSectionId = "other-gear-section" };
        fixture.Refresh();
        Require(fixture.Notes.Text == "parent A saved", "A draft escaped its section boundary.");
    }

    private static void ObservedSuccessCleanupRequiresExactSuccessorProjection()
    {
        foreach (string family in new[] { "collection", "condition", "attribute" })
        foreach (bool exact in new[] { false, true })
        foreach (string lifetime in new[] { "same-page", "refresh", "departure", "recreated", "newer-input-aba", "other-owner" })
        {
            using var fixture = new Fixture(condition: family == "condition");
            fixture.State = fixture.State with
            { DisplayOwnerContext = new(new OwnerScope("tablet-owner-a"), "tablet-account-authority", 1) };
            fixture.Refresh();
            if (family == "attribute")
            {
                fixture.State = fixture.State with { ActiveCollectionEditor = null, ActiveSectionId = "attributes",
                    ActiveSectionJson = AttributeJson(2, 0) };
                fixture.Refresh();
                fixture.Picker("tablet-attribute-base-body").SelectedIndex = 3; // value 4, minimum 1
                fixture.Picker("tablet-attribute-karma-body").SelectedIndex = 1;
            }
            else if (family == "condition") fixture.Picker("tablet-condition-filled-physical").SelectedIndex = 7;
            else fixture.Notes.Text = "applied note";
            CharacterOverviewState expected = fixture.State;
            object? draft = typeof(TabletBuildPage).GetField("_renderedDraft",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Page);
            TabletBuildPage operationPage = fixture.Page;
            if (lifetime == "newer-input-aba")
            {
                // Even equal final text must not authorize an older completion
                // to clear a newly edited draft incarnation.
                if (family == "attribute")
                {
                    fixture.Picker("tablet-attribute-base-body").SelectedIndex = 2;
                    fixture.Picker("tablet-attribute-base-body").SelectedIndex = 3;
                }
                else if (family == "condition")
                {
                    fixture.Picker("tablet-condition-filled-physical").SelectedIndex = 6;
                    fixture.Picker("tablet-condition-filled-physical").SelectedIndex = 7;
                }
                else
                {
                    fixture.Notes.Text = "new input";
                    fixture.Notes.Text = "applied note";
                }
                fixture.Recreate();
            }
            else if (lifetime == "refresh") fixture.Refresh();
            else if (lifetime == "departure") fixture.Depart();
            else if (lifetime == "recreated") fixture.Recreate();
            fixture.AdvanceRevision();
            string matcher;
            object[] arguments;
            if (family == "attribute")
            {
                fixture.State = fixture.State with { ActiveSectionJson = AttributeJson(4, exact ? 1 : 0) };
                matcher = "AttributeEditObserved";
                arguments = ["body", 4, 1];
            }
            else if (family == "condition")
            {
                fixture.State = fixture.State with { ActiveConditionMonitor = fixture.State.ActiveConditionMonitor! with
                { Tracks = fixture.State.ActiveConditionMonitor!.Tracks.Select(track => track with
                    { Filled = track.Track == WorkspaceConditionMonitorTrack.Physical && exact ? 7 : track.Filled }).ToArray() }};
                matcher = "ConditionEditObserved";
                arguments = [new ConditionMonitorEditRequest(WorkspaceConditionMonitorTrack.Physical, 7)];
            }
            else
            {
                fixture.State = fixture.State with { ActiveCollectionEditor = Editor(exact ? "applied note" : "different", "saved B") };
                matcher = "CollectionPatchObserved";
                arguments = [new WorkspacePatchCollectionItemRequest(new(WorkspaceCollectionKind.Gear, ItemAId),
                    TextValues: new Dictionary<WorkspaceCollectionTextField, string?>
                    { [WorkspaceCollectionTextField.Notes] = "applied note" })];
            }
            Func<CharacterOverviewState, bool> matches = current => (bool)typeof(TabletBuildPage)
                .GetMethod(matcher, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [current, ..arguments])!;
            if (lifetime == "other-owner")
                fixture.State = fixture.State with
                { DisplayOwnerContext = new(new OwnerScope("tablet-owner-b"), "tablet-account-authority", 2) };
            typeof(TabletBuildPage).GetMethod("ForgetObservedAppliedDraft", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(operationPage, [expected, draft, matches]);
            fixture.State = fixture.State with { DisplayOwnerContext = expected.DisplayOwnerContext };
            fixture.Refresh();
            fixture.Recreate();
            Require(fixture.Has("tablet-inspector-draft-conflict") == (!exact || lifetime is "newer-input-aba" or "other-owner"),
                $"{family}/{lifetime}: cleanup lost newer input or retained an exactly applied unchanged draft.");
        }
        // These are synthetic typed readback tests of the real cleanup logic.
        // They do not assert that Core writes, save receipts or process restart occurred.
    }

    private static string AttributeJson(int baseValue, int karma)
        => System.Text.Json.JsonSerializer.Serialize(new { attributes = new[]
        {
            new { name = "body", baseValue, karmaValue = karma, totalValue = baseValue + karma,
                metatypeMin = 1, metatypeMax = 6, metatypeAugMax = 9, priorityMaximum = 6,
                karmaMaximum = 6, baseUnlocked = true, created = false }
        }});

    private static void AmbiguousCollectionReadbackCannotClearDraft()
    {
        using var fixture = new Fixture();
        var target = new WorkspaceCollectionItemTarget(WorkspaceCollectionKind.Gear, ItemAId);
        WorkspaceCollectionItemEditorState item = fixture.State.ActiveCollectionEditor!.Items[0];
        var method = typeof(TabletBuildPage).GetMethod("CollectionPatchObserved", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (bool toggle in new[] { false, true })
        {
            var request = toggle
                ? new WorkspacePatchCollectionItemRequest(target,
                    ToggleValues: new Dictionary<WorkspaceCollectionToggleField, bool> { [WorkspaceCollectionToggleField.Equipped] = true })
                : new WorkspacePatchCollectionItemRequest(target,
                    TextValues: new Dictionary<WorkspaceCollectionTextField, string?> { [WorkspaceCollectionTextField.Notes] = "wanted" });
            WorkspaceCollectionItemEditorState ambiguous = toggle
                ? item with { ToggleValues = [new(WorkspaceCollectionToggleField.Equipped, true), new(WorkspaceCollectionToggleField.Equipped, false)] }
                : item with { TextValues = [new(WorkspaceCollectionTextField.Notes, "wanted"), new(WorkspaceCollectionTextField.Notes, "other")] };
            CharacterOverviewState state = fixture.State with { ActiveCollectionEditor = fixture.State.ActiveCollectionEditor with
                { Items = [ambiguous] } };
            Require(!(bool)method.Invoke(null, [state, request])!, "Ambiguous duplicate-field readback was accepted as exact.");
        }
    }

    private static void DelayedDamageActionCannotMixTracksAndPickers()
    {
        using var fixture = new Fixture(condition: true);
        Button oldApply = fixture.Button("tablet-condition-save-physical");
        Button oldClear = fixture.Button("tablet-condition-clear-physical");
        fixture.Click("tablet-condition-track-stun");
        fixture.Picker("tablet-condition-filled-stun").SelectedIndex = 6;
        ((IButtonController)oldApply).SendClicked();
        ((IButtonController)oldClear).SendClicked();
        Require(fixture.ConditionRequests.Count == 0,
            "Detached Physical action forwarded a mutation after selecting Stun: "
            + string.Join(", ", fixture.ConditionRequests.Select(request => $"{request.Track}:{request.Filled}")));
        Require(fixture.Picker("tablet-condition-filled-stun").SelectedIndex == 6,
            "Rejected damage action discarded the current Stun draft.");
        fixture.Click("tablet-condition-save-stun");
        Require(fixture.ConditionRequests.SequenceEqual([new(WorkspaceConditionMonitorTrack.Stun, 6)]),
            "Current Stun Apply did not forward exactly its selected value.");
        fixture.Click("tablet-condition-clear-stun");
        Require(fixture.ConditionRequests.SequenceEqual([
            new(WorkspaceConditionMonitorTrack.Stun, 6), new(WorkspaceConditionMonitorTrack.Stun, 0)]),
            "Current Clear did not preserve the Stun typed identity.");
        fixture.Picker("tablet-condition-filled-stun").SelectedIndex = -1;
        fixture.Click("tablet-condition-save-stun");
        Require(fixture.ConditionRequests.Count == 2, "Missing picker choice was converted into a mutation.");
    }

    private static void DetachedMoveCannotReorderThePreviousItem()
    {
        using var fixture = new Fixture(mutable: true);
        Button oldDown = fixture.Button("tablet-inspector-move-down");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        ((IButtonController)oldDown).SendClicked();
        Require(fixture.Requests.Count == 0, "Detached Move reordered the previously selected item.");
        fixture.Click("tablet-inspector-move-up");
        Require(fixture.Requests.Count == 1
            && fixture.Requests[0] is WorkspaceMoveCollectionItemRequest { Target.ItemId: ItemBId, TargetIndex: 0 },
            "Current Move did not forward B's exact typed order request.");
    }

    private static async Task DamageWaitRechecksTrackAndWorkspaceAsync()
    {
        foreach (string change in new[] { "workspace", "revision", "track", "refresh", "departure", "owner-b", "owner-aba", "unchanged" })
        {
            using var fixture = new Fixture(condition: true);
            BindOwnerDriftCase(fixture, change);
            SemaphoreSlim gate = fixture.ActivationGate;
            Require(gate.Wait(0), "Could not reserve workspace gate.");
            Task<bool> action;
            try
            {
                action = (Task<bool>)typeof(TabletBuildPage).GetMethod("ApplyConditionInspectorAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Page,
                    [new ConditionMonitorEditRequest(WorkspaceConditionMonitorTrack.Physical, 7),
                        fixture.State, fixture.Generation])!;
                Require(!action.IsCompleted && fixture.ConditionRequests.Count == 0,
                    "Damage action did not wait for actual activation.");
                switch (change)
                {
                    case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                    case "revision": fixture.AdvanceRevision(); break;
                    case "track": fixture.Click("tablet-condition-track-stun"); break;
                    case "refresh": fixture.Refresh(); break;
                    case "departure": fixture.Depart(); break;
                    case "owner-b":
                    case "owner-aba": ChangeOwnerWithoutRefresh(fixture, change); break;
                }
            }
            finally { gate.Release(); }
            if (change == "unchanged")
            {
                bool observed = false;
                try { await action; } catch (OperationCanceledException) { observed = true; }
                Require(observed && fixture.ConditionRequests.SequenceEqual([new(WorkspaceConditionMonitorTrack.Physical, 7)]),
                    "Unchanged queued damage action did not forward the captured value exactly once.");
            }
            else Require(!await action && fixture.ConditionRequests.Count == 0,
                $"Damage applied after {change} changed behind the gate.");
        }
    }

    private static async Task DeleteConfirmationStaysBoundToTheReviewedItemAsync()
    {
        foreach (string change in new[] { "declined", "selection", "workspace", "revision", "departure", "owner-b", "owner-aba", "unchanged" })
        {
            using var fixture = new Fixture(mutable: true);
            BindOwnerDriftCase(fixture, change);
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Confirmation = answer.Task;
            Button oldDelete = fixture.Button("tablet-inspector-delete");
            Task action = fixture.DeleteAsync();
            Require(!action.IsCompleted && fixture.DialogCalls == 1
                && fixture.Prompt.Contains(ItemAId, StringComparison.Ordinal),
                "Delete did not wait for confirmation of the exact selected item.");
            await fixture.DeleteAsync();
            Require(fixture.DialogCalls == 1, "Overlapping Delete opened a second confirmation.");
            switch (change)
            {
                case "selection": fixture.Click($"tablet-collection-item-{ItemBId}"); break;
                case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                case "revision": fixture.AdvanceRevision(); break;
                case "departure": fixture.Depart(); break;
                case "owner-b":
                case "owner-aba": ChangeOwnerWithoutRefresh(fixture, change); break;
            }
            answer.SetResult(change != "declined");
            await action;
            if (change == "unchanged")
                Require(fixture.Requests.Count == 1 && fixture.Requests[0] is WorkspaceDeleteCollectionItemRequest
                    { Target.ItemId: ItemAId }, "Confirmed current Delete did not preserve A's typed identity.");
            else
                Require(fixture.Requests.Count == 0, $"Delete mutated after {change} while awaiting confirmation.");
            if (change == "selection")
            {
                int dialogs = fixture.DialogCalls;
                ((IButtonController)oldDelete).SendClicked();
                Require(fixture.DialogCalls == dialogs, "Detached Delete opened a fresh dialog for A.");
                fixture.Confirmation = Task.FromResult(true);
                fixture.Click("tablet-inspector-delete");
                Require(fixture.Requests.Count == 1 && fixture.Requests[0].Target.ItemId == ItemBId,
                    "Current B Delete did not remain usable after rejecting A's confirmation.");
            }
        }
    }

    private static async Task DeleteWaitRechecksAuthorityAfterConfirmationAsync()
    {
        foreach (string change in new[] { "selection", "workspace", "revision", "departure", "owner-b", "owner-aba", "unchanged" })
        {
            using var fixture = new Fixture(mutable: true);
            BindOwnerDriftCase(fixture, change);
            fixture.Confirmation = Task.FromResult(true);
            SemaphoreSlim gate = fixture.ActivationGate;
            Require(gate.Wait(0), "Could not reserve the Delete activation gate.");
            Task action;
            try
            {
                action = fixture.DeleteAsync();
                Require(fixture.DialogCalls == 1 && !action.IsCompleted && fixture.Requests.Count == 0,
                    "Confirmed Delete did not wait for the actual activation gate.");
                await fixture.DeleteAsync();
                Require(fixture.DialogCalls == 1, "Queued confirmed Delete admitted a second dialog.");
                switch (change)
                {
                    case "selection": fixture.Click($"tablet-collection-item-{ItemBId}"); break;
                    case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                    case "revision": fixture.AdvanceRevision(); break;
                    case "departure": fixture.Depart(); break;
                    case "owner-b":
                    case "owner-aba": ChangeOwnerWithoutRefresh(fixture, change); break;
                }
            }
            finally { gate.Release(); }
            await action;
            if (change == "unchanged")
                Require(fixture.Requests.Count == 1 && fixture.Requests[0] is WorkspaceDeleteCollectionItemRequest
                    { Target.ItemId: ItemAId }, "Unchanged confirmed Delete did not forward A exactly once after gate release.");
            else
                Require(fixture.Requests.Count == 0,
                    $"Delete mutated after {change} changed between confirmation and activation.");
        }
    }

    private static void NestedChooserKeepsTheCurrentParent()
    {
        using var fixture = new Fixture(nested: true);
        var navigation = new NavigationPage(fixture.Page);
        Button oldAdd = fixture.Button("tablet-inspector-add-gear");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        ((IButtonController)oldAdd).SendClicked();
        Require(navigation.Navigation.NavigationStack.Count == 1 && navigation.CurrentPage == fixture.Page,
            "Detached A Add opened a chooser after selecting B.");

        fixture.Click("tablet-inspector-add-gear");
        // The real managed stack changes synchronously. With no Android handler,
        // the later Pushed animation event is not native-navigation completion proof.
        Page destination = navigation.CurrentPage;
        Require(navigation.Navigation.NavigationStack.Count == 2
            && ReferenceEquals(navigation.CurrentPage, destination)
            && destination is NestedCollectionAddPage && destination.AutomationId == "nested-add-gear",
            "Current B Add did not open exactly the real nested Gear chooser.");
        var parent = (WorkspaceCollectionItemTarget)typeof(NestedCollectionAddPage)
            .GetField("_parent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(destination)!;
        var kind = (WorkspaceNestedCollectionKind)typeof(NestedCollectionAddPage)
            .GetField("_nestedKind", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(destination)!;
        Require(parent == fixture.State.ActiveCollectionEditor!.Items[1].Target
            && kind == WorkspaceNestedCollectionKind.Gear,
            "Nested chooser did not retain B's exact parent identity and nested kind.");
        Require(fixture.Requests.Count == 0, "Opening the nested chooser mutated the runner.");
    }

    private static void DelayedApplyCannotMixTargetsAndControls()
    {
        using var fixture = new Fixture();
        Button oldApply = fixture.Button("tablet-inspector-save");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        fixture.Notes.Text = "B's unsaved notes";
        ((IButtonController)oldApply).SendClicked();
        Require(fixture.Requests.Count == 0,
            "Detached A Apply submitted a mutation after selecting B: "
            + string.Join(", ", fixture.Requests.Select(request => request.Target.ItemId)));
        Require(fixture.Notes.Text == "B's unsaved notes", "Rejected A Apply lost B's draft.");

        fixture.Click("tablet-inspector-save");
        Require(fixture.Requests.Count == 1
            && fixture.Requests[0] is WorkspacePatchCollectionItemRequest
            {
                Target.ItemId: ItemBId, TextValues: { } values
            }
            && values[WorkspaceCollectionTextField.Notes] == "B's unsaved notes",
            "Current B Apply did not forward exactly B's typed patch.");
    }

    private static void DetachedSelectionCannotRestoreAnOldEditor()
    {
        using var fixture = new Fixture();
        Button oldB = fixture.Button($"tablet-collection-item-{ItemBId}");
        fixture.State = fixture.State with { ActiveCollectionEditor = Editor("new A", "new B") };
        fixture.Refresh();
        fixture.Notes.Text = "retained A draft";
        ((IButtonController)oldB).SendClicked();
        Require(fixture.Notes.Text == "retained A draft",
            "Detached selection restored an obsolete editor and lost the current draft.");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        Require(fixture.Notes.Text == "new B", "Current selection did not use the refreshed editor.");
        Require(fixture.Requests.Count == 0, "Selection mutated the runner.");
    }

    private static void RefreshAndDepartureInvalidateApply()
    {
        foreach (bool departure in new[] { false, true })
        {
            using var fixture = new Fixture();
            Button oldApply = fixture.Button("tablet-inspector-save");
            fixture.Notes.Text = "must not be written";
            if (departure) fixture.Depart();
            else fixture.Refresh();
            ((IButtonController)oldApply).SendClicked();
            Require(fixture.Requests.Count == 0, "Old Apply survived same-target refresh or departure.");
            if (!departure)
            {
                fixture.Notes.Text = "current A draft";
                fixture.Click("tablet-inspector-save");
                Require(fixture.Requests.Count == 1 && fixture.Requests[0].Target.ItemId == ItemAId,
                    "Refresh disabled the replacement Apply control.");
            }
        }
    }

    private static async Task QueuedApplyRechecksAuthorityInsideGateAsync()
    {
        foreach (string change in new[]
        {
            "workspace", "revision", "saved-revision", "section", "editor", "selection",
            "refresh", "departure", "busy", "error", "disposed", "owner-b", "owner-aba", "unchanged"
        })
        {
            using var fixture = new Fixture();
            BindOwnerDriftCase(fixture, change);
            fixture.Notes.Text = "queued A draft";
            var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Coordinator)!;
            Require(gate.Wait(0), "Could not reserve the real workspace activation gate.");
            Task<bool> action;
            try
            {
                long generation = (long)typeof(TabletBuildPage).GetField("_inspectorGeneration",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Page)!;
                action = (Task<bool>)typeof(TabletBuildPage).GetMethod("SaveInspectorAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Page,
                        [fixture.State.ActiveCollectionEditor!.Items[0], fixture.State, generation])!;
                Require(!action.IsCompleted && fixture.Requests.Count == 0,
                    "Apply did not wait for the actual activation gate.");
                var workspace = fixture.State.ActiveWorkspace!;
                switch (change)
                {
                    case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                    case "revision":
                    case "saved-revision":
                        var changed = workspace with
                        {
                            ContentRevision = change == "revision" ? 6 : workspace.ContentRevision,
                            SavedRevision = change == "saved-revision" ? 4 : workspace.SavedRevision
                        };
                        fixture.State = fixture.State with
                        {
                            OpenWorkspaces = [changed],
                            Session = new(changed.Id, [changed], [changed.Id])
                        };
                        break;
                    case "section": fixture.State = fixture.State with { ActiveSectionId = "weapons" }; break;
                    case "editor": fixture.State = fixture.State with { ActiveCollectionEditor = Editor("new A", "new B") }; break;
                    case "selection": fixture.Click($"tablet-collection-item-{ItemBId}"); break;
                    case "refresh": fixture.Refresh(); break;
                    case "departure": fixture.Depart(); break;
                    case "busy": fixture.State = fixture.State with { IsBusy = true }; break;
                    case "error": fixture.State = fixture.State with { Error = "Source unavailable" }; break;
                    case "disposed": fixture.Coordinator.Dispose(); break;
                    case "owner-b":
                    case "owner-aba": ChangeOwnerWithoutRefresh(fixture, change); break;
                }
            }
            finally { gate.Release(); }
            if (change == "unchanged")
            {
                bool boundaryCancellation = false;
                try { await action; }
                catch (OperationCanceledException) { boundaryCancellation = true; }
                Require(boundaryCancellation && fixture.Requests.Count == 1
                    && fixture.Requests[0] is WorkspacePatchCollectionItemRequest
                    { Target.ItemId: ItemAId, TextValues: { } values }
                    && values[WorkspaceCollectionTextField.Notes] == "queued A draft",
                    "Unchanged queued Apply did not forward exactly its captured typed patch.");
            }
            else
                Require(!await action && fixture.Requests.Count == 0,
                    $"Queued Apply forwarded an obsolete request after {change} changed.");
        }
    }

    private static void BindOwnerDriftCase(Fixture fixture, string change)
    {
        if (change is not ("owner-b" or "owner-aba")) return;
        fixture.State = fixture.State with
        { DisplayOwnerContext = new(new OwnerScope("tablet-owner-a"), "tablet-account-authority", 1) };
        fixture.Refresh();
    }

    private static void ChangeOwnerWithoutRefresh(Fixture fixture, string change)
    {
        // Leave the workspace, projection references and page generation alone.
        // Only the account publication changes while the action is queued.
        fixture.State = fixture.State with
        { DisplayOwnerContext = new(new OwnerScope("tablet-owner-b"), "tablet-account-authority", 2) };
        if (change == "owner-aba")
            fixture.State = fixture.State with
            { DisplayOwnerContext = new(new OwnerScope("tablet-owner-a"), "tablet-account-authority", 3) };
    }

    private static WorkspaceCollectionEditorState Editor(string a, string b)
        => new("gear", WorkspaceCollectionKind.Gear, null,
        [Item(ItemAId, 0, a), Item(ItemBId, 1, b)]);

    private static WorkspaceCollectionItemEditorState Item(string id, int index, string notes)
        => new(new(WorkspaceCollectionKind.Gear, id), index, id,
            [new(WorkspaceCollectionTextField.Notes, notes)], null, null, [], [],
            CanDelete: false, CanMove: false);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task RejectedNestedAddRetainsDraftAsync()
    {
        using var fixture = new Fixture(mutable: true);
        var expected = fixture.State;
        var page = new NestedCollectionAddPage(fixture.Coordinator,
            expected.ActiveCollectionEditor!.Items[0].Target, WorkspaceNestedCollectionKind.Gear, expected);
        typeof(NestedCollectionAddPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, null);
        var name = (Entry)typeof(NestedCollectionAddPage).GetField("_name", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(page)!;
        name.Text = "Retain my unsubmitted child";
        long generation = (long)typeof(NestedCollectionAddPage).GetField("_renderGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(page)!;
        fixture.State = fixture.State with { WorkspaceId = new("another-runner") };
        bool? accepted = null;
        Func<Task<bool>> action = async () =>
        {
            accepted = await (Task<bool>)typeof(NestedCollectionAddPage).GetMethod("SaveAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [generation])!;
            return accepted.Value;
        };
        await (Task)typeof(NativePageBase).GetMethod("RunWithConditionalRefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [action])!;
        Require(accepted == false && name.Text == "Retain my unsubmitted child"
            && ReferenceEquals(name, typeof(NestedCollectionAddPage).GetField("_name",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page))
            && fixture.Requests.Count == 0,
            "Rejected nested add erased its draft, refreshed the stale form or dispatched to another runner.");
    }

    private static async Task LinkedPickerAndUnknownOutcomeRemainSafeAsync()
    {
        List<string> failures = [];
        foreach (string change in new[] { "workspace", "revision", "same-context" })
        {
            var files = new LinkedFiles();
            using var fixture = new Fixture(linkedFiles: files);
            Task action = fixture.Coordinator.AttachLinkedCharacterAsync(fixture.State.ActiveCollectionEditor!.Items[0].Target);
            Require(files.StageCalls == 1 && !action.IsCompleted, "Attachment did not await the controlled picker.");
            if (change == "workspace") fixture.State = fixture.State with { WorkspaceId = new("other-runner") };
            if (change == "revision") fixture.AdvanceRevision();
            files.Picker.SetResult(files.Staged);
            bool unknown = false;
            try { await action; } catch (OperationCanceledException) { unknown = true; }
            if (change != "same-context" && fixture.Requests.Count != 0)
                failures.Add($"Picker {change} drift dispatched into the wrong authority.");
            if (change == "same-context" && (!unknown || fixture.Requests.Count != 1 || files.Deleted.Count != 0))
                failures.Add("An uncertain dispatched link deleted a file that may already be referenced.");
        }
        Require(failures.Count == 0, string.Join(" ", failures));
    }

    private sealed class LinkedFiles : IAndroidLinkedCharacterFileService
    {
        public readonly TaskCompletionSource<AndroidStagedLinkedCharacter?> Picker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly AndroidStagedLinkedCharacter Staged = new("/test-private/new-link.chum5", "linked-characters/new-link.chum5",
            "new-link.chum5", new("Linked runner", "Runner", "Alias", "Human", "", "", "")) { ContentSha256 = new('a', 64) };
        public readonly List<string?> Deleted = [];
        public int StageCalls;
        public Task<AndroidStagedLinkedCharacter?> StageAsync(WorkspaceCollectionItemTarget target, CancellationToken token)
        { StageCalls++; return Picker.Task; }
        public Task DeleteOwnedAsync(WorkspaceCollectionItemTarget target, string? path, CancellationToken token)
        { Deleted.Add(path); return Task.CompletedTask; }
        public Task<bool> MatchesStagedFileAsync(WorkspaceCollectionItemTarget target, string file, string hash, CancellationToken token)
            => Task.FromResult(file == Staged.FileName && hash == Staged.ContentSha256);
    }

    internal sealed class Fixture : IDisposable
    {
        public CharacterOverviewState State = Program.NewCreationOverview(new("tablet-runner"), 5, 5) with
        {
            ActiveSectionId = "gear",
            ActiveCollectionEditor = Editor("saved A", "saved B")
        };
        public readonly List<WorkspaceCollectionMutationRequest> Requests = [];
        public readonly List<ConditionMonitorEditRequest> ConditionRequests = [];
        public readonly RunnerSessionCoordinator Coordinator;
        public TabletBuildPage Page;
        public Task<bool> Confirmation = Task.FromResult(false);
        public int DialogCalls;
        public string Prompt = string.Empty;
        public Func<WorkspaceCollectionMutationRequest, Task>? CollectionResult;
        public string LinkedJournalDirectory { get; } = Directory.CreateTempSubdirectory("chummer-linked-host-control-").FullName;
        public AndroidLinkedCharacterIntentJournal LinkedJournal { get; }

        public Fixture(bool condition = false, bool mutable = false, bool nested = false, bool rich = false,
            IAndroidLinkedCharacterFileService? linkedFiles = null, IAndroidAccountLinkService? account = null,
            IAndroidLinkedWorkspaceReader? linkedReader = null,
            Func<IAndroidLinkedWorkspaceReader, IAndroidLinkedWorkspaceReader>? linkedReaderDecorator = null)
        {
            if (linkedFiles is not null)
                State = State with { DisplayOwnerContext = LinkedDisplayOwner, ActiveSectionId = "contacts", ActiveCollectionEditor = new("contacts", WorkspaceCollectionKind.Contact, null,
                    State.ActiveCollectionEditor!.Items.Select(item => item with
                    { Target = new(WorkspaceCollectionKind.Contact, item.Target.ItemId),
                        LinkedCharacter = new(true, true, "/test-private/prior.chum5", "linked-characters/prior.chum5", "prior.chum5", true, true)
                    }).ToArray()) };
            if (condition)
                State = State with
                {
                    Profile = State.Profile! with { Created = true },
                    ActiveSectionId = "conditionmonitor", ActiveCollectionEditor = null,
                    ActiveConditionMonitor = new(true, [
                        new(WorkspaceConditionMonitorTrack.Physical, "Physical", 2, 10, 0, 10, 0, "", false),
                        new(WorkspaceConditionMonitorTrack.Stun, "Stun", 3, 10, 0, 10, 0, "", false)])
                };
            if (mutable)
                State = State with { ActiveCollectionEditor = State.ActiveCollectionEditor! with
                {
                    Items = State.ActiveCollectionEditor!.Items.Select(item => item with
                    { CanMove = true, CanDelete = true }).ToArray()
                }};
            if (nested)
                State = State with { ActiveCollectionEditor = State.ActiveCollectionEditor! with
                {
                    Items = State.ActiveCollectionEditor!.Items.Select(item => item with
                    { AddableNestedKinds = [WorkspaceNestedCollectionKind.Gear] }).ToArray()
                }};
            if (rich)
                State = State with { ActiveCollectionEditor = State.ActiveCollectionEditor! with
                {
                    Items = State.ActiveCollectionEditor!.Items.Select(item => item with
                    {
                        Rating = new(1, 0, 6), Quantity = new(1),
                        Contact = new(2, 6, true, 2, 6, true, true),
                        ToggleValues = [new(WorkspaceCollectionToggleField.Equipped, false)],
                        PhysicalConditionMonitor = new("Physical", 1, 10, true, true),
                        MatrixConditionMonitor = new("Matrix", 2, 10, true, true)
                    }).ToArray()
                }};
            var presenter = TabletMutationProxy.Create(() => State, Requests.Add, ConditionRequests.Add,
                request => CollectionResult?.Invoke(request) ?? Task.FromCanceled(new CancellationToken(true)));
            LinkedJournal = new AndroidLinkedCharacterIntentJournal(LinkedJournalDirectory);
            linkedReader ??= new ControlledLinkedReader(() => State);
            if (linkedReaderDecorator is not null) linkedReader = linkedReaderDecorator(linkedReader);
            Coordinator = new RunnerSessionCoordinator(presenter,
                null!, null!, null!, null!, null!, null!, StrictPageProxy.Create<IShellPresenter>(),
                null!, null!, null!, linkedFiles!, null!, account ?? StrictPageProxy.Create<IAndroidAccountLinkService>(),
                null!, null!, linkedCharacterJournal: LinkedJournal, linkedWorkspaceReader: linkedReader);
            Page = NewPage();
            Refresh();
        }

        private TabletBuildPage NewPage() => new(Coordinator, (_, message, _, _) =>
            {
                DialogCalls++;
                Prompt = message;
                return Confirmation;
            });

        public InputView Notes => Elements(Page).OfType<InputView>()
            .Single(input => input.AutomationId == "tablet-field-notes");
        public Button Button(string id) => Elements(Page).OfType<Button>()
            .Single(button => button.AutomationId == id);
        public Picker Picker(string id) => Elements(Page).OfType<Picker>()
            .Single(picker => picker.AutomationId == id);
        public InputView Input(string id) => Elements(Page).OfType<InputView>()
            .Single(input => input.AutomationId == id);
        public Switch Toggle(string id) => Elements(Page).OfType<Switch>()
            .Single(input => input.AutomationId == id);
        public Label Label(string id) => Elements(Page).OfType<Label>()
            .Single(input => input.AutomationId == id);
        public bool Has(string id) => Elements(Page).Any(element => element.AutomationId == id);
        public void Recreate()
        {
            Depart();
            Page = NewPage();
            Refresh();
        }
        public Task DiscardAsync() => (Task)typeof(TabletBuildPage).GetMethod("DiscardInspectorDraftAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, [State, Generation])!;
        public long Generation => (long)typeof(TabletBuildPage).GetField("_inspectorGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Page)!;
        public SemaphoreSlim ActivationGate => (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Coordinator)!;
        public Task DeleteAsync() => (Task)typeof(TabletBuildPage).GetMethod("DeleteInspectorItemAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page,
                [State.ActiveCollectionEditor!.Items[0], State, Generation])!;
        public void AdvanceRevision()
        {
            var changed = State.ActiveWorkspace! with { ContentRevision = State.ContentRevision + 1 };
            State = State with { OpenWorkspaces = [changed], Session = new(changed.Id, [changed], [changed.Id]) };
        }
        public void Click(string id) => ((IButtonController)Button(id)).SendClicked();
        public void Refresh() => typeof(TabletBuildPage).GetMethod("Refresh",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, null);
        public void Depart() => typeof(TabletBuildPage).GetMethod("OnDisappearing",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, null);
        public void Dispose()
        {
            Coordinator.Dispose();
            Directory.Delete(LinkedJournalDirectory, recursive: true); // Only this fixture's fresh private directory.
        }
    }

    private sealed class ControlledLinkedReader(Func<CharacterOverviewState> state) : IAndroidLinkedWorkspaceReader
    {
        private static OwnerContextStamp Stamp => LinkedDisplayOwner;
        public bool IsAvailable => true;
        public AndroidLinkedOwner CurrentOwner => new("local-single-user", true);
        public Task<OwnerContextStamp> CaptureOwnerContextAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Stamp); // Host-control fixture, not production lease proof.
        }
        public Task<AndroidLinkedOwner> ReadCurrentOwnerAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(CurrentOwner); // In-memory controlled owner, no file I/O.
        }
        public async Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(Chummer.Contracts.Workspaces.CharacterWorkspaceId id,
            string section, WorkspaceCollectionMutationRequest request, CancellationToken token)
        {
            var snapshot = await ReadAsync(id, section, token);
            return snapshot! with { ExpectedDocumentAuthoritySha256 =
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"controlled:{snapshot.ContentRevision + 1}:{snapshot.SavedRevision}"))) };
        }
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(OwnerContextStamp expectedOwner,
            Chummer.Contracts.Workspaces.CharacterWorkspaceId id, string section,
            WorkspaceCollectionMutationRequest request, CancellationToken token)
            => expectedOwner == Stamp ? ReadForMutationAsync(id, section, request, token)
                : Task.FromResult<AndroidLinkedWorkspaceSnapshot?>(null);
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadAsync(Chummer.Contracts.Workspaces.CharacterWorkspaceId id,
            string section, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var current = state();
            // Controlled host observation only; real canonical reads are exercised
            // separately through actual Core/Presentation/native/FileWorkspaceStore.
            return Task.FromResult<AndroidLinkedWorkspaceSnapshot?>(new(CurrentOwner, id.Value,
                current.ContentRevision, current.SavedRevision,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"controlled:{current.ContentRevision}:{current.SavedRevision}"))),
                current.ActiveCollectionEditor));
        }
    }

    private static IEnumerable<Element> Elements(Element root)
    {
        yield return root;
        IEnumerable<Element> children = root switch
        {
            ContentPage page when page.Content is not null => [page.Content],
            ScrollView scroll when scroll.Content is not null => [scroll.Content],
            Border border when border.Content is not null => [border.Content],
            Layout layout => layout.Children.OfType<Element>(),
            _ => []
        };
        foreach (Element child in children)
        foreach (Element descendant in Elements(child)) yield return descendant;
    }
}

public interface ITabletOwnerBoundMutationTestPresenter : ICharacterOverviewPresenter, IOwnerBoundWorkspaceMutationPresenter { }

public class TabletMutationProxy : DispatchProxy
{
    private Func<CharacterOverviewState> _state = null!;
    private Action<WorkspaceCollectionMutationRequest> _observe = null!;
    private Action<ConditionMonitorEditRequest> _condition = null!;
    private Func<WorkspaceCollectionMutationRequest, Task>? _collectionResult;

    public static ICharacterOverviewPresenter Create(Func<CharacterOverviewState> state,
        Action<WorkspaceCollectionMutationRequest> observe,
        Action<ConditionMonitorEditRequest> condition,
        Func<WorkspaceCollectionMutationRequest, Task>? collectionResult = null)
    {
        var instance = Create<ITabletOwnerBoundMutationTestPresenter, TabletMutationProxy>();
        var proxy = (TabletMutationProxy)(object)instance;
        proxy._state = state;
        proxy._observe = observe;
        proxy._condition = condition;
        proxy._collectionResult = collectionResult;
        return instance;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string name = method?.Name ?? throw new InvalidOperationException("Missing method.");
        if (name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal))
            return null;
        if (name == "get_State") return _state();
        if (name == "ApplyConditionMonitorEditAsync")
        {
            _condition((ConditionMonitorEditRequest)args![0]!);
            return Task.FromCanceled(new CancellationToken(canceled: true));
        }
        if (name == "ApplyCollectionMutationAsync")
        {
            if (args!.Length == 3)
                return ApplyBoundAsync((WorkspaceCollectionMutationRequest)args[0]!,
                    (OwnerContextStamp)args[1]!, (CancellationToken)args[2]!);
            _observe((WorkspaceCollectionMutationRequest)args![0]!);
            if (_collectionResult is not null)
                return _collectionResult((WorkspaceCollectionMutationRequest)args[0]!);
            // Observe the real coordinator's typed boundary; do not fabricate Core writes,
            // receipts, or persistence. Cancellation prevents unrelated shell side effects.
            return Task.FromCanceled(new CancellationToken(canceled: true));
        }
        throw new InvalidOperationException($"Unexpected tablet dependency: {name}");
    }

    private async Task<OwnerBoundWorkspaceMutationDispatch> ApplyBoundAsync(
        WorkspaceCollectionMutationRequest request, OwnerContextStamp stamp, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return OwnerBoundWorkspaceMutationDispatch.NotDispatched;
        if (!stamp.IsValid) throw new InvalidOperationException("The host dropped its original owner stamp.");
        _observe(request);
        await (_collectionResult?.Invoke(request) ?? Task.FromCanceled(new CancellationToken(true)));
        // This test boundary does not establish any Core write or receipt.
        return OwnerBoundWorkspaceMutationDispatch.Dispatched;
    }
}
