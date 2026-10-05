using System.Reflection;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.OriginBooks;
using Chummer.Run.Contracts.Community;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunOriginIllustrationAcceptanceAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            var account = DispatchProxy.Create<IAndroidAccountLinkService, OriginIllustrationAcceptanceAccount>();
            var remote = (OriginIllustrationAcceptanceAccount)account;
            remote.AfterAcceptance = () => remote.Accepted = true;
            var output = new LifeBookOutputProbe();
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, accountService: account,
                outputDocuments: output);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Illustrated childhood", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await runtime.Coordinator.SaveAsync();
            await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 2));
            await runtime.Presenter.LoadAsync(id, default);
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            var chapter = book.Chapters.Single(c => !book.IsOpeningSetup(c));
            var source = book.AuthoringSource(chapter);
            var prose = OriginBookProseDraft.Create(chapter, book.Locale, OriginChapterSourceIdentity.RequestId(source),
                new string('d', 64), "The complete childhood chapter remains readable while its illustration is prepared.");
            remote.Seed(source, prose);
            book = (await runtime.Coordinator.SyncOriginChapterAsync(book, chapter, source, true, () => true,
                default, consentToAutomaticIllustrations: true)).Book!;
            var reader = new RetainedOriginBookPage(runtime.Coordinator);
            var window = new Window(new NavigationPage(reader));
            using var alerts = new IssuedPageAlerts(reader, window);
            await alerts.PreflightAsync();
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reader, "OnAppearing"));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            remote.BeforeAcceptance = _ => { entered.TrySetResult(); return release.Task; };
            var button = IssuedElements(reader).OfType<Button>().Single(b => b.AutomationId == $"origin-chapter-read-{chapter.Sequence}");
            Task reading = ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(IssuedElements(reader).OfType<Label>().Any(l => l.Text == prose.Text)
                    && IssuedElements(reader).OfType<Button>().Any(b => b.AutomationId == "origin-book-export-epub" && b.IsEnabled),
                    "Slow reader acknowledgement blocked full text or EPUB export.");
            }
            finally { release.TrySetResult(); }
            await reading.WaitAsync(TimeSpan.FromSeconds(10));
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!IssuedElements(reader).Any(e => e.AutomationId == $"origin-book-scene-{chapter.Sequence}")
                && remote.EarlySceneReads == 0 && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
            Require(remote.EarlySceneReads == 0, "Illustration read raced the durable server reader acknowledgement.");
            Require(IssuedElements(reader).OfType<Image>().Any(e => e.AutomationId == $"origin-book-scene-{chapter.Sequence}")
                && remote.ImageRequests == 1 && remote.Requests == 0, "The first automatic image was not inserted exactly once.");
            await ui.BeginAsyncVoid(() => ((IButtonController)IssuedElements(reader).OfType<Button>()
                .Single(b => b.AutomationId == "origin-book-export-epub")).SendClicked());
            using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(output.Epub)))
                Require(zip.Entries.Any(e => e.FullName.StartsWith("EPUB/images/", StringComparison.Ordinal)),
                    "The automatic illustration was absent from the EPUB.");
            IssuedPageLifecycle(reader, "OnDisappearing");
            remote.BeforeAcceptance = null;
            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            Require(book.Scene(chapter) is not null, "Cold reading lost the inserted illustration.");
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reader, "OnAppearing"));
            Require(remote.ImageRequests == 1, "Reopening replayed the image request.");
            IssuedPageLifecycle(reader, "OnDisappearing");
            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            // Cold local selection with a missing/lost server acknowledgement:
            // failures must stop before even reading Media, never spend again.
            int sceneReads = remote.SceneReads;
            remote.Seed(source, prose); remote.Accepted = false; remote.FailAcceptance = true;
            var failed = await runtime.Coordinator.SyncOriginBookSceneAsync(book, chapter, "", "", true,
                () => true, default, automatic: true);
            Require(failed.Result.Outcome == AndroidOriginSceneOutcome.Unavailable && remote.SceneReads == sceneReads
                && remote.ImageRequests == 1, "Failed reader acknowledgement reached Media or replayed an image.");
            remote.FailAcceptance = false; remote.CorruptPredecessor = true;
            var mismatch = await runtime.Coordinator.SyncOriginBookSceneAsync(book, chapter, "", "", true,
                () => true, default, automatic: true);
            Require(mismatch.Result.Outcome == AndroidOriginSceneOutcome.Conflict && remote.SceneReads == sceneReads,
                "Different remote prose was acknowledged for an illustration.");
            remote.CorruptPredecessor = false;
            using (var canceled = new CancellationTokenSource())
            {
                remote.AfterPredecessorRead = canceled.Cancel;
                var stopped = await runtime.Coordinator.SyncOriginBookSceneAsync(book, chapter, "", "", true,
                    () => true, canceled.Token, automatic: true);
                Require(stopped.Result.Outcome == AndroidOriginSceneOutcome.Unauthorized && remote.SceneReads == sceneReads,
                    "Canceled acceptance preflight reached Media.");
            }
            remote.AfterPredecessorRead = null;
            var recovered = await runtime.Coordinator.SyncOriginBookSceneAsync(book, chapter, "", "", true,
                () => true, default, automatic: true);
            Require(recovered.Scene is not null && remote.ImageRequests == 1 && remote.Accepted,
                "The saved acceptance outbox did not recover the existing image without replay.");
            RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
            Require(alerts.Titles.Count == 0, "Illustration acknowledgement displayed an unexpected error.");
            ui.AssertHealthy();
            Console.WriteLine("PASS illustration: delayed reader acknowledgement before media, responsive full text/export, one image, EPUB and cold persistence without replay");
        });
    }
}

public class OriginIllustrationAcceptanceAccount : OriginSuccessorAccount, IAndroidOriginSceneTransport
{
    public bool Accepted;
    public int EarlySceneReads, ImageRequests, SceneReads;
    private bool _persisted;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
    public Task<AndroidOriginSceneResult> ReadSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, CancellationToken ct = default)
    {
        SceneReads++;
        if (!Accepted) { EarlySceneReads++; return Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Conflict)); }
        return Task.FromResult(_persisted ? new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Available, "persisted",
            new("The same growing protagonist", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Png)),
                "onemin", new string('e', 64), Png.ToArray(), OriginBookReadingState.AutomaticIllustrations))
            : new AndroidOriginSceneResult(AndroidOriginSceneOutcome.NotFound));
    }
    public Task<AndroidOriginSceneResult> RequestAutomaticSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, bool externalProcessingConsent, CancellationToken ct = default)
    {
        if (!Accepted || !externalProcessingConsent) throw new InvalidOperationException("Unacknowledged image request.");
        ImageRequests++; _persisted = true;
        return Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Available, "persisted"));
    }
    public Task<AndroidOriginSceneResult> RequestSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, string excerpt, string alt, bool consent, CancellationToken ct = default)
        => throw new InvalidOperationException("No manual scene request expected.");
    public Task<AndroidOriginSceneResult> DecideSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, string hash, bool approve, bool confirmed, CancellationToken ct = default)
        => throw new InvalidOperationException("No manual image decision expected.");
}
