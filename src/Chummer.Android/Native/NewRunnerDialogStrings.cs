using Chummer.Presentation.Overview;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Display only. Bindings, editable values and option identities stay canonical.</summary>
internal static class NewRunnerDialogStrings
{
    internal const string DialogId = "dialog.new_character";

    // The presenter accepts only the canonical method-specific profile. The
    // legacy Core Rulebook value is an alias, not a free-form settings chooser.
    internal static string? FixedSettingsDescription(string dialogId, DesktopDialogField field,
        DesktopDialogState? dialog)
    {
        if (dialogId != DialogId || field.Id != "newCharacterSetting"
            || field.Label != "Character Setting" || field.Value != "Core Rulebook") return null;
        bool sr5 = dialog?.Fields.SingleOrDefault(item => item.Id == "newCharacterRulesetId")?.Value == "sr5";
        return sr5
            ? PhoneStrings.Get("NewRunnerFixedSettingsAllSources",
                "Standard creation rules · all sources enabled. The profile follows your build method.")
            : PhoneStrings.Get("NewRunnerFixedSettings",
                "Standard creation rules. The profile follows your ruleset and build method.");
    }

    internal static NativeDialogScopedField Project(DesktopDialogState dialog, DesktopDialogField field,
        NativeDialogScopedField original)
    {
        if (dialog.Id != DialogId) return original;
        string? key = (field.Id, field.Label) switch
        {
            ("newCharacterName", "Character Name") => "NewRunnerFieldName",
            ("newCharacterAlias", "Alias") => "NewRunnerFieldAlias",
            ("newCharacterRulesetId", "Ruleset") => "NewRunnerFieldRuleset",
            ("newCharacterBuildMethod", "Build Method") => "NewRunnerFieldMethod",
            ("newCharacterSetting", "Character Setting") => "NewRunnerFieldSettings",
            ("newCharacterIgnoreRules", "Ignore Character Creation Rules") => "NewRunnerFieldIgnoreRules",
            _ => null
        };
        return original with
        {
            Label = key is null ? original.Label : PhoneStrings.Get(key, original.Label),
            Options = field.Id == "newCharacterBuildMethod" && field.Label == "Build Method"
                ? original.Options?.Select(option => option with { Label = MethodLabel(option) }).ToArray()
                : original.Options
        };
    }

    internal static string MethodLabel(DesktopDialogFieldOption option)
    {
        string? key = option.Label switch
        {
            "Priority" when option.Value == "Priority" || option.Value == Sr6CharacterCreationBuildMethods.Priority => "NewRunnerMethodPriority",
            "Sum-to-Ten" when option.Value == "SumToTen" || option.Value == Sr6CharacterCreationBuildMethods.SumToTen => "NewRunnerMethodSumToTen",
            "Karma" when option.Value == "Karma" => "NewRunnerMethodKarma",
            "Life Modules" when option.Value == "LifeModule" => "NewRunnerMethodLifeModules",
            "Point Buy" when option.Value == Sr6CharacterCreationBuildMethods.PointBuy => "NewRunnerMethodPointBuy",
            "Life Path" when option.Value == Sr6CharacterCreationBuildMethods.LifePath => "NewRunnerMethodLifePath",
            "Karma (SR6)" when option.Value == Sr6CharacterCreationBuildMethods.Karma => "NewRunnerMethodKarmaSr6",
            "BP" when option.Value == "BP" => "NewRunnerMethodBp",
            _ => null
        };
        return key is null ? option.Label : PhoneStrings.Get(key, option.Label);
    }

    internal static string PickerTitle(string dialogId, DesktopDialogField field, string displayLabel)
        => dialogId == DialogId && field.Id is "newCharacterRulesetId" or "newCharacterBuildMethod"
            ? PhoneStrings.Format("NewRunnerChooseField", "Choose {0}", displayLabel)
            : string.IsNullOrWhiteSpace(field.Placeholder) ? $"Choose {displayLabel}" : field.Placeholder;
}
