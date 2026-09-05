using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Native;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Platform;

public sealed class AndroidAccountLinkService : IAndroidAccountLinkService
{
    private const string InstallationIdKey = "chummer.account.installation-id.v1";
    private const string PrivateKeyKey = "chummer.account.installation-private-key.v1";
    private const string GrantIdKey = "chummer.account.installation-grant-id.v2";
    private const string AccessTokenKey = "chummer.account.installation-grant.v1";
    private const string GrantExpiryKey = "chummer.account.installation-grant-expiry.v1";
    private const string PendingStateKey = "chummer.account.pending-state.v1";
    private const string PendingStartedKey = "chummer.account.pending-started.v1";
    private const string LastProofTimestampKey = "chummer.account.last-proof-timestamp.v1";
    private const string LinkPath = "/account/access/install-link";
    private const string CallbackPath = "/app/install-link";
    private const string HeadId = "android";
    private const string PlatformId = "android";
    private const string ReleaseChannel = "preview";
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(18);
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromDays(7);
    private static readonly HashSet<string> RequiredErasureComponents = new(StringComparer.Ordinal)
    {
        "hosted_build_workspaces",
        "support",
        "first_party_auxiliary_stores",
        "community",
        "identity"
    };

    private readonly AndroidAccountLinkHttpTransport _httpTransport;
    private readonly IAndroidSystemService _systemService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AndroidAccountLinkSnapshot _snapshot = new(
        AndroidAccountLinkStatus.Loading,
        AccountText("Checking", "Checking"));

    internal AndroidAccountLinkService(
        AndroidAccountLinkHttpTransport httpTransport,
        IAndroidSystemService systemService)
    {
        _httpTransport = httpTransport;
        _systemService = systemService;
    }

    public event EventHandler? Changed;

