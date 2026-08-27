using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public static class Sr5CustomDrugLabSchemas
{
    public const string CheckpointV1 = "chummer.android.sr5-custom-drug.checkpoint.v1";
    public const string CreationContributionV1 =
        "chummer.sr5.creation-custom-drug.finalization-contribution.v1";
}

public static class Sr5CustomDrugLabNotices
{
    public const string DraftRestored = "custom-drug-draft-restored";
    public const string ReviewStale = "custom-drug-review-stale";
    public const string ReviewReady = "custom-drug-review-ready";
    public const string QueuedForFinalization = "custom-drug-queued-for-finalization";
    public const string CommitRecovered = "custom-drug-commit-recovered";
    public const string CommitApplied = "custom-drug-commit-applied";
    public const string CommitNotApplied = "custom-drug-commit-not-applied";
    public const string RecoveryUnknown = "custom-drug-recovery-unknown";
    public const string UndoApplied = "custom-drug-undo-applied";
}

public enum Sr5CustomDrugCheckpointPhase
{
    Editing = 0,
    Reviewed = 1,
    QueuedForFinalization = 2,
    Applying = 3,
    Applied = 4,
    RecoveryUnknown = 5
}

/// <summary>
/// The exact hand-off expected by the atomic whole-character creation finalizer.
/// Android never interprets this as permission to mutate XML. The finalizer must
/// re-Prepare in Creation context, re-Quote the Selection, verify every binding
/// and identity below, then include the recipe in its single atomic write.
/// </summary>
public sealed record Sr5CreationCustomDrugFinalizationContribution(
    string SchemaId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string ExpectedCharacterDigest,
    string ExpectedCatalogDigest,
    string ExpectedRulesDigest,
    CharacterCustomDrugSelection Selection,
    CharacterCustomDrugQuote Quote,
    CharacterCustomDrugInstanceId NewDrugInstanceId,
    IReadOnlyList<Guid> NewComponentInstanceIds,
    string ContributionDigest)
{
    public CharacterCustomDrugCommitCommand ToVerificationCommand()
        => new(
            ExpectedContentRevision,
            ExpectedCharacterDigest,
            ExpectedCatalogDigest,
            ExpectedRulesDigest,
            Quote.QuoteDigest,
            FinalizerIdempotencyKey(WorkspaceId, NewDrugInstanceId),
            Selection,
            NewDrugInstanceId,
            NewComponentInstanceIds);

    public bool IsStructurallyValid()
        => string.Equals(SchemaId, Sr5CustomDrugLabSchemas.CreationContributionV1, StringComparison.Ordinal)
           && !string.IsNullOrWhiteSpace(WorkspaceId.Value)
           && ExpectedContentRevision >= 0
           && Quote.Exact
           && Quote.QuoteDigest.Length == 64
           && NewDrugInstanceId.Value != Guid.Empty
           && NewComponentInstanceIds.Count == Selection.Components.Count
           && NewComponentInstanceIds.All(static value => value != Guid.Empty)
           && NewComponentInstanceIds.Distinct().Count() == NewComponentInstanceIds.Count
           && string.Equals(
               ContributionDigest,
               ComputeDigest(this with { ContributionDigest = string.Empty }),
               StringComparison.Ordinal);

    public static Sr5CreationCustomDrugFinalizationContribution Create(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugCommitCommand command,
        CharacterCustomDrugQuote quote)
    {
        var unsigned = new Sr5CreationCustomDrugFinalizationContribution(
            Sr5CustomDrugLabSchemas.CreationContributionV1,
            workspaceId,
            command.ExpectedContentRevision,
            command.ExpectedCharacterDigest,
            command.ExpectedCatalogDigest,
            command.ExpectedRulesDigest,
            command.Selection,
            quote,
            command.NewDrugInstanceId,
            command.NewComponentInstanceIds.ToArray(),
            string.Empty);
        return unsigned with { ContributionDigest = ComputeDigest(unsigned) };
    }

    private static string ComputeDigest(Sr5CreationCustomDrugFinalizationContribution value)
    {
        CharacterCustomDrugCommitCommand command = value.ToVerificationCommand();
        string canonical = string.Join(
            "\n",
            value.SchemaId,
            value.WorkspaceId.Value,
            value.ExpectedContentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CharacterCustomDrugRules.ComputeCommandDigest(command));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string FinalizerIdempotencyKey(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugInstanceId drugId)
    {
        string workspaceDigest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(workspaceId.Value)))
            .ToLowerInvariant();
        return $"creation-custom-drug:{workspaceDigest}:{drugId.Value:N}";
    }
}

