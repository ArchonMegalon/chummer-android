namespace Microsoft.Maui.Storage
{
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
}

namespace Chummer.Android.Native
{
    public interface ISr5CareerCheckpointBackend
    {
        string Read();
        void Write(string payload);
        void Remove();
    }

    internal sealed class MemoryBackend : ISr5CareerCheckpointBackend
    {
        public string Payload { get; set; } = string.Empty;
        public string Read() => Payload;
        public void Write(string payload) => Payload = payload;
        public void Remove() => Payload = string.Empty;
    }
}
