namespace Chummer.Android.Native;

/// <summary>
/// Marker for the small set of root surfaces on which an automatic Play review may be requested.
/// Editor, wizard, preview, review, apply, conflict, and settings-draft pages deliberately do not
/// implement this interface.
/// </summary>
public interface IPlayReviewSafeSurface;

public static class PlayReviewInteractionGuard
{
    private static int _actionDepth;

    public static bool HasActionInFlight => Volatile.Read(ref _actionDepth) > 0;

    public static void EnterAction() => Interlocked.Increment(ref _actionDepth);

    public static void ExitAction() => Interlocked.Decrement(ref _actionDepth);
}

public static class PlayReviewSafety
{
    public static PlayReviewSafetyContext Capture(RunnerSessionCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        Shell? shell = Shell.Current;
        Page? currentPage = shell?.CurrentPage;
        INavigation? navigation = currentPage?.Navigation;
        return new PlayReviewSafetyContext(
            IsExplicitSafeSurface: currentPage is IPlayReviewSafeSurface,
            IsRootNavigation: navigation is not null && navigation.NavigationStack.Count <= 1,
            HasModal: navigation?.ModalStack.Count > 0 || shell?.FlyoutIsPresented == true,
            HasActiveDialog: coordinator.State.ActiveDialog is not null,
            HasUnsavedMutation: coordinator.State.IsDirty,
            HasActionInFlight: PlayReviewInteractionGuard.HasActionInFlight,
            HasBusyWork: coordinator.IsBusy);
    }
}
