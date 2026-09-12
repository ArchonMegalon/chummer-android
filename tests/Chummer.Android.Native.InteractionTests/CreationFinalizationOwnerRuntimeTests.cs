using System.Reflection;
using System.Security.Cryptography;
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

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunCreationFinalizationOwnerCasesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        // A missing/invalid ready fixture must stop before any owner diagnosis.
        await RunCreationFinalizationLocalBaselineAsync(contentRoot);
        var failures = new List<string>();
        foreach (string scenario in new[] { "linked-only", "linked-shadow", "queued-owner-b", "queued-owner-aba", "queued-owner-b-view",
                     "stale-review-b", "stale-review-aba", "stale-view-reload" })
        {
            try { await RunCreationFinalizationLinkedAsync(contentRoot, scenario); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { failures.Add(scenario + ": " + error.Message); }
        }
        foreach (string fault in new[] { "cancellation", "owner-switch", "subscriber" })
        {
            try { await RunFinalizationPostCommitFaultAsync(contentRoot, fault); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { failures.Add(fault + ": " + error.Message); }
        }
        Require(failures.Count == 0, "Native finalization original-owner regressions:\n" + string.Join("\n", failures));
    }

    private static async Task RunCreationFinalizationLocalBaselineAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        var uiContext = new SynchronizationContext();
        FinalizationThreadProbe? threadProbe = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationFinalization: true,
            finalizationDecorator: actual => threadProbe = new FinalizationThreadProbe(actual, uiContext));
        Require(owners.Current == OwnerScope.LocalSingleUser, "Local positive control did not retain the trusted local scope.");
        var before = PrepareActualFinalizationReadyContext(runtime);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var loaded = runtime.Coordinator.LoadCreationFinalization();
        Require(loaded.Value is { CanReview: true }, "Local native finalization Load failed: " + JsonSerializer.Serialize(loaded));
        var review = await StartFromUiContext(uiContext,
            () => runtime.Coordinator.ReviewCreationFinalizationAsync(loaded.Value!.Binding));
        Require(review.Value is { CanConfirm: true, Plan: not null },
            "Local native finalization Review failed: " + JsonSerializer.Serialize(review));
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        Require(FinalizationDocumentDigest(store.Get(runtime.Id).Value!) == FinalizationDocumentDigest(before),
            "Finalization Load/Review mutated the local ready fixture.");
        var applied = await StartFromUiContext(uiContext,
            () => runtime.Coordinator.ConfirmCreationFinalizationAsync(review.Value!, "native-finalization-local-baseline"));
        Require(threadProbe is { OffContextReviews: 1, OffContextConfirms: 1 },
            "Actual Core finalization did not leave the caller's UI synchronization context.");
        var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        Require(applied.Outcome == CharacterCreationFinalizationOutcomes.Applied && applied.Value is not null,
            "Local native finalization Confirm failed: " + JsonSerializer.Serialize(applied));
        Require(cold.ContentRevision == before.ContentRevision + 1 && cold.SavedRevision == cold.ContentRevision
            && cold.Document.AuxiliaryState.CharacterCreationFinalizationReceipts?.Single().Receipt.ReceiptDigest == applied.Value!.ReceiptDigest
            && runtime.Coordinator.State.Profile?.Created == true
            && runtime.Coordinator.State.DisplayOwnerContext == owners.Capture()
            && runtime.Coordinator.State.Session.OwnerContext == owners.Capture()
            && runtime.Coordinator.State.ContentRevision == cold.ContentRevision
            && runtime.Coordinator.State.SavedRevision == cold.SavedRevision,
            "Local baseline did not preserve the actual atomic finalization receipt/checkpoint and live owner.");
        Console.WriteLine("FINALIZATION_LOCAL_BASELINE " + JsonSerializer.Serialize(new
        {
            applied.Outcome, before.ContentRevision, before.SavedRevision,
            afterContentRevision = cold.ContentRevision, afterSavedRevision = cold.SavedRevision,
            receiptDigest = applied.Value!.ReceiptDigest,
            beforeDigest = FinalizationDocumentDigest(before), afterDigest = FinalizationDocumentDigest(cold)
        }));
        Console.WriteLine("PASS actual local native/Core finalization baseline");
    }

    private static async Task RunCreationFinalizationLinkedAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationFinalization: true);
        var ready = PrepareActualFinalizationReadyContext(runtime);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, ready);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerB, ready);
        if (scenario == "linked-only")
            Require(store.Delete(runtime.Id, ready.ContentRevision).Success, "Could not remove only the temporary local fixture.");
        owners.Set(ContactsOwnerA);
        await HydrateFinalizationOwnerAsync(runtime, owners, ready);
        var originalOwner = owners.Capture();
        var originalDisplay = runtime.Coordinator.State;
        var before = ColdPartitions();
        var loaded = runtime.Coordinator.LoadCreationFinalization();
        var reviewed = loaded.Value is { CanReview: true }
            ? runtime.Coordinator.ReviewCreationFinalization(loaded.Value.Binding) : null;
        Require(ColdPartitions().All(pair => pair.Value == before[pair.Key]),
            "Native finalization Load/Review mutated an owner partition.");
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>? result = null;
        bool confirmAttempted = false;
        Exception? dispatchError = null;
        Exception? setupError = null;
        if (reviewed?.Value is { CanConfirm: true, Plan: not null } review)
        {
            if (scenario.StartsWith("stale-", StringComparison.Ordinal))
            {
                if (scenario != "stale-view-reload")
                {
                    owners.Set(ContactsOwnerB);
                    if (scenario == "stale-review-aba") owners.Set(ContactsOwnerA);
                }
                await HydrateFinalizationOwnerAsync(runtime, owners, ready);
                Require(!ReferenceEquals(originalDisplay.Profile, runtime.Coordinator.State.Profile),
                    "Stale-view fixture did not actually replace the original display.");
                var lateLoad = runtime.Coordinator.LoadCreationFinalization(originalDisplay);
                Require(lateLoad.Value is null, "Delayed background load rebound an original display to its replacement.");
                Require(runtime.Coordinator.ReviewCreationFinalization(loaded.Value!.Binding).Value is null,
                    "Old loaded binding authorized a new review after display replacement.");
            }
            var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            await gate.WaitAsync();
            Task<CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>>? pending = null;
            try
            {
                pending = runtime.Coordinator.ConfirmCreationFinalizationAsync(review, "native-finalization-" + scenario);
                confirmAttempted = true;
                Require(!pending.IsCompleted, "Finalization did not wait at the actual activation gate.");
                if (scenario.StartsWith("queued-", StringComparison.Ordinal))
                {
                    owners.Set(ContactsOwnerB);
                    if (scenario == "queued-owner-aba") owners.Set(ContactsOwnerA);
                    if (scenario == "queued-owner-b-view")
                        await HydrateFinalizationOwnerAsync(runtime, owners, ready);
                    else
                        Require(runtime.Coordinator.State.DisplayOwnerContext == originalOwner,
                            "Stale-display fixture unexpectedly rebound before gate release.");
                    Require(owners.Capture() != originalOwner, "Owner epoch never changed in queued regression.");
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { setupError = error; }
            finally { gate.Release(); }
            // Always join and cold-read even if refresh throws after a real commit.
            if (pending is not null)
                try { result = await pending.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception error) when (error is not OutOfMemoryException) { dispatchError = error; }
        }
        var after = ColdPartitions();
        var coldStore = new FileWorkspaceStore(runtime.StateDirectory);
        var afterRows = new Dictionary<string, WorkspaceStoreReadResult>
        {
            ["local"] = coldStore.Get(runtime.Id), ["account-a"] = coldStore.Get(ContactsOwnerA, runtime.Id),
            ["account-b"] = coldStore.Get(ContactsOwnerB, runtime.Id)
        };
        Console.WriteLine("FINALIZATION_OWNER_OBSERVATION " + JsonSerializer.Serialize(new
        {
            scenario, originalOwner, liveOwner = owners.Capture(), displayOwner = runtime.Coordinator.State.DisplayOwnerContext,
            sessionOwner = runtime.Coordinator.State.Session.OwnerContext,
            loadOutcome = loaded.Outcome, loadBlockers = loaded.Blockers,
            reviewOutcome = reviewed?.Outcome, reviewBlockers = reviewed?.Blockers,
            confirmAttempted, confirmOutcome = result?.Outcome,
            confirmBlockers = result?.Blockers, receipt = result?.Value,
            setupError = setupError?.Message, dispatchError = dispatchError?.GetType().Name,
            changed = after.Keys.Where(key => after[key] != before[key]).ToArray(), before, after,
            cold = afterRows.ToDictionary(pair => pair.Key, pair => new
            {
                pair.Value.Outcome, pair.Value.Value?.ContentRevision, pair.Value.Value?.SavedRevision,
                finalizationReceipts = pair.Value.Value?.Document.AuxiliaryState.CharacterCreationFinalizationReceipts
            })
        }));
        Require(setupError is null, "Queued fixture failed before release: " + setupError?.Message);
        Require(owners.ActiveLeases == 0, "Fixture retained an owner lease after dispatch.");
        if (scenario.StartsWith("queued-", StringComparison.Ordinal) || scenario.StartsWith("stale-", StringComparison.Ordinal))
        {
            Require(loaded.Value is { CanReview: true } && reviewed?.Value is { CanConfirm: true, Plan: not null }
                && confirmAttempted,
                "Queued regression never dispatched an actual confirmable native/Core finalization review.");
            Require(after.All(pair => pair.Value == before[pair.Key]) && result?.Value is null,
                "Retired original-owner finalization mutated a partition or returned an applied receipt.");
        }
        else
        {
            Require(loaded.Value is { CanReview: true } && reviewed?.Value is { CanConfirm: true },
                "A canonically ready linked runner is unavailable while the exact same fixture passes local finalization.");
            Require(after["local"] == before["local"] && after["account-b"] == before["account-b"]
                && after["account-a"] != before["account-a"] && result?.Value is not null
                && afterRows["account-a"].Value?.ContentRevision == ready.ContentRevision + 1
                && afterRows["account-a"].Value?.SavedRevision == ready.ContentRevision + 1
                && afterRows["account-a"].Value?.Document.AuxiliaryState.CharacterCreationFinalizationReceipts?
                    .Single().Receipt.ReceiptDigest == result?.Value?.ReceiptDigest,
                "Finalization did not atomically affect only the admitted linked owner's exact ready runner.");
            Require(dispatchError is null, "Linked finalization lost its observed result after commit.");
        }
        Console.WriteLine("PASS actual finalization original-owner boundary: " + scenario);

        Dictionary<string, string> ColdPartitions()
        {
            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            return new()
            {
                ["local"] = Describe(cold.Get(runtime.Id)), ["account-a"] = Describe(cold.Get(ContactsOwnerA, runtime.Id)),
                ["account-b"] = Describe(cold.Get(ContactsOwnerB, runtime.Id))
            };
        }
        static string Describe(WorkspaceStoreReadResult read) => read.Value is { } row
            ? row.ContentRevision + "/" + row.SavedRevision + ":" + FinalizationDocumentDigest(row) : read.Outcome.ToString();
    }

    private static async Task RunFinalizationPostCommitFaultAsync(string contentRoot, string fault)
    {
        using var cancellation = new CancellationTokenSource();
        var owners = new ControlledLinkedOwner();
        RunnerSessionCoordinator? coordinator = null;
        int observedCommits = 0;
        EventHandler throwing = (_, _) => throw new IOException("Injected postcommit notification failure.");
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationFinalization: true, finalizationDecorator: actual => new FinalizationPostCommitProbe(actual, () =>
            {
                observedCommits++;
                if (fault == "cancellation") cancellation.Cancel();
                else if (fault == "owner-switch") owners.Set(ContactsOwnerB);
                else coordinator!.Changed += throwing;
            }));
        coordinator = runtime.Coordinator;
        var ready = PrepareActualFinalizationReadyContext(runtime);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, ready);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerB, ready);
        owners.Set(ContactsOwnerA);
        await HydrateFinalizationOwnerAsync(runtime, owners, ready);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        string localBefore = FinalizationDocumentDigest(store.Get(runtime.Id).Value!);
        string bBefore = FinalizationDocumentDigest(store.Get(ContactsOwnerB, runtime.Id).Value!);
        var loaded = coordinator.LoadCreationFinalization();
        Require(loaded.Value is { CanReview: true }, "Postcommit fixture did not load a real ready draft.");
        var reviewed = coordinator.ReviewCreationFinalization(loaded.Value!.Binding);
        Require(reviewed.Value is { CanConfirm: true, Plan: not null }, "Postcommit fixture has no actual reviewed plan.");
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> result;
        try
        {
            result = await coordinator.ConfirmCreationFinalizationAsync(
                reviewed.Value!, "postcommit-finalization-" + fault, cancellation.Token);
        }
        finally { coordinator.Changed -= throwing; }
        var cold = new FileWorkspaceStore(runtime.StateDirectory);
        var committed = cold.Get(ContactsOwnerA, runtime.Id).Value!;
        Require(observedCommits == 1 && result.Value is not null
            && result.Outcome == CharacterCreationFinalizationOutcomes.Applied
            && result.Blockers.Contains(CharacterCreationFinalizationBlockers.PostCommitReopenRequired)
            && committed.ContentRevision == ready.ContentRevision + 1
            && committed.SavedRevision == committed.ContentRevision
            && committed.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt == result.Value,
            "A postcommit fault lost the exact real receipt or replayed the mutation.");
        Require(FinalizationDocumentDigest(cold.Get(runtime.Id).Value!) == localBefore
            && FinalizationDocumentDigest(cold.Get(ContactsOwnerB, runtime.Id).Value!) == bBefore,
            "Postcommit recovery changed another owner's runner.");
        if (fault == "owner-switch")
            Require(!coordinator.CanDisplayCreationFinalizationReceipt(result.Value!), "Receipt leaked into the new account's display.");
        Require(owners.ActiveLeases == 0, "Postcommit fault retained an owner lease.");
        Console.WriteLine("PASS actual native finalization postcommit fault: " + fault);
    }

    private static Task<T> StartFromUiContext<T>(SynchronizationContext uiContext, Func<Task<T>> start)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(uiContext);
            return start();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    // Delegates to real Core; observes the synchronization boundary, not a fake
    // rule result. ControlledLinkedOwner independently checks lease thread affinity.
    private sealed class FinalizationThreadProbe(
        IOwnerBoundCharacterCreationFinalizationService actual, SynchronizationContext uiContext)
        : IOwnerBoundCharacterCreationFinalizationService
    {
        public int OffContextReviews { get; private set; }
        public int OffContextConfirms { get; private set; }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
            OwnerContextStamp owner, CharacterCreationFinalizationLoadRequest request) => actual.Load(owner, request);
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
            OwnerContextStamp owner, CharacterCreationFinalizationReviewRequest request)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, uiContext),
                "Core Review ran synchronously on the caller's UI context.");
            OffContextReviews++;
            return actual.Review(owner, request);
        }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
            OwnerContextStamp owner, CharacterCreationFinalizationConfirmRequest request)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, uiContext),
                "Core Confirm ran synchronously on the caller's UI context.");
            OffContextConfirms++;
            return actual.Confirm(owner, request);
        }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
            OwnerContextStamp owner, CharacterCreationFinalizationReceiptLookupRequest request) => actual.LookupReceipt(owner, request);
    }

    private sealed class FinalizationPostCommitProbe(
        IOwnerBoundCharacterCreationFinalizationService actual, Action afterActualCommit)
        : IOwnerBoundCharacterCreationFinalizationService
    {
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
            OwnerContextStamp owner, CharacterCreationFinalizationLoadRequest request) => actual.Load(owner, request);
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
            OwnerContextStamp owner, CharacterCreationFinalizationReviewRequest request) => actual.Review(owner, request);
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
            OwnerContextStamp owner, CharacterCreationFinalizationConfirmRequest request)
        {
            var result = actual.Confirm(owner, request);
            Require(result.Outcome == CharacterCreationFinalizationOutcomes.Applied && result.Value is not null,
                "Fault injection requires an actual durable Core commit.");
            afterActualCommit();
            return result;
        }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
            OwnerContextStamp owner, CharacterCreationFinalizationReceiptLookupRequest request)
            => actual.LookupReceipt(owner, request);
    }

    private static void CloneFinalizationRecordFixture(NativeRewardRuntime runtime, OwnerScope destination,
        WorkspaceStoredDocument canonical)
    {
        // TEST STORAGE CONSTRUCTION ONLY, NOT authenticated cross-owner restore.
        // Core creates the private destination/permissions; copy the already-real
        // record byte-for-byte, without minting drafts, receipts, revisions or plans.
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        string source = Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json");
        var prior = Directory.GetFiles(runtime.StateDirectory, runtime.Id.Value + ".json", SearchOption.AllDirectories)
            .ToHashSet(StringComparer.Ordinal);
        var placeholder = canonical.Document with { State = canonical.Document.State with
            { AuxiliaryState = new WorkspaceDocumentAuxiliaryState() } };
        Require(store.CreateWorkspaceDocument(destination, runtime.Id, placeholder).Success,
            "Core could not create the private fixture destination.");
        string target = Directory.GetFiles(runtime.StateDirectory, runtime.Id.Value + ".json", SearchOption.AllDirectories)
            .Single(path => !prior.Contains(path));
        Require(File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint) == false
            && File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint) == false,
            "Fixture paths must be real private files.");
        byte[] original = File.ReadAllBytes(source);
        File.WriteAllBytes(target, original);
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        Require(File.ReadAllBytes(target).SequenceEqual(original), "Fixture record copy changed canonical bytes.");
        var copied = new FileWorkspaceStore(runtime.StateDirectory).Get(destination, runtime.Id);
        Require(copied.Success && FinalizationDocumentDigest(copied.Value!) == FinalizationDocumentDigest(canonical)
            && copied.Value!.LastUpdatedUtc == canonical.LastUpdatedUtc,
            "Cold owner-scoped read rejected or changed the complete real ready record.");
    }

    private static async Task HydrateFinalizationOwnerAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, WorkspaceStoredDocument expected)
    {
        // Same real lifecycle order as native owner initialization. A same-ID
        // Switch shortcut can legitimately skip Load after Initialize.
        await runtime.Shell.InitializeAsync(default);
        await runtime.Presenter.InitializeAsync(default);
        await runtime.Presenter.LoadAsync(expected.Id, default);
        var state = runtime.Coordinator.State;
        Require(state.WorkspaceId == expected.Id && state.Session.ActiveWorkspaceId == expected.Id
            && state.Profile?.Created == false && state.Error is null && !state.IsBusy
            && state.DisplayOwnerContext == owners.Capture() && state.Session.OwnerContext == owners.Capture()
            && runtime.Shell.State.OwnerContext == owners.Capture()
            && state.ContentRevision == expected.ContentRevision && state.SavedRevision == expected.SavedRevision,
            "Finalization fixture never established exact actual native owner/session/revisions: " + JsonSerializer.Serialize(new
            { state.WorkspaceId, state.ContentRevision, state.SavedRevision, state.DisplayOwnerContext, state.Error }));
    }

    private static string FinalizationDocumentDigest(WorkspaceStoredDocument stored) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { stored.Id, stored.Document, stored.ContentRevision, stored.SavedRevision }))).ToLowerInvariant();

    private static WorkspaceStoredDocument PrepareActualFinalizationReadyContext(NativeRewardRuntime runtime)
    {
        // Test fixture adapted from Core f750 CharacterCreationFinalizationServiceTests.ReadyContext:
        // canonical Priority/Human/Mundane, actual services issue every draft and receipt.
        // No manual auxiliary state, fake admission, finalization plan or receipt.
        var services = runtime.Services;
        var store = services.GetRequiredService<IWorkspaceStore>();
        var bootstrap = services.GetRequiredService<ICharacterCreationBootstrapService>();
        Require(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            CharacterCreationBuildMethods.Priority, out string profile), "Canonical Priority profile missing.");
        var created = bootstrap.Create(new(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, "sr5", "Finalization Runner", "Finalizer",
            CharacterCreationBuildMethods.Priority, profile));
        Require(created.Outcome == CharacterCreationBootstrapOutcomes.Success && created.Value is not null,
            "Actual Bootstrap failed: " + JsonSerializer.Serialize(created));
        runtime.Id = created.Value!.WorkspaceId;
        var prerequisites = services.GetRequiredService<ICharacterCreationPrerequisiteService>();
        var initial = prerequisites.Load(new(runtime.Id)).Value!;
        var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CharacterCreationPriorityCategoryIds.Heritage] = "A",
            [CharacterCreationPriorityCategoryIds.Talent] = "E",
            [CharacterCreationPriorityCategoryIds.Attributes] = "B",
            [CharacterCreationPriorityCategoryIds.Skills] = "C",
            [CharacterCreationPriorityCategoryIds.Resources] = "D"
        };
        var human = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && item.Rank == "A").HeritageOptions.First(item => item.IsEnabled && item.MetavariantSourceId is null
                && item.MetatypeName == "Human");
        var mundane = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && item.Rank == "E").TalentOptions.First(item => item.IsEnabled
                && item.Value.Equals(CharacterCreationMagicResonanceKinds.Mundane, StringComparison.OrdinalIgnoreCase)
                && item.Magic is null && item.Resonance is null && item.Depth is null
                && item.ActiveSkillGrant is null && item.SkillGroupGrant is null);
        var prerequisitePreview = prerequisites.Preview(new(initial.Binding, ranks)
            { HeritageSelectionId = human.SelectionId, TalentSelectionId = mundane.SelectionId }).Value!;
        var prerequisiteReceipt = prerequisites.Confirm(new(prerequisitePreview.Binding, ranks,
            prerequisitePreview.PreviewDigest, ExplicitlyConfirmed: true)
            { HeritageSelectionId = human.SelectionId, TalentSelectionId = mundane.SelectionId });
        Require(prerequisiteReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual prerequisite failed: " + JsonSerializer.Serialize(prerequisiteReceipt));
        var attributes = services.GetRequiredService<ICharacterCreationAttributesService>();
        var attributePreview = attributes.Preview(new(attributes.Load(new(runtime.Id)).Value!.Binding, [])).Value!;
        var attributeReceipt = attributes.Confirm(new(attributePreview.Binding, [], attributePreview.PreviewDigest, true));
        Require(attributeReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual Attributes failed: " + JsonSerializer.Serialize(attributeReceipt));
        var skills = services.GetRequiredService<ICharacterCreationSkillsService>();
        var skillState = skills.Load(new(runtime.Id)).Value!;
        var native = skillState.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
        CharacterCreationSkillAllocation[] allocations =
            [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
        var skillPreview = skills.Preview(new(skillState.Binding, allocations, [])).Value!;
        var skillReceipt = skills.Confirm(new(skillPreview.Binding, allocations, [], skillPreview.PreviewDigest,
            "native-finalization-skills", true));
        Require(skillReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual Skills failed: " + JsonSerializer.Serialize(skillReceipt));
        var qualities = services.GetRequiredService<ICharacterCreationQualitiesService>();
        var qualityPreview = qualities.Preview(new(qualities.Load(new(runtime.Id)).Value!.Binding, [])).Value!;
        var qualityReceipt = qualities.Confirm(new(qualityPreview.Binding, [], qualityPreview.PreviewDigest,
            "native-finalization-qualities", Guid.NewGuid(), true));
        Require(qualityReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual Qualities failed: " + JsonSerializer.Serialize(qualityReceipt));
        var resources = services.GetRequiredService<ICharacterCreationResourcesService>();
        var resourcesState = resources.Load(new(runtime.Id)).Value!;
        var zero = resourcesState.Options.First(item => item.IsEnabled && item.KarmaInvestment == 0);
        var resourcePreview = resources.Preview(new(resourcesState.Binding, zero.OptionId)).Value!;
        var resourceReceipt = resources.Confirm(new(resourcePreview.Binding, zero.OptionId, resourcePreview.PreviewDigest,
            "native-finalization-resources", true));
        Require(resourceReceipt.Outcome == CharacterCreationResourcesOutcomes.Applied,
            "Actual Resources failed: " + JsonSerializer.Serialize(resourceReceipt));
        var gear = services.GetRequiredService<ICharacterCreationGearService>();
        var gearPreview = gear.Preview(new(gear.Load(new(runtime.Id)).Value!.Binding, [])).Value!;
        var gearReceipt = gear.Confirm(new(gearPreview.Binding, [], gearPreview.PreviewDigest, "native-finalization-gear", true));
        Require(gearReceipt.Outcome == CharacterCreationGearOutcomes.Applied,
            "Actual Gear failed: " + JsonSerializer.Serialize(gearReceipt));
        var finalizer = services.GetRequiredService<ICharacterCreationFinalizationService>();
        var loaded = finalizer.Load(new(runtime.Id));
        Require(loaded.Value is { CanReview: true }, "Core ReadyContext is not ready: " + JsonSerializer.Serialize(loaded));
        var preview = finalizer.Review(new(loaded.Value!.Binding));
        Require(preview.Value is { CanConfirm: true, Plan: not null },
            "Core ReadyContext is not confirmable: " + JsonSerializer.Serialize(preview));
        var read = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id);
        Require(read.Success && read.Value is not null, "Canonical ready fixture cold read failed: " + read.Error);
        var stored = read.Value!;
        Require(stored.ContentRevision > 0 && stored.SavedRevision == stored.ContentRevision
            && stored.Document.AuxiliaryState.CharacterCreationFinalizationReceipts is null,
            "ReadyContext is not an actual unfinalized canonical checkpoint.");
        return stored;
    }
}
