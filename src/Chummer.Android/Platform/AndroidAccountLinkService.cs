using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Android.Native;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace Chummer.Android.Platform;

public sealed class AndroidAccountLinkService : IAndroidAccountLinkService
{
    private const string InstallationIdKey = AndroidAccountLinkKeyAuthority.InstallationIdStorageKey;
    private const string AccessTokenKey = "chummer.account.installation-grant.v1";
    private const string GrantExpiryKey = "chummer.account.installation-grant-expiry.v1";
    private const string RefreshAttemptKey = "chummer.account.installation-grant-refresh-attempt.v1";
    private const string StagedGrantCommitKey = "chummer.account.staged-grant-commit.v1";
    private const string PendingPollOperationKey = "chummer.account.pending-poll-operation.v1";
    private const string PendingStateKey = "chummer.account.pending-state.v1";
    private const string PendingStartedKey = "chummer.account.pending-started.v1";
    private const string PendingInstallationIdKey = "chummer.account.pending-installation-id.v2";
    private const string LastProofTimestampKey = "chummer.account.last-proof-timestamp.v1";
    private const string LinkPath = "/account/access/install-link";
    private const string CallbackPath = "/app/install-link";
    private const string HeadId = "android";
    private const string PlatformId = "android";
    private const string ReleaseChannel = "preview";
    private const string InstallLinkTransport = "proof_poll_v2";
    private const string BootstrapGrantCommitKind = "bootstrap";
    private const string RefreshGrantCommitKind = "refresh";
    private const int PendingPollOperationVersion = 1;
    private const int RefreshOperationVersion = 1;
    private const int StagedGrantCommitVersion = 1;
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(18);
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan RefreshRecoveryLifetime = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions OperationJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
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
    private readonly AndroidAccountLinkKeyAuthority _keyAuthority;
    private readonly IAndroidAccountLinkKeyMetadataStore _metadataStore;
    private readonly Func<string> _versionProvider;
    private readonly Func<string> _hostLabelProvider;
    private readonly Func<string> _architectureProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _credentialCommitGate = new(1, 1);
    private AndroidAccountLinkSnapshot _snapshot = new(
        AndroidAccountLinkStatus.Loading,
        AccountText("Checking", "Checking"));

    internal AndroidAccountLinkService(
        AndroidAccountLinkHttpTransport httpTransport,
        IAndroidSystemService systemService,
        AndroidAccountLinkKeyAuthority keyAuthority,
        IAndroidAccountLinkKeyMetadataStore metadataStore,
        Func<string>? versionProvider = null,
        Func<string>? hostLabelProvider = null,
        Func<string>? architectureProvider = null)
    {
        _httpTransport = httpTransport;
        _systemService = systemService;
        _keyAuthority = keyAuthority;
        _metadataStore = metadataStore;
        _versionProvider = versionProvider ?? (() => AppInfo.Current.VersionString);
        _hostLabelProvider = hostLabelProvider ?? ResolveHostLabel;
        _architectureProvider = architectureProvider ?? ResolveArchitecture;
    }

    public event EventHandler? Changed;

