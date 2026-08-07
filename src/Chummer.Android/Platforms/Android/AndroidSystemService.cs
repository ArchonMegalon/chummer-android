using Android.App;
using Android.Content;
using Android.Print;
using Android.Webkit;
using Android.OS;
using Java.IO;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Chummer.Android.Platform;

public sealed class AndroidSystemService : IAndroidSystemService
{
    private const string PackageId = "com.myexternalbrain.chummer";

    public Task OpenUriAsync(Uri uri) => Launcher.Default.OpenAsync(uri);

    public async Task OpenStoreListingAsync()
    {
        Uri marketUri = new($"market://details?id={PackageId}");
        if (!await Launcher.Default.TryOpenAsync(marketUri))
        {
            await Launcher.Default.OpenAsync(new Uri($"https://play.google.com/store/apps/details?id={PackageId}"));
        }
    }

    public Task ShareTextAsync(string text)
        => Share.Default.RequestAsync(new ShareTextRequest(text, "Share Chummer"));

    public Task<bool> PrintCurrentViewAsync(string jobName)
    {
        Activity? activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        MainPage? page = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page as MainPage;
        global::Android.Webkit.WebView? nativeWebView = page?.WebView.Handler?.PlatformView as global::Android.Webkit.WebView;
        PrintManager? printManager = activity?.GetSystemService(Context.PrintService) as PrintManager;
        if (nativeWebView is null || printManager is null)
        {
            return Task.FromResult(false);
        }

        PrintDocumentAdapter adapter = nativeWebView.CreatePrintDocumentAdapter(jobName);
        printManager.Print(jobName, adapter, null);
        return Task.FromResult(true);
    }

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
                byte[] buffer = new byte[32 * 1024];
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
