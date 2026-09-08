using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Explicit host reload seam. An implementation reloads the exact saved workspace
/// into the presenter and publishes the corresponding immutable runner binding.
/// This is not a general command, a save, or permission to switch the selected runner.
/// The local next-wave runner host implements this seam; release consumption
/// still requires the new Core/UI/Android graph to be sealed and qualified.
/// </summary>
public interface ISr5AfterRunRewardSavedRunnerRefresh
{
    Task ReloadAsync(CharacterWorkspaceId workspaceId, CancellationToken cancellationToken);
}

/// <summary>
/// Verified local reward plus independently read current saved facts. This is NOT
/// a consequence quote, run/GM approval, or a raw-XML character projection digest.
/// Consequences are a separate transaction requiring their own genuine proposal,
/// fresh projection, reviews and Core quote. Historical receipt balances need not
/// equal this snapshot after later edits.
/// </summary>
public sealed class Sr5AfterRunRewardConsequencesHandoff
{
    internal Sr5AfterRunRewardConsequencesHandoff(Sr5AfterRunRewardRunnerBinding runner,
        Sr5AfterRunRewardCheckpoint recorded, CharacterAfterRunRewardSnapshot snapshot)
    {
        if (!runner.IsCleanSavedSr5() || !recorded.IsExact()
            || recorded.Phase != Sr5AfterRunRewardCheckpointPhase.Applied
            || recorded.OwnerId != runner.OwnerId || recorded.Command.WorkspaceId != runner.WorkspaceId
            || !CharacterAfterRunRewardProjector.IsValidSnapshot(snapshot)
            || snapshot.WorkspaceId != runner.WorkspaceId
            || snapshot.ContentRevision != runner.ContentRevision
            || snapshot.SavedRevision != runner.SavedRevision
            || snapshot.ContentRevision < recorded.Receipt!.CommittedWorkspaceRevision)
            throw new ArgumentException("The verified reward and fresh saved runner do not agree.");
        Runner = runner;
        Recorded = recorded;
        Snapshot = snapshot;
    }

    public Sr5AfterRunRewardRunnerBinding Runner { get; }
    public Sr5AfterRunRewardCheckpoint Recorded { get; }
    public CharacterAfterRunRewardSnapshot Snapshot { get; }

    public bool IsCurrent(ISr5AfterRunRewardRunnerAuthority authority)
        => authority.Current == Runner && Runner.IsCleanSavedSr5();
}
