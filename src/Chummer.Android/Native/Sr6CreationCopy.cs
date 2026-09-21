using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal static class Sr6CreationCopy
{
    internal static string Text(string key) => CreationAllocationStrings.Get("Sr6." + key, key);
    internal static string Label(string id) => CreationAllocationStrings.Get("Sr6.Option." + id, id);
    internal static string PointBuyLimits(Sr6CreationPointBuyLimits limits) => CreationAllocationStrings.Format("Sr6.PointBuyLimits",
        "{0} CP. Free: {1} attribute, {2} skill, {3} adjustment points. Costs per extra point: {4}/{5}/{6} CP. Resource unit: {7:N0} ¥ for {8} CP. Awakened/technomancer: {9} CP. Separate customization Karma: {10}.",
        limits.CharacterPoints, limits.FreeAttributePoints, limits.FreeSkillPoints, limits.FreeAdjustmentPoints,
        limits.AttributePointCost, limits.SkillPointCost, limits.AdjustmentPointCost, limits.NuyenPerResourceUnit,
        limits.ResourceUnitCost, limits.AwakenedOrResonanceCost, limits.CustomizationKarma);
    internal static string PointBuyBudget(Sr6CreationPointBuyPreview preview) => CreationAllocationStrings.Format("Sr6.PointBuyBudget",
        "CP spent: {0}/{1} · remaining: {2}\nTalent {3} + attributes {4} + skills {5} + adjustment {6} + resources {7} + power points {9}. Separate Karma: {8}.",
        preview.PointsSpent, preview.CharacterPoints, preview.PointsRemaining, preview.TalentCost,
        preview.AttributeCost, preview.SkillCost, preview.AdjustmentCost, preview.ResourceCost, preview.CustomizationKarma, preview.PowerPointCost ?? 0);
    internal static string TalentOptions(Sr6CreationTalentOptions options) => CreationAllocationStrings.Format("Sr6.TalentOptions",
        "Automatic power points: {0}. Maximum selectable whole power points: {1}; CP per point: {2}.",
        options.AutomaticPowerPoints, options.MaximumSelectedPowerPoints, options.CharacterPointsPerPowerPoint);
    internal static string TalentBudget(Sr6CreationTalentPreview preview) => CreationAllocationStrings.Format("Sr6.TalentBudget",
        "Magic {0} · Resonance {1}\nPower-point budget {2} · CP cost {3}\nSpells/rituals: limit {4}, free {5}\nAlchemical spells: limit {6}, free {7}\nComplex forms: limit {8}, free {9}\nCP per purchased spell/form: {10}",
        preview.Magic, preview.Resonance, preview.PowerPointBudget, preview.PowerPointCharacterPointCost,
        preview.SpellOrRitualLimit, preview.FreeSpellOrRitualSlots, preview.AlchemicalSpellLimit, preview.FreeAlchemicalSpellSlots,
        preview.ComplexFormLimit, preview.FreeComplexFormSlots, preview.CharacterPointsPerSpellOrForm);
    internal static string LanguageLevel(string level) => Text("KnowledgeLevel." + level);
    internal static string KnowledgePool(int logic) => CreationAllocationStrings.Format("Sr6.KnowledgePool",
        "Knowledge/language picks: {0} (Logic) + one free native language", logic);
    internal static string KnowledgeBudget(Sr6CreationKnowledgePreview preview) => CreationAllocationStrings.Format("Sr6.KnowledgeBudget",
        "Knowledge/language picks spent: {0} · remaining: {1} · Logic: {2}", preview.PointsSpent, preview.PointsRemaining, preview.Logic);
    internal static string LanguageValue(Sr6CreationLanguageValue value) => CreationAllocationStrings.Format("Sr6.KnowledgeLanguageValue",
        "{0}: {1} · {2} picks · comprehension +{3}", value.Name, LanguageLevel(value.Level), value.PointCost, value.ComprehensionBonus);
    internal static string SkillBudget(Sr6CreationSkillPreview preview) => CreationAllocationStrings.Format("Sr6.SkillBudget",
        "Skill points spent: {0} · remaining: {1}", preview.PointsSpent, preview.PointsRemaining);
    internal static string SkillValue(Sr6CreationSkillValue value) => CreationAllocationStrings.Format("Sr6.SkillValue",
        "{0}: rating {1} + specializations {2} = {3} points · {4}", Label(value.SkillId), value.Rating,
        value.SpecializationCost, value.TotalCost, string.Join("; ", value.Specializations));
    internal static string AttributeRange(Sr6CreationAttributeOption option) => CreationAllocationStrings.Format("Sr6.AttributeRange",
        "{0} · Base {1}, maximum {2}", Label(option.AttributeId), option.BaseValue, option.Maximum);
    internal static string AttributeBudget(Sr6CreationAttributePreview preview) => CreationAllocationStrings.Format("Sr6.AttributeBudget",
        "Unspent attribute points: {0} · Unspent adjustment points: {1}", preview.AttributePointsRemaining, preview.AdjustmentPointsRemaining);
    internal static string AttributeValue(Sr6CreationAttributeValue value) => CreationAllocationStrings.Format("Sr6.AttributeValue",
        "{0}: {1} + {2} attribute + {3} adjustment = {4} (maximum {5})", Label(value.AttributeId), value.BaseValue,
        value.AttributePoints, value.AdjustmentPoints, value.Value, value.Maximum);
    internal static string Budget(Sr6CreationFoundationPreview preview) => CreationAllocationStrings.Format("Sr6.Budget",
        "Attributes: {0} · Skills: {1} · Resources: {2:N0} ¥ · Adjustment: {3} · Base Magic: {4} · Base Resonance: {5}",
        preview.Budget.AttributePoints, preview.Budget.SkillPoints, preview.Budget.ResourcesNuyen,
        preview.Budget.MetatypeAdjustmentPoints, preview.BaseMagic, preview.BaseResonance);
    internal static string Blocker(string code) => code switch
    {
        Sr6CreationPriorityBlockers.CategoriesInvalid => Text("ChooseAll"),
        Sr6CreationPriorityBlockers.UniqueRanksRequired => Text("UniqueRanks"),
        Sr6CreationPriorityBlockers.SumToTenMismatch => Text("SumTen"),
        Sr6CreationFoundationBlockers.MetatypeUnavailable => Text("MetatypeRank"),
        Sr6CreationFoundationBlockers.TalentUnavailable => Text("TalentRank"),
        Sr6CreationFoundationBlockers.StaleBinding => Text("Stale"),
        Sr6CreationTalentBlockers.InvalidSelection => Text("TalentInvalid"),
        Sr6CreationTalentBlockers.AttributesRequired => Text("TalentAttributesRequired"),
        Sr6CreationTalentBlockers.AspectRequired => Text("SkillAspectRequired"),
        Sr6CreationTalentBlockers.PowerPointLimit => Text("TalentInvalid"),
        Sr6CreationPointBuyBlockers.InvalidSelection => Text("PointBuyInvalid"),
        Sr6CreationPointBuyBlockers.MethodMismatch => Text("PointBuyInvalid"),
        Sr6CreationPointBuyBlockers.SourceRequired => Text("PointBuySource"),
        Sr6CreationPointBuyBlockers.BudgetExceeded => Text("PointBuyOverspend"),
        Sr6CreationKnowledgeBlockers.InvalidSelection => Text("KnowledgeInvalid"),
        Sr6CreationKnowledgeBlockers.AttributesRequired => Text("KnowledgeAttributesRequired"),
        Sr6CreationKnowledgeBlockers.BudgetExceeded => Text("KnowledgeOverspend"),
        Sr6CreationSkillBlockers.InvalidAllocation => Text("SkillInvalid"),
        Sr6CreationSkillBlockers.SkillUnavailable => Text("SkillUnavailable"),
        Sr6CreationSkillBlockers.AspectRequired => Text("SkillAspectRequired"),
        Sr6CreationSkillBlockers.AstralPowerRequired => Text("SkillAstralPower"),
        Sr6CreationSkillBlockers.MaximumCountExceeded => Text("SkillMaximumCount"),
        Sr6CreationSkillBlockers.BudgetExceeded => Text("SkillOverspend"),
        Sr6CreationSkillBlockers.SpecializationInvalid => Text("SkillSpecializationInvalid"),
        Sr6CreationAttributeBlockers.InvalidAllocation => Text("AttributeInvalid"),
        Sr6CreationAttributeBlockers.PointKindUnavailable => Text("AttributePointKind"),
        Sr6CreationAttributeBlockers.RatingExceeded => Text("AttributeMaximum"),
        Sr6CreationAttributeBlockers.MaximumCountExceeded => Text("AttributeMaximumCount"),
        Sr6CreationAttributeBlockers.AttributeBudgetExceeded => Text("AttributeOverspend"),
        Sr6CreationAttributeBlockers.AdjustmentBudgetExceeded => Text("AdjustmentOverspend"),
        RunnerSessionCoordinator.Sr6SaveNeedsRefresh => Text("SavedReopen"),
        RunnerSessionCoordinator.Sr6OutcomeUnknown => Text("Unknown"),
        _ => code
    };
}
