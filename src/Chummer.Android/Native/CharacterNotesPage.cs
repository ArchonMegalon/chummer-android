using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed class CharacterNotesPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly Editor _notes;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public CharacterNotesPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        _workspaceId = coordinator.State.WorkspaceId
            ?? throw new InvalidOperationException("Open a runner before editing Notes.");
        _contentRevision = coordinator.State.ContentRevision;
        Title = "Notes";
        AutomationId = "character-notes";

        _body.Add(NativeTheme.Eyebrow("Runner"));
        _body.Add(NativeTheme.Title("Notes"));
        _body.Add(NativeTheme.Body(
            "Private character notes stored in this runner file.",
            NativeTheme.Muted));
        _body.Add(NativeTheme.FieldLabel("Character notes"));
        _notes = NativeTheme.TextArea(
            "character-notes-editor",
            coordinator.CharacterNotes,
            "Add notes for this runner");
        _body.Add(NativeTheme.Card(_notes, new Thickness(12, 6)));

        Button save = NativeTheme.PrimaryButton("Save notes");
        save.AutomationId = "character-notes-save";
        save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        _body.Add(save);
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        if (Coordinator.State.WorkspaceId != _workspaceId)
        {
            _notes.IsEnabled = false;
        }
    }

    private async Task SaveAsync()
    {
        await Coordinator.ApplyCharacterNotesEditAsync(new CharacterNotesEditRequest(
            _workspaceId,
            _contentRevision,
            _notes.Text ?? string.Empty));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
