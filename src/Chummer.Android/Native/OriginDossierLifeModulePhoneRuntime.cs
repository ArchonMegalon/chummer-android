using System.Security.Cryptography;
using System.Text;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

public sealed record OriginDossierLifeModulePhoneResult(
    string Outcome,
    OriginDossierLifeModuleDecisionState? State,
    IReadOnlyList<string> Blockers,
    bool Completed = false,
    CharacterCreationBudgetState? LifeModuleBudget = null,
    string? FoundationSnapshotDigest = null,
    string? BoundContentDigest = null,
    string? BoundSourceDigest = null,
    string? BoundMechanicsSnapshotDigest = null,
    LifeModuleOriginDossierDraftCheckpoint? StoryCheckpoint = null)
{
    public bool IsSuccess => string.Equals(
        Outcome,
        LifeModuleOriginDossierOutcomes.Success,
        StringComparison.Ordinal);
}

/// <summary>
/// Phone orchestration only. Core owns every decision, digest and mutation;
/// this class persists the sealed user timeline and projects it for MAUI.
/// </summary>
public sealed class OriginDossierLifeModulePhoneRuntime
{
    private readonly IOwnerBoundLifeModuleOriginService _interaction;
    private readonly IOriginDossierDraftTimelineStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Foundation's digest format is prefixed; Origin's is raw lowercase hex.
    // Keep the comparison strict so an invalid/stale binding never gains admission.
    internal static bool MatchesFoundationDigest(string? foundationDigest, string? originDigest)
        => foundationDigest is { Length: 71 }
           && foundationDigest.StartsWith("sha256:", StringComparison.Ordinal)
           && LifeModuleDecisionAcceptanceIntegrity.IsDigest(originDigest)
           && string.Equals(foundationDigest[7..], originDigest, StringComparison.Ordinal);

