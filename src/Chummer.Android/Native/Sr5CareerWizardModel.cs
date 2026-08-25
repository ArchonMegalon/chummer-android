using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public enum Sr5CareerWizardLane
{
    Advancement,
    BeforeRun,
    LiveRun,
    AfterRun,
    Downtime,
    Corrections
}

public enum Sr5CareerWizardAvailability
{
    Available,
    Partial,
    Blocked
}

public static class Sr5CareerWizardRoutes
{
    public const string Hub = "sr5-career";
    public const string ActiveSkillChoose = "sr5-career/advancement/active-skill/choose";
    public const string ActiveSkillReview = "sr5-career/advancement/active-skill/review";
    public const string ActiveSkillReceipt = "sr5-career/advancement/active-skill/receipt";

    public static string Lane(Sr5CareerWizardLane lane)
        => $"sr5-career/{Sr5CareerWizardPage.LaneToken(lane)}";
}

public enum Sr5CareerActionKind
{
    ActiveSkillAdvance,
    AttributeAdvance,
    SkillGroupAdvance
}

public sealed record Sr5CareerCostQuote(
    int KarmaCost,
    decimal NuyenCost,
    decimal EssenceCost,
    int? Availability,
    TimeSpan? ElapsedTime,
    string RuleDigest,
    string LogicalRevision,
    bool IsExact,
    string Blocker);

