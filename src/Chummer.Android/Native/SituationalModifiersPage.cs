using System.Globalization;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class SituationalModifiersPage : NativePageBase
{
    private readonly SituationalModifiersEditorState _editor;
    private readonly Picker _counterspellingDice;
    private readonly Picker _liftCarryHits;
    private readonly Button _save;

    public SituationalModifiersPage(
        RunnerSessionCoordinator coordinator,
        SituationalModifiersEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Situational modifiers";
        AutomationId = "situational-modifiers";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Runner state"));
        body.Add(NativeTheme.Title("Situational modifiers"));
        body.Add(NativeTheme.Body(
            "Track active counterspelling dice and lift/carry test hits for this runner.",
            NativeTheme.Muted));

        _counterspellingDice = AddPicker(
            body,
            "Counterspelling dice",
            "situational-counterspelling-dice",
            editor.CounterspellingDice);
        _liftCarryHits = AddPicker(
            body,
            "Active lift/carry test hits",
            "situational-lift-carry-hits",
            editor.LiftCarryHits);

        _save = NativeTheme.PrimaryButton("Save modifiers");
        _save.AutomationId = "situational-modifiers-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        bool exactRevision = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _counterspellingDice.IsEnabled = exactRevision;
        _liftCarryHits.IsEnabled = exactRevision;
        _save.IsEnabled = exactRevision;
    }

    private async Task SaveAsync()
    {
        await Coordinator.ApplySituationalModifiersEditAsync(new SituationalModifiersEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            SelectedValue(_counterspellingDice, _editor.CounterspellingDice),
            SelectedValue(_liftCarryHits, _editor.LiftCarryHits)));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static Picker AddPicker(
        VerticalStackLayout body,
        string label,
        string automationId,
        int value)
    {
        body.Add(NativeTheme.FieldLabel(label));
        string[] values = Enumerable.Range(0, 101)
            .Select(candidate => candidate.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        Picker picker = new()
        {
            AutomationId = automationId,
            Title = label,
            ItemsSource = values,
            SelectedIndex = value,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(picker);
        return picker;
    }

    private static int SelectedValue(Picker picker, int fallback)
        => picker.SelectedItem is string selected
            && int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
}
