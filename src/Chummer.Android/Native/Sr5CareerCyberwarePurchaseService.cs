using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Restart-safe Android orchestration for Core's bounded Career Cyberware
/// purchase authority. Gear purchase, Bioware, descendants, generated rows,
/// prompts, mounts, and modular cyberlimbs deliberately receive no mutation
/// seam here.
/// </summary>
public sealed class Sr5CareerCyberwarePurchaseService(
    ICharacterCyberwarePurchaseAuthority authority,
    ISr5CareerCyberwareWorkspaceStore workspaces,
    ISr5CareerCyberwarePurchaseCheckpointStore checkpoints)
{
    private readonly object _gate = new();

    public static CharacterCyberwarePurchaseSelection EmptySelection { get; } = new(
        new CharacterCyberwareSourceId(Guid.Empty),
        new CharacterCyberwareGradeId(Guid.Empty),
        Rating: 0,
        EssenceDiscountPercent: 0,
        BlackMarketDiscount: false,
        MarkupPercent: 0m,
        FreeCost: false);

    public Sr5CareerCyberwarePurchaseSnapshot Load(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            return LoadLocked(workspaceId);
        }
    }

    public Sr5CareerCyberwarePurchaseSnapshot UpdateSelection(
        CharacterWorkspaceId workspaceId,
        CharacterCyberwarePurchaseSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        lock (_gate)
        {
            Sr5CareerCyberwarePurchaseSnapshot live = RequireReady(LoadLocked(workspaceId));
            if (live.Checkpoint?.Phase is Sr5CareerCyberwarePurchasePhase.Applying
                    or Sr5CareerCyberwarePurchasePhase.Applied
                    or Sr5CareerCyberwarePurchasePhase.RecoveryUnknown)
            {
                throw new InvalidOperationException(
                    "Resolve or explicitly reopen the durable Cyberware purchase before editing.");
            }
            CharacterCyberwarePurchaseSelection rebound = RebindSelection(
                live.Preparation!,
                selection);
            Sr5CareerCyberwarePurchaseCheckpoint editing = EditingCheckpoint(
                workspaceId,
                live.Preparation!,
                rebound);
            checkpoints.Write(editing);
            return Snapshot(
                workspaceId,
                live.Preparation!,
                editing,
                Sr5CareerCyberwarePurchaseNotices.DraftRestored);
        }
    }

    public Sr5CareerCyberwarePurchaseSnapshot Review(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerCyberwarePurchaseSnapshot live = RequireReady(LoadLocked(workspaceId));
            CharacterCyberwarePurchasePreparation preparation = live.Preparation!;
            CharacterCyberwarePurchaseQuote quote = authority.Quote(preparation, live.Selection);
            if (!quote.Exact)
                throw new InvalidOperationException(quote.BlockReason);

            CharacterCyberwareInstanceId instanceId = NewDistinctInstanceId(live.Selection);
            Guid expenseId = NewDistinctExpenseId(live.Selection, instanceId);
            var command = new CharacterCyberwarePurchaseCommand(
                preparation.ContentRevision,
                preparation.CharacterDigest,
                preparation.CatalogDigest,
                quote.QuoteDigest,
                live.Selection,
                instanceId,
                expenseId,
                DateTimeOffset.UtcNow);
            var reviewed = new Sr5CareerCyberwarePurchaseCheckpoint(
                Sr5CareerCyberwarePurchaseSchemas.CheckpointV1,
                workspaceId,
                preparation.ContentRevision,
                preparation.CharacterDigest,
                preparation.CatalogDigest,
                live.Selection,
                Sr5CareerCyberwarePurchasePhase.Reviewed,
                command,
                Receipt: null);
            checkpoints.Write(reviewed);
            return Snapshot(
                workspaceId,
                preparation,
                reviewed,
                Sr5CareerCyberwarePurchaseNotices.ReviewReady);
        }
    }

    public Sr5CareerCyberwarePurchaseSnapshot Confirm(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerCyberwarePurchaseSnapshot live = RequireReady(LoadLocked(workspaceId));
            if (!live.CanConfirm || live.Checkpoint?.Command is not { } command)
                throw new InvalidOperationException("The reviewed Cyberware purchase is stale.");

            Sr5CareerCyberwarePurchaseCheckpoint applying = live.Checkpoint with
            {
                Phase = Sr5CareerCyberwarePurchasePhase.Applying,
                Receipt = null
            };
            checkpoints.Write(applying);
            Sr5CareerCyberwareWorkspaceSnapshot current = RequireCleanWorkspace(workspaceId);
            CharacterCyberwarePurchaseCommitResult committed = authority.Commit(
                current.Document.Content,
                current.ContentRevision,
                command);
            if (!committed.Committed || committed.UndoReceipt is null)
            {
                Sr5CareerCyberwarePurchaseCheckpoint reviewed = applying with
                {
                    Phase = Sr5CareerCyberwarePurchasePhase.Reviewed
                };
                checkpoints.Write(reviewed);
                CharacterCyberwarePurchasePreparation latest = authority.Prepare(
                    current.Document.Content,
                    current.ContentRevision);
                return Snapshot(
                    workspaceId,
                    latest,
                    reviewed,
                    Sr5CareerCyberwarePurchaseNotices.CommitNotApplied,
                    committed.BlockReason);
            }

            // Persist the Core receipt before crossing the external CAS boundary.
            // This makes a post-write process death recoverable without replay.
            applying = applying with { Receipt = committed.UndoReceipt };
            checkpoints.Write(applying);
            _ = workspaces.ReplaceAndCheckpoint(current, committed.CharacterXml);
            Sr5CareerCyberwareWorkspaceSnapshot? after = workspaces.Read(workspaceId);
            if (after is not null && ReceiptProvesApplied(after, committed.UndoReceipt))
            {
                return AppliedCheckpoint(
                    after,
                    applying,
                    committed.UndoReceipt,
                    Sr5CareerCyberwarePurchaseNotices.CommitApplied);
            }

            Sr5CareerCyberwarePurchaseCheckpoint unknown = applying with
            {
                Phase = Sr5CareerCyberwarePurchasePhase.RecoveryUnknown
            };
            checkpoints.Write(unknown);
            CharacterCyberwarePurchasePreparation unresolvedPreparation = after is null
                ? authority.Prepare(current.Document.Content, current.ContentRevision)
                : authority.Prepare(after.Document.Content, after.ContentRevision);
            return Snapshot(
                workspaceId,
                unresolvedPreparation,
                unknown,
                Sr5CareerCyberwarePurchaseNotices.RecoveryUnknown);
        }
    }

    public Sr5CareerCyberwarePurchaseSnapshot Undo(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerCyberwarePurchaseSnapshot live = LoadLocked(workspaceId);
            if (!live.HasAppliedReceipt || live.Checkpoint?.Receipt is not { } receipt)
                throw new InvalidOperationException("No exact Cyberware purchase receipt is available to undo.");
            Sr5CareerCyberwareWorkspaceSnapshot current = RequireCleanWorkspace(workspaceId);
            CharacterCyberwarePurchaseCommitResult undone = authority.Undo(
                current.Document.Content,
                current.ContentRevision,
                new CharacterCyberwarePurchaseUndoCommand(receipt));
            if (!undone.Committed
                || undone.NewContentRevision != checked(current.ContentRevision + 1))
            {
                return RecoveryUnknown(live, undone.BlockReason);
            }

            Sr5CareerCyberwareWorkspaceWriteResult write =
                workspaces.ReplaceAndCheckpoint(current, undone.CharacterXml);
            Sr5CareerCyberwareWorkspaceSnapshot? verified = workspaces.Read(workspaceId);
            bool exact = write.Applied
                && verified is not null
                && verified.ContentRevision == undone.NewContentRevision
                && verified.SavedRevision == verified.ContentRevision
                && string.Equals(
                    CharacterCyberwarePurchaseRules.ComputeCharacterDigest(verified.Document.Content),
                    undone.NewCharacterDigest,
                    StringComparison.Ordinal);
            if (!exact)
                return RecoveryUnknown(live, write.Error);

            CharacterCyberwarePurchasePreparation rebound = authority.Prepare(
                verified!.Document.Content,
                verified.ContentRevision);
            Sr5CareerCyberwarePurchaseCheckpoint editing = EditingCheckpoint(
                workspaceId,
                rebound,
                RebindSelection(rebound, live.Selection));
            checkpoints.Write(editing);
            return Snapshot(
                workspaceId,
                rebound,
                editing,
                Sr5CareerCyberwarePurchaseNotices.UndoApplied);
        }
    }

    public Sr5CareerCyberwarePurchaseSnapshot Reopen(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerCyberwarePurchaseSnapshot live = LoadLocked(workspaceId);
            if (live.IsRecoveryUnknown)
                throw new InvalidOperationException(
                    "An unknown Cyberware purchase outcome cannot be discarded or replayed on this phone.");
            Sr5CareerCyberwarePurchaseSnapshot ready = RequireReady(live);
            Sr5CareerCyberwarePurchaseCheckpoint editing = EditingCheckpoint(
                workspaceId,
                ready.Preparation!,
                RebindSelection(ready.Preparation!, ready.Selection));
            checkpoints.Write(editing);
            return Snapshot(
                workspaceId,
                ready.Preparation!,
                editing,
                Sr5CareerCyberwarePurchaseNotices.Reopened);
        }
    }

    private Sr5CareerCyberwarePurchaseSnapshot LoadLocked(CharacterWorkspaceId workspaceId)
    {
        Sr5CareerCyberwareWorkspaceSnapshot? stored = workspaces.Read(workspaceId);
        if (stored is null)
            return Sr5CareerCyberwarePurchaseSnapshot.Blocked(
                workspaceId,
                CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
        if (stored.ContentRevision != stored.SavedRevision)
            return Sr5CareerCyberwarePurchaseSnapshot.Blocked(
                workspaceId,
                "The Cyberware purchase lane requires one clean saved Career revision.");

        CharacterCyberwarePurchasePreparation preparation = authority.Prepare(
            stored.Document.Content,
            stored.ContentRevision);
        Sr5CareerCyberwarePurchaseCheckpoint? checkpoint = checkpoints.Read(workspaceId);
        if (checkpoint?.Phase is Sr5CareerCyberwarePurchasePhase.Applying
                or Sr5CareerCyberwarePurchasePhase.Applied
                or Sr5CareerCyberwarePurchasePhase.RecoveryUnknown)
        {
            return ResolveLocked(stored, preparation, checkpoint);
        }
        if (!preparation.Exact)
        {
            return new Sr5CareerCyberwarePurchaseSnapshot(
                workspaceId,
                preparation,
                checkpoint?.Selection ?? DefaultSelection(preparation),
                null,
                checkpoint,
                string.Empty,
                preparation.Blockers);
        }
        if (checkpoint is null)
        {
            return Snapshot(
                workspaceId,
                preparation,
                checkpoint: null,
                notice: string.Empty,
                selection: DefaultSelection(preparation));
        }
        if (!checkpoint.BelongsTo(workspaceId))
        {
            checkpoints.Clear(workspaceId);
            return Snapshot(
                workspaceId,
                preparation,
                checkpoint: null,
                notice: Sr5CareerCyberwarePurchaseNotices.ReviewStale,
                selection: DefaultSelection(preparation));
        }
        if (!checkpoint.Matches(preparation))
        {
            Sr5CareerCyberwarePurchaseCheckpoint rebound = EditingCheckpoint(
                workspaceId,
                preparation,
                RebindSelection(preparation, checkpoint.Selection));
            checkpoints.Write(rebound);
            return Snapshot(
                workspaceId,
                preparation,
                rebound,
                Sr5CareerCyberwarePurchaseNotices.ReviewStale);
        }
        return Snapshot(
            workspaceId,
            preparation,
            checkpoint,
            Sr5CareerCyberwarePurchaseNotices.DraftRestored);
    }

    private Sr5CareerCyberwarePurchaseSnapshot ResolveLocked(
        Sr5CareerCyberwareWorkspaceSnapshot stored,
        CharacterCyberwarePurchasePreparation preparation,
        Sr5CareerCyberwarePurchaseCheckpoint checkpoint)
    {
        if (checkpoint.Receipt is { } receipt && ReceiptProvesApplied(stored, receipt))
        {
            return AppliedCheckpoint(
                stored,
                checkpoint,
                receipt,
                checkpoint.Phase == Sr5CareerCyberwarePurchasePhase.Applied
                    ? Sr5CareerCyberwarePurchaseNotices.DraftRestored
                    : Sr5CareerCyberwarePurchaseNotices.CommitRecovered);
        }

        CharacterCyberwarePurchaseCommand? command = checkpoint.Command;
        if (command is not null
            && preparation.Exact
            && stored.ContentRevision == command.ExpectedContentRevision
            && string.Equals(preparation.CharacterDigest, command.ExpectedCharacterDigest, StringComparison.Ordinal)
            && string.Equals(preparation.CatalogDigest, command.ExpectedCatalogDigest, StringComparison.Ordinal))
        {
            CharacterCyberwarePurchaseQuote quote = authority.Quote(preparation, command.Selection);
            if (quote.Exact
                && string.Equals(quote.QuoteDigest, command.ExpectedQuoteDigest, StringComparison.Ordinal))
            {
                Sr5CareerCyberwarePurchaseCheckpoint reviewed = checkpoint with
                {
                    BoundContentRevision = preparation.ContentRevision,
                    BoundCharacterDigest = preparation.CharacterDigest,
                    BoundCatalogDigest = preparation.CatalogDigest,
                    Selection = command.Selection,
                    Phase = Sr5CareerCyberwarePurchasePhase.Reviewed,
                    Receipt = null
                };
                checkpoints.Write(reviewed);
                return Snapshot(
                    checkpoint.WorkspaceId,
                    preparation,
                    reviewed,
                    Sr5CareerCyberwarePurchaseNotices.CommitNotApplied);
            }
        }

        Sr5CareerCyberwarePurchaseCheckpoint unknown = checkpoint with
        {
            Phase = Sr5CareerCyberwarePurchasePhase.RecoveryUnknown
        };
        checkpoints.Write(unknown);
        return Snapshot(
            checkpoint.WorkspaceId,
            preparation,
            unknown,
            Sr5CareerCyberwarePurchaseNotices.RecoveryUnknown,
            preparation.Exact ? null : preparation.Blockers.FirstOrDefault());
    }

    private bool ReceiptProvesApplied(
        Sr5CareerCyberwareWorkspaceSnapshot stored,
        CharacterCyberwarePurchaseUndoReceipt receipt)
    {
        if (receipt.ContentRevision != stored.ContentRevision
            || !string.Equals(
                receipt.CharacterDigest,
                CharacterCyberwarePurchaseRules.ComputeCharacterDigest(stored.Document.Content),
                StringComparison.Ordinal)
            || !string.Equals(
                receipt.ReceiptDigest,
                CharacterCyberwarePurchaseRules.ComputeUndoReceiptDigest(receipt),
                StringComparison.Ordinal))
        {
            return false;
        }

        // Core's Undo is a pure XML projection. Running it without persisting
        // the returned XML proves the exact instance/expense/source binding.
        CharacterCyberwarePurchaseCommitResult lookup = authority.Undo(
            stored.Document.Content,
            stored.ContentRevision,
            new CharacterCyberwarePurchaseUndoCommand(receipt));
        return lookup.Committed
               && lookup.PreviousContentRevision == stored.ContentRevision
               && lookup.NewContentRevision == checked(stored.ContentRevision + 1);
    }

    private Sr5CareerCyberwarePurchaseSnapshot AppliedCheckpoint(
        Sr5CareerCyberwareWorkspaceSnapshot stored,
        Sr5CareerCyberwarePurchaseCheckpoint checkpoint,
        CharacterCyberwarePurchaseUndoReceipt receipt,
        string notice)
    {
        CharacterCyberwarePurchasePreparation preparation = authority.Prepare(
            stored.Document.Content,
            stored.ContentRevision);
        Sr5CareerCyberwarePurchaseCheckpoint applied = checkpoint with
        {
            BoundContentRevision = preparation.ContentRevision,
            BoundCharacterDigest = preparation.CharacterDigest,
            BoundCatalogDigest = preparation.CatalogDigest,
            Phase = Sr5CareerCyberwarePurchasePhase.Applied,
            Receipt = receipt
        };
        checkpoints.Write(applied);
        return Snapshot(checkpoint.WorkspaceId, preparation, applied, notice);
    }

    private Sr5CareerCyberwarePurchaseSnapshot RecoveryUnknown(
        Sr5CareerCyberwarePurchaseSnapshot live,
        string? blocker)
    {
        Sr5CareerCyberwarePurchaseCheckpoint unknown = live.Checkpoint! with
        {
            Phase = Sr5CareerCyberwarePurchasePhase.RecoveryUnknown
        };
        checkpoints.Write(unknown);
        return Snapshot(
            live.WorkspaceId,
            live.Preparation!,
            unknown,
            Sr5CareerCyberwarePurchaseNotices.RecoveryUnknown,
            blocker);
    }

    private Sr5CareerCyberwareWorkspaceSnapshot RequireCleanWorkspace(
        CharacterWorkspaceId workspaceId)
    {
        Sr5CareerCyberwareWorkspaceSnapshot current = workspaces.Read(workspaceId)
            ?? throw new InvalidOperationException("The Cyberware purchase workspace is unavailable.");
        if (current.ContentRevision != current.SavedRevision)
            throw new InvalidOperationException("The Cyberware purchase workspace is not durably saved.");
        return current;
    }

    private static Sr5CareerCyberwarePurchaseSnapshot RequireReady(
        Sr5CareerCyberwarePurchaseSnapshot snapshot)
    {
        if (!snapshot.IsReady)
            throw new InvalidOperationException(
                snapshot.Blockers.FirstOrDefault()
                ?? CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
        return snapshot;
    }

    private Sr5CareerCyberwarePurchaseSnapshot Snapshot(
        CharacterWorkspaceId workspaceId,
        CharacterCyberwarePurchasePreparation preparation,
        Sr5CareerCyberwarePurchaseCheckpoint? checkpoint,
        string notice,
        string? blocker = null,
        CharacterCyberwarePurchaseSelection? selection = null)
    {
        CharacterCyberwarePurchaseSelection selected = RebindSelection(
            preparation,
            selection ?? checkpoint?.Selection ?? DefaultSelection(preparation));
        CharacterCyberwarePurchaseQuote quote = authority.Quote(preparation, selected);
        string[] blockers = string.IsNullOrWhiteSpace(blocker) ? [] : [blocker];
        return new Sr5CareerCyberwarePurchaseSnapshot(
            workspaceId,
            preparation,
            selected,
            quote,
            checkpoint,
            notice,
            blockers);
    }

    private static Sr5CareerCyberwarePurchaseCheckpoint EditingCheckpoint(
        CharacterWorkspaceId workspaceId,
        CharacterCyberwarePurchasePreparation preparation,
        CharacterCyberwarePurchaseSelection selection)
        => new(
            Sr5CareerCyberwarePurchaseSchemas.CheckpointV1,
            workspaceId,
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            RebindSelection(preparation, selection),
            Sr5CareerCyberwarePurchasePhase.Editing,
            Command: null,
            Receipt: null);

    private static CharacterCyberwarePurchaseSelection DefaultSelection(
        CharacterCyberwarePurchasePreparation preparation)
    {
        CharacterCyberwarePurchaseCatalogEntry? source = preparation.Entries.FirstOrDefault();
        CharacterCyberwarePurchaseGrade? grade = source?.Grades.FirstOrDefault();
        return source is null || grade is null
            ? EmptySelection
            : EmptySelection with { SourceId = source.SourceId, GradeId = grade.Id };
    }

    private static CharacterCyberwarePurchaseSelection RebindSelection(
        CharacterCyberwarePurchasePreparation preparation,
        CharacterCyberwarePurchaseSelection selection)
    {
        CharacterCyberwarePurchaseCatalogEntry? source = preparation.Entries.SingleOrDefault(candidate =>
            candidate.SourceId == selection.SourceId);
        if (source is null)
            return DefaultSelection(preparation);
        CharacterCyberwarePurchaseGrade? grade = source.Grades.SingleOrDefault(candidate =>
            candidate.Id == selection.GradeId) ?? source.Grades.FirstOrDefault();
        if (grade is null)
            return DefaultSelection(preparation);
        return selection with
        {
            SourceId = source.SourceId,
            GradeId = grade.Id,
            Rating = 0,
            EssenceDiscountPercent = preparation.Settings.AllowEssenceDiscounts
                ? selection.EssenceDiscountPercent
                : 0,
            BlackMarketDiscount = source.BlackMarketEligible && selection.BlackMarketDiscount
        };
    }

    private static CharacterCyberwareInstanceId NewDistinctInstanceId(
        CharacterCyberwarePurchaseSelection selection)
    {
        Guid value;
        do value = Guid.NewGuid();
        while (value == selection.SourceId.Value || value == selection.GradeId.Value);
        return new CharacterCyberwareInstanceId(value);
    }

    private static Guid NewDistinctExpenseId(
        CharacterCyberwarePurchaseSelection selection,
        CharacterCyberwareInstanceId instanceId)
    {
        Guid value;
        do value = Guid.NewGuid();
        while (value == selection.SourceId.Value
               || value == selection.GradeId.Value
               || value == instanceId.Value);
        return value;
    }
}
