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

public static class AndroidAccountErasureConfirmation
{
    public const string RequiredPhrase = "ERASE MY CHUMMER ACCOUNT";
}

public sealed record AndroidAccountErasureReceipt(
    bool Erased,
    DateTimeOffset ErasedAtUtc,
    string ReceiptSha256);

public sealed record AndroidOnlineCharacter(
    string WorkspaceId,
    string RulesetId,
    string Format,
    string Payload,
    DateTimeOffset UpdatedAtUtc,
    string Name,
    string Alias,
    string Metatype);

public sealed record AndroidLinkedGroupMember(string Role, string? RunnerHandle);

public sealed record AndroidLinkedGroup(
    string GroupId,
    string Name,
    string GroupType,
    string Visibility,
    string Role,
    bool CanManage,
    string? RunnerDossierId,
    string? RunnerHandle,
    IReadOnlyList<AndroidLinkedGroupMember> Members,
    DateTimeOffset UpdatedAtUtc);

public sealed record AndroidChronicleDraft(
    string Title,
    string BookKind,
    string Audience,
    string SourceSummary,
    string ModelKey,
    int TargetChapterCount,
    int TargetWordsPerChapter,
    bool IncludeRunnerRoster,
    bool IncludeCover,
    bool IncludeTranslation,
    bool IncludeAudiobook,
    bool ExternalProcessingConsent,
    bool ParticipantConsentConfirmed,
    bool RedactionReviewed,
    bool SourceRightsConfirmed,
    bool SpoilerReviewConfirmed = false);

public sealed record AndroidChronicleProject(
    string ChronicleProjectId,
    string Title,
    string BookKind,
    string Audience,
    string Status,
    string SourceSummary,
    string ModelKey,
    int TargetChapterCount,
    int TargetWordsPerChapter,
    bool IncludeRunnerRoster,
    IReadOnlyList<string> RunnerRoster,
    bool IncludeCover,
    bool IncludeTranslation,
    bool IncludeAudiobook,
    bool ExternalProcessingConsent,
    bool ParticipantConsentConfirmed,
    bool RedactionReviewed,
    bool SourceRightsConfirmed,
    int SourcePacketVersion,
    string SourcePacketSha256,
    int EstimatedCredits,
    string Provider,
    bool OperatorRequired,
    bool UnattendedAutomationAllowed,
    string? ExternalProjectRef,
    string? ArtifactUrl,
    string? ArtifactSha256,
    string? ExportFormat,
    DateTimeOffset? SourceApprovedAtUtc,
    DateTimeOffset? HandoffApprovedAtUtc,
    DateTimeOffset? OutlineApprovedAtUtc,
    DateTimeOffset? ArtifactImportedAtUtc,
    DateTimeOffset? PublicationApprovedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool SpoilerReviewConfirmed = false,
    DateTimeOffset? GenerationApprovedAtUtc = null,
    DateTimeOffset? ExternalSendApprovedAtUtc = null,
    DateTimeOffset? UploadApprovedAtUtc = null);

public sealed record AndroidChroniclePacket(
    string FileName,
    string MediaType,
    string ContentBase64,
    string Sha256);

public interface IAndroidAccountLinkService
{
    event EventHandler? Changed;

    AndroidAccountLinkSnapshot Snapshot { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task BeginLinkAsync(CancellationToken cancellationToken = default);

    Task ResumePendingLinkAsync(Uri? callbackUri = null, CancellationToken cancellationToken = default);

    Task UnlinkAsync(CancellationToken cancellationToken = default);

    Task OpenAccountAsync(CancellationToken cancellationToken = default);

    Task<AndroidAccountErasureReceipt> EraseAccountAsync(
        string confirmation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AndroidOnlineCharacter>> ListOnlineCharactersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AndroidLinkedGroup>> ListGroupsAsync(CancellationToken cancellationToken = default);

    Task<AndroidLinkedGroup> CreateGroupAsync(
        string name,
        string visibility,
        CancellationToken cancellationToken = default);

    Task<AndroidLinkedGroup> UpdateGroupAsync(
        string groupId,
        string name,
        string visibility,
        CancellationToken cancellationToken = default);

    Task<Uri> CreateGroupInviteAsync(string groupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AndroidChronicleProject>> ListChroniclesAsync(
        string groupId,
        CancellationToken cancellationToken = default);

    Task<AndroidChronicleProject> CreateChronicleAsync(
        string groupId,
        AndroidChronicleDraft draft,
        CancellationToken cancellationToken = default);

    Task<AndroidChronicleProject> ReviseChronicleAsync(
        string groupId,
        string chronicleProjectId,
        AndroidChronicleDraft draft,
        CancellationToken cancellationToken = default);

    Task<AndroidChronicleProject> AdvanceChronicleAsync(
        string groupId,
        string chronicleProjectId,
        string action,
        string? externalProjectRef = null,
        string? artifactUrl = null,
        string? artifactSha256 = null,
        string? exportFormat = null,
        CancellationToken cancellationToken = default);

    Task<AndroidChroniclePacket> DownloadChroniclePacketAsync(
        string groupId,
        string chronicleProjectId,
        CancellationToken cancellationToken = default);

    Task<AndroidChroniclePacket> DownloadChronicleHandoffAsync(
        string groupId,
        string chronicleProjectId,
        CancellationToken cancellationToken = default);
}
