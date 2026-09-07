using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Per-page selection and read-only presenter reload adapter over the real
/// runner session. Owner identity is the existing local Career journal owner;
/// no account, remote run or GM identity is synthesized by this adapter.
/// </summary>
internal sealed class RunnerSessionSr5AfterRunRewardHost(
    RunnerSessionCoordinator coordinator,
    ISr5CareerCheckpointOwnerAuthority owner)
    : ISr5AfterRunRewardRunnerAuthority, ISr5AfterRunRewardSavedRunnerRefresh
{
    private readonly RunnerSessionCoordinator _coordinator = coordinator
        ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly ISr5CareerCheckpointOwnerAuthority _owner = owner
        ?? throw new ArgumentNullException(nameof(owner));

    public Sr5AfterRunRewardRunnerBinding Current
        => _coordinator.CaptureAfterRunRewardBinding(_owner.CurrentOwnerId);

    public async Task ReloadAsync(CharacterWorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        var before = Current;
        if (!before.CanReloadSavedSr5() || before.WorkspaceId != workspaceId)
            throw new InvalidOperationException("Only the selected saved reward runner may be reloaded.");
        await _coordinator.ReloadAfterRunRewardWorkspaceAsync(before, cancellationToken);
        var current = Current;
        if (!current.IsCleanSavedSr5() || !current.SameSelection(before))
            throw new InvalidOperationException("Reward ownership changed during presenter reload.");
    }
}
