using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal sealed partial class Sr6CreationFoundationPage
{
    private void BuildLifestyleEditor(IReadOnlyList<Sr6CreationLifestyleOption> catalog, Func<bool> current,
        Action<Sr6CreationFoundationSelection> change)
    {
        _selection = _selection! with { Lifestyle = _selection.Lifestyle ?? new("", 1) };
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("LifestyleHelp"), NativeTheme.Muted));
        if (_state?.Selection?.Lifestyle is { } saved)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentSavedBudget"), NativeTheme.Muted));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.LifestyleBudget(saved)));
        }
        else if (_state?.Selection is { } foundation)
            _body.Add(NativeTheme.Body(foundation.Gear is { } gear ? Sr6CreationCopy.GearBudget(gear)
                : Sr6CreationCopy.GearResources(foundation.Karma?.ResourcesNuyen ?? foundation.Budget.ResourcesNuyen)));

        var options = catalog.ToArray();
        var picker = new Picker { Title = Sr6CreationCopy.Text("LifestyleChoose"), AutomationId = "sr6-lifestyle-choice" };
        foreach (var option in options) picker.Items.Add(Sr6CreationCopy.LifestyleOption(option));
        picker.SelectedIndex = Array.FindIndex(options, row => row.Id == _selection.Lifestyle.LifestyleId);
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!current()) return;
            string id = picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Length ? options[picker.SelectedIndex].Id : "";
            change(_selection with { Lifestyle = _selection.Lifestyle! with { LifestyleId = id } });
            // Keep the picker attached while its native selection callback is active.
        };
        _body.Add(picker);
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("LifestyleMonths")));
        var months = new Entry { Keyboard = Keyboard.Numeric, Text = _selection.Lifestyle.Months.ToString(CultureInfo.InvariantCulture),
            AutomationId = "sr6-lifestyle-months" };
        months.TextChanged += (_, _) =>
        {
            if (current()) change(_selection with { Lifestyle = _selection.Lifestyle! with
                { Months = int.TryParse(months.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : 0 } });
        };
        _body.Add(months);
    }
}
