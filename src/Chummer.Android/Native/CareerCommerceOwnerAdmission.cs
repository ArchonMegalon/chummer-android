using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// One synchronous commerce operation, admitted by the actual account authority.
/// Workspace and draft adapters share this scope; no scope may cross an await.
/// </summary>
public sealed class CareerCommerceOwnerAdmission(IOwnerContextLeaseAccessor owners) : IDisposable
{
    private readonly ThreadLocal<Scope?> _current = new();
    private readonly ConditionalWeakTable<object, SnapshotOwner> _snapshots = new();
    private sealed record SnapshotOwner(OwnerContextStamp Stamp, CharacterWorkspaceId Workspace);

    public T BindSnapshot<T>(CharacterWorkspaceId workspace, T snapshot) where T : class
    {
        _snapshots.Add(snapshot, new(RequireStamp(workspace), workspace));
        return snapshot;
    }

    public void RequireSnapshot(CharacterWorkspaceId workspace, object snapshot)
    {
        var current = RequireStamp(workspace);
        if (!_snapshots.TryGetValue(snapshot, out var issued)
            || issued.Workspace != workspace || issued.Stamp != current)
            throw new InvalidOperationException("The workspace snapshot belongs to another owner or transition.");
    }

    public bool TryEnter(OwnerContextStamp expected, CharacterWorkspaceId workspace,
        [NotNullWhen(true)] out IDisposable? scope)
    {
        scope = null;
        // Android's credential lease is not reentrant. Reject before acquiring.
        if (_current.Value is not null || string.IsNullOrWhiteSpace(workspace.Value)
            || !owners.TryAcquire(expected, out var lease)) return false;
        var admitted = new Scope(this, lease, workspace);
        _current.Value = admitted;
        scope = admitted;
        return true;
    }

    public OwnerContextStamp RequireStamp(CharacterWorkspaceId workspace)
    {
        if (_current.Value is not { } scope || scope.Workspace != workspace)
            throw new InvalidOperationException("Commerce requires an admitted owner and workspace.");
        return scope.Lease.Stamp;
    }

    public OwnerScope RequireOwner(CharacterWorkspaceId workspace) => RequireStamp(workspace).Owner;

    public string CheckpointKey(string prefix, CharacterWorkspaceId workspace)
    {
        OwnerScope owner = RequireOwner(workspace);
        static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        // Preserve installed local drafts exactly. Linked accounts never adopt
        // that legacy namespace, including an untrusted local-name lookalike.
        return owner.IsLocalSingleUser ? prefix + Hash(workspace.Value)
            : prefix + "owner." + Hash(owner.NormalizedValue) + "." + Hash(workspace.Value);
    }

    public void Dispose() => _current.Dispose();

    private sealed class Scope(CareerCommerceOwnerAdmission admission,
        IOwnerContextLease lease, CharacterWorkspaceId workspace) : IDisposable
    {
        internal IOwnerContextLease Lease { get; } = lease;
        internal CharacterWorkspaceId Workspace { get; } = workspace;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            if (!ReferenceEquals(admission._current.Value, this))
                throw new InvalidOperationException("Commerce admission must be released on its owning thread.");
            Lease.Dispose();
            admission._current.Value = null;
            _disposed = true;
        }
    }
}
