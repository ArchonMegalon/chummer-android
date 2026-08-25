using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Fail-closed Android registration seam for the SR5 Career quality authority.
/// The active Chummer client must itself expose the trusted atomic capability;
/// Android never derives source, rule, GM, effect or persistence authority.
/// </summary>
public sealed class AndroidCareerQualityAtomicWorkspace : ICareerQualityAtomicWorkspace,
    IDisposable
{
    private static readonly JsonSerializerOptions ExactJson = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly IChummerClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AndroidCareerQualityAtomicWorkspace(IChummerClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<CareerQualityAuthoritySnapshot?> ReadAsync(
        CharacterWorkspaceId workspaceId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ICareerQualityAtomicWorkspace? authority = ResolveAuthority();
            if (authority is null)
            {
                return null;
            }

            CareerQualityAuthoritySnapshot? snapshot = await authority
                .ReadAsync(workspaceId, ct)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                return null;
            }

            RequireWorkspace(snapshot, workspaceId);
            RequireCompleteProjection(snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CareerQualityAtomicCommitResult?> CommitAsync(
        CharacterCareerQualityPlan plan,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ICareerQualityAtomicWorkspace? authority = ResolveAuthority();
            if (authority is null || !CharacterCareerQualityRules.IsCoherent(plan))
            {
                return null;
            }

            CharacterWorkspaceId workspaceId = new(plan.WorkspaceId);
            CareerQualityAuthoritySnapshot snapshot = await ReadRequiredAsync(
                    authority,
                    workspaceId,
                    ct)
                .ConfigureAwait(false);
            CareerQualityEditorState state = RequireCompleteProjection(snapshot);
            RequireExpectedBinding(
                state,
                plan.OwnerId,
                plan.ExpectedWorkspaceRevision,
                plan.ExpectedSavedRevision,
                plan.ExpectedRuntimeFingerprint,
                plan.ExpectedContentDigest);

            CharacterCareerQualityQuote quote = ResolveExactQuote(
                state,
                plan.Operation,
                plan.Identity,
                plan.ExpectedLogicalRevision,
                plan.ExpectedSourceRevision,
                plan.ExpectedRuleDigest);
            CareerQualityDraft draft = CareerQualityWorkflow.CreateDraft(state, quote);
            CareerQualityReview review = CareerQualityWorkflow.Review(snapshot, draft);
            CharacterCareerQualityPlan replanned = CareerQualityWorkflow.PlanConfirmation(
                snapshot,
                review,
                confirmed: true,
                plan.TransactionId,
                plan.ExpenseDateLocal);
            if (!ExactPayloadEquals(replanned, plan))
            {
                throw new InvalidOperationException(
                    "The SR5 quality plan drifted from the current typed authority projection.");
            }

            CareerQualityAtomicCommitResult? committed = await authority
                .CommitAsync(plan, ct)
                .ConfigureAwait(false);
            if (committed is null)
            {
                return null;
            }

            _ = CareerQualityWorkflow.ValidateAtomicCommit(review, plan, committed);
            return committed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CareerQualityAtomicCorrectionResult?> CorrectAsync(
        CharacterCareerQualityCorrectionPlan correction,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(correction);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ICareerQualityAtomicWorkspace? authority = ResolveAuthority();
            if (authority is null || !CharacterCareerQualityRules.IsCoherent(correction))
            {
                return null;
            }

            CharacterWorkspaceId workspaceId = new(correction.WorkspaceId);
            CareerQualityAuthoritySnapshot snapshot = await ReadRequiredAsync(
                    authority,
                    workspaceId,
                    ct)
                .ConfigureAwait(false);
            CareerQualityEditorState state = RequireCompleteProjection(snapshot);
            RequireExpectedBinding(
                state,
                correction.OwnerId,
                correction.ExpectedWorkspaceRevision,
                correction.ExpectedSavedRevision,
                correction.ExpectedRuntimeFingerprint,
                correction.ExpectedContentDigest);

            CharacterCareerQualityReceipt original = state.RecoverableReceipts
                .Where(receipt => receipt.TransactionId == correction.OriginalTransactionId)
                .Take(2)
                .SingleOrDefault()
                ?? throw new InvalidOperationException(
                    "The SR5 quality correction receipt is missing or ambiguous.");
            CareerQualityCorrectionRequest request = new(
                workspaceId,
                correction.OwnerId,
                correction.ExpectedWorkspaceRevision,
                correction.ExpectedSavedRevision,
                snapshot.RulesetId,
                original,
                correction.OriginalReceiptDigest,
                Confirmed: true,
                correction.CorrectionId,
                correction.Reason);
            CharacterCareerQualityCorrectionPlan replanned =
                CareerQualityWorkflow.PlanCorrection(snapshot, request);
            if (!ExactPayloadEquals(replanned, correction))
            {
                throw new InvalidOperationException(
                    "The SR5 quality correction drifted from the current typed authority projection.");
            }

            CareerQualityAtomicCorrectionResult? committed = await authority
                .CorrectAsync(correction, ct)
                .ConfigureAwait(false);
            if (committed is null)
            {
                return null;
            }

            _ = CareerQualityWorkflow.ValidateAtomicCorrection(
                original,
                correction,
                committed);
            return committed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private ICareerQualityAtomicWorkspace? ResolveAuthority()
        => ReferenceEquals(_client, this)
            ? null
            : _client as ICareerQualityAtomicWorkspace;

    private static async Task<CareerQualityAuthoritySnapshot> ReadRequiredAsync(
        ICareerQualityAtomicWorkspace authority,
        CharacterWorkspaceId workspaceId,
        CancellationToken ct)
    {
        CareerQualityAuthoritySnapshot snapshot = await authority
            .ReadAsync(workspaceId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Exact SR5 quality authority is unavailable for this dossier.");
        RequireWorkspace(snapshot, workspaceId);
        return snapshot;
    }

    private static CareerQualityEditorState RequireCompleteProjection(
        CareerQualityAuthoritySnapshot snapshot)
    {
        CareerQualityEditorState state = CareerQualityWorkflow.Project(snapshot);
        if (state.OmittedCandidateCount != 0 || state.OmittedReceiptCount != 0)
        {
            throw new InvalidOperationException(
                "The SR5 quality authority contains unsupported or ambiguous projections.");
        }

        return state;
    }

    private static void RequireWorkspace(
        CareerQualityAuthoritySnapshot snapshot,
        CharacterWorkspaceId expectedWorkspaceId)
    {
        if (string.IsNullOrWhiteSpace(expectedWorkspaceId.Value)
            || !string.Equals(
                snapshot.Binding.WorkspaceId,
                expectedWorkspaceId.Value,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The SR5 quality authority returned a different dossier identity.");
        }
    }

    private static void RequireExpectedBinding(
        CareerQualityEditorState state,
        string ownerId,
        long workspaceRevision,
        long savedRevision,
        string runtimeFingerprint,
        string contentDigest)
    {
        if (!string.Equals(state.OwnerId, ownerId, StringComparison.Ordinal)
            || state.WorkspaceRevision != workspaceRevision
            || state.SavedRevision != savedRevision
            || !string.Equals(
                state.RuntimeFingerprint,
                runtimeFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(state.ContentDigest, contentDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The SR5 quality workspace, saved revision, runtime or content authority drifted before persistence.");
        }
    }

    private static CharacterCareerQualityQuote ResolveExactQuote(
        CareerQualityEditorState state,
        CharacterCareerQualityOperation operation,
        CharacterCareerQualityIdentity identity,
        string logicalRevision,
        string sourceRevision,
        string ruleDigest)
    {
        CharacterCareerQualityQuote[] matches = state.Quotes
            .Where(quote => quote.Operation == operation
                && quote.Identity == identity
                && string.Equals(
                    quote.LogicalRevision,
                    logicalRevision,
                    StringComparison.Ordinal)
                && string.Equals(
                    quote.SourceRevision,
                    sourceRevision,
                    StringComparison.Ordinal)
                && string.Equals(quote.RuleDigest, ruleDigest, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                "The SR5 quality identity, source, rule or logical authority is stale or ambiguous.");
        }

        return matches[0];
    }

    private static bool ExactPayloadEquals<T>(T expected, T actual)
        => string.Equals(
            JsonSerializer.Serialize(expected, ExactJson),
            JsonSerializer.Serialize(actual, ExactJson),
            StringComparison.Ordinal);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
