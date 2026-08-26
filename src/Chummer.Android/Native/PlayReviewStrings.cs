using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

public static class PlayReviewStrings
{
    private static readonly ResourceManager ResourceManager = new(
        "Chummer.Android.Resources.Localization.ReviewStrings",
        typeof(PlayReviewStrings).Assembly);

    public static string SettingsSection(CultureInfo? culture = null)
        => Get("SettingsSection", "Google Play", culture);

    public static string RateOnGooglePlay(CultureInfo? culture = null)
        => Get("RateOnGooglePlay", "Rate Chummer on Google Play", culture);

    public static string RateOnGooglePlayDescription(CultureInfo? culture = null)
        => Get(
            "RateOnGooglePlayDescription",
            "Open Chummer's Google Play listing to write or update a review.",
            culture);

    private static string Get(string key, string fallback, CultureInfo? culture)
    {
        try
        {
            return ResourceManager.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? fallback;
        }
        catch (MissingManifestResourceException)
        {
            // The platform-neutral compile gate intentionally does not embed Android app resources.
            return fallback;
        }
    }
}
