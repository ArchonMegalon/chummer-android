using System.Security.Cryptography;
using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>A local, disposable review handle; visible metadata is not write authority.</summary>
public sealed class NativeWorkspaceContinuationReview : IDisposable
{
    private int _consumed;
    internal RunnerSessionCoordinator Issuer { get; }
    internal OwnerContextStamp Owner { get; }
    internal WorkspaceContinuationExport Expected { get; }
    internal WorkspaceSessionState Selection { get; }
    internal IWorkspaceContinuationReview CoreReview { get; }
    public WorkspaceContinuationRestoreResult Result => CoreReview.Result;
    public bool CanConfirm => Volatile.Read(ref _consumed) == 0
        && Result.Outcome is WorkspaceContinuationRestoreOutcome.Available or WorkspaceContinuationRestoreOutcome.AlreadyCurrent;
    public long ContentRevision => Expected.Snapshot.Workspace.ContentRevision;
    public long SavedRevision => Expected.Snapshot.Workspace.SavedRevision;
    internal NativeWorkspaceContinuationReview(RunnerSessionCoordinator issuer, OwnerContextStamp owner,
        WorkspaceContinuationExport expected, WorkspaceSessionState selection, IWorkspaceContinuationReview coreReview)
        => (Issuer, Owner, Expected, Selection, CoreReview) = (issuer, owner, expected, selection, coreReview);
    internal bool TryClaim(RunnerSessionCoordinator issuer) => ReferenceEquals(Issuer, issuer)
        && Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
    public void Dispose() { Interlocked.Exchange(ref _consumed, 1); CoreReview.Dispose(); }
}

/// <summary>The Core result survives failure or cancellation of screen activation.</summary>
public sealed record NativeWorkspaceContinuationOpenResult(
    WorkspaceContinuationRestoreResult Restore,
    NativeWorkspaceActivationReceipt? Activation);

public sealed partial class RunnerSessionCoordinator
{
    private sealed record ContinuationCatalog(OwnerContextStamp Owner, IReadOnlyList<AndroidWorkspaceContinuationItem> Items);
    private ContinuationCatalog? _continuationCatalog;
    private long _continuationCatalogGeneration;

    private bool IsContinuationOwnerCurrent(OwnerContextStamp original)
        => TryCaptureWorkspaceOwner(out OwnerContextStamp? current) && current == original && original.IsValid;

    public bool IsWorkspaceActivationCurrent(NativeWorkspaceActivationReceipt? receipt, NativeWorkspaceActivationKind kind)
        => receipt?.Matches(State, kind) == true
            && (receipt.OriginalOwner is not { } original || IsContinuationOwnerCurrent(original));

    public bool HasCompleteOnlineContinuation(AndroidOnlineCharacter character)
    {
        var catalog = Volatile.Read(ref _continuationCatalog);
        return catalog is not null && IsContinuationOwnerCurrent(catalog.Owner)
            && catalog.Items.Any(item => ReferenceEquals(item.Character, character) && item.Continuation is not null);
    }

    private void RetireContinuationCatalogOnAccountChange()
    {
        var catalog = Volatile.Read(ref _continuationCatalog);
        if (catalog is not null && !IsContinuationOwnerCurrent(catalog.Owner))
        {
            Interlocked.Increment(ref _continuationCatalogGeneration);
            Interlocked.Exchange(ref _continuationCatalog, null);
            _onlineCharacters = [];
            _groups = [];
            _chronicles = [];
        }
        // A real credential transition must rebind the local Shell as well as
        // its online cards. It does not adopt or delete another owner's files.
        if (_initialized && !IsWorkspaceOwnerInitialized()) _workspaceOwnerInitializationPending = true;
    }

