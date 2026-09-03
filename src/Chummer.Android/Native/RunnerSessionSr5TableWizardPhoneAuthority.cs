using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Composes existing typed Edge and direct Career Weapon projections only while the runner's
/// workspace identity and revision remain unchanged across the complete read.
/// </summary>
internal sealed class RunnerSessionSr5TableWizardPhoneAuthority
{
    private readonly RunnerSessionCoordinator _coordinator;

    public RunnerSessionSr5TableWizardPhoneAuthority(RunnerSessionCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async Task<Sr5TableWizardSnapshot?> LoadAsync(
        Sr5TableWizardLane lane,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(lane)
            || !IsCurrentCreatedSr5()
            || _coordinator.State.WorkspaceId is not { } workspaceId
            || _coordinator.State.ContentRevision <= 0
            || _coordinator.State.IsDirty
            || _coordinator.State.SavedRevision != _coordinator.State.ContentRevision)
        {
            return null;
        }
        long revision = _coordinator.State.ContentRevision;

        CareerEdgeUseEditorState? edge = await _coordinator
            .PrepareCareerEdgeUseEditAsync(cancellationToken)
            .ConfigureAwait(false);
        if (edge is null
            || edge.WorkspaceId != workspaceId
            || edge.ContentRevision != revision
            || !Matches(workspaceId, revision))
        {
            return null;
        }

        CareerWeaponFireCatalogEditorState? weapons = null;
        if (lane == Sr5TableWizardLane.Playtime)
        {
            weapons = await _coordinator
                .PrepareCareerWeaponFireCatalogAsync(cancellationToken)
                .ConfigureAwait(false);
            if (weapons is null
                || weapons.WorkspaceId != workspaceId
                || weapons.ContentRevision != revision
                || !Matches(workspaceId, revision))
            {
                return null;
            }
        }

        try
        {
            Sr5TableWizardSnapshot snapshot = Sr5TableWizardProjector.Project(lane, edge, weapons);
            return Matches(workspaceId, revision) ? snapshot : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private bool Matches(Chummer.Contracts.Workspaces.CharacterWorkspaceId workspaceId, long revision)
        => IsCurrentCreatedSr5()
           && _coordinator.State.WorkspaceId == workspaceId
           && _coordinator.State.ContentRevision == revision
           && _coordinator.State.SavedRevision == revision
           && !_coordinator.State.IsDirty;

    private bool IsCurrentCreatedSr5()
        => _coordinator.State.Profile?.Created == true
           && Sr5CareerWizardCatalog.IsSr5CareerRunner(
               true,
               _coordinator.State.Rules?.GameEdition);
}
