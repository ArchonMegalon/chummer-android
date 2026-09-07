namespace Chummer.Android.Native;

public sealed class Sr5CareerReputationWizardPage : NativePageBase
{
    private readonly Sr5CareerReputationPhoneModel _model;
    private readonly Sr5CareerReputationView _view;
    private readonly Func<bool>? _entryStillCurrent;
    private bool _entryAccepted;
    private CancellationTokenSource? _lifetime;
    private readonly SemaphoreSlim _appearancePreparation = new(1, 1);

    public Sr5CareerReputationWizardPage(RunnerSessionCoordinator coordinator)
        : this(coordinator, coordinator.CreateCareerReputationModel()) { }

    internal Sr5CareerReputationWizardPage(RunnerSessionCoordinator coordinator, Sr5CareerReputationPhoneModel model,
        Func<bool>? entryStillCurrent = null) : base(coordinator)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _entryStillCurrent = entryStillCurrent;
        Title = PhoneStrings.Get("ReputationWizardTitle", "Career · Reputation");
        AutomationId = "sr5-career-reputation-page";
        _view = new(model, RunAsync, async () => { if (model.CanFinish) await Navigation.PopAsync(); });
        // No action is authorized until the owning page actually appears.
        _view.SetLifetime(new CancellationToken(canceled: true));
        Content = new ScrollView { Content = _view };
    }

    internal void RequireCurrentEntry(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_model.HasCurrentSelection || !_entryAccepted && _entryStillCurrent?.Invoke() == false)
            throw new InvalidOperationException(PhoneStrings.Get("ReputationWizardRunnerChanged", "The selected runner changed."));
    }

    protected override void OnAppearing()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new();
        // Observation must validate the contextual handoff before controls gain
        // this appearance's action token. A later appearance never revives an old click.
        _view.SetLifetime(new CancellationToken(canceled: true));
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _view.SetLifetime(new CancellationToken(canceled: true));
        base.OnDisappearing();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        var lifetime = _lifetime;
        // Core reads may already be running when a page disappears. Wait for
        // that older observation to drain without blocking the UI thread; the
        // model's busy no-op is not a successful new appearance preparation.
        await _appearancePreparation.WaitAsync(cancellationToken);
        try
        {
            if (lifetime is null || !ReferenceEquals(lifetime, _lifetime) || lifetime.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            RequireCurrentEntry(cancellationToken);
            Task preparation = _model.InitializeAsync(cancellationToken);
            _view.Refresh();
            await preparation;
            RequireCurrentEntry(cancellationToken);
            if (!ReferenceEquals(lifetime, _lifetime) || lifetime.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            _entryAccepted = true;
            _view.SetLifetime(lifetime.Token);
        }
        finally { _appearancePreparation.Release(); }
    }

    protected override void Refresh() => _view.Refresh();
}
