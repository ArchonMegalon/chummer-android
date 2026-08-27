using Chummer.Application.Characters;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;

namespace Chummer.Android.Native;

public sealed record Sr5VehicleWorkshopPhoneSnapshot(
    WorkspaceDocumentSnapshot Workspace,
    CharacterVehicleWorkshopCatalog Catalog,
    CharacterVehicleWorkshopPreparation Preparation);

public sealed record Sr5VehicleWorkshopPhoneLoadResult(
    Sr5VehicleWorkshopPhoneSnapshot? Snapshot,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Snapshot is not null && Blockers.Count == 0;
}

public enum Sr5VehicleWorkshopPhoneMutationStatus
{
    Completed,
    Blocked,
    OutcomeUnknown
}

public sealed record Sr5VehicleWorkshopPhoneMutationResult(
    Sr5VehicleWorkshopPhoneMutationStatus Status,
    CharacterVehicleWorkshopCommitResult? CoreResult,
    string BlockReason)
{
    public CharacterVehicleWorkshopCommitReceipt? Receipt => CoreResult?.Receipt;
}

/// <summary>
/// Native transport/orchestration adapter. Core remains the sole catalog, quote, XML mutation,
/// receipt, recovery, and undo authority; Android only performs revision-aware document CAS.
/// </summary>
public sealed class RunnerSessionSr5VehicleWorkshopAuthority
{
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly IChummerClient _client;
    private readonly ICharacterSourceDataResolver _sourceData;
    private readonly ICharacterVehicleWorkshopAuthority _workshop;

