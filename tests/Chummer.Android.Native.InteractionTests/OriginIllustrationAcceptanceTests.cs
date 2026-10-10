using System.Reflection;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.OriginBooks;
using Chummer.Run.Contracts.Community;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunOriginIllustrationAfterDecisionAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        var account = DispatchProxy.Create<IAndroidAccountLinkService, OriginIllustrationAcceptanceAccount>();
        var remote = (OriginIllustrationAcceptanceAccount)account;
        remote.AfterAcceptance = () => remote.Accepted = true;
        await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
            productionCreationOverview: true, linkedOwners: owners, accountService: account);
        await runtime.Coordinator.InitializeAsync();
        await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
        await runtime.Coordinator.CreateRunnerAsync();
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Delayed childhood illustration", default);
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
        await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
        var id = runtime.Coordinator.State.WorkspaceId!.Value;
        await runtime.Coordinator.SaveAsync();
        await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 2));
        await runtime.Presenter.LoadAsync(id, default);
        var book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
        var chapter = book.Chapters.Single(c => !book.IsOpeningSetup(c));
        var source = book.AuthoringSource(chapter);
        var prose = OriginBookProseDraft.Create(chapter, book.Locale, OriginChapterSourceIdentity.RequestId(source),
            new string('d', 64), "The accepted childhood chapter remains unchanged when the next decision is committed.");
        remote.Seed(source, prose);
        book = (await runtime.Coordinator.SyncOriginChapterAsync(book, chapter, source, true, () => true,
            default, consentToAutomaticIllustrations: true)).Book!;
        book = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(book, prose, true, true, () => true, default))!;
        Require(runtime.Coordinator.CanAutomaticallyIllustrateOriginBook(book)
            && runtime.Coordinator.CanRequestOriginBookScene(book, chapter), "Initial accepted illustration is not eligible.");

        // Reproduce leaving the reader and committing the next real Core
        // decision before the first illustration has been admitted. No provider
        // generation is performed by this isolated account adapter.
        var timeline = new FileOriginDossierDraftTimelineStore(runtime.StateDirectory);
        var checkpoint = (await timeline.LoadAsync(owners.Capture().Owner.Value, id.Value))!;
        var advanced = await Task.Run(() =>
        {
            var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(
                new CharacterCreationFoundationLifeModuleDecisionAuthority(
                    runtime.Services.GetRequiredService<IWorkspaceStore>(),
                    runtime.Services.GetRequiredService<ICharacterCreationFoundationService>(),
                    runtime.Services.GetRequiredService<ICharacterFileQueries>(), () => "de-DE")));
            var restored = interaction.Restore(checkpoint).Value!;
            var choice = restored.Projection.CurrentTurn.LegalChoices.First(c => c.ChoiceId != "finish-life-module-selection");
            var answers = choice.FollowUps?.ToDictionary(prompt => prompt.PromptId,
                prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Renraku");
            var prepared = interaction.Prepare(restored, choice.ChoiceId, answers).Value!;
            return interaction.Confirm(prepared, prepared.PendingPreview!.PreviewDigest,
                "illustration-after-next-decision", true).Value!.Checkpoint;
        });
        await timeline.SaveAsync(advanced);
        await runtime.Presenter.LoadAsync(id, default);
        var afterDecision = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
        book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
        chapter = book.Chapters.Single(c => c.ChapterId == chapter.ChapterId);
        Require(book.Reading(chapter)?.Selected?.DraftDigest == prose.DraftDigest
            && book.Readings?.IllustrationPolicy == OriginBookReadingState.AutomaticIllustrations,
            "The next module lost the accepted prose or saved automatic illustration policy.");
        Require(runtime.Coordinator.CanAutomaticallyIllustrateOriginBook(book)
            && runtime.Coordinator.CanRequestOriginBookScene(book, chapter),
            "Committing the next module silently disabled the previous chapter's illustration.");
        var recovered = await runtime.Coordinator.SyncAutomaticOriginBookSceneAsync(book, chapter, true, () => true, default);
        Require(recovered.Book?.Scene(chapter) is not null && remote.ImageRequests == 1
            && remote.Requests == 0 && remote.Accepted,
            "The previous chapter's image did not recover exactly once after the next decision.");
        book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
        var reopened = await runtime.Coordinator.SyncAutomaticOriginBookSceneAsync(book, chapter, true, () => true, default);
        Require(reopened.Book?.Scene(chapter) is not null && remote.ImageRequests == 1 && remote.Requests == 0,
            "File-backed reader reload replayed image or chapter generation.");
        RequireSameRewardDocument(afterDecision, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
        Console.WriteLine("PASS next Core decision preserves accepted prose, automatic illustration eligibility and file-backed no-replay recovery");
    }

    internal static async Task RunOriginIllustrationAcceptanceAsync(string contentRoot,
        bool transientReadBeforeCreation = false, bool lostResultAfterCreation = false,
        bool existingRemoteAdmission = false)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            var account = DispatchProxy.Create<IAndroidAccountLinkService, OriginIllustrationAcceptanceAccount>();
            var remote = (OriginIllustrationAcceptanceAccount)account;
            remote.FailInitialSceneRead = transientReadBeforeCreation;
            remote.LoseCreatedSceneRead = lostResultAfterCreation;
            remote.ExistingRemoteAdmission = existingRemoteAdmission;
            int expectedImageRequests = existingRemoteAdmission ? 0 : 1;
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
            if (lostResultAfterCreation || existingRemoteAdmission)
            {
                // The paid request succeeded, but its result read was interrupted.
                // Even a subsequent "not found" must not cause a second request.
                int expectedReads = existingRemoteAdmission ? 2 : 3;
                while (remote.SceneReads < expectedReads && wait.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(10);
                Require(remote.SceneReads >= expectedReads && remote.ImageRequests == expectedImageRequests,
                    "A lost result read replayed the paid image request or was never observed again.");
                IssuedPageLifecycle(reader, "OnDisappearing");
                book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
                var reconciled = await runtime.Coordinator.SyncAutomaticOriginBookSceneAsync(book, chapter,
                    false, () => true, default);
                Require(reconciled.Book?.Scene(chapter) is not null && remote.ImageRequests == expectedImageRequests,
                    "Read-only reconciliation did not retain the existing image without another request.");
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reader, "OnAppearing"));
                wait.Restart();
            }
            while (!IssuedElements(reader).Any(e => e.AutomationId == $"origin-book-scene-{chapter.Sequence}")
                && remote.EarlySceneReads == 0 && wait.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(10);
            Require(remote.EarlySceneReads == 0, "Illustration read raced the durable server reader acknowledgement.");
            Require(IssuedElements(reader).OfType<Image>().Any(e => e.AutomationId == $"origin-book-scene-{chapter.Sequence}")
                && remote.ImageRequests == expectedImageRequests && remote.Requests == 0,
                "The automatic image was not retained without an unexpected creation request.");
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
            Require(remote.ImageRequests == expectedImageRequests, "Reopening replayed the image request.");
            IssuedPageLifecycle(reader, "OnDisappearing");
            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            // Cold local selection with a missing/lost server acknowledgement:
            // failures must stop before even reading Media, never spend again.
            int sceneReads = remote.SceneReads;
            remote.Seed(source, prose); remote.Accepted = false; remote.FailAcceptance = true;
            var failed = await runtime.Coordinator.SyncOriginBookSceneAsync(book, chapter, "", "", true,
                () => true, default, automatic: true);
            Require(failed.Result.Outcome == AndroidOriginSceneOutcome.Unavailable && remote.SceneReads == sceneReads
                && remote.ImageRequests == expectedImageRequests, "Failed reader acknowledgement reached Media or replayed an image.");
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
            Require(recovered.Scene is not null && remote.ImageRequests == expectedImageRequests && remote.Accepted,
                "The saved acceptance outbox did not recover the existing image without replay.");
            RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
            Require(alerts.Titles.Count == 0, "Illustration acknowledgement displayed an unexpected error.");
            ui.AssertHealthy();
            Console.WriteLine($"PASS illustration: delayed acknowledgement, responsive full text/export, one image, EPUB and cold persistence; initial-read-failure={transientReadBeforeCreation}, lost-result-read={lostResultAfterCreation}, existing-admission={existingRemoteAdmission}");
        });
    }
}

public class OriginIllustrationAcceptanceAccount : OriginSuccessorAccount, IAndroidOriginSceneTransport
{
    public bool Accepted;
    public int EarlySceneReads, ImageRequests, SceneReads;
    public bool FailInitialSceneRead, LoseCreatedSceneRead, ExistingRemoteAdmission;
    private bool _persisted;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
    public Task<AndroidOriginSceneResult> ReadSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, CancellationToken ct = default)
    {
        SceneReads++;
        if (!Accepted) { EarlySceneReads++; return Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Conflict)); }
        if (ExistingRemoteAdmission && SceneReads == 1)
        {
            _persisted = true;
            return Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Available, "uncertain"));
        }
        if (FailInitialSceneRead && SceneReads == 1 || LoseCreatedSceneRead && SceneReads == 2)
            return Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Unavailable,
                RetryableReadFailure: true));
        if (LoseCreatedSceneRead && SceneReads == 3 || ExistingRemoteAdmission && SceneReads == 2)
            return Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.NotFound));
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
