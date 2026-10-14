using System.Globalization;
using Chummer.Application.Owners;
using static Chummer.Android.Native.Sr5CareerFlowStrings;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerManualKarmaPage : NativePageBase
{
    private readonly CareerManualKarmaEditorState _editor;
    private readonly Entry _amount;
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

    public CareerManualKarmaPage(
        RunnerSessionCoordinator coordinator,
        CareerManualKarmaEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _ownerContext = coordinator.State.DisplayOwnerContext;
        Title = Text("Record Karma");
        AutomationId = "career-manual-karma-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Title(Text("Record Karma")));
        Label summary = NativeTheme.Body(
            Format("{0} Karma · {1} Nuyen", editor.Karma.AvailableKarma, editor.Karma.AvailableNuyen),
            NativeTheme.Muted);
        summary.AutomationId = "career-manual-karma-summary";
        body.Add(NativeTheme.Card(summary));

        body.Add(NativeTheme.FieldLabel(Text("Amount")));
        _amount = new Entry
        {
            Text = "1",
            Keyboard = Keyboard.Numeric,
            AutomationId = "career-manual-karma-amount",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = CharacterCareerManualKarmaRules.MaximumAmount.ToString(CultureInfo.InvariantCulture).Length
        };
        _amount.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_amount);

        body.Add(NativeTheme.FieldLabel(Text("Reason")));
        _reason = NativeTheme.TextArea(
            "career-manual-karma-reason",
            Text("Expense"),
            Text("Reason"));
        _reason.MaxLength = CharacterCareerManualKarmaRules.MaximumReasonLength;
        body.Add(_reason);

        _optionsBody = new VerticalStackLayout { Spacing = 10, IsVisible = false,
            AutomationId = "career-manual-karma-options" };
        _optionsBody.Add(NativeTheme.FieldLabel(Text("Date")));
        _date = new DatePicker
        {
            AutomationId = "career-manual-karma-date",
            Date = DateTime.Today,
            MinimumDate = new DateTime(1753, 1, 1),
            MaximumDate = new DateTime(9998, 12, 31),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _optionsBody.Add(_date);
        _time = new TimePicker
        {
            AutomationId = "career-manual-karma-time",
            Time = DateTime.Now.TimeOfDay,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _optionsBody.Add(_time);

        _refund = ToggleRow(_optionsBody, Text("Refund"), "career-manual-karma-refund");
        _exchange = ToggleRow(_optionsBody, Text("Karma/Nuyen exchange"), "career-manual-karma-exchange");
        _forceCareerVisible = ToggleRow(
            _optionsBody,
            Text("Force Career visibility"),
            "career-manual-karma-force-career-visible");
        _exchange.Toggled += (_, args) =>
        {
            if (!args.Value)
            {
                _forceCareerVisible.IsToggled = false;
            }
            RefreshEnabledState();
        };
        _optionsBody.Add(NativeTheme.Body(
            Format("Working for the People records {0} Nuyen in the expense and changes the balance by {1}. Working for the Man uses {1} for both.", editor.Karma.NuyenPerKarmaWorkingForPeople, editor.Karma.NuyenPerKarmaWorkingForMan),
            NativeTheme.Muted));

        _gain = NativeTheme.PrimaryButton(Text("Record Karma gained"));
        _gain.AutomationId = "career-manual-karma-gain";
        _gain.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerManualKarmaAction.Gain));
        body.Add(_gain);
        _spend = NativeTheme.SecondaryButton(Text("Record Karma spent"));
        _spend.AutomationId = "career-manual-karma-spend";
        _spend.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerManualKarmaAction.Spend));
        body.Add(_spend);

        _progress = new ActivityIndicator
        {
            AutomationId = "career-manual-karma-save-progress",
            IsVisible = false, IsRunning = false, Color = NativeTheme.Signal
        };
        _saveStatus = NativeTheme.Body(Text("Saving entry…"), NativeTheme.Muted);
        _saveStatus.AutomationId = "career-manual-karma-save-status";
        _saveStatus.IsVisible = false;
        body.Add(_progress);
        body.Add(_saveStatus);
        _options = NativeTheme.ReadingButton(Text("More options"));
        _options.AutomationId = "career-manual-karma-options-toggle";
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

    private Switch ToggleRow(VerticalStackLayout body, string label, string automationId)
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
        bool amountValid = int.TryParse(
            _amount.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int amount)
            && amount is >= CharacterCareerManualKarmaRules.MinimumAmount
                and <= CharacterCareerManualKarmaRules.MaximumAmount;
        _options.IsEnabled = revisionMatches;
        _progress.IsVisible = _saving;
        _progress.IsRunning = _saving;
        _saveStatus.IsVisible = _attempted || !MatchesRunner();
        _saveStatus.Text = Text(_saving ? "Saving entry…"
            : _recorded ? "Entry saved. Return to Career to continue."
            : "Reopen the runner and check its balance before trying again.");
        _amount.IsEnabled = revisionMatches;
        _reason.IsEnabled = revisionMatches;
        _date.IsEnabled = revisionMatches;
        _time.IsEnabled = revisionMatches;
        _refund.IsEnabled = revisionMatches;
        _exchange.IsEnabled = revisionMatches;
        _forceCareerVisible.IsEnabled = revisionMatches && _exchange.IsToggled;
        _gain.Text = Text(_saving ? "Saving entry…" : "Record Karma gained");
        _spend.Text = Text(_saving ? "Saving entry…" : "Record Karma spent");
        _gain.IsEnabled = revisionMatches && amountValid;
        _spend.IsEnabled = revisionMatches && amountValid && amount <= _editor.Karma.AvailableKarma;
    }

    private async Task ApplyAsync(CharacterCareerManualKarmaAction action)
    {
        if (_attempted || !MatchesRunner()) return;
        if (!int.TryParse(
                _amount.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int amount)
            || amount is < CharacterCareerManualKarmaRules.MinimumAmount
                or > CharacterCareerManualKarmaRules.MaximumAmount)
        {
            await DisplayAlertAsync(
                Text("Invalid amount"),
                Format("Enter a whole Karma amount from {0} to {1}.", CharacterCareerManualKarmaRules.MinimumAmount, CharacterCareerManualKarmaRules.MaximumAmount),
                Text("OK"));
            return;
        }
        if (action == CharacterCareerManualKarmaAction.Spend
            && amount > _editor.Karma.AvailableKarma)
        {
            await DisplayAlertAsync(
                Text("Not enough Karma"),
                Format("Only {0} Karma is available.", _editor.Karma.AvailableKarma),
                Text("OK"));
            return;
        }

        DateTime localDate = (_date.Date ?? DateTime.Today).Date.Add(_time.Time ?? TimeSpan.Zero);
        var request = new CareerManualKarmaEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.Karma,
            action,
            amount,
            _reason.Text ?? string.Empty,
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
            await Coordinator.ApplyCareerManualKarmaEditAsync(request);
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
