// Host-only compile seams. Core contracts, projectors, services, and workspace
// persistence are supplied by real Core project references in this harness.
namespace Chummer.Android.Native
{
    // The existing checkpoint backend interface lives beside the unrelated
    // active-skill checkpoint implementation in the Android application.
    public interface ISr5CareerCheckpointBackend
    {
        string Read();
        void Write(string payload);
        void Remove();
    }
}

namespace Microsoft.Maui.Storage
{
    // Tests must inject their host storage backend explicitly. Simulated MAUI
    // preferences must never stand in for durable checkpoint/reopen evidence.
    internal static class Preferences
    {
        public static UnavailablePreferences Default { get; } = new();
    }

    internal sealed class UnavailablePreferences
    {
        public string Get(string key, string fallback)
            => throw MissingHostStorage();

        public void Set(string key, string value)
            => throw MissingHostStorage();

        public void Remove(string key)
            => throw MissingHostStorage();

        private static NotSupportedException MissingHostStorage()
            => new("Managed reward tests must inject an explicit checkpoint backend; MAUI preferences are unavailable.");
    }
}
