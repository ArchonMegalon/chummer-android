using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class TraditionDrainPage : NativePageBase
{
    private sealed record DrainOption(string Value, string Label);

    private readonly TraditionDrainEditorState _editor;
    private readonly Picker _expression;
    private readonly Button _save;

    public TraditionDrainPage(
        RunnerSessionCoordinator coordinator,
        TraditionDrainEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Tradition drain";
        AutomationId = "tradition-drain-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Magic"));
        body.Add(NativeTheme.Title("Drain attributes"));
        body.Add(NativeTheme.Body(
            "Choose only from the exact drain expressions enabled by this runner's Chummer5 content profile.",
            NativeTheme.Muted));

        DrainOption[] options = editor.AllowedExpressions
            .Select(value => new DrainOption(value, value.Length == 0 ? "None" : value))
            .ToArray();
        _expression = new Picker
        {
            Title = "Drain attributes",
            ItemsSource = options,
            ItemDisplayBinding = new Binding(nameof(DrainOption.Label)),
            SelectedItem = options.Single(option => string.Equals(
                option.Value,
                editor.DrainExpression,
                StringComparison.Ordinal)),
            AutomationId = "tradition-drain-value"
        };
        body.Add(NativeTheme.FieldLabel("Drain attributes"));
        body.Add(_expression);

        _save = NativeTheme.PrimaryButton("Save drain attributes");
        _save.AutomationId = "tradition-drain-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _expression.IsEnabled = revisionMatches;
        _save.IsEnabled = revisionMatches;
    }

    private async Task SaveAsync()
    {
        if (_expression.SelectedItem is not DrainOption selected
            || !CharacterTraditionDrainRules.TryValidateRequestedExpression(
                selected.Value,
                _editor.AllowedExpressions,
                out string validated))
        {
            await DisplayAlertAsync(
                "Drain attributes unavailable",
                "Choose an expression from the exact Chummer5 source list.",
                "OK");
            return;
        }
        if (string.Equals(validated, _editor.DrainExpression, StringComparison.Ordinal))
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyTraditionDrainEditAsync(new TraditionDrainEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.TraditionId,
            _editor.DrainExpression,
            validated));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
