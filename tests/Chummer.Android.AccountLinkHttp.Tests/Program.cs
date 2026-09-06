using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Android.Platform;

internal static class Program
{
    private const string AccessToken = "hostile-secret-token-that-must-never-be-logged";
    private const string RotatedAccessToken = "rotated-secret-token-that-must-never-be-logged";
    private const string StoredAccessTokenKey = "chummer.account.installation-grant.v1";
    private const string StoredGrantExpiryKey = "chummer.account.installation-grant-expiry.v1";
    private const string RefreshAttemptKey = "chummer.account.installation-grant-refresh-attempt.v1";
    private const string StagedGrantCommitKey = "chummer.account.staged-grant-commit.v1";
    private const string PendingPollOperationKey = "chummer.account.pending-poll-operation.v1";
    private const string PendingStateKey = "chummer.account.pending-state.v1";
    private const string PendingStartedKey = "chummer.account.pending-started.v1";
    private const string PendingInstallationIdKey = "chummer.account.pending-installation-id.v2";
    private const string InstallationIdKey = AndroidAccountLinkKeyAuthority.InstallationIdStorageKey;

    private static async Task Main()
    {
        await BearerTokenIsRequestBoundAndRedacted();
        await PacketProofBindsExactBodyAndCaseSensitivePath();
        await BootstrapProofAndPollBodyMatchV2Contract();
        ResponseGrantHeadersAreSingleBoundedAndRedacted();
        await ResponseGrantAuthorityIsRedactedAndOneShot();
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
        await BrowserLaunchAndFirstPollShareOperationAcrossRestartAsync();
        await ReopeningPendingLinkReusesBrowserOperationAsync();
        await BrowserOpenFailureRetainsRecoverableOperationAsync();
        await CallerCancellationCannotInterruptCredentialCommitAsync();
        await StagedCredentialCommitRecoversAfterEveryWriteAsync();
        await CredentialCommitCleanupFailureRemainsRestartableAsync();
        await StaleLinkedRejectionCannotClearConcurrentRefreshAsync();
        await RefreshOperationPublicationClosesPreVisibilityRaceAsync();
        await InvalidStagedCredentialIsQuarantinedWithoutDeletingNewerGrantAsync();
        await InvalidStagedInstallationCannotReachKeyAuthorityAsync();
        await LostBootstrapResponseSurvivesProcessRestartAsync();
        await AlreadyRedeemedBootstrapConflictRetainsFreshCredentialsAsync();
        await SuccessfulUnlinkCannotLeaveAStaleLinkedSnapshotAsync();
        await SuccessfulErasureCannotLeaveAStaleLinkedSnapshotAsync();
        await LostRefreshResponseSurvivesProcessRestartAsync();
        await MismatchedOperationResponsesRetainRecoveryStateAsync();
        Console.WriteLine("Account-link HTTP hardening tests passed: 31");
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
        const string operationId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string nonce = "bootstrap-nonce";
        const string hostLabel = "Runner Phone";
        byte[] canonical = AndroidAccountLinkBootstrapProof.CreateCanonicalPayload(
            "proof_poll_v2",
            operationId,
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
                "proof_poll_v2",
                operationId,
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
                InstallLinkTransport = "proof_poll_v2",
                OperationId = operationId,
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
        Require(observed.Body.Contains("\"installLinkTransport\":\"proof_poll_v2\"", StringComparison.Ordinal));
        Require(observed.Body.Contains($"\"operationId\":\"{operationId}\"", StringComparison.Ordinal));
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

    private static async Task ResponseGrantAuthorityIsRedactedAndOneShot()
    {
        var terminal = new RecordingHandler(_ => GrantResponse(
            "grant-next",
            RotatedAccessToken,
            "{\"ok\":true}"));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);

        using HttpResponseMessage response = await transport.PostJsonAsync(
            "/api/v2/install-linking/grants/refresh",
            new InstallationRequest("android-install"),
            CreateAuthority(),
            CancellationToken.None);

        Require(!response.Headers.Contains("Authorization"));
        Require(!response.ToString().Contains(RotatedAccessToken, StringComparison.Ordinal));
        Require(response.RequestMessage is null
            || !response.RequestMessage.ToString().Contains(
                RotatedAccessToken,
                StringComparison.Ordinal));

        AndroidAccountLinkResponseGrantAuthority authority =
            AndroidAccountLinkHttpTransport.ReadResponseGrantAuthority(response);
        Require(authority.GrantId == "grant-next");
        Require(authority.AccessToken == RotatedAccessToken);

        InvalidDataException replay = RequireThrows<InvalidDataException>(() =>
            AndroidAccountLinkHttpTransport.ReadResponseGrantAuthority(response));
        Require(!replay.ToString().Contains(RotatedAccessToken, StringComparison.Ordinal));
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

    private static async Task BrowserLaunchAndFirstPollShareOperationAcrossRestartAsync()
    {
        LinkFixture fixture = await CreateUnlinkedFixtureAsync();
        var browser = new StubSystemService(_ =>
        {
            Require(fixture.Metadata.Contains(PendingPollOperationKey));
            return true;
        });
        var unusedTerminal = new RecordingHandler(_ => JsonResponse("{}"));
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(unusedTerminal))
        {
            AndroidAccountLinkService service = CreateService(
                transport,
                fixture,
                version: "0.1.0-preview.11",
                hostLabel: "Launch phone",
                architecture: "arm64",
                systemService: browser);
            await service.BeginLinkAsync();
            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
        }

        Require(browser.OpenedUris.Count == 1);
        Require(unusedTerminal.Requests.Count == 0);
        Uri browserUri = browser.OpenedUris[0];
        IReadOnlyDictionary<string, string> browserQuery = QueryValues(browserUri);
        string storedOperation = fixture.Metadata.GetRaw(PendingPollOperationKey)!;
        using JsonDocument operation = JsonDocument.Parse(storedOperation);
        string operationId = operation.RootElement.GetProperty("operationId").GetString()!;
        Require(operation.RootElement.GetProperty("installLinkTransport").GetString()
            == "proof_poll_v2");
        Require(browserQuery["installationId"] == fixture.Identity.InstallationId);
        Require(browserQuery["applicationVersion"] == "0.1.0-preview.11");
        Require(browserQuery["arch"] == "arm64");
        Require(browserQuery["installLinkTransport"] == "proof_poll_v2");
        Require(browserQuery["installLinkTransport"] != "proof_poll");
        Require(browserQuery["publicKey"] == fixture.Identity.PublicKey);
        Require(operation.RootElement.GetProperty("applicationVersion").GetString()
            == browserQuery["applicationVersion"]);
        Require(operation.RootElement.GetProperty("architecture").GetString()
            == browserQuery["arch"]);

        var recoveryTerminal = new RecordingHandler(request => BootstrapGrantResponse(
            fixture.Identity,
            RequestOperationId(request),
            alreadyClaimed: false));
        using AndroidAccountLinkHttpTransport recoveryTransport = CreateTransport(recoveryTerminal);
        AndroidAccountLinkService recoveryService = CreateService(
            recoveryTransport,
            fixture,
            version: "0.1.0-preview.12",
            hostLabel: "Restarted upgraded phone",
            architecture: "x64");
        await recoveryService.ResumePendingLinkAsync();

        Require(recoveryService.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        ObservedRequest poll = RequireSingle(recoveryTerminal.Requests);
        using JsonDocument pollBody = JsonDocument.Parse(poll.Body);
        Require(pollBody.RootElement.GetProperty("operationId").GetString() == operationId);
        Require(pollBody.RootElement.GetProperty("installLinkTransport").GetString()
            == browserQuery["installLinkTransport"]);
        Require(pollBody.RootElement.GetProperty("applicationVersion").GetString()
            == browserQuery["applicationVersion"]);
        Require(pollBody.RootElement.GetProperty("architecture").GetString()
            == browserQuery["arch"]);
        Require(pollBody.RootElement.GetProperty("publicKey").GetString()
            == browserQuery["publicKey"]);
        Require(pollBody.RootElement.GetProperty("hostLabel").GetString() == "Launch phone");
    }

    private static async Task ReopeningPendingLinkReusesBrowserOperationAsync()
    {
        LinkFixture fixture = await CreateUnlinkedFixtureAsync();
        var browser = new StubSystemService();
        var unusedTerminal = new RecordingHandler(_ => JsonResponse("{}"));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(unusedTerminal);
        AndroidAccountLinkService firstService = CreateService(
            transport,
            fixture,
            version: "0.1.0-preview.11",
            hostLabel: "Original phone",
            architecture: "arm64",
            systemService: browser);
        await firstService.BeginLinkAsync();
        string originalOperation = fixture.Metadata.GetRaw(PendingPollOperationKey)!;
        string originalState = fixture.Metadata.GetRaw(PendingStateKey)!;

        AndroidAccountLinkService reopenedService = CreateService(
            transport,
            fixture,
            version: "0.1.0-preview.13",
            hostLabel: "Renamed phone",
            architecture: "x86",
            systemService: browser);
        await reopenedService.BeginLinkAsync();

        Require(reopenedService.Snapshot.Status == AndroidAccountLinkStatus.Pending);
        Require(browser.OpenedUris.Count == 2);
        Require(browser.OpenedUris[1] == browser.OpenedUris[0]);
        Require(fixture.Metadata.GetRaw(PendingPollOperationKey) == originalOperation);
        Require(fixture.Metadata.GetRaw(PendingStateKey) == originalState);
        Require(fixture.Keys.Contains(fixture.Identity.Alias));
    }

    private static async Task BrowserOpenFailureRetainsRecoverableOperationAsync()
    {
        LinkFixture fixture = await CreateUnlinkedFixtureAsync();
        var failingBrowser = new StubSystemService(_ =>
        {
            Require(fixture.Metadata.Contains(PendingStateKey));
            Require(fixture.Metadata.Contains(PendingInstallationIdKey));
            Require(fixture.Metadata.Contains(PendingStartedKey));
            Require(fixture.Metadata.Contains(PendingPollOperationKey));
            return false;
        });
        var unusedTerminal = new RecordingHandler(_ => JsonResponse("{}"));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(unusedTerminal);
        AndroidAccountLinkService service = CreateService(
            transport,
            fixture,
            version: "0.1.0-preview.11",
            hostLabel: "Launch phone",
            architecture: "arm64",
            systemService: failingBrowser);
        await service.BeginLinkAsync();

        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
        Require(fixture.Metadata.Contains(PendingStateKey));
        Require(fixture.Metadata.Contains(PendingInstallationIdKey));
        Require(fixture.Metadata.Contains(PendingStartedKey));
        Require(fixture.Metadata.Contains(PendingPollOperationKey));
        Require(fixture.Keys.Contains(fixture.Identity.Alias));
        Require(failingBrowser.OpenedUris.Count == 1);
        string originalOperation = fixture.Metadata.GetRaw(PendingPollOperationKey)!;

        var recoveryBrowser = new StubSystemService();
        AndroidAccountLinkService recoveryService = CreateService(
            transport,
            fixture,
            version: "0.1.0-preview.12",
            hostLabel: "Restarted phone",
            architecture: "x64",
            systemService: recoveryBrowser);
        await recoveryService.BeginLinkAsync();

        Require(recoveryService.Snapshot.Status == AndroidAccountLinkStatus.Pending);
        Require(recoveryBrowser.OpenedUris.Count == 1);
        Require(recoveryBrowser.OpenedUris[0] == failingBrowser.OpenedUris[0]);
        Require(fixture.Metadata.GetRaw(PendingPollOperationKey) == originalOperation);
        Require(fixture.Keys.Contains(fixture.Identity.Alias));
    }

    private static async Task CallerCancellationCannotInterruptCredentialCommitAsync()
    {
        string[] commitWrites =
        [
            StagedGrantCommitKey,
            StoredAccessTokenKey,
            StoredGrantExpiryKey,
            AndroidAccountLinkKeyAuthority.BindingStorageKey
        ];
        foreach (string cancelAfterWrite in commitWrites)
        {
            LinkFixture bootstrapFixture = await CreatePendingFixtureAsync();
            using var bootstrapCancellation = new CancellationTokenSource();
            bootstrapFixture.Metadata.AfterSet = key =>
            {
                if (key == cancelAfterWrite)
                {
                    bootstrapCancellation.Cancel();
                }
            };
            var bootstrapTerminal = new RecordingHandler(request => BootstrapGrantResponse(
                bootstrapFixture.Identity,
                RequestOperationId(request),
                alreadyClaimed: false));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(bootstrapTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, bootstrapFixture);
                await service.ResumePendingLinkAsync(
                    cancellationToken: bootstrapCancellation.Token);
                Require(bootstrapCancellation.IsCancellationRequested);
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
            }
            await RequireCommittedGrantAsync(bootstrapFixture, "grant-after-response-loss");
            Require(!bootstrapFixture.Metadata.Contains(PendingPollOperationKey));
            Require(!bootstrapFixture.Metadata.Contains(StagedGrantCommitKey));

            LinkFixture refreshFixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
            using var refreshCancellation = new CancellationTokenSource();
            refreshFixture.Metadata.AfterSet = key =>
            {
                if (key == cancelAfterWrite)
                {
                    refreshCancellation.Cancel();
                }
            };
            int refreshCall = 0;
            var refreshTerminal = new RecordingHandler(request => ++refreshCall == 1
                ? JsonResponse("{}")
                : RefreshGrantResponse(refreshFixture.Identity, RequestOperationId(request)));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(refreshTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, refreshFixture);
                await service.InitializeAsync(refreshCancellation.Token);
                Require(refreshCancellation.IsCancellationRequested);
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
            }
            await RequireCommittedGrantAsync(refreshFixture, "grant-after-refresh");
            Require(!refreshFixture.Metadata.Contains(RefreshAttemptKey));
            Require(!refreshFixture.Metadata.Contains(StagedGrantCommitKey));
        }
    }

