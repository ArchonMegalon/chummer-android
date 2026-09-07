using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>Host selection facts, not a replacement for Core's saved snapshot.</summary>
public sealed record Sr5AfterRunRewardRunnerBinding(
    Guid OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ActivationGeneration,
    bool Created,
    string? GameEdition,
    long ContentRevision,
    long SavedRevision,
    bool IsDirty,
    string? Error)
{
    public bool IsCleanSavedSr5()
        => OwnerId != Guid.Empty
            && ActivationGeneration > 0
            && CharacterAfterRunRewardProjector.IsValidWorkspaceId(WorkspaceId)
            && Created
            && string.Equals(GameEdition, "SR5", StringComparison.OrdinalIgnoreCase)
            && ContentRevision > 0
            && SavedRevision == ContentRevision
            && !IsDirty
            && string.IsNullOrWhiteSpace(Error);

    public bool SameSelection(Sr5AfterRunRewardRunnerBinding other)
        => OwnerId == other.OwnerId && WorkspaceId == other.WorkspaceId
            && ActivationGeneration == other.ActivationGeneration;
}

/// <summary>Current must return an immutable, thread-safe host selection snapshot.</summary>
public interface ISr5AfterRunRewardRunnerAuthority
{
    Sr5AfterRunRewardRunnerBinding Current { get; }
}

/// <summary>In-memory review only. Previews are never written to the journal.</summary>
public sealed record Sr5AfterRunRewardReview(
    Sr5AfterRunRewardRunnerBinding Runner,
    CharacterAfterRunRewardPreview Preview)
{
    public bool IsExact()
        => Runner is not null && Runner.IsCleanSavedSr5()
            && CharacterAfterRunRewardProjector.IsCoherentPreview(Preview)
            && Preview.Command.WorkspaceId == Runner.WorkspaceId
            && Preview.Command.ExpectedWorkspaceRevision == Runner.ContentRevision;
}

public sealed record Sr5AfterRunRewardPreparation(
    CharacterAfterRunRewardOutcome Outcome,
    Sr5AfterRunRewardReview? Review = null,
    string? Blocker = null);

public enum Sr5AfterRunRewardCheckpointPhase
{
    Confirmed,
    Applying,
    Applied
}

/// <summary>
/// Immutable caller identity and exact confirmed Core command. ApplyingVersion
/// remains 2 after resolution so interrupted owner release uses its original key.
/// </summary>
public sealed record Sr5AfterRunRewardCheckpoint(
    int SchemaVersion,
    long Version,
    Guid OwnerId,
    Sr5AfterRunRewardCheckpointPhase Phase,
    CharacterAfterRunRewardCommand Command,
    string CommandDigest,
    CharacterAfterRunRewardReceipt? Receipt = null)
{
    public const int CurrentSchemaVersion = 1;
    public const long ApplyingVersion = 2;

    public bool IsExact()
    {
        if (SchemaVersion != CurrentSchemaVersion || OwnerId == Guid.Empty
            || !Enum.IsDefined(Phase)
            || !CharacterAfterRunRewardProjector.IsValidCommand(Command)
            || !CharacterAfterRunRewardProjector.IsDigest(CommandDigest)
            || CommandDigest != Command.CommandDigest())
            return false;
        return Phase switch
        {
            Sr5AfterRunRewardCheckpointPhase.Confirmed => Version == 1 && Receipt is null,
            Sr5AfterRunRewardCheckpointPhase.Applying => Version == ApplyingVersion && Receipt is null,
            Sr5AfterRunRewardCheckpointPhase.Applied => Version == 3 && ReceiptMatches(Receipt),
            _ => false
        };
    }

    public bool ReceiptMatches(CharacterAfterRunRewardReceipt? receipt)
        => CharacterAfterRunRewardReceiptLedgerIntegrity.IsCoherent(Command.WorkspaceId, receipt)
            && receipt!.OperationId == Command.OperationId
            && receipt.RewardId == Command.RewardId
            && receipt.CommandDigest == CommandDigest
            && CharacterAfterRunRewardProjector.CanonicalEquals(receipt.Command, Command);

    internal Sr5CareerMutationOwner MutationOwner()
        => new(Sr5CareerMutationOwner.CurrentSchemaVersion, "after-run-reward",
            Command.WorkspaceId.Value, OwnerId, Command.OperationId, ApplyingVersion,
            Command.ExpectedWorkspaceRevision, CommandDigest);
}

public enum Sr5AfterRunRewardResolutionStatus
{
    Recorded,
    Blocked,
    OutcomeUnknown
}

/// <summary>
/// Journal/ownership observation for one local runner. Recorded entries still
/// need a live Core Lookup before handoff; their presence is not current proof.
/// RecoveryRequired also includes an Applied receipt with an unreleased owner.
/// </summary>
public sealed record Sr5AfterRunRewardEntryState(
    IReadOnlyList<Sr5AfterRunRewardCheckpoint> Recorded,
    Sr5AfterRunRewardCheckpoint? RecoveryRequired);

public sealed record Sr5AfterRunRewardResolution(
    Sr5AfterRunRewardResolutionStatus Status,
    Sr5AfterRunRewardCheckpoint? Checkpoint,
    string Message,
    long? CurrentWorkspaceRevision = null);

/// <summary>
/// Process-bound proof minted only after the coordinator observed the actual
/// service. JSON receipt coherence alone cannot release durable mutation ownership.
/// This is local process integrity, not a signature from a run or GM authority.
/// </summary>
internal sealed record Sr5AfterRunRewardReceiptObservation(
    CharacterAfterRunRewardReceipt Receipt,
    long CurrentWorkspaceRevision,
    string Proof)
{
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

    internal static Sr5AfterRunRewardReceiptObservation Create(
        Sr5AfterRunRewardCheckpoint checkpoint,
        CharacterAfterRunRewardReceipt receipt,
        long currentRevision)
        => new(receipt, currentRevision, Sign(checkpoint, receipt, currentRevision));

    internal bool Verifies(Sr5AfterRunRewardCheckpoint checkpoint)
    {
        if (!checkpoint.IsExact() || !checkpoint.ReceiptMatches(Receipt)
            || CurrentWorkspaceRevision < Receipt.CommittedWorkspaceRevision
            || !CharacterAfterRunRewardProjector.IsDigest(Proof))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(Proof),
            Convert.FromHexString(Sign(checkpoint, Receipt, CurrentWorkspaceRevision)));
    }

    private static string Sign(Sr5AfterRunRewardCheckpoint checkpoint,
        CharacterAfterRunRewardReceipt receipt, long revision)
        => Convert.ToHexStringLower(HMACSHA256.HashData(ProcessKey,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { checkpoint, receipt, revision }))));
}
