using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// One reviewed specialization purchase. The complete Core quote and plan are
/// retained; labels shown by Android are never used as saved identity.
/// </summary>
public sealed record Sr5CareerSpecializationDraft(
    Guid OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterCareerSkillSpecializationQuote Quote,
    CharacterCareerSkillSpecializationPlan Plan)
{
    [JsonIgnore]
    public Sr5CareerActionPlan ActionPlan => Sr5CareerActionPlan.FromSpecialization(
        OwnerId,
        WorkspaceId,
        ExpectedContentRevision,
        Quote,
        Plan);

    public CareerSkillSpecializationRequest ToRequest()
        => new(
            WorkspaceId,
            ExpectedContentRevision,
            Quote,
            Quote.CharacterRevision,
            Quote.SourceRevision,
            Quote.RuleDigest,
            Quote.LogicalRevision,
            Confirmed: true,
            Plan.SpecializationId,
            Plan.ExpenseId,
            Plan.ExpenseDateLocal);

    public bool Matches(CharacterWorkspaceId? workspaceId, long contentRevision)
        => workspaceId == WorkspaceId && contentRevision == ExpectedContentRevision;

    public bool IsExact()
    {
        if (OwnerId == Guid.Empty
            || string.IsNullOrWhiteSpace(WorkspaceId.Value)
            || ExpectedContentRevision <= 0
            || !CharacterCareerSkillSpecializationRules.IsCoherent(Quote)
            || !Quote.CanAdd
            || !CharacterCareerSkillSpecializationRules.TryPlanAdd(
                Quote,
                Quote.CharacterRevision,
                Quote.SourceRevision,
                Quote.RuleDigest,
                Quote.LogicalRevision,
                confirmed: true,
                Plan.SpecializationId,
                Plan.ExpenseId,
                Plan.ExpenseDateLocal,
                out CharacterCareerSkillSpecializationPlan expected)
            || expected != Plan)
        {
            return false;
        }

        Sr5CareerActionPlan action = ActionPlan;
        string sourceIdentity = Quote.Identity.SourceSkillId?.ToString("D") ?? "custom";
        return action.OwnerId == OwnerId
            && action.ActionId == Plan.ExpenseId
            && action.Kind == Sr5CareerActionKind.SkillSpecializationAdd
            && action.WorkspaceId == WorkspaceId
            && action.ExpectedContentRevision == ExpectedContentRevision
            && string.Equals(action.RouteId, Sr5CareerWizardRoutes.SpecializationReview, StringComparison.Ordinal)
            && string.Equals(
                action.DomainIdentity,
                $"{Quote.Identity.Kind}:{Quote.Identity.SkillId:D}:{sourceIdentity}",
                StringComparison.Ordinal)
            && action.IdempotencyKey is { Length: 64 }
            && action.CostQuote.IsExact
            && action.CostQuote.KarmaCost == Quote.KarmaCost
            && string.Equals(action.CostQuote.RuleDigest, Quote.RuleDigest, StringComparison.Ordinal)
            && string.Equals(action.CostQuote.LogicalRevision, Quote.LogicalRevision, StringComparison.Ordinal);
    }

    public static bool TryCreate(
        CareerSkillSpecializationEditorState? editor,
        CharacterCareerSkillSpecializationQuote? quote,
        Guid ownerId,
        Guid specializationId,
        Guid expenseId,
        DateTime expenseDateLocal,
        out Sr5CareerSpecializationDraft draft,
        out string blocker)
    {
        draft = null!;
        blocker = string.Empty;
        if (editor is null
            || string.IsNullOrWhiteSpace(editor.WorkspaceId.Value)
            || editor.ContentRevision <= 0)
        {
            blocker = "The exact runner identity or revision is unavailable.";
            return false;
        }
        if (ownerId == Guid.Empty || quote is null)
        {
            blocker = "Choose and quote one exact specialization for the authenticated local owner.";
            return false;
        }

        CareerSkillSpecializationCandidate[] candidates = editor.Skills
            .Where(candidate => CandidateMatchesQuote(candidate, quote))
            .Take(2)
            .ToArray();
        if (candidates.Length != 1
            || !CharacterCareerSkillSpecializationRules.IsCoherent(quote)
            || !quote.CanAdd)
        {
            blocker = quote is { CanAdd: false }
                ? BlockerText(quote.Blocker)
                : "The specialization quote is foreign, ambiguous, stale, or incoherent.";
            return false;
        }

        DateTime serializedDate = DateTime.SpecifyKind(
            new DateTime(
                expenseDateLocal.Year,
                expenseDateLocal.Month,
                expenseDateLocal.Day,
                expenseDateLocal.Hour,
                expenseDateLocal.Minute,
                expenseDateLocal.Second),
            DateTimeKind.Unspecified);
        if (!CharacterCareerSkillSpecializationRules.TryPlanAdd(
                quote,
                quote.CharacterRevision,
                quote.SourceRevision,
                quote.RuleDigest,
                quote.LogicalRevision,
                confirmed: true,
                specializationId,
                expenseId,
                serializedDate,
                out CharacterCareerSkillSpecializationPlan plan))
        {
            blocker = "Core rejected the confirmation, identities, date, or four-revision quote.";
            return false;
        }

        Sr5CareerSpecializationDraft candidateDraft = new(
            ownerId,
            editor.WorkspaceId,
            editor.ContentRevision,
            quote,
            plan);
        if (!candidateDraft.IsExact())
        {
            blocker = "The exact SR5 CostQuote and CareerActionPlan could not be bound.";
            return false;
        }
        draft = candidateDraft;
        return true;
    }

    internal static bool CandidateMatchesQuote(
        CareerSkillSpecializationCandidate candidate,
        CharacterCareerSkillSpecializationQuote quote)
    {
        bool selectionExists = quote.Selection.Kind
            == CharacterCareerSkillSpecializationOptionKind.Custom
                ? quote.Selection.OptionIdentity is null
                    && !string.IsNullOrWhiteSpace(quote.Selection.Name)
                : candidate.AvailableOptions.Count(option =>
                    string.Equals(option.OptionIdentity, quote.Selection.OptionIdentity, StringComparison.Ordinal)
                    && string.Equals(option.Name, quote.Selection.Name, StringComparison.Ordinal)
                    && option.Kind == quote.Selection.Kind) == 1;
        return candidate.Identity == quote.Identity
            && string.Equals(candidate.SkillName, quote.SkillName, StringComparison.Ordinal)
            && string.Equals(candidate.SkillCategory, quote.SkillCategory, StringComparison.Ordinal)
            && string.Equals(candidate.SkillGroup, quote.SkillGroup, StringComparison.Ordinal)
            && candidate.TotalBaseRating == quote.TotalBaseRating
            && candidate.ExistingSpecializationCount == quote.ExistingSpecializationCount
            && selectionExists;
    }

    public static string BlockerText(CharacterCareerSkillSpecializationBlocker blocker)
        => blocker switch
        {
            CharacterCareerSkillSpecializationBlocker.NativeLanguage =>
                "Native languages cannot receive a purchased specialization.",
            CharacterCareerSkillSpecializationBlocker.UpgradeDisallowed =>
                "Upgrades are disabled for this knowledge skill.",
            CharacterCareerSkillSpecializationBlocker.SkillDisabled =>
                "This skill is disabled by the active rule environment.",
            CharacterCareerSkillSpecializationBlocker.ExoticSkill =>
                "Exotic skills use their own specific skill identity and cannot add a specialization here.",
            CharacterCareerSkillSpecializationBlocker.KarmaLocked =>
                "Karma advancement is locked for this skill.",
            CharacterCareerSkillSpecializationBlocker.RatingRequired =>
                "The skill needs a positive rating before adding a specialization.",
            CharacterCareerSkillSpecializationBlocker.SkillSpecializationsBlocked =>
                "Specializations are blocked for this skill.",
            CharacterCareerSkillSpecializationBlocker.SkillCategorySpecializationsBlocked =>
                "Specializations are blocked for this skill category.",
            CharacterCareerSkillSpecializationBlocker.InsufficientKarma =>
                "The runner does not have enough Karma.",
            CharacterCareerSkillSpecializationBlocker.None => string.Empty,
            _ => "Core blocked this specialization purchase."
        };
}

