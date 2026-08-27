using System.Security.Cryptography;
using System.Text;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Shared native orchestration for the SR5 recipe designer. All calculations
/// and Career XML projection stay inside ICharacterCustomDrugAuthority.
/// Creation stops at a typed, durable finalizer contribution.
/// </summary>
public sealed class Sr5CustomDrugLabService(
    ICharacterCustomDrugAuthority authority,
    ICharacterCreationCustomDrugContributionService creationContributions,
    ISr5CustomDrugWorkspaceStore workspaces,
    ISr5CustomDrugLabCheckpointStore checkpoints)
{
    private readonly object _gate = new();

    public static CharacterCustomDrugSelection EmptySelection { get; } = new(
        string.Empty,
        new CharacterCustomDrugGradeId(Guid.Empty),
        Quantity: 1m,
        Stolen: false,
        FreeCost: false,
        MarkupPercent: 0m,
        Components: []);

    public Sr5CustomDrugLabSnapshot Load(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context)
    {
        lock (_gate)
        {
            return LoadLocked(workspaceId, context);
        }
    }

    public Sr5CustomDrugLabSnapshot UpdateSelection(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context,
        CharacterCustomDrugSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        lock (_gate)
        {
            Sr5CustomDrugLabSnapshot live = RequireReady(LoadLocked(workspaceId, context));
            if (live.Checkpoint?.Phase is Sr5CustomDrugCheckpointPhase.Applied
                    or Sr5CustomDrugCheckpointPhase.Applying
                    or Sr5CustomDrugCheckpointPhase.QueuedForFinalization
                    or Sr5CustomDrugCheckpointPhase.RecoveryUnknown)
            {
                throw new InvalidOperationException(
                    "Resolve or explicitly reopen the durable custom-drug checkpoint before editing.");
            }
            CharacterCustomDrugSelection normalized = RecipeDefinitionSelection(selection);
            CharacterCustomDrugPreparation preparation = live.Preparation!;
            Sr5CustomDrugLabCheckpoint checkpoint = EditingCheckpoint(
                workspaceId,
                context,
                preparation,
                normalized);
            checkpoints.Write(checkpoint);
            return Snapshot(preparation, checkpoint, Sr5CustomDrugLabNotices.DraftRestored);
        }
    }

    public Sr5CustomDrugLabSnapshot StartEditing(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context)
    {
        lock (_gate)
        {
            Sr5CustomDrugLabSnapshot live = RequireReady(LoadLocked(workspaceId, context));
            if (context == CharacterCustomDrugContext.Creation
                && live.IsQueuedForFinalization)
            {
                throw new InvalidOperationException(
                    "The durable creation contribution is immutable until finalization.");
            }
            CharacterCustomDrugSelection selection = live.Checkpoint?.Selection ?? live.Selection;
            Sr5CustomDrugLabCheckpoint checkpoint = EditingCheckpoint(
                workspaceId,
                context,
                live.Preparation!,
                selection);
            checkpoints.Write(checkpoint);
            return Snapshot(live.Preparation!, checkpoint, Sr5CustomDrugLabNotices.DraftRestored);
        }
    }

    public Sr5CustomDrugLabSnapshot Review(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context)
    {
        lock (_gate)
        {
            Sr5CustomDrugLabSnapshot live = RequireReady(LoadLocked(workspaceId, context));
            CharacterCustomDrugPreparation preparation = live.Preparation!;
            CharacterCustomDrugQuote quote = authority.Quote(preparation, live.Selection);
            if (!quote.Exact)
                throw new InvalidOperationException(quote.BlockReason);

            CharacterCustomDrugInstanceId drugId = new(Guid.NewGuid());
            Guid[] componentIds = live.Selection.Components.Select(static _ => Guid.NewGuid()).ToArray();
            var command = new CharacterCustomDrugCommitCommand(
                preparation.ContentRevision,
                preparation.CharacterDigest,
                preparation.CatalogDigest,
                preparation.RulesDigest,
                quote.QuoteDigest,
                IdempotencyKey(workspaceId, context, drugId),
                live.Selection,
                drugId,
                componentIds);
            var checkpoint = new Sr5CustomDrugLabCheckpoint(
                Sr5CustomDrugLabSchemas.CheckpointV1,
                workspaceId,
                context,
                preparation.ContentRevision,
                preparation.CharacterDigest,
                preparation.CatalogDigest,
                preparation.RulesDigest,
                live.Selection,
                Sr5CustomDrugCheckpointPhase.Reviewed,
                command,
                Receipt: null,
                CreationContribution: null);
            checkpoints.Write(checkpoint);
            return Snapshot(preparation, checkpoint, Sr5CustomDrugLabNotices.ReviewReady);
        }
    }

    public Sr5CustomDrugLabSnapshot ConfirmCreation(
        CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CustomDrugLabSnapshot live = RequireReady(
                LoadLocked(workspaceId, CharacterCustomDrugContext.Creation));
            if (!live.CanConfirm
                || live.Checkpoint?.Command is not { } command)
            {
                throw new InvalidOperationException(
                    "The reviewed creation recipe is stale or not bound to an exact Core command.");
            }

            Sr5CustomDrugWorkspaceSnapshot current = RequireCleanWorkspace(workspaceId);
            if (current.ContentRevision != live.Preparation!.ContentRevision
                || !live.Checkpoint.Matches(live.Preparation))
            {
                throw new InvalidOperationException(
                    CharacterCreationCustomDrugBlockers.StaleWorkspaceRevision);
            }
            var request = new CharacterCreationCustomDrugQueueRequest(
                workspaceId,
                current.ContentRevision,
                current.SavedRevision,
                current.Document.AuxiliaryStateDigest,
                command,
                QueueIdempotencyKey(workspaceId, command.NewDrugInstanceId),
                ExplicitlyConfirmed: true);
            CharacterCreationCustomDrugResult result = creationContributions.Queue(request);
            CharacterCreationCustomDrugFinalizationContribution? contribution =
                MatchingContribution(result.Contribution, request);
            bool recoveredByLookup = false;
            if (contribution is null)
            {
                CharacterCreationCustomDrugResult lookup = creationContributions.Load(
                    new CharacterCreationCustomDrugLoadRequest(workspaceId));
                contribution = MatchingContribution(lookup.Contribution, request);
                recoveredByLookup = contribution is not null;
            }
            if (contribution is null)
            {
                throw new InvalidOperationException(
                    result.Blockers.FirstOrDefault()
                    ?? CharacterCreationCustomDrugBlockers.PersistenceAuthorityRequired);
            }

            Sr5CustomDrugWorkspaceSnapshot persisted = RequireCleanWorkspace(workspaceId);
            CharacterCustomDrugPreparation rebound = authority.Prepare(
                persisted.Document.Content,
                persisted.ContentRevision,
                CharacterCustomDrugContext.Creation);
            if (!rebound.Exact
                || !CharacterCreationCustomDrugContributionRules.IsValid(
                    contribution,
                    workspaceId,
                    persisted.ContentRevision)
                || !ContributionMatchesPreparation(contribution, rebound))
            {
                throw new InvalidOperationException(
                    CharacterCreationCustomDrugBlockers.ProjectionRejected);
            }
            Sr5CustomDrugLabCheckpoint queued = live.Checkpoint with
            {
                BoundContentRevision = rebound.ContentRevision,
                BoundCharacterDigest = rebound.CharacterDigest,
                BoundCatalogDigest = rebound.CatalogDigest,
                BoundRulesDigest = rebound.RulesDigest,
                Phase = Sr5CustomDrugCheckpointPhase.QueuedForFinalization,
                Command = contribution.ToVerificationCommand(),
                CreationContribution = contribution
            };
            checkpoints.Write(queued);
            return Snapshot(
                rebound,
                queued,
                recoveredByLookup
                || result.Outcome == CharacterCreationCustomDrugOutcomes.Replayed
                    ? Sr5CustomDrugLabNotices.FinalizerContributionRecovered
                    : Sr5CustomDrugLabNotices.QueuedForFinalization);
        }
    }

    public Sr5CustomDrugLabSnapshot ConfirmCareer(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CustomDrugLabSnapshot live = RequireReady(
                LoadLocked(workspaceId, CharacterCustomDrugContext.Career));
            if (!live.CanConfirm || live.Checkpoint?.Command is not { } command)
                throw new InvalidOperationException("The reviewed Career recipe is stale.");

            Sr5CustomDrugLabCheckpoint applying = live.Checkpoint with
            {
                Phase = Sr5CustomDrugCheckpointPhase.Applying,
                Receipt = null
            };
            checkpoints.Write(applying);
            return CommitCareerLocked(applying, command);
        }
    }

    public Sr5CustomDrugLabSnapshot UndoCareer(CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CustomDrugLabSnapshot live = RequireReady(
                LoadLocked(workspaceId, CharacterCustomDrugContext.Career));
            if (!live.HasAppliedReceipt || live.Checkpoint?.Receipt is not { } receipt)
                throw new InvalidOperationException("No exact custom-drug receipt is available to undo.");
            Sr5CustomDrugWorkspaceSnapshot current = RequireCleanWorkspace(workspaceId);
            CharacterCustomDrugCommitResult undone = authority.Undo(
                current.Document.Content,
                current.ContentRevision,
                CharacterCustomDrugContext.Career,
                new CharacterCustomDrugUndoCommand(receipt));
            if (!undone.Committed
                || undone.NewContentRevision != checked(current.ContentRevision + 1))
            {
                return live with
                {
                    Notice = Sr5CustomDrugLabNotices.RecoveryUnknown,
                    Blockers = [undone.BlockReason]
                };
            }

            Sr5CustomDrugWorkspaceWriteResult write =
                workspaces.ReplaceAndCheckpoint(current, undone.CharacterXml);
            Sr5CustomDrugWorkspaceSnapshot? verified = workspaces.Read(workspaceId);
            bool exact = write.Applied
                && verified is not null
                && verified.ContentRevision == undone.NewContentRevision
                && verified.SavedRevision == verified.ContentRevision
                && string.Equals(
                    CharacterCustomDrugRules.ComputeCharacterDigest(verified.Document.Content),
                    undone.NewCharacterDigest,
                    StringComparison.Ordinal);
            if (!exact)
            {
                Sr5CustomDrugLabCheckpoint unknown = live.Checkpoint! with
                {
                    Phase = Sr5CustomDrugCheckpointPhase.RecoveryUnknown
                };
                checkpoints.Write(unknown);
                return Snapshot(
                    live.Preparation!,
                    unknown,
                    Sr5CustomDrugLabNotices.RecoveryUnknown,
                    write.Error);
            }

            CharacterCustomDrugPreparation rebound = authority.Prepare(
                verified!.Document.Content,
                verified.ContentRevision,
                CharacterCustomDrugContext.Career);
            Sr5CustomDrugLabCheckpoint editing = EditingCheckpoint(
                workspaceId,
                CharacterCustomDrugContext.Career,
                rebound,
                live.Selection);
            checkpoints.Write(editing);
            return Snapshot(rebound, editing, Sr5CustomDrugLabNotices.UndoApplied);
        }
    }

    public CharacterCreationCustomDrugFinalizationContribution? ReadCreationContribution(
        CharacterWorkspaceId workspaceId)
    {
        lock (_gate)
        {
            Sr5CustomDrugLabSnapshot live = LoadLocked(
                workspaceId,
                CharacterCustomDrugContext.Creation);
            return live.IsQueuedForFinalization
                && live.Checkpoint?.CreationContribution is { } contribution
                && live.Preparation is { } preparation
                && CharacterCreationCustomDrugContributionRules.IsValid(
                    contribution,
                    workspaceId,
                    preparation.ContentRevision)
                    ? contribution
                    : null;
        }
    }

    private Sr5CustomDrugLabSnapshot LoadLocked(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context)
    {
        Sr5CustomDrugWorkspaceSnapshot? stored = workspaces.Read(workspaceId);
        if (stored is null)
            return Sr5CustomDrugLabSnapshot.Blocked(
                workspaceId,
                context,
                CharacterCustomDrugBlockers.AuthorityUnavailable);
        if (stored.ContentRevision != stored.SavedRevision)
            return Sr5CustomDrugLabSnapshot.Blocked(
                workspaceId,
                context,
                "The custom-drug lab requires one clean saved runner revision.");

        CharacterCustomDrugPreparation preparation = authority.Prepare(
            stored.Document.Content,
            stored.ContentRevision,
            context);
        if (!preparation.Exact)
        {
            return new Sr5CustomDrugLabSnapshot(
                workspaceId,
                context,
                preparation,
                EmptySelection,
                null,
                null,
                string.Empty,
                preparation.Blockers);
        }

        Sr5CustomDrugLabCheckpoint? checkpoint = checkpoints.Read(workspaceId, context);
        if (context == CharacterCustomDrugContext.Creation
            && checkpoint?.Phase is null or Sr5CustomDrugCheckpointPhase.QueuedForFinalization)
        {
            CharacterCreationCustomDrugResult loaded = creationContributions.Load(
                new CharacterCreationCustomDrugLoadRequest(workspaceId));
            if (loaded.Success
                && loaded.Contribution is { } contribution
                && CharacterCreationCustomDrugContributionRules.IsValid(
                    contribution,
                    workspaceId,
                    stored.ContentRevision)
                && ContributionMatchesPreparation(contribution, preparation))
            {
                Sr5CustomDrugLabCheckpoint recovered = QueuedCreationCheckpoint(
                    workspaceId,
                    preparation,
                    contribution);
                checkpoints.Write(recovered);
                return Snapshot(
                    preparation,
                    recovered,
                    checkpoint is null
                        ? Sr5CustomDrugLabNotices.FinalizerContributionRecovered
                        : Sr5CustomDrugLabNotices.DraftRestored);
            }
            if (checkpoint?.Phase == Sr5CustomDrugCheckpointPhase.QueuedForFinalization)
            {
                Sr5CustomDrugLabCheckpoint rejected = EditingCheckpoint(
                    workspaceId,
                    context,
                    preparation,
                    checkpoint.Selection);
                checkpoints.Write(rejected);
                return Snapshot(
                    preparation,
                    rejected,
                    Sr5CustomDrugLabNotices.ReviewStale,
                    loaded.Blockers.FirstOrDefault()
                    ?? CharacterCreationCustomDrugBlockers.ProjectionRejected);
            }
        }
        if (checkpoint is null)
        {
            CharacterCustomDrugSelection initial = EmptySelection with
            {
                GradeId = preparation.Grades.Count == 1
                    ? preparation.Grades[0].Id
                    : EmptySelection.GradeId
            };
            return Snapshot(
                preparation,
                null,
                string.Empty,
                selection: initial,
                workspaceId: workspaceId);
        }

        if (context == CharacterCustomDrugContext.Career
            && checkpoint.Phase is Sr5CustomDrugCheckpointPhase.Applying
                or Sr5CustomDrugCheckpointPhase.Applied
                or Sr5CustomDrugCheckpointPhase.RecoveryUnknown)
        {
            return ResolveCareerLocked(preparation, checkpoint, stored);
        }

        if (!checkpoint.Matches(preparation))
        {
            Sr5CustomDrugLabCheckpoint rebound = EditingCheckpoint(
                workspaceId,
                context,
                preparation,
                checkpoint.Selection);
            checkpoints.Write(rebound);
            return Snapshot(preparation, rebound, Sr5CustomDrugLabNotices.ReviewStale);
        }

        return Snapshot(preparation, checkpoint, Sr5CustomDrugLabNotices.DraftRestored);
    }

    private Sr5CustomDrugLabSnapshot CommitCareerLocked(
        Sr5CustomDrugLabCheckpoint applying,
        CharacterCustomDrugCommitCommand command)
    {
        Sr5CustomDrugWorkspaceSnapshot current = RequireCleanWorkspace(applying.WorkspaceId);
        CharacterCustomDrugCommitResult recovered = authority.LookupReceipt(
            current.Document.Content,
            current.ContentRevision,
            CharacterCustomDrugContext.Career,
            command);
        if (recovered.AlreadyCommitted && recovered.Receipt is { } recoveredReceipt)
            return AppliedCheckpoint(current, applying, recoveredReceipt, Sr5CustomDrugLabNotices.CommitRecovered);

        CharacterCustomDrugCommitResult committed = authority.Commit(
            current.Document.Content,
            current.ContentRevision,
            CharacterCustomDrugContext.Career,
            command);
        if (!committed.Committed || committed.Receipt is null)
        {
            Sr5CustomDrugLabCheckpoint reviewed = applying with
            {
                Phase = Sr5CustomDrugCheckpointPhase.Reviewed
            };
            checkpoints.Write(reviewed);
            CharacterCustomDrugPreparation preparation = authority.Prepare(
                current.Document.Content,
                current.ContentRevision,
                CharacterCustomDrugContext.Career);
            return Snapshot(preparation, reviewed, Sr5CustomDrugLabNotices.CommitNotApplied, committed.BlockReason);
        }

        _ = workspaces.ReplaceAndCheckpoint(current, committed.CharacterXml);
        Sr5CustomDrugWorkspaceSnapshot? after = workspaces.Read(applying.WorkspaceId);
        if (after is not null)
        {
            CharacterCustomDrugCommitResult lookup = authority.LookupReceipt(
                after.Document.Content,
                after.ContentRevision,
                CharacterCustomDrugContext.Career,
                command);
            if (lookup.AlreadyCommitted && lookup.Receipt is { } receipt)
                return AppliedCheckpoint(after, applying, receipt, Sr5CustomDrugLabNotices.CommitApplied);
        }

        Sr5CustomDrugLabCheckpoint unknown = applying with
        {
            Phase = Sr5CustomDrugCheckpointPhase.RecoveryUnknown
        };
        checkpoints.Write(unknown);
        CharacterCustomDrugPreparation latest = after is null
            ? authority.Prepare(
                current.Document.Content,
                current.ContentRevision,
                CharacterCustomDrugContext.Career)
            : authority.Prepare(
                after.Document.Content,
                after.ContentRevision,
                CharacterCustomDrugContext.Career);
        return Snapshot(latest, unknown, Sr5CustomDrugLabNotices.RecoveryUnknown);
    }

    private Sr5CustomDrugLabSnapshot ResolveCareerLocked(
        CharacterCustomDrugPreparation preparation,
        Sr5CustomDrugLabCheckpoint checkpoint,
        Sr5CustomDrugWorkspaceSnapshot stored)
    {
        if (checkpoint.Command is null)
        {
            Sr5CustomDrugLabCheckpoint unknown = checkpoint with
            {
                Phase = Sr5CustomDrugCheckpointPhase.RecoveryUnknown
            };
            checkpoints.Write(unknown);
            return Snapshot(preparation, unknown, Sr5CustomDrugLabNotices.RecoveryUnknown);
        }

        CharacterCustomDrugCommitResult lookup = authority.LookupReceipt(
            stored.Document.Content,
            stored.ContentRevision,
            CharacterCustomDrugContext.Career,
            checkpoint.Command);
        if (lookup.AlreadyCommitted && lookup.Receipt is { } receipt)
            return AppliedCheckpoint(stored, checkpoint, receipt, Sr5CustomDrugLabNotices.CommitRecovered);

        if (stored.ContentRevision == checkpoint.Command.ExpectedContentRevision
            && string.Equals(
                preparation.CharacterDigest,
                checkpoint.Command.ExpectedCharacterDigest,
                StringComparison.Ordinal)
            && string.Equals(preparation.CatalogDigest, checkpoint.Command.ExpectedCatalogDigest, StringComparison.Ordinal)
            && string.Equals(preparation.RulesDigest, checkpoint.Command.ExpectedRulesDigest, StringComparison.Ordinal))
        {
            Sr5CustomDrugLabCheckpoint reviewed = checkpoint with
            {
                Phase = Sr5CustomDrugCheckpointPhase.Reviewed,
                Receipt = null
            };
            checkpoints.Write(reviewed);
            return Snapshot(preparation, reviewed, Sr5CustomDrugLabNotices.CommitNotApplied);
        }

        Sr5CustomDrugLabCheckpoint unresolved = checkpoint with
        {
            Phase = Sr5CustomDrugCheckpointPhase.RecoveryUnknown
        };
        checkpoints.Write(unresolved);
        return Snapshot(preparation, unresolved, Sr5CustomDrugLabNotices.RecoveryUnknown);
    }

    private Sr5CustomDrugLabSnapshot AppliedCheckpoint(
        Sr5CustomDrugWorkspaceSnapshot stored,
        Sr5CustomDrugLabCheckpoint checkpoint,
        CharacterCustomDrugCommitReceipt receipt,
        string notice)
    {
        CharacterCustomDrugPreparation preparation = authority.Prepare(
            stored.Document.Content,
            stored.ContentRevision,
            CharacterCustomDrugContext.Career);
        Sr5CustomDrugLabCheckpoint applied = checkpoint with
        {
            BoundContentRevision = preparation.ContentRevision,
            BoundCharacterDigest = preparation.CharacterDigest,
            BoundCatalogDigest = preparation.CatalogDigest,
            BoundRulesDigest = preparation.RulesDigest,
            Phase = Sr5CustomDrugCheckpointPhase.Applied,
            Receipt = receipt
        };
        checkpoints.Write(applied);
        return Snapshot(preparation, applied, notice);
    }

    private Sr5CustomDrugWorkspaceSnapshot RequireCleanWorkspace(CharacterWorkspaceId workspaceId)
    {
        Sr5CustomDrugWorkspaceSnapshot current = workspaces.Read(workspaceId)
            ?? throw new InvalidOperationException("The custom-drug workspace is unavailable.");
        if (current.ContentRevision != current.SavedRevision)
            throw new InvalidOperationException("The custom-drug workspace is not durably saved.");
        return current;
    }

    private static Sr5CustomDrugLabSnapshot RequireReady(Sr5CustomDrugLabSnapshot snapshot)
    {
        if (!snapshot.IsReady)
            throw new InvalidOperationException(
                snapshot.Blockers.FirstOrDefault() ?? CharacterCustomDrugBlockers.AuthorityUnavailable);
        return snapshot;
    }

    private Sr5CustomDrugLabSnapshot Snapshot(
        CharacterCustomDrugPreparation preparation,
        Sr5CustomDrugLabCheckpoint? checkpoint,
        string notice,
        string? blocker = null,
        CharacterCustomDrugSelection? selection = null,
        CharacterWorkspaceId? workspaceId = null)
    {
        CharacterCustomDrugSelection selected = RecipeDefinitionSelection(
            selection ?? checkpoint?.Selection ?? EmptySelection);
        CharacterCustomDrugQuote quote = authority.Quote(preparation, selected);
        string[] blockers = string.IsNullOrWhiteSpace(blocker)
            ? []
            : [blocker];
        return new Sr5CustomDrugLabSnapshot(
            checkpoint?.WorkspaceId ?? workspaceId ?? default,
            preparation.Context,
            preparation,
            selected,
            quote,
            checkpoint,
            notice,
            blockers);
    }

    private static Sr5CustomDrugLabCheckpoint EditingCheckpoint(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context,
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection)
        => new(
            Sr5CustomDrugLabSchemas.CheckpointV1,
            workspaceId,
            context,
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            preparation.RulesDigest,
            RecipeDefinitionSelection(selection),
            Sr5CustomDrugCheckpointPhase.Editing,
            Command: null,
            Receipt: null,
            CreationContribution: null);

    private static CharacterCustomDrugSelection RecipeDefinitionSelection(
        CharacterCustomDrugSelection selection)
        => selection with
        {
            Name = selection.Name?.Trim() ?? string.Empty,
            Quantity = 1m,
            Stolen = false,
            FreeCost = false,
            MarkupPercent = 0m,
            Components = selection.Components?.ToArray() ?? []
        };

    private static string IdempotencyKey(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context,
        CharacterCustomDrugInstanceId drugId)
    {
        string workspaceDigest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(workspaceId.Value)))
            .ToLowerInvariant();
        return $"android-custom-drug:{context}:{workspaceDigest}:{drugId.Value:N}";
    }

    private static string QueueIdempotencyKey(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugInstanceId drugId)
        => IdempotencyKey(workspaceId, CharacterCustomDrugContext.Creation, drugId)
           + ":queue";

    private static CharacterCreationCustomDrugFinalizationContribution? MatchingContribution(
        CharacterCreationCustomDrugFinalizationContribution? contribution,
        CharacterCreationCustomDrugQueueRequest request)
    {
        if (contribution is null
            || contribution.WorkspaceId != request.WorkspaceId
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                contribution.RequestIdempotencyKeyDigest,
                CharacterCreationCustomDrugContributionRules
                    .ComputeRequestIdempotencyKeyDigest(request.IdempotencyKey))
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                contribution.RequestCommandDigest,
                CharacterCreationCustomDrugContributionRules
                    .ComputeRequestCommandDigest(request)))
            return null;
        return contribution;
    }

    private static bool ContributionMatchesPreparation(
        CharacterCreationCustomDrugFinalizationContribution contribution,
        CharacterCustomDrugPreparation preparation)
        => contribution.ExpectedContentRevision == preparation.ContentRevision
           && CharacterCreationFinalizationDigest.EqualsFixedTime(
               contribution.ExpectedCharacterDigest,
               preparation.CharacterDigest)
           && CharacterCreationFinalizationDigest.EqualsFixedTime(
               contribution.ExpectedCatalogDigest,
               preparation.CatalogDigest)
           && CharacterCreationFinalizationDigest.EqualsFixedTime(
               contribution.ExpectedRulesDigest,
               preparation.RulesDigest);

    private static Sr5CustomDrugLabCheckpoint QueuedCreationCheckpoint(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugPreparation preparation,
        CharacterCreationCustomDrugFinalizationContribution contribution)
        => new(
            Sr5CustomDrugLabSchemas.CheckpointV1,
            workspaceId,
            CharacterCustomDrugContext.Creation,
            preparation.ContentRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            preparation.RulesDigest,
            contribution.Selection,
            Sr5CustomDrugCheckpointPhase.QueuedForFinalization,
            contribution.ToVerificationCommand(),
            Receipt: null,
            contribution);
}