    public OriginDossierLifeModulePhoneRuntime(
        IOwnerBoundLifeModuleOriginService interaction,
        IOriginDossierDraftTimelineStore store)
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<OriginDossierLifeModulePhoneResult> OpenAsync(
        OwnerContextStamp owner,
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_interaction.IsCurrent(owner)) return StaleOwner();
            LifeModuleOriginDossierDraftCheckpoint? persisted =
                await _store.LoadAsync(owner.Owner.NormalizedValue, workspaceId, cancellationToken)
                    .ConfigureAwait(false);
            LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> result;
            if (persisted is null)
            {
                result = await Task.Run(() => _interaction.Start(owner, workspaceId), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                result = await Task.Run(() => _interaction.Restore(owner, persisted), cancellationToken)
                    .ConfigureAwait(false);
                // A crash after the atomic mechanics commit but before saving
                // the next chapter leaves a valid pending preview. Keep it available for
                // the idempotent Confirm retry instead of guessing completion.
                if (_interaction.IsCurrent(owner)
                    && result.Outcome == LifeModuleOriginDossierOutcomes.Conflict
                    && persisted.PendingPreview is not null)
                {
                    return Project(
                        LifeModuleOriginDossierOutcomes.Success,
                        persisted,
                        result.Blockers);
                }
            }
            if (!IsSuccess(result) || result.Value is not { } checkpoint)
                return Failed(result.Outcome, result.Blockers);
            if (!_interaction.IsCurrent(owner)) return StaleOwner();
            await _store.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
            return _interaction.IsCurrent(owner) ? Project(result.Outcome, checkpoint, result.Blockers) : StaleOwner();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OriginDossierLifeModulePhoneResult> PrepareAsync(
        OwnerContextStamp owner,
        string workspaceId,
        string choiceId,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? followUpValues = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_interaction.IsCurrent(owner)) return StaleOwner();
            LifeModuleOriginDossierDraftCheckpoint? checkpoint =
                await _store.LoadAsync(owner.Owner.NormalizedValue, workspaceId, cancellationToken)
                    .ConfigureAwait(false);
            if (checkpoint is null)
                return Failed(LifeModuleOriginDossierOutcomes.Missing, [LifeModuleOriginDossierBlockers.ProjectionInvalid]);
            // Prepare already performs a fresh Core restore. Avoid doing the
            // full catalog projection twice and never do it on the UI thread.
            LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> prepared =
                await Task.Run(() => _interaction.Prepare(owner, checkpoint, choiceId, followUpValues), cancellationToken)
                    .ConfigureAwait(false);
            if (!IsSuccess(prepared) || prepared.Value is not { } next)
                return Failed(prepared.Outcome, prepared.Blockers);
            if (!_interaction.IsCurrent(owner)) return StaleOwner();
            await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            return _interaction.IsCurrent(owner) ? Project(prepared.Outcome, next, prepared.Blockers) : StaleOwner();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OriginDossierLifeModulePhoneResult> ConfirmAsync(
        OwnerContextStamp owner,
        string workspaceId,
        string choiceId,
        string previewDigest,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_interaction.IsCurrent(owner)) return StaleOwner();
            LifeModuleOriginDossierDraftCheckpoint? checkpoint =
                await _store.LoadAsync(owner.Owner.NormalizedValue, workspaceId, cancellationToken)
                    .ConfigureAwait(false);
            if (checkpoint?.PendingPreview is not { } pending
                || !string.Equals(pending.SelectedChoice.ChoiceId, choiceId, StringComparison.Ordinal)
                || !string.Equals(pending.PreviewDigest, previewDigest, StringComparison.Ordinal))
                return Failed(LifeModuleOriginDossierOutcomes.Conflict, [LifeModuleOriginDossierBlockers.ProjectionInvalid]);
            string idempotencyKey = "origin:" + Digest(
                checkpoint.WorkspaceId + "\0" + checkpoint.BoundSeedDigest + "\0"
                + choiceId + "\0" + previewDigest);
            LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> confirmed =
                await Task.Run(() => _interaction.Confirm(
                    owner, checkpoint,
                    previewDigest,
                    idempotencyKey,
                    explicitlyConfirmed: true), cancellationToken).ConfigureAwait(false);
            if (!IsSuccess(confirmed) || confirmed.Value is not { } advance)
                return Failed(confirmed.Outcome, confirmed.Blockers);
            // The confirmed chapter is the user's book, not a disposable wizard
            // checkpoint. Persist terminal turns as well. Once Core committed,
            // cancellation must not discard its result. If storage fails, the
            // previous pending preview still permits an idempotent recovery.
            await _store.SaveAsync(advance.Checkpoint, CancellationToken.None).ConfigureAwait(false);
            return _interaction.IsCurrent(owner) ? Project(confirmed.Outcome, advance.Checkpoint, confirmed.Blockers) : StaleOwner();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static OriginDossierLifeModulePhoneResult StaleOwner()
        => Failed(LifeModuleOriginDossierOutcomes.Blocked, [LifeModuleOriginDossierBlockers.AuthorityInvalid]);

    private static OriginDossierLifeModulePhoneResult Project(
        string outcome,
        LifeModuleOriginDossierDraftCheckpoint checkpoint,
        IReadOnlyList<string> blockers)
    {
        try
        {
            return new(
                outcome,
                checkpoint.Projection.CurrentTurn.IsTerminal
                    ? null
                    : OriginDossierLifeModuleInteractionProjector.Project(checkpoint),
                blockers,
                Completed: checkpoint.Projection.CurrentTurn.IsTerminal,
                LifeModuleBudget: null,
                FoundationSnapshotDigest: null,
                BoundContentDigest: checkpoint.BoundContentDigest,
                BoundSourceDigest: checkpoint.BoundSourceDigest,
                BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest,
                StoryCheckpoint: checkpoint);
        }
        catch (InvalidOperationException)
        {
            return Failed(
                LifeModuleOriginDossierOutcomes.Invalid,
                [LifeModuleOriginDossierBlockers.ProjectionInvalid]);
        }
    }

    private static OriginDossierLifeModulePhoneResult Failed(
        string outcome,
        IReadOnlyList<string> blockers)
        => new(outcome, null, blockers);

    private static bool IsSuccess<T>(LifeModuleOriginDossierResult<T> result)
        => string.Equals(result.Outcome, LifeModuleOriginDossierOutcomes.Success, StringComparison.Ordinal);

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
