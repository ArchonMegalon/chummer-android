namespace Chummer.Android.Platform;

public interface IAndroidSystemService
{
    Task<bool> OpenUriAsync(Uri uri);
    Task<bool> OpenStoreListingAsync();
    Task ShareTextAsync(string text);
    Task<bool> PrintPdfAsync(string fileName, string contentBase64, string title, CancellationToken cancellationToken);
}
