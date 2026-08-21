using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class TraditionNamePage : NativePageBase
{
    private readonly TraditionNameEditorState _editor;
    private readonly Entry _name;
    private readonly Button _save;

    public TraditionNamePage(
        RunnerSessionCoordinator coordinator,
        TraditionNameEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Tradition name";
        AutomationId = "tradition-name-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Magic"));
        body.Add(NativeTheme.Title("Custom tradition name"));
        body.Add(NativeTheme.Body(
            "Edit the exact Chummer5 name of the selected Custom magical tradition. Published traditions cannot be renamed here.",
            NativeTheme.Muted));

        _name = new Entry
        {
            Text = editor.TraditionName,
            MaxLength = CharacterTraditionNameRules.MaximumLength,
            AutomationId = "tradition-name-value"
        };
        body.Add(NativeTheme.FieldLabel("Tradition name"));
        body.Add(_name);

        _save = NativeTheme.PrimaryButton("Save tradition name");
        _save.AutomationId = "tradition-name-save";
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
        _name.IsEnabled = revisionMatches;
        _save.IsEnabled = revisionMatches;
    }

    private async Task SaveAsync()
    {
        string requested = _name.Text ?? string.Empty;
        if (!CharacterTraditionNameRules.TryValidate(requested, out string validated))
        {
            await DisplayAlertAsync(
                "Tradition name unavailable",
                "Use one line with at most 32,767 characters.",
                "OK");
            return;
        }
        if (string.Equals(validated, _editor.TraditionName, StringComparison.Ordinal))
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyTraditionNameEditAsync(new TraditionNameEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.TraditionId,
            _editor.TraditionName,
            validated));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
