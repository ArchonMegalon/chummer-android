using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed class CharacterNotesPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly Editor _characterNotes;
    private readonly Editor? _gameNotes;
    private readonly Editor _groupNotes;
    private readonly Button _save;
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
            "Private notes stored in this runner file. Game notes are available after character creation.",
            NativeTheme.Muted));
        _body.Add(NativeTheme.FieldLabel("Character notes"));
        _characterNotes = NativeTheme.TextArea(
            "character-notes-editor",
            coordinator.CharacterNotes,
            "Add notes for this runner");
        _body.Add(NativeTheme.Card(_characterNotes, new Thickness(12, 6)));

        CharacterProfileSection profile = coordinator.State.Profile
            ?? throw new InvalidOperationException("Open a runner before editing Notes.");
        if (profile.Created)
        {
            _body.Add(NativeTheme.FieldLabel("Game notes"));
            _gameNotes = NativeTheme.TextArea(
                "character-game-notes-editor",
                coordinator.GameNotes,
                "Add notes from play");
            _body.Add(NativeTheme.Card(_gameNotes, new Thickness(12, 6)));
        }

        _body.Add(NativeTheme.FieldLabel("Group notes"));
        _groupNotes = NativeTheme.TextArea(
            "character-group-notes-editor",
            coordinator.GroupNotes,
            "Add notes shared with this runner's group");
        _body.Add(NativeTheme.Card(_groupNotes, new Thickness(12, 6)));

        _save = NativeTheme.PrimaryButton("Save notes");
        _save.AutomationId = "character-notes-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        _body.Add(_save);
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        if (Coordinator.State.WorkspaceId != _workspaceId)
        {
            _characterNotes.IsEnabled = false;
            if (_gameNotes is not null)
            {
                _gameNotes.IsEnabled = false;
            }
            _groupNotes.IsEnabled = false;
            _save.IsEnabled = false;
        }
    }

    private async Task SaveAsync()
    {
        await Coordinator.ApplyCharacterNotesEditAsync(new CharacterNotesEditRequest(
            _workspaceId,
            _contentRevision,
            _characterNotes.Text ?? string.Empty,
            _gameNotes?.Text ?? Coordinator.GameNotes,
            _groupNotes.Text ?? string.Empty));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