    private async Task RefreshContinuationCatalogAsync(CancellationToken ct)
    {
        await _account.InitializeAsync(ct);
        long generation = Interlocked.Increment(ref _continuationCatalogGeneration);
        if (!_account.Snapshot.IsLinked || _account is not IAndroidWorkspaceContinuationTransport transport
            || !TryCaptureWorkspaceOwner(out OwnerContextStamp? captured) || captured is not { IsValid: true } original)
        {
            Interlocked.Exchange(ref _continuationCatalog, null);
            _onlineCharacters = [];
            _groups = [];
            _chronicles = [];
            NotifyChanged();
            return;
        }
        // Core carrier validation may cover many bounded snapshots. Keep JSON,
        // history and digest work off the Android synchronization context.
        var charactersTask = Task.Run(() => transport.ListContinuationsAsync(original, ct), ct);
        var groupsTask = _account.ListGroupsAsync(ct);
        string selectedGroupId = Preferences.Default.Get(SelectedGroupPreferenceKey, string.Empty);
        await Task.WhenAll(charactersTask, groupsTask);
        if (!IsContinuationOwnerCurrent(original) || Volatile.Read(ref _continuationCatalogGeneration) != generation) return;
        var catalog = new ContinuationCatalog(original, (await charactersTask).ToArray());
        var groups = await groupsTask;
        var selected = groups.FirstOrDefault(group => group.GroupId == selectedGroupId) ?? groups.FirstOrDefault();
        IReadOnlyList<AndroidChronicleProject> chronicles = selected is not null
            ? await _account.ListChroniclesAsync(selected.GroupId, ct) : [];
        if (_account is not AndroidAccountLinkService actual) return;
        var authority = new AndroidAccountOwnerContextAccessor(actual);
        if (!authority.TryAcquire(original, out var lease)) return;
        using (lease)
        {
            if (Volatile.Read(ref _continuationCatalogGeneration) != generation
                || Preferences.Default.Get(SelectedGroupPreferenceKey, string.Empty) != selectedGroupId) return;
            // One synchronous credential lease closes the account-transition
            // race at publication; no network or notification runs under it.
            _groups = groups;
            _chronicles = chronicles;
            Interlocked.Exchange(ref _continuationCatalog, catalog);
            _onlineCharacters = catalog.Items.Select(item => item.Character).ToArray();
        }
        NotifyChanged();
    }

    public async Task<NativeWorkspaceContinuationReview?> ReviewOnlineAsync(
        AndroidOnlineCharacter character, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        var catalog = Volatile.Read(ref _continuationCatalog);
        var item = catalog?.Items.SingleOrDefault(item => ReferenceEquals(item.Character, character));
        if (catalog is null || item?.Continuation is null || !IsContinuationOwnerCurrent(catalog.Owner)
            || _client is not IOwnerBoundWorkspaceContinuationClient continuation
            || _presenter is not IOwnerBoundWorkspaceContinuationPresenter)
        {
            _notice = PhoneStrings.Get("OnlineContinuationUnavailable", "This runner has no complete restorable workspace. Refresh or upload it from a supported client.");
            NotifyChanged();
            return null;
        }
        WorkspaceSessionState selection = State.Session;
        await _workspaceActivationGate.WaitAsync(ct);
        byte[]? bytes = null;
        IWorkspaceContinuationReview? review = null;
        try
        {
            if (!IsContinuationSelectionCurrent(catalog.Owner, selection)) return null;
            bytes = await Task.Run(() => WorkspaceContinuationCodec.Encode(item.Continuation, 512 * 1024), ct);
            if (!IsContinuationSelectionCurrent(catalog.Owner, selection)) return null;
            // A private decode prevents caller-mutable auxiliary/history arrays
            // from becoming the screen's expected state after confirmation.
            var expected = await Task.Run(() => WorkspaceContinuationCodec.TryDecodeCandidate(bytes, 512 * 1024, out var candidate)
                ? candidate : null, ct);
            if (expected is null || !IsContinuationSelectionCurrent(catalog.Owner, selection)) return null;
            review = await continuation.ReviewContinuationAsync(catalog.Owner, bytes, ct);
            if (!IsContinuationSelectionCurrent(catalog.Owner, selection)) return null;
            if (review.Result.Outcome == WorkspaceContinuationRestoreOutcome.Available
                && review.SnapshotDigest != expected!.SnapshotDigest) return null;
            if (review.Result.Outcome == WorkspaceContinuationRestoreOutcome.AlreadyCurrent
                && review.Result.Target?.SnapshotDigest != expected!.SnapshotDigest) return null;
            var handle = new NativeWorkspaceContinuationReview(this, catalog.Owner, expected, selection, review);
            review = null;
            return handle;
        }
        finally
        {
            review?.Dispose();
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            _workspaceActivationGate.Release();
            NotifyChanged();
        }
    }

