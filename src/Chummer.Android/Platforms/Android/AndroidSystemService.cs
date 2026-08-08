using Android.App;
using Android.Content;
using Android.Print;
using Android.OS;
using Java.IO;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Buffers;
using System.Security.Cryptography;

namespace Chummer.Android.Platform;

public sealed class AndroidSystemService : IAndroidSystemService
{
    private const string PackageId = "com.myexternalbrain.chummer";

    public Task<bool> OpenUriAsync(Uri uri) => Launcher.Default.OpenAsync(uri);

    public async Task<bool> OpenStoreListingAsync()
    {
        Uri marketUri = new($"market://details?id={PackageId}");
        if (await Launcher.Default.TryOpenAsync(marketUri))
        {
            return true;
        }

        return await Launcher.Default.OpenAsync(new Uri($"https://play.google.com/store/apps/details?id={PackageId}"));
    }

    public Task ShareTextAsync(string text)
        => Share.Default.RequestAsync(new ShareTextRequest(text, "Share Chummer"));

    public async Task<bool> PrintPdfAsync(
        string fileName,
        string contentBase64,
        string title,
        CancellationToken cancellationToken)
    {
        Activity? activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        PrintManager? printManager = activity?.GetSystemService(Context.PrintService) as PrintManager;
        if (printManager is null)
        {
            return false;
        }

        byte[] bytes = Convert.FromBase64String(contentBase64);
        string safeName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "chummer-character.pdf" : fileName);
        if (!safeName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            safeName += ".pdf";
        }
        string pdfPath = Path.Combine(FileSystem.CacheDirectory, safeName);
        try
        {
            await System.IO.File.WriteAllBytesAsync(pdfPath, bytes, cancellationToken);
        }
        finally
        {
            Array.Clear(bytes);
        }

        printManager.Print(
            string.IsNullOrWhiteSpace(title) ? "Chummer character" : title,
            new PdfFilePrintDocumentAdapter(pdfPath, safeName),
            null);
        return true;
    }

    private sealed class PdfFilePrintDocumentAdapter : PrintDocumentAdapter
    {
        private readonly string _path;
        private readonly string _displayName;

        public PdfFilePrintDocumentAdapter(string path, string displayName)
        {
            _path = path;
            _displayName = displayName;
        }

        public override void OnLayout(
            PrintAttributes? oldAttributes,
            PrintAttributes? newAttributes,
            CancellationSignal? cancellationSignal,
            LayoutResultCallback? callback,
            Bundle? extras)
        {
            if (cancellationSignal?.IsCanceled == true)
            {
                callback?.OnLayoutCancelled();
                return;
            }

            PrintDocumentInfo info = new PrintDocumentInfo.Builder(_displayName)
                .SetContentType(PrintContentType.Document)
                .SetPageCount(PrintDocumentInfo.PageCountUnknown)
                .Build();
            callback?.OnLayoutFinished(info, changed: true);
        }

        public override void OnWrite(
            PageRange[]? pages,
            ParcelFileDescriptor? destination,
            CancellationSignal? cancellationSignal,
            WriteResultCallback? callback)
        {
            if (destination is null || cancellationSignal?.IsCanceled == true)
            {
                callback?.OnWriteCancelled();
                return;
            }

            try
            {
                using FileInputStream input = new(_path);
                using FileOutputStream output = new(destination.FileDescriptor);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
                try
                {
                    int read;
                    while ((read = input.Read(buffer)) > 0)
                    {
                        if (cancellationSignal?.IsCanceled == true)
                        {
                            callback?.OnWriteCancelled();
                            return;
                        }
                        output.Write(buffer, 0, read);
                    }
                    output.Flush();
                    callback?.OnWriteFinished([PageRange.AllPages!]);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(buffer);
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                }
            }
            catch (Exception ex)
            {
                callback?.OnWriteFailed(ex.Message);
            }
        }

        public override void OnFinish()
        {
            try
            {
                System.IO.File.Delete(_path);
            }
            catch (System.IO.IOException)
            {
                // The app cache is non-authoritative and Android may clean it later.
            }
            catch (UnauthorizedAccessException)
            {
                // Finishing a print job must not fail because cache cleanup was denied.
            }
            finally
            {
                base.OnFinish();
            }
        }
    }
}
