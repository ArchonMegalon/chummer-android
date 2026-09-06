using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android.Native;

public abstract class NativePageBase : ContentPage
{
    private int _subscribed;
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
        _coordinatorRefresh.AbandonThrough(appearanceGeneration - 1);
        Interlocked.Exchange(ref _appearanceRefreshActive, 1);
        CancellationTokenSource appearanceLifetime = new();
        CancellationTokenSource? previousAppearance =
            Interlocked.Exchange(ref _appearanceLifetime, appearanceLifetime);
        previousAppearance?.Cancel();
        CancellationToken appearanceToken = appearanceLifetime.Token;
        if (Interlocked.CompareExchange(ref _subscribed, 1, 0) == 0)
        {
            Coordinator.Changed += OnCoordinatorChanged;
        }

        try
        {
            await Coordinator.InitializeAsync();
            ThrowIfAppearanceIsStale(appearanceGeneration, appearanceToken);
            await PrepareForAppearanceRefreshAsync(appearanceToken);
            ThrowIfAppearanceIsStale(appearanceGeneration, appearanceToken);
            _coordinatorRefresh.DiscardPendingThrough(appearanceGeneration);
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

            _coordinatorRefresh.DiscardPendingThrough(appearanceGeneration);
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
        long departedGeneration = Interlocked.Increment(ref _appearanceGeneration) - 1;
        if (Interlocked.Exchange(ref _subscribed, 0) != 0)
        {
            Coordinator.Changed -= OnCoordinatorChanged;
        }
        CancellationTokenSource? appearanceLifetime =
            Interlocked.Exchange(ref _appearanceLifetime, null);
        appearanceLifetime?.Cancel();
        Volatile.Write(ref _appearanceRefreshActive, 0);
        _coordinatorRefresh.AbandonThrough(departedGeneration);

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
        => Volatile.Read(ref _subscribed) != 0
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
        if (Volatile.Read(ref _subscribed) == 0
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
            TryScheduleCoordinatorRefresh(appearanceGeneration);
        }
    }

    protected async Task RunAsync(Func<Task> action)
    {
        if (!_actionGate.TryClaim())
        {
            return;
        }

        long actionGeneration = Volatile.Read(ref _appearanceGeneration);
        PlayReviewMeaningfulState before = default;
        PlayReviewInteractionGuard.EnterAction();
        bool succeeded = false;
        try
        {
            before = CapturePlayReviewMeaningfulState();
            await action();
            if (IsCurrentAppearanceGeneration(actionGeneration))
            {
                _coordinatorRefresh.DiscardPendingThrough(actionGeneration);
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
            TryScheduleCoordinatorRefresh(Volatile.Read(ref _appearanceGeneration));
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

        long actionGeneration = Volatile.Read(ref _appearanceGeneration);
        PlayReviewMeaningfulState before = default;
        PlayReviewInteractionGuard.EnterAction();
        bool succeeded = false;
        try
        {
            before = CapturePlayReviewMeaningfulState();
            if (await action() && IsCurrentAppearanceGeneration(actionGeneration))
            {
                _coordinatorRefresh.DiscardPendingThrough(actionGeneration);
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
            TryScheduleCoordinatorRefresh(Volatile.Read(ref _appearanceGeneration));
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
        if (Volatile.Read(ref _subscribed) == 0)
        {
            return;
        }

        long appearanceGeneration = Volatile.Read(ref _appearanceGeneration);
        if (appearanceGeneration <= 0)
        {
            return;
        }

        // Record the change before observing either suppression owner. An explicit render
        // discards changes that preceded its state read; a change racing that read remains
        // pending and receives one trailing dispatcher pass after the owner releases.
        _coordinatorRefresh.MarkPending(appearanceGeneration);
        TryScheduleCoordinatorRefresh(appearanceGeneration);
    }

    private bool IsCurrentAppearanceGeneration(long appearanceGeneration)
        => appearanceGeneration > 0
           && Volatile.Read(ref _subscribed) != 0
           && Volatile.Read(ref _appearanceGeneration) == appearanceGeneration;

    private void TryScheduleCoordinatorRefresh(long appearanceGeneration)
    {
        if (!IsCurrentAppearanceGeneration(appearanceGeneration)
            || _actionGate.IsClaimed
            || Volatile.Read(ref _appearanceRefreshActive) > 0)
        {
            return;
        }

        // Guard-release endpoints call this helper even when no Changed event occurred.
        // Never dirty a page-specific gesture lease unless there is actual pending work.
        if (!_coordinatorRefresh.TryGetPendingRequest(
                appearanceGeneration,
                out long pendingRequestId))
        {
            return;
        }

        if (TryDeferCoordinatorRefresh())
        {
            _coordinatorRefresh.DiscardPending(
                appearanceGeneration,
                pendingRequestId);
            return;
        }

        if (_coordinatorRefresh.TrySchedulePending(appearanceGeneration))
        {
            DispatchCoordinatorRefresh(appearanceGeneration);
        }
    }

    private void DispatchCoordinatorRefresh(long appearanceGeneration)
    {
        try
        {
            if (!TryDispatchCoordinatorRefresh(
                    () => _ = DrainCoordinatorRefreshAsync(appearanceGeneration)))
            {
                // The page may have left its dispatcher between the Changed event and this
                // post. Release scheduling ownership so a later appearance cannot inherit a
                // permanently scheduled refresh that will never execute.
                _coordinatorRefresh.ReleaseSchedule(appearanceGeneration);
            }
        }
        catch
        {
            _coordinatorRefresh.ReleaseSchedule(appearanceGeneration);
            throw;
        }
    }

    protected virtual bool TryDispatchCoordinatorRefresh(Action action)
        => Dispatcher.Dispatch(action);

    private async Task DrainCoordinatorRefreshAsync(long appearanceGeneration)
    {
        try
        {
            if (!IsCurrentAppearanceGeneration(appearanceGeneration)
                || _actionGate.IsClaimed
                || Volatile.Read(ref _appearanceRefreshActive) > 0)
            {
                return;
            }

            if (!_coordinatorRefresh.TryGetPendingRequest(
                    appearanceGeneration,
                    out long pendingRequestId))
            {
                return;
            }

            if (TryDeferCoordinatorRefresh())
            {
                _coordinatorRefresh.DiscardPending(
                    appearanceGeneration,
                    pendingRequestId);
                return;
            }
            if (!_coordinatorRefresh.TryTakePending(appearanceGeneration))
            {
                return;
            }

            Refresh();
            if (IsCurrentAppearanceGeneration(appearanceGeneration))
            {
                await ShowActiveDialogAsync();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (IsCurrentAppearanceGeneration(appearanceGeneration))
            {
                await DisplayAlertAsync("Chummer", ex.Message, "OK");
            }
        }
        finally
        {
            // Release this exact generation before checking guards again. If an action or
            // appearance endpoint raced the rejected drain while it still owned scheduling,
            // this post-release check observes the retained dirty state and schedules it.
            _coordinatorRefresh.ReleaseSchedule(appearanceGeneration);
            TryScheduleCoordinatorRefresh(Volatile.Read(ref _appearanceGeneration));
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
