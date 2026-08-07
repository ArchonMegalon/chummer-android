namespace Chummer.Android.Platform;

public sealed record AndroidDocument(string DisplayName, string ContentUri, string? MediaType, byte[] Content);

public interface IAndroidDocumentService
{
    Task<AndroidDocument?> OpenAsync(CancellationToken cancellationToken);
    Task<bool> SaveAsAsync(string suggestedName, string mediaType, Stream content, CancellationToken cancellationToken);
}
