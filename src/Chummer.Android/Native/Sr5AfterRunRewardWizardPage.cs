namespace Chummer.Android.Native;

/// <summary>Local reward entry; separate from governed run consequences.</summary>
public sealed class Sr5AfterRunRewardWizardPage : NativePageBase
{
    private readonly Sr5AfterRunRewardPhoneModel _model;
    private readonly Sr5AfterRunRewardView _view;
    private readonly Func<CancellationToken, Task<Sr5AfterRunSettlementEditorState>> _prepareConsequences;
    private CancellationTokenSource? _lifetime;

    public Sr5AfterRunRewardWizardPage(RunnerSessionCoordinator coordinator)
        : this(coordinator, coordinator.CreateAfterRunRewardModel(DateTime.Now))
    {
    }

    internal Sr5AfterRunRewardWizardPage(RunnerSessionCoordinator coordinator,
        Sr5AfterRunRewardPhoneModel model,
        Func<CancellationToken, Task<Sr5AfterRunSettlementEditorState>>? prepareConsequences = null) : base(coordinator)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _prepareConsequences = prepareConsequences ?? coordinator.PrepareAfterRunSettlementAsync;
        Title = PhoneStrings.Get("AfterRunRewardTitle", "After Run · Rewards");
        AutomationId = "sr5-after-run-local-reward-page";
        _view = new(_model, RunAsync, async () =>
        {
            if (_model.CanContinue) await Navigation.PopAsync();
        }, OpenConsequencesAsync, OpenDowntimeAsync,
            coordinator.SupportsCareerReputationEntry ? OpenReputationAsync : null);
        Content = new ScrollView { Content = _view };
    }

    internal async Task OpenDowntimeAsync(CancellationToken cancellationToken)
    {
        var saved = _model.Handoff;
        RequireCurrentReward(saved, cancellationToken);
        // Local planning has its own typed preview and confirmation. A saved
        // reward is an entry context, never a run proposal or GM approval.
        var destination = new Sr5DowntimeCalendarWizardPage(Coordinator,
            new RunnerSessionSr5DowntimeCalendarAuthority(Coordinator),
            Sr5DowntimeCalendarJournalStore.CreateDefault(),
            entryStillCurrent: () => _model.CanContinue && ReferenceEquals(saved, _model.Handoff));
        RequireCurrentReward(saved, cancellationToken);
        await Navigation.PushAsync(destination);
    }

    internal async Task OpenReputationAsync(CancellationToken cancellationToken)
    {
        var saved = _model.Handoff;
        RequireCurrentReward(saved, cancellationToken);
        var model = await Coordinator.PrepareCareerReputationEntryAsync(cancellationToken);
        RequireCurrentReward(saved, cancellationToken);
        var destination = new Sr5CareerReputationWizardPage(Coordinator, model,
            entryStillCurrent: () => _model.CanContinue && ReferenceEquals(saved, _model.Handoff));
        destination.RequireCurrentEntry(cancellationToken);
        await Navigation.PushAsync(destination);
    }

    internal async Task OpenConsequencesAsync(CancellationToken cancellationToken)
    {
        var saved = _model.Handoff;
        var editor = await PrepareConsequencesAsync(cancellationToken);
        RequireCurrentConsequences(editor, saved, cancellationToken);
        // This is the governed proposal route, not the general entry factory:
        // missing proposals must never loop back into another reward entry.
        var destination = new Sr5AfterRunSettlementWizardPage(Coordinator, editor);
        RequireCurrentConsequences(editor, saved, cancellationToken);
        await Navigation.PushAsync(destination);
    }

    internal async Task<Sr5AfterRunSettlementEditorState> PrepareConsequencesAsync(CancellationToken cancellationToken)
    {
        var saved = _model.Handoff;
        RequireCurrentReward(saved, cancellationToken);
        var editor = await _prepareConsequences(cancellationToken);
        RequireCurrentConsequences(editor, saved, cancellationToken);
        return editor;
    }

    private void RequireCurrentConsequences(Sr5AfterRunSettlementEditorState editor,
        Sr5AfterRunRewardConsequencesHandoff? saved, CancellationToken cancellationToken)
    {
        RequireCurrentReward(saved, cancellationToken);
        if (editor is null || !editor.IsExact()
            || editor.WorkspaceId != saved!.Runner.WorkspaceId
            || editor.WorkspaceRevision != saved.Runner.SavedRevision)
            throw ChangedConsequences();
    }

    private void RequireCurrentReward(Sr5AfterRunRewardConsequencesHandoff? saved,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (saved is null || !_model.CanContinue || !ReferenceEquals(saved, _model.Handoff))
            throw ChangedConsequences();
    }

    private static InvalidOperationException ChangedConsequences() => new(PhoneStrings.Get(
        "AfterRunRewardConsequencesChanged",
        "The saved runner or run proposal changed. Recheck the saved reward before continuing."));

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
        // A child wizard may have saved a newer revision. Re-establish the
        // existing Applied receipt's handoff through lookup and reload only.
        // Pending/unknown operations still require explicit user recovery;
        // appearing never confirms, retries or creates a new reward.
        Task preparation = _model.Checkpoint?.Phase == Sr5AfterRunRewardCheckpointPhase.Applied
            && !_model.CanContinue && _model.CanRecover
            ? _model.RecoverAsync(cancellationToken)
            : _model.InitializeAsync(cancellationToken);
        _view.Refresh(); // Disable editors as soon as the journal read starts.
        return preparation;
    }

    protected override void Refresh() => _view.Refresh();
}
