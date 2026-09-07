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
        Console.WriteLine("PASS 5 actual native/runtime/file-store integration cases");
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
        public CharacterWorkspaceId Id;

        public NativeRewardRuntime(string contentRoot, bool governedConsequences = false)
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
                var services = new ServiceCollection();
                services.AddChummerLocalRuntimeClient(contentRoot, contentRoot);
                services.Replace(ServiceDescriptor.Singleton<IWorkspaceStore>(new FileWorkspaceStore(StateDirectory)));
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
                _provider = services.BuildServiceProvider();
                Client = _provider.GetRequiredService<IChummerClient>();
                Require(Client is InProcessChummerClient, "Runtime integration must never use a network client.");
                Shell = new ShellPresenter(Client);
                Presenter = new CharacterOverviewPresenter(Client, shellPresenter: Shell);
                var store = _provider.GetRequiredService<IWorkspaceStore>();
                var checkpoints = governedConsequences ? Sr5AfterRunRewardCheckpointStore.CreateDefault(StateDirectory)
                    : new Sr5AfterRunRewardCheckpointStore(
                    new FileSr5AfterRunRewardJournalBackend(StateDirectory),
                    new Sr5CareerMutationOwnerStore(new MemoryBackend()));
                Coordinator = new RunnerSessionCoordinator(Presenter, Client, new WorkspaceOperationCoordinator(),
                    null!, null!, null!, null!, Shell,
                    _provider.GetRequiredService<IShellSurfaceResolver>(),
                    _provider.GetRequiredService<ICommandAvailabilityEvaluator>(),
                    null!, null!, null!, StrictPageProxy.Create<IAndroidAccountLinkService>(), null!, null!,
                    afterRunSettlementService: governedConsequences ? _provider.GetRequiredService<ICharacterAfterRunSettlementService>() : null,
                    afterRunProposalCatalog: governedConsequences ? _provider.GetRequiredService<Sr5AfterRunManualProposalSource>() : null,
                    afterRunRewardService: new WorkspaceCharacterAfterRunRewardService(store),
                    afterRunRewardCheckpoints: checkpoints);
            }
            catch
            {
                try { _provider?.Dispose(); }
                finally { RestoreHost(); }
                throw;
            }
        }

        public async Task LoadRunnerAsync()
        {
            var imported = await Client.ImportAsync(new WorkspaceImportDocument(
                """
                <character><name>Native reward runner</name><gameedition>SR5</gameedition>
                <metatype>Human</metatype><buildmethod>Priority</buildmethod>
                <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
                <created>True</created><karma>30</karma><nuyen>1000</nuyen>
                <streetcred>10</streetcred><notoriety>4</notoriety><publicawareness>6</publicawareness>
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
