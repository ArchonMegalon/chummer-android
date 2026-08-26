using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Immutable, display-only reward context supplied by the run authority. The
/// After Run settlement service does not award Karma or Nuyen; Android must
/// never turn these values into an independent compatibility mutation.
/// </summary>
public sealed record Sr5AfterRunRewardContext(
    string ContractName,
    CharacterAfterRunSettlementIdentity Identity,
    string RunTitle,
    DateTimeOffset CompletedAt,
    int KarmaAward,
    decimal NuyenAward,
    string RewardReceiptDigest,
    string ContextDigest)
{
    public const string CurrentContractName =
        "chummer.android.sr5-after-run-reward-context/v1";

    public bool IsExact()
        => string.Equals(ContractName, CurrentContractName, StringComparison.Ordinal)
            && Identity is
            {
                ProposalId: var proposalId,
                RunId: var runId,
                CharacterId: var characterId
            }
            && proposalId != Guid.Empty
            && runId != Guid.Empty
            && characterId != Guid.Empty
            && !string.IsNullOrWhiteSpace(RunTitle)
            && RunTitle.Length <= CharacterAfterRunSettlementRules.MaximumTextLength
            && CompletedAt != default
            && KarmaAward is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
            && NuyenAward is >= 0m and <= CharacterAfterRunSettlementRules.MaximumValue
            && CharacterAfterRunSettlementRules.IsCanonicalDigest(RewardReceiptDigest)
            && CharacterAfterRunSettlementRules.IsCanonicalDigest(ContextDigest)
            && string.Equals(ContextDigest, ComputeDigest(this), StringComparison.Ordinal);

    public static Sr5AfterRunRewardContext Create(
        CharacterAfterRunSettlementIdentity identity,
        string runTitle,
        DateTimeOffset completedAt,
        int karmaAward,
        decimal nuyenAward,
        string rewardReceiptDigest)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(runTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(rewardReceiptDigest);
        var unsigned = new Sr5AfterRunRewardContext(
            CurrentContractName,
            identity,
            runTitle.Trim(),
            completedAt,
            karmaAward,
            nuyenAward,
            rewardReceiptDigest,
            string.Empty);
        return unsigned with { ContextDigest = ComputeDigest(unsigned) };
    }

    private static string ComputeDigest(Sr5AfterRunRewardContext context)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join(
                '\0',
                context.ContractName,
                context.Identity.ProposalId.ToString("D"),
                context.Identity.RunId.ToString("D"),
                context.Identity.CharacterId.ToString("D"),
                context.RunTitle,
                context.CompletedAt.ToUniversalTime().ToString("O"),
                context.KarmaAward.ToString(System.Globalization.CultureInfo.InvariantCulture),
                context.NuyenAward.ToString(System.Globalization.CultureInfo.InvariantCulture),
                context.RewardReceiptDigest))));
}

public sealed record Sr5AfterRunSettlementCandidate(
    Sr5AfterRunRewardContext RewardContext,
    CharacterAfterRunSettlementQuoteBinding Binding)
{
    [JsonIgnore]
    public CharacterAfterRunSettlementQuote Quote => Binding.Quote;

    public bool IsExact(CharacterWorkspaceId workspaceId, long workspaceRevision)
        => RewardContext is not null
            && RewardContext.IsExact()
            && Binding is not null
            && RewardContext.Identity == Binding.Identity
            && string.Equals(
                Binding.ContractName,
                CharacterAfterRunSettlementServiceSchemas.QuoteV1,
                StringComparison.Ordinal)
            && Binding.WorkspaceId == workspaceId
            && Binding.WorkspaceRevision == workspaceRevision
            && Binding.Identity == Quote.Identity
            && CharacterAfterRunSettlementRules.IsCoherent(Quote)
            && CharacterAfterRunSettlementRules.IsCanonicalDigest(
                Binding.BindingDigest)
            && CharacterAfterRunSettlementServiceIntegrity.TryComputeBindingDigest(
                workspaceId,
                workspaceRevision,
                Quote,
                out string bindingDigest)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(bindingDigest),
                Encoding.UTF8.GetBytes(Binding.BindingDigest));
}

public enum Sr5AfterRunCatalogStatus
{
    Available,
    Missing,
    Corrupt,
    Unavailable
}

