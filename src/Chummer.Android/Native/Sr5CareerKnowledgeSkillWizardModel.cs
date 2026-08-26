using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// A reviewed SR5 knowledge skill advancement. The draft retains the exact typed
/// Core quote and plan; Android never rebuilds costs or legality itself.
/// </summary>
public sealed record Sr5CareerKnowledgeSkillDraft(
    Guid OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterCareerKnowledgeSkillAdvanceQuote Quote,
    CharacterCareerKnowledgeSkillAdvancePlan Plan)
{
    public Sr5CareerActionPlan ActionPlan
        => Sr5CareerActionPlan.FromKnowledgeSkill(
            OwnerId,
            WorkspaceId,
            ExpectedContentRevision,
            Quote,
            Plan);

    public CareerKnowledgeSkillAdvanceRequest ToRequest()
        => new(
            WorkspaceId,
            ExpectedContentRevision,
            Quote,
            Quote.CharacterRevision,
            Quote.LogicalRevision,
            Quote.SourceRevision,
            Quote.RuleDigest,
            Confirmed: true,
            Plan.ExpenseId,
            Plan.ExpenseDateLocal);

    public bool Matches(CharacterWorkspaceId? workspaceId, long contentRevision)
        => workspaceId == WorkspaceId && contentRevision == ExpectedContentRevision;

    public bool IsExact()
    {
        if (OwnerId == Guid.Empty
            || string.IsNullOrWhiteSpace(WorkspaceId.Value)
            || ExpectedContentRevision <= 0
            || !CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(Quote)
            || !Quote.CanAdvance
            || !CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(Plan)
            || Quote.Identity != Plan.Identity
            || !CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
                Quote,
                Quote.CharacterRevision,
                Quote.LogicalRevision,
                Quote.SourceRevision,
                Quote.RuleDigest,
                confirmed: true,
                Plan.ExpenseId,
                Plan.ExpenseDateLocal,
                out CharacterCareerKnowledgeSkillAdvancePlan expected)
            || expected != Plan)
        {
            return false;
        }

        Sr5CareerActionPlan action = ActionPlan;
        return action.OwnerId == OwnerId
            && action.ActionId == Plan.ExpenseId
            && action.Kind == Sr5CareerActionKind.KnowledgeSkillAdvance
            && action.WorkspaceId == WorkspaceId
            && action.ExpectedContentRevision == ExpectedContentRevision
            && string.Equals(
                action.RouteId,
                Sr5CareerWizardRoutes.KnowledgeSkillReview,
                StringComparison.Ordinal)
            && string.Equals(
                action.DomainIdentity,
                $"{Quote.Identity.SkillId:D}:{Quote.Identity.SourceSkillId?.ToString("D") ?? "custom"}",
                StringComparison.Ordinal)
            && action.CostQuote.IsExact
            && action.CostQuote.KarmaCost == Quote.KarmaCost
            && action.CostQuote.NuyenCost == 0m
            && action.CostQuote.EssenceCost == 0m
            && action.CostQuote.Availability is null
            && action.CostQuote.ElapsedTime == Quote.ApplicationDuration
            && string.Equals(action.CostQuote.RuleDigest, Quote.RuleDigest, StringComparison.Ordinal)
            && string.Equals(action.CostQuote.LogicalRevision, Quote.LogicalRevision, StringComparison.Ordinal)
            && string.IsNullOrEmpty(action.CostQuote.Blocker);
    }

    public static bool TryCreate(
        CareerKnowledgeSkillAdvanceEditorState? editor,
        CharacterCareerKnowledgeSkillAdvanceQuote? selected,
        Guid ownerId,
        Guid expenseId,
        DateTime expenseDateLocal,
        out Sr5CareerKnowledgeSkillDraft draft,
        out string blocker)
    {
        draft = null!;
        blocker = string.Empty;
        if (editor is null
            || string.IsNullOrWhiteSpace(editor.WorkspaceId.Value)
            || editor.ContentRevision <= 0)
        {
            blocker = "The runner identity or revision is unavailable. Reopen knowledge skill advancement.";
            return false;
        }
        if (ownerId == Guid.Empty)
        {
            blocker = "The local Career owner identity is unavailable. Reopen knowledge skill advancement.";
            return false;
        }
        if (selected is null)
        {
            blocker = "Choose an exact saved knowledge skill.";
            return false;
        }

        CharacterCareerKnowledgeSkillAdvanceQuote[] matches = editor.Skills
            .Where(candidate => candidate.Identity == selected.Identity
                && string.Equals(candidate.CharacterRevision, selected.CharacterRevision, StringComparison.Ordinal)
                && string.Equals(candidate.LogicalRevision, selected.LogicalRevision, StringComparison.Ordinal)
                && string.Equals(candidate.SourceRevision, selected.SourceRevision, StringComparison.Ordinal)
                && string.Equals(candidate.RuleDigest, selected.RuleDigest, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1
            || !CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(matches[0]))
        {
            blocker = "The selected knowledge skill quote is foreign, ambiguous, stale, or incoherent.";
            return false;
        }

        CharacterCareerKnowledgeSkillAdvanceQuote authoritative = matches[0];
        if (!authoritative.CanAdvance)
        {
            blocker = BlockerText(authoritative.Blocker);
            return false;
        }

        DateTime serializedExpenseDate = DateTime.SpecifyKind(
            new DateTime(
                expenseDateLocal.Year,
                expenseDateLocal.Month,
                expenseDateLocal.Day,
                expenseDateLocal.Hour,
                expenseDateLocal.Minute,
                expenseDateLocal.Second),
            DateTimeKind.Unspecified);
        if (!CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
                authoritative,
                authoritative.CharacterRevision,
                authoritative.LogicalRevision,
                authoritative.SourceRevision,
                authoritative.RuleDigest,
                confirmed: true,
                expenseId,
                serializedExpenseDate,
                out CharacterCareerKnowledgeSkillAdvancePlan plan))
        {
            blocker = "Core rejected the confirmed expense identity, date, quote, or rule revisions.";
            return false;
        }

        Sr5CareerKnowledgeSkillDraft candidate = new(
            ownerId,
            editor.WorkspaceId,
            editor.ContentRevision,
            authoritative,
            plan);
        if (!candidate.IsExact())
        {
            blocker = "The exact SR5 CostQuote and CareerActionPlan could not be bound.";
            return false;
        }

        draft = candidate;
        return true;
    }

    public static string BlockerText(CharacterCareerKnowledgeSkillAdvanceBlocker blocker)
        => blocker switch
        {
            CharacterCareerKnowledgeSkillAdvanceBlocker.NotCareerCharacter =>
                "Knowledge/Language advancement is available only after character creation.",
            CharacterCareerKnowledgeSkillAdvanceBlocker.UnsupportedRuleset =>
                "This quote is not governed by the SR5 knowledge skill authority.",
            CharacterCareerKnowledgeSkillAdvanceBlocker.NotKnowledgeSkill =>
                "The selected row is not a Knowledge or Language skill.",
            CharacterCareerKnowledgeSkillAdvanceBlocker.ForeignIdentity =>
                "The selected knowledge skill does not have one exact saved identity.",
            CharacterCareerKnowledgeSkillAdvanceBlocker.UpgradeDisallowed =>
                "Upgrades are disabled for this knowledge skill.",
            CharacterCareerKnowledgeSkillAdvanceBlocker.NativeLanguage =>
                "A native language has no Karma rating to advance.",
            CharacterCareerKnowledgeSkillAdvanceBlocker.AtMaximum =>
                "This knowledge skill is already at its exact maximum.",
            CharacterCareerKnowledgeSkillAdvanceBlocker.InsufficientKarma =>
                "The runner does not have enough Karma for this advancement.",
            CharacterCareerKnowledgeSkillAdvanceBlocker.None => string.Empty,
            _ => "Core blocked this knowledge skill advancement."
        };
}

