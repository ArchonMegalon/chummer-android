namespace Chummer.Android.Native;

/// <summary>Coalesces allocation gestures; persistence still uses the issued Core preview and confirm.</summary>
public abstract class CreationAutosavePage : NativePageBase
{
    protected readonly record struct SaveResult(bool Saved, string? Error = null, bool RequiresReopen = false);
    private CancellationTokenSource? _delay;
    private Task<bool>? _saving;
    private bool _dirty;
    private bool _requiresReopen;
    private bool _dragging;
    private bool _navigating;
    private bool _navigationRefreshPending;
    private View? _editor;
    private Label? _status;
    private Button? _next;
    protected bool IsSaving => _saving is { IsCompleted: false };
    protected bool CanEditAllocation => !IsSaving && !_requiresReopen && !_navigating;
    // Re-entry may refresh a saved page, never discard a pending edit or turn
    // an uncertain write into a fresh editing session.
    protected bool CanReloadSavedAllocation => !_dirty && CanEditAllocation;

    protected void RequireReopenForContinuation(string message)
    {
        _requiresReopen = true;
        if (_editor is not null) _editor.IsEnabled = false;
        if (_next is not null) _next.IsEnabled = false;
        ShowStatus(message, true);
    }

    protected CreationAutosavePage(RunnerSessionCoordinator coordinator) : base(coordinator) { }
    protected abstract Task<SaveResult> SaveDraftAsync();
    protected abstract Task OpenNextStepAsync(long appearance);
    protected abstract bool HasSavedDraft { get; }

