using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Android.Platform;

internal static class Program
{
    private const string AccessToken = "hostile-secret-token-that-must-never-be-logged";
    private const string RotatedAccessToken = "rotated-secret-token-that-must-never-be-logged";
    private const string StoredAccessTokenKey = "chummer.account.installation-grant.v1";
    private const string StoredGrantExpiryKey = "chummer.account.installation-grant-expiry.v1";
    private const string RefreshAttemptKey = "chummer.account.installation-grant-refresh-attempt.v1";
    private const string PendingStateKey = "chummer.account.pending-state.v1";
    private const string PendingStartedKey = "chummer.account.pending-started.v1";
    private const string PendingInstallationIdKey = "chummer.account.pending-installation-id.v2";

    private static async Task Main()
    {
        await BearerTokenIsRequestBoundAndRedacted();
        await PacketProofBindsExactBodyAndCaseSensitivePath();
        await BootstrapProofAndPollBodyMatchV2Contract();
        ResponseGrantHeadersAreSingleBoundedAndRedacted();
        await BearerTokenCannotBeSerializedIntoRequestBody();
        ServiceRequestDtosCannotCarryAccessToken();
        BearerAuthorityRejectsHeaderInjectionWithoutEchoingCredential();
        await ResponseHeadersReadAvoidsImplicitBuffering();
        await OversizedChunkedResponseIsRejected();
        await MissingAndOversizedContentLengthAreHandledSafely();
        await ResponseBodyReadRetainsABoundedTimeout();
        await CrossOriginRedirectCannotReceiveBearerToken();
        await SameOriginRedirectAndUnsafePathsFailClosed();
        await MalformedCollectionsFailClosedWithoutEchoingResponseData();
        await LostBootstrapResponseCanReplayTheOriginalGrantAsync();
        await AlreadyRedeemedBootstrapConflictRetainsFreshCredentialsAsync();
        await SuccessfulUnlinkCannotLeaveAStaleLinkedSnapshotAsync();
        await SuccessfulErasureCannotLeaveAStaleLinkedSnapshotAsync();
        await LostRefreshResponseReusesItsDurableIdempotencyKeyAsync();
        Console.WriteLine("Account-link HTTP hardening tests passed: 19");
    }

    private static async Task BearerTokenIsRequestBoundAndRedacted()
    {
        var terminal = new RecordingHandler(_ => JsonResponse("{\"ok\":true}"));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);

        using HttpResponseMessage response = await transport.PostJsonAsync(
            "/api/v2/android/linked/groups/create",
            new GroupRequest("android-install", "Runners", "private"),
            CreateAuthority(),
            CancellationToken.None);

