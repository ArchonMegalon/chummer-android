using Chummer.Application.Owners;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private int _initialPhoneAccountRecoverySettled;

    private async Task InitializeAccountWithInitialPhoneReadinessAsync()
    {
        try { await InitializeAccountInBackgroundAsync().ConfigureAwait(false); }
        finally
        {
            // The account call has finished and released its gates. This is
            // completion evidence, not the displayed Linked/Loading label.
            Volatile.Write(ref _initialPhoneAccountRecoverySettled, 1);
            try { NotifyChanged(); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // An observer cannot un-set recovery completion or make the
                // background account task fail after its actual outcome.
                _ = exception;
            }
        }
    }

    public PhoneInitialRouteReadiness CaptureInitialPhoneRouteReadiness()
    {
        // Capture is non-blocking: account Changed can execute while the real
        // credential writer owns its non-reentrant lease. No store or Hub read.
        bool available = TryCaptureWorkspaceOwner(out OwnerContextStamp? owner)
            && owner is { IsValid: true };
        if (_disposed || (_initialized && !available
            && Volatile.Read(ref _initialPhoneAccountRecoverySettled) != 0))
            return new(PhoneInitialRouteReadinessKind.Unavailable, owner, null, false);
        if (!_initialized || _workspaceOwnerInitializationPending || !available)
            return new(PhoneInitialRouteReadinessKind.Pending, owner, null, false);

        var overview = State;
        var shell = _shellPresenter.State;
        if (overview.Session.OwnerContext != owner || shell.OwnerContext != owner
            || overview.Error is not null || shell.Error is not null)
            return new(PhoneInitialRouteReadinessKind.Pending, owner, null, false);
        if (overview.WorkspaceId is not null
            && (overview.DisplayOwnerContext != owner
                || overview.Session.ActiveWorkspaceId != overview.WorkspaceId
                || shell.ActiveWorkspaceId != overview.WorkspaceId
                || overview.Profile is null))
            return new(PhoneInitialRouteReadinessKind.Pending, owner, null, false);
        if (!TryCaptureWorkspaceOwner(out OwnerContextStamp? current) || current != owner)
            return new(PhoneInitialRouteReadinessKind.Unavailable, current, null, false);
        return new(PhoneInitialRouteReadinessKind.Ready, owner,
            overview.WorkspaceId, overview.Profile is not null);
    }
}
