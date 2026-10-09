using Chummer.Contracts.Api;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android.Native;

/// <summary>
/// Android-phone settings. Desktop-only Chummer5 preferences remain readable by the shared
/// settings store for compatibility, but are deliberately not exposed as ineffective phone controls.
/// </summary>
public sealed class ApplicationSettingsPage : NativePageBase
{
    private ApplicationDeleteConfirmationState _baseline;
    private readonly IPlayReviewService? _playReview;
    private readonly Switch _confirmDelete;
    private readonly Picker _language;
    private readonly Picker _region;
    private readonly NativeProblemReporter? _reports;
    private readonly Button? _automaticDiagnostics;
    private readonly Label? _diagnosticsStatus;
    private bool _diagnosticsEnabled;
    private bool _diagnosticsBusy;
    private bool? _diagnosticsRetryTarget;

    public ApplicationSettingsPage(RunnerSessionCoordinator coordinator)
        : this(
            coordinator,
            IPlatformApplication.Current?.Services.GetService<IPlayReviewService>())
    {
    }

    public ApplicationSettingsPage(
        RunnerSessionCoordinator coordinator,
        IPlayReviewService? playReview) : base(coordinator)
    {
        Title = PhoneStrings.Get("ApplicationSettings", "Settings");
        AutomationId = "application-settings-page";
        _baseline = coordinator.ApplicationSettings;
        _playReview = playReview;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 20, 20, 36),
            Spacing = 18
        };
        body.Add(NativeTheme.Eyebrow(PhoneStrings.Get("SettingsPhone", "Android phone")));
        body.Add(NativeTheme.Title(PhoneStrings.Get("ApplicationSettings", "Settings")));
        body.Add(NativeTheme.Body(
            PhoneStrings.Get(
                "SettingsPhoneOnlyDetail",
                "Only options that change how Chummer behaves on this phone appear here."),
            NativeTheme.Muted));

        body.Add(NativeTheme.Title(PhoneStrings.Get("SettingsSafety", "Safety")));
        _confirmDelete = new Switch
        {
            AutomationId = "settings-confirm-delete",
            IsToggled = _baseline.ConfirmDelete
        };
        body.Add(CreateSwitchCard(
            PhoneStrings.Get("SettingsConfirmDelete", "Ask before deleting items"),
            CurrentPhoneWizardScope.MarkExperimental(
                PhoneStrings.Get(
                    "SettingsConfirmDeleteDetail",
                    "Shown before destructive item removal. Wizard review steps always remain enabled.")),
            _confirmDelete,
            "settings-confirm-delete-experimental"));

        PhoneLocalePreferences locale = PhoneLocalePolicy.ReadPreferences(Preferences.Default);
        VerticalStackLayout languageCard = new() { Spacing = 5 };
        languageCard.Add(NativeTheme.Title(
            PhoneStrings.Get("SettingsLanguageRegion", "Language & region"),
            20));
        _language = CreateLocalePicker(
            "settings-language", PhoneStrings.Get("SettingsAppLanguage", "App language"),
            PhoneLocalePolicy.LanguageChoices(), locale.Language);
        _region = CreateLocalePicker(
            "settings-region", PhoneStrings.Get("SettingsRegionalFormats", "Regional formats"),
            PhoneLocalePolicy.RegionChoices(), locale.Region);
        languageCard.Add(NativeTheme.Body(_language.Title));
        languageCard.Add(_language);
        languageCard.Add(NativeTheme.Body(_region.Title));
        languageCard.Add(_region);
        Label formatPreview = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        formatPreview.AutomationId = "settings-region-preview";
        void RefreshFormatPreview()
        {
            string selectedRegion = (_region.SelectedItem as PhoneLocaleChoice)?.Value ?? string.Empty;
            CultureInfo formats = selectedRegion.Length == 0
                ? PhoneLocalePolicy.SystemFormatCulture
                : CultureInfo.GetCultureInfo(selectedRegion);
            formatPreview.Text = $"{new DateTime(2026, 2, 21).ToString("d", formats)} · {1234.56m.ToString("N2", formats)}";
        }
        _region.SelectedIndexChanged += (_, _) => RefreshFormatPreview();
        RefreshFormatPreview();
        languageCard.Add(formatPreview);
        languageCard.Add(NativeTheme.Body(
            PhoneStrings.Get(
                "SettingsLocaleRestart",
                "Save, then restart Chummer to apply. Book language and game rules stay unchanged."),
            NativeTheme.Muted));
        body.Add(NativeTheme.Card(languageCard));

        NativeProblemLog? diagnostics = IPlatformApplication.Current?.Services.GetService<NativeProblemLog>();
        var system = IPlatformApplication.Current?.Services.GetService<Chummer.Android.Platform.IAndroidSystemService>();
        if (diagnostics is not null && system is not null)
        {
            body.Add(NativeTheme.Title(PhoneStrings.Get("SettingsDiagnostics", "Technical diagnostics")));
            body.Add(NativeTheme.Body(PhoneStrings.Get("SettingsDiagnosticsLocal",
                "Chummer keeps up to 128 technical events for two days on this phone: app version, page category, operation, duration and error category. No runner, book, account data or error text."), NativeTheme.Muted));
            _reports = IPlatformApplication.Current?.Services.GetService<NativeProblemReporter>();
            if (_reports is not null)
            {
                body.Add(NativeTheme.Body(NativeProblemPolicy.InternalTestBuild
                    ? PhoneStrings.Get("SettingsDiagnosticsInternalDefault", "Internal test: automatic technical reports are on by default. A saved off choice stays off, including after updates. You can disable sending below.")
                    : PhoneStrings.Get("SettingsDiagnosticsOtherDefault", "Automatic technical reports are off by default in this build. A saved explicit choice is retained."), NativeTheme.Muted));
                body.Add(NativeTheme.Body(PhoneStrings.Get("SettingsDiagnosticsAutomaticDetail",
                    "When enabled, new failures and operations over 30 seconds are sent to the Chummer team at chummer.run while the app is running. Up to eight reports per two days; no old history. Reports stay in the private inbox for up to two days. Disable anytime to clear unsent reports; already received reports expire separately."), NativeTheme.Muted));
                _diagnosticsStatus = NativeTheme.Body(PhoneStrings.Get("SettingsDiagnosticsLoading", "Loading diagnostic setting…"));
                _diagnosticsStatus.AutomationId = "settings-diagnostics-status";
                body.Add(_diagnosticsStatus);
                _automaticDiagnostics = NativeTheme.SecondaryButton(PhoneStrings.Get("SettingsDiagnosticsEnable", "Enable automatic technical reports"));
                _automaticDiagnostics.AutomationId = "settings-diagnostics-automatic";
                _automaticDiagnostics.IsEnabled = false;
                _automaticDiagnostics.Clicked += async (_, _) => await RefreshDiagnosticsAsync(_diagnosticsRetryTarget ?? !_diagnosticsEnabled);
                body.Add(_automaticDiagnostics);
            }
            Button shareDiagnostics = NativeTheme.SecondaryButton(PhoneStrings.Get("SettingsDiagnosticsShare", "Share technical report"));
            shareDiagnostics.AutomationId = "settings-share-diagnostics";
            shareDiagnostics.Clicked += async (_, _) => await RunAsync(async () =>
                await system.ShareTextAsync(await diagnostics.ExportAsync()));
            body.Add(shareDiagnostics);
        }

        Label updateAuthority = NativeTheme.Body(
            PhoneStrings.Get(
                "SettingsUpdatesPlayManaged",
                "Updates and preview access are managed by Google Play, not inside Chummer."),
            NativeTheme.Muted);
        updateAuthority.AutomationId = "settings-updates-play-managed";
        body.Add(NativeTheme.Title(PhoneStrings.Get("SettingsGooglePlay", "Google Play")));
        body.Add(updateAuthority);
        if (_playReview is not null)
        {
            body.Add(NativeTheme.NavigationRow(
                PlayReviewStrings.RateOnGooglePlay(),
                PlayReviewStrings.RateOnGooglePlayDescription(),
                OpenStoreListingAsync,
                automationId: "settings-rate-on-google-play"));
        }

        Button save = NativeTheme.PrimaryButton(PhoneStrings.Get("Save", "Save"));
        save.AutomationId = "settings-save";
        save.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (_language.SelectedItem is not PhoneLocaleChoice language
                || _region.SelectedItem is not PhoneLocaleChoice region) return;
            await Coordinator.SaveDeleteConfirmationSettingAsync(
                _confirmDelete.IsToggled,
                _baseline.Revision);
            _baseline = Coordinator.ApplicationSettings;
            PhoneLocalePolicy.SavePreferences(Preferences.Default, new(language.Value, region.Value));
            await Navigation.PopAsync();
        });
        body.Add(save);

        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        // This page stages phone settings. The baseline is intentionally held stable until Save
        // so a concurrent settings update fails through the coordinator's expected-revision check.
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshDiagnosticsAsync();
    }

    private async Task RefreshDiagnosticsAsync(bool? enabled = null)
    {
        if (_reports is null || _automaticDiagnostics is null || _diagnosticsStatus is null || _diagnosticsBusy) return;
        _diagnosticsBusy = true;
        _automaticDiagnostics.IsEnabled = false;
        _diagnosticsStatus.Text = PhoneStrings.Get("SettingsDiagnosticsLoading", "Loading diagnostic setting…");
        try
        {
            bool saved = enabled is null || await _reports.SetEnabledAsync(enabled.Value);
            if (enabled is not null) _diagnosticsRetryTarget = saved ? null : enabled;
            _diagnosticsEnabled = await _reports.IsEnabledAsync();
            _diagnosticsStatus.Text = _diagnosticsRetryTarget is not null
                ? PhoneStrings.Get("SettingsDiagnosticsSaveFailed", "Could not save this choice. Sending is stopped for this session. Try again before restarting Chummer.")
                : _diagnosticsEnabled
                    ? PhoneStrings.Get("SettingsDiagnosticsOn", "Automatic technical reports: on. Changes apply immediately.")
                    : PhoneStrings.Get("SettingsDiagnosticsOff", "Automatic technical reports: off. Changes apply immediately.");
            _automaticDiagnostics.Text = _diagnosticsRetryTarget is not null
                ? PhoneStrings.Get("SettingsDiagnosticsRetry", "Retry saving diagnostic choice")
                : _diagnosticsEnabled
                ? PhoneStrings.Get("SettingsDiagnosticsDisable", "Disable and clear unsent reports")
                : PhoneStrings.Get("SettingsDiagnosticsEnable", "Enable automatic technical reports");
        }
        catch (OperationCanceledException) { }
        finally { _diagnosticsBusy = false; _automaticDiagnostics.IsEnabled = true; }
    }

    private static Picker CreateLocalePicker(string automationId, string title,
        PhoneLocaleChoice[] choices, string selectedValue)
    {
        Picker picker = new()
        {
            AutomationId = automationId,
            Title = title,
            ItemsSource = choices,
            ItemDisplayBinding = new Binding(nameof(PhoneLocaleChoice.Label)),
            SelectedItem = choices.First(choice => choice.Value == selectedValue),
            TextColor = NativeTheme.Text,
            BackgroundColor = NativeTheme.Surface,
            MinimumHeightRequest = 52
        };
        SemanticProperties.SetDescription(picker, title);
        return picker;
    }

    private static Border CreateSwitchCard(
        string title,
        string description,
        Switch value,
        string descriptionAutomationId)
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
        Label descriptionLabel = NativeTheme.Body(description, NativeTheme.Danger);
        descriptionLabel.AutomationId = descriptionAutomationId;
        labels.Add(descriptionLabel);
        SemanticProperties.SetDescription(value, $"{title}. {description}");
        row.Add(labels);
        row.Add(value, 1);
        return NativeTheme.Card(row);
    }

    private async Task OpenStoreListingAsync()
    {
        if (_playReview is not null)
        {
            await _playReview.OpenStoreListingAsync();
        }
    }
}
