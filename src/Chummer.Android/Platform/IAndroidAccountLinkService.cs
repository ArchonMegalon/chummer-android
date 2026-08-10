namespace Chummer.Android.Platform;

public enum AndroidAccountLinkStatus
{
    Loading,
    Unlinked,
    Pending,
    Linked,
    Error
}

public sealed record AndroidAccountLinkSnapshot(
    AndroidAccountLinkStatus Status,
    string Label,
    string? Detail = null,
    DateTimeOffset? GrantExpiresAtUtc = null)
{
    public bool IsLinked => Status == AndroidAccountLinkStatus.Linked;
    public bool IsPending => Status == AndroidAccountLinkStatus.Pending;
}

public interface IAndroidAccountLinkService
{
    event EventHandler? Changed;

    AndroidAccountLinkSnapshot Snapshot { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task BeginLinkAsync(CancellationToken cancellationToken = default);

    Task ResumePendingLinkAsync(Uri? callbackUri = null, CancellationToken cancellationToken = default);

    Task UnlinkAsync(CancellationToken cancellationToken = default);

    Task OpenAccountAsync(CancellationToken cancellationToken = default);
}
