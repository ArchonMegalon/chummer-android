using System.Globalization;
using System.Reflection;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunNativeRuntimeCasesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply an explicit Core directory containing data/.", nameof(contentRoot));
        await NativeRewardCommitReloadsRealPresenterAndShellAsync(contentRoot);
        await NativeRewardRefreshFailureRecoversWithoutCreditAsync(contentRoot, cancel: false);
        await NativeRewardRefreshFailureRecoversWithoutCreditAsync(contentRoot, cancel: true);
        await NativeRewardConsequencesContinuationIsReadOnlyAsync(contentRoot);
        await NativeRewardGovernedConsequencesPersistOnceAsync(contentRoot);
        await NativeRewardDowntimePlansWithoutRecreditAsync(contentRoot);
        Console.WriteLine("PASS 6 actual native/runtime/file-store integration cases");
        await NativeReputationContinuationPersistsAndReturnsAsync(contentRoot);
        await NativeReputationEntryLifecycleFencesAsync(contentRoot);
        await NativeReputationRefreshInterruptionRecoversAsync(contentRoot, cancel: false);
        await NativeReputationRefreshInterruptionRecoversAsync(contentRoot, cancel: true);
        await NativeReputationOverlappingAppearancesAsync(contentRoot);
        Console.WriteLine("PASS 5 actual reputation/native/runtime/file-store integration cases");
    }

    private static async Task NativeRewardDowntimePlansWithoutRecreditAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot);
        await runtime.LoadRunnerAsync();
        var reward = await runtime.PrepareRewardAsync();
        var page = new Sr5AfterRunRewardWizardPage(runtime.Coordinator, reward);
        var navigation = new NavigationPage(page);
        var view = (Sr5AfterRunRewardView)((ScrollView)page.Content!).Content;
        var button = ((VerticalStackLayout)view.Content!).Children.OfType<Button>()
            .SingleOrDefault(control => control.AutomationId == "sr5-reward-downtime");
        Require(button is not null, "Saved rewards have no ordinary local Downtime planning continuation.");
        Require(!button!.IsEnabled && !button.IsVisible, "An uncommitted reward enabled Downtime continuation.");
        await ExpectConsequencesRejectedAsync(() => InvokeTokenPageActionAsync(page, "OpenDowntimeAsync", default));
        Require(navigation.Navigation.NavigationStack.Count == 1, "Unconfirmed continuation changed navigation.");
        await reward.ConfirmAsync();
        view.Refresh();
        Require(button.IsEnabled && button.IsVisible && reward.CanContinue, "Saved reward did not enable local planning.");
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var rewarded = store.Get(runtime.Id).Value!;
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await ExpectConsequencesRejectedAsync(() => InvokeTokenPageActionAsync(page, "OpenDowntimeAsync", canceled.Token), canceled: true);
            Require(navigation.Navigation.NavigationStack.Count == 1, "Canceled continuation pushed a calendar.");
        }
        await NativeRewardDowntimeClickGateAsync(reward);
        var expiredEntry = new Sr5AfterRunRewardWizardPage(runtime.Coordinator, reward);
        var expiredNavigation = new NavigationPage(expiredEntry);
        await InvokeTokenPageActionAsync(expiredEntry, "OpenDowntimeAsync", default);
        var expiredCalendar = (Sr5DowntimeCalendarWizardPage)expiredNavigation.CurrentPage;
        await reward.RecoverAsync(); // Same revision, a separately verified handoff.
        await InvokeTokenPageActionAsync(expiredCalendar, "PrepareForAppearanceRefreshAsync", default);
        RefreshCalendar(expiredCalendar);
        Require(CalendarField<Sr5DowntimeCalendarPhoneLoadResult?>(expiredCalendar, "_load") is null
                && !PageButton(expiredCalendar, "sr5-downtime-calendar-review").IsEnabled,
            "A replaced reward handoff was silently renewed while Calendar attached.");
        int entryChecks = 0;
        var changedDuringProjection = new Sr5DowntimeCalendarWizardPage(runtime.Coordinator,
            new RunnerSessionSr5DowntimeCalendarAuthority(runtime.Coordinator),
            Sr5DowntimeCalendarJournalStore.CreateDefault(), entryStillCurrent: () => ++entryChecks == 1);
        await InvokeTokenPageActionAsync(changedDuringProjection, "PrepareForAppearanceRefreshAsync", default);
        RefreshCalendar(changedDuringProjection);
        Require(entryChecks == 2 && CalendarField<Sr5DowntimeCalendarPhoneLoadResult?>(changedDuringProjection, "_load") is null
                && !PageButton(changedDuringProjection, "sr5-downtime-calendar-review").IsEnabled,
            "A Calendar projection was accepted after its entry context expired.");
        RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);
        await InvokeTokenPageActionAsync(page, "OpenDowntimeAsync", default);
        Require(navigation.CurrentPage is Sr5DowntimeCalendarWizardPage, "Local continuation did not reuse the governed Calendar wizard.");
        var calendar = (Sr5DowntimeCalendarWizardPage)navigation.CurrentPage;
        await InvokeTokenPageActionAsync(calendar, "PrepareForAppearanceRefreshAsync", default);
        RefreshCalendar(calendar);
        Require(PageButton(calendar, "sr5-downtime-calendar-review").IsEnabled,
            "Real Calendar authority is unavailable for the freshly rewarded runner: "
            + CalendarField<Label>(calendar, "_binding").Text + " / " + CalendarField<Label>(calendar, "_status").Text);
        var load = CalendarField<Sr5DowntimeCalendarPhoneLoadResult>(calendar, "_load");
        Require(load.IsReady && load.Binding!.WorkspaceId == runtime.Id.Value
                && load.Binding.WorkspaceRevision == rewarded.SavedRevision && load.Editor!.Weeks.Count == 0,
            "Calendar did not bind the exact saved reward revision.");
        CalendarField<Entry>(calendar, "_year").Text = "2078";
        CalendarField<Entry>(calendar, "_week").Text = "12";
        await InvokePageActionAsync(calendar, "ReviewAsync");
        RefreshCalendar(calendar);
        var session = CalendarField<Sr5DowntimeCalendarDesktopSession>(calendar, "_session");
        Require(session.State.Preview is not null && !session.State.CanApply,
            "Calendar preview is missing or implicitly confirmed.");
        RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);
        await ExpectConsequencesRejectedAsync(() => InvokePageActionAsync(calendar, "ApplyAsync"));
        RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);
        // Managed test supplies the explicit user decision to the real session.
        // This does not exercise the native Android confirmation dialog.
        Require(session.TryConfirm(session.State.Preview!.PreviewDigest), "Exact Calendar preview could not be confirmed.");
        RefreshCalendar(calendar);
        Require(PageButton(calendar, "sr5-downtime-calendar-apply").IsEnabled, "Confirmed Calendar save is disabled.");
        await InvokePageActionAsync(calendar, "ApplyAsync");
        RefreshCalendar(calendar);
        var planned = store.Get(runtime.Id).Value!;
        var projected = await runtime.Coordinator.PrepareCareerCalendarEditAsync();
        Require(planned.ContentRevision == rewarded.ContentRevision + 1 && planned.SavedRevision == planned.ContentRevision
                && projected?.Weeks.Count == 1 && projected.Weeks[0].Year == 2078 && projected.Weeks[0].Week == 12,
            "Calendar did not save the user's chosen week exactly once.");
        var document = System.Xml.Linq.XDocument.Parse(planned.Document.Content).Root!;
        Require(document.Element("karma")?.Value == "38" && document.Element("nuyen")?.Value == "13500"
                && document.Element("contacts")?.Elements("contact").Count() == 0
                && planned.Document.AuxiliaryState.CharacterAfterRunRewardReceipts?.Count == 1
                && (planned.Document.AuxiliaryState.CharacterAfterRunSettlementReceipts?.Count ?? 0) == 0,
            "Local Downtime re-credited a reward or invented governed run consequences.");
        Require(PageButton(calendar, "sr5-downtime-calendar-clear-applied").IsVisible,
            "Successful Calendar save did not retain its verified receipt.");
        await ExpectConsequencesRejectedAsync(() => InvokePageActionAsync(calendar, "ApplyAsync"));
        RequireSameRewardDocument(planned, store.Get(runtime.Id).Value!);
        var reopened = new Sr5DowntimeCalendarWizardPage(runtime.Coordinator);
        await InvokeTokenPageActionAsync(reopened, "PrepareForAppearanceRefreshAsync", default);
        RefreshCalendar(reopened);
        Require(PageButton(reopened, "sr5-downtime-calendar-clear-applied").IsVisible
                && !PageButton(reopened, "sr5-downtime-calendar-apply").IsEnabled,
            "Reconstructed Calendar lost its receipt or restored save permission.");
        Require(!reward.CanContinue, "Calendar mutation left an old reward handoff current.");
        await ExpectConsequencesRejectedAsync(() => InvokeTokenPageActionAsync(page, "OpenDowntimeAsync", default));
        await reward.RecoverAsync();
        Require(reward.CanContinue && reward.Handoff?.Snapshot.AvailableKarma == 38
                && reward.Handoff.Snapshot.AvailableNuyen == 13500,
            "Reward recovery after Calendar save lost the current balance.");
        RequireSameRewardDocument(planned, store.Get(runtime.Id).Value!);
        Console.WriteLine("PASS actual saved reward → local Calendar review/confirmed save/reopen without a proposal or duplicate reward");
    }

    private static async Task NativeRewardDowntimeClickGateAsync(Sr5AfterRunRewardPhoneModel model)
    {
        Task pending = Task.CompletedTask;
        int plans = 0, proposals = 0;
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new Sr5AfterRunRewardView(model, action => pending = action(),
            () => throw new InvalidOperationException("Planning invoked Done."),
            _ => { proposals++; return Task.CompletedTask; },
            _ => { plans++; return response.Task; });
        var buttons = ((VerticalStackLayout)view.Content!).Children.OfType<Button>().ToArray();
        var plan = buttons.Single(button => button.AutomationId == "sr5-reward-downtime");
        var consequences = buttons.Single(button => button.AutomationId == "sr5-reward-consequences");
        ((IButtonController)plan).SendClicked();
        Task first = pending;
        Require(plans == 1 && !first.IsCompleted && !plan.IsEnabled && !consequences.IsEnabled,
            "Local planning did not retain the shared action gate while navigating.");
        ((IButtonController)plan).SendClicked();
        ((IButtonController)consequences).SendClicked();
        Require(plans == 1 && proposals == 0, "Overlapping clicks entered another After Run destination.");
        response.SetResult();
        await first;
        Require(plan.IsEnabled, "Planning remained disabled after navigation drained.");
        using var lifetime = new CancellationTokenSource();
        view.SetLifetime(lifetime.Token);
        lifetime.Cancel();
        ((IButtonController)plan).SendClicked();
        await pending;
        Require(plans == 1, "A departed reward page issued a Calendar navigation.");
    }

    private static T CalendarField<T>(Sr5DowntimeCalendarWizardPage page, string field)
        => (T)typeof(Sr5DowntimeCalendarWizardPage).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static void RefreshCalendar(Sr5DowntimeCalendarWizardPage page)
        => typeof(Sr5DowntimeCalendarWizardPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);

    private static Task InvokeTokenPageActionAsync(Page page, string name, CancellationToken token)
    {
        var method = page.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing actual page action: " + name);
        try { return ((Task)method.Invoke(page, [token])!).WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { return Task.FromException(exception.InnerException); }
    }

    private static async Task NativeRewardGovernedConsequencesPersistOnceAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot, governedConsequences: true);
        await runtime.LoadRunnerAsync();
        var reward = await runtime.PrepareRewardAsync();
        await reward.ConfirmAsync();
        Require(reward.CanContinue, "Real reward did not reach the saved handoff.");
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var rewarded = store.Get(runtime.Id).Value!;
        // Explicit synthetic actor/review fixtures, not an authenticated live GM.
        // The production publisher and Core still validate every submitted field.
        var submission = ManualSubmission() with
        {
            WorkspaceId = runtime.Id,
            ExpectedWorkspaceRevision = rewarded.SavedRevision,
            RewardReceiptDigest = reward.Checkpoint!.Receipt!.ReceiptDigest
        };
        var rejected = await runtime.Coordinator.PublishManualAfterRunProposalAsync(submission with { GmApproved = false });
        Require(!rejected.Published, "A manual proposal without the separate GM review was accepted.");
        var published = await runtime.Coordinator.PublishManualAfterRunProposalAsync(submission);
        Require(published.Published && published.Proposal?.IsExact() == true, published.Blocker);
        var coldSource = new Sr5AfterRunManualProposalSource(new AndroidAfterRunWorkspaceSnapshotSource(store),
            new FileSr5AfterRunManualProposalBackend(runtime.StateDirectory));
        Require(coldSource.Load(runtime.Id).Entries.Single().Identity == submission.Identity,
            "The file-backed proposal did not survive reconstruction.");
        RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);

        var rewardPage = new Sr5AfterRunRewardWizardPage(runtime.Coordinator, reward);
        var editor = await rewardPage.PrepareConsequencesAsync(default);
        Require(editor.Status == Sr5AfterRunCatalogStatus.Available && editor.Candidates.Count == 1
                && editor.Candidates[0].Quote.CanSettle && editor.Candidates[0].Quote.KarmaBefore == 38,
            "The real Core service did not quote the registered proposal against the rewarded character.");
        var navigation = new NavigationPage(rewardPage);
        await rewardPage.OpenConsequencesAsync(default);
        Require(navigation.CurrentPage is Sr5AfterRunSettlementWizardPage, "No governed proposal page.");
        Require(PageButton(navigation.CurrentPage, "sr5-after-run-open-rewards").IsEnabled,
            "The exact available proposal cannot be selected.");
        await InvokePageActionAsync(navigation.CurrentPage, "OpenRewardsAsync");
        bool claimedSigned = DescendantLabels(((ScrollView)((ContentPage)navigation.CurrentPage).Content!).Content)
            .Any(label => label.Text?.Contains("signed run context", StringComparison.Ordinal) == true);
        foreach (string stage in new[] { "rewards", "consequences", "contacts", "gm", "owner" })
        {
            Require(navigation.CurrentPage is Sr5AfterRunSettlementStagePage,
                "Missing native review stage: " + stage);
            var button = ((VerticalStackLayout)((ScrollView)((ContentPage)navigation.CurrentPage).Content!).Content).Children
                .OfType<Button>().Single(control => control.AutomationId?.StartsWith("sr5-after-run-acknowledge-", StringComparison.Ordinal) == true);
            Require(button.IsEnabled, "Exact governed review is disabled: " + stage);
            RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);
            await InvokePageActionAsync(navigation.CurrentPage, "ContinueAsync");
        }
        Require(navigation.CurrentPage is Sr5AfterRunSettlementReviewPage, "The six acknowledgements did not lead to final review.");
        var reviewPage = navigation.CurrentPage;
        var draft = (Sr5AfterRunSettlementDraft)typeof(Sr5AfterRunSettlementReviewPage)
            .GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reviewPage)!;
        Require(draft.Acknowledgements.AllReviewed && draft.IsExact(), "Final review lost typed acknowledgements or plan identity.");
        Require(PageButton(reviewPage, "sr5-after-run-confirm").IsEnabled, "Exact final review is not confirmable.");
        RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);
        await InvokePageActionAsync(reviewPage, "ApplyAsync");
        Require(navigation.CurrentPage is Sr5AfterRunSettlementReceiptPage, "Atomic settlement did not reach its receipt page.");
        var settled = store.Get(runtime.Id).Value!;
        Require(settled.ContentRevision == rewarded.ContentRevision + 1 && settled.SavedRevision == settled.ContentRevision
                && runtime.Presenter.State.SavedRevision == settled.SavedRevision,
            "Settlement and native reload did not agree on one saved revision.");
        var document = System.Xml.Linq.XDocument.Parse(settled.Document.Content).Root!;
        Require(document.Element("nuyen")?.Value == "13500"
                && document.Element("karma")?.Value == draft.Plan.TargetKarma.ToString(CultureInfo.InvariantCulture)
                && draft.Plan.ContactKarmaCost == 11 && draft.Plan.TargetKarma == 27,
            "Consequences credited the reward again or lost the real Core contact cost.");
        Require(document.Element("contacts")?.Elements("contact").Count() == 2
                && settled.Document.AuxiliaryState.CharacterAfterRunRewardReceipts?.Count == 1
                && settled.Document.AuxiliaryState.CharacterAfterRunSettlementReceipts?.Count == 1,
            "Atomic settlement duplicated contacts or changed the independent reward receipt count.");
        var command = draft.ToCommand();
        var replay = await runtime.Coordinator.SettleAfterRunAsync(command);
        Require(replay?.Outcome == CharacterAfterRunSettlementServiceOutcome.Replayed,
            "The actual native/Core duplicate did not resolve to its original receipt.");
        var coldService = new CharacterAfterRunSettlementService(new WorkspaceCharacterAfterRunSettlementWorkspace(
            new FileWorkspaceStore(runtime.StateDirectory), coldSource));
        Require(coldService.Settle(command).Outcome == CharacterAfterRunSettlementServiceOutcome.Replayed,
            "Cold Core lookup did not recover the exact recorded settlement.");
        RequireSameRewardDocument(settled, store.Get(runtime.Id).Value!);
        Require(!reward.CanContinue, "The old reward handoff survived a later settlement revision.");
        await reward.RecoverAsync();
        Require(reward.CanContinue && reward.Handoff?.Snapshot.AvailableKarma == 27
                && reward.Handoff.Snapshot.AvailableNuyen == 13500
                && reward.Checkpoint?.Receipt?.ReceiptDigest == submission.RewardReceiptDigest,
            "Returning to the reward confused its historical receipt with current post-settlement balances.");
        RequireSameRewardDocument(settled, store.Get(runtime.Id).Value!);
        Require(!claimedSigned, "A manually entered proposal was presented as a signed run context.");
        Console.WriteLine("PASS actual manual proposal, Core quote, native review stages, atomic settlement and duplicate/cold receipt recovery");
    }

    private static Button PageButton(Page page, string automationId)
        => ((VerticalStackLayout)((ScrollView)((ContentPage)page).Content!).Content).Children
            .OfType<Button>().Single(button => button.AutomationId == automationId);

    private static async Task InvokePageActionAsync(Page page, string name)
    {
        var method = page.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Missing actual page action: " + name);
        await ((Task)method.Invoke(page, null)!).WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static IEnumerable<Label> DescendantLabels(IView view)
    {
        if (view is Label label) yield return label;
        if (view is Layout layout)
            foreach (var child in layout.Children)
                foreach (var nested in DescendantLabels(child)) yield return nested;
        if (view is ContentView { Content: { } content })
            foreach (var nested in DescendantLabels(content)) yield return nested;
    }

    private static async Task NativeRewardConsequencesContinuationIsReadOnlyAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot);
        await runtime.LoadRunnerAsync();
        var model = await runtime.PrepareRewardAsync();
        var page = new Sr5AfterRunRewardWizardPage(runtime.Coordinator, model);
        var view = (Sr5AfterRunRewardView)((ScrollView)page.Content!).Content;
        var button = ((VerticalStackLayout)view.Content!).Children.OfType<Button>()
            .SingleOrDefault(control => control.AutomationId == "sr5-reward-consequences");
        Require(button is not null, "The saved local reward has no continuation to independently reviewed run consequences.");
        Require(!button!.IsEnabled, "Uncommitted reward enabled run-consequence navigation.");
        int queries = 0;
        var unconfirmed = new Sr5AfterRunRewardWizardPage(runtime.Coordinator, model, token =>
        {
            queries++;
            return runtime.Coordinator.PrepareAfterRunSettlementAsync(token);
        });
        await ExpectConsequencesRejectedAsync(() => unconfirmed.PrepareConsequencesAsync(default));
        Require(queries == 0, "Unconfirmed reward queried governed proposals.");
        await model.ConfirmAsync();
        view.Refresh();
        Require(model.CanContinue && button.IsEnabled && button.IsVisible,
            "Fresh saved reward did not enable the explicit read-only consequence query.");
        var stored = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var editor = await page.PrepareConsequencesAsync(default);
        Require(editor.WorkspaceId == runtime.Id && editor.WorkspaceRevision == stored.SavedRevision
                && editor.Status == Sr5AfterRunCatalogStatus.Unavailable && editor.Candidates.Count == 0,
            "Missing governed proposal authority was replaced by a fabricated run or consequence quote.");
        var navigation = new NavigationPage(page);
        await page.OpenConsequencesAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
        Require(navigation.Navigation.NavigationStack.Count == 2
                && navigation.CurrentPage is Sr5AfterRunSettlementWizardPage,
            "Read-only continuation did not reach the existing governed proposal page.");
        RequireSameRewardDocument(stored, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        await NativeRewardConsequenceClickGateAsync(model);
        foreach (var invalid in new[]
        {
            editor with { WorkspaceId = new("another-runner") },
            editor with { WorkspaceRevision = stored.SavedRevision - 1 },
            editor with { Status = Sr5AfterRunCatalogStatus.Available },
            editor with { Blockers = null! },
            null!
        })
        {
            var hostile = new Sr5AfterRunRewardWizardPage(runtime.Coordinator, model,
                _ => Task.FromResult(invalid));
            await ExpectConsequencesRejectedAsync(() => hostile.PrepareConsequencesAsync(default));
        }

        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await ExpectConsequencesRejectedAsync(() => unconfirmed.PrepareConsequencesAsync(canceled.Token), canceled: true);
            Require(queries == 0, "Canceled entry queried governed proposals.");
        }

        foreach (string change in new[] { "cancellation", "handoff-replaced", "selection-roundtrip" })
        {
            var response = new TaskCompletionSource<Sr5AfterRunSettlementEditorState>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var lifetime = new CancellationTokenSource();
            var delayed = new Sr5AfterRunRewardWizardPage(runtime.Coordinator, model, _ => response.Task);
            var delayedNavigation = new NavigationPage(delayed);
            var originalHandoff = model.Handoff;
            Task pending = delayed.OpenConsequencesAsync(lifetime.Token);
            Require(!pending.IsCompleted, "Consequence query did not reach its controlled read boundary.");
            if (change == "cancellation") lifetime.Cancel();
            else if (change == "handoff-replaced")
            {
                await model.RecoverAsync();
                Require(model.CanContinue && !ReferenceEquals(originalHandoff, model.Handoff),
                    "Fixture failed to replace a handoff via real read-only recovery.");
            }
            else
            {
                var originalId = runtime.Id;
                await runtime.LoadRunnerAsync();
                await runtime.Presenter.LoadAsync(originalId, default);
                runtime.Id = originalId;
                Require(!model.CanContinue, "A→B→A did not invalidate the original reward selection generation.");
            }
            response.SetResult(editor);
            await ExpectConsequencesRejectedAsync(() => pending, canceled: change == "cancellation");
            Require(delayedNavigation.Navigation.NavigationStack.Count == 1
                    && ReferenceEquals(delayedNavigation.CurrentPage, delayed),
                "Rejected consequence response still pushed a destination page.");
            RequireSameRewardDocument(stored, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        }
        Console.WriteLine("PASS native reward consequence entry preserves saved reward and reports missing proposal authority");
    }

    private static async Task NativeRewardConsequenceClickGateAsync(Sr5AfterRunRewardPhoneModel model)
    {
        Task pending = Task.CompletedTask;
        int queries = 0;
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new Sr5AfterRunRewardView(model, action => pending = action(),
            () => throw new InvalidOperationException("Consequence click invoked Done."),
            _ => { queries++; return response.Task; });
        var button = ((VerticalStackLayout)view.Content!).Children.OfType<Button>()
            .Single(control => control.AutomationId == "sr5-reward-consequences");
        ((IButtonController)button).SendClicked();
        Task first = pending;
        Require(!first.IsCompleted && queries == 1 && !button.IsEnabled,
            "Consequence button did not disable during its awaited query.");
        ((IButtonController)button).SendClicked();
        Require(queries == 1, "A double tap issued another consequence query.");
        response.SetResult();
        await first;
        Require(button.IsEnabled, "Consequence button remained busy after query completion.");
        using var lifetime = new CancellationTokenSource();
        view.SetLifetime(lifetime.Token);
        lifetime.Cancel();
        ((IButtonController)button).SendClicked();
        await pending;
        Require(queries == 1, "A departed page click issued a consequence query.");
    }

    private static async Task ExpectConsequencesRejectedAsync(Func<Task> action, bool canceled = false)
    {
        try { await action(); }
        catch (OperationCanceledException) when (canceled) { return; }
        catch (InvalidOperationException) when (!canceled) { return; }
        throw new InvalidOperationException("Stale, unconfirmed or canceled reward accepted consequence navigation.");
    }

    private static async Task NativeRewardCommitReloadsRealPresenterAndShellAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot);
        await runtime.LoadRunnerAsync();
        var initialReward = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(runtime.StateDirectory))
            .Read(runtime.Id);
        Require(initialReward.Outcome == CharacterAfterRunRewardOutcome.Available,
            $"Imported canonical runtime document is unavailable to Core rewards: {initialReward.Outcome}; {initialReward.Error}");
        var model = await runtime.PrepareRewardAsync();
        await model.ConfirmAsync();
        Require(model.CanContinue && model.Handoff is not null,
            $"Real native presenter/shell reload did not produce a handoff: {model.Status}; {runtime.Presenter.State.Error}");
        Require(runtime.Coordinator.State.ContentRevision == 2 && runtime.Coordinator.State.SavedRevision == 2
                && runtime.Presenter.State.Progress?.Karma == 38,
            "Actual presenter did not load the committed saved revision and Karma.");
        Require(model.Handoff!.Snapshot.AvailableKarma == 38 && model.Handoff.Snapshot.AvailableNuyen == 13500,
            "Fresh Core balances differ from the committed reward.");
        Require(runtime.Shell.State.ActiveWorkspaceId == runtime.Id,
            "Actual shell did not follow the reloaded runner.");
        var cold = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(runtime.StateDirectory));
        Require(cold.Read(runtime.Id).Snapshot?.AvailableKarma == 38
                && cold.Lookup(runtime.Id, model.OperationId, model.Checkpoint!.CommandDigest).Receipt is not null,
            "Cold Core file-store read did not find the exact committed reward.");
        var beforeResume = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var reopened = runtime.Coordinator.CreateAfterRunRewardModel(DateTime.Now, runtime.Owner);
        await reopened.InitializeAsync();
        Require(reopened.RecordedRewards.Any(r => r.Command.OperationId == model.OperationId),
            "A new native model could not discover the recorded reward.");
        await reopened.ResumeRecordedRewardAsync(model.OperationId);
        Require(reopened.CanContinue && reopened.OperationId == model.OperationId
                && reopened.Checkpoint!.CommandDigest == model.Checkpoint!.CommandDigest,
            "Explicit history resume did not retain the exact original command.");
        RequireSameRewardDocument(beforeResume, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        Console.WriteLine("PASS native reward commit, actual presenter/shell reload and recorded-history resume");
    }

    private static async Task NativeRewardRefreshFailureRecoversWithoutCreditAsync(string contentRoot, bool cancel)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot);
        await runtime.LoadRunnerAsync();
        var model = await runtime.PrepareRewardAsync();
        using var cancellation = new CancellationTokenSource();
        int interruptions = 0;
        EventHandler observer = (_, _) =>
        {
            if (cancel && runtime.Presenter.State.IsBusy && interruptions == 0)
            {
                interruptions++;
                cancellation.Cancel();
            }
        };
        runtime.Presenter.StateChanged += observer;
        runtime.Settings.FailSelectedWorkspaceWrite = !cancel;
        try { await model.ConfirmAsync(cancellation.Token); }
        finally
        {
            runtime.Presenter.StateChanged -= observer;
            runtime.Settings.FailSelectedWorkspaceWrite = false;
        }
        Require(cancel ? interruptions == 1 : runtime.Settings.RejectedWrites == 1,
            "The real native refresh did not reach its controlled interruption boundary.");
        Require(model.Status == Sr5AfterRunRewardPhoneStatus.RefreshRequired && model.Handoff is null
                && model.Checkpoint?.Phase == Sr5AfterRunRewardCheckpointPhase.Applied
                && model.CanRecover && !model.CanRetry && !model.CanConfirm,
            $"Interrupted post-commit refresh lost recovery safety: {model.Status}; {runtime.Presenter.State.Error}");
        var cold = new FileWorkspaceStore(runtime.StateDirectory);
        var committed = cold.Get(runtime.Id).Value!;
        Require(committed.ContentRevision == 2 && committed.SavedRevision == 2
                && committed.Document.AuxiliaryState.CharacterAfterRunRewardReceipts?.Count == 1,
            "The reward was not durably committed before refresh was interrupted.");
        string digest = model.Checkpoint!.CommandDigest;
        Guid operationId = model.OperationId;
        await model.RecoverAsync();
        Require(model.CanContinue && model.Status == Sr5AfterRunRewardPhoneStatus.Recorded
                && runtime.Presenter.State.Error is null && runtime.Presenter.State.Progress?.Karma == 38
                && runtime.Presenter.State.SavedRevision == 2 && runtime.Shell.State.ActiveWorkspaceId == runtime.Id
                && model.OperationId == operationId && model.Checkpoint!.CommandDigest == digest,
            $"Real reload/lookup did not recover the committed reward: {model.Status}; {runtime.Presenter.State.Error}");
        RequireSameRewardDocument(committed, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        Console.WriteLine($"PASS native reward refresh {(cancel ? "cancellation" : "preferences failure")} and exact read-only recovery");
    }

    private static void RequireSameRewardDocument(WorkspaceStoredDocument before, WorkspaceStoredDocument after)
    {
        Require(before.ContentRevision == after.ContentRevision && before.SavedRevision == after.SavedRevision
                && before.Document.Content == after.Document.Content
                && before.Document.AuxiliaryStateDigest == after.Document.AuxiliaryStateDigest,
            "Native refresh/recovery changed the persisted character or reward ledger.");
    }

    private sealed class NativeRewardRuntime : IAsyncDisposable
    {
        private readonly Dictionary<string, string?> _environment = new();
        private readonly IPreferences _priorPreferences;
        private readonly MethodInfo _setPreferences;
        private readonly ServiceProvider _provider;
        public string StateDirectory { get; }
        public readonly TestOwner Owner = new(OwnerId);
        public readonly RuntimePreferences Settings = new();
        public readonly IChummerClient Client;
        public readonly CharacterOverviewPresenter Presenter;
        public readonly ShellPresenter Shell;
        public readonly RunnerSessionCoordinator Coordinator;
        public ICharacterCareerReputationService ReputationService => _provider.GetRequiredService<ICharacterCareerReputationService>();
        public CharacterWorkspaceId Id;

        public NativeRewardRuntime(string contentRoot, bool governedConsequences = false, bool reputation = false,
            Func<ICharacterCareerReputationService, ICharacterCareerReputationService>? reputationDecorator = null,
            Action<string>? creationSkillsSeed = null,
            Func<ICharacterCreationSkillsService, ICharacterCreationSkillsService>? skillsDecorator = null,
            IAndroidLinkedCharacterFileService? linkedCharacters = null)
        {
            _priorPreferences = Preferences.Default;
            _setPreferences = typeof(Preferences).GetMethod("SetDefault",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("MAUI Preferences test adapter entry is unavailable.");
            StateDirectory = Directory.CreateTempSubdirectory("chummer-native-reward-runtime-").FullName;
            try
            {
                _setPreferences.Invoke(null, [Settings]);
                SetEnvironment("CHUMMER_STATE_PATH", StateDirectory);
                SetEnvironment("CHUMMER_WORKSPACE_STORE_PATH", StateDirectory);
                SetEnvironment("CHUMMER_CLIENT_MODE", "local");
                SetEnvironment("CHUMMER_DESKTOP_CLIENT_MODE", "local");
                creationSkillsSeed?.Invoke(StateDirectory);
                var services = new ServiceCollection();
                services.AddChummerLocalRuntimeClient(contentRoot, contentRoot);
                services.Replace(ServiceDescriptor.Singleton<IWorkspaceStore>(new FileWorkspaceStore(StateDirectory)));
                if (reputation)
                {
                    Settings.Set("sr5.career.owner.v1", OwnerId.ToString("D"));
                    services.AddSingleton(provider => Sr5CareerReputationJournal.CreateDefault(StateDirectory,
                        provider.GetRequiredService<ICharacterCareerReputationService>()));
                }
                if (governedConsequences)
                {
                    Settings.Set("sr5.career.owner.v1", OwnerId.ToString("D"));
                    services.AddSingleton(provider => new Sr5AfterRunManualProposalSource(
                        new AndroidAfterRunWorkspaceSnapshotSource(provider.GetRequiredService<IWorkspaceStore>()),
                        new FileSr5AfterRunManualProposalBackend(StateDirectory)));
                    services.Replace(ServiceDescriptor.Singleton<ICharacterAfterRunSettlementProposalProjectionSource>(
                        provider => provider.GetRequiredService<Sr5AfterRunManualProposalSource>()));
                }
                services.AddSingleton<ICommandAvailabilityEvaluator, DefaultCommandAvailabilityEvaluator>();
                services.AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();
                // Match MauiProgram: presenter and native host share one real
                // session coordinator. Separate instances leave native reads
                // without an active workspace and must fail closed.
                services.AddSingleton<IWorkspaceOperationCoordinator, WorkspaceOperationCoordinator>();
                _provider = services.BuildServiceProvider();
                Client = _provider.GetRequiredService<IChummerClient>();
                Require(Client is InProcessChummerClient, "Runtime integration must never use a network client.");
                Shell = new ShellPresenter(Client);
                var operations = _provider.GetRequiredService<IWorkspaceOperationCoordinator>();
                Presenter = new CharacterOverviewPresenter(Client, shellPresenter: Shell,
                    workspaceOperationCoordinator: operations);
                var store = _provider.GetRequiredService<IWorkspaceStore>();
                var reputationService = reputation ? _provider.GetRequiredService<ICharacterCareerReputationService>() : null;
                if (reputationService is not null && reputationDecorator is not null)
                    reputationService = reputationDecorator(reputationService);
                var checkpoints = governedConsequences || reputation ? Sr5AfterRunRewardCheckpointStore.CreateDefault(StateDirectory)
                    : new Sr5AfterRunRewardCheckpointStore(
                    new FileSr5AfterRunRewardJournalBackend(StateDirectory),
                    new Sr5CareerMutationOwnerStore(new MemoryBackend()));
                Coordinator = new RunnerSessionCoordinator(Presenter, Client, operations,
                    null!, null!, null!, null!, Shell,
                    _provider.GetRequiredService<IShellSurfaceResolver>(),
                    _provider.GetRequiredService<ICommandAvailabilityEvaluator>(),
                    null!, linkedCharacters!, null!, StrictPageProxy.Create<IAndroidAccountLinkService>(), null!, null!,
                    afterRunSettlementService: governedConsequences ? _provider.GetRequiredService<ICharacterAfterRunSettlementService>() : null,
                    afterRunProposalCatalog: governedConsequences ? _provider.GetRequiredService<Sr5AfterRunManualProposalSource>() : null,
                    afterRunRewardService: new WorkspaceCharacterAfterRunRewardService(store),
                    afterRunRewardCheckpoints: checkpoints,
                    creationSkillsService: creationSkillsSeed is null ? null : skillsDecorator is null
                        ? _provider.GetRequiredService<ICharacterCreationSkillsService>()
                        : skillsDecorator(_provider.GetRequiredService<ICharacterCreationSkillsService>()),
                    careerReputationService: reputationService,
                    careerReputationJournal: reputation ? _provider.GetRequiredService<Sr5CareerReputationJournal>() : null);
            }
            catch
            {
                try { _provider?.Dispose(); }
                finally { RestoreHost(); }
                throw;
            }
        }

        public async Task LoadRunnerAsync(string? runnerXml = null)
        {
            var imported = await Client.ImportAsync(new WorkspaceImportDocument(
                runnerXml ?? """
                <character><name>Native reward runner</name><gameedition>SR5</gameedition>
                <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings>
                <metatype>Human</metatype><buildmethod>Priority</buildmethod>
                <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
                <created>True</created><karma>30</karma><nuyen>1000</nuyen>
                <streetcred>10</streetcred><notoriety>4</notoriety><publicawareness>6</publicawareness>
                <burntstreetcred>0</burntstreetcred><improvements/>
                <contacts/><expenses/><notes>Retain unrelated data</notes></character>
                """, "sr5"), default);
            Id = imported.Id;
            Require((await Client.SaveAsync(Id, default)).Success, "Imported runtime runner could not be saved.");
            var validation = await Client.ValidateAsync(Id, default);
            Require(validation.IsValid, "Runtime fixture failed canonical validation: "
                + System.Text.Json.JsonSerializer.Serialize(validation.Issues));
            await Presenter.LoadAsync(Id, default);
            Require(Presenter.State.WorkspaceId == Id && Presenter.State.ContentRevision == 1
                    && Presenter.State.SavedRevision == 1 && Presenter.State.Profile?.Created == true
                    && Presenter.State.Rules?.GameEdition == "SR5" && Presenter.State.Error is null,
                $"Actual presenter failed to load initial saved SR5 runner: {Presenter.State.Error}");
        }

        public async Task<Sr5AfterRunRewardPhoneModel> PrepareRewardAsync()
        {
            var model = Coordinator.CreateAfterRunRewardModel(new DateTime(2026, 9, 7, 12, 0, 0), Owner);
            await model.InitializeAsync();
            Require(model.UpdateDraft(model.Draft with { Karma = "8", Nuyen = "12500", Reason = "Native integration run" }),
                "The current native reward draft was not editable.");
            await model.PreviewAsync(CultureInfo.InvariantCulture);
            Require(model.CanConfirm, $"Real runtime preview unavailable: {model.Status}");
            return model;
        }

        private void SetEnvironment(string name, string? value)
        {
            _environment.Add(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }

        private void RestoreHost()
        {
            try { _setPreferences.Invoke(null, [_priorPreferences]); }
            finally
            {
                foreach (var pair in _environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                Directory.Delete(StateDirectory, recursive: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Coordinator.Dispose();
                await Presenter.DisposeAsync();
            }
            finally
            {
                try { await _provider.DisposeAsync(); }
                finally { RestoreHost(); }
            }
        }
    }

    private sealed class RuntimePreferences : IPreferences
    {
        private readonly Dictionary<(string?, string), object> _values = new();
        public bool FailSelectedWorkspaceWrite { get; set; }
        public int RejectedWrites { get; private set; }
        public bool ContainsKey(string key, string? sharedName = null)
        { lock (_values) return _values.ContainsKey((sharedName, key)); }
        public void Remove(string key, string? sharedName = null)
        { lock (_values) _values.Remove((sharedName, key)); }
        public void Clear(string? sharedName = null)
        { lock (_values) foreach (var key in _values.Keys.Where(k => k.Item1 == sharedName).ToArray()) _values.Remove(key); }
        public void Set<T>(string key, T value, string? sharedName = null)
        {
            lock (_values)
            {
                if (FailSelectedWorkspaceWrite && key == "chummer.android.selected-workspace.v1")
                {
                    RejectedWrites++;
                    throw new IOException("Controlled selected-workspace preference failure after reward commit.");
                }
                _values[(sharedName, key)] = value!;
            }
        }
        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        { lock (_values) return _values.TryGetValue((sharedName, key), out var value) ? (T)value : defaultValue; }
    }
}
