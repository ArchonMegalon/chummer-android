using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Text.Json;
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
    public const string SkillGroupChoose = "sr5-career/advancement/skill-group/choose";
    public const string SkillGroupReview = "sr5-career/advancement/skill-group/review";
    public const string SkillGroupReceipt = "sr5-career/advancement/skill-group/receipt";
    public const string SpecializationChoose = "sr5-career/advancement/specialization/choose";
    public const string SpecializationConfigure = "sr5-career/advancement/specialization/configure";
    public const string SpecializationReview = "sr5-career/advancement/specialization/review";
    public const string SpecializationReceipt = "sr5-career/advancement/specialization/receipt";
    public const string KnowledgeSkillChoose = "sr5-career/advancement/knowledge-language/choose";
    public const string KnowledgeSkillReview = "sr5-career/advancement/knowledge-language/review";
    public const string KnowledgeSkillReceipt = "sr5-career/advancement/knowledge-language/receipt";
    public const string AttributeChoose = "sr5-career/advancement/attribute/choose";
    public const string AttributeReview = "sr5-career/advancement/attribute/review";
    public const string AttributeReceipt = "sr5-career/advancement/attribute/receipt";
    public const string QualityChoose = "sr5-career/advancement/quality/choose";
    public const string QualityReview = "sr5-career/advancement/quality/review";
    public const string QualityReceipt = "sr5-career/advancement/quality/receipt";
    public const string AfterRunEnter = "sr5-career/after-run/settlement/enter";
    public const string AfterRunChoose = "sr5-career/after-run/settlement/choose";
    public const string AfterRunRewards = "sr5-career/after-run/settlement/rewards";
    public const string AfterRunConsequences = "sr5-career/after-run/settlement/consequences";
    public const string AfterRunContacts = "sr5-career/after-run/settlement/contacts";
    public const string AfterRunGmReview = "sr5-career/after-run/settlement/gm-review";
    public const string AfterRunOwnerReview = "sr5-career/after-run/settlement/owner-review";
    public const string AfterRunReview = "sr5-career/after-run/settlement/review";
    public const string AfterRunReceipt = "sr5-career/after-run/settlement/receipt";

    public static string Lane(Sr5CareerWizardLane lane)
        => $"sr5-career/{Sr5CareerWizardPage.LaneToken(lane)}";
}

/// <summary>
/// Shared runner identity/revision boundary for every SR5 Career lane. Domain
/// coordinators remain specialized; none may depend on another lane's guard.
/// </summary>
public sealed record Sr5CareerRunnerBinding(
    bool Created,
    string? GameEdition,
    CharacterWorkspaceId? WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    bool IsDirty,
    string? Error);

public static class Sr5CareerRunnerGuard
{
    public static void RequireCreated(Sr5CareerRunnerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition))
        {
            throw new InvalidOperationException(
                "SR5 Career actions require a created Shadowrun Fifth Edition runner.");
        }
        if (binding.WorkspaceId is not { } workspaceId
            || string.IsNullOrWhiteSpace(workspaceId.Value)
            || binding.ContentRevision <= 0)
        {
            throw new InvalidOperationException(
                "SR5 Career actions require an exact saved runner identity and revision.");
        }
    }
}

public enum Sr5CareerActionKind
{
    ActiveSkillAdvance = 0,
    AttributeAdvance = 1,
    SkillGroupAdvance = 2,
    QualityTransaction = 3,
    KnowledgeSkillAdvance = 4,
    SkillSpecializationAdd = 5,
    AfterRunSettlement = 6
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
        string idempotencyKey = ComputeActiveSkillIdempotencyKey(
            ownerId,
            workspaceId.Value,
            expectedContentRevision,
            plan.ExpenseId,
            quote.Identity.SkillId,
            quote.Identity.SourceSkillId,
            quote.LogicalRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            quote.Name,
            quote.SkillCategory,
            quote.BasePoints,
            quote.KarmaPoints,
            quote.RatingMaximum,
            plan.ExpenseDateLocal,
            plan.ExpenseAmount,
            plan.ExpenseReason,
            plan.KarmaUndoType,
            plan.NuyenUndoType,
            plan.UndoObjectId,
            plan.UndoQuantity,
            plan.UndoExtra,
            quote.TotalBaseRating,
            quote.TotalBaseRating + 1,
            plan.SavedCharacterKarma);
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

