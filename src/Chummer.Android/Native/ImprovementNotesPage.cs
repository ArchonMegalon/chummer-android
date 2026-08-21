using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ImprovementNotesPage : NativePageBase
{
    private sealed record ImprovementOption(CharacterImprovementNotesState State, string Label);

    private readonly ImprovementNotesEditorState _editor;
    private readonly IReadOnlyList<ImprovementOption> _options;
    private readonly Picker _target;
    private readonly Editor _notes;
    private readonly Entry _notesColor;
    private readonly Button _save;

    public ImprovementNotesPage(
        RunnerSessionCoordinator coordinator,
        ImprovementNotesEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.Improvements.Any(item =>
                !CharacterImprovementActiveRules.IsValidIdentity(item.Identity)
                || !CharacterImprovementNotesRules.IsValidLegacyHtmlColor(item.NotesColor)))
        {
            throw new ArgumentException(
                "Improvement notes require exact stable saved identity and legacy HTML color.",
                nameof(editor));
        }

        Title = "Improvement Notes";
        AutomationId = "improvement-notes-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career improvements"));
        body.Add(NativeTheme.Title("Notes"));
        body.Add(NativeTheme.Body(
            "Choose one direct saved Improvement and edit the Chummer5 note text and note color together.",
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
            SelectedIndex = _options.Count == 0 ? -1 : 0,
            AutomationId = "improvement-notes-target",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Improvement"));
        body.Add(_target);

        _notes = new Editor
        {
            AutomationId = "improvement-notes-text",
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 150,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            Placeholder = "Notes"
        };
        SemanticProperties.SetDescription(_notes, "Improvement notes");
        body.Add(NativeTheme.FieldLabel("Notes"));
        body.Add(_notes);

        _notesColor = new Entry
        {
            AutomationId = "improvement-notes-color",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            Placeholder = "#RRGGBB or legacy HTML color name"
        };
        SemanticProperties.SetDescription(_notesColor, "Notes color");
        body.Add(NativeTheme.FieldLabel("Notes color"));
        body.Add(_notesColor);

        _save = NativeTheme.PrimaryButton("Save Notes");
        _save.AutomationId = "improvement-notes-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private CharacterImprovementNotesState? SelectedState()
        => _target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count
            ? _options[_target.SelectedIndex].State
            : null;

    private void LoadSelectedState()
    {
        CharacterImprovementNotesState? selected = SelectedState();
        _notes.Text = selected?.Notes ?? string.Empty;
        _notesColor.Text = selected?.NotesColor ?? string.Empty;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision
            && SelectedState() is not null;
        _target.IsEnabled = current;
        _notes.IsEnabled = current;
        _notesColor.IsEnabled = current;
        _save.IsEnabled = current;
    }

    private async Task SaveAsync()
    {
        CharacterImprovementNotesState? selected = SelectedState();
        if (selected is null)
        {
            await DisplayAlertAsync(
                "Improvement required",
                "Choose one exact saved Improvement before saving.",
                "OK");
            return;
        }

        string notes = _notes.Text ?? string.Empty;
        string notesColor = _notesColor.Text?.Trim() ?? string.Empty;
        if (!CharacterImprovementNotesRules.CanSetLegacyHtmlColor(
                selected.NotesColor,
                notesColor))
        {
            await DisplayAlertAsync(
                "Notes color required",
                "Use #RRGGBB or a legacy HTML color name.",
                "OK");
            return;
        }
        if (string.Equals(notes, selected.Notes, StringComparison.Ordinal)
            && string.Equals(notesColor, selected.NotesColor, StringComparison.Ordinal))
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyImprovementNotesEditAsync(new ImprovementNotesEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            notes,
            notesColor));
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
