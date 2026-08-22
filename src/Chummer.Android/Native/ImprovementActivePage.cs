using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ImprovementActivePage : NativePageBase
{
    private sealed record ImprovementOption(CharacterImprovementActiveState State, string Label);

    private readonly ImprovementActiveEditorState _editor;
    private readonly ImprovementOption[] _options;
    private readonly Picker _target;
    private readonly Switch _enabled;
    private readonly Button _save;

    public ImprovementActivePage(
        RunnerSessionCoordinator coordinator,
        ImprovementActiveEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.Improvements.Any(item =>
                !CharacterImprovementActiveRules.IsValidIdentity(item.Identity)))
        {
            throw new ArgumentException(
                "Improvement Active requires exact stable saved identities.",
                nameof(editor));
        }

        Title = "Improvement Active";
        AutomationId = "improvement-active-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career improvements"));
        body.Add(NativeTheme.Title("Active state"));
        body.Add(NativeTheme.Body(
            "Choose one direct saved Improvement, matching Chummer5's Improvement tree selection.",
            NativeTheme.Muted));

        _options = editor.Improvements.Select(item => new ImprovementOption(
            item,
            $"{item.DisplayName} · {IdentityToken(item.Identity)}"))
            .ToArray();
        _target = new Picker
        {
            Title = "Improvement",
            ItemsSource = _options,
            ItemDisplayBinding = new Binding(nameof(ImprovementOption.Label)),
            SelectedIndex = _options.Length == 0 ? -1 : 0,
            AutomationId = "improvement-active-target",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Improvement"));
        body.Add(_target);

        _enabled = new Switch
        {
            AutomationId = "improvement-active-toggle",
            OnColor = NativeTheme.Signal
        };
        SemanticProperties.SetDescription(_enabled, "Active");
        Grid toggleRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        toggleRow.Add(NativeTheme.FieldLabel("Active"), 0, 0);
        toggleRow.Add(_enabled, 1, 0);
        body.Add(NativeTheme.Card(toggleRow));

        _save = NativeTheme.PrimaryButton("Save active state");
        _save.AutomationId = "improvement-active-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void LoadSelectedState()
    {
        if (_target.SelectedIndex < 0 || _target.SelectedIndex >= _options.Length)
        {
            _enabled.IsToggled = false;
            return;
        }
        _enabled.IsToggled = _options[_target.SelectedIndex].State.Enabled;
    }

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision
            && _options.Length != 0;
        _target.IsEnabled = current;
        _enabled.IsEnabled = current;
        _save.IsEnabled = current;
    }

    private async Task SaveAsync()
    {
        if (_target.SelectedIndex < 0 || _target.SelectedIndex >= _options.Length)
        {
            await DisplayAlertAsync(
                "Improvement required",
                "Choose one exact saved Improvement before saving.",
                "OK");
            return;
        }

        CharacterImprovementActiveState selected = _options[_target.SelectedIndex].State;
        if (_enabled.IsToggled == selected.Enabled)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyImprovementActiveEditAsync(new ImprovementActiveEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            _enabled.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string IdentityToken(CharacterImprovementIdentity identity)
    {
        string token = identity.SourceName.Replace("-", string.Empty, StringComparison.Ordinal);
        return token[..Math.Min(8, token.Length)];
    }
}
