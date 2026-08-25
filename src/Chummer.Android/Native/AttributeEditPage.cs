using System.Globalization;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class AttributeEditPage : NativePageBase
{
    private AttributeWorkbenchRow _row;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public AttributeEditPage(RunnerSessionCoordinator coordinator, AttributeWorkbenchRow row) : base(coordinator)
    {
        _row = row;
        Title = row.DisplayName;
        Content = new ScrollView { Content = _body };
        AutomationId = $"attribute-editor-{Token(row.AttributeName)}";
    }

    protected override void Refresh()
    {
        AttributeWorkbenchRow? current = AttributeWorkbenchProjector.BuildRows(
                Coordinator.State.ActiveSectionId,
                Coordinator.State.ActiveSectionJson ?? string.Empty)
            .FirstOrDefault(candidate => string.Equals(
                candidate.AttributeName,
                _row.AttributeName,
                StringComparison.OrdinalIgnoreCase));
        if (current is not null)
        {
            _row = current;
        }

        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(_row.CareerMode ? "Career" : "Create"));
        _body.Add(NativeTheme.Title(_row.DisplayName));

        VerticalStackLayout summary = new() { Spacing = 10 };
        summary.Add(NativeTheme.Metric("Current", _row.TotalValue.ToString(CultureInfo.InvariantCulture)));
        summary.Add(NativeTheme.Metric("Natural range", $"{_row.MetatypeMin}-{_row.MetatypeMax}"));
        summary.Add(NativeTheme.Metric("Augmented max", _row.MetatypeAugMax.ToString(CultureInfo.InvariantCulture)));
        _body.Add(NativeTheme.Card(summary));

        if (_row.CareerMode)
        {
            AddCareerActions();
        }
        else
        {
            AddCreationControls();
        }

        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error!, NativeTheme.Danger));
        }
    }

    private void AddCreationControls()
    {
        Picker basePicker = NumberPicker(
            $"attribute-base-{Token(_row.AttributeName)}",
            _row.EffectiveBaseMinimum,
            _row.EffectiveBaseMaximum,
            _row.BaseValue);
        basePicker.Title = "Base";
        basePicker.IsEnabled = _row.BaseUnlocked;

        Picker karmaPicker = NumberPicker(
            $"attribute-karma-{Token(_row.AttributeName)}",
            0,
            _row.EffectiveKarmaMaximum,
            _row.KarmaValue);
        karmaPicker.Title = "Karma";

        VerticalStackLayout controls = new() { Spacing = 8 };
        controls.Add(NativeTheme.FieldLabel("Base"));
        controls.Add(basePicker);
        controls.Add(NativeTheme.FieldLabel("Karma"));
        controls.Add(karmaPicker);
        _body.Add(NativeTheme.Card(controls));

        Button save = NativeTheme.PrimaryButton("Save changes");
        save.AutomationId = $"attribute-save-{Token(_row.AttributeName)}";
        save.Clicked += async (_, _) => await RunAsync(async () =>
        {
            int baseValue = SelectedNumber(basePicker, _row.BaseValue);
            int karmaValue = SelectedNumber(karmaPicker, _row.KarmaValue);
            if (_row.BaseUnlocked && baseValue != _row.BaseValue)
            {
                await Coordinator.ApplyAttributeEditAsync(new AttributeEditRequest(_row.AttributeName, "base", baseValue));
            }

            if (karmaValue != _row.KarmaValue)
            {
                await Coordinator.ApplyAttributeEditAsync(new AttributeEditRequest(_row.AttributeName, "karma", karmaValue));
            }
        });
        _body.Add(save);
    }

    private void AddCareerActions()
    {
        _body.Add(NativeTheme.Eyebrow("Advance"));
        _body.Add(NativeTheme.Body($"Available Karma: {_row.AvailableKarma}", NativeTheme.Muted));

        Button improve = NativeTheme.PrimaryButton(
            _row.UpgradeKarmaCost > 0 ? $"Improve · {_row.UpgradeKarmaCost} Karma" : "At maximum");
        improve.AutomationId = $"attribute-improve-{Token(_row.AttributeName)}";
        improve.IsEnabled = AttributeWorkbenchProjector.CanCareerAdvance(_row);
        improve.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyAttributeEditAsync(
            new AttributeEditRequest(_row.AttributeName, "improve", _row.TotalValue + 1)));
        _body.Add(improve);

        if (AttributeWorkbenchProjector.CanBurnEdge(_row))
        {
            Button burn = NativeTheme.SecondaryButton("Burn Edge");
            burn.AutomationId = "attribute-burn-edge";
            burn.TextColor = NativeTheme.Danger;
            burn.Clicked += async (_, _) =>
            {
                bool confirmed = await DisplayAlertAsync(
                    "Burn Edge?",
                    "This permanently reduces Edge by one.",
                    "Burn",
                    "Cancel");
                if (confirmed)
                {
                    await RunAsync(() => Coordinator.ApplyAttributeEditAsync(
                        new AttributeEditRequest(_row.AttributeName, "burn", 0)));
                }
            };
            _body.Add(burn);
        }
    }

    private static Picker NumberPicker(string automationId, int minimum, int maximum, int selected)
    {
        int safeMaximum = Math.Max(minimum, maximum);
        string[] values = Enumerable.Range(minimum, safeMaximum - minimum + 1)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return new Picker
        {
            AutomationId = automationId,
            ItemsSource = values,
            SelectedIndex = Math.Clamp(selected - minimum, 0, values.Length - 1),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
    }

    private static int SelectedNumber(Picker picker, int fallback)
        => picker.SelectedItem is string selected
            && int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
