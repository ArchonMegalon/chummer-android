using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>The existing immutable Career selection token is shared; no reputation rules live here.</summary>
public sealed record Sr5CareerReputationReview(Sr5AfterRunRewardRunnerBinding Runner,
    CharacterCareerReputationSnapshot Snapshot, CharacterCareerReputationPreview Preview)
{
    public bool IsExact()
        => Runner is not null && Runner.IsCleanSavedSr5() && Snapshot is not null && Preview is not null
            && Snapshot.WorkspaceId == Runner.WorkspaceId && Snapshot.ContentRevision == Runner.ContentRevision
            && Snapshot.SavedRevision == Runner.SavedRevision
            && CharacterCareerReputationTransaction.IsBoundCommand(Preview.Command)
            && Snapshot.Reputation?.Inputs is not null
            && !Preview.Command.ExplicitlyConfirmed
            && CharacterCareerReputationTransaction.TryPreview(Snapshot, Preview.Command.Request,
                Preview.Command.Binding.RuntimeDigest, out var rebuilt) && rebuilt == Preview;
}

public enum Sr5CareerReputationResolutionStatus { Recorded, Blocked, OutcomeUnknown, Superseded }
public sealed record Sr5CareerReputationResolution(Sr5CareerReputationResolutionStatus Status,
    Sr5CareerReputationCheckpoint? Checkpoint, string Message, long? CurrentWorkspaceRevision = null);
public sealed record Sr5CareerReputationPreparation(Sr5CareerReputationReview? Review, string? Error = null);

