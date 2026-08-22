using System.Globalization;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-only Chummer5 Global Options surface for confirmation, visibility, selection behavior,
/// and date/time settings.
/// All controls are local drafts; only the explicit Save action invokes one atomic persistence boundary.
/// </summary>
public sealed class ApplicationSettingsPage : NativePageBase
{
    private readonly ApplicationDeleteConfirmationState _baseline;
    private readonly Switch _confirmDelete;
    private readonly Switch _confirmKarmaExpense;
    private readonly Switch _hideMasterIndex;
    private readonly Switch _hideCharacterRoster;
    private readonly Switch _searchInCategoryOnly;
    private readonly Switch _allowEasterEggs;
    private readonly Switch _customDateTimeFormats;
    private readonly Entry _dateFormat;
    private readonly Label _datePreview;
    private readonly Entry _timeFormat;
    private readonly Label _timePreview;
    private readonly Switch _datesIncludeTime;
    private readonly CultureInfo _culture;
    private readonly Label _revision;

    public ApplicationSettingsPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Application settings";
        AutomationId = "application-settings-page";
        _baseline = coordinator.ApplicationSettings;
        _culture = CultureInfo.CurrentCulture;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 20, 20, 36),
            Spacing = 18
        };
        body.Add(NativeTheme.Eyebrow("Global options"));
        body.Add(NativeTheme.Title("Confirmations"));
        body.Add(NativeTheme.Body(
            "Matches Chummer5’s confirmdelete and confirmkarmaexpense options. These settings do not modify runner XML.",
            NativeTheme.Muted));

        _confirmDelete = new Switch
        {
            AutomationId = "settings-confirm-delete",
            IsToggled = _baseline.ConfirmDelete
        };
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
        labels.Add(NativeTheme.Title("Ask before deleting items", 20));
        labels.Add(NativeTheme.Body(
            "Changes remain a draft until Save. Back discards the draft.",
            NativeTheme.Muted));
        row.Add(labels);
        row.Add(_confirmDelete, 1);
        body.Add(NativeTheme.Card(row));

        _confirmKarmaExpense = new Switch
        {
            AutomationId = "settings-confirm-karma-expense",
            IsToggled = _baseline.ConfirmKarmaExpense
        };
        Grid karmaRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        VerticalStackLayout karmaLabels = new() { Spacing = 3 };
        karmaLabels.Add(NativeTheme.Title("Ask before Karma expenses", 20));
        karmaLabels.Add(NativeTheme.Body(
            "Changes remain a draft until Save. Back discards both confirmation drafts.",
            NativeTheme.Muted));
        karmaRow.Add(karmaLabels);
        karmaRow.Add(_confirmKarmaExpense, 1);
        body.Add(NativeTheme.Card(karmaRow));

        body.Add(NativeTheme.Title("Navigation visibility"));
        body.Add(NativeTheme.Body(
            "Matches Chummer5’s independent hidemasterindex and hidecharacterroster application options.",
            NativeTheme.Muted));

        _hideMasterIndex = new Switch
        {
            AutomationId = "settings-hide-master-index",
            IsToggled = _baseline.HideMasterIndex
        };
        body.Add(CreateSwitchCard(
            "Hide the Master Index",
            "Stored as the hidemasterindex application setting. It does not change runner XML.",
            _hideMasterIndex));

        _hideCharacterRoster = new Switch
        {
            AutomationId = "settings-hide-character-roster",
            IsToggled = _baseline.HideCharacterRoster
        };
        body.Add(CreateSwitchCard(
            "Hide the Character Roster",
            "Stored independently as hidecharacterroster. Back discards both visibility drafts.",
            _hideCharacterRoster));

        body.Add(NativeTheme.Title("Selection behavior"));
        body.Add(NativeTheme.Body(
            "Matches Chummer5’s independent searchincategoryonly and alloweastereggs application options.",
            NativeTheme.Muted));

        _searchInCategoryOnly = new Switch
        {
            AutomationId = "settings-search-in-category-only",
            IsToggled = _baseline.SearchInCategoryOnly
        };
        body.Add(CreateSwitchCard(
            "Search only in the current category",
            "Enabled by default, matching Chummer5 selection forms.",
            _searchInCategoryOnly));

        _allowEasterEggs = new Switch
        {
            AutomationId = "settings-allow-easter-eggs",
            IsToggled = _baseline.AllowEasterEggs
        };
        body.Add(CreateSwitchCard(
            "Allow Easter Eggs",
            "Disabled by default. It remains independent of category-restricted searching.",
            _allowEasterEggs));

        body.Add(NativeTheme.Title("Date and time"));
        body.Add(NativeTheme.Body(
            "Matches Chummer5’s custom format phase and Dates include time option. Invalid custom text shows Error, as on desktop; Save preserves the exact draft.",
            NativeTheme.Muted));

        _customDateTimeFormats = new Switch
        {
            AutomationId = "settings-custom-date-time-formats",
            IsToggled = _baseline.CustomDateTimeFormats
        };
        body.Add(CreateSwitchCard(
            "Use custom date and time formats",
            "Turning this off restores the current culture’s short date and time patterns in the draft.",
            _customDateTimeFormats));

        string initialDateFormat = _baseline.CustomDateTimeFormats
            ? _baseline.CustomDateFormat
            : _culture.DateTimeFormat.ShortDatePattern;
        _dateFormat = new Entry
        {
            AutomationId = "settings-date-format",
            Text = initialDateFormat,
            IsEnabled = _baseline.CustomDateTimeFormats,
            Placeholder = _culture.DateTimeFormat.ShortDatePattern
        };
        _datePreview = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _datePreview.AutomationId = "settings-date-format-preview";
        VerticalStackLayout dateCard = new() { Spacing = 8 };
        dateCard.Add(NativeTheme.Title("Date format", 20));
        dateCard.Add(_dateFormat);
        dateCard.Add(_datePreview);
        body.Add(NativeTheme.Card(dateCard));

        string initialTimeFormat = _baseline.CustomDateTimeFormats
            ? _baseline.CustomTimeFormat
            : _culture.DateTimeFormat.ShortTimePattern;
        _timeFormat = new Entry
        {
            AutomationId = "settings-time-format",
            Text = initialTimeFormat,
            IsEnabled = _baseline.CustomDateTimeFormats,
            Placeholder = _culture.DateTimeFormat.ShortTimePattern
        };
        _timePreview = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _timePreview.AutomationId = "settings-time-format-preview";
        VerticalStackLayout timeCard = new() { Spacing = 8 };
        timeCard.Add(NativeTheme.Title("Time format", 20));
        timeCard.Add(_timeFormat);
        timeCard.Add(_timePreview);
        body.Add(NativeTheme.Card(timeCard));

        _datesIncludeTime = new Switch
        {
            AutomationId = "settings-dates-include-time",
            IsToggled = _baseline.DatesIncludeTime
        };
        body.Add(CreateSwitchCard(
            "Dates include time",
            "Independent of whether culture-default or custom formatting is active.",
            _datesIncludeTime));

        _customDateTimeFormats.Toggled += (_, args) =>
            UpdateDateTimeDraft(resetCultureDefaults: !args.Value);
        _dateFormat.TextChanged += (_, _) => UpdateDateTimePreviews();
        _timeFormat.TextChanged += (_, _) => UpdateDateTimePreviews();
        UpdateDateTimeDraft(resetCultureDefaults: false);

        _revision = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _revision.AutomationId = "settings-revision";
        body.Add(_revision);

        Button save = NativeTheme.PrimaryButton("Save");
        save.AutomationId = "settings-save";
        save.Clicked += async (_, _) => await RunAsync(async () =>
        {
            await Coordinator.SaveApplicationSettingsAsync(
                _confirmDelete.IsToggled,
                _confirmKarmaExpense.IsToggled,
                _customDateTimeFormats.IsToggled,
                _dateFormat.Text ?? string.Empty,
                _timeFormat.Text ?? string.Empty,
                _datesIncludeTime.IsToggled,
                _hideMasterIndex.IsToggled,
                _hideCharacterRoster.IsToggled,
                _searchInCategoryOnly.IsToggled,
                _allowEasterEggs.IsToggled,
                _baseline.Revision);
            await Navigation.PopAsync();
        });
        body.Add(save);

        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        _revision.Text = $"Settings revision {_baseline.Revision}";
    }

    private void UpdateDateTimeDraft(bool resetCultureDefaults)
    {
        bool custom = _customDateTimeFormats.IsToggled;
        _dateFormat.IsEnabled = custom;
        _timeFormat.IsEnabled = custom;
        if (resetCultureDefaults)
        {
            _dateFormat.Text = _culture.DateTimeFormat.ShortDatePattern;
            _timeFormat.Text = _culture.DateTimeFormat.ShortTimePattern;
        }
        UpdateDateTimePreviews();
    }

    private void UpdateDateTimePreviews()
    {
        DateTime sample = DateTime.Now;
        ApplicationDateTimeFormatPreview date = ApplicationDeleteConfirmationRules.PreviewDateTimeFormat(
            ApplicationSettingIdentity.CustomDateFormat,
            _customDateTimeFormats.IsToggled,
            _dateFormat.Text ?? string.Empty,
            _culture.DateTimeFormat.ShortDatePattern,
            sample,
            _culture);
        ApplicationDateTimeFormatPreview time = ApplicationDeleteConfirmationRules.PreviewDateTimeFormat(
            ApplicationSettingIdentity.CustomTimeFormat,
            _customDateTimeFormats.IsToggled,
            _timeFormat.Text ?? string.Empty,
            _culture.DateTimeFormat.ShortTimePattern,
            sample,
            _culture);
        _datePreview.Text = $"{date.Phase} preview: {date.Sample}";
        _timePreview.Text = $"{time.Phase} preview: {time.Sample}";
    }

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
