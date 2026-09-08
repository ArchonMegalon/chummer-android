using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Native host orchestration over the real synchronous local Core API. Parsing,
/// ledger verification, journal I/O and Core calls execute off the caller's UI
/// thread. No rule calculation, XML mutation or remotely authenticated authority
/// is implemented here.
/// </summary>
public sealed class Sr5AfterRunRewardCoordinator(
    ICharacterAfterRunRewardService service,
    ISr5AfterRunRewardRunnerAuthority authority,
    Sr5AfterRunRewardCheckpointStore store)
{
    private readonly ICharacterAfterRunRewardService _service = service
        ?? throw new ArgumentNullException(nameof(service));
    private readonly ISr5AfterRunRewardRunnerAuthority _authority = authority
        ?? throw new ArgumentNullException(nameof(authority));
    private readonly Sr5AfterRunRewardCheckpointStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    public async Task<CharacterAfterRunRewardReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        Sr5AfterRunRewardRunnerBinding before = Capture();
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            CharacterAfterRunRewardReadResult result = _service.Read(before.WorkspaceId);
            if (!OwnsExact(before) || result.Outcome == CharacterAfterRunRewardOutcome.Available
                && (!CharacterAfterRunRewardProjector.IsValidSnapshot(result.Snapshot)
                    || result.Snapshot!.WorkspaceId != before.WorkspaceId
                    || result.Snapshot.ContentRevision != before.ContentRevision))
                return new(CharacterAfterRunRewardOutcome.Unavailable,
                    Error: "The exact saved runner changed while reward facts were read.");
            return result;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<Sr5AfterRunRewardCheckpoint>> ReadHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        Sr5AfterRunRewardRunnerBinding before = Capture();
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.TryReadOwned(before.OwnerId, before.WorkspaceId, out var entries, out string blocker)
                || !OwnsSelection(before))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(blocker)
                    ? "The selected reward owner changed." : blocker);
            return entries;
        }, cancellationToken);
    }

    /// <summary>Read-only entry observation; never releases ownership or looks up/commits a reward.</summary>
    public async Task<Sr5AfterRunRewardEntryState> ReadEntryStateAsync(
        CancellationToken cancellationToken = default)
    {
        Sr5AfterRunRewardRunnerBinding before = Capture();
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.TryReadEntryState(before.OwnerId, before.WorkspaceId, out var state, out string blocker)
                || !OwnsSelection(before))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(blocker)
                    ? "The selected reward owner changed." : blocker);
            return state!;
        }, cancellationToken);
    }

    public async Task<Sr5AfterRunRewardPreparation> PreviewAsync(CharacterAfterRunRewardPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Sr5AfterRunRewardRunnerBinding before = Capture();
        if (request.WorkspaceId != before.WorkspaceId)
            return new(CharacterAfterRunRewardOutcome.Conflict, Blocker: "The proposal belongs to another runner.");
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.TryReadOwned(before.OwnerId, before.WorkspaceId, out var entries, out string blocker)
                || entries.Any(entry => entry.Phase != Sr5AfterRunRewardCheckpointPhase.Applied))
                return new Sr5AfterRunRewardPreparation(CharacterAfterRunRewardOutcome.Unavailable,
                    Blocker: string.IsNullOrWhiteSpace(blocker)
                        ? "Resolve the original pending reward before preparing another." : blocker);
            if (!OwnsExact(before))
                return new(CharacterAfterRunRewardOutcome.Conflict, Blocker: "The selected runner changed.");
            CharacterAfterRunRewardPreviewResult result = _service.Preview(request);
            if (result.Outcome != CharacterAfterRunRewardOutcome.Available)
                return new(result.Outcome, Blocker: result.Error ?? "Core cannot preview this reward.");
            var review = result.Preview is null ? null : new Sr5AfterRunRewardReview(before, result.Preview);
            if (!OwnsExact(before) || review is null || !review.IsExact()
                || result.CurrentWorkspaceRevision != before.ContentRevision
                || !MatchesRequest(review.Preview.Command, request))
                return new(CharacterAfterRunRewardOutcome.Conflict,
                    Blocker: "The Core preview is incoherent or no longer owns this exact proposal and runner.");
            return new(CharacterAfterRunRewardOutcome.Available, review);
        }, cancellationToken);
    }

    /// <summary>Call only for the user's explicit confirmation of this exact review.</summary>
    public async Task<Sr5AfterRunRewardResolution> ConfirmAsync(Sr5AfterRunRewardReview review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        Sr5AfterRunRewardRunnerBinding before = Capture();
        if (!review.IsExact() || !before.SameSelection(review.Runner))
            return Blocked("This reward review no longer belongs to the selected owner and runner.");
        // This is the only permitted change to Core's returned preview command.
        CharacterAfterRunRewardCommand command = review.Preview.Command with { ExplicitlyConfirmed = true };
        return await Task.Run(async () =>
        {
            if (!_store.TryGet(before.OwnerId, before.WorkspaceId, command.OperationId,
                    out var existing, out string blocker)) return Blocked(blocker);
            if (existing is not null)
            {
                if (existing.CommandDigest != command.CommandDigest()
                    || !CharacterAfterRunRewardProjector.CanonicalEquals(existing.Command, command))
                    return Blocked("The original operation owns a different confirmed command.", existing);
                // Cancellation is not proof that an earlier invocation did not
                // persist. Keep its exact journal identity for later lookup.
                if (cancellationToken.IsCancellationRequested)
                    return Blocked("Confirmation was canceled; retain the existing reward for recovery.", existing);
                return await ExecuteAsync(existing, before, mayCommit: true, cancellationToken).ConfigureAwait(false);
            }
            if (cancellationToken.IsCancellationRequested)
                return Blocked("Confirmation was canceled before a reward intent was prepared.");
            if (!OwnsExact(review.Runner)) return Blocked("The previewed saved runner changed. Review it again.");
            CharacterAfterRunRewardReadResult saved = _service.Read(before.WorkspaceId);
            // Still before TryPrepare or Commit: this cancellation has a known
            // outcome, unlike cancellation after either durable boundary.
            if (cancellationToken.IsCancellationRequested)
                return Blocked("Confirmation was canceled before a reward intent was prepared.");
            if (!OwnsExact(review.Runner) || saved.Outcome != CharacterAfterRunRewardOutcome.Available
                || !CharacterAfterRunRewardProjector.IsValidSnapshot(saved.Snapshot)
                || !CharacterAfterRunRewardProjector.Matches(command, saved.Snapshot!))
                return Blocked(saved.Error ?? "The saved source or auxiliary state changed. Review again before confirming.");
            var confirmed = new Sr5AfterRunRewardCheckpoint(1, 1, before.OwnerId,
                Sr5AfterRunRewardCheckpointPhase.Confirmed, command, command.CommandDigest());
            if (!_store.TryPrepare(confirmed, () => OwnsExact(review.Runner), out blocker, out bool writeUnknown))
                return writeUnknown
                    ? Unknown(confirmed, "No Core commit was started, but journal persistence is uncertain. Retain this exact command. " + blocker)
                    : Blocked(blocker);
            return await ExecuteAsync(confirmed, before, mayCommit: true, cancellationToken).ConfigureAwait(false);
        }, CancellationToken.None);
    }

    /// <summary>Lookup only. NotFound never starts a new award or clears the original intent.</summary>
    public Task<Sr5AfterRunRewardResolution> RecoverAsync(Guid operationId,
        CancellationToken cancellationToken = default)
        => RecoverAsync(operationId, mayCommit: false, cancellationToken);

    /// <summary>Explicit retry: lookup first, then only the original persisted command.</summary>
    public Task<Sr5AfterRunRewardResolution> RetryAsync(Guid operationId,
        CancellationToken cancellationToken = default)
        => RecoverAsync(operationId, mayCommit: true, cancellationToken);

    private async Task<Sr5AfterRunRewardResolution> RecoverAsync(Guid operationId, bool mayCommit,
        CancellationToken cancellationToken)
    {
        Sr5AfterRunRewardRunnerBinding before = Capture();
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.TryGet(before.OwnerId, before.WorkspaceId, operationId,
                    out var checkpoint, out string blocker) || checkpoint is null)
                return Blocked(string.IsNullOrWhiteSpace(blocker)
                    ? "The original reward command is missing; do not allocate new identities." : blocker);
            return await ExecuteAsync(checkpoint, before, mayCommit, cancellationToken).ConfigureAwait(false);
        }, CancellationToken.None);
    }

    private async Task<Sr5AfterRunRewardResolution> ExecuteAsync(Sr5AfterRunRewardCheckpoint checkpoint,
        Sr5AfterRunRewardRunnerBinding before, bool mayCommit, CancellationToken cancellationToken)
    {
        bool Owns() => OwnsSelection(before) && checkpoint.OwnerId == before.OwnerId
            && checkpoint.Command.WorkspaceId == before.WorkspaceId;
        if (!Owns()) return Blocked("The reward owner or runner changed.", checkpoint);
        try
        {
            if (checkpoint.Phase == Sr5AfterRunRewardCheckpointPhase.Confirmed)
            {
                if (!_store.TryBegin(checkpoint, Owns, out var applying, out string blocker))
                    return ResolveAdvancedApplied(checkpoint, before, blocker);
                checkpoint = applying!;
            }

            CharacterAfterRunRewardResult result;
            if (checkpoint.Phase == Sr5AfterRunRewardCheckpointPhase.Applied)
            {
                // Historical recovery does not require current revision n+1 and
                // never re-commits, even when a caller used the explicit retry API.
                cancellationToken.ThrowIfCancellationRequested();
                result = Lookup(checkpoint);
            }
            else
            {
                if (!_store.TryOwnApplying(checkpoint, Owns, out string blocker))
                    return ResolveAdvancedApplied(checkpoint, before, blocker);
                using (await _store.AcquireApplyingLeaseAsync(checkpoint, Owns, mayCommit,
                           cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result = Lookup(checkpoint);
                    if (result.Outcome == CharacterAfterRunRewardOutcome.NotFound && mayCommit)
                    {
                        if (!Owns()) return Unknown(checkpoint, "The owner changed before the original command call.");
                        // Cancellation is passed through, but once entered it is
                        // never interpreted as rollback or permission to re-key.
                        result = _service.Commit(checkpoint.Command, cancellationToken);
                    }
                }
            }

            return ResolveObserved(checkpoint, result, before);
        }
        catch (Exception)
        {
            return Unknown(checkpoint,
                "The reward outcome or journal acknowledgement is unavailable. Keep the original command and look it up.");
        }
    }

    private Sr5AfterRunRewardResolution ResolveAdvancedApplied(Sr5AfterRunRewardCheckpoint stale,
        Sr5AfterRunRewardRunnerBinding before, string blocker)
    {
        // Another caller may complete between the journal precheck and shared
        // owner reservation. Never release an accidentally re-reserved owner
        // merely because local bytes now say Applied: perform actual Core Lookup.
        if (OwnsSelection(before)
            && _store.TryGet(stale.OwnerId, stale.Command.WorkspaceId, stale.Command.OperationId,
                out var current, out _) && current is not null
            && current.Phase == Sr5AfterRunRewardCheckpointPhase.Applied
            && current.CommandDigest == stale.CommandDigest
            && CharacterAfterRunRewardProjector.CanonicalEquals(current.Command, stale.Command))
            return ResolveObserved(current, Lookup(current), before);
        return Unknown(stale, blocker);
    }

    private Sr5AfterRunRewardResolution ResolveObserved(Sr5AfterRunRewardCheckpoint checkpoint,
        CharacterAfterRunRewardResult result, Sr5AfterRunRewardRunnerBinding before)
    {
        bool Owns() => OwnsSelection(before) && checkpoint.OwnerId == before.OwnerId
            && checkpoint.Command.WorkspaceId == before.WorkspaceId;
        if (!Owns()) return Unknown(checkpoint, "The selected owner changed. Preserve the original reward for recovery.");
        if (result.Outcome is not (CharacterAfterRunRewardOutcome.Applied or CharacterAfterRunRewardOutcome.Replayed)
            || result.Receipt is not { } receipt || !checkpoint.ReceiptMatches(receipt)
            || result.CurrentWorkspaceRevision is not { } revision
            || revision < receipt.CommittedWorkspaceRevision)
            return Unknown(checkpoint, result.Error
                ?? "The original reward has no exact durable receipt yet. Lookup before any explicit retry.");
        var observation = Sr5AfterRunRewardReceiptObservation.Create(checkpoint, receipt, revision);
        // Execution leases must already be disposed: TryComplete owns that gate.
        if (!_store.TryRecordReceipt(checkpoint, observation, Owns, out var applied, out string resolutionBlocker))
            return Unknown(checkpoint, resolutionBlocker);
        return new(Sr5AfterRunRewardResolutionStatus.Recorded, applied,
            receipt.Kind == CharacterAfterRunRewardKind.NoAward
                ? "The explicit no-award decision and local Core receipt are durable."
                : "The original local reward and Core receipt are durable.", revision);
    }

    private CharacterAfterRunRewardResult Lookup(Sr5AfterRunRewardCheckpoint checkpoint)
        => _service.Lookup(checkpoint.Command.WorkspaceId, checkpoint.Command.OperationId, checkpoint.CommandDigest);

    private Sr5AfterRunRewardRunnerBinding Capture()
    {
        Sr5AfterRunRewardRunnerBinding binding = _authority.Current;
        if (!binding.IsCleanSavedSr5())
            throw new InvalidOperationException("Local rewards require the exact owner and a clean saved created SR5 runner.");
        return binding;
    }

    private bool OwnsExact(Sr5AfterRunRewardRunnerBinding expected)
        => _authority.Current == expected && expected.IsCleanSavedSr5();

    private bool OwnsSelection(Sr5AfterRunRewardRunnerBinding expected)
    {
        Sr5AfterRunRewardRunnerBinding current = _authority.Current;
        return current.IsCleanSavedSr5() && current.SameSelection(expected);
    }

    private static bool MatchesRequest(CharacterAfterRunRewardCommand command, CharacterAfterRunRewardPreviewRequest request)
        => command.WorkspaceId == request.WorkspaceId && command.OperationId == request.OperationId
            && command.RewardId == request.RewardId && command.KarmaAmount == request.KarmaAmount
            && command.NuyenAmount == request.NuyenAmount && command.ExpenseDateLocal == request.ExpenseDateLocal
            && command.ExpenseDateLocal.Kind == request.ExpenseDateLocal.Kind && command.Reason == request.Reason
            && command.ExistingKarmaExpenseId == request.ExistingKarmaExpenseId
            && command.ExistingNuyenExpenseId == request.ExistingNuyenExpenseId && command.Kind == request.Kind;

    private static Sr5AfterRunRewardResolution Blocked(string message, Sr5AfterRunRewardCheckpoint? checkpoint = null)
        => new(Sr5AfterRunRewardResolutionStatus.Blocked, checkpoint, message);

    private static Sr5AfterRunRewardResolution Unknown(Sr5AfterRunRewardCheckpoint checkpoint, string message)
        => new(Sr5AfterRunRewardResolutionStatus.OutcomeUnknown, checkpoint, message);
}
