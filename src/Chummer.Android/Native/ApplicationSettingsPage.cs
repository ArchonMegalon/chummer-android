using Chummer.Contracts.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android.Native;

/// <summary>
/// Android-phone settings. Desktop-only Chummer5 preferences remain readable by the shared
/// settings store for compatibility, but are deliberately not exposed as ineffective phone controls.
/// </summary>
public sealed class ApplicationSettingsPage : NativePageBase
{
    private readonly ApplicationDeleteConfirmationState _baseline;
    private readonly IPlayReviewService? _playReview;
    private readonly Switch _confirmDelete;

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

        Label languageAuthority = NativeTheme.Body(
            PhoneStrings.Get(
                "SettingsLanguageDeviceManaged",
                "Chummer uses your phone language and regional date and time formats."),
            NativeTheme.Muted);
        languageAuthority.AutomationId = "settings-language-device-managed";
        VerticalStackLayout languageCard = new() { Spacing = 5 };
        languageCard.Add(NativeTheme.Title(
            PhoneStrings.Get("SettingsLanguageRegion", "Language & region"),
            20));
        languageCard.Add(languageAuthority);
        languageCard.Add(NativeTheme.Body(
            PhoneStrings.Get(
                "SettingsSupportedLanguages",
                "Supported: Deutsch, English, Español"),
            NativeTheme.Muted));
        body.Add(NativeTheme.Card(languageCard));

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
            await Coordinator.SaveDeleteConfirmationSettingAsync(
                _confirmDelete.IsToggled,
                _baseline.Revision);
            await Navigation.PopAsync();
        });
        body.Add(save);

        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        // This page stages one phone setting. The baseline is intentionally held stable until Save
        // so a concurrent settings update fails through the coordinator's expected-revision check.
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
