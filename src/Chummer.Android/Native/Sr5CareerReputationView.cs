using System.Globalization;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Stable native editors and explicit review actions; no rule calculations or XML edits.</summary>
public sealed class Sr5CareerReputationView : ContentView
{
    private readonly Sr5CareerReputationPhoneModel _model;
    private readonly Func<Func<Task>, Task> _run;
    private readonly Func<Task> _finish;
    private readonly Entry _streetCred, _notoriety, _awareness;
    private readonly Editor _reason;
    private readonly Label _status, _facts, _review, _receipt;
    private readonly ActivityIndicator _working;
    private readonly Button _preview, _burn, _confirm, _recover, _retry, _close, _reload, _done, _resume, _older, _newer;
    private readonly Picker _history;
    private Sr5CareerReputationCheckpoint[] _historyEntries = [];
    private IReadOnlyList<Sr5CareerReputationCheckpoint>? _historySource, _supersededSource;
    private int _historyPage;
    private bool _rendering, _actionActive;
    private CancellationToken _lifetime;
    private const int PageSize = 20;

    public Sr5CareerReputationView(Sr5CareerReputationPhoneModel model, Func<Func<Task>, Task> run, Func<Task> finish)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _finish = finish ?? throw new ArgumentNullException(nameof(finish));
        VerticalStackLayout body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Title(T("Title")));
        body.Add(NativeTheme.Body(T("Intro"), NativeTheme.Muted));
        body.Add(NativeTheme.Body(T("Scope"), NativeTheme.Muted));
        _status = NativeTheme.Body(""); _status.AutomationId = "sr5-reputation-status"; body.Add(_status);
        _working = new() { AutomationId = "sr5-reputation-working", Color = NativeTheme.Success }; body.Add(_working);
        _facts = NativeTheme.Body(""); _facts.AutomationId = "sr5-reputation-facts"; body.Add(_facts);
        body.Add(NativeTheme.Body(T("InputHelp"), NativeTheme.Muted));
        _streetCred = Field(body, "street-cred", "StreetCred");
        _notoriety = Field(body, "notoriety", "Notoriety");
        _awareness = Field(body, "awareness", "Awareness");
        _reason = NativeTheme.TextArea("sr5-reputation-reason", model.Draft.Reason);
        _reason.MaxLength = CharacterCareerReputationTransaction.MaximumReasonLength;
        body.Add(NativeTheme.FieldLabel(T("Reason"))); SemanticProperties.SetDescription(_reason, T("Reason")); body.Add(_reason);
        _reason.TextChanged += (_, _) => DraftChanged();
        _preview = AddButton(body, "preview", "Preview", t => model.PreviewAsync(culture: CultureInfo.CurrentCulture, token: t));
        _burn = AddButton(body, "burn", "Burn", t => model.PreviewAsync(burnStreetCred: true, token: t));
        _review = NativeTheme.Body(""); _review.AutomationId = "sr5-reputation-review"; body.Add(_review);
        _confirm = AddButton(body, "confirm", "Confirm", model.ConfirmAsync);
        _recover = AddButton(body, "recover", "Recover", model.RecoverAsync);
        _retry = AddButton(body, "retry", "Retry", model.RetryAsync);
        _close = AddButton(body, "close-superseded", "CloseSuperseded", model.CloseSupersededAsync);
        _reload = AddButton(body, "reload", "Reload", model.InitializeAsync);
        _receipt = NativeTheme.Body(""); _receipt.AutomationId = "sr5-reputation-receipt"; body.Add(_receipt);
        _done = AddButton(body, "done", "Done", async _ => { if (model.CanFinish) await _finish(); });
        body.Add(NativeTheme.FieldLabel(T("History")));
        _history = new() { AutomationId = "sr5-reputation-history", Title = T("ChooseHistory") };
        _history.SelectedIndexChanged += (_, _) => { if (!_rendering) Refresh(); }; body.Add(_history);
        _newer = AddButton(body, "newer", "Newer", _ => { if (_model.CanEdit && _historyPage > 0) { _historyPage--; BindHistory(); } return Task.CompletedTask; });
        _older = AddButton(body, "older", "Older", _ => { if (_model.CanEdit && (_historyPage + 1) * PageSize < HistoryCount) { _historyPage++; BindHistory(); } return Task.CompletedTask; });
        _resume = AddButton(body, "resume", "Resume", async token =>
        {
            int index = _history.SelectedIndex;
            if (index >= 0 && index < _historyEntries.Length)
                await model.ResumeHistoryAsync(_historyEntries[index].Command.Request.OperationId, token);
        });
        Content = body;
        Refresh();
    }

    public void SetLifetime(CancellationToken token) { _lifetime = token; Refresh(); }
    private static string T(string key) => PhoneStrings.Get("ReputationWizard" + key, key);
    private static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    private int HistoryCount => _model.History.Count + _model.SupersededHistory.Count;
    private Entry Field(VerticalStackLayout body, string id, string key)
    {
        body.Add(NativeTheme.FieldLabel(T(key)));
        // Numeric Android keyboards need not expose a minus key; signed intent
        // therefore uses the normal keyboard and bounded culture-aware parsing.
        var entry = NativeTheme.TextField("sr5-reputation-" + id, ""); entry.MaxLength = 12;
        SemanticProperties.SetDescription(entry, T(key)); entry.TextChanged += (_, _) => DraftChanged(); body.Add(entry); return entry;
    }
    private Button AddButton(VerticalStackLayout body, string id, string key, Func<CancellationToken, Task> action)
    {
        var button = NativeTheme.PrimaryButton(T(key)); button.AutomationId = "sr5-reputation-" + id;
        button.Clicked += async (_, _) =>
        {
            var token = _lifetime;
            await _run(async () =>
            {
                if (_actionActive || token.IsCancellationRequested) return;
                _actionActive = true; Refresh();
                try { await action(token); }
                finally { _actionActive = false; if (!_lifetime.IsCancellationRequested) Refresh(); }
            });
        };
        body.Add(button); return button;
    }
    private void DraftChanged()
    {
        if (_rendering || _actionActive || _lifetime.IsCancellationRequested) return;
        _model.UpdateDraft(new(_streetCred.Text ?? "", _notoriety.Text ?? "", _awareness.Text ?? "", _reason.Text ?? ""));
        Refresh();
    }
    private static string Facts(CharacterCareerReputationProjection p)
        => Format("Values", p.Inputs.StreetCred, p.TotalStreetCred, p.Inputs.Notoriety, p.TotalNotoriety,
            p.Inputs.PublicAwareness, p.TotalPublicAwareness, p.Inputs.BurntStreetCred);
    private void BindHistory()
    {
        _historySource = _model.History; _supersededSource = _model.SupersededHistory;
        _historyEntries = _model.History.Concat(_model.SupersededHistory)
            .OrderByDescending(e => e.Command.Binding.WorkspaceRevision).Skip(_historyPage * PageSize).Take(PageSize).ToArray();
        _history.ItemsSource = _historyEntries.Select(e => Format("HistoryRow", e.Command.Binding.WorkspaceRevision,
            T(e.Phase == Sr5CareerReputationPhase.Applied ? "Recorded" : "Superseded"), e.Command.Request.Reason)).ToArray();
        _history.SelectedIndex = -1;
    }
    public void Refresh()
    {
        if (_rendering) return;
        _rendering = true;
        try
        {
            bool busy = _actionActive || _model.IsBusy;
            bool active = !_lifetime.IsCancellationRequested;
            bool edit = active && !busy && _model.CanEdit;
            _working.IsVisible = _working.IsRunning = busy || _model.Status == Sr5CareerReputationPhoneStatus.Loading;
            _status.Text = !_model.HasCurrentSelection ? T("RunnerChanged") : busy ? T("Working") : T(_model.Status.ToString());
            if (_streetCred.Text != _model.Draft.StreetCred) _streetCred.Text = _model.Draft.StreetCred;
            if (_notoriety.Text != _model.Draft.Notoriety) _notoriety.Text = _model.Draft.Notoriety;
            if (_awareness.Text != _model.Draft.PublicAwareness) _awareness.Text = _model.Draft.PublicAwareness;
            if (_reason.Text != _model.Draft.Reason) _reason.Text = _model.Draft.Reason;
            _streetCred.IsEnabled = _notoriety.IsEnabled = _awareness.IsEnabled = _reason.IsEnabled = edit;
            _preview.IsEnabled = edit;
            _burn.IsEnabled = edit && _model.Snapshot?.Reputation.CanBurnStreetCred == true;
            _confirm.IsVisible = _review.IsVisible = _model.CanConfirm;
            _confirm.IsEnabled = active && !busy && _model.CanConfirm;
            _review.Text = _model.Review is { } review ? Format("Comparison",
                T(review.Preview.Quote.Operation == CharacterCareerReputationOperation.BurnStreetCred ? "Burn" : "Preview"),
                Facts(review.Preview.Quote.Before), Facts(review.Preview.Quote.After)) : "";
            _facts.Text = _model.HasCurrentSelection && _model.Snapshot is { } snapshot ? Facts(snapshot.Reputation) : "";
            _recover.IsVisible = _model.HasRetainedIntent && !_model.CanFinish;
            _retry.IsVisible = _close.IsVisible = _model.HasRetainedIntent && _model.Checkpoint?.IsTerminal != true;
            _recover.IsEnabled = active && !busy && _model.CanRecover;
            _retry.IsEnabled = _close.IsEnabled = active && !busy && _model.CanRetry;
            _reload.IsVisible = !_model.HasRetainedIntent; _reload.IsEnabled = active && !busy && _model.HasCurrentSelection;
            _done.IsVisible = _model.CanFinish; _done.IsEnabled = active && !busy && _model.CanFinish;
            _receipt.Text = _model.HasCurrentSelection && _model.Checkpoint is { IsTerminal: true } checkpoint
                ? checkpoint.Receipt is { } receipt ? Format("SavedReceipt", receipt.CommittedWorkspaceRevision, Facts(receipt.Quote.After))
                    : Format("ClosedReceipt", checkpoint.SupersededAtRevision!) : "";
            if (!ReferenceEquals(_historySource, _model.History) || !ReferenceEquals(_supersededSource, _model.SupersededHistory))
            { _historyPage = 0; BindHistory(); }
            _history.IsVisible = _resume.IsVisible = _model.HasCurrentSelection && HistoryCount > 0;
            _history.IsEnabled = edit;
            _resume.IsEnabled = edit && _history.SelectedIndex >= 0;
            _older.IsVisible = _newer.IsVisible = _model.HasCurrentSelection && HistoryCount > PageSize;
            _older.IsEnabled = edit && (_historyPage + 1) * PageSize < HistoryCount;
            _newer.IsEnabled = edit && _historyPage > 0;
        }
        finally { _rendering = false; }
    }
}
