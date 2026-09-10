using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Presentation;
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
        OwnerContextStamp owner = await RequireLinkedOwnerStampAsync(expected, cancellationToken);
        if (!LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;

        // The platform service returns a fresh exclusively-created staging file.
        // Selection remains free to change while Android's picker is open.
        AndroidStagedLinkedCharacter? staged = await _linkedCharacters.StageAsync(target, cancellationToken);
        if (staged is null) return false;
        bool dispatched = false;
        bool custodyAttempted = false;
        try
        {
            return await WithWorkspaceActivationGateAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await LinkedOwnerStampIsCurrentAsync(owner, cancellationToken)
                    || !LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;
                var request = new WorkspaceSetLinkedCharacterRequest(
                    target, staged.FileName, staged.RelativeFileName, staged.DisplayName, staged.Identity);
                AndroidLinkedCharacterIntent intent = await CaptureLinkedIntentAsync(
                    owner, expected, authority, request, staged, cancellationToken);
                if (!await LinkedOwnerStampIsCurrentAsync(owner, cancellationToken)
                    || !LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;
                custodyAttempted = true;
                await Task.Run(() => _linkedJournal!.Begin(intent), CancellationToken.None);
                // The final editor check follows owner-file I/O: navigation
                // during that await must never dispatch into another workspace.
                bool currentOwner = await LinkedOwnerStampIsCurrentAsync(owner, CancellationToken.None);
                using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (!currentOwner || !LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector))
                    dispatchCancellation.Cancel();
                dispatched = true; // Escaping exceptions are unknown, never non-dispatch evidence.
                OwnerBoundWorkspaceMutationDispatch dispatch = await ApplyOwnerBoundLinkedMutationCoreAsync(
                    request, owner, dispatchCancellation.Token);
                if (dispatch == OwnerBoundWorkspaceMutationDispatch.NotDispatched)
                {
                    await AbandonUndispatchedLinkedIntentAsync(intent);
                    dispatched = false;
                    custodyAttempted = false;
                    cancellationToken.ThrowIfCancellationRequested();
                    return false;
                }
                bool observed = LinkedSuccessorIsCurrent(expected)
                    && UniqueLinkedItem(State, target)?.LinkedCharacter is { IsLinked: true, IdentityResolved: true } linked
                    && linked.FileName == staged.FileName && linked.RelativeFileName == staged.RelativeFileName
                    && linked.DisplayName == staged.DisplayName;
                observed = observed && await ObserveLinkedIntentAsync(intent, cancellationToken);
                _notice = observed ? $"Linked {staged.Identity.CharacterName}." : UnconfirmedLinkedOutcome();
                NotifyChanged();
                return observed;
            }, cancellationToken);
        }
        catch
        {
            if (dispatched || custodyAttempted) { _notice = UnconfirmedLinkedOutcome(); NotifyChanged(); }
            throw;
        }
        finally
        {
            if (!dispatched && !custodyAttempted)
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
        OwnerContextStamp owner = await RequireLinkedOwnerStampAsync(expected, cancellationToken);
        if (!LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;
        return await WithWorkspaceActivationGateAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await LinkedOwnerStampIsCurrentAsync(owner, cancellationToken)
                || !LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;
            try
            {
                var request = new WorkspaceRemoveLinkedCharacterRequest(target);
                AndroidLinkedCharacterIntent intent = await CaptureLinkedIntentAsync(
                    owner, expected, authority, request, null, cancellationToken);
                if (!await LinkedOwnerStampIsCurrentAsync(owner, cancellationToken)
                    || !LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector)) return false;
                await Task.Run(() => _linkedJournal!.Begin(intent), CancellationToken.None);
                bool currentOwner = await LinkedOwnerStampIsCurrentAsync(owner, CancellationToken.None);
                using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (!currentOwner || !LinkedEditorIsCurrent(expected, target, authority, isCurrentInspector))
                    dispatchCancellation.Cancel();
                OwnerBoundWorkspaceMutationDispatch dispatch = await ApplyOwnerBoundLinkedMutationCoreAsync(
                    request, owner, dispatchCancellation.Token);
                if (dispatch == OwnerBoundWorkspaceMutationDispatch.NotDispatched)
                {
                    await AbandonUndispatchedLinkedIntentAsync(intent);
                    cancellationToken.ThrowIfCancellationRequested();
                    return false;
                }
                bool observed = LinkedSuccessorIsCurrent(expected)
                    && UniqueLinkedItem(State, target)?.LinkedCharacter is { IsLinked: false, FileName.Length: 0, RelativeFileName.Length: 0 };
                observed = observed && await ObserveLinkedIntentAsync(intent, cancellationToken);
                _notice = observed ? "Linked runner removed." : UnconfirmedLinkedOutcome();
                NotifyChanged();
                // Old files remain available to other workspaces and recovery.
                return observed;
            }
            catch { _notice = UnconfirmedLinkedOutcome(); NotifyChanged(); throw; }
        }, cancellationToken);
    }

    private async Task<AndroidLinkedOwner> RequireLinkedOwnerAsync(CancellationToken token)
    {
        if (_linkedJournal is null || _linkedWorkspaceReader?.IsAvailable != true)
            throw new InvalidOperationException(PhoneStrings.Get("LinkedRecoveryUnavailable",
                "Linked-runner recovery is unavailable. No link was changed."));
        return await _linkedWorkspaceReader.ReadCurrentOwnerAsync(token);
    }

    private async Task<OwnerContextStamp> RequireLinkedOwnerStampAsync(CharacterOverviewState expected, CancellationToken token)
    {
        if (_linkedJournal is null || _linkedWorkspaceReader?.IsAvailable != true
            || _presenter is not IOwnerBoundWorkspaceMutationPresenter
            || expected.DisplayOwnerContext is not { IsValid: true } originalOwner)
            throw new InvalidOperationException(PhoneStrings.Get("LinkedRecoveryUnavailable",
                "Linked-runner recovery is unavailable. No link was changed."));
        // Obtain display provenance synchronously from the exact immutable frame,
        // never from a newly scheduled owner-file read. The background read below
        // validates that original authority; it cannot grant B authority to an A
        // view even when both partitions have identical workspace/editor bytes.
        OwnerContextStamp current = await _linkedWorkspaceReader.CaptureOwnerContextAsync(token);
        if (current != originalOwner)
            throw new InvalidOperationException("The displayed runner belongs to an earlier owner context. Reopen it before linking.");
        // Keep this original stamp transient through every subsequent await.
        return originalOwner;
    }

    private async Task<bool> LinkedOwnerStampIsCurrentAsync(OwnerContextStamp owner, CancellationToken token)
    {
        if (_disposed || _linkedWorkspaceReader?.IsAvailable != true) return false;
        OwnerContextStamp current = await _linkedWorkspaceReader.CaptureOwnerContextAsync(token);
        return !_disposed && current == owner;
    }

    private async Task<OwnerBoundWorkspaceMutationDispatch> ApplyOwnerBoundLinkedMutationCoreAsync(
        WorkspaceCollectionMutationRequest request, OwnerContextStamp originalOwner, CancellationToken token)
    {
        var presenter = _presenter as IOwnerBoundWorkspaceMutationPresenter
            ?? throw new InvalidOperationException("Owner-bound linked mutation dispatch is unavailable.");
        OwnerBoundWorkspaceMutationDispatch dispatch = await presenter.ApplyCollectionMutationAsync(
            request, originalOwner, token);
        if (dispatch != OwnerBoundWorkspaceMutationDispatch.NotDispatched)
        {
            _notice = State.Error is null ? "Runner item updated." : null;
            await SyncShellAsync(token);
            NotifyChanged();
        }
        return dispatch;
    }

    private async Task<bool> LinkedOwnerIsCurrentAsync(AndroidLinkedOwner owner, CancellationToken token)
    {
        if (_disposed || _linkedWorkspaceReader?.IsAvailable != true) return false;
        AndroidLinkedOwner current = await _linkedWorkspaceReader.ReadCurrentOwnerAsync(token);
        return !_disposed && current == owner;
    }

    private async Task<AndroidLinkedCharacterIntent> CaptureLinkedIntentAsync(OwnerContextStamp owner,
        CharacterOverviewState expected, string editorAuthority, WorkspaceCollectionMutationRequest request,
        AndroidStagedLinkedCharacter? staged, CancellationToken token)
    {
        AndroidLinkedWorkspaceSnapshot? snapshot = await _linkedWorkspaceReader!.ReadForMutationAsync(
            owner, expected.WorkspaceId!.Value, expected.ActiveSectionId!, request, token);
        if (snapshot is null || snapshot.Owner != new AndroidLinkedOwner(owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser)
            || !await LinkedOwnerStampIsCurrentAsync(owner, token)
            || snapshot.WorkspaceId != expected.WorkspaceId.Value.Value
            || snapshot.ContentRevision != expected.ContentRevision || snapshot.SavedRevision != expected.SavedRevision
            || snapshot.ExpectedDocumentAuthoritySha256 is not { Length: 64 }
            || snapshot.ExpectedDocumentAuthoritySha256 == snapshot.DocumentAuthoritySha256
            || JsonSerializer.Serialize(snapshot.Editor) != JsonSerializer.Serialize(expected.ActiveCollectionEditor))
            throw new InvalidOperationException("The exact linked-runner document changed before intent capture.");
        if (staged is not null && !await _linkedCharacters.MatchesStagedFileAsync(
            request.Target, staged.FileName, staged.ContentSha256, token))
            throw new InvalidDataException("The selected linked-runner file changed before dispatch.");
        // Persist only stable owner scope. The transient authority epoch is not
        // a filesystem partition, operation identity or historical recovery key.
        return new(Guid.NewGuid(), owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser, snapshot.WorkspaceId,
            snapshot.ContentRevision, snapshot.SavedRevision, snapshot.DocumentAuthoritySha256, snapshot.ExpectedDocumentAuthoritySha256,
            editorAuthority, expected.ActiveSectionId!, request.Target, staged is not null,
            Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, request.GetType()))),
            staged?.FileName, staged?.ContentSha256, request as WorkspaceSetLinkedCharacterRequest);
    }

    private Task AbandonUndispatchedLinkedIntentAsync(AndroidLinkedCharacterIntent intent)
        // Called only after a joined typed NotDispatched result, while this
        // invocation still holds the activation gate. Never a recovery action.
        => Task.Run(() => _linkedJournal!.AbandonBeforeDispatch(intent.OperationId,
            intent.OwnerScope, intent.TrustedLocalOwner, intent.WorkspaceId), CancellationToken.None);

    internal async Task<IReadOnlyList<AndroidLinkedCharacterIntentRecord>> ReadLinkedCharacterHistoryAsync(
        CancellationToken token = default)
    {
        AndroidLinkedOwner owner = await RequireLinkedOwnerAsync(token);
        var records = await Task.Run(() => _linkedJournal!.ReadAll(owner.Scope, owner.TrustedLocal), CancellationToken.None);
        if (!await LinkedOwnerIsCurrentAsync(owner, token)) throw new InvalidOperationException("The runtime owner changed during history read.");
        return records;
    }

    internal async Task<bool> CheckLinkedCharacterIntentAsync(Guid operationId, CancellationToken token = default)
    {
        var records = await ReadLinkedCharacterHistoryAsync(token);
        var record = records.SingleOrDefault(entry => entry.Intent.OperationId == operationId)
            ?? throw new InvalidOperationException("The original link intent is unavailable for this owner.");
        if (record.EffectObserved || record.NotDispatched) return record.EffectObserved;
        // Reads the original workspace even when it is no longer selected or the
        // target has vanished. Never loads it into the editor or replays a write.
        return await ObserveLinkedIntentAsync(record.Intent, token);
    }

    private async Task<bool> ObserveLinkedIntentAsync(AndroidLinkedCharacterIntent intent, CancellationToken token)
    {
        var owner = new AndroidLinkedOwner(intent.OwnerScope, intent.TrustedLocalOwner);
        if (!await LinkedOwnerIsCurrentAsync(owner, token)) return false;
        AndroidLinkedWorkspaceSnapshot? snapshot = await _linkedWorkspaceReader!.ReadAsync(
            new(intent.WorkspaceId), intent.SectionId, token);
        if (snapshot is null || snapshot.Owner != owner || snapshot.WorkspaceId != intent.WorkspaceId
            || snapshot.ContentRevision != intent.ContentRevision + 1 || snapshot.SavedRevision != intent.SavedRevision
            || snapshot.DocumentAuthoritySha256 != intent.ExpectedDocumentAuthoritySha256)
            return false;
        var items = snapshot.Editor?.Items.Where(item => CollectionItemEditorPage.TargetsMatch(item.Target, intent.Target)).ToArray() ?? [];
        if (items.Length != 1 || items[0].LinkedCharacter is not { } link) return false;
        if (intent.Attach)
        {
            if (intent.Attachment is not { } attachment || !link.IsLinked || !link.IdentityResolved
                || link.FileName != attachment.FileName || link.RelativeFileName != attachment.RelativeFileName
                || link.DisplayName != attachment.DisplayName
                || !await _linkedCharacters.MatchesStagedFileAsync(intent.Target, attachment.FileName,
                    intent.StagedFileSha256!, token)) return false;
            // Exact full-document equality above covers literal persisted linked
            // identity fields (including empty values), paths, and unrelated data.
            // The display projection alone intentionally cannot establish that.
        }
        else if (link.IsLinked || link.FileName.Length != 0 || link.RelativeFileName.Length != 0) return false;
        var after = await _linkedWorkspaceReader.ReadAsync(new(intent.WorkspaceId), intent.SectionId, token);
        if (after is null || after.Owner != owner || after.WorkspaceId != snapshot.WorkspaceId
            || after.ContentRevision != snapshot.ContentRevision || after.SavedRevision != snapshot.SavedRevision
            || after.DocumentAuthoritySha256 != snapshot.DocumentAuthoritySha256
            || !await LinkedOwnerIsCurrentAsync(owner, token)) return false;
        try
        {
            await Task.Run(() => _linkedJournal!.Observe(intent.OperationId, intent.OwnerScope, intent.TrustedLocalOwner,
                intent.WorkspaceId, snapshot.DocumentAuthoritySha256, snapshot.ContentRevision, snapshot.SavedRevision), CancellationToken.None);
        }
        catch (IOException)
        {
            // Another read-only check (or the original command's observation)
            // may have won the append-only journal CAS after both read pending.
            // Reconcile only the identical acknowledged observation; never retry
            // publication or treat readable but unacknowledged bytes as success.
            if (!await LinkedOwnerIsCurrentAsync(owner, token)) return false;
            AndroidLinkedCharacterIntentRecord? recorded = await Task.Run(() =>
                _linkedJournal!.Read(intent.OwnerScope, intent.TrustedLocalOwner, intent.WorkspaceId)
                    .SingleOrDefault(record => record.Intent.OperationId == intent.OperationId), CancellationToken.None);
            if (recorded?.Intent != intent || !recorded.EffectObserved
                || recorded.Observation is not { Outcome: "current-effect-observed" } observation
                || observation.DocumentAuthoritySha256 != snapshot.DocumentAuthoritySha256
                || observation.ContentRevision != snapshot.ContentRevision
                || observation.SavedRevision != snapshot.SavedRevision)
                throw;
        }
        // This is a recorded current-state observation, NOT a Core operation
        // receipt, historical attribution, or permission to replay the command.
        return await LinkedOwnerIsCurrentAsync(owner, token);
    }

    private bool LinkedSuccessorIsCurrent(CharacterOverviewState expected)
        => !_disposed && !State.IsBusy && State.Error is null && State.WorkspaceId == expected.WorkspaceId
            && State.DisplayOwnerContext == expected.DisplayOwnerContext
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
            && current.DisplayOwnerContext == expected.DisplayOwnerContext
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
        "The link outcome could not be confirmed. Open Linked-runner recovery from Runners; do not retry. Local files were retained.");
}
