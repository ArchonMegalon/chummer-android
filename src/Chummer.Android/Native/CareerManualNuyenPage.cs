using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerManualNuyenPage : NativePageBase
{
    private readonly CareerManualNuyenEditorState _editor;
    private readonly Entry _amount;
    private readonly Entry _percent;
    private readonly Editor _reason;
    private readonly DatePicker _date;
    private readonly TimePicker _time;
    private readonly Switch _refund;
    private readonly Switch _exchange;
    private readonly Switch _forceCareerVisible;
    private readonly Button _gain;
    private readonly Button _spend;

    public CareerManualNuyenPage(
        RunnerSessionCoordinator coordinator,
        CareerManualNuyenEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Manual Nuyen";
        AutomationId = "career-manual-nuyen-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Manual Nuyen entry"));
        Label summary = NativeTheme.Body(
            $"{editor.Nuyen.AvailableNuyen.ToString(CultureInfo.InvariantCulture)} Nuyen · "
            + $"{editor.Nuyen.AvailableKarma.ToString(CultureInfo.InvariantCulture)} Karma",
            NativeTheme.Muted);
        summary.AutomationId = "career-manual-nuyen-summary";
        body.Add(NativeTheme.Card(summary));

        body.Add(NativeTheme.FieldLabel("Amount"));
        _amount = new Entry
        {
            Text = "1",
            Keyboard = Keyboard.Numeric,
            AutomationId = "career-manual-nuyen-amount",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = CharacterCareerManualNuyenRules.MaximumAmount.ToString(CultureInfo.InvariantCulture).Length
        };
        _amount.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_amount);

        body.Add(NativeTheme.FieldLabel("Percent"));
        _percent = new Entry
        {
            Text = "100",
            Keyboard = Keyboard.Numeric,
            AutomationId = "career-manual-nuyen-percent",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = 7
        };
        _percent.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_percent);

        body.Add(NativeTheme.FieldLabel("Reason"));
        _reason = NativeTheme.TextArea(
            "career-manual-nuyen-reason",
            "Expense",
            "Reason");
        _reason.MaxLength = CharacterCareerManualNuyenRules.MaximumReasonLength;
        body.Add(_reason);

        body.Add(NativeTheme.FieldLabel("Local expense date"));
        _date = new DatePicker
        {
            AutomationId = "career-manual-nuyen-date",
            Date = DateTime.Today,
            MinimumDate = new DateTime(1753, 1, 1),
            MaximumDate = new DateTime(9998, 12, 31),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(_date);
        _time = new TimePicker
        {
            AutomationId = "career-manual-nuyen-time",
            Time = DateTime.Now.TimeOfDay,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(_time);

        _refund = ToggleRow(body, "Refund", "career-manual-nuyen-refund");
        _exchange = ToggleRow(body, "Karma/Nuyen exchange", "career-manual-nuyen-exchange");
        _forceCareerVisible = ToggleRow(
            body,
            "Force Career visibility",
            "career-manual-nuyen-force-career-visible");
        _exchange.Toggled += (_, args) =>
        {
            if (args.Value
                && editor.Nuyen.NuyenPerKarmaWorkingForPeople == decimal.Truncate(
                    editor.Nuyen.NuyenPerKarmaWorkingForPeople)
                && editor.Nuyen.NuyenPerKarmaWorkingForPeople is >= CharacterCareerManualNuyenRules.MinimumAmount
                    and <= CharacterCareerManualNuyenRules.MaximumAmount)
            {
                _amount.Text = decimal.ToInt32(editor.Nuyen.NuyenPerKarmaWorkingForPeople)
                    .ToString(CultureInfo.InvariantCulture);
            }
            if (!args.Value)
            {
                _forceCareerVisible.IsToggled = false;
            }
            RefreshEnabledState();
        };
        body.Add(NativeTheme.Body(
            $"Exchange multiples are validated at {editor.Nuyen.NuyenPerKarmaWorkingForPeople.ToString(CultureInfo.InvariantCulture)} Nuyen. Gained Nuyen converts with the {editor.Nuyen.NuyenPerKarmaWorkingForMan.ToString(CultureInfo.InvariantCulture)} Working for the Man rate; spent Nuyen converts with the Working for the People rate, matching Chummer5.",
            NativeTheme.Muted));

        _gain = NativeTheme.PrimaryButton("Record Nuyen gained");
        _gain.AutomationId = "career-manual-nuyen-gain";
        _gain.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerManualNuyenAction.Gain));
        body.Add(_gain);
        _spend = NativeTheme.SecondaryButton("Record Nuyen spent");
        _spend.AutomationId = "career-manual-nuyen-spend";
        _spend.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerManualNuyenAction.Spend));
        body.Add(_spend);

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

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
        bool inputsValid = TryReadInputs(out int amount, out decimal percent);
        bool gainValid = inputsValid
            && CharacterCareerManualNuyenRules.TryQuote(
                _editor.Nuyen,
                CharacterCareerManualNuyenAction.Gain,
                amount,
                percent,
                _exchange.IsToggled,
                out _);
        bool spendValid = inputsValid
            && CharacterCareerManualNuyenRules.TryQuote(
                _editor.Nuyen,
                CharacterCareerManualNuyenAction.Spend,
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
        _gain.IsEnabled = revisionMatches && gainValid;
        _spend.IsEnabled = revisionMatches && spendValid;
    }

    private bool TryReadInputs(out int amount, out decimal percent)
    {
        bool amountValid = int.TryParse(
            _amount.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out amount)
            && amount is >= CharacterCareerManualNuyenRules.MinimumAmount
                and <= CharacterCareerManualNuyenRules.MaximumAmount;
        bool percentValid = decimal.TryParse(
            _percent.Text,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out percent)
            && percent is >= CharacterCareerManualNuyenRules.MinimumPercent
                and <= CharacterCareerManualNuyenRules.MaximumPercent
            && decimal.Round(percent, CharacterCareerManualNuyenRules.MaximumPercentDecimalPlaces) == percent;
        return amountValid && percentValid;
    }

    private async Task ApplyAsync(CharacterCareerManualNuyenAction action)
    {
        if (!TryReadInputs(out int amount, out decimal percent)
            || !CharacterCareerManualNuyenRules.TryQuote(
                _editor.Nuyen,
                action,
                amount,
                percent,
                _exchange.IsToggled,
                out _))
        {
            await DisplayAlertAsync(
                "Invalid Nuyen entry",
                action == CharacterCareerManualNuyenAction.Spend
                    ? "The amount, percentage, exchange multiple, or available Nuyen does not permit this spend."
                    : "The amount, percentage, or exchange multiple does not permit this gain.",
                "OK");
            return;
        }

        DateTime localDate = (_date.Date ?? DateTime.Today).Date.Add(_time.Time ?? TimeSpan.Zero);
        string reason = _reason.Text ?? string.Empty;
        if (_exchange.IsToggled && string.IsNullOrWhiteSpace(reason))
        {
            reason = action == CharacterCareerManualNuyenAction.Gain
                ? "Working for the Man"
                : "Working for the People";
        }
        await Coordinator.ApplyCareerManualNuyenEditAsync(new CareerManualNuyenEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.Nuyen,
            action,
            amount,
            percent,
            reason,
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
