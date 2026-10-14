using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

/// <summary>Governed native renderer for one exact SR5 Downtime Calendar mutation.</summary>
public class Sr5DowntimeCalendarWizardPage : NativePageBase
{
    private readonly RunnerSessionSr5DowntimeCalendarAuthority _authority;
    private readonly Sr5DowntimeCalendarJournalStore _journalStore;
    private readonly Func<bool>? _entryStillCurrent;
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Label _binding;
    private readonly Label _status;
    private readonly Label _outcomeUnknownStatus;
    private readonly Label _receiptStatus;
    private readonly VerticalStackLayout _form = new() { Spacing = 12 };
    private readonly View _weekField;
    private readonly View _firstWeekFields;
    private readonly View _notesFields;
    private readonly Label _emptyWeeks;
    private readonly View _bindingCard;
    private readonly View _previewCard;
    private readonly View _statusCard;
    private readonly View _outcomeUnknownCard;
    private readonly View _receiptCard;
    private readonly Picker _operation;
    private readonly Picker _weekPicker;
    private readonly Entry _year;
    private readonly Entry _week;
    private readonly Editor _notes;
    private readonly Entry _notesColor;
    private readonly Label _preview;
    private readonly Button _review;
    private readonly Button _editReview;
    private readonly Button _apply;
    private readonly Button _clear;
    private readonly Button _discardReview;
    private Sr5DowntimeCalendarPhoneLoadResult? _load;
    private Sr5DowntimeCalendarDesktopSession? _session;
    private Sr5DowntimeCalendarJournal? _journal;
    private bool _loaded;
    private bool _outcomeUnknown;
    private bool _staleReview;

    public Sr5DowntimeCalendarWizardPage(RunnerSessionCoordinator coordinator)
        : this(coordinator, new RunnerSessionSr5DowntimeCalendarAuthority(coordinator),
            Sr5DowntimeCalendarJournalStore.CreateDefault()) { }