    public static Sr5CareerActionPlan FromAttribute(
        Guid ownerId,
        CharacterWorkspaceId workspaceId,
        long expectedContentRevision,
        CharacterCareerAttributeAdvanceQuote quote,
        CharacterCareerAttributeAdvancePlan plan)
    {
        string identity = $"{quote.Identity.Abbreviation}:{quote.Identity.Kind}";
        string idempotencyKey = ComputeAttributeIdempotencyKey(
            ownerId,
            workspaceId.Value,
            expectedContentRevision,
            plan.ExpenseId,
            quote.Identity.Abbreviation,
            quote.Identity.Kind,
            quote.LogicalRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            quote.DisplayName,
            quote.BasePoints,
            quote.KarmaPoints,
            quote.EffectiveValue,
            quote.TargetValue,
            quote.NaturalMaximum,
            quote.MetatypeMinimum,
            quote.AvailableKarma,
            quote.KarmaCost,
            quote.RepairsBurnedEdge,
            quote.BurnedEdgePoints,
            plan.SavedAttributeKarmaPoints,
            plan.SavedCharacterKarma,
            plan.SavedBurnedEdgePoints,
            plan.ExpenseDateLocal,
            plan.ExpenseAmount,
            plan.ExpenseReason,
            plan.KarmaUndoType,
            plan.NuyenUndoType,
            plan.UndoObjectId,
            plan.UndoQuantity,
            plan.UndoExtra);
        return new Sr5CareerActionPlan(
            ownerId,
            plan.ExpenseId,
            idempotencyKey,
            Sr5CareerWizardRoutes.AttributeReview,
            Sr5CareerActionKind.AttributeAdvance,
            workspaceId,
            expectedContentRevision,
            identity,
            new Sr5CareerCostQuote(
                quote.KarmaCost,
                NuyenCost: 0m,
                EssenceCost: 0m,
                Availability: null,
                ElapsedTime: quote.ApplicationDuration,
                RuleDigest: quote.RuleDigest,
                LogicalRevision: quote.LogicalRevision,
                IsExact: CharacterCareerAttributeAdvanceRules.IsCoherent(quote)
                    && CharacterCareerAttributeAdvanceRules.IsCoherent(plan),
                Blocker: quote.CanAdvance ? string.Empty : quote.Blocker.ToString()));
    }

