using System.Globalization;
using Chummer.Application.Owners;
using static Chummer.Android.Native.Sr5CareerFlowStrings;
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
    private readonly Button _options;
    private readonly VerticalStackLayout _optionsBody;
    private readonly ActivityIndicator _progress;
    private readonly Label _saveStatus;
    private readonly OwnerContextStamp? _ownerContext;
    private bool _saving;
    private bool _attempted;
    private bool _recorded;

    public CareerManualNuyenPage(
        RunnerSessionCoordinator coordinator,
        CareerManualNuyenEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _ownerContext = coordinator.State.DisplayOwnerContext;
        Title = Text("Record Nuyen");
        AutomationId = "career-manual-nuyen-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Title(Text("Record Nuyen")));
        Label summary = NativeTheme.Body(
            Format("{0} Karma · {1} Nuyen", editor.Nuyen.AvailableKarma, editor.Nuyen.AvailableNuyen),
            NativeTheme.Muted);
        summary.AutomationId = "career-manual-nuyen-summary";
        body.Add(NativeTheme.Card(summary));

        body.Add(NativeTheme.FieldLabel(Text("Amount")));
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

        body.Add(NativeTheme.FieldLabel(Text("Reason")));
        _reason = NativeTheme.TextArea(
            "career-manual-nuyen-reason",
            Text("Expense"),
            Text("Reason"));
        _reason.MaxLength = CharacterCareerManualNuyenRules.MaximumReasonLength;
        body.Add(_reason);

        _optionsBody = new VerticalStackLayout { Spacing = 10, IsVisible = false,
            AutomationId = "career-manual-nuyen-options" };
        _optionsBody.Add(NativeTheme.FieldLabel(Text("Percent")));
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
        _optionsBody.Add(_percent);

        _optionsBody.Add(NativeTheme.FieldLabel(Text("Date")));
        _date = new DatePicker
        {
            AutomationId = "career-manual-nuyen-date",
            Date = DateTime.Today,
            MinimumDate = new DateTime(1753, 1, 1),
            MaximumDate = new DateTime(9998, 12, 31),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _optionsBody.Add(_date);
        _time = new TimePicker
        {
            AutomationId = "career-manual-nuyen-time",
            Time = DateTime.Now.TimeOfDay,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _optionsBody.Add(_time);

        _refund = ToggleRow(_optionsBody, Text("Refund"), "career-manual-nuyen-refund");
        _exchange = ToggleRow(_optionsBody, Text("Karma/Nuyen exchange"), "career-manual-nuyen-exchange");
        _forceCareerVisible = ToggleRow(
            _optionsBody,
            Text("Force Career visibility"),
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
        _optionsBody.Add(NativeTheme.Body(
            Format("Exchange uses multiples of {0} Nuyen. Working for the Man uses {1} Nuyen per Karma.", editor.Nuyen.NuyenPerKarmaWorkingForPeople, editor.Nuyen.NuyenPerKarmaWorkingForMan),
            NativeTheme.Muted));

        _gain = NativeTheme.PrimaryButton(Text("Record Nuyen gained"));
        _gain.AutomationId = "career-manual-nuyen-gain";
        _gain.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerManualNuyenAction.Gain));
        body.Add(_gain);
        _spend = NativeTheme.SecondaryButton(Text("Record Nuyen spent"));
        _spend.AutomationId = "career-manual-nuyen-spend";
        _spend.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerManualNuyenAction.Spend));
        body.Add(_spend);

        _progress = new ActivityIndicator
        {
            AutomationId = "career-manual-nuyen-save-progress",
            IsVisible = false, IsRunning = false, Color = NativeTheme.Signal
        };
        _saveStatus = NativeTheme.Body(Text("Saving entry…"), NativeTheme.Muted);
        _saveStatus.AutomationId = "career-manual-nuyen-save-status";
        _saveStatus.IsVisible = false;
        body.Add(_progress);
        body.Add(_saveStatus);
        _options = NativeTheme.ReadingButton(Text("More options"));
        _options.AutomationId = "career-manual-nuyen-options-toggle";
        _options.Clicked += (_, _) =>
        {
            if (_attempted || !MatchesRunner()) return;
            _optionsBody.IsVisible = !_optionsBody.IsVisible;
            _options.Text = Text(_optionsBody.IsVisible ? "Fewer options" : "More options");
        };
        body.Add(_options);
        body.Add(_optionsBody);

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

    private bool MatchesRunner()
        => Coordinator.IsCareerNavigationOwnerCurrent(_ownerContext)
           && Coordinator.State.WorkspaceId == _editor.WorkspaceId
           && Coordinator.State.ContentRevision == _editor.ContentRevision;

    private void RefreshEnabledState()
    {
        bool revisionMatches = !_attempted && MatchesRunner();
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

        _options.IsEnabled = revisionMatches;
        _progress.IsVisible = _saving;
        _progress.IsRunning = _saving;
        _saveStatus.IsVisible = _attempted || !MatchesRunner();
        _saveStatus.Text = Text(_saving ? "Saving entry…"
            : _recorded ? "Entry saved. Return to Career to continue."
            : "Reopen the runner and check its balance before trying again.");
        _amount.IsEnabled = revisionMatches;
        _percent.IsEnabled = revisionMatches && !_exchange.IsToggled;
        _reason.IsEnabled = revisionMatches;
        _date.IsEnabled = revisionMatches;
        _time.IsEnabled = revisionMatches;
        _refund.IsEnabled = revisionMatches;
        _exchange.IsEnabled = revisionMatches;
        _forceCareerVisible.IsEnabled = revisionMatches && _exchange.IsToggled;
        _gain.Text = Text(_saving ? "Saving entry…" : "Record Nuyen gained");
        _spend.Text = Text(_saving ? "Saving entry…" : "Record Nuyen spent");
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
        if (_attempted || !MatchesRunner()) return;
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
                Text("Invalid Nuyen entry"),
                Text(action == CharacterCareerManualNuyenAction.Spend
                    ? "The amount, percentage, exchange multiple, or available Nuyen does not permit this spend."
                    : "The amount, percentage, or exchange multiple does not permit this gain."),
                Text("OK"));
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
        var request = new CareerManualNuyenEditRequest(
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
            _forceCareerVisible.IsToggled);
        long appearance = CaptureAppearanceGeneration();
        _attempted = true;
        _saving = true;
        RefreshEnabledState();
        try
        {
            // Keep the original request immutable while the action is pending.
            // The existing coordinator/presenter retains owner and save admission.
            await Coordinator.ApplyCareerManualNuyenEditAsync(request);
            _recorded = Coordinator.State.Error is null && !Coordinator.State.IsDirty
                && Coordinator.State.WorkspaceId == _editor.WorkspaceId
                && Coordinator.State.ContentRevision > _editor.ContentRevision;
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
