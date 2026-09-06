using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android.Native;

public abstract class NativePageBase : ContentPage
{
    private bool _subscribed;
    private bool _dialogVisible;
    private int _appearanceRefreshActive;
    private long _appearanceGeneration;
    private CancellationTokenSource? _appearanceLifetime;
    private readonly NativeRefreshCoalescer _coordinatorRefresh = new();
    private readonly NativePageActionGate _actionGate = new();

    protected NativePageBase(RunnerSessionCoordinator coordinator)
    {
        Coordinator = coordinator;
        BackgroundColor = NativeTheme.Paper;
    }

    protected RunnerSessionCoordinator Coordinator { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        long appearanceGeneration = Interlocked.Increment(ref _appearanceGeneration);
        Interlocked.Exchange(ref _appearanceRefreshActive, 1);
        CancellationTokenSource appearanceLifetime = new();
        CancellationTokenSource? previousAppearance =
            Interlocked.Exchange(ref _appearanceLifetime, appearanceLifetime);
        previousAppearance?.Cancel();
        CancellationToken appearanceToken = appearanceLifetime.Token;
        if (!_subscribed)
        {
            Coordinator.Changed += OnCoordinatorChanged;
            _subscribed = true;
        }

        try
        {
            await Coordinator.InitializeAsync();
            ThrowIfAppearanceIsStale(appearanceGeneration, appearanceToken);
            await PrepareForAppearanceRefreshAsync(appearanceToken);
            ThrowIfAppearanceIsStale(appearanceGeneration, appearanceToken);
            _coordinatorRefresh.DiscardPending();
            Refresh();
            ClearAppearanceRefreshIfCurrent(appearanceGeneration);
            ThrowIfAppearanceIsStale(appearanceGeneration, appearanceToken);
            await ShowActiveDialogAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(750), appearanceToken);
            ThrowIfAppearanceIsStale(appearanceGeneration, appearanceToken);
            await NotifyPlayReviewSafeMomentAsync(
                signalMeaningfulSuccess: false,
                cancellationToken: appearanceToken);
        }
        catch (OperationCanceledException) when (
            appearanceToken.IsCancellationRequested
            || !IsCurrentAppearance(appearanceGeneration, appearanceLifetime))
        {
            // The page left before the deliberately deferred idle checkpoint.
        }
        catch (Exception ex)
        {
            if (!IsCurrentAppearance(appearanceGeneration, appearanceLifetime))
            {
                return;
            }

            _coordinatorRefresh.DiscardPending();
            Refresh();
            ClearAppearanceRefreshIfCurrent(appearanceGeneration);
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
        finally
        {
            ClearAppearanceRefreshIfCurrent(appearanceGeneration);
            Interlocked.CompareExchange(
                ref _appearanceLifetime,
                null,
                appearanceLifetime);
            appearanceLifetime.Dispose();
        }
    }

    protected override void OnDisappearing()
    {
        Interlocked.Increment(ref _appearanceGeneration);
        CancellationTokenSource? appearanceLifetime =
            Interlocked.Exchange(ref _appearanceLifetime, null);
        appearanceLifetime?.Cancel();
        Volatile.Write(ref _appearanceRefreshActive, 0);
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

    /// <summary>
    /// Allows a page to coalesce a destructive coordinator render while a native
    /// gesture owns controls from the current visual tree.
    /// </summary>
    protected virtual bool TryDeferCoordinatorRefresh()
        => false;

    private bool IsCurrentAppearance(
        long appearanceGeneration,
        CancellationTokenSource appearanceLifetime)
        => _subscribed
            && Volatile.Read(ref _appearanceGeneration) == appearanceGeneration
            && ReferenceEquals(
                Interlocked.CompareExchange(
                    ref _appearanceLifetime,
                    null,
                    null),
                appearanceLifetime);

    private void ThrowIfAppearanceIsStale(
        long appearanceGeneration,
        CancellationToken appearanceToken)
    {
        appearanceToken.ThrowIfCancellationRequested();
        if (!_subscribed
            || Volatile.Read(ref _appearanceGeneration) != appearanceGeneration)
        {
            throw new OperationCanceledException(appearanceToken);
        }
    }

    private void ClearAppearanceRefreshIfCurrent(long appearanceGeneration)
    {
        if (Volatile.Read(ref _appearanceGeneration) == appearanceGeneration)
        {
            Volatile.Write(ref _appearanceRefreshActive, 0);
        }
    }

    protected async Task RunAsync(Func<Task> action)
    {
        if (!_actionGate.TryClaim())
        {
            return;
        }

        PlayReviewMeaningfulState before = default;
        PlayReviewInteractionGuard.EnterAction();
        bool succeeded = false;
        try
        {
            before = CapturePlayReviewMeaningfulState();
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
            PlayReviewInteractionGuard.ExitAction();
            _actionGate.Release();
        }

        if (succeeded)
        {
            await NotifyPlayReviewSafeMomentAsync(
                signalMeaningfulSuccess: before != CapturePlayReviewMeaningfulState());
        }
    }

    protected async Task RunWithConditionalRefreshAsync(Func<Task<bool>> action)
    {
        if (!_actionGate.TryClaim())
        {
            return;
        }

        PlayReviewMeaningfulState before = default;
        PlayReviewInteractionGuard.EnterAction();
        bool succeeded = false;
        try
        {
            before = CapturePlayReviewMeaningfulState();
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
            PlayReviewInteractionGuard.ExitAction();
            _actionGate.Release();
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
        if (_actionGate.IsClaimed)
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

        if (TryDeferCoordinatorRefresh())
        {
            return;
        }

        if (_coordinatorRefresh.Request())
        {
            DispatchCoordinatorRefresh();
        }
    }

    private void DispatchCoordinatorRefresh()
    {
        try
        {
            if (!Dispatcher.Dispatch(() => _ = DrainCoordinatorRefreshAsync()))
            {
                // The page may have left its dispatcher between the Changed event and this
                // post. Release scheduling ownership so a later appearance cannot inherit a
                // permanently scheduled refresh that will never execute.
                _coordinatorRefresh.Complete(allowReschedule: false);
            }
        }
        catch
        {
            _coordinatorRefresh.Complete(allowReschedule: false);
            throw;
        }
    }

    private async Task DrainCoordinatorRefreshAsync()
    {
        try
        {
            if (!_subscribed
                || _actionGate.IsClaimed
                || Volatile.Read(ref _appearanceRefreshActive) > 0)
            {
                return;
            }

            if (TryDeferCoordinatorRefresh())
            {
                _coordinatorRefresh.DiscardPending();
                return;
            }
            if (!_coordinatorRefresh.TryTakePending())
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
                && !_actionGate.IsClaimed
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
