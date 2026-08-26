using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed record Sr5CareerSpecializationApplyObservation(
    CareerSkillSpecializationEditorState Editor,
    CharacterCareerSkillSpecializationQuote Quote,
    long SavedContentRevision);

public interface ISr5CareerSpecializationPresenter
{
    Sr5CareerRunnerBinding Binding { get; }

    Task<CareerSkillSpecializationEditorState?> LoadAsync(CancellationToken cancellationToken);

    Task<CharacterCareerSkillSpecializationQuote?> QuoteAsync(
        CareerSkillSpecializationQuoteRequest request,
        CancellationToken cancellationToken);

    Task<Sr5CareerSpecializationApplyObservation?> ApplyAndSaveAsync(
        CareerSkillSpecializationRequest request,
        CancellationToken cancellationToken);
}

internal sealed class RunnerSessionSr5CareerSpecializationPresenter(
    RunnerSessionCoordinator coordinator) : ISr5CareerSpecializationPresenter
{
    public Sr5CareerRunnerBinding Binding => new(
        coordinator.State.Profile?.Created == true,
        coordinator.State.Rules?.GameEdition,
        coordinator.State.WorkspaceId,
        coordinator.State.ContentRevision,
        coordinator.State.SavedRevision,
        coordinator.State.IsDirty,
        coordinator.State.Error);

    public Task<CareerSkillSpecializationEditorState?> LoadAsync(CancellationToken cancellationToken)
        => coordinator.PrepareCareerSkillSpecializationAsync(cancellationToken);

    public Task<CharacterCareerSkillSpecializationQuote?> QuoteAsync(
        CareerSkillSpecializationQuoteRequest request,
        CancellationToken cancellationToken)
        => coordinator.PrepareCareerSkillSpecializationQuoteAsync(request, cancellationToken);

    public Task<Sr5CareerSpecializationApplyObservation?> ApplyAndSaveAsync(
        CareerSkillSpecializationRequest request,
        CancellationToken cancellationToken)
        => coordinator.ApplyCareerSkillSpecializationAsync(request, cancellationToken);
}

