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

// Native view tests now use actual MAUI Controls/Essentials assemblies. Every
// journal test still injects its own backend; Android Preferences are neither
// simulated nor invoked as evidence of durable physical-device storage.
