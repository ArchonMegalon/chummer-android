namespace Chummer.Android.Native;

/// <summary>Local reward entry; separate from governed run consequences.</summary>
public sealed class Sr5AfterRunRewardWizardPage : NativePageBase
{
    private readonly Sr5AfterRunRewardPhoneModel _model;
    private readonly Sr5AfterRunRewardView _view;
    private CancellationTokenSource? _lifetime;

    public Sr5AfterRunRewardWizardPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        _model = coordinator.CreateAfterRunRewardModel(DateTime.Now);
        Title = PhoneStrings.Get("AfterRunRewardTitle", "After Run · Rewards");
        AutomationId = "sr5-after-run-local-reward-page";
        _view = new(_model, RunAsync, async () =>
        {
            if (_model.CanContinue) await Navigation.PopAsync();
        });
        Content = new ScrollView { Content = _view };
    }

    protected override void OnAppearing()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new();
        _view.SetLifetime(_lifetime.Token);
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        // Cancel observation/refresh, never interpret departure as rollback or
        // replace a retained operation ID. Returning keeps this same model.
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        base.OnDisappearing();
    }

    protected override Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        Task preparation = _model.InitializeAsync(cancellationToken);
        _view.Refresh(); // Disable editors as soon as the journal read starts.
        return preparation;
    }

    protected override void Refresh() => _view.Refresh();
}
