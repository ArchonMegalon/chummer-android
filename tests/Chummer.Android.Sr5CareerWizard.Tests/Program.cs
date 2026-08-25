using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

internal static class Program
{
    private static void Main()
    {
        CatalogIsSr5OnlyAndReleaseHonest();
        ActiveSkillDraftCarriesExactPlanIntoRequest();
        ActiveSkillDraftRejectsForeignAndBlockedQuotes();
        ReceiptRequiresOneCleanSavedSuccessorRevision();
        Console.WriteLine("SR5 Career wizard contract tests passed: 4");
    }

    private static void CatalogIsSr5OnlyAndReleaseHonest()
    {
        Require(Sr5CareerWizardCatalog.IsSr5CareerRunner(true, "SR5"), "Created SR5 must be accepted.");
        Require(Sr5CareerWizardCatalog.IsSr5CareerRunner(true, " sr5 "), "Edition matching is normalized.");
        Require(!Sr5CareerWizardCatalog.IsSr5CareerRunner(false, "SR5"), "Creation must fail closed.");
        Require(!Sr5CareerWizardCatalog.IsSr5CareerRunner(true, "SR6"), "Other editions must fail closed.");
        Require(Sr5CareerWizardCatalog.Lanes.Count == 6, "Every requested Career lane must be represented.");
        Require(
            Sr5CareerWizardCatalog.Lanes.All(lane => lane.Availability != Sr5CareerWizardAvailability.Available),
            "No broad lane may claim full availability while required families remain missing.");
    }

    private static void ActiveSkillDraftCarriesExactPlanIntoRequest()
    {
        CharacterWorkspaceId workspaceId = new("sr5-career-runner");
        CharacterCareerActiveSkillAdvanceQuote quote = Quote(canAdvance: true);
        CareerActiveSkillAdvanceEditorState editor = new(workspaceId, 41, [quote], 0);
        Guid expenseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        DateTime expenseDate = new(2081, 6, 3, 19, 30, 0, DateTimeKind.Local);

        Require(
            Sr5CareerActiveSkillDraft.TryCreate(
                editor,
                quote,
                expenseId,
                expenseDate,
                out Sr5CareerActiveSkillDraft draft,
                out string blocker),
            $"Exact draft must be accepted: {blocker}");
        CareerActiveSkillAdvanceRequest request = draft.ToRequest();
        Require(request.WorkspaceId == workspaceId, "Workspace identity must survive review.");
        Require(request.ExpectedContentRevision == 41, "Revision must survive review.");
        Require(request.ExpectedSkill.Identity == quote.Identity, "Stable skill identity must survive review.");
        Require(request.ExpectedRuleDigest == quote.RuleDigest, "Rule digest must survive review.");
        Require(request.Confirmed, "Final request must be explicitly confirmed.");
        Require(request.ExpenseId == expenseId, "Expense identity must be stable across review/apply.");
        Require(
            draft.Plan.SavedCharacterKarma == quote.AvailableKarma - quote.KarmaCost,
            "Core plan must own the saved balance.");
        Require(draft.Plan.KarmaUndoType == "ImproveSkill", "Core plan must own undo semantics.");
        Sr5CareerActionPlan actionPlan = draft.ActionPlan;
        Require(actionPlan.Kind == Sr5CareerActionKind.ActiveSkillAdvance, "Shared action kind must be typed.");
        Require(actionPlan.RouteId == Sr5CareerWizardRoutes.ActiveSkillReview, "Route identity must survive review.");
        Require(actionPlan.ActionId == expenseId, "Expense ID is the stable one-shot action identity.");
        Require(actionPlan.CostQuote.KarmaCost == quote.KarmaCost, "Shared cost quote must preserve Core cost.");
        Require(actionPlan.CostQuote.IsExact, "Shared cost quote must be marked exact only for a coherent quote.");
        Require(actionPlan.AtomicSingleAction, "The active-skill mutation and expense form one Core action.");

        Sr5CareerDraftCheckpoint checkpoint = Sr5CareerDraftCheckpoint.FromDraft(draft);
        Require(
            checkpoint.TryResume(editor, out Sr5CareerActiveSkillDraft resumed, out string recoveryBlocker),
            $"A reviewed checkpoint must resume on its exact revision: {recoveryBlocker}");
        Require(
            resumed.ActionPlan.IdempotencyKey == actionPlan.IdempotencyKey,
            "Crash recovery must retain the same idempotency binding.");
        Require(
            !Sr5CareerDraftCheckpoint.FromDraft(draft, Sr5CareerCheckpointPhase.Applying)
                .TryResume(editor, out _, out string applyingBlocker)
            && applyingBlocker.Contains("do not retry", StringComparison.OrdinalIgnoreCase),
            "An applying checkpoint must fail closed against replay after a crash.");
    }

