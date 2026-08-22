using Chummer.Android.Platform;

namespace Chummer.Android;

public sealed class App : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new ContentPage());
}

public static class AndroidBundledContentMaterializer
{
    public static string Materialize() => FileSystem.AppDataDirectory;
}

public sealed class AndroidDocumentService : IAndroidDocumentService
{
    public Task<AndroidDocument?> OpenAsync(CancellationToken cancellationToken)
        => Task.FromResult<AndroidDocument?>(null);

    public Task<bool> SaveAsAsync(
        string suggestedName,
        string mediaType,
        Stream content,
        CancellationToken cancellationToken)
        => Task.FromResult(false);
}

public sealed class AndroidImageDocumentService : IAndroidImageDocumentService
{
    public Task<AndroidImageDocumentCandidate?> OpenValidatedAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<AndroidImageDocumentCandidate?>(null);
}

public sealed class AndroidSystemService : IAndroidSystemService
{
    public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);

    public Task<AndroidUpdateCheckResult> CheckForUpdatesAsync()
        => Task.FromResult(AndroidUpdateCheckResult.Unavailable);

    public Task ShareTextAsync(string text) => Task.CompletedTask;

    public Task<bool> PrintPdfAsync(
        string fileName,
        string contentBase64,
        string title,
        CancellationToken cancellationToken)
        => Task.FromResult(false);
}
