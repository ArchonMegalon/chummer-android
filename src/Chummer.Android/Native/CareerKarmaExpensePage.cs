using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerKarmaExpensePage : NativePageBase
{
    private readonly CareerKarmaExpenseEditorState _editor;
    private readonly Picker _expenses;
    private readonly Label _balance;
    private readonly Entry _amount;
    private readonly Editor _reason;
    private readonly DatePicker _date;
    private readonly TimePicker _time;
    private readonly Label _lockedMetadata;
    private readonly Button _save;
    private CharacterCareerKarmaExpenseEntry _selected;

    public CareerKarmaExpensePage(
        RunnerSessionCoordinator coordinator,
        CareerKarmaExpenseEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _selected = editor.Expenses.First();
        Title = "Karma expenses";
        AutomationId = "career-karma-expense-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Edit Karma expense"));
        _balance = NativeTheme.Body(
            $"Available Karma: {editor.AvailableKarma.ToString(CultureInfo.InvariantCulture)}. "
            + "Date and reason are always editable; Chummer5 permits amount edits only for manual entries.",
            NativeTheme.Muted);
        _balance.AutomationId = "career-karma-expense-balance";
        body.Add(_balance);

        body.Add(NativeTheme.FieldLabel("Saved expense"));
        _expenses = new Picker
        {
            AutomationId = "career-karma-expense-picker",
            Title = "Saved expense",
            ItemsSource = editor.Expenses.Select(ExpenseLabel).ToArray(),
            SelectedIndex = 0,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _expenses.SelectedIndexChanged += (_, _) => SelectExpense();
        body.Add(_expenses);

        body.Add(NativeTheme.FieldLabel("Amount"));
        _amount = new Entry
        {
            AutomationId = "career-karma-expense-amount",
            Keyboard = Keyboard.Default,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = 32
        };
        _amount.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_amount);

        body.Add(NativeTheme.FieldLabel("Reason"));
        _reason = NativeTheme.TextArea(
            "career-karma-expense-reason",
            "Expense reason",
            "Reason");
        _reason.MaxLength = CharacterCareerKarmaExpenseEditRules.MaximumReasonLength;
        body.Add(_reason);

        body.Add(NativeTheme.FieldLabel("Local expense date"));
        _date = new DatePicker
        {
            AutomationId = "career-karma-expense-date",
            MinimumDate = CharacterCareerKarmaExpenseEditRules.MinimumDate,
            MaximumDate = CharacterCareerKarmaExpenseEditRules.MaximumDate.Date,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(_date);
        _time = new TimePicker
        {
            AutomationId = "career-karma-expense-time",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(_time);

        _lockedMetadata = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _lockedMetadata.AutomationId = "career-karma-expense-locked-metadata";
        body.Add(NativeTheme.Card(_lockedMetadata));

        _save = NativeTheme.PrimaryButton("Save expense");
        _save.AutomationId = "career-karma-expense-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);

        Content = new ScrollView { Content = body };
        LoadSelection();
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string ExpenseLabel(CharacterCareerKarmaExpenseEntry expense)
    {
        string reason = string.IsNullOrWhiteSpace(expense.Reason) ? "(no reason)" : expense.Reason;
        return $"{expense.ExpenseId:D} · {expense.ExpenseDateLocal:yyyy-MM-dd HH:mm:ss} · "
            + $"{expense.Amount.ToString(CultureInfo.InvariantCulture)} Karma · {reason}";
    }

    private void SelectExpense()
    {
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
        string rawUndoType = _selected.KarmaUndoTypeElementPresent
            ? $"present '{_selected.RawKarmaUndoType}'"
            : "absent (Chummer5 ImproveAttribute default)";
        _lockedMetadata.Text =
            $"Expense {_selected.ExpenseId:D} · Karma undo type {rawUndoType} · "
            + $"refund {_selected.Refund} · force Career visibility {_selected.ForceCareerVisible}";
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        string currentReasonNormalizationLanguage = DesktopLocalizationCatalog.NormalizeOrDefault(
            Coordinator.State.Preferences.Language);
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision
            && string.Equals(
                _editor.ReasonNormalizationLanguage,
                currentReasonNormalizationLanguage,
                StringComparison.Ordinal);
        bool valid = TryReadEdit(out _, out DateTime localDate)
            && CharacterCareerKarmaExpenseEditRules.TryEdit(
                _selected,
                ReadAmountOrFallback(),
                _reason.Text,
                localDate,
                out _);
        _expenses.IsEnabled = revisionMatches;
        _amount.IsEnabled = revisionMatches && _selected.AmountEditable;
        _reason.IsEnabled = revisionMatches;
        _date.IsEnabled = revisionMatches;
        _time.IsEnabled = revisionMatches;
        _save.IsEnabled = revisionMatches && valid;
    }

    private decimal ReadAmountOrFallback()
        => decimal.TryParse(
            _amount.Text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out decimal amount)
            ? amount
            : _selected.Amount;

    private bool TryReadEdit(out decimal amount, out DateTime localDate)
    {
        bool amountValid = decimal.TryParse(
            _amount.Text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
        localDate = (_date.Date ?? _selected.ExpenseDateLocal.Date).Date
            .Add(_time.Time ?? _selected.ExpenseDateLocal.TimeOfDay);
        return amountValid;
    }

    private async Task SaveAsync()
    {
        if (!TryReadEdit(out decimal amount, out DateTime localDate)
            || !CharacterCareerKarmaExpenseEditRules.TryEdit(
                _selected,
                amount,
                _reason.Text,
                localDate,
                out _))
        {
            await DisplayAlertAsync(
                "Invalid expense edit",
                "The amount, date, reason, or Karma undo type is outside Chummer5's exact editable bounds.",
                "OK");
            return;
        }

        bool persisted = await Coordinator.ApplyCareerKarmaExpenseEditAsync(new CareerKarmaExpenseEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.AvailableKarma,
            _selected,
            amount,
            _reason.Text ?? string.Empty,
            localDate,
            _editor.ReasonNormalizationLanguage));
        if (persisted)
        {
            await Navigation.PopAsync();
        }
    }
}