public sealed record Sr5CustomDrugLabCheckpoint(
    string SchemaId,
    CharacterWorkspaceId WorkspaceId,
    CharacterCustomDrugContext Context,
    long BoundContentRevision,
    string BoundCharacterDigest,
    string BoundCatalogDigest,
    string BoundRulesDigest,
    CharacterCustomDrugSelection Selection,
    Sr5CustomDrugCheckpointPhase Phase,
    CharacterCustomDrugCommitCommand? Command,
    CharacterCustomDrugCommitReceipt? Receipt,
    Sr5CreationCustomDrugFinalizationContribution? CreationContribution)
{
    public bool BelongsTo(CharacterWorkspaceId workspaceId, CharacterCustomDrugContext context)
        => string.Equals(SchemaId, Sr5CustomDrugLabSchemas.CheckpointV1, StringComparison.Ordinal)
           && WorkspaceId == workspaceId
           && Context == context
           && Selection is not null;

    public bool Matches(CharacterCustomDrugPreparation preparation)
        => BoundContentRevision == preparation.ContentRevision
           && string.Equals(BoundCharacterDigest, preparation.CharacterDigest, StringComparison.Ordinal)
           && string.Equals(BoundCatalogDigest, preparation.CatalogDigest, StringComparison.Ordinal)
           && string.Equals(BoundRulesDigest, preparation.RulesDigest, StringComparison.Ordinal);

    public bool HasFreshReview(CharacterCustomDrugQuote quote)
        => Phase == Sr5CustomDrugCheckpointPhase.Reviewed
           && Command is not null
           && Command.ExpectedContentRevision == BoundContentRevision
           && string.Equals(Command.ExpectedCharacterDigest, BoundCharacterDigest, StringComparison.Ordinal)
           && string.Equals(Command.ExpectedCatalogDigest, BoundCatalogDigest, StringComparison.Ordinal)
           && string.Equals(Command.ExpectedRulesDigest, BoundRulesDigest, StringComparison.Ordinal)
           && string.Equals(Command.ExpectedQuoteDigest, quote.QuoteDigest, StringComparison.Ordinal)
           && SelectionEquals(Command.Selection, Selection);

    private static bool SelectionEquals(
        CharacterCustomDrugSelection left,
        CharacterCustomDrugSelection right)
        => string.Equals(left.Name, right.Name, StringComparison.Ordinal)
           && left.GradeId == right.GradeId
           && left.Quantity == right.Quantity
           && left.Stolen == right.Stolen
           && left.FreeCost == right.FreeCost
           && left.MarkupPercent == right.MarkupPercent
           && left.Components.SequenceEqual(right.Components);
}

public sealed record Sr5CustomDrugLabSnapshot(
    CharacterWorkspaceId WorkspaceId,
    CharacterCustomDrugContext Context,
    CharacterCustomDrugPreparation? Preparation,
    CharacterCustomDrugSelection Selection,
    CharacterCustomDrugQuote? Quote,
    Sr5CustomDrugLabCheckpoint? Checkpoint,
    string Notice,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Preparation is { Exact: true } && Blockers.Count == 0;

    public bool CanReview => IsReady
        && Quote is { Exact: true }
        && Checkpoint?.Phase is null or Sr5CustomDrugCheckpointPhase.Editing;

    public bool CanConfirm => Quote is { Exact: true } quote
        && Checkpoint?.HasFreshReview(quote) == true;

    public bool IsQueuedForFinalization
        => Checkpoint is
        {
            Context: CharacterCustomDrugContext.Creation,
            Phase: Sr5CustomDrugCheckpointPhase.QueuedForFinalization,
            CreationContribution: { }
        };

    public bool HasAppliedReceipt
        => Checkpoint is
        {
            Context: CharacterCustomDrugContext.Career,
            Phase: Sr5CustomDrugCheckpointPhase.Applied,
            Receipt: { }
        };

    public static Sr5CustomDrugLabSnapshot Blocked(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context,
        params string[] blockers)
        => new(
            workspaceId,
            context,
            null,
            Sr5CustomDrugLabService.EmptySelection,
            null,
            null,
            string.Empty,
            blockers.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray());
}

public interface ISr5CustomDrugLabCheckpointStore
{
    Sr5CustomDrugLabCheckpoint? Read(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context);

    void Write(Sr5CustomDrugLabCheckpoint checkpoint);

    void Clear(CharacterWorkspaceId workspaceId, CharacterCustomDrugContext context);
}