    public AndroidAccountLinkSnapshot Snapshot => _snapshot;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            StoredGrant? grant = await ReadStoredGrantAsync();
            if (grant is null)
            {
                ClearGrant();
                string? pendingState = await SecureStorage.Default.GetAsync(PendingStateKey);
                DateTimeOffset? pendingStarted = await ReadTimestampAsync(PendingStartedKey);
                if (string.IsNullOrWhiteSpace(pendingState) || !IsPendingLinkCurrent(pendingStarted))
                {
                    ClearPending();
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Unlinked,
                        AccountText("AccountNotLinked", "Not linked")));
                }
                else
                {
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Pending,
                        AccountText("AccountFinishLinking", "Finish linking"),
                        AccountText("AccountApproveBrowser", "Approve in your browser, then return.")));
                }
                return;
            }

            DateTimeOffset? expiresAtUtc = await ReadGrantExpiryAsync();
            if (expiresAtUtc is null || expiresAtUtc <= DateTimeOffset.UtcNow)
            {
                ClearGrant();
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Unlinked,
                    AccountText("AccountLinkExpired", "Link expired"),
                    AccountText("AccountLinkAgainRestore", "Link again to restore account access.")));
                return;
            }

            GrantValidationResult validation = await ValidateGrantAsync(grant, cancellationToken);
            if (validation == GrantValidationResult.Invalid)
            {
                ClearGrant();
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Unlinked,
                    AccountText("AccountLinkExpired", "Link expired"),
                    AccountText("AccountLinkAgainRestore", "Link again to restore account access.")));
                return;
            }

            if (validation == GrantValidationResult.Offline)
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Linked,
                    AccountText("AccountLinked", "Linked"),
                    AccountText("AccountAvailableOffline", "Available offline."),
                    expiresAtUtc));
                return;
            }

            if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow.Add(RefreshWindow))
            {
                GrantContract? refreshed = await RefreshGrantAsync(grant, cancellationToken);
                if (refreshed is not null)
                {
                    await SaveGrantAsync(refreshed);
                    expiresAtUtc = refreshed.ExpiresAtUtc;
                }
            }

            SetSnapshot(new(
                AndroidAccountLinkStatus.Linked,
                AccountText("AccountLinked", "Linked"),
                null,
                expiresAtUtc));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SetSnapshot(new(
                AndroidAccountLinkStatus.Error,
                AccountText("AccountLinkUnavailable", "Link unavailable"),
                AccountText(
                    "AccountSecureStorageUnavailable",
                    "Android secure storage could not be opened.")));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BeginLinkAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            InstallationIdentity identity = await GetOrCreateInstallationIdentityAsync();
            string? savedState = await SecureStorage.Default.GetAsync(PendingStateKey);
            DateTimeOffset? pendingStarted = await ReadTimestampAsync(PendingStartedKey);
            bool resumeCurrentAttempt = !string.IsNullOrWhiteSpace(savedState)
                && IsPendingLinkCurrent(pendingStarted);
            string state = resumeCurrentAttempt ? savedState! : NewBase64UrlToken(24);
            if (!resumeCurrentAttempt)
            {
                await SecureStorage.Default.SetAsync(PendingStateKey, state);
                await SecureStorage.Default.SetAsync(
                    PendingStartedKey,
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            }

            string callback = $"https://chummer.run{CallbackPath}?state={Uri.EscapeDataString(state)}";
            Dictionary<string, string> query = new(StringComparer.Ordinal)
            {
                ["installationId"] = identity.InstallationId,
                ["headId"] = HeadId,
                ["applicationVersion"] = AppInfo.Current.VersionString,
                ["releaseChannel"] = ReleaseChannel,
                ["platform"] = PlatformId,
                ["arch"] = ResolveArchitecture(),
                ["installLinkCallbackUri"] = callback,
                ["installLinkTransport"] = "proof_poll",
                ["publicKey"] = identity.PublicKey
            };
            string href = $"{LinkPath}?{string.Join('&', query.Select(static item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"))}";
            SetSnapshot(new(
                AndroidAccountLinkStatus.Pending,
                AccountText("AccountFinishLinking", "Finish linking"),
                AccountText("AccountApproveBrowser", "Approve in your browser, then return.")));
            if (!await _systemService.OpenUriAsync(ChummerWebRoutes.Resolve(href)))
            {
                ClearPending();
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Error,
                    AccountText("AccountBrowserUnavailable", "Browser unavailable"),
                    AccountText("AccountOpenLinkingAgain", "Open account linking again.")));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SetSnapshot(new(
                AndroidAccountLinkStatus.Error,
                AccountText("AccountCouldNotLink", "Could not link"),
                AccountText("AccountTryAgainMoment", "Try again in a moment.")));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumePendingLinkAsync(Uri? callbackUri = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string? pendingState = await SecureStorage.Default.GetAsync(PendingStateKey);
            if (string.IsNullOrWhiteSpace(pendingState))
            {
                return;
            }

            if (callbackUri is not null && !IsExpectedCallback(callbackUri, pendingState))
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Error,
                    AccountText("AccountReturnRejected", "Return rejected"),
                    AccountText("AccountStartLinkingAgain", "Start linking again from this app.")));
                return;
            }

            DateTimeOffset? pendingStarted = await ReadTimestampAsync(PendingStartedKey);
            if (!IsPendingLinkCurrent(pendingStarted))
            {
                ClearPending();
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Error,
                    AccountText("AccountApprovalExpired", "Approval expired"),
                    AccountText("AccountStartFreshLink", "Start a fresh account link.")));
                return;
            }

            InstallationIdentity identity = await GetOrCreateInstallationIdentityAsync();
            using RSA signingKey = RSA.Create();
            byte[] privateKey = Convert.FromBase64String(identity.PrivateKey);
            try
            {
                signingKey.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
                if (bytesRead != privateKey.Length)
                {
                    throw new CryptographicException("The installation key is invalid.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }

            long issuedAt = await NextProofTimestampAsync();
            string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            string architecture = ResolveArchitecture();
            string version = AppInfo.Current.VersionString;
            string hostLabel = ResolveHostLabel();
            byte[] proof = AndroidAccountLinkBootstrapProof.CreateCanonicalPayload(
                identity.InstallationId,
                HeadId,
                version,
                ReleaseChannel,
                PlatformId,
                architecture,
                issuedAt,
                nonce,
                hostLabel);
            string signature;
            try
            {
                signature = Convert.ToBase64String(signingKey.SignData(
                    proof,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(proof);
            }

            PollRequest request = new(
                identity.InstallationId,
                HeadId,
                version,
                ReleaseChannel,
                PlatformId,
                architecture,
                identity.PublicKey,
                issuedAt,
                nonce,
                signature,
                hostLabel);
            using HttpResponseMessage response = await _httpTransport.PostJsonAsync(
                AndroidAccountLinkBootstrapProof.PollPath,
                request,
                authority: null,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Pending,
                    AccountText("AccountFinishLinking", "Finish linking"),
                    AccountText("AccountApproveBrowser", "Approve in your browser, then return.")));
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    ClearAllCredentials();
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Error,
                        AccountText("AccountFreshLinkRequired", "Fresh link required"),
                        AccountText("AccountChooseLinkTryAgain", "Choose Link account and try again.")));
                }
                else if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    ClearAllCredentials();
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Error,
                        AccountText("AccountFreshLinkRequired", "Fresh link required"),
                        AccountText("AccountChooseLinkTryAgain", "Choose Link account and try again.")));
                }
                else if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    ClearPending();
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Error,
                        AccountText("AccountApprovalExpired", "Approval expired"),
                        AccountText("AccountStartFreshLink", "Start a fresh account link.")));
                }
                else
                {
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Error,
                        AccountText("AccountCouldNotLink", "Could not link"),
                        AccountText("AccountCheckConnection", "Check your connection and try again.")));
                }
                return;
            }

            AndroidAccountLinkResponseGrantAuthority responseAuthority =
                AndroidAccountLinkHttpTransport.ReadResponseGrantAuthority(response);
            ExchangeResponse exchange = await _httpTransport.ReadJsonAsync<ExchangeResponse>(
                response,
                cancellationToken);
            GrantContract grant = MaterializeIssuedGrant(
                exchange.Grant,
                responseAuthority,
                identity.InstallationId);
            if (!IsUsableGrant(grant, identity.InstallationId))
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Error,
                    AccountText("AccountCouldNotLink", "Could not link"),
                    AccountText("AccountIncompleteReply", "The reply was incomplete. Try again.")));
                return;
            }

            await SaveGrantAsync(grant);
            ClearPending();
            SetSnapshot(new(
                AndroidAccountLinkStatus.Linked,
                AccountText("AccountLinked", "Linked"),
                null,
                grant.ExpiresAtUtc));
        }
        catch (HttpRequestException)
        {
            SetSnapshot(new(
                AndroidAccountLinkStatus.Pending,
                AccountText("AccountFinishLinking", "Finish linking"),
                AccountText("AccountConnectInternet", "Connect to the internet and try again.")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SetSnapshot(new(
                AndroidAccountLinkStatus.Error,
                AccountText("AccountCouldNotLink", "Could not link"),
                AccountText("AccountStartLinkingAgainShort", "Start linking again.")));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        DateTimeOffset? grantExpiresAtUtc = _snapshot.GrantExpiresAtUtc;
        try
        {
            grantExpiresAtUtc ??= await ReadGrantExpiryAsync();
            StoredGrant? grant = await ReadStoredGrantAsync();
            if (grant is not null)
            {
                AndroidAccountLinkRequestAuthority authority = CreateRequestAuthority(grant);
                using HttpResponseMessage response = await _httpTransport.PostJsonAsync(
                    "/api/v2/install-linking/grants/revoke",
                    new InstallationGrantRequest(grant.InstallationId),
                    authority,
                    cancellationToken);
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Linked,
                        AccountText("AccountStillLinked", "Still linked"),
                        AccountText(
                            "AccountRevokeUnavailable",
                            "Could not reach Chummer. Try unlinking again."),
                        grantExpiresAtUtc));
                    return;
                }
            }

            ClearAllCredentials();
            SetSnapshot(new(
                AndroidAccountLinkStatus.Unlinked,
                AccountText("AccountNotLinked", "Not linked")));
        }
        catch (HttpRequestException)
        {
            SetSnapshot(new(
                AndroidAccountLinkStatus.Linked,
                AccountText("AccountStillLinked", "Still linked"),
                AccountText("AccountRevokeOffline", "Connect to the internet and try again."),
                grantExpiresAtUtc));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task OpenAccountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _systemService.OpenUriAsync(ChummerWebRoutes.Resolve(ChummerWebRoutes.AccountAccess));
    }

    public async Task<AndroidAccountErasureReceipt> EraseAccountAsync(
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                confirmation,
                AndroidAccountErasureConfirmation.RequiredPhrase,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Enter {AndroidAccountErasureConfirmation.RequiredPhrase} exactly.",
                nameof(confirmation));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            StoredGrant grant = await RequireStoredGrantAsync();
            AndroidAccountErasureReceipt receipt = await SendLinkedAsync<AndroidAccountErasureReceipt>(
                "/api/v2/android/linked/account/erase",
                new AccountErasureRequest(
                    grant.InstallationId,
                    AndroidAccountErasureConfirmation.RequiredPhrase),
                grant,
                cancellationToken);
            if (!IsCompleteAccountErasureReceipt(receipt))
            {
                throw new InvalidDataException("Chummer returned an invalid deletion receipt.");
            }

            ClearAllCredentials();
            SetSnapshot(new(
                AndroidAccountLinkStatus.Unlinked,
                AccountText("AccountDeleted", "Account deleted")));
            return receipt;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AndroidOnlineCharacter>> ListOnlineCharactersAsync(
        CancellationToken cancellationToken = default)
    {
        StoredGrant grant = await RequireStoredGrantAsync();
        WorkspaceListResponse response = await SendLinkedAsync<WorkspaceListResponse>(
            "/api/v2/install-linking/continuation/workspaces/list",
            new InstallationGrantRequest(grant.InstallationId),
            grant,
            cancellationToken);
        if (response.Snapshots.Count > 200
            || response.Snapshots.Any(static item => item.Payload.Length > 512 * 1024))
        {
            throw new InvalidDataException("The online character list is too large to open safely.");
        }

        return response.Snapshots
            .OrderByDescending(static item => item.UpdatedAtUtc)
            .Select(static item => new AndroidOnlineCharacter(
                item.WorkspaceId,
                item.RulesetId,
                item.Format,
                item.Payload,
                item.UpdatedAtUtc,
                item.Summary?.Name ?? string.Empty,
                item.Summary?.Alias ?? string.Empty,
                item.Summary?.Metatype ?? string.Empty))
            .ToArray();
    }

    public async Task<IReadOnlyList<AndroidLinkedGroup>> ListGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedGroupListResponse response = await SendLinkedAsync<LinkedGroupListResponse>(
            "/api/v2/android/linked/groups",
            new InstallationGrantRequest(grant.InstallationId),
            grant,
            cancellationToken);
        return response.Groups.Select(ToLinkedGroup).ToArray();
    }

    public async Task<AndroidLinkedGroup> CreateGroupAsync(
        string name,
        string visibility,
        CancellationToken cancellationToken = default)
    {
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedGroupDto group = await SendLinkedAsync<LinkedGroupDto>(
            "/api/v2/android/linked/groups/create",
            new LinkedGroupMutationRequest(grant.InstallationId, name, visibility),
            grant,
            cancellationToken);
        return ToLinkedGroup(group);
    }

    public async Task<AndroidLinkedGroup> UpdateGroupAsync(
        string groupId,
        string name,
        string visibility,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedGroupDto group = await SendLinkedAsync<LinkedGroupDto>(
            $"/api/v2/android/linked/groups/{Uri.EscapeDataString(groupId)}/update",
            new LinkedGroupMutationRequest(grant.InstallationId, name, visibility),
            grant,
            cancellationToken);
        return ToLinkedGroup(group);
    }

    public async Task<Uri> CreateGroupInviteAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedInviteResponse invite = await SendLinkedAsync<LinkedInviteResponse>(
            $"/api/v2/android/linked/groups/{Uri.EscapeDataString(groupId)}/invites",
            new InstallationGrantRequest(grant.InstallationId),
            grant,
            cancellationToken);
        string code = invite.Code?.Trim() ?? string.Empty;
        string expectedPath = $"/groups/join/{Uri.EscapeDataString(code)}";
        if (code.Length is 0 or > 256
            || !Uri.TryCreate(invite.InviteUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "chummer.run", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException("Chummer returned an invalid group invite link.");
        }

        return uri;
    }

    public async Task<IReadOnlyList<AndroidChronicleProject>> ListChroniclesAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedChronicleListResponse response = await SendLinkedAsync<LinkedChronicleListResponse>(
            $"/api/v2/android/linked/groups/{Uri.EscapeDataString(groupId)}/chronicles",
            new InstallationGrantRequest(grant.InstallationId),
            grant,
            cancellationToken);
        return response.Projects.Select(ToChronicleProject).ToArray();
    }

    public async Task<AndroidChronicleProject> CreateChronicleAsync(
        string groupId,
        AndroidChronicleDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(draft);
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedChronicleDto project = await SendLinkedAsync<LinkedChronicleDto>(
            $"/api/v2/android/linked/groups/{Uri.EscapeDataString(groupId)}/chronicles/create",
            ToChronicleDraftRequest(grant, draft),
            grant,
            cancellationToken);
        return ToChronicleProject(project);
    }

    public async Task<AndroidChronicleProject> ReviseChronicleAsync(
        string groupId,
        string chronicleProjectId,
        AndroidChronicleDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chronicleProjectId);
        ArgumentNullException.ThrowIfNull(draft);
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedChronicleDto project = await SendLinkedAsync<LinkedChronicleDto>(
            $"/api/v2/android/linked/groups/{Uri.EscapeDataString(groupId)}/chronicles/{Uri.EscapeDataString(chronicleProjectId)}/draft",
            ToChronicleDraftRequest(grant, draft),
            grant,
            cancellationToken);
        return ToChronicleProject(project);
    }

    public async Task<AndroidChronicleProject> AdvanceChronicleAsync(
        string groupId,
        string chronicleProjectId,
        string action,
        string? externalProjectRef = null,
        string? artifactUrl = null,
        string? artifactSha256 = null,
        string? exportFormat = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chronicleProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedChronicleDto project = await SendLinkedAsync<LinkedChronicleDto>(
            $"/api/v2/android/linked/groups/{Uri.EscapeDataString(groupId)}/chronicles/{Uri.EscapeDataString(chronicleProjectId)}/actions",
            new LinkedChronicleActionRequest(
                grant.InstallationId,
                action,
                externalProjectRef,
                artifactUrl,
                artifactSha256,
                exportFormat),
            grant,
            cancellationToken);
        return ToChronicleProject(project);
    }

    public async Task<AndroidChroniclePacket> DownloadChroniclePacketAsync(
        string groupId,
        string chronicleProjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chronicleProjectId);
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedChroniclePacketResponse packet = await SendLinkedAsync<LinkedChroniclePacketResponse>(
            $"/api/v2/android/linked/groups/{Uri.EscapeDataString(groupId)}/chronicles/{Uri.EscapeDataString(chronicleProjectId)}/packet",
            new InstallationGrantRequest(grant.InstallationId),
            grant,
            cancellationToken);
        return new AndroidChroniclePacket(packet.FileName, packet.MediaType, packet.ContentBase64, packet.Sha256);
    }

    public async Task<AndroidChroniclePacket> DownloadChronicleHandoffAsync(
        string groupId,
        string chronicleProjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chronicleProjectId);
        StoredGrant grant = await RequireStoredGrantAsync();
        LinkedChroniclePacketResponse handoff = await SendLinkedAsync<LinkedChroniclePacketResponse>(
            $"/api/v2/android/linked/groups/{Uri.EscapeDataString(groupId)}/chronicles/{Uri.EscapeDataString(chronicleProjectId)}/handoff",
            new InstallationGrantRequest(grant.InstallationId),
            grant,
            cancellationToken);
        return new AndroidChroniclePacket(handoff.FileName, handoff.MediaType, handoff.ContentBase64, handoff.Sha256);
    }

    private static AndroidLinkedGroup ToLinkedGroup(LinkedGroupDto group)
        => new(
            group.GroupId,
            group.Name,
            group.GroupType,
            group.Visibility,
            group.Role,
            group.CanManage,
            group.RunnerDossierId,
            group.RunnerHandle,
            group.Members.Select(static member => new AndroidLinkedGroupMember(member.Role, member.RunnerHandle)).ToArray(),
            group.UpdatedAtUtc);

    private static bool IsCompleteAccountErasureReceipt(AndroidAccountErasureReceipt receipt)
    {
        if (!receipt.Erased
            || !IsSha256(receipt.SubjectKeySha256)
            || (receipt.UserKeySha256 is not null && !IsSha256(receipt.UserKeySha256))
            || !IsSha256(receipt.ReceiptSha256)
            || receipt.Components.Count != RequiredErasureComponents.Count)
        {
            return false;
        }

        HashSet<string> completed = new(StringComparer.Ordinal);
        foreach (AndroidAccountErasureComponentReceipt component in receipt.Components)
        {
            if (!component.Completed
                || component.RecordsRemoved < 0
                || !RequiredErasureComponents.Contains(component.Component)
                || !completed.Add(component.Component)
                || !IsSha256(component.ReceiptSha256))
            {
                return false;
            }
        }

        return completed.SetEquals(RequiredErasureComponents);
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 }
            && value.All(static character => Uri.IsHexDigit(character));

    private static LinkedChronicleDraftRequest ToChronicleDraftRequest(
        StoredGrant grant,
        AndroidChronicleDraft draft)
        => new(
            grant.InstallationId,
            draft.Title,
            draft.BookKind,
            draft.Audience,
            draft.SourceSummary,
            draft.ModelKey,
            draft.TargetChapterCount,
            draft.TargetWordsPerChapter,
            draft.IncludeRunnerRoster,
            draft.IncludeCover,
            draft.IncludeTranslation,
            draft.IncludeAudiobook,
            draft.ExternalProcessingConsent,
            draft.ParticipantConsentConfirmed,
            draft.RedactionReviewed,
            draft.SourceRightsConfirmed,
            draft.SpoilerReviewConfirmed);

    private static AndroidChronicleProject ToChronicleProject(LinkedChronicleDto project)
        => new(
            project.ChronicleProjectId,
            project.Title,
            project.BookKind,
            project.Audience,
            project.Status,
            project.SourceSummary,
            project.ModelKey,
            project.TargetChapterCount,
            project.TargetWordsPerChapter,
            project.IncludeRunnerRoster,
            project.RunnerRoster,
            project.IncludeCover,
            project.IncludeTranslation,
            project.IncludeAudiobook,
            project.ExternalProcessingConsent,
            project.ParticipantConsentConfirmed,
            project.RedactionReviewed,
            project.SourceRightsConfirmed,
            project.SourcePacketVersion,
            project.SourcePacketSha256,
            project.EstimatedCredits,
            project.Provider,
            project.OperatorRequired,
            project.UnattendedAutomationAllowed,
            project.ExternalProjectRef,
            project.ArtifactUrl,
            project.ArtifactSha256,
            project.ExportFormat,
            project.SourceApprovedAtUtc,
            project.HandoffApprovedAtUtc,
            project.OutlineApprovedAtUtc,
            project.ArtifactImportedAtUtc,
            project.PublicationApprovedAtUtc,
            project.UpdatedAtUtc,
            project.SpoilerReviewConfirmed,
            project.GenerationApprovedAtUtc,
            project.ExternalSendApprovedAtUtc,
            project.UploadApprovedAtUtc);

    private static async Task<StoredGrant> RequireStoredGrantAsync()
    {
        StoredGrant? grant = await ReadStoredGrantAsync();
        return grant ?? throw new InvalidOperationException("Link your account first.");
    }

    private async Task<T> SendLinkedAsync<T>(
        string path,
        object request,
        StoredGrant grant,
        CancellationToken cancellationToken)
    {
        AndroidAccountLinkRequestAuthority authority = CreateRequestAuthority(grant);
        using HttpResponseMessage response = await _httpTransport.PostJsonAsync(
            path,
            request,
            authority,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Conflict)
        {
            ClearGrant();
            SetSnapshot(new(
                AndroidAccountLinkStatus.Unlinked,
                AccountText("AccountLinkExpired", "Link expired"),
                AccountText("AccountLinkAgainRestore", "Link again to restore account access.")));
            throw new InvalidOperationException("Link your account again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "Chummer account data is unavailable.",
                inner: null,
                response.StatusCode);
        }

        return await _httpTransport.ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<GrantValidationResult> ValidateGrantAsync(
        StoredGrant grant,
        CancellationToken cancellationToken)
    {
        try
        {
            AndroidAccountLinkRequestAuthority authority = CreateRequestAuthority(grant);
            using HttpResponseMessage response = await _httpTransport.PostJsonAsync(
                "/api/v2/install-linking/grants/status",
                new InstallationGrantRequest(grant.InstallationId),
                authority,
                cancellationToken);
            return response.IsSuccessStatusCode
                ? GrantValidationResult.Valid
                : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                    ? GrantValidationResult.Invalid
                    : GrantValidationResult.Offline;
        }
        catch (HttpRequestException)
        {
            return GrantValidationResult.Offline;
        }
    }

    private async Task<GrantContract?> RefreshGrantAsync(
        StoredGrant grant,
        CancellationToken cancellationToken)
    {
        try
        {
            InstallationIdentity identity = await GetOrCreateInstallationIdentityAsync();
            AndroidAccountLinkRequestAuthority authority = CreateRequestAuthority(grant);
            using HttpResponseMessage response = await _httpTransport.PostJsonAsync(
                "/api/v2/install-linking/grants/refresh",
                new RefreshRequest(
                    grant.InstallationId,
                    HeadId,
                    AppInfo.Current.VersionString,
                    ReleaseChannel,
                    PlatformId,
                    ResolveArchitecture(),
                    identity.PublicKey,
                    ResolveHostLabel()),
                authority,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            AndroidAccountLinkResponseGrantAuthority responseAuthority =
                AndroidAccountLinkHttpTransport.ReadResponseGrantAuthority(response);
            RefreshResponse refreshed = await _httpTransport.ReadJsonAsync<RefreshResponse>(
                response,
                cancellationToken);
            GrantContract rotated = MaterializeIssuedGrant(
                refreshed.Grant,
                responseAuthority,
                grant.InstallationId);
            return IsUsableGrant(rotated, grant.InstallationId)
                ? rotated
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static bool IsExpectedCallback(Uri callbackUri, string expectedState)
    {
        if (!string.Equals(callbackUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(callbackUri.Host, "chummer.run", StringComparison.OrdinalIgnoreCase)
            || !callbackUri.IsDefaultPort
            || !string.Equals(callbackUri.AbsolutePath, CallbackPath, StringComparison.Ordinal))
        {
            return false;
        }

        string? state = ParseQueryValue(callbackUri.Query, "state");
        if (state is null)
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(expectedState);
        byte[] actual = Encoding.UTF8.GetBytes(state);
        try
        {
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private async Task<InstallationIdentity> GetOrCreateInstallationIdentityAsync()
    {
        string? installationId = await SecureStorage.Default.GetAsync(InstallationIdKey);
        string? privateKey = await SecureStorage.Default.GetAsync(PrivateKeyKey);
        if (!string.IsNullOrWhiteSpace(installationId) && !string.IsNullOrWhiteSpace(privateKey))
        {
            try
            {
                using RSA existingKey = RSA.Create();
                byte[] existingBytes = Convert.FromBase64String(privateKey);
                try
                {
                    existingKey.ImportPkcs8PrivateKey(existingBytes, out int bytesRead);
                    if (bytesRead == existingBytes.Length && existingKey.KeySize >= 2048)
                    {
                        return new(
                            installationId,
                            privateKey,
                            Convert.ToBase64String(existingKey.ExportSubjectPublicKeyInfo()));
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(existingBytes);
                }
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                ClearAllCredentials();
                throw new CryptographicException("The protected installation identity is invalid.", ex);
            }

            ClearAllCredentials();
            throw new CryptographicException("The protected installation identity is invalid.");
        }

        SecureStorage.Default.Remove(InstallationIdKey);
        SecureStorage.Default.Remove(PrivateKeyKey);
        SecureStorage.Default.Remove(LastProofTimestampKey);
        ClearGrant();

        using RSA newKey = RSA.Create(2048);
        byte[] exportedPrivateKey = newKey.ExportPkcs8PrivateKey();
        try
        {
            installationId = $"android-{NewBase64UrlToken(24)}";
            privateKey = Convert.ToBase64String(exportedPrivateKey);
            await SecureStorage.Default.SetAsync(InstallationIdKey, installationId);
            await SecureStorage.Default.SetAsync(PrivateKeyKey, privateKey);
            return new(
                installationId,
                privateKey,
                Convert.ToBase64String(newKey.ExportSubjectPublicKeyInfo()));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exportedPrivateKey);
        }
    }

    private static async Task SaveGrantAsync(GrantContract grant)
    {
        if (!IsUsableGrant(grant, grant.InstallationId))
        {
            throw new InvalidDataException("Chummer returned an invalid account grant.");
        }

        try
        {
            await SecureStorage.Default.SetAsync(GrantIdKey, grant.GrantId);
            await SecureStorage.Default.SetAsync(AccessTokenKey, grant.AccessToken);
            await SecureStorage.Default.SetAsync(
                GrantExpiryKey,
                grant.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture));
        }
        catch
        {
            // A partially rotated grant must never become request authority after restart.
            ClearGrant();
            throw;
        }
    }

    private static async Task<StoredGrant?> ReadStoredGrantAsync()
    {
        string? installationId = await SecureStorage.Default.GetAsync(InstallationIdKey);
        string? grantId = await SecureStorage.Default.GetAsync(GrantIdKey);
        string? accessToken = await SecureStorage.Default.GetAsync(AccessTokenKey);
        string? privateKey = await SecureStorage.Default.GetAsync(PrivateKeyKey);
        return string.IsNullOrWhiteSpace(installationId)
            || string.IsNullOrWhiteSpace(grantId)
            || string.IsNullOrWhiteSpace(accessToken)
            || string.IsNullOrWhiteSpace(privateKey)
                ? null
                : new StoredGrant(installationId, grantId, accessToken, privateKey);
    }

    private static AndroidAccountLinkRequestAuthority CreateRequestAuthority(StoredGrant grant)
        => new(
            grant.InstallationId,
            grant.GrantId,
            grant.AccessToken,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            canonical => SignRequestProof(grant.PrivateKey, canonical));

    private static string SignRequestProof(string privateKeyPkcs8Base64, byte[] canonical)
    {
        byte[] privateKey = Convert.FromBase64String(privateKeyPkcs8Base64);
        try
        {
            using RSA signingKey = RSA.Create();
            signingKey.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
            if (bytesRead != privateKey.Length || signingKey.KeySize < 2048)
            {
                throw new CryptographicException("The protected installation key is invalid.");
            }
            return Convert.ToBase64String(signingKey.SignData(
                canonical,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static bool IsUsableGrant(GrantContract? grant, string expectedInstallationId)
        => grant is not null
            && !string.IsNullOrWhiteSpace(grant.GrantId)
            && grant.GrantId.Length <= 128
            && string.Equals(grant.InstallationId, expectedInstallationId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(grant.AccessToken)
            && grant.AccessToken.Length <= 256
            && grant.ExpiresAtUtc > grant.IssuedAtUtc
            && grant.ExpiresAtUtc > DateTimeOffset.UtcNow;

    private static GrantContract MaterializeIssuedGrant(
        GrantMetadata metadata,
        AndroidAccountLinkResponseGrantAuthority authority,
        string expectedInstallationId)
    {
        if (!string.Equals(metadata.GrantId, authority.GrantId, StringComparison.Ordinal)
            || !string.Equals(metadata.InstallationId, expectedInstallationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Chummer returned a mismatched account grant.");
        }
        return new GrantContract(
            metadata.GrantId,
            metadata.InstallationId,
            metadata.Status,
            authority.AccessToken,
            metadata.IssuedAtUtc,
            metadata.ExpiresAtUtc);
    }

    private static async Task<DateTimeOffset?> ReadGrantExpiryAsync()
        => await ReadTimestampAsync(GrantExpiryKey);

    private static async Task<DateTimeOffset?> ReadTimestampAsync(string key)
    {
        string? value = await SecureStorage.Default.GetAsync(key);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static bool IsPendingLinkCurrent(DateTimeOffset? startedAtUtc)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return startedAtUtc is not null
            && startedAtUtc >= now.Subtract(PendingLifetime)
            && startedAtUtc <= now.AddMinutes(2);
    }

    private static async Task<long> NextProofTimestampAsync()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string? saved = await SecureStorage.Default.GetAsync(LastProofTimestampKey);
        if (long.TryParse(saved, NumberStyles.None, CultureInfo.InvariantCulture, out long last) && now <= last)
        {
            now = last + 1;
        }

        await SecureStorage.Default.SetAsync(LastProofTimestampKey, now.ToString(CultureInfo.InvariantCulture));
        return now;
    }

    private static string ResolveArchitecture()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => "unknown"
        };

    private static string ResolveHostLabel()
    {
        string candidate = DeviceInfo.Current.Name;
        string cleaned = new(candidate.Where(static value => !char.IsControl(value)).Take(120).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Android device" : cleaned.Trim();
    }

    private static string NewBase64UrlToken(int bytes)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? ParseQueryValue(string query, string key)
    {
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.Ordinal))
            {
                return parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            }
        }

        return null;
    }

    private static string AccountText(string key, string englishFallback)
        => PhoneStrings.Get(key, englishFallback);

    private void SetSnapshot(AndroidAccountLinkSnapshot snapshot)
    {
        if (_snapshot == snapshot)
        {
            return;
        }

        _snapshot = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void ClearGrant()
    {
        SecureStorage.Default.Remove(GrantIdKey);
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(GrantExpiryKey);
    }

    private static void ClearPending()
    {
        SecureStorage.Default.Remove(PendingStateKey);
        SecureStorage.Default.Remove(PendingStartedKey);
    }

    private static void ClearAllCredentials()
    {
        ClearGrant();
        ClearPending();
        SecureStorage.Default.Remove(InstallationIdKey);
        SecureStorage.Default.Remove(PrivateKeyKey);
        SecureStorage.Default.Remove(LastProofTimestampKey);
    }

    private enum GrantValidationResult
    {
        Valid,
        Invalid,
        Offline
    }

    private sealed record InstallationIdentity(string InstallationId, string PrivateKey, string PublicKey);
    private sealed class StoredGrant
    {
        internal StoredGrant(
            string installationId,
            string grantId,
            string accessToken,
            string privateKey)
        {
            InstallationId = installationId;
            GrantId = grantId;
            AccessToken = accessToken;
            PrivateKey = privateKey;
        }

        internal string InstallationId { get; }
        internal string GrantId { get; }
        internal string AccessToken { get; }
        internal string PrivateKey { get; }

        public override string ToString()
            => $"StoredGrant {{ InstallationId = {InstallationId}, GrantId = {GrantId}, AccessToken = [REDACTED], PrivateKey = [REDACTED] }}";
    }

    private sealed record InstallationGrantRequest(string InstallationId);
    private sealed record AccountErasureRequest(
        string InstallationId,
        string Confirmation);
    private sealed record PollRequest(
        string InstallationId,
        string HeadId,
        string ApplicationVersion,
        string ChannelId,
        string Platform,
        string Architecture,
        string PublicKey,
        long IssuedAtUnixSeconds,
        string Nonce,
        string Signature,
        string HostLabel);
    private sealed record RefreshRequest(
        string InstallationId,
        string HeadId,
        string ApplicationVersion,
        string ChannelId,
        string Platform,
        string Architecture,
        string PublicKey,
        string HostLabel);
    private sealed record GrantContract(
        string GrantId,
        string InstallationId,
        string Status,
        string AccessToken,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc);
    private sealed record GrantMetadata(
        string GrantId,
        string InstallationId,
        string Status,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc);
    private sealed record ExchangeResponse(GrantMetadata Grant, bool AlreadyClaimed);
    private sealed record RefreshResponse(GrantMetadata Grant, bool Rotated);
    private sealed record WorkspaceListResponse(IReadOnlyList<WorkspaceSnapshotDto> Snapshots);
    private sealed record WorkspaceSnapshotDto(
        string WorkspaceId,
        string RulesetId,
        string Format,
        string Payload,
        DateTimeOffset UpdatedAtUtc,
        WorkspaceSummaryDto? Summary);
    private sealed record WorkspaceSummaryDto(string? Name, string? Alias, string? Metatype);
    private sealed record LinkedGroupListResponse(IReadOnlyList<LinkedGroupDto> Groups);
    private sealed record LinkedGroupDto(
        string GroupId,
        string Name,
        string GroupType,
        string Visibility,
        string Role,
        bool CanManage,
        string? RunnerDossierId,
        string? RunnerHandle,
        IReadOnlyList<LinkedGroupMemberDto> Members,
        DateTimeOffset UpdatedAtUtc);
    private sealed record LinkedGroupMemberDto(string Role, string? RunnerHandle);
    private sealed record LinkedGroupMutationRequest(
        string InstallationId,
        string Name,
        string Visibility);
    private sealed record LinkedInviteResponse(string Code, string InviteUrl, DateTimeOffset? ExpiresAtUtc);
    private sealed record LinkedChronicleListResponse(IReadOnlyList<LinkedChronicleDto> Projects);
    private sealed record LinkedChronicleDraftRequest(
        string InstallationId,
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
    private sealed record LinkedChronicleActionRequest(
        string InstallationId,
        string Action,
        string? ExternalProjectRef,
        string? ArtifactUrl,
        string? ArtifactSha256,
        string? ExportFormat);
    private sealed record LinkedChronicleDto(
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
    private sealed record LinkedChroniclePacketResponse(
        string FileName,
        string MediaType,
        string ContentBase64,
        string Sha256);
}
