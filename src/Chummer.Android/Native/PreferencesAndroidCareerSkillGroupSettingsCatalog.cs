using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

internal sealed class PreferencesAndroidCareerSkillGroupSettingsCatalog :
    IAndroidCareerSkillGroupSettingsCatalog
{
    private const string StorageKey =
        "chummer.android.character-settings-catalog.v1";

    public string ReadCatalogJson()
        => Preferences.Default.Get(StorageKey, string.Empty);
}
