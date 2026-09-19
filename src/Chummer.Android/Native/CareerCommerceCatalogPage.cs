namespace Chummer.Android.Native;

/// <summary>Asynchronous loading shared only by the six commerce catalog pages.</summary>
public abstract class CareerCommerceCatalogPage<TSnapshot>(RunnerSessionCoordinator coordinator)
    : NativePageBase(coordinator)
{
    private CancellationTokenSource? _catalogLoad;
    private long _catalogGeneration;

    protected abstract VerticalStackLayout CatalogBody { get; }
    protected abstract Task<TSnapshot> LoadCatalogAsync(CancellationToken token, Func<bool> isCurrentPage);
    protected abstract void RenderCatalog(TSnapshot snapshot);

    protected sealed override void Refresh() => _ = RefreshCatalogAsync();

    private async Task RefreshCatalogAsync()
    {
        long appearance = CaptureAppearanceGeneration();
        long generation = ++_catalogGeneration;
        var lifetime = new CancellationTokenSource();
        var previous = _catalogLoad;
        _catalogLoad = lifetime;
        previous?.Cancel();
        bool Current() => generation == _catalogGeneration
            && IsCurrentAppearanceGeneration(appearance) && !lifetime.IsCancellationRequested;
        try
        {
            CatalogBody.IsEnabled = false;
            CatalogBody.Clear();
            CatalogBody.Add(new ActivityIndicator
            {
                AutomationId = "career-commerce-loading", IsRunning = true, HeightRequest = 24
            });
            CatalogBody.Add(NativeTheme.Body(WizardStrings.Get("Career.Loading",
                "Checking the exact workspace and typed Career authorities…"), NativeTheme.Muted));
            TSnapshot snapshot = await LoadCatalogAsync(lifetime.Token, Current);
            if (!Current()) return;
            CatalogBody.Clear();
            RenderCatalog(snapshot);
            CatalogBody.IsEnabled = true;
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!Current()) return;
            CatalogBody.Clear();
            CatalogBody.Add(NativeTheme.Body(WizardStrings.Get("Career.Blocker.WorkspaceChanged",
                "The runner changed while Career authorities were loading. Retry from the current revision."),
                NativeTheme.Danger));
            // No old controls or raw document/exception text survive failure.
        }
        finally
        {
            if (ReferenceEquals(_catalogLoad, lifetime)) _catalogLoad = null;
            lifetime.Dispose();
        }
    }

    protected override void OnDisappearing()
    {
        ++_catalogGeneration;
        _catalogLoad?.Cancel();
        CatalogBody.IsEnabled = false;
        base.OnDisappearing();
    }
}
