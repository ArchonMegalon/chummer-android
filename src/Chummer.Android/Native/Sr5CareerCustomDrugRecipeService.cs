using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Restart-safe Android orchestration for Core's Career-only custom-drug recipe
/// authority. It defines one recipe and the rule-owned free initial dose; later
/// quantity purchases and Creation finalization deliberately have no seam here.
/// </summary>
public sealed class Sr5CareerCustomDrugRecipeService(
    ICharacterCustomDrugAuthority authority,
    ISr5CareerCustomDrugWorkspaceStore workspaces,
    ISr5CareerCustomDrugRecipeCheckpointStore checkpoints)
{
    private readonly object _gate = new();

    public static CharacterCustomDrugSelection EmptySelection { get; } = new(
        Name: string.Empty,
        new CharacterCustomDrugGradeId(Guid.Empty),
        Quantity: 1m,
        Stolen: false,
        FreeCost: false,
        MarkupPercent: 0m,
        Components: []);

    public Sr5CareerCustomDrugRecipeSnapshot Load(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            return LoadLocked(workspaceId);
        }
    }

    public Sr5CareerCustomDrugRecipeSnapshot UpdateSelection(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        lock (_gate)
        {
            Sr5CareerCustomDrugRecipeSnapshot live = RequireReady(LoadLocked(workspaceId));
            if (live.Checkpoint?.Phase is Sr5CareerCustomDrugRecipePhase.Reviewed
                    or Sr5CareerCustomDrugRecipePhase.Applying
                    or Sr5CareerCustomDrugRecipePhase.Applied
                    or Sr5CareerCustomDrugRecipePhase.RecoveryUnknown)
            {
                throw new InvalidOperationException(
                    "Resolve or explicitly reopen the durable custom-drug recipe before editing.");
            }
            CharacterCustomDrugSelection rebound = RebindSelection(live.Preparation!, selection);
            Sr5CareerCustomDrugRecipeCheckpoint editing = EditingCheckpoint(
                workspaceId,
                live.Preparation!,
                rebound);
            checkpoints.Write(editing);
            return Snapshot(
                workspaceId,
                live.Preparation!,
                editing,
                Sr5CareerCustomDrugRecipeNotices.DraftRestored);
        }
    }

    public Sr5CareerCustomDrugRecipeSnapshot Review(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerCustomDrugRecipeSnapshot live = RequireReady(LoadLocked(workspaceId));
            if (live.Checkpoint?.Phase is Sr5CareerCustomDrugRecipePhase.Reviewed
                    or Sr5CareerCustomDrugRecipePhase.Applying
                    or Sr5CareerCustomDrugRecipePhase.Applied
                    or Sr5CareerCustomDrugRecipePhase.RecoveryUnknown)
                throw new InvalidOperationException(
                    "The custom-drug recipe is already reviewed, applying, applied, or recovery-locked.");
            CharacterCustomDrugPreparation preparation = live.Preparation!;
            CharacterCustomDrugQuote quote = authority.Quote(preparation, live.Selection);
            if (!quote.Exact)
                throw new InvalidOperationException(quote.BlockReason);

            CharacterCustomDrugCommitCommand command = NewCommand(preparation, live.Selection, quote);
            var reviewed = new Sr5CareerCustomDrugRecipeCheckpoint(
                Sr5CareerCustomDrugRecipeSchemas.CheckpointV1,
                workspaceId,
                preparation.ContentRevision,
                preparation.CharacterDigest,
                preparation.CatalogDigest,
                preparation.RulesDigest,
                live.Selection,
                Sr5CareerCustomDrugRecipePhase.Reviewed,
                command,
                Receipt: null);
            checkpoints.Write(reviewed);
            return Snapshot(
                workspaceId,
                preparation,
                reviewed,
                Sr5CareerCustomDrugRecipeNotices.ReviewReady);
        }
    }

    public Sr5CareerCustomDrugRecipeSnapshot Confirm(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerCustomDrugRecipeSnapshot live = RequireReady(LoadLocked(workspaceId));
            if (!live.CanConfirm || live.Checkpoint?.Command is not { } command)
                throw new InvalidOperationException("The reviewed custom-drug recipe is stale.");

            Sr5CareerCustomDrugRecipeCheckpoint applying = live.Checkpoint with
            {
                Phase = Sr5CareerCustomDrugRecipePhase.Applying,
                Receipt = null
            };
            checkpoints.Write(applying);
            Sr5CareerCustomDrugWorkspaceSnapshot current = RequireCleanWorkspace(workspaceId);
            CharacterCustomDrugCommitResult committed = authority.Commit(
                current.Document.Content,
                current.ContentRevision,
                CharacterCustomDrugContext.Career,
                command);
            if (!committed.Committed || committed.Receipt is null)
            {
                Sr5CareerCustomDrugRecipeCheckpoint reviewed = applying with
                {
                    Phase = Sr5CareerCustomDrugRecipePhase.Reviewed
                };
                checkpoints.Write(reviewed);
                CharacterCustomDrugPreparation latest = authority.Prepare(
                    current.Document.Content,
                    current.ContentRevision,
                    CharacterCustomDrugContext.Career);
                return Snapshot(
                    workspaceId,
                    latest,
                    reviewed,
                    Sr5CareerCustomDrugRecipeNotices.CommitNotApplied,
                    committed.BlockReason);
            }

            // Persist the Core receipt before crossing the external CAS boundary.
            // A process death after the write can then be resolved by LookupReceipt
            // without ever replaying the recipe mutation.
            applying = applying with { Receipt = committed.Receipt };
            checkpoints.Write(applying);
            _ = workspaces.ReplaceAndCheckpoint(current, committed.CharacterXml);
            Sr5CareerCustomDrugWorkspaceSnapshot? after = workspaces.Read(workspaceId);
            if (after is not null && ReceiptProvesApplied(after, command, committed.Receipt))
            {
                return AppliedCheckpoint(
                    after,
                    applying,
                    committed.Receipt,
                    Sr5CareerCustomDrugRecipeNotices.CommitApplied);
            }

            Sr5CareerCustomDrugRecipeCheckpoint unknown = applying with
            {
                Phase = Sr5CareerCustomDrugRecipePhase.RecoveryUnknown
            };
            checkpoints.Write(unknown);
            CharacterCustomDrugPreparation unresolvedPreparation = after is null
                ? authority.Prepare(current.Document.Content, current.ContentRevision, CharacterCustomDrugContext.Career)
                : authority.Prepare(after.Document.Content, after.ContentRevision, CharacterCustomDrugContext.Career);
            return Snapshot(
                workspaceId,
                unresolvedPreparation,
                unknown,
                Sr5CareerCustomDrugRecipeNotices.RecoveryUnknown);
        }
    }

    public Sr5CareerCustomDrugRecipeSnapshot Undo(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerCustomDrugRecipeSnapshot live = LoadLocked(workspaceId);
            if (!live.HasAppliedReceipt || live.Checkpoint?.Receipt is not { } receipt)
                throw new InvalidOperationException("No exact custom-drug recipe receipt is available to undo.");
            Sr5CareerCustomDrugWorkspaceSnapshot current = RequireCleanWorkspace(workspaceId);
            CharacterCustomDrugCommitResult undone = authority.Undo(
                current.Document.Content,
                current.ContentRevision,
                CharacterCustomDrugContext.Career,
                new CharacterCustomDrugUndoCommand(receipt));
            if (!undone.Committed
                || undone.NewContentRevision != checked(current.ContentRevision + 1))
            {
                return RecoveryUnknown(live, undone.BlockReason);
            }

            Sr5CareerCustomDrugWorkspaceWriteResult write =
                workspaces.ReplaceAndCheckpoint(current, undone.CharacterXml);
            Sr5CareerCustomDrugWorkspaceSnapshot? verified = workspaces.Read(workspaceId);
            bool exact = write.Applied
                && verified is not null
                && verified.ContentRevision == undone.NewContentRevision
                && verified.SavedRevision == verified.ContentRevision
                && string.Equals(
                    CharacterCustomDrugRules.ComputeCharacterDigest(verified.Document.Content),
                    undone.NewCharacterDigest,
                    StringComparison.Ordinal);
            if (!exact)
                return RecoveryUnknown(live, write.Error);

            CharacterCustomDrugPreparation rebound = authority.Prepare(
                verified!.Document.Content,
                verified.ContentRevision,
                CharacterCustomDrugContext.Career);
            Sr5CareerCustomDrugRecipeCheckpoint editing = EditingCheckpoint(
                workspaceId,
                rebound,
                RebindSelection(rebound, live.Selection));
            checkpoints.Write(editing);
            return Snapshot(
                workspaceId,
                rebound,
                editing,
                Sr5CareerCustomDrugRecipeNotices.UndoApplied);
        }
    }

    public Sr5CareerCustomDrugRecipeSnapshot Reopen(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CareerCustomDrugRecipeSnapshot live = LoadLocked(workspaceId);
            if (live.IsRecoveryUnknown)
                throw new InvalidOperationException(
                    "An unknown custom-drug recipe outcome cannot be discarded or replayed on this phone.");
            if (!live.HasAppliedReceipt)
                throw new InvalidOperationException(
                    "Only an exact applied custom-drug recipe receipt can be closed.");
            Sr5CareerCustomDrugRecipeSnapshot ready = RequireReady(live);
            Sr5CareerCustomDrugRecipeCheckpoint editing = EditingCheckpoint(
                workspaceId,
                ready.Preparation!,
                DefaultSelection(ready.Preparation!));
            checkpoints.Write(editing);
            return Snapshot(
                workspaceId,
                ready.Preparation!,
                editing,
                Sr5CareerCustomDrugRecipeNotices.Reopened);
        }
    }

    private Sr5CareerCustomDrugRecipeSnapshot LoadLocked(CharacterWorkspaceId workspaceId)
    {
        Sr5CareerCustomDrugWorkspaceSnapshot? stored = workspaces.Read(workspaceId);
        if (stored is null)
            return Sr5CareerCustomDrugRecipeSnapshot.Blocked(
                workspaceId,
                CharacterCustomDrugBlockers.AuthorityUnavailable);
        if (stored.ContentRevision != stored.SavedRevision)
            return Sr5CareerCustomDrugRecipeSnapshot.Blocked(
                workspaceId,
                "The custom-drug recipe lane requires one clean saved Career revision.");

        CharacterCustomDrugPreparation preparation = authority.Prepare(
            stored.Document.Content,
            stored.ContentRevision,
            CharacterCustomDrugContext.Career);
        Sr5CareerCustomDrugRecipeCheckpoint? checkpoint = checkpoints.Read(workspaceId);
        if (checkpoint?.Phase is Sr5CareerCustomDrugRecipePhase.Applying
                or Sr5CareerCustomDrugRecipePhase.Applied
                or Sr5CareerCustomDrugRecipePhase.RecoveryUnknown)
        {
            return ResolveLocked(stored, preparation, checkpoint);
        }
        if (!preparation.Exact)
        {
            return new Sr5CareerCustomDrugRecipeSnapshot(
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
                notice: Sr5CareerCustomDrugRecipeNotices.ReviewStale,
                selection: DefaultSelection(preparation));
        }
        if (!checkpoint.Matches(preparation))
        {
            Sr5CareerCustomDrugRecipeCheckpoint rebound = EditingCheckpoint(
                workspaceId,
                preparation,
                RebindSelection(preparation, checkpoint.Selection));
            checkpoints.Write(rebound);
            return Snapshot(
                workspaceId,
                preparation,
                rebound,
                Sr5CareerCustomDrugRecipeNotices.ReviewStale);
        }
        return Snapshot(
            workspaceId,
            preparation,
            checkpoint,
            Sr5CareerCustomDrugRecipeNotices.DraftRestored);
    }

    private Sr5CareerCustomDrugRecipeSnapshot ResolveLocked(
        Sr5CareerCustomDrugWorkspaceSnapshot stored,
        CharacterCustomDrugPreparation preparation,
        Sr5CareerCustomDrugRecipeCheckpoint checkpoint)
    {
        if (checkpoint.Command is { } command
            && ReceiptProvesApplied(stored, command, checkpoint.Receipt))
        {
            CharacterCustomDrugCommitResult lookup = authority.LookupReceipt(
                stored.Document.Content,
                stored.ContentRevision,
                CharacterCustomDrugContext.Career,
                command);
            return AppliedCheckpoint(
                stored,
                checkpoint,
                lookup.Receipt!,
                checkpoint.Phase == Sr5CareerCustomDrugRecipePhase.Applied
                    ? Sr5CareerCustomDrugRecipeNotices.DraftRestored
                    : Sr5CareerCustomDrugRecipeNotices.CommitRecovered);
        }

        if (checkpoint.Command is { } pending
            && preparation.Exact
            && stored.ContentRevision == pending.ExpectedContentRevision
            && string.Equals(preparation.CharacterDigest, pending.ExpectedCharacterDigest, StringComparison.Ordinal)
            && string.Equals(preparation.CatalogDigest, pending.ExpectedCatalogDigest, StringComparison.Ordinal)
            && string.Equals(preparation.RulesDigest, pending.ExpectedRulesDigest, StringComparison.Ordinal))
        {
            CharacterCustomDrugQuote quote = authority.Quote(preparation, pending.Selection);
            if (quote.Exact
                && string.Equals(quote.QuoteDigest, pending.ExpectedQuoteDigest, StringComparison.Ordinal))
            {
                Sr5CareerCustomDrugRecipeCheckpoint reviewed = checkpoint with
                {
                    BoundContentRevision = preparation.ContentRevision,
                    BoundCharacterDigest = preparation.CharacterDigest,
                    BoundCatalogDigest = preparation.CatalogDigest,
                    BoundRulesDigest = preparation.RulesDigest,
                    Selection = pending.Selection,
                    Phase = Sr5CareerCustomDrugRecipePhase.Reviewed,
                    Receipt = null
                };
                checkpoints.Write(reviewed);
                return Snapshot(
                    checkpoint.WorkspaceId,
                    preparation,
                    reviewed,
                    Sr5CareerCustomDrugRecipeNotices.CommitNotApplied);
            }
        }

        Sr5CareerCustomDrugRecipeCheckpoint unknown = checkpoint with
        {
            Phase = Sr5CareerCustomDrugRecipePhase.RecoveryUnknown
        };
        checkpoints.Write(unknown);
        return Snapshot(
            checkpoint.WorkspaceId,
            preparation,
            unknown,
            Sr5CareerCustomDrugRecipeNotices.RecoveryUnknown,
            preparation.Exact ? null : preparation.Blockers.FirstOrDefault());
    }

    private bool ReceiptProvesApplied(
        Sr5CareerCustomDrugWorkspaceSnapshot stored,
        CharacterCustomDrugCommitCommand command,
        CharacterCustomDrugCommitReceipt? expectedReceipt)
    {
        CharacterCustomDrugCommitResult lookup = authority.LookupReceipt(
            stored.Document.Content,
            stored.ContentRevision,
            CharacterCustomDrugContext.Career,
            command);
        CharacterCustomDrugCommitReceipt? receipt = lookup.Receipt;
        return lookup.Committed
               && lookup.AlreadyCommitted
               && receipt is not null
               && receipt.ContentRevision == stored.ContentRevision
               && string.Equals(
                   receipt.CharacterDigest,
                   CharacterCustomDrugRules.ComputeCharacterDigest(stored.Document.Content),
                   StringComparison.Ordinal)
               && string.Equals(
                   receipt.ReceiptDigest,
                   CharacterCustomDrugRules.ComputeReceiptDigest(receipt),
                   StringComparison.Ordinal)
               && (expectedReceipt is null
                   || string.Equals(expectedReceipt.ReceiptDigest, receipt.ReceiptDigest, StringComparison.Ordinal));
    }

    private Sr5CareerCustomDrugRecipeSnapshot AppliedCheckpoint(
        Sr5CareerCustomDrugWorkspaceSnapshot stored,
        Sr5CareerCustomDrugRecipeCheckpoint checkpoint,
        CharacterCustomDrugCommitReceipt receipt,
        string notice)
    {
        CharacterCustomDrugPreparation preparation = authority.Prepare(
            stored.Document.Content,
            stored.ContentRevision,
            CharacterCustomDrugContext.Career);
        Sr5CareerCustomDrugRecipeCheckpoint applied = checkpoint with
        {
            BoundContentRevision = preparation.ContentRevision,
            BoundCharacterDigest = preparation.CharacterDigest,
            BoundCatalogDigest = preparation.CatalogDigest,
            BoundRulesDigest = preparation.RulesDigest,
            Phase = Sr5CareerCustomDrugRecipePhase.Applied,
            Receipt = receipt
        };
        checkpoints.Write(applied);
        return Snapshot(checkpoint.WorkspaceId, preparation, applied, notice);
    }

    private Sr5CareerCustomDrugRecipeSnapshot RecoveryUnknown(
        Sr5CareerCustomDrugRecipeSnapshot live,
        string? blocker)
    {
        Sr5CareerCustomDrugRecipeCheckpoint unknown = live.Checkpoint! with
        {
            Phase = Sr5CareerCustomDrugRecipePhase.RecoveryUnknown
        };
        checkpoints.Write(unknown);
        return Snapshot(
            live.WorkspaceId,
            live.Preparation!,
            unknown,
            Sr5CareerCustomDrugRecipeNotices.RecoveryUnknown,
            blocker);
    }

    private Sr5CareerCustomDrugWorkspaceSnapshot RequireCleanWorkspace(
        CharacterWorkspaceId workspaceId)
    {
        Sr5CareerCustomDrugWorkspaceSnapshot current = workspaces.Read(workspaceId)
            ?? throw new InvalidOperationException("The custom-drug recipe workspace is unavailable.");
        if (current.ContentRevision != current.SavedRevision)
            throw new InvalidOperationException("The custom-drug recipe workspace is not durably saved.");
        return current;
    }

    private static Sr5CareerCustomDrugRecipeSnapshot RequireReady(
        Sr5CareerCustomDrugRecipeSnapshot snapshot)
    {
        if (!snapshot.IsReady)
            throw new InvalidOperationException(
                snapshot.Blockers.FirstOrDefault()
                ?? CharacterCustomDrugBlockers.AuthorityUnavailable);
        return snapshot;
    }

    private Sr5CareerCustomDrugRecipeSnapshot Snapshot(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugPreparation preparation,
        Sr5CareerCustomDrugRecipeCheckpoint? checkpoint,
        string notice,
        string? blocker = null,
        CharacterCustomDrugSelection? selection = null)
    {
        CharacterCustomDrugSelection selected = RebindSelection(
            preparation,
            selection ?? checkpoint?.Selection ?? DefaultSelection(preparation));
        CharacterCustomDrugQuote quote = authority.Quote(preparation, selected);
        string[] blockers = string.IsNullOrWhiteSpace(blocker) ? [] : [blocker];
        return new Sr5CareerCustomDrugRecipeSnapshot(
            workspaceId,
            preparation,
            selected,
            quote,
            checkpoint,
            notice,
            blockers);
    }

    private static Sr5CareerCustomDrugRecipeCheckpoint EditingCheckpoint(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection)
        => new(
            Sr5CareerCustomDrugRecipeSchemas.CheckpointV1,
            workspaceId,
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            preparation.RulesDigest,
            RebindSelection(preparation, selection),
            Sr5CareerCustomDrugRecipePhase.Editing,
            Command: null,
            Receipt: null);

    private static CharacterCustomDrugSelection DefaultSelection(
        CharacterCustomDrugPreparation preparation)
        => EmptySelection with
        {
            GradeId = preparation.Grades.FirstOrDefault()?.Id
                      ?? new CharacterCustomDrugGradeId(Guid.Empty)
        };

    private static CharacterCustomDrugSelection RebindSelection(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection)
    {
        CharacterCustomDrugGradeId gradeId = preparation.Grades.Any(candidate =>
            candidate.Id == selection.GradeId)
            ? selection.GradeId
            : preparation.Grades.FirstOrDefault()?.Id
              ?? new CharacterCustomDrugGradeId(Guid.Empty);
        Dictionary<CharacterCustomDrugComponentId, CharacterCustomDrugComponentSource> components =
            preparation.Components
                .GroupBy(static component => component.Id)
                .Where(static group => group.Count() == 1)
                .ToDictionary(static group => group.Key, static group => group.Single());
        CharacterCustomDrugComponentSelection[] rebound = (selection.Components ?? [])
            .Where(selected => components.TryGetValue(selected.ComponentId, out CharacterCustomDrugComponentSource? source)
                               && source.Effects.Count(effect => effect.Level == selected.Level) == 1)
            .Take(Math.Max(0, preparation.Policy.MaximumComponents))
            .ToArray();
        return selection with
        {
            GradeId = gradeId,
            Quantity = 1m,
            Stolen = false,
            FreeCost = false,
            MarkupPercent = 0m,
            Components = rebound
        };
    }

    private static CharacterCustomDrugCommitCommand NewCommand(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection,
        CharacterCustomDrugQuote quote)
    {
        var occupied = new HashSet<Guid>(selection.Components.Select(component => component.ComponentId.Value))
        {
            selection.GradeId.Value
        };
        Guid drugId = NewDistinctGuid(occupied);
        occupied.Add(drugId);
        Guid[] componentIds = selection.Components.Select(_ =>
        {
            Guid value = NewDistinctGuid(occupied);
            occupied.Add(value);
            return value;
        }).ToArray();
        return new CharacterCustomDrugCommitCommand(
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            preparation.RulesDigest,
            quote.QuoteDigest,
            $"custom-drug-recipe:{Guid.NewGuid():N}",
            selection,
            new CharacterCustomDrugInstanceId(drugId),
            componentIds);
    }

    private static Guid NewDistinctGuid(IReadOnlySet<Guid> occupied)
    {
        Guid value;
        do value = Guid.NewGuid();
        while (value == Guid.Empty || occupied.Contains(value));
        return value;
    }
}
