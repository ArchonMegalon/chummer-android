using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public static class Sr5CareerCustomDrugRecipeSchemas
{
    public const string CheckpointV1 = "chummer.android.sr5-career-custom-drug-recipe.checkpoint.v1";
}

public static class Sr5CareerCustomDrugRecipeNotices
{
    public const string DraftRestored = "custom-drug-recipe-draft-restored";
    public const string ReviewStale = "custom-drug-recipe-review-stale";
    public const string ReviewReady = "custom-drug-recipe-review-ready";
    public const string CommitApplied = "custom-drug-recipe-commit-applied";
    public const string CommitRecovered = "custom-drug-recipe-commit-recovered";
    public const string CommitNotApplied = "custom-drug-recipe-commit-not-applied";
    public const string RecoveryUnknown = "custom-drug-recipe-recovery-unknown";
    public const string UndoApplied = "custom-drug-recipe-undo-applied";
    public const string Reopened = "custom-drug-recipe-reopened";
}

public enum Sr5CareerCustomDrugRecipePhase
{
    Editing = 0,
    Reviewed = 1,
    Applying = 2,
    Applied = 3,
    RecoveryUnknown = 4
}

public sealed record Sr5CareerCustomDrugRecipeCheckpoint(
    string SchemaId,
    CharacterWorkspaceId WorkspaceId,
    long BoundContentRevision,
    string BoundCharacterDigest,
    string BoundCatalogDigest,
    string BoundRulesDigest,
    CharacterCustomDrugSelection Selection,
    Sr5CareerCustomDrugRecipePhase Phase,
    CharacterCustomDrugCommitCommand? Command,
    CharacterCustomDrugCommitReceipt? Receipt)
{
    public bool BelongsTo(CharacterWorkspaceId workspaceId)
        => string.Equals(SchemaId, Sr5CareerCustomDrugRecipeSchemas.CheckpointV1, StringComparison.Ordinal)
           && WorkspaceId == workspaceId
           && Enum.IsDefined(Phase)
           && Selection is not null
           && Selection.Components is not null
           && Selection.Components.All(static component => component is not null);

    public bool Matches(CharacterCustomDrugPreparation preparation)
        => BoundContentRevision == preparation.ContentRevision
           && string.Equals(BoundCharacterDigest, preparation.CharacterDigest, StringComparison.Ordinal)
           && string.Equals(BoundCatalogDigest, preparation.CatalogDigest, StringComparison.Ordinal)
           && string.Equals(BoundRulesDigest, preparation.RulesDigest, StringComparison.Ordinal);

    public bool HasFreshReview(CharacterCustomDrugQuote quote)
        => Phase == Sr5CareerCustomDrugRecipePhase.Reviewed
           && Command is not null
           && Command.ExpectedContentRevision == BoundContentRevision
           && string.Equals(Command.ExpectedCharacterDigest, BoundCharacterDigest, StringComparison.Ordinal)
           && string.Equals(Command.ExpectedCatalogDigest, BoundCatalogDigest, StringComparison.Ordinal)
           && string.Equals(Command.ExpectedRulesDigest, BoundRulesDigest, StringComparison.Ordinal)
           && string.Equals(Command.ExpectedQuoteDigest, quote.QuoteDigest, StringComparison.Ordinal)
           && Sr5CareerCustomDrugRecipeSelections.Equal(Command.Selection, Selection);
}

public sealed record Sr5CareerCustomDrugRecipeSnapshot(
    CharacterWorkspaceId WorkspaceId,
    CharacterCustomDrugPreparation? Preparation,
    CharacterCustomDrugSelection Selection,
    CharacterCustomDrugQuote? Quote,
    Sr5CareerCustomDrugRecipeCheckpoint? Checkpoint,
    string Notice,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Preparation is { Exact: true } && Blockers.Count == 0;

    public bool CanReview => IsReady
        && Quote is { Exact: true }
        && Checkpoint?.Phase is null or Sr5CareerCustomDrugRecipePhase.Editing;

    public bool CanConfirm => Quote is { Exact: true } quote
        && Checkpoint?.HasFreshReview(quote) == true;

    public bool HasAppliedReceipt
        => Checkpoint is
        {
            Phase: Sr5CareerCustomDrugRecipePhase.Applied,
            Receipt: { }
        };

    public bool IsRecoveryUnknown
        => Checkpoint?.Phase == Sr5CareerCustomDrugRecipePhase.RecoveryUnknown;

    public static Sr5CareerCustomDrugRecipeSnapshot Blocked(
        CharacterWorkspaceId workspaceId,
        params string[] blockers)
        => new(
            workspaceId,
            null,
            Sr5CareerCustomDrugRecipeService.EmptySelection,
            null,
            null,
            string.Empty,
            blockers.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray());
}

public sealed record Sr5CareerCustomDrugWorkspaceSnapshot(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    WorkspaceDocument Document);

public sealed record Sr5CareerCustomDrugWorkspaceWriteResult(
    bool Applied,
    bool Conflict,
    long ContentRevision,
    long SavedRevision,
    string Error);

public interface ISr5CareerCustomDrugWorkspaceStore
{
    Sr5CareerCustomDrugWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId);

    Sr5CareerCustomDrugWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CareerCustomDrugWorkspaceSnapshot expected,
        string characterXml);
}

public interface ISr5CareerCustomDrugRecipeCheckpointStore
{
    Sr5CareerCustomDrugRecipeCheckpoint? Read(CharacterWorkspaceId workspaceId);

    void Write(Sr5CareerCustomDrugRecipeCheckpoint checkpoint);

    void Clear(CharacterWorkspaceId workspaceId);
}

internal static class Sr5CareerCustomDrugRecipeSelections
{
    public static bool Equal(CharacterCustomDrugSelection? left, CharacterCustomDrugSelection? right)
        => left is not null
           && right is not null
           && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
           && left.GradeId == right.GradeId
           && left.Quantity == right.Quantity
           && left.Stolen == right.Stolen
           && left.FreeCost == right.FreeCost
           && left.MarkupPercent == right.MarkupPercent
           && left.Components is not null
           && right.Components is not null
           && left.Components.SequenceEqual(right.Components);
}
