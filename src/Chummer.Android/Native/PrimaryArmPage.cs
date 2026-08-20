using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class PrimaryArmPage : NativePageBase
{
    private readonly PrimaryArmEditorState _editor;
    private readonly Picker _primaryArm;
    private readonly Button _save;

    public PrimaryArmPage(
        RunnerSessionCoordinator coordinator,
        PrimaryArmEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Primary arm";
        AutomationId = "primary-arm";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Runner profile"));
        body.Add(NativeTheme.Title("Primary arm"));
        body.Add(NativeTheme.Body(
            editor.Ambidextrous
                ? "This runner is Ambidextrous, so Chummer5 keeps primary arm read-only."
                : "Choose the runner's preferred arm. Chummer5 stores the exact Left or Right value.",
            NativeTheme.Muted));
        body.Add(NativeTheme.FieldLabel("Primary arm"));

        string[] values = editor.Ambidextrous ? ["Ambidextrous"] : ["Left", "Right"];
        _primaryArm = new Picker
        {
            AutomationId = "primary-arm-choice",
            Title = "Primary arm",
            ItemsSource = values,
            SelectedIndex = editor.Ambidextrous
                ? 0
                : string.Equals(editor.Value, "Left", StringComparison.Ordinal) ? 0 : 1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            IsEnabled = !editor.Ambidextrous
        };
        body.Add(_primaryArm);

        _save = NativeTheme.PrimaryButton("Save primary arm");
        _save.AutomationId = "primary-arm-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        bool editable = !_editor.Ambidextrous
            && Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _primaryArm.IsEnabled = editable;
        _save.IsEnabled = editable;
    }

    private async Task SaveAsync()
    {
        if (_editor.Ambidextrous)
        {
            await DisplayAlertAsync(
                "Primary arm is read-only",
                "This runner is Ambidextrous.",
                "OK");
            return;
        }

        string value = _primaryArm.SelectedItem as string ?? _editor.Value;
        await Coordinator.ApplyPrimaryArmEditAsync(new PrimaryArmEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            value));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
