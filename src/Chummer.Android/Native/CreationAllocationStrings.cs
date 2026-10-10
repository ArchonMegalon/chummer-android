using System.Globalization;
using System.Resources;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed UI copy for the native SR5 creation Attribute, Skills, and Metatype
/// allocation surfaces. Core-projected identifiers, digests, and blockers deliberately
/// retain their exact values; translated labels and guidance are display-only, never admission logic.
/// </summary>
public static class CreationAllocationStrings
{
    public static string BasicsSummary => Get("Creation.BasicsSummary", "View your runner's rules and enabled sourcebooks. These settings cannot be changed here.");

    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.CreationAllocationStrings",
        typeof(CreationAllocationStrings).Assembly);

    public static string Get(string key, string englishFallback)
        => Get(key, englishFallback, CultureInfo.CurrentUICulture);

    public static string Get(string key, string englishFallback, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(englishFallback);
        ArgumentNullException.ThrowIfNull(culture);
        try
        {
            return Resources.GetString(key, culture) is { Length: > 0 } value
                ? value
                : englishFallback;
        }
        catch (MissingManifestResourceException)
        {
            return englishFallback;
        }
        catch (MissingSatelliteAssemblyException)
        {
            return englishFallback;
        }
    }

    public static string Format(string key, string englishFallback, params object?[] arguments)
        // App language and regional number/date formats are independent settings.
        => string.Format(CultureInfo.CurrentCulture, Get(key, englishFallback), arguments);

    public static string Format(
        CultureInfo culture,
        string key,
        string englishFallback,
        params object?[] arguments)
        => string.Format(culture, Get(key, englishFallback, culture), arguments);

    public static string AttributeName(string attributeId)
        => attributeId switch
        {
            "BOD" => Get("Attribute.BOD", "Body"),
            "AGI" => Get("Attribute.AGI", "Agility"),
            "REA" => Get("Attribute.REA", "Reaction"),
            "STR" => Get("Attribute.STR", "Strength"),
            "CHA" => Get("Attribute.CHA", "Charisma"),
            "INT" => Get("Attribute.INT", "Intuition"),
            "LOG" => Get("Attribute.LOG", "Logic"),
            "WIL" => Get("Attribute.WIL", "Willpower"),
            "EDG" => Get("Attribute.EDG", "Edge"),
            "MAG" => Get("Attribute.MAG", "Magic"),
            "RES" => Get("Attribute.RES", "Resonance"),
            "ESS" => Get("Attribute.ESS", "Essence"),
            "DEP" => Get("Attribute.DEP", "Depth"),
            _ => attributeId
        };

    // Display-only explanations: no code, identity, permission or saved data is
    // changed. Every exact reason remains in the page's technical disclosure.
    public static string SkillBlocker(string code) => code switch
    {
            CharacterCreationSkillsBlockers.AllocationDuplicate
                or CharacterCreationSkillsBlockers.GroupAllocationDuplicate
                => Get("Skills.Message.Duplicate", "A skill or group is selected more than once. Remove the duplicate choice."),
            CharacterCreationSkillsBlockers.AllocationInvalid
                or CharacterCreationSkillsBlockers.RatingInvalid
                => Get("Skills.Message.Allocation", "Check this skill's rating and selection against the current limits."),
            CharacterCreationSkillsBlockers.ActiveBudgetExceeded
                => Get("Skills.Message.ActivePoints", "Not enough active skill points remain. Lower an allocation before reviewing again."),
            CharacterCreationSkillsBlockers.GroupBudgetExceeded
                => Get("Skills.Message.GroupPoints", "Not enough skill group points remain. Lower a group rating before reviewing again."),
            CharacterCreationSkillsBlockers.KnowledgeBudgetExceeded
                => Get("Skills.Message.KnowledgePoints", "Not enough knowledge skill points remain. Review your knowledge and language allocations."),
            CharacterCreationSkillsBlockers.GroupBroken
                => Get("Skills.Message.GroupMixed", "The group and its individual skill choices conflict. Review their ratings and specializations together."),
            CharacterCreationSkillsBlockers.GroupInvalid
                => Get("Skills.Message.GroupUnavailable", "This skill group is not available with the current choices."),
            CharacterCreationSkillsBlockers.SpecializationInvalid
                => Get("Skills.Message.Specialization", "Choose an available specialization for this skill, or remove the specialization."),
            CharacterCreationSkillsBlockers.NativeLanguageInvalid
                => Get("Skills.Message.NativeChoice", "Check the selected native language. It cannot also have a purchased rating or specialization."),
            CharacterCreationSkillsBlockers.NativeLanguageLimitExceeded
                => Get("Skills.Message.NativeLimit", "Too many native languages are selected. Remove one before reviewing again."),
            CharacterCreationSkillsBlockers.ExoticSkillUnsupported
                => Get("Skills.Message.Exotic", "This exotic skill is not supported by this editor. Your saved choices are not changed by opening it."),
            CharacterCreationSkillsBlockers.MovementRequirementUnmet
                => Get("Skills.Message.Movement", "This skill's movement requirement is not met by the current runner."),
            CharacterCreationSkillsBlockers.TalentAccessRequired
                => Get("Skills.Message.Talent", "The current Talent does not grant access to this skill."),
            CharacterCreationSkillsBlockers.KnowledgeContributionAuthorityUnsupported
                => Get("Skills.Message.KnowledgeRule", "This knowledge-point calculation is not supported here yet. Do not change the saved draft to work around it."),
            CharacterCreationSkillsBlockers.AttributesDraftInvalid
                or CharacterCreationSkillsBlockers.AttributesDraftRequired
                => Get("Skills.Message.Attributes", "Review and save your Attributes before reviewing Skills again."),
            CharacterCreationSkillsBlockers.AuthorityUnavailable
                or CharacterCreationSkillsBlockers.WorkspaceUnavailable
                or CharacterCreationSkillsBlockers.PersistenceAuthorityRequired
                => Get("Skills.Message.Unavailable", "The current runner or its rules could not be loaded safely. Reopen the runner before continuing."),
            CharacterCreationSkillsBlockers.DraftDuplicate
                => Get("Skills.Message.Unchanged", "These skill choices are already saved. Change a selection or return to character creation."),
            CharacterCreationSkillsBlockers.DraftConflict
                or CharacterCreationSkillsBlockers.DraftInvalid
                or CharacterCreationSkillsBlockers.ReceiptLedgerInvalid
                => Get("Skills.Message.History", "The saved Skills history needs checking. Reopen the runner; do not repeat a save whose result is uncertain."),
            CharacterCreationSkillsBlockers.IdempotencyConflict
                or CharacterCreationSkillsBlockers.IdempotencyKeyInvalid
                => Get("Skills.Message.SaveCheck", "This save request cannot be confirmed. Reopen the runner and check what was saved before trying again."),
            CharacterCreationSkillsBlockers.PostCommitRefreshRequired
                => Get("Skills.Message.SavedReopen", "Your skills were saved, but this view could not refresh. Reopen the runner; do not save these choices again."),
            CharacterCreationSkillsBlockers.PrerequisiteSourceDrift
                or CharacterCreationSkillsBlockers.PreviewDigestMismatch
                or CharacterCreationSkillsBlockers.RuntimeDrift
                or CharacterCreationSkillsBlockers.SkillsPriorityAuthorityInvalid
                or CharacterCreationSkillsBlockers.SkillsSourceDrift
                or CharacterCreationSkillsBlockers.StaleRawCharacterXmlDigest
                or CharacterCreationSkillsBlockers.StaleWorkspaceRevision
                => Get("Skills.Message.Reopen", "The runner or rules changed since this view was prepared. Reopen Skills to review the current choices."),
            CharacterCreationSkillsBlockers.ExplicitConfirmationRequired
                => Get("Skills.Message.Review", "Review the complete proposal and confirm it explicitly. Opening this page does not save changes."),
            CharacterCreationSkillsBlockers.NativeLanguageRequired
                => Get("Skills.NativeLanguageRequired", "Your skill choices are not saved yet. Choose a native language under Knowledge & languages before reviewing and saving."),
            CharacterCreationSkillsReReviewSchemas.Unavailable
                => Get("Skills.Message.Unavailable", "The current runner or its rules could not be loaded safely. Reopen the runner before continuing."),
            CharacterCreationSkillsReReviewSchemas.Stale
                => Get("Skills.Message.Reopen", "The runner or rules changed since this view was prepared. Reopen Skills to review the current choices."),
            CharacterCreationSkillsReReviewSchemas.ExplicitReviewRequired
                => Get("Skills.Message.Review", "Review the complete proposal and confirm it explicitly. Opening this page does not save changes."),
            _ => Get("Skills.Message.Unknown", "This choice cannot be confirmed. Check the current choices; technical details contain the exact reason.")
    };

    // Exact blocker codes remain in the technical disclosure. Unknown codes never
    // become permission to spend points or a guessed rule explanation.
    public static string AttributeBlocker(string code) => code switch
    {
        "creation-attributes-special-not-enabled" or "creation-attributes-attribute-disabled"
            => Get("Attributes.NotEnabledByTalent", "Not enabled by this Talent"),
        "creation-attributes-essence-not-spendable"
            => Get("Attributes.EssenceNotSpendable", "Essence cannot be raised with attribute points or Karma here."),
        "creation-attributes-special-points-exceeded"
            => Get("Attributes.SpecialPointsExceeded", "Not enough special attribute points remain."),
        "creation-attributes-normal-points-exceeded"
            => Get("Attributes.NormalPointsExceeded", "Not enough normal attribute points remain."),
        "creation-attributes-global-karma-exceeded"
            => Get("Attributes.KarmaExceeded", "Not enough creation Karma remains."),
        "creation-attributes-maximum-count-exceeded"
            => Get("Attributes.MaximumCountExceeded", "Too many attributes are at their natural maximum. Lower another attribute first."),
        "creation-attributes-stale-workspace-revision" or "creation-attributes-stale-raw-character-xml-digest"
            or "creation-attributes-preview-digest-mismatch"
            => Get("AttributesPreview.Reopen", "This review is no longer current. Return to your runner and reopen Attributes to check the latest choices."),
        "creation-attributes-confirm-outcome-unknown"
            => Get("AttributesPreview.SaveUncertain", "We couldn't confirm whether your choices were saved. Reopen your runner and check Attributes before making another change. Don't repeat this save."),
        "creation-attributes-post-commit-refresh-required"
            => Get("AttributesPreview.SavedReopen", "Your attribute choices were saved, but this view couldn't refresh. Reopen your runner to see them. Don't save these choices again."),
        _ => Get("Attributes.ChangeUnavailable", "This change is unavailable with the current choices and limits. Technical details contain the exact reason.")
    };
}
