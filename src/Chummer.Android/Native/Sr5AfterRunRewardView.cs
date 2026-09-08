using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Stable native controls over the real reward model. Rendering never rebuilds
/// focused editors or submits a command. Only explicit buttons execute actions
/// through the owning page's action gate and current appearance lifetime.
/// </summary>
public sealed class Sr5AfterRunRewardView : ContentView
{
    private const int HistoryPageSize = 25;
    private readonly Sr5AfterRunRewardPhoneModel _model;
    private readonly Func<Func<Task>, Task> _runAction;
    private readonly Func<Task> _finish;
    private readonly Func<CancellationToken, Task>? _reviewConsequences;
    private readonly Func<CancellationToken, Task>? _planDowntime;
    private readonly Func<CancellationToken, Task>? _reviewReputation;
    private readonly Entry _karma, _nuyen;
    private readonly Editor _reason;
    private readonly DatePicker _date;
    private readonly TimePicker _time;
    private readonly CheckBox _noAward;
    private readonly Label _status, _review, _receipt, _historyPosition;
    private readonly ActivityIndicator _working;
    private readonly Button _preview, _confirm, _recover, _retry, _done, _consequences, _downtime, _reputation, _resume, _previous, _next;
    private readonly Picker _history;
    private readonly VerticalStackLayout _historySection;
    private IReadOnlyList<Sr5AfterRunRewardCheckpoint> _historySource = [];
    private Sr5AfterRunRewardCheckpoint[] _historyItems = [];
    private int _historyPage;
    private bool _rendering, _actionActive;
    private CancellationToken _lifetime;

    public Sr5AfterRunRewardView(Sr5AfterRunRewardPhoneModel model,
        Func<Func<Task>, Task> runAction, Func<Task> finish,
        Func<CancellationToken, Task>? reviewConsequences = null,
        Func<CancellationToken, Task>? planDowntime = null,
        Func<CancellationToken, Task>? reviewReputation = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _runAction = runAction ?? throw new ArgumentNullException(nameof(runAction));
        _finish = finish ?? throw new ArgumentNullException(nameof(finish));
        _reviewConsequences = reviewConsequences;
        _planDowntime = planDowntime;
        _reviewReputation = reviewReputation;
        VerticalStackLayout body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Title(T("AfterRunRewardTitle", "After Run · Rewards")));
        body.Add(NativeTheme.Body(T("AfterRunRewardIntro",
            "Record a local reward for this runner. Review the changes before saving. This does not approve run consequences."), NativeTheme.Muted));
        body.Add(NativeTheme.Body(T("AfterRunRewardExperimental",
            "Experimental workflow — not yet covered by the current phone release's device tests."), NativeTheme.Muted));
        _status = NativeTheme.Body(string.Empty);
        _status.AutomationId = "sr5-reward-status";
        body.Add(_status);
        _working = new() { AutomationId = "sr5-reward-working", Color = NativeTheme.Success };
        body.Add(_working);

