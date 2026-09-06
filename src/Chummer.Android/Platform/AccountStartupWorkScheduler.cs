namespace Chummer.Android.Platform;

/// <summary>
/// Keeps account recovery and remote grant validation out of the first-render UI context.
/// Account state remains fail-closed as Loading until the owned operation publishes a snapshot.
/// </summary>
internal static class AccountStartupWorkScheduler
{
    internal static Task RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }
}