    private static async Task StagedCredentialCommitRecoversAfterEveryWriteAsync()
    {
        string[] commitWrites =
        [
            StagedGrantCommitKey,
            StoredAccessTokenKey,
            StoredGrantExpiryKey,
            AndroidAccountLinkKeyAuthority.BindingStorageKey
        ];
        foreach (string terminateAfterWrite in commitWrites)
        {
            LinkFixture bootstrapFixture = await CreatePendingFixtureAsync();
            bootstrapFixture.Metadata.ThrowAfterSetKey = terminateAfterWrite;
            var bootstrapTerminal = new RecordingHandler(request => BootstrapGrantResponse(
                bootstrapFixture.Identity,
                RequestOperationId(request),
                alreadyClaimed: false));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(bootstrapTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, bootstrapFixture);
                await service.ResumePendingLinkAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
            }
            Require(bootstrapFixture.Metadata.Contains(StagedGrantCommitKey));
            Require(bootstrapFixture.Metadata.Contains(PendingPollOperationKey));
            Require(bootstrapFixture.Keys.Contains(bootstrapFixture.Identity.Alias));
            Require(
                bootstrapFixture.Metadata.GetRaw(StoredAccessTokenKey)
                == (terminateAfterWrite == StagedGrantCommitKey ? null : RotatedAccessToken));
            Require(
                StoredBindingGrantId(bootstrapFixture)
                == (terminateAfterWrite == AndroidAccountLinkKeyAuthority.BindingStorageKey
                    ? "grant-after-response-loss"
                    : null));

            var noNetworkTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException("Staged recovery must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(noNetworkTerminal))
            {
                AndroidAccountLinkService restarted = CreateService(transport, bootstrapFixture);
                await restarted.InitializeAsync();
                Require(restarted.Snapshot.Status == AndroidAccountLinkStatus.Linked);
            }
            Require(noNetworkTerminal.Requests.Count == 0);
            await RequireCommittedGrantAsync(bootstrapFixture, "grant-after-response-loss");
            Require(!bootstrapFixture.Metadata.Contains(PendingPollOperationKey));
            Require(!bootstrapFixture.Metadata.Contains(StagedGrantCommitKey));

            LinkFixture refreshFixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
            refreshFixture.Metadata.ThrowAfterSetKey = terminateAfterWrite;
            int refreshCall = 0;
            var refreshTerminal = new RecordingHandler(request => ++refreshCall == 1
                ? JsonResponse("{}")
                : RefreshGrantResponse(refreshFixture.Identity, RequestOperationId(request)));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(refreshTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, refreshFixture);
                await service.InitializeAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
            }
            Require(refreshFixture.Metadata.Contains(StagedGrantCommitKey));
            Require(refreshFixture.Metadata.Contains(RefreshAttemptKey));
            Require(
                refreshFixture.Metadata.GetRaw(StoredAccessTokenKey)
                == (terminateAfterWrite == StagedGrantCommitKey ? AccessToken : RotatedAccessToken));
            Require(
                StoredBindingGrantId(refreshFixture)
                == (terminateAfterWrite == AndroidAccountLinkKeyAuthority.BindingStorageKey
                    ? "grant-after-refresh"
                    : "grant-before-refresh"));

            var refreshRecoveryTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException("Staged refresh recovery must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(refreshRecoveryTerminal))
            {
                AndroidAccountLinkService restarted = CreateService(transport, refreshFixture);
                await restarted.InitializeAsync();
                Require(restarted.Snapshot.Status == AndroidAccountLinkStatus.Linked);
            }
            Require(refreshRecoveryTerminal.Requests.Count == 0);
            await RequireCommittedGrantAsync(refreshFixture, "grant-after-refresh");
            Require(!refreshFixture.Metadata.Contains(RefreshAttemptKey));
            Require(!refreshFixture.Metadata.Contains(StagedGrantCommitKey));
        }
    }