/// <summary>
/// One exact saved runner revision plus the run proposals visible to that
/// owner. An empty/unavailable projection is a first-class state, not a cue to
/// fabricate a run or fall back to generic editing.
/// </summary>
public sealed record Sr5AfterRunSettlementEditorState(
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    Sr5AfterRunCatalogStatus Status,
    IReadOnlyList<Sr5AfterRunSettlementCandidate> Candidates,
    int OmittedProposalCount,
    IReadOnlyList<string> Blockers)
{
    public bool IsExact()
        => !string.IsNullOrWhiteSpace(WorkspaceId.Value)
            && WorkspaceRevision > 0
            && Enum.IsDefined(Status)
            && Candidates is not null
            && OmittedProposalCount >= 0
            && Blockers is not null
            && Blockers.All(static blocker =>
                !string.IsNullOrWhiteSpace(blocker)
                && blocker.Length <= CharacterAfterRunSettlementRules.MaximumTextLength)
            && Candidates.All(candidate =>
                candidate is not null
                && candidate.IsExact(WorkspaceId, WorkspaceRevision))
            && Candidates.Select(candidate => candidate.Quote.Identity).Distinct().Count()
                == Candidates.Count
            && (Status == Sr5AfterRunCatalogStatus.Available
                ? Candidates.Count > 0 && Blockers.Count == 0
                : Candidates.Count == 0);

    public static Sr5AfterRunSettlementEditorState Unavailable(
        CharacterWorkspaceId workspaceId,
        long workspaceRevision,
        string blocker)
        => new(
            workspaceId,
            workspaceRevision,
            Sr5AfterRunCatalogStatus.Unavailable,
            [],
            OmittedProposalCount: 0,
            [blocker]);
}

public sealed record Sr5AfterRunReviewAcknowledgements(
    bool RunContextReviewed,
    bool RewardsReviewed,
    bool ConsequencesReviewed,
    bool ContactsReviewed,
    bool GmApprovalReviewed,
    bool OwnerApprovalReviewed)
{
    public bool AllReviewed => RunContextReviewed
        && RewardsReviewed
        && ConsequencesReviewed
        && ContactsReviewed
        && GmApprovalReviewed
        && OwnerApprovalReviewed;
}

