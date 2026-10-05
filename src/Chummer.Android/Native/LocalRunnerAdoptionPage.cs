namespace Chummer.Android.Native;

internal sealed class LocalRunnerAdoptionPage : NativePageBase
{
    private readonly string _runnerRoute;
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 14 };
    private IReadOnlyList<NativeLocalRunnerCandidate> _runners = [];
    private bool _busy;
    private bool _loaded;
    private string? _status;

    internal LocalRunnerAdoptionPage(RunnerSessionCoordinator coordinator, string runnerRoute) : base(coordinator)
    {
        _runnerRoute = runnerRoute;
        Title = PhoneStrings.Get("LocalRunnerAdoptionTitle", "Runners on this device");
        Content = new ScrollView { Content = _body };
    }

    protected override Task PrepareForAppearanceRefreshAsync(CancellationToken ct) => LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        _loaded = false;
        _runners = [];
        _busy = true;
        Refresh();
        try
        {
            _runners = await Coordinator.ListLocalRunnerCandidatesAsync(ct);
            ct.ThrowIfCancellationRequested();
            _loaded = true;
        }
        finally { _busy = false; }
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Title(PhoneStrings.Get("LocalRunnerAdoptionTitle", "Runners on this device")));
        if (_busy)
        {
            _body.Add(new ActivityIndicator { AutomationId = "local-runner-adoption-progress", IsRunning = true, Color = NativeTheme.Text });
            _body.Add(NativeTheme.Body(PhoneStrings.Get("LocalRunnerAdoptionWorking", "Keeping your runner and book together…")));
            return;
        }
        if (Coordinator.HasPendingLocalRunnerAdoption)
        {
            _runners = [];
            _body.Add(NativeTheme.Body(Coordinator.Notice ?? PhoneStrings.Get("LocalRunnerAdoptionRetry", "Finish runner transfer")));
            var retry = NativeTheme.SecondaryButton(PhoneStrings.Get("LocalRunnerAdoptionRetry", "Finish runner transfer"));
            retry.AutomationId = "local-runner-adoption-retry";
            retry.Clicked += async (_, _) => await RunAsync(async () =>
            {
                _busy = true;
                Refresh();
                try { await Coordinator.RetryLocalRunnerAdoptionsAsync(); }
                finally { _busy = false; }
                await LoadAsync(default);
            });
            _body.Add(retry);
            return;
        }
        if (!Coordinator.CanAdoptLocalRunner)
        {
            _runners = [];
            _body.Add(NativeTheme.Body(PhoneStrings.Get("LocalRunnerAdoptionAccountChanged", "Open your account first, then return to this list.")));
            return;
        }
        _body.Add(NativeTheme.Body(PhoneStrings.Get("LocalRunnerAdoptionIntro",
            "Choose a runner created here before linking your account. Its saved choices and book stay together. Nothing is uploaded by this step.")));
        if (_loaded && _runners.Count == 0)
            _body.Add(NativeTheme.Body(PhoneStrings.Get("LocalRunnerAdoptionEmpty", "No unlinked runners on this device.")));
        foreach (var runner in _runners)
        {
            var button = NativeTheme.SecondaryButton(runner.Name);
            button.AutomationId = "local-runner-adoption-review";
            button.Clicked += async (_, _) => await RunAsync(() => AdoptAsync(runner));
            _body.Add(button);
        }
        if (_status is not null) _body.Add(NativeTheme.Body(_status));
        var reload = NativeTheme.SecondaryButton(PhoneStrings.Get("LocalRunnerAdoptionReload", "Reload list"));
        reload.AutomationId = "local-runner-adoption-reload";
        reload.Clicked += async (_, _) => await RunAsync(() => LoadAsync(default));
        _body.Add(reload);
    }

    private async Task AdoptAsync(NativeLocalRunnerCandidate runner)
    {
        long appearance = CaptureAppearanceGeneration();
        _busy = true;
        _status = null;
        Refresh();
        try
        {
            var review = await Coordinator.ReviewLocalRunnerAdoptionAsync(runner);
            if (!IsCurrentAppearanceGeneration(appearance)) return;
            if (review is null)
            {
                _status = PhoneStrings.Get("LocalRunnerAdoptionUnavailable",
                    "This runner cannot be transferred safely right now. Nothing was changed.");
                return;
            }
            bool approved = await DisplayAlertAsync(Title,
                PhoneStrings.Format("LocalRunnerAdoptionQuestion",
                    "Move “{0}” and its book to your currently linked account on this device? It will no longer appear in unlinked mode. Existing account runners are kept.", runner.Name),
                PhoneStrings.Get("LocalRunnerAdoptionConfirm", "Move to my account"),
                PhoneStrings.Get("Cancel", "Cancel"));
            if (!approved || !IsCurrentAppearanceGeneration(appearance)) return;
            bool activated = await Coordinator.ConfirmLocalRunnerAdoptionAsync(review, true);
            if (!IsCurrentAppearanceGeneration(appearance)) return;
            if (activated) await Shell.Current.GoToAsync(_runnerRoute);
            else _status = Coordinator.HasPendingLocalRunnerAdoption ? Coordinator.Notice
                : PhoneStrings.Get("LocalRunnerAdoptionReload", "Reload list");
        }
        finally { _busy = false; }
    }
}
