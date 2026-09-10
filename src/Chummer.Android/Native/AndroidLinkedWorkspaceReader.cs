using System.Text.Json;
using System.Xml;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed record AndroidLinkedOwner(string Scope, bool TrustedLocal);

/// <summary>A current persisted observation, never a link-operation commit receipt.</summary>
public sealed record AndroidLinkedWorkspaceSnapshot(
    AndroidLinkedOwner Owner,
    string WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string DocumentAuthoritySha256,
    WorkspaceCollectionEditorState? Editor)
{
    // Only populated for an explicit link mutation preview, computed from this
    // exact baseline by the shared Presentation mutation catalog. Never persisted
    // by the reader. The normal Contacts DTO is lossy for empty linked fields.
    public string? ExpectedDocumentAuthoritySha256 { get; init; }
    // Display-only canonical projection. Fallback values here are not proof of
    // literal saved identity fields; recovery uses the full document digest.
    public IReadOnlyList<CharacterContactSummary> Contacts { get; init; } = [];
}

public interface IAndroidLinkedWorkspaceReader
{
    bool IsAvailable { get; }
    AndroidLinkedOwner CurrentOwner { get; }
    Task<OwnerContextStamp> CaptureOwnerContextAsync(CancellationToken cancellationToken);
    Task<AndroidLinkedOwner> ReadCurrentOwnerAsync(CancellationToken cancellationToken);
    Task<AndroidLinkedWorkspaceSnapshot?> ReadAsync(
        CharacterWorkspaceId workspaceId, string sectionId, CancellationToken cancellationToken);
    Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(CharacterWorkspaceId workspaceId,
        string sectionId, WorkspaceCollectionMutationRequest request, CancellationToken cancellationToken)
        => Task.FromResult<AndroidLinkedWorkspaceSnapshot?>(null);
    Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId, string sectionId, WorkspaceCollectionMutationRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult<AndroidLinkedWorkspaceSnapshot?>(null);
}

/// <summary>
/// Owner-scoped observations from the existing in-process runtime store. Composition
/// must supply the same store and owner accessor used by that runtime's client; no
/// independent store, account label, or deserialized owner grants read authority.
/// Matching current bytes cannot establish which operation produced those bytes.
/// </summary>
public sealed class AndroidLinkedWorkspaceReader : IAndroidLinkedWorkspaceReader
{
    private static readonly JsonSerializerOptions SectionJson = new(JsonSerializerDefaults.Web);
    private readonly IWorkspaceStore _store;
    private readonly IOwnerContextAccessor _owners;
    private readonly IOwnerBoundWorkspaceMutationClient? _ownerClient;
    private readonly IRulesetWorkspaceCodecResolver _codecs;

    public AndroidLinkedWorkspaceReader(IChummerClient client, IWorkspaceStore store,
        IOwnerContextAccessor owners, IRulesetWorkspaceCodecResolver codecs)
    {
        ArgumentNullException.ThrowIfNull(client);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _owners = owners ?? throw new ArgumentNullException(nameof(owners));
        _ownerClient = client as IOwnerBoundWorkspaceMutationClient;
        _codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
        // HTTP clients and in-memory compatibility stores cannot establish the
        // owner-bound persisted authority of this Android file-runtime adapter.
        IsAvailable = client is InProcessChummerClient && store is FileWorkspaceStore
            && owners is IOwnerContextLeaseAccessor && _ownerClient is not null;
    }

    public bool IsAvailable { get; }

    public AndroidLinkedOwner CurrentOwner
    {
        get
        {
            if (!IsAvailable)
                throw new InvalidOperationException("Owner-bound local linked-runner reads are unavailable.");
            return Describe(CaptureOwnerContext().Owner);
        }
    }

