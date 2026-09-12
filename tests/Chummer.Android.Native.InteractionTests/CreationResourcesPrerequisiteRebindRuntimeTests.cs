using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunCreationResourcesPrerequisiteRebindCasesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        Require(typeof(RunnerSessionCoordinator).GetMethod("RefreshApi36ProofWorkspaceAuthorityAsync",
            BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "This reproducer requires Debug and ChummerNativeProofCaptureTests=true, not a skipped proof branch.");
        PropertyInfo proofProperty = typeof(RunnerSessionCoordinator).GetProperty("DebugWorkspaceAuthority")
            ?? throw new InvalidOperationException("The actual Debug workspace capture is required.");
        using var ui = new IssuedPageUiContext();
        bool priorOptIn = AndroidE2EAuthority.Enabled;
        try
        {
            AndroidE2EAuthority.ConfigureForCurrentProcess(true);
            await ui.RunAsync(async () =>
            {
                await RunActualResourcesPrerequisiteRebindAsync(contentRoot, ui, proofProperty);
                ui.AssertHealthy();
                Console.WriteLine("PASS actual trusted-local Resources revision3 → prerequisite rebind: 1");
            });
        }
        finally { AndroidE2EAuthority.ConfigureForCurrentProcess(priorOptIn); }
    }

    private static async Task RunActualResourcesPrerequisiteRebindAsync(
        string contentRoot, IssuedPageUiContext ui, PropertyInfo proofProperty,
        Func<NativeRewardRuntime, ActualAccountFixture, ResourcesRebindCoreProbe, ResourcesRebindTrace,
            CharacterCreationPrerequisiteState, Task>? afterBaseline = null,
        FinalizationAdmissionProbe? finalizationProbe = null)
    {
        using var account = new ActualAccountFixture();
        var trace = new ResourcesRebindTrace();
        ResourcesRebindCoreProbe? probe = null;
        await using var runtime = new NativeRewardRuntime(contentRoot,
            linkedOwners: account.Owner, accountService: account.Account,
            creationBootstrap: true, creationPrerequisite: true, productionCreationOverview: true,
            finalizationDecorator: finalizationProbe is null ? null : finalizationProbe.Bind,
            prerequisiteDecorator: actual => probe = new ResourcesRebindCoreProbe(actual, trace, ui));
        Require(ReferenceEquals(runtime.Services.GetRequiredService<IOwnerContextAccessor>(), account.Owner),
            "SETUP: the real Android owner authority was replaced by a desktop or controlled owner.");
        var actualStore = runtime.Services.GetRequiredService<IWorkspaceStore>();
        Require(actualStore is FileWorkspaceStore, "SETUP: the production file-backed store is required.");
        var observedOwner = new ResourcesRebindOwnerProbe(account.Owner, trace);
        var observedStore = new ResourcesRebindStoreProbe(actualStore, observedOwner, trace);
        // Same actual class, physical store, issuing Android authority and source/query
        // dependencies. Only forwarding observers are inserted; no outcomes are changed.
        probe!.Observe(new OwnerBoundCharacterCreationPrerequisiteService(observedStore, observedOwner,
            runtime.Services.GetRequiredService<ICharacterFileQueries>(),
            runtime.Services.GetRequiredService<ICharacterSourceDataResolver>()));
        finalizationProbe?.Observe(observedStore, observedOwner,
            runtime.Services.GetRequiredService<ICharacterFileQueries>(),
            runtime.Services.GetRequiredService<ICharacterSourceDataResolver>());
        try
        {
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            OwnerContextStamp owner = account.Owner.Capture();
            await AwaitNativeAccountOwnerInitializationAsync(runtime, account, owner);
            Require(owner.Owner.IsLocalSingleUser && account.Requests == 0,
                "SETUP: device-local startup must establish real local authority without transport requests.");

            trace.Phase = "bootstrap";
            // This helper performs a real Bootstrap Create. Do NOT clone its record
            // into another owner, import replacement XML, or author auxiliary state.
            WorkspaceStoredDocument initial = PreparePrerequisiteOwnerFixture(runtime);
            RecordCold("bootstrap", initial);
            Require(initial.ContentRevision == 1 && initial.SavedRevision == 0,
                "Actual Bootstrap did not create revision1/saved0.");
            await runtime.Shell.InitializeAsync(default);
            await runtime.Presenter.InitializeAsync(default);
            await runtime.Presenter.LoadAsync(runtime.Id, default);
            RequireDisplay(1, 0);

            trace.Phase = "initial-prerequisite-load";
            var loaded = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
            RequireReady(loaded, 1, 0);
            var state = loaded.Value!;
            // Actual hosted rank choices, with the final skill-group Talent. Every
            // selection ID and grant member comes from the actual Core projection.
            var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CharacterCreationPriorityCategoryIds.Heritage] = "E",
                [CharacterCreationPriorityCategoryIds.Talent] = "B",
                [CharacterCreationPriorityCategoryIds.Attributes] = "A",
                [CharacterCreationPriorityCategoryIds.Skills] = "C",
                [CharacterCreationPriorityCategoryIds.Resources] = "D"
            };
            var human = state.Authority.Options.Single(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && row.Rank == "E").HeritageOptions.Single(row => row.IsEnabled
                    && row.MetavariantSourceId is null && row.MetatypeName == "Human");
            var talent = state.Authority.Options.Single(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && row.Rank == "B").TalentOptions.Single(row => row.IsEnabled && row.Magic == 5
                    && row.Name.StartsWith("Aspected Magician", StringComparison.Ordinal)
                    && row.SkillGroupGrant is { IsSupported: true, Blockers.Count: 0 });
            var grant = talent.SkillGroupGrant!;
            Require(grant.Quantity > 0 && grant.Options.Count >= grant.Quantity,
                "SETUP: the actual hosted Talent did not expose its required skill-group choices.");
            var selections = new CreationPrerequisitePhoneSelections(human.SelectionId, talent.SelectionId, [],
                grant.Options.OrderBy(row => row.CanonicalName, StringComparer.Ordinal)
                    .Take(grant.Quantity).Select(row => row.SelectionId).ToArray());
            trace.Phase = "prerequisite-preview-confirm";
            var preview = await runtime.Coordinator.PreviewCreationPrerequisiteAsync(state.Binding, ranks, selections);
            Require(preview.Value is { CanConfirm: true, Blockers.Count: 0 },
                "Real prerequisite preview was unavailable: " + preview.Outcome + " / " + string.Join(",", preview.Blockers));
            var confirmed = await runtime.Coordinator.ConfirmCreationPrerequisiteAsync(preview.Value!, ranks, selections);
            Require(confirmed.Receipt is not null && confirmed.RefreshedState is not null,
                "Real prerequisite confirmation failed: " + confirmed.Outcome + " / " + string.Join(",", confirmed.Blockers));
            WorkspaceStoredDocument afterPrerequisite = Cold();
            Require(afterPrerequisite.ContentRevision == 2 && afterPrerequisite.SavedRevision == 2,
                "Prerequisite confirmation did not atomically checkpoint revision2/saved2.");
            RecordCold("prerequisite-confirmed", afterPrerequisite);
            RequireDisplay(2, 2);

            // Resources currently exposes the real trusted-local service/presenter,
            // not a linked-owner adapter. No linked success is inferred here.
            var resources = new CharacterCreationResourcesInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationResourcesService>());
            trace.Phase = "resources-load-preview";
            var resourceLoad = resources.Load(runtime.Coordinator.State);
            Require(resourceLoad.State is { CanEdit: true } resourceState
                && CreationResourcesPhoneAuthority.IsReady(resourceState, runtime.Coordinator.State),
                "Real Resources was unavailable: " + resourceLoad.Outcome + " / " + string.Join(",", resourceLoad.Blockers));
            var zero = resourceLoad.State!.Options.Single(row => row.IsEnabled && row.Blockers.Count == 0 && row.KarmaInvestment == 0);
            Require(zero.TotalStartingNuyen == 50000, "Canonical rankD zero-karma Resources did not grant50000.");
            var prepared = resources.Prepare(runtime.Coordinator.State, zero.OptionId);
            Require(prepared.PreparedPreview is { CanConfirm: true, Blockers.Count: 0 },
                "Real Resources preview was unavailable: " + prepared.Outcome + " / " + string.Join(",", prepared.Blockers));
            var navigation = new NavigationPage(new ContentPage { Title = "Resources rebind origin" });
            var resourcePage = new CreationResourcesPage(runtime.Coordinator, resources, runtime.Presenter, null, resourceLoad.State);
            await navigation.PushAsync(resourcePage, animated: false);
            var window = new Window(navigation);
            using var resourceAlerts = new IssuedPageAlerts(resourcePage, window);
            await resourceAlerts.PreflightAsync();
            await Appear(resourcePage);
            var resourcePreviewPage = new CreationResourcesPreviewPage(runtime.Coordinator, resources,
                runtime.Presenter, prepared.PreparedPreview!, AndroidSurfaceStrings.Resolve());
            IssuedPageLifecycle(resourcePage, "OnDisappearing");
            await navigation.PushAsync(resourcePreviewPage, animated: false);
            using var previewAlerts = new IssuedPageAlerts(resourcePreviewPage, window);
            await previewAlerts.PreflightAsync();
            await Appear(resourcePreviewPage);
            var confirmButton = IssuedElements(resourcePreviewPage).OfType<Button>()
                .Single(button => button.AutomationId == "creation-resources-confirm");
            Require(confirmButton.IsEnabled, "Actual Resources page refused its exact confirmation.");
            trace.Phase = "resources-confirm";
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)confirmButton).SendClicked()));
            var receipt = (CharacterCreationResourcesReceipt?)typeof(CreationResourcesPreviewPage)
                .GetField("_receipt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resourcePreviewPage);
            string? resourceFailure = (string?)typeof(CreationResourcesPreviewPage)
                .GetField("_failure", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resourcePreviewPage);
            Require(receipt is { WorkspaceRevision: 3, SavedRevision: 3, DraftRevision: 1, TotalStartingNuyen: 50000 }
                && receipt.OptionId == zero.OptionId && resourceFailure is null,
                "Actual native Resources confirm/reload failed: " + resourceFailure);
            CharacterCreationResourcesReceipt confirmedReceipt = receipt
                ?? throw new InvalidOperationException("The actual Resources confirmation receipt is missing.");
            RequireDisplay(3, 3);
            WorkspaceStoredDocument afterResources = Cold();
            RecordCold("resources-confirmed", afterResources);
            string retainedDigest = FinalizationDocumentDigest(afterResources);
            string retainedRaw = RebindRawDigest(afterResources.Document.Content);
            Require(afterResources.ContentRevision == 3 && afterResources.SavedRevision == 3
                && confirmedReceipt.RawCharacterXmlDigest == retainedRaw,
                "The native Resources receipt did not bind the actual persisted3/3 raw XML.");

            trace.Phase = "resources-reopen";
            IssuedPageLifecycle(resourcePreviewPage, "OnDisappearing");
            await navigation.PopAsync(animated: false);
            await Appear(resourcePage);
            var reopened = resources.Load(runtime.Coordinator.State);
            Require(reopened.State?.PendingDraft is { DraftRevision: 1 } draft
                && draft.DraftDigest == confirmedReceipt.DraftDigest
                && CreationResourcesPhoneAuthority.RefreshedStateMatches(prepared.PreparedPreview!, confirmedReceipt, reopened.State),
                "Actual Resources reopen did not preserve its exact persisted draft and receipt binding.");
            IssuedPageLifecycle(resourcePage, "OnDisappearing");
            await navigation.PopAsync(animated: false);
            await runtime.Presenter.LoadAsync(runtime.Id, default);
            RequireDisplay(3, 3);

            trace.Phase = "post-resources-load";
            int loads = probe.LoadCalls;
            loaded = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
            Require(probe.LoadCalls == loads + 1, "Post-Resources Load did not enter Core exactly once.");
            RequireReady(loaded, 3, 3);
            var issued = loaded.Value!;
            trace.Phase = "post-resources-revalidate";
            loads = probe.LoadCalls;
            var revalidated = await runtime.Coordinator.RevalidateCreationPrerequisiteAsync(issued);
            Require(probe.LoadCalls == loads + 1, "Explicit revalidation did not enter Core exactly once.");
            RequireReady(revalidated, 3, 3);
            Require(revalidated.Value!.Binding.RawCharacterXmlDigest == retainedRaw
                && revalidated.Value.Binding.AuxiliaryStateDigest == afterResources.Document.AuxiliaryStateDigest,
                "Revalidated prerequisite did not bind the actual post-Resources raw/auxiliary state.");

            trace.Phase = "post-resources-page-appearance";
            var page = new CreationPrerequisitePage(runtime.Coordinator, issued);
            await navigation.PushAsync(page, animated: false);
            using var pageAlerts = new IssuedPageAlerts(page, window);
            await pageAlerts.PreflightAsync();
            loads = probe.LoadCalls;
            try
            {
                await Appear(page);
                Require(probe.LoadCalls == loads + 1, "Page appearance did not enter real Core exactly once.");
                string[] unavailable = IssuedElements(page).OfType<Border>()
                    .Where(item => item.AutomationId == "creation-prerequisite-unavailable")
                    .SelectMany(item => ((VerticalStackLayout)item.Content!).Children.OfType<Label>().Skip(1))
                    .Select(label => label.Text).ToArray();
                Require(unavailable.Length == 0,
                    "Actual post-Resources prerequisite appearance failed: " + string.Join(",", unavailable));
                var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                Require(body.IsEnabled && IssuedElements(page).OfType<Button>()
                    .Any(button => button.AutomationId == "creation-prerequisite-prepare-preview" && button.IsEnabled),
                    "Actual prerequisite page did not expose ready, current controls.");
                var proof = proofProperty.GetValue(runtime.Coordinator) as NativeWorkspaceAuthoritySnapshot;
                Require(proof is { ContentRevision: 3, SavedRevision: 3 } && proof.WorkspaceId == runtime.Id.Value
                    && "sha256:" + proof.PayloadSha256 == retainedRaw
                    && proof.DocumentSha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(afterResources.Document),
                    "The actual conditional page capture did not bind post-Resources3/3 raw/document bytes.");
            }
            finally { IssuedPageLifecycle(page, "OnDisappearing"); }
            Require(Cold().LastUpdatedUtc == afterResources.LastUpdatedUtc
                && FinalizationDocumentDigest(Cold()) == retainedDigest,
                "Read-only Resources/prerequisite rebind changed the persisted document.");
            Require(account.Owner.Capture() == owner && observedOwner.ActiveLeases == 0 && account.Requests == 0
                && resourceAlerts.Titles.Count == 0 && previewAlerts.Titles.Count == 0 && pageAlerts.Titles.Count == 0,
                "The local rebind changed owner, leaked a lease, contacted a provider, or raised an alert.");
            foreach (string phase in new[] { "post-resources-load", "post-resources-revalidate", "post-resources-page-appearance" })
                trace.RequireSuccessfulLeasedLoad(phase);
            if (afterBaseline is not null)
                await afterBaseline(runtime, account, probe, trace, issued);

            async Task Appear(NativePageBase page)
                => await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
            WorkspaceStoredDocument Cold()
            {
                var read = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id);
                Require(read.Success && read.Value is not null, "Actual local cold read failed: " + read.Outcome);
                return read.Value!;
            }
            void RequireDisplay(long content, long saved)
            {
                var display = runtime.Coordinator.State;
                Require(display.WorkspaceId == runtime.Id && display.ContentRevision == content && display.SavedRevision == saved
                    && display.DisplayOwnerContext == owner && display.Session.OwnerContext == owner
                    && display.Profile?.Created == false && display.CreationWizard is not null
                    && !display.IsBusy && display.Error is null, "Actual owner/display/revision hydration failed.");
            }
            void RequireReady(CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> result, long content, long saved)
                => Require(result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null }
                    && result.Value.Binding.ContentRevision == content && result.Value.Binding.SavedRevision == saved
                    && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(result.Value)
                    && CreationPrerequisitePhoneAuthority.IsReady(result.Value, runtime.Coordinator.State),
                    "Real prerequisite load failed: " + result.Outcome + " / " + string.Join(",", result.Blockers));
            void RecordCold(string operation, WorkspaceStoredDocument stored)
                => trace.Record(operation, "cold-observation", false, stored);
        }
        finally { trace.Dump(); }
    }

    private static string RebindRawDigest(string xml)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(xml)));

    private sealed class ResourcesRebindTrace
    {
        private readonly List<ResourcesRebindObservation> _observations = [];
        internal string Phase { get; set; } = "setup";
        internal void Record(string operation, string outcome, bool lease, WorkspaceStoredDocument? row = null,
            bool? ownerMatchedBefore = null, bool? ownerMatchedAfter = null)
        {
            lock (_observations)
                _observations.Add(new(Phase, operation, outcome, lease, Environment.CurrentManagedThreadId,
                    row?.ContentRevision, row?.SavedRevision,
                    row is null ? null : RebindRawDigest(row.Document.Content), row?.Document.AuxiliaryStateDigest,
                    ownerMatchedBefore, ownerMatchedAfter));
        }
        internal void RequireSuccessfulLeasedLoad(string phase)
        {
            ResourcesRebindObservation[] rows;
            lock (_observations) rows = _observations.Where(row => row.Phase == phase).ToArray();
            Require(rows.Count(row => row.Operation == "owner-admission" && row.Outcome == "accepted" && row.Lease
                    && row.OwnerMatchedBefore == true && row.OwnerMatchedAfter == true) == 1
                && rows.Count(row => row.Operation == "core-get-local" && row.Outcome == "Success" && row.Lease
                    && row.Content == 3 && row.Saved == 3) == 1
                && rows.Count(row => row.Operation == "core-load" && row.Outcome == CharacterCreationFoundationOutcomes.Success) == 1,
                "Expected one genuine Android admission, consumed Core3/3 Get, and successful Load: " + phase);
        }
        internal void Dump()
        {
            lock (_observations)
                Console.WriteLine("RESOURCES_PREREQUISITE_REBIND " + JsonSerializer.Serialize(_observations));
        }
    }

    private sealed record ResourcesRebindObservation(string Phase, string Operation, string Outcome, bool Lease,
        int Thread, long? Content, long? Saved, string? RawDigest, string? AuxiliaryDigest,
        bool? OwnerMatchedBefore, bool? OwnerMatchedAfter);

    private sealed class ResourcesRebindOwnerProbe(IOwnerContextLeaseAccessor actual, ResourcesRebindTrace trace)
        : IOwnerContextLeaseAccessor
    {
        private int _thread;
        internal int ActiveLeases { get; private set; }
        internal bool IsLeasedThread => ActiveLeases == 1 && _thread == Environment.CurrentManagedThreadId;
        public OwnerScope Current => actual.Current;
        public OwnerContextStamp Capture() => actual.Capture();
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            bool before = actual.Capture() == expected;
            bool accepted = actual.TryAcquire(expected, out var realLease);
            bool after = actual.Capture() == expected;
            trace.Record("owner-admission", accepted ? "accepted" : "rejected", accepted,
                ownerMatchedBefore: before, ownerMatchedAfter: after);
            lease = null;
            if (!accepted) return false;
            Require(realLease is not null && realLease.Stamp == expected && ActiveLeases == 0,
                "The actual Android authority returned an inconsistent lease.");
            _thread = Environment.CurrentManagedThreadId; ActiveLeases = 1;
            lease = new ForwardedLease(realLease!, this, trace);
            return true;
        }
        private sealed class ForwardedLease(IOwnerContextLease actual, ResourcesRebindOwnerProbe owner,
            ResourcesRebindTrace trace) : IOwnerContextLease
        {
            public OwnerContextStamp Stamp => actual.Stamp;
            public void Dispose()
            {
                actual.Dispose();
                owner.ActiveLeases = 0; owner._thread = 0;
                trace.Record("owner-release", "released", false);
            }
        }
    }

    private sealed class ResourcesRebindCoreProbe : IOwnerBoundCharacterCreationPrerequisiteService
    {
        private IOwnerBoundCharacterCreationPrerequisiteService _actual;
        private readonly ResourcesRebindTrace _trace;
        private readonly IssuedPageUiContext _ui;
        internal int LoadCalls { get; private set; }
        internal ResourcesRebindCoreProbe(IOwnerBoundCharacterCreationPrerequisiteService actual,
            ResourcesRebindTrace trace, IssuedPageUiContext ui)
        {
            Require(actual is OwnerBoundCharacterCreationPrerequisiteService, "SETUP: actual Core prerequisite adapter required.");
            _actual = actual; _trace = trace; _ui = ui;
        }
        internal void Observe(OwnerBoundCharacterCreationPrerequisiteService observed) => _actual = observed;
        public CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> Load(OwnerContextStamp owner,
            CharacterCreationPrerequisiteLoadRequest request)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, _ui), "Core Load ran on the UI pump.");
            LoadCalls++;
            try
            {
                var result = _actual.Load(owner, request);
                _trace.Record("core-load", result.Outcome, false);
                return result;
            }
            catch (Exception error)
            {
                _trace.Record("core-load-threw", error.GetType().Name, false);
                throw;
            }
        }
        public CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> Preview(OwnerContextStamp owner,
            CharacterCreationPrerequisitePreviewRequest request) => _actual.Preview(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> Confirm(OwnerContextStamp owner,
            CharacterCreationPrerequisiteConfirmRequest request) => _actual.Confirm(owner, request);
    }

    private sealed class ResourcesRebindStoreProbe : IWorkspaceStore, IWorkspaceAuxiliaryStateAtomicCommitCapability,
        IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        private readonly IWorkspaceStore _actual;
        private readonly IWorkspaceAuxiliaryStateAtomicCommitCapability _local;
        private readonly IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability _owned;
        private readonly ResourcesRebindOwnerProbe _owner;
        private readonly ResourcesRebindTrace _trace;
        internal ResourcesRebindStoreProbe(IWorkspaceStore actual, ResourcesRebindOwnerProbe owner, ResourcesRebindTrace trace)
        {
            _actual = actual; _owner = owner; _trace = trace;
            _local = actual as IWorkspaceAuxiliaryStateAtomicCommitCapability
                ?? throw new InvalidOperationException("Actual local atomic capability required.");
            _owned = actual as IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
                ?? throw new InvalidOperationException("Actual owner atomic capability required.");
            Require(_local.SupportsWorkspaceAuxiliaryStateAtomicCommit && _owned.SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit,
                "Forwarding cannot manufacture an unavailable atomic capability.");
        }
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => _local.SupportsWorkspaceAuxiliaryStateAtomicCommit;
        public bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit => _owned.SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit;
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            var result = _actual.Get(id);
            _trace.Record("core-get-local", result.Outcome.ToString(), _owner.IsLeasedThread, result.Value);
            return result;
        }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
        {
            var result = _actual.Get(owner, id);
            _trace.Record("core-get-owned", result.Outcome.ToString(), _owner.IsLeasedThread, result.Value);
            return result;
        }
        public IReadOnlyList<WorkspaceStoreEntry> List() => _actual.List();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => _actual.List(owner);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => _actual.CreateWorkspaceDocument(document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => _actual.CreateWorkspaceDocument(owner, document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(CharacterWorkspaceId id, WorkspaceDocument document)
            => _actual.CreateWorkspaceDocument(id, document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document)
            => _actual.CreateWorkspaceDocument(owner, id, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document)
            => _actual.ReplaceWorkspaceDocument(id, revision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document)
            => _actual.ReplaceWorkspaceDocument(owner, id, revision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(CharacterWorkspaceId id, long revision, WorkspaceDocument document)
            => _actual.ReplaceWorkspaceDocumentAndCheckpoint(id, revision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document)
            => _actual.ReplaceWorkspaceDocumentAndCheckpoint(owner, id, revision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(CharacterWorkspaceId id,
            long revision, string digest, WorkspaceDocument document)
            => _local.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(id, revision, digest, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(OwnerScope owner,
            CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
            => _owned.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(owner, id, revision, digest, document);
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => _actual.SaveCheckpoint(id, revision);
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision) => _actual.SaveCheckpoint(owner, id, revision);
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => _actual.Delete(id, revision);
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long revision) => _actual.Delete(owner, id, revision);
    }
}