    public AndroidAccountLinkSnapshot Snapshot => _snapshot;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            GrantContract? recoveredCommit = await RecoverStagedGrantCommitAsync();
            if (recoveredCommit is not null)
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Linked,
                    AccountText("AccountLinked", "Linked"),
                    null,
                    recoveredCommit.ExpiresAtUtc));
                return;
            }

            await _keyAuthority.RemoveLegacyPrivateKeyAsync(cancellationToken);
            StoredGrant? grant = await ReadStoredGrantAsync(cancellationToken);
            if (grant is null)
            {
                await TryClearIncompleteGrantAsync();
                string? pendingState = await _metadataStore.GetAsync(PendingStateKey, cancellationToken);
                string? pendingInstallationId = await _metadataStore.GetAsync(
                    PendingInstallationIdKey,
                    cancellationToken);
                DateTimeOffset? pendingStarted = await ReadTimestampAsync(
                    PendingStartedKey,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(pendingState)
                    || string.IsNullOrWhiteSpace(pendingInstallationId)
                    || !IsPendingLinkCurrent(pendingStarted))
                {
                    await ClearPendingAsync(CancellationToken.None);
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Unlinked,
                        AccountText("AccountNotLinked", "Not linked")));
                }
                else
                {
                    await _keyAuthority.RequirePendingIdentityAsync(
                        pendingInstallationId,
                        cancellationToken);
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Pending,
                        AccountText("AccountFinishLinking", "Finish linking"),
                        AccountText("AccountApproveBrowser", "Approve in your browser, then return.")));
                }
                return;
            }

            DateTimeOffset? expiresAtUtc = await ReadGrantExpiryAsync(cancellationToken);
            string? serializedRefreshOperation = await _metadataStore.GetAsync(
                RefreshAttemptKey,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(serializedRefreshOperation))
            {
                RefreshOperation? refreshOperation = TryDeserializeRefreshOperation(
                    serializedRefreshOperation);
                if (refreshOperation is null
                    || !IsExpectedRefreshOperation(refreshOperation, grant))
                {
                    SetSnapshot(RefreshRecoveryExpiredSnapshot());
                    return;
                }
                if (!IsRefreshOperationCurrent(refreshOperation.StartedAtUtc))
                {
                    SetSnapshot(RefreshRecoveryExpiredSnapshot());
                    return;
                }

                // A prior request may have rotated the remote grant before its response was lost.
                // Retry the exact stored operation fields before validating or expiring the
                // superseded local grant. A fresh packet proof preserves anti-replay while the
                // stable operation digest lets Hub replay the original rotation receipt.
                GrantContract? recovered = await RefreshGrantAsync(
                    grant,
                    refreshOperation,
                    cancellationToken);
                if (recovered is null)
                {
                    SetSnapshot(RefreshRecoveryPendingSnapshot(expiresAtUtc));
                    return;
                }

                await SaveGrantAsync(
                    recovered,
                    refreshOperation.OperationId,
                    refreshOperation.PublicKey,
                    RefreshGrantCommitKind);
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Linked,
                    AccountText("AccountLinked", "Linked"),
                    null,
                    recovered.ExpiresAtUtc));
                return;
            }

            if (expiresAtUtc is null || expiresAtUtc <= DateTimeOffset.UtcNow)
            {
                await SetSnapshotAfterRejectedGrantAsync(
                    await TryClearGrantIfCurrentAsync(grant));
                return;
            }

            GrantValidationResult validation = await ValidateGrantAsync(grant, cancellationToken);
            if (validation == GrantValidationResult.Invalid)
            {
                await SetSnapshotAfterRejectedGrantAsync(
                    await TryClearGrantIfCurrentAsync(grant));
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
                RefreshOperation? refreshOperation = await CreateAndPersistRefreshOperationAsync(
                    grant,
                    cancellationToken);
                if (refreshOperation is null)
                {
                    return;
                }
                GrantContract? refreshed = await RefreshGrantAsync(
                    grant,
                    refreshOperation,
                    cancellationToken);
                if (refreshed is null)
                {
                    SetSnapshot(RefreshRecoveryPendingSnapshot(expiresAtUtc));
                    return;
                }

                await SaveGrantAsync(
                    refreshed,
                    refreshOperation.OperationId,
                    refreshOperation.PublicKey,
                    RefreshGrantCommitKind);
                expiresAtUtc = refreshed.ExpiresAtUtc;
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
        catch (AndroidDeviceRelinkRequiredException)
        {
            if (!await TryClearAllCredentialsAsync())
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Error,
                    AccountText("AccountLinkUnavailable", "Link unavailable"),
                    AccountText(
                        "AccountSecureStorageUnavailable",
                        "Android secure storage could not be opened.")));
                return;
            }
            SetSnapshot(new(
                AndroidAccountLinkStatus.Unlinked,
                AccountText("AccountFreshLinkRequired", "Fresh link required"),
                AccountText("AccountChooseLinkTryAgain", "Choose Link account and try again.")));
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
            GrantContract? recoveredCommit = await RecoverStagedGrantCommitAsync();
            if (recoveredCommit is not null)
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Linked,
                    AccountText("AccountLinked", "Linked"),
                    null,
                    recoveredCommit.ExpiresAtUtc));
                return;
            }

            bool hadStoredGrant;
            AndroidAccountLinkKeyIdentity identity;
            await _credentialCommitGate.WaitAsync(cancellationToken);
            try
            {
                hadStoredGrant = !string.IsNullOrWhiteSpace(
                    await _metadataStore.GetAsync(AccessTokenKey, CancellationToken.None));
                identity = await _keyAuthority
                    .StartOrResumeExplicitLinkAsync(CancellationToken.None);
                await ClearGrantCoreAsync(CancellationToken.None);
            }
            finally
            {
                _credentialCommitGate.Release();
            }
            string? savedState = await _metadataStore.GetAsync(PendingStateKey, cancellationToken);
            string? savedInstallationId = await _metadataStore.GetAsync(
                PendingInstallationIdKey,
                cancellationToken);
            DateTimeOffset? pendingStarted = await ReadTimestampAsync(
                PendingStartedKey,
                cancellationToken);
            bool resumeCurrentAttempt = !hadStoredGrant
                && !string.IsNullOrWhiteSpace(savedState)
                && string.Equals(savedInstallationId, identity.InstallationId, StringComparison.Ordinal)
                && IsPendingLinkCurrent(pendingStarted);
            string state = resumeCurrentAttempt ? savedState! : NewBase64UrlToken(24);
            if (!resumeCurrentAttempt)
            {
                await ClearPendingAsync(CancellationToken.None);
                await _metadataStore.SetAsync(
                    PendingInstallationIdKey,
                    identity.InstallationId,
                    cancellationToken);
                await _metadataStore.SetAsync(PendingStateKey, state, cancellationToken);
                await _metadataStore.SetAsync(
                    PendingStartedKey,
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    cancellationToken);
            }

            // The browser approval and proof-poll exchange are one immutable operation. Persist
            // its exact callback-bound fields before launching the browser so an app upgrade,
            // architecture change, process restart, or repeated Begin action cannot drift from
            // the values the Hub authorized.
            PendingPollOperation operation = await ReadOrCreatePendingPollOperationAsync(
                identity,
                cancellationToken);
            string callback = $"https://chummer.run{CallbackPath}?state={Uri.EscapeDataString(state)}";
            Dictionary<string, string> query = new(StringComparer.Ordinal)
            {
                ["installationId"] = operation.InstallationId,
                ["headId"] = operation.HeadId,
                ["applicationVersion"] = operation.ApplicationVersion,
                ["releaseChannel"] = operation.ChannelId,
                ["platform"] = operation.Platform,
                ["arch"] = operation.Architecture,
                ["installLinkCallbackUri"] = callback,
                ["installLinkTransport"] = operation.InstallLinkTransport,
                ["publicKey"] = operation.PublicKey
            };
            string href = $"{LinkPath}?{string.Join('&', query.Select(static item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"))}";
            SetSnapshot(new(
                AndroidAccountLinkStatus.Pending,
                AccountText("AccountFinishLinking", "Finish linking"),
                AccountText("AccountApproveBrowser", "Approve in your browser, then return.")));
            if (!await _systemService.OpenUriAsync(ChummerWebRoutes.Resolve(href)))
            {
                // Platform launch results can be ambiguous. Retain the pending state, immutable
                // operation, and non-exportable key so a later Begin/Resume can safely recover.
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
            GrantContract? recoveredCommit = await RecoverStagedGrantCommitAsync();
            if (recoveredCommit is not null)
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Linked,
                    AccountText("AccountLinked", "Linked"),
                    null,
                    recoveredCommit.ExpiresAtUtc));
                return;
            }

            string? pendingState = await _metadataStore.GetAsync(PendingStateKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(pendingState))
            {
                return;
            }

            string? pendingInstallationId = await _metadataStore.GetAsync(
                PendingInstallationIdKey,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(pendingInstallationId))
            {
                await ClearPendingAsync(CancellationToken.None);
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Error,
                    AccountText("AccountApprovalExpired", "Approval expired"),
                    AccountText("AccountStartFreshLink", "Start a fresh account link.")));
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

            DateTimeOffset? pendingStarted = await ReadTimestampAsync(
                PendingStartedKey,
                cancellationToken);
            if (!IsPendingLinkCurrent(pendingStarted))
            {
                await ClearPendingAsync(CancellationToken.None);
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Error,
                    AccountText("AccountApprovalExpired", "Approval expired"),
                    AccountText("AccountStartFreshLink", "Start a fresh account link.")));
                return;
            }

            AndroidAccountLinkKeyIdentity identity = await _keyAuthority
                .RequirePendingIdentityAsync(pendingInstallationId, cancellationToken);
            PendingPollOperation operation = await ReadOrCreatePendingPollOperationAsync(
                identity,
                cancellationToken);

            long issuedAt = await NextProofTimestampAsync();
            string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            byte[] proof = AndroidAccountLinkBootstrapProof.CreateCanonicalPayload(
                operation.InstallLinkTransport,
                operation.OperationId,
                operation.InstallationId,
                operation.HeadId,
                operation.ApplicationVersion,
                operation.ChannelId,
                operation.Platform,
                operation.Architecture,
                issuedAt,
                nonce,
                operation.HostLabel);
            string signature;
            try
            {
                byte[] signatureBytes = await _keyAuthority.SignAsync(identity, proof, cancellationToken);
                try
                {
                    signature = Convert.ToBase64String(signatureBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(signatureBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(proof);
            }

            PollRequest request = new(
                operation.InstallLinkTransport,
                operation.OperationId,
                operation.InstallationId,
                operation.HeadId,
                operation.ApplicationVersion,
                operation.ChannelId,
                operation.Platform,
                operation.Architecture,
                operation.PublicKey,
                issuedAt,
                nonce,
                signature,
                operation.HostLabel);
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
                    // A successful redemption response can be lost before local commit. Conflict
                    // is therefore ambiguous and must never destroy the pending installation/key;
                    // a later idempotent poll can still replay the originally issued grant.
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Pending,
                        AccountText("AccountFinishLinking", "Finish linking"),
                        AccountText(
                            "AccountApprovalReceivedRetry",
                            "Approval was received. Keep this link and try again.")));
                }
                else if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    await ClearAllCredentialsAsync(CancellationToken.None);
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Error,
                        AccountText("AccountFreshLinkRequired", "Fresh link required"),
                        AccountText("AccountChooseLinkTryAgain", "Choose Link account and try again.")));
                }
                else if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    await ClearPendingAsync(CancellationToken.None);
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
            if (!string.Equals(
                    exchange.OperationId,
                    operation.OperationId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Chummer returned a mismatched link operation.");
            }
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

            await SaveGrantAsync(
                grant,
                operation.OperationId,
                operation.PublicKey,
                BootstrapGrantCommitKind);
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
        catch (AndroidDeviceRelinkRequiredException)
        {
            if (!await TryClearAllCredentialsAsync())
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Error,
                    AccountText("AccountLinkUnavailable", "Link unavailable"),
                    AccountText(
                        "AccountSecureStorageUnavailable",
                        "Android secure storage could not be opened.")));
                return;
            }
            SetSnapshot(new(
                AndroidAccountLinkStatus.Error,
                AccountText("AccountFreshLinkRequired", "Fresh link required"),
                AccountText("AccountChooseLinkTryAgain", "Choose Link account and try again.")));
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
            grantExpiresAtUtc ??= await ReadGrantExpiryAsync(cancellationToken);
            StoredGrant? grant = await ReadStoredGrantAsync(cancellationToken);
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

            if (await TryClearAllCredentialsAsync())
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Unlinked,
                    AccountText("AccountNotLinked", "Not linked")));
            }
            else
            {
                SetSnapshot(LocalCleanupPendingSnapshot());
            }
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
            StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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

            if (await TryClearAllCredentialsAsync())
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Unlinked,
                    AccountText("AccountDeleted", "Account deleted")));
            }
            else
            {
                SetSnapshot(LocalCleanupPendingSnapshot());
            }
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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
        StoredGrant grant = await RequireStoredGrantAsync(cancellationToken);
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

    private async Task<StoredGrant> RequireStoredGrantAsync(CancellationToken cancellationToken)
    {
        StoredGrant? grant = await ReadStoredGrantAsync(cancellationToken);
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
            if (await TryClearGrantIfCurrentAsync(grant))
            {
                SetSnapshot(new(
                    AndroidAccountLinkStatus.Unlinked,
                    AccountText("AccountLinkExpired", "Link expired"),
                    AccountText("AccountLinkAgainRestore", "Link again to restore account access.")));
                throw new InvalidOperationException("Link your account again.");
            }

            // The request raced a refresh or another credential transition. Its rejection says
            // nothing about the newer generation, so leave both credentials and linked UI state
            // untouched and let the caller retry with a fresh snapshot.
            throw new InvalidOperationException("The account link changed. Try again.");
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
        RefreshOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            AndroidAccountLinkRequestAuthority authority = CreateRequestAuthority(grant);
            using HttpResponseMessage response = await _httpTransport.PostJsonAsync(
                "/api/v2/install-linking/grants/refresh",
                new RefreshRequest(
                    operation.OperationId,
                    operation.InstallationId,
                    operation.HeadId,
                    operation.ApplicationVersion,
                    operation.ChannelId,
                    operation.Platform,
                    operation.Architecture,
                    operation.PublicKey,
                    operation.HostLabel),
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
            if (!string.Equals(
                    refreshed.OperationId,
                    operation.OperationId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Chummer returned a mismatched refresh operation.");
            }
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

    private async Task SaveGrantAsync(
        GrantContract grant,
        string operationId,
        string publicKey,
        string commitKind)
    {
        StagedGrantCommit staged = new(
            StagedGrantCommitVersion,
            commitKind,
            operationId,
            publicKey,
            grant);
        if (!IsExpectedStagedGrantCommit(staged))
        {
            throw new InvalidDataException("Chummer returned an invalid account grant.");
        }

        // Once the authenticated response and echoed operation ID have been accepted, caller
        // cancellation can no longer interrupt the local transaction. The exact replacement
        // bundle is written first; it remains the restart selector until all active credential
        // fields and the key-binding commit marker are durably consistent.
        await _credentialCommitGate.WaitAsync(CancellationToken.None);
        try
        {
            await _metadataStore.SetAsync(
                StagedGrantCommitKey,
                JsonSerializer.Serialize(staged, OperationJsonOptions),
                CancellationToken.None);
            await FinalizeStagedGrantCommitAsync(staged);
            await TryCleanupStagedGrantCommitAsync(staged);
        }
        finally
        {
            _credentialCommitGate.Release();
        }
    }

    private async Task<GrantContract?> RecoverStagedGrantCommitAsync()
    {
        await _credentialCommitGate.WaitAsync(CancellationToken.None);
        try
        {
            return await RecoverStagedGrantCommitCoreAsync();
        }
        finally
        {
            _credentialCommitGate.Release();
        }
    }

    private async Task<GrantContract?> RecoverStagedGrantCommitCoreAsync()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string? serialized = await _metadataStore.GetAsync(
                StagedGrantCommitKey,
                CancellationToken.None);
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return null;
            }

            StagedGrantCommit? staged = TryDeserializeStagedGrantCommit(serialized);
            if (staged is null || !IsExpectedStagedGrantCommit(staged))
            {
                if (!await QuarantineInvalidStagedGrantCommitAsync(serialized))
                {
                    continue;
                }
                return null;
            }

            try
            {
                await ValidateStagedGrantOperationAsync(staged);
            }
            catch (InvalidDataException)
            {
                if (!await QuarantineInvalidStagedGrantCommitAsync(serialized))
                {
                    continue;
                }
                return null;
            }

            await FinalizeStagedGrantCommitAsync(staged);
            await TryCleanupStagedGrantCommitAsync(staged);
            return staged.Grant;
        }

        throw new InvalidDataException("The staged account grant changed during recovery.");
    }

    private async Task<bool> QuarantineInvalidStagedGrantCommitAsync(string observedStage)
    {
        string? currentStage = await _metadataStore.GetAsync(
            StagedGrantCommitKey,
            CancellationToken.None);
        if (!FixedTimeEqualsSecret(currentStage, observedStage))
        {
            // Another serialized commit owner installed a newer generation. Restart validation
            // from that exact stage and never clean using observations from the older value.
            return false;
        }

        string? pendingOperation = await _metadataStore.GetAsync(
            PendingPollOperationKey,
            CancellationToken.None);
        string? refreshOperation = await _metadataStore.GetAsync(
            RefreshAttemptKey,
            CancellationToken.None);
        if (string.IsNullOrWhiteSpace(pendingOperation)
            && string.IsNullOrWhiteSpace(refreshOperation))
        {
            string? activeInstallationId = await _metadataStore.GetAsync(
                InstallationIdKey,
                CancellationToken.None);
            if (!AndroidAccountLinkKeyAuthority.IsExpectedInstallationId(activeInstallationId))
            {
                // A missing or malformed active installation selector means this cannot be a
                // harmless post-commit stage orphan. Treat the stage and dependent active fields
                // as one torn credential generation and retain key-cleanup safety via RemoveAsync.
                await ClearAllCredentialsCoreAsync(CancellationToken.None);
                return true;
            }

            // Operation cleanup precedes stage removal, so this is a post-commit orphan. Remove
            // only the exact bad stage when the active installation selector is independently
            // well formed; the already-committed active generation may be newer and independent.
            await _metadataStore.RemoveAsync(StagedGrantCommitKey, CancellationToken.None);
            return true;
        }

        // An unusable stage with its operation still present can describe torn active fields.
        // Fail closed through key-authority cleanup, which persists a durable alias tombstone if
        // Android Keystore deletion fails, and remove every dependent credential selector.
        await ClearAllCredentialsCoreAsync(CancellationToken.None);
        return true;
    }

    private async Task ValidateStagedGrantOperationAsync(StagedGrantCommit staged)
    {
        string? installationId = await _metadataStore.GetAsync(
            InstallationIdKey,
            CancellationToken.None);
        if (!string.Equals(installationId, staged.Grant.InstallationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The staged account grant installation is invalid.");
        }

        string operationKey = staged.CommitKind == BootstrapGrantCommitKind
            ? PendingPollOperationKey
            : RefreshAttemptKey;
        string? serializedOperation = await _metadataStore.GetAsync(
            operationKey,
            CancellationToken.None);
        if (string.IsNullOrWhiteSpace(serializedOperation))
        {
            // Operation cleanup runs before removal of the staged bundle. If that cleanup
            // completed but stage removal failed, only an already-bound exact replacement may
            // be finalized again.
            AndroidAccountLinkKeyIdentity committed = await _keyAuthority
                .RequireLinkedIdentityAsync(staged.Grant.InstallationId, CancellationToken.None);
            if (!string.Equals(committed.GrantId, staged.Grant.GrantId, StringComparison.Ordinal)
                || !string.Equals(committed.PublicKey, staged.PublicKey, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The staged account grant lost its operation binding.");
            }
            return;
        }

        if (staged.CommitKind == BootstrapGrantCommitKind)
        {
            PendingPollOperation? operation = TryDeserializePendingPollOperation(serializedOperation);
            if (operation is null
                || operation.Version != PendingPollOperationVersion
                || !string.Equals(
                    operation.InstallLinkTransport,
                    InstallLinkTransport,
                    StringComparison.Ordinal)
                || !string.Equals(operation.OperationId, staged.OperationId, StringComparison.Ordinal)
                || !string.Equals(
                    operation.InstallationId,
                    staged.Grant.InstallationId,
                    StringComparison.Ordinal)
                || !string.Equals(operation.PublicKey, staged.PublicKey, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The staged account grant operation is invalid.");
            }
            return;
        }

        RefreshOperation? refreshOperation = TryDeserializeRefreshOperation(serializedOperation);
        if (refreshOperation is null
            || refreshOperation.Version != RefreshOperationVersion
            || !string.Equals(
                refreshOperation.OperationId,
                staged.OperationId,
                StringComparison.Ordinal)
            || !string.Equals(
                refreshOperation.InstallationId,
                staged.Grant.InstallationId,
                StringComparison.Ordinal)
            || !string.Equals(refreshOperation.PublicKey, staged.PublicKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The staged account refresh operation is invalid.");
        }
    }

    private async Task FinalizeStagedGrantCommitAsync(StagedGrantCommit staged)
    {
        await _metadataStore.SetAsync(
            AccessTokenKey,
            staged.Grant.AccessToken,
            CancellationToken.None);
        await _metadataStore.SetAsync(
            GrantExpiryKey,
            staged.Grant.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CancellationToken.None);
        // The binding is the final consistency marker. Until it succeeds, the staged exact
        // bundle remains present and every credential read resumes this idempotent finalize.
        await _keyAuthority.BindGrantAsync(
            staged.Grant.InstallationId,
            staged.Grant.GrantId,
            CancellationToken.None);
    }

    private async Task TryCleanupStagedGrantCommitAsync(StagedGrantCommit staged)
    {
        try
        {
            if (staged.CommitKind == BootstrapGrantCommitKind)
            {
                await ClearPendingAsync(CancellationToken.None);
            }
            else
            {
                await _metadataStore.RemoveAsync(
                    RefreshAttemptKey,
                    CancellationToken.None);
            }

            // Stage removal is deliberately last. Any interrupted or failed operation cleanup
            // therefore leaves a complete restart selector instead of mixed active fields.
            await _metadataStore.RemoveAsync(
                StagedGrantCommitKey,
                CancellationToken.None);
        }
        catch (Exception)
        {
            // The active credentials are already committed. A future read retries cleanup using
            // the retained staged bundle; UI state must not regress from Linked.
        }
    }

    private static StagedGrantCommit? TryDeserializeStagedGrantCommit(string serialized)
    {
        try
        {
            return JsonSerializer.Deserialize<StagedGrantCommit>(
                serialized,
                OperationJsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsExpectedStagedGrantCommit(StagedGrantCommit staged)
        => staged.Version == StagedGrantCommitVersion
            && staged.CommitKind is BootstrapGrantCommitKind or RefreshGrantCommitKind
            && IsExpectedOperationId(staged.OperationId)
            && IsBoundedOperationText(staged.PublicKey, 4096)
            // System.Text.Json permits null for a non-nullable reference property unless the
            // runtime contract opts into stricter required-member handling. Treat an omitted or
            // explicit-null nested grant as hostile persisted data and route it through the same
            // fail-closed quarantine as every other unusable stage.
            && staged.Grant is not null
            && AndroidAccountLinkKeyAuthority.IsExpectedInstallationId(
                staged.Grant.InstallationId)
            && IsUsableGrant(staged.Grant, staged.Grant.InstallationId);

    private async Task<StoredGrant?> ReadStoredGrantAsync(CancellationToken cancellationToken)
    {
        await _credentialCommitGate.WaitAsync(CancellationToken.None);
        try
        {
            await RecoverStagedGrantCommitCoreAsync();
            string? installationId = await _metadataStore.GetAsync(
                InstallationIdKey,
                cancellationToken);
            string? accessToken = await _metadataStore.GetAsync(
                AccessTokenKey,
                cancellationToken);
            return string.IsNullOrWhiteSpace(installationId)
                || string.IsNullOrWhiteSpace(accessToken)
                    ? null
                    : new StoredGrant(
                        await _keyAuthority.RequireLinkedIdentityAsync(
                            installationId,
                            cancellationToken),
                        accessToken);
        }
        finally
        {
            _credentialCommitGate.Release();
        }
    }

    private AndroidAccountLinkRequestAuthority CreateRequestAuthority(StoredGrant grant)
        => new(
            grant.InstallationId,
            grant.GrantId,
            grant.AccessToken,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            (canonical, token) => _keyAuthority.SignAsync(grant.Identity, canonical, token));

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
    private async Task<DateTimeOffset?> ReadGrantExpiryAsync(CancellationToken cancellationToken)
        => await ReadTimestampAsync(GrantExpiryKey, cancellationToken);

    private async Task<DateTimeOffset?> ReadTimestampAsync(
        string key,
        CancellationToken cancellationToken)
    {
        string? value = await _metadataStore.GetAsync(key, cancellationToken);
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

    private async Task<long> NextProofTimestampAsync()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string? saved = await _metadataStore.GetAsync(
            LastProofTimestampKey,
            CancellationToken.None);
        if (long.TryParse(saved, NumberStyles.None, CultureInfo.InvariantCulture, out long last) && now <= last)
        {
            now = last + 1;
        }

        await _metadataStore.SetAsync(
            LastProofTimestampKey,
            now.ToString(CultureInfo.InvariantCulture),
            CancellationToken.None);
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

    private async Task<PendingPollOperation> ReadOrCreatePendingPollOperationAsync(
        AndroidAccountLinkKeyIdentity identity,
        CancellationToken cancellationToken)
    {
        string? serialized = await _metadataStore.GetAsync(
            PendingPollOperationKey,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(serialized))
        {
            PendingPollOperation? stored = TryDeserializePendingPollOperation(serialized);
            if (stored is not null && IsExpectedPendingPollOperation(stored, identity))
            {
                return stored;
            }

            throw new AndroidDeviceRelinkRequiredException(
                AndroidDeviceKeyAvailability.Invalidated,
                "The pending account-link operation is invalid.");
        }

        PendingPollOperation created = new(
            PendingPollOperationVersion,
            InstallLinkTransport,
            NewBase64UrlToken(24),
            identity.InstallationId,
            HeadId,
            _versionProvider(),
            ReleaseChannel,
            PlatformId,
            _architectureProvider(),
            identity.PublicKey,
            _hostLabelProvider());
        if (!IsExpectedPendingPollOperation(created, identity))
        {
            throw new InvalidDataException("The pending account-link operation is invalid.");
        }

        await _metadataStore.SetAsync(
            PendingPollOperationKey,
            JsonSerializer.Serialize(created, OperationJsonOptions),
            cancellationToken);
        return created;
    }

    private RefreshOperation CreateRefreshOperation(StoredGrant grant)
    {
        RefreshOperation created = new(
            RefreshOperationVersion,
            NewBase64UrlToken(24),
            DateTimeOffset.UtcNow,
            grant.InstallationId,
            grant.GrantId,
            HeadId,
            _versionProvider(),
            ReleaseChannel,
            PlatformId,
            _architectureProvider(),
            grant.Identity.PublicKey,
            _hostLabelProvider());
        return IsExpectedRefreshOperation(created, grant)
            ? created
            : throw new InvalidDataException("The account refresh operation is invalid.");
    }

    private async Task<RefreshOperation?> CreateAndPersistRefreshOperationAsync(
        StoredGrant grant,
        CancellationToken cancellationToken)
    {
        await _credentialCommitGate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(await _metadataStore.GetAsync(
                    StagedGrantCommitKey,
                    CancellationToken.None))
                || !string.IsNullOrWhiteSpace(await _metadataStore.GetAsync(
                    RefreshAttemptKey,
                    CancellationToken.None))
                || !await IsExactActiveGrantCoreAsync(grant))
            {
                return null;
            }

            RefreshOperation operation = CreateRefreshOperation(grant);
            // Publication shares the exact lock used by compare-and-clear. Once this section
            // starts, a stale rejection can observe either the old exact generation before the
            // transition or this durable operation marker, never an absent-marker write gap.
            await _metadataStore.SetAsync(
                RefreshAttemptKey,
                JsonSerializer.Serialize(operation, OperationJsonOptions),
                CancellationToken.None);
            return operation;
        }
        finally
        {
            _credentialCommitGate.Release();
        }
    }

    private static PendingPollOperation? TryDeserializePendingPollOperation(string serialized)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingPollOperation>(
                serialized,
                OperationJsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static RefreshOperation? TryDeserializeRefreshOperation(string serialized)
    {
        try
        {
            return JsonSerializer.Deserialize<RefreshOperation>(
                serialized,
                OperationJsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsExpectedPendingPollOperation(
        PendingPollOperation operation,
        AndroidAccountLinkKeyIdentity identity)
        => operation.Version == PendingPollOperationVersion
            && string.Equals(
                operation.InstallLinkTransport,
                InstallLinkTransport,
                StringComparison.Ordinal)
            && IsExpectedOperationId(operation.OperationId)
            && string.Equals(operation.InstallationId, identity.InstallationId, StringComparison.Ordinal)
            && string.Equals(operation.PublicKey, identity.PublicKey, StringComparison.Ordinal)
            && HasExpectedOperationFields(
                operation.HeadId,
                operation.ApplicationVersion,
                operation.ChannelId,
                operation.Platform,
                operation.Architecture,
                operation.HostLabel);

    private static bool IsExpectedRefreshOperation(
        RefreshOperation operation,
        StoredGrant grant)
        => operation.Version == RefreshOperationVersion
            && IsExpectedOperationId(operation.OperationId)
            && string.Equals(operation.InstallationId, grant.InstallationId, StringComparison.Ordinal)
            && string.Equals(operation.SourceGrantId, grant.GrantId, StringComparison.Ordinal)
            && string.Equals(operation.PublicKey, grant.Identity.PublicKey, StringComparison.Ordinal)
            && HasExpectedOperationFields(
                operation.HeadId,
                operation.ApplicationVersion,
                operation.ChannelId,
                operation.Platform,
                operation.Architecture,
                operation.HostLabel);

    private static bool HasExpectedOperationFields(
        string headId,
        string applicationVersion,
        string channelId,
        string platform,
        string architecture,
        string hostLabel)
        => string.Equals(headId, HeadId, StringComparison.Ordinal)
            && IsBoundedOperationText(applicationVersion, 64)
            && string.Equals(channelId, ReleaseChannel, StringComparison.Ordinal)
            && string.Equals(platform, PlatformId, StringComparison.Ordinal)
            && architecture is "arm64" or "arm" or "x64" or "x86" or "unknown"
            && IsBoundedOperationText(hostLabel, 120);

    private static bool IsBoundedOperationText(string? value, int maximumLength)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumLength
            && !value.Any(char.IsControl);

    private static bool IsExpectedOperationId(string? value)
        => value is { Length: 32 }
            && value.AsSpan().IndexOfAnyExcept(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".AsSpan()) < 0;

    private static bool IsRefreshOperationCurrent(DateTimeOffset startedAtUtc)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return startedAtUtc >= now.Subtract(RefreshRecoveryLifetime)
            && startedAtUtc <= now.AddMinutes(2);
    }

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

    private async Task<bool> TryClearGrantIfCurrentAsync(StoredGrant rejectedGrant)
    {
        await _credentialCommitGate.WaitAsync(CancellationToken.None);
        try
        {
            // Presence of any staged generation makes destructive cleanup ambiguous. The commit
            // owner will either finish it or retain the exact restart bundle.
            string? staged = await _metadataStore.GetAsync(
                StagedGrantCommitKey,
                CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(staged))
            {
                return false;
            }
            string? refreshOperation = await _metadataStore.GetAsync(
                RefreshAttemptKey,
                CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(refreshOperation))
            {
                // The operation is persisted before its request. Protect that pre-stage window:
                // the in-flight response can still install a newer credential bundle.
                return false;
            }
            if (!await IsExactActiveGrantCoreAsync(rejectedGrant))
            {
                return false;
            }

            await ClearGrantCoreAsync(CancellationToken.None);
            return true;
        }
        finally
        {
            _credentialCommitGate.Release();
        }
    }

    private async Task<bool> IsExactActiveGrantCoreAsync(StoredGrant expectedGrant)
    {
        string? installationId = await _metadataStore.GetAsync(
            InstallationIdKey,
            CancellationToken.None);
        string? accessToken = await _metadataStore.GetAsync(
            AccessTokenKey,
            CancellationToken.None);
        if (!string.Equals(
                installationId,
                expectedGrant.InstallationId,
                StringComparison.Ordinal)
            || !FixedTimeEqualsSecret(accessToken, expectedGrant.AccessToken))
        {
            return false;
        }

        AndroidAccountLinkKeyIdentity current = await _keyAuthority
            .RequireLinkedIdentityAsync(expectedGrant.InstallationId, CancellationToken.None);
        return string.Equals(current.GrantId, expectedGrant.GrantId, StringComparison.Ordinal)
            && string.Equals(
                current.PublicKey,
                expectedGrant.Identity.PublicKey,
                StringComparison.Ordinal);
    }

    private static bool FixedTimeEqualsSecret(string? left, string right)
    {
        if (left is null)
        {
            return false;
        }

        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        try
        {
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private async Task<bool> TryClearIncompleteGrantAsync()
    {
        await _credentialCommitGate.WaitAsync(CancellationToken.None);
        try
        {
            if (!string.IsNullOrWhiteSpace(await _metadataStore.GetAsync(
                    StagedGrantCommitKey,
                    CancellationToken.None)))
            {
                return false;
            }

            string? installationId = await _metadataStore.GetAsync(
                InstallationIdKey,
                CancellationToken.None);
            string? accessToken = await _metadataStore.GetAsync(
                AccessTokenKey,
                CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(installationId)
                && !string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            await ClearGrantCoreAsync(CancellationToken.None);
            return true;
        }
        finally
        {
            _credentialCommitGate.Release();
        }
    }

    private async Task ClearGrantCoreAsync(CancellationToken cancellationToken)
    {
        await _metadataStore.RemoveAsync(AccessTokenKey, cancellationToken);
        await _metadataStore.RemoveAsync(GrantExpiryKey, cancellationToken);
        await _metadataStore.RemoveAsync(RefreshAttemptKey, cancellationToken);
        await _metadataStore.RemoveAsync(StagedGrantCommitKey, cancellationToken);
    }

    private async Task ClearPendingAsync(CancellationToken cancellationToken)
    {
        await _metadataStore.RemoveAsync(PendingStateKey, cancellationToken);
        await _metadataStore.RemoveAsync(PendingStartedKey, cancellationToken);
        await _metadataStore.RemoveAsync(PendingInstallationIdKey, cancellationToken);
        await _metadataStore.RemoveAsync(PendingPollOperationKey, cancellationToken);
    }

    private async Task ClearAllCredentialsAsync(CancellationToken cancellationToken)
    {
        await _credentialCommitGate.WaitAsync(cancellationToken);
        try
        {
            await ClearAllCredentialsCoreAsync(cancellationToken);
        }
        finally
        {
            _credentialCommitGate.Release();
        }
    }

    private async Task ClearAllCredentialsCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _keyAuthority.RemoveAsync(cancellationToken);
        }
        finally
        {
            await ClearGrantCoreAsync(CancellationToken.None);
            await ClearPendingAsync(CancellationToken.None);
            await _metadataStore.RemoveAsync(LastProofTimestampKey, CancellationToken.None);
        }
    }

    private async Task<bool> TryClearAllCredentialsAsync()
    {
        try
        {
            await ClearAllCredentialsAsync(CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static AndroidAccountLinkSnapshot LocalCleanupPendingSnapshot()
        => new(
            AndroidAccountLinkStatus.Unlinked,
            AccountText("AccountFreshLinkRequired", "Fresh link required"),
            AccountText(
                "AccountLocalCleanupPending",
                "Remote access ended. Local key cleanup will be retried."));

    private async Task SetSnapshotAfterRejectedGrantAsync(bool rejectedGenerationCleared)
    {
        if (rejectedGenerationCleared)
        {
            SetSnapshot(new(
                AndroidAccountLinkStatus.Unlinked,
                AccountText("AccountLinkExpired", "Link expired"),
                AccountText("AccountLinkAgainRestore", "Link again to restore account access.")));
            return;
        }

        GrantContract? recovered = await RecoverStagedGrantCommitAsync();
        DateTimeOffset? expiresAtUtc = recovered?.ExpiresAtUtc
            ?? await ReadGrantExpiryAsync(CancellationToken.None);
        SetSnapshot(new(
            AndroidAccountLinkStatus.Linked,
            AccountText("AccountLinked", "Linked"),
            null,
            expiresAtUtc));
    }

    private static AndroidAccountLinkSnapshot RefreshRecoveryPendingSnapshot(
        DateTimeOffset? grantExpiresAtUtc)
        => new(
            AndroidAccountLinkStatus.Error,
            AccountText("AccountRefreshPending", "Finishing account security refresh"),
            AccountText(
                "AccountRefreshRetry",
                "Keep this link and reconnect to finish the refresh."),
            grantExpiresAtUtc);

    private static AndroidAccountLinkSnapshot RefreshRecoveryExpiredSnapshot()
        => new(
            AndroidAccountLinkStatus.Error,
            AccountText("AccountFreshLinkRequired", "Fresh link required"),
            AccountText("AccountChooseLinkTryAgain", "Choose Link account and try again."));

    private enum GrantValidationResult
    {
        Valid,
        Invalid,
        Offline
    }

    private sealed class StoredGrant
    {
        internal StoredGrant(AndroidAccountLinkKeyIdentity identity, string accessToken)
        {
            Identity = identity;
            AccessToken = accessToken;
        }

        internal AndroidAccountLinkKeyIdentity Identity { get; }
        internal string InstallationId => Identity.InstallationId;
        internal string GrantId => Identity.GrantId!;
        internal string AccessToken { get; }

        public override string ToString()
            => $"StoredGrant {{ InstallationId = {InstallationId}, GrantId = {GrantId}, AccessToken = [REDACTED], Key = [NON-EXPORTABLE] }}";
    }

    private sealed record InstallationGrantRequest(string InstallationId);
    private sealed record AccountErasureRequest(
        string InstallationId,
        string Confirmation);
    private sealed record PollRequest(
        string InstallLinkTransport,
        string OperationId,
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
        string OperationId,
        string InstallationId,
        string HeadId,
        string ApplicationVersion,
        string ChannelId,
        string Platform,
        string Architecture,
        string PublicKey,
        string HostLabel);
    private sealed record PendingPollOperation(
        int Version,
        string InstallLinkTransport,
        string OperationId,
        string InstallationId,
        string HeadId,
        string ApplicationVersion,
        string ChannelId,
        string Platform,
        string Architecture,
        string PublicKey,
        string HostLabel);
    private sealed record RefreshOperation(
        int Version,
        string OperationId,
        DateTimeOffset StartedAtUtc,
        string InstallationId,
        string SourceGrantId,
        string HeadId,
        string ApplicationVersion,
        string ChannelId,
        string Platform,
        string Architecture,
        string PublicKey,
        string HostLabel);
    private sealed record StagedGrantCommit(
        int Version,
        string CommitKind,
        string OperationId,
        string PublicKey,
        GrantContract Grant);
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
    private sealed record ExchangeResponse(
        GrantMetadata Grant,
        bool AlreadyClaimed,
        string OperationId);
    private sealed record RefreshResponse(
        GrantMetadata Grant,
        bool Rotated,
        string OperationId);
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