    public static Sr5CareerActionPlan FromSkillGroup(
        Guid ownerId,
        CharacterCareerSkillGroupQuoteBinding binding,
        CharacterCareerSkillGroupAdvancePlan plan,
        string contentDigest,
        string runtimeDigest)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(plan);
        CharacterCareerSkillGroupAdvanceQuote quote = binding.Quote;
        string identity = quote.Identity.InternalId.ToString("D");
        string idempotencyKey = ComputeSkillGroupIdempotencyKey(
            ownerId,
            binding.WorkspaceId.Value,
            binding.WorkspaceRevision,
            plan.TransactionId,
            quote.Identity.InternalId,
            binding.ContractName,
            binding.BindingDigest,
            quote.LogicalRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            contentDigest,
            runtimeDigest,
            quote.Name,
            quote.BasePoints,
            quote.KarmaPoints,
            quote.GroupRating,
            quote.CostRating,
            quote.TargetGroupRating,
            quote.TargetCostRating,
            quote.EnabledMemberCount,
            quote.RatingMaximum,
            quote.AvailableKarma,
            quote.Disabled,
            quote.Broken,
            quote.KarmaCost,
            quote.ApplicationDuration,
            plan.SavedGroupKarmaPoints,
            plan.SavedCharacterKarma,
            plan.ExpenseDateLocal,
            plan.ExpenseAmount,
            plan.ExpenseReason,
            plan.KarmaUndoType,
            plan.NuyenUndoType,
            plan.UndoObjectId,
            plan.UndoQuantity,
            plan.UndoExtra);
        return new Sr5CareerActionPlan(
            ownerId,
            plan.TransactionId,
            idempotencyKey,
            Sr5CareerWizardRoutes.SkillGroupReview,
            Sr5CareerActionKind.SkillGroupAdvance,
            binding.WorkspaceId,
            binding.WorkspaceRevision,
            identity,
            new Sr5CareerCostQuote(
                quote.KarmaCost,
                NuyenCost: 0m,
                EssenceCost: 0m,
                Availability: null,
                ElapsedTime: quote.ApplicationDuration,
                RuleDigest: quote.RuleDigest,
                LogicalRevision: quote.LogicalRevision,
                IsExact: CharacterCareerSkillGroupAdvanceRules.IsCoherent(quote)
                    && CharacterCareerSkillGroupAdvanceRules.IsCoherent(plan),
                Blocker: quote.CanAdvance ? string.Empty : quote.Blocker.ToString()));
    }

    public static Sr5CareerActionPlan FromQuality(
        Guid ownerId,
        CareerQualityReview review,
        Guid transactionId,
        DateTime expenseDateLocal,
        string contentDigest,
        string runtimeDigest)
    {
        CharacterCareerQualityQuote quote = review.Quote;
        string identity = $"{quote.Operation}:{quote.Identity.InternalId:D}:{quote.Identity.SourceId:D}";
        string idempotencyKey = ComputeQualityIdempotencyKey(
            ownerId,
            review.Draft.WorkspaceId.Value,
            review.Draft.ExpectedWorkspaceRevision,
            review.Draft.ExpectedSavedRevision,
            transactionId,
            expenseDateLocal,
            contentDigest,
            runtimeDigest,
            review);
        return new Sr5CareerActionPlan(
            ownerId,
            transactionId,
            idempotencyKey,
            Sr5CareerWizardRoutes.QualityReview,
            Sr5CareerActionKind.QualityTransaction,
            review.Draft.WorkspaceId,
            review.Draft.ExpectedWorkspaceRevision,
            identity,
            new Sr5CareerCostQuote(
                quote.RuleKarmaCost,
                NuyenCost: 0m,
                EssenceCost: 0m,
                Availability: null,
                ElapsedTime: quote.ApplicationDuration,
                RuleDigest: quote.RuleDigest,
                LogicalRevision: quote.LogicalRevision,
                IsExact: CharacterCareerQualityRules.IsCoherent(quote),
                Blocker: quote.CanApply ? string.Empty : quote.Blocker.ToString()));
    }

    public static Sr5CareerActionPlan FromKnowledgeSkill(
        Guid ownerId,
        CharacterWorkspaceId workspaceId,
        long expectedContentRevision,
        CharacterCareerKnowledgeSkillAdvanceQuote quote,
        CharacterCareerKnowledgeSkillAdvancePlan plan)
    {
        string sourceIdentity = quote.Identity.SourceSkillId?.ToString("D") ?? "custom";
        string identity = $"{quote.Identity.SkillId:D}:{sourceIdentity}";
        string idempotencyKey = ComputeKnowledgeSkillIdempotencyKey(
            ownerId,
            workspaceId.Value,
            expectedContentRevision,
            plan.ExpenseId,
            quote.Identity.SkillId,
            quote.Identity.SourceSkillId,
            quote.CharacterRevision,
            quote.LogicalRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            quote.Name,
            quote.SkillType,
            quote.SkillCategory,
            quote.BasePoints,
            quote.KarmaPoints,
            quote.TotalBaseRating,
            quote.RatingMaximum,
            quote.AvailableKarma,
            quote.KarmaCost,
            plan.SavedSkillKarmaPoints,
            plan.SavedCharacterKarma,
            plan.ExpenseDateLocal,
            plan.ExpenseAmount,
            plan.ExpenseReason,
            plan.KarmaUndoType,
            plan.NuyenUndoType,
            plan.UndoObjectId,
            plan.UndoQuantity,
            plan.UndoExtra);
        return new Sr5CareerActionPlan(
            ownerId,
            plan.ExpenseId,
            idempotencyKey,
            Sr5CareerWizardRoutes.KnowledgeSkillReview,
            Sr5CareerActionKind.KnowledgeSkillAdvance,
            workspaceId,
            expectedContentRevision,
            identity,
            new Sr5CareerCostQuote(
                quote.KarmaCost,
                NuyenCost: 0m,
                EssenceCost: 0m,
                Availability: null,
                ElapsedTime: quote.ApplicationDuration,
                quote.RuleDigest,
                quote.LogicalRevision,
                IsExact: CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(quote)
                    && CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(plan),
                Blocker: quote.CanAdvance ? string.Empty : quote.Blocker.ToString()));
    }

    public static Sr5CareerActionPlan FromSpecialization(
        Guid ownerId,
        CharacterWorkspaceId workspaceId,
        long expectedContentRevision,
        CharacterCareerSkillSpecializationQuote quote,
        CharacterCareerSkillSpecializationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(plan);
        string sourceIdentity = quote.Identity.SourceSkillId?.ToString("D") ?? "custom";
        string domainIdentity = $"{quote.Identity.Kind}:{quote.Identity.SkillId:D}:{sourceIdentity}";
        string idempotencyKey = ComputeSpecializationIdempotencyKey(
            ownerId,
            workspaceId,
            expectedContentRevision,
            quote,
            plan);
        return new Sr5CareerActionPlan(
            ownerId,
            plan.ExpenseId,
            idempotencyKey,
            Sr5CareerWizardRoutes.SpecializationReview,
            Sr5CareerActionKind.SkillSpecializationAdd,
            workspaceId,
            expectedContentRevision,
            domainIdentity,
            new Sr5CareerCostQuote(
                quote.KarmaCost,
                NuyenCost: 0m,
                EssenceCost: 0m,
                Availability: null,
                ElapsedTime: null,
                quote.RuleDigest,
                quote.LogicalRevision,
                IsExact: CharacterCareerSkillSpecializationRules.IsCoherent(quote),
                Blocker: quote.CanAdd ? string.Empty : quote.Blocker.ToString()));
    }

    public static Sr5CareerActionPlan FromAfterRunSettlement(
        Guid ownerId,
        CharacterAfterRunSettlementQuoteBinding binding,
        CharacterAfterRunSettlementPlan plan,
        string rewardContextDigest)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(plan);
        CharacterAfterRunSettlementQuote quote = binding.Quote;
        string identity = string.Join(
            ":",
            quote.Identity.ProposalId.ToString("D"),
            quote.Identity.RunId.ToString("D"),
            quote.Identity.CharacterId.ToString("D"));
        string idempotencyKey = Sha256Canonical(
            "chummer.android.sr5-after-run-settlement-action/v1",
            ownerId.ToString("D"),
            binding.WorkspaceId.Value,
            binding.WorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            plan.TransactionId.ToString("D"),
            identity,
            binding.ContractName,
            binding.BindingDigest,
            quote.SourceDigest,
            quote.CustomDataDigest,
            quote.GmPolicyDigest,
            quote.RuntimeDigest,
            quote.LogicalDigest,
            quote.GmReviewDigest,
            quote.OwnerReviewDigest,
            rewardContextDigest,
            plan.PlanDigest);
        return new Sr5CareerActionPlan(
            ownerId,
            plan.TransactionId,
            idempotencyKey,
            Sr5CareerWizardRoutes.AfterRunReview,
            Sr5CareerActionKind.AfterRunSettlement,
            binding.WorkspaceId,
            binding.WorkspaceRevision,
            identity,
            new Sr5CareerCostQuote(
                quote.ContactKarmaCost,
                NuyenCost: 0m,
                EssenceCost: 0m,
                Availability: null,
                ElapsedTime: null,
                RuleDigest: quote.GmPolicyDigest,
                LogicalRevision: quote.LogicalDigest,
                IsExact: CharacterAfterRunSettlementRules.IsCoherent(quote)
                    && CharacterAfterRunSettlementRules.IsCoherent(plan),
                Blocker: quote.CanSettle ? string.Empty : quote.Blocker.ToString()));
    }

    private static string Sha256Canonical(params string[] values)
    {
        string payload = string.Join('\0', values.Select(value =>
            string.Concat(
                value.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                value)));
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string ComputeSpecializationIdempotencyKey(
        Guid ownerId,
        CharacterWorkspaceId workspaceId,
        long expectedContentRevision,
        CharacterCareerSkillSpecializationQuote quote,
        CharacterCareerSkillSpecializationPlan plan)
    {
        string payload = string.Join(
            "\n",
            "chummer.android.sr5-career-specialization-action/v1",
            ownerId.ToString("D"),
            workspaceId.Value,
            expectedContentRevision.ToString(CultureInfo.InvariantCulture),
            quote.Identity.Kind.ToString(),
            quote.Identity.SkillId.ToString("D"),
            quote.Identity.SourceSkillId?.ToString("D") ?? "custom",
            quote.Selection.Kind.ToString(),
            quote.Selection.OptionIdentity ?? "custom",
            quote.Selection.Name,
            quote.CharacterRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            quote.LogicalRevision,
            quote.KarmaCost.ToString(CultureInfo.InvariantCulture),
            quote.ExistingSpecializationCount.ToString(CultureInfo.InvariantCulture),
            quote.AvailableKarma.ToString(CultureInfo.InvariantCulture),
            plan.SpecializationId.ToString("D"),
            plan.ExpenseId.ToString("D"),
            plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture),
            plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture),
            plan.SavedCharacterKarma.ToString(CultureInfo.InvariantCulture),
            string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    public static string ComputeActiveSkillIdempotencyKey(
        Guid ownerId,
        string workspaceId,
        long expectedContentRevision,
        Guid actionId,
        Guid skillId,
        Guid sourceSkillId,
        string logicalRevision,
        string sourceRevision,
        string ruleDigest,
        string skillName,
        string skillCategory,
        int basePoints,
        int previousKarmaPoints,
        int ratingMaximum,
        DateTime expenseDateLocal,
        decimal expenseAmount,
        string expenseReason,
        string karmaUndoType,
        string nuyenUndoType,
        string undoObjectId,
        decimal undoQuantity,
        string undoExtra,
        int previousRating,
        int targetRating,
        int savedKarma)
        => ComputeIdempotencyKey(
            Sr5CareerWizardRoutes.ActiveSkillReview,
            ownerId.ToString("D"),
            workspaceId,
            expectedContentRevision.ToString(CultureInfo.InvariantCulture),
            actionId.ToString("D"),
            $"{skillId:D}:{sourceSkillId:D}",
            logicalRevision,
            sourceRevision,
            ruleDigest,
            skillName,
            skillCategory,
            basePoints.ToString(CultureInfo.InvariantCulture),
            previousKarmaPoints.ToString(CultureInfo.InvariantCulture),
            ratingMaximum.ToString(CultureInfo.InvariantCulture),
            DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified)
                .ToString("O", CultureInfo.InvariantCulture),
            expenseAmount.ToString(CultureInfo.InvariantCulture),
            expenseReason,
            "Karma",
            false.ToString(CultureInfo.InvariantCulture),
            false.ToString(CultureInfo.InvariantCulture),
            karmaUndoType,
            nuyenUndoType,
            undoObjectId,
            undoQuantity.ToString(CultureInfo.InvariantCulture),
            undoExtra,
            previousRating.ToString(CultureInfo.InvariantCulture),
            targetRating.ToString(CultureInfo.InvariantCulture),
            savedKarma.ToString(CultureInfo.InvariantCulture));

    public static string ComputeAttributeIdempotencyKey(
        Guid ownerId,
        string workspaceId,
        long expectedContentRevision,
        Guid actionId,
        string abbreviation,
        CharacterCareerAttributeKind kind,
        string logicalRevision,
        string sourceRevision,
        string ruleDigest,
        string displayName,
        int basePoints,
        int previousKarmaPoints,
        int previousEffectiveValue,
        int targetValue,
        int naturalMaximum,
        int metatypeMinimum,
        int availableKarma,
        int karmaCost,
        bool repairsBurnedEdge,
        int burnedEdgePoints,
        int savedAttributeKarmaPoints,
        int savedCharacterKarma,
        int savedBurnedEdgePoints,
        DateTime expenseDateLocal,
        int expenseAmount,
        string expenseReason,
        string karmaUndoType,
        string nuyenUndoType,
        string undoObjectId,
        decimal undoQuantity,
        string undoExtra)
        => ComputeIdempotencyKey(
            Sr5CareerWizardRoutes.AttributeReview,
            ownerId.ToString("D"),
            workspaceId,
            expectedContentRevision.ToString(CultureInfo.InvariantCulture),
            actionId.ToString("D"),
            abbreviation,
            kind.ToString(),
            logicalRevision,
            sourceRevision,
            ruleDigest,
            displayName,
            basePoints.ToString(CultureInfo.InvariantCulture),
            previousKarmaPoints.ToString(CultureInfo.InvariantCulture),
            previousEffectiveValue.ToString(CultureInfo.InvariantCulture),
            targetValue.ToString(CultureInfo.InvariantCulture),
            naturalMaximum.ToString(CultureInfo.InvariantCulture),
            metatypeMinimum.ToString(CultureInfo.InvariantCulture),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            karmaCost.ToString(CultureInfo.InvariantCulture),
            repairsBurnedEdge.ToString(CultureInfo.InvariantCulture),
            burnedEdgePoints.ToString(CultureInfo.InvariantCulture),
            savedAttributeKarmaPoints.ToString(CultureInfo.InvariantCulture),
            savedCharacterKarma.ToString(CultureInfo.InvariantCulture),
            savedBurnedEdgePoints.ToString(CultureInfo.InvariantCulture),
            DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified)
                .ToString("O", CultureInfo.InvariantCulture),
            expenseAmount.ToString(CultureInfo.InvariantCulture),
            expenseReason,
            "Karma",
            false.ToString(CultureInfo.InvariantCulture),
            false.ToString(CultureInfo.InvariantCulture),
            karmaUndoType,
            nuyenUndoType,
            undoObjectId,
            undoQuantity.ToString(CultureInfo.InvariantCulture),
            undoExtra);

    public static string ComputeSkillGroupIdempotencyKey(
        Guid ownerId,
        string workspaceId,
        long expectedContentRevision,
        Guid actionId,
        Guid internalId,
        string quoteContractName,
        string bindingDigest,
        string logicalRevision,
        string sourceRevision,
        string ruleDigest,
        string contentDigest,
        string runtimeDigest,
        string name,
        int basePoints,
        int previousKarmaPoints,
        int groupRating,
        int costRating,
        int targetGroupRating,
        int targetCostRating,
        int enabledMemberCount,
        int ratingMaximum,
        int availableKarma,
        bool disabled,
        bool broken,
        int karmaCost,
        TimeSpan applicationDuration,
        int savedGroupKarmaPoints,
        int savedCharacterKarma,
        DateTime expenseDateLocal,
        int expenseAmount,
        string expenseReason,
        string karmaUndoType,
        string nuyenUndoType,
        string undoObjectId,
        decimal undoQuantity,
        string undoExtra)
        => ComputeIdempotencyKey(
            Sr5CareerWizardRoutes.SkillGroupReview,
            ownerId.ToString("D"),
            workspaceId,
            expectedContentRevision.ToString(CultureInfo.InvariantCulture),
            actionId.ToString("D"),
            internalId.ToString("D"),
            quoteContractName,
            bindingDigest,
            logicalRevision,
            sourceRevision,
            ruleDigest,
            contentDigest,
            runtimeDigest,
            name,
            basePoints.ToString(CultureInfo.InvariantCulture),
            previousKarmaPoints.ToString(CultureInfo.InvariantCulture),
            groupRating.ToString(CultureInfo.InvariantCulture),
            costRating.ToString(CultureInfo.InvariantCulture),
            targetGroupRating.ToString(CultureInfo.InvariantCulture),
            targetCostRating.ToString(CultureInfo.InvariantCulture),
            enabledMemberCount.ToString(CultureInfo.InvariantCulture),
            ratingMaximum.ToString(CultureInfo.InvariantCulture),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            disabled.ToString(CultureInfo.InvariantCulture),
            broken.ToString(CultureInfo.InvariantCulture),
            karmaCost.ToString(CultureInfo.InvariantCulture),
            applicationDuration.Ticks.ToString(CultureInfo.InvariantCulture),
            savedGroupKarmaPoints.ToString(CultureInfo.InvariantCulture),
            savedCharacterKarma.ToString(CultureInfo.InvariantCulture),
            DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified)
                .ToString("O", CultureInfo.InvariantCulture),
            expenseAmount.ToString(CultureInfo.InvariantCulture),
            expenseReason,
            "Karma",
            false.ToString(CultureInfo.InvariantCulture),
            true.ToString(CultureInfo.InvariantCulture),
            karmaUndoType,
            nuyenUndoType,
            undoObjectId,
            undoQuantity.ToString(CultureInfo.InvariantCulture),
            undoExtra);

    public static string ComputeQualityIdempotencyKey(
        Guid ownerId,
        string workspaceId,
        long expectedWorkspaceRevision,
        long expectedSavedRevision,
        Guid transactionId,
        DateTime expenseDateLocal,
        string contentDigest,
        string runtimeDigest,
        CareerQualityReview review)
        => ComputeIdempotencyKey(
            Sr5CareerWizardRoutes.QualityReview,
            ownerId.ToString("D"),
            workspaceId,
            expectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            expectedSavedRevision.ToString(CultureInfo.InvariantCulture),
            transactionId.ToString("D"),
            DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified)
                .ToString("O", CultureInfo.InvariantCulture),
            contentDigest,
            runtimeDigest,
            JsonSerializer.Serialize(review));

    public static string ComputeKnowledgeSkillIdempotencyKey(
        Guid ownerId,
        string workspaceId,
        long expectedContentRevision,
        Guid actionId,
        Guid skillId,
        Guid? sourceSkillId,
        string characterRevision,
        string logicalRevision,
        string sourceRevision,
        string ruleDigest,
        string skillName,
        string skillType,
        string skillCategory,
        int basePoints,
        int previousKarmaPoints,
        int previousRating,
        int ratingMaximum,
        int availableKarma,
        int karmaCost,
        int savedSkillKarmaPoints,
        int savedCharacterKarma,
        DateTime expenseDateLocal,
        int expenseAmount,
        string expenseReason,
        string karmaUndoType,
        string nuyenUndoType,
        string undoObjectId,
        decimal undoQuantity,
        string undoExtra)
    {
        string sourceIdentity = sourceSkillId?.ToString("D") ?? "custom";
        return ComputeIdempotencyKey(
            Sr5CareerWizardRoutes.KnowledgeSkillReview,
            ownerId.ToString("D"),
            workspaceId,
            expectedContentRevision.ToString(CultureInfo.InvariantCulture),
            actionId.ToString("D"),
            $"{skillId:D}:{sourceIdentity}",
            characterRevision,
            logicalRevision,
            sourceRevision,
            ruleDigest,
            skillName,
            skillType,
            skillCategory,
            basePoints.ToString(CultureInfo.InvariantCulture),
            previousKarmaPoints.ToString(CultureInfo.InvariantCulture),
            previousRating.ToString(CultureInfo.InvariantCulture),
            ratingMaximum.ToString(CultureInfo.InvariantCulture),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            karmaCost.ToString(CultureInfo.InvariantCulture),
            savedSkillKarmaPoints.ToString(CultureInfo.InvariantCulture),
            savedCharacterKarma.ToString(CultureInfo.InvariantCulture),
            DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified)
                .ToString("O", CultureInfo.InvariantCulture),
            expenseAmount.ToString(CultureInfo.InvariantCulture),
            expenseReason,
            "Karma",
            false.ToString(CultureInfo.InvariantCulture),
            false.ToString(CultureInfo.InvariantCulture),
            karmaUndoType,
            nuyenUndoType,
            undoObjectId,
            undoQuantity.ToString(CultureInfo.InvariantCulture),
            undoExtra);
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
    string SkillName,
    string SkillCategory,
    int BasePoints,
    int PreviousKarmaPoints,
    int RatingMaximum,
    Guid ActionId,
    DateTime ExpenseDateLocal,
    decimal ExpenseAmount,
    string ExpenseReason,
    string ExpenseType,
    bool ExpenseRefund,
    bool ExpenseForceCareerVisible,
    string KarmaUndoType,
    string NuyenUndoType,
    string UndoObjectId,
    decimal UndoQuantity,
    string UndoExtra,
    int PreviousRating,
    int TargetRating,
    int SavedKarma,
    string IdempotencyKey,
    Sr5CareerCheckpointPhase Phase)
{
    public const int CurrentSchemaVersion = 3;

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
            && !string.IsNullOrWhiteSpace(SkillName)
            && SkillName.Length <= CharacterCareerActiveSkillAdvanceRules.MaximumNameLength
            && !string.IsNullOrWhiteSpace(SkillCategory)
            && SkillCategory.Length <= CharacterCareerActiveSkillAdvanceRules.MaximumNameLength
            && BasePoints is >= 0 and <= CharacterCareerActiveSkillAdvanceRules.MaximumRating
            && PreviousKarmaPoints is >= 0 and <= CharacterCareerActiveSkillAdvanceRules.MaximumRating
            && RatingMaximum is >= 0 and <= CharacterCareerActiveSkillAdvanceRules.MaximumRating
            && ActionId != Guid.Empty
            && normalizedExpenseDate == ExpenseDateLocal
            && normalizedExpenseDate.Ticks % TimeSpan.TicksPerSecond == 0
            && normalizedExpenseDate >= CharacterCareerActiveSkillAdvanceRules.MinimumExpenseDate
            && normalizedExpenseDate <= CharacterCareerActiveSkillAdvanceRules.MaximumExpenseDate
            && ExpenseAmount < 0m
            && ExpenseAmount >= -CharacterCareerActiveSkillAdvanceRules.MaximumKarma
            && decimal.Truncate(ExpenseAmount) == ExpenseAmount
            && !string.IsNullOrWhiteSpace(ExpenseReason)
            && string.Equals(ExpenseType, "Karma", StringComparison.Ordinal)
            && !ExpenseRefund
            && !ExpenseForceCareerVisible
            && !string.IsNullOrWhiteSpace(KarmaUndoType)
            && !string.IsNullOrWhiteSpace(NuyenUndoType)
            && Guid.TryParse(UndoObjectId, out Guid undoObjectId)
            && undoObjectId == SkillId
            && UndoQuantity == 0m
            && UndoExtra is not null
            && PreviousRating >= 0
            && TargetRating == PreviousRating + 1
            && SavedKarma >= 0
            && IdempotencyKey.Length == 64
            && IdempotencyKey.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && string.Equals(
                IdempotencyKey,
                Sr5CareerActionPlan.ComputeActiveSkillIdempotencyKey(
                    OwnerId,
                    WorkspaceId,
                    ExpectedContentRevision,
                    ActionId,
                    SkillId,
                    SourceSkillId,
                    LogicalRevision,
                    SourceRevision,
                    RuleDigest,
                    SkillName,
                    SkillCategory,
                    BasePoints,
                    PreviousKarmaPoints,
                    RatingMaximum,
                    ExpenseDateLocal,
                    ExpenseAmount,
                    ExpenseReason,
                    KarmaUndoType,
                    NuyenUndoType,
                    UndoObjectId,
                    UndoQuantity,
                    UndoExtra,
                    PreviousRating,
                    TargetRating,
                    SavedKarma),
                StringComparison.Ordinal)
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
            draft.Quote.Name,
            draft.Quote.SkillCategory,
            draft.Quote.BasePoints,
            draft.Quote.KarmaPoints,
            draft.Quote.RatingMaximum,
            draft.Plan.ExpenseId,
            draft.Plan.ExpenseDateLocal,
            draft.Plan.ExpenseAmount,
            draft.Plan.ExpenseReason,
            "Karma",
            ExpenseRefund: false,
            ExpenseForceCareerVisible: false,
            draft.Plan.KarmaUndoType,
            draft.Plan.NuyenUndoType,
            draft.Plan.UndoObjectId,
            draft.Plan.UndoQuantity,
            draft.Plan.UndoExtra,
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
        if (!MatchesReviewedDraft(draft))
        {
            draft = null!;
            blocker = "The saved Career draft idempotency binding no longer matches its exact plan.";
            return false;
        }
        return true;
    }

    public bool MatchesReviewedDraft(Sr5CareerActiveSkillDraft draft)
        => Phase == Sr5CareerCheckpointPhase.Reviewed
           && MatchesActionDraft(draft);

    public bool MatchesActionDraft(Sr5CareerActiveSkillDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return IsStructurallyValid()
            && string.Equals(WorkspaceId, draft.WorkspaceId.Value, StringComparison.Ordinal)
            && OwnerId == draft.OwnerId
            && ExpectedContentRevision == draft.ExpectedContentRevision
            && SkillId == draft.Quote.Identity.SkillId
            && SourceSkillId == draft.Quote.Identity.SourceSkillId
            && string.Equals(LogicalRevision, draft.Quote.LogicalRevision, StringComparison.Ordinal)
            && string.Equals(SourceRevision, draft.Quote.SourceRevision, StringComparison.Ordinal)
            && string.Equals(RuleDigest, draft.Quote.RuleDigest, StringComparison.Ordinal)
            && string.Equals(SkillName, draft.Quote.Name, StringComparison.Ordinal)
            && string.Equals(SkillCategory, draft.Quote.SkillCategory, StringComparison.Ordinal)
            && BasePoints == draft.Quote.BasePoints
            && PreviousKarmaPoints == draft.Quote.KarmaPoints
            && RatingMaximum == draft.Quote.RatingMaximum
            && ActionId == draft.Plan.ExpenseId
            && ExpenseDateLocal == draft.Plan.ExpenseDateLocal
            && ExpenseAmount == draft.Plan.ExpenseAmount
            && string.Equals(ExpenseReason, draft.Plan.ExpenseReason, StringComparison.Ordinal)
            && string.Equals(ExpenseType, "Karma", StringComparison.Ordinal)
            && !ExpenseRefund
            && !ExpenseForceCareerVisible
            && string.Equals(KarmaUndoType, draft.Plan.KarmaUndoType, StringComparison.Ordinal)
            && string.Equals(NuyenUndoType, draft.Plan.NuyenUndoType, StringComparison.Ordinal)
            && string.Equals(UndoObjectId, draft.Plan.UndoObjectId, StringComparison.Ordinal)
            && UndoQuantity == draft.Plan.UndoQuantity
            && string.Equals(UndoExtra, draft.Plan.UndoExtra, StringComparison.Ordinal)
            && PreviousRating == draft.Quote.TotalBaseRating
            && TargetRating == draft.Quote.TotalBaseRating + 1
            && SavedKarma == draft.Plan.SavedCharacterKarma
            && string.Equals(draft.ActionPlan.IdempotencyKey, IdempotencyKey, StringComparison.Ordinal);
    }
}

