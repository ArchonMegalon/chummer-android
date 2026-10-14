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
        _view = new(model, RunAsync, FinishAsync);
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

    private async Task FinishAsync()
    {
        var lifetime = _lifetime;
        var navigation = Navigation;
        Page[] stack = navigation.NavigationStack.ToArray();
        bool Current() => lifetime is not null && ReferenceEquals(lifetime, _lifetime)
            && !lifetime.IsCancellationRequested && _entryAccepted && _model.CanFinish
            && ReferenceEquals(stack.LastOrDefault(), this)
            && navigation.NavigationStack.SequenceEqual(stack) && navigation.ModalStack.Count == 0;
        if (!Current()) return;

        int parentIndex = stack.Length - 2;
        var reward = parentIndex >= 0 ? stack[parentIndex] as Sr5AfterRunRewardWizardPage : null;
        if (reward is not null)
        {
            if (!reward.IsSavedContinuationFor(Coordinator)) return;
            parentIndex--;
        }
        Page? origin = parentIndex >= 0 ? stack[parentIndex] : null;
        // Only retire this exact completed flow. Never unwind arbitrary editors,
        // another session, or the whole shell to find a Career page.
        if (origin is not null && (origin is not (BuildPage or Sr5CareerWizardPage or PhoneTablePage)
            || origin is not NativePageBase native || !native.UsesCoordinator(Coordinator))) return;
        Page? career = origin is BuildPage or Sr5CareerWizardPage ? null : new BuildPage(Coordinator);
        if (!Current() || reward is not null && !reward.IsSavedContinuationFor(Coordinator)) return;
        if (career is not null) navigation.InsertPageBefore(career, this);
        if (reward is not null) navigation.RemovePage(reward);
        await navigation.PopAsync();
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
