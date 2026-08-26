namespace Microsoft.Maui.Storage;

public interface IPreferences
{
    string Get(string key, string fallback);
    void Set(string key, string value);
    void Remove(string key);
}

internal sealed class MemoryPreferences : IPreferences
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public string Get(string key, string fallback)
        => _values.TryGetValue(key, out string? value) ? value : fallback;

    public void Set(string key, string value) => _values[key] = value;
    public void Remove(string key) => _values.Remove(key);
}

public static class Preferences
{
    public static IPreferences Default { get; } = new MemoryPreferences();
}
