using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal static class Sr6CreationCopy
{
    internal static string Text(string key) => CreationAllocationStrings.Get("Sr6." + key, key);
    internal static string Label(string id) => CreationAllocationStrings.Get("Sr6.Option." + id, id);
    internal static string FinishDomain(string id) => id switch
    {
        "equipment-runtime-stats" or "spell-runtime-stats" or "complex-form-runtime-stats" or "magical-tradition" or "passive-effect-conflict"
            => Text("FinishDomain." + id),
        _ => DraftStepTitle(id)
    };
    internal static string PassiveAttribute(Sr6CreationPassiveAttributeValue row) => row.Rating is null
        ? CreationAllocationStrings.Format("Sr6.PassiveUnresolved", "{0}: unresolved", Label(row.AttributeId))
        : CreationAllocationStrings.Format("Sr6.PassiveAttribute", "{0}: {1} natural + {2} permanent = {3}",
            Label(row.AttributeId), row.NaturalRating, row.PermanentBonus, row.Rating);
    internal static string PassiveSkill(Sr6CreationPassiveSkillValue row) => CreationAllocationStrings.Format("Sr6.PassiveSkill",
        "{0}: {1} natural + {2} permanent = {3}; noncombat only +{4} = {5}",
        Label(row.SkillId), row.NaturalRating, row.AlwaysBonus, row.AllUsesRating, row.NoncombatOnlyBonus, row.NoncombatRating);
    internal static string PassiveDerived(Sr6CreationDerivedValue row) => row.Value is null
        ? CreationAllocationStrings.Format("Sr6.PassiveUnresolved", "{0}: unresolved", Text("PassiveStat." + row.Id))
        : Text("PassiveStat." + row.Id) + "\n" + row.Calculation;
    internal static string NaturalMissing(string domain) => CreationAllocationStrings.Format("Sr6.NaturalMissing",
        "{0}: no saved allocation yet", Text(domain));
    internal static string NaturalAttribute(Sr6CreationNaturalAttributeValue row) => CreationAllocationStrings.Format("Sr6.NaturalAttribute",
        "{0}: {1} base + {2} attribute + {3} adjustment + {4} Karma increase = {5} (maximum {6})",
        Label(row.AttributeId), row.BaseValue, row.AttributePoints, row.AdjustmentPoints, row.KarmaIncrease, row.Rating, row.Maximum);
    internal static string NaturalSkill(Sr6CreationNaturalSkill row) => CreationAllocationStrings.Format("Sr6.NaturalSkill",
        "{0}: {1} pool + {2} Karma increase = rating {3}", Label(row.SkillId), row.PoolRating, row.KarmaIncrease, row.Rating)
        + string.Concat(row.Specializations.Select(specialty => "\n" + (row.SkillId == "ExoticWeapons"
            ? CreationAllocationStrings.Format("Sr6.NaturalExotic", "Weapon permission: {0} (no specialty dice bonus)", specialty.Subject)
            : CreationAllocationStrings.Format("Sr6.NaturalSpecialty", "{0}: +{1} dice only when applicable; GM review", specialty.Subject, specialty.DicePoolBonus))));
    internal static string NaturalNative(string name) => CreationAllocationStrings.Format("Sr6.NaturalNative", "Native language: {0}", name);
    internal static string NaturalKnowledge(string name) => CreationAllocationStrings.Format("Sr6.NaturalKnowledge", "Knowledge: {0}", name);
    internal static string NaturalLanguage(Sr6CreationNaturalLanguage row) => CreationAllocationStrings.Format("Sr6.NaturalLanguage",
        "{0}: {1} · comprehension bonus +{2}", row.Name, Text("NaturalLevel." + row.Level), row.ComprehensionBonus);
    internal static string DraftStepTitle(string id) => Text(id switch
    {
        "foundation" => "Title", "qualities" => "QualitiesTitle", "attributes" => "AttributeTitle",
        "skills" => "SkillTitle", "karma" => "KarmaTitle", "knowledge" => "KnowledgeTitle", "resources" => "FinishResources",
        "contacts" => "ContactsTitle", "talent" => "TalentTitle", "forms" => "FormsTitle",
        "spells" => "SpellsTitle", "powers" => "PowersTitle", "gear" => "GearTitle", "lifestyle" => "LifestyleTitle",
        _ => "DraftTitle"
    });
    internal static string DraftRemainder(Sr6CreationDraftRemainder remainder) => CreationAllocationStrings.Format("Sr6.DraftRemainder",
        "{0}: {1:0.##}", Text("DraftBudget." + remainder.Id), remainder.Amount);
    internal static string DraftBalances(Sr6CreationDraftBalances balances) => CreationAllocationStrings.Format("Sr6.DraftBalances",
        "Saved resources {0:N2} ¥ − equipment {1:N2} ¥ − lifestyle {2:N2} ¥ = {3:N2} ¥\nProjected starting cash {4:N2} ¥ · above carry-over limit {5:N2} ¥\nKarma remaining {6} · projected carry-over {7} · above limit {8}\nNothing has been discarded or applied to a finished runner.",
        balances.ResourcesNuyen, balances.GearSpentNuyen, balances.LifestyleSpentNuyen, balances.RemainingNuyen,
        balances.ProjectedStartingNuyen, balances.NuyenAboveCarryOver, balances.RemainingKarma,
        balances.ProjectedStartingKarma, balances.KarmaAboveCarryOver);
    internal static string LifestyleOption(Sr6CreationLifestyleOption option) => CreationAllocationStrings.Format("Sr6.LifestyleOption",
        "{0} · {1:N2} ¥/month", Text("Lifestyle." + option.Id), option.MonthlyNuyen);
    internal static string LifestyleBudget(Sr6CreationLifestylePreview preview) => CreationAllocationStrings.Format("Sr6.LifestyleBudget",
        "{0}: {1} × {2:N2} ¥ = {3:N2} ¥\nEquipment {4:N2} ¥ · total resources {5:N2} ¥ · remaining {6:N2} ¥\nProjected starting cash {7:N2} ¥ (limit {8:N2} ¥) · unspent above limit {9:N2} ¥",
        Text("Lifestyle." + preview.Option.Id), preview.Selection.Months, preview.Option.MonthlyNuyen,
        preview.LifestyleSpentNuyen, preview.GearSpentNuyen, preview.ResourcesNuyen, preview.RemainingNuyen,
        preview.ProjectedStartingNuyen, preview.MaximumCarryOverNuyen, preview.UnspentAboveCarryOver);
    internal static string GearName(Sr6CreationGearOption option) => option.SourceName
        + (option.Rating is { } rating ? " · " + CreationAllocationStrings.Format("Sr6.GearRating", "rating {0}", rating) : "");
    internal static string GearOption(Sr6CreationGearOption option) => CreationAllocationStrings.Format("Sr6.GearOption",
        "{0} · availability {1}, {2}\nBase {3:N2} ¥ + {4}% size surcharge = {5:N2} ¥ each · {6}",
        GearName(option), option.Availability, Text("GearLegality." + option.Legality), option.BasePrice,
        option.SizeSurchargePercent, option.UnitPrice, option.SourceAnchorId)
        + (option.Available ? "" : "\n" + Blocker(option.UnavailableReason!));
    internal static string GearValue(Sr6CreationGearValue item) => CreationAllocationStrings.Format("Sr6.GearValue",
        "{0}: {1} × {2:N2} ¥ = {3:N2} ¥", GearName(item.Option), item.Choice.Quantity, item.Option.UnitPrice, item.TotalPrice);
    internal static string GearResources(decimal nuyen) => CreationAllocationStrings.Format("Sr6.GearResources", "Purchase budget: {0:N2} ¥", nuyen);
    internal static string GearBudget(Sr6CreationGearPreview preview) => CreationAllocationStrings.Format("Sr6.GearBudget",
        "Spent {0:N2} / {1:N2} ¥ · remaining {2:N2} ¥\nCarry-over limit {3:N2} ¥ · unspent above limit {4:N2} ¥",
        preview.SpentNuyen, preview.ResourcesNuyen, preview.RemainingNuyen, preview.MaximumCarryOverNuyen, preview.UnspentAboveCarryOver);
    internal static string ContactOptions(Sr6CreationContactOptions options) => CreationAllocationStrings.Format("Sr6.ContactsOptions",
        "Charisma {0} × {1} = {2} contact points. Connection and Loyalty each range from {3} to {4}. Cost: Connection + Loyalty. Separate from Karma and character points.",
        options.Charisma, options.PointsPerCharisma, options.PointBudget, options.MinimumRating, options.MaximumRating);
    internal static string ContactBudget(Sr6CreationContactPreview preview) => CreationAllocationStrings.Format("Sr6.ContactsBudget",
        "Contacts: {0}/{1} points spent · {2} remaining · Charisma {3}",
        preview.PointsSpent, preview.Options.PointBudget, preview.PointsRemaining, preview.Options.Charisma);
    internal static string ContactValue(Sr6CreationContactValue value) => CreationAllocationStrings.Format("Sr6.ContactsValue",
        "{0} · {1} · Connection {2} + Loyalty {3} = {4} points", value.Contact.Name,
        value.Contact.Role ?? "", value.Contact.Connection, value.Contact.Loyalty, value.PointCost);
    internal static string QualityName(Sr6CreationQualityOption option) => option.Name
        + (option.AttributeId is { } attribute ? " (" + Label(attribute) + ")" : "")
        + (option.SkillId is { } skill ? " (" + Label(skill) + ")" : "")
        + (option.Rating is { } rating ? " · " + CreationAllocationStrings.Format("Sr6.QualityLevel", "level {0}", rating.Total) : "");
    internal static string QualityValue(Sr6CreationQualityOption option) => CreationAllocationStrings.Format("Sr6.QualityValue",
        "{0} · Karma cost {1} (negative = bonus) · {2}", QualityName(option), option.KarmaCost, option.SourceAnchorId)
        + (option.Rating is { } rating ? "\n" + CreationAllocationStrings.Format("Sr6.QualityRating",
            "{0} total − {1} innate = {2} purchased · {3} Karma per purchased level (negative = bonus)",
            rating.Total, rating.Innate, rating.Purchased, rating.KarmaPerLevel) : "")
        + (option.Available ? "" : " · " + Blocker(option.UnavailableReason!));
    internal static string QualityBudget(Sr6CreationQualityPreview preview) => CreationAllocationStrings.Format("Sr6.QualityBudget",
        "Qualities: {0}/{1} · advantages {2} Karma · disadvantages +{3} Karma · net bonus {4}/{5}\nRemaining customization budget: {6} Karma before other purchases",
        preview.Values.Count, preview.MaximumChoices, preview.PositiveKarmaCost, preview.NegativeKarmaBonus,
        preview.NetKarmaBonus, preview.MaximumNetBonus, preview.CustomizationKarma);
    internal static string KarmaLanguageLevel(string? level) => Text(level is null ? "KarmaLanguageNew" : "KarmaLanguageLevel." + level);
    internal static string KarmaKnowledgeHelp(Sr6CreationKarmaKnowledgeOptions options) => CreationAllocationStrings.Format("Sr6.KarmaKnowledgeHelp",
        "New knowledge: {0} Karma each. Languages: {1} Karma per additional level, up to Expert. Free pool choices and native language ({2}) stay separate. Core checks duplicates and recalculates the shared Karma budget.",
        options.KnowledgeKarmaCost, options.LanguageLevelKarmaCost, options.NativeLanguage);
    internal static string KarmaKnowledgeValue(Sr6CreationKarmaKnowledgeValue value) => CreationAllocationStrings.Format("Sr6.KarmaKnowledgeValue",
        "Knowledge: {0} · {1} Karma · GM review", value.Name, value.KarmaCost);
    internal static string KarmaLanguageValue(Sr6CreationKarmaLanguageValue value) => CreationAllocationStrings.Format("Sr6.KarmaLanguageValue",
        "{0}: {1} → {2} · {3} new levels · {4} Karma · comprehension +{5}", value.Name,
        KarmaLanguageLevel(value.BaseLevel), KarmaLanguageLevel(value.Level), value.LevelsPurchased, value.KarmaCost, value.ComprehensionBonus);
    internal static string KarmaSpecializationHelp(Sr6CreationKarmaSpecializationOptions options) => CreationAllocationStrings.Format("Sr6.KarmaSpecializationHelp",
        "A specialty costs {0} Karma; final skill rating must be at least {1}. One specialty per skill across pool and Karma purchases. Exotic Weapons may have several, without a bonus; the first comes free with the skill. Names require GM review. Expertise and Karma spells/forms are prohibited during creation.", options.KarmaCost, options.MinimumRating);
    internal static string KarmaSpecializationOption(Sr6CreationKarmaSpecializationOption option) => CreationAllocationStrings.Format("Sr6.KarmaSpecializationOption",
        "Before Karma: rating {0} · existing pool specialties: {1} · dice bonus +{2}", option.BaseRating, string.Join(", ", option.PoolSpecializations), option.DicePoolBonus)
        + (option.Available ? "" : " · " + Blocker(option.UnavailableReason!));
    internal static string KarmaSpecializationValue(Sr6CreationKarmaSpecializationValue value) => CreationAllocationStrings.Format("Sr6.KarmaSpecializationValue",
        "{0}: {1} · {2} Karma · dice bonus +{3}", Label(value.SkillId), value.Subject, value.KarmaCost, value.DicePoolBonus);
    internal static string KarmaOptions(Sr6CreationKarmaOptions options) => CreationAllocationStrings.Format("Sr6.KarmaOptions",
        "Customization Karma: {0} · {1:N0} ¥ per Karma · carry over at most {2}", options.KarmaBudget, options.NuyenPerKarma, options.MaximumCarryOver);
    internal static string KarmaOption(Sr6CreationKarmaOption option) => CreationAllocationStrings.Format("Sr6.KarmaOption",
        "Before Karma: {0} · up to {1} additional levels", option.BaseRating, option.MaximumIncrease)
        + (option.Available ? "" : " · " + Blocker(option.UnavailableReason!));
    internal static string KarmaChoice(Sr6CreationKarmaIncrease row) => CreationAllocationStrings.Format("Sr6.KarmaChoice",
        "{0}: +{1} levels · {2}", Label(row.Id), row.Increase, row.FirstExoticSpecialization ?? "");
    internal static string KarmaValue(Sr6CreationKarmaValue row) => CreationAllocationStrings.Format("Sr6.KarmaValue",
        "{0}: {1} → {2} · Karma {3} = {4} · {5}", Label(row.Id), row.BaseRating, row.Rating,
        string.Join(" + ", row.Steps.Select(step => step.KarmaCost)), row.KarmaCost, row.FirstExoticSpecialization ?? "");
    internal static string KarmaBudget(Sr6CreationKarmaPreview preview) => CreationAllocationStrings.Format("Sr6.KarmaBudget",
        "Karma: {0}/{1} spent · {2} left ({3} above carry-over cap)\nCash: {4} Karma → {5:N0} ¥ · total resources {6:N0} ¥\nAdditional power points from Karma: {7}",
        preview.KarmaSpent, preview.KarmaBudget, preview.KarmaRemaining, preview.UnspentAboveCarryOver,
        preview.KarmaForNuyen, preview.AdditionalNuyen, preview.ResourcesNuyen, preview.AdditionalPowerPoints);
    internal static string PointBuyLimits(Sr6CreationPointBuyLimits limits) => CreationAllocationStrings.Format("Sr6.PointBuyLimits",
        "{0} CP. Free: {1} attribute, {2} skill, {3} adjustment points. Costs per extra point: {4}/{5}/{6} CP. Resource unit: {7:N0} ¥ for {8} CP. Awakened/technomancer: {9} CP. Separate customization Karma: {10}.",
        limits.CharacterPoints, limits.FreeAttributePoints, limits.FreeSkillPoints, limits.FreeAdjustmentPoints,
        limits.AttributePointCost, limits.SkillPointCost, limits.AdjustmentPointCost, limits.NuyenPerResourceUnit,
        limits.ResourceUnitCost, limits.AwakenedOrResonanceCost, limits.CustomizationKarma);
    internal static string PointBuyBudget(Sr6CreationPointBuyPreview preview) => CreationAllocationStrings.Format("Sr6.PointBuyBudget",
        "CP spent: {0}/{1} · remaining: {2}\nTalent {3} + attributes {4} + skills {5} + adjustment {6} + resources {7} + power points {9} + complex forms {10} + spells/rituals {11}. Separate base Karma before qualities: {8}.",
        preview.PointsSpent, preview.CharacterPoints, preview.PointsRemaining, preview.TalentCost,
        preview.AttributeCost, preview.SkillCost, preview.AdjustmentCost, preview.ResourceCost, preview.CustomizationKarma, preview.PowerPointCost ?? 0, preview.ComplexFormCost ?? 0, preview.SpellCost ?? 0);
    internal static string PowerName(Sr6CreationAdeptPowerOption option) => option.SourceName
        + (option.SubjectId is null ? "" : " (" + (option.SubjectKind == "sense" ? Text("PowersSense." + option.SubjectId) : Label(option.SubjectId)) + ")")
        + (option.SubjectKind == "skill" ? " · " + Text("PowersUse." + option.UseId) : "");
    internal static string PowerOption(Sr6CreationAdeptPowerOption option) => CreationAllocationStrings.Format("Sr6.PowersOption",
        "{0} PP per level · maximum {1} · {2}", option.QuarterPointsPerRating / 4m, option.MaximumRating, option.SourceAnchorId)
        + (option.MaximumRating == 0 ? " · " + Text("PowersRatingUnavailable") : "");
    internal static string PowerValue(Sr6CreationAdeptPowerValue value) => CreationAllocationStrings.Format("Sr6.PowersValue",
        "{0} · level {1} · {2} PP · {3}", PowerName(value.Option), value.Rating, value.QuarterPointsSpent / 4m, value.Option.SourceAnchorId);
    internal static string PowersBudget(Sr6CreationAdeptPowerPreview preview) => CreationAllocationStrings.Format("Sr6.PowersBudget",
        "Power points: {0}/{1} · remaining: {2}", preview.QuarterPointsSpent / 4m, preview.QuarterPointBudget / 4m, preview.QuarterPointsRemaining / 4m);
    internal static string SpellName(Sr6CreationSpellOption option) => Text("SpellsKind." + option.Kind) + ": " + option.SourceName;
    internal static string SpellsBudget(Sr6CreationSpellPreview preview) => CreationAllocationStrings.Format("Sr6.SpellsBudget",
        "Spells/rituals: {0}/{1} · slots remaining: {2} · free slots used: {3} · CP cost: {4}",
        preview.Spells.Count, preview.Limit, preview.SlotsRemaining, preview.FreeSlotsUsed, preview.CharacterPointCost);
    internal static string FormsBudget(Sr6CreationComplexFormPreview preview) => CreationAllocationStrings.Format("Sr6.FormsBudget",
        "Complex forms: {0}/{1} · slots remaining: {2} · free slots used: {3} · CP cost: {4}",
        preview.Forms.Count, preview.Limit, preview.SlotsRemaining, preview.FreeSlotsUsed, preview.CharacterPointCost);
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
        Sr6CreationContactBlockers.InvalidSelection => Text("ContactsInvalid"),
        Sr6CreationLifestyleBlockers.InvalidSelection => Text("LifestyleInvalid"),
        Sr6CreationLifestyleBlockers.Unavailable => Text("LifestyleUnavailable"),
        Sr6CreationLifestyleBlockers.BudgetExceeded => Text("LifestyleBudgetExceeded"),
        Sr6CreationGearBlockers.InvalidSelection => Text("GearInvalid"),
        Sr6CreationGearBlockers.CatalogUnavailable => Text("GearCatalogUnavailable"),
        Sr6CreationGearBlockers.AvailabilityExceeded => Text("GearAvailabilityExceeded"),
        Sr6CreationGearBlockers.BudgetExceeded => Text("GearBudgetExceeded"),
        Sr6CreationContactBlockers.AttributesRequired => Text("ContactsAttributesRequired"),
        Sr6CreationContactBlockers.RatingExceeded => Text("ContactsRating"),
        Sr6CreationContactBlockers.BudgetExceeded => Text("ContactsOverspend"),
        Sr6CreationQualityBlockers.InvalidSelection => Text("QualitiesInvalid"),
        Sr6CreationQualityBlockers.LimitExceeded => Text("QualitiesLimit"),
        Sr6CreationQualityBlockers.Unavailable => Text("QualitiesUnavailable"),
        Sr6CreationQualityBlockers.Conflict => Text("QualitiesConflict"),
        Sr6CreationQualityBlockers.BudgetExceeded => Text("QualitiesOverspend"),
        Sr6CreationQualityBlockers.AttributesRequired => Text("QualitiesAttributesRequired"),
        Sr6CreationQualityBlockers.RatingUnavailable => Text("QualitiesRatingUnavailable"),
        Sr6CreationKarmaBlockers.InvalidSelection => Text("KarmaInvalid"),
        Sr6CreationKarmaBlockers.AllocationsRequired => Text("KarmaAllocationsRequired"),
        Sr6CreationKarmaBlockers.RatingUnavailable => Text("KarmaRatingUnavailable"),
        Sr6CreationKarmaBlockers.BudgetExceeded => Text("KarmaOverspend"),
        Sr6CreationKarmaBlockers.SpecializationLimit => Text("KarmaSpecializationLimit"),
        Sr6CreationKarmaBlockers.SpecializationRatingRequired => Text("KarmaSpecializationRatingRequired"),
        Sr6CreationKarmaBlockers.KnowledgeRequired => Text("KarmaKnowledgeRequired"),
        Sr6CreationKarmaBlockers.KnowledgeConflict => Text("KarmaKnowledgeConflict"),
        Sr6CreationTalentBlockers.InvalidSelection => Text("TalentInvalid"),
        Sr6CreationTalentBlockers.AttributesRequired => Text("TalentAttributesRequired"),
        Sr6CreationTalentBlockers.AspectRequired => Text("SkillAspectRequired"),
        Sr6CreationTalentBlockers.PowerPointLimit => Text("TalentInvalid"),
        Sr6CreationComplexFormBlockers.InvalidSelection => Text("FormsInvalid"),
        Sr6CreationComplexFormBlockers.TalentRequired => Text("FormsTalentRequired"),
        Sr6CreationComplexFormBlockers.CatalogUnavailable => Text("FormsInvalid"),
        Sr6CreationComplexFormBlockers.LimitExceeded => Text("FormsLimit"),
        Sr6CreationSpellBlockers.InvalidSelection => Text("SpellsInvalid"),
        Sr6CreationSpellBlockers.TalentRequired => Text("SpellsTalentRequired"),
        Sr6CreationSpellBlockers.CatalogUnavailable => Text("SpellsInvalid"),
        Sr6CreationSpellBlockers.LimitExceeded => Text("SpellsLimit"),
        Sr6CreationAdeptPowerBlockers.InvalidSelection => Text("PowersInvalid"),
        Sr6CreationAdeptPowerBlockers.CatalogUnavailable => Text("PowersInvalid"),
        Sr6CreationAdeptPowerBlockers.TalentRequired => Text("PowersTalentRequired"),
        Sr6CreationAdeptPowerBlockers.RatingExceeded => Text("PowersRatingUnavailable"),
        Sr6CreationAdeptPowerBlockers.BudgetExceeded => Text("PowersOverspend"),
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
