using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerCreateExpenseMenuPage : NativePageBase
{
    public CareerCreateExpenseMenuPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Create expense";
        AutomationId = "career-create-expense-menu-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Create expense"));
        body.Add(NativeTheme.Body(
            "Choose the exact CharacterCareer operation before editing so exchange labels and signs remain source-exact.",
            NativeTheme.Muted));
        AddOperation(body, "Karma gained", "career-create-expense-karma-gained",
            CharacterCareerCreateExpenseOperation.KarmaGained);
        AddOperation(body, "Karma spent", "career-create-expense-karma-spent",
            CharacterCareerCreateExpenseOperation.KarmaSpent);
        AddOperation(body, "Nuyen gained", "career-create-expense-nuyen-gained",
            CharacterCareerCreateExpenseOperation.NuyenGained);
        AddOperation(body, "Nuyen spent", "career-create-expense-nuyen-spent",
            CharacterCareerCreateExpenseOperation.NuyenSpent);
        Content = new ScrollView { Content = body };
    }

    private void AddOperation(
        VerticalStackLayout body,
        string label,
        string automationId,
        CharacterCareerCreateExpenseOperation operation)
    {
        body.Add(NativeTheme.NavigationRow(
            label,
            "Open the revision-bound CreateExpense editor",
            async () =>
            {
                CareerCreateExpenseEditorState? editor =
                    await Coordinator.PrepareCareerCreateExpenseEditAsync(operation);
                if (editor is not null)
                {
                    await Navigation.PushAsync(new CareerCreateExpensePage(Coordinator, editor));
                }
            },
            automationId: automationId));
    }
}

public sealed class CareerCreateExpensePage : NativePageBase
{
    private readonly CareerCreateExpenseEditorState _editor;
    private readonly Entry _amount;
    private readonly Entry _percent;
    private readonly Editor _reason;
    private readonly DatePicker _date;
    private readonly TimePicker _time;
    private readonly Switch _refund;
    private readonly Switch _exchange;
    private readonly Switch _forceCareerVisible;
    private readonly Button _ok;

