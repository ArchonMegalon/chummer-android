using Android.App;
using Android.Content;

namespace Chummer.Android.Platform;

internal static class DocumentIntentBroker
{
    internal const int OpenRequestCode = 6411;
    internal const int CreateRequestCode = 6412;
    private static TaskCompletionSource<global::Android.Net.Uri?>? _pending;

    public static Task<global::Android.Net.Uri?> LaunchAsync(Activity activity, Intent intent, int requestCode, CancellationToken cancellationToken)
    {
        TaskCompletionSource<global::Android.Net.Uri?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _pending, completion, null) is not null)
        {
            throw new InvalidOperationException("Another Android document request is already active.");
        }

        CancellationTokenRegistration registration = cancellationToken.Register(() => Complete(null));
        _ = completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        try
        {
            activity.StartActivityForResult(intent, requestCode);
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _pending, null, completion);
            completion.TrySetException(ex);
        }

        return completion.Task;
    }

    public static void Complete(global::Android.Net.Uri? uri)
    {
        TaskCompletionSource<global::Android.Net.Uri?>? completion = Interlocked.Exchange(ref _pending, null);
        completion?.TrySetResult(uri);
    }
}