    /// <summary>
    /// Capture from the actual mutation client and validate against the same
    /// authority supplied to this reader. An accessor that only exposes Current
    /// cannot be upgraded into a lease authority by polling its owner name.
    /// </summary>
    public async Task<OwnerContextStamp> CaptureOwnerContextAsync(CancellationToken cancellationToken)
        => await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            OwnerContextStamp stamp = CaptureOwnerContext();
            cancellationToken.ThrowIfCancellationRequested();
            return stamp;
        }, CancellationToken.None).ConfigureAwait(false);

    private OwnerContextStamp CaptureOwnerContext()
    {
        if (!IsAvailable || _owners is not IOwnerContextLeaseAccessor owners)
            throw new InvalidOperationException("The actual local owner lease authority is unavailable.");
        OwnerContextStamp stamp = _ownerClient!.CaptureOwnerContext();
        if (!stamp.IsValid || !owners.TryAcquire(stamp, out var lease))
            throw new InvalidOperationException("The reader and mutation client no longer share this live owner authority.");
        using (lease)
        {
            if (lease.Stamp != stamp)
                throw new InvalidOperationException("The acquired owner authority differs from its requested stamp.");
            _ = Describe(lease.Stamp.Owner);
        }
        return stamp;
    }

    // The real install owner accessor reads its state file. Join that read off
    // the native event caller, including when cancellation arrives during I/O.
    public async Task<AndroidLinkedOwner> ReadCurrentOwnerAsync(CancellationToken cancellationToken)
        => await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            AndroidLinkedOwner owner = CurrentOwner;
            cancellationToken.ThrowIfCancellationRequested();
            return owner;
        // The async boundary preserves canceled task status with the caller's
        // token; the noncancelable worker still joins any owner I/O already begun.
        }, CancellationToken.None).ConfigureAwait(false);

    public async Task<AndroidLinkedWorkspaceSnapshot?> ReadAsync(
        CharacterWorkspaceId workspaceId, string sectionId, CancellationToken cancellationToken)
        // Do not allow scheduling cancellation to detach work from its caller.
        // All store reads, owner-file access, hashing and canonical parsing are
        // joined off the caller's synchronization context.
        => await Task.Run(() => ReadCore(workspaceId, sectionId, null, null, cancellationToken),
            CancellationToken.None).ConfigureAwait(false);

    public async Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(CharacterWorkspaceId workspaceId,
        string sectionId, WorkspaceCollectionMutationRequest request, CancellationToken cancellationToken)
        => await Task.Run(() => ReadCore(workspaceId, sectionId, request, null, cancellationToken),
            CancellationToken.None).ConfigureAwait(false);

    public async Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId, string sectionId, WorkspaceCollectionMutationRequest request,
        CancellationToken cancellationToken)
        => await Task.Run(() => ReadCore(workspaceId, sectionId, request, expectedOwner, cancellationToken),
            CancellationToken.None).ConfigureAwait(false);

    private AndroidLinkedWorkspaceSnapshot? ReadCore(
        CharacterWorkspaceId workspaceId, string sectionId, WorkspaceCollectionMutationRequest? request,
        OwnerContextStamp? expectedOwner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable || string.IsNullOrWhiteSpace(workspaceId.Value)
            || sectionId is not ("contacts" or "pets"))
            return null;

        try
        {
            OwnerContextStamp stamp = expectedOwner ?? CaptureOwnerContext();
            // Explicit preview callers still have to name this mutation
            // client's issuer, not just a stamp accepted by an unrelated reader
            // accessor. Compare against the current capture; never replace the
            // caller's original stamp with that newer capture.
            if (expectedOwner.HasValue && _ownerClient!.CaptureOwnerContext() != stamp) return null;
            if (_owners is not IOwnerContextLeaseAccessor owners
                || !stamp.IsValid || !owners.TryAcquire(stamp, out var lease)) return null;
            // The actual owner writer is excluded for the entire synchronous
            // file-store read/projection. Acquire/use/dispose on this worker;
            // never carry a thread-affine Core lease across an await.
            using var readLease = lease;
            if (readLease.Stamp != stamp) return null;
            OwnerScope liveOwner = readLease.Stamp.Owner;
            AndroidLinkedOwner owner = Describe(liveOwner);
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceStoreReadResult firstRead = ReadOwned(liveOwner, workspaceId);
            cancellationToken.ThrowIfCancellationRequested();
            if (!firstRead.Success || firstRead.Value is not { } first || !IsExact(first, workspaceId))
                return null;
            string firstDigest = RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(first.Document);

            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceStoreReadResult secondRead = ReadOwned(liveOwner, workspaceId);
            cancellationToken.ThrowIfCancellationRequested();
            if (!secondRead.Success || secondRead.Value is not { } second || !IsExact(second, workspaceId)
                || first.ContentRevision != second.ContentRevision
                || first.SavedRevision != second.SavedRevision
                || first.LastUpdatedUtc != second.LastUpdatedUtc)
                return null;
            string secondDigest = RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(second.Document);
            if (!string.Equals(firstDigest, secondDigest, StringComparison.Ordinal))
                return null;

            cancellationToken.ThrowIfCancellationRequested();
            // Both the hash and typed editor come from this same stored document.
            // No ownerless client re-read, UI-state inference or Android XML rules.
            var envelope = second.Document.PayloadEnvelope;
            object section = _codecs.Resolve(envelope.RulesetId).ParseSection(sectionId, envelope);
            if (section is not CharacterContactsSection contacts) return null;
            WorkspaceCollectionEditorState? editor = WorkspaceCollectionEditorProjector.TryProject(
                sectionId, JsonSerializer.SerializeToNode(contacts, SectionJson));
            string? expectedDigest = null;
            if (request is not null)
            {
                if (request is not (WorkspaceSetLinkedCharacterRequest or WorkspaceRemoveLinkedCharacterRequest)
                    || request.Target.Kind != (sectionId == "contacts" ? WorkspaceCollectionKind.Contact : WorkspaceCollectionKind.Pet))
                    return null;
                // Same pure shared transformation and envelope replacement used
                // by CharacterOverviewPresenter; no Android XML mutation rules.
                WorkspaceDocument replacement = WorkspaceLinkedCharacterMutationPreview.Create(second.Document, request);
                expectedDigest = RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(replacement);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(owner, second.Id.Value, second.ContentRevision, second.SavedRevision, secondDigest, editor)
            {
                ExpectedDocumentAuthoritySha256 = expectedDigest,
                Contacts = Array.AsReadOnly(contacts.Contacts.ToArray())
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or JsonException or XmlException
            or NotSupportedException or FormatException or OverflowException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Missing, corrupt, conflicting or unavailable observations do not
            // resolve intent, establish non-commit, or authorize another mutation.
            return null;
        }
    }

    private WorkspaceStoreReadResult ReadOwned(OwnerScope liveOwner, CharacterWorkspaceId workspaceId)
        // Match WorkspaceService.ScopedStoreAccess exactly. The explicit scoped
        // FileWorkspaceStore API rejects even a trusted LocalSingleUser sentinel;
        // only a live runtime-owned sentinel may select its local overload.
        // A serialized scope string never grants this capability.
        => liveOwner.IsLocalSingleUser ? _store.Get(workspaceId) : _store.Get(liveOwner, workspaceId);

    private static bool IsExact(WorkspaceStoredDocument saved, CharacterWorkspaceId requested)
        => string.Equals(saved.Id.Value, requested.Value, StringComparison.Ordinal)
            && saved.ContentRevision > 0 && saved.SavedRevision >= 0
            && saved.SavedRevision <= saved.ContentRevision
            && saved.Document is not null && saved.Document.State is not null
            && saved.Document.Format == WorkspaceDocumentFormat.NativeXml
            && !string.IsNullOrWhiteSpace(saved.Document.RulesetId);

    private static AndroidLinkedOwner Describe(OwnerScope owner)
    {
        if (string.IsNullOrWhiteSpace(owner.NormalizedValue)
            || owner.UsesLocalSingleUserValue && !owner.IsLocalSingleUser)
            throw new InvalidOperationException("The current runtime owner scope is unavailable or untrusted.");
        // Never construct a privileged OwnerScope from these serialized fields.
        return new(owner.NormalizedValue, owner.IsLocalSingleUser);
    }
}
