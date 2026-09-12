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
        => await RunCreationPrerequisiteCasesAsync(contentRoot, parentReadinessOnly: false);

    public static Task RunCreationPrerequisiteParentReadinessCasesAsync(string contentRoot)
        => RunCreationPrerequisiteCasesAsync(contentRoot, parentReadinessOnly: true);

    private static async Task RunCreationPrerequisiteCasesAsync(string contentRoot, bool parentReadinessOnly)
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
                "page-back-departure", "page-back-old-render", "page-back-owner-b", "page-back-owner-aba",
                "parent-return", "parent-return-fault", "parent-return-departure", "parent-return-owner-b", "parent-return-owner-aba",
                "parent-return-core-blocked", "parent-return-core-invalid", "parent-return-core-empty",
                "parent-return-core-copy", "parent-return-core-recovery", "parent-return-core-fault-recovery",
                "parent-return-core-currentness"];
            if (parentReadinessOnly)
                cases = cases.Where(scenario => scenario.StartsWith("parent-return", StringComparison.Ordinal)).ToArray();
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
            if (scenario.StartsWith("parent-return-core-", StringComparison.Ordinal))
            {
                await RunPrerequisiteAppearanceFailureAsync(runtime, owners, ui, probe!, state, scenario);
                return;
            }
            if (scenario.StartsWith("parent-return", StringComparison.Ordinal))
            {
                await RunPrerequisiteParentReturnAsync(runtime, owners, ui, probe!, state, scenario);
                return;
            }
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
            if (scenario is "queued-load-b" or "queued-preview-b" or "missing-service"
                || scenario.StartsWith("parent-return", StringComparison.Ordinal))
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
        public int AppearanceLoadFaultCalls { get; private set; }
        public int AppearanceOutcomeCalls { get; private set; }
        public string? AppearanceOutcomeOverride { get; set; }
        public List<string> AppearanceBlockers { get; } = [];
        public bool FailAppearanceLoad { get; set; }
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
                if (FailAppearanceLoad)
                {
                    AppearanceLoadFaultCalls++;
                    throw new InvalidOperationException("diagnostic appearance Load fault");
                }
                if (FailPostCommitLoad && SuccessfulConfirms > 0)
                {
                    PostCommitLoadFaultCalls++;
                    throw new InvalidOperationException("diagnostic postcommit Load fault");
                }
                var result = actual.Load(owner, request);
                if (AppearanceOutcomeOverride is { } outcome)
                {
                    // Keep actual owner acquisition/source loading as the setup;
                    // substitute only a negative response, never successful authority.
                    Require(result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null },
                        "SETUP: real Core Load was not successful before the negative response injection.");
                    AppearanceOutcomeCalls++;
                    return new(outcome, null, AppearanceBlockers);
                }
                return result;
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

    private static async Task RunPrerequisiteAppearanceFailureAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, IssuedPageUiContext ui, PrerequisiteCoreProbe probe,
        CharacterCreationPrerequisiteState state, string scenario)
    {
        var page = new CreationPrerequisitePage(runtime.Coordinator, state);
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        var navigation = new NavigationPage(new ContentPage { Title = "Prerequisite diagnostics origin" });
        await navigation.PushAsync(page, animated: false);
        var window = new Window(navigation);
        using var alerts = new IssuedPageAlerts(page, window);
        await alerts.PreflightAsync();
        Task? pending = null;
        int renders = 0, offUiRenders = 0;
        void Observe(object? sender, ElementEventArgs args)
        {
            renders++;
            if (!ReferenceEquals(SynchronizationContext.Current, ui) || page.Dispatcher.IsDispatchRequired) offUiRenders++;
        }
        body.ChildAdded += Observe; body.ChildRemoved += Observe;
        try
        {
            await AppearAsync();
            Require(body.IsEnabled && alerts.Titles.Count == 0
                && IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "creation-prerequisite-prepare-preview"),
                "SETUP: actual initial prerequisite appearance never rendered its ready surface.");
            IssuedPageLifecycle(page, "OnDisappearing");
            bool fault = scenario == "parent-return-core-fault-recovery";
            bool invalid = scenario is "parent-return-core-invalid" or "parent-return-core-empty";
            string[] expected = scenario == "parent-return-core-empty" ? [CharacterCreationFoundationOutcomes.Invalid]
                : invalid ? ["diagnostic-core-prerequisite-invalid"]
                : ["diagnostic-core-prerequisite-source-unavailable", "diagnostic-core-prerequisite-profile-unavailable"];
            probe.AppearanceOutcomeOverride = fault ? null
                : invalid ? CharacterCreationFoundationOutcomes.Invalid : CharacterCreationFoundationOutcomes.Blocked;
            if (scenario != "parent-return-core-empty") probe.AppearanceBlockers.AddRange(expected);
            probe.FailAppearanceLoad = fault;
            int calls = probe.Calls.Count;
            await AppearAsync();
            Require(probe.Calls.Count == calls + 1 && probe.AppearanceOutcomeCalls == (fault ? 0 : 1)
                && probe.AppearanceLoadFaultCalls == (fault ? 1 : 0),
                "The intended appearance result/exception was not reached exactly once.");
            Require(alerts.Titles.Count == (fault ? 1 : 0), "Negative Core result was mistaken for an exception, or load exception did not alert.");
            AssertUnavailable(fault ? ["creation-prerequisite-appearance-load-failed"] : expected);

            if (scenario == "parent-return-core-copy")
            {
                probe.AppearanceBlockers.Clear(); probe.AppearanceBlockers.Add("forged-after-return-blocker");
                calls = probe.Calls.Count;
                typeof(CreationPrerequisitePage).GetMethod("Refresh",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(page, null);
                Require(probe.Calls.Count == calls, "A diagnostic rerender re-entered Core.");
                AssertUnavailable(expected);
            }
            if (scenario == "parent-return-core-currentness")
            {
                IssuedPageLifecycle(page, "OnDisappearing");
                var oldDisplay = runtime.Coordinator.State;
                OwnerContextStamp owner = owners.Capture();
                var target = new FileWorkspaceStore(runtime.StateDirectory).Get(owners.Current, runtime.Id).Value!;
                await HydrateFinalizationOwnerAsync(runtime, owners, target);
                Require(owners.Capture() == owner && !ReferenceEquals(oldDisplay.Profile, runtime.Coordinator.State.Profile)
                    && oldDisplay.ContentRevision == runtime.Coordinator.State.ContentRevision
                    && oldDisplay.SavedRevision == runtime.Coordinator.State.SavedRevision,
                    "SETUP: same-owner/revision display replacement never happened.");
                calls = probe.Calls.Count;
                await AppearAsync();
                Require(probe.Calls.Count == calls && probe.AppearanceOutcomeCalls == 1,
                    "A retained stale issuance entered Core or borrowed the replacement display.");
                AssertUnavailable([CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision]);
                Require(alerts.Titles.Count == 0, "A currentness rejection raised an unrelated exception.");
            }
            if (scenario is "parent-return-core-recovery" or "parent-return-core-fault-recovery")
            {
                IssuedPageLifecycle(page, "OnDisappearing");
                probe.AppearanceOutcomeOverride = null; probe.FailAppearanceLoad = false;
                calls = probe.Calls.Count;
                await AppearAsync();
                Require(probe.Calls.Count == calls + 1 && body.IsEnabled
                    && !IssuedElements(page).Any(item => item.AutomationId == "creation-prerequisite-unavailable")
                    && IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "creation-prerequisite-prepare-preview")
                    && alerts.Titles.Count == (fault ? 1 : 0),
                    "Later actual Core success retained the prior failure or repeated its alert.");
                var current = (CharacterCreationPrerequisiteState?)typeof(CreationPrerequisitePage)
                    .GetField("_dashboardAuthority", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                Require(current is not null && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(current),
                    "Recovered controls were not backed by an actual fresh Core issuance.");
            }
            Require(probe.ConfirmCalls == 0 && owners.ActiveLeases == 0, "Diagnostic appearance mutated or retained an owner lease.");
        }
        finally
        {
            try
            {
                if (pending is not null) await JoinIssuedPageAsync(pending);
                if (IssuedPageField<int>(page, "_subscribed") != 0) IssuedPageLifecycle(page, "OnDisappearing");
                await JoinIssuedPageAsync(ui.RunAsync(() => Task.CompletedTask));
                Require(renders > 0 && offUiRenders == 0 && owners.ActiveLeases == 0,
                    "Diagnostic page renders escaped the actual managed dispatcher or leaked a lease.");
                Console.WriteLine("PREREQUISITE_APPEARANCE_DIAGNOSTIC " + JsonSerializer.Serialize(new
                { scenario, renders, offUiRenders, probe.AppearanceOutcomeCalls, probe.AppearanceLoadFaultCalls,
                    alerts = alerts.Titles.ToArray(), scope = "actual managed Core/native lifecycle; proof-instrumentation branch not compiled" }));
            }
            finally { body.ChildAdded -= Observe; body.ChildRemoved -= Observe; }
        }
        async Task AppearAsync()
        {
            pending = ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            await JoinIssuedPageAsync(pending); pending = null;
        }
        void AssertUnavailable(string[] blockers)
        {
            Require(!body.IsEnabled && !IssuedElements(page).OfType<Button>().Any()
                && !IssuedElements(page).OfType<ActivityIndicator>().Any(item => item.IsRunning),
                "Unavailable appearance left controls active or a loading operation running.");
            var border = IssuedElements(page).OfType<Border>().Single(item => item.AutomationId == "creation-prerequisite-unavailable");
            string[] rendered = ((VerticalStackLayout)border.Content!).Children.OfType<Label>().Skip(1).Select(item => item.Text).ToArray();
            Require(rendered.SequenceEqual(blockers), "Unavailable card replaced/lost/reordered blockers: " + JsonSerializer.Serialize(rendered));
        }
    }

    private static async Task RunPrerequisiteParentReturnAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, IssuedPageUiContext ui, PrerequisiteCoreProbe probe,
        CharacterCreationPrerequisiteState state, string scenario)
    {
        var page = new CreationPrerequisitePage(runtime.Coordinator, state);
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        bool initiallyDisabled = !body.IsEnabled;
        var navigation = new NavigationPage(new ContentPage { Title = "Prerequisite origin" });
        await navigation.PushAsync(page, animated: false);
        var window = new Window(navigation);
        using var alerts = new IssuedPageAlerts(page, window);
        await alerts.PreflightAsync();
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        Task? pending = null;
        bool held = false;
        bool returnGateReached = false;
        string stage = "initial-appearance";
        Exception? primaryFailure = null;
        int renderChanges = 0, offUiRenderChanges = 0;
        void ObserveRender(object? sender, ElementEventArgs args)
        {
            Interlocked.Increment(ref renderChanges);
            if (!ReferenceEquals(SynchronizationContext.Current, ui) || page.Dispatcher.IsDispatchRequired)
                Interlocked.Increment(ref offUiRenderChanges);
        }
        body.ChildAdded += ObserveRender;
        body.ChildRemoved += ObserveRender;
        try
        {
            int initialCalls = probe.Calls.Count;
            pending = ui.BeginAsyncVoid(() => Lifecycle("OnAppearing"));
            await JoinIssuedPageAsync(pending); pending = null;
            Require(renderChanges > 0 && probe.Calls.Count == initialCalls + 1,
                "SETUP: actual initial appearance did not load Core and render controls.");
            Require(body.IsEnabled && !Loading() && alerts.Titles.Count == 0,
                "SETUP: current prerequisite appearance did not become ready.");
            stage = "ranked-heritage-navigation";
            state = (CharacterCreationPrerequisiteState)typeof(CreationPrerequisitePage)
                .GetField("_dashboardAuthority", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var draft = (CreationPrerequisitePhoneDraft)typeof(CreationPrerequisitePage)
                .GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            // Seed local ranks through the actual typed draft, never a fabricated
            // authority or saved state. The navigation/callbacks below are real.
            Require(draft.TrySelect(state, runtime.Coordinator.State, CharacterCreationPriorityCategoryIds.Heritage, "A")
                && draft.TrySelect(state, runtime.Coordinator.State, CharacterCreationPriorityCategoryIds.Talent, "E"),
                "SETUP: real prerequisite ranks were unavailable.");
            typeof(CreationPrerequisitePage).GetMethod("Refresh",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(page, null);
            Button oldTalent = FindButton("creation-prerequisite-talent-selection");
            Button heritage = FindButton("creation-prerequisite-heritage-selection");
            Require(oldTalent.IsEnabled && heritage.IsEnabled, "SETUP: ranked navigation was disabled.");
            pending = ui.BeginAsyncVoid(() => ((IButtonController)heritage).SendClicked());
            await JoinIssuedPageAsync(pending); pending = null;
            Require(navigation.Navigation.NavigationStack.Last() is CreationPriorityDetailPage
                { AutomationId: "creation-prerequisite-heritage-page" }, "Actual Heritage tap did not push its detail page.");
            Lifecycle("OnDisappearing");
            bool departedDisabled = !body.IsEnabled && !Loading();
            await navigation.PopAsync(animated: false);
            int callsBeforeReturn = probe.Calls.Count;
            await gate.WaitAsync(); held = true;
            if (scenario == "parent-return-fault") probe.FailAppearanceLoad = true;
            stage = "return-appearance";
            pending = ui.BeginAsyncVoid(() => Lifecycle("OnAppearing"));
            Require(!pending.IsCompleted && probe.Calls.Count == callsBeforeReturn,
                "SETUP: return appearance did not await the actual Core activation gate.");
            returnGateReached = true;
            stage = "gate-held-readiness";
            Require(!body.IsEnabled && !oldTalent.IsEnabled && initiallyDisabled && departedDisabled,
                "Returned parent exposed stale enabled controls while authority revalidation was pending.");
            Require(Loading(), "Pending parent did not expose its running loading indicator.");
            var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Post(_ => tick.TrySetResult(), null);
            await tick.Task.WaitAsync(TimeSpan.FromSeconds(5));
            OwnerContextStamp originalOwner = owners.Capture();
            if (scenario == "parent-return-departure") Lifecycle("OnDisappearing");
            if (scenario is "parent-return-owner-b" or "parent-return-owner-aba")
            {
                owners.Set(ContactsOwnerB);
                if (scenario == "parent-return-owner-aba") owners.Set(ContactsOwnerA);
                var target = new FileWorkspaceStore(runtime.StateDirectory).Get(owners.Current, runtime.Id).Value!;
                await HydrateFinalizationOwnerAsync(runtime, owners, target);
                Require(owners.Capture() != originalOwner, "SETUP: owner epoch did not change.");
            }
            gate.Release(); held = false;
            stage = "return-completion";
            await JoinIssuedPageAsync(pending); pending = null;
            Require(!Loading() && probe.AppearanceLoadFaultCalls == (scenario == "parent-return-fault" ? 1 : 0),
                "Return stayed busy or did not reach exactly the intended appearance fault.");
            Require(alerts.Titles.Count == (scenario == "parent-return-fault" ? 1 : 0),
                "Unexpected/missing appearance error alert.");
            var stack = navigation.Navigation.NavigationStack.ToArray();
            if (scenario == "parent-return")
            {
                Button freshTalent = FindButton("creation-prerequisite-talent-selection");
                Require(body.IsEnabled && freshTalent.IsEnabled && !ReferenceEquals(oldTalent, freshTalent)
                    && probe.Calls.Count == callsBeforeReturn + 1, "Fresh return did not rebuild current actionable controls.");
                // The retained old control is now detached from the disabled
                // body. Its callback must still reject the old appearance.
                Require(oldTalent.IsEnabled, "SETUP: retained old callback is not independently invocable.");
                pending = ui.BeginAsyncVoid(() => ((IButtonController)oldTalent).SendClicked());
                await JoinIssuedPageAsync(pending); pending = null;
                Require(navigation.Navigation.NavigationStack.SequenceEqual(stack), "Old appearance callback navigated after fresh render.");
                pending = ui.BeginAsyncVoid(() => ((IButtonController)freshTalent).SendClicked());
                await JoinIssuedPageAsync(pending); pending = null;
                Require(navigation.Navigation.NavigationStack.Count == stack.Length + 1
                    && navigation.Navigation.NavigationStack.Last() is CreationPriorityDetailPage
                    { AutomationId: "creation-prerequisite-talent-page" }, "One fresh Talent tap did not push the real detail page.");
            }
            else
            {
                Require(!body.IsEnabled && navigation.Navigation.NavigationStack.SequenceEqual(stack),
                    "Failed/departed/replaced return revived the old editor.");
                if (scenario != "parent-return-fault")
                    Require(probe.Calls.Count == callsBeforeReturn, "Stale return entered Core under a replacement/departed frame.");
            }
            Require(probe.ConfirmCalls == 0 && owners.ActiveLeases == 0,
                "Readiness/navigation committed a draft or retained an owner lease.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            primaryFailure = error;
            Console.WriteLine("PREREQUISITE_PARENT_PRIMARY " + scenario + " stage=" + stage + "\n" + error);
            throw;
        }
        finally
        {
            try
            {
                if (held) { gate.Release(); held = false; }
                if (pending is not null) await JoinIssuedPageAsync(pending);
                Lifecycle("OnDisappearing");
                await JoinIssuedPageAsync(ui.RunAsync(() => Task.CompletedTask));
                Require(renderChanges > 0 && offUiRenderChanges == 0 && owners.ActiveLeases == 0,
                    "Parent readiness rendering escaped the managed UI context or leaked a lease.");
            }
            catch (Exception cleanup) when (primaryFailure is not null && cleanup is not OutOfMemoryException)
            {
                throw new AggregateException("Parent readiness primary and cleanup failures.", primaryFailure, cleanup);
            }
            finally
            {
                Console.WriteLine("PREREQUISITE_PARENT_READINESS " + JsonSerializer.Serialize(new
                { scenario, stage, returnGateReached, renderChanges, offUiRenderChanges,
                    activeLeases = owners.ActiveLeases, coreCalls = probe.Calls.Count,
                    probe.AppearanceLoadFaultCalls, primaryFailure = primaryFailure?.GetType().Name,
                    scope = "managed page lifecycle/navigation only; no device accessibility proof" }));
                body.ChildAdded -= ObserveRender; body.ChildRemoved -= ObserveRender;
            }
        }
        Button FindButton(string id) => IssuedElements(page).OfType<Button>().Single(item => item.AutomationId == id);
        bool Loading() => IssuedElements(page).OfType<ActivityIndicator>()
            .Any(indicator => indicator.AutomationId == "creation-prerequisite-loading" && indicator.IsRunning);
        // Use the same exact zero-argument virtual entry as the established page
        // harness; a name-only reflection lookup can select ambiguous overloads.
        void Lifecycle(string method) => IssuedPageLifecycle(page, method);
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
