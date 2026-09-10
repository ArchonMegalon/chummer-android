using System.Security.Cryptography;
using System.Text;

namespace Chummer.Android.Platform;

// Local storage identity only. The authenticated subject remains unchanged on
// the wire and in grant bindings; Core normalizes owner keys to lowercase.
internal static class AndroidAccountOwnerKey
{
    private const string Domain = "chummer-install-account-owner-v1\0";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool TryCreate(string? subject, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (subject is not { Length: > 0 }) return false;
        try
        {
            ownerKey = "install-account-v1:" + Convert.ToHexStringLower(
                SHA256.HashData(StrictUtf8.GetBytes(Domain + subject)));
            return true;
        }
        catch (EncoderFallbackException)
        {
            // Invalid UTF-16 must not alias a different subject through UTF-8
            // replacement characters. No legacy owner-key fallback is allowed.
            return false;
        }
    }
}

// Process-local authority owned by the real account service. Core adapters
// share its credential gate; a Linked UI snapshot is deliberately not an input.
internal sealed class AndroidAccountOwnerAuthority
{
    private readonly SemaphoreSlim _credentialGate;
    private readonly TimeProvider _clock;
    private readonly string _issuer = Guid.NewGuid().ToString("N");
    private AndroidAccountOwnerState _current;

    internal AndroidAccountOwnerAuthority(SemaphoreSlim credentialGate, TimeProvider clock)
    {
        _credentialGate = credentialGate;
        _clock = clock;
        _current = new(_issuer, 0, false, null, null, null, null);
    }

    internal long Revision => Volatile.Read(ref _current).Revision;

    internal bool MatchesLinked(string installation, string grant, string? subject, DateTimeOffset expiry)
    {
        AndroidAccountOwnerState current = Volatile.Read(ref _current);
        return subject is not null && !current.DeviceLocal && current.InstallationId == installation
            && current.GrantId == grant && current.SubjectId == subject && current.ExpiresAtUtc == expiry;
    }

    internal AndroidAccountOwnerState? Capture()
    {
        AndroidAccountOwnerState current = Volatile.Read(ref _current);
        return current.DeviceLocal || (current.SubjectId is not null && current.ExpiresAtUtc > _clock.GetUtcNow())
            ? current : null;
    }

    internal bool TryAcquire(AndroidAccountOwnerState expected, out IDisposable? lease)
    {
        lease = null;
        if (!_credentialGate.Wait(0)) return false;
        if (!ReferenceEquals(Capture(), expected))
        {
            _credentialGate.Release();
            return false;
        }
        lease = new CredentialLease(_credentialGate);
        return true;
    }

    // All publishers run under the credential gate held by the account service.
    // Readers remain non-blocking; only lease admission competes with writers.
    internal void Invalidate() => Publish(false, null, null, null, null);
    internal void PublishLocal() => Publish(true, null, null, null, null);
    internal void PublishLinked(string installationId, string grantId, string subjectId, DateTimeOffset expiry)
        => Publish(false, installationId, grantId, subjectId, expiry);

    private void Publish(bool local, string? installation, string? grant, string? subject, DateTimeOffset? expiry)
    {
        if (_credentialGate.CurrentCount != 0)
            throw new InvalidOperationException("Account owner publication requires credential exclusion.");
        AndroidAccountOwnerState previous = Volatile.Read(ref _current);
        if (previous.DeviceLocal == local && previous.InstallationId == installation
            && previous.GrantId == grant && previous.SubjectId == subject && previous.ExpiresAtUtc == expiry)
            return;
        long revision = checked(previous.Revision + 1);
        Volatile.Write(ref _current, new(_issuer, revision, local, installation, grant, subject, expiry));
    }

    private sealed class CredentialLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        public void Dispose()
        {
            if (Volatile.Read(ref _gate) is null) return;
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new InvalidOperationException("Account owner leases must be released on the acquiring thread.");
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}

internal sealed record AndroidAccountOwnerState(string Issuer, long Revision, bool DeviceLocal,
    string? InstallationId, string? GrantId, string? SubjectId, DateTimeOffset? ExpiresAtUtc)
{
    public override string ToString() => $"AndroidAccountOwnerState {{ Revision = {Revision}, Identity = [REDACTED] }}";
}
