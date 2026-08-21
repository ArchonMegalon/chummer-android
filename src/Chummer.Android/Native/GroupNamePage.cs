using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GroupNamePage : NativePageBase
{
    private readonly GroupNameEditorState _editor;
    private readonly Entry _name;
    private readonly Button _save;

    public GroupNamePage(
        RunnerSessionCoordinator coordinator,
        GroupNameEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Group name";
        AutomationId = "group-name-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Initiation group"));
        body.Add(NativeTheme.Title("Group name"));
        body.Add(NativeTheme.Body(
            "Edit the exact Chummer5 group name. This is separate from contact-group names and joining or leaving the group.",
            NativeTheme.Muted));

        _name = new Entry
        {
            Text = editor.GroupName,
            MaxLength = CharacterGroupNameRules.MaximumLength,
            AutomationId = "group-name-value"
        };
        body.Add(NativeTheme.FieldLabel("Group name"));
        body.Add(_name);

        _save = NativeTheme.PrimaryButton("Save group name");
        _save.AutomationId = "group-name-save";
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
        if (!CharacterGroupNameRules.TryValidate(requested, out string validated))
        {
            await DisplayAlertAsync(
                "Group name unavailable",
                "Use one line with at most 32,767 characters.",
                "OK");
            return;
        }
        if (string.Equals(validated, _editor.GroupName, StringComparison.Ordinal))
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyGroupNameEditAsync(new GroupNameEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.GroupName,
            validated));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
