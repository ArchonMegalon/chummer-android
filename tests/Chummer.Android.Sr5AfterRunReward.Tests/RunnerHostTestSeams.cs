using Chummer.Application.Characters;
using Chummer.Contracts.Workspaces;

// Only presenter/session scaffolding is substituted. The actual reward partial
// and host adapter are compiled below, alongside real Core and file persistence.
// This does not compile/qualify the rest of RunnerSessionCoordinator or MAUI.
namespace Chummer.Android.Native;

public interface ISr5CareerCheckpointOwnerAuthority { Guid CurrentOwnerId { get; } }

internal sealed class PreferencesSr5CareerCheckpointOwnerAuthority : ISr5CareerCheckpointOwnerAuthority
{
    public Guid CurrentOwnerId => throw new NotSupportedException("Inject the explicit local test owner.");
}

internal sealed record RewardHostTestProfile(bool Created);
internal sealed record RewardHostTestRules(string GameEdition);
internal sealed record RewardHostTestState(CharacterWorkspaceId? WorkspaceId,
    long ContentRevision, long SavedRevision, bool IsDirty, bool IsBusy, string? Error,
    RewardHostTestProfile? Profile, RewardHostTestRules? Rules);

internal sealed class RewardHostPresenterProbe(RewardHostTestState initial,
    Func<CharacterWorkspaceId, RewardHostTestState> loadSaved)
{
    private RewardHostTestState _state = initial;
    internal RewardHostTestState State => Volatile.Read(ref _state);
    internal event Action? Changed;
    internal int Loads;
    internal Func<CancellationToken, Task>? BeforeLoad;

    internal void Publish(RewardHostTestState state)
    {
        Volatile.Write(ref _state, state);
        Changed?.Invoke();
    }

    public async Task LoadAsync(CharacterWorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        Loads++;
        Publish(State with { IsBusy = true, Error = null });
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BeforeLoad is not null) await BeforeLoad(cancellationToken);
            Publish(loadSaved(workspaceId));
        }
        catch (Exception error)
        {
            // Matches the inspected presenter's error publication. A normal
            // return from LoadAsync alone is not proof of a successful reload.
            Publish(State with { IsBusy = false, Error = error.Message });
        }
    }
}

public sealed partial class RunnerSessionCoordinator : IDisposable
{
    private readonly RewardHostPresenterProbe _presenter;
    private readonly SemaphoreSlim _workspaceActivationGate = new(1, 1);
    private bool _disposed;
    internal RewardHostTestState State => _presenter.State;
    internal int Notifications;
    internal Func<Task>? ShellSync;

    internal RunnerSessionCoordinator(RewardHostPresenterProbe presenter,
        ICharacterAfterRunRewardService? service, Sr5AfterRunRewardCheckpointStore? store)
    {
        _presenter = presenter;
        InitializeAfterRunRewardHost(service, store);
        _presenter.Changed += ObserveAfterRunRewardSelection;
    }

    private Task SyncShellAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ShellSync?.Invoke() ?? Task.CompletedTask;
    }

    private void NotifyChanged() => Notifications++;
    internal void AdvanceSelectionForTest() => AdvanceAfterRunRewardSelection();
    internal Task HoldActivationForTest() => _workspaceActivationGate.WaitAsync();
    internal void ReleaseActivationForTest() => _workspaceActivationGate.Release();

    public void Dispose()
    {
        _disposed = true;
        AdvanceAfterRunRewardSelection();
        _presenter.Changed -= ObserveAfterRunRewardSelection;
    }
}
