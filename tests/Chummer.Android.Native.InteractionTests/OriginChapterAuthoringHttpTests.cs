using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Android.Platform;
using Chummer.Run.Contracts.Community;
using Chummer.Application.Owners;
using System.Reflection;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunOriginChapterHttpCasesAsync()
    {
        OriginDossierBookRuntimeTests.RunAuthoringSource();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        OriginChapterSource source = new("runner", "chapter", new string('a', 64), "choice-1", "de-AT",
            "Nera", [new("metatype", "choice-1", "Elf")]);
        string digest = OriginChapterSourceIdentity.Digest(source);
        string id = OriginChapterSourceIdentity.RequestId(source);
        var queued = new OriginChapterAuthoringJob(id, digest, source,
            OriginChapterAuthoringStates.AwaitingAuthoring, "first_book_ai", null, null);
        var ready = queued with { State = OriginChapterAuthoringStates.ReviewRequired,
            DraftText = "Ein ausdrücklich synthetischer Entwurf.", ProviderReceiptDigest = new string('b', 64) };
        string textDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(ready.DraftText))).ToLowerInvariant();
        var accepted = ready with { ReaderAcceptedTextDigest = textDigest };
        using (var fixture = new ContinuationAccountFixture())
        using (var ui = new IssuedPageUiContext())
        {
            await fixture.LinkAsync("subject", "chapter-ui-thread");
            IAndroidOriginChapterTransport transport = fixture.Account;
            var owner = fixture.Owner.Capture();
            await ui.RunAsync(async () =>
            {
                foreach (int operation in new[] { 0, 1, 2 })
                {
                    bool disposed = false;
                    fixture.ChapterResponse = (_, _) =>
                    {
                        Require(!ReferenceEquals(SynchronizationContext.Current, ui),
                            "Chapter HTTP dispatch ran on the UI context.");
                        return new(operation == 0 ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                        {
                            Content = new OffUiChapterContent(operation == 0 ? "missing" :
                                JsonSerializer.Serialize(operation == 1 ? queued : accepted, json), () =>
                                {
                                    Require(!ReferenceEquals(SynchronizationContext.Current, ui),
                                        "Chapter response disposal ran on the UI context.");
                                    disposed = true;
                                })
                        };
                    };
                    var response = operation switch
                    {
                        0 => await transport.ReadChapterAsync(owner, source),
                        1 => await transport.RequestChapterAsync(owner, source, true),
                        _ => await transport.AcceptChapterAsync(owner, source,
                            ready.ProviderReceiptDigest!, ready.DraftText!, true)
                    };
                    Require(response.Outcome == (operation == 0 ? AndroidOriginChapterOutcome.NotFound
                            : AndroidOriginChapterOutcome.Available) && disposed
                        && ReferenceEquals(SynchronizationContext.Current, ui),
                        "Chapter I/O must leave the UI context, dispose even unread responses off-thread, and return to its caller.");
                }
            });
        }
        // Parse with case-sensitive object keys, as an actual wire payload does.
        // SerializeToNode(Web options) would replace an existing property when
        // adding a differently cased alias, erasing the hostile test condition.
        JsonObject Wire(OriginChapterAuthoringJob job) => JsonNode.Parse(JsonSerializer.Serialize(job, json))!.AsObject();
        using (var fixture = new ContinuationAccountFixture())
        {
            await fixture.LinkAsync("subject", "chapter-grant");
            IAndroidOriginChapterTransport transport = fixture.Account;
            var owner = fixture.Owner.Capture();
            var rejected = await transport.RequestChapterAsync(owner, source, false);
            Require(rejected.Job is null && fixture.ChapterRequests == 0, "Absent consent sent narrative facts.");
            var absent = await transport.ReadChapterAsync(owner, source);
            Require(absent.Outcome == AndroidOriginChapterOutcome.NotFound && fixture.ChapterRequests == 1,
                "A missing chapter was not distinguished from a transport failure.");
            fixture.ChapterResponse = (path, body) =>
            {
                Require(path.EndsWith("/request", StringComparison.Ordinal), "Unexpected chapter operation.");
                var request = body["authoring"]!.Deserialize<OriginChapterAuthoringRequest>(json)!;
                Require(request.RequestId == id && request.ExternalProcessingConsent
                    && OriginChapterSourceIdentity.Digest(request.Source) == digest, "Signed source or consent changed.");
                return ContinuationJsonResponse(Wire(queued));
            };
            var pending = await transport.RequestChapterAsync(owner, source, true);
            Require(pending.Job?.State == OriginChapterAuthoringStates.AwaitingAuthoring && !pending.UnknownRemoteOutcome,
                "The exact chapter request was not retained.");
            var previous = new OriginChapterPredecessor("accepted-previous", new string('c', 64), new string('d', 64), new string('e', 64));
            fixture.ChapterResponse = (path, body) =>
            {
                var request = body["authoring"]!.Deserialize<OriginChapterAuthoringRequest>(json)!;
                Require(request.Previous == previous, "The signed request lost its exact accepted predecessor.");
                return ContinuationJsonResponse(Wire(queued with { Previous = previous }));
            };
            Require((await transport.RequestChapterAsync(owner, source, true, previous: previous)).Job?.Previous == previous,
                "The exact predecessor was not retained in request readback.");
            fixture.ChapterResponse = (_, _) => ContinuationJsonResponse(Wire(queued));
            Require((await transport.RequestChapterAsync(owner, source, true, previous: previous)).Job is null,
                "The server silently dropping the predecessor was accepted.");
            fixture.ChapterResponse = (_, _) => ContinuationJsonResponse(Wire(queued with
                { Previous = previous with { TextDigest = new string('f', 64) } }));
            Require((await transport.RequestChapterAsync(owner, source, true, previous: previous)).Job is null,
                "A changed accepted predecessor was accepted.");
            int priorRequests = fixture.ChapterRequests;
            Require((await transport.RequestChapterAsync(owner, source, true, previous: previous with { SourceDigest = "invalid" })).Job is null
                && (await transport.RequestChapterAsync(owner, source, true, previous: previous with { RequestId = id })).Job is null
                && fixture.ChapterRequests == priorRequests, "Invalid/self predecessors reached the network.");
            int before = fixture.ChapterRequests;
            fixture.ChapterResponse = (_, _) => throw new HttpRequestException("Lost after possible write.");
            var lost = await transport.RequestChapterAsync(owner, source, true);
            Require(lost.UnknownRemoteOutcome && lost.Job is null && fixture.ChapterRequests == before + 1,
                "An uncertain write was retried or reported as definitely absent.");
            fixture.ChapterResponse = (path, body) =>
            {
                Require(path.EndsWith("/read", StringComparison.Ordinal) && body["requestId"]!.GetValue<string>() == id
                    && !body.ContainsKey("authoring"), "Recovery resent facts or changed job identity.");
                return ContinuationJsonResponse(Wire(ready));
            };
            var recovered = await transport.ReadChapterAsync(owner, source);
            Require(recovered.Job?.DraftText == ready.DraftText && recovered.Job.RequiresReaderReview
                && !recovered.Job.PublicationAuthorized && !recovered.Job.AffectsMechanics,
                "The readback did not preserve the review-only result.");
            Require(OriginChapterSourceIdentity.RequestId(JsonSerializer.Deserialize<OriginChapterSource>(
                JsonSerializer.Serialize(source, json), json)!) == id, "Cold reconstruction invented a second paid request.");
            before = fixture.ChapterRequests;
            Require((await transport.AcceptChapterAsync(owner, source, ready.ProviderReceiptDigest!, ready.DraftText, false)).Job is null
                && fixture.ChapterRequests == before, "Missing reader confirmation reached Hub.");
            fixture.ChapterResponse = (path, body) =>
            {
                Require(path.EndsWith("/accept", StringComparison.Ordinal) && body["sourceDigest"]!.GetValue<string>() == digest
                    && body["providerReceiptDigest"]!.GetValue<string>() == ready.ProviderReceiptDigest
                    && body["textDigest"]!.GetValue<string>() == textDigest && body["explicitlyConfirmed"]!.GetValue<bool>()
                    && !body.ContainsKey("authoring") && !body.ContainsKey("draftText"), "Acceptance did not bind exactly the reviewed bytes.");
                return ContinuationJsonResponse(Wire(accepted));
            };
            Require((await transport.AcceptChapterAsync(owner, source, ready.ProviderReceiptDigest!, ready.DraftText, true))
                .Job?.ReaderAcceptedTextDigest == textDigest, "The signed reader acceptance was not returned.");
            fixture.ChapterResponse = (_, _) => ContinuationJsonResponse(Wire(ready));
            Require((await transport.AcceptChapterAsync(owner, source, ready.ProviderReceiptDigest!, ready.DraftText, true))
                .Job is null, "An unchanged/unaccepted job was reported as accepted.");
            fixture.ChapterResponse = (_, _) => throw new HttpRequestException("Lost acceptance response.");
            before = fixture.ChapterRequests;
            var uncertainAcceptance = await transport.AcceptChapterAsync(owner, source, ready.ProviderReceiptDigest!, ready.DraftText, true);
            Require(uncertainAcceptance.UnknownRemoteOutcome && fixture.ChapterRequests == before + 1,
                "Unknown acceptance was blindly retried or reported as definitely absent.");
            fixture.ChapterResponse = (_, _) => ContinuationJsonResponse(Wire(accepted));
            Require((await transport.ReadChapterAsync(owner, source)).Job?.ReaderAcceptedTextDigest == textDigest,
                "Cold acceptance readback was not recoverable.");

            var attacks = new Action<JsonObject>[]
            {
                row => row["requestId"] = "other",
                row => row["sourceDigest"] = new string('c', 64),
                row => row["source"]!["workspaceId"] = "other",
                row => row["source"]!["facts"]![0]!["text"] = "unchosen future",
                row => row["state"] = "approved",
                row => row["provider"] = "unapproved",
                row => row["requiresReaderReview"] = false,
                row => row["affectsMechanics"] = true,
                row => row["publicationAuthorized"] = true,
                row => row["providerReceiptDigest"] = "unbound",
                row => row["draftText"] = new string('z', 65537),
                row => row["readerAcceptedTextDigest"] = new string('f', 64),
                row => row["DraftText"] = "case alias",
                row => row.Remove("source"),
                row => row["unexpectedSecret"] = "must-not-pass"
            };
            for (int attackIndex = 0; attackIndex < attacks.Length; attackIndex++)
            {
                var row = Wire(ready); attacks[attackIndex](row);
                fixture.ChapterResponse = (_, _) => ContinuationJsonResponse(row);
                var malformed = await transport.ReadChapterAsync(owner, source);
                Require(malformed.Outcome == AndroidOriginChapterOutcome.Unavailable && malformed.Job is null
                    && !malformed.RetryableReadFailure,
                    $"Malformed chapter case {attackIndex} returned {malformed.Outcome}; job present: {malformed.Job is not null}.");
            }
            fixture.ChapterResponse = (_, _) => new(HttpStatusCode.OK) { Content = new StringContent(new string(' ', 512 * 1024 + 1)) };
            var oversized = await transport.ReadChapterAsync(owner, source);
            Require(oversized.Job is null && !oversized.RetryableReadFailure,
                "Oversized chapter body was accepted or admitted for automatic retry.");
            fixture.ChapterResponse = (_, _) => new(HttpStatusCode.Conflict);
            string originalCredentials = JsonSerializer.Serialize(fixture.Metadata.Inner.Rows);
            Require((await transport.RequestChapterAsync(owner, source, true)).Outcome == AndroidOriginChapterOutcome.Conflict
                && fixture.Owner.Capture() == owner && fixture.Account.Snapshot.IsLinked
                && JsonSerializer.Serialize(fixture.Metadata.Inner.Rows) == originalCredentials,
                "Chapter conflict cleared or rotated account credentials.");
            fixture.ChapterResponse = (_, _) => new(HttpStatusCode.ServiceUnavailable);
            before = fixture.ChapterRequests;
            var unavailableRead = await transport.ReadChapterAsync(owner, source);
            var unavailableCreate = await transport.RequestChapterAsync(owner, source, true);
            var unavailableAccept = await transport.AcceptChapterAsync(owner, source,
                ready.ProviderReceiptDigest!, ready.DraftText, true);
            Require(unavailableRead.Outcome == AndroidOriginChapterOutcome.Unavailable
                && unavailableRead.RetryableReadFailure
                && unavailableCreate.Outcome == AndroidOriginChapterOutcome.Unavailable
                && unavailableAccept.Outcome == AndroidOriginChapterOutcome.Unavailable
                && !unavailableCreate.RetryableReadFailure && !unavailableAccept.RetryableReadFailure
                && unavailableRead.Job is null && unavailableCreate.Job is null && unavailableAccept.Job is null
                && !unavailableRead.UnknownRemoteOutcome && unavailableCreate.UnknownRemoteOutcome
                && unavailableAccept.UnknownRemoteOutcome && fixture.ChapterRequests == before + 3
                && fixture.Owner.Capture() == owner && fixture.Account.Snapshot.IsLinked
                && JsonSerializer.Serialize(fixture.Metadata.Inner.Rows) == originalCredentials,
                "Temporary Hub unavailability revoked credentials, exposed a job, retried, or hid an uncertain write.");
            foreach (var status in new[] { HttpStatusCode.BadGateway, HttpStatusCode.GatewayTimeout,
                HttpStatusCode.InternalServerError, HttpStatusCode.TooManyRequests, HttpStatusCode.Unauthorized,
                HttpStatusCode.Forbidden, HttpStatusCode.Conflict, HttpStatusCode.NotFound, HttpStatusCode.Redirect })
            {
                fixture.ChapterResponse = (path, body) =>
                {
                    Require(path.EndsWith("/read", StringComparison.Ordinal)
                        && body["requestId"]!.GetValue<string>() == id && !body.ContainsKey("authoring"),
                        "Status classification changed the job or resent narrative facts.");
                    return new(status);
                };
                before = fixture.ChapterRequests;
                var response = await transport.ReadChapterAsync(owner, source);
                Require(response.RetryableReadFailure == (status is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
                    && response.Job is null && !response.UnknownRemoteOutcome && fixture.ChapterRequests == before + 1,
                    $"HTTP {status} classification retried, exposed a job or admitted a non-transient read.");
            }
            foreach (var error in new[] { HttpRequestError.NameResolutionError, HttpRequestError.ConnectionError,
                HttpRequestError.ResponseEnded, HttpRequestError.SecureConnectionError,
                HttpRequestError.InvalidResponse, HttpRequestError.Unknown })
            {
                fixture.ChapterResponse = (_, _) => throw new HttpRequestException(error, "Synthetic transport interruption.");
                before = fixture.ChapterRequests;
                var response = await transport.ReadChapterAsync(owner, source);
                bool retryable = error is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError
                    or HttpRequestError.ResponseEnded;
                Require(response.RetryableReadFailure == retryable && response.Job is null
                    && !response.UnknownRemoteOutcome && fixture.ChapterRequests == before + 1,
                    $"Transport failure {error} was retried or misclassified.");
                var createResponse = await transport.RequestChapterAsync(owner, source, true);
                var acceptResponse = await transport.AcceptChapterAsync(owner, source,
                    ready.ProviderReceiptDigest!, ready.DraftText, true);
                Require(!createResponse.RetryableReadFailure && !acceptResponse.RetryableReadFailure
                    && createResponse.UnknownRemoteOutcome && acceptResponse.UnknownRemoteOutcome
                    && fixture.ChapterRequests == before + 3,
                    "A write transport interruption became an automatic retry.");
            }
            fixture.ChapterResponse = (_, _) => throw new TaskCanceledException("Synthetic internal deadline.");
            Require((await transport.ReadChapterAsync(owner, source)).RetryableReadFailure,
                "An internal read deadline could not be observed again.");
            using var cancellation = new CancellationTokenSource();
            fixture.ChapterResponse = (_, _) =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            };
            before = fixture.ChapterRequests;
            var canceled = await transport.ReadChapterAsync(owner, source, cancellation.Token);
            Require(!canceled.RetryableReadFailure && canceled.Job is null && fixture.ChapterRequests == before + 1,
                "Caller cancellation was treated as a recoverable deadline.");
            Require(!(await transport.ReadChapterAsync(owner, source, cancellation.Token)).RetryableReadFailure
                && fixture.ChapterRequests == before + 1, "Pre-canceled status read reached the network.");
            Require(fixture.Owner.Capture() == owner && fixture.Account.Snapshot.IsLinked
                && JsonSerializer.Serialize(fixture.Metadata.Inner.Rows) == originalCredentials,
                "Read-failure classification mutated account authority.");
        }
        foreach (bool signing in new[] { true, false })
        foreach (int operation in new[] { 0, 1, 2 })
        {
            using var fixture = new ContinuationAccountFixture();
            await fixture.LinkAsync("subject", "chapter-A");
            var owner = fixture.Owner.Capture();
            IAndroidOriginChapterTransport transport = fixture.Account;
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Wait() { started.SetResult(); await release.Task; }
            if (signing) fixture.Keys.BeforeRelease = async () => { fixture.Keys.BeforeRelease = null; await Wait(); };
            else fixture.BeforeHttpResponse = Wait;
            fixture.ChapterResponse = (_, _) => operation == 0
                ? throw new HttpRequestException(HttpRequestError.ConnectionError, "Read interrupted after owner changed.")
                : ContinuationJsonResponse(Wire(operation == 2 ? accepted : ready));
            var pending = operation switch
            {
                0 => transport.ReadChapterAsync(owner, source),
                1 => transport.RequestChapterAsync(owner, source, true),
                _ => transport.AcceptChapterAsync(owner, source, ready.ProviderReceiptDigest!, ready.DraftText, true)
            };
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.LinkAsync("other-subject", "chapter-B").WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.LinkAsync("subject", "chapter-A2").WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Require(result.Outcome == AndroidOriginChapterOutcome.Unauthorized && result.Job is null && !result.RetryableReadFailure
                && fixture.ChapterRequests == (signing ? 0 : 1), "Retired A-to-B-to-A owner accepted a chapter.");
        }
        Console.WriteLine("PASS signed Origin chapter consent/reader acceptance, stable recovery, 15 hostile readbacks, bounds, classified transient reads, cancellation, no write retry and owner ABA");
    }

    private sealed class OffUiChapterContent(string text, Action onDispose)
        : StringContent(text, Encoding.UTF8, "application/json")
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) onDispose();
            base.Dispose(disposing);
        }
    }
}

