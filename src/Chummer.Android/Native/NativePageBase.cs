using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android.Native;

public abstract class NativePageBase : ContentPage
{
    private bool _subscribed;
    private bool _dialogVisible;
    private int _runningActionDepth;
    private CancellationTokenSource? _appearanceLifetime;

    protected NativePageBase(RunnerSessionCoordinator coordinator)
    {
        Coordinator = coordinator;
        BackgroundColor = NativeTheme.Paper;
    }

    protected RunnerSessionCoordinator Coordinator { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
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
            Refresh();
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
            Refresh();
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
    }

    protected override void OnDisappearing()
    {
        _appearanceLifetime?.Cancel();
        _appearanceLifetime?.Dispose();
        _appearanceLifetime = null;
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

        Dispatcher.Dispatch(async () =>
        {
            Refresh();
            await ShowActiveDialogAsync();
        });
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
