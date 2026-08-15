using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class NativeDialogPage : ContentPage
{
    private readonly RunnerSessionCoordinator _coordinator;
    private bool _closing;

    public NativeDialogPage(RunnerSessionCoordinator coordinator, DesktopDialogState dialog)
    {
        _coordinator = coordinator;
        BackgroundColor = NativeTheme.Paper;
        Title = dialog.Title;
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Close",
            Command = new Command(async () => await CloseAsync(updatePresenter: true))
        });
        Render(dialog);
    }

    public event EventHandler? Closed;

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(updatePresenter: true);
        return true;
    }

    private void Render(DesktopDialogState dialog)
    {
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 32),
            Spacing = 16
        };
        body.Add(NativeTheme.Eyebrow("Runner setup"));
        body.Add(NativeTheme.Title(dialog.Title, 24));
        if (!string.IsNullOrWhiteSpace(dialog.Message))
        {
            body.Add(NativeTheme.Body(dialog.Message, NativeTheme.Muted));
        }

        foreach (DesktopDialogField field in dialog.Fields)
        {
            if (string.Equals(field.LayoutSlot, DesktopDialogFieldLayoutSlots.Hidden, StringComparison.Ordinal))
            {
                continue;
            }

            body.Add(CreateField(field));
        }

        Grid actions = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        int index = 0;
        foreach (DesktopDialogAction action in dialog.Actions)
        {
            Button button = action.IsPrimary
                ? NativeTheme.PrimaryButton(action.Label)
                : NativeTheme.SecondaryButton(action.Label);
            button.AutomationId = $"dialog-action-{Token(action.Id)}";
            button.Clicked += async (_, _) => await ExecuteAsync(action.Id);
            actions.Add(button, index % 2, index / 2);
            index++;
        }
        if (dialog.Actions.Count > 0)
        {
            body.Add(actions);
        }

        Content = new ScrollView { Content = body };
    }

    private View CreateField(DesktopDialogField field)
    {
        VerticalStackLayout fieldLayout = new() { Spacing = 7 };
        Label label = NativeTheme.Body(field.Label);
        label.FontAttributes = FontAttributes.Bold;
        fieldLayout.Add(label);

        if (field.Options is { Count: > 0 })
        {
            IReadOnlyList<DesktopDialogFieldOption> options = field.Options;
            int selectedIndex = options.ToList().FindIndex(option =>
                string.Equals(option.Value, field.Value, StringComparison.Ordinal));
            Picker picker = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                Title = string.IsNullOrWhiteSpace(field.Placeholder) ? $"Choose {field.Label}" : field.Placeholder,
                ItemsSource = options.Select(static option => option.Label).ToArray(),
                SelectedIndex = selectedIndex,
                IsEnabled = !field.IsReadOnly,
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text,
                HeightRequest = 52
            };
            picker.SelectedIndexChanged += async (_, _) =>
            {
                if (picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Count)
                {
                    await UpdateFieldAsync(field.Id, options[picker.SelectedIndex].Value);
                }
            };
            fieldLayout.Add(picker);
            return NativeTheme.Card(fieldLayout, new Thickness(14));
        }

        if (string.Equals(field.InputType, "checkbox", StringComparison.OrdinalIgnoreCase))
        {
            Switch toggle = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                IsToggled = bool.TryParse(field.Value, out bool enabled) && enabled,
                IsEnabled = !field.IsReadOnly,
                OnColor = NativeTheme.Signal
            };
            toggle.Toggled += async (_, args) => await UpdateFieldAsync(field.Id, args.Value ? "true" : "false");
            fieldLayout.Add(toggle);
            return NativeTheme.Card(fieldLayout, new Thickness(14));
        }

        if (field.IsMultiline)
        {
            Editor editor = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                Text = field.Value,
                Placeholder = field.Placeholder,
                IsReadOnly = field.IsReadOnly,
                AutoSize = EditorAutoSizeOption.TextChanges,
                MinimumHeightRequest = 110,
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text
            };
            editor.Unfocused += async (_, _) => await UpdateFieldAsync(field.Id, editor.Text);
            fieldLayout.Add(editor);
        }
        else
        {
            Entry entry = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                Text = field.Value,
                Placeholder = field.Placeholder,
                IsReadOnly = field.IsReadOnly,
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text,
                Keyboard = string.Equals(field.InputType, "number", StringComparison.OrdinalIgnoreCase)
                    ? Keyboard.Numeric
                    : Keyboard.Default
            };
            entry.Unfocused += async (_, _) => await UpdateFieldAsync(field.Id, entry.Text);
            fieldLayout.Add(entry);
        }

        return NativeTheme.Card(fieldLayout, new Thickness(14));
    }

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());

    private async Task UpdateFieldAsync(string fieldId, string? value)
    {
        try
        {
            await _coordinator.UpdateDialogFieldAsync(fieldId, value);
            DesktopDialogState? next = _coordinator.State.ActiveDialog;
            if (next is not null && RequiresStructuralRerender(next, fieldId))
            {
                Title = next.Title;
                Render(next);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
    }

    private static bool RequiresStructuralRerender(DesktopDialogState dialog, string fieldId)
        => string.Equals(dialog.Id, "dialog.new_character.priority_workflow", StringComparison.Ordinal)
            && (string.Equals(fieldId, "newCharacterMetatypeCategory", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterMetatype", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterMetavariant", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPriorityHeritage", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPriorityAttributes", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPriorityTalent", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPrioritySkills", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPriorityResources", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPriorityTalentChoice", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPrioritySkillChoice1", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPrioritySkillChoice2", StringComparison.Ordinal)
                || string.Equals(fieldId, "newCharacterPrioritySkillChoice3", StringComparison.Ordinal));

    private async Task ExecuteAsync(string actionId)
    {
        try
        {
            await _coordinator.ExecuteDialogActionAsync(actionId);
            DesktopDialogState? next = _coordinator.State.ActiveDialog;
            if (next is null)
            {
                await CloseAsync(updatePresenter: false);
            }
            else
            {
                Title = next.Title;
                Render(next);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
    }

    private async Task CloseAsync(bool updatePresenter)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            if (updatePresenter)
            {
                await _coordinator.CloseDialogAsync();
            }
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync();
            }
            Closed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _closing = false;
        }
    }
}
