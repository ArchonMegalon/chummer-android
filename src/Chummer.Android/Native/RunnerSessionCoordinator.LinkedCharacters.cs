using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Android.Platform;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    internal async Task<bool> TryAttachBoundLinkedCharacterAsync(
        WorkspaceCollectionItemTarget target, CharacterOverviewState expected,
        Func<bool> isCurrentInspector, CancellationToken cancellationToken = default)
    {
        WorkspaceCollectionItemEditorState? item = UniqueLinkedItem(expected, target);
        if (item?.LinkedCharacter is not { CanAttach: true }) return false;
        string authority = LinkedEditorAuthority(expected, item);
        if (!LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;

        // The platform service returns a fresh exclusively-created staging file.
        // Selection remains free to change while Android's picker is open.
        AndroidStagedLinkedCharacter? staged = await _linkedCharacters.StageAsync(target, cancellationToken);
        if (staged is null) return false;
        bool dispatched = false;
        try
        {
            return await WithWorkspaceActivationGateAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;
                dispatched = true; // Task failure after this point is not non-commit proof.
                await ApplyCollectionMutationCoreAsync(new WorkspaceSetLinkedCharacterRequest(
                    target, staged.FileName, staged.RelativeFileName, staged.DisplayName, staged.Identity), cancellationToken);
                bool observed = LinkedSuccessorIsCurrent(expected)
                    && UniqueLinkedItem(State, target)?.LinkedCharacter is { IsLinked: true, IdentityResolved: true } linked
                    && linked.FileName == staged.FileName && linked.RelativeFileName == staged.RelativeFileName
                    && linked.DisplayName == staged.DisplayName;
                _notice = observed ? $"Linked {staged.Identity.CharacterName}." : UnconfirmedLinkedOutcome();
                NotifyChanged();
                return observed;
            }, cancellationToken);
        }
        catch
        {
            if (dispatched) { _notice = UnconfirmedLinkedOutcome(); NotifyChanged(); }
            throw;
        }
        finally
        {
            if (!dispatched)
                await _linkedCharacters.DeleteOwnedAsync(target, staged.FileName, CancellationToken.None);
            // Never delete a possibly committed file, or eagerly delete the
            // previous path: duplicated workspaces can still reference it.
            // Reference-aware reclamation/durable outcome recovery are separate.
        }
    }

    internal async Task<bool> TryRemoveBoundLinkedCharacterAsync(
        WorkspaceCollectionItemTarget target, CharacterOverviewState expected,
        Func<bool> isCurrentInspector, CancellationToken cancellationToken = default)
    {
        WorkspaceCollectionItemEditorState? item = UniqueLinkedItem(expected, target);
        if (item?.LinkedCharacter is not { CanRemove: true }) return false;
        string authority = LinkedEditorAuthority(expected, item);
        if (!LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;
        return await WithWorkspaceActivationGateAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;
            try
            {
                await ApplyCollectionMutationCoreAsync(new WorkspaceRemoveLinkedCharacterRequest(target), cancellationToken);
                bool observed = LinkedSuccessorIsCurrent(expected)
                    && UniqueLinkedItem(State, target)?.LinkedCharacter is { IsLinked: false, FileName.Length: 0, RelativeFileName.Length: 0 };
                _notice = observed ? "Linked runner removed." : UnconfirmedLinkedOutcome();
                NotifyChanged();
                // Old files remain available to other workspaces and recovery.
                return observed;
            }
            catch { _notice = UnconfirmedLinkedOutcome(); NotifyChanged(); throw; }
        }, cancellationToken);
    }

    private bool LinkedSuccessorIsCurrent(CharacterOverviewState expected)
        => !_disposed && !State.IsBusy && State.Error is null && State.WorkspaceId == expected.WorkspaceId
            && State.ActiveSectionId == expected.ActiveSectionId
            && expected.ContentRevision < long.MaxValue
            && State.ContentRevision == expected.ContentRevision + 1
            // Core replaces the durable workspace document, but does not checkpoint
            // the external runner file here. A normal replacement is 5/5 -> 6/5.
            && State.SavedRevision == expected.SavedRevision;

    private bool LinkedEditorIsCurrent(CharacterOverviewState expected, WorkspaceCollectionItemTarget target,
        string authority, Func<bool> isCurrentInspector)
    {
        CharacterOverviewState current = State;
        WorkspaceCollectionItemEditorState? item = UniqueLinkedItem(current, target);
        return !_disposed && !current.IsBusy && current.Error is null && expected.WorkspaceId is not null
            && current.WorkspaceId == expected.WorkspaceId
            && current.ContentRevision == expected.ContentRevision && current.SavedRevision == expected.SavedRevision
            && current.ActiveSectionId == expected.ActiveSectionId
            && ReferenceEquals(current.ActiveCollectionEditor, expected.ActiveCollectionEditor)
            && item is not null && LinkedEditorAuthority(current, item) == authority && isCurrentInspector();
    }

    private static WorkspaceCollectionItemEditorState? UniqueLinkedItem(CharacterOverviewState state,
        WorkspaceCollectionItemTarget target)
    {
        WorkspaceCollectionItemEditorState[] matches = state.ActiveCollectionEditor?.Items
            .Where(item => CollectionItemEditorPage.TargetsMatch(item.Target, target)).ToArray() ?? [];
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static string LinkedEditorAuthority(CharacterOverviewState state, WorkspaceCollectionItemEditorState item)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            state.WorkspaceId, state.ContentRevision, state.SavedRevision,
            state.ActiveSectionId, state.ActiveSectionJson, state.Profile?.Created, state.Rules, Item = item
        })));

    private static string UnconfirmedLinkedOutcome() => PhoneStrings.Get("LinkedRunnerOutcomeUnconfirmed",
        "The link outcome could not be confirmed. Reopen this runner before trying again; local files were retained.");
}
