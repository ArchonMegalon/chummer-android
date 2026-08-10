using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;

namespace Chummer.Android;

internal static class AndroidInAppUpdatePolicy
{
    internal static bool ShouldStartFlexibleUpdate(int availability, bool flexibleAllowed)
        => availability == UpdateAvailability.UpdateAvailable && flexibleAllowed;

    internal static bool ShouldOfferCompletion(int installStatus)
        => installStatus == InstallStatus.Downloaded;
}
