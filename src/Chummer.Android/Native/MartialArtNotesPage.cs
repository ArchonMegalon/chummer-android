using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class MartialArtNotesPage : NativePageBase
{
    private sealed record TargetOption(CharacterMartialArtNotesState State, string Label);

    private readonly MartialArtNotesEditorState _editor;
    private readonly IReadOnlyList<TargetOption> _options;
    private readonly Picker _target;
    private readonly Editor _notes;
    private readonly Entry _notesColor;
    private readonly Button _save;

    public MartialArtNotesPage(
        RunnerSessionCoordinator coordinator,
        MartialArtNotesEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.Targets.Any(target =>
                !CharacterMartialArtNotesRules.IsValidIdentity(target.Identity)
                || !CharacterImprovementNotesRules.IsValidLegacyHtmlColor(target.NotesColor)
                || target.Economics is not { KarmaDelta: 0, NuyenDelta: 0m }))
        {
            throw new ArgumentException(
                "Martial Arts notes require exact stable identity, color, and zero-cost state.",
                nameof(editor));
        }

        Title = "Martial Arts Notes";
        AutomationId = "martial-art-notes-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(editor.Targets.FirstOrDefault()?.Created == true ? "Career" : "Creation"));
        body.Add(NativeTheme.Title("Martial Arts Notes"));
        body.Add(NativeTheme.Body(
            "Choose one saved Martial Art or a parent-scoped Technique, then edit Chummer5 notes and color together.",
            NativeTheme.Muted));

        _options = editor.Targets.Select(target => new TargetOption(target, Label(target))).ToArray();
        _target = new Picker
        {
            Title = "Martial Art or Technique",
            ItemsSource = _options,
            ItemDisplayBinding = new Binding(nameof(TargetOption.Label)),
            SelectedIndex = _options.Count == 0 ? -1 : 0,
            AutomationId = "martial-art-notes-target",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Martial Art or Technique"));
        body.Add(_target);

        _notes = new Editor
        {
            AutomationId = "martial-art-notes-text",
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 150,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            Placeholder = "Notes"
        };
        SemanticProperties.SetDescription(_notes, "Martial Arts notes");
        body.Add(NativeTheme.FieldLabel("Notes"));
        body.Add(_notes);

        _notesColor = new Entry
        {
            AutomationId = "martial-art-notes-color",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            Placeholder = "#RRGGBB or legacy HTML color name"
        };
        SemanticProperties.SetDescription(_notesColor, "Notes color");
        body.Add(NativeTheme.FieldLabel("Notes color"));
        body.Add(_notesColor);

        _save = NativeTheme.PrimaryButton("Save Notes");
        _save.AutomationId = "martial-art-notes-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private CharacterMartialArtNotesState? SelectedState()
        => _target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count
            ? _options[_target.SelectedIndex].State
            : null;

    private void LoadSelectedState()
    {
        CharacterMartialArtNotesState? selected = SelectedState();
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
        CharacterMartialArtNotesState? selected = SelectedState();
        if (selected is null)
        {
            await DisplayAlertAsync("Target required", "Choose a Martial Art or Technique.", "OK");
            return;
        }
        string notes = _notes.Text ?? string.Empty;
        string notesColor = _notesColor.Text?.Trim() ?? string.Empty;
        if (!CharacterImprovementNotesRules.CanSetLegacyHtmlColor(selected.NotesColor, notesColor))
        {
            await DisplayAlertAsync(
                "Notes color required",
                "Use #RRGGBB or preserve the saved legacy HTML color name.",
                "OK");
            return;
        }
        if (string.Equals(notes, selected.Notes, StringComparison.Ordinal)
            && string.Equals(notesColor, selected.NotesColor, StringComparison.Ordinal))
        {
            await Navigation.PopAsync();
            return;
        }
        await Coordinator.ApplyMartialArtNotesEditAsync(new MartialArtNotesEditRequest(
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

    private static string Label(CharacterMartialArtNotesState state)
    {
        Guid id = state.Identity.TechniqueId ?? state.Identity.MartialArtId;
        string token = id.ToString("N")[..8];
        return state.Identity.IsTechnique
            ? $"Technique · {state.MartialArtName} > {state.TargetName} · {token}"
            : $"Art · {state.TargetName} · {token}";
    }
}