public sealed record Sr5CareerSpecializationCheckpoint(
    int SchemaVersion,
    long Version,
    string RouteId,
    Sr5CareerCheckpointPhase Phase,
    Sr5CareerSpecializationDraft Draft,
    string IdempotencyKey)
{
    public const int CurrentSchemaVersion = 1;

    public static Sr5CareerSpecializationCheckpoint FromDraft(Sr5CareerSpecializationDraft draft)
        => new(
            CurrentSchemaVersion,
            Version: 1,
            Sr5CareerWizardRoutes.SpecializationReview,
            Sr5CareerCheckpointPhase.Reviewed,
            draft,
            draft.ActionPlan.IdempotencyKey);

    public bool IsStructurallyValid()
        => SchemaVersion == CurrentSchemaVersion
            && Version > 0
            && string.Equals(RouteId, Sr5CareerWizardRoutes.SpecializationReview, StringComparison.Ordinal)
            && Enum.IsDefined(Phase)
            && Draft is not null
            && Draft.IsExact()
            && IdempotencyKey is { Length: 64 }
            && IdempotencyKey.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            && string.Equals(IdempotencyKey, Draft.ActionPlan.IdempotencyKey, StringComparison.Ordinal);

    public bool MatchesActionDraft(Sr5CareerSpecializationDraft draft)
        => IsStructurallyValid()
            && Draft == draft
            && string.Equals(IdempotencyKey, draft.ActionPlan.IdempotencyKey, StringComparison.Ordinal);
}