public sealed class Sr5CareerSpecializationCoordinator(
    ISr5CareerSpecializationPresenter presenter,
    ISr5CareerCheckpointOwnerAuthority ownerAuthority)
{
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

    public async Task<CareerSkillSpecializationEditorState?> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        Sr5CareerRunnerGuard.RequireCreated(presenter.Binding);
        CareerSkillSpecializationEditorState? editor =
            await presenter.LoadAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerRunnerGuard.RequireCreated(after);
        if (editor is not null
            && (after.WorkspaceId != editor.WorkspaceId
                || after.ContentRevision != editor.ContentRevision))
        {
            throw new InvalidOperationException(
                "The SR5 runner changed while specialization choices were loading.");
        }
        return editor;
    }

    public async Task<CharacterCareerSkillSpecializationQuote?> QuoteAsync(
        CareerSkillSpecializationEditorState editor,
        CharacterCareerSkillIdentity identity,
        CharacterCareerSkillSpecializationSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(selection);
        Sr5CareerRunnerGuard.RequireCreated(presenter.Binding);
        if (presenter.Binding.WorkspaceId != editor.WorkspaceId
            || presenter.Binding.ContentRevision != editor.ContentRevision)
        {
            throw new InvalidOperationException(
                "The runner changed before the specialization could be quoted.");
        }
        CharacterCareerSkillSpecializationQuote? quote = await presenter.QuoteAsync(
            new CareerSkillSpecializationQuoteRequest(
                editor.WorkspaceId,
                editor.ContentRevision,
                identity,
                selection),
            cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerRunnerGuard.RequireCreated(after);
        if (quote is not null
            && (after.WorkspaceId != editor.WorkspaceId
                || after.ContentRevision != editor.ContentRevision
                || !CharacterCareerSkillSpecializationRules.IsCoherent(quote)))
        {
            throw new InvalidOperationException(
                "The specialization quote is stale or incoherent under the current runner revision.");
        }
        return quote;
    }

    public async Task<Sr5CareerSpecializationApplyResult> ApplyAsync(
        Sr5CareerSpecializationDraft draft,
        Sr5CareerSpecializationCheckpoint applyingCheckpoint,
        Sr5CareerSpecializationCheckpointStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(applyingCheckpoint);
        ArgumentNullException.ThrowIfNull(store);
        using IDisposable lease = await store.AcquireDurableApplyingLeaseAsync(
            applyingCheckpoint,
            cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding before = presenter.Binding;
        Sr5CareerRunnerGuard.RequireCreated(before);
        if (ownerAuthority.CurrentOwnerId != draft.OwnerId
            || before.WorkspaceId != draft.WorkspaceId
            || before.ContentRevision != draft.ExpectedContentRevision
            || before.SavedRevision != draft.ExpectedContentRevision
            || before.IsDirty
            || !string.IsNullOrWhiteSpace(before.Error)
            || !draft.IsExact()
            || !applyingCheckpoint.MatchesActionDraft(draft))
        {
            throw new InvalidOperationException(
                "The reviewed specialization action does not own this exact clean runner revision.");
        }

        Sr5CareerSpecializationApplyObservation? observation;
        try
        {
            observation = await presenter.ApplyAndSaveAsync(
                draft.ToRequest(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            observation = null;
        }

        if (!TryCreateImmediateReceipt(draft, observation, out Sr5CareerSpecializationReceipt receipt))
        {
            return new(
                Sr5CareerSpecializationRecoveryStatus.OutcomeUnknown,
                draft.ActionPlan,
                Receipt: null,
                "No complete persisted Core receipt authority exists for specialization recovery. The Applying lock remains; do not replay or clear it.");
        }
        return new(
            Sr5CareerSpecializationRecoveryStatus.AppliedVerifiedInCurrentProcess,
            draft.ActionPlan,
            receipt,
            "The current process verified the saved revision and fresh typed specialization projection.");
    }

    public async Task<Sr5CareerSpecializationResolution> ResolveAsync(
        Sr5CareerSpecializationCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding binding = presenter.Binding;
        Sr5CareerRunnerGuard.RequireCreated(binding);
        if (!checkpoint.IsStructurallyValid()
            || ownerAuthority.CurrentOwnerId != checkpoint.Draft.OwnerId
            || binding.WorkspaceId != checkpoint.Draft.WorkspaceId
            || binding.IsDirty
            || !string.IsNullOrWhiteSpace(binding.Error))
        {
            throw new InvalidOperationException(
                "The specialization recovery lock does not own this authenticated SR5 runner.");
        }

        if (binding.ContentRevision != checkpoint.Draft.ExpectedContentRevision
            || binding.SavedRevision != checkpoint.Draft.ExpectedContentRevision)
        {
            return SignResolution(
                checkpoint,
                Sr5CareerSpecializationRecoveryStatus.OutcomeUnknown,
                "A changed revision cannot prove the persisted specialization and expense identities. Do not replay or clear this lock.");
        }

        CareerSkillSpecializationEditorState? editor =
            await presenter.LoadAsync(cancellationToken).ConfigureAwait(false);
        CharacterCareerSkillSpecializationQuote? quote = editor is null
            ? null
            : await presenter.QuoteAsync(
                new CareerSkillSpecializationQuoteRequest(
                    editor.WorkspaceId,
                    editor.ContentRevision,
                    checkpoint.Draft.Quote.Identity,
                    checkpoint.Draft.Quote.Selection),
                cancellationToken).ConfigureAwait(false);
        if (editor is not null
            && quote is not null
            && editor.WorkspaceId == checkpoint.Draft.WorkspaceId
            && editor.ContentRevision == checkpoint.Draft.ExpectedContentRevision
            && QuotesMatchExactly(quote, checkpoint.Draft.Quote))
        {
            return SignResolution(
                checkpoint,
                Sr5CareerSpecializationRecoveryStatus.NotAppliedVerified,
                "Fresh typed projections prove the reviewed mutation was not applied.");
        }
        return SignResolution(
            checkpoint,
            Sr5CareerSpecializationRecoveryStatus.OutcomeUnknown,
            "The current projection no longer proves the original quote. Do not replay or clear this lock.");
    }

    internal static bool VerifiesReceipt(
        Sr5CareerSpecializationDraft draft,
        Sr5CareerSpecializationReceipt receipt)
    {
        if (!draft.IsExact()
            || receipt.WorkspaceId != draft.WorkspaceId.Value
            || receipt.SavedContentRevision != draft.ExpectedContentRevision + 1
            || receipt.OwnerId != draft.OwnerId
            || receipt.ActionId != draft.Plan.ExpenseId
            || receipt.SkillIdentity != draft.Quote.Identity
            || receipt.SpecializationId != draft.Plan.SpecializationId
            || receipt.ExpenseId != draft.Plan.ExpenseId
            || !string.Equals(receipt.SpecializationName, draft.Plan.SpecializationName, StringComparison.Ordinal)
            || receipt.SpecializationCountBefore != draft.Quote.ExistingSpecializationCount
            || receipt.SpecializationCountAfter != draft.Quote.ExistingSpecializationCount + 1
            || receipt.KarmaBefore != draft.Quote.AvailableKarma
            || receipt.KarmaAfter != draft.Plan.SavedCharacterKarma
            || !string.Equals(receipt.ReviewedCharacterRevision, draft.Quote.CharacterRevision, StringComparison.Ordinal)
            || !string.Equals(receipt.ReviewedSourceRevision, draft.Quote.SourceRevision, StringComparison.Ordinal)
            || !string.Equals(receipt.ReviewedRuleDigest, draft.Quote.RuleDigest, StringComparison.Ordinal)
            || !string.Equals(receipt.ReviewedLogicalRevision, draft.Quote.LogicalRevision, StringComparison.Ordinal))
        {
            return false;
        }
        return FixedTimeEquals(receipt.ProcessProof, SignReceipt(receipt with { ProcessProof = string.Empty }));
    }

    internal static bool VerifiesResolution(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationResolution resolution)
        => resolution.Receipt is null
            && FixedTimeEquals(
                resolution.ProcessProof,
                SignResolutionPayload(checkpoint, resolution.Status, resolution.Message));

    private static bool TryCreateImmediateReceipt(
        Sr5CareerSpecializationDraft draft,
        Sr5CareerSpecializationApplyObservation? observation,
        out Sr5CareerSpecializationReceipt receipt)
    {
        receipt = null!;
        if (observation is null
            || observation.SavedContentRevision != draft.ExpectedContentRevision + 1
            || observation.Editor.WorkspaceId != draft.WorkspaceId
            || observation.Editor.ContentRevision != observation.SavedContentRevision
            || !CharacterCareerSkillSpecializationRules.IsCoherent(observation.Quote)
            || observation.Quote.Identity != draft.Quote.Identity
            || observation.Quote.Selection != draft.Quote.Selection
            || observation.Quote.ExistingSpecializationCount != draft.Quote.ExistingSpecializationCount + 1
            || observation.Quote.AvailableKarma != draft.Plan.SavedCharacterKarma
            || observation.Quote.TotalBaseRating != draft.Quote.TotalBaseRating
            || !string.Equals(observation.Quote.SourceRevision, draft.Quote.SourceRevision, StringComparison.Ordinal)
            || !string.Equals(observation.Quote.RuleDigest, draft.Quote.RuleDigest, StringComparison.Ordinal)
            || observation.Editor.Skills.Count(candidate =>
                Sr5CareerSpecializationDraft.CandidateMatchesQuote(candidate, observation.Quote)) != 1)
        {
            return false;
        }
        Sr5CareerSpecializationReceipt unsigned = new(
            draft.WorkspaceId.Value,
            observation.SavedContentRevision,
            draft.OwnerId,
            draft.Plan.ExpenseId,
            draft.Quote.Identity,
            draft.Plan.SpecializationId,
            draft.Plan.ExpenseId,
            draft.Plan.SpecializationName,
            draft.Quote.ExistingSpecializationCount,
            observation.Quote.ExistingSpecializationCount,
            draft.Quote.AvailableKarma,
            observation.Quote.AvailableKarma,
            draft.Quote.CharacterRevision,
            draft.Quote.SourceRevision,
            draft.Quote.RuleDigest,
            draft.Quote.LogicalRevision,
            ProcessProof: string.Empty);
        receipt = unsigned with { ProcessProof = SignReceipt(unsigned) };
        return VerifiesReceipt(draft, receipt);
    }

    private static bool QuotesMatchExactly(
        CharacterCareerSkillSpecializationQuote current,
        CharacterCareerSkillSpecializationQuote reviewed)
    {
        try
        {
            return CharacterCareerSkillSpecializationRules.IsCoherent(current)
                && CharacterCareerSkillSpecializationRules.IsCoherent(reviewed)
                && string.Equals(
                    JsonSerializer.Serialize(current),
                    JsonSerializer.Serialize(reviewed),
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static Sr5CareerSpecializationResolution SignResolution(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationRecoveryStatus status,
        string message)
        => new(status, Receipt: null, message, SignResolutionPayload(checkpoint, status, message));

    private static string SignReceipt(Sr5CareerSpecializationReceipt receipt)
        => Sign(JsonSerializer.Serialize(receipt));

    private static string SignResolutionPayload(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationRecoveryStatus status,
        string message)
        => Sign(string.Join("\n", JsonSerializer.Serialize(checkpoint), status, message));

    private static string Sign(string payload)
        => Convert.ToHexString(HMACSHA256.HashData(ProcessKey, Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

    private static bool FixedTimeEquals(string actualHex, string expectedHex)
    {
        try
        {
            byte[] actual = Convert.FromHexString(actualHex);
            byte[] expected = Convert.FromHexString(expectedHex);
            return actual.Length == expected.Length
                && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
