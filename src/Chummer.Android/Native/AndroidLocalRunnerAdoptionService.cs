using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

internal sealed record NativeLocalRunnerCandidate(OwnerContextStamp Owner, CharacterWorkspaceId WorkspaceId, string Name);

internal sealed class NativeLocalRunnerAdoptionReview
{
    internal required AndroidLocalRunnerAdoptionService Issuer { get; init; }
    internal required OwnerContextStamp Owner { get; init; }
    internal required WorkspaceLocalAdoptionReview Core { get; init; }
    internal required LocalRunnerBookAdoptionJournal Journal { get; init; }
}

internal sealed record LocalRunnerBookAdoptionJournal(string Owner, string Workspace, Guid Operation,
    string ReadingDigest, string SceneDigest, string TimelineDigest, string InputsDigest)
{
    public string Schema { get; init; } = "chummer.android.local-runner-book-adoption/v1";
}

/// <summary>
/// Explicit installation-local runner adoption. Core owns the claim; Android
/// carries only app-private book material. No network, XML import or rules replay.
/// Every public operation executes on a worker; no owner lease crosses an await.
/// </summary>
public sealed class AndroidLocalRunnerAdoptionService(
    string stateDirectory, IWorkspaceStore store, IOwnerContextAccessor owners,
    IOwnerBoundLifeModuleOriginService origin, OriginBookReadingStore readings,
    OriginBookSceneStore scenes, FileOriginDossierDraftTimelineStore timeline,
    LifeModuleCompletionDraftStore inputs, ICharacterFileQueries characterFiles)
{
    private readonly WorkspaceLocalAdoptionService _core = new(store, owners);
    private readonly object _gate = new();
    private readonly string _root = Path.Combine(stateDirectory, "local-runner-book-adoptions");
    private const int MaximumJournalBytes = 8192;
    private const int MaximumJournals = 512;

    internal Task<IReadOnlyList<NativeLocalRunnerCandidate>> ListAsync(OwnerContextStamp owner,
        CancellationToken ct = default) => Task.Run<IReadOnlyList<NativeLocalRunnerCandidate>>(() =>
        {
            lock (_gate)
            {
                using var lease = Acquire(owner);
                ct.ThrowIfCancellationRequested();
                var inventory = (store as IWorkspaceStoreInventory)?.Inspect();
                if (inventory?.Success != true || inventory.Value is null)
                    throw new IOException("The local runner list could not be read completely.");
                var result = new List<NativeLocalRunnerCandidate>();
                foreach (var entry in inventory.Value.OrderByDescending(item => item.LastUpdatedUtc))
                {
                    ct.ThrowIfCancellationRequested();
                    var local = store.Get(entry.Id);
                    if (!local.Success || local.Value is null)
                        throw new IOException("A local runner changed. Reload the list.");
                    var summary = characterFiles.ParseSummary(new CharacterDocument(local.Value.Document.Content));
                    string name = !string.IsNullOrWhiteSpace(summary.Alias) ? summary.Alias : summary.Name;
                    result.Add(new(owner, entry.Id, string.IsNullOrWhiteSpace(name)
                        ? PhoneStrings.Get("RunnerFallback", "Runner") : name));
                }
                return result;
            }
        }, ct);

    internal Task<NativeLocalRunnerAdoptionReview?> ReviewAsync(OwnerContextStamp owner,
        CharacterWorkspaceId workspace, CancellationToken ct = default)
        => Task.Run(() =>
        {
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                var review = _core.Review(owner, workspace);
                if (review is null) return null;
                using var lease = Acquire(owner);
                // Existing account data is not inferred to belong to this runner.
                // Even an apparently empty serialized destination must be reviewed
                // as a conflict before Core commits the account claim.
                readings.RequireAdoptionDestinationAbsent(owner.Owner.NormalizedValue, workspace.Value);
                scenes.RequireAdoptionDestinationAbsent(owner.Owner.NormalizedValue, workspace.Value);
                timeline.RequireAdoptionDestinationAbsent(owner.Owner.NormalizedValue, workspace.Value);
                inputs.RequireAdoptionDestinationAbsent(owner.Owner.NormalizedValue, workspace);
                return new NativeLocalRunnerAdoptionReview
                {
                    Issuer = this, Owner = owner, Core = review,
                    Journal = Capture(owner.Owner.NormalizedValue, workspace, review.OperationId)
                };
            }
        }, ct);

    internal Task<WorkspaceLocalAdoptionResult> ConfirmAsync(OwnerContextStamp owner,
        NativeLocalRunnerAdoptionReview review, bool explicitlyConfirmed, CancellationToken ct = default)
        => Task.Run(() =>
        {
            lock (_gate)
            {
                if (!explicitlyConfirmed || !ReferenceEquals(review.Issuer, this) || review.Owner != owner)
                    return new WorkspaceLocalAdoptionResult(WorkspaceLocalAdoptionOutcome.Rejected);
                using (var lease = Acquire(owner))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Capture(review.Journal.Owner, review.Core.WorkspaceId, review.Core.OperationId) != review.Journal)
                        return new WorkspaceLocalAdoptionResult(WorkspaceLocalAdoptionOutcome.Conflict);
                    readings.RequireAdoptionDestinationAbsent(review.Journal.Owner, review.Journal.Workspace);
                    scenes.RequireAdoptionDestinationAbsent(review.Journal.Owner, review.Journal.Workspace);
                    timeline.RequireAdoptionDestinationAbsent(review.Journal.Owner, review.Journal.Workspace);
                    inputs.RequireAdoptionDestinationAbsent(review.Journal.Owner, review.Core.WorkspaceId);
                    string path = JournalPath(review.Journal);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    OriginAdoptionFiles.CommitNewOrExact(path, JsonSerializer.SerializeToUtf8Bytes(review.Journal), () => true, ct);
                }
                var result = _core.Confirm(owner, review.Core, explicitlyConfirmed, ct);
                if (result.Receipt is null) return result;
                // The durable claim has committed. Cancellation now means resume
                // placement, not start another claim or lose the original files.
                return Finish(owner, review.Journal);
            }
        }, ct);

    /// <summary>Call before publishing the account roster or opening its runners.</summary>
    internal Task<bool> RecoverPendingAsync(OwnerContextStamp owner, CancellationToken ct = default)
        => Task.Run(() =>
        {
            lock (_gate)
            {
                if (owner.Owner.UsesLocalSingleUserValue) return true;
                string directory = OwnerDirectory(owner.Owner.NormalizedValue);
                string[] paths;
                using (var lease = Acquire(owner))
                {
                    if (!Directory.Exists(directory)) return true;
                    paths = Directory.EnumerateFiles(directory, "*.json").Take(MaximumJournals + 1).ToArray();
                    if (paths.Length > MaximumJournals) throw new InvalidDataException("Too many pending runner adoptions.");
                }
                var unclaimed = new List<LocalRunnerBookAdoptionJournal>();
                foreach (string path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    LocalRunnerBookAdoptionJournal journal;
                    using (var lease = Acquire(owner))
                    {
                        journal = JsonSerializer.Deserialize<LocalRunnerBookAdoptionJournal>(
                            OriginAdoptionFiles.Read(path, MaximumJournalBytes)!)
                            ?? throw new InvalidDataException("The adoption journal is invalid.");
                        if (journal.Schema != "chummer.android.local-runner-book-adoption/v1"
                            || journal.Owner != owner.Owner.NormalizedValue || journal.Operation == Guid.Empty
                            || string.IsNullOrWhiteSpace(journal.Workspace) || JournalPath(journal) != path)
                            throw new InvalidDataException("The adoption journal has a different identity.");
                        if (IsComplete(journal)) continue;
                    }
                    var result = Finish(owner, journal);
                    if (result.Outcome == WorkspaceLocalAdoptionOutcome.Rejected)
                    {
                        // Resolve admitted operations first: an older intention
                        // must not block a newer claim's interrupted placement
                        // merely because directory enumeration returned it first.
                        unclaimed.Add(journal);
                        continue;
                    }
                    if (result.Outcome is not (WorkspaceLocalAdoptionOutcome.Applied or WorkspaceLocalAdoptionOutcome.Recovered))
                        return false;
                }
                foreach (var journal in unclaimed)
                {
                    ct.ThrowIfCancellationRequested();
                    using var lease = Acquire(owner);
                    var id = new CharacterWorkspaceId(journal.Workspace);
                    // No Core claim: leave the intention and local data intact.
                    if (store.Get(id).Value is { LocalHistory.LocalAdoption: null }) continue;
                    // Only a real, different Core claim for this exact account
                    // and runner can supersede the old intention. Its own journal
                    // must have participated in this complete recovery pass.
                    var claimed = store.Get(owner.Owner, id).Value?.LocalHistory?.LocalAdoption;
                    if (claimed is not null && claimed.OwnerId == journal.Owner
                        && claimed.WorkspaceId == id && claimed.OperationId != journal.Operation
                        && paths.Contains(Path.Combine(directory, claimed.OperationId.ToString("N") + ".json"), StringComparer.Ordinal))
                        continue;
                    return false;
                }
                return true;
            }
        }, ct);

    private WorkspaceLocalAdoptionResult Finish(OwnerContextStamp owner, LocalRunnerBookAdoptionJournal journal)
    {
        var id = new CharacterWorkspaceId(journal.Workspace);
        var result = _core.Recover(owner, id, journal.Operation);
        if (result.Outcome != WorkspaceLocalAdoptionOutcome.Recovered || result.Receipt is not { } receipt
            || receipt.OwnerId != journal.Owner || receipt.WorkspaceId != id || receipt.OperationId != journal.Operation)
            return result;
        try
        {
            LifeModuleOriginDossierDraftCheckpoint? old;
            using (var lease = Acquire(owner))
            {
                if (IsComplete(journal)) return result;
                if (Capture(journal.Owner, id, journal.Operation) != journal)
                    return new(WorkspaceLocalAdoptionOutcome.RecoveryRequired, receipt);
                old = timeline.ReadForAdoption(OwnerScope.LocalSingleUser.NormalizedValue, journal.Workspace);
            }
            // Core validates the complete old checkpoint against the adopted
            // workspace, then changes its outer custodian only. History stays exact.
            LifeModuleOriginDossierDraftCheckpoint? rebound = null;
            if (old is not null)
            {
                var migrated = origin.AdoptLocalCheckpoint(owner, old);
                if (migrated.Outcome != LifeModuleOriginDossierOutcomes.Success || migrated.Value is null)
                    return new(WorkspaceLocalAdoptionOutcome.RecoveryRequired, receipt);
                rebound = migrated.Value;
            }
            using (var lease = Acquire(owner))
            {
                if (Capture(journal.Owner, id, journal.Operation) != journal)
                    return new(WorkspaceLocalAdoptionOutcome.RecoveryRequired, receipt);
                readings.AdoptLocalEdition(journal.Owner, journal.Workspace, journal.ReadingDigest, () => true, default);
                scenes.AdoptLocalScenes(journal.Owner, journal.Workspace, journal.SceneDigest, () => true, default);
                if (rebound is not null) timeline.RetainAdoptedCheckpoint(rebound, () => true, default);
                if (inputs.ReadForAdoption(OwnerScope.LocalSingleUser.NormalizedValue, id) is { } draft)
                    inputs.RetainAdoptedInputs(journal.Owner, draft, () => true, default);
                OriginAdoptionFiles.CommitNewOrExact(JournalPath(journal) + ".complete",
                    CompletionBytes(journal), () => true, default);
            }
            return result;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            return new(WorkspaceLocalAdoptionOutcome.RecoveryRequired, receipt);
        }
    }

    private LocalRunnerBookAdoptionJournal Capture(string account, CharacterWorkspaceId id, Guid operation)
    {
        string local = OwnerScope.LocalSingleUser.NormalizedValue;
        return new(account, id.Value, operation, readings.Load(local, id.Value).Digest,
            scenes.Load(local, id.Value).Digest,
            OriginAdoptionFiles.Digest(timeline.ReadForAdoption(local, id.Value)),
            OriginAdoptionFiles.Digest(inputs.ReadForAdoption(local, id)));
    }

    private IDisposable Acquire(OwnerContextStamp owner)
    {
        OriginAdoptionFiles.RequireAccount(owner.Owner.NormalizedValue);
        if (!owner.IsValid || owners is not IOwnerContextLeaseAccessor authority)
            throw new InvalidOperationException("The account changed. Reopen the runner list.");
        if (authority.TryAcquire(owner, out var lease) && lease is not null && lease.Stamp == owner)
            return lease;
        lease?.Dispose();
        throw new InvalidOperationException("The account changed. Reopen the runner list.");
    }

    private string OwnerDirectory(string owner) => Path.Combine(_root, OriginAdoptionFiles.Digest(owner));
    private string JournalPath(LocalRunnerBookAdoptionJournal journal)
        => Path.Combine(OwnerDirectory(journal.Owner), journal.Operation.ToString("N") + ".json");
    private static byte[] CompletionBytes(LocalRunnerBookAdoptionJournal journal)
        => JsonSerializer.SerializeToUtf8Bytes(new { journal.Operation, JournalDigest = OriginAdoptionFiles.Digest(journal) });
    private bool IsComplete(LocalRunnerBookAdoptionJournal journal)
    {
        var bytes = OriginAdoptionFiles.Read(JournalPath(journal) + ".complete", MaximumJournalBytes);
        if (bytes is null) return false;
        if (!bytes.AsSpan().SequenceEqual(CompletionBytes(journal))) throw new InvalidDataException("The adoption completion record changed.");
        return true;
    }
}
