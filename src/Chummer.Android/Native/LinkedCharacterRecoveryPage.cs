namespace Chummer.Android.Native;

/// <summary>Read-only runner recovery, independent of ordinary editor initialization.</summary>
public sealed class LinkedCharacterRecoveryPage : ContentPage
{
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20), Spacing = 12 };
    private CancellationTokenSource? _appearance;
    private IReadOnlyList<AndroidLinkedCharacterIntentRecord> _records = [];
    private string? _status;
    private bool _loading;
    private bool _reloadRequested;
    private int _offset;
    private const int PageSize = 25;

    public LinkedCharacterRecoveryPage(RunnerSessionCoordinator coordinator)
    {
        _coordinator = coordinator;
        Title = PhoneStrings.Get("LinkedRecoveryTitle", "Linked-runner recovery");
        AutomationId = "linked-runner-recovery-page";
        Content = new ScrollView { Content = _body };
        Render();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _appearance?.Cancel();
        _appearance?.Dispose();
        _appearance = new CancellationTokenSource();
        _coordinator.Changed -= OnCoordinatorChanged;
        _coordinator.Changed += OnCoordinatorChanged;
        await ReadAsync(null);
    }

    protected override void OnDisappearing()
    {
        _coordinator.Changed -= OnCoordinatorChanged;
        _appearance?.Cancel();
        _appearance?.Dispose();
        _appearance = null;
        _records = [];
        _reloadRequested = false;
        base.OnDisappearing();
    }

    private void OnCoordinatorChanged(object? sender, EventArgs args)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_appearance is null) return;
            _records = [];
            _offset = 0;
            _status = PhoneStrings.Get("LinkedRecoveryReload", "Context changed. Refresh recovery history.");
            // Do not leave an old owner's history visible through account changes.
            _appearance?.Cancel();
            _appearance?.Dispose();
            _appearance = new CancellationTokenSource();
            Render();
        });

    private async Task ReadAsync(Guid? check)
    {
        if (_appearance is null) return;
        // A returning page must not permanently miss its appearance load because
        // the previous generation is still joining canceled file I/O.
        if (_loading) { _reloadRequested = true; return; }
        var appearance = _appearance;
        var token = appearance.Token;
        _loading = true;
        _status = null;
        _records = [];
        Render();
        try
        {
            string? status = null;
            if (check is { } operation)
            {
                bool observed = await _coordinator.CheckLinkedCharacterIntentAsync(operation, token);
                if (!observed) status = PhoneStrings.Get("LinkedRecoveryUnresolved",
                    "Still unresolved. No change was replayed and no file was removed.");
            }
            var history = await _coordinator.ReadLinkedCharacterHistoryAsync(token);
            if (token.IsCancellationRequested || _appearance != appearance) return;
            _status = status;
            _records = history;
            _offset = Math.Min(_offset, Math.Max(0, ((_records.Count - 1) / PageSize) * PageSize));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_appearance == appearance)
                _status = PhoneStrings.Get("LinkedRecoveryUnavailable",
                    "Recovery is unavailable or damaged. No link was changed; keep local recovery files.");
        }
        finally
        {
            _loading = false;
            if (_appearance is not null)
            {
                Render();
                if (_reloadRequested)
                {
                    _reloadRequested = false;
                    await ReadAsync(null);
                }
            }
        }
    }

    private void Render()
    {
        _body.Clear();
        _body.Add(NativeTheme.Title(Title));
        _body.Add(NativeTheme.Body(PhoneStrings.Get("LinkedRecoveryBoundary",
            "These are local intents and state observations, not Core transaction receipts. Unresolved changes must not be retried.")));
        if (_status is not null) _body.Add(NativeTheme.Body(_status));
        var refresh = NativeTheme.SecondaryButton(PhoneStrings.Get("Refresh", "Refresh"));
        refresh.AutomationId = "linked-recovery-refresh";
        refresh.IsEnabled = !_loading;
        refresh.Clicked += async (_, _) => await ReadAsync(null);
        _body.Add(refresh);
        foreach (var entry in _records.Skip(_offset).Take(PageSize))
        {
            var card = new VerticalStackLayout { Spacing = 8 };
            card.Add(NativeTheme.Body($"{entry.Intent.WorkspaceId} · {entry.Intent.Target.Kind} · {entry.Intent.Target.ItemId}"));
            card.Add(NativeTheme.Body($"{entry.Intent.OperationId:D} · R{entry.Intent.ContentRevision}/S{entry.Intent.SavedRevision}"));
            card.Add(NativeTheme.Body(entry.EffectObserved
                ? PhoneStrings.Get("LinkedRecoveryObserved", "Requested state observed at the next revision; no historical commit attribution.")
                : entry.NotDispatched
                    ? PhoneStrings.Get("LinkedRecoveryNotDispatched", "Stopped before dispatch. The runner was not changed by this attempt.")
                    : PhoneStrings.Get("LinkedRecoveryPending", "Outcome unknown — retain files and do not retry.")));
            if (!entry.EffectObserved && !entry.NotDispatched)
            {
                var check = NativeTheme.SecondaryButton(PhoneStrings.Get("LinkedRecoveryCheck", "Check current saved state"));
                check.AutomationId = $"linked-recovery-check-{entry.Intent.OperationId:N}";
                check.IsEnabled = !_loading;
                check.Clicked += async (_, _) => await ReadAsync(entry.Intent.OperationId);
                card.Add(check);
            }
            _body.Add(NativeTheme.Card(card));
        }
        if (_offset > 0) AddPageButton("LinkedRecoveryPrevious", "Previous", -PageSize);
        if (_offset + PageSize < _records.Count) AddPageButton("LinkedRecoveryNext", "Next", PageSize);
    }

    private void AddPageButton(string key, string fallback, int delta)
    {
        var button = NativeTheme.SecondaryButton(PhoneStrings.Get(key, fallback));
        button.IsEnabled = !_loading;
        button.Clicked += (_, _) => { _offset += delta; Render(); };
        _body.Add(button);
    }
}
