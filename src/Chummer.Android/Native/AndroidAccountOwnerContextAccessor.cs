using System.Diagnostics.CodeAnalysis;
using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;

namespace Chummer.Android.Native;

/// <summary>Core's lease boundary over the actual Android credential writer.</summary>
public sealed class AndroidAccountOwnerContextAccessor(AndroidAccountLinkService account) : IOwnerContextLeaseAccessor
{
    private readonly AndroidAccountOwnerAuthority _authority = account.OwnerAuthority;
    private CoreLease? _activeLease;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => account.InitializeOwnerContextAsync(cancellationToken);

    public OwnerScope Current => Capture() is { IsValid: true } stamp
        ? stamp.Owner : throw new InvalidOperationException("The Android workspace owner is unavailable.");

    public OwnerContextStamp Capture()
    {
        CoreLease? lease = Volatile.Read(ref _activeLease);
        if (lease is not null && lease.IsActiveOnCurrentThread) return lease.Stamp;
        return ToStamp(_authority.Capture());
    }

    public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
    {
        lease = null;
        AndroidAccountOwnerState? current = _authority.Capture();
        if (!expected.IsValid || current is null || ToStamp(current) != expected
            || !_authority.TryAcquire(current, out IDisposable? credentialLease)) return false;
        var acquired = new CoreLease(this, expected, credentialLease!);
        Volatile.Write(ref _activeLease, acquired);
        lease = acquired;
        return true;
    }

    private static OwnerContextStamp ToStamp(AndroidAccountOwnerState? state)
    {
        if (state is null) return default;
        if (state.DeviceLocal) return new(OwnerScope.LocalSingleUser, state.Issuer, state.Revision);
        return AndroidAccountOwnerKey.TryCreate(state.SubjectId, out string ownerKey)
            ? new(new OwnerScope(ownerKey), state.Issuer, state.Revision)
            : default;
    }

    private sealed class CoreLease(AndroidAccountOwnerContextAccessor owner, OwnerContextStamp stamp,
        IDisposable credentialLease) : IOwnerContextLease
    {
        private IDisposable? _credentialLease = credentialLease;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal bool IsActiveOnCurrentThread => Volatile.Read(ref _credentialLease) is not null
            && _thread == Environment.CurrentManagedThreadId;
        public OwnerContextStamp Stamp => IsActiveOnCurrentThread ? stamp
            : throw new InvalidOperationException("The Android owner lease is inactive on this thread.");
        public void Dispose()
        {
            IDisposable? held = Volatile.Read(ref _credentialLease);
            if (held is null) return;
            if (!IsActiveOnCurrentThread)
                throw new InvalidOperationException("The Android owner lease must be released on its acquiring thread.");
            Interlocked.CompareExchange(ref owner._activeLease, null, this);
            Interlocked.Exchange(ref _credentialLease, null);
            held.Dispose();
        }
    }
}
