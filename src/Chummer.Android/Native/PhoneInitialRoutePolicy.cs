using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public enum PhoneInitialRouteReadinessKind { Pending, Ready, Unavailable }

/// <summary>A read-only startup observation, never workspace mutation authority.</summary>
public sealed record PhoneInitialRouteReadiness(
    PhoneInitialRouteReadinessKind Kind,
    OwnerContextStamp? Owner,
    CharacterWorkspaceId? WorkspaceId,
    bool HasProfile);

/// <summary>
/// One initial navigation intent. Pending owner hydration cannot consume it;
/// a user departure or any observed owner transition permanently supersedes it.
/// </summary>
public sealed class PhoneInitialRoutePolicy
{
    private readonly object _gate = new();
    private OwnerContextStamp? _originalOwner;
    private bool _retired;

    public bool IsRetired { get { lock (_gate) return _retired; } }

    // Safe in an account notification: this only records immutable observations,
    // never dispatches, takes a credential lease, or calls the coordinator.
    public void Observe(PhoneInitialRouteReadiness readiness)
    {
        lock (_gate) ObserveCore(readiness);
    }

    public bool TryResolve(PhoneInitialRouteReadiness readiness)
    {
        lock (_gate)
        {
            ObserveCore(readiness);
            if (_retired || readiness.Kind != PhoneInitialRouteReadinessKind.Ready
                || readiness.Owner is not { IsValid: true }) return false;
            _retired = true;
            return true;
        }
    }

    public void Retire() { lock (_gate) _retired = true; }

    public void ObserveNavigation(string? currentLocation, string? targetLocation, bool isDefaultRunnersPage)
    {
        if (targetLocation is not null && currentLocation == targetLocation) return;
        // Initial Shell attachment can settle an implicit route onto its first
        // Runners item. That is not a user departure. A leave-and-return cannot
        // revive an intent retired by the intervening non-default destination.
        if (isDefaultRunnersPage && targetLocation is not null
            && (targetLocation == PhoneShellRoutes.RunnersAbsolute
                || targetLocation.EndsWith("/" + PhoneShellRoutes.Runners, StringComparison.Ordinal))) return;
        Retire();
    }

    private void ObserveCore(PhoneInitialRouteReadiness readiness)
    {
        if (_retired) return;
        if (_originalOwner is { } original && readiness.Owner != original)
        {
            _retired = true;
            return;
        }
        if (readiness.Owner is { IsValid: true } owner) _originalOwner ??= owner;
        if (readiness.Kind == PhoneInitialRouteReadinessKind.Unavailable) _retired = true;
    }
}
