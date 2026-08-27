namespace Chummer.Android.Native;

internal sealed class PreferencesSr5VehicleWorkshopCheckpointBackend :
    ISr5VehicleWorkshopCheckpointBackend
{
    internal const string StorageKey = "chummer.android.sr5-vehicle-workshop.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}
