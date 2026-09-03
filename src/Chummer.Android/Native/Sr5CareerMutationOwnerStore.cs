using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

internal static class Sr5CareerMutationDomains
{
    public const string ActiveSkillAdvance = "active-skill-advance";
    public const string AttributeAdvance = "attribute-advance";
    public const string SkillGroupAdvance = "skill-group-advance";
    public const string KnowledgeSkillAdvance = "knowledge-skill-advance";
    public const string QualityChange = "quality-change";
    public const string SkillSpecializationAdd = "skill-specialization-add";
    public const string AfterRunSettlement = "after-run-settlement";
    public const string DowntimeCalendar = "downtime-calendar";
    public const string PlaytimeDamage = "playtime-damage";
}

/// <summary>
/// Durable, cross-lane ownership for the single runner mutation that may be
/// unresolved at a time. Domain journals keep their exact typed payloads; this
/// record only binds the common ownership and applying-version boundary.
/// </summary>
internal sealed record Sr5CareerMutationOwner(
    int SchemaVersion,
    string Domain,
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long ApplyingCheckpointVersion,
    long ExpectedContentRevision,
    string IdempotencyKey)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsStructurallyValid()
        => SchemaVersion == CurrentSchemaVersion
            && !string.IsNullOrWhiteSpace(Domain)
            && !string.IsNullOrWhiteSpace(WorkspaceId)
            && OwnerId != Guid.Empty
            && ActionId != Guid.Empty
            && ApplyingCheckpointVersion >= 2
            && ExpectedContentRevision > 0
            && IdempotencyKey is { Length: 64 };
}

internal sealed record Sr5CareerMutationBeginResult(
    bool Success,
    bool ExactReviewedStateWasRestored,
    string Blocker);

