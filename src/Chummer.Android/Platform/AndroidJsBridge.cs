using Microsoft.JSInterop;

namespace Chummer.Android.Platform;

public sealed class AndroidJsBridge
{
    private readonly IAndroidDocumentService _documents;
    private readonly IAndroidSystemService _system;

    public AndroidJsBridge(IAndroidDocumentService documents, IAndroidSystemService system)
    {
        _documents = documents;
        _system = system;
    }

    [JSInvokable]
    public async Task<bool> SaveBase64Async(string fileName, string contentBase64, string mimeType)
    {
        byte[] bytes = Convert.FromBase64String(contentBase64);
        try
        {
            await using MemoryStream stream = new(bytes, writable: false);
            return await _documents.SaveAsAsync(fileName, mimeType, stream, CancellationToken.None);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    [JSInvokable]
    public Task<bool> PrintBase64Async(string fileName, string contentBase64, string mimeType, string title)
    {
        if (!string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        return _system.PrintPdfAsync(fileName, contentBase64, title, CancellationToken.None);
    }
}