    public RunnerSessionSr5VehicleWorkshopAuthority(
        RunnerSessionCoordinator coordinator,
        IChummerClient client,
        ICharacterSourceDataResolver sourceData,
        ICharacterVehicleWorkshopAuthority workshop)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sourceData = sourceData ?? throw new ArgumentNullException(nameof(sourceData));
        _workshop = workshop ?? throw new ArgumentNullException(nameof(workshop));
    }

    public async Task<Sr5VehicleWorkshopPhoneLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (_coordinator.State.WorkspaceId is not { } workspaceId
            || _coordinator.State.Profile?.Created != true
            || !string.Equals(
                _coordinator.State.Rules?.GameEdition,
                "SR5",
                StringComparison.OrdinalIgnoreCase))
        {
            return Blocked(CharacterVehicleWorkshopBlockers.NotCareer);
        }

        Sr5VehicleWorkshopPhoneLoadResult load = await LoadWorkspaceAsync(
            workspaceId,
            cancellationToken).ConfigureAwait(false);
        if (load.Snapshot is { } snapshot
            && (snapshot.Workspace.ContentRevision != _coordinator.State.ContentRevision
                || snapshot.Workspace.SavedRevision != _coordinator.State.SavedRevision))
        {
            return Blocked(CharacterVehicleWorkshopBlockers.StaleRevision);
        }
        return load;
    }

    public CharacterVehicleWorkshopQuote Quote(
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        Sr5VehicleWorkshopDraft draft)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(draft);
        if (!draft.Matches(snapshot.Workspace.Id.Value, snapshot.Preparation)
            || !draft.TryCreateSelection(out CharacterVehicleWorkshopSelection selection))
        {
            return BlockedQuote(draft);
        }
        return _workshop.Quote(snapshot.Preparation, selection);
    }

    public CharacterVehicleWorkshopCommitResult PrepareCommit(
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        CharacterVehicleWorkshopCommitCommand command)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command);
        return _workshop.Commit(
            snapshot.Workspace.Document.Content,
            snapshot.Workspace.ContentRevision,
            snapshot.Catalog,
            command);
    }

    public CharacterVehicleWorkshopCommitResult PrepareUndo(
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        CharacterVehicleWorkshopCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(receipt);
        return _workshop.Undo(
            snapshot.Workspace.Document.Content,
            snapshot.Workspace.ContentRevision,
            snapshot.Catalog,
            new CharacterVehicleWorkshopUndoCommand(receipt));
    }

    public async Task<Sr5VehicleWorkshopPhoneMutationResult> PersistPreparedAsync(
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        CharacterVehicleWorkshopCommitResult prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(prepared);
        if (prepared.Status is not (CharacterVehicleWorkshopCommitStatus.Committed
            or CharacterVehicleWorkshopCommitStatus.Undone)
            || prepared.PreviousContentRevision != snapshot.Workspace.ContentRevision
            || !CharacterVehicleWorkshopRules.FixedEquals(
                prepared.PreviousCharacterDigest,
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(
                    snapshot.Workspace.Document.Content))
            || prepared.NewContentRevision != prepared.PreviousContentRevision + 1
            || !CharacterVehicleWorkshopRules.FixedEquals(
                prepared.NewCharacterDigest,
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(prepared.CharacterXml)))
        {
            return MutationBlocked(prepared.BlockReason.Length == 0
                ? CharacterVehicleWorkshopBlockers.StaleQuote
                : prepared.BlockReason, prepared);
        }

        Sr5VehicleWorkshopPhoneLoadResult currentLoad = await LoadWorkspaceAsync(
            snapshot.Workspace.Id,
            cancellationToken).ConfigureAwait(false);
        if (currentLoad.Snapshot is not { } current)
        {
            return MutationBlocked(currentLoad.Blockers.FirstOrDefault()
                                   ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable,
                prepared);
        }
        if (!SnapshotsMatch(snapshot.Workspace, current.Workspace))
            return MutationBlocked(CharacterVehicleWorkshopBlockers.StaleRevision, prepared);
        if (!CharacterVehicleWorkshopRules.FixedEquals(
                snapshot.Preparation.CatalogDigest,
                current.Preparation.CatalogDigest))
        {
            return MutationBlocked(CharacterVehicleWorkshopBlockers.StaleCatalog, prepared);
        }

        var replacement = new WorkspaceDocument(
            current.Workspace.Document.State with { Payload = prepared.CharacterXml },
            current.Workspace.Document.Format);
        try
        {
            CommandResult<WorkspaceRevisionReceipt> persisted =
                await _client.ReplaceWorkspaceDocumentAsync(
                    current.Workspace.Id,
                    current.Workspace.ContentRevision,
                    replacement,
                    cancellationToken).ConfigureAwait(false);
            if (!persisted.Success
                || persisted.Value is null
                || persisted.Value.ContentRevision != prepared.NewContentRevision)
            {
                return MutationBlocked(
                    persisted.Error ?? CharacterVehicleWorkshopBlockers.StaleRevision,
                    prepared);
            }

            await _coordinator.ReloadCurrentWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            return new Sr5VehicleWorkshopPhoneMutationResult(
                Sr5VehicleWorkshopPhoneMutationStatus.Completed,
                prepared,
                string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new Sr5VehicleWorkshopPhoneMutationResult(
                Sr5VehicleWorkshopPhoneMutationStatus.OutcomeUnknown,
                prepared,
                "The workshop persistence outcome is unknown. Use Recover outcome before retrying.");
        }
    }

    public async Task<Sr5VehicleWorkshopPhoneMutationResult> RecoverCommitAsync(
        Sr5VehicleWorkshopCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.IsValid(out string checkpointReason)
            || checkpoint.Command is null)
        {
            return MutationBlocked(checkpointReason);
        }

        Sr5VehicleWorkshopPhoneLoadResult load = await LoadWorkspaceByIdAsync(
            checkpoint.Draft.Binding.WorkspaceId,
            cancellationToken).ConfigureAwait(false);
        if (load.Snapshot is not { } current)
            return MutationBlocked(load.Blockers.FirstOrDefault()
                                   ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);

        CharacterVehicleWorkshopCommitResult recovered = _workshop.Recover(
            current.Workspace.Document.Content,
            current.Workspace.ContentRevision,
            current.Catalog,
            checkpoint.Command);
        if (recovered.Status == CharacterVehicleWorkshopCommitStatus.Recovered
            && recovered.Receipt is not null)
        {
            await _coordinator.ReloadCurrentWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            return new Sr5VehicleWorkshopPhoneMutationResult(
                Sr5VehicleWorkshopPhoneMutationStatus.Completed,
                recovered,
                string.Empty);
        }

        string currentDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(
            current.Workspace.Document.Content);
        bool stillAtInput = current.Workspace.ContentRevision
                                == checkpoint.Command.ExpectedContentRevision
                            && CharacterVehicleWorkshopRules.FixedEquals(
                                currentDigest,
                                checkpoint.Command.ExpectedCharacterDigest);
        if (!stillAtInput)
        {
            return MutationBlocked(recovered.BlockReason.Length == 0
                ? CharacterVehicleWorkshopBlockers.StaleRevision
                : recovered.BlockReason, recovered);
        }

        CharacterVehicleWorkshopCommitResult retried = _workshop.Commit(
            current.Workspace.Document.Content,
            current.Workspace.ContentRevision,
            current.Catalog,
            checkpoint.Command);
        if (retried.Status != CharacterVehicleWorkshopCommitStatus.Committed
            || retried.NewContentRevision != checkpoint.ExpectedOutputRevision
            || !CharacterVehicleWorkshopRules.FixedEquals(
                retried.NewCharacterDigest,
                checkpoint.ExpectedOutputDigest))
        {
            return MutationBlocked(retried.BlockReason.Length == 0
                ? CharacterVehicleWorkshopBlockers.StaleQuote
                : retried.BlockReason, retried);
        }
        return await PersistPreparedAsync(current, retried, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Sr5VehicleWorkshopPhoneMutationResult> RecoverUndoAsync(
        Sr5VehicleWorkshopCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.IsValid(out string checkpointReason)
            || checkpoint.Receipt is null)
        {
            return MutationBlocked(checkpointReason);
        }

        Sr5VehicleWorkshopPhoneLoadResult load = await LoadWorkspaceByIdAsync(
            checkpoint.Draft.Binding.WorkspaceId,
            cancellationToken).ConfigureAwait(false);
        if (load.Snapshot is not { } current)
            return MutationBlocked(load.Blockers.FirstOrDefault()
                                   ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);

        string currentDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(
            current.Workspace.Document.Content);
        if (current.Workspace.ContentRevision == checkpoint.ExpectedOutputRevision
            && CharacterVehicleWorkshopRules.FixedEquals(
                currentDigest,
                checkpoint.ExpectedOutputDigest))
        {
            await _coordinator.ReloadCurrentWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            var recoveredUndo = new CharacterVehicleWorkshopCommitResult(
                CharacterVehicleWorkshopCommitStatus.Undone,
                string.Empty,
                checkpoint.Receipt.ContentRevision,
                current.Workspace.ContentRevision,
                checkpoint.Receipt.CharacterDigest,
                currentDigest,
                current.Workspace.Document.Content,
                checkpoint.Receipt.VehicleInstanceId,
                checkpoint.Receipt.ExpenseId,
                -checkpoint.Receipt.NuyenDelta,
                Receipt: null);
            return new Sr5VehicleWorkshopPhoneMutationResult(
                Sr5VehicleWorkshopPhoneMutationStatus.Completed,
                recoveredUndo,
                string.Empty);
        }

        bool stillAtReceipt = current.Workspace.ContentRevision == checkpoint.Receipt.ContentRevision
                              && CharacterVehicleWorkshopRules.FixedEquals(
                                  currentDigest,
                                  checkpoint.Receipt.CharacterDigest);
        if (!stillAtReceipt)
            return MutationBlocked(CharacterVehicleWorkshopBlockers.StaleReceipt);

        CharacterVehicleWorkshopCommitResult retried = PrepareUndo(current, checkpoint.Receipt);
        if (retried.Status != CharacterVehicleWorkshopCommitStatus.Undone
            || retried.NewContentRevision != checkpoint.ExpectedOutputRevision
            || !CharacterVehicleWorkshopRules.FixedEquals(
                retried.NewCharacterDigest,
                checkpoint.ExpectedOutputDigest))
        {
            return MutationBlocked(retried.BlockReason.Length == 0
                ? CharacterVehicleWorkshopBlockers.StaleReceipt
                : retried.BlockReason, retried);
        }
        return await PersistPreparedAsync(current, retried, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Sr5VehicleWorkshopPhoneMutationResult> ReopenReceiptAsync(
        Sr5VehicleWorkshopCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.IsValid(out string checkpointReason)
            || checkpoint.Command is null)
        {
            return MutationBlocked(checkpointReason);
        }
        Sr5VehicleWorkshopPhoneLoadResult load = await LoadWorkspaceByIdAsync(
            checkpoint.Draft.Binding.WorkspaceId,
            cancellationToken).ConfigureAwait(false);
        if (load.Snapshot is not { } current)
            return MutationBlocked(load.Blockers.FirstOrDefault()
                                   ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        CharacterVehicleWorkshopCommitResult recovered = _workshop.Recover(
            current.Workspace.Document.Content,
            current.Workspace.ContentRevision,
            current.Catalog,
            checkpoint.Command);
        return recovered.Status == CharacterVehicleWorkshopCommitStatus.Recovered
               && recovered.Receipt is not null
            ? new Sr5VehicleWorkshopPhoneMutationResult(
                Sr5VehicleWorkshopPhoneMutationStatus.Completed,
                recovered,
                string.Empty)
            : MutationBlocked(recovered.BlockReason.Length == 0
                ? CharacterVehicleWorkshopBlockers.StaleReceipt
                : recovered.BlockReason, recovered);
    }

    private async Task<Sr5VehicleWorkshopPhoneLoadResult> LoadWorkspaceByIdAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        if (_coordinator.State.WorkspaceId is not { } active
            || !string.Equals(active.Value, workspaceId, StringComparison.Ordinal))
        {
            return Blocked(CharacterVehicleWorkshopBlockers.StaleCharacter);
        }
        return await LoadWorkspaceAsync(active, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Sr5VehicleWorkshopPhoneLoadResult> LoadWorkspaceAsync(
        CharacterWorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        try
        {
            WorkspaceDocumentSnapshot first = RequireSnapshot(
                await _client.GetWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false));
            if (first.Document.Format != WorkspaceDocumentFormat.NativeXml
                || !string.Equals(first.Document.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase))
            {
                return Blocked(CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
            }

            ICharacterSourceDataContext? context = _sourceData.TryCreateContext(
                first.Document.Content);
            if (context is null
                || !context.TryResolveVehicleWorkshopCatalog(
                    out CharacterVehicleWorkshopCatalog? catalog))
            {
                return Blocked(CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
            }
            CharacterVehicleWorkshopPreparation preparation = _workshop.Prepare(
                first.Document.Content,
                first.ContentRevision,
                catalog);

            WorkspaceDocumentSnapshot verified = RequireSnapshot(
                await _client.GetWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false));
            if (!SnapshotsMatch(first, verified))
                return Blocked(CharacterVehicleWorkshopBlockers.StaleRevision);

            return new Sr5VehicleWorkshopPhoneLoadResult(
                new Sr5VehicleWorkshopPhoneSnapshot(verified, catalog, preparation),
                preparation.Exact ? [] : preparation.Blockers);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Blocked(CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        }
    }

    private static WorkspaceDocumentSnapshot RequireSnapshot(
        CommandResult<WorkspaceDocumentSnapshot> result)
        => result.Success && result.Value is not null
            ? result.Value
            : throw new InvalidOperationException(
                result.Error ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);

    private static bool SnapshotsMatch(
        WorkspaceDocumentSnapshot left,
        WorkspaceDocumentSnapshot right)
        => left.Id == right.Id
           && left.ContentRevision == right.ContentRevision
           && left.SavedRevision == right.SavedRevision
           && left.Document.Format == right.Document.Format
           && left.Document.State == right.Document.State;

    private static Sr5VehicleWorkshopPhoneLoadResult Blocked(string blocker)
        => new(null, [blocker]);

    private static Sr5VehicleWorkshopPhoneMutationResult MutationBlocked(
        string blocker,
        CharacterVehicleWorkshopCommitResult? coreResult = null)
        => new(Sr5VehicleWorkshopPhoneMutationStatus.Blocked, coreResult, blocker);

    private static CharacterVehicleWorkshopQuote BlockedQuote(
        Sr5VehicleWorkshopDraft draft)
        => new(
            Exact: false,
            Blockers: [CharacterVehicleWorkshopBlockers.StaleQuote],
            draft.ChassisSourceId ?? default,
            draft.NewVehicleInstanceId,
            draft.CustomName,
            CharacterVehicleChassisKind.Vehicle,
            CharacterVehicleChassisPosture.Stock,
            0m,
            0m,
            0,
            0,
            0,
            0,
            new CharacterVehicleWorkshopAvailability(
                0,
                CharacterVehicleWorkshopLegality.Legal,
                false),
            [],
            string.Empty);
}
