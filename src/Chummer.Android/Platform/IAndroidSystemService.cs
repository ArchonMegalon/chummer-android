namespace Chummer.Android.Platform;

public enum AndroidUpdateCheckResult
{
    Current,
    Started,
    ReadyToInstall,
    PlayManagedRequired,
    Checking,
    Unavailable
}

public interface IAndroidSystemService
{
    Task<bool> OpenUriAsync(Uri uri);
    Task<AndroidUpdateCheckResult> CheckForUpdatesAsync();
    Task ShareTextAsync(string text);
    Task<bool> PrintPdfAsync(string fileName, string contentBase64, string title, CancellationToken cancellationToken);

    Task<bool> PrintPdfAsync(string fileName, string contentBase64, string title,
        Func<bool> isOriginalContextCurrent, CancellationToken cancellationToken)
        => Task.FromException<bool>(new InvalidOperationException("Context-bound printing is unavailable."));
}
