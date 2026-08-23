using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerCalendarPage : NativePageBase
{
    private readonly CareerCalendarEditorState _editor;
    private readonly Picker _weeks;
    private readonly VerticalStackLayout _firstWeekFields;
    private readonly Entry _firstYear;
    private readonly Entry _firstWeek;
    private readonly Label _addSummary;
    private readonly Button _add;
    private readonly Editor _notes;
    private readonly Entry _notesColor;
    private readonly Button _save;
    private readonly Button _delete;
    private CharacterCareerCalendarWeekState? _selected;

    public CareerCalendarPage(
        RunnerSessionCoordinator coordinator,
        CareerCalendarEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _selected = editor.Weeks.FirstOrDefault();
        Title = "Calendar";
        AutomationId = "career-calendar-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Calendar weeks"));
        body.Add(NativeTheme.Body(
            "Add the next saved ISO week, then edit or delete notes by stable Chummer5 week identity.",
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel("Saved week"));
        _weeks = new Picker
        {
            AutomationId = "career-calendar-week-picker",
            Title = "Saved calendar week",
            ItemsSource = editor.Weeks.Select(WeekLabel).ToArray(),
            SelectedIndex = editor.Weeks.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _weeks.SelectedIndexChanged += (_, _) => SelectWeek();
        body.Add(_weeks);

        _firstYear = new Entry
        {
            AutomationId = "career-calendar-first-year",
            Keyboard = Keyboard.Numeric,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = 4,
            Text = Math.Clamp(
                    DateTime.Today.Year + 62,
                    CharacterCareerCalendarRules.FirstWeekMinimumYear,
                    CharacterCareerCalendarRules.FirstWeekMaximumYear)
                .ToString(CultureInfo.InvariantCulture)
        };
        _firstWeek = new Entry
        {
            AutomationId = "career-calendar-first-week",
            Keyboard = Keyboard.Numeric,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = 2,
            Text = "1"
        };
        _firstYear.TextChanged += (_, _) => RefreshEnabledState();
        _firstWeek.TextChanged += (_, _) => RefreshEnabledState();
        _firstWeekFields = new VerticalStackLayout { Spacing = 8 };
        _firstWeekFields.Add(NativeTheme.FieldLabel("First calendar year (2000–9000)"));
        _firstWeekFields.Add(_firstYear);
        _firstWeekFields.Add(NativeTheme.FieldLabel("First ISO week"));
        _firstWeekFields.Add(_firstWeek);
        body.Add(_firstWeekFields);

        _addSummary = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _addSummary.AutomationId = "career-calendar-add-summary";
        body.Add(_addSummary);
        _add = NativeTheme.PrimaryButton("Add week");
        _add.AutomationId = "career-calendar-add";
        _add.Clicked += async (_, _) => await RunAsync(AddAsync);
        body.Add(_add);

        body.Add(NativeTheme.FieldLabel("Selected week notes"));
        _notes = NativeTheme.TextArea(
            "career-calendar-notes",
            "Calendar week notes",
            "Notes");
        _notes.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_notes);

        body.Add(NativeTheme.FieldLabel("Notes color (name or #RRGGBB)"));
        _notesColor = new Entry
        {
            AutomationId = "career-calendar-notes-color",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = 64
        };
        _notesColor.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_notesColor);

        _save = NativeTheme.PrimaryButton("Save week notes");
        _save.AutomationId = "career-calendar-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);

        _delete = NativeTheme.SecondaryButton("Delete selected week");
        _delete.AutomationId = "career-calendar-delete";
        _delete.Clicked += async (_, _) => await RunAsync(DeleteAsync);
        body.Add(_delete);

        Button changeStart = NativeTheme.SecondaryButton("Change starting date (unavailable)");
        changeStart.AutomationId = "career-calendar-change-start-disabled";
        changeStart.IsEnabled = false;
        body.Add(changeStart);
        Label blocker = NativeTheme.Body(editor.ChangeStartingDateBlocker, NativeTheme.Danger);
        blocker.AutomationId = "career-calendar-change-start-blocker";
        body.Add(NativeTheme.Card(blocker));

        Content = new ScrollView { Content = body };
        LoadSelection();
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string WeekLabel(CharacterCareerCalendarWeekState week)
    {
        string notes = string.IsNullOrWhiteSpace(week.Notes) ? "(no notes)" : week.Notes;
        return $"{week.Year.ToString(CultureInfo.InvariantCulture)} W"
            + $"{week.Week.ToString("00", CultureInfo.InvariantCulture)} · {notes} · {week.Identity.WeekId:D}";
    }

    private void SelectWeek()
    {
        _selected = _weeks.SelectedIndex >= 0 && _weeks.SelectedIndex < _editor.Weeks.Count
            ? _editor.Weeks[_weeks.SelectedIndex]
            : null;
        LoadSelection();
    }

    private void LoadSelection()
    {
        _notes.Text = _selected?.Notes ?? string.Empty;
        _notesColor.Text = _selected?.NotesColor
            ?? CharacterCareerCalendarRules.DefaultNotesColor;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        bool firstWeek = _editor.Weeks.Count == 0;
        _firstWeekFields.IsVisible = firstWeek;
        int requestedYear = ReadInt(_firstYear.Text);
        int requestedWeek = ReadInt(_firstWeek.Text);
        bool addValid = CharacterCareerCalendarRules.TryPlanAdd(
            _editor.Weeks,
            new CharacterCareerCalendarWeekIdentity(Guid.NewGuid()),
            requestedYear,
            requestedWeek,
            out CharacterCareerCalendarWeekDraft planned);
        _addSummary.Text = addValid
            ? $"Next saved week: {planned.Year.ToString(CultureInfo.InvariantCulture)} W"
                + planned.Week.ToString("00", CultureInfo.InvariantCulture)
            : firstWeek
                ? "Choose a valid first ISO week. Long years permit week 53."
                : "The saved calendar has no valid next week within supported Chummer5 bounds.";

        bool editValid = _selected is not null
            && CharacterCareerCalendarRules.TryEdit(
                _selected,
                _selected.SourceRevision,
                _notes.Text,
                _notesColor.Text,
                out _);
        _weeks.IsEnabled = revisionMatches && _editor.Weeks.Count > 0;
        _firstYear.IsEnabled = revisionMatches && firstWeek;
        _firstWeek.IsEnabled = revisionMatches && firstWeek;
        _add.IsEnabled = revisionMatches && addValid;
        _notes.IsEnabled = revisionMatches && _selected is not null;
        _notesColor.IsEnabled = revisionMatches && _selected is not null;
        _save.IsEnabled = revisionMatches && editValid;
        _delete.IsEnabled = revisionMatches
            && _selected is not null
            && CharacterCareerCalendarRules.CanDelete(
                _selected,
                _selected.Identity,
                _selected.SourceRevision,
                confirmed: true);
    }

    private async Task AddAsync()
    {
        int requestedYear = ReadInt(_firstYear.Text);
        int requestedWeek = ReadInt(_firstWeek.Text);
        CharacterCareerCalendarWeekIdentity identity = new(Guid.NewGuid());
        if (!CharacterCareerCalendarRules.TryPlanAdd(
                _editor.Weeks,
                identity,
                requestedYear,
                requestedWeek,
                out _))
        {
            await DisplayAlertAsync(
                "Invalid calendar week",
                "Choose an ISO week within Chummer5's first-week bounds, or reopen the changed runner.",
                "OK");
            return;
        }

        bool persisted = await Coordinator.ApplyCareerCalendarAddAsync(new CareerCalendarAddRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            identity,
            requestedYear,
            requestedWeek));
        if (persisted)
        {
            await Navigation.PopAsync();
        }
    }

    private async Task SaveAsync()
    {
        if (_selected is null
            || !CharacterCareerCalendarRules.TryEdit(
                _selected,
                _selected.SourceRevision,
                _notes.Text,
                _notesColor.Text,
                out _))
        {
            await DisplayAlertAsync(
                "Invalid calendar edit",
                "The selected week changed or its notes color is not a Chummer5 HTML color.",
                "OK");
            return;
        }

        bool persisted = await Coordinator.ApplyCareerCalendarEditAsync(new CareerCalendarEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _selected,
            _selected.SourceRevision,
            _notes.Text ?? string.Empty,
            _notesColor.Text ?? CharacterCareerCalendarRules.DefaultNotesColor));
        if (persisted)
        {
            await Navigation.PopAsync();
        }
    }

    private async Task DeleteAsync()
    {
        if (_selected is null)
        {
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "Delete calendar week?",
            $"Delete {_selected.Year.ToString(CultureInfo.InvariantCulture)} W"
                + $"{_selected.Week.ToString("00", CultureInfo.InvariantCulture)}?",
            "Delete",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        bool persisted = await Coordinator.ApplyCareerCalendarDeleteAsync(new CareerCalendarDeleteRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _selected,
            _selected.SourceRevision,
            Confirmed: true));
        if (persisted)
        {
            await Navigation.PopAsync();
        }
    }

    private static int ReadInt(string? value)
        => int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsed)
            ? parsed
            : 0;
}
