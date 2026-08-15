using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public abstract class NativePageBase : ContentPage
{
    private bool _subscribed;
    private bool _dialogVisible;
    private int _runningActionDepth;

    protected NativePageBase(RunnerSessionCoordinator coordinator)
    {
        Coordinator = coordinator;
        BackgroundColor = NativeTheme.Paper;
    }

    protected RunnerSessionCoordinator Coordinator { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_subscribed)
        {
            Coordinator.Changed += OnCoordinatorChanged;
            _subscribed = true;
        }

        try
        {
            await Coordinator.InitializeAsync();
            Refresh();
            await ShowActiveDialogAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
    }

    protected override void OnDisappearing()
    {
        if (_subscribed)
        {
            Coordinator.Changed -= OnCoordinatorChanged;
            _subscribed = false;
        }

        base.OnDisappearing();
    }

    protected abstract void Refresh();

    protected async Task RunAsync(Func<Task> action)
    {
        Interlocked.Increment(ref _runningActionDepth);
        try
        {
            await action();
            Refresh();
            await ShowActiveDialogAsync();
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
        }
    }

    protected async Task RunWithConditionalRefreshAsync(Func<Task<bool>> action)
    {
        Interlocked.Increment(ref _runningActionDepth);
        try
        {
            if (await action())
            {
                Refresh();
            }
            await ShowActiveDialogAsync();
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
}
