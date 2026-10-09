using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed native phone copy. Callers provide an explicit English fallback so a missing
/// or damaged satellite assembly remains usable and can be detected by completeness tests.
/// </summary>
public static class PhoneStrings
{
    // The shared presenter retains canonical English feedback. Translate only
    // exact known notices at the display boundary, never the underlying state
    // or arbitrary exception/user text. In particular this grants no save or
    // account-recovery authority.
    internal static string? RunnerNotice(string? notice)
    {
        switch (notice)
        {
            case "Saved.":
                return Get("RunnerNoticeSaved", "Saved.");
            case "Application settings saved.":
                return Get("RunnerNoticeSettingsSaved", "Application settings saved.");
            case "Account recovery is still finishing.":
                return Get("RunnerNoticeAccountRecovery", "Account recovery is still finishing.");
            case "Workspace verification unavailable. Recent-file attribution was not recorded.":
                return Get("RunnerNoticeVerificationUnavailable",
                    "Workspace verification unavailable. Recent-file attribution was not recorded.");
            case "Restored 1 runner dossier.":
                return Get("RunnerNoticeRestoredOne", "Restored 1 runner dossier.");
        }

        const string prefix = "Restored ";
        const string suffix = " runner dossiers.";
        if (notice is not null && notice.StartsWith(prefix, StringComparison.Ordinal)
            && notice.EndsWith(suffix, StringComparison.Ordinal)
            && notice.Length > prefix.Length + suffix.Length)
        {
            ReadOnlySpan<char> digits = notice.AsSpan(prefix.Length,
                notice.Length - prefix.Length - suffix.Length);
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                && count != 1
                && digits.SequenceEqual(count.ToString(CultureInfo.InvariantCulture).AsSpan()))
                return Format("RunnerNoticeRestoredMany", "Restored {0} runner dossiers.", count);
        }

        return notice;
    }

    // Display only. Workspace selection/deletion must continue using the retained
    // typed identity, never this label (names and aliases need not be unique).
    internal static string RunnerName(string? name, string? alias)
    {
        name = name?.Trim();
        alias = alias?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return string.IsNullOrWhiteSpace(alias) ? Get("RunnerFallback", "Runner") : alias;
        if (string.IsNullOrWhiteSpace(alias)
            || string.Equals(alias, "Runner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)) return name;
        return $"{alias} · {name}";
    }

    private static readonly ResourceManager ResourceManager = new(
        "Chummer.Android.Resources.Localization.PhoneStrings",
        typeof(PhoneStrings).Assembly);

    public static string Get(string key, string englishFallback, CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(englishFallback);
        try
        {
            return ResourceManager.GetString(key, culture ?? CultureInfo.CurrentUICulture)
                   ?? englishFallback;
        }
        catch (MissingManifestResourceException)
        {
            // Platform-neutral compile gates intentionally omit Android resource satellites.
            return englishFallback;
        }
    }

    public static string Format(
        string key,
        string englishFallback,
        params object?[] arguments)
        => string.Format(
            CultureInfo.CurrentCulture,
            Get(key, englishFallback),
            arguments);
}
