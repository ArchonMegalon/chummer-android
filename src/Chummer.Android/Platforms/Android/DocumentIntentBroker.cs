using Android.App;
using Android.Content;

namespace Chummer.Android.Platform;

internal static class DocumentIntentBroker
{
    internal const int OpenRequestCode = 6411;
    internal const int CreateRequestCode = 6412;
    internal const int ImageOpenRequestCode = 6413;
    private sealed record PendingRequest(
        Activity Owner,
        int RequestCode,
        TaskCompletionSource<global::Android.Net.Uri?> Completion)
    {
        public int LaunchState;
    }

    private const int Reserved = 0;
    private const int Launched = 1;
    private const int CancelledBeforeLaunch = 2;

    private static PendingRequest? _pending;

    public static Task<global::Android.Net.Uri?> LaunchAsync(Activity activity, Intent intent, int requestCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(intent);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<global::Android.Net.Uri?>(null);
        }
        TaskCompletionSource<global::Android.Net.Uri?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingRequest pending = new(activity, requestCode, completion);
        if (Interlocked.CompareExchange(ref _pending, pending, null) is not null)
        {
            throw new InvalidOperationException("Another Android document request is already active.");
        }

        CancellationTokenRegistration registration = cancellationToken.Register(
            static state => Cancel((PendingRequest)state!),
            pending);
        _ = completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        if (Interlocked.CompareExchange(ref pending.LaunchState, Launched, Reserved) != Reserved)
        {
            return completion.Task;
        }
        try
        {
            activity.StartActivityForResult(intent, requestCode);
        }
        catch (Exception ex)
        {
            if (TryTake(pending))
            {
                completion.TrySetException(ex);
            }
        }

        return completion.Task;
    }

    public static void Complete(
        Activity activity,
        int requestCode,
        global::Android.Net.Uri? uri)
    {
        ArgumentNullException.ThrowIfNull(activity);
        PendingRequest? pending = Volatile.Read(ref _pending);
        if (pending is not null
            && ReferenceEquals(pending.Owner, activity)
            && pending.RequestCode == requestCode
            && TryTake(pending))
        {
            pending.Completion.TrySetResult(uri);
        }
    }

    public static void Cancel(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        PendingRequest? pending = Volatile.Read(ref _pending);
        if (pending is not null
            && ReferenceEquals(pending.Owner, activity)
            && TryTake(pending))
        {
            pending.Completion.TrySetResult(null);
        }
    }

    private static void Cancel(PendingRequest pending)
    {
        if (Interlocked.CompareExchange(
                ref pending.LaunchState,
                CancelledBeforeLaunch,
                Reserved) == Reserved)
        {
            _ = TryTake(pending);
        }
        // Once Android owns the intent, keep this exact request as a tombstone until its
        // callback or Activity teardown arrives. Reusing a fixed request code sooner could
        // let the retired callback complete a successor from the same Activity.
        pending.Completion.TrySetResult(null);
    }

    private static bool TryTake(PendingRequest pending)
        => ReferenceEquals(
            Interlocked.CompareExchange(ref _pending, null, pending),
            pending);
}
