using Android.App;
using Android.Runtime;

namespace Chummer.Android;

[Application(AllowBackup = false, UsesCleartextTraffic = false)]
public sealed class MainApplication : MauiApplication
{
    public MainApplication(nint handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
