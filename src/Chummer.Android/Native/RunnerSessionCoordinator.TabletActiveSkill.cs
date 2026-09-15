using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    internal bool IsTabletCareerActiveSkillOwnerCurrent(CharacterOverviewState expected)
        => expected.DisplayOwnerContext is { } owner
            && State.DisplayOwnerContext == owner
            && IsNativePersistenceOwnerCurrent(owner);

    // This is only an admission fence for the tablet's rendered selection. The
    // existing typed advancement/save path remains the sole mutation operation.
    internal Task<bool> TryApplyBoundCareerActiveSkillAdvanceAsync(
        CareerActiveSkillAdvanceRequest request,
        CharacterOverviewState expected,
        Func<bool> isCurrentInspector,
        CancellationToken cancellationToken = default)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(expected);
            ArgumentNullException.ThrowIfNull(isCurrentInspector);
            cancellationToken.ThrowIfCancellationRequested();
            CharacterOverviewState current = State;
            if (_disposed || current.IsBusy || current.Error is not null
                || current.IsDirty || expected.WorkspaceId is null
                || expected.ContentRevision <= 0
                || current.WorkspaceId != expected.WorkspaceId
                || request.WorkspaceId != expected.WorkspaceId
                || request.ExpectedContentRevision != expected.ContentRevision
                || current.DisplayOwnerContext != expected.DisplayOwnerContext
                || !IsTabletCareerActiveSkillOwnerCurrent(expected)
                || current.ContentRevision != expected.ContentRevision
                || current.SavedRevision != expected.SavedRevision
                || current.SavedRevision != current.ContentRevision
                || !string.Equals(current.ActiveSectionId, expected.ActiveSectionId, StringComparison.Ordinal)
                || !ReferenceEquals(current.ActiveCollectionEditor, expected.ActiveCollectionEditor)
                || !isCurrentInspector())
                throw new OperationCanceledException("The selected tablet skill no longer owns this review.", cancellationToken);

            // ApplyCareerActiveSkillAdvanceAsync does not reacquire the activation gate.
            return await ApplyCareerActiveSkillAdvanceAsync(request, cancellationToken);
        }, cancellationToken);
}