    protected void SetAutosaveContent(View editor, string prefix)
    {
        _editor = editor;
        _status = NativeTheme.Body(PhoneStrings.Get("CreationAutosaveHelp", "Changes save automatically."), NativeTheme.Muted);
        _status.AutomationId = prefix + "-save-status";
        _next = NativeTheme.PrimaryButton(PhoneStrings.Get("CreationAutosaveContinue", "Continue"));
        _next.AutomationId = prefix + "-continue";
        _next.Clicked += async (_, _) =>
        {
            if (_navigating) return;
            long appearance = CaptureAppearanceGeneration();
            _navigating = true;
            _next.IsEnabled = false;
            try
            {
                if (await FlushAutosaveAsync(ensureSaved: true) && IsCurrentAppearanceGeneration(appearance))
                {
                    _next.Text = CreationFlowStrings.Get("Qualities.Review.OpeningNext", "Opening the next step…");
                    _next.IsEnabled = false;
                    if (_editor is not null) _editor.IsEnabled = false;
                    await OpenNextStepAsync(appearance);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (IsCurrentAppearanceGeneration(appearance))
                    ShowStatus(PhoneStrings.Get("CreationAutosaveNextFailed", "Saved. Could not open the next step. Try Continue again."), true);
            }
            finally { FinishNavigation(appearance); }
        };
        var footer = new VerticalStackLayout { Padding = new Thickness(20, 6, 20, 12), Spacing = 4 };
        footer.Add(_status);
        footer.Add(_next);
        var layout = new Grid { RowDefinitions = new() { new(GridLength.Star), new(GridLength.Auto) } };
        layout.Add(editor);
        layout.Add(footer, 0, 1);
        Content = layout;
    }

    protected void DraftChanged()
    {
        if (!CanEditAllocation) return;
        _dirty = true;
        ShowStatus(PhoneStrings.Get("CreationAutosavePending", "Not saved yet…"));
        ScheduleSave();
    }

    protected void BeginAllocationGesture()
    {
        _dragging = true;
        _delay?.Cancel();
    }

    protected void EndAllocationGesture()
    {
        _dragging = false;
        if (_dirty) ScheduleSave();
    }

    private void ScheduleSave() => _ = ScheduleSaveAsync();

    private async Task ScheduleSaveAsync()
    {
        _delay?.Cancel();
        if (_dragging || !_dirty || _requiresReopen) return;
        using var delay = new CancellationTokenSource();
        _delay = delay;
        try
        {
            await Task.Delay(800, delay.Token);
            if (!delay.IsCancellationRequested) await FlushAutosaveAsync();
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_delay, delay)) _delay = null; }
    }

    internal Task<bool> FlushAutosaveAsync(bool ensureSaved = false)
    {
        _delay?.Cancel();
        if (IsSaving) return _saving!;
        if (_requiresReopen) return Task.FromResult(false);
        if (!_dirty && (!ensureSaved || HasSavedDraft)) return Task.FromResult(true);
        // Publish the single-flight task before entering any synchronous callback.
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _saving = completion.Task;
        _ = SaveAsync(completion);
        return completion.Task;
    }

    private async Task SaveAsync(TaskCompletionSource<bool> completion)
    {
        long appearance = CaptureAppearanceGeneration();
        if (_editor is not null) _editor.IsEnabled = false;
        if (_next is not null) _next.IsEnabled = false;
        ShowStatus(PhoneStrings.Get("CreationAutosaveSaving", "Saving…"));
        SaveResult result;
        try { result = await SaveDraftAsync(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // An unexpected failure might follow a durable write. Never replay it.
            result = new(false, RequiresReopen: true);
        }
        _requiresReopen = result.RequiresReopen;
        _dirty = !result.Saved;
        ShowStatus(result.RequiresReopen
            ? PhoneStrings.Get("CreationAutosaveReopen", "Save could not be verified. Reopen this runner before editing again.")
            : result.Saved ? PhoneStrings.Get("CreationAutosaveSaved", "Saved")
            : !string.IsNullOrWhiteSpace(result.Error) ? result.Error
            : PhoneStrings.Get("CreationAutosaveFailed", "Not saved. Check your choices and try again."), !result.Saved);
        // Complete persistence before refreshing. A retired appearance never opens a page.
        _saving = null;
        if (_editor is not null) _editor.IsEnabled = !_requiresReopen;
        if (_next is not null) _next.IsEnabled = !_requiresReopen;
        try
        {
            if (result.Saved && IsCurrentAppearanceGeneration(appearance)) Refresh();
        }
        finally { completion.TrySetResult(result.Saved); }
    }

    private void ShowStatus(string message, bool error = false)
    {
        if (_status is null) return;
        _status.Text = message;
        _status.TextColor = error ? NativeTheme.Danger : NativeTheme.Muted;
    }

    protected override bool TryDeferCoordinatorRefresh()
    {
        // A next-step recovery can commit a new revision before navigation.
        // Do not render the old editor as invalid in that short interval.
        // If navigation stays here (cancel/error/owner change), refresh once
        // after the operation; admission checks still run on every action.
        if (_navigating) _navigationRefreshPending = true;
        return IsSaving || _navigating;
    }

    private void FinishNavigation(long appearance)
    {
        _navigating = false;
        bool refresh = _navigationRefreshPending;
        _navigationRefreshPending = false;
        if (_editor is not null) _editor.IsEnabled = !_requiresReopen;
        if (_next is not null)
        {
            _next.Text = PhoneStrings.Get("CreationAutosaveContinue", "Continue");
            _next.IsEnabled = !_requiresReopen;
        }
        if (refresh && IsCurrentAppearanceGeneration(appearance)) Refresh();
    }

    protected override void OnDisappearing()
    {
        // Flush the original, owner-bound edit on departure too; never cancel an admitted write.
        _ = FlushAutosaveAsync();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_requiresReopen || (!_dirty && !IsSaving)) return base.OnBackButtonPressed();
        _ = LeaveAfterSaveAsync();
        return true;
    }

    private async Task LeaveAfterSaveAsync()
    {
        if (_navigating) return;
        _navigating = true;
        long appearance = CaptureAppearanceGeneration();
        try
        {
            if (await FlushAutosaveAsync() && IsCurrentAppearanceGeneration(appearance)
                && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this))
                await Navigation.PopAsync();
        }
        finally { FinishNavigation(appearance); }
    }
}