public sealed record Sr5AfterRunSettlementDraft(
    Guid OwnerId,
    Sr5AfterRunSettlementCandidate Candidate,
    CharacterAfterRunSettlementPlan Plan,
    Sr5AfterRunReviewAcknowledgements Acknowledgements)
{
    [JsonIgnore]
    public CharacterAfterRunSettlementQuoteBinding Binding => Candidate.Binding;

    [JsonIgnore]
    public CharacterAfterRunSettlementQuote Quote => Candidate.Quote;

    [JsonIgnore]
    public CharacterWorkspaceId WorkspaceId => Binding.WorkspaceId;

    [JsonIgnore]
    public long ExpectedWorkspaceRevision => Binding.WorkspaceRevision;

    [JsonIgnore]
    public Sr5CareerActionPlan ActionPlan => Sr5CareerActionPlan.FromAfterRunSettlement(
        OwnerId,
        Binding,
        Plan,
        Candidate.RewardContext.ContextDigest);

    public CharacterAfterRunSettlementCommand ToCommand()
        => new(
            ContractName: CharacterAfterRunSettlementServiceSchemas.CommandV1,
            WorkspaceId: WorkspaceId,
            ExpectedWorkspaceRevision: ExpectedWorkspaceRevision,
            Identity: Quote.Identity,
            ExpectedSourceDigest: Quote.SourceDigest,
            ExpectedCustomDataDigest: Quote.CustomDataDigest,
            ExpectedGmPolicyDigest: Quote.GmPolicyDigest,
            ExpectedRuntimeDigest: Quote.RuntimeDigest,
            ExpectedLogicalDigest: Quote.LogicalDigest,
            ExpectedBindingDigest: Binding.BindingDigest,
            TransactionId: Plan.TransactionId,
            ExplicitlyConfirmed: true);

    public bool IsExact()
    {
        if (OwnerId == Guid.Empty
            || Candidate is null
            || Candidate.Binding is null
            || !Candidate.IsExact(
                Candidate.Binding.WorkspaceId,
                Candidate.Binding.WorkspaceRevision)
            || Acknowledgements is null
            || !Acknowledgements.AllReviewed
            || !Quote.CanSettle
            || !HasApprovedPrerequisite(
                CharacterAfterRunSettlementPrerequisite.GmApproved)
            || !HasApprovedPrerequisite(
                CharacterAfterRunSettlementPrerequisite.OwnerApproved)
            || !CharacterAfterRunSettlementRules.IsCoherent(Plan)
            || !CharacterAfterRunSettlementRules.TryCreatePlan(
                Quote,
                Quote.SourceDigest,
                Quote.CustomDataDigest,
                Quote.GmPolicyDigest,
                Quote.RuntimeDigest,
                Quote.LogicalDigest,
                explicitlyConfirmed: true,
                transactionIdAlreadyExists: false,
                Plan.TransactionId,
                out CharacterAfterRunSettlementPlan expectedPlan)
            || !PlansMatch(expectedPlan, Plan)
            || !CharacterAfterRunSettlementServiceIntegrity.TryComputeCommandDigest(
                ToCommand(),
                out _))
        {
            return false;
        }

        Sr5CareerActionPlan action = ActionPlan;
        return action.Kind == Sr5CareerActionKind.AfterRunSettlement
            && action.ActionId == Plan.TransactionId
            && action.OwnerId == OwnerId
            && action.WorkspaceId == WorkspaceId
            && action.ExpectedContentRevision == ExpectedWorkspaceRevision
            && string.Equals(
                action.RouteId,
                Sr5CareerWizardRoutes.AfterRunReview,
                StringComparison.Ordinal)
            && action.IdempotencyKey is { Length: 64 }
            && action.CostQuote.IsExact
            && action.CostQuote.KarmaCost == Quote.ContactKarmaCost
            && string.Equals(
                action.CostQuote.RuleDigest,
                Quote.GmPolicyDigest,
                StringComparison.Ordinal)
            && string.Equals(
                action.CostQuote.LogicalRevision,
                Quote.LogicalDigest,
                StringComparison.Ordinal);
    }

    public bool Matches(CharacterWorkspaceId? workspaceId, long revision)
        => workspaceId == WorkspaceId && revision == ExpectedWorkspaceRevision;

    public bool SemanticallyEquals(Sr5AfterRunSettlementDraft? other)
        => other is not null
            && IsExact()
            && other.IsExact()
            && OwnerId == other.OwnerId
            && WorkspaceId == other.WorkspaceId
            && ExpectedWorkspaceRevision == other.ExpectedWorkspaceRevision
            && Binding.Identity == other.Binding.Identity
            && string.Equals(
                Binding.BindingDigest,
                other.Binding.BindingDigest,
                StringComparison.Ordinal)
            && string.Equals(
                Candidate.RewardContext.ContextDigest,
                other.Candidate.RewardContext.ContextDigest,
                StringComparison.Ordinal)
            && Acknowledgements == other.Acknowledgements
            && PlansMatch(Plan, other.Plan)
            && string.Equals(
                ActionPlan.IdempotencyKey,
                other.ActionPlan.IdempotencyKey,
                StringComparison.Ordinal);

    public static bool TryCreate(
        Sr5AfterRunSettlementEditorState? editor,
        Sr5AfterRunSettlementCandidate? selected,
        Guid ownerId,
        Guid transactionId,
        Sr5AfterRunReviewAcknowledgements acknowledgements,
        out Sr5AfterRunSettlementDraft draft,
        out string blocker)
    {
        draft = null!;
        blocker = string.Empty;
        if (editor is null || !editor.IsExact()
            || editor.Status != Sr5AfterRunCatalogStatus.Available)
        {
            blocker = "The exact After Run proposal catalog is unavailable.";
            return false;
        }
        if (ownerId == Guid.Empty || transactionId == Guid.Empty)
        {
            blocker = "The local owner or transaction identity is unavailable.";
            return false;
        }
        if (acknowledgements is null || !acknowledgements.AllReviewed)
        {
            blocker = "Review the run, rewards, consequences, contacts, GM approval, and owner approval before confirmation.";
            return false;
        }
        if (selected is null)
        {
            blocker = "Choose one exact unsettled completed-run proposal.";
            return false;
        }

        Sr5AfterRunSettlementCandidate[] matches = editor.Candidates
            .Where(candidate => candidate.Binding.Identity == selected.Binding.Identity
                && string.Equals(
                    candidate.Binding.BindingDigest,
                    selected.Binding.BindingDigest,
                    StringComparison.Ordinal)
                && string.Equals(
                    candidate.RewardContext.ContextDigest,
                    selected.RewardContext.ContextDigest,
                    StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1
            || !matches[0].IsExact(editor.WorkspaceId, editor.WorkspaceRevision))
        {
            blocker = "The selected After Run proposal is foreign, ambiguous, stale, or incoherent.";
            return false;
        }

        CharacterAfterRunSettlementQuote quote = matches[0].Quote;
        if (!quote.CanSettle)
        {
            blocker = BlockerText(quote.Blocker);
            return false;
        }
        if (!CharacterAfterRunSettlementRules.TryCreatePlan(
                quote,
                quote.SourceDigest,
                quote.CustomDataDigest,
                quote.GmPolicyDigest,
                quote.RuntimeDigest,
                quote.LogicalDigest,
                explicitlyConfirmed: true,
                transactionIdAlreadyExists: false,
                transactionId,
                out CharacterAfterRunSettlementPlan plan))
        {
            blocker = "Core rejected the exact reviewed proposal, approvals, digests, confirmation, or transaction identity.";
            return false;
        }

        var candidateDraft = new Sr5AfterRunSettlementDraft(
            ownerId,
            matches[0],
            plan,
            acknowledgements);
        if (!candidateDraft.IsExact())
        {
            blocker = "The After Run proposal could not be bound to an exact typed action.";
            return false;
        }
        draft = candidateDraft;
        return true;
    }

    private bool HasApprovedPrerequisite(
        CharacterAfterRunSettlementPrerequisite prerequisite)
        => Quote.Prerequisites.Count(candidate =>
                candidate.Prerequisite == prerequisite && candidate.Satisfied) == 1;

    private static bool PlansMatch(
        CharacterAfterRunSettlementPlan left,
        CharacterAfterRunSettlementPlan right)
        => left.Identity == right.Identity
            && left.TransactionId == right.TransactionId
            && left.TargetHeat == right.TargetHeat
            && left.TargetStreetCred == right.TargetStreetCred
            && left.TargetNotoriety == right.TargetNotoriety
            && left.TargetPublicAwareness == right.TargetPublicAwareness
            && left.TargetKarma == right.TargetKarma
            && left.ContactKarmaCost == right.ContactKarmaCost
            && left.ContactsToAdd.SequenceEqual(right.ContactsToAdd)
            && left.ExpenseId == right.ExpenseId
            && left.ExpenseAmount == right.ExpenseAmount
            && string.Equals(left.ExpenseReason, right.ExpenseReason, StringComparison.Ordinal)
            && string.Equals(left.GmReviewDigest, right.GmReviewDigest, StringComparison.Ordinal)
            && string.Equals(left.OwnerReviewDigest, right.OwnerReviewDigest, StringComparison.Ordinal)
            && string.Equals(left.ExpectedSourceDigest, right.ExpectedSourceDigest, StringComparison.Ordinal)
            && string.Equals(left.ExpectedCustomDataDigest, right.ExpectedCustomDataDigest, StringComparison.Ordinal)
            && string.Equals(left.ExpectedGmPolicyDigest, right.ExpectedGmPolicyDigest, StringComparison.Ordinal)
            && string.Equals(left.ExpectedRuntimeDigest, right.ExpectedRuntimeDigest, StringComparison.Ordinal)
            && string.Equals(left.ExpectedLogicalDigest, right.ExpectedLogicalDigest, StringComparison.Ordinal)
            && string.Equals(left.PlanDigest, right.PlanDigest, StringComparison.Ordinal);

    public static string BlockerText(CharacterAfterRunSettlementBlocker blocker)
        => blocker switch
        {
            CharacterAfterRunSettlementBlocker.NotCareerCharacter =>
                "The proposal does not target a created Career character.",
            CharacterAfterRunSettlementBlocker.UnsupportedRuleset =>
                "The proposal is not governed by the SR5 settlement authority.",
            CharacterAfterRunSettlementBlocker.ForeignTarget =>
                "The proposal targets another character.",
            CharacterAfterRunSettlementBlocker.InexactProjection =>
                "The current runner projection is not exact.",
            CharacterAfterRunSettlementBlocker.RunNotCompleted =>
                "The run is not complete.",
            CharacterAfterRunSettlementBlocker.AlreadySettled =>
                "The proposal has already been settled.",
            CharacterAfterRunSettlementBlocker.GmReviewPending =>
                "The GM has not approved this exact proposal.",
            CharacterAfterRunSettlementBlocker.GmRejected =>
                "The GM rejected this proposal.",
            CharacterAfterRunSettlementBlocker.OwnerReviewPending =>
                "The character owner has not approved this exact proposal.",
            CharacterAfterRunSettlementBlocker.OwnerRejected =>
                "The character owner rejected this proposal.",
            CharacterAfterRunSettlementBlocker.HeatOutsidePolicy =>
                "The Heat delta is outside the active GM policy.",
            CharacterAfterRunSettlementBlocker.ReputationOutsidePolicy =>
                "The reputation delta is outside the active GM policy.",
            CharacterAfterRunSettlementBlocker.ContactOutsidePolicy =>
                "At least one contact proposal is outside the active policy.",
            CharacterAfterRunSettlementBlocker.InsufficientKarma =>
                "The runner cannot pay the Karma cost of the proposed contacts.",
            CharacterAfterRunSettlementBlocker.None => string.Empty,
            _ => "Core blocked this After Run settlement."
        };
}

public sealed record Sr5AfterRunSettlementCheckpoint(
    int SchemaVersion,
    long Version,
    string RouteId,
    Sr5CareerCheckpointPhase Phase,
    Sr5AfterRunSettlementDraft Draft,
    CharacterAfterRunSettlementReceipt? Receipt,
    string IdempotencyKey)
{
    public const int CurrentSchemaVersion = 1;

    public static Sr5AfterRunSettlementCheckpoint FromDraft(
        Sr5AfterRunSettlementDraft draft)
        => new(
            CurrentSchemaVersion,
            Version: 1,
            Sr5CareerWizardRoutes.AfterRunReview,
            Sr5CareerCheckpointPhase.Reviewed,
            draft,
            Receipt: null,
            draft.ActionPlan.IdempotencyKey);

    public bool IsStructurallyValid()
        => SchemaVersion == CurrentSchemaVersion
            && Version > 0
            && string.Equals(
                RouteId,
                Sr5CareerWizardRoutes.AfterRunReview,
                StringComparison.Ordinal)
            && Enum.IsDefined(Phase)
            && Draft is not null
            && Draft.IsExact()
            && (Phase == Sr5CareerCheckpointPhase.Applied
                ? Receipt is not null
                    && Sr5AfterRunSettlementCoordinator.ReceiptMatchesDraft(
                        Draft,
                        Receipt)
                : Receipt is null)
            && IdempotencyKey is { Length: 64 }
            && IdempotencyKey.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && string.Equals(
                IdempotencyKey,
                Draft.ActionPlan.IdempotencyKey,
                StringComparison.Ordinal);

    public bool MatchesActionDraft(Sr5AfterRunSettlementDraft draft)
        => IsStructurallyValid()
            && Draft.SemanticallyEquals(draft)
            && string.Equals(
                IdempotencyKey,
                draft.ActionPlan.IdempotencyKey,
                StringComparison.Ordinal);

    public bool TryResume(
        Sr5AfterRunSettlementEditorState? editor,
        out Sr5AfterRunSettlementDraft draft,
        out string blocker)
    {
        draft = null!;
        if (!IsStructurallyValid()
            || Phase != Sr5CareerCheckpointPhase.Reviewed)
        {
            blocker = "The saved After Run settlement is applying, applied, or uses an unsupported schema.";
            return false;
        }
        if (editor is null
            || !editor.IsExact()
            || editor.WorkspaceId != Draft.WorkspaceId
            || editor.WorkspaceRevision != Draft.ExpectedWorkspaceRevision)
        {
            blocker = "The saved After Run review belongs to another runner revision.";
            return false;
        }
        if (!Sr5AfterRunSettlementDraft.TryCreate(
                editor,
                Draft.Candidate,
                Draft.OwnerId,
                Draft.Plan.TransactionId,
                Draft.Acknowledgements,
                out Sr5AfterRunSettlementDraft resumed,
                out blocker)
            || !MatchesActionDraft(resumed))
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The saved After Run review no longer matches its exact proposal, approvals, and plan."
                : blocker;
            return false;
        }
        draft = resumed;
        return true;
    }
}

