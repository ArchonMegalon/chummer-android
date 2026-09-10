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
    public Task<bool> OpenUriAsync(Uri uri) => Launcher.Default.OpenAsync(uri);

    public async Task<AndroidUpdateCheckResult> CheckForUpdatesAsync()
    {
        Activity? activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is Chummer.Android.MainActivity mainActivity)
        {
            AndroidUpdateCheckResult result = await mainActivity.CheckForPlayUpdateAsync(userInitiated: true);
            if (result != AndroidUpdateCheckResult.Unavailable)
            {
                return result;
            }

            return mainActivity.IsGooglePlayManaged
                ? AndroidUpdateCheckResult.Unavailable
                : AndroidUpdateCheckResult.PlayManagedRequired;
        }

        return AndroidUpdateCheckResult.Unavailable;
    }

    public Task ShareTextAsync(string text)
        => Share.Default.RequestAsync(new ShareTextRequest(text, "Share Chummer"));

    public Task<bool> PrintPdfAsync(
        string fileName,
        string contentBase64,
        string title,
        CancellationToken cancellationToken)
        => PrintPdfAsync(fileName, contentBase64, title, static () => true, cancellationToken);

    public async Task<bool> PrintPdfAsync(string fileName, string contentBase64, string title,
        Func<bool> isOriginalContextCurrent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isOriginalContextCurrent);
        cancellationToken.ThrowIfCancellationRequested();
        if (!isOriginalContextCurrent()) return false;
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
        // Each print owns one private cache file. Reusing a display filename can
        // make a queued adapter print another account's subsequently written PDF.
        string pdfPath = Path.Combine(FileSystem.CacheDirectory, "chummer-print-" + Guid.NewGuid().ToString("N") + ".pdf");
        bool handedOff = false;
        try
        {
            await System.IO.File.WriteAllBytesAsync(pdfPath, bytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isOriginalContextCurrent()) return false;
            handedOff = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!isOriginalContextCurrent()) return false;
                return printManager.Print(
                    string.IsNullOrWhiteSpace(title) ? "Chummer character" : title,
                    new PdfFilePrintDocumentAdapter(pdfPath, safeName, isOriginalContextCurrent), null) is not null;
            });
            return handedOff;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (!handedOff) RemovePrintCache(pdfPath);
        }
    }

    private static void RemovePrintCache(string path)
    {
        try { System.IO.File.Delete(path); }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class PdfFilePrintDocumentAdapter : PrintDocumentAdapter
    {
        private readonly string _path;
        private readonly string _displayName;
        private readonly Func<bool> _isOriginalContextCurrent;

        public PdfFilePrintDocumentAdapter(string path, string displayName, Func<bool> isOriginalContextCurrent)
        {
            _path = path;
            _displayName = displayName;
            _isOriginalContextCurrent = isOriginalContextCurrent;
        }

        public override void OnLayout(
            PrintAttributes? oldAttributes,
            PrintAttributes? newAttributes,
            CancellationSignal? cancellationSignal,
            LayoutResultCallback? callback,
            Bundle? extras)
        {
            if (cancellationSignal?.IsCanceled == true || !_isOriginalContextCurrent())
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
            if (destination is null || cancellationSignal?.IsCanceled == true || !_isOriginalContextCurrent())
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
                        if (cancellationSignal?.IsCanceled == true || !_isOriginalContextCurrent())
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
