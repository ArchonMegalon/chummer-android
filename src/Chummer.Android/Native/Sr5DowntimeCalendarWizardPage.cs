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
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Label _binding;
    private readonly Label _status;
    private readonly Label _outcomeUnknownStatus;
    private readonly Label _receiptStatus;
    private readonly Picker _operation;
    private readonly Picker _weekPicker;
    private readonly Entry _year;
    private readonly Entry _week;
    private readonly Editor _notes;
    private readonly Entry _notesColor;
    private readonly Label _preview;
    private readonly Button _review;
    private readonly Button _confirm;
    private readonly Button _apply;
    private readonly Button _clear;
    private Sr5DowntimeCalendarPhoneLoadResult? _load;
    private Sr5DowntimeCalendarDesktopSession? _session;
    private Sr5DowntimeCalendarJournal? _journal;
    private bool _loaded;
    private bool _outcomeUnknown;

    public Sr5DowntimeCalendarWizardPage(RunnerSessionCoordinator coordinator)
        : this(coordinator, new RunnerSessionSr5DowntimeCalendarAuthority(coordinator),
            Sr5DowntimeCalendarJournalStore.CreateDefault()) { }

    internal Sr5DowntimeCalendarWizardPage(
        RunnerSessionCoordinator coordinator,
        RunnerSessionSr5DowntimeCalendarAuthority authority,
        Sr5DowntimeCalendarJournalStore journalStore) : base(coordinator)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        Title = Text("Downtime calendar");
        AutomationId = "sr5-downtime-calendar-page";
        VerticalStackLayout body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Table · Downtime")));
        body.Add(NativeTheme.Title(Text("Plan one calendar change")));
        body.Add(NativeTheme.Body(
            Text("Review a deterministic Core-owned Calendar change, confirm it, then save once. Restart never preserves confirmation."),
            NativeTheme.Muted));
        _binding = NativeTheme.Body(Text("Loading exact runner authority…"), NativeTheme.Muted);
        _binding.AutomationId = "sr5-downtime-calendar-binding";
        body.Add(NativeTheme.Card(_binding));
        _operation = new Picker
        {
            AutomationId = "sr5-downtime-calendar-operation", Title = Text("Calendar action"),
            ItemsSource = new[] { Text("Add next week"), Text("Edit week"), Text("Delete week") }, SelectedIndex = 0,
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text
        };
        _operation.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        body.Add(NativeTheme.FieldLabel(Text("Action")));
        body.Add(_operation);
        _weekPicker = new Picker
        {
            AutomationId = "sr5-downtime-calendar-week", Title = Text("Exact saved week"),
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text
        };
        _weekPicker.SelectedIndexChanged += (_, _) => SelectWeek();
        body.Add(NativeTheme.FieldLabel(Text("Saved week")));
        body.Add(_weekPicker);
        _year = new Entry
        {
            AutomationId = "sr5-downtime-calendar-year", Keyboard = Keyboard.Numeric, MaxLength = 4,
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text,
            Text = Math.Clamp(DateTime.Today.Year + 62,
                    CharacterCareerCalendarRules.FirstWeekMinimumYear,
                    CharacterCareerCalendarRules.FirstWeekMaximumYear).ToString(CultureInfo.InvariantCulture)
        };
        _week = new Entry
        {
            AutomationId = "sr5-downtime-calendar-iso-week", Keyboard = Keyboard.Numeric, MaxLength = 2,
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text, Text = "1"
        };
        body.Add(NativeTheme.FieldLabel(Text("First year (empty calendar only)")));
        body.Add(_year);
        body.Add(NativeTheme.FieldLabel(Text("First ISO week")));
        body.Add(_week);
        _notes = NativeTheme.TextArea("sr5-downtime-calendar-notes", Text("Exact week notes"), Text("Downtime notes"));
        body.Add(NativeTheme.FieldLabel(Text("Notes (edit only)")));
        body.Add(_notes);
        _notesColor = new Entry
        {
            AutomationId = "sr5-downtime-calendar-notes-color", MaxLength = 64,
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text,
            Text = CharacterCareerCalendarRules.DefaultNotesColor
        };
        body.Add(NativeTheme.FieldLabel(Text("Notes color (edit only)")));
        body.Add(_notesColor);
        _review = NativeTheme.PrimaryButton(Text("Create exact preview"));
        _review.AutomationId = "sr5-downtime-calendar-review";
        _review.Clicked += async (_, _) => await RunAsync(ReviewAsync);
        body.Add(_review);
        _preview = NativeTheme.Body(Text("No reviewed change."), NativeTheme.Muted);
        _preview.AutomationId = "sr5-downtime-calendar-preview";
        body.Add(NativeTheme.Card(_preview));
        _confirm = NativeTheme.SecondaryButton(Text("Confirm reviewed preview"));
        _confirm.AutomationId = "sr5-downtime-calendar-confirm";
        _confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
        body.Add(_confirm);
        _apply = NativeTheme.PrimaryButton(Text("Save confirmed change"));
        _apply.AutomationId = "sr5-downtime-calendar-apply";
        _apply.Clicked += async (_, _) => await RunAsync(ApplyAsync);
        body.Add(_apply);
        _status = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _status.AutomationId = "sr5-downtime-calendar-status";
        body.Add(NativeTheme.Card(_status));
        _outcomeUnknownStatus = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _outcomeUnknownStatus.AutomationId = "sr5-downtime-calendar-outcome-unknown";
        body.Add(NativeTheme.Card(_outcomeUnknownStatus));
        _receiptStatus = NativeTheme.Body(string.Empty, NativeTheme.Success);
        _receiptStatus.AutomationId = "sr5-downtime-calendar-receipt";
        body.Add(NativeTheme.Card(_receiptStatus));
        _clear = NativeTheme.SecondaryButton(Text("Start another calendar change"));
        _clear.AutomationId = "sr5-downtime-calendar-clear-applied";
        _clear.Clicked += async (_, _) => await RunAsync(ClearAppliedAsync);
        body.Add(_clear);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        try { await LoadAndRecoverAsync(); }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.TextColor = NativeTheme.Danger;
        }
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private async Task LoadAndRecoverAsync()
    {
        _load = await _authority.LoadAsync();
        if (!_load.IsReady)
        {
            _binding.Text = _load.Blocker ?? Text("Exact Downtime Calendar authority is unavailable.");
            _binding.TextColor = NativeTheme.Danger;
            return;
        }
        _binding.Text = Format(
            "{0} · saved revision {1} · exact SR5 runtime/source/content",
            _load.Binding!.WorkspaceId,
            _load.Binding.WorkspaceRevision.ToString(CultureInfo.InvariantCulture));
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

    private void RecoverJournal(Sr5DowntimeCalendarJournal journal)
    {
        if (_load is not { IsReady: true }) return;
        if (journal.Phase == Sr5DowntimeCalendarJournalPhase.Applied)
        {
            _status.Text = Format(
                "Applied receipt {0}… at revision {1}.",
                journal.Receipt!.ReceiptDigest[..19],
                journal.Receipt.AppliedWorkspaceRevision);
            _status.TextColor = NativeTheme.Success;
            _receiptStatus.Text = _status.Text;
            return;
        }
        if (journal.Phase == Sr5DowntimeCalendarJournalPhase.Review)
        {
            _session = new Sr5DowntimeCalendarDesktopSession();
            if (!_session.Bind(_load.Binding!, _load.Editor!, journal.Review).Resume.Restored)
            {
                MarkOutcomeUnknown(Text("The durable review no longer matches the exact runner snapshot."));
                return;
            }
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
                    _status.Text = Text("Interrupted save verified from its exact postcondition and receipt.");
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
        => $"{value.Year.ToString(CultureInfo.InvariantCulture)} W{value.Week.ToString("00", CultureInfo.InvariantCulture)} · {value.Identity.WeekId:D}";

    private async Task ReviewAsync()
    {
        if (_session is null || _load is not { IsReady: true }) return;
        if (!CurrentBindingMatches())
            throw new InvalidOperationException(Text("The saved runner changed. Reopen Downtime before reviewing."));
        if (_journal is { Phase: Sr5DowntimeCalendarJournalPhase.Applying })
            throw new InvalidOperationException(Text("Resolve the interrupted Calendar save first."));
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
        _status.Text = Text("Exact preview is durable. Confirmation is still required.");
        _status.TextColor = NativeTheme.Muted;
    }

    private async Task ConfirmAsync()
    {
        if (_session?.State.Preview is not { } preview
            || _journal is not { Phase: Sr5DowntimeCalendarJournalPhase.Review }) return;
        if (!CurrentBindingMatches())
            throw new InvalidOperationException(Text("The saved runner changed. Reopen Downtime before confirming."));
        bool accepted = await DisplayAlertAsync(Text("Confirm Calendar change"),
            Format("{0} This exact preview will be the only allowed save.", preview.Summary),
            Text("Confirm"),
            Text("Cancel"));
        if (_session.TryConfirm(accepted ? preview.PreviewDigest : string.Empty))
        { _status.Text = Text("Preview confirmed for this foreground session."); _status.TextColor = NativeTheme.Text; }
    }

    private async Task ApplyAsync()
    {
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
                    _status.Text = Format("Saved with verified receipt {0}…", receipt.ReceiptDigest[..19]);
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
        _status.Text = Text("Applied receipt cleared. A new preview may be created.");
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
        bool ready = _load is { IsReady: true } && !_outcomeUnknown && CurrentBindingMatches();
        bool review = _journal?.Phase == Sr5DowntimeCalendarJournalPhase.Review;
        bool applying = _journal?.Phase == Sr5DowntimeCalendarJournalPhase.Applying;
        bool applied = _journal?.Phase == Sr5DowntimeCalendarJournalPhase.Applied;
        bool targetRequired = _operation.SelectedIndex is 1 or 2;
        _operation.IsEnabled = ready && !applying && !applied;
        _weekPicker.IsVisible = targetRequired; _weekPicker.IsEnabled = ready && targetRequired && !applying && !applied;
        _year.IsVisible = _operation.SelectedIndex == 0 && (_load?.Editor?.Weeks.Count ?? 0) == 0;
        _week.IsVisible = _year.IsVisible;
        _notes.IsVisible = _operation.SelectedIndex == 1; _notesColor.IsVisible = _notes.IsVisible;
        _review.IsEnabled = ready && !applying && !applied && (!targetRequired || SelectedWeek() is not null);
        Sr5DowntimeCalendarPreview? preview = _session?.State.Preview;
        _preview.Text = preview is null ? Text("No reviewed change.")
            : Format(
                "{0}\nOperation: {1}\nWeek: {2}\nPreview: {3}…",
                preview.Summary,
                preview.Operation,
                preview.WeekId.ToString("D"),
                preview.PreviewDigest[..19]);
        _confirm.IsVisible = review; _confirm.IsEnabled = ready && review && preview is not null;
        _apply.IsVisible = review; _apply.IsEnabled = ready && review && _session?.State.CanApply == true;
        _clear.IsVisible = applied; _clear.IsEnabled = applied;
        _outcomeUnknownStatus.IsVisible = applying || _outcomeUnknown;
        _receiptStatus.IsVisible = applied;
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