public sealed record Sr5CareerActionPlan(
    Guid OwnerId,
    Guid ActionId,
    string IdempotencyKey,
    string RouteId,
    Sr5CareerActionKind Kind,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string DomainIdentity,
    Sr5CareerCostQuote CostQuote)
{
    public static Sr5CareerActionPlan FromActiveSkill(
        Guid ownerId,
        CharacterWorkspaceId workspaceId,
        long expectedContentRevision,
        CharacterCareerActiveSkillAdvanceQuote quote,
        CharacterCareerActiveSkillAdvancePlan plan)
    {
        string identity = $"{quote.Identity.SkillId:D}:{quote.Identity.SourceSkillId:D}";
        string idempotencyKey = ComputeIdempotencyKey(
            Sr5CareerWizardRoutes.ActiveSkillReview,
            ownerId,
            workspaceId.Value,
            expectedContentRevision,
            plan.ExpenseId,
            identity,
            quote.LogicalRevision,
            quote.RuleDigest);
        return new Sr5CareerActionPlan(
            ownerId,
            plan.ExpenseId,
            idempotencyKey,
            Sr5CareerWizardRoutes.ActiveSkillReview,
            Sr5CareerActionKind.ActiveSkillAdvance,
            workspaceId,
            expectedContentRevision,
            identity,
            new Sr5CareerCostQuote(
                quote.KarmaCost,
                NuyenCost: 0m,
                EssenceCost: 0m,
                Availability: null,
                ElapsedTime: null,
                quote.RuleDigest,
                quote.LogicalRevision,
                IsExact: CharacterCareerActiveSkillAdvanceRules.IsCoherent(quote),
                Blocker: quote.CanAdvance ? string.Empty : quote.Blocker.ToString()));
    }

    private static string ComputeIdempotencyKey(params object[] values)
    {
        string payload = string.Join("\n", values.Select(static value => value.ToString()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

public enum Sr5CareerApplyStatus
{
    Applied,
    RejectedBeforeMutation,
    OutcomeUnknown
}

public sealed record Sr5CareerApplyResult(
    Sr5CareerApplyStatus Status,
    Sr5CareerActionPlan ActionPlan,
    long? SavedContentRevision,
    Sr5CareerActiveSkillReceipt? Receipt,
    Sr5CareerRecoveryResolution Resolution,
    string Message);

public enum Sr5CareerCheckpointPhase
{
    Reviewed,
    Applying,
    Applied
}

public sealed record Sr5CareerDraftCheckpoint(
    int SchemaVersion,
    long Version,
    string RouteId,
    Sr5CareerActionKind Kind,
    string WorkspaceId,
    Guid OwnerId,
    long ExpectedContentRevision,
    Guid SkillId,
    Guid SourceSkillId,
    string LogicalRevision,
    string SourceRevision,
    string RuleDigest,
    Guid ActionId,
    DateTime ExpenseDateLocal,
    decimal ExpenseAmount,
    string ExpenseReason,
    string KarmaUndoType,
    int PreviousRating,
    int TargetRating,
    int SavedKarma,
    string IdempotencyKey,
    Sr5CareerCheckpointPhase Phase)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsStructurallyValid()
    {
        DateTime normalizedExpenseDate = DateTime.SpecifyKind(
            ExpenseDateLocal,
            DateTimeKind.Unspecified);
        return SchemaVersion == CurrentSchemaVersion
            && Version > 0
            && string.Equals(RouteId, Sr5CareerWizardRoutes.ActiveSkillReview, StringComparison.Ordinal)
            && Kind == Sr5CareerActionKind.ActiveSkillAdvance
            && !string.IsNullOrWhiteSpace(WorkspaceId)
            && OwnerId != Guid.Empty
            && ExpectedContentRevision > 0
            && SkillId != Guid.Empty
            && SourceSkillId != Guid.Empty
            && !string.IsNullOrWhiteSpace(LogicalRevision)
            && !string.IsNullOrWhiteSpace(SourceRevision)
            && !string.IsNullOrWhiteSpace(RuleDigest)
            && ActionId != Guid.Empty
            && normalizedExpenseDate == ExpenseDateLocal
            && normalizedExpenseDate.Ticks % TimeSpan.TicksPerSecond == 0
            && normalizedExpenseDate >= CharacterCareerActiveSkillAdvanceRules.MinimumExpenseDate
            && normalizedExpenseDate <= CharacterCareerActiveSkillAdvanceRules.MaximumExpenseDate
            && ExpenseAmount < 0m
            && decimal.Truncate(ExpenseAmount) == ExpenseAmount
            && !string.IsNullOrWhiteSpace(ExpenseReason)
            && !string.IsNullOrWhiteSpace(KarmaUndoType)
            && PreviousRating >= 0
            && TargetRating == PreviousRating + 1
            && SavedKarma >= 0
            && IdempotencyKey.Length == 64
            && IdempotencyKey.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && Enum.IsDefined(Phase);
    }

    public static Sr5CareerDraftCheckpoint FromDraft(
        Sr5CareerActiveSkillDraft draft,
        Sr5CareerCheckpointPhase phase = Sr5CareerCheckpointPhase.Reviewed)
        => new(
            CurrentSchemaVersion,
            Version: 1,
            Sr5CareerWizardRoutes.ActiveSkillReview,
            Sr5CareerActionKind.ActiveSkillAdvance,
            draft.WorkspaceId.Value,
            draft.OwnerId,
            draft.ExpectedContentRevision,
            draft.Quote.Identity.SkillId,
            draft.Quote.Identity.SourceSkillId,
            draft.Quote.LogicalRevision,
            draft.Quote.SourceRevision,
            draft.Quote.RuleDigest,
            draft.Plan.ExpenseId,
            draft.Plan.ExpenseDateLocal,
            draft.Plan.ExpenseAmount,
            draft.Plan.ExpenseReason,
            draft.Plan.KarmaUndoType,
            draft.Quote.TotalBaseRating,
            draft.Quote.TotalBaseRating + 1,
            draft.Plan.SavedCharacterKarma,
            draft.ActionPlan.IdempotencyKey,
            phase);

    public bool TryResume(
        CareerActiveSkillAdvanceEditorState? editor,
        out Sr5CareerActiveSkillDraft draft,
        out string blocker)
    {
        draft = null!;
        if (SchemaVersion != CurrentSchemaVersion
            || Kind != Sr5CareerActionKind.ActiveSkillAdvance
            || !string.Equals(RouteId, Sr5CareerWizardRoutes.ActiveSkillReview, StringComparison.Ordinal))
        {
            blocker = "The saved Career draft has an unsupported route or schema.";
            return false;
        }
        if (Phase != Sr5CareerCheckpointPhase.Reviewed)
        {
            blocker = "This action may already have been applied. Reload and inspect the expense ledger; do not retry it.";
            return false;
        }
        if (editor is null
            || !string.Equals(editor.WorkspaceId.Value, WorkspaceId, StringComparison.Ordinal)
            || editor.ContentRevision != ExpectedContentRevision)
        {
            blocker = "The saved Career draft belongs to another runner revision and cannot be resumed.";
            return false;
        }

        CharacterCareerActiveSkillAdvanceQuote? selected = editor.Skills.SingleOrDefault(candidate =>
            candidate.Identity.SkillId == SkillId
            && candidate.Identity.SourceSkillId == SourceSkillId
            && string.Equals(candidate.LogicalRevision, LogicalRevision, StringComparison.Ordinal)
            && string.Equals(candidate.SourceRevision, SourceRevision, StringComparison.Ordinal)
            && string.Equals(candidate.RuleDigest, RuleDigest, StringComparison.Ordinal));
        if (!Sr5CareerActiveSkillDraft.TryCreate(
                editor,
                selected,
                OwnerId,
                ActionId,
                ExpenseDateLocal,
                out draft,
                out blocker))
        {
            return false;
        }
        if (!string.Equals(draft.ActionPlan.IdempotencyKey, IdempotencyKey, StringComparison.Ordinal))
        {
            draft = null!;
            blocker = "The saved Career draft idempotency binding no longer matches its exact plan.";
            return false;
        }
        return true;
    }
}

public sealed record Sr5CareerWizardLaneDefinition(
    Sr5CareerWizardLane Lane,
    string Title,
    string Summary,
    Sr5CareerWizardAvailability Availability,
    string AuthorityNote);

public static class Sr5CareerWizardCatalog
{
    public const string Edition = "SR5";

    public static IReadOnlyList<Sr5CareerWizardLaneDefinition> Lanes { get; } =
    [
        new(
            Sr5CareerWizardLane.Advancement,
            "Advance",
            "Improve the runner with exact, separately quoted SR5 actions.",
            Sr5CareerWizardAvailability.Partial,
            "Active-skill advancement has a complete typed review/apply boundary. Other advancement families remain separate or blocked."),
        new(
            Sr5CareerWizardLane.BeforeRun,
            "Before the run",
            "Review and make only safe, revision-bound preparation changes.",
            Sr5CareerWizardAvailability.Partial,
            "Edge use is typed. A complete loadout, healing, contact and acquisition checklist is not authoritative yet."),
        new(
            Sr5CareerWizardLane.LiveRun,
            "Live / playtime",
            "Use table-safe actions without exposing unrestricted runner editing.",
            Sr5CareerWizardAvailability.Partial,
            "Edge use and context-bound weapon fire exist as typed leaves. There is no atomic live-action session transaction."),
        new(
            Sr5CareerWizardLane.AfterRun,
            "After the run",
            "Record rewards and reputation through exact SR5 mutation families.",
            Sr5CareerWizardAvailability.Partial,
            "Karma, Nuyen and reputation are independent typed saves. Contacts and a combined atomic run closeout are blocked."),
        new(
            Sr5CareerWizardLane.Downtime,
            "Downtime",
            "Plan calendar weeks and execute only actions with exact rule authority.",
            Sr5CareerWizardAvailability.Partial,
            "Calendar CRUD and active-skill advancement are typed. Training time, healing, crafting and acquisitions are not composed yet."),
        new(
            Sr5CareerWizardLane.Corrections,
            "Corrections and receipts",
            "Inspect editable expense records and recover safely from stale revisions.",
            Sr5CareerWizardAvailability.Partial,
            "Active-skill reviews have a local crash checkpoint and one-shot outcome guard. Undo, correction receipts and shared recovery orchestration remain unavailable.")
    ];

    public static bool IsSr5CareerRunner(bool characterCreated, string? gameEdition)
        => characterCreated
           && string.Equals(gameEdition?.Trim(), Edition, StringComparison.OrdinalIgnoreCase);
}

public sealed record Sr5CareerActiveSkillDraft(
    Guid OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterCareerActiveSkillAdvanceQuote Quote,
    CharacterCareerActiveSkillAdvancePlan Plan)
{
    public Sr5CareerActionPlan ActionPlan
        => Sr5CareerActionPlan.FromActiveSkill(OwnerId, WorkspaceId, ExpectedContentRevision, Quote, Plan);

    public CareerActiveSkillAdvanceRequest ToRequest()
        => new(
            WorkspaceId,
            ExpectedContentRevision,
            Quote,
            Quote.RuleDigest,
            Confirmed: true,
            Plan.ExpenseId,
            Plan.ExpenseDateLocal);

    public bool Matches(CharacterWorkspaceId? workspaceId, long contentRevision)
        => workspaceId == WorkspaceId && contentRevision == ExpectedContentRevision;

    public static bool TryCreate(
        CareerActiveSkillAdvanceEditorState? editor,
        CharacterCareerActiveSkillAdvanceQuote? selected,
        Guid ownerId,
        Guid expenseId,
        DateTime expenseDateLocal,
        out Sr5CareerActiveSkillDraft draft,
        out string blocker)
    {
        draft = null!;
        blocker = string.Empty;
        if (editor is null
            || string.IsNullOrWhiteSpace(editor.WorkspaceId.Value)
            || editor.ContentRevision <= 0)
        {
            blocker = "The runner identity or revision is unavailable. Reopen the wizard.";
            return false;
        }
        if (ownerId == Guid.Empty)
        {
            blocker = "The wizard owner identity is unavailable. Reopen the wizard.";
            return false;
        }
        if (selected is null)
        {
            blocker = "Choose an exact saved active skill.";
            return false;
        }

        CharacterCareerActiveSkillAdvanceQuote? authoritative = editor.Skills.SingleOrDefault(candidate =>
            candidate.Identity == selected.Identity
            && string.Equals(candidate.LogicalRevision, selected.LogicalRevision, StringComparison.Ordinal)
            && string.Equals(candidate.RuleDigest, selected.RuleDigest, StringComparison.Ordinal));
        if (authoritative is null
            || !CharacterCareerActiveSkillAdvanceRules.IsCoherent(authoritative))
        {
            blocker = "The selected quote is not part of this revision's exact skill projection.";
            return false;
        }
        if (!authoritative.CanAdvance)
        {
            blocker = authoritative.Blocker switch
            {
                CharacterCareerActiveSkillAdvanceBlocker.AtMaximum =>
                    "The selected skill is already at its exact career maximum.",
                CharacterCareerActiveSkillAdvanceBlocker.InsufficientKarma =>
                    "The runner does not have enough Karma for this advancement.",
                _ => "The selected skill cannot be advanced."
            };
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
        if (!CharacterCareerActiveSkillAdvanceRules.TryPlanAdvance(
                authoritative,
                authoritative.RuleDigest,
                confirmed: true,
                expenseId,
                serializedExpenseDate,
                out CharacterCareerActiveSkillAdvancePlan plan))
        {
            blocker = "Core rejected the confirmed expense identity, date or rule digest.";
            return false;
        }

        draft = new Sr5CareerActiveSkillDraft(
            ownerId,
            editor.WorkspaceId,
            editor.ContentRevision,
            authoritative,
            plan);
        return true;
    }
}

public sealed record Sr5CareerActiveSkillReceipt(
    Guid OwnerId,
    Guid ActionId,
    string IdempotencyKey,
    string RouteId,
    CharacterWorkspaceId WorkspaceId,
    long PreviousContentRevision,
    long SavedContentRevision,
    Guid SkillId,
    Guid SourceSkillId,
    string SkillName,
    int PreviousRating,
    int SavedRating,
    int KarmaCost,
    int SavedKarma,
    Guid ExpenseId,
    DateTime ExpenseDateLocal,
    string ExpenseReason,
    string KarmaUndoType,
    string RuleDigest,
    string SourceRevision);

public enum Sr5CareerRecoveryStatus
{
    AppliedVerified,
    NotAppliedVerified,
    OutcomeUnknown
}

public sealed record Sr5CareerRecoveryResolution(
    Sr5CareerRecoveryStatus Status,
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long CheckpointVersion,
    Sr5CareerActiveSkillReceipt? Receipt,
    string Message);
