using System.Globalization;
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

    public CareerManualKarmaPage(
        RunnerSessionCoordinator coordinator,
        CareerManualKarmaEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Manual Karma";
        AutomationId = "career-manual-karma-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Manual Karma entry"));
        Label summary = NativeTheme.Body(
            $"{editor.Karma.AvailableKarma.ToString(CultureInfo.InvariantCulture)} Karma · "
            + $"{editor.Karma.AvailableNuyen.ToString(CultureInfo.InvariantCulture)} Nuyen",
            NativeTheme.Muted);
        summary.AutomationId = "career-manual-karma-summary";
        body.Add(NativeTheme.Card(summary));

        body.Add(NativeTheme.FieldLabel("Amount"));
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

        body.Add(NativeTheme.FieldLabel("Reason"));
        _reason = NativeTheme.TextArea(
            "career-manual-karma-reason",
            "Expense",
            "Reason");
        _reason.MaxLength = CharacterCareerManualKarmaRules.MaximumReasonLength;
        body.Add(_reason);

        body.Add(NativeTheme.FieldLabel("Local expense date"));
        _date = new DatePicker
        {
            AutomationId = "career-manual-karma-date",
            Date = DateTime.Today,
            MinimumDate = new DateTime(1753, 1, 1),
            MaximumDate = new DateTime(9998, 12, 31),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(_date);
        _time = new TimePicker
        {
            AutomationId = "career-manual-karma-time",
            Time = DateTime.Now.TimeOfDay,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(_time);

        _refund = ToggleRow(body, "Refund", "career-manual-karma-refund");
        _exchange = ToggleRow(body, "Karma/Nuyen exchange", "career-manual-karma-exchange");
        _forceCareerVisible = ToggleRow(
            body,
            "Force Career visibility",
            "career-manual-karma-force-career-visible");
        _exchange.Toggled += (_, args) =>
        {
            if (!args.Value)
            {
                _forceCareerVisible.IsToggled = false;
            }
            RefreshEnabledState();
        };
        body.Add(NativeTheme.Body(
            $"Working for the People uses {editor.Karma.NuyenPerKarmaWorkingForPeople.ToString(CultureInfo.InvariantCulture)} Nuyen in the expense and {editor.Karma.NuyenPerKarmaWorkingForMan.ToString(CultureInfo.InvariantCulture)} in the saved balance. Working for the Man uses {editor.Karma.NuyenPerKarmaWorkingForMan.ToString(CultureInfo.InvariantCulture)} for both, matching Chummer5.",
            NativeTheme.Muted));

        _gain = NativeTheme.PrimaryButton("Record Karma gained");
        _gain.AutomationId = "career-manual-karma-gain";
        _gain.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerManualKarmaAction.Gain));
        body.Add(_gain);
        _spend = NativeTheme.SecondaryButton("Record Karma spent");
        _spend.AutomationId = "career-manual-karma-spend";
        _spend.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerManualKarmaAction.Spend));
        body.Add(_spend);

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

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        bool amountValid = int.TryParse(
            _amount.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int amount)
            && amount is >= CharacterCareerManualKarmaRules.MinimumAmount
                and <= CharacterCareerManualKarmaRules.MaximumAmount;
        _amount.IsEnabled = revisionMatches;
        _reason.IsEnabled = revisionMatches;
        _date.IsEnabled = revisionMatches;
        _time.IsEnabled = revisionMatches;
        _refund.IsEnabled = revisionMatches;
        _exchange.IsEnabled = revisionMatches;
        _forceCareerVisible.IsEnabled = revisionMatches && _exchange.IsToggled;
        _gain.IsEnabled = revisionMatches && amountValid;
        _spend.IsEnabled = revisionMatches && amountValid && amount <= _editor.Karma.AvailableKarma;
    }

    private async Task ApplyAsync(CharacterCareerManualKarmaAction action)
    {
        if (!int.TryParse(
                _amount.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int amount)
            || amount is < CharacterCareerManualKarmaRules.MinimumAmount
                or > CharacterCareerManualKarmaRules.MaximumAmount)
        {
            await DisplayAlertAsync(
                "Invalid amount",
                $"Enter a whole Karma amount from {CharacterCareerManualKarmaRules.MinimumAmount} to {CharacterCareerManualKarmaRules.MaximumAmount}.",
                "OK");
            return;
        }
        if (action == CharacterCareerManualKarmaAction.Spend
            && amount > _editor.Karma.AvailableKarma)
        {
            await DisplayAlertAsync(
                "Not enough Karma",
                $"Only {_editor.Karma.AvailableKarma.ToString(CultureInfo.InvariantCulture)} Karma is available.",
                "OK");
            return;
        }

        DateTime localDate = (_date.Date ?? DateTime.Today).Date.Add(_time.Time ?? TimeSpan.Zero);
        await Coordinator.ApplyCareerManualKarmaEditAsync(new CareerManualKarmaEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.Karma,
            action,
            amount,
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