    private static async Task CredentialCommitCleanupFailureRemainsRestartableAsync()
    {
        foreach (string failedCleanup in new[] { PendingStateKey, StagedGrantCommitKey })
        {
            LinkFixture fixture = await CreatePendingFixtureAsync();
            fixture.Metadata.ThrowBeforeRemoveKey = failedCleanup;
            var terminal = new RecordingHandler(request => BootstrapGrantResponse(
                fixture.Identity,
                RequestOperationId(request),
                alreadyClaimed: false));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(terminal))
            {
                AndroidAccountLinkService service = CreateService(transport, fixture);
                await service.ResumePendingLinkAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
            }
            Require(fixture.Metadata.Contains(StagedGrantCommitKey));
            await RecoverCommittedGrantWithoutNetworkAsync(fixture, "grant-after-response-loss");
        }

        foreach (string failedCleanup in new[] { RefreshAttemptKey, StagedGrantCommitKey })
        {
            LinkFixture fixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
            fixture.Metadata.ThrowBeforeRemoveKey = failedCleanup;
            int call = 0;
            var terminal = new RecordingHandler(request => ++call == 1
                ? JsonResponse("{}")
                : RefreshGrantResponse(fixture.Identity, RequestOperationId(request)));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(terminal))
            {
                AndroidAccountLinkService service = CreateService(transport, fixture);
                await service.InitializeAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
            }
            Require(fixture.Metadata.Contains(StagedGrantCommitKey));
            await RecoverCommittedGrantWithoutNetworkAsync(fixture, "grant-after-refresh");
        }
    }

    private static async Task StaleLinkedRejectionCannotClearConcurrentRefreshAsync()
    {
        (string? Key, bool IsSet, bool AfterSuccess, bool ExpectCommitWait)[] boundaries =
        [
            (RefreshAttemptKey, true, false, true),
            (StagedGrantCommitKey, true, false, true),
            (StoredAccessTokenKey, true, false, true),
            (StoredGrantExpiryKey, true, false, true),
            (AndroidAccountLinkKeyAuthority.BindingStorageKey, true, false, true),
            (RefreshAttemptKey, false, false, true),
            (StagedGrantCommitKey, false, false, true),
            (null, false, true, false)
        ];
        foreach (HttpStatusCode staleStatus in new[]
                 {
                     HttpStatusCode.Unauthorized,
                     HttpStatusCode.Conflict
                 })
        {
            foreach ((string? key, bool isSet, bool afterSuccess, bool expectCommitWait) in boundaries)
            {
                LinkFixture fixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
                MetadataWriteBoundary? boundary = key is null
                    ? null
                    : new MetadataWriteBoundary(key, isSet);
                fixture.Metadata.WriteBoundary = boundary;
                var staleRequestStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseStaleResponse = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var staleResponseSent = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var terminal = new RecordingHandler(async (request, _) =>
                {
                    string path = request.RequestUri!.AbsolutePath;
                    if (path == "/api/v2/android/linked/groups")
                    {
                        Require(request.Headers.Authorization?.Parameter == AccessToken);
                        Require(request.Headers.GetValues(
                            AndroidAccountLinkRequestAuthority.GrantHeader).Single()
                            == "grant-before-refresh");
                        staleRequestStarted.TrySetResult();
                        await releaseStaleResponse.Task;
                        staleResponseSent.TrySetResult();
                        return JsonResponse("{}", staleStatus);
                    }
                    if (path == "/api/v2/install-linking/grants/status")
                    {
                        return JsonResponse("{}");
                    }
                    if (path == "/api/v2/install-linking/grants/refresh")
                    {
                        return RefreshGrantResponse(fixture.Identity, RequestOperationId(request));
                    }
                    throw new InvalidOperationException("Unexpected concurrent request path.");
                });
                using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
                AndroidAccountLinkService service = CreateService(transport, fixture);

                Task<IReadOnlyList<AndroidLinkedGroup>> staleRequest = service.ListGroupsAsync();
                await staleRequestStarted.Task;
                Task refresh = service.InitializeAsync();
                if (afterSuccess)
                {
                    await refresh;
                }
                else
                {
                    await boundary!.Reached;
                }

                releaseStaleResponse.TrySetResult();
                await staleResponseSent.Task;
                InvalidOperationException? staleFailure = null;
                if (!afterSuccess && expectCommitWait)
                {
                    for (int yield = 0; yield < 4; yield++)
                    {
                        await Task.Yield();
                    }
                    Require(!staleRequest.IsCompleted);
                    boundary!.Resume();
                    await refresh;
                }
                else if (!afterSuccess)
                {
                    staleFailure = await RequireThrowsAsync<InvalidOperationException>(async () =>
                        await staleRequest);
                    boundary!.Resume();
                    await refresh;
                }

                staleFailure ??=
                    await RequireThrowsAsync<InvalidOperationException>(async () =>
                        await staleRequest);
                Require(staleFailure.Message.Contains("changed", StringComparison.Ordinal));
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
                await RequireCommittedGrantAsync(fixture, "grant-after-refresh");
                Require(!fixture.Metadata.Contains(RefreshAttemptKey));
                Require(!fixture.Metadata.Contains(StagedGrantCommitKey));
            }
        }
    }

    private static async Task RefreshOperationPublicationClosesPreVisibilityRaceAsync()
    {
        foreach (HttpStatusCode staleStatus in new[]
                 {
                     HttpStatusCode.Unauthorized,
                     HttpStatusCode.Conflict
                 })
        {
            LinkFixture fixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
            var publicationBoundary = new MetadataWriteBoundary(
                RefreshAttemptKey,
                isSet: true,
                beforeVisibility: true);
            fixture.Metadata.WriteBoundary = publicationBoundary;
            var staleRequestStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseStaleResponse = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var staleResponseSent = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshRequestStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRefreshResponse = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var terminal = new RecordingHandler(async (request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == "/api/v2/android/linked/groups")
                {
                    staleRequestStarted.TrySetResult();
                    await releaseStaleResponse.Task;
                    staleResponseSent.TrySetResult();
                    return JsonResponse("{}", staleStatus);
                }
                if (path == "/api/v2/install-linking/grants/status")
                {
                    return JsonResponse("{}");
                }
                if (path == "/api/v2/install-linking/grants/refresh")
                {
                    refreshRequestStarted.TrySetResult();
                    await releaseRefreshResponse.Task;
                    return RefreshGrantResponse(fixture.Identity, RequestOperationId(request));
                }
                throw new InvalidOperationException("Unexpected publication-race request path.");
            });
            using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
            AndroidAccountLinkService service = CreateService(transport, fixture);

            Task<IReadOnlyList<AndroidLinkedGroup>> staleRequest = service.ListGroupsAsync();
            await staleRequestStarted.Task;
            Task refresh = service.InitializeAsync();
            await publicationBoundary.Reached;
            Require(!fixture.Metadata.Contains(RefreshAttemptKey));
            Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == AccessToken);
            Require(StoredBindingGrantId(fixture) == "grant-before-refresh");

            releaseStaleResponse.TrySetResult();
            await staleResponseSent.Task;
            for (int yield = 0; yield < 4; yield++)
            {
                await Task.Yield();
            }
            Require(!staleRequest.IsCompleted);

            publicationBoundary.Resume();
            await refreshRequestStarted.Task;
            InvalidOperationException staleFailure =
                await RequireThrowsAsync<InvalidOperationException>(async () =>
                    await staleRequest);
            Require(staleFailure.Message.Contains("changed", StringComparison.Ordinal));
            Require(fixture.Metadata.Contains(RefreshAttemptKey));
            Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == AccessToken);
            Require(StoredBindingGrantId(fixture) == "grant-before-refresh");

            releaseRefreshResponse.TrySetResult();
            await refresh;
            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
            await RequireCommittedGrantAsync(fixture, "grant-after-refresh");
            Require(!fixture.Metadata.Contains(RefreshAttemptKey));
            Require(!fixture.Metadata.Contains(StagedGrantCommitKey));
        }
    }

    private static async Task InvalidStagedCredentialIsQuarantinedWithoutDeletingNewerGrantAsync()
    {
        foreach (string invalidStage in new[]
                 {
                     "malformed",
                     "missing-grant",
                     "null-grant",
                     "expired",
                     "tampered"
                 })
        {
            LinkFixture fixture = await CreateInterruptedBootstrapStageAsync();
            SetInvalidStage(fixture, invalidStage);

            var noNetworkTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException("Invalid stage quarantine must not call Hub."));
            using AndroidAccountLinkHttpTransport transport = CreateTransport(noNetworkTerminal);
            AndroidAccountLinkService service = CreateService(transport, fixture);
            await service.InitializeAsync();

            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Unlinked);
            Require(noNetworkTerminal.Requests.Count == 0);
            Require(!fixture.Metadata.Contains(StagedGrantCommitKey));
            Require(!fixture.Metadata.Contains(PendingPollOperationKey));
            Require(!fixture.Metadata.Contains(StoredAccessTokenKey));
            Require(!fixture.Keys.Contains(fixture.Identity.Alias));

            await service.BeginLinkAsync();
            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
            Require(fixture.Metadata.Contains(PendingPollOperationKey));
        }

        foreach (string invalidStage in new[] { "missing-grant", "null-grant" })
        {
            LinkFixture beginFixture = await CreateInterruptedBootstrapStageAsync();
            SetInvalidStage(beginFixture, invalidStage);
            var beginTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException("Invalid stage Begin must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(beginTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, beginFixture);
                await service.BeginLinkAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
            }
            Require(!beginFixture.Metadata.Contains(StagedGrantCommitKey));
            Require(beginFixture.Metadata.Contains(PendingPollOperationKey));

            LinkFixture unlinkFixture = await CreateInterruptedRefreshStageAsync();
            SetInvalidStage(unlinkFixture, invalidStage);
            var unlinkTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException("Invalid stage unlink must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(unlinkTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, unlinkFixture);
                await service.UnlinkAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Unlinked);
            }
            Require(!unlinkFixture.Metadata.Contains(StagedGrantCommitKey));
            Require(!unlinkFixture.Metadata.Contains(RefreshAttemptKey));
            Require(!unlinkFixture.Metadata.Contains(StoredAccessTokenKey));
            Require(!unlinkFixture.Keys.Contains(unlinkFixture.Identity.Alias));

            LinkFixture eraseFixture = await CreateInterruptedRefreshStageAsync();
            SetInvalidStage(eraseFixture, invalidStage);
            var eraseTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException("Invalid stage erase must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(eraseTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, eraseFixture);
                InvalidOperationException failure =
                    await RequireThrowsAsync<InvalidOperationException>(() =>
                        service.EraseAccountAsync(
                            AndroidAccountErasureConfirmation.RequiredPhrase));
                Require(failure.Message == "Link your account first.");
            }
            Require(!eraseFixture.Metadata.Contains(StagedGrantCommitKey));
            Require(!eraseFixture.Metadata.Contains(RefreshAttemptKey));
            Require(!eraseFixture.Metadata.Contains(StoredAccessTokenKey));
            Require(!eraseFixture.Keys.Contains(eraseFixture.Identity.Alias));
        }

        foreach (string invalidStage in new[] { "missing-grant", "null-grant" })
        {
            LinkFixture cleanupFixture = await CreateInterruptedBootstrapStageAsync();
            cleanupFixture.Keys.FailDeleteAlias = cleanupFixture.Identity.Alias;
            SetInvalidStage(cleanupFixture, invalidStage);
            var cleanupTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException("Invalid stage cleanup must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(cleanupTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, cleanupFixture);
                await service.InitializeAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
                Require(!cleanupFixture.Metadata.Contains(StagedGrantCommitKey));
                Require(!cleanupFixture.Metadata.Contains(PendingPollOperationKey));
                Require(cleanupFixture.Metadata.Contains(
                    AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey));
                Require(cleanupFixture.Keys.Contains(cleanupFixture.Identity.Alias));

                cleanupFixture.Keys.FailDeleteAlias = null;
                await service.BeginLinkAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
                Require(!cleanupFixture.Metadata.Contains(
                    AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey));
                Require(!cleanupFixture.Keys.Contains(cleanupFixture.Identity.Alias));
            }
        }

        LinkFixture newerFixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
        int newerCall = 0;
        var newerTerminal = new RecordingHandler(request => ++newerCall == 1
            ? JsonResponse("{}")
            : RefreshGrantResponse(newerFixture.Identity, RequestOperationId(request)));
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(newerTerminal))
        {
            AndroidAccountLinkService service = CreateService(transport, newerFixture);
            await service.InitializeAsync();
            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        }
        newerFixture.Metadata.SetRaw(StagedGrantCommitKey, "{orphaned-stage");
        var statusTerminal = new RecordingHandler(_ => JsonResponse("{}"));
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(statusTerminal))
        {
            AndroidAccountLinkService restarted = CreateService(transport, newerFixture);
            await restarted.InitializeAsync();
            Require(restarted.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        }
        await RequireCommittedGrantAsync(newerFixture, "grant-after-refresh");
        Require(!newerFixture.Metadata.Contains(StagedGrantCommitKey));

        LinkFixture concurrentFixture = await CreateInterruptedRefreshStageAsync();
        string newerStage = concurrentFixture.Metadata.GetRaw(StagedGrantCommitKey)!;
        concurrentFixture.Metadata.SetRaw(StagedGrantCommitKey, "{stale-stage");
        int stageReads = 0;
        concurrentFixture.Metadata.BeforeGet = key =>
        {
            if (key == StagedGrantCommitKey && ++stageReads == 2)
            {
                concurrentFixture.Metadata.SetRaw(StagedGrantCommitKey, newerStage);
            }
        };
        var concurrentTerminal = new RecordingHandler(_ =>
            throw new InvalidOperationException("Newer staged recovery must not call Hub."));
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(concurrentTerminal))
        {
            AndroidAccountLinkService restarted = CreateService(transport, concurrentFixture);
            await restarted.InitializeAsync();
            Require(restarted.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        }
        concurrentFixture.Metadata.BeforeGet = null;
        await RequireCommittedGrantAsync(concurrentFixture, "grant-after-refresh");
        Require(!concurrentFixture.Metadata.Contains(StagedGrantCommitKey));
    }

    private static void SetInvalidStage(LinkFixture fixture, string invalidStage)
    {
        if (invalidStage == "malformed")
        {
            fixture.Metadata.SetRaw(StagedGrantCommitKey, "{not-json");
            return;
        }

        JsonObject staged = JsonNode.Parse(
            fixture.Metadata.GetRaw(StagedGrantCommitKey)!)!.AsObject();
        switch (invalidStage)
        {
            case "missing-grant":
                Require(staged.Remove("grant"));
                break;
            case "null-grant":
                staged["grant"] = null;
                break;
            case "expired":
                staged["grant"]!["expiresAtUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1);
                break;
            case "tampered":
                staged["operationId"] = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
                break;
            default:
                throw new InvalidOperationException("Unknown invalid staged-grant scenario.");
        }
        fixture.Metadata.SetRaw(StagedGrantCommitKey, staged.ToJsonString());
    }

    private static async Task InvalidStagedInstallationCannotReachKeyAuthorityAsync()
    {
        foreach (string invalidInstallation in new[]
                 {
                     "missing",
                     "null",
                     "blank",
                     "oversized"
                 })
        {
            LinkFixture initializeFixture = await CreateNoOperationInvalidInstallationStageAsync(
                invalidInstallation);
            var initializeTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException(
                    "Invalid staged installation Initialize must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(initializeTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, initializeFixture);
                await service.InitializeAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Unlinked);
            }
            Require(initializeTerminal.Requests.Count == 0);
            Require(!initializeFixture.Metadata.Contains(StagedGrantCommitKey));
            Require(!initializeFixture.Metadata.Contains(StoredAccessTokenKey));
            Require(!initializeFixture.Keys.Contains(initializeFixture.Identity.Alias));

            LinkFixture beginFixture = await CreateNoOperationInvalidInstallationStageAsync(
                invalidInstallation);
            var beginTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException(
                    "Invalid staged installation Begin must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(beginTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, beginFixture);
                await service.BeginLinkAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
            }
            Require(beginTerminal.Requests.Count == 0);
            Require(!beginFixture.Metadata.Contains(StagedGrantCommitKey));
            Require(!beginFixture.Keys.Contains(beginFixture.Identity.Alias));
            Require(beginFixture.Metadata.Contains(PendingPollOperationKey));

            LinkFixture unlinkFixture = await CreateNoOperationInvalidInstallationStageAsync(
                invalidInstallation);
            var unlinkTerminal = new RecordingHandler(_ =>
                throw new InvalidOperationException(
                    "Invalid staged installation Unlink must not call Hub."));
            using (AndroidAccountLinkHttpTransport transport = CreateTransport(unlinkTerminal))
            {
                AndroidAccountLinkService service = CreateService(transport, unlinkFixture);
                await service.UnlinkAsync();
                Require(service.Snapshot.Status == AndroidAccountLinkStatus.Unlinked);
            }
            Require(unlinkTerminal.Requests.Count == 0);
            Require(!unlinkFixture.Metadata.Contains(StagedGrantCommitKey));
            Require(!unlinkFixture.Metadata.Contains(StoredAccessTokenKey));
            Require(!unlinkFixture.Keys.Contains(unlinkFixture.Identity.Alias));
        }
    }

    private static async Task LostBootstrapResponseSurvivesProcessRestartAsync()
    {
        LinkFixture fixture = await CreatePendingFixtureAsync();
        var lostResponseTerminal = new RecordingHandler(_ =>
        {
            Require(fixture.Metadata.Contains(PendingPollOperationKey));
            throw new HttpRequestException("Injected response loss after redemption.");
        });
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(lostResponseTerminal))
        {
            AndroidAccountLinkService service = CreateService(transport, fixture);
            await service.ResumePendingLinkAsync();
            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
        }

        Require(fixture.Metadata.Contains(PendingStateKey));
        Require(fixture.Metadata.Contains(PendingPollOperationKey));
        Require(fixture.Keys.Contains(fixture.Identity.Alias));
        Require(lostResponseTerminal.Requests.Count == 1);
        string persistedOperation = fixture.Metadata.GetRaw(PendingPollOperationKey)!;
        Require(!persistedOperation.Contains("signature", StringComparison.OrdinalIgnoreCase));
        Require(!persistedOperation.Contains("nonce", StringComparison.OrdinalIgnoreCase));
        Require(!persistedOperation.Contains(AccessToken, StringComparison.Ordinal));

        var recoveryTerminal = new RecordingHandler(request =>
            BootstrapGrantResponse(
                fixture.Identity,
                RequestOperationId(request),
                alreadyClaimed: true));
        using AndroidAccountLinkHttpTransport recoveryTransport = CreateTransport(recoveryTerminal);
        AndroidAccountLinkService recoveryService = CreateService(
            recoveryTransport,
            fixture,
            version: "0.1.0-preview.12",
            hostLabel: "Renamed restart phone");
        await recoveryService.ResumePendingLinkAsync();

        Require(recoveryService.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == RotatedAccessToken);
        Require(!fixture.Metadata.Contains(PendingStateKey));
        Require(!fixture.Metadata.Contains(PendingPollOperationKey));
        AndroidAccountLinkKeyIdentity linked = await fixture.Authority.RequireLinkedIdentityAsync(
            fixture.Identity.InstallationId);
        Require(linked.GrantId == "grant-after-response-loss");
        Require(recoveryTerminal.Requests.Count == 1);
        RequireBootstrapRetriesShareOnlyStableOperation(
            lostResponseTerminal.Requests[0],
            recoveryTerminal.Requests[0]);
    }

    private static async Task AlreadyRedeemedBootstrapConflictRetainsFreshCredentialsAsync()
    {
        LinkFixture fixture = await CreatePendingFixtureAsync();
        int call = 0;
        var terminal = new RecordingHandler(request => ++call switch
        {
            1 => throw new HttpRequestException("Injected response loss after redemption."),
            2 => AlreadyRedeemedResponse(RequestOperationId(request)),
            _ => BootstrapGrantResponse(
                fixture.Identity,
                RequestOperationId(request),
                alreadyClaimed: true)
        });
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        AndroidAccountLinkService service = CreateService(transport, fixture);

        await service.ResumePendingLinkAsync();
        await service.ResumePendingLinkAsync();

        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Pending);
        Require(fixture.Metadata.Contains(PendingStateKey));
        Require(fixture.Metadata.Contains(PendingInstallationIdKey));
        Require(fixture.Metadata.Contains(PendingPollOperationKey));
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

    private static async Task LostRefreshResponseSurvivesProcessRestartAsync()
    {
        LinkFixture fixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
        int firstPhaseCall = 0;
        var lostResponseTerminal = new RecordingHandler(_ =>
        {
            if (++firstPhaseCall == 1)
            {
                return JsonResponse("{}");
            }

            Require(fixture.Metadata.Contains(RefreshAttemptKey));
            throw new HttpRequestException("Injected response loss after grant rotation.");
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
        string persistedOperation = fixture.Metadata.GetRaw(RefreshAttemptKey)!;
        Require(!persistedOperation.Contains(AccessToken, StringComparison.Ordinal));
        Require(!persistedOperation.Contains(RotatedAccessToken, StringComparison.Ordinal));
        string requestOperationId;
        using (JsonDocument body = JsonDocument.Parse(originalRefreshBody))
        {
            requestOperationId = body.RootElement.GetProperty("operationId").GetString()!;
            Require(requestOperationId.Length == 32);
        }
        using (JsonDocument stored = JsonDocument.Parse(persistedOperation))
        {
            Require(stored.RootElement.GetProperty("operationId").GetString() == requestOperationId);
            Require(stored.RootElement.GetProperty("sourceGrantId").GetString() == "grant-before-refresh");
        }
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == AccessToken);
        Require((await fixture.Authority.RequireLinkedIdentityAsync(fixture.Identity.InstallationId)).GrantId
            == "grant-before-refresh");

        int recoveryCall = 0;
        var recoveryTerminal = new RecordingHandler(request => ++recoveryCall == 1
            ? AlreadyRedeemedResponse(RequestOperationId(request))
            : RefreshGrantResponse(fixture.Identity, RequestOperationId(request)));
        using AndroidAccountLinkHttpTransport recoveryTransport = CreateTransport(recoveryTerminal);
        AndroidAccountLinkService recoveryService = CreateService(
            recoveryTransport,
            fixture,
            version: "0.1.0-preview.12",
            hostLabel: "Renamed restart phone");

        await recoveryService.InitializeAsync();
        Require(recoveryService.Snapshot.Status == AndroidAccountLinkStatus.Error);
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == AccessToken);
        Require(fixture.Metadata.Contains(RefreshAttemptKey));
        Require(recoveryTerminal.Requests[0].Body == originalRefreshBody);
        Require(recoveryTerminal.Requests[0].AuthorizationParameter == AccessToken);
        Require(recoveryTerminal.Requests[0].GrantId == "grant-before-refresh");
        Require(
            recoveryTerminal.Requests[0].PacketKey
            != lostResponseTerminal.Requests[1].PacketKey);
        Require(
            recoveryTerminal.Requests[0].Signature
            != lostResponseTerminal.Requests[1].Signature);

        await recoveryService.InitializeAsync();
        Require(recoveryService.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == RotatedAccessToken);
        Require(!fixture.Metadata.Contains(RefreshAttemptKey));
        Require(recoveryTerminal.Requests[1].Body == originalRefreshBody);
        Require(
            recoveryTerminal.Requests[1].PacketKey
            != recoveryTerminal.Requests[0].PacketKey);
        Require(
            recoveryTerminal.Requests[1].Signature
            != recoveryTerminal.Requests[0].Signature);
        Require((await fixture.Authority.RequireLinkedIdentityAsync(fixture.Identity.InstallationId)).GrantId
            == "grant-after-refresh");
    }

    private static async Task MismatchedOperationResponsesRetainRecoveryStateAsync()
    {
        const string mismatchedOperationId = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        LinkFixture pendingFixture = await CreatePendingFixtureAsync();
        var bootstrapTerminal = new RecordingHandler(_ => BootstrapGrantResponse(
            pendingFixture.Identity,
            mismatchedOperationId,
            alreadyClaimed: false));
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(bootstrapTerminal))
        {
            AndroidAccountLinkService service = CreateService(transport, pendingFixture);
            await service.ResumePendingLinkAsync();
            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
        }
        Require(pendingFixture.Metadata.Contains(PendingStateKey));
        Require(pendingFixture.Metadata.Contains(PendingPollOperationKey));
        Require(!pendingFixture.Metadata.Contains(StoredAccessTokenKey));
        Require(pendingFixture.Keys.Contains(pendingFixture.Identity.Alias));

        LinkFixture refreshFixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
        int call = 0;
        var refreshTerminal = new RecordingHandler(_ => ++call == 1
            ? JsonResponse("{}")
            : RefreshGrantResponse(refreshFixture.Identity, mismatchedOperationId));
        using (AndroidAccountLinkHttpTransport transport = CreateTransport(refreshTerminal))
        {
            AndroidAccountLinkService service = CreateService(transport, refreshFixture);
            await service.InitializeAsync();
            Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
        }
        Require(refreshFixture.Metadata.GetRaw(StoredAccessTokenKey) == AccessToken);
        Require(refreshFixture.Metadata.Contains(RefreshAttemptKey));
        Require((await refreshFixture.Authority.RequireLinkedIdentityAsync(
            refreshFixture.Identity.InstallationId)).GrantId == "grant-before-refresh");
    }

    private static AndroidAccountLinkService CreateService(
        AndroidAccountLinkHttpTransport transport,
        LinkFixture fixture,
        string version = "0.1.0-preview.11",
        string hostLabel = "Hostile test phone",
        string architecture = "arm64",
        IAndroidSystemService? systemService = null)
        => new(
            transport,
            systemService ?? new StubSystemService(),
            fixture.Authority,
            fixture.Metadata,
            versionProvider: () => version,
            hostLabelProvider: () => hostLabel,
            architectureProvider: () => architecture);

    private static async Task RequireCommittedGrantAsync(
        LinkFixture fixture,
        string expectedGrantId)
    {
        Require(fixture.Metadata.GetRaw(StoredAccessTokenKey) == RotatedAccessToken);
        Require(DateTimeOffset.TryParse(
            fixture.Metadata.GetRaw(StoredGrantExpiryKey),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset expiresAtUtc));
        Require(expiresAtUtc > DateTimeOffset.UtcNow);
        AndroidAccountLinkKeyIdentity linked = await fixture.Authority
            .RequireLinkedIdentityAsync(fixture.Identity.InstallationId);
        Require(linked.GrantId == expectedGrantId);
    }

    private static string? StoredBindingGrantId(LinkFixture fixture)
    {
        using JsonDocument binding = JsonDocument.Parse(
            fixture.Metadata.GetRaw(AndroidAccountLinkKeyAuthority.BindingStorageKey)!);
        JsonElement grantId = binding.RootElement.GetProperty("grantId");
        return grantId.ValueKind == JsonValueKind.Null ? null : grantId.GetString();
    }

    private static async Task RecoverCommittedGrantWithoutNetworkAsync(
        LinkFixture fixture,
        string expectedGrantId)
    {
        var terminal = new RecordingHandler(_ =>
            throw new InvalidOperationException("Staged cleanup recovery must not call Hub."));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        AndroidAccountLinkService restarted = CreateService(transport, fixture);
        await restarted.InitializeAsync();
        Require(restarted.Snapshot.Status == AndroidAccountLinkStatus.Linked);
        Require(terminal.Requests.Count == 0);
        await RequireCommittedGrantAsync(fixture, expectedGrantId);
        Require(!fixture.Metadata.Contains(StagedGrantCommitKey));
    }

    private static async Task<LinkFixture> CreateUnlinkedFixtureAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        return new(metadata, keys, authority, identity);
    }

    private static async Task<LinkFixture> CreatePendingFixtureAsync()
    {
        LinkFixture fixture = await CreateUnlinkedFixtureAsync();
        await fixture.Metadata.SetAsync(PendingStateKey, "pending-state");
        await fixture.Metadata.SetAsync(PendingInstallationIdKey, fixture.Identity.InstallationId);
        await fixture.Metadata.SetAsync(
            PendingStartedKey,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return fixture;
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

    private static async Task<LinkFixture> CreateInterruptedBootstrapStageAsync()
    {
        LinkFixture fixture = await CreatePendingFixtureAsync();
        fixture.Metadata.ThrowAfterSetKey = StagedGrantCommitKey;
        var terminal = new RecordingHandler(request => BootstrapGrantResponse(
            fixture.Identity,
            RequestOperationId(request),
            alreadyClaimed: false));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        AndroidAccountLinkService service = CreateService(transport, fixture);
        await service.ResumePendingLinkAsync();
        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
        Require(fixture.Metadata.Contains(StagedGrantCommitKey));
        Require(fixture.Metadata.Contains(PendingPollOperationKey));
        return fixture;
    }

    private static async Task<LinkFixture> CreateInterruptedRefreshStageAsync()
    {
        LinkFixture fixture = await CreateLinkedFixtureAsync(DateTimeOffset.UtcNow.AddDays(1));
        fixture.Metadata.ThrowAfterSetKey = StagedGrantCommitKey;
        int call = 0;
        var terminal = new RecordingHandler(request => ++call == 1
            ? JsonResponse("{}")
            : RefreshGrantResponse(fixture.Identity, RequestOperationId(request)));
        using AndroidAccountLinkHttpTransport transport = CreateTransport(terminal);
        AndroidAccountLinkService service = CreateService(transport, fixture);
        await service.InitializeAsync();
        Require(service.Snapshot.Status == AndroidAccountLinkStatus.Error);
        Require(fixture.Metadata.Contains(StagedGrantCommitKey));
        Require(fixture.Metadata.Contains(RefreshAttemptKey));
        return fixture;
    }

    private static async Task<LinkFixture> CreateNoOperationInvalidInstallationStageAsync(
        string invalidInstallation)
    {
        LinkFixture fixture = await CreateInterruptedRefreshStageAsync();
        await fixture.Metadata.RemoveAsync(RefreshAttemptKey);

        JsonObject staged = JsonNode.Parse(
            fixture.Metadata.GetRaw(StagedGrantCommitKey)!)!.AsObject();
        JsonObject grant = staged["grant"]!.AsObject();
        string? storedInstallationId;
        switch (invalidInstallation)
        {
            case "missing":
                Require(grant.Remove("installationId"));
                storedInstallationId = null;
                break;
            case "null":
                grant["installationId"] = null;
                storedInstallationId = null;
                break;
            case "blank":
                grant["installationId"] = "   ";
                storedInstallationId = "   ";
                break;
            case "oversized":
                storedInstallationId = "android-" + new string('A', 4096);
                grant["installationId"] = storedInstallationId;
                break;
            default:
                throw new InvalidOperationException("Unknown invalid installation scenario.");
        }

        fixture.Metadata.SetRaw(StagedGrantCommitKey, staged.ToJsonString());
        if (storedInstallationId is null)
        {
            await fixture.Metadata.RemoveAsync(InstallationIdKey);
        }
        else
        {
            fixture.Metadata.SetRaw(InstallationIdKey, storedInstallationId);
        }
        return fixture;
    }

    private static HttpResponseMessage BootstrapGrantResponse(
        AndroidAccountLinkKeyIdentity identity,
        string operationId,
        bool alreadyClaimed)
        => GrantResponse(
            "grant-after-response-loss",
            RotatedAccessToken,
            JsonSerializer.Serialize(new
            {
                grant = GrantMetadata("grant-after-response-loss", identity.InstallationId),
                alreadyClaimed,
                operationId
            }));

    private static HttpResponseMessage RefreshGrantResponse(
        AndroidAccountLinkKeyIdentity identity,
        string operationId)
        => GrantResponse(
            "grant-after-refresh",
            RotatedAccessToken,
            JsonSerializer.Serialize(new
            {
                grant = GrantMetadata("grant-after-refresh", identity.InstallationId),
                rotated = true,
                operationId
            }));

    private static HttpResponseMessage AlreadyRedeemedResponse(string operationId)
        => JsonResponse(
            JsonSerializer.Serialize(new { alreadyRedeemed = true, operationId }),
            HttpStatusCode.Conflict);

    private static string RequestOperationId(HttpRequestMessage request)
    {
        string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using JsonDocument json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("operationId").GetString()
            ?? throw new InvalidOperationException("Operation ID is required.");
    }

    private static IReadOnlyDictionary<string, string> QueryValues(Uri uri)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in uri.Query.TrimStart('?').Split(
            '&',
            StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0]);
            string value = parts.Length == 2
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : string.Empty;
            values.Add(key, value);
        }
        return values;
    }

    private static void RequireBootstrapRetriesShareOnlyStableOperation(
        ObservedRequest original,
        ObservedRequest retry)
    {
        using JsonDocument originalJson = JsonDocument.Parse(original.Body);
        using JsonDocument retryJson = JsonDocument.Parse(retry.Body);
        foreach (string property in new[]
                 {
                     "operationId",
                     "installLinkTransport",
                     "installationId",
                     "headId",
                     "applicationVersion",
                     "channelId",
                     "platform",
                     "architecture",
                     "publicKey",
                     "hostLabel"
                 })
        {
            Require(
                originalJson.RootElement.GetProperty(property).GetString()
                == retryJson.RootElement.GetProperty(property).GetString());
        }

        long originalIssued = originalJson.RootElement.GetProperty("issuedAtUnixSeconds").GetInt64();
        long retryIssued = retryJson.RootElement.GetProperty("issuedAtUnixSeconds").GetInt64();
        Require(retryIssued > originalIssued);
        Require(
            originalJson.RootElement.GetProperty("nonce").GetString()
            != retryJson.RootElement.GetProperty("nonce").GetString());
        Require(
            originalJson.RootElement.GetProperty("signature").GetString()
            != retryJson.RootElement.GetProperty("signature").GetString());
    }

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

        internal Action<string>? BeforeGet { get; set; }

        internal Action<string>? AfterSet { get; set; }

        internal string? ThrowAfterSetKey { get; set; }

        internal string? ThrowBeforeRemoveKey { get; set; }

        internal MetadataWriteBoundary? WriteBoundary { get; set; }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeGet?.Invoke(key);
            return Task.FromResult(_values.GetValueOrDefault(key));
        }

        public async Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (WriteBoundary is { IsSet: true, BeforeVisibility: true } beforeBoundary
                && string.Equals(beforeBoundary.Key, key, StringComparison.Ordinal))
            {
                await beforeBoundary.PauseAsync();
            }
            _values[key] = value;
            AfterSet?.Invoke(key);
            if (string.Equals(ThrowAfterSetKey, key, StringComparison.Ordinal))
            {
                ThrowAfterSetKey = null;
                throw new SimulatedProcessTerminationException();
            }
            if (WriteBoundary is { IsSet: true, BeforeVisibility: false } boundary
                && string.Equals(boundary.Key, key, StringComparison.Ordinal))
            {
                await boundary.PauseAsync();
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(ThrowBeforeRemoveKey, key, StringComparison.Ordinal))
            {
                ThrowBeforeRemoveKey = null;
                throw new IOException("Injected SecureStorage cleanup failure.");
            }
            if (WriteBoundary is { IsSet: false } boundary
                && string.Equals(boundary.Key, key, StringComparison.Ordinal))
            {
                await boundary.PauseAsync();
            }
            _values.Remove(key);
        }

        internal bool Contains(string key) => _values.ContainsKey(key);

        internal string? GetRaw(string key) => _values.GetValueOrDefault(key);

        internal void SetRaw(string key, string value) => _values[key] = value;
    }

    private sealed class SimulatedProcessTerminationException : IOException
    {
        internal SimulatedProcessTerminationException()
            : base("Injected process termination after a durable SecureStorage write.")
        {
        }
    }

    private sealed class MetadataWriteBoundary
    {
        private readonly TaskCompletionSource _reached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal MetadataWriteBoundary(
            string key,
            bool isSet,
            bool beforeVisibility = false)
        {
            Key = key;
            IsSet = isSet;
            BeforeVisibility = beforeVisibility;
        }

        internal string Key { get; }

        internal bool IsSet { get; }

        internal bool BeforeVisibility { get; }

        internal Task Reached => _reached.Task;

        internal async Task PauseAsync()
        {
            _reached.TrySetResult();
            await _resume.Task;
        }

        internal void Resume() => _resume.TrySetResult();
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
        private readonly Func<Uri, bool> _openUri;

        internal StubSystemService(Func<Uri, bool>? openUri = null)
        {
            _openUri = openUri ?? (_ => true);
        }

        internal List<Uri> OpenedUris { get; } = [];

        public Task<bool> OpenUriAsync(Uri uri)
        {
            OpenedUris.Add(uri);
            return Task.FromResult(_openUri(uri));
        }

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
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;

        internal RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = (request, _) => Task.FromResult(response(request));
        }

        internal RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
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
            HttpResponseMessage response = await _response(request, cancellationToken);
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