    public CareerCreateExpensePage(
        RunnerSessionCoordinator coordinator,
        CareerCreateExpenseEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        bool nuyen = CharacterCareerCreateExpenseRules.IsNuyen(editor.Operation);
        string operationLabel = OperationLabel(editor.Operation);
        Title = operationLabel;
        AutomationId = "career-create-expense-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner · CreateExpense"));
        body.Add(NativeTheme.Title(operationLabel));
        Label summary = NativeTheme.Body(
            $"{editor.Expense.AvailableKarma.ToString(CultureInfo.InvariantCulture)} Karma · "
            + $"{editor.Expense.AvailableNuyen.ToString(CultureInfo.InvariantCulture)} Nuyen",
            NativeTheme.Muted);
        summary.AutomationId = "career-create-expense-summary";
        body.Add(NativeTheme.Card(summary));

        body.Add(NativeTheme.FieldLabel(nuyen ? "Nuyen amount" : "Karma amount"));
        _amount = new Entry
        {
            Text = CharacterCareerCreateExpenseRules.MinimumAmount.ToString(CultureInfo.InvariantCulture),
            Keyboard = Keyboard.Numeric,
            AutomationId = "career-create-expense-amount",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = CharacterCareerCreateExpenseRules.MaximumAmount.ToString(CultureInfo.InvariantCulture).Length
        };
        _amount.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_amount);

        _percent = new Entry
        {
            Text = "100",
            Keyboard = Keyboard.Numeric,
            AutomationId = "career-create-expense-percent",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = 7
        };
        _percent.TextChanged += (_, _) => RefreshEnabledState();
        if (nuyen)
        {
            body.Add(NativeTheme.FieldLabel("Percent"));
            body.Add(_percent);
        }

        body.Add(NativeTheme.FieldLabel("Description"));
        _reason = NativeTheme.TextArea(
            "career-create-expense-description",
            CharacterCareerCreateExpenseRules.DefaultReason,
            "Description");
        _reason.Text = CharacterCareerCreateExpenseRules.DefaultReason;
        _reason.MaxLength = CharacterCareerCreateExpenseRules.MaximumReasonLength;
        body.Add(_reason);

        DateTime opened = DateTime.Now;
        body.Add(NativeTheme.FieldLabel("Local expense date"));
        _date = new DatePicker
        {
            AutomationId = "career-create-expense-date",
            Date = opened.Date,
            MinimumDate = CharacterCareerCreateExpenseRules.MinimumDate,
            MaximumDate = CharacterCareerCreateExpenseRules.MaximumDate.Date,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(_date);
        _time = new TimePicker
        {
            AutomationId = "career-create-expense-time",
            Time = opened.TimeOfDay,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(_time);

        _refund = ToggleRow(body, "Refund", "career-create-expense-refund");
        _exchange = ToggleRow(body, "Karma/Nuyen exchange", "career-create-expense-exchange");
        _forceCareerVisible = ToggleRow(
            body,
            "Show in Career Karma/Nuyen",
            "career-create-expense-force-career-visible");
        _exchange.Toggled += (_, args) =>
        {
            if (args.Value)
            {
                _reason.Text = CharacterCareerCreateExpenseRules.ExchangeReason(_editor.Operation);
                if (nuyen
                    && editor.Expense.NuyenPerKarmaWorkingForPeople == decimal.Truncate(
                        editor.Expense.NuyenPerKarmaWorkingForPeople)
                    && editor.Expense.NuyenPerKarmaWorkingForPeople
                        is >= CharacterCareerCreateExpenseRules.MinimumAmount
                        and <= CharacterCareerCreateExpenseRules.MaximumAmount)
                {
                    _amount.Text = decimal.ToInt32(editor.Expense.NuyenPerKarmaWorkingForPeople)
                        .ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                _forceCareerVisible.IsToggled = false;
            }
            RefreshEnabledState();
        };

        _ok = NativeTheme.PrimaryButton("OK");
        _ok.AutomationId = "career-create-expense-ok";
        _ok.Clicked += async (_, _) => await RunAsync(ApplyAsync);
        body.Add(_ok);
        body.Add(NativeTheme.Body(
            "Back cancels and discards this draft. An integral Nuyen exchange deliberately remains open without a save, matching canonical Chummer5.",
            NativeTheme.Muted));

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string OperationLabel(CharacterCareerCreateExpenseOperation operation)
        => operation switch
        {
            CharacterCareerCreateExpenseOperation.KarmaGained => "Karma gained",
            CharacterCareerCreateExpenseOperation.KarmaSpent => "Karma spent",
            CharacterCareerCreateExpenseOperation.NuyenGained => "Nuyen gained",
            CharacterCareerCreateExpenseOperation.NuyenSpent => "Nuyen spent",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static Switch ToggleRow(VerticalStackLayout body, string label, string automationId)
    {
        Switch toggle = new()
        {
            AutomationId = automationId,
            OnColor = NativeTheme.Signal
        };
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel(label), 0, 0);
        row.Add(toggle, 1, 0);
        body.Add(NativeTheme.Card(row));
        return toggle;
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        bool valid = TryReadInputs(out int amount, out decimal percent)
            && CharacterCareerCreateExpenseRules.TryEvaluateDialog(
                _editor.Expense,
                _editor.Operation,
                amount,
                percent,
                _exchange.IsToggled,
                out _);
        _amount.IsEnabled = revisionMatches;
        _percent.IsEnabled = revisionMatches && !_exchange.IsToggled;
        _reason.IsEnabled = revisionMatches;
        _date.IsEnabled = revisionMatches;
        _time.IsEnabled = revisionMatches;
        _refund.IsEnabled = revisionMatches;
        _exchange.IsEnabled = revisionMatches;
        _forceCareerVisible.IsEnabled = revisionMatches && _exchange.IsToggled;
        _ok.IsEnabled = revisionMatches && valid;
    }

    private bool TryReadInputs(out int amount, out decimal percent)
    {
        bool amountValid = int.TryParse(
            _amount.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out amount)
            && amount is >= CharacterCareerCreateExpenseRules.MinimumAmount
                and <= CharacterCareerCreateExpenseRules.MaximumAmount;
        if (!CharacterCareerCreateExpenseRules.IsNuyen(_editor.Operation))
        {
            percent = 100m;
            return amountValid;
        }
        bool percentValid = decimal.TryParse(
            _percent.Text,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out percent)
            && percent is >= CharacterCareerCreateExpenseRules.MinimumPercent
                and <= CharacterCareerCreateExpenseRules.MaximumPercent
            && decimal.Round(percent, CharacterCareerCreateExpenseRules.MaximumPercentDecimalPlaces) == percent;
        return amountValid && percentValid;
    }

    private async Task ApplyAsync()
    {
        if (!TryReadInputs(out int amount, out decimal percent)
            || !CharacterCareerCreateExpenseRules.TryEvaluateDialog(
                _editor.Expense,
                _editor.Operation,
                amount,
                percent,
                _exchange.IsToggled,
                out CharacterCareerCreateExpenseDialogOutcome outcome))
        {
            await DisplayAlertAsync("Invalid expense", "The expense values are outside Chummer5's rules.", "OK");
            return;
        }
        if (outcome == CharacterCareerCreateExpenseDialogOutcome.NuyenExchangeValidationRejected)
        {
            await DisplayAlertAsync(
                "Invalid Karma/Nuyen exchange",
                $"Enter an exact multiple of {_editor.Expense.NuyenPerKarmaWorkingForPeople.ToString(CultureInfo.InvariantCulture)} Nuyen.",
                "OK");
            return;
        }
        if (outcome == CharacterCareerCreateExpenseDialogOutcome.NuyenExchangeCanonicalNoOp)
        {
            // fe4355d CreateExpense.cmdOK_Click has no success branch here: do not save,
            // do not close, and leave the editor draft untouched.
            return;
        }
        if (outcome == CharacterCareerCreateExpenseDialogOutcome.CallerBalanceValidationRejected)
        {
            string resource = CharacterCareerCreateExpenseRules.IsNuyen(_editor.Operation)
                ? "Nuyen"
                : "Karma";
            await DisplayAlertAsync(
                $"Not enough {resource}",
                $"The runner does not have enough {resource} for this expense.",
                "OK");
            await Navigation.PopAsync();
            return;
        }

        DateTime localDate = (_date.Date ?? DateTime.Today).Date.Add(_time.Time ?? TimeSpan.Zero);
        await Coordinator.ApplyCareerCreateExpenseEditAsync(new CareerCreateExpenseEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.Expense,
            _editor.Operation,
            amount,
            percent,
            _reason.Text ?? string.Empty,
            localDate,
            _refund.IsToggled,
            _exchange.IsToggled,
            _forceCareerVisible.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
