using System.Globalization;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

public sealed class CareerKarmaExpensePage : NativePageBase
{
    private readonly CareerKarmaExpenseEditorState _editor;
    private readonly OwnerContextStamp? _ownerContext;
    private readonly Picker _expenses;
    private readonly Entry _amount;
    private readonly Editor _reason;
    private readonly DatePicker _date;
    private readonly TimePicker _time;
    private readonly Label _amountHint;
    private readonly Label _saveStatus;
    private readonly Button _save;
    private readonly Button _options;
    private readonly VerticalStackLayout _optionsBody;
    private readonly ActivityIndicator _progress;
    private CharacterCareerKarmaExpenseEntry _selected;
    private bool _attempted;
    private bool _saving;
    private bool _recorded;

    public CareerKarmaExpensePage(
        RunnerSessionCoordinator coordinator,
        CareerKarmaExpenseEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _ownerContext = coordinator.State.DisplayOwnerContext;
        _selected = editor.Expenses.First();
        Title = Text("Edit Karma entry");
        AutomationId = "career-karma-expense-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Title(Text("Edit Karma entry")));
        Label balance = NativeTheme.Body(Format("{0} Karma available", editor.AvailableKarma), NativeTheme.Muted);
        balance.AutomationId = "career-karma-expense-balance";
        body.Add(balance);

        body.Add(NativeTheme.FieldLabel(Text("Saved entry")));
        _expenses = new Picker
        {
            AutomationId = "career-karma-expense-picker",
            Title = Text("Saved entry"),
            ItemsSource = editor.Expenses.Select(ExpenseLabel).ToArray(),
            SelectedIndex = 0,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _expenses.SelectedIndexChanged += (_, _) => SelectExpense();
        body.Add(_expenses);

        body.Add(NativeTheme.FieldLabel(Text("Amount")));
        _amount = new Entry
        {
            AutomationId = "career-karma-expense-amount",
            Keyboard = Keyboard.Default,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = 32
        };
        body.Add(_amount);
        _amountHint = NativeTheme.Body(Text("Only manually recorded amounts can be changed."), NativeTheme.Muted);
        _amountHint.AutomationId = "career-karma-expense-amount-hint";
        body.Add(_amountHint);

        body.Add(NativeTheme.FieldLabel(Text("Reason")));
        _reason = NativeTheme.TextArea(
            "career-karma-expense-reason", Text("Expense"), Text("Reason"));
        _reason.MaxLength = CharacterCareerKarmaExpenseEditRules.MaximumReasonLength;
        body.Add(_reason);

        _optionsBody = new VerticalStackLayout
        {
            Spacing = 10, IsVisible = false, AutomationId = "career-karma-expense-options"
        };
        _optionsBody.Add(NativeTheme.FieldLabel(Text("Date")));
        _date = new DatePicker
        {
            AutomationId = "career-karma-expense-date",
            MinimumDate = CharacterCareerKarmaExpenseEditRules.MinimumDate,
            MaximumDate = CharacterCareerKarmaExpenseEditRules.MaximumDate.Date,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _optionsBody.Add(_date);
        _time = new TimePicker
        {
            AutomationId = "career-karma-expense-time",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _optionsBody.Add(_time);

        _save = NativeTheme.PrimaryButton(Text("Save entry"));
        _save.AutomationId = "career-karma-expense-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        _progress = new ActivityIndicator
        {
            AutomationId = "career-karma-expense-save-progress",
            IsVisible = false, IsRunning = false, Color = NativeTheme.Signal
        };
        _saveStatus = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _saveStatus.AutomationId = "career-karma-expense-save-status";
        body.Add(_progress);
        body.Add(_saveStatus);
        _options = NativeTheme.ReadingButton(Text("More options"));
        _options.AutomationId = "career-karma-expense-options-toggle";
        _options.Clicked += (_, _) =>
        {
            if (_attempted || !MatchesRunner()) return;
            _optionsBody.IsVisible = !_optionsBody.IsVisible;
            _options.Text = Text(_optionsBody.IsVisible ? "Fewer options" : "More options");
        };
        body.Add(_options);
        body.Add(_optionsBody);
        Content = new ScrollView { Content = body };
        LoadSelection();
        _amount.TextChanged += (_, _) => RefreshEnabledState();
        _reason.TextChanged += (_, _) => RefreshEnabledState();
        _date.DateSelected += (_, _) => RefreshEnabledState();
        _time.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TimePicker.Time)) RefreshEnabledState();
        };
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string ExpenseLabel(CharacterCareerKarmaExpenseEntry expense)
    {
        string reason = string.IsNullOrWhiteSpace(expense.Reason) ? Text("No reason") : expense.Reason;
        return $"{expense.ExpenseDateLocal:yyyy-MM-dd HH:mm} · "
            + $"{expense.Amount.ToString(CultureInfo.CurrentCulture)} Karma · {reason}";
    }

    private void SelectExpense()
    {
        if (_attempted || !MatchesRunner()) return;
        if (_expenses.SelectedIndex >= 0 && _expenses.SelectedIndex < _editor.Expenses.Count)
        {
            _selected = _editor.Expenses[_expenses.SelectedIndex];
            LoadSelection();
        }
    }

    private void LoadSelection()
    {
        _amount.Text = _selected.Amount.ToString(CultureInfo.InvariantCulture);
        _reason.Text = _selected.Reason;
        _date.Date = _selected.ExpenseDateLocal.Date;
        _time.Time = _selected.ExpenseDateLocal.TimeOfDay;
        RefreshEnabledState();
    }

    private bool MatchesRunner()
        => Coordinator.IsCareerNavigationOwnerCurrent(_ownerContext)
           && Coordinator.State.WorkspaceId == _editor.WorkspaceId
           && Coordinator.State.ContentRevision == _editor.ContentRevision
           && string.Equals(_editor.ReasonNormalizationLanguage,
               DesktopLocalizationCatalog.NormalizeOrDefault(Coordinator.State.Preferences.Language),
               StringComparison.Ordinal);

    private void RefreshEnabledState()
    {
        bool current = !_attempted && MatchesRunner();
        bool valid = TryReadEdit(out decimal amount, out DateTime localDate)
            && CharacterCareerKarmaExpenseEditRules.TryEdit(_selected, amount, _reason.Text, localDate, out _);
        _expenses.IsEnabled = current;
        _amount.IsEnabled = current && _selected.AmountEditable;
        _amountHint.IsVisible = !_selected.AmountEditable;
        _reason.IsEnabled = current;
        _date.IsEnabled = current;
        _time.IsEnabled = current;
        _options.IsEnabled = current;
        _save.IsEnabled = current && valid;
        _save.Text = Text(_saving ? "Saving entry…" : "Save entry");
        _progress.IsVisible = _saving;
        _progress.IsRunning = _saving;
        _saveStatus.IsVisible = _attempted || !MatchesRunner() || !valid;
        _saveStatus.Text = Text(_saving ? "Saving entry…"
            : _recorded ? "Entry saved. Return to Career to continue."
            : _attempted || !MatchesRunner() ? "Reopen the runner and check its balance before trying again."
            : "Check the amount, reason and date before saving.");
    }

    private bool TryReadEdit(out decimal amount, out DateTime localDate)
    {
        bool amountValid = decimal.TryParse(_amount.Text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out amount);
        localDate = (_date.Date ?? _selected.ExpenseDateLocal.Date).Date
            .Add(_time.Time ?? _selected.ExpenseDateLocal.TimeOfDay);
        return amountValid;
    }

    private async Task SaveAsync()
    {
        if (_attempted || !MatchesRunner()) return;
        if (!TryReadEdit(out decimal amount, out DateTime localDate)
            || !CharacterCareerKarmaExpenseEditRules.TryEdit(
                _selected, amount, _reason.Text, localDate, out _))
        {
            RefreshEnabledState();
            return;
        }
        var request = new CareerKarmaExpenseEditRequest(
            _editor.WorkspaceId, _editor.ContentRevision, _editor.AvailableKarma,
            _selected, amount, _reason.Text ?? string.Empty, localDate, _editor.ReasonNormalizationLanguage);
        long appearance = CaptureAppearanceGeneration();
        _attempted = true;
        _saving = true;
        RefreshEnabledState();
        try
        {
            bool persisted = await Coordinator.ApplyCareerKarmaExpenseEditAsync(request);
            _recorded = persisted && Coordinator.State.Error is null && !Coordinator.State.IsDirty
                && Coordinator.State.WorkspaceId == _editor.WorkspaceId
                && _editor.ContentRevision < long.MaxValue
                && Coordinator.State.ContentRevision == _editor.ContentRevision + 1
                && Coordinator.State.SavedRevision == Coordinator.State.ContentRevision;
            if (_recorded && IsCurrentAppearanceGeneration(appearance)
                && Coordinator.IsCareerNavigationOwnerCurrent(_ownerContext)
                && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this))
                await Navigation.PopAsync();
        }
        finally
        {
            _saving = false;
            RefreshEnabledState();
        }
    }
}
