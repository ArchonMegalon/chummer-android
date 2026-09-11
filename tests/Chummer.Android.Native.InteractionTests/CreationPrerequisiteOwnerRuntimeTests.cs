using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunCreationPrerequisiteOwnerCasesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var failures = new List<string>();
            string[] cases = ["local", "linked", "issued-b", "issued-aba", "same-owner-reload",
                "queued-confirm-b", "queued-confirm-aba", "queued-load-b", "queued-preview-b",
                "missing-service", "cancel-before-dispatch", "postcommit-cancel", "postcommit-load-fault",
                "postcommit-owner-b", "postcommit-observer", "entered-unknown",
                "page-same", "page-departure", "page-old-render", "page-queued-departure", "page-load-fault",
                "page-back-departure", "page-back-old-render", "page-back-owner-b", "page-back-owner-aba"];
            foreach (string scenario in cases)
            {
                try
                {
                    await RunPrerequisiteOwnerCaseAsync(contentRoot, ui, scenario);
                    ui.AssertHealthy();
                    Console.WriteLine("PASS actual native prerequisite: " + scenario);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Console.WriteLine("PREREQUISITE_FAILURE " + scenario + "\n" + error);
                    // Missing setup or a live/unjoined callback cannot be used as an owner diagnosis.
                    if (error is IssuedPageUnjoinedException || error.ToString().Contains("SETUP:", StringComparison.Ordinal)) throw;
                    failures.Add(scenario + ": " + error.Message);
                }
            }
            Require(failures.Count == 0, "Native prerequisite regressions:\n" + string.Join("\n", failures));
            Console.WriteLine("PASS actual native prerequisite owner/dispatch cases: " + cases.Length);
        });
    }

    private static async Task RunPrerequisiteOwnerCaseAsync(string contentRoot, IssuedPageUiContext ui, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        PrerequisiteCoreProbe? probe = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationPrerequisite: true,
            prerequisiteDecorator: actual =>
            {
                probe = new(actual, owners, ui);
                return scenario == "missing-service" ? null : probe;
            });
        await runtime.Coordinator.InitializeAsync();
        await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
        WorkspaceStoredDocument seed = PreparePrerequisiteOwnerFixture(runtime);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, seed);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerB, seed);
        if (scenario != "local") owners.Set(ContactsOwnerA);
        await HydrateFinalizationOwnerAsync(runtime, owners, seed);
        OwnerContextStamp originalOwner = owners.Capture();
        var originalDisplay = runtime.Coordinator.State;
        var before = PrerequisiteColdRows(runtime);
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        CreationPrerequisitePhoneConfirmResult? result = null;
        CharacterCreationPrerequisitePreview? preview = null;
        Task? liveOperation = null;
        bool observerFault = false;
        int observerFaultCalls = 0;
        EventHandler changed = (_, _) =>
        {
            // Presenter/Shell notifications are thread-neutral. NativePageBase
            // dispatches actual rendering; observe that control boundary below.
            if (observerFault && probe!.SuccessfulConfirms > 0)
            {
                Interlocked.Increment(ref observerFaultCalls);
                throw new InvalidOperationException("diagnostic postcommit observer fault");
            }
        };
        runtime.Coordinator.Changed += changed;
        try
        {
            if (scenario == "queued-load-b")
            {
                await gate.WaitAsync();
                Task<CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>> pending;
                try
                {
                    pending = runtime.Coordinator.LoadCreationPrerequisiteAsync();
                    liveOperation = pending;
                    Require(!pending.IsCompleted, "SETUP: Load did not wait at the real activation gate.");
                    await ReplaceOwnerAsync();
                }
                finally { gate.Release(); }
                var rejected = await pending;
                liveOperation = null;
                Require(rejected.Value is null && probe!.Calls.Count == 0,
                    "Queued Load recaptured a replacement owner or entered Core.");
                return;
            }

            var loaded = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
            if (scenario == "missing-service")
            {
                Require(loaded.Value is null && loaded.Blockers.Contains(CharacterCreationPrerequisiteBlockers.WorkspaceUnavailable)
                    && probe!.Calls.Count == 0, "Missing bound adapter fell back to ambient Core.");
                return;
            }
            Require(loaded.Value is { } && CreationPrerequisitePhoneAuthority.IsReady(loaded.Value, runtime.Coordinator.State)
                    && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(loaded.Value),
                "SETUP: actual Bootstrap/Load did not issue an actionable Priority prerequisite: " + JsonSerializer.Serialize(loaded));
            var state = loaded.Value!;
            var (assignments, selections) = PrerequisiteSelections(state);

            if (scenario == "queued-preview-b")
            {
                await gate.WaitAsync();
                Task<CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>> pending;
                try
                {
                    pending = runtime.Coordinator.PreviewCreationPrerequisiteAsync(state.Binding, assignments, selections);
                    liveOperation = pending;
                    Require(!pending.IsCompleted, "SETUP: Preview did not wait at the real activation gate.");
                    await ReplaceOwnerAsync();
                }
                finally { gate.Release(); }
                Require((await pending).Value is null && probe!.Calls.SequenceEqual(["load"]),
                    "Queued Preview borrowed a replacement owner.");
                liveOperation = null;
                return;
            }

            // Byte-identical cloned DTOs are not an issuance. No Core call may be made.
            int beforeForged = probe!.Calls.Count;
            Require((await runtime.Coordinator.PreviewCreationPrerequisiteAsync(state.Binding with { }, assignments, selections)).Value is null
                && probe.Calls.Count == beforeForged, "A copied binding acquired native issuance.");

            CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> reviewed;
            if (scenario is "local" or "linked")
            {
                // Copy happens before queuing; mutate the caller's dictionary/list while blocked.
                var mutableAssignments = new Dictionary<string, string>(assignments, StringComparer.Ordinal);
                var mutableSkills = new List<string>();
                var mutableGroups = new List<string>();
                var mutableSelections = selections with
                    { TalentActiveSkillSelectionIds = mutableSkills, TalentSkillGroupSelectionIds = mutableGroups };
                await gate.WaitAsync();
                Task<CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>> pending;
                try
                {
                    pending = runtime.Coordinator.PreviewCreationPrerequisiteAsync(state.Binding, mutableAssignments, mutableSelections);
                    liveOperation = pending;
                    Require(!pending.IsCompleted, "SETUP: copied input Preview never queued.");
                    mutableAssignments.Clear(); mutableSkills.Add("not-a-core-choice"); mutableGroups.Add("not-a-core-group");
                }
                finally { gate.Release(); }
                reviewed = await pending;
                liveOperation = null;
            }
            else reviewed = await runtime.Coordinator.PreviewCreationPrerequisiteAsync(state.Binding, assignments, selections);
            Require(reviewed.Value is { CanConfirm: true, Blockers.Count: 0 },
                "SETUP: actual Core Preview was not confirmable: " + JsonSerializer.Serialize(reviewed));
            preview = reviewed.Value!;
            beforeForged = probe.Calls.Count;
            Require((await runtime.Coordinator.ConfirmCreationPrerequisiteAsync(preview with { }, assignments, selections)).Receipt is null
                && probe.Calls.Count == beforeForged, "A copied preview acquired native confirmation authority.");
            Require(PrerequisiteColdRows(runtime).All(pair => pair.Value.Digest == before[pair.Key].Digest),
                "Load/Preview changed a cold owner partition.");

            if (scenario.StartsWith("page-", StringComparison.Ordinal))
            {
                result = await RunPrerequisitePageAsync(runtime, owners, ui, probe, preview, assignments, selections, scenario);
            }
            else
            {
                if (scenario is "issued-b" or "issued-aba" or "same-owner-reload") await ReplaceOwnerAsync();
                using var cancellation = new CancellationTokenSource();
                if (scenario == "postcommit-cancel") probe.AfterCommit = cancellation.Cancel;
                if (scenario == "postcommit-owner-b") probe.AfterCommit = () => owners.Set(ContactsOwnerB);
                if (scenario == "postcommit-load-fault") probe.FailPostCommitLoad = true;
                if (scenario == "postcommit-observer") observerFault = true;
                if (scenario == "entered-unknown") probe.AfterCommit = () => throw new InvalidOperationException("diagnostic lost Core return");

                bool queued = scenario.StartsWith("queued-confirm", StringComparison.Ordinal) || scenario == "cancel-before-dispatch";
                if (queued) await gate.WaitAsync();
                Task<CreationPrerequisitePhoneConfirmResult> pending;
                try
                {
                    pending = runtime.Coordinator.ConfirmCreationPrerequisiteAsync(preview, assignments, selections, cancellation.Token);
                    liveOperation = pending;
                    if (queued)
                    {
                        Require(!pending.IsCompleted, "SETUP: Confirm did not wait at the real activation gate.");
                        if (scenario == "cancel-before-dispatch") cancellation.Cancel();
                        else await ReplaceOwnerAsync();
                    }
                }
                finally { if (queued) gate.Release(); }
                try { result = await pending; }
                catch (OperationCanceledException) when (scenario == "cancel-before-dispatch") { }
                liveOperation = null;
                if (scenario is "cancel-before-dispatch" or "postcommit-cancel")
                    Require(cancellation.IsCancellationRequested, "The intended cancellation was never issued.");
            }

            Require(probe.PostCommitLoadFaultCalls == (scenario is "postcommit-load-fault" or "page-load-fault" ? 1 : 0),
                "The intended postcommit Load fault was not reached exactly once, or another case injected it.");
            Require(probe.AfterCommitCalls == (scenario is "postcommit-cancel" or "postcommit-owner-b" or "entered-unknown" ? 1 : 0),
                "The intended after-commit callback was not reached exactly once, or another case invoked it.");
            Require(scenario == "postcommit-observer" ? observerFaultCalls > 0 : observerFaultCalls == 0,
                "The intended observer fault was never reached, or another case injected it.");
            if (scenario == "postcommit-owner-b")
                Require(owners.Current == ContactsOwnerB, "The actual postcommit owner transition was not reached.");

            bool shouldCommit = scenario is "local" or "linked" or "postcommit-cancel" or "postcommit-load-fault"
                or "postcommit-owner-b" or "postcommit-observer" or "entered-unknown" or "page-same" or "page-load-fault"
                || scenario.StartsWith("page-back-", StringComparison.Ordinal);
            var after = PrerequisiteColdRows(runtime);
            string ownKey = scenario == "local" ? "local" : "account-a";
            Require(after.All(pair => pair.Key == ownKey && shouldCommit
                ? pair.Value.Content == before[pair.Key].Content + 1 && pair.Value.Saved == pair.Value.Content
                  && pair.Value.RawXml == before[pair.Key].RawXml && pair.Value.Draft is not null
                : pair.Value.Digest == before[pair.Key].Digest), "Unexpected cold partition/document/revision change.");
            Require(probe.SuccessfulConfirms == (shouldCommit ? 1 : 0), "Unexpected actual Core successful Confirm count.");
            if (shouldCommit && scenario != "entered-unknown")
                Require(result?.Receipt is { } receipt && receipt.ContentRevision == after[ownKey].Content
                    && receipt.SavedRevision == after[ownKey].Saved && receipt.DraftDigest == after[ownKey].Draft!.DraftDigest,
                    "A genuine committed receipt was lost or no longer matches its cold atomic draft.");
            else Require(result?.Receipt is null, "Rejected/unknown operation exposed a receipt it did not receive.");

            if (scenario is "local" or "linked" or "page-same")
                Require(result?.RefreshedState is { } refreshed
                    && runtime.Coordinator.IsCreationPrerequisiteReceiptCurrent(result.Receipt!, refreshed),
                    "Successful same-owner confirmation did not return a joined current receipt/state.");
            if (scenario.StartsWith("postcommit-", StringComparison.Ordinal) || scenario == "page-load-fault")
                Require(result is { RefreshedState: null } && result.Blockers.Contains(RunnerSessionCoordinator.PrerequisitePostCommitRefreshRequired),
                    "Failed postcommit refresh was described as current/reloaded.");
            if (scenario == "postcommit-owner-b")
                Require(!runtime.Coordinator.CanDisplayCreationPrerequisiteReceipt(result!.Receipt!),
                    "Original A receipt is visible under replacement B.");
            if (scenario == "entered-unknown")
                Require(result?.Blockers.Contains(RunnerSessionCoordinator.PrerequisiteOutcomeUnknown) == true,
                    "Entered exception was silently treated as a safe retry or successful confirmation.");
            if (scenario == "cancel-before-dispatch")
                Require(runtime.Coordinator.IsCreationPrerequisitePreviewCurrent(preview) && probe.ConfirmCalls == 0,
                    "Proven predispatch cancellation consumed the preview or entered Core.");

            if (probe.ConfirmCalls > 0)
            {
                int confirmIndex = probe.Calls.IndexOf("confirm");
                Require(confirmIndex > 0 && probe.Calls[confirmIndex - 1] == (scenario.StartsWith("page-", StringComparison.Ordinal) ? "load" : "preview")
                    && probe.Calls.Take(confirmIndex).Count(call => call == "load") == (scenario.StartsWith("page-", StringComparison.Ordinal) ? 2 : 1),
                    "Confirm added a redundant pre-evaluation Core Load.");
                int calls = probe.ConfirmCalls;
                observerFault = false;
                var duplicate = await runtime.Coordinator.ConfirmCreationPrerequisiteAsync(preview, assignments, selections);
                Require(duplicate.Receipt is null && probe.ConfirmCalls == calls
                    && PrerequisiteColdRows(runtime).All(pair => pair.Value.Digest == after[pair.Key].Digest),
                    "Consumed preview replayed Core or changed a cold row.");
            }
        }
        finally
        {
            observerFault = false;
            runtime.Coordinator.Changed -= changed;
            if (liveOperation is not null)
            {
                try { await JoinIssuedPageAsync(liveOperation); }
                catch (IssuedPageUnjoinedException) { throw; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Console.WriteLine("PREREQUISITE_JOINED_EXCEPTION " + error); }
            }
            Require(owners.ActiveLeases == 0, "Prerequisite leaked a real Core owner lease.");
            var after = PrerequisiteColdRows(runtime);
            Console.WriteLine("PREREQUISITE_OWNER_OBSERVATION " + JsonSerializer.Serialize(new
            {
                scenario, originalOwner, liveOwner = owners.Capture(), displayOwner = runtime.Coordinator.State.DisplayOwnerContext,
                calls = probe!.Calls, probe.SuccessfulConfirms, probe.UiTicks, outcome = result?.Outcome,
                probe.PostCommitLoadFaultCalls, probe.AfterCommitCalls, observerFaultCalls,
                receipt = result?.Receipt, refreshed = result?.RefreshedState is not null, blockers = result?.Blockers,
                before = before.ToDictionary(pair => pair.Key, pair => new { pair.Value.Content, pair.Value.Saved, pair.Value.Digest }),
                after = after.ToDictionary(pair => pair.Key, pair => new { pair.Value.Content, pair.Value.Saved, pair.Value.Digest })
            }));
            if (scenario is "queued-load-b" or "queued-preview-b" or "missing-service")
                Require(after.All(pair => pair.Value.Digest == before[pair.Key].Digest), "Rejected read changed a cold partition.");
        }

        async Task ReplaceOwnerAsync()
        {
            if (scenario != "same-owner-reload") owners.Set(ContactsOwnerB);
            if (scenario.EndsWith("aba", StringComparison.Ordinal)) owners.Set(ContactsOwnerA);
            await HydrateFinalizationOwnerAsync(runtime, owners, seed);
            Require(!ReferenceEquals(originalDisplay.Profile, runtime.Coordinator.State.Profile)
                    && (scenario == "same-owner-reload" || owners.Capture() != originalOwner),
                "SETUP: same-ID replacement/owner epoch never changed.");
            Require(runtime.Coordinator.LoadCreationPrerequisite().Value is null,
                "Cache-only read reattached old authority to a replacement display.");
            if (scenario is "issued-b" or "issued-aba" or "same-owner-reload")
            {
                var replacement = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
                Require(replacement.Value is { } && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(replacement.Value),
                    "SETUP: same-ID replacement did not issue its own fresh authority.");
                Require(preview is not null && !runtime.Coordinator.IsCreationPrerequisitePreviewCurrent(preview),
                    "A replacement's same-digest cache reattached the original preview.");
            }
        }
    }

    private static WorkspaceStoredDocument PreparePrerequisiteOwnerFixture(NativeRewardRuntime runtime)
    {
        Require(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(CharacterCreationBuildMethods.Priority, out string profile),
            "SETUP: canonical Priority profile missing.");
        var created = runtime.Services.GetRequiredService<ICharacterCreationBootstrapService>().Create(new(
            CharacterCreationBootstrapSchemas.RequestV1, CharacterCreationBootstrapStages.AwaitingFoundationSelection,
            "sr5", "Prerequisite owner fixture", "Prerequisite", CharacterCreationBuildMethods.Priority, profile));
        Require(created is { Outcome: CharacterCreationBootstrapOutcomes.Success, Value: not null },
            "SETUP: real Bootstrap failed: " + JsonSerializer.Serialize(created));
        runtime.Id = created.Value!.WorkspaceId;
        var row = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id);
        Require(row.Success && row.Value is { ContentRevision: 1, SavedRevision: 0 },
            "SETUP: actual Bootstrap did not retain its canonical initial 1/0 revisions.");
        return row.Value!;
    }

    private static (IReadOnlyDictionary<string, string>, CreationPrerequisitePhoneSelections)
        PrerequisiteSelections(CharacterCreationPrerequisiteState state)
    {
        // Choose exact real Core-projected options, as the existing finalization fixture does.
        var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CharacterCreationPriorityCategoryIds.Heritage] = "A", [CharacterCreationPriorityCategoryIds.Talent] = "E",
            [CharacterCreationPriorityCategoryIds.Attributes] = "B", [CharacterCreationPriorityCategoryIds.Skills] = "C",
            [CharacterCreationPriorityCategoryIds.Resources] = "D"
        };
        var human = state.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage && item.Rank == "A")
            .HeritageOptions.First(item => item.IsEnabled && item.MetavariantSourceId is null && item.MetatypeName == "Human");
        var mundane = state.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent && item.Rank == "E")
            .TalentOptions.First(item => item.IsEnabled && item.Value.Equals(CharacterCreationMagicResonanceKinds.Mundane, StringComparison.OrdinalIgnoreCase)
                && item.Magic is null && item.Resonance is null && item.Depth is null && item.ActiveSkillGrant is null && item.SkillGroupGrant is null);
        return (ranks, new(human.SelectionId, mundane.SelectionId, [], []));
    }

    private sealed record PrerequisiteColdRow(long Content, long Saved, string Digest, string RawXml,
        CharacterCreationPrerequisiteDraft? Draft);

    private static Dictionary<string, PrerequisiteColdRow> PrerequisiteColdRows(NativeRewardRuntime runtime)
    {
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        return new Dictionary<string, OwnerScope> { ["local"] = OwnerScope.LocalSingleUser,
            ["account-a"] = ContactsOwnerA, ["account-b"] = ContactsOwnerB }.ToDictionary(pair => pair.Key, pair =>
        {
            var row = pair.Value.IsLocalSingleUser
                ? store.Get(runtime.Id)
                : store.Get(pair.Value, runtime.Id);
            Require(row.Success && row.Value is not null, "SETUP: cold owner fixture missing: " + pair.Key);
            return new PrerequisiteColdRow(row.Value!.ContentRevision, row.Value.SavedRevision,
                FinalizationDocumentDigest(row.Value), row.Value.Document.Content,
                row.Value.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        });
    }

    private sealed class PrerequisiteCoreProbe(IOwnerBoundCharacterCreationPrerequisiteService actual,
        ControlledLinkedOwner owners, IssuedPageUiContext ui) : IOwnerBoundCharacterCreationPrerequisiteService
    {
        public List<string> Calls { get; } = [];
        public int ConfirmCalls { get; private set; }
        public int SuccessfulConfirms { get; private set; }
        public int UiTicks { get; private set; }
        public int PostCommitLoadFaultCalls { get; private set; }
        public int AfterCommitCalls { get; private set; }
        public bool FailPostCommitLoad { get; set; }
        public Action? AfterCommit { get; set; }

        private T Invoke<T>(string operation, Func<T> work)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, ui), "Actual Core prerequisite work ran on the managed UI pump.");
            Calls.Add(operation);
            owners.AfterAcquire = () =>
            {
                Require(owners.ActiveLeases == 1, "Actual Core entry did not acquire its owner lease.");
                var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ui.Post(_ => { UiTicks++; tick.TrySetResult(); }, null);
                Require(tick.Task.Wait(TimeSpan.FromSeconds(5)), "Managed UI pump failed to progress during real leased Core work.");
            };
            try { return work(); }
            finally { owners.AfterAcquire = null; Require(owners.ActiveLeases == 0, "Core retained a lease after its synchronous call."); }
        }
        public CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> Load(OwnerContextStamp owner,
            CharacterCreationPrerequisiteLoadRequest request) => Invoke("load", () =>
            {
                if (FailPostCommitLoad && SuccessfulConfirms > 0)
                {
                    PostCommitLoadFaultCalls++;
                    throw new InvalidOperationException("diagnostic postcommit Load fault");
                }
                return actual.Load(owner, request);
            });
        public CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> Preview(OwnerContextStamp owner,
            CharacterCreationPrerequisitePreviewRequest request) => Invoke("preview", () => actual.Preview(owner, request));
        public CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> Confirm(OwnerContextStamp owner,
            CharacterCreationPrerequisiteConfirmRequest request)
        {
            ConfirmCalls++;
            var result = Invoke("confirm", () => actual.Confirm(owner, request));
            if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null })
            {
                SuccessfulConfirms++;
                if (AfterCommit is { } afterCommit)
                {
                    AfterCommitCalls++;
                    afterCommit();
                }
            }
            return result;
        }
    }

    private static async Task<CreationPrerequisitePhoneConfirmResult?> RunPrerequisitePageAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, IssuedPageUiContext ui, PrerequisiteCoreProbe probe, CharacterCreationPrerequisitePreview preview,
        IReadOnlyDictionary<string, string> assignments, CreationPrerequisitePhoneSelections selections, string scenario)
    {
        var page = new CreationPrerequisitePreviewPage(runtime.Coordinator, preview, assignments, selections, CharacterCreationBuildMethods.Priority);
        var navigation = new NavigationPage(new ContentPage { Title = "Prerequisite origin" });
        await navigation.PushAsync(page, animated: false);
        var window = new Window(navigation);
        using var alerts = new IssuedPageAlerts(page, window);
        await alerts.PreflightAsync();
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        Task? pending = null;
        bool held = false;
        bool completed = false;
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        int renderedChildChanges = 0;
        int offUiRenderedChildChanges = 0;
        void ObserveRenderedChild(object? sender, ElementEventArgs args)
        {
            // Observe actual Refresh control mutations, not raw Changed delivery.
            // Record first; throwing here would alter the product's error path.
            Interlocked.Increment(ref renderedChildChanges);
            if (!ReferenceEquals(SynchronizationContext.Current, ui) || page.Dispatcher.IsDispatchRequired)
                Interlocked.Increment(ref offUiRenderedChildChanges);
        }
        body.ChildAdded += ObserveRenderedChild;
        body.ChildRemoved += ObserveRenderedChild;
        try
        {
            pending = ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            await JoinIssuedPageAsync(pending); pending = null;
            Require(renderedChildChanges > 0, "SETUP: actual appearance never reached the observed render controls.");
            Require(alerts.Titles.Count == 0 && runtime.Coordinator.IsCreationPrerequisitePreviewCurrent(preview),
                "SETUP: real page appearance failed/replaced the issued preview.");
            Button button = IssuedElements(page).OfType<Button>().Single(item => item.AutomationId == "creation-prerequisite-confirm");
            Require(button.IsEnabled, "SETUP: actual page did not render an enabled Confirm.");
            int calls = probe.Calls.Count;
            RefreshPage();
            Require(probe.Calls.Count == calls, "PreviewPage.Refresh invoked Core.");
            if (scenario != "page-old-render")
                button = IssuedElements(page).OfType<Button>().Single(item => item.AutomationId == "creation-prerequisite-confirm");
            if (scenario == "page-departure") IssuedPageLifecycle(page, "OnDisappearing");
            if (scenario == "page-load-fault") probe.FailPostCommitLoad = true;
            if (scenario == "page-queued-departure") { await gate.WaitAsync(); held = true; }
            pending = ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
            if (held)
            {
                Require(!pending.IsCompleted && probe.ConfirmCalls == 0, "SETUP: page Confirm did not queue before Core.");
                IssuedPageLifecycle(page, "OnDisappearing"); gate.Release(); held = false;
            }
            await JoinIssuedPageAsync(pending); pending = null;
            Require(alerts.Titles.Count == 0, "Unexpected native prerequisite alert: " + string.Join(",", alerts.Titles));
            var result = (CreationPrerequisitePhoneConfirmResult?)typeof(CreationPrerequisitePreviewPage)
                .GetField("_confirmation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
            var ids = IssuedElements(page).Select(item => item.AutomationId).ToArray();
            if (scenario == "page-same")
                Require(ids.Contains("creation-prerequisite-confirmed") && ids.Contains("creation-prerequisite-confirm-receipt"),
                    "Current receipt/reload markers were not rendered after a real saved confirmation.");
            if (scenario == "page-load-fault")
                Require(result?.Receipt is not null && ids.Contains("creation-prerequisite-saved-reopen-required")
                    && !ids.Contains("creation-prerequisite-confirmed") && !ids.Contains("creation-prerequisite-confirm-receipt")
                    && !ids.Contains("creation-prerequisite-confirm"), "Failed reload exposed successful-reload or retry controls.");
            if (scenario.StartsWith("page-back-", StringComparison.Ordinal))
            {
                Require(result?.Receipt is not null && result.RefreshedState is not null
                    && runtime.Coordinator.IsCreationPrerequisiteReceiptCurrent(result.Receipt, result.RefreshedState),
                    "SETUP: Back negative did not start from an actually current committed receipt.");
                Button back = IssuedElements(page).OfType<Button>().Single(item => item.AutomationId == "creation-prerequisite-back-to-build");
                Require(back.IsEnabled && Shell.Current is null,
                    "SETUP: this bounded no-navigation negative requires a real enabled Back and no ambient Shell.");
                if (scenario == "page-back-departure") IssuedPageLifecycle(page, "OnDisappearing");
                if (scenario == "page-back-old-render") RefreshPage();
                if (scenario is "page-back-owner-b" or "page-back-owner-aba")
                {
                    owners.Set(ContactsOwnerB);
                    if (scenario == "page-back-owner-aba") owners.Set(ContactsOwnerA);
                    var target = new FileWorkspaceStore(runtime.StateDirectory).Get(owners.Current, runtime.Id).Value!;
                    await HydrateFinalizationOwnerAsync(runtime, owners, target);
                }
                var stack = navigation.Navigation.NavigationStack.ToArray();
                int coreCalls = probe.Calls.Count;
                pending = ui.BeginAsyncVoid(() => ((IButtonController)back).SendClicked());
                await JoinIssuedPageAsync(pending); pending = null;
                // Without the guard, the real handler reaches the absent-MainShell
                // error and alerts. This proves rejection before navigation only;
                // it is not a successful real Android/Shell route test.
                Require(alerts.Titles.Count == 0 && Shell.Current is null
                    && navigation.Navigation.NavigationStack.SequenceEqual(stack) && probe.Calls.Count == coreCalls,
                    "A stale Back handler reached Shell navigation or changed the current stack/Core.");
                Console.WriteLine("PREREQUISITE_BACK_SCOPE managed stale-callback rejection; real Shell navigation untested");
            }
            completed = true;
            return result;
        }
        finally
        {
            if (held) gate.Release();
            try
            {
                if (pending is not null) await JoinIssuedPageAsync(pending);
                if (IssuedPageField<int>(page, "_subscribed") != 0) IssuedPageLifecycle(page, "OnDisappearing");
                // Drain already queued dispatcher callbacks after departure has
                // fenced further renders; keep observing through that boundary.
                await JoinIssuedPageAsync(ui.RunAsync(() => Task.CompletedTask));
                Console.WriteLine("PREREQUISITE_RENDER_OBSERVATION " + JsonSerializer.Serialize(new
                { scenario, renderedChildChanges, offUiRenderedChildChanges }));
                if (completed)
                    Require(offUiRenderedChildChanges == 0,
                        "Actual prerequisite page rendered controls outside its managed UI dispatcher.");
            }
            finally
            {
                body.ChildAdded -= ObserveRenderedChild;
                body.ChildRemoved -= ObserveRenderedChild;
            }
        }
        void RefreshPage() => typeof(CreationPrerequisitePreviewPage).GetMethod("Refresh",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(page, null);
    }
}
