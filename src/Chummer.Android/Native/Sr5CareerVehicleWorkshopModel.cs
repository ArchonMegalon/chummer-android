using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public static class Sr5CareerVehicleWorkshopSchemas
{
    public const string CheckpointV1 = "chummer.android.sr5-career-vehicle-workshop.checkpoint.v1";
}

public static class Sr5CareerVehicleWorkshopNotices
{
    public const string DraftRestored = "vehicle-workshop-draft-restored";
    public const string ReviewStale = "vehicle-workshop-review-stale";
    public const string ReviewReady = "vehicle-workshop-review-ready";
    public const string CommitApplied = "vehicle-workshop-commit-applied";
    public const string CommitRecovered = "vehicle-workshop-commit-recovered";
    public const string CommitNotApplied = "vehicle-workshop-commit-not-applied";
    public const string RecoveryUnknown = "vehicle-workshop-recovery-unknown";
    public const string UndoApplied = "vehicle-workshop-undo-applied";
    public const string Reopened = "vehicle-workshop-reopened";
}

public enum Sr5CareerVehicleWorkshopPhase
{
    Editing,
    Reviewed,
    Applying,
    Applied,
    RecoveryUnknown
}

public sealed record Sr5CareerVehicleWorkshopCheckpoint(
    string SchemaId,
    CharacterWorkspaceId WorkspaceId,
    long BoundContentRevision,
    string BoundCharacterDigest,
    string BoundCatalogDigest,
    CharacterVehicleWorkshopSelection Selection,
    Sr5CareerVehicleWorkshopPhase Phase,
    CharacterVehicleWorkshopCommitCommand? Command,
    CharacterVehicleWorkshopCommitReceipt? Receipt)
{
    public bool BelongsTo(CharacterWorkspaceId workspaceId)
        => string.Equals(SchemaId, Sr5CareerVehicleWorkshopSchemas.CheckpointV1, StringComparison.Ordinal)
           && WorkspaceId == workspaceId
           && Enum.IsDefined(Phase)
           && Selection is not null
           && Selection.Modifications is not null
           && Selection.WeaponMounts is not null
           && PhaseStructureIsValid();

    public bool Matches(CharacterVehicleWorkshopPreparation preparation)
        => BoundContentRevision == preparation.ContentRevision
           && string.Equals(BoundCharacterDigest, preparation.CharacterDigest, StringComparison.Ordinal)
           && string.Equals(BoundCatalogDigest, preparation.CatalogDigest, StringComparison.Ordinal);

    public bool HasFreshReview(CharacterVehicleWorkshopQuote quote)
        => Phase == Sr5CareerVehicleWorkshopPhase.Reviewed
           && Command is { } command
           && command.ExpectedContentRevision == BoundContentRevision
           && string.Equals(command.ExpectedCharacterDigest, BoundCharacterDigest, StringComparison.Ordinal)
           && string.Equals(command.ExpectedCatalogDigest, BoundCatalogDigest, StringComparison.Ordinal)
           && string.Equals(command.ExpectedQuoteDigest, quote.QuoteDigest, StringComparison.Ordinal)
           && Sr5CareerVehicleWorkshopSelections.Equal(command.Selection, Selection);

    private bool PhaseStructureIsValid()
        => Phase switch
        {
            Sr5CareerVehicleWorkshopPhase.Editing => Command is null && Receipt is null,
            Sr5CareerVehicleWorkshopPhase.Reviewed => Command is { } command
                && Receipt is null
                && Sr5CareerVehicleWorkshopSelections.Equal(command.Selection, Selection),
            Sr5CareerVehicleWorkshopPhase.Applying or Sr5CareerVehicleWorkshopPhase.RecoveryUnknown => true,
            Sr5CareerVehicleWorkshopPhase.Applied => Command is not null && Receipt is not null,
            _ => false
        };
}

public sealed record Sr5CareerVehicleWorkshopSnapshot(
    CharacterWorkspaceId WorkspaceId,
    CharacterVehicleWorkshopPreparation? Preparation,
    CharacterVehicleWorkshopSelection Selection,
    CharacterVehicleWorkshopQuote? Quote,
    Sr5CareerVehicleWorkshopCheckpoint? Checkpoint,
    string Notice,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Preparation is { Exact: true } && Blockers.Count == 0;
    public bool CanReview => IsReady && Quote is { Exact: true }
        && Checkpoint?.Phase is null or Sr5CareerVehicleWorkshopPhase.Editing;
    public bool CanConfirm => Quote is { Exact: true } quote
        && Checkpoint?.HasFreshReview(quote) == true;
    public bool HasAppliedReceipt => Checkpoint is
        { Phase: Sr5CareerVehicleWorkshopPhase.Applied, Receipt: { } };
    public bool IsRecoveryUnknown => Checkpoint?.Phase == Sr5CareerVehicleWorkshopPhase.RecoveryUnknown;

    public static Sr5CareerVehicleWorkshopSnapshot Blocked(
        CharacterWorkspaceId workspaceId,
        params string[] blockers)
        => new(
            workspaceId,
            null,
            Sr5CareerVehicleWorkshopService.EmptySelection,
            null,
            null,
            string.Empty,
            blockers.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray());
}

public sealed record Sr5CareerVehicleWorkshopWorkspaceSnapshot(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    WorkspaceDocument Document);

public sealed record Sr5CareerVehicleWorkshopWorkspaceWriteResult(
    bool Applied,
    bool Conflict,
    long ContentRevision,
    long SavedRevision,
    string Error);

public interface ISr5CareerVehicleWorkshopWorkspaceStore
{
    Sr5CareerVehicleWorkshopWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId);
    Sr5CareerVehicleWorkshopWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CareerVehicleWorkshopWorkspaceSnapshot expected,
        string characterXml);
}

public interface ISr5CareerVehicleWorkshopCheckpointStore
{
    Sr5CareerVehicleWorkshopCheckpoint? Read(CharacterWorkspaceId workspaceId);
    void Write(Sr5CareerVehicleWorkshopCheckpoint checkpoint);
    void Clear(CharacterWorkspaceId workspaceId);
}

internal static class Sr5CareerVehicleWorkshopSelections
{
    public static bool Equal(
        CharacterVehicleWorkshopSelection? left,
        CharacterVehicleWorkshopSelection? right)
        => left is not null
           && right is not null
           && left.ChassisSourceId == right.ChassisSourceId
           && left.NewVehicleInstanceId == right.NewVehicleInstanceId
           && string.Equals(left.CustomName, right.CustomName, StringComparison.Ordinal)
           && string.Equals(left.GmAuthorityDigest, right.GmAuthorityDigest, StringComparison.Ordinal)
           && left.Modifications.SequenceEqual(right.Modifications)
           && left.WeaponMounts.SequenceEqual(right.WeaponMounts);
}