internal sealed class PreferencesSr5CareerMutationOwnerBackend :
    ISr5CareerCheckpointBackend
{
    private const string StorageKey = "sr5.career.mutation-owner.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

/// <summary>
/// Combines a process-wide exclusion gate with a durable owner record. The
/// process lease may end after the API call, but the owner remains until the
/// exact domain journal has durably recorded an authoritative resolution.
/// </summary>
internal sealed class Sr5CareerMutationOwnerStore
{
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private readonly ISr5CareerCheckpointBackend _backend;

    public Sr5CareerMutationOwnerStore(ISr5CareerCheckpointBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public static Sr5CareerMutationOwnerStore CreateDefault()
        => new(new PreferencesSr5CareerMutationOwnerBackend());

    public static Sr5CareerMutationOwnerStore CreateIsolated()
        => new(new VolatileBackend());

    public bool TryBegin(
        Sr5CareerMutationOwner owner,
        Func<Sr5CareerMutationBeginResult> persistApplying,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(persistApplying);
        blocker = string.Empty;
        if (!owner.IsStructurallyValid())
        {
            blocker = "The shared Career mutation owner is incomplete or invalid.";
            return false;
        }
        if (!ProcessGate.Wait(0))
        {
            blocker = "Another Career mutation transition is running.";
            return false;
        }

        try
        {
            if (!TryReserveLocked(owner, out bool newlyReserved, out blocker))
            {
                return false;
            }

            Sr5CareerMutationBeginResult result;
            try
            {
                result = persistApplying();
            }
            catch (Exception exception)
            {
                blocker = $"The domain Applying journal failed: {exception.Message}";
                return false;
            }

            if (result.Success)
            {
                blocker = string.Empty;
                return true;
            }

            blocker = string.IsNullOrWhiteSpace(result.Blocker)
                ? "The exact domain journal did not enter Applying."
                : result.Blocker;
            if (result.ExactReviewedStateWasRestored)
            {
                if (TryReleaseLocked(owner, out string releaseBlocker))
                {
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(releaseBlocker))
                {
                    blocker += $" {releaseBlocker}";
                }
            }
            else if (!result.ExactReviewedStateWasRestored || !newlyReserved)
            {
                blocker += " The shared owner remains durable until the state is reconciled.";
            }
            return false;
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    public async Task<IDisposable> AcquireExecutionLeaseAsync(
        Sr5CareerMutationOwner owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await ProcessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryReadLocked(out Sr5CareerMutationOwner current, out string blocker)
                || current != owner)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(blocker)
                        ? "The exact durable Career mutation owner no longer exists."
                        : blocker);
            }
            return new ProcessLease();
        }
        catch
        {
            ProcessGate.Release();
            throw;
        }
    }

    public bool TryComplete(
        Sr5CareerMutationOwner owner,
        Func<(bool Success, string Blocker)> persistResolution,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(persistResolution);
        blocker = string.Empty;
        if (!ProcessGate.Wait(0))
        {
            blocker = "The owning Career mutation is still running.";
            return false;
        }

        try
        {
            if (!TryReadLocked(out Sr5CareerMutationOwner current, out blocker)
                || current != owner)
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact durable Career mutation owner may record its resolution."
                    : blocker;
                return false;
            }

            (bool success, string persistBlocker) = persistResolution();
            if (!success)
            {
                blocker = persistBlocker;
                return false;
            }
            if (!TryReleaseLocked(owner, out blocker))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "The domain outcome is durable, but its shared mutation owner could not be released."
                    : $"The domain outcome is durable, but {blocker}";
                return false;
            }
            return true;
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    public bool TryRunWhenUnowned(
        Func<(bool Success, string Blocker)> action,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(action);
        blocker = string.Empty;
        if (!ProcessGate.Wait(0))
        {
            blocker = "Another Career mutation transition is running.";
            return false;
        }
        try
        {
            if (TryReadLocked(out _, out string readBlocker))
            {
                blocker = "An unresolved Career mutation owns the runner.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(readBlocker))
            {
                blocker = readBlocker;
                return false;
            }
            (bool success, string actionBlocker) = action();
            blocker = actionBlocker;
            return success;
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    public bool TryReconcileResolved(
        Sr5CareerMutationOwner owner,
        Func<bool> exactResolvedDomainStateStillExists,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(exactResolvedDomainStateStillExists);
        blocker = string.Empty;
        if (!ProcessGate.Wait(0))
        {
            blocker = "Another Career mutation transition is running.";
            return false;
        }
        try
        {
            if (!TryReadLocked(out Sr5CareerMutationOwner current, out string readBlocker))
            {
                blocker = readBlocker;
                return string.IsNullOrWhiteSpace(readBlocker);
            }
            if (current != owner || !exactResolvedDomainStateStillExists())
            {
                blocker = "The durable mutation owner does not match an exact resolved domain journal.";
                return false;
            }
            return TryReleaseLocked(owner, out blocker);
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    private bool TryReserveLocked(
        Sr5CareerMutationOwner owner,
        out bool newlyReserved,
        out string blocker)
    {
        newlyReserved = false;
        if (TryReadLocked(out Sr5CareerMutationOwner existing, out string readBlocker))
        {
            if (existing == owner)
            {
                blocker = string.Empty;
                return true;
            }
            blocker = $"Career mutation '{existing.Domain}' already owns this runner transition.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(readBlocker))
        {
            blocker = readBlocker;
            return false;
        }

        try
        {
            _backend.Write(JsonSerializer.Serialize(owner));
            if (!TryReadLocked(out Sr5CareerMutationOwner readBack, out blocker)
                || readBack != owner)
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "The shared Career mutation owner write did not survive exact read-back."
                    : blocker;
                return false;
            }
            newlyReserved = true;
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The shared Career mutation owner could not be written: {exception.Message}";
            return false;
        }
    }

    private bool TryReleaseLocked(
        Sr5CareerMutationOwner expected,
        out string blocker)
    {
        if (!TryReadLocked(out Sr5CareerMutationOwner current, out blocker)
            || current != expected)
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The shared Career mutation owner changed before release."
                : blocker;
            return false;
        }
        try
        {
            _backend.Remove();
            if (!string.IsNullOrWhiteSpace(_backend.Read()))
            {
                blocker = "The shared Career mutation owner release was not durable on read-back.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The shared Career mutation owner could not be released: {exception.Message}";
            return false;
        }
    }

    private bool TryReadLocked(
        out Sr5CareerMutationOwner owner,
        out string blocker)
    {
        owner = null!;
        try
        {
            string payload = _backend.Read();
            if (string.IsNullOrWhiteSpace(payload))
            {
                blocker = string.Empty;
                return false;
            }
            owner = JsonSerializer.Deserialize<Sr5CareerMutationOwner>(payload)!;
            if (owner is null || !owner.IsStructurallyValid())
            {
                owner = null!;
                blocker = "The shared Career mutation owner is unreadable; it remains replay-blocking.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The shared Career mutation owner is unreadable and replay-blocking: {exception.Message}";
            return false;
        }
    }

    private sealed class ProcessLease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ProcessGate.Release();
            }
        }
    }

    private sealed class VolatileBackend : ISr5CareerCheckpointBackend
    {
        private string _payload = string.Empty;

        public string Read() => _payload;
        public void Write(string payload) => _payload = payload;
        public void Remove() => _payload = string.Empty;
    }
}
