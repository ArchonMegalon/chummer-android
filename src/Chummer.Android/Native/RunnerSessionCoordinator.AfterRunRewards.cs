using Chummer.Application.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private readonly object _afterRunRewardSelectionGate = new();
    private long _afterRunRewardSelectionGeneration = 1;
    private CharacterWorkspaceId? _afterRunRewardObservedWorkspace;
    private ICharacterAfterRunRewardService? _afterRunRewardService;
    private Sr5AfterRunRewardCheckpointStore? _afterRunRewardCheckpoints;

    private void InitializeAfterRunRewardHost(ICharacterAfterRunRewardService? service,
        Sr5AfterRunRewardCheckpointStore? checkpoints)
    {
        _afterRunRewardService = service;
        _afterRunRewardCheckpoints = checkpoints;
        lock (_afterRunRewardSelectionGate) _afterRunRewardObservedWorkspace = State.WorkspaceId;
    }

    public bool SupportsAfterRunRewardEntry
        => !_disposed && _afterRunRewardService is not null && _afterRunRewardCheckpoints is not null;

    internal Sr5AfterRunRewardPhoneModel CreateAfterRunRewardModel(DateTime localNow,
        ISr5CareerCheckpointOwnerAuthority? owner = null)
    {
        if (!SupportsAfterRunRewardEntry)
            throw new InvalidOperationException("The local Core reward authority is unavailable.");
        var host = new RunnerSessionSr5AfterRunRewardHost(this,
            owner ?? new PreferencesSr5CareerCheckpointOwnerAuthority());
        if (!host.Current.IsCleanSavedSr5())
            throw new InvalidOperationException("Open a clean saved SR5 Career runner before recording rewards.");
        return new(new Sr5AfterRunRewardCoordinator(_afterRunRewardService!, host,
            _afterRunRewardCheckpoints!), host, host, localNow);
    }

    /// <summary>
    /// Observe local recovery before choosing a catalog route. A newly available
    /// run proposal must not strand an older local reward. No lookup, retry,
    /// confirmation, journal write or owner release happens during this read.
    /// </summary>
    internal async Task<Sr5AfterRunRewardPhoneModel?> PrepareAfterRunRewardEntryAsync(
        bool allowNewReward, CancellationToken cancellationToken = default,
        ISr5CareerCheckpointOwnerAuthority? owner = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SupportsAfterRunRewardEntry) return null;
        var model = CreateAfterRunRewardModel(DateTime.Now, owner);
        await model.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!model.HasCurrentSelection)
            throw new InvalidOperationException(PhoneStrings.Get("AfterRunRewardRunnerChanged",
                "The selected runner changed. Reopen After Run for the current saved runner."));
        if (!allowNewReward && model.Status == Sr5AfterRunRewardPhoneStatus.JournalUnavailable)
            throw new InvalidOperationException(PhoneStrings.Get("AfterRunRewardJournalUnavailable",
                "A pending Career operation or unreadable journal prevents a new reward. Resolve that operation first; nothing has been cleared."));
        return allowNewReward || model.HasRetainedIntent ? model : null;
    }

    // These reads contain no Preferences, controls, XML parsing or file I/O.
    // One immutable presenter state and one selection generation form the frame.
    internal Sr5AfterRunRewardRunnerBinding CaptureAfterRunRewardBinding(Guid ownerId)
    {
        lock (_afterRunRewardSelectionGate)
        {
            var state = State;
            ObserveAfterRunRewardWorkspaceLocked(state.WorkspaceId);
            return new(_disposed ? Guid.Empty : ownerId, state.WorkspaceId ?? default, _afterRunRewardSelectionGeneration,
                state.Profile?.Created == true, state.Rules?.GameEdition,
                state.ContentRevision, state.SavedRevision, state.IsDirty,
                _disposed ? "The runner session is closed."
                    : state.IsBusy ? "The selected runner is still loading." : state.Error,
                IsBusy: state.IsBusy);
        }
    }

    private void ObserveAfterRunRewardSelection()
    {
        lock (_afterRunRewardSelectionGate) ObserveAfterRunRewardWorkspaceLocked(State.WorkspaceId);
    }

    private void ObserveAfterRunRewardWorkspaceLocked(CharacterWorkspaceId? workspaceId)
    {
        if (_afterRunRewardObservedWorkspace == workspaceId) return;
        _afterRunRewardObservedWorkspace = workspaceId;
        _afterRunRewardSelectionGeneration = checked(_afterRunRewardSelectionGeneration + 1);
    }

    // Explicit selection/import intents fence an A -> B -> A cycle even if a
    // queued page refresh only observes the final A. Ordinary same-runner loads
    // and revision updates must not invalidate the reward's own reload handoff.
    private void AdvanceAfterRunRewardSelection()
    {
        lock (_afterRunRewardSelectionGate)
        {
            _afterRunRewardSelectionGeneration = checked(_afterRunRewardSelectionGeneration + 1);
            _afterRunRewardObservedWorkspace = State.WorkspaceId;
        }
    }

    internal async Task ReloadAfterRunRewardWorkspaceAsync(Sr5AfterRunRewardRunnerBinding expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        await _workspaceActivationGate.WaitAsync(cancellationToken);
        try
        {
            var before = CaptureAfterRunRewardBinding(expected.OwnerId);
            if (!expected.CanReloadSavedSr5() || before != expected)
                throw new InvalidOperationException("The selected reward runner changed before reload.");

            // Read the existing runtime store into the existing presenter. This
            // is neither Save nor SwitchWorkspace and cannot credit a reward.
            await _presenter.LoadAsync(expected.WorkspaceId, cancellationToken);
            RequireRewardReloadSelection(expected);
            await SyncShellAsync(cancellationToken);
            RequireRewardReloadSelection(expected);
            NotifyChanged();
        }
        finally { _workspaceActivationGate.Release(); }
    }

    private void RequireRewardReloadSelection(Sr5AfterRunRewardRunnerBinding expected)
    {
        var current = CaptureAfterRunRewardBinding(expected.OwnerId);
        if (!current.IsCleanSavedSr5() || !current.SameSelection(expected))
            throw new InvalidOperationException("The saved reward runner could not be reloaded in its original selection.");
    }
}