    private static void ActiveSkillDraftRejectsForeignAndBlockedQuotes()
    {
        CharacterWorkspaceId workspaceId = new("sr5-career-runner");
        CharacterCareerActiveSkillAdvanceQuote quote = Quote(canAdvance: true);
        CareerActiveSkillAdvanceEditorState editor = new(workspaceId, 41, [quote], 0);
        CharacterCareerActiveSkillAdvanceQuote foreign = quote with
        {
            Identity = new CharacterCareerActiveSkillIdentity(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                quote.Identity.SourceSkillId)
        };
        Require(
            !Sr5CareerActiveSkillDraft.TryCreate(
                editor,
                foreign,
                Guid.NewGuid(),
                DateTime.Now,
                out _,
                out _),
            "A quote not projected for this revision must be rejected.");

        CharacterCareerActiveSkillAdvanceQuote blocked = quote with
        {
            AvailableKarma = 0,
            CanAdvance = false,
            Blocker = CharacterCareerActiveSkillAdvanceBlocker.InsufficientKarma
        };
        CareerActiveSkillAdvanceEditorState blockedEditor = new(workspaceId, 41, [blocked], 0);
        Require(
            !Sr5CareerActiveSkillDraft.TryCreate(
                blockedEditor,
                blocked,
                Guid.NewGuid(),
                DateTime.Now,
                out _,
                out string blocker)
            && !string.IsNullOrWhiteSpace(blocker),
            $"An insufficient-Karma quote must remain blocked: {blocker}");
    }

    private static void ReceiptRequiresOneCleanSavedSuccessorRevision()
    {
        CharacterWorkspaceId workspaceId = new("sr5-career-runner");
        CharacterCareerActiveSkillAdvanceQuote quote = Quote(canAdvance: true);
        CareerActiveSkillAdvanceEditorState editor = new(workspaceId, 41, [quote], 0);
        Require(
            Sr5CareerActiveSkillDraft.TryCreate(
                editor,
                quote,
                Guid.NewGuid(),
                DateTime.Now,
                out Sr5CareerActiveSkillDraft draft,
                out _),
            "Draft setup must succeed.");
        Require(
            Sr5CareerApplyResult.TryCreateApplied(
                draft,
                workspaceId,
                contentRevision: 42,
                savedRevision: 42,
                isDirty: false,
                actualKarma: draft.Plan.SavedCharacterKarma,
                error: null,
                out Sr5CareerApplyResult result,
                out string blocker),
            $"One clean saved successor must produce an atomic ApplyResult: {blocker}");
        Sr5CareerActiveSkillReceipt receipt = result.Receipt!;
        Require(result.Status == Sr5CareerApplyStatus.Applied, "ApplyResult must declare the durable outcome.");
        Require(result.Atomic, "ApplyResult must preserve the single-action atomic boundary.");
        Require(
            result.ActionPlan.IdempotencyKey == receipt.IdempotencyKey,
            "ApplyResult and receipt must bind the same idempotency key.");
        Require(receipt.SavedRating == 4, "Receipt must bind the reviewed rating delta.");
        Require(
            receipt.SavedKarma == draft.Plan.SavedCharacterKarma,
            "Receipt must bind the actual reviewed Karma balance.");

        Require(
            !Sr5CareerApplyResult.TryCreateApplied(
                draft,
                workspaceId,
                contentRevision: 42,
                savedRevision: 41,
                isDirty: true,
                actualKarma: draft.Plan.SavedCharacterKarma,
                error: null,
                out _,
                out _),
            "Dirty or unsaved state must not produce a receipt.");
        Require(
            !Sr5CareerApplyResult.TryCreateApplied(
                draft,
                workspaceId,
                contentRevision: 42,
                savedRevision: 42,
                isDirty: false,
                actualKarma: draft.Plan.SavedCharacterKarma + 1,
                error: null,
                out _,
                out _),
            "A mismatched saved Karma balance must not produce a receipt.");
    }

    private static CharacterCareerActiveSkillAdvanceQuote Quote(bool canAdvance)
    {
        CharacterCareerActiveSkillAdvanceInput input = new(
            new CharacterCareerActiveSkillIdentity(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222")),
            Created: true,
            "Sneaking",
            "Physical Active",
            "Sneaking",
            BasePoints: 2,
            KarmaPoints: 1,
            TotalBaseRating: 3,
            RatingMaximum: 12,
            AvailableKarma: canAdvance ? 20 : 0,
            new CharacterCareerActiveSkillAdvanceSettings(2, 2, 5, 5, false),
            OtherGroupMembers: [],
            Modifiers: [],
            RawSourceState: "<skill><name>Sneaking</name></skill>",
            RawRuleState: "<settings />");
        Require(
            CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(input, out CharacterCareerActiveSkillAdvanceQuote quote),
            "Test quote authority must be coherent.");
        Require(quote.CanAdvance == canAdvance, "Test quote blocker setup must match.");
        return quote;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