public enum Sr5CareerSpecializationRecoveryStatus
{
    AppliedVerifiedInCurrentProcess,
    NotAppliedVerified,
    OutcomeUnknown
}

/// <summary>
/// This receipt is deliberately Android/process scoped. Core currently has no
/// persisted specialization receipt authority, so it can never resolve a
/// restarted Applying checkpoint.
/// </summary>
public sealed record Sr5CareerSpecializationReceipt(
    string WorkspaceId,
    long SavedContentRevision,
    Guid OwnerId,
    Guid ActionId,
    CharacterCareerSkillIdentity SkillIdentity,
    Guid SpecializationId,
    Guid ExpenseId,
    string SpecializationName,
    int SpecializationCountBefore,
    int SpecializationCountAfter,
    int KarmaBefore,
    int KarmaAfter,
    string ReviewedCharacterRevision,
    string ReviewedSourceRevision,
    string ReviewedRuleDigest,
    string ReviewedLogicalRevision,
    string ProcessProof);

public sealed record Sr5CareerSpecializationResolution(
    Sr5CareerSpecializationRecoveryStatus Status,
    Sr5CareerSpecializationReceipt? Receipt,
    string Message,
    string ProcessProof);

public sealed record Sr5CareerSpecializationApplyResult(
    Sr5CareerSpecializationRecoveryStatus Status,
    Sr5CareerActionPlan ActionPlan,
    Sr5CareerSpecializationReceipt? Receipt,
    string Message);

/// <summary>
/// Contract only; it does not claim a device run. Release proof must populate
/// every evidence field from a physical API-36 session before publication.
/// </summary>
public sealed record Sr5CareerSpecializationPhysicalProofContract(
    string DeviceSerial,
    int ApiLevel,
    string BuildSha256,
    string BeforeWorkspaceSha256,
    string AfterWorkspaceSha256,
    string EvidenceArtifactSha256)
{
    public const string ContractName = "chummer.android.sr5-specialization-physical-proof/v1";
    public static IReadOnlyList<string> RequiredRoutes { get; } =
    [
        Sr5CareerWizardRoutes.SpecializationChoose,
        Sr5CareerWizardRoutes.SpecializationConfigure,
        Sr5CareerWizardRoutes.SpecializationReview,
        Sr5CareerWizardRoutes.SpecializationReceipt
    ];

    public bool IsSatisfied()
        => ApiLevel >= 36
            && !string.IsNullOrWhiteSpace(DeviceSerial)
            && IsDigest(BuildSha256)
            && IsDigest(BeforeWorkspaceSha256)
            && IsDigest(AfterWorkspaceSha256)
            && IsDigest(EvidenceArtifactSha256)
            && !string.Equals(BeforeWorkspaceSha256, AfterWorkspaceSha256, StringComparison.Ordinal);

    private static bool IsDigest(string value)
        => value is { Length: 64 }
            && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
