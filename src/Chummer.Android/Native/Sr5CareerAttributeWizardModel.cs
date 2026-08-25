using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// A reviewed SR5 attribute advancement. The draft retains the exact typed
/// Core quote and plan; Android never rebuilds costs or legality itself.
/// </summary>
public sealed record Sr5CareerAttributeDraft(
    Guid OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterCareerAttributeAdvanceQuote Quote,
    CharacterCareerAttributeAdvancePlan Plan)
{
    public Sr5CareerActionPlan ActionPlan
        => Sr5CareerActionPlan.FromAttribute(
            OwnerId,
            WorkspaceId,
            ExpectedContentRevision,
            Quote,
            Plan);

    public CareerAttributeAdvanceRequest ToRequest()
        => new(
            WorkspaceId,
            ExpectedContentRevision,
            Quote,
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
            || !CharacterCareerAttributeAdvanceRules.IsCoherent(Quote)
            || !Quote.CanAdvance
            || !CharacterCareerAttributeAdvanceRules.IsCoherent(Plan)
            || Quote.Identity != Plan.Identity
            || !CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
                Quote,
                Quote.LogicalRevision,
                Quote.SourceRevision,
                Quote.RuleDigest,
                confirmed: true,
                Plan.ExpenseId,
                Plan.ExpenseDateLocal,
                out CharacterCareerAttributeAdvancePlan expected)
            || expected != Plan)
        {
            return false;
        }

        Sr5CareerActionPlan action = ActionPlan;
        return action.OwnerId == OwnerId
            && action.ActionId == Plan.ExpenseId
            && action.Kind == Sr5CareerActionKind.AttributeAdvance
            && action.WorkspaceId == WorkspaceId
            && action.ExpectedContentRevision == ExpectedContentRevision
            && string.Equals(
                action.RouteId,
                Sr5CareerWizardRoutes.AttributeReview,
                StringComparison.Ordinal)
            && string.Equals(
                action.DomainIdentity,
                $"{Quote.Identity.Abbreviation}:{Quote.Identity.Kind}",
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
        CareerAttributeAdvanceEditorState? editor,
        CharacterCareerAttributeAdvanceQuote? selected,
        Guid ownerId,
        Guid expenseId,
        DateTime expenseDateLocal,
        out Sr5CareerAttributeDraft draft,
        out string blocker)
    {
        draft = null!;
        blocker = string.Empty;
        if (editor is null
            || string.IsNullOrWhiteSpace(editor.WorkspaceId.Value)
            || editor.ContentRevision <= 0)
        {
            blocker = "The runner identity or revision is unavailable. Reopen attribute advancement.";
            return false;
        }
        if (ownerId == Guid.Empty)
        {
            blocker = "The local Career owner identity is unavailable. Reopen attribute advancement.";
            return false;
        }
        if (selected is null)
        {
            blocker = "Choose an exact saved attribute.";
            return false;
        }

        CharacterCareerAttributeAdvanceQuote[] matches = editor.Attributes
            .Where(candidate => candidate.Identity == selected.Identity
                && string.Equals(candidate.LogicalRevision, selected.LogicalRevision, StringComparison.Ordinal)
                && string.Equals(candidate.SourceRevision, selected.SourceRevision, StringComparison.Ordinal)
                && string.Equals(candidate.RuleDigest, selected.RuleDigest, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1
            || !CharacterCareerAttributeAdvanceRules.IsCoherent(matches[0]))
        {
            blocker = "The selected attribute quote is foreign, ambiguous, stale, or incoherent.";
            return false;
        }

        CharacterCareerAttributeAdvanceQuote authoritative = matches[0];
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
        if (!CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
                authoritative,
                authoritative.LogicalRevision,
                authoritative.SourceRevision,
                authoritative.RuleDigest,
                confirmed: true,
                expenseId,
                serializedExpenseDate,
                out CharacterCareerAttributeAdvancePlan plan))
        {
            blocker = "Core rejected the confirmed expense identity, date, quote, or rule revisions.";
            return false;
        }

        Sr5CareerAttributeDraft candidate = new(
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

    public static string BlockerText(CharacterCareerAttributeAdvanceBlocker blocker)
        => blocker switch
        {
            CharacterCareerAttributeAdvanceBlocker.NotCareerCharacter =>
                "Attribute advancement is available only after character creation.",
            CharacterCareerAttributeAdvanceBlocker.UnsupportedRuleset =>
                "This quote is not governed by the SR5 attribute authority.",
            CharacterCareerAttributeAdvanceBlocker.ForeignTarget =>
                "The selected attribute is not an exact SR5 attribute identity.",
            CharacterCareerAttributeAdvanceBlocker.SpecialAttributeDisabled =>
                "This special attribute is not enabled for the current runner.",
            CharacterCareerAttributeAdvanceBlocker.AtNaturalMaximum =>
                "This attribute is already at its exact natural maximum.",
            CharacterCareerAttributeAdvanceBlocker.InsufficientKarma =>
                "The runner does not have enough Karma for this advancement.",
            CharacterCareerAttributeAdvanceBlocker.None => string.Empty,
            _ => "Core blocked this attribute advancement."
        };
}

public sealed record Sr5CareerAttributeCheckpoint(
    int SchemaVersion,
    long Version,
    string RouteId,
    Sr5CareerCheckpointPhase Phase,
    Sr5CareerAttributeDraft Draft,
    string IdempotencyKey)
{
    public const int CurrentSchemaVersion = 1;

    public static Sr5CareerAttributeCheckpoint FromDraft(
        Sr5CareerAttributeDraft draft,
        Sr5CareerCheckpointPhase phase = Sr5CareerCheckpointPhase.Reviewed)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new(
            CurrentSchemaVersion,
            Version: 1,
            Sr5CareerWizardRoutes.AttributeReview,
            phase,
            draft,
            draft.ActionPlan.IdempotencyKey);
    }

    public bool IsStructurallyValid()
        => SchemaVersion == CurrentSchemaVersion
            && Version > 0
            && string.Equals(RouteId, Sr5CareerWizardRoutes.AttributeReview, StringComparison.Ordinal)
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
        CareerAttributeAdvanceEditorState? editor,
        out Sr5CareerAttributeDraft draft,
        out string blocker)
    {
        draft = null!;
        if (!IsStructurallyValid()
            || Phase != Sr5CareerCheckpointPhase.Reviewed)
        {
            blocker = "The saved attribute action is applying, applied, or uses an unsupported schema.";
            return false;
        }
        if (editor is null
            || editor.WorkspaceId != Draft.WorkspaceId
            || editor.ContentRevision != Draft.ExpectedContentRevision)
        {
            blocker = "The saved attribute review belongs to another runner revision.";
            return false;
        }
        if (!Sr5CareerAttributeDraft.TryCreate(
                editor,
                Draft.Quote,
                Draft.OwnerId,
                Draft.Plan.ExpenseId,
                Draft.Plan.ExpenseDateLocal,
                out Sr5CareerAttributeDraft resumed,
                out blocker)
            || !MatchesActionDraft(resumed))
        {
            draft = null!;
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The saved attribute review no longer matches its exact quote and plan."
                : blocker;
            return false;
        }

        draft = resumed;
        return true;
    }

    public bool MatchesActionDraft(Sr5CareerAttributeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return IsStructurallyValid()
            && Draft.OwnerId == draft.OwnerId
            && Draft.WorkspaceId == draft.WorkspaceId
            && Draft.ExpectedContentRevision == draft.ExpectedContentRevision
            && Draft.Quote.Identity == draft.Quote.Identity
            && string.Equals(Draft.Quote.LogicalRevision, draft.Quote.LogicalRevision, StringComparison.Ordinal)
            && string.Equals(Draft.Quote.SourceRevision, draft.Quote.SourceRevision, StringComparison.Ordinal)
            && string.Equals(Draft.Quote.RuleDigest, draft.Quote.RuleDigest, StringComparison.Ordinal)
            && Draft.Plan == draft.Plan
            && string.Equals(IdempotencyKey, draft.ActionPlan.IdempotencyKey, StringComparison.Ordinal);
    }
}

public enum Sr5CareerAttributeRecoveryStatus
{
    AppliedVerified,
    NotAppliedVerified,
    OutcomeUnknown
}

public sealed record Sr5CareerAttributeRecoveryResolution(
    Sr5CareerAttributeRecoveryStatus Status,
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long CheckpointVersion,
    CharacterCareerAttributeAdvanceReceipt? Receipt,
    string Message,
    string AuthorityProof);

public enum Sr5CareerAttributeApplyStatus
{
    Applied,
    RejectedBeforeMutation,
    OutcomeUnknown
}

public sealed record Sr5CareerAttributeApplyResult(
    Sr5CareerAttributeApplyStatus Status,
    Sr5CareerActionPlan ActionPlan,
    long? SavedContentRevision,
    CharacterCareerAttributeAdvanceReceipt? Receipt,
    Sr5CareerAttributeRecoveryResolution Resolution,
    string Message);