    public async Task<NativeWorkspaceContinuationOpenResult> ConfirmOnlineAsync(
        NativeWorkspaceContinuationReview review, bool explicitlyConfirmed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!review.TryClaim(this)) return new(new(WorkspaceContinuationRestoreOutcome.ReviewConsumed), null);
        if (!explicitlyConfirmed) { review.Dispose(); return new(new(WorkspaceContinuationRestoreOutcome.Canceled), null); }
        WorkspaceContinuationRestoreResult result = new(WorkspaceContinuationRestoreOutcome.Unavailable);
        try
        {
            await _workspaceActivationGate.WaitAsync(ct);
        }
        catch { review.Dispose(); throw; }
        try
        {
            if (!IsContinuationSelectionCurrent(review.Owner, review.Selection)
                || _client is not IOwnerBoundWorkspaceContinuationClient continuation
                || _presenter is not IOwnerBoundWorkspaceContinuationPresenter presenter)
                return new(new(WorkspaceContinuationRestoreOutcome.Conflict, ReopenRequired: true), null);
            result = review.Result.Outcome == WorkspaceContinuationRestoreOutcome.AlreadyCurrent
                ? review.Result
                : await continuation.ConfirmContinuationAsync(review.Owner, review.CoreReview, true, ct);
            if (result.Outcome is not (WorkspaceContinuationRestoreOutcome.Applied
                or WorkspaceContinuationRestoreOutcome.Recovered or WorkspaceContinuationRestoreOutcome.AlreadyCurrent))
                return new(result, null);

            bool activated = await presenter.ActivateContinuationAsync(review.Owner, review.Expected, result.Receipt, ct);
            if (!activated || !IsContinuationOwnerCurrent(review.Owner))
            {
                _notice = PhoneStrings.Get("OnlineContinuationStored", "The workspace is stored locally. Reopen it to continue; do not repeat the restore.");
                return new(result, null);
            }
            var workspace = review.Expected.Snapshot.Workspace;
            await SyncShellAsync(ct);
            if (!IsContinuationOwnerCurrent(review.Owner) || State.WorkspaceId != workspace.Id
                || State.ContentRevision != workspace.ContentRevision || State.SavedRevision != workspace.SavedRevision)
                return new(result, null);
            RestorePlayState();
            return new(result, new(NativeWorkspaceActivationKind.OnlineCharacter, workspace.Id)
            {
                OriginalOwner = review.Owner,
                ExpectedContentRevision = workspace.ContentRevision,
                ExpectedSavedRevision = workspace.SavedRevision
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
            && result.Outcome is WorkspaceContinuationRestoreOutcome.Applied or WorkspaceContinuationRestoreOutcome.Recovered)
        {
            // Postcommit display/cancellation failure cannot hide the durable
            // receipt, issue another write, or be reported as a failed restore.
            _notice = PhoneStrings.Get("OnlineContinuationStored", "The workspace is stored locally. Reopen it to continue; do not repeat the restore.");
            return new(result, null);
        }
        finally
        {
            review.Dispose();
            _workspaceActivationGate.Release();
            try { NotifyChanged(); }
            catch (Exception ex) when (ex is not OutOfMemoryException
                && result.Outcome is WorkspaceContinuationRestoreOutcome.Applied or WorkspaceContinuationRestoreOutcome.Recovered)
            {
                // A faulty view subscriber cannot replace the committed Core
                // result with an exception or make the caller replay a restore.
            }
        }
    }

    private bool IsContinuationSelectionCurrent(OwnerContextStamp original, WorkspaceSessionState selection)
        => IsContinuationOwnerCurrent(original) && ReferenceEquals(State.Session, selection)
            && State.Session.OwnerContext == original && !State.IsDirty && State.ActiveWorkspace?.ConflictState is null;
}
