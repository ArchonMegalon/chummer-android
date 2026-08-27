using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public static class Sr5CustomDrugLabSchemas
{
    public const string CheckpointV1 = "chummer.android.sr5-custom-drug.checkpoint.v1";
}

public static class Sr5CustomDrugLabNotices
{
    public const string DraftRestored = "custom-drug-draft-restored";
    public const string ReviewStale = "custom-drug-review-stale";
    public const string ReviewReady = "custom-drug-review-ready";
    public const string QueuedForFinalization = "custom-drug-queued-for-finalization";
    public const string FinalizerContributionRecovered =
        "custom-drug-finalizer-contribution-recovered";
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
    CharacterCreationCustomDrugFinalizationContribution? CreationContribution)
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