    internal Sr5DowntimeCalendarWizardPage(
        RunnerSessionCoordinator coordinator,
        RunnerSessionSr5DowntimeCalendarAuthority authority,
        Sr5DowntimeCalendarJournalStore journalStore,
        Func<bool>? entryStillCurrent = null) : base(coordinator)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _entryStillCurrent = entryStillCurrent;
        Title = Text("Downtime calendar");
        AutomationId = "sr5-downtime-calendar-page";
        VerticalStackLayout body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Title(Text("Plan one calendar change")));
        _binding = NativeTheme.Body(Text("Loading calendar…"), NativeTheme.Muted);
        _binding.AutomationId = "sr5-downtime-calendar-binding";
        _bindingCard = NativeTheme.Card(_binding);
        body.Add(_bindingCard);
        _status = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _status.AutomationId = "sr5-downtime-calendar-status";
        _statusCard = NativeTheme.Card(_status);
        body.Add(_statusCard);
        _discardReview = NativeTheme.PrimaryButton(Text("Discard preview and continue"));
        _discardReview.AutomationId = "sr5-downtime-calendar-discard-review";
        SemanticProperties.SetDescription(_discardReview, Text("Discard preview and continue"));
        _discardReview.Clicked += async (_, _) => await RunAsync(DiscardStaleReviewAsync);
        body.Add(_discardReview);
        _operation = new Picker
        {
            AutomationId = "sr5-downtime-calendar-operation", Title = Text("Calendar action"),
            ItemsSource = new[] { Text("Add next week"), Text("Edit week"), Text("Delete week") }, SelectedIndex = 0,
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text
        };
        SemanticProperties.SetDescription(_operation, Text("Calendar action"));
        _operation.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        _form.Add(Field("Action", _operation));
        _weekPicker = new Picker
        {
            AutomationId = "sr5-downtime-calendar-week", Title = Text("Saved week"),
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text
        };
        SemanticProperties.SetDescription(_weekPicker, Text("Saved week"));
        _weekPicker.SelectedIndexChanged += (_, _) => SelectWeek();
        _weekField = Field("Saved week", _weekPicker);
        _form.Add(_weekField);
        _emptyWeeks = NativeTheme.Body(Text("No saved weeks yet. Add a week first."), NativeTheme.Muted);
        _form.Add(_emptyWeeks);
        _year = new Entry
        {
            AutomationId = "sr5-downtime-calendar-year", Keyboard = Keyboard.Numeric, MaxLength = 4,
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text,
            Text = Math.Clamp(DateTime.Today.Year + 62,
                    CharacterCareerCalendarRules.FirstWeekMinimumYear,
                    CharacterCareerCalendarRules.FirstWeekMaximumYear).ToString(CultureInfo.InvariantCulture)
        };
        SemanticProperties.SetDescription(_year, Text("First year (empty calendar only)"));
        _week = new Entry
        {
            AutomationId = "sr5-downtime-calendar-iso-week", Keyboard = Keyboard.Numeric, MaxLength = 2,
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text, Text = "1"
        };
        SemanticProperties.SetDescription(_week, Text("First ISO week"));
        _firstWeekFields = new VerticalStackLayout
        {
            Spacing = 12, Children = { Field("Year", _year), Field("First ISO week", _week) }
        };
        _form.Add(_firstWeekFields);
        _notes = NativeTheme.TextArea("sr5-downtime-calendar-notes", Text("Downtime notes"), Text("Downtime notes"));
        SemanticProperties.SetDescription(_notes, Text("Downtime notes"));
        _notesColor = new Entry
        {
            AutomationId = "sr5-downtime-calendar-notes-color", MaxLength = 64,
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text,
            Text = CharacterCareerCalendarRules.DefaultNotesColor
        };
        SemanticProperties.SetDescription(_notesColor, Text("Notes color (edit only)"));
        _notesFields = new VerticalStackLayout
        {
            Spacing = 12, Children = { Field("Downtime notes", _notes), Field("Notes color", _notesColor) }
        };
        _form.Add(_notesFields);
        body.Add(_form);
        _review = NativeTheme.PrimaryButton(Text("Review change"));
        _review.AutomationId = "sr5-downtime-calendar-review";
        SemanticProperties.SetDescription(_review, Text("Review change"));
        _review.Clicked += async (_, _) => await RunAsync(ReviewAsync);
        body.Add(_review);
        _preview = NativeTheme.Body(string.Empty);
        _preview.AutomationId = "sr5-downtime-calendar-preview";
        _previewCard = NativeTheme.Card(_preview);
        body.Add(_previewCard);
        _editReview = NativeTheme.SecondaryButton(Text("Change preview"));
        _editReview.AutomationId = "sr5-downtime-calendar-edit-review";
        _editReview.Clicked += async (_, _) => await RunAsync(EditReviewAsync);
        body.Add(_editReview);
        _apply = NativeTheme.PrimaryButton(Text("Save change"));
        _apply.AutomationId = "sr5-downtime-calendar-apply";
        SemanticProperties.SetDescription(_apply, Text("Save change"));
        _apply.Clicked += async (_, _) => await RunAsync(SaveReviewedAsync);
        body.Add(_apply);
        _outcomeUnknownStatus = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _outcomeUnknownStatus.AutomationId = "sr5-downtime-calendar-outcome-unknown";
        _outcomeUnknownCard = NativeTheme.Card(_outcomeUnknownStatus);
        body.Add(_outcomeUnknownCard);
        _receiptStatus = NativeTheme.Body(string.Empty, NativeTheme.Success);
        _receiptStatus.AutomationId = "sr5-downtime-calendar-receipt";
        _receiptCard = NativeTheme.Card(_receiptStatus);
        body.Add(_receiptCard);
        _clear = NativeTheme.SecondaryButton(Text("Start another calendar change"));
        _clear.AutomationId = "sr5-downtime-calendar-clear-applied";
        SemanticProperties.SetDescription(_clear, Text("Start another calendar change"));
        _clear.Clicked += async (_, _) => await RunAsync(ClearAppliedAsync);
        body.Add(_clear);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    private static VerticalStackLayout Field(string label, View input)
        => new() { Spacing = 4, Children = { NativeTheme.FieldLabel(Text(label)), input } };

    protected override async Task PrepareForAppearanceRefreshAsync(
        CancellationToken cancellationToken)
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadAndRecoverAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _loaded = false;
            throw;
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.TextColor = NativeTheme.Danger;
        }
    }

    protected override void Refresh() => RefreshEnabledState();

    private async Task LoadAndRecoverAsync(CancellationToken cancellationToken)
    {
        // A contextual entry may expire while the destination attaches or its
        // projection is awaited. Do not publish a foreign/renewed entry's state.
        RequireCurrentEntry(cancellationToken);
        var load = await _authority.LoadAsync(cancellationToken);
        RequireCurrentEntry(cancellationToken);
        _load = load;
        if (!_load.IsReady)
        {
            _binding.Text = _load.Blocker ?? Text("Exact Downtime Calendar authority is unavailable.");
            _binding.TextColor = NativeTheme.Danger;
            return;
        }
        _binding.Text = string.Empty;
        _binding.TextColor = NativeTheme.Text;
        PopulateWeeks(_load.Editor!);
        if (!_journalStore.TryRead(out _journal, out string readBlocker))
        {
            if (!string.IsNullOrWhiteSpace(readBlocker)) MarkOutcomeUnknown(readBlocker);
            _session = new Sr5DowntimeCalendarDesktopSession();
            _session.Bind(_load.Binding!, _load.Editor!);
            return;
        }
        RecoverJournal(_journal!);
    }

    private void RequireCurrentEntry(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_entryStillCurrent?.Invoke() == false)
            throw new InvalidOperationException(Text("The saved runner changed. Reopen Downtime before reviewing."));
    }

    private void RecoverJournal(Sr5DowntimeCalendarJournal journal)
    {
        if (_load is not { IsReady: true }) return;
        if (journal.Phase == Sr5DowntimeCalendarJournalPhase.Applied)
        {
            _status.Text = Text("Calendar change saved.");
            _status.TextColor = NativeTheme.Success;
            _receiptStatus.Text = _status.Text;
            return;
        }
        if (journal.Phase == Sr5DowntimeCalendarJournalPhase.Review)
        {
            _session = new Sr5DowntimeCalendarDesktopSession();
            if (!_session.Bind(_load.Binding!, _load.Editor!, journal.Review).Resume.Restored)
            {
                // A Review has not dispatched a save. Keep it until an explicit
                // discard instead of labelling it as an uncertain Applying.
                _staleReview = true;
                _status.Text = Text("Your runner changed after this preview. Discard the preview to plan a new change. Nothing from this preview was saved.");
                _status.TextColor = NativeTheme.Muted;
                return;
            }
            RestoreForm(journal.Review.Preview);
            _status.Text = Text("Reviewed preview restored. Confirm it again before saving.");
            return;
        }
        long current = _load.Binding!.WorkspaceRevision;
        if (current == journal.Review.WorkspaceRevision)
        {
            var candidate = new Sr5DowntimeCalendarDesktopSession();
            string blocker = string.Empty;
            if (candidate.Bind(_load.Binding, _load.Editor!, journal.Review).Resume.Restored
                && _journalStore.TryReturnToReview(journal, out _journal, out blocker))
            {
                _session = candidate;
                RestoreForm(journal.Review.Preview);
                _status.Text = Text("Interrupted save proven not applied. Confirm again.");
                return;
            }
            MarkOutcomeUnknown(string.IsNullOrWhiteSpace(blocker)
                ? Text("The unchanged runner no longer matches the reviewed precondition.") : blocker);
            return;
        }
        if (current == journal.Review.WorkspaceRevision + 1)
        {
            try
            {
                Sr5DowntimeCalendarPersistenceReceipt receipt = Sr5DowntimeCalendarPersistenceReceipt.Create(
                    journal, _load.Binding, _load.Editor!);
                if (_journalStore.TryComplete(journal, receipt, out _journal, out string blocker))
                {
                    _status.Text = Text("Calendar change saved.");
                    _receiptStatus.Text = _status.Text;
                    _status.TextColor = NativeTheme.Success;
                    return;
                }
                MarkOutcomeUnknown(blocker);
                return;
            }
            catch (InvalidOperationException exception) { MarkOutcomeUnknown(exception.Message); return; }
        }
        MarkOutcomeUnknown(Text("Runner revision moved outside the one-step Calendar CAS boundary."));
    }

    private void PopulateWeeks(CareerCalendarEditorState editor)
    {
        _weekPicker.ItemsSource = editor.Weeks.Select(WeekLabel).ToArray();
        _weekPicker.SelectedIndex = editor.Weeks.Count > 0 ? 0 : -1;
        SelectWeek();
    }

    private void SelectWeek()
    {
        CharacterCareerCalendarWeekState? selected = SelectedWeek();
        _notes.Text = selected?.Notes ?? string.Empty;
        _notesColor.Text = selected?.NotesColor ?? CharacterCareerCalendarRules.DefaultNotesColor;
        RefreshEnabledState();
    }

    private CharacterCareerCalendarWeekState? SelectedWeek()
        => _load?.Editor is { } editor && _weekPicker.SelectedIndex >= 0
            && _weekPicker.SelectedIndex < editor.Weeks.Count ? editor.Weeks[_weekPicker.SelectedIndex] : null;

    private static string WeekLabel(CharacterCareerCalendarWeekState value)
        => Format("{0} · Week {1}", value.Year, value.Week.ToString("00", CultureInfo.InvariantCulture));

    private static string PreviewText(Sr5DowntimeCalendarPreview preview)
    {
        string action = preview.Operation switch
        {
            Sr5DowntimeCalendarOperation.Add => Text("Add next week"),
            Sr5DowntimeCalendarOperation.Edit => Text("Edit week"),
            Sr5DowntimeCalendarOperation.Delete => Text("Delete week"),
            _ => throw new InvalidOperationException("Unknown Calendar operation.")
        };
        string text = action + "\n" + Format("{0} · Week {1}", preview.Year,
            preview.Week.ToString("00", CultureInfo.InvariantCulture));
        if (preview.Operation != Sr5DowntimeCalendarOperation.Add)
            text += "\n" + (string.IsNullOrEmpty(preview.Notes) ? Text("No notes") : preview.Notes)
                + "\n" + Format("Notes color: {0}", preview.NotesColor);
        return text;
    }

    private void RestoreForm(Sr5DowntimeCalendarPreview preview)
    {
        _operation.SelectedIndex = (int)preview.Operation;
        _weekPicker.SelectedIndex = _load!.Editor!.Weeks.ToList().FindIndex(
            week => week.Identity.WeekId == preview.WeekId);
        _year.Text = preview.Year.ToString(CultureInfo.InvariantCulture);
        _week.Text = preview.Week.ToString(CultureInfo.InvariantCulture);
        _notes.Text = preview.Notes;
        _notesColor.Text = preview.NotesColor;
    }

    private async Task ReviewAsync()
    {
        if (_session is null || _load is not { IsReady: true }) return;
        if (!CurrentBindingMatches())
            throw new InvalidOperationException(Text("The saved runner changed. Reopen Downtime before reviewing."));
        if (_staleReview)
            throw new InvalidOperationException(Text("Your runner changed after this preview. Discard the preview to plan a new change. Nothing from this preview was saved."));
        if (_journal is { Phase: Sr5DowntimeCalendarJournalPhase.Applying })
            throw new InvalidOperationException(Text("Resolve the interrupted Calendar save first."));
        if (_journal is not null)
            throw new InvalidOperationException(Text("Choose Change preview before editing this change."));
        bool previewed;
        string blocker;
        switch ((Sr5DowntimeCalendarOperation)_operation.SelectedIndex)
        {
            case Sr5DowntimeCalendarOperation.Add:
                previewed = _session.TryPreviewAdd(Guid.NewGuid(), ReadInt(_year.Text), ReadInt(_week.Text), out blocker);
                break;
            case Sr5DowntimeCalendarOperation.Edit when SelectedWeek() is { } edit:
                previewed = _session.TryPreviewEdit(edit.Identity.WeekId, _notes.Text, _notesColor.Text, out blocker);
                break;
            case Sr5DowntimeCalendarOperation.Delete when SelectedWeek() is { } delete:
                previewed = _session.TryPreviewDelete(delete.Identity.WeekId, out blocker);
                break;
            default: previewed = false; blocker = Text("Choose an exact saved week."); break;
        }
        if (!previewed) { await DisplayAlertAsync(Text("Preview unavailable"), blocker, Text("OK")); return; }
        if (!_journalStore.TryWriteReview(_session, _ownerId, out _journal, out blocker))
        { await DisplayAlertAsync(Text("Review not checkpointed"), blocker, Text("OK")); return; }
        _outcomeUnknown = false;
        _status.Text = Text("Review your change, then save or change it.");
        _status.TextColor = NativeTheme.Muted;
    }

    private async Task SaveReviewedAsync()
    {
        if (await ConfirmAsync()) await ApplyAsync();
    }

    private async Task<bool> ConfirmAsync()
    {
        long appearance = CaptureAppearanceGeneration();
        var session = _session;
        if (session?.State.Preview is not { } preview || _staleReview || _outcomeUnknown
            || _journal is not { Phase: Sr5DowntimeCalendarJournalPhase.Review } review) return false;
        RequireCurrentEntry(CancellationToken.None);
        if (!CurrentBindingMatches())
            throw new InvalidOperationException(Text("The saved runner changed. Reopen Downtime before confirming."));
        bool accepted = await DisplayAlertAsync(Text("Confirm Calendar change"),
            PreviewText(preview),
            Text("Confirm"),
            Text("Cancel"));
        if (!accepted) { session.TryConfirm(string.Empty); return false; }
        if (!IsCurrentAppearanceGeneration(appearance)) return false;
        RequireCurrentEntry(CancellationToken.None);
        if (!ReferenceEquals(session, _session) || !ReferenceEquals(preview, session.State.Preview)
            || !Equals(review, _journal) || !CurrentBindingMatches() || _staleReview || _outcomeUnknown)
            throw new InvalidOperationException(Text("The saved runner changed. Reopen Downtime before confirming."));
        return session.TryConfirm(preview.PreviewDigest);
    }

    private async Task ApplyAsync()
    {
        RequireCurrentEntry(CancellationToken.None);
        if (_session is null || !_session.State.CanApply
            || _journal is not { Phase: Sr5DowntimeCalendarJournalPhase.Review } review
            || _session.State.Preview is not { } preview)
            throw new InvalidOperationException(Text("Review and explicitly confirm the exact preview first."));
        if (!CurrentBindingMatches())
            throw new InvalidOperationException(Text("The saved runner changed before Applying. Reopen Downtime."));
        CareerCalendarAddRequest? add = preview.Operation == Sr5DowntimeCalendarOperation.Add ? _session.CreateAddRequest() : null;
        CareerCalendarEditRequest? edit = preview.Operation == Sr5DowntimeCalendarOperation.Edit ? _session.CreateEditRequest() : null;
        CareerCalendarDeleteRequest? delete = preview.Operation == Sr5DowntimeCalendarOperation.Delete ? _session.CreateDeleteRequest() : null;
        if (!_journalStore.TryBeginApplying(review, out Sr5DowntimeCalendarJournal applying, out string blocker))
            throw new InvalidOperationException(blocker);
        _journal = applying;
        _status.Text = Text("Saving calendar change…");
        _status.TextColor = NativeTheme.Muted;
        RefreshEnabledState();
        try
        {
            using (await _journalStore.AcquireApplyingLeaseAsync(applying, CancellationToken.None))
            {
                _ = preview.Operation switch
                {
                    Sr5DowntimeCalendarOperation.Add => await Coordinator.ApplyCareerCalendarAddAsync(add!),
                    Sr5DowntimeCalendarOperation.Edit => await Coordinator.ApplyCareerCalendarEditAsync(edit!),
                    Sr5DowntimeCalendarOperation.Delete => await Coordinator.ApplyCareerCalendarDeleteAsync(delete!),
                    _ => false
                };
            }
        }
        catch { MarkOutcomeUnknown(Text("Calendar save was interrupted after durable Applying.")); throw; }
        _load = await _authority.LoadAsync();
        if (!_load.IsReady) { MarkOutcomeUnknown(Text("The saved runner could not be projected after Calendar apply.")); return; }
        PopulateWeeks(_load.Editor!);
        if (_load.Binding!.WorkspaceRevision == review.Review.WorkspaceRevision + 1)
        {
            try
            {
                Sr5DowntimeCalendarPersistenceReceipt receipt = Sr5DowntimeCalendarPersistenceReceipt.Create(
                    applying, _load.Binding, _load.Editor!);
                if (_journalStore.TryComplete(applying, receipt, out _journal, out blocker))
                {
                    _status.Text = Text("Calendar change saved.");
                    _receiptStatus.Text = _status.Text;
                    _status.TextColor = NativeTheme.Success; _session = null; return;
                }
                MarkOutcomeUnknown(blocker); return;
            }
            catch (InvalidOperationException exception) { MarkOutcomeUnknown(exception.Message); return; }
        }
        if (_load.Binding.WorkspaceRevision == review.Review.WorkspaceRevision)
        {
            var restoredSession = new Sr5DowntimeCalendarDesktopSession();
            if (restoredSession.Bind(_load.Binding, _load.Editor!, review.Review).Resume.Restored
                && _journalStore.TryReturnToReview(applying, out _journal, out blocker))
            {
                _session = restoredSession; _status.Text = Text("Core proved no mutation. Confirm again."); return;
            }
        }
        MarkOutcomeUnknown(Text("Calendar result is outside its exact expected+1 CAS/postcondition boundary."));
    }

    private Task DiscardStaleReviewAsync() => DiscardReviewAsync(staleOnly: true);

    private Task EditReviewAsync() => DiscardReviewAsync(staleOnly: false);

    private Task DiscardReviewAsync(bool staleOnly)
    {
        RequireCurrentEntry(CancellationToken.None);
        if (_load is not { IsReady: true } || !CurrentBindingMatches())
            throw new InvalidOperationException(Text("The saved runner changed. Reopen Downtime before reviewing."));
        string blocker = string.Empty;
        if (_staleReview != staleOnly || _outcomeUnknown
            || _journal is not { Phase: Sr5DowntimeCalendarJournalPhase.Review } review
            || !_journalStore.TryDiscardUnappliedReview(review, out blocker))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(blocker)
                ? Text("Only an exact unapplied Downtime Calendar review can be discarded.") : blocker);
        _journal = null;
        _staleReview = false;
        if (!staleOnly) RestoreForm(review.Review.Preview);
        _session = new Sr5DowntimeCalendarDesktopSession();
        _session.Bind(_load.Binding!, _load.Editor!);
        _status.Text = Text("Preview discarded. Choose your new calendar change.");
        _status.TextColor = NativeTheme.Muted;
        return Task.CompletedTask;
    }

    private async Task ClearAppliedAsync()
    {
        string blocker = string.Empty;
        if (_journal is not { Phase: Sr5DowntimeCalendarJournalPhase.Applied } applied
            || !_journalStore.TryClearResolved(applied, out blocker))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(blocker)
                ? Text("Only an exact Applied Calendar receipt can be cleared.") : blocker);
        _journal = null;
        _load = await _authority.LoadAsync();
        _session = null;
        if (_load.IsReady)
        {
            PopulateWeeks(_load.Editor!);
            _session = new Sr5DowntimeCalendarDesktopSession();
            _session.Bind(_load.Binding!, _load.Editor!);
        }
        _status.Text = Text("Choose your next calendar change.");
        _status.TextColor = NativeTheme.Muted;
        _receiptStatus.Text = string.Empty;
    }

    private void MarkOutcomeUnknown(string message)
    {
        _outcomeUnknown = true;
        _status.Text = Format("{0} The durable Applying journal remains locked.", message);
        _outcomeUnknownStatus.Text = _status.Text;
        _status.TextColor = NativeTheme.Danger;
    }

    private void RefreshEnabledState()
    {
        bool current = _load is { IsReady: true } && !_outcomeUnknown && CurrentBindingMatches();
        bool ready = current && !_staleReview;
        bool review = _journal?.Phase == Sr5DowntimeCalendarJournalPhase.Review;
        bool applying = _journal?.Phase == Sr5DowntimeCalendarJournalPhase.Applying;
        bool applied = _journal?.Phase == Sr5DowntimeCalendarJournalPhase.Applied;
        bool editing = ready && !review && !applying && !applied;
        _bindingCard.IsVisible = _load is not { IsReady: true };
        _discardReview.IsVisible = _staleReview && review && !_outcomeUnknown;
        _discardReview.IsEnabled = _discardReview.IsVisible && current && _entryStillCurrent?.Invoke() != false;
        bool targetRequired = _operation.SelectedIndex is 1 or 2;
        _form.IsVisible = !review && !applying && !applied && !_outcomeUnknown;
        _form.IsEnabled = editing;
        _operation.IsEnabled = editing;
        _weekPicker.IsVisible = targetRequired;
        _weekField.IsVisible = targetRequired;
        _emptyWeeks.IsVisible = targetRequired && (_load?.Editor?.Weeks.Count ?? 0) == 0;
        _year.IsVisible = _operation.SelectedIndex == 0 && (_load?.Editor?.Weeks.Count ?? 0) == 0;
        _week.IsVisible = _year.IsVisible;
        _firstWeekFields.IsVisible = _year.IsVisible;
        _notes.IsVisible = _operation.SelectedIndex == 1; _notesColor.IsVisible = _notes.IsVisible;
        _notesFields.IsVisible = _notes.IsVisible;
        _year.IsEnabled = _week.IsEnabled = _notes.IsEnabled = _notesColor.IsEnabled = editing;
        _weekPicker.IsEnabled = editing && targetRequired;
        _review.IsVisible = _form.IsVisible;
        _review.IsEnabled = editing && (!targetRequired || SelectedWeek() is not null);
        Sr5DowntimeCalendarPreview? preview = _session?.State.Preview;
        _preview.Text = preview is null ? string.Empty : PreviewText(preview);
        _previewCard.IsVisible = review && !_staleReview && preview is not null && !_outcomeUnknown;
        _editReview.IsVisible = _previewCard.IsVisible;
        _editReview.IsEnabled = _editReview.IsVisible && ready;
        _apply.IsVisible = _previewCard.IsVisible;
        _apply.IsEnabled = ready && _apply.IsVisible;
        _clear.IsVisible = applied; _clear.IsEnabled = applied;
        _outcomeUnknownStatus.IsVisible = _outcomeUnknown;
        _receiptStatus.IsVisible = applied;
        _outcomeUnknownCard.IsVisible = _outcomeUnknownStatus.IsVisible;
        _receiptCard.IsVisible = applied;
        _statusCard.IsVisible = !applied && !_outcomeUnknownCard.IsVisible && !string.IsNullOrWhiteSpace(_status.Text);
    }

    private bool CurrentBindingMatches()
        => _load?.Binding is { } binding
            && Coordinator.State.WorkspaceId is { } workspaceId
            && string.Equals(binding.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
            && binding.WorkspaceRevision == Coordinator.State.ContentRevision
            && binding.SavedRevision == Coordinator.State.SavedRevision
            && !Coordinator.State.IsDirty
            && Coordinator.State.Error is null;

    private static int ReadInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
}