        ObservedRequest observed = RequireSingle(terminal.Requests);
        Require(observed.AuthorizationScheme == "Bearer");
        Require(observed.AuthorizationParameter == AccessToken);
        Require(!observed.Body.Contains(AccessToken, StringComparison.Ordinal));
        Require(!observed.Body.Contains("accessToken", StringComparison.OrdinalIgnoreCase));
        Require(!observed.Diagnostic.Contains(AccessToken, StringComparison.Ordinal));
        Require(observed.Diagnostic.Contains("Bearer [REDACTED]", StringComparison.Ordinal));
        Require(observed.ContentType == "application/json; charset=utf-8");
        Require(response.RequestMessage is null
            || !response.RequestMessage.ToString().Contains(AccessToken, StringComparison.Ordinal));
    }

    private static async Task PacketProofBindsExactBodyAndCaseSensitivePath()
    {
        const string exactPath = "/api/v2/android/linked/groups/CaseSensitive-42/update";
        const string installationId = "android-install";
        const string grantId = "grant-v2";
        const long issuedAt = 1_788_543_210;
        using RSA signingKey = RSA.Create(2048);
        var terminal = new RecordingHandler(_ => JsonResponse("{\"ok\":true}"));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        var authority = new AndroidAccountLinkRequestAuthority(
            installationId,
            grantId,
            AccessToken,
            issuedAt,
            (canonical, _) => Task.FromResult(signingKey.SignData(
                canonical.Span,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1)));

        using HttpResponseMessage response = await transport.PostJsonAsync(
            exactPath,
            new GroupRequest(installationId, "Rünners ∆", "private"),
            authority,
            CancellationToken.None);

        ObservedRequest observed = RequireSingle(terminal.Requests);
        Require(observed.ProofScheme == AndroidAccountLinkRequestAuthority.Scheme);
        Require(observed.InstallationId == installationId);
        Require(observed.GrantId == grantId);
        Require(observed.IssuedAt == issuedAt.ToString(CultureInfo.InvariantCulture));
        Require(observed.PacketKey is { Length: 43 });
        Require(observed.Signature is not null);
        byte[] canonical = AndroidAccountLinkRequestAuthority.CreateCanonicalPayload(
            HttpMethod.Post,
            exactPath,
            installationId,
            grantId,
            issuedAt,
            observed.PacketKey!,
            observed.BodyBytes);
        try
        {
            Require(signingKey.VerifyData(
                canonical,
                Convert.FromBase64String(observed.Signature!),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));
            string canonicalText = Encoding.UTF8.GetString(canonical);
            string expectedDigest = Convert.ToHexString(SHA256.HashData(observed.BodyBytes))
                .ToLowerInvariant();
            string expectedCanonical = string.Join(
                '\n',
                AndroidAccountLinkRequestAuthority.Scheme,
                "POST",
                exactPath,
                installationId,
                grantId,
                issuedAt.ToString(CultureInfo.InvariantCulture),
                observed.PacketKey,
                $"sha256:{expectedDigest}");
            Require(canonicalText == expectedCanonical);
            Require(!canonicalText.EndsWith('\n'));
            Require(canonicalText.Split('\n').Length == 8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }
    }

    private static async Task BootstrapProofAndPollBodyMatchV2Contract()
    {
        const long issuedAt = 1_788_543_210;
        const string nonce = "bootstrap-nonce";
        const string hostLabel = "Runner Phone";
        byte[] canonical = AndroidAccountLinkBootstrapProof.CreateCanonicalPayload(
            "android-install",
            "android",
            "0.1.0-preview.11",
            "preview",
            "android",
            "arm64",
            issuedAt,
            nonce,
            hostLabel);
        try
        {
            Require(Encoding.UTF8.GetString(canonical) == string.Join(
                '\n',
                AndroidAccountLinkBootstrapProof.Scheme,
                "POST",
                AndroidAccountLinkBootstrapProof.PollPath,
                "android-install",
                "android",
                "0.1.0-preview.11",
                "preview",
                "android",
                "arm64",
                issuedAt.ToString(CultureInfo.InvariantCulture),
                nonce,
                hostLabel));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }

        var terminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        using HttpResponseMessage response = await transport.PostJsonAsync(
            AndroidAccountLinkBootstrapProof.PollPath,
            new
            {
                InstallationId = "android-install",
                HeadId = "android",
                ApplicationVersion = "0.1.0-preview.11",
                ChannelId = "preview",
                Platform = "android",
                Architecture = "arm64",
                PublicKey = "spki",
                IssuedAtUnixSeconds = issuedAt,
                Nonce = nonce,
                Signature = "signature",
                HostLabel = hostLabel
            },
            authority: null,
            CancellationToken.None);
        ObservedRequest observed = RequireSingle(terminal.Requests);
        Require(observed.AuthorizationScheme is null);
        Require(observed.ProofScheme is null);
        Require(observed.Body.Contains("\"architecture\":\"arm64\"", StringComparison.Ordinal));
        Require(!observed.Body.Contains("\"arch\"", StringComparison.Ordinal));
        Require(!observed.Body.Contains("accessToken", StringComparison.OrdinalIgnoreCase));
    }

    private static void ResponseGrantHeadersAreSingleBoundedAndRedacted()
    {
        using var valid = new HttpResponseMessage(HttpStatusCode.OK);
        valid.Headers.TryAddWithoutValidation("Authorization", $"Bearer {RotatedAccessToken}");
        valid.Headers.TryAddWithoutValidation(AndroidAccountLinkRequestAuthority.GrantHeader, "grant-next");
        AndroidAccountLinkResponseGrantAuthority authority =
            AndroidAccountLinkHttpTransport.ReadResponseGrantAuthority(valid);
        Require(authority.GrantId == "grant-next");
        Require(authority.AccessToken == RotatedAccessToken);
        Require(!authority.ToString().Contains(RotatedAccessToken, StringComparison.Ordinal));
        Require(!valid.Headers.Contains("Authorization"));

        foreach (HttpResponseMessage hostile in new[]
                 {
                     GrantResponse(null, "grant-next"),
                     GrantResponse($"Basic {RotatedAccessToken}", "grant-next"),
                     GrantResponse("Bearer", "grant-next"),
                     GrantResponse($"Bearer {RotatedAccessToken}"),
                     GrantResponse($"Bearer {RotatedAccessToken}", "grant-a", "grant-b"),
                     GrantResponse($"Bearer {RotatedAccessToken}", "grant-a,grant-b"),
                     GrantResponse($"Bearer {RotatedAccessToken}", " grant-next")
                 })
        {
            using (hostile)
            {
                InvalidDataException error = RequireThrows<InvalidDataException>(() =>
                    AndroidAccountLinkHttpTransport.ReadResponseGrantAuthority(hostile));
                Require(!error.ToString().Contains(RotatedAccessToken, StringComparison.Ordinal));
            }
        }
    }

    private static HttpResponseMessage GrantResponse(
        string? authorization,
        params string?[] grantIds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        if (authorization is not null)
        {
            response.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        foreach (string? grantId in grantIds)
        {
            if (grantId is not null)
            {
                response.Headers.TryAddWithoutValidation(
                    AndroidAccountLinkRequestAuthority.GrantHeader,
                    grantId);
            }
        }
        return response;
    }

    private static async Task BearerTokenCannotBeSerializedIntoRequestBody()
    {
        var terminal = new RecordingHandler(_ => JsonResponse("{\"ok\":true}"));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);

        InvalidOperationException namedProperty = await RequireThrowsAsync<InvalidOperationException>(
            () => transport.PostJsonAsync(
                "/api/v2/install-linking/grants/status",
                new LegacyCredentialRequest("android-install", AccessToken),
                CreateAuthority(),
                CancellationToken.None));
        InvalidOperationException nestedValue = await RequireThrowsAsync<InvalidOperationException>(
            () => transport.PostJsonAsync(
                "/api/v2/install-linking/grants/status",
                new NestedPayload(new($"prefix-{AccessToken}-suffix")),
                CreateAuthority(),
                CancellationToken.None));

        Require(terminal.Requests.Count == 0);
        Require(!namedProperty.ToString().Contains(AccessToken, StringComparison.Ordinal));
        Require(!nestedValue.ToString().Contains(AccessToken, StringComparison.Ordinal));
    }

    private static void BearerAuthorityRejectsHeaderInjectionWithoutEchoingCredential()
    {
        string injectedToken = $"{AccessToken}\r\nX-Stolen: yes";
        ArgumentException error = RequireThrows<ArgumentException>(() =>
            _ = new AndroidAccountLinkRequestAuthority(
                "android-install",
                "grant-v2",
                injectedToken,
                1_788_543_210,
                (_, _) => Task.FromResult(new byte[] { 1 })));

        Require(!error.ToString().Contains(injectedToken, StringComparison.Ordinal));
        Require(!CreateAuthority().ToString().Contains(AccessToken, StringComparison.Ordinal));
    }

    private static void ServiceRequestDtosCannotCarryAccessToken()
    {
        Type[] requestTypes = typeof(AndroidAccountLinkService)
            .GetNestedTypes(BindingFlags.NonPublic)
            .Where(static type => type.Name.Contains("Request", StringComparison.Ordinal))
            .ToArray();

        Require(requestTypes.Length >= 6);
        Require(requestTypes.All(static type => type.GetProperty(
            "AccessToken",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null));
    }

    private static async Task ResponseHeadersReadAvoidsImplicitBuffering()
    {
        var content = new HeadersOnlyContent("{\"items\":[\"ready\"]}");
        var terminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);

        using HttpResponseMessage response = await transport.PostJsonAsync(
            "/api/v2/android/linked/groups",
            new InstallationRequest("android-install"),
            CreateAuthority(),
            CancellationToken.None);
        CollectionEnvelope envelope = await transport.ReadJsonAsync<CollectionEnvelope>(
            response,
            CancellationToken.None);

        Require(envelope.Items.SequenceEqual(["ready"]));
        Require(content.SerializeCalls == 0);
    }

    private static async Task OversizedChunkedResponseIsRejected()
    {
        var terminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new OversizedJsonReadStream(
                AndroidAccountLinkHttpTransport.MaxResponseBodyBytes))
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);

        using HttpResponseMessage response = await transport.PostJsonAsync(
            "/api/v2/android/linked/groups",
            new InstallationRequest("android-install"),
            CreateAuthority(),
            CancellationToken.None);
        Require(response.Content.Headers.ContentLength is null);
        InvalidDataException error = await RequireThrowsAsync<InvalidDataException>(
            () => transport.ReadJsonAsync<CollectionEnvelope>(
                response,
                CancellationToken.None));

        Require(error.Message.Contains("too large", StringComparison.Ordinal));
        Require(!error.ToString().Contains(AccessToken, StringComparison.Ordinal));
    }

    private static async Task MissingAndOversizedContentLengthAreHandledSafely()
    {
        byte[] boundedPayload = Encoding.UTF8.GetBytes("{\"items\":[\"bounded\"]}");
        var absentLengthTerminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableReadStream(boundedPayload))
        });
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(absentLengthTerminal))
        {
            using HttpResponseMessage response = await transport.PostJsonAsync(
                "/api/v2/android/linked/groups",
                new InstallationRequest("android-install"),
                CreateAuthority(),
                CancellationToken.None);
            Require(response.Content.Headers.ContentLength is null);
            CollectionEnvelope envelope = await transport.ReadJsonAsync<CollectionEnvelope>(
                response,
                CancellationToken.None);
            Require(envelope.Items.SequenceEqual(["bounded"]));
        }

        var declaredLargeContent = new ByteArrayContent([]);
        declaredLargeContent.Headers.ContentLength =
            AndroidAccountLinkHttpTransport.MaxResponseBodyBytes + 1;
        var largeLengthTerminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = declaredLargeContent
        });
        using AndroidAccountLinkHttpTransport largeTransport = CreateTransport(largeLengthTerminal);
        using HttpResponseMessage largeResponse = await largeTransport.PostJsonAsync(
            "/api/v2/android/linked/groups",
            new InstallationRequest("android-install"),
            CreateAuthority(),
            CancellationToken.None);
        InvalidDataException error = await RequireThrowsAsync<InvalidDataException>(
            () => largeTransport.ReadJsonAsync<CollectionEnvelope>(
                largeResponse,
                CancellationToken.None));
        Require(error.Message.Contains("too large", StringComparison.Ordinal));
    }

    private static async Task ResponseBodyReadRetainsABoundedTimeout()
    {
        var terminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NeverCompletingReadStream())
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(
            terminal,
            TimeSpan.FromMilliseconds(100));

        using HttpResponseMessage response = await transport.PostJsonAsync(
            "/api/v2/android/linked/groups",
            new InstallationRequest("android-install"),
            CreateAuthority(),
            CancellationToken.None);
        Stopwatch elapsed = Stopwatch.StartNew();
        OperationCanceledException error = await RequireThrowsAsync<OperationCanceledException>(
            () => transport.ReadJsonAsync<CollectionEnvelope>(response, CancellationToken.None));
        elapsed.Stop();

        Require(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        Require(!error.ToString().Contains(AccessToken, StringComparison.Ordinal));
    }

    private static async Task CrossOriginRedirectCannotReceiveBearerToken()
    {
        var terminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
        {
            Headers =
            {
                Location = new Uri("https://credential-thief.invalid/collect")
            }
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);

        HttpRequestException redirectError = await RequireThrowsAsync<HttpRequestException>(
            () => transport.PostJsonAsync(
                "/api/v2/install-linking/grants/status",
                new InstallationRequest("android-install"),
                CreateAuthority(),
                CancellationToken.None));
        Require(redirectError.Message.Contains("cross-origin", StringComparison.Ordinal));
        Require(!redirectError.ToString().Contains(AccessToken, StringComparison.Ordinal));
        Require(terminal.Requests.Count == 1);
        Require(terminal.Requests[0].Uri.Host == AndroidAccountLinkHttpTransport.TrustedOrigin.Host);

        await RequireThrowsAsync<InvalidOperationException>(
            () => transport.PostJsonAsync(
                "https://credential-thief.invalid/collect",
                new InstallationRequest("android-install"),
                CreateAuthority(),
                CancellationToken.None));
        Require(terminal.Requests.Count == 1);
    }

    private static async Task SameOriginRedirectAndUnsafePathsFailClosed()
    {
        var terminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.PermanentRedirect)
        {
            Headers =
            {
                Location = new Uri("/api/v2/install-linking/grants/status-redirect", UriKind.Relative)
            }
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);

        HttpRequestException sameOrigin = await RequireThrowsAsync<HttpRequestException>(
            () => transport.PostJsonAsync(
                "/api/v2/install-linking/grants/status",
                new InstallationRequest("android-install"),
                CreateAuthority(),
                CancellationToken.None));
        Require(!sameOrigin.Message.Contains("cross-origin", StringComparison.Ordinal));
        Require(terminal.Requests.Count == 1);

        foreach (string unsafePath in new[]
                 {
                     "api/v2/install-linking/grants/status",
                     "/api/v1/install-linking/grants/status",
                     "//credential-thief.invalid/collect",
                     "/api/v2/install-linking/grants/status?token=forbidden",
                     "/api/v2/install-linking/grants/status#fragment",
                     "/api/v2\\credential-thief.invalid",
                     "/api/v2/android/linked/../not-linked"
                 })
        {
            InvalidOperationException error = await RequireThrowsAsync<InvalidOperationException>(
                () => transport.PostJsonAsync(
                    unsafePath,
                    new InstallationRequest("android-install"),
                    CreateAuthority(),
                    CancellationToken.None));
            Require(!error.ToString().Contains(AccessToken, StringComparison.Ordinal));
        }
        Require(terminal.Requests.Count == 1);
    }

    private static async Task MalformedCollectionsFailClosedWithoutEchoingResponseData()
    {
        foreach (string json in new[]
                 {
                     "{\"items\":null}",
                     "{}",
                     "{\"items\":{\"secret\":\"hostile-secret-token-that-must-never-be-logged\"}}"
                 })
        {
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using AndroidAccountLinkHttpTransport transport = CreateTransport(
                new RecordingHandler(_ => JsonResponse("{}")));
            InvalidDataException error = await RequireThrowsAsync<InvalidDataException>(
                () => transport.ReadJsonAsync<CollectionEnvelope>(
                    response,
                    CancellationToken.None));
            Require(error.Message == "Chummer returned an invalid account response.");
            Require(error.InnerException is null);
            Require(!error.ToString().Contains(AccessToken, StringComparison.Ordinal));
        }
    }

    private static async Task LostBootstrapResponseCanReplayTheOriginalGrantAsync()
    {
        LinkFixture fixture = await CreatePendingFixtureAsync();
        int call = 0;
        var terminal = new RecordingHandler(_ => ++call == 1
            ? throw new HttpRequestException("Injected response loss after redemption.")
            : BootstrapGrantResponse(fixture.Identity, alreadyClaimed: true));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        AndroidAccountLinkService service = CreateService(transport, fixture);

        await service.ResumePendingLinkAsync();
        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
        Require(fixture.Metadata.Contains(PendingStateKey));
        Require(fixture.Keys.Contains(fixture.Identity.Alias));

        await service.ResumePendingLinkAsync();

        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == RotatedAccessToken);
        Require(!fixture.Metadata.Contains(PendingStateKey));
        AndroidAccountLinkKeyIdentity linked = await fixture.Authority.RequireLinkedIdentityAsync(
            fixture.Identity.InstallationId);
        Require(linked.GrantId == "grant-after-response-loss");
        Require(terminal.Requests.Count == 2);
    }

    private static async Task AlreadyRedeemedBootstrapConflictRetainsFreshCredentialsAsync()
    {
        LinkFixture fixture = await CreatePendingFixtureAsync();
        int call = 0;
        var terminal = new RecordingHandler(_ => ++call switch
        {
            1 => throw new HttpRequestException("Injected response loss after redemption."),
            2 => JsonResponse("{\"alreadyRedeemed\":true}", HttpStatusCode.Conflict),
            _ => BootstrapGrantResponse(fixture.Identity, alreadyClaimed: true)
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        AndroidAccountLinkService service = CreateService(transport, fixture);

        await service.ResumePendingLinkAsync();
        await service.ResumePendingLinkAsync();

        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
        Require(fixture.Metadata.Contains(PendingStateKey));
        Require(fixture.Metadata.Contains(PendingInstallationIdKey));
        Require(fixture.Metadata.Contains(AndroidAccountLinkKeyAuthority.BindingStorageKey));
        Require(fixture.Metadata.Contains(AndroidAccountLinkKeyAuthority.InstallationIdStorageKey));
        Require(fixture.Keys.Contains(fixture.Identity.Alias));
        AndroidAccountLinkKeyIdentity pending = await fixture.Authority.RequirePendingIdentityAsync(
            fixture.Identity.InstallationId);
        Require(pending.GrantId is null);

        await service.ResumePendingLinkAsync();
        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        Require(terminal.Requests.Count == 3);
    }

    private static async Task SuccessfulUnlinkCannotLeaveAStaleLinkedSnapshotAsync()
    {
        LinkFixture fixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(30));
        var terminal = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/install-linking/grants/status" => JsonResponse("{}"),
            "/api/v2/install-linking/grants/revoke" => JsonResponse("{}"),
            _ => throw new InvalidOperationException("Unexpected account-link route.")
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        AndroidAccountLinkService service = CreateService(transport, fixture);
        await service.InitializeAsync();
        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        fixture.Keys.FailDeleteAlias = fixture.Identity.Alias;

        await service.UnlinkAsync();

        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Unlinked);
        Require(!service.Snapshot.IsLinked);
        Require(!fixture.Metadata.Contains(StoredAccessTokenKey));
        Require(fixture.Metadata.Contains(AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey));
        Require(fixture.Keys.Contains(fixture.Identity.Alias));
        fixture.Keys.FailDeleteAlias = null;
        await fixture.Authority.RemoveAsync();
    }

    private static async Task SuccessfulErasureCannotLeaveAStaleLinkedSnapshotAsync()
    {
        LinkFixture fixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(30));
        var terminal = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/install-linking/grants/status" => JsonResponse("{}"),
            "/api/v2/android/linked/account/erase" => AccountErasureResponse(),
            _ => throw new InvalidOperationException("Unexpected account-link route.")
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        AndroidAccountLinkService service = CreateService(transport, fixture);
        await service.InitializeAsync();
        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        fixture.Keys.FailDeleteAlias = fixture.Identity.Alias;

        AndroidAccountErasureReceipt receipt = await service.EraseAccountAsync(
            AndroidAccountErasureConfirmation.RequiredPhrase);

        Require(receipt.Erased);
        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Unlinked);
        Require(!service.Snapshot.IsLinked);
        Require(!fixture.Metadata.Contains(StoredAccessTokenKey));
        Require(fixture.Metadata.Contains(AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey));
        Require(fixture.Keys.Contains(fixture.Identity.Alias));
        fixture.Keys.FailDeleteAlias = null;
        await fixture.Authority.RemoveAsync();
    }

    private static async Task LostRefreshResponseReusesItsDurableIdempotencyKeyAsync()
    {
        LinkFixture fixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
        int firstPhaseCall = 0;
        var lostResponseTerminal = new RecordingHandler(_ => ++firstPhaseCall switch
        {
            1 => JsonResponse("{}"),
            _ => throw new HttpRequestException("Injected response loss after grant rotation.")
        });
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(lostResponseTerminal))
        {
            AndroidAccountLinkService service = CreateService(transport, fixture);
            await service.InitializeAsync();
            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
        }

        Require(lostResponseTerminal.Requests.Count == 2);
        string originalRefreshBody = lostResponseTerminal.Requests[1].Body;
        Require(lostResponseTerminal.Requests[1].AuthorizationParameter == AccessToken);
        Require(lostResponseTerminal.Requests[1].GrantId == "grant-before-refresh");
        using (JsonDocument body = JsonDocument.Parse(originalRefreshBody))
        {
            string? requestKey = body.RootElement.GetProperty("idempotencyKey").GetString();
            Require(requestKey is { Length: 32 });
            Require(requestKey == fixture.Metadata.GetRaw(RefreshAttemptKey));
        }
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == AccessToken);
        Require((await fixture.Authority.RequireLinkedIdentityAsync(fixture.Identity.InstallationId)).GrantId
            == "grant-before-refresh");

        int recoveryCall = 0;
        var recoveryTerminal = new RecordingHandler(_ => ++recoveryCall == 1
            ? JsonResponse("{\"alreadyRedeemed\":true}", HttpStatusCode.Conflict)
            : RefreshGrantResponse(fixture.Identity));
        using AndroidAccountLinkHttpTransport recoveryTransport = CreateTransport(recoveryTerminal);
        AndroidAccountLinkService recoveryService = CreateService(recoveryTransport, fixture);

        await recoveryService.InitializeAsync();
        Require(recoveryService.Snapshot.Status == AndroidAccountLinkStatus.Error);
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == AccessToken);
        Require(fixture.Metadata.Contains(RefreshAttemptKey));
        Require(recoveryTerminal.Requests[0].Body == originalRefreshBody);
        Require(recoveryTerminal.Requests[0].AuthorizationParameter == AccessToken);
        Require(recoveryTerminal.Requests[0].GrantId == "grant-before-refresh");

        await recoveryService.InitializeAsync();
        Require(recoveryService.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == RotatedAccessToken);
        Require(!fixture.Metadata.Contains(RefreshAttemptKey));
        Require(recoveryTerminal.Requests[1].Body == originalRefreshBody);
        Require((await fixture.Authority.RequireLinkedIdentityAsync(fixture.Identity.InstallationId)).GrantId
            == "grant-after-refresh");
    }

    private static AndroidAccountLinkService CreateService(
        AndroidAccountLinkHttpTransport transport,
        LinkFixture fixture)
        => new(
            transport,
            new StubSystemService(),
            fixture.Authority,
            fixture.Metadata,
            versionProvider: static () => "0.1.0-preview.11",
            hostLabelProvider: static () => "Hostile test phone");

    private static async Task<LinkFixture> CreatePendingFixtureAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        await metadata.SetAsync(PendingStateKey, "pending-state");
        await metadata.SetAsync(PendingInstallationIdKey, identity.InstallationId);
        await metadata.SetAsync(
            PendingStartedKey,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return new(metadata, keys, authority, identity);
    }

    private static async Task<LinkFixture> CreateLinkedFixtureAsync(DateTimeOffset expiresAtUtc)
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        await authority.BindGrantAsync(identity.InstallationId, "grant-before-refresh");
        await metadata.SetAsync(StoredAccessTokenKey, AccessToken);
        await metadata.SetAsync(
            StoredGrantExpiryKey,
            expiresAtUtc.ToString("O", CultureInfo.InvariantCulture));
        return new(metadata, keys, authority, identity);
    }

    private static HttpResponseMessage BootstrapGrantResponse(
        AndroidAccountLinkKeyIdentity identity,
        bool alreadyClaimed)
        => GrantResponse(
            "grant-after-response-loss",
            RotatedAccessToken,
            JsonSerializer.Serialize(new
            {
                grant = GrantMetadata("grant-after-response-loss", identity.InstallationId),
                alreadyClaimed
            }));

    private static HttpResponseMessage RefreshGrantResponse(AndroidAccountLinkKeyIdentity identity)
        => GrantResponse(
            "grant-after-refresh",
            RotatedAccessToken,
            JsonSerializer.Serialize(new
            {
                grant = GrantMetadata("grant-after-refresh", identity.InstallationId),
                rotated = true
            }));

    private static object GrantMetadata(string grantId, string installationId)
        => new
        {
            grantId,
            installationId,
            status = "active",
            issuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
        };

    private static HttpResponseMessage GrantResponse(
        string grantId,
        string accessToken,
        string json)
    {
        HttpResponseMessage response = JsonResponse(json);
        response.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        response.Headers.TryAddWithoutValidation(
            AndroidAccountLinkRequestAuthority.GrantHeader,
            grantId);
        return response;
    }

    private static HttpResponseMessage AccountErasureResponse()
    {
        string digest = new('a', 64);
        return JsonResponse(JsonSerializer.Serialize(new
        {
            erased = true,
            subjectKeySha256 = digest,
            userKeySha256 = digest,
            components = new[]
            {
                "hosted_build_workspaces",
                "support",
                "first_party_auxiliary_stores",
                "community",
                "identity"
            }.Select(component => new
            {
                component,
                completed = true,
                recordsRemoved = 1,
                receiptSha256 = digest
            }),
            erasedAtUtc = DateTimeOffset.UtcNow,
            receiptSha256 = digest
        }));
    }

    private static AndroidAccountLinkHttpTransport CreateTransport(
        HttpMessageHandler terminal,
        TimeSpan? timeout = null)
        => new(terminal, timeout);

    private static AndroidAccountLinkRequestAuthority CreateAuthority()
        => new(
            "android-install",
            "grant-v2",
            AccessToken,
            1_788_543_210,
            (canonical, _) => Task.FromResult(SHA256.HashData(canonical.Span)));

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static ObservedRequest RequireSingle(IReadOnlyList<ObservedRequest> requests)
    {
        Require(requests.Count == 1);
        return requests[0];
    }

    private static async Task<TException> RequireThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static TException RequireThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Account-link HTTP hardening assertion failed.");
        }
    }

    private sealed record InstallationRequest(string InstallationId);

    private sealed record GroupRequest(
        string InstallationId,
        string Name,
        string Visibility);

    private sealed record LegacyCredentialRequest(
        string InstallationId,
        string AccessToken);

    private sealed record NestedPayload(NestedValue Metadata);

    private sealed record NestedValue(string Note);

    private sealed record CollectionEnvelope(IReadOnlyList<string> Items);

    private sealed record LinkFixture(
        MemoryMetadataStore Metadata,
        MemoryDeviceKeyStore Keys,
        AndroidAccountLinkKeyAuthority Authority,
        AndroidAccountLinkKeyIdentity Identity);

    private sealed record ObservedRequest(
        Uri Uri,
        string Body,
        byte[] BodyBytes,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Diagnostic,
        string? ContentType,
        string? ProofScheme,
        string? InstallationId,
        string? GrantId,
        string? PacketKey,
        string? IssuedAt,
        string? Signature);

    private sealed class MemoryMetadataStore : IAndroidAccountLinkKeyMetadataStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.GetValueOrDefault(key));
        }

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        internal bool Contains(string key) => _values.ContainsKey(key);

        internal string? GetRaw(string key) => _values.GetValueOrDefault(key);
    }

    private sealed class MemoryDeviceKeyStore : IAndroidDeviceKeyStore, IDisposable
    {
        private readonly Dictionary<string, RSA> _keys = new(StringComparer.Ordinal);

        internal string? FailDeleteAlias { get; set; }

        public Task<AndroidDevicePublicKey> CreateAsync(
            string alias,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RSA key = RSA.Create(2048);
            _keys.Add(alias, key);
            return Task.FromResult(new AndroidDevicePublicKey(
                AndroidDeviceKeyAvailability.Available,
                Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
        }

        public Task<AndroidDevicePublicKey> GetPublicKeyAsync(
            string alias,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_keys.TryGetValue(alias, out RSA? key)
                ? new AndroidDevicePublicKey(
                    AndroidDeviceKeyAvailability.Available,
                    Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))
                : new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Missing));
        }

        public Task<byte[]> SignAsync(
            string alias,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_keys.TryGetValue(alias, out RSA? key))
            {
                throw new AndroidDeviceRelinkRequiredException(
                    AndroidDeviceKeyAvailability.Missing,
                    "Test key missing.");
            }
            return Task.FromResult(key.SignData(
                payload.Span,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));
        }

        public Task DeleteAsync(string alias, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(FailDeleteAlias, alias, StringComparison.Ordinal))
            {
                throw new CryptographicException("Injected Android Keystore deletion failure.");
            }
            if (_keys.Remove(alias, out RSA? key))
            {
                key.Dispose();
            }
            return Task.CompletedTask;
        }

        internal bool Contains(string alias) => _keys.ContainsKey(alias);

        public void Dispose()
        {
            foreach (RSA key in _keys.Values)
            {
                key.Dispose();
            }
            _keys.Clear();
        }
    }

    private sealed class StubSystemService : IAndroidSystemService
    {
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(true);

        public Task<AndroidUpdateCheckResult> CheckForUpdatesAsync()
            => Task.FromResult(AndroidUpdateCheckResult.Unavailable);

        public Task ShareTextAsync(string text) => Task.CompletedTask;

        public Task<bool> PrintPdfAsync(
            string fileName,
            string contentBase64,
            string title,
            CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        internal RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        internal List<ObservedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthenticationHeaderValue? authorization = request.Headers.Authorization;
            byte[] bodyBytes = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            string body = Encoding.UTF8.GetString(bodyBytes);
            Requests.Add(new ObservedRequest(
                request.RequestUri ?? throw new InvalidOperationException("Request URI is required."),
                body,
                bodyBytes,
                authorization?.Scheme,
                authorization?.Parameter,
                request.ToString(),
                request.Content?.Headers.ContentType?.ToString(),
                Header(request, AndroidAccountLinkRequestAuthority.SchemeHeader),
                Header(request, AndroidAccountLinkRequestAuthority.InstallationHeader),
                Header(request, AndroidAccountLinkRequestAuthority.GrantHeader),
                Header(request, AndroidAccountLinkRequestAuthority.PacketKeyHeader),
                Header(request, AndroidAccountLinkRequestAuthority.IssuedHeader),
                Header(request, AndroidAccountLinkRequestAuthority.SignatureHeader)));
            HttpResponseMessage response = _response(request);
            response.RequestMessage = request;
            return response;
        }

        private static string? Header(HttpRequestMessage request, string name)
            => request.Headers.TryGetValues(name, out IEnumerable<string>? values)
                ? values.SingleOrDefault()
                : null;
    }

    private sealed class HeadersOnlyContent : HttpContent
    {
        private readonly byte[] _payload;

        internal HeadersOnlyContent(string json)
        {
            _payload = Encoding.UTF8.GetBytes(json);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        internal int SerializeCalls { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            SerializeCalls++;
            throw new InvalidOperationException("Response body was buffered before headers returned.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new NonSeekableReadStream(_payload));
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly byte[] _payload;
        private int _position;

        internal NonSeekableReadStream(byte[] payload)
        {
            _payload = payload;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Min(count, _payload.Length - _position);
            if (available <= 0)
            {
                return 0;
            }
            _payload.AsSpan(_position, available).CopyTo(buffer.AsSpan(offset, available));
            _position += available;
            return available;
        }

        public override int Read(Span<byte> buffer)
        {
            int available = Math.Min(buffer.Length, _payload.Length - _position);
            if (available <= 0)
            {
                return 0;
            }
            _payload.AsSpan(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class OversizedJsonReadStream : Stream
    {
        private static readonly byte[] Prefix = Encoding.UTF8.GetBytes("{\"items\":[\"");
        private static readonly byte[] Suffix = Encoding.UTF8.GetBytes("\"]}");
        private readonly long _repeatedBytes;
        private long _position;

        internal OversizedJsonReadStream(long repeatedBytes)
        {
            _repeatedBytes = repeatedBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int written = 0;
            while (!buffer.IsEmpty && _position < Prefix.Length + _repeatedBytes + Suffix.Length)
            {
                if (_position < Prefix.Length)
                {
                    int length = (int)Math.Min(buffer.Length, Prefix.Length - _position);
                    Prefix.AsSpan((int)_position, length).CopyTo(buffer);
                    _position += length;
                    written += length;
                    buffer = buffer[length..];
                    continue;
                }

                long repeatedEnd = Prefix.Length + _repeatedBytes;
                if (_position < repeatedEnd)
                {
                    int length = (int)Math.Min(buffer.Length, repeatedEnd - _position);
                    buffer[..length].Fill((byte)'x');
                    _position += length;
                    written += length;
                    buffer = buffer[length..];
                    continue;
                }

                int suffixOffset = (int)(_position - repeatedEnd);
                int suffixLength = Math.Min(buffer.Length, Suffix.Length - suffixOffset);
                Suffix.AsSpan(suffixOffset, suffixLength).CopyTo(buffer);
                _position += suffixLength;
                written += suffixLength;
                buffer = buffer[suffixLength..];
            }

            return written;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NeverCompletingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