internal sealed record Sr5CareerReviewedCheckpointAccess(
    Guid OwnerId,
    string WorkspaceId,
    long ExpectedContentRevision,
    Guid ActionId,
    string IdempotencyKey,
    int SchemaVersion,
    string RouteId,
    bool CharacterCreated,
    string? GameEdition)
{
    public static Sr5CareerReviewedCheckpointAccess FromCurrent(
        Guid currentOwnerId,
        Sr5CareerActiveSkillDraft draft,
        Sr5CareerRunnerBinding currentBinding)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(currentBinding);
        return new(
            currentOwnerId,
            currentBinding.WorkspaceId?.Value ?? string.Empty,
            currentBinding.ContentRevision,
            draft.Plan.ExpenseId,
            draft.ActionPlan.IdempotencyKey,
            Sr5CareerDraftCheckpoint.CurrentSchemaVersion,
            Sr5CareerWizardRoutes.ActiveSkillReview,
            currentBinding.Created,
            currentBinding.GameEdition);
    }

    public bool Owns(Sr5CareerDraftCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return Sr5CareerWizardCatalog.IsSr5CareerRunner(CharacterCreated, GameEdition)
            && OwnerId != Guid.Empty
            && !string.IsNullOrWhiteSpace(WorkspaceId)
            && ExpectedContentRevision > 0
            && ActionId != Guid.Empty
            && SchemaVersion == Sr5CareerDraftCheckpoint.CurrentSchemaVersion
            && string.Equals(
                RouteId,
                Sr5CareerWizardRoutes.ActiveSkillReview,
                StringComparison.Ordinal)
            && checkpoint.IsStructurallyValid()
            && checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            && checkpoint.SchemaVersion == SchemaVersion
            && string.Equals(checkpoint.RouteId, RouteId, StringComparison.Ordinal)
            && checkpoint.OwnerId == OwnerId
            && string.Equals(checkpoint.WorkspaceId, WorkspaceId, StringComparison.Ordinal)
            && checkpoint.ExpectedContentRevision == ExpectedContentRevision
            && checkpoint.ActionId == ActionId
            && string.Equals(checkpoint.IdempotencyKey, IdempotencyKey, StringComparison.Ordinal);
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
            "Active-skill, Knowledge/Language, attribute, and skill-group advancement have separate typed review boundaries. Skill-group mutation is Core-service-only and fails closed when atomic receipt persistence is unavailable."),
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
            "Review one governed run proposal and settle its character-local consequences atomically.",
            Sr5CareerWizardAvailability.Partial,
            "The native settlement wizard binds run/reward context, Heat, reputation, contacts, GM review, owner review, Core quote, CAS and receipt. It remains explicitly unavailable until a governed proposal catalog and concrete atomic workspace adapter are composed."),
        new(
            Sr5CareerWizardLane.Downtime,
            "Downtime",
            "Plan calendar weeks and execute only actions with exact rule authority.",
            Sr5CareerWizardAvailability.Partial,
            "Calendar CRUD plus active-skill, Knowledge/Language, attribute, and skill-group advancement are typed. Training time, healing, crafting and acquisitions are not composed yet."),
        new(
            Sr5CareerWizardLane.Corrections,
            "Corrections and receipts",
            "Inspect editable expense records and recover safely from stale revisions.",
            Sr5CareerWizardAvailability.Partial,
            "Skill-group applying recovery is bound to the shared durable mutation owner and the exact idempotent Core command. Its correction remains blocked until Core exposes atomic compensating receipt authority.")
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
    string SkillCategory,
    int BasePoints,
    int SavedSkillKarmaPoints,
    int RatingMaximum,
    int PreviousRating,
    int SavedRating,
    int KarmaCost,
    int SavedKarma,
    int NextKarmaCost,
    bool CanAdvanceAgain,
    CharacterCareerActiveSkillAdvanceBlocker NextAdvanceBlocker,
    Guid ExpenseId,
    DateTime ExpenseDateLocal,
    string ExpenseReason,
    string ExpenseType,
    bool ExpenseRefund,
    bool ExpenseForceCareerVisible,
    string KarmaUndoType,
    string NuyenUndoType,
    string UndoObjectId,
    decimal UndoQuantity,
    string UndoExtra,
    string ReviewedRuleDigest,
    string LogicalRevision,
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
    string Message,
    string AuthorityProof);
