using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Restart-safe phone orchestration around Core's typed SR5 vehicle/drone workshop.
/// Android owns only draft/review UX and durable handoff; Core owns the catalog,
/// price, legality, availability, slot/capacity, commit, recovery, and undo decisions.
/// </summary>
public sealed class Sr5CareerVehicleWorkshopService(
    ICharacterVehicleWorkshopAuthority authority,
    ICharacterSourceDataResolver sourceData,
    ISr5CareerVehicleWorkshopWorkspaceStore workspaces,
    ISr5CareerVehicleWorkshopCheckpointStore checkpoints)
{
    private readonly object _gate = new();

    public static CharacterVehicleWorkshopSelection EmptySelection { get; } = new(
        new CharacterVehicleChassisSourceId(Guid.Empty),
        new CharacterVehicleInstanceId(Guid.Empty),
        string.Empty,
        string.Empty,
        [],
        []);

    public Sr5CareerVehicleWorkshopSnapshot Load(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
            return LoadLocked(workspaceId);
    }

    public Sr5CareerVehicleWorkshopSnapshot UpdateSelection(
        CharacterWorkspaceId workspaceId,
        CharacterVehicleWorkshopSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        lock (_gate)
        {
            Sr5CareerVehicleWorkshopSnapshot live = RequireReady(LoadLocked(workspaceId));
            if (live.Checkpoint?.Phase is not (null or Sr5CareerVehicleWorkshopPhase.Editing))
                throw new InvalidOperationException("Resolve or reopen the durable vehicle workshop before editing.");
            CharacterVehicleWorkshopSelection rebound = RebindSelection(live.Preparation!, selection);
            Sr5CareerVehicleWorkshopCheckpoint editing = EditingCheckpoint(
                workspaceId, live.Preparation!, rebound);
            checkpoints.Write(editing);
            return Snapshot(workspaceId, live.Preparation!, editing,
                Sr5CareerVehicleWorkshopNotices.DraftRestored);
        }
    }

    public Sr5CareerVehicleWorkshopSnapshot Review(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerVehicleWorkshopSnapshot live = RequireReady(LoadLocked(workspaceId));
            if (live.Checkpoint?.Phase is not (null or Sr5CareerVehicleWorkshopPhase.Editing))
                throw new InvalidOperationException("The vehicle workshop is already reviewed or locked.");
            CharacterVehicleWorkshopQuote quote = authority.Quote(live.Preparation!, live.Selection);
            if (!quote.Exact)
                throw new InvalidOperationException(quote.Blockers.FirstOrDefault()
                    ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
            CharacterVehicleWorkshopCommitCommand command = NewCommand(
                live.Preparation!, live.Selection, quote);
            var reviewed = new Sr5CareerVehicleWorkshopCheckpoint(
                Sr5CareerVehicleWorkshopSchemas.CheckpointV1,
                workspaceId,
                live.Preparation!.ContentRevision,
                live.Preparation.CharacterDigest,
                live.Preparation.CatalogDigest,
                live.Selection,
                Sr5CareerVehicleWorkshopPhase.Reviewed,
                command,
                null);
            checkpoints.Write(reviewed);
            return Snapshot(workspaceId, live.Preparation, reviewed,
                Sr5CareerVehicleWorkshopNotices.ReviewReady);
        }
    }

    public Sr5CareerVehicleWorkshopSnapshot Confirm(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerVehicleWorkshopSnapshot live = RequireReady(LoadLocked(workspaceId));
            if (!live.CanConfirm || live.Checkpoint?.Command is not { } command)
                throw new InvalidOperationException("The reviewed vehicle workshop quote is stale.");

            Sr5CareerVehicleWorkshopCheckpoint applying = live.Checkpoint with
            {
                Phase = Sr5CareerVehicleWorkshopPhase.Applying,
                Receipt = null
            };
            checkpoints.Write(applying);
            LiveAuthority current = RequireLiveAuthority(workspaceId);
            CharacterVehicleWorkshopCommitResult committed = authority.Commit(
                current.Stored.Document.Content,
                current.Stored.ContentRevision,
                current.Catalog,
                command);
            if (committed.Status != CharacterVehicleWorkshopCommitStatus.Committed
                || committed.Receipt is null)
            {
                Sr5CareerVehicleWorkshopCheckpoint reviewed = applying with
                {
                    Phase = Sr5CareerVehicleWorkshopPhase.Reviewed
                };
                checkpoints.Write(reviewed);
                return Snapshot(workspaceId, current.Preparation, reviewed,
                    Sr5CareerVehicleWorkshopNotices.CommitNotApplied, committed.BlockReason);
            }

            applying = applying with { Receipt = committed.Receipt };
            checkpoints.Write(applying);
            _ = workspaces.ReplaceAndCheckpoint(current.Stored, committed.CharacterXml);
            Sr5CareerVehicleWorkshopWorkspaceSnapshot? after = workspaces.Read(workspaceId);
            if (after is not null
                && after.ContentRevision == committed.NewContentRevision
                && after.SavedRevision == after.ContentRevision
                && string.Equals(
                    CharacterVehicleWorkshopRules.ComputeCharacterDigest(after.Document.Content),
                    committed.NewCharacterDigest,
                    StringComparison.Ordinal))
            {
                CharacterVehicleWorkshopCommitResult recovered = authority.Recover(
                    after.Document.Content, after.ContentRevision, current.Catalog, command);
                if (recovered.Status == CharacterVehicleWorkshopCommitStatus.Recovered
                    && recovered.Receipt is not null)
                {
                    return AppliedCheckpoint(after, current.Catalog, applying,
                        recovered.Receipt, Sr5CareerVehicleWorkshopNotices.CommitApplied);
                }
            }
            return RecoveryUnknown(workspaceId, after ?? current.Stored, current.Catalog, applying);
        }
    }

    public Sr5CareerVehicleWorkshopSnapshot Undo(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerVehicleWorkshopSnapshot live = LoadLocked(workspaceId);
            if (!live.HasAppliedReceipt || live.Checkpoint?.Receipt is not { } receipt)
                throw new InvalidOperationException("No exact vehicle workshop receipt is available to undo.");
            LiveAuthority current = RequireLiveAuthority(workspaceId);
            CharacterVehicleWorkshopCommitResult undone = authority.Undo(
                current.Stored.Document.Content,
                current.Stored.ContentRevision,
                current.Catalog,
                new CharacterVehicleWorkshopUndoCommand(receipt));
            if (undone.Status != CharacterVehicleWorkshopCommitStatus.Undone
                || undone.NewContentRevision != checked(current.Stored.ContentRevision + 1))
                return RecoveryUnknown(workspaceId, current.Stored, current.Catalog, live.Checkpoint);

            Sr5CareerVehicleWorkshopWorkspaceWriteResult write =
                workspaces.ReplaceAndCheckpoint(current.Stored, undone.CharacterXml);
            Sr5CareerVehicleWorkshopWorkspaceSnapshot? verified = workspaces.Read(workspaceId);
            if (!write.Applied || verified is null
                || verified.ContentRevision != undone.NewContentRevision
                || verified.SavedRevision != verified.ContentRevision
                || !string.Equals(
                    CharacterVehicleWorkshopRules.ComputeCharacterDigest(verified.Document.Content),
                    undone.NewCharacterDigest,
                    StringComparison.Ordinal))
            {
                return RecoveryUnknown(workspaceId, verified ?? current.Stored,
                    current.Catalog, live.Checkpoint);
            }

            LiveAuthority rebound = ResolveAuthority(verified);
            Sr5CareerVehicleWorkshopCheckpoint editing = EditingCheckpoint(
                workspaceId, rebound.Preparation,
                RebindSelection(rebound.Preparation, live.Selection));
            checkpoints.Write(editing);
            return Snapshot(workspaceId, rebound.Preparation, editing,
                Sr5CareerVehicleWorkshopNotices.UndoApplied);
        }
    }

    public Sr5CareerVehicleWorkshopSnapshot Reopen(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerVehicleWorkshopSnapshot live = LoadLocked(workspaceId);
            if (live.IsRecoveryUnknown)
                throw new InvalidOperationException("An unknown vehicle workshop outcome cannot be discarded.");
            if (!live.HasAppliedReceipt)
                throw new InvalidOperationException("Only an applied vehicle workshop receipt can be closed.");
            Sr5CareerVehicleWorkshopSnapshot ready = RequireReady(live);
            Sr5CareerVehicleWorkshopCheckpoint editing = EditingCheckpoint(
                workspaceId, ready.Preparation!, DefaultSelection(ready.Preparation!));
            checkpoints.Write(editing);
            return Snapshot(workspaceId, ready.Preparation!, editing,
                Sr5CareerVehicleWorkshopNotices.Reopened);
        }
    }

    private Sr5CareerVehicleWorkshopSnapshot LoadLocked(CharacterWorkspaceId workspaceId)
    {
        Sr5CareerVehicleWorkshopWorkspaceSnapshot? stored = workspaces.Read(workspaceId);
        if (stored is null)
            return Sr5CareerVehicleWorkshopSnapshot.Blocked(
                workspaceId, CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        if (stored.ContentRevision != stored.SavedRevision)
            return Sr5CareerVehicleWorkshopSnapshot.Blocked(
                workspaceId, "The vehicle workshop requires one clean saved Career revision.");

        LiveAuthority live;
        try
        {
            live = ResolveAuthority(stored);
        }
        catch (InvalidOperationException exception)
        {
            return Sr5CareerVehicleWorkshopSnapshot.Blocked(workspaceId, exception.Message);
        }

        Sr5CareerVehicleWorkshopCheckpoint? checkpoint = checkpoints.Read(workspaceId);
        if (checkpoint?.Phase is Sr5CareerVehicleWorkshopPhase.Applying
                or Sr5CareerVehicleWorkshopPhase.RecoveryUnknown
                or Sr5CareerVehicleWorkshopPhase.Applied)
            return ResolvePending(live, checkpoint);
        if (checkpoint is not null && !checkpoint.BelongsTo(workspaceId))
        {
            checkpoints.Clear(workspaceId);
            checkpoint = null;
        }
        if (!live.Preparation.Exact)
        {
            return new Sr5CareerVehicleWorkshopSnapshot(
                workspaceId, live.Preparation,
                checkpoint?.Selection ?? DefaultSelection(live.Preparation),
                null, checkpoint, string.Empty, live.Preparation.Blockers);
        }
        if (checkpoint is null)
            return Snapshot(workspaceId, live.Preparation, null, string.Empty,
                selection: DefaultSelection(live.Preparation));
        if (!checkpoint.Matches(live.Preparation))
        {
            Sr5CareerVehicleWorkshopCheckpoint rebound = EditingCheckpoint(
                workspaceId, live.Preparation,
                RebindSelection(live.Preparation, checkpoint.Selection));
            checkpoints.Write(rebound);
            return Snapshot(workspaceId, live.Preparation, rebound,
                Sr5CareerVehicleWorkshopNotices.ReviewStale);
        }
        return Snapshot(workspaceId, live.Preparation, checkpoint,
            Sr5CareerVehicleWorkshopNotices.DraftRestored);
    }

    private Sr5CareerVehicleWorkshopSnapshot ResolvePending(
        LiveAuthority live,
        Sr5CareerVehicleWorkshopCheckpoint checkpoint)
    {
        if (checkpoint.Command is { } command)
        {
            CharacterVehicleWorkshopCommitResult recovered = authority.Recover(
                live.Stored.Document.Content,
                live.Stored.ContentRevision,
                live.Catalog,
                command);
            if (recovered.Status == CharacterVehicleWorkshopCommitStatus.Recovered
                && recovered.Receipt is not null)
            {
                return AppliedCheckpoint(live.Stored, live.Catalog, checkpoint,
                    recovered.Receipt, Sr5CareerVehicleWorkshopNotices.CommitRecovered);
            }
            // The exact pre-commit bytes prove that no CAS mutation happened. A reviewed
            // command may be restored without replaying the mutation.
            if (live.Stored.ContentRevision == command.ExpectedContentRevision
                && string.Equals(live.Preparation.CharacterDigest,
                    command.ExpectedCharacterDigest, StringComparison.Ordinal)
                && string.Equals(live.Preparation.CatalogDigest,
                    command.ExpectedCatalogDigest, StringComparison.Ordinal))
            {
                Sr5CareerVehicleWorkshopCheckpoint reviewed = checkpoint with
                {
                    Phase = Sr5CareerVehicleWorkshopPhase.Reviewed,
                    Receipt = null
                };
                checkpoints.Write(reviewed);
                return Snapshot(live.Stored.WorkspaceId, live.Preparation, reviewed,
                    Sr5CareerVehicleWorkshopNotices.CommitNotApplied);
            }
        }
        return RecoveryUnknown(live.Stored.WorkspaceId, live.Stored, live.Catalog, checkpoint);
    }

    private LiveAuthority RequireLiveAuthority(CharacterWorkspaceId workspaceId)
    {
        Sr5CareerVehicleWorkshopWorkspaceSnapshot stored = workspaces.Read(workspaceId)
            ?? throw new InvalidOperationException(CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        if (stored.ContentRevision != stored.SavedRevision)
            throw new InvalidOperationException("The vehicle workshop requires one clean saved Career revision.");
        return ResolveAuthority(stored);
    }

    private LiveAuthority ResolveAuthority(Sr5CareerVehicleWorkshopWorkspaceSnapshot stored)
    {
        ICharacterSourceDataContext context = sourceData.TryCreateContext(stored.Document.Content)
            ?? throw new InvalidOperationException(CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        if (!context.TryResolveVehicleWorkshopCatalog(out CharacterVehicleWorkshopCatalog catalog))
            throw new InvalidOperationException(CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(
            stored.Document.Content, stored.ContentRevision, catalog);
        return new LiveAuthority(stored, catalog, preparation);
    }

    private Sr5CareerVehicleWorkshopSnapshot AppliedCheckpoint(
        Sr5CareerVehicleWorkshopWorkspaceSnapshot stored,
        CharacterVehicleWorkshopCatalog catalog,
        Sr5CareerVehicleWorkshopCheckpoint checkpoint,
        CharacterVehicleWorkshopCommitReceipt receipt,
        string notice)
    {
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(
            stored.Document.Content, stored.ContentRevision, catalog);
        Sr5CareerVehicleWorkshopCheckpoint applied = checkpoint with
        {
            BoundContentRevision = preparation.ContentRevision,
            BoundCharacterDigest = preparation.CharacterDigest,
            BoundCatalogDigest = preparation.CatalogDigest,
            Phase = Sr5CareerVehicleWorkshopPhase.Applied,
            Receipt = receipt
        };
        checkpoints.Write(applied);
        return Snapshot(stored.WorkspaceId, preparation, applied, notice);
    }

    private Sr5CareerVehicleWorkshopSnapshot RecoveryUnknown(
        CharacterWorkspaceId workspaceId,
        Sr5CareerVehicleWorkshopWorkspaceSnapshot stored,
        CharacterVehicleWorkshopCatalog catalog,
        Sr5CareerVehicleWorkshopCheckpoint checkpoint)
    {
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(
            stored.Document.Content, stored.ContentRevision, catalog);
        Sr5CareerVehicleWorkshopCheckpoint unknown = checkpoint with
        {
            Phase = Sr5CareerVehicleWorkshopPhase.RecoveryUnknown
        };
        checkpoints.Write(unknown);
        return Snapshot(workspaceId, preparation, unknown,
            Sr5CareerVehicleWorkshopNotices.RecoveryUnknown);
    }

    private Sr5CareerVehicleWorkshopSnapshot Snapshot(
        CharacterWorkspaceId workspaceId,
        CharacterVehicleWorkshopPreparation preparation,
        Sr5CareerVehicleWorkshopCheckpoint? checkpoint,
        string notice,
        string extraBlocker = "",
        CharacterVehicleWorkshopSelection? selection = null)
    {
        CharacterVehicleWorkshopSelection selected = selection
            ?? checkpoint?.Selection
            ?? DefaultSelection(preparation);
        CharacterVehicleWorkshopQuote quote = authority.Quote(preparation, selected);
        string[] blockers = preparation.Blockers
            .Concat(string.IsNullOrWhiteSpace(extraBlocker) ? [] : [extraBlocker])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new Sr5CareerVehicleWorkshopSnapshot(
            workspaceId, preparation, selected, quote, checkpoint, notice, blockers);
    }

    private static Sr5CareerVehicleWorkshopSnapshot RequireReady(
        Sr5CareerVehicleWorkshopSnapshot snapshot)
    {
        if (!snapshot.IsReady)
            throw new InvalidOperationException(snapshot.Blockers.FirstOrDefault()
                ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        return snapshot;
    }

    private static Sr5CareerVehicleWorkshopCheckpoint EditingCheckpoint(
        CharacterWorkspaceId workspaceId,
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopSelection selection)
        => new(
            Sr5CareerVehicleWorkshopSchemas.CheckpointV1,
            workspaceId,
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            selection,
            Sr5CareerVehicleWorkshopPhase.Editing,
            null,
            null);

    private static CharacterVehicleWorkshopCommitCommand NewCommand(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopSelection selection,
        CharacterVehicleWorkshopQuote quote)
        => new(
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            quote.QuoteDigest,
            $"android-vehicle-workshop:{Guid.NewGuid():N}",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            selection);

    private static CharacterVehicleWorkshopSelection DefaultSelection(
        CharacterVehicleWorkshopPreparation preparation)
    {
        CharacterVehicleWorkshopChassisEntry? chassis = preparation.Chassis.FirstOrDefault(static candidate =>
            candidate.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact);
        return chassis is null
            ? EmptySelection
            : new CharacterVehicleWorkshopSelection(
                chassis.SourceId,
                new CharacterVehicleInstanceId(Guid.NewGuid()),
                string.Empty,
                chassis.Posture == CharacterVehicleChassisPosture.GmApprovedCustom
                    ? chassis.GmAuthorityDigest
                    : string.Empty,
                [],
                []);
    }

    private static CharacterVehicleWorkshopSelection RebindSelection(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopSelection selection)
    {
        CharacterVehicleWorkshopChassisEntry? chassis = preparation.Chassis.SingleOrDefault(candidate =>
            candidate.SourceId == selection.ChassisSourceId
            && candidate.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact);
        chassis ??= preparation.Chassis.FirstOrDefault(static candidate =>
            candidate.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact);
        if (chassis is null)
            return EmptySelection;

        CharacterVehicleWorkshopModificationSelection[] modifications =
            (selection.Modifications ?? [])
            .Where(selected => preparation.Modifications.Any(candidate =>
                candidate.SourceId == selected.SourceId
                && candidate.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                && selected.Rating >= candidate.MinimumRating
                && selected.Rating <= candidate.MaximumRating
                && (candidate.AllowedChassis.Count == 0
                    || candidate.AllowedChassis.Contains(chassis.SourceId))))
            .Select(selected => selected.InstanceId.Value == Guid.Empty
                ? selected with { InstanceId = new CharacterVehicleModificationInstanceId(Guid.NewGuid()) }
                : selected)
            .ToArray();
        return selection with
        {
            ChassisSourceId = chassis.SourceId,
            NewVehicleInstanceId = selection.NewVehicleInstanceId.Value == Guid.Empty
                ? new CharacterVehicleInstanceId(Guid.NewGuid())
                : selection.NewVehicleInstanceId,
            CustomName = (selection.CustomName ?? string.Empty).Trim(),
            GmAuthorityDigest = chassis.Posture == CharacterVehicleChassisPosture.GmApprovedCustom
                ? chassis.GmAuthorityDigest
                : string.Empty,
            Modifications = modifications,
            // Weapon-mount composition remains outside this first phone slice. Core
            // authority is present, but Android never fabricates its required four IDs.
            WeaponMounts = []
        };
    }

    private sealed record LiveAuthority(
        Sr5CareerVehicleWorkshopWorkspaceSnapshot Stored,
        CharacterVehicleWorkshopCatalog Catalog,
        CharacterVehicleWorkshopPreparation Preparation);
}
