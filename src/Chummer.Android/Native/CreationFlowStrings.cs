using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed copy for the native Contacts, Lifestyles, Qualities, and
/// Magic/Resonance creation flows. Rules values and authority diagnostics never pass through it.
/// </summary>
public static class CreationFlowStrings
{
    // Dashboard display only. Callers retain the original codes in technical
    // details and use Core readiness, never this text, to admit actions.
    internal static string DashboardBlocker(string code)
    {
        string known = FinalizationBlocker(code);
        if (!string.Equals(known, code, StringComparison.Ordinal)) return known;
        return code switch
        {
            "creation-prerequisite-dependent-attributes-draft-exists"
                => Get("Dashboard.MethodLocked", "Your saved Attributes depend on these choices, so Build method is locked. Continue with the remaining creation steps."),
            "creation-authority-loading"
                => Get("Dashboard.Loading", "Loading your creation choices…"),
            "creation-prerequisite-authority-load-failed" or "creation-attributes-authority-load-failed"
                or "creation-skills-authority-load-failed" or "creation-contacts-authority-load-failed"
                or "creation-resources-authority-load-failed"
                => Get("Dashboard.LoadFailed", "Some choices could not be loaded. Try loading them again or reopen this runner."),
            "creation-identity-draft-contract-unavailable"
                => Get("Dashboard.IdentityUnavailable", "Story details cannot be edited in this build."),
            _ => Get("Dashboard.StepBlocked", "This step needs attention before you can continue. See technical details for the exact reason.")
        };
    }

    // Presentation only: exact known blockers get an actionable explanation.
    // Unknown codes are retained verbatim, never converted into readiness.
    internal static string FinalizationBlocker(string code) => code switch
    {
        "creation-finalization-prerequisite-draft-required"
            or "creation-attributes-prerequisite-draft-required"
            or "creation-magic-resonance-prerequisite-draft-required"
            or "creation-qualities-prerequisite-draft-required"
            or "creation-resources-prerequisite-draft-required"
            => Get("Finalization.ReviewMethod", "Open Build method, review your priorities and metatype, and save your choices."),
        "creation-finalization-attributes-draft-required"
            or "creation-magic-resonance-attributes-draft-required"
            or "creation-qualities-attributes-draft-required"
            or "creation-skills-attributes-draft-required"
            => Get("Finalization.ReviewAttributes", "Open Attributes and review and save your allocation."),
        "creation-finalization-skills-draft-required"
            => Get("Finalization.ReviewSkills", "Open Skills and review and save your skill and language choices."),
        "creation-finalization-qualities-draft-required"
            => Get("Finalization.ReviewQualities", "Open Qualities and review and save your choices, even if you choose none."),
        "creation-finalization-magic-resonance-draft-required"
            => Get("Finalization.ReviewMagic", "Open Magic / Resonance and review and save the choices required for your talent."),
        "creation-finalization-resources-draft-required" or "creation-gear-resources-draft-required"
            => Get("Finalization.ReviewResources", "Open Resources and review and save your funding before choosing equipment."),
        "creation-finalization-gear-draft-required"
            => Get("Finalization.ReviewGear", "Open Gear and review and save your equipment choices."),
        "creation-skills-prerequisite-source-drift"
            => Get("Finalization.ReviewSkillSources", "Open Skills and review your choices against the current build method and enabled books."),
        _ => code
    };

    // Core's catalog projector emits OptionDisabled for a disabled source book.
    // Do not use this interpretation for generic mutation/admission failures.
    internal static string MagicCatalogBlocker(string code)
        => code == "creation-magic-resonance-option-disabled"
            ? Get("Magic.Blocker.SourceDisabled", "This source is disabled in this runner's saved rule settings. New runners enable all sources by default.")
            : MagicBlocker(code);