public sealed record Sr5CareerKnowledgeSkillCheckpoint(
    int SchemaVersion,
    long Version,
    string RouteId,
    Sr5CareerCheckpointPhase Phase,
    Sr5CareerKnowledgeSkillDraft Draft,
    string IdempotencyKey)
{
    public const int CurrentSchemaVersion = 1;

    public static Sr5CareerKnowledgeSkillCheckpoint FromDraft(
        Sr5CareerKnowledgeSkillDraft draft,
        Sr5CareerCheckpointPhase phase = Sr5CareerCheckpointPhase.Reviewed)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new(
            CurrentSchemaVersion,
            Version: 1,
            Sr5CareerWizardRoutes.KnowledgeSkillReview,
            phase,
            draft,
            draft.ActionPlan.IdempotencyKey);
    }

    public bool IsStructurallyValid()
        => SchemaVersion == CurrentSchemaVersion
            && Version > 0
            && string.Equals(RouteId, Sr5CareerWizardRoutes.KnowledgeSkillReview, StringComparison.Ordinal)
            && Enum.IsDefined(Phase)
            && Draft is not null
            && Draft.IsExact()
            && IdempotencyKey is { Length: 64 }
            && IdempotencyKey.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && string.Equals(
                IdempotencyKey,
                Draft.ActionPlan.IdempotencyKey,
                StringComparison.Ordinal);

    public bool TryResume(
        CareerKnowledgeSkillAdvanceEditorState? editor,
        out Sr5CareerKnowledgeSkillDraft draft,
        out string blocker)
    {
        draft = null!;
        if (!IsStructurallyValid()
            || Phase != Sr5CareerCheckpointPhase.Reviewed)
        {
            blocker = "The saved knowledge skill action is applying, applied, or uses an unsupported schema.";
            return false;
        }
        if (editor is null
            || editor.WorkspaceId != Draft.WorkspaceId
            || editor.ContentRevision != Draft.ExpectedContentRevision)
        {
            blocker = "The saved knowledge skill review belongs to another runner revision.";
            return false;
        }
        if (!Sr5CareerKnowledgeSkillDraft.TryCreate(
                editor,
                Draft.Quote,
                Draft.OwnerId,
                Draft.Plan.ExpenseId,
                Draft.Plan.ExpenseDateLocal,
                out Sr5CareerKnowledgeSkillDraft resumed,
                out blocker)
            || !MatchesActionDraft(resumed))
        {
            draft = null!;
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The saved knowledge skill review no longer matches its exact quote and plan."
                : blocker;
            return false;
        }

        draft = resumed;
        return true;
    }

    public bool MatchesActionDraft(Sr5CareerKnowledgeSkillDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return IsStructurallyValid()
            && Draft.OwnerId == draft.OwnerId
            && Draft.WorkspaceId == draft.WorkspaceId
            && Draft.ExpectedContentRevision == draft.ExpectedContentRevision
            && Draft.Quote.Identity == draft.Quote.Identity
            && string.Equals(Draft.Quote.CharacterRevision, draft.Quote.CharacterRevision, StringComparison.Ordinal)
            && string.Equals(Draft.Quote.LogicalRevision, draft.Quote.LogicalRevision, StringComparison.Ordinal)
            && string.Equals(Draft.Quote.SourceRevision, draft.Quote.SourceRevision, StringComparison.Ordinal)
            && string.Equals(Draft.Quote.RuleDigest, draft.Quote.RuleDigest, StringComparison.Ordinal)
            && Draft.Plan == draft.Plan
            && string.Equals(IdempotencyKey, draft.ActionPlan.IdempotencyKey, StringComparison.Ordinal);
    }
}

public enum Sr5CareerKnowledgeSkillRecoveryStatus
{
    AppliedVerified,
    NotAppliedVerified,
    OutcomeUnknown
}

public sealed record Sr5CareerKnowledgeSkillRecoveryResolution(
    Sr5CareerKnowledgeSkillRecoveryStatus Status,
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long CheckpointVersion,
    CharacterCareerKnowledgeSkillAdvanceReceipt? Receipt,
    string Message,
    string AuthorityProof);

public enum Sr5CareerKnowledgeSkillApplyStatus
{
    Applied,
    RejectedBeforeMutation,
    OutcomeUnknown
}

public sealed record Sr5CareerKnowledgeSkillApplyResult(
    Sr5CareerKnowledgeSkillApplyStatus Status,
    Sr5CareerActionPlan ActionPlan,
    long? SavedContentRevision,
    CharacterCareerKnowledgeSkillAdvanceReceipt? Receipt,
    Sr5CareerKnowledgeSkillRecoveryResolution Resolution,
    string Message);
