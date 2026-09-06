namespace Chummer.Android.Platform;

internal static class DocumentProviderWorkScheduler
{
    internal static Task RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }

    internal static Task<TResult> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }
}
