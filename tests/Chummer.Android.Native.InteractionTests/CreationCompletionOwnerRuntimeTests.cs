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
using Chummer.Presentation.Creation;
using Microsoft.Extensions.DependencyInjection;

internal static partial class AfterRunAuthorityHarness
{
    internal static Task RunCreationCompletionRecoveryAsync(string contentRoot)
        => CompletionCommittedReplyIsRecoveredAsync(contentRoot);

    internal static async Task RunCreationCompletionCoordinatorAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));

        await CompletionPreparationDoesNotSaveAsync(contentRoot);
        await CompletionExplicitApplySavesOnceAsync(contentRoot);
        await CompletionCommittedReplyIsRecoveredAsync(contentRoot);
        await CompletionQueuedOwnerAbaIsRejectedAsync(contentRoot);
        await CompletionUnknownApplyIsNotRepeatedAsync(contentRoot);
    }

    private static async Task CompletionPreparationDoesNotSaveAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        var ui = new SynchronizationContext();
        CompletionReadProbe? reader = null;
        CompletionServiceProbe? service = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationCompletion: true, creationFinalization: true, productionCreationOverview: true,
            completionReaderDecorator: actual => reader = new(actual, ui),
            completionServiceDecorator: actual => service = new(actual, ui));
        var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var files = CompletionFiles(runtime);

        var loaded = await StartFromUiContext(ui, () => runtime.Coordinator.LoadCreationCompletionInputsAsync());
        Require(loaded.Outcome == CharacterCreationFoundationOutcomes.Success && loaded.Value is not null,
            "An actual unfinished runner could not load completion inputs: " + string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        Require(runtime.Coordinator.IsCreationCompletionInputsCurrent(inputs),
            "Completion did not retain the original owner-bound input read.");
        var intent = CompletionIntent(owners.Capture(), inputs);
        string inputBytes = JsonSerializer.Serialize(inputs);
        string intentBytes = JsonSerializer.Serialize(intent);
        var built = await StartFromUiContext(ui,
            () => runtime.Coordinator.BuildCreationCompletionProposalAsync(inputs, intent));
        Require(built.Value is { CanReview: true } proposal
            && runtime.Coordinator.IsCreationCompletionProposalCurrent(proposal)
            && proposal.AllocationPolicy == CompletionAllocationPolicy.MissingOnly
            && proposal.Intent.OwnerContext == owners.Capture()
            && proposal.Intent.InputsSnapshotDigest == inputs.SnapshotDigest,
            "The starter proposal lost its exact original input or owner: " + string.Join(",", built.Blockers));
        Require(JsonSerializer.Serialize(inputs) == inputBytes && JsonSerializer.Serialize(intent) == intentBytes,
            "Preparing a proposal mutated the caller's original inputs or intent.");
        var wrongSnapshot = await runtime.Coordinator.BuildCreationCompletionProposalAsync(
            inputs, intent with { InputsSnapshotDigest = new string('0', 64) });
        Require(wrongSnapshot.Value is null && wrongSnapshot.Outcome != CharacterCreationFoundationOutcomes.Success,
            "Missing-only suggestions accepted intent for a different source-bound input snapshot.");
        Require(reader!.Calls > 0 && service!.ApplyCalls == 0,
            "Preparing a proposal must use the real reader without dispatching an Apply.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        int readsBeforeCancellation = reader.Calls;
        Func<Task>[] canceledWork =
        [
            () => runtime.Coordinator.LoadCreationCompletionInputsAsync(canceled.Token),
            () => runtime.Coordinator.BuildCreationCompletionProposalAsync(inputs, intent, canceled.Token)
        ];
        foreach (var start in canceledWork)
        {
            try
            {
                await start();
                throw new InvalidOperationException("Already-canceled completion preparation was not canceled.");
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        }
        Require(reader.Calls == readsBeforeCancellation,
            "Already-canceled completion preparation reached the Core reader.");
        AssertCompletionFilesUnchanged(runtime, files);
        Require(FinalizationDocumentDigest(new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!)
            == FinalizationDocumentDigest(before) && owners.ActiveLeases == 0,
            "Completion preparation changed the saved unfinished runner or retained an owner lease.");
        Console.WriteLine("PASS completion preparation keeps actual unfinished runner unchanged");
    }

    private static async Task CompletionExplicitApplySavesOnceAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        var ui = new SynchronizationContext();
        CompletionServiceProbe? service = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationCompletion: true, creationFinalization: true, productionCreationOverview: true,
            completionServiceDecorator: actual => service = new(actual, ui));
        var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var files = CompletionFiles(runtime);
        var (_, _, review) = await PrepareCompletionReviewAsync(runtime, owners, ui);
        AssertCompletionFilesUnchanged(runtime, files);

        const string key = "native-completion-explicit-once";
        var refused = await StartFromUiContext(ui,
            () => runtime.Coordinator.ApplyCreationCompletionAsync(review, key, explicitlyConfirmed: false));
        Require(refused.Value is null
            && refused.Blockers.Contains(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired)
            && service!.ApplyCalls == 0 && runtime.Coordinator.IsCreationCompletionReviewCurrent(review),
            "An unconfirmed completion reached Core or consumed the still-unconfirmed review.");
        AssertCompletionFilesUnchanged(runtime, files);

        var applied = await StartFromUiContext(ui,
            () => runtime.Coordinator.ApplyCreationCompletionAsync(review, key, explicitlyConfirmed: true));
        Require(applied.Outcome == CharacterCreationFoundationOutcomes.Success && applied.Value is not null,
            "Confirmed completion did not produce a durable receipt: " + string.Join(",", applied.Blockers));
        var receipt = applied.Value!;
        var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        Require(service!.ApplyCalls == 1 && cold.ContentRevision == before.ContentRevision + 1
            && cold.SavedRevision == cold.ContentRevision
            && cold.Document.Content == review.Projection.CharacterXml
            && cold.Document.AuxiliaryState.CharacterCreationFinalizationReceipts?.Single().Receipt.ReceiptDigest
                == receipt.ReceiptDigest
            && receipt.ContentRevision == cold.ContentRevision && receipt.SavedRevision == cold.SavedRevision,
            "Completion did not atomically persist the reviewed character and one receipt at one new revision.");
        Require(runtime.Coordinator.State.Profile?.Created == true
            && runtime.Coordinator.IsCreationFinalizationReceiptCurrent(receipt),
            "A known completion receipt did not reopen the exact saved Career runner.");

        var committed = CompletionFiles(runtime);
        var repeat = await runtime.Coordinator.ApplyCreationCompletionAsync(review, key, explicitlyConfirmed: true);
        Require(service.ApplyCalls == 1
            && (repeat.Value is null || repeat.Value.ReceiptDigest == receipt.ReceiptDigest),
            "Reusing a consumed completion review dispatched another Apply or returned a different receipt.");
        AssertCompletionFilesUnchanged(runtime, committed);
        Require(owners.ActiveLeases == 0, "Completion Apply retained an owner lease.");
        Console.WriteLine("PASS explicit completion atomically saves once and reopens Career");
    }

    private static async Task CompletionCommittedReplyIsRecoveredAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        var ui = new SynchronizationContext();
        using var canceledAfterCommit = new CancellationTokenSource();
        CompletionServiceProbe? service = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationCompletion: true, creationFinalization: true, productionCreationOverview: true,
            completionServiceDecorator: actual => service = new(actual, ui)
            {
                ThrowAfterApply = true,
                CancelAfterCommit = canceledAfterCommit
            });
        var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
        owners.Set(ContactsOwnerA);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var (_, _, review) = await PrepareCompletionReviewAsync(runtime, owners, ui);
        const string key = "native-completion-committed-reply-lost";

        var recovered = await StartFromUiContext(ui,
            () => runtime.Coordinator.ApplyCreationCompletionAsync(review, key,
                explicitlyConfirmed: true, cancellationToken: canceledAfterCommit.Token));
        Require(canceledAfterCommit.IsCancellationRequested && service!.ApplyCalls == 1
            && recovered.Outcome == CharacterCreationFoundationOutcomes.Success && recovered.Value is not null,
            "A lost reply and post-commit cancellation hid the actual saved completion receipt.");
        var receipt = recovered.Value!;
        var coldRead = new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, runtime.Id);
        Require(coldRead.Success && coldRead.Value is { } cold
            && cold.ContentRevision == before.ContentRevision + 1 && cold.SavedRevision == cold.ContentRevision
            && cold.Document.Content == review.Projection.CharacterXml
            && cold.Document.AuxiliaryState.CharacterCreationFinalizationReceipts?.Single().Receipt.ReceiptDigest
                == receipt.ReceiptDigest,
            "Receipt recovery did not match the one durable commit in the original linked owner partition.");
        var committed = CompletionFiles(runtime);

        // The first caller stopped waiting, but this is recovery of the exact
        // operation, not permission to allocate/finalize the character again.
        var reopened = await runtime.Coordinator.ApplyCreationCompletionAsync(review, key,
            explicitlyConfirmed: true);
        Require(reopened.Outcome == CharacterCreationFoundationOutcomes.Success
            && reopened.Value?.ReceiptDigest == receipt.ReceiptDigest && service.ApplyCalls == 1
            && runtime.Coordinator.State.Profile?.Created == true
            && runtime.Coordinator.IsCreationFinalizationReceiptCurrent(receipt),
            $"Recovering a committed completion did not reopen its exact Career state without replay: outcome={reopened.Outcome}; "
            + $"sameReceipt={reopened.Value?.ReceiptDigest == receipt.ReceiptDigest}; applies={service!.ApplyCalls}; "
            + $"created={runtime.Coordinator.State.Profile?.Created}; currentReceipt={runtime.Coordinator.IsCreationFinalizationReceiptCurrent(receipt)}; "
            + $"blockers={string.Join(',', reopened.Blockers)}; displayOwnerCurrent={runtime.Coordinator.State.DisplayOwnerContext == owners.Capture()}; "
            + $"sessionOwnerCurrent={runtime.Coordinator.State.Session.OwnerContext == owners.Capture()}; "
            + $"workspaceCurrent={runtime.Coordinator.State.WorkspaceId == receipt.WorkspaceId}; busy={runtime.Coordinator.State.IsBusy}");
        AssertCompletionFilesUnchanged(runtime, committed);
        Require(owners.ActiveLeases == 0, "Lost-reply completion recovery retained an owner lease.");
        Console.WriteLine("PASS committed completion survives lost reply and cancellation without replay");
    }

    private static async Task CompletionQueuedOwnerAbaIsRejectedAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        var ui = new SynchronizationContext();
        CompletionServiceProbe? service = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationCompletion: true, creationFinalization: true, productionCreationOverview: true,
            completionServiceDecorator: actual => service = new(actual, ui));
        var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerB, before);
        owners.Set(ContactsOwnerA);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var originalOwner = owners.Capture();
        var (inputs, proposal, review) = await PrepareCompletionReviewAsync(runtime, owners, ui);
        var files = CompletionFiles(runtime);
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        Task<CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>>? pending = null;
        bool queued = false;
        await gate.WaitAsync();
        try
        {
            pending = runtime.Coordinator.ApplyCreationCompletionAsync(review,
                "native-completion-owner-aba", explicitlyConfirmed: true);
            queued = !pending.IsCompleted;
            owners.Set(ContactsOwnerB);
            owners.Set(ContactsOwnerA);
        }
        finally { gate.Release(); }
        var rejected = await pending!.WaitAsync(TimeSpan.FromSeconds(30));
        Require(queued && owners.Capture() != originalOwner,
            "The completion regression did not queue across a real A->B->A owner generation change.");
        Require(rejected.Value is null && rejected.Outcome != CharacterCreationFoundationOutcomes.Success
            && service!.ApplyCalls == 0,
            "A queued completion borrowed the replacement owner generation and reached Core Apply.");
        Require(!runtime.Coordinator.IsCreationCompletionInputsCurrent(inputs)
            && !runtime.Coordinator.IsCreationCompletionProposalCurrent(proposal)
            && !runtime.Coordinator.IsCreationCompletionReviewCurrent(review),
            "Retained completion authority became current again after owner ABA.");
        int reviews = service.ReviewCalls;
        var staleBuild = await runtime.Coordinator.BuildCreationCompletionProposalAsync(inputs, proposal.Intent);
        var staleReview = await runtime.Coordinator.ReviewCreationCompletionAsync(proposal,
            CompletionCash(runtime, before));
        Require(staleBuild.Value is null && staleReview.Value is null && service.ReviewCalls == reviews,
            "An old input or proposal was rebound into fresh authority after owner ABA.");
        AssertCompletionFilesUnchanged(runtime, files);
        Require(owners.ActiveLeases == 0, "Rejected completion retained an owner lease.");
        Console.WriteLine("PASS queued completion rejects original-owner ABA without touching any partition");
    }

    private static async Task CompletionUnknownApplyIsNotRepeatedAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        var ui = new SynchronizationContext();
        CompletionServiceProbe? service = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationCompletion: true, creationFinalization: true, productionCreationOverview: true,
            completionServiceDecorator: actual => service = new(actual, ui) { ThrowBeforeApply = true });
        var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var (_, _, review) = await PrepareCompletionReviewAsync(runtime, owners, ui);
        var files = CompletionFiles(runtime);
        const string key = "native-completion-unknown-once";
        var unknown = await StartFromUiContext(ui,
            () => runtime.Coordinator.ApplyCreationCompletionAsync(review, key, explicitlyConfirmed: true));
        Require(unknown.Outcome == "outcome-unknown" && unknown.Value is null
            && unknown.Blockers.Contains("creation-completion-apply-outcome-unknown") && service!.ApplyCalls == 1,
            "An ambiguous completion dispatch was not retained as an unknown outcome.");
        Require(!runtime.Coordinator.IsCreationCompletionReviewCurrent(review),
            "An ambiguously dispatched completion review remained an available mutation action.");

        var sameKey = await runtime.Coordinator.ApplyCreationCompletionAsync(review, key, explicitlyConfirmed: true);
        var otherKey = await runtime.Coordinator.ApplyCreationCompletionAsync(review, key + "-changed", explicitlyConfirmed: true);
        Require(sameKey.Value is null && otherKey.Value is null && service.ApplyCalls == 1,
            "A missing receipt or changed idempotency key replayed an uncertain mutation.");
        AssertCompletionFilesUnchanged(runtime, files);
        Require(owners.ActiveLeases == 0, "Unknown completion outcome retained an owner lease.");
        Console.WriteLine("PASS unknown completion dispatch cannot be replayed with the same or a new key");
    }

    private static async Task<(CharacterCreationCompletionReviewInputs Inputs, CompletionAssistantProposal Proposal,
        CharacterCreationCompletionApplyReview Review)> PrepareCompletionReviewAsync(
        NativeRewardRuntime runtime, ControlledLinkedOwner owners, SynchronizationContext ui)
    {
        var loaded = await StartFromUiContext(ui, () => runtime.Coordinator.LoadCreationCompletionInputsAsync());
        Require(loaded.Outcome == CharacterCreationFoundationOutcomes.Success && loaded.Value is not null,
            "SETUP: real unfinished completion inputs unavailable: " + string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        var built = await StartFromUiContext(ui, () => runtime.Coordinator.BuildCreationCompletionProposalAsync(
            inputs, CompletionIntent(owners.Capture(), inputs)));
        Require(built.Value is { CanReview: true },
            "SETUP: actual starter proposal unavailable: " + string.Join(",", built.Blockers));
        var proposal = built.Value!;
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var read = owners.Current.IsLocalSingleUser ? store.Get(runtime.Id) : store.Get(owners.Current, runtime.Id);
        Require(read.Success && read.Value is not null,
            "SETUP: actual completion workspace unavailable from its original owner partition.");
        var stored = read.Value!;
        var reviewed = await StartFromUiContext(ui,
            () => runtime.Coordinator.ReviewCreationCompletionAsync(proposal, CompletionCash(runtime, stored)));
        Require(reviewed.Outcome == CharacterCreationFoundationOutcomes.Success && reviewed.Value is not null,
            "SETUP: Core did not admit the real starter proposal: " + string.Join(",", reviewed.Blockers));
        Require(runtime.Coordinator.IsCreationCompletionReviewCurrent(reviewed.Value!),
            "SETUP: Core review was not issued to the original native display.");
        return (inputs, proposal, reviewed.Value!);
    }

    private static CompletionAssistantIntent CompletionIntent(OwnerContextStamp owner,
        CharacterCreationCompletionReviewInputs inputs)
    {
        return new(owner, inputs.SnapshotDigest, CompletionAssistantRules.GeneralSurvivor,
            new([], []), new())
        {
            NativeLanguageSourceId = inputs.Skills.Authority.KnowledgeSkills
                .First(row => row.CanBeNativeLanguage).SourceSkillId
        };
    }

    private static CharacterCreationStartingCashChoice CompletionCash(NativeRewardRuntime runtime,
        WorkspaceStoredDocument stored)
    {
        var source = runtime.Services.GetRequiredService<ICharacterSourceDataResolver>()
            .TryCreateContext(stored.Document.Content);
        Require(source is not null, "SETUP: actual completion source context unavailable.");
        Require(source!.TryResolveCreationDefaultStartingNuyen(out var cash) && cash is not null,
            "SETUP: actual default starting-cash authority unavailable.");
        return new(cash!.AuthorityDigest, cash.Dice * 3);
    }

    private static Dictionary<string, string> CompletionFiles(NativeRewardRuntime runtime)
        => Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(runtime.StateDirectory, path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static void AssertCompletionFilesUnchanged(NativeRewardRuntime runtime,
        IReadOnlyDictionary<string, string> expected)
    {
        var actual = CompletionFiles(runtime);
        Require(actual.Count == expected.Count && expected.All(pair =>
                actual.TryGetValue(pair.Key, out string? digest) && digest == pair.Value),
            "Completion changed saved files during a read, rejected action or receipt-only recovery.");
    }

    private sealed class CompletionReadProbe(IOwnerBoundCharacterCreationCompletionReader actual,
        SynchronizationContext ui) : IOwnerBoundCharacterCreationCompletionReader
    {
        public int Calls { get; private set; }

        public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputs(
            OwnerContextStamp owner, CharacterCreationSkillsLoadRequest request, CancellationToken cancellationToken = default)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, ui),
                "Completion inputs were read on the caller's UI context.");
            Calls++;
            return actual.LoadCompletionInputs(owner, request, cancellationToken);
        }
    }

    private sealed class CompletionServiceProbe(IOwnerBoundCharacterCreationCompletionService actual,
        SynchronizationContext ui) : IOwnerBoundCharacterCreationCompletionService
    {
        public int ReviewCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public bool ThrowBeforeApply { get; init; }
        public bool ThrowAfterApply { get; init; }
        public CancellationTokenSource? CancelAfterCommit { get; init; }

        public CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
            OwnerContextStamp owner, CharacterWorkspaceId workspaceId, CharacterCreationCompletionReviewRequest proposal,
            CharacterCreationStartingCashChoice startingCash, CancellationToken cancellationToken = default)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, ui),
                "Completion Core review ran on the caller's UI context.");
            ReviewCalls++;
            return actual.Review(owner, workspaceId, proposal, startingCash, cancellationToken);
        }

        public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
            OwnerContextStamp owner, CharacterCreationCompletionApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, ui),
                "Completion Core Apply ran on the caller's UI context.");
            ApplyCalls++;
            // The caller cannot infer whether a transport-style failure occurred
            // before or after storage. This fixture deliberately leaves no receipt.
            if (ThrowBeforeApply) throw new IOException("Injected uncertain completion dispatch.");
            var result = actual.Apply(owner, request, cancellationToken);
            if (ThrowAfterApply)
            {
                Require(result.Outcome == CharacterCreationFoundationOutcomes.Success && result.Value is not null,
                    "SETUP: the lost-reply fixture must commit successfully before losing its response.");
                CancelAfterCommit?.Cancel();
                throw new IOException("Injected response loss after durable completion.");
            }
            return result;
        }
    }
}