public enum Sr5AfterRunSettlementRecoveryStatus
{
    AppliedVerified,
    NotAppliedVerified,
    OutcomeUnknown
}

public sealed record Sr5AfterRunSettlementRecoveryResolution(
    Sr5AfterRunSettlementRecoveryStatus Status,
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long CheckpointVersion,
    CharacterAfterRunSettlementReceipt? Receipt,
    string Message,
    string AuthorityProof);

public enum Sr5AfterRunSettlementApplyStatus
{
    Applied,
    RejectedBeforeMutation,
    OutcomeUnknown
}

public sealed record Sr5AfterRunSettlementApplyResult(
    Sr5AfterRunSettlementApplyStatus Status,
    Sr5CareerActionPlan ActionPlan,
    long? SavedWorkspaceRevision,
    CharacterAfterRunSettlementReceipt? Receipt,
    Sr5AfterRunSettlementRecoveryResolution Resolution,
    string Message);

internal static class Sr5AfterRunSettlementRecoveryProof
{
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

    public static Sr5AfterRunSettlementRecoveryResolution Create(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementRecoveryStatus status,
        CharacterAfterRunSettlementReceipt? receipt,
        string message)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(
            status,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.TransactionId,
            checkpoint.Version,
            receipt,
            message,
            Sign(checkpoint, status, receipt, message));
    }

    public static bool Verifies(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementRecoveryResolution resolution)
    {
        if (resolution.AuthorityProof is not { Length: 64 })
        {
            return false;
        }
        try
        {
            byte[] expected = Convert.FromHexString(Sign(
                checkpoint,
                resolution.Status,
                resolution.Receipt,
                resolution.Message));
            byte[] actual = Convert.FromHexString(resolution.AuthorityProof);
            return expected.Length == actual.Length
                && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Sign(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementRecoveryStatus status,
        CharacterAfterRunSettlementReceipt? receipt,
        string message)
        => Convert.ToHexStringLower(HMACSHA256.HashData(
            ProcessKey,
            Encoding.UTF8.GetBytes(string.Join(
                "\n",
                JsonSerializer.Serialize(checkpoint),
                status.ToString(),
                JsonSerializer.Serialize(receipt),
                message))));
}

/// <summary>
/// Proof schema only. It cannot become a pass without an exact committed run
/// fixture, physical API-36 device, saved-workspace delta, process restart, and
/// the digest-bound Core receipt from that same transaction.
/// </summary>
public sealed record Sr5AfterRunSettlementPhysicalProofContract(
    string FixtureSha256,
    string DeviceSerial,
    int ApiLevel,
    string BuildSha256,
    Guid RunId,
    Guid ProposalId,
    Guid TransactionId,
    string BeforeWorkspaceSha256,
    string AfterWorkspaceSha256,
    string CoreReceiptSha256,
    string EvidenceArtifactSha256,
    int ProcessIdBeforeRestart,
    int ProcessIdAfterRestart,
    IReadOnlyList<string> VisitedRoutes)
{
    public const string ContractName =
        "chummer.android.sr5-after-run-settlement-physical-proof/v1";

    public static IReadOnlyList<string> RequiredRoutes { get; } =
    [
        Sr5CareerWizardRoutes.AfterRunChoose,
        Sr5CareerWizardRoutes.AfterRunRewards,
        Sr5CareerWizardRoutes.AfterRunConsequences,
        Sr5CareerWizardRoutes.AfterRunContacts,
        Sr5CareerWizardRoutes.AfterRunGmReview,
        Sr5CareerWizardRoutes.AfterRunOwnerReview,
        Sr5CareerWizardRoutes.AfterRunReview,
        Sr5CareerWizardRoutes.AfterRunReceipt
    ];

    public bool IsSatisfied()
        => IsDigest(FixtureSha256)
            && ApiLevel >= 36
            && !string.IsNullOrWhiteSpace(DeviceSerial)
            && IsDigest(BuildSha256)
            && RunId != Guid.Empty
            && ProposalId != Guid.Empty
            && TransactionId != Guid.Empty
            && IsDigest(BeforeWorkspaceSha256)
            && IsDigest(AfterWorkspaceSha256)
            && !string.Equals(
                BeforeWorkspaceSha256,
                AfterWorkspaceSha256,
                StringComparison.Ordinal)
            && IsDigest(CoreReceiptSha256)
            && IsDigest(EvidenceArtifactSha256)
            && ProcessIdBeforeRestart > 0
            && ProcessIdAfterRestart > 0
            && ProcessIdBeforeRestart != ProcessIdAfterRestart
            && VisitedRoutes is not null
            && RequiredRoutes.All(VisitedRoutes.Contains);

    private static bool IsDigest(string value)
        => value is { Length: 64 }
            && value.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
