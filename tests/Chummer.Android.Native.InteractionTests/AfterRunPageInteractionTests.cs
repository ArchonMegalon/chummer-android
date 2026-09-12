using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Android.Sr5AfterRunReward.Tests;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static Task RunNativeDialogBusyCaseAsync()
    {
        using var fixture = new PageFixture();
        DesktopDialogState dialog = new("dialog.new_character", "Select Build Method", null, [],
            [new DesktopDialogAction("create_character", "OK"), new DesktopDialogAction("cancel", "Cancel")]);
        var page = new NativeDialogPage(fixture.Coordinator, dialog);
        MethodInfo setBusy = typeof(NativeDialogPage).GetMethod("SetInteractionBusy",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo render = typeof(NativeDialogPage).GetMethod("Render",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Check(false);
        setBusy.Invoke(page, [true]);
        Check(true);
        // Replacement controls must retain the in-flight action's visible state.
        render.Invoke(page, [dialog]);
        Check(true);
        setBusy.Invoke(page, [false]);
        Check(false);
        Console.WriteLine("PASS actual native dialog busy container, children, actions and rerender");
        return Task.CompletedTask;

        void Check(bool busy)
        {
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content;
            var container = body.Children.OfType<HorizontalStackLayout>()
                .Single(item => item.AutomationId == "dialog-busy");
            var indicator = container.Children.OfType<ActivityIndicator>().Single();
            var label = container.Children.OfType<Label>().Single();
            Require(container.IsVisible == busy && indicator.IsVisible == busy
                    && indicator.IsRunning == busy && label.IsVisible == busy,
                "Busy feedback must have a visible parent and running children only during the action.");
            Require(body.Children.OfType<Grid>().Single().Children.OfType<Button>()
                    .All(button => button.IsEnabled == !busy)
                    && page.ToolbarItems.Single().IsEnabled == !busy,
                "Native dialog actions and Close must follow the busy interaction state.");
        }
    }

    public static async Task RunNativePageCasesAsync()
    {
        await DiscardDialogKeepsOrDeletesOnlyTheReviewedJournalAsync();
        await DepartedDiscardDialogCannotDeleteReviewAsync();
        await ChangedRunnerDialogCannotDeleteReviewAsync();
        await ReplacedCheckpointDialogCannotDeleteNewReviewAsync();
        await ApplyingCheckpointDialogCannotDeleteRecoveryAsync();
        await NativeEntryFactorySelectsOnlyAllowedPagesAsync();
        await NativeExplicitGovernedEntryRequiresIntentAndPreservesRecoveryAsync();
        await ManualEntryAndPublicationRejectInactiveRunnerAsync();
        await ManualPublicationUsesRealSourceAndPreservesCanceledInputAsync();
        await ManualPublicationDoesNotAcceptResultAfterDisposalAsync();
        await NativeEntryFactoryRejectsChangedOrCanceledObservationAsync();
        await NativeEntryFactoryPreservesPendingRewardBesideSettlementAsync();
        await NativeEntryFactoryKeepsApplyingSettlementPriorityAsync();
        Console.WriteLine("PASS 13 native After Run page/dialog/entry cases");
    }

    private static async Task DiscardDialogKeepsOrDeletesOnlyTheReviewedJournalAsync()
    {
        using var fixture = new PageFixture();
        Button discard = fixture.Button("sr5-after-run-abandon");
        Require(discard.IsVisible && discard.IsEnabled, "Changed catalog hid explicit discard.");
        Require(!fixture.Button("sr5-after-run-resume").IsVisible
                && !fixture.Button("sr5-after-run-open-rewards").IsEnabled,
            "Discard-only page enabled resume or new settlement.");
        string retained = fixture.Backend.Payload;
        Task action = fixture.Page.AbandonReviewedAsync();
        Require(!action.IsCompleted && fixture.DialogCalls == 1, "Page did not await real action-path confirmation.");
        Require(fixture.Prompt.Contains("Operation Glass Harbor", StringComparison.Ordinal)
                && fixture.Prompt.Contains("41", StringComparison.Ordinal), "Dialog omitted original review identity.");
        await fixture.Page.AbandonReviewedAsync();
        Require(fixture.DialogCalls == 1, "Overlapping action opened a second confirmation.");
        fixture.Answer.SetResult(false);
        await action;
        Require(fixture.Backend.Payload == retained && fixture.OwnerBackend.Payload.Length == 0,
            "Declining confirmation changed retained state.");
        fixture.Answer = NewAnswer();
        action = fixture.Page.AbandonReviewedAsync();
        fixture.Answer.SetResult(true);
        await action;
        Require(fixture.Backend.Payload.Length == 0 && fixture.OwnerBackend.Payload.Length == 0,
            "Explicit confirmation did not retire only the local review.");
        Require(!discard.IsVisible && fixture.State.ContentRevision == 42,
            "Page retained stale discard control or changed runner revision.");
    }

    private static async Task DepartedDiscardDialogCannotDeleteReviewAsync()
    {
        using var fixture = new PageFixture();
        string retained = fixture.Backend.Payload;
        Task action = fixture.Page.AbandonReviewedAsync();
        // Invoke the actual native lifecycle hook, without substituting its body
        // or claiming Android handler/activity execution.
        typeof(Sr5AfterRunSettlementWizardPage).GetMethod("OnDisappearing",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Page, null);
        fixture.Answer.SetResult(true);
        await action;
        Require(fixture.Backend.Payload == retained,
            "A confirmation arriving after page departure deleted the review.");
        fixture.Answer = NewAnswer();
        int priorDialogs = fixture.DialogCalls;
        Task queued = fixture.Page.AbandonReviewedAsync();
        fixture.Answer.TrySetResult(false);
        await queued;
        Require(fixture.DialogCalls == priorDialogs && fixture.Backend.Payload == retained,
            "A queued old-page click opened a new dialog after departure.");
    }

    private static TaskCompletionSource<bool> NewAnswer()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task ChangedRunnerDialogCannotDeleteReviewAsync()
    {
        foreach (string change in new[] { "revision", "busy", "dirty", "error", "selection-roundtrip", "disposed" })
        {
            using var fixture = new PageFixture();
            string retained = fixture.Backend.Payload;
            Task action = fixture.Page.AbandonReviewedAsync();
            var original = fixture.State;
            switch (change)
            {
                case "revision":
                case "dirty":
                    var workspace = original.ActiveWorkspace! with
                    {
                        ContentRevision = 43,
                        SavedRevision = change == "dirty" ? 42 : 43
                    };
                    fixture.State = original with
                    {
                        OpenWorkspaces = [workspace],
                        Session = new(WorkspaceId, [workspace], [WorkspaceId])
                    };
                    break;
                case "busy": fixture.State = original with { IsBusy = true }; break;
                case "error": fixture.State = original with { Error = "Reload failed." }; break;
                case "disposed": fixture.Coordinator.Dispose(); break;
                case "selection-roundtrip":
                    fixture.State = original with { WorkspaceId = new("another-runner") };
                    _ = fixture.Coordinator.CaptureAfterRunRewardBinding(OwnerId);
                    fixture.State = original;
                    _ = fixture.Coordinator.CaptureAfterRunRewardBinding(OwnerId);
                    break;
            }
            fixture.Answer.SetResult(true);
            await action;
            Require(fixture.Backend.Payload == retained && fixture.OwnerBackend.Payload.Length == 0,
                $"A {change} change allowed the old dialog to remove its review.");
        }
    }

    private static async Task ReplacedCheckpointDialogCannotDeleteNewReviewAsync()
    {
        using var fixture = new PageFixture(staleCatalog: false);
        Task action = fixture.Page.AbandonReviewedAsync();
        Require(fixture.Store.TryDeleteReviewed(Sr5AfterRunSettlementCheckpointCas.From(fixture.Checkpoint),
            out var blocker), blocker);
        var editor = Editor(Input());
        Require(Sr5AfterRunSettlementDraft.TryCreate(editor, editor.Candidates.Single(), OwnerId,
            Guid.NewGuid(), AllReviewed(), out var replacement, out blocker), blocker);
        Require(fixture.Store.TryCreate(Sr5AfterRunSettlementCheckpoint.FromDraft(replacement),
            out _, out blocker), blocker);
        string retained = fixture.Backend.Payload;
        // Execute actual page re-observation while its native dialog awaits input.
        typeof(Sr5AfterRunSettlementWizardPage).GetMethod("LoadCheckpoint",
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Page, null);
        fixture.Answer.SetResult(true);
        await action;
        Require(fixture.Backend.Payload == retained,
            "The dialog confirmed the replacement review rather than its original checkpoint.");
    }

    private static async Task ApplyingCheckpointDialogCannotDeleteRecoveryAsync()
    {
        using var fixture = new PageFixture(staleCatalog: false);
        Task action = fixture.Page.AbandonReviewedAsync();
        Require(fixture.Store.TryBeginApply(Sr5AfterRunSettlementCheckpointCas.From(fixture.Checkpoint),
            out _, out var blocker), blocker);
        string retained = fixture.Backend.Payload;
        string ownership = fixture.OwnerBackend.Payload;
        fixture.Answer.SetResult(true);
        await action;
        Require(fixture.Backend.Payload == retained && fixture.OwnerBackend.Payload == ownership,
            "The dialog erased an Applying checkpoint or its shared recovery owner.");
        Require(!fixture.Button("sr5-after-run-abandon").IsVisible
            && !fixture.Button("sr5-after-run-resume").IsVisible
            && fixture.Button("sr5-after-run-resolve").IsVisible,
            "After rejected discard the page still presented the stale Reviewed controls.");
    }

    private static async Task NativeEntryFactorySelectsOnlyAllowedPagesAsync()
    {
        using var fixture = new PageFixture();
        var missing = fixture.EditorState with { Status = Sr5AfterRunCatalogStatus.Missing };
        // An old unapplied review must not turn Missing into a new award.
        Page review = await fixture.EnterAsync(missing);
        Require(review is Sr5AfterRunSettlementWizardPage && fixture.RewardBackend.Reads == 1,
            "Factory bypassed discard-only settlement or failed to observe local recovery.");
        Require(fixture.Store.TryDeleteReviewed(Sr5AfterRunSettlementCheckpointCas.From(fixture.Checkpoint),
            out var blocker), blocker);
        Page reward = await fixture.EnterAsync(missing);
        Require(reward is Sr5AfterRunRewardWizardPage
                && reward.AutomationId == "sr5-after-run-local-reward-page",
            "Missing catalog and empty journals did not route to the actual local reward page.");
        var view = (Sr5AfterRunRewardView)((ScrollView)((ContentPage)reward).Content!).Content;
        var body = (VerticalStackLayout)view.Content!;
        Require(body.Children.OfType<Button>().Single(b => b.AutomationId == "sr5-reward-preview").IsEnabled
                && !body.Children.OfType<Button>().Single(b => b.AutomationId == "sr5-reward-confirm").IsEnabled,
            "Factory did not hand its prepared editable model to the actual native reward controls.");
        Require(fixture.Backend.Payload.Length == 0 && fixture.OwnerBackend.Payload.Length == 0
                && fixture.RewardBackend.Writes == 0 && fixture.ServiceCalls == 0,
            "Entry observation wrote state or called a rule/mutation service.");

        using var governed = new PageFixture(staleCatalog: false);
        Require(governed.Store.TryDeleteReviewed(Sr5AfterRunSettlementCheckpointCas.From(governed.Checkpoint),
            out blocker), blocker);
        Page proposal = await governed.EnterAsync(governed.EditorState);
        Require(proposal is Sr5AfterRunSettlementWizardPage, "Available catalog was replaced by a new reward.");
        governed.RewardBackend.Payload = "{}";
        await ExpectEntryRejectedAsync(() => governed.EnterAsync(governed.EditorState));
        Require(governed.RewardBackend.Payload == "{}" && governed.RewardBackend.Writes == 0,
            "Corrupt local journal was cleared or bypassed on entry.");
    }

    private static async Task NativeExplicitGovernedEntryRequiresIntentAndPreservesRecoveryAsync()
    {
        using var fixture = new PageFixture(manualEntry: true);
        var missing = fixture.EditorState with { Status = Sr5AfterRunCatalogStatus.Missing };
        Require(await fixture.EnterAsync(missing, requestGovernedProposal: true) is Sr5AfterRunSettlementWizardPage,
            "Explicit manual entry bypassed a discard-only review.");
        Require(fixture.Store.TryDeleteReviewed(Sr5AfterRunSettlementCheckpointCas.From(fixture.Checkpoint),
            out var blocker), blocker);
        Require(await fixture.EnterAsync(missing) is Sr5AfterRunRewardWizardPage,
            "Manual authority availability changed the ordinary local reward default.");
        Page manual = await fixture.EnterAsync(missing, requestGovernedProposal: true);
        Require(manual is Sr5AfterRunManualProposalPage && manual.AutomationId == Sr5CareerWizardRoutes.AfterRunEnter,
            "Explicit governed entry cannot reach the actual manual proposal page.");
        Require(fixture.Backend.Payload.Length == 0 && fixture.OwnerBackend.Payload.Length == 0
                && fixture.RewardBackend.Writes == 0 && fixture.ServiceCalls == 0,
            "Selecting governed intake wrote a journal, fabricated a proposal, or called Core.");
        Require(await fixture.EnterAsync(fixture.EditorState, requestGovernedProposal: true) is Sr5AfterRunSettlementWizardPage,
            "Unavailable catalog was treated as permission for manual intake.");
        fixture.RewardBackend.Payload = "{}";
        await ExpectEntryRejectedAsync(() => fixture.EnterAsync(missing, requestGovernedProposal: true));
        Require(fixture.RewardBackend.Payload == "{}" && fixture.RewardBackend.Writes == 0,
            "Explicit intake cleared a corrupt local recovery journal.");

        using var unavailable = new PageFixture();
        Require(unavailable.Store.TryDeleteReviewed(Sr5AfterRunSettlementCheckpointCas.From(unavailable.Checkpoint),
            out blocker), blocker);
        Require(await unavailable.EnterAsync(missing, requestGovernedProposal: true) is Sr5AfterRunSettlementWizardPage,
            "Missing manual host authority permitted intake or silently substituted local rewards.");

        using var available = new PageFixture(staleCatalog: false, manualEntry: true);
        Require(available.Store.TryDeleteReviewed(Sr5AfterRunSettlementCheckpointCas.From(available.Checkpoint),
            out blocker), blocker);
        Require(await available.EnterAsync(available.EditorState, requestGovernedProposal: true) is Sr5AfterRunSettlementWizardPage,
            "An existing governed catalog was replaced by manual intake.");
    }

    private static async Task ManualEntryAndPublicationRejectInactiveRunnerAsync()
    {
        List<string> failures = [];
        foreach (string change in new[] { "disposed", "creation", "sr6", "busy", "error", "dirty", "revision", "foreign" })
        {
            using var fixture = new PageFixture(staleCatalog: false, manualEntry: true);
            Require(fixture.Coordinator.SupportsManualAfterRunProposalEntry,
                "The test did not compose real manual entry capability.");
            _ = new Sr5AfterRunManualProposalPage(fixture.Coordinator, WorkspaceId, 41);
            string journal = fixture.Backend.Payload;
            switch (change)
            {
                case "disposed": fixture.Coordinator.Dispose(); break;
                case "creation": fixture.State = fixture.State with
                    { Profile = fixture.State.Profile! with { Created = false } }; break;
                case "sr6": fixture.State = fixture.State with
                    { Rules = fixture.State.Rules! with { GameEdition = "SR6" } }; break;
                case "busy": fixture.State = fixture.State with { IsBusy = true }; break;
                case "error": fixture.State = fixture.State with { Error = "Reload failed." }; break;
                case "foreign": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                default:
                    var workspace = fixture.State.ActiveWorkspace! with
                    { ContentRevision = 42, SavedRevision = change == "dirty" ? 41 : 42 };
                    fixture.State = fixture.State with
                    { OpenWorkspaces = [workspace], Session = new(WorkspaceId, [workspace], [WorkspaceId]) };
                    break;
            }
            if (change == "disposed" && fixture.Coordinator.SupportsManualAfterRunProposalEntry)
                failures.Add("disposed coordinator still advertises manual authority");
            bool rejected = false;
            try { _ = new Sr5AfterRunManualProposalPage(fixture.Coordinator, WorkspaceId, 41); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) failures.Add($"{change} constructed a manual page");
            try
            {
                var result = await fixture.Coordinator.PublishManualAfterRunProposalAsync(ManualSubmission());
                if (result.Published || result.Proposal is not null || string.IsNullOrWhiteSpace(result.Blocker))
                    failures.Add($"{change} returned an accepted publication");
            }
            catch (InvalidOperationException)
            {
                // A throwing dependency is not an entry rejection: the call
                // counter below proves whether the host was reached at all.
            }
            if (fixture.ServiceCalls != 0) failures.Add($"{change} invoked proposal authority");
            Require(fixture.Backend.Payload == journal && fixture.OwnerBackend.Payload.Length == 0
                    && fixture.RewardBackend.Writes == 0,
                $"Rejected {change} entry changed recovery state.");
        }
        Require(failures.Count == 0, string.Join("; ", failures));
    }

    private static async Task ManualPublicationUsesRealSourceAndPreservesCanceledInputAsync()
    {
        var backend = new ManualProposalBackend();
        var source = new Sr5AfterRunManualProposalSource(new ManualSnapshotSource(ManualSnapshot(41, 'f')), backend);
        using var fixture = new PageFixture(staleCatalog: false, manualAuthority: source);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool rejected = false;
        try { await fixture.Coordinator.PublishManualAfterRunProposalAsync(ManualSubmission(), canceled.Token); }
        catch (OperationCanceledException) { rejected = true; }
        Require(rejected && backend.Payload.Length == 0,
            "A canceled queued proposal entered the real persistent source.");
        var result = await fixture.Coordinator.PublishManualAfterRunProposalAsync(ManualSubmission());
        Require(result.Published && !result.Replayed && result.Proposal?.IsExact() == true,
            "An exact live Career runner cannot publish through the actual host/Core source.");
        string persisted = backend.Payload;
        var replay = await fixture.Coordinator.PublishManualAfterRunProposalAsync(ManualSubmission());
        Require(replay.Published && replay.Replayed && backend.Payload == persisted
                && fixture.State.ContentRevision == 41 && !fixture.State.IsDirty,
            "Revisiting the same manual proposal duplicated or changed the runner transaction.");
    }

    private static async Task ManualPublicationDoesNotAcceptResultAfterDisposalAsync()
    {
        var backend = new ManualProposalBackend();
        var source = new Sr5AfterRunManualProposalSource(new ManualSnapshotSource(ManualSnapshot(41, 'f')), backend);
        var observed = new ObservedManualCatalog(source);
        using var fixture = new PageFixture(staleCatalog: false, manualAuthority: observed);
        observed.BeforePublish = fixture.Coordinator.Dispose;
        bool rejected = false;
        try { await fixture.Coordinator.PublishManualAfterRunProposalAsync(ManualSubmission()); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && observed.PublishCalls == 1 && backend.Payload.Length > 0,
            "A disposed caller accepted an in-flight result or pretended the proposal write was rolled back.");
        string persisted = backend.Payload;
        var later = await fixture.Coordinator.PublishManualAfterRunProposalAsync(ManualSubmission());
        Require(!later.Published && observed.PublishCalls == 1 && backend.Payload == persisted
                && source.Load(WorkspaceId).Entries.Count == 1 && fixture.State.ContentRevision == 41,
            "Shutdown replayed, erased, duplicated or applied the durably recorded proposal.");
    }

    private sealed class ObservedManualCatalog(Sr5AfterRunManualProposalSource source) : IManualEntryCatalog
    {
        public Action? BeforePublish;
        public int PublishCalls;
        public Sr5AfterRunProposalCatalogResult Load(Chummer.Contracts.Workspaces.CharacterWorkspaceId workspaceId)
            => source.Load(workspaceId);
        public Sr5AfterRunManualProposalPublishResult Publish(Sr5AfterRunManualProposalSubmission submission)
        {
            PublishCalls++;
            BeforePublish?.Invoke();
            return source.Publish(submission);
        }
    }

    private static async Task NativeEntryFactoryRejectsChangedOrCanceledObservationAsync()
    {
        foreach (string change in new[] { "canceled-before", "canceled-during", "busy", "selection" })
        foreach (bool requestGovernedProposal in new[] { false, true })
        {
            using var fixture = new PageFixture();
            using var cancellation = new CancellationTokenSource();
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.RewardBackend.OnRead = () =>
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release journal observation.");
            };
            string retained = fixture.Backend.Payload;
            if (change == "canceled-before") cancellation.Cancel();
            Task<Page> entry = fixture.EnterAsync(fixture.EditorState, cancellation.Token, requestGovernedProposal);
            try
            {
                if (change != "canceled-before")
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(!entry.IsCompleted, "Factory did not await background journal observation.");
                    if (change == "canceled-during") cancellation.Cancel();
                    else fixture.State = fixture.State with
                    {
                        IsBusy = change == "busy",
                        WorkspaceId = change == "selection" ? new("different-runner") : WorkspaceId
                    };
                }
            }
            finally { release.Set(); }
            await ExpectEntryRejectedAsync(() => entry);
            Require(fixture.Backend.Payload == retained && fixture.RewardBackend.Writes == 0
                    && fixture.ServiceCalls == 0,
                $"Rejected {change} entry mutated state.");
            if (change == "canceled-before") Require(fixture.DependencyReads == 0,
                "A canceled entry created platform dependencies before checking its token.");
        }
    }

    private static async Task ExpectEntryRejectedAsync(Func<Task<Page>> entry)
    {
        try { await entry(); }
        catch (OperationCanceledException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Changed, canceled or corrupt entry returned a page.");
    }

    private static async Task NativeEntryFactoryPreservesPendingRewardBesideSettlementAsync()
    {
        using var fixture = new PageFixture(staleCatalog: false);
        using var source = new RealAfterRunRewardFixture(workspaceId: WorkspaceId);
        var prepared = source.Service.Preview(new CharacterAfterRunRewardPreviewRequest(
            WorkspaceId, Guid.NewGuid(), Guid.NewGuid(), 8, 1000,
            new DateTime(2026, 9, 7, 11, 0, 0, DateTimeKind.Unspecified), "Earlier local reward"));
        Require(prepared.Preview is not null && prepared.Outcome == CharacterAfterRunRewardOutcome.Available,
            "Real Core could not prepare the older reward command.");
        var command = prepared.Preview!.Command with { ExplicitlyConfirmed = true };
        // The caller now observes a later saved revision; the original command
        // remains pending and needs explicit resolution, not an invented rebase.
        var pending = new Sr5AfterRunRewardCheckpoint(1, 1, OwnerId,
            Sr5AfterRunRewardCheckpointPhase.Confirmed, command, command.CommandDigest());
        Require(fixture.RewardStore.TryPrepare(pending, () => true, out var blocker), blocker);
        string retainedReward = fixture.RewardBackend.Payload;
        string retainedSettlement = fixture.Backend.Payload;
        Page route = await fixture.EnterAsync(fixture.EditorState);
        Require(route is Sr5AfterRunRewardWizardPage,
            "An available catalog/review hid the original pending reward.");
        Require(await fixture.EnterAsync(fixture.EditorState, requestGovernedProposal: true) is Sr5AfterRunRewardWizardPage,
            "Explicit governed entry bypassed the pending local reward.");
        Require(fixture.RewardBackend.Payload == retainedReward && fixture.Backend.Payload == retainedSettlement,
            "Entry rebased or retired either retained intent.");

        var applied = fixture.Checkpoint with
        {
            Version = 3,
            Phase = Sr5CareerCheckpointPhase.Applied,
            Receipt = Receipt(fixture.Checkpoint.Draft)
        };
        Require(applied.IsStructurallyValid(), "Recorded receipt fixture is invalid.");
        fixture.Backend.Payload = JsonSerializer.Serialize(applied);
        var saved = fixture.State.ActiveWorkspace! with { ContentRevision = 43, SavedRevision = 43 };
        fixture.State = fixture.State with { OpenWorkspaces = [saved], Session = new(WorkspaceId, [saved], [WorkspaceId]) };
        var current = Sr5AfterRunSettlementEditorState.Unavailable(WorkspaceId, 43, "No current catalog.");
        route = await fixture.EnterAsync(current);
        Require(route is Sr5AfterRunRewardWizardPage && fixture.RewardBackend.Payload == retainedReward,
            "Historical settlement receipt hid or replaced the pending reward.");
        fixture.RewardBackend.Payload = "{}";
        string receiptPayload = fixture.Backend.Payload;
        route = await fixture.EnterAsync(current);
        Require(route is Sr5AfterRunSettlementReceiptPage && fixture.RewardBackend.Payload == "{}"
                && fixture.Backend.Payload == receiptPayload && fixture.OwnerBackend.Payload.Length == 0
                && fixture.ServiceCalls == 0,
            "Read-only receipt fallback changed journals or invoked mutation/lookup authority.");
        var receiptBody = (VerticalStackLayout)((ScrollView)((ContentPage)route).Content!).Content;
        var receiptCard = (VerticalStackLayout)receiptBody.Children.OfType<Border>().Single().Content!;
        foreach ((string id, string expected) in new[]
        {
            ("sr5-after-run-receipt-transaction-id", applied.Receipt!.TransactionId.ToString("D")),
            ("sr5-after-run-receipt-digest", applied.Receipt.ReceiptDigest)
        })
        {
            var row = receiptCard.Children.OfType<VerticalStackLayout>().Single(item =>
                item.Children.OfType<Label>().Any(label => label.AutomationId == id));
            Require(row.Children.Count == 2 && row.Children[0] is Label { Text.Length: > 0 }
                    && row.Children[1] is Label value && value.Text == expected
                    && value.LineBreakMode == LineBreakMode.CharacterWrap,
                "Receipt identity must retain its full value beneath a separate label with character wrapping.");
        }
        Require(await fixture.EnterAsync(current, requestGovernedProposal: true) is Sr5AfterRunSettlementReceiptPage,
            "Explicit governed entry hid the read-only historical receipt.");
        Require(source.Service.Read(WorkspaceId).Snapshot!.AvailableKarma == 30,
            "Read-only entry committed the earlier reward.");
    }

    private static async Task NativeEntryFactoryKeepsApplyingSettlementPriorityAsync()
    {
        using var fixture = new PageFixture(staleCatalog: false);
        Require(fixture.Store.TryBeginApply(Sr5AfterRunSettlementCheckpointCas.From(fixture.Checkpoint),
            out _, out var blocker), blocker);
        string settlement = fixture.Backend.Payload;
        string owner = fixture.OwnerBackend.Payload;
        fixture.RewardBackend.Payload = "{}";
        Page route = await fixture.EnterAsync(fixture.EditorState);
        Require(route is Sr5AfterRunSettlementWizardPage && fixture.RewardBackend.Reads == 0
                && fixture.Backend.Payload == settlement && fixture.OwnerBackend.Payload == owner
                && fixture.ServiceCalls == 0,
            "Entry failed to retain existing Applying settlement recovery priority.");
        Require(await fixture.EnterAsync(fixture.EditorState, requestGovernedProposal: true) is Sr5AfterRunSettlementWizardPage
                && fixture.RewardBackend.Reads == 0 && fixture.Backend.Payload == settlement
                && fixture.OwnerBackend.Payload == owner && fixture.ServiceCalls == 0,
            "Explicit governed entry bypassed Applying settlement ownership.");
    }

    private sealed class PageFixture : IDisposable
    {
        public readonly MemoryBackend Backend = new();
        public readonly MemoryBackend OwnerBackend = new();
        public readonly RunnerSessionCoordinator Coordinator;
        public readonly Sr5AfterRunSettlementWizardPage Page;
        public readonly Sr5AfterRunSettlementCheckpointStore Store;
        public readonly Sr5AfterRunSettlementCheckpoint Checkpoint;
        public readonly Sr5AfterRunSettlementWizardDependencies Dependencies;
        public readonly Sr5AfterRunSettlementEditorState EditorState;
        public readonly PageRewardBackend RewardBackend = new();
        public readonly Sr5AfterRunRewardCheckpointStore RewardStore;
        public CharacterOverviewState State;
        public TaskCompletionSource<bool> Answer = NewAnswer();
        public int DialogCalls;
        public int DependencyReads;
        public int ServiceCalls;
        public string Prompt = string.Empty;

        public PageFixture(bool staleCatalog = true, bool manualEntry = false,
            IAndroidAfterRunProposalCatalog? manualAuthority = null)
        {
            long revision = staleCatalog ? 42 : 41;
            var baseState = Program.NewCreationOverview(WorkspaceId, revision, revision);
            State = baseState with
            {
                Profile = baseState.Profile! with { Created = true },
                Rules = new CharacterRulesSection("SR5", "", "", 0, 0, 0, 0, [])
            };
            var presenter = StrictPageProxy.Create<ICharacterOverviewPresenter>(() => State);
            RewardStore = new Sr5AfterRunRewardCheckpointStore(RewardBackend,
                new Sr5CareerMutationOwnerStore(OwnerBackend));
            Coordinator = new RunnerSessionCoordinator(presenter,
                null!, null!, null!, null!, null!, null!,
                StrictPageProxy.Create<IShellPresenter>(), null!, null!, null!, null!, null!,
                StrictPageProxy.Create<IAndroidAccountLinkService>(), null!, null!,
                afterRunProposalCatalog: manualAuthority ?? (manualEntry ? StrictPageProxy.Create<IManualEntryCatalog>(
                    unexpected: () => ServiceCalls++) : null),
                afterRunRewardService: StrictPageProxy.Create<ICharacterAfterRunRewardService>(
                    unexpected: () => ServiceCalls++),
                afterRunRewardCheckpoints: RewardStore);
            var owner = new TestOwner(OwnerId);
            var editor = Editor(Input());
            var oldAuthority = new Sr5AfterRunSettlementLiveCheckpointAuthority(owner, editor, () => Binding(41));
            var original = new Sr5AfterRunSettlementCheckpointStore(Backend, oldAuthority,
                new Sr5CareerMutationOwnerStore(OwnerBackend));
            Require(original.TryCreate(Sr5AfterRunSettlementCheckpoint.FromDraft(Draft(editor)),
                out Checkpoint, out var blocker), blocker);
            var missing = staleCatalog
                ? Sr5AfterRunSettlementEditorState.Unavailable(WorkspaceId, revision, "Catalog changed.")
                : editor;
            EditorState = missing;
            var nativePresenter = new RunnerSessionSr5AfterRunSettlementPresenter(Coordinator);
            var authority = new Sr5AfterRunSettlementLiveCheckpointAuthority(owner, missing,
                () => nativePresenter.Binding);
            Store = new Sr5AfterRunSettlementCheckpointStore(Backend, authority,
                new Sr5CareerMutationOwnerStore(OwnerBackend));
            Dependencies = new(new(nativePresenter, owner), Store, authority, owner);
            Page = new Sr5AfterRunSettlementWizardPage(Coordinator, missing, Dependencies,
                (_, prompt, _, _) => { DialogCalls++; Prompt = prompt; return Answer.Task; });
        }

        public Button Button(string id)
            => ((VerticalStackLayout)((ScrollView)Page.Content!).Content).Children
                .OfType<Button>().Single(button => button.AutomationId == id);

        public void Dispose() => Coordinator.Dispose();

        public Task<Page> EnterAsync(Sr5AfterRunSettlementEditorState editor, CancellationToken token = default,
            bool requestGovernedProposal = false)
            => Sr5AfterRunSettlementWizardPage.CreateEntryDestinationAsync(Coordinator, editor, token,
                () => { DependencyReads++; return Dependencies; }, requestGovernedProposal);
    }

    private sealed class PageRewardBackend : ISr5AfterRunRewardJournalBackend
    {
        public string Payload = string.Empty;
        public int Reads;
        public int Writes;
        public Action? OnRead;
        public string Read() { Interlocked.Increment(ref Reads); OnRead?.Invoke(); return Payload; }
        public void Write(string payload) { Writes++; Payload = payload; }
    }
}

// Test-only outer presenter/event adapters. Unexpected runtime calls throw;
// the actual native coordinator, page, action gate and journal are not replaced.
public interface IManualEntryCatalog : IAndroidAfterRunProposalCatalog, ISr5AfterRunManualProposalAuthority { }

public class StrictPageProxy : DispatchProxy
{
    private Func<object?>? _state;
    private Action? _unexpected;
    public static T Create<T>(Func<object?>? state = null, Action? unexpected = null) where T : class
    {
        var instance = Create<T, StrictPageProxy>();
        ((StrictPageProxy)(object)instance)._state = state;
        ((StrictPageProxy)(object)instance)._unexpected = unexpected;
        return instance;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string name = method?.Name ?? throw new InvalidOperationException("Missing proxy method.");
        if (name.StartsWith("add_", StringComparison.Ordinal)
            || name.StartsWith("remove_", StringComparison.Ordinal)) return null;
        if (name == "get_State" && _state is not null) return _state();
        _unexpected?.Invoke();
        throw new InvalidOperationException($"Unexpected page-test dependency call: {name}");
    }
}