// Only the remote job is synthetic here. Page/coordinator/Core/file-store paths
// are real; signed HTTP and owner admission are exercised separately above.
public class OriginAuthoringPageAccount : StrictPageProxy, IAndroidOriginChapterTransport
{
    public int Requests { get; private set; }
    public int Reads { get; private set; }
    public int Acceptances { get; private set; }
    public bool FailAcceptance { get; set; }
    public bool FailRead { get; set; }
    public bool TransientReadFailure { get; set; }
    public bool Ready { get; set; }
    public AndroidAccountLinkStatus Status { get; set; } = AndroidAccountLinkStatus.Linked;
    public int LinkStarts { get; private set; }
    private OriginChapterAuthoringJob? _job;

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name == "get_Snapshot") return new AndroidAccountLinkSnapshot(Status, "Test account");
        if (method?.Name == "BeginLinkAsync") { LinkStarts++; return Task.CompletedTask; }
        if (method?.Name is "InitializeAsync" or "RefreshAsync") return Task.CompletedTask;
        return base.Invoke(method, args);
    }

    public Task<AndroidOriginChapterResult> RequestChapterAsync(OwnerContextStamp owner, OriginChapterSource source,
        bool externalProcessingConsent, CancellationToken ct = default, OriginChapterPredecessor? previous = null)
    {
        if (!externalProcessingConsent) throw new InvalidOperationException("No consent.");
        Requests++;
        _job ??= new(OriginChapterSourceIdentity.RequestId(source), OriginChapterSourceIdentity.Digest(source), source,
            OriginChapterAuthoringStates.AwaitingAuthoring, "first_book_ai", null, null) { Previous = previous };
        return Task.FromResult(new AndroidOriginChapterResult(AndroidOriginChapterOutcome.Available, _job));
    }

    public Task<AndroidOriginChapterResult> ReadChapterAsync(OwnerContextStamp owner, OriginChapterSource source,
        CancellationToken ct = default)
    {
        Reads++;
        if (TransientReadFailure) return Task.FromResult(new AndroidOriginChapterResult(
            AndroidOriginChapterOutcome.Unavailable, RetryableReadFailure: true));
        if (FailRead) return Task.FromResult(new AndroidOriginChapterResult(AndroidOriginChapterOutcome.Unavailable));
        if (_job is null) return Task.FromResult(new AndroidOriginChapterResult(AndroidOriginChapterOutcome.NotFound));
        if (Ready) _job = _job with { State = OriginChapterAuthoringStates.ReviewRequired,
            DraftText = "Synthetic transport chapter for explicit review.", ProviderReceiptDigest = new string('d', 64) };
        return Task.FromResult(new AndroidOriginChapterResult(AndroidOriginChapterOutcome.Available, _job));
    }

    public Task<AndroidOriginChapterResult> AcceptChapterAsync(OwnerContextStamp owner, OriginChapterSource source,
        string providerReceiptDigest, string draftText, bool explicitlyConfirmed, CancellationToken ct = default)
    {
        if (!explicitlyConfirmed || _job?.SourceDigest != OriginChapterSourceIdentity.Digest(source)
            || _job.DraftText != draftText || _job.ProviderReceiptDigest != providerReceiptDigest)
            throw new InvalidOperationException("Wrong reader acceptance.");
        Acceptances++;
        if (FailAcceptance) return Task.FromResult(new AndroidOriginChapterResult(
            AndroidOriginChapterOutcome.Unavailable, UnknownRemoteOutcome: true));
        _job = _job with { ReaderAcceptedTextDigest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(draftText))).ToLowerInvariant() };
        return Task.FromResult(new AndroidOriginChapterResult(AndroidOriginChapterOutcome.Available, _job));
    }
}
