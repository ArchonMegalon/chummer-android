using Chummer.Contracts.Api;

namespace Chummer.Android.Native;

/// <summary>
/// Phone editor for Chummer5's print and PDF-note application settings. Every switch is a local
/// draft until the explicit Save action submits one typed, revision-checked transaction.
/// </summary>
public sealed class ApplicationPrintSettingsPage : NativePageBase
{
    private readonly ApplicationDeleteConfirmationState _baseline;
    private readonly Switch _printToFileFirst;
    private readonly Switch _printSkillsWithZeroRating;
    private readonly Switch _printExpenses;
    private readonly Switch _printFreeExpenses;
    private readonly Switch _printNotes;
    private readonly Switch _insertPdfNotesIfAvailable;
    private readonly Label _revision;

    public ApplicationPrintSettingsPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = PhoneStrings.Get("ApplicationPrintSettingsTitle", "Print & PDF notes");
        AutomationId = "application-print-settings-page";
        _baseline = coordinator.ApplicationSettings;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 20, 20, 36),
            Spacing = 18
        };
        body.Add(NativeTheme.Eyebrow(
            PhoneStrings.Get("ApplicationPrintSettingsEyebrow", "Application output")));
        body.Add(NativeTheme.Title(
            PhoneStrings.Get("ApplicationPrintSettingsTitle", "Print & PDF notes")));
        body.Add(NativeTheme.Body(
            PhoneStrings.Get(
                "ApplicationPrintSettingsSummary",
                "These are device-wide Chummer settings, not runner data. Back discards the draft; Save writes all six options atomically."),
            NativeTheme.Muted));

        _printToFileFirst = CreateSwitch(
            "settings-print-to-file-first",
            _baseline.PrintToFileFirst);
        body.Add(CreateSwitchCard(
            PhoneStrings.Get("ApplicationPrintToFileFirstTitle", "Render to a file before printing"),
            PhoneStrings.Get(
                "ApplicationPrintToFileFirstDescription",
                "Uses Chummer5's printtofilefirst application option."),
            _printToFileFirst));

        _printSkillsWithZeroRating = CreateSwitch(
            "settings-print-zero-rating-skills",
            _baseline.PrintSkillsWithZeroRating);
        body.Add(CreateSwitchCard(
            PhoneStrings.Get("ApplicationPrintZeroSkillsTitle", "Include skills with rating zero"),
            PhoneStrings.Get(
                "ApplicationPrintZeroSkillsDescription",
                "Keeps zero-rated skills on generated character sheets."),
            _printSkillsWithZeroRating));

        _printExpenses = CreateSwitch("settings-print-expenses", _baseline.PrintExpenses);
        body.Add(CreateSwitchCard(
            PhoneStrings.Get("ApplicationPrintExpensesTitle", "Include expenses"),
            PhoneStrings.Get(
                "ApplicationPrintExpensesDescription",
                "Adds the expense history to printable output."),
            _printExpenses));

        _printFreeExpenses = CreateSwitch(
            "settings-print-free-expenses",
            _baseline.PrintFreeExpenses);
        body.Add(CreateSwitchCard(
            PhoneStrings.Get("ApplicationPrintFreeExpensesTitle", "Include free expenses"),
            PhoneStrings.Get(
                "ApplicationPrintFreeExpensesDescription",
                "Available only while expense printing is enabled, matching Chummer5."),
            _printFreeExpenses));

        _printNotes = CreateSwitch("settings-print-notes", _baseline.PrintNotes);
        body.Add(CreateSwitchCard(
            PhoneStrings.Get("ApplicationPrintNotesTitle", "Include notes"),
            PhoneStrings.Get(
                "ApplicationPrintNotesDescription",
                "Adds runner notes to printable output."),
            _printNotes));

        _insertPdfNotesIfAvailable = CreateSwitch(
            "settings-insert-pdf-notes",
            _baseline.InsertPdfNotesIfAvailable);
        body.Add(CreateSwitchCard(
            PhoneStrings.Get("ApplicationInsertPdfNotesTitle", "Insert PDF notes when available"),
            PhoneStrings.Get(
                "ApplicationInsertPdfNotesDescription",
                "Uses embedded PDF notes when the selected sheet provides them."),
            _insertPdfNotesIfAvailable));

        _printExpenses.Toggled += (_, _) => UpdateExpenseDependency();
        UpdateExpenseDependency();

        _revision = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _revision.AutomationId = "settings-print-revision";
        body.Add(_revision);

        Button save = NativeTheme.PrimaryButton(PhoneStrings.Get("Save", "Save"));
        save.AutomationId = "settings-print-save";
        save.Clicked += async (_, _) => await RunAsync(async () =>
        {
            await Coordinator.SaveApplicationPrintSettingsAsync(
                _printToFileFirst.IsToggled,
                _printSkillsWithZeroRating.IsToggled,
                _printExpenses.IsToggled,
                _printFreeExpenses.IsToggled,
                _printNotes.IsToggled,
                _insertPdfNotesIfAvailable.IsToggled,
                _baseline.Revision);
            await Navigation.PopToRootAsync();
        });
        body.Add(save);

        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        _revision.Text = PhoneStrings.Format(
            "ApplicationSettingsRevision",
            "Settings revision {0}",
            _baseline.Revision);
    }

    private void UpdateExpenseDependency()
    {
        _printFreeExpenses.IsEnabled = _printExpenses.IsToggled;
        if (!_printExpenses.IsToggled)
        {
            _printFreeExpenses.IsToggled = false;
        }
    }

    private static Switch CreateSwitch(string automationId, bool value)
        => new()
        {
            AutomationId = automationId,
            IsToggled = value
        };

    private static Border CreateSwitchCard(string title, string description, Switch value)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        VerticalStackLayout labels = new() { Spacing = 3 };
        labels.Add(NativeTheme.Title(title, 20));
        labels.Add(NativeTheme.Body(description, NativeTheme.Muted));
        row.Add(labels);
        row.Add(value, 1);
        return NativeTheme.Card(row);
    }
}
