using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android.Native;

public abstract class NativePageBase : ContentPage
{
    private bool _subscribed;
    private bool _dialogVisible;
    private int _runningActionDepth;
    private int _appearanceRefreshActive;
    private CancellationTokenSource? _appearanceLifetime;
    private readonly NativeRefreshCoalescer _coordinatorRefresh = new();

    protected NativePageBase(RunnerSessionCoordinator coordinator)
    {
        Coordinator = coordinator;
        BackgroundColor = NativeTheme.Paper;
    }

    protected RunnerSessionCoordinator Coordinator { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Interlocked.Exchange(ref _appearanceRefreshActive, 1);
        _appearanceLifetime?.Cancel();
        _appearanceLifetime?.Dispose();
        _appearanceLifetime = new CancellationTokenSource();
        CancellationToken appearanceToken = _appearanceLifetime.Token;
        if (!_subscribed)
        {
            Coordinator.Changed += OnCoordinatorChanged;
            _subscribed = true;
        }

        try
        {
            await Coordinator.InitializeAsync();
            await PrepareForAppearanceRefreshAsync(appearanceToken);
            _coordinatorRefresh.DiscardPending();
            Refresh();
            Volatile.Write(ref _appearanceRefreshActive, 0);
            await ShowActiveDialogAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(750), appearanceToken);
            await NotifyPlayReviewSafeMomentAsync(
                signalMeaningfulSuccess: false,
                cancellationToken: appearanceToken);
        }
        catch (OperationCanceledException) when (appearanceToken.IsCancellationRequested)
        {
            // The page left before the deliberately deferred idle checkpoint.
        }
        catch (Exception ex)
        {
            _coordinatorRefresh.DiscardPending();
            Refresh();
            Volatile.Write(ref _appearanceRefreshActive, 0);
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
        finally
        {
            Volatile.Write(ref _appearanceRefreshActive, 0);
        }
    }

    protected override void OnDisappearing()
    {
        _appearanceLifetime?.Cancel();
        _appearanceLifetime?.Dispose();
        _appearanceLifetime = null;
        _coordinatorRefresh.DiscardPending();
        if (_subscribed)
        {
            Coordinator.Changed -= OnCoordinatorChanged;
            _subscribed = false;
        }

        base.OnDisappearing();
    }

    protected abstract void Refresh();

    protected virtual Task PrepareForAppearanceRefreshAsync(
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected async Task RunAsync(Func<Task> action)
    {
        PlayReviewMeaningfulState before = CapturePlayReviewMeaningfulState();
        Interlocked.Increment(ref _runningActionDepth);
        PlayReviewInteractionGuard.EnterAction();
        bool succeeded = false;
        try
        {
            await action();
            _coordinatorRefresh.DiscardPending();
            Refresh();
            await ShowActiveDialogAsync();
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            // Android pickers and page transitions use cancellation for a normal back action.
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
        finally
        {
            Interlocked.Decrement(ref _runningActionDepth);
            PlayReviewInteractionGuard.ExitAction();
        }

        if (succeeded)
        {
            await NotifyPlayReviewSafeMomentAsync(
                signalMeaningfulSuccess: before != CapturePlayReviewMeaningfulState());
        }
    }

    protected async Task RunWithConditionalRefreshAsync(Func<Task<bool>> action)
    {
        PlayReviewMeaningfulState before = CapturePlayReviewMeaningfulState();
        Interlocked.Increment(ref _runningActionDepth);
        PlayReviewInteractionGuard.EnterAction();
        bool succeeded = false;
        try
        {
            if (await action())
            {
                _coordinatorRefresh.DiscardPending();
                Refresh();
            }
            await ShowActiveDialogAsync();
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            // Android pickers and page transitions use cancellation for a normal back action.
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
        finally
        {
            Interlocked.Decrement(ref _runningActionDepth);
            PlayReviewInteractionGuard.ExitAction();
        }

        if (succeeded)
        {
            await NotifyPlayReviewSafeMomentAsync(
                signalMeaningfulSuccess: before != CapturePlayReviewMeaningfulState());
        }
    }

    protected async Task ShowActiveDialogAsync()
    {
        DesktopDialogState? dialog = Coordinator.State.ActiveDialog;
        if (_dialogVisible || dialog is null || Navigation.ModalStack.Count > 0)
        {
            return;
        }

        _dialogVisible = true;
        NativeDialogPage page = new(Coordinator, dialog);
        page.Closed += (_, _) => _dialogVisible = false;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private void OnCoordinatorChanged(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _runningActionDepth) > 0)
        {
            return;
        }

        // Initialize/prepare owns one explicit render. Dispatching Changed while
        // that pipeline is active can replace an already-proven button after the
        // page reports settled, losing the user's next tap. The explicit render
        // observes the newest coordinator state, so these requests are redundant.
        if (Volatile.Read(ref _appearanceRefreshActive) > 0)
        {
            return;
        }

        if (_coordinatorRefresh.Request())
        {
            DispatchCoordinatorRefresh();
        }
    }

    private void DispatchCoordinatorRefresh()
        => Dispatcher.Dispatch(() => _ = DrainCoordinatorRefreshAsync());

    private async Task DrainCoordinatorRefreshAsync()
    {
        try
        {
            if (!_subscribed
                || Volatile.Read(ref _runningActionDepth) > 0
                || Volatile.Read(ref _appearanceRefreshActive) > 0
                || !_coordinatorRefresh.TryTakePending())
            {
                return;
            }

            Refresh();
            await ShowActiveDialogAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (_subscribed)
            {
                await DisplayAlertAsync("Chummer", ex.Message, "OK");
            }
        }
        finally
        {
            bool mayRender = _subscribed
                && Volatile.Read(ref _runningActionDepth) == 0
                && Volatile.Read(ref _appearanceRefreshActive) == 0;
            if (_coordinatorRefresh.Complete(mayRender))
            {
                DispatchCoordinatorRefresh();
            }
        }
    }

    private async Task NotifyPlayReviewSafeMomentAsync(
        bool signalMeaningfulSuccess,
        CancellationToken cancellationToken = default)
    {
        IPlayReviewService? review = IPlatformApplication.Current?.Services
            .GetService<IPlayReviewService>();
        if (review is null)
        {
            return;
        }

        if (signalMeaningfulSuccess)
        {
            // Evidence comes from an actual workspace/revision transition, never from
            // page appearance or a merely non-throwing picker/navigation callback.
            review.SignalMeaningfulSuccess();
        }

        PlayReviewSafetyContext safety = PlayReviewSafety.Capture(Coordinator);
        if (!safety.IsSafe)
        {
            return;
        }

        await review.TryRequestAtSafeMomentAsync(safety, cancellationToken);
    }

    private PlayReviewMeaningfulState CapturePlayReviewMeaningfulState()
        => new(
            Coordinator.State.WorkspaceId?.Value,
            Coordinator.State.ContentRevision,
            Coordinator.State.SavedRevision);

    private readonly record struct PlayReviewMeaningfulState(
        string? WorkspaceId,
        long ContentRevision,
        long SavedRevision);
}