        _karma = NativeTheme.TextField("sr5-reward-karma", model.Draft.Karma);
        _nuyen = NativeTheme.TextField("sr5-reward-nuyen", model.Draft.Nuyen);
        _karma.Keyboard = _nuyen.Keyboard = Keyboard.Numeric;
        _karma.MaxLength = _nuyen.MaxLength = int.MaxValue.ToString(CultureInfo.InvariantCulture).Length;
        _reason = NativeTheme.TextArea("sr5-reward-reason", model.Draft.Reason);
        _reason.MaxLength = CharacterCareerManualKarmaRules.MaximumReasonLength;
        _date = new() { AutomationId = "sr5-reward-date", Date = model.Draft.Date };
        _time = new() { AutomationId = "sr5-reward-time", Time = model.Draft.Time };
        _noAward = new() { AutomationId = "sr5-reward-no-award" };
        AddField(body, T("AfterRunRewardKarma", "Karma gained"), _karma);
        AddField(body, T("AfterRunRewardNuyen", "Nuyen gained"), _nuyen);
        AddField(body, T("AfterRunRewardReason", "Reason / run name (optional)"), _reason);
        AddField(body, T("AfterRunRewardDate", "Entry date"), _date);
        AddField(body, T("AfterRunRewardTime", "Local time"), _time);
        AddField(body, T("AfterRunRewardNoAward", "Explicitly record no reward (0 Karma / 0 Nuyen)"), _noAward);
        _karma.TextChanged += (_, _) => DraftChanged();
        _nuyen.TextChanged += (_, _) => DraftChanged();
        _reason.TextChanged += (_, _) => DraftChanged();
        _date.DateSelected += (_, _) => DraftChanged();
        _time.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(TimePicker.Time)) DraftChanged(); };
        _noAward.CheckedChanged += (_, _) => DraftChanged();

        _preview = Button("sr5-reward-preview", T("AfterRunRewardPreview", "Review reward"),
            token => _model.PreviewAsync(CultureInfo.CurrentCulture, token));
        body.Add(_preview);
        _review = NativeTheme.Body(string.Empty);
        _review.AutomationId = "sr5-reward-review";
        body.Add(_review);
        _confirm = Button("sr5-reward-confirm", T("AfterRunRewardConfirm", "Confirm and save this reward"), _model.ConfirmAsync);
        body.Add(_confirm);
        _recover = Button("sr5-reward-recover", T("AfterRunRewardRecover", "Check saved result"), _model.RecoverAsync);
        body.Add(_recover);
        _retry = Button("sr5-reward-retry", T("AfterRunRewardRetry", "Retry the same confirmed reward"), _model.RetryAsync);
        body.Add(_retry);
        _receipt = NativeTheme.Body(string.Empty, NativeTheme.Success);
        _receipt.AutomationId = "sr5-reward-receipt";
        body.Add(_receipt);
        _downtime = Button("sr5-reward-downtime", T("AfterRunRewardDowntime", "Plan downtime"), async token =>
        {
            if (_model.CanContinue && _planDowntime is not null) await _planDowntime(token);
        });
        body.Add(_downtime);
        _reputation = Button("sr5-reward-reputation", T("ReputationWizardTitle", "Career · Reputation"), async token =>
        {
            if (_model.CanContinue && _reviewReputation is not null) await _reviewReputation(token);
        });
        body.Add(_reputation);
        body.Add(NativeTheme.Body(T("AfterRunRewardDowntimeBoundary",
            "Plan your calendar locally. Each calendar change needs its own review and confirmation; it does not spend Karma, grant rewards or approve run consequences."), NativeTheme.Muted));
        _done = Button("sr5-reward-done", T("AfterRunRewardDone", "Done · return to Career"), async _ =>
        {
            if (_model.CanContinue) await _finish();
        });
        body.Add(_done);
        _consequences = Button("sr5-reward-consequences", T("AfterRunRewardConsequences", "Review run consequences"), async token =>
        {
            if (_model.CanContinue && _reviewConsequences is not null) await _reviewConsequences(token);
        });
        body.Add(_consequences);
        body.Add(NativeTheme.Body(T("AfterRunRewardConsequencesBoundary",
            "Heat, reputation and contacts require a separate reviewed run proposal. Checking it does not grant rewards again or approve consequences."), NativeTheme.Muted));

        _historySection = new() { Spacing = 10 };
        _historySection.Add(NativeTheme.FieldLabel(T("AfterRunRewardHistory", "Previously recorded rewards")));
        _history = new() { AutomationId = "sr5-reward-history", Title = T("AfterRunRewardHistoryChoose", "Choose a recorded reward") };
        _history.SelectedIndexChanged += (_, _) => { if (!_rendering) Refresh(); };
        _historySection.Add(_history);
        _historyPosition = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _historyPosition.AutomationId = "sr5-reward-history-position";
        _historySection.Add(_historyPosition);
        _previous = Button("sr5-reward-history-previous", T("AfterRunRewardHistoryPrevious", "Previous rewards"), _ =>
        {
            if (_model.CanEdit && _historyPage > 0) { _historyPage--; BindHistory(force: true); }
            return Task.CompletedTask;
        });
        _next = Button("sr5-reward-history-next", T("AfterRunRewardHistoryNext", "More rewards"), _ =>
        {
            if (_model.CanEdit && (_historyPage + 1) * HistoryPageSize < _historySource.Count)
            { _historyPage++; BindHistory(force: true); }
            return Task.CompletedTask;
        });
        _resume = Button("sr5-reward-history-resume", T("AfterRunRewardHistoryResume", "Verify selected saved reward"), async token =>
        {
            int index = _history.SelectedIndex;
            if (index >= 0 && index < _historyItems.Length)
                await _model.ResumeRecordedRewardAsync(_historyItems[index].Command.OperationId, token);
        });
        _historySection.Add(_previous);
        _historySection.Add(_next);
        _historySection.Add(_resume);
        body.Add(_historySection);
        Content = body;
        Refresh();
    }

    public void SetLifetime(CancellationToken cancellationToken) => _lifetime = cancellationToken;

    private static string T(string key, string fallback) => PhoneStrings.Get(key, fallback);

    private static void AddField(VerticalStackLayout body, string label, View field)
    {
        body.Add(NativeTheme.FieldLabel(label));
        SemanticProperties.SetDescription(field, label);
        body.Add(field);
    }

    private Button Button(string id, string label, Func<CancellationToken, Task> action)
    {
        var button = NativeTheme.PrimaryButton(label);
        button.AutomationId = id;
        button.Clicked += async (_, _) => await ExecuteAsync(action);
        return button;
    }

    private Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        // Capture this appearance before awaiting the page gate. A later page
        // appearance must never renew an already canceled click's authority.
        CancellationToken token = _lifetime;
        return _runAction(async () =>
        {
            if (_actionActive || token.IsCancellationRequested) return;
            _actionActive = true;
            Refresh();
            try { await action(token); }
            finally
            {
                _actionActive = false;
                // A return can happen before the old canceled action drains.
                // Render this model's latest state for a live NEW appearance,
                // without renewing the old click's captured mutation token.
                if (!_lifetime.IsCancellationRequested) Refresh();
            }
        });
    }

    private void DraftChanged()
    {
        if (_rendering || _actionActive) return;
        _model.UpdateDraft(new(_karma.Text ?? string.Empty, _nuyen.Text ?? string.Empty,
            _reason.Text ?? string.Empty, _date.Date ?? _model.Draft.Date,
            _time.Time ?? _model.Draft.Time, _noAward.IsChecked));
        Refresh();
    }

    public void Refresh()
    {
        if (_rendering) return;
        _rendering = true;
        try
        {
            bool busy = _actionActive || _model.IsBusy;
            bool editable = !busy && _model.CanEdit;
            _working.IsVisible = _working.IsRunning = busy || _model.Status == Sr5AfterRunRewardPhoneStatus.Loading;
            _status.Text = StatusText(busy);
            // Assign only changed values. Coordinator refreshes cannot replace
            // the native editor or normalize partial user input while typing.
            if (_karma.Text != _model.Draft.Karma) _karma.Text = _model.Draft.Karma;
            if (_nuyen.Text != _model.Draft.Nuyen) _nuyen.Text = _model.Draft.Nuyen;
            if (_reason.Text != _model.Draft.Reason) _reason.Text = _model.Draft.Reason;
            if (_date.Date != _model.Draft.Date) _date.Date = _model.Draft.Date;
            if (_time.Time != _model.Draft.Time) _time.Time = _model.Draft.Time;
            _noAward.IsChecked = _model.Draft.NoAward;
            _karma.IsEnabled = _nuyen.IsEnabled = editable;
            _reason.IsEnabled = _date.IsEnabled = _time.IsEnabled = _noAward.IsEnabled = editable;
            _preview.IsEnabled = editable;
            _confirm.IsVisible = _model.Review is not null;
            _confirm.IsEnabled = !busy && _model.CanConfirm;
            _review.IsVisible = _model.CanConfirm;
            _review.Text = _model.Review is { } review ? PreviewText(review.Preview) : string.Empty;
            _recover.IsVisible = _retry.IsVisible = _model.HasRetainedIntent && !_model.CanContinue;
            _recover.IsEnabled = !busy && _model.CanRecover;
            _retry.IsEnabled = !busy && _model.CanRetry;
            _done.IsVisible = _done.IsEnabled = !busy && _model.CanContinue;
            _consequences.IsVisible = _reviewConsequences is not null && _model.CanContinue;
            _consequences.IsEnabled = !busy && _consequences.IsVisible;
            _downtime.IsVisible = _planDowntime is not null && _model.CanContinue;
            _downtime.IsEnabled = !busy && _downtime.IsVisible;
            _reputation.IsVisible = _reviewReputation is not null && _model.CanContinue;
            _reputation.IsEnabled = !busy && _reputation.IsVisible;
            _receipt.IsVisible = _model.CanContinue;
            _receipt.Text = _model.Handoff is { } saved
                ? PhoneStrings.Format("AfterRunRewardSaved", "Saved. Current balance: {0} Karma · {1} Nuyen.",
                    saved.Snapshot.AvailableKarma.ToString("N0", CultureInfo.CurrentCulture),
                    saved.Snapshot.AvailableNuyen.ToString("N2", CultureInfo.CurrentCulture)) : string.Empty;
            BindHistory();
            _historySection.IsVisible = _historySource.Count > 0 && !_model.HasRetainedIntent;
            _history.IsEnabled = editable;
            _previous.IsEnabled = editable && _historyPage > 0;
            _next.IsEnabled = editable && (_historyPage + 1) * HistoryPageSize < _historySource.Count;
            _resume.IsEnabled = editable && _history.SelectedIndex >= 0;
        }
        finally { _rendering = false; }
    }

    private void BindHistory(bool force = false)
    {
        if (!force && ReferenceEquals(_historySource, _model.RecordedRewards)) return;
        bool wasRendering = _rendering;
        _rendering = true;
        try
        {
            if (!ReferenceEquals(_historySource, _model.RecordedRewards)) _historyPage = 0;
            _historySource = _model.RecordedRewards;
            _historyItems = _historySource.Reverse().Skip(_historyPage * HistoryPageSize).Take(HistoryPageSize).ToArray();
            _history.ItemsSource = _historyItems.Select(entry => PhoneStrings.Format("AfterRunRewardHistoryItem",
                "{0} · {1} · {2} Karma / {3} Nuyen", entry.Command.ExpenseDateLocal.ToString("g", CultureInfo.CurrentCulture),
                entry.Command.Reason.Length > 80 ? entry.Command.Reason[..80] + "…" : entry.Command.Reason,
                entry.Command.KarmaAmount.ToString("N0", CultureInfo.CurrentCulture),
                entry.Command.NuyenAmount.ToString("N0", CultureInfo.CurrentCulture))).ToArray();
            _history.SelectedIndex = -1;
            _historyPosition.Text = PhoneStrings.Format("AfterRunRewardHistoryPosition", "Page {0} of {1}",
                _historyPage + 1, Math.Max(1, (_historySource.Count + HistoryPageSize - 1) / HistoryPageSize));
        }
        finally { _rendering = wasRendering; }
    }

    private static string PreviewText(CharacterAfterRunRewardPreview preview)
        => PhoneStrings.Format("AfterRunRewardPreviewSummary",
            "Karma: {0} → {1}\nNuyen: {2} → {3}\n{4} · {5}",
            preview.KarmaBefore.ToString("N0", CultureInfo.CurrentCulture),
            preview.KarmaAfter.ToString("N0", CultureInfo.CurrentCulture),
            preview.NuyenBefore.ToString("N2", CultureInfo.CurrentCulture),
            preview.NuyenAfter.ToString("N2", CultureInfo.CurrentCulture),
            preview.Command.ExpenseDateLocal.ToString("g", CultureInfo.CurrentCulture), preview.Command.Reason);

    private string StatusText(bool busy)
    {
        if (busy) return T("AfterRunRewardWorking", "Working… Please wait.");
        if (!_model.HasCurrentSelection)
            return T("AfterRunRewardRunnerChanged", "The selected runner changed. Reopen After Run for the current saved runner.");
        return _model.Status switch
        {
            Sr5AfterRunRewardPhoneStatus.Loading => T("AfterRunRewardLoading", "Checking saved rewards…"),
            Sr5AfterRunRewardPhoneStatus.Editing => T("AfterRunRewardEditing", "Enter the reward, then review it."),
            Sr5AfterRunRewardPhoneStatus.Review => T("AfterRunRewardReviewReady", "Check the exact changes below before confirming."),
            Sr5AfterRunRewardPhoneStatus.InvalidInput => T("AfterRunRewardInvalid", "Check the non-negative whole amounts, date and note. For 0 / 0, explicitly select no reward."),
            Sr5AfterRunRewardPhoneStatus.ReviewUnavailable => T("AfterRunRewardReviewUnavailable", "A current reward preview is unavailable. Recheck the saved runner and try reviewing again."),
            Sr5AfterRunRewardPhoneStatus.Pending => T("AfterRunRewardPending", "An earlier reward is unfinished. Check its saved result before doing anything else."),
            Sr5AfterRunRewardPhoneStatus.OutcomeUnknown => T("AfterRunRewardUnknown", "The result is uncertain. Do not enter it again as a new reward. Check the result or explicitly retry this same operation."),
            Sr5AfterRunRewardPhoneStatus.Recorded => T("AfterRunRewardRecorded", "Reward recorded and saved runner reloaded."),
            Sr5AfterRunRewardPhoneStatus.RefreshRequired => T("AfterRunRewardRefreshRequired", "The reward is retained, but the current result could not be loaded. Check the saved result; do not award it again."),
            Sr5AfterRunRewardPhoneStatus.RunnerChanged => T("AfterRunRewardRunnerChanged", "The selected runner changed. Reopen After Run for the current saved runner."),
            _ => T("AfterRunRewardJournalUnavailable", "A pending Career operation or unreadable journal prevents a new reward. Resolve that operation first; nothing has been cleared.")
        };
    }
}
