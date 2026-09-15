using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    internal bool IsTabletCareerSkillGroupOwnerCurrent(CharacterOverviewState expected)
        => expected.DisplayOwnerContext is { IsValid: true } owner
            && expected.Session.OwnerContext == owner
            && State.DisplayOwnerContext == owner
            && State.Session.OwnerContext == owner
            && IsNativePersistenceOwnerCurrent(owner);

    // Selection admission is checked after both the durable checkpoint lease
    // and this activation gate have been acquired. Core still owns the atomic
    // command, its idempotency ledger, and every persisted mutation/receipt.
    internal Task<CharacterCareerSkillGroupAdvanceResult?> TryAdvanceBoundCareerSkillGroupAsync(
        CharacterCareerSkillGroupAdvanceCommand command,
        CharacterOverviewState expected,
        Func<bool> isCurrentInspector,
        bool resolving,
        CancellationToken cancellationToken = default)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(expected);
            ArgumentNullException.ThrowIfNull(isCurrentInspector);
            cancellationToken.ThrowIfCancellationRequested();
            CharacterOverviewState current = State;
            bool commandRevision = command.ExpectedWorkspaceRevision == expected.ContentRevision
                || resolving && command.ExpectedWorkspaceRevision > 0
                    && command.ExpectedWorkspaceRevision < long.MaxValue
                    && command.ExpectedWorkspaceRevision + 1 == expected.ContentRevision;
            bool renderedRevision = current.ContentRevision == expected.ContentRevision
                || resolving && expected.ContentRevision < long.MaxValue
                    && current.ContentRevision == expected.ContentRevision + 1;
            if (_disposed || current.IsBusy || current.Error is not null || current.IsDirty
                || expected.WorkspaceId is null || expected.ContentRevision <= 0
                || expected.SavedRevision != expected.ContentRevision
                || current.WorkspaceId != expected.WorkspaceId || command.WorkspaceId != expected.WorkspaceId
                || !commandRevision || !renderedRevision || current.SavedRevision != current.ContentRevision
                || !IsTabletCareerSkillGroupOwnerCurrent(expected)
                || !string.Equals(current.ActiveSectionId, expected.ActiveSectionId, StringComparison.Ordinal)
                || !Sr5CareerWizardCatalog.IsSr5CareerRunner(current.Profile?.Created == true, current.Rules?.GameEdition)
                || !isCurrentInspector())
                throw new OperationCanceledException("The selected tablet skill group no longer owns this review.", cancellationToken);

            CharacterCareerSkillGroupAdvanceResult? result =
                await AdvanceCareerSkillGroupAsync(command, cancellationToken, expected.DisplayOwnerContext);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTabletCareerSkillGroupOwnerCurrent(expected) || !isCurrentInspector())
                throw new OperationCanceledException("The tablet skill-group result no longer owns this inspector.", cancellationToken);
            return result;
        }, cancellationToken);
}
