using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public static class Sr5CareerCyberwarePurchaseSchemas
{
    public const string CheckpointV1 = "chummer.android.sr5-career-cyberware-purchase.checkpoint.v1";
}

public static class Sr5CareerCyberwarePurchaseNotices
{
    public const string DraftRestored = "cyberware-purchase-draft-restored";
    public const string ReviewStale = "cyberware-purchase-review-stale";
    public const string ReviewReady = "cyberware-purchase-review-ready";
    public const string CommitApplied = "cyberware-purchase-commit-applied";
    public const string CommitRecovered = "cyberware-purchase-commit-recovered";
    public const string CommitNotApplied = "cyberware-purchase-commit-not-applied";
    public const string RecoveryUnknown = "cyberware-purchase-recovery-unknown";
    public const string UndoApplied = "cyberware-purchase-undo-applied";
    public const string Reopened = "cyberware-purchase-reopened";
}

public enum Sr5CareerCyberwarePurchasePhase
{
    Editing = 0,
    Reviewed = 1,
    Applying = 2,
    Applied = 3,
    RecoveryUnknown = 4
}

public sealed record Sr5CareerCyberwarePurchaseCheckpoint(
    string SchemaId,
    CharacterWorkspaceId WorkspaceId,
    long BoundContentRevision,
    string BoundCharacterDigest,
    string BoundCatalogDigest,
    CharacterCyberwarePurchaseSelection Selection,
    Sr5CareerCyberwarePurchasePhase Phase,
    CharacterCyberwarePurchaseCommand? Command,
    CharacterCyberwarePurchaseUndoReceipt? Receipt)
{
    public bool BelongsTo(CharacterWorkspaceId workspaceId)
        => string.Equals(SchemaId, Sr5CareerCyberwarePurchaseSchemas.CheckpointV1, StringComparison.Ordinal)
           && WorkspaceId == workspaceId
           && Selection is not null;

    public bool Matches(CharacterCyberwarePurchasePreparation preparation)
        => BoundContentRevision == preparation.ContentRevision
           && string.Equals(BoundCharacterDigest, preparation.CharacterDigest, StringComparison.Ordinal)
           && string.Equals(BoundCatalogDigest, preparation.CatalogDigest, StringComparison.Ordinal);

    public bool HasFreshReview(CharacterCyberwarePurchaseQuote quote)
        => Phase == Sr5CareerCyberwarePurchasePhase.Reviewed
           && Command is not null
           && Command.ExpectedContentRevision == BoundContentRevision
           && string.Equals(Command.ExpectedCharacterDigest, BoundCharacterDigest, StringComparison.Ordinal)
           && string.Equals(Command.ExpectedCatalogDigest, BoundCatalogDigest, StringComparison.Ordinal)
           && string.Equals(Command.ExpectedQuoteDigest, quote.QuoteDigest, StringComparison.Ordinal)
           && Command.Selection == Selection;
}

public sealed record Sr5CareerCyberwarePurchaseSnapshot(
    CharacterWorkspaceId WorkspaceId,
    CharacterCyberwarePurchasePreparation? Preparation,
    CharacterCyberwarePurchaseSelection Selection,
    CharacterCyberwarePurchaseQuote? Quote,
    Sr5CareerCyberwarePurchaseCheckpoint? Checkpoint,
    string Notice,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Preparation is { Exact: true } && Blockers.Count == 0;

    public bool CanReview => IsReady
        && Quote is { Exact: true }
        && Checkpoint?.Phase is null or Sr5CareerCyberwarePurchasePhase.Editing;

    public bool CanConfirm => Quote is { Exact: true } quote
        && Checkpoint?.HasFreshReview(quote) == true;

    public bool HasAppliedReceipt
        => Checkpoint is
        {
            Phase: Sr5CareerCyberwarePurchasePhase.Applied,
            Receipt: { }
        };

    public bool IsRecoveryUnknown
        => Checkpoint?.Phase == Sr5CareerCyberwarePurchasePhase.RecoveryUnknown;

    public static Sr5CareerCyberwarePurchaseSnapshot Blocked(
        CharacterWorkspaceId workspaceId,
        params string[] blockers)
        => new(
            workspaceId,
            null,
            Sr5CareerCyberwarePurchaseService.EmptySelection,
            null,
            null,
            string.Empty,
            blockers.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray());
}

public interface ISr5CareerCyberwarePurchaseCheckpointStore
{
    Sr5CareerCyberwarePurchaseCheckpoint? Read(CharacterWorkspaceId workspaceId);

    void Write(Sr5CareerCyberwarePurchaseCheckpoint checkpoint);

    void Clear(CharacterWorkspaceId workspaceId);
}
