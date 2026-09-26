using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Chummer.Android.Platform;
using Chummer.Run.Contracts.Community;
using Chummer.Android.Native;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunOriginSceneHttpCasesAsync()
    {
        foreach (string chapter in new[] { "First paragraph.\n\nNext paragraph.", "First paragraph.\r\n\r\nNext paragraph.",
            string.Concat(Enumerable.Repeat("🌲Ä", 1025)) })
        {
            string suggested = OriginBookScenePage.ExcerptFromChapter(chapter);
            Require(chapter.Contains(suggested, StringComparison.Ordinal) && Encoding.UTF8.GetByteCount(suggested) <= 3072
                && !suggested.Contains("Next paragraph", StringComparison.Ordinal), "Suggested excerpt changed prose, Unicode or byte bounds.");
        }
        OriginChapterSource source = new("runner", "chapter", new string('a', 64), "choice-1", "de-AT",
            "Nera", [new("metatype", "choice-1", "Elf")]);
        const string prose = "Nera stood in the forest clearing.", excerpt = "forest clearing", alt = "A forest clearing";
        string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        string imageHash = Convert.ToHexStringLower(SHA256.HashData(LifeSceneInputProbe.Png));
        string assetId = Hash(string.Join('\0', Hash("subject"), source.WorkspaceId, source.ChapterId, source.ChapterDigest, Hash(prose)));
        JsonObject Wire(string state = "review", bool image = true)
        {
            var wire = new JsonObject { ["assetId"] = assetId, ["state"] = state, ["publicationAuthorized"] = false };
            if (state is "review" or "persisted")
            {
                wire["manifest"] = new JsonObject { ["schema"] = "chummer.media.origin-scene/v1", ["assetId"] = assetId,
                    ["ownerDigest"] = Hash("subject"), ["workspaceId"] = source.WorkspaceId, ["chapterId"] = source.ChapterId,
                    ["chapterDigest"] = source.ChapterDigest, ["textDigest"] = Hash(prose), ["altText"] = alt,
                    ["contentType"] = "image/png", ["contentLengthBytes"] = LifeSceneInputProbe.Png.Length,
                    ["contentHash"] = imageHash, ["width"] = 1, ["height"] = 1, ["provider"] = "onemin",
                    ["providerReceiptDigest"] = new string('b', 64), ["admissionDigest"] = new string('c', 64),
                    ["publicationAuthorized"] = false };
                if (image) wire["imageBase64"] = Convert.ToBase64String(LifeSceneInputProbe.Png);
            }
            return wire;
        }
        using (var fixture = new ContinuationAccountFixture())
        using (var ui = new IssuedPageUiContext())
        {
            await fixture.LinkAsync("subject", "scene-A");
            IAndroidOriginSceneTransport transport = fixture.Account;
            var owner = fixture.Owner.Capture();
            await transport.RequestSceneAsync(owner, source, prose, excerpt, alt, false);
            await transport.RequestSceneAsync(owner, source, prose, "invented scene", alt, true);
            await transport.DecideSceneAsync(owner, source, prose, imageHash, true, false);
            Require(fixture.SceneRequests == 0, "Missing scene consent or invented text reached Hub.");
            Require((await transport.ReadSceneAsync(owner, source, prose)).Outcome == AndroidOriginSceneOutcome.NotFound,
                "Absent image was not distinguished from failed transport.");
            await ui.RunAsync(async () =>
            {
                foreach (int operation in new[] { 0, 1, 2 })
                {
                    bool disposed = false;
                    fixture.SceneResponse = (path, body) =>
                    {
                        Require(!ReferenceEquals(SynchronizationContext.Current, ui), "Scene dispatch blocked the UI context.");
                        Require(body["chapterRequestId"]!.GetValue<string>() == OriginChapterSourceIdentity.RequestId(source)
                            && body["textDigest"]!.GetValue<string>() == Hash(prose) && !body.ContainsKey("ownerDigest")
                            && !body.ContainsKey("assetId") && !body.ContainsKey("acceptedText"), "Scene request lost its exact chapter identity.");
                        if (operation == 1) Require(path.EndsWith("/request", StringComparison.Ordinal)
                            && body["externalProcessingConsent"]!.GetValue<bool>() && body["sceneExcerpt"]!.GetValue<string>() == excerpt,
                            "Signed image consent changed.");
                        if (operation == 2) Require(path.EndsWith("/decide", StringComparison.Ordinal)
                            && body["expectedImageHash"]!.GetValue<string>() == imageHash && body["explicitlyConfirmed"]!.GetValue<bool>(),
                            "Image approval was not bound to the preview bytes.");
                        var wire = Wire(operation == 2 ? "persisted" : "review", operation == 0);
                        if (operation == 2) wire.Remove("manifest");
                        return new(HttpStatusCode.OK) { Content = new OffUiChapterContent(wire.ToJsonString(), () =>
                        { Require(!ReferenceEquals(SynchronizationContext.Current, ui), "Scene response disposal blocked the UI."); disposed = true; }) };
                    };
                    var result = operation switch
                    {
                        0 => await transport.ReadSceneAsync(owner, source, prose),
                        1 => await transport.RequestSceneAsync(owner, source, prose, excerpt, alt, true),
                        _ => await transport.DecideSceneAsync(owner, source, prose, imageHash, true, true)
                    };
                    Require(result.Outcome == AndroidOriginSceneOutcome.Available && disposed
                        && (operation != 0 || result.Image!.Bytes.SequenceEqual(LifeSceneInputProbe.Png)), "Exact private scene roundtrip failed.");
                }
            });
            var attacks = new Action<JsonObject>[]
            {
                w => w["assetId"] = new string('f', 64), w => w["publicationAuthorized"] = true,
                w => w.Remove("publicationAuthorized"), w => w["url"] = "https://provider.invalid/image.png",
                w => w["state"] = "ready", w => w["manifest"]!["ownerDigest"] = new string('f', 64),
                w => w["manifest"]!["workspaceId"] = "other", w => w["manifest"]!["chapterDigest"] = new string('f', 64),
                w => w["manifest"]!["textDigest"] = new string('f', 64), w => w["manifest"]!["contentHash"] = new string('f', 64),
                w => w["manifest"]!["contentLengthBytes"] = 10, w => w["manifest"]!["width"] = 2,
                w => w["manifest"]!["height"] = 5000, w => w["manifest"]!["contentType"] = "image/svg+xml",
                w => w["manifest"]!["provider"] = "unknown", w => w["manifest"]!["providerReceiptDigest"] = "invalid",
                w => w["manifest"]!["publicationAuthorized"] = true, w => w["imageBase64"] = "invalid",
                w => w["imageBase64"] = Convert.ToBase64String(new byte[LifeSceneInputProbe.Png.Length]),
                w => w.Remove("imageBase64"), w => w["State"] = "persisted"
            };
            foreach (var attack in attacks)
            {
                var wire = Wire(); attack(wire);
                fixture.SceneResponse = (_, _) => ContinuationJsonResponse(wire);
                var result = await transport.ReadSceneAsync(owner, source, prose);
                Require(result.Outcome == AndroidOriginSceneOutcome.Unavailable && result.Image is null,
                    "Hostile scene readback reached the reader.");
            }
            fixture.SceneResponse = (_, _) => new(HttpStatusCode.OK) { Content = new UnboundedSceneContent() };
            Require((await transport.ReadSceneAsync(owner, source, prose)).Outcome == AndroidOriginSceneOutcome.Unavailable,
                "Oversized chunked image response passed materialization limits.");
            fixture.SceneResponse = (_, _) => new(HttpStatusCode.Redirect);
            Require((await transport.ReadSceneAsync(owner, source, prose)).Image is null, "Redirect delivered provider content.");
            fixture.SceneResponse = (_, _) => throw new HttpRequestException("Lost possible render acknowledgement.");
            int before = fixture.SceneRequests;
            Require((await transport.RequestSceneAsync(owner, source, prose, excerpt, alt, true)).UnknownRemoteOutcome
                && fixture.SceneRequests == before + 1, "Unknown render outcome was retried or treated as absent.");
            fixture.SceneResponse = (_, _) => ContinuationJsonResponse(Wire("persisted"));
            Require((await transport.ReadSceneAsync(owner, source, prose)).Image?.ImageHash == imageHash,
                "Accepted image could not be recovered by an idempotent read.");
        }
        foreach (bool signing in new[] { true, false })
        foreach (int operation in new[] { 0, 1, 2 })
        {
            using var fixture = new ContinuationAccountFixture();
            await fixture.LinkAsync("subject", "scene-A");
            var owner = fixture.Owner.Capture();
            IAndroidOriginSceneTransport transport = fixture.Account;
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Wait() { started.SetResult(); await release.Task; }
            if (signing) fixture.Keys.BeforeRelease = async () => { fixture.Keys.BeforeRelease = null; await Wait(); };
            else fixture.BeforeHttpResponse = Wait;
            fixture.SceneResponse = (_, _) => ContinuationJsonResponse(Wire());
            var pending = operation switch
            {
                0 => transport.ReadSceneAsync(owner, source, prose),
                1 => transport.RequestSceneAsync(owner, source, prose, excerpt, alt, true),
                _ => transport.DecideSceneAsync(owner, source, prose, imageHash, true, true)
            };
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.LinkAsync("other", "scene-B");
            await fixture.LinkAsync("subject", "scene-A2");
            release.SetResult();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Require(result.Outcome == AndroidOriginSceneOutcome.Unauthorized && result.Image is null
                && fixture.SceneRequests == (signing ? 0 : 1), "Owner A→B→A accepted a retired private illustration.");
        }
        Console.WriteLine("PASS signed scene consent, exact image admission/review, 21 hostile readbacks, chunked bounds, no mutation retry, off-UI I/O and owner ABA");
    }

    private sealed class UnboundedSceneContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(new byte[6 * 1024 * 1024 + 1]).AsTask();
    }
}
