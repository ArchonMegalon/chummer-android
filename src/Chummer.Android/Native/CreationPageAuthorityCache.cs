namespace Chummer.Android.Native;

/// <summary>
/// Reuses a dashboard-projected, revision-bound authority when a Creation
/// deep-link opens.  The fallback is invoked only when the captured authority
/// no longer matches the live workspace, avoiding a second synchronous Core
/// source projection on the Android UI thread.
/// </summary>
public static class CreationPageAuthorityCache
{
    public static TState? Resolve<TState>(
        TState? captured,
        Func<TState, bool> isCurrent,
        Func<TState?> loadCurrent)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(isCurrent);
        ArgumentNullException.ThrowIfNull(loadCurrent);
        return captured is not null && isCurrent(captured)
            ? captured
            : loadCurrent();
    }
}