    // Display-only translation. Admission and diagnostics retain the original code.
    // Never echo arbitrary exceptions or invent readiness for an unknown reason.
    internal static string MagicBlocker(string code) => code switch
    {
        "creation-magic-resonance-authority-unavailable"
            or "creation-magic-resonance-workspace-unavailable"
            => Get("Magic.Blocker.Load", "Magic choices could not be loaded. Reopen this runner and try opening Magic / Resonance again."),
        "creation-magic-resonance-attributes-draft-invalid"
            or "creation-magic-resonance-attributes-draft-required"
            => Get("Magic.Blocker.Attributes", "Open Attributes, review your allocation and save it before choosing magic."),
        "creation-magic-resonance-prerequisite-draft-invalid"
            or "creation-magic-resonance-prerequisite-draft-required"
            or "creation-magic-resonance-priority-assignment-mismatch"
            or "creation-magic-resonance-metatype-prerequisite-unresolved"
            => Get("Magic.Blocker.Method", "Open Build method and review and save your metatype and talent choices."),
        "creation-magic-resonance-custom-data-drift"
            or "creation-magic-resonance-gm-policy-drift"
            or "creation-magic-resonance-prerequisite-source-drift"
            or "creation-magic-resonance-runtime-drift"
            or "creation-magic-resonance-source-drift"
            or "creation-magic-resonance-preview-digest-mismatch"
            or "creation-magic-resonance-stale-raw-character-xml-digest"
            or "creation-magic-resonance-stale-workspace-revision"
            or "creation-magic-resonance-draft-conflict"
            => Get("Magic.Blocker.Changed", "This view no longer matches your runner or rules. Reopen Magic / Resonance and review the current choices."),
        "creation-magic-resonance-draft-duplicate"
            or "creation-magic-resonance-draft-invalid"
            or "creation-magic-resonance-finalization-contribution-invalid"
            or "creation-magic-resonance-finalization-payload-invalid"
            => Get("Magic.Blocker.Draft", "These choices could not be verified. Review them again before saving; do not create a second copy of an unresolved draft."),
        "creation-magic-resonance-explicit-confirmation-required"
            => Get("Magic.Blocker.Confirm", "Review your choices and use Confirm to save them."),
        "creation-magic-resonance-metatype-forbidden"
            => Get("Magic.Blocker.Metatype", "Your metatype does not allow this talent. Review the talent selected in Build method."),
        "creation-magic-resonance-option-disabled"
            or "creation-magic-resonance-option-invalid"
            => Get("Magic.Blocker.Disabled", "This choice is not available for your current runner and rules."),
        "creation-magic-resonance-option-duplicate"
            => Get("Magic.Blocker.Duplicate", "This choice is already selected. Keep it only once."),
        "creation-magic-resonance-option-semantics-unsupported"
            or "creation-magic-resonance-talent-unsupported"
            or "creation-magic-resonance-power-budget-unsupported"
            => Get("Magic.Blocker.Unsupported", "This choice needs rules that this wizard does not support yet. Choose a supported option to continue."),
        "creation-magic-resonance-persistence-authority-required"
            or "creation-magic-resonance-receipt-ledger-invalid"
            or "creation-magic-resonance-idempotency-conflict"
            or "creation-magic-resonance-idempotency-key-invalid"
            or "creation-magic-resonance-commit-outcome-unknown"
            => Get("Magic.Blocker.SaveLocked", "The save result could not be verified. Keep this draft and use its recovery action; do not start a second save."),
        "creation-magic-resonance-post-commit-refresh-required"
            => Get("Magic.Blocker.RefreshSaved", "The choices were saved, but this view could not refresh. Reopen the runner to check the saved result; do not save again."),
        "creation-magic-resonance-power-budget-exceeded"
            => Get("Magic.Blocker.PowersOver", "Your adept powers exceed the available power points. Remove a power or reduce its level."),
        "creation-magic-resonance-power-budget-incomplete"
            => Get("Magic.Blocker.PowersLeft", "You still have power points to assign. Choose powers or adjust their levels to fill the remaining budget."),
        "creation-magic-resonance-power-selection-not-allowed"
            => Get("Magic.Blocker.PowersNotAllowed", "Your selected talent does not allow adept powers. Remove them from this draft."),
        "creation-magic-resonance-spell-budget-exceeded"
            => Get("Magic.Blocker.SpellsOver", "You have selected too many spells. Remove spells until the remaining count is zero."),
        "creation-magic-resonance-spell-budget-incomplete"
            => Get("Magic.Blocker.SpellsLeft", "You still have spells to choose. Open Spells and fill the remaining choices."),
        "creation-magic-resonance-spell-selection-not-allowed"
            => Get("Magic.Blocker.SpellsNotAllowed", "Your selected talent does not allow spells. Remove them from this draft."),
        "creation-magic-resonance-tradition-required"
            or "creation-magic-resonance-tradition-invalid"
            => Get("Magic.Blocker.Tradition", "Choose an available magical tradition before saving."),
        "creation-magic-resonance-stream-required"
            or "creation-magic-resonance-stream-invalid"
            => Get("Magic.Blocker.Stream", "Choose an available Resonance stream before saving."),
        "creation-magic-resonance-complex-form-budget-exceeded"
            => Get("Magic.Blocker.FormsOver", "You have selected too many complex forms. Remove forms until the remaining count is zero."),
        "creation-magic-resonance-complex-form-budget-incomplete"
            => Get("Magic.Blocker.FormsLeft", "You still have complex forms to choose. Open Complex forms and fill the remaining choices."),
        "creation-magic-resonance-complex-form-selection-not-allowed"
            => Get("Magic.Blocker.FormsNotAllowed", "Your selected talent does not allow complex forms. Remove them from this draft."),
        _ => Get("Magic.Blocker.Unknown", "This choice cannot be confirmed yet. Open Technical details for the reason; do not repeat a save whose result is still unknown.")
    };

    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.CreationFlowStrings",
        typeof(CreationFlowStrings).Assembly);

    public static string Get(string key, string fallback)
        => Get(key, fallback, CultureInfo.CurrentUICulture);

    public static string Get(string key, string fallback, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);
        try
        {
            return Resources.GetString(key, culture) is { Length: > 0 } value
                ? value
                : fallback;
        }
        catch (MissingManifestResourceException)
        {
            return fallback;
        }
    }

    public static string Format(string key, string fallback, params object?[] arguments)
        => Format(CultureInfo.CurrentUICulture, key, fallback, arguments);

    public static string Format(
        CultureInfo culture,
        string key,
        string fallback,
        params object?[] arguments)
        => string.Format(culture, Get(key, fallback, culture), arguments);
}
