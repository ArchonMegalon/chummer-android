namespace Chummer.Android.Platform;

public interface IAndroidSystemService
{
    Task OpenUriAsync(Uri uri);
    Task OpenStoreListingAsync();
    Task ShareTextAsync(string text);
    Task<bool> PrintCurrentViewAsync(string jobName);
    Task<bool> PrintPdfAsync(string fileName, string contentBase64, string title, CancellationToken cancellationToken);
}