/// <summary>
/// Native orchestration only: Core owns arithmetic, source validation and atomic persistence.
/// All Core/file work runs off the caller thread; no detached or replayed mutation is started.
/// </summary>
public sealed class Sr5CareerReputationCoordinator(ICharacterCareerReputationService service,
    ISr5AfterRunRewardRunnerAuthority authority, Sr5CareerReputationJournal journal)
{
    private readonly ICharacterCareerReputationService _core = service ?? throw new ArgumentNullException(nameof(service));
    private readonly ISr5AfterRunRewardRunnerAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    private readonly Sr5CareerReputationJournal _journal = journal ?? throw new ArgumentNullException(nameof(journal));

    public Task<CharacterCareerReputationReadResult> ReadAsync(CancellationToken token = default)
    {
        var before = Capture();
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var result = _core.Read(before.WorkspaceId);
            if (!OwnsExact(before))
                return new(CharacterCareerReputationOutcome.Unavailable, Error: "The exact saved runner changed while reputation was read.");
            if (result.Outcome != CharacterCareerReputationOutcome.Available) return result;
            return result.Snapshot?.WorkspaceId == before.WorkspaceId
                && result.Snapshot.ContentRevision == before.ContentRevision && result.Snapshot.SavedRevision == before.SavedRevision
                ? result : new(CharacterCareerReputationOutcome.Unavailable, Error: "The exact saved reputation snapshot is unavailable.");
        }, token);
    }

    public Task<Sr5CareerReputationEntryState> InspectAsync(CancellationToken token = default)
    {
        var before = Capture();
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!_journal.TryInspect(before.OwnerId, before.WorkspaceId, out var state, out string error) || !OwnsSelection(before))
                throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "The selected reputation owner changed." : error);
            return state!;
        }, token);
    }

    public Task<Sr5CareerReputationPreparation> PreviewAsync(CharacterCareerReputationRequest request, CancellationToken token = default)
    {
        var before = Capture();
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!CharacterCareerReputationTransaction.IsValidRequest(request) || request.WorkspaceId != before.WorkspaceId)
                return new Sr5CareerReputationPreparation(null, "The reputation request does not identify this runner.");
            if (!_journal.TryInspect(before.OwnerId, before.WorkspaceId, out var entry, out string error)
                || entry!.RecoveryRequired is not null)
                return new(null, string.IsNullOrEmpty(error) ? "Recover the original reputation intent first." : error);
            var read = _core.Read(before.WorkspaceId);
            if (!OwnsExact(before) || read.Outcome != CharacterCareerReputationOutcome.Available || read.Snapshot is null)
                return new(null, read.Error ?? "The saved reputation context is unavailable.");
            var result = _core.Preview(request);
            var review = result.Preview is null ? null : new Sr5CareerReputationReview(before, read.Snapshot, result.Preview);
            if (!OwnsExact(before) || result.Outcome != CharacterCareerReputationOutcome.Available
                || result.CurrentWorkspaceRevision != before.ContentRevision || review is null || !review.IsExact()
                || review.Preview.Command.Request != request)
                return new(null, result.Error ?? "The exact preview or saved runner changed.");
            return new(review);
        }, token);
    }

    /// <summary>Only an explicit user confirmation may call this; preview/read never journal or mutate.</summary>
    public Task<Sr5CareerReputationResolution> ConfirmAsync(Sr5CareerReputationReview review, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        var before = Capture();
        return Task.Run(async () =>
        {
            if (!review.IsExact() || !before.SameSelection(review.Runner)) return Blocked("The reputation review belongs to an earlier runner selection.");
            var command = review.Preview.Command with { ExplicitlyConfirmed = true };
            if (!_journal.TryGet(before.OwnerId, before.WorkspaceId, command.Request.OperationId, out var existing, out string error))
                return Blocked(error);
            if (existing is not null)
            {
                if (existing.Command != command) return Blocked("The original operation owns different confirmed choices.", existing);
                if (token.IsCancellationRequested) return Unknown(existing, "Confirmation was canceled; retain the original intent for lookup.");
                return await ExecuteAsync(existing, before, true, token).ConfigureAwait(false);
            }
            if (token.IsCancellationRequested || !OwnsExact(review.Runner)) return Blocked("Confirmation stopped before any intent was written.");
            var fresh = _core.Preview(command.Request);
            if (token.IsCancellationRequested || !OwnsExact(review.Runner)
                || fresh.Outcome != CharacterCareerReputationOutcome.Available || fresh.Preview != review.Preview)
                return Blocked(fresh.Error ?? "The saved source changed. Review the current runner again.");
            var confirmed = new Sr5CareerReputationCheckpoint(1, 1, before.OwnerId, Sr5CareerReputationPhase.Confirmed,
                command, CharacterCareerReputationTransaction.CommandDigest(command));
            if (!_journal.TryPrepare(confirmed, () => OwnsExact(review.Runner), out error, out bool unknown))
                return unknown ? Unknown(confirmed, "No Core commit started, but journal durability is uncertain. " + error) : Blocked(error);
            return await ExecuteAsync(confirmed, before, true, token).ConfigureAwait(false);
        }, CancellationToken.None);
    }

    public Task<Sr5CareerReputationResolution> RecoverAsync(Guid operation, CancellationToken token = default)
        => ResumeAsync(operation, false, token);
    public Task<Sr5CareerReputationResolution> RetryAsync(Guid operation, CancellationToken token = default)
        => ResumeAsync(operation, true, token);

    /// <summary>Explicit user close of a revision-superseded command; never a general cancel or rollback.</summary>
    public Task<Sr5CareerReputationResolution> CloseSupersededAsync(Guid operation, CancellationToken token = default)
    {
        var before = Capture();
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!_journal.TryGet(before.OwnerId, before.WorkspaceId, operation, out var entry, out string blocker) || entry is null)
                return Blocked("The original reputation command is unavailable.");
            bool Owns() => OwnsSelection(before);
            try
            {
                if (entry.Phase == Sr5CareerReputationPhase.Applied)
                    return ReconcileAdvanced(entry, before, "A committed change cannot be discarded.");
                if (entry.Phase == Sr5CareerReputationPhase.Confirmed)
                {
                    if (!_journal.TryBegin(entry, Owns, out var applying, out blocker)) return ReconcileAdvanced(entry, before, blocker);
                    entry = applying!;
                }
                if (_journal.TrySupersede(entry, Owns, out var closed, out blocker)) return Superseded(closed!);
                return ReconcileAdvanced(entry, before, blocker);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            { return ReconcileAdvanced(entry, before, "Retain the original outcome for lookup. " + ex.Message); }
        }, CancellationToken.None);
    }

    private Task<Sr5CareerReputationResolution> ResumeAsync(Guid operation, bool mayCommit, CancellationToken token)
    {
        var before = Capture();
        return Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (operation == Guid.Empty || !_journal.TryGet(before.OwnerId, before.WorkspaceId, operation, out var entry, out string error)
                || entry is null) return Blocked("The original reputation command is missing or unavailable; do not allocate another identity.");
            return await ExecuteAsync(entry, before, mayCommit, token).ConfigureAwait(false);
        }, CancellationToken.None);
    }

    private async Task<Sr5CareerReputationResolution> ExecuteAsync(Sr5CareerReputationCheckpoint entry,
        Sr5AfterRunRewardRunnerBinding before, bool mayCommit, CancellationToken token)
    {
        bool Owns() => OwnsSelection(before) && entry.OwnerId == before.OwnerId && entry.Command.Request.WorkspaceId == before.WorkspaceId;
        try
        {
            if (!Owns()) return Unknown(entry, "The reputation runner or owner changed.");
            if (entry.Phase == Sr5CareerReputationPhase.Superseded)
                return _journal.TrySupersede(entry, Owns, out var closed, out string closeError)
                    ? Superseded(closed!) : Unknown(entry, closeError);
            if (entry.Phase != Sr5CareerReputationPhase.Applied)
            {
                if (!_journal.TryBegin(entry, Owns, out var applying, out string error)) return ReconcileAdvanced(entry, before, error);
                entry = applying!;
                using (await _journal.AcquireAsync(entry, Owns, mayCommit, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    var result = _core.Lookup(before.WorkspaceId, entry.Command.Request.OperationId, entry.CommandDigest);
                    if (result.Outcome == CharacterCareerReputationOutcome.NotFound && mayCommit)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!Owns()) return Unknown(entry, "The selected runner changed before Core execution.");
                        result = _core.Commit(entry.Command, token);
                    }
                    if (result.Outcome is not (CharacterCareerReputationOutcome.Applied or CharacterCareerReputationOutcome.Replayed))
                        return Unknown(entry, result.Error ?? "No durable reputation receipt was found. Lookup never starts a new mutation.");
                }
            }
            if (_journal.TryResolve(entry, Owns, out var applied, out var revision, out string blocker))
                return new(Sr5CareerReputationResolutionStatus.Recorded, applied, "Reputation change recorded.", revision);
            return ReconcileAdvanced(entry, before, blocker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or OperationCanceledException or OverflowException)
        { return ReconcileAdvanced(entry, before, "The outcome needs lookup. " + ex.Message); }
    }

    private Sr5CareerReputationResolution ReconcileAdvanced(Sr5CareerReputationCheckpoint original,
        Sr5AfterRunRewardRunnerBinding before, string blocker)
    {
        bool Owns() => OwnsSelection(before);
        try
        {
            if (Owns() && _journal.TryGet(before.OwnerId, before.WorkspaceId, original.Command.Request.OperationId,
                    out var current, out _) && current is not null && current.Command == original.Command)
            {
                if (current.Phase == Sr5CareerReputationPhase.Applied
                    && _journal.TryResolve(current, Owns, out var applied, out var revision, out _))
                    return new(Sr5CareerReputationResolutionStatus.Recorded, applied, "Original reputation receipt recovered.", revision);
                if (current.Phase == Sr5CareerReputationPhase.Superseded
                    && _journal.TrySupersede(current, Owns, out var closed, out _)) return Superseded(closed!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        return Unknown(original, blocker);
    }

    private Sr5AfterRunRewardRunnerBinding Capture()
    {
        var current = _authority.Current;
        if (!current.IsCleanSavedSr5()) throw new InvalidOperationException("Open a clean saved SR5 Career runner before changing reputation.");
        return current;
    }
    private bool OwnsSelection(Sr5AfterRunRewardRunnerBinding before)
    { var current = _authority.Current; return current.IsCleanSavedSr5() && current.SameSelection(before); }
    private bool OwnsExact(Sr5AfterRunRewardRunnerBinding before) => _authority.Current == before && before.IsCleanSavedSr5();
    private static Sr5CareerReputationResolution Blocked(string message, Sr5CareerReputationCheckpoint? entry = null)
        => new(Sr5CareerReputationResolutionStatus.Blocked, entry, message);
    private static Sr5CareerReputationResolution Unknown(Sr5CareerReputationCheckpoint entry, string message)
        => new(Sr5CareerReputationResolutionStatus.OutcomeUnknown, entry, message);
    private static Sr5CareerReputationResolution Superseded(Sr5CareerReputationCheckpoint entry)
        => new(Sr5CareerReputationResolutionStatus.Superseded, entry,
            "Not applied: the saved runner revision superseded this original command. Review new choices separately.", entry.SupersededAtRevision);
}
