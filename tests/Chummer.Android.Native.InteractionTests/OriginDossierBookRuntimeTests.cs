using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Presentation.OriginBooks;
using Chummer.Run.Contracts.Community;
using Microsoft.Maui.Controls;

internal static class OriginDossierBookRuntimeTests
{
    public static async Task RunStoryFlowAsync()
    {
        RunAuthoringSource();
        RunOpeningStoryDetailsStorage();
        RunStoryLanguageStorage();
        await RunLocalBookAdoptionStorageAsync();
        await RunChapterRefinementStorageAsync();
        await RunChapterRefinementPageAsync();
        await RunStoryLanguagePickerAsync();
        await RunOptionalOpeningDetailsAsync();
        await RunReadBeforeNextChoiceAsync();
        await RunCitySuggestionsAsync();
    }

    private static readonly OwnerContextStamp TestOwner = new(OwnerScope.LocalSingleUser, "origin-test-owner", 0);

    private static async Task RunLocalBookAdoptionStorageAsync()
    {
        string directory = Directory.CreateTempSubdirectory("chummer-local-book-adoption-").FullName;
        try
        {
            const string account = "adoption-account-a";
            var authority = new DecisionAuthority(2);
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var prepared = service.Prepare(service.Start("workspace-1").Value!, "choice-1").Value!;
            var accepted = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "adoption-first", true).Value!;
            var checkpoint = service.Prepare(accepted.Checkpoint, "choice-2").Value!;
            string local = checkpoint.OwnerId;
            var chapter = checkpoint.Projection.VisibleChapters.Single();
            var readings = new OriginBookReadingStore(directory);
            var empty = readings.Load(local, checkpoint.WorkspaceId);
            var profile = new OriginStoryProfile(Gender: "other", Pronouns: "they/them") { StoryLanguage = "de-DE" };
            var source = profile.Apply(OriginBookAuthoringSource.Create(checkpoint.Projection, chapter));
            string request = OriginChapterSourceIdentity.RequestId(source);
            string prose = string.Join("\n\n", Enumerable.Range(1, 35).Select(i =>
                $"Absatz {i}: Die Lichter der Straße begleiteten ihren langen Heimweg. 🌧️"));
            var stored = readings.Save(empty, empty with
            {
                StoryProfile = profile,
                IllustrationPolicy = OriginBookReadingState.AutomaticIllustrations,
                PendingRefinement = new(checkpoint.PendingPreview!.PreviewDigest, checkpoint.Projection.CurrentTurn.SeedDigest,
                    "choice-2", 1, new("Path 2", TurningPoint: "A letter arrives late")),
                Chapters = [new(chapter.ChapterId, OriginBookProseDraft.Create(chapter, "de-DE", request, Digest("selected"), prose),
                    OriginBookProseDraft.Create(chapter, "de-DE", request, Digest("pending"), "Private alternative ending"))
                    { AuthoringSource = source }]
            }, () => true, default);
            var sceneStore = new OriginBookSceneStore(directory);
            var book = new RetainedOriginBook(checkpoint.Projection, stored);
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
            var scenes = sceneStore.Save(sceneStore.Load(local, checkpoint.WorkspaceId), new(local, checkpoint.WorkspaceId,
                [OriginBookScene.ForChapter(book, chapter, "Rain over the street", png)]), () => true, default);
            var timeline = new FileOriginDossierDraftTimelineStore(directory);
            await timeline.SaveAsync(checkpoint);
            Require(OriginAdoptionFiles.Digest(timeline.ReadForAdoption(local, checkpoint.WorkspaceId))
                == OriginAdoptionFiles.Digest(checkpoint), "The pending local chapter checkpoint was lost.");

            readings.AdoptLocalEdition(account, stored.Workspace, stored.Digest, () => true, default);
            // Simulate termination before the second private file is placed.
            new OriginBookReadingStore(directory).AdoptLocalEdition(account, stored.Workspace, stored.Digest, () => true, default);
            new OriginBookSceneStore(directory).AdoptLocalScenes(account, stored.Workspace, scenes.Digest, () => true, default);
            var cold = new OriginBookReadingStore(directory).Load(account, stored.Workspace);
            var coldScenes = new OriginBookSceneStore(directory).Load(account, stored.Workspace);
            Require(cold.Digest == (stored with { Owner = account }).Digest
                && coldScenes.Digest == new OriginBookScenes(account, stored.Workspace, scenes.Scenes).Digest
                && readings.Load(local, stored.Workspace).Digest == stored.Digest
                && sceneStore.Load(local, stored.Workspace).Digest == scenes.Digest,
                "Adoption changed retained prose, image bytes, preferences, pending wishes or the recovery originals.");
            var adopted = new RetainedOriginBook(checkpoint.Projection, cold, coldScenes);
            Require(adopted.ChapterText(chapter) == prose
                && OriginChapterSourceIdentity.RequestId(adopted.AuthoringSource(chapter)) == request,
                "Account adoption changed the paid request identity or substituted a summary.");
            using (var archive = new ZipArchive(new MemoryStream(OriginBookEpub.Create(adopted,
                AndroidSurfaceStrings.Resolve("de-DE"))), ZipArchiveMode.Read))
            {
                using var text = archive.GetEntry("EPUB/chapter-1.xhtml")!.Open();
                XNamespace html = "http://www.w3.org/1999/xhtml";
                var page = XDocument.Load(text);
                Require(string.Join("\n\n", page.Descendants(html + "p").Select(p => p.Value)) == prose
                    && page.Descendants(html + "img").Single().Attribute("src")?.Value == "images/scene-1.png",
                    "The adopted EPUB lost full chapter text or its offline illustration.");
                using var image = archive.GetEntry("EPUB/images/scene-1.png")!.Open();
                using var bytes = new MemoryStream(); image.CopyTo(bytes);
                Require(bytes.ToArray().SequenceEqual(png), "The adopted EPUB changed image bytes.");
            }

            void Reject(Action action, string message)
            {
                bool rejected = false;
                try { action(); }
                catch (Exception error) when (error is InvalidOperationException or InvalidDataException or OperationCanceledException) { rejected = true; }
                Require(rejected, message);
            }
            Reject(() => readings.AdoptLocalEdition("adoption-account-b", stored.Workspace, stored.Digest, () => false, default),
                "A retired owner created another account edition.");
            Reject(() => sceneStore.AdoptLocalScenes("adoption-account-b", stored.Workspace, scenes.Digest, () => true, new(true)),
                "Canceled adoption copied illustrations.");
            Reject(() => readings.AdoptLocalEdition(account, stored.Workspace, Digest("stale"), () => true, default),
                "A stale local source was treated as recovered.");
            var other = readings.Load("adoption-account-b", stored.Workspace);
            readings.Save(other, other with { StoryProfile = new(Tone: "hopeful") }, () => true, default);
            Reject(() => readings.AdoptLocalEdition(other.Owner, stored.Workspace, stored.Digest, () => true, default),
                "Different existing account data was overwritten.");
            Require(readings.Load(other.Owner, stored.Workspace).StoryProfile?.Tone == "hopeful"
                && sceneStore.Load(other.Owner, stored.Workspace).Scenes.Count == 0,
                "Rejected adoption damaged another account's state.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("PASS local book adoption storage: full prose, images, exact paid identity, preferences/refinements, partial recovery, EPUB and conflicts");
    }

    private static async Task RunChapterRefinementStorageAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-chapter-refinement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var authority = new DecisionAuthority(2);
            var timeline = new FileOriginDossierDraftTimelineStore(directory);
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var runtime = new OriginDossierLifeModulePhoneRuntime(new TestOrigin(service), timeline);
            var store = new OriginBookReadingStore(directory);
            await runtime.OpenAsync(TestOwner, "workspace-1");
            var prepared = await runtime.PrepareAsync(TestOwner, "workspace-1", "choice-1");
            var checkpoint = prepared.StoryCheckpoint!;
            var initial = store.Load(checkpoint.OwnerId, checkpoint.WorkspaceId);
            var brief = new OriginChapterRefinement("Path 1", "Follow an old friend", "An unexpected mentor", "A second chance");
            var pending = new OriginPendingChapterRefinement(checkpoint.PendingPreview!.PreviewDigest,
                checkpoint.Projection.CurrentTurn.SeedDigest, "choice-1", 0, brief);
            store.Save(initial, initial with { PendingRefinement = pending }, () => true, default);
            bool fail = true;
            LifeModuleOriginDossierInteractionAdvance? accepted = null;
            Task Bind(LifeModuleOriginDossierDraftCheckpoint before, LifeModuleOriginDossierInteractionAdvance advance)
            {
                accepted = advance;
                if (fail) throw new IOException("Synthetic interrupted story save after rules commit");
                var saved = store.Load(before.OwnerId, before.WorkspaceId);
                store.BindRefinement(saved, before, advance, () => true);
                return Task.CompletedTask;
            }
            try
            {
                await runtime.ConfirmAsync(TestOwner, "workspace-1", "choice-1", pending.PreviewDigest, retainChapterRefinement: Bind);
                throw new Exception("Expected synthetic story save failure.");
            }
            catch (IOException) { }
            Require(authority.MutationCount == 1 && store.Load(initial.Owner, initial.Workspace).PendingRefinement == pending,
                "Interrupted confirmation lost the story wishes or replayed rules.");
            foreach (var wrong in new[] { pending with { ChoiceId = "another-choice" },
                pending with { PreviewDigest = Digest("another-preview") }, pending with { TurnSeedDigest = Digest("another-turn") } })
            {
                bool rejected = false;
                try { store.BindRefinement(store.Load(initial.Owner, initial.Workspace) with { PendingRefinement = wrong },
                    checkpoint, accepted!, () => true); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "A different choice, preview or turn captured this chapter's refinement.");
            }
            var blocked = new RetainedOriginBook(accepted!.Checkpoint.Projection, store.Load(initial.Owner, initial.Workspace));
            bool blockedAuthoring = false;
            try { blocked.AuthoringSource(blocked.Projection.VisibleChapters.Single()); }
            catch (InvalidOperationException) { blockedAuthoring = true; }
            Require(blockedAuthoring, "An unresolved story brief silently dispatched an unrefined chapter.");
            var restored = await runtime.OpenAsync(TestOwner, "workspace-1");
            Require(restored.IsSuccess && restored.StoryCheckpoint?.PendingPreview?.PreviewDigest == pending.PreviewDigest,
                "Cold reopen did not retain the existing idempotent confirmation recovery.");
            fail = false;
            var confirmed = await runtime.ConfirmAsync(TestOwner, "workspace-1", "choice-1", pending.PreviewDigest,
                retainChapterRefinement: Bind);
            var cold = new OriginBookReadingStore(directory).Load(initial.Owner, initial.Workspace);
            Require(confirmed.IsSuccess && authority.MutationCount == 1 && cold.PendingRefinement is null
                && cold.ChapterRefinements?.Single().Brief == brief, "Recovery lost or misbound the chapter refinement.");
            var book = new RetainedOriginBook(confirmed.StoryCheckpoint!.Projection, cold);
            var chapter = book.Projection.VisibleChapters.Single();
            var source = book.AuthoringSource(chapter);
            Require(source.Facts.Last().Text.Contains("Follow an old friend", StringComparison.Ordinal)
                && source.Facts.Last().DecisionId.StartsWith("player-chapter-brief-", StringComparison.Ordinal)
                && source.Facts.Count == OriginBookAuthoringSource.Create(book.Projection, chapter).Facts.Count + 1,
                "Authoring omitted player wishes or impersonated Core facts.");
            cold = store.Save(cold, cold with { Chapters = [new(chapter.ChapterId, null, null) { AuthoringSource = source }] }, () => true, default);
            bool immutable = false;
            try { store.Save(cold, cold with { ChapterRefinements = null }, () => true, default); }
            catch (InvalidOperationException) { immutable = true; }
            Require(immutable, "A confirmed refinement could be deleted to create another paid identity.");
            bool wrongOwner = false;
            try { store.Save(cold, cold with { Owner = "different-owner" }, () => true, default); }
            catch (InvalidDataException) { wrongOwner = true; }
            Require(wrongOwner && store.Load("different-owner", initial.Workspace).ChapterRefinements is null,
                "Story refinements crossed account boundaries.");
            var second = await runtime.PrepareAsync(TestOwner, "workspace-1", "choice-2");
            var next = await runtime.ConfirmAsync(TestOwner, "workspace-1", "choice-2", second.State!.PendingPreviewDigest!);
            var later = new RetainedOriginBook(next.StoryCheckpoint!.Projection, cold);
            Require(OriginChapterSourceIdentity.Digest(later.AuthoringSource(chapter)) == OriginChapterSourceIdentity.Digest(source)
                && !later.AuthoringSource(later.Projection.VisibleChapters.Last()).Facts.Any(f => f.DecisionId.StartsWith("player-chapter-brief-")),
                "Later module choices changed the frozen chapter or inherited another module's wishes.");
            foreach (var invalid in new[] { brief with { Motivation = new string('x', 257) }, brief with { Relationship = "bad\ntext" } })
                Require(!invalid.IsValid, "Unbounded/control text entered a story brief.");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS chapter refinements: exact acceptance, interrupted-save recovery without rules replay, cold reopen, owner isolation, frozen jobs");
    }

    private static async Task RunChapterRefinementPageAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
            var checkpoint = service.Prepare(service.Start("workspace-1").Value!, "choice-1").Value!;
            foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
            foreach (int stage in new[] { LifeModuleJourneyStageOrders.FormativeYears,
                LifeModuleJourneyStageOrders.TeenYears, LifeModuleJourneyStageOrders.FurtherEducation,
                LifeModuleJourneyStageOrders.RealLife })
            {
                int saves = 0, confirmations = 0;
                bool fail = false;
                OriginBookReadingState saved = new(checkpoint.OwnerId, checkpoint.WorkspaceId, []);
                var display = new OriginDossierLifeModulePhoneResult(LifeModuleOriginDossierOutcomes.Success,
                    OriginDossierLifeModuleInteractionProjector.Project(checkpoint) with { Locale = locale, StageOrder = stage }, [],
                    LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                    FoundationSnapshotDigest: "sha256:" + Digest("foundation"), BoundContentDigest: checkpoint.BoundContentDigest,
                    BoundSourceDigest: checkpoint.BoundSourceDigest, BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest,
                    StoryCheckpoint: checkpoint);
                OriginDossierLifeModuleDecisionPage Page() => new(display, locale,
                    (_, _) => throw new Exception("Refinement must not prepare rule choices."),
                    (_, _) => { confirmations++; return Task.FromResult<OriginDossierLifeModulePhoneResult?>(null); },
                    openingDetails: saved, saveChapterRefinement: (expected, reviewed, brief, current) =>
                    {
                        Require(current() && expected == saved && reviewed == checkpoint, "Stale refinement saved.");
                        saves++;
                        if (fail) throw new IOException("Synthetic story save failure");
                        saved = expected with { PendingRefinement = brief is null ? null : new(reviewed.PendingPreview!.PreviewDigest,
                            reviewed.Projection.CurrentTurn.SeedDigest, "choice-1", 0, brief) };
                        return Task.FromResult<OriginBookReadingState?>(saved);
                    });
                var page = Page();
                T Find<T>(string id) where T : Element => Elements(page).OfType<T>().Single(e => e.AutomationId == id);
                Task Click(string id) => ui.BeginAsyncVoid(() => ((IButtonController)Find<Button>(id)).SendClicked());
                Require(!Find<VerticalStackLayout>("origin-chapter-refinement").IsVisible
                    && Elements(page).OfType<Entry>().All(e => !e.IsVisible), "Refinements were mandatory or Custom started visible.");
                await Click("origin-life-confirm");
                Require(confirmations == 1 && saves == 0, "Skipping optional questions saved invented facts or blocked confirmation.");
                ((IButtonController)Find<Button>("origin-chapter-refine")).SendClicked();
                Require(Find<VerticalStackLayout>("origin-chapter-refinement").IsVisible
                    && Elements(page).OfType<Picker>().Count(p => p.Title!.Contains("Path 1", StringComparison.Ordinal)) == 3,
                    "Questions were not generated for the selected module.");
                var motivation = Find<Picker>("origin-chapter-motivation-suggestions");
                var copy = AndroidSurfaceStrings.Resolve(locale);
                string firstMotive = stage switch {
                    LifeModuleJourneyStageOrders.FormativeYears => "curiosity",
                    LifeModuleJourneyStageOrders.FurtherEducation => "ambition",
                    _ => "belong" };
                Require(motivation.Items[1] == copy["Origin.RefineAnswer." + firstMotive]
                    && Find<Picker>("origin-chapter-relationship-suggestions").Items[1]
                        == copy["Origin.RefineAnswer." + (stage == LifeModuleJourneyStageOrders.FormativeYears ? "family" : "friend")]
                    && Find<Picker>("origin-chapter-turning-point-suggestions").Items[1]
                        == copy["Origin.RefineAnswer." + (stage == LifeModuleJourneyStageOrders.FurtherEducation ? "letter" : "loyalty")],
                    "Chapter suggestions did not follow the Core narrative stage order.");
                motivation.SelectedIndex = 1;
                var turning = Find<Picker>("origin-chapter-turning-point-suggestions");
                turning.SelectedIndex = turning.Items.Count - 1;
                Require(Find<Entry>("origin-chapter-turning-point").IsVisible, "Custom did not reveal the answer field.");
                Find<Entry>("origin-chapter-turning-point").Text = "An acceptance letter arrives too late";
                ((IButtonController)Find<Button>("origin-chapter-refine")).SendClicked();
                await Click("origin-life-confirm");
                Require(saves == 1 && confirmations == 2 && saved.PendingRefinement?.Brief.TurningPoint == "An acceptance letter arrives too late",
                    "Collapsed answers were lost before confirmation.");
                page = Page();
                Require(Find<Picker>("origin-chapter-motivation-suggestions").SelectedIndex == 1
                    && Find<Entry>("origin-chapter-turning-point").Text == "An acceptance letter arrives too late",
                    "Reopen lost the pending choices/custom answer.");
                var stale = Find<Entry>("origin-chapter-motivation");
                stale.Text = "Hidden text must not replace the preset";
                var window = new Window(new NavigationPage(page));
                using var alerts = new AfterRunAuthorityHarness.IssuedPageAlerts(page, window);
                fail = true;
                await Click("origin-life-confirm");
                Require(confirmations == 2 && saves == 2 && Find<Button>("origin-life-confirm").IsEnabled,
                    "Failed story save confirmed rules or left the UI stuck.");
                fail = false;
                foreach (var id in new[] { "motivation", "relationship", "turning-point" })
                    Find<Picker>("origin-chapter-" + id + "-suggestions").SelectedIndex = 0;
                await Click("origin-life-confirm");
                Require(confirmations == 3 && saved.PendingRefinement is null, "Clearing answers retained an old brief.");
            }
            ui.AssertHealthy();
        });
        Console.WriteLine("PASS chapter refinement UI: optional/contextual presets, Custom, collapse, save failure, reopen and clear in DE/EN/ES");
    }
    private sealed class TestOrigin(LifeModuleOriginDossierInteractionService inner) : IOwnerBoundLifeModuleOriginService
    {
        public bool IsCurrent(OwnerContextStamp owner) => owner == TestOwner;
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Start(OwnerContextStamp owner, string id) => inner.Start(id);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Restore(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint) => inner.Restore(checkpoint);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Prepare(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint, string choiceId, IReadOnlyDictionary<string, string>? followUpValues = null) => inner.Prepare(checkpoint, choiceId, followUpValues);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> Confirm(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint, string previewDigest, string idempotencyKey, bool explicitlyConfirmed) => inner.Confirm(checkpoint, previewDigest, idempotencyKey, explicitlyConfirmed);
    }
    public static async Task RunAsync()
    {
        string digest = Digest("foundation");
        Require(OriginDossierLifeModulePhoneRuntime.MatchesFoundationDigest("sha256:" + digest, digest),
            "A real Foundation digest cannot bind the Origin budget.");
        foreach (string? invalid in new[] { null, string.Empty, digest, "SHA256:" + digest,
                     "sha256:" + digest.ToUpperInvariant(), "sha256:" + Digest("other"), "sha256:bad" })
            Require(!OriginDossierLifeModulePhoneRuntime.MatchesFoundationDigest(invalid, digest),
                "An invalid or different Foundation digest bound the Origin budget.");
        foreach (string? invalid in new[] { null, string.Empty, "sha256:" + digest, digest.ToUpperInvariant(), "bad" })
            Require(!OriginDossierLifeModulePhoneRuntime.MatchesFoundationDigest("sha256:" + digest, invalid),
                "An invalid Origin digest bound the Foundation budget.");
        Console.WriteLine("PASS Origin book: Foundation digest boundary");
        RunLegacyChapterDisplay();
        RunOpeningSetupDisplay();
        RunFinishedSelectionDisplay();
        RunAuthoringSource();
        RunOpportunityContext();
        RunOpeningStoryDetailsStorage();
        RunStoryLanguageStorage();
        await RunStoryLanguagePickerAsync();
        RunReadingStreamBounds();
        RunLongBookStorage();
        RunIllustratedBookStorage();
        await RunOptionalOpeningDetailsAsync();
        await RunReadBeforeNextChoiceAsync();
        await RunTextOnlyDecisionDisplayAsync();
        await RunStoryChoiceCopyAsync();
        await RunMetatypePageAsync();
        await RunConfirmOffUiContextAsync();
        await RunLiveContinuationPageAsync();
        await RunFinishChoiceOrderingAsync();
        await RunFollowUpPageAsync();
        await RunCitySuggestionsAsync();
        foreach (string scenario in new[] { "terminal", "two-chapters", "cancel-after-commit", "storage-failure", "tampered-pending", "stale-book" })
        {
            string directory = Path.Combine(Path.GetTempPath(), "chummer-origin-book-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var authority = new DecisionAuthority(scenario == "two-chapters" ? 2 : 1);
                var files = new FileOriginDossierDraftTimelineStore(directory);
                var store = new FaultStore(files);
                OriginDossierLifeModulePhoneRuntime Runtime() => new(
                    new TestOrigin(new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority))), store);
                var runtime = Runtime();
                Require((await runtime.OpenAsync(TestOwner, "workspace-1")).IsSuccess, "Initial turn unavailable.");
                var prepared = await runtime.PrepareAsync(TestOwner, "workspace-1", "choice-1");
                Require(prepared.IsSuccess && prepared.State?.PendingPreviewDigest is not null, "Preview missing.");
                if (scenario == "tampered-pending")
                {
                    var checkpoint = (await files.LoadAsync("local-single-user", "workspace-1"))!;
                    await files.SaveAsync(checkpoint with { TimelineChapterDigests = [Digest("invented")] });
                    Require(!(await Runtime().OpenAsync(TestOwner, "workspace-1")).IsSuccess && authority.MutationCount == 0,
                        "A corrupt pending preview was exposed as a recoverable live decision.");
                    Console.WriteLine("PASS Origin book: " + scenario);
                    continue;
                }
                using var cancellation = new CancellationTokenSource();
                if (scenario == "cancel-after-commit") authority.AfterCommit = cancellation.Cancel;
                if (scenario == "storage-failure") store.FailNextSave = true;
                OriginDossierLifeModulePhoneResult confirmed;
                try
                {
                    confirmed = await runtime.ConfirmAsync(TestOwner, "workspace-1", "choice-1",
                        prepared.State!.PendingPreviewDigest!, cancellation.Token);
                    Require(scenario != "storage-failure", "Injected storage error was not exercised.");
                }
                catch (IOException) when (scenario == "storage-failure")
                {
                    // New runtime and disk read: replay only the already accepted command.
                    runtime = Runtime();
                    var recovered = await runtime.OpenAsync(TestOwner, "workspace-1");
                    Require(recovered.IsSuccess && recovered.State?.PendingPreviewDigest == prepared.State!.PendingPreviewDigest,
                        "Lost the idempotent recovery preview after a failed chapter save.");
                    confirmed = await runtime.ConfirmAsync(TestOwner, "workspace-1", "choice-1", recovered.State!.PendingPreviewDigest!);
                }
                Require(confirmed.IsSuccess && authority.MutationCount == 1, "Confirmation changed mechanics more than once.");
                if (scenario == "two-chapters")
                {
                    Require(!confirmed.Completed && confirmed.State?.Timeline.Count == 1, "The live next turn lost its first chapter.");
                    runtime = Runtime();
                    Require((await runtime.OpenAsync(TestOwner, "workspace-1")).State?.Timeline.Count == 1, "The next turn did not reopen.");
                    prepared = await runtime.PrepareAsync(TestOwner, "workspace-1", "choice-2");
                    confirmed = await runtime.ConfirmAsync(TestOwner, "workspace-1", "choice-2", prepared.State!.PendingPreviewDigest!);
                }
                Require(confirmed.Completed && confirmed.StoryCheckpoint?.Projection.VisibleChapters.Count == authority.MutationCount,
                    "The terminal result discarded confirmed chapters.");
                var persisted = await new FileOriginDossierDraftTimelineStore(directory).LoadAsync("local-single-user", "workspace-1");
                Require(persisted?.CheckpointDigest == confirmed.StoryCheckpoint!.CheckpointDigest && persisted.PendingPreview is null,
                    "The complete sealed book was not retained on disk.");
                if (scenario == "stale-book") authority.Current = authority.Current with { SourceDigest = Digest("changed-source") };
                var reopened = await Runtime().OpenAsync(TestOwner, "workspace-1");
                if (scenario == "stale-book")
                    Require(!reopened.IsSuccess && reopened.StoryCheckpoint is null, "A stale book was admitted as live authority.");
                else
                {
                    Require(reopened.IsSuccess && reopened.Completed && reopened.State is null
                        && reopened.StoryCheckpoint!.CheckpointDigest == persisted!.CheckpointDigest,
                        "Terminal book could not reopen without the live-choice projector.");
                    foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
                    {
                        var reader = new OriginDossierBookPage(reopened.StoryCheckpoint!, locale);
                        Require(reader.AutomationId == "origin-life-book", "Reader route missing after locale change.");
                        Require(reader.BackgroundColor == NativeTheme.Paper,
                            "The book's fixed dark text must not inherit a system-dark page background.");
                        Require(!Elements(reader).Any(element => element.AutomationId == "origin-life-module-selection-saved"),
                            "A halted turn was mislabeled as explicitly finished module selection.");
                        var finishedCopy = reopened.StoryCheckpoint! with { Projection = reopened.StoryCheckpoint.Projection with
                        {
                            CurrentTurn = reopened.StoryCheckpoint.Projection.CurrentTurn with
                            { StageId = CharacterCreationLifeModuleStageIds.SelectionFinished }
                        }};
                        var finishedReader = new OriginDossierBookPage(finishedCopy, locale);
                        Require(Elements(finishedReader).OfType<Label>().Any(label =>
                                label.AutomationId == "origin-life-module-selection-saved"
                                && label.Text == AndroidSurfaceStrings.Resolve(locale)["Origin.ModuleSelectionSaved"]),
                            "The reader failed to distinguish saved selection from Career readiness.");
                    }
                }
                Require(await files.LoadAsync("another-owner", "workspace-1") is null, "Story leaked into another owner's storage namespace.");
                Console.WriteLine("PASS Origin book: " + scenario);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    private static void RunOpeningSetupDisplay()
    {
        var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
        var opened = service.Start("workspace-1").Value!;
        var prepared = service.Prepare(opened, "choice-1").Value!;
        var seed = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "setup-display", true).Value!.Checkpoint.Projection;
        var foundation = seed.VisibleChapters.Single() with
        { Title = "Troll · UCAS · Seattle", VisibleMarkdown = "$real was born in $ARCOLOGY." };
        var fixture = seed with
        {
            CurrentTurn = seed.CurrentTurn with { JourneyId = "sr5-life-modules-foundation" },
            VisibleChapters = [foundation],
            AllowedCanonicalFactIds = ["race", "birth", "answer", "future"],
            CanonicalLayer = seed.CanonicalLayer with { Facts = [
                new("race", "accepted-metatype", "Troll", foundation.ThroughAcceptedDecisionId, [], ""),
                new("birth", "accepted-life-module", "UCAS", foundation.ThroughAcceptedDecisionId, [], ""),
                new("answer", "accepted-life-module-answer", "Birthplace: $real <script>", foundation.ThroughAcceptedDecisionId, [], ""),
                new("hidden", "accepted-life-module-answer", "unapproved-secret", foundation.ThroughAcceptedDecisionId, [], ""),
                new("future", "accepted-life-module-answer", "future-school", "later-decision", [], "")
            ] }
        };
        foreach (var (locale, expected) in new[] { ("de-DE", "kein separates KI-Kapitel"),
                     ("en-US", "not a separate AI chapter"), ("es-ES", "no es un capítulo de IA separado") })
        {
            var localized = fixture with { CurrentTurn = fixture.CurrentTurn with { Locale = locale } };
            string original = JsonSerializer.Serialize(localized);
            string text = OriginBookChapterText.Render(localized, foundation);
            Require(text.Contains(expected, StringComparison.Ordinal) && text.Contains("Troll", StringComparison.Ordinal)
                && text.Contains(foundation.Title, StringComparison.Ordinal) && text.Contains("Birthplace: $real <script>", StringComparison.Ordinal)
                && !text.Contains("unapproved-secret", StringComparison.Ordinal) && !text.Contains("future-school", StringComparison.Ordinal)
                && !text.Contains("$ARCOLOGY", StringComparison.Ordinal),
                "Opening setup looks like a missing AI chapter or includes unconfirmed/later biography.");
            var retained = new RetainedOriginBook(localized);
            Require(retained.ChapterText(foundation) == text && !retained.CanOpenAuthoring(foundation)
                && !retained.HasReadCurrentStory, "A setup summary grants authoring or story-reading authority.");
            var copy = AndroidSurfaceStrings.Resolve(locale);
            string html = retained.ToHtml(copy);
            Require(!html.Contains(System.Net.WebUtility.HtmlEncode(text), StringComparison.Ordinal)
                && retained.ReadableChapter(foundation) is null && !retained.HasExportableChapters,
                "A setup summary was passed off as a full book chapter.");
            VerifyNoProseExport(retained, locale);
            var page = new OriginDossierBookPage(opened with { Projection = localized }, "en-US");
            Require(Elements(page).OfType<Label>().Single(l => l.AutomationId == "origin-life-book-chapter-1").Text == text,
                "Creation and retained readers disagree about the setup.");
            var authored = OriginBookProseDraft.Create(foundation, locale, Digest("setup-job"), Digest("setup-provider"), "Previously accepted opening prose.");
            var selected = new RetainedOriginBook(localized, new("local-single-user", "workspace-1", [new(foundation.ChapterId, authored, null)]));
            Require(selected.ChapterText(foundation) == authored.Text, "Setup summary replaced an accepted reading edition.");
            VerifyEpub(selected, authored.Text, locale);
            var cold = JsonSerializer.Deserialize<OriginStoryArcSeed>(original)!;
            Require(new RetainedOriginBook(cold).ChapterText(cold.VisibleChapters.Single()) == text
                && JsonSerializer.Serialize(localized) == original, "Display changed the sealed story or cannot reopen.");

            // Non-template or authored source remains verbatim, even with dollar expressions.
            foreach (var chapter in new[] {
                         foundation with { VisibleMarkdown = "An already written beginning." },
                         foundation with { PlayerLayerDigest = Digest("player"), VisibleMarkdown = "I kept $real as my nickname." },
                         foundation with { ProviderLayerDigest = Digest("provider"), VisibleMarkdown = "A story about $real." } })
                Require(OriginBookChapterText.Render(localized with { VisibleChapters = [chapter] }, chapter) == chapter.VisibleMarkdown,
                    "Opening display replaced authored or non-template source prose.");
            var otherJourney = localized with { CurrentTurn = localized.CurrentTurn with { JourneyId = "sr6-life-path" } };
            Require(!OriginBookChapterText.Render(otherJourney, foundation).Contains(expected, StringComparison.Ordinal),
                "SR5 opening semantics leaked into another journey.");
        }
        Console.WriteLine("PASS Origin book: confirmed opening setup, immutable authored editions, reader/HTML/EPUB parity");
    }

    private static void RunLegacyChapterDisplay()
    {
        var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
        var opened = service.Start("workspace-1").Value!;
        var prepared = service.Prepare(opened, "choice-1").Value!;
        var book = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "display-fixture", true).Value!.Checkpoint.Projection;
        var original = book.VisibleChapters.Single();
        Require(OriginBookChapterText.Render(book, original) == original.VisibleMarkdown,
            "Complete saved prose was rewritten merely for display.");
        var legacy = original with { VisibleMarkdown = "$real grew up $LUCK in $ARCOLOGY. $CUSTOM_123" };
        // Synthetic display fixtures, never written into a Core ledger. The
        // production caller obtains the validated book from Core before rendering.
        var fixture = book with
        {
            VisibleChapters = [legacy],
            CanonicalLayer = book.CanonicalLayer with { Facts = [
                new("answer", "accepted-life-module-answer", "Arcology: Renraku", original.ThroughAcceptedDecisionId, [], ""),
                new("future", "accepted-life-module-answer", "future-school-must-not-appear", "later-decision", [], "")
            ] }
        };
        foreach (var (locale, expected) in new[] { ("de-DE", "Vorgeschichte steht fest"), ("en-US", "background is settled"), ("es-ES", "historia de Neon") })
        {
            var localized = fixture with { CurrentTurn = fixture.CurrentTurn with { Locale = locale } };
            string before = JsonSerializer.Serialize(localized);
            string text = OriginBookChapterText.Render(localized, legacy);
            Require(text.Contains(expected, StringComparison.Ordinal) && text.Contains("Renraku", StringComparison.Ordinal)
                && !text.Contains('$') && !text.Contains("future-school", StringComparison.Ordinal),
                "Legacy chapter retained template markers, wrong language or future biography.");
            Require(JsonSerializer.Serialize(localized) == before, "Display normalization changed the accepted book/digests.");
            var checkpoint = opened with { Projection = localized };
            var reader = new OriginDossierBookPage(checkpoint, "es-ES");
            Require(Elements(reader).OfType<Label>().Single(label => label.AutomationId == "origin-life-book-chapter-1").Text == text,
                "Creation reader did not retain the story language or shared display text.");
            var retained = new RetainedOriginBook(localized);
            Require(retained.ChapterText(retained.Chapters.Single()) == text
                && retained.ReadableChapter(legacy) is null
                && !retained.ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains(System.Net.WebUtility.HtmlEncode(text), StringComparison.Ordinal),
                "A legacy decision summary leaked into full-book reading/export.");
        }
        var dollarAnswer = fixture with { CanonicalLayer = fixture.CanonicalLayer with { Facts = [
            new("literal", "accepted-life-module-answer", "Nickname: $real <script>", original.ThroughAcceptedDecisionId, [], "")
        ] } };
        Require(OriginBookChapterText.Render(dollarAnswer, legacy).Contains("Nickname: $real <script>", StringComparison.Ordinal)
            && !new RetainedOriginBook(dollarAnswer).ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains("<script>", StringComparison.Ordinal),
            "Literal player text was recursively interpreted as a macro or escaped unsafely.");
        bool denied = false;
        try { OriginBookChapterText.Render(book, legacy); }
        catch (InvalidOperationException) { denied = true; }
        Require(denied, "A chapter outside the displayed book was accepted.");
        Console.WriteLine("PASS Origin book: legacy templates → decision-scoped DE/EN/ES display, immutable history and matching safe export");
    }

    private static void RunFinishedSelectionDisplay()
    {
        var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
        var opened = service.Start("workspace-1").Value!;
        var prepared = service.Prepare(opened, "choice-1").Value!;
        var book = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "finish-display", true).Value!.Checkpoint.Projection;
        var chapter = book.VisibleChapters.Single() with
        {
            Title = "Heading is not completion authority",
            VisibleMarkdown = "Choose the next part of your background.\n\n**Finish module selection**\n\nRule effects and the transition to Career still need review."
        };
        var finish = new OriginCanonicalNarrativeFact("finished", "accepted-module-selection-finish",
            "Historic confirmation text", chapter.ThroughAcceptedDecisionId, [], "");
        // Synthetic display fixtures only. Production gets this fact and the
        // unchanged chapter/digest from the Core-validated retained ledger.
        var fixture = book with { VisibleChapters = [chapter], CanonicalLayer = book.CanonicalLayer with { Facts = [finish] } };
        foreach (var (locale, expected) in new[] {
                     ("de-DE", "Deine Modulauswahl ist abgeschlossen."),
                     ("en-US", "The Life Modules selection is complete."),
                     ("es-ES", "La selección de módulos de vida está completa.") })
        {
            var localized = fixture with { CurrentTurn = fixture.CurrentTurn with { Locale = locale } };
            string original = JsonSerializer.Serialize(localized);
            string text = OriginBookChapterText.Render(localized, chapter);
            Require(text.StartsWith(expected, StringComparison.Ordinal) && text.Contains(localized.CurrentTurn.RunnerDisplayName, StringComparison.Ordinal)
                && !text.Contains("**", StringComparison.Ordinal) && !text.Contains("Career", StringComparison.Ordinal),
                "The finished chapter still instructs a Career runner to create again, or declares Career from selection alone.");
            var reader = new OriginDossierBookPage(opened with { Projection = localized }, "es-ES");
            Require(Elements(reader).OfType<Label>().Single(label => label.AutomationId == "origin-life-book-chapter-1").Text == text,
                "The Creation book did not use the story language's saved-selection display.");
            var retained = new RetainedOriginBook(localized);
            Require(retained.ChapterText(chapter) == text
                && retained.ReadableChapter(chapter) is null
                && !retained.ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains(System.Net.WebUtility.HtmlEncode(text), StringComparison.Ordinal),
                "Finishing module selection was confused with a complete generated chapter.");
            Require(JsonSerializer.Serialize(localized) == original, "Rendering rewrote the archived chapter, fact or digest.");

            var prose = OriginBookProseDraft.Create(chapter, locale, "reviewed-finish", new string('b', 64),
                "I chose my own path. <script>literal prose</script>");
            var selected = new RetainedOriginBook(localized, new("local-single-user", "workspace-1", [new(chapter.ChapterId, prose, null)]));
            Require(selected.ChapterText(chapter) == prose.Text
                && selected.ToHtml(AndroidSurfaceStrings.Resolve("de")).Contains(System.Net.WebUtility.HtmlEncode(prose.Text), StringComparison.Ordinal),
                "Finish display rewrote reader-approved narration or exported executable markup.");
            VerifyEpub(selected, prose.Text, locale);
            var pending = new RetainedOriginBook(localized, new("local-single-user", "workspace-1", [new(chapter.ChapterId, null, prose)]));
            Require(pending.ChapterText(chapter) == text, "Unconfirmed narration replaced the completed-selection display.");
            Require(pending.ReadableChapter(chapter)?.Text == prose.Text && !pending.HasReadCurrentStory,
                "The full returned chapter is not directly readable, or reading availability silently acknowledged it.");
            VerifyNoProseExport(pending, locale);
            var damaged = new RetainedOriginBook(localized, new("local-single-user", "workspace-1",
                [new(chapter.ChapterId, prose with { Text = "tampered" }, null)]));
            Require(damaged.ReadableChapter(chapter) is null && !damaged.HasExportableChapters,
                "Damaged full text became reader/export authority.");
            if (locale == "de-DE")
            {
                VerifyLongEpub(localized, chapter);
                VerifyIllustratedEpub(selected, chapter);
                WriteIllustratedPreview(localized, chapter);
            }
        }
        var laterFinish = fixture with { CanonicalLayer = fixture.CanonicalLayer with { Facts = [finish with { AcceptedDecisionId = "later" }] } };
        Require(OriginBookChapterText.Render(laterFinish, chapter) == chapter.VisibleMarkdown,
            "A later finish fact rewrote an earlier chapter.");
        var mereTitle = fixture with { CanonicalLayer = fixture.CanonicalLayer with { Facts = [finish with { FactKind = "accepted-life-module-answer" }] } };
        Require(OriginBookChapterText.Render(mereTitle, chapter) == chapter.VisibleMarkdown,
            "Finish-looking text was treated as completion authority.");
        foreach (bool playerLayer in new[] { true, false })
        {
            var authored = playerLayer ? chapter with { PlayerLayerDigest = new string('c', 64) }
                : chapter with { ProviderLayerDigest = new string('c', 64) };
            Require(OriginBookChapterText.Render(fixture with { VisibleChapters = [authored] }, authored) == authored.VisibleMarkdown,
                "The canonical-only finish formatter rewrote an authored layer.");
        }
        Console.WriteLine("PASS Origin book: finished selection display in DE/EN/ES, immutable history, matching readers/export and retained approved prose");
    }

    private static void VerifyIllustratedEpub(RetainedOriginBook book, OriginNarrativeChapterProjection chapter)
    {
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
        var picture = new OriginBookEpub.Illustration(chapter.ChapterId, Digest(book.ChapterText(chapter)),
            "A quiet forest scene <not markup> & a spiral of stones", png);
        var copy = AndroidSurfaceStrings.Resolve(book.Locale);
        byte[] exported = OriginBookEpub.Create(book, copy, [picture]);
        using var archive = new ZipArchive(new MemoryStream(exported), ZipArchiveMode.Read);
        using var imageStream = archive.GetEntry("EPUB/images/scene-1.png")!.Open();
        using var image = new MemoryStream(); imageStream.CopyTo(image);
        Require(image.ToArray().SequenceEqual(png), "EPUB did not retain the exact offline image bytes.");
        using var chapterStream = archive.GetEntry("EPUB/chapter-1.xhtml")!.Open();
        var page = XDocument.Load(chapterStream);
        XNamespace html = "http://www.w3.org/1999/xhtml";
        var element = page.Descendants(html + "img").Single();
        Require(element.Attribute("src")?.Value == "images/scene-1.png"
            && element.Attribute("alt")?.Value == picture.AltText
            && string.Join("\n\n", page.Descendants(html + "p").Select(p => p.Value)) == book.ChapterText(chapter),
            "An illustration used a remote URL, lost its accessible description or changed prose.");
        using var packageStream = archive.GetEntry("EPUB/package.opf")!.Open();
        XNamespace opf = "http://www.idpf.org/2007/opf";
        Require(XDocument.Load(packageStream).Descendants(opf + "item").Any(item =>
                item.Attribute("href")?.Value == "images/scene-1.png" && item.Attribute("media-type")?.Value == "image/png"),
            "The offline image is not declared in the EPUB manifest.");
        void Reject(OriginBookEpub.Illustration invalid)
        {
            bool rejected = false;
            try { OriginBookEpub.Create(book, copy, [invalid]); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "An invalid, oversized or wrong-chapter illustration was exported.");
        }
        Reject(picture with { TextDigest = Digest("unselected or stale story") });
        Reject(picture with { ChapterId = "another-book-chapter" });
        Reject(picture with { Bytes = Encoding.UTF8.GetBytes("<svg onload='bad()'/>") });
        Reject(picture with { Bytes = new byte[4 * 1024 * 1024 + 1] });
        Reject(picture with { AltText = "" });
        byte[] oversizedDimensions = png.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(oversizedDimensions.AsSpan(16), 50000);
        Reject(picture with { Bytes = oversizedDimensions });
        bool tooManyRejected = false;
        try { OriginBookEpub.Create(book, copy, Enumerable.Repeat(picture, 129).ToArray()); }
        catch (InvalidDataException) { tooManyRejected = true; }
        Require(tooManyRejected, "The EPUB illustration-count bound was ignored.");
        bool duplicateRejected = false;
        try { OriginBookEpub.Create(book, copy, [picture, picture]); }
        catch (InvalidDataException) { duplicateRejected = true; }
        Require(duplicateRejected, "Duplicate illustrations for one chapter were exported.");
        VerifySceneStore(book, chapter, png);
        Console.WriteLine("PASS Origin EPUB: exact embedded raster, alt text, selected-chapter binding and unsafe/oversized image rejection");
    }

    public static void RunIllustratedBookStorage()
    {
        string directory = Directory.CreateTempSubdirectory("chummer-illustrated-book-").FullName;
        try
        {
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
            var prepared = service.Prepare(service.Start("workspace-1").Value!, "choice-1").Value!;
            var projection = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest,
                "illustrated-book-fixture", true).Value!.Checkpoint.Projection;
            var seed = projection.VisibleChapters.Single();
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
            foreach (int count in new[] { 8, 9, 128 })
            {
                var chapters = Enumerable.Range(1, count).Select(i => seed with
                {
                    ChapterId = "illustrated-chapter-" + i, ChapterDigest = Digest("illustrated-chapter-" + i),
                    ThroughAcceptedDecisionId = "decision-" + i, Sequence = i,
                    Title = "Straße " + i + " — 🌧️", VisibleMarkdown = "Never export this decision summary."
                }).ToArray();
                string Prose(int i) => string.Join("\n\n", Enumerable.Range(1, 30).Select(p =>
                    $"Kapitel {i}, Absatz {p}: Über den Dächern 🌧️ – <Bilder & Erinnerungen> blieben ihre eigenen."));
                var readingStore = new OriginBookReadingStore(directory);
                var emptyReadings = readingStore.Load("owner-a", "book-" + count);
                var readings = readingStore.Save(emptyReadings, emptyReadings with
                {
                    Chapters = chapters.Select((c, i) => new OriginBookReadingChapter(c.ChapterId,
                        OriginBookProseDraft.Create(c, "de-DE", "selected-" + i, Digest("selected"), Prose(i)),
                        OriginBookProseDraft.Create(c, "de-DE", "pending-" + i, Digest("pending"), "PRIVATE PENDING TEXT"))).ToArray()
                }, () => true, default);
                var projected = projection with { VisibleChapters = chapters,
                    CurrentTurn = projection.CurrentTurn with { Locale = "de-DE" },
                    CanonicalLayer = projection.CanonicalLayer with
                        { AcceptedDecisionIds = chapters.Select(c => c.ThroughAcceptedDecisionId).ToArray() } };
                var book = new RetainedOriginBook(projected, readings);
                var sceneStore = new OriginBookSceneStore(directory);
                var emptyScenes = sceneStore.Load(readings.Owner, readings.Workspace);
                // Exercise JSON escaping well beyond the former 64 KiB manifest cap.
                var scenes = new OriginBookScenes(readings.Owner, readings.Workspace,
                    chapters.Select(c => OriginBookScene.ForChapter(book, c, new string('Ü', 1024), png)));
                sceneStore.Save(emptyScenes, scenes, () => true, default);
                var coldScenes = new OriginBookSceneStore(directory).Load(readings.Owner, readings.Workspace);
                var coldReadings = new OriginBookReadingStore(directory).Load(readings.Owner, readings.Workspace);
                var coldBook = new RetainedOriginBook(projected, coldReadings, coldScenes);
                Require(coldScenes.Digest == scenes.Digest && coldScenes.Scenes.Count == count
                    && coldReadings.Digest == readings.Digest
                    && sceneStore.Load("owner-b", readings.Workspace).Scenes.Count == 0,
                    "Cold illustrated-book read lost data or crossed owners.");
                using var archive = new ZipArchive(new MemoryStream(OriginBookEpub.Create(coldBook,
                    AndroidSurfaceStrings.Resolve("de-DE"))), ZipArchiveMode.Read);
                XNamespace html = "http://www.w3.org/1999/xhtml";
                XNamespace opf = "http://www.idpf.org/2007/opf";
                XDocument Read(string name) { using var s = archive.GetEntry("EPUB/" + name)!.Open(); return XDocument.Load(s); }
                Require(Read("nav.xhtml").Descendants(html + "a").Count() == count
                    && Read("package.opf").Descendants(opf + "itemref").Count() == count + 1
                    && archive.Entries.Count(e => e.FullName.StartsWith("EPUB/images/", StringComparison.Ordinal)) == count,
                    "The full illustrated book lost chapters, images or reading order.");
                for (int i = 0; i < count; i++)
                {
                    var page = Read($"chapter-{i + 1}.xhtml");
                    Require(string.Join("\n\n", page.Descendants(html + "p").Select(p => p.Value)) == Prose(i)
                        && page.Descendants(html + "img").Single().Attribute("alt")?.Value == new string('Ü', 1024),
                        "A chapter was truncated, substituted or lost its image description.");
                    using var input = archive.GetEntry($"EPUB/images/scene-{i + 1}.png")!.Open();
                    using var output = new MemoryStream(); input.CopyTo(output);
                    Require(output.ToArray().SequenceEqual(png), "The retained image bytes changed during export.");
                }
                if (count == 128)
                {
                    File.WriteAllText(Path.Combine(directory, "restart-fixture.json"),
                        JsonSerializer.Serialize(new IllustratedRestartFixture(projected, readings.Digest, scenes.Digest)));
                    var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false };
                    start.ArgumentList.Add(typeof(OriginDossierBookRuntimeTests).Assembly.Location);
                    start.ArgumentList.Add("--origin-illustrated-book-reopen");
                    start.ArgumentList.Add(directory);
                    using var process = System.Diagnostics.Process.Start(start)!;
                    if (!process.WaitForExit(30000))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                        throw new InvalidOperationException("Separate-process illustrated book reopen did not finish.");
                    }
                    Require(process.ExitCode == 0, "A separate process could not reopen and export the illustrated book.");
                    var extra = chapters[0] with { ChapterId = "chapter-129", ChapterDigest = Digest("129") };
                    var extraBook = new RetainedOriginBook(projected with { VisibleChapters = [extra] });
                    var extraScene = OriginBookScene.ForChapter(extraBook, extra, "Extra", png);
                    bool rejected = false;
                    try { sceneStore.Save(coldScenes, new(readings.Owner, readings.Workspace,
                        coldScenes.Scenes.Append(extraScene)), () => true, default); }
                    catch (InvalidDataException) { rejected = true; }
                    Require(rejected && sceneStore.Load(readings.Owner, readings.Workspace).Digest == scenes.Digest,
                        "An excessive scene count replaced a valid full book.");
                }
                if (count == 9)
                {
                    VerifyIllustratedEpub(new RetainedOriginBook(
                        projected with { VisibleChapters = [chapters[0]] }, readings), chapters[0]);
                    // A bounded raster-envelope fixture exercises aggregate byte
                    // admission, not a claim that a decoder accepts padded PNGs.
                    byte[] large = new byte[4 * 1024 * 1024];
                    png.CopyTo(large, 0); png.AsSpan(png.Length - 12).CopyTo(large.AsSpan(large.Length - 12));
                    var oversized = new OriginBookScenes(readings.Owner, readings.Workspace,
                        chapters.Take(5).Select(c => OriginBookScene.ForChapter(book, c, "Size fixture", large)));
                    bool storageRejected = false, exportRejected = false;
                    try { sceneStore.Save(coldScenes, oversized, () => true, default); }
                    catch (InvalidDataException) { storageRejected = true; }
                    try { OriginBookEpub.Create(book, AndroidSurfaceStrings.Resolve("de-DE"),
                        oversized.Scenes.Select(s => s.Export()).ToArray()); }
                    catch (InvalidDataException) { exportRejected = true; }
                    Require(storageRejected && exportRejected
                        && sceneStore.Load(readings.Owner, readings.Workspace).Digest == scenes.Digest,
                        "The unchanged 16 MiB aggregate image limit was bypassed or damaged a saved book.");
                    var unselected = new RetainedOriginBook(projected,
                        readings with { Chapters = readings.Chapters.Select(r => r with { Selected = null }).ToArray() });
                    bool pendingRejected = false;
                    try { OriginBookEpub.Create(unselected, AndroidSurfaceStrings.Resolve("de-DE"),
                        [OriginBookScene.ForChapter(unselected, chapters[0], "Unselected fixture", png).Export()]); }
                    catch (InvalidDataException) { pendingRejected = true; }
                    Require(pendingRejected, "An illustration for an unselected chapter bypassed export admission.");
                }
                Console.WriteLine($"PASS Origin illustrated book: {count} complete chapters/images, escaped metadata, cold read, owner isolation and EPUB");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        WriteIllustratedDeviceFixture();
    }

    private sealed record IllustratedRestartFixture(OriginStoryArcSeed Projection, string ReadingDigest, string SceneDigest);

    private static void WriteIllustratedDeviceFixture()
    {
        string? directory = Environment.GetEnvironmentVariable("CHUMMER_ILLUSTRATED_DEVICE_FIXTURE");
        if (string.IsNullOrEmpty(directory)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "checkpoint.json")));
        Require(document.RootElement.GetProperty("ownerId").GetString() == "local-single-user",
            "The illustrated device fixture must be a synthetic local owner, never an account copy.");
        var projection = document.RootElement.GetProperty("projection").Deserialize<OriginStoryArcSeed>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var chapter = projection.VisibleChapters.Single();
        var readings = new OriginBookReadingStore(directory);
        var empty = readings.Load("local-single-user", projection.CurrentTurn.WorkspaceId);
        Require(empty.Chapters.Count == 0, "Do not overwrite a retained illustrated fixture.");
        var selected = OriginBookProseDraft.Create(chapter, projection.CurrentTurn.Locale,
            "synthetic-illustrated-capacity-fixture", Digest("synthetic"),
            "SYNTHETIC ILLUSTRATED STORAGE TEST — not an AI chapter.\n\n"
            + "The ninth stored illustration must not prevent reading this complete passage or exporting it.\n\n"
            + "Über den Dächern lag Regen. 🌧️\n\nEND OF SYNTHETIC ILLUSTRATED STORAGE TEST.");
        var saved = readings.Save(empty, empty with { Chapters = [new(chapter.ChapterId, selected, null)] }, () => true, default);
        var book = new RetainedOriginBook(projection, saved);
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
        var scene = OriginBookScene.ForChapter(book, chapter, "Synthetic storage pixel, not a rendered scene", png);
        var scenes = new OriginBookSceneStore(directory);
        var old = scenes.Load(empty.Owner, empty.Workspace);
        Require(old.Scenes.Count == 0, "Do not replace an existing scene fixture.");
        // Extra entries exercise storage only, never invent accepted modules.
        var retained = Enumerable.Range(1, 8).Select(i => OriginBookScene.Create(
            scene.Identity with { ChapterId = "synthetic-storage-only-" + i }, png)).Append(scene);
        scenes.Save(old, new(empty.Owner, empty.Workspace, retained), () => true, default);
        Console.WriteLine("PASS wrote opt-in local device fixture: nine stored scenes, one real chapter, no provider/canonical mutation");
    }

    public static void ReopenIllustratedBook(string directory)
    {
        var fixture = JsonSerializer.Deserialize<IllustratedRestartFixture>(
            File.ReadAllText(Path.Combine(directory, "restart-fixture.json")))!;
        var readings = new OriginBookReadingStore(directory).Load("owner-a", "book-128");
        var scenes = new OriginBookSceneStore(directory).Load("owner-a", "book-128");
        Require(readings.Digest == fixture.ReadingDigest && scenes.Digest == fixture.SceneDigest,
            "A new process did not restore the exact reading/image identities.");
        var book = new RetainedOriginBook(fixture.Projection, readings, scenes);
        using var archive = new ZipArchive(new MemoryStream(OriginBookEpub.Create(book,
            AndroidSurfaceStrings.Resolve("de-DE"))), ZipArchiveMode.Read);
        XNamespace html = "http://www.w3.org/1999/xhtml";
        for (int i = 0; i < 128; i++)
        {
            using var chapter = archive.GetEntry($"EPUB/chapter-{i + 1}.xhtml")!.Open();
            var page = XDocument.Load(chapter);
            Require(string.Join("\n\n", page.Descendants(html + "p").Select(p => p.Value)) == readings.Chapters[i].Selected!.Text
                && page.Descendants(html + "img").Single().Attribute("src")?.Value == $"images/scene-{i + 1}.png",
                "A new process lost the selected full text or its illustration in EPUB.");
        }
        Console.WriteLine("PASS Origin illustrated book: separate-process reopen and full 128-chapter EPUB");
    }

    private static void VerifySceneStore(RetainedOriginBook book, OriginNarrativeChapterProjection chapter, byte[] png)
    {
        string directory = Directory.CreateTempSubdirectory("chummer-origin-scenes-").FullName;
        try
        {
            var store = new OriginBookSceneStore(directory);
            var empty = store.Load("owner-a", "workspace");
            byte[] input = png.ToArray();
            var scene = OriginBookScene.ForChapter(book, chapter, "Forest & stones <scene>", input);
            input[0] = 0;
            var next = new OriginBookScenes(empty.Owner, empty.Workspace, [scene]);
            Require(scene.Export().Bytes.SequenceEqual(png), "Scene custody shares mutable input bytes.");
            byte[] exportedCopy = scene.Export().Bytes; exportedCopy[0] = 0;
            Require(scene.Export().Bytes.SequenceEqual(png), "An export mutated the retained scene.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool rejected = false;
                try { store.Save(empty, next, () => true, cancellation.Token); }
                catch (OperationCanceledException) { rejected = true; }
                Require(rejected && store.Load(empty.Owner, empty.Workspace).Scenes.Count == 0,
                    "Cancellation committed a scene.");
            }
            bool stale = false;
            int checks = 0;
            try { store.Save(empty, next, () => ++checks == 1, default); }
            catch (OperationCanceledException) { stale = true; }
            Require(stale && store.Load(empty.Owner, empty.Workspace).Scenes.Count == 0
                && !Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories).Any(),
                "A changed context committed a scene or left temporary private bytes.");
            store.Save(empty, next, () => true, default);
            var reopened = new OriginBookSceneStore(directory).Load(empty.Owner, empty.Workspace);
            Require(reopened.Digest == next.Digest && reopened.Scenes.Single().Export().Bytes.SequenceEqual(png)
                && store.Load("owner-b", "workspace").Scenes.Count == 0
                && store.Load("owner-a", "other-workspace").Scenes.Count == 0,
                "Cold scene read lost exact bytes or crossed owner/workspace boundaries.");
            var illustrated = new RetainedOriginBook(book.Projection, book.Readings, reopened);
            Require(illustrated.Scene(chapter) is not null && illustrated.ToHtml(AndroidSurfaceStrings.Resolve("en"))
                    .Contains("data:image/png;base64,", StringComparison.Ordinal),
                "Saved scenes were omitted from the offline reader/HTML.");
            using (var epub = new ZipArchive(new MemoryStream(OriginBookEpub.Create(illustrated, AndroidSurfaceStrings.Resolve("en"))), ZipArchiveMode.Read))
                Require(epub.GetEntry("EPUB/images/scene-1.png") is not null,
                    "Normal EPUB export did not include the retained book scene.");
            var changed = new RetainedOriginBook(book.Projection, null, reopened);
            Require(changed.Scene(chapter) is null && changed.SceneExports().Count == 0,
                "An illustration survived a change of selected prose.");
            var changedChapter = chapter with { ChapterDigest = Digest("different canonical chapter") };
            Require(!scene.Matches(new RetainedOriginBook(book.Projection with { VisibleChapters = [changedChapter] }, book.Readings, reopened), changedChapter),
                "Scene binding ignored canonical chapter identity.");
            bool conflict = false;
            try { store.Save(empty, next, () => true, default); }
            catch (InvalidOperationException) { conflict = true; }
            Require(conflict, "A stale scene collection silently overwrote a newer edition.");
            string archive = Directory.EnumerateFiles(Path.Combine(directory, "origin-book-scenes"), "*.zip").Single();
            using (var zip = new ZipArchive(File.Open(archive, FileMode.Open, FileAccess.ReadWrite), ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("scene-0")!;
                using var bytes = entry.Open(); bytes.Position = 0; bytes.WriteByte(0);
            }
            bool corruption = false;
            try { store.Load(empty.Owner, empty.Workspace); }
            catch (InvalidDataException) { corruption = true; }
            Require(corruption, "Tampered scene bytes passed cold readback.");
            Console.WriteLine("PASS Origin scenes: offline restart, exact PNG, text/canon binding, owner isolation, canceled/stale writes and corruption rejection");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // Optional, operator-selected example inputs for visual review, never part
    // of normal tests or a Hub/provider handoff. This does not select app prose.
    private static void WriteIllustratedPreview(OriginStoryArcSeed projection, OriginNarrativeChapterProjection original)
    {
        string? directory = Environment.GetEnvironmentVariable("CHUMMER_ORIGIN_EPUB_PREVIEW_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        string markdown = File.ReadAllText(Path.Combine(directory, "chapter.md"));
        int newline = markdown.IndexOf('\n');
        var chapter = original with { Title = markdown[..newline].TrimStart('#', ' ').Trim() };
        string prose = markdown[(newline + 1)..].Trim();
        var selected = OriginBookProseDraft.Create(chapter, "en-US", "illustrated-layout-test", new string('e', 64), prose);
        var book = new RetainedOriginBook(projection with
        {
            CurrentTurn = projection.CurrentTurn with { Locale = "en-US", RunnerDisplayName = "Grounding — illustrated test excerpt" },
            VisibleChapters = [chapter]
        }, new("synthetic-test-owner", "synthetic-test-workspace", [new(chapter.ChapterId, selected, null)]));
        var picture = new OriginBookEpub.Illustration(chapter.ChapterId, Digest(prose),
            "A spiral of pale and dark stones on damp evergreen forest soil, beside a small rainwater channel.",
            File.ReadAllBytes(Path.Combine(directory, "scene.png")));
        string output = Path.Combine(directory, "origin-illustrated-preview.epub");
        using var file = new FileStream(output, FileMode.CreateNew);
        file.Write(OriginBookEpub.Create(book, AndroidSurfaceStrings.Resolve("en-US"), [picture]));
        Console.WriteLine("PASS Origin EPUB: wrote explicitly labeled synthetic illustrated test excerpt, not a production book");
    }

    private static void VerifyLongEpub(OriginStoryArcSeed projection, OriginNarrativeChapterProjection chapter)
    {
        string prose = string.Join("\n\n", Enumerable.Range(1, 180).Select(i =>
            $"Absatz {i}: Über den Dächern lag warmer Regen. 🌧️ Sie erinnerte sich an ihre Kindheit – an Türen, "
            + "die offen blieben, und an Menschen, die ihr zuhörten. <Bilder & Erinnerungen> blieben ihre eigenen."));
        var second = chapter with { ChapterId = chapter.ChapterId + "-second", Sequence = chapter.Sequence + 1,
            Title = "Eine neue Straße – 🌆", VisibleMarkdown = "Unselected fallback must not replace the chosen story." };
        var selected = OriginBookProseDraft.Create(second, "de-DE", "long-story-fixture", new string('d', 64), prose);
        var first = OriginBookProseDraft.Create(chapter, "de-DE", "first-story-fixture", new string('c', 64),
            "The complete opening, not its decision summary.");
        var book = new RetainedOriginBook(projection with { VisibleChapters = [chapter, second] },
            new("local-single-user", "workspace-1", [new(chapter.ChapterId, first, null), new(second.ChapterId, selected, null)]));
        using var archive = new ZipArchive(new MemoryStream(OriginBookEpub.Create(book, AndroidSurfaceStrings.Resolve("de-DE"))), ZipArchiveMode.Read);
        XDocument Read(string name) { using var stream = archive.GetEntry("EPUB/" + name)!.Open(); return XDocument.Load(stream); }
        XNamespace html = "http://www.w3.org/1999/xhtml";
        XNamespace opf = "http://www.idpf.org/2007/opf";
        var exported = Read("chapter-2.xhtml");
        Require(string.Join("\n\n", exported.Descendants(html + "p").Select(p => p.Value)) == prose
            && exported.Descendants(html + "p").Count() == 180,
            "A long Unicode chapter was truncated, lost paragraph breaks, or interpreted prose as markup.");
        Require(exported.Descendants(html + "h1").Single().Value == second.Title,
            "The EPUB lost Unicode in the chapter title.");
        Require(Read("package.opf").Descendants(opf + "itemref").Select(i => i.Attribute("idref")!.Value)
                .SequenceEqual(new[] { "title", "chapter-1", "chapter-2" })
            && Read("nav.xhtml").Descendants(html + "a").Select(a => a.Attribute("href")!.Value)
                .SequenceEqual(new[] { "chapter-1.xhtml", "chapter-2.xhtml" }),
            "The long book's reading order and contents disagree.");
        Console.WriteLine("PASS Origin EPUB: long story, full Unicode, escaped prose, paragraph breaks and ordered chapters");
    }

    private static void VerifyNoProseExport(RetainedOriginBook book, string locale)
    {
        Require(!book.HasExportableChapters, "An unfinished book enabled export.");
        using var archive = new ZipArchive(new MemoryStream(OriginBookEpub.Create(book,
            AndroidSurfaceStrings.Resolve(locale))), ZipArchiveMode.Read);
        Require(!archive.Entries.Any(e => e.FullName.StartsWith("EPUB/chapter-", StringComparison.Ordinal)),
            "EPUB silently substituted decision drafts for missing full prose.");
    }

    private static void VerifyEpub(RetainedOriginBook book, string expectedText, string locale)
    {
        string original = JsonSerializer.Serialize(book.Projection);
        byte[] bytes = OriginBookEpub.Create(book, AndroidSurfaceStrings.Resolve(locale));
        // Local ZIP header: compression method 0, name "mimetype", no extra.
        Require(BitConverter.ToUInt16(bytes, 8) == 0 && BitConverter.ToUInt16(bytes, 28) == 0,
            "EPUB mimetype is compressed or has a ZIP extra field.");
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        string Read(string name) { using var reader = new StreamReader(archive.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
        Require(archive.Entries.First().FullName == "mimetype" && Read("mimetype") == "application/epub+zip",
            "Not a real EPUB container.");
        XNamespace html = "http://www.w3.org/1999/xhtml";
        XNamespace opf = "http://www.idpf.org/2007/opf";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        var package = XDocument.Parse(Read("EPUB/package.opf"));
        Require(package.Root!.Attribute("version")?.Value == "3.0"
            && package.Descendants(dc + "language").Single().Value == locale,
            "EPUB metadata lost its version or reading language.");
        foreach (var item in package.Descendants(opf + "item"))
            Require(archive.GetEntry("EPUB/" + item.Attribute("href")!.Value) is not null, "EPUB has a broken manifest item.");
        var navigation = XDocument.Parse(Read("EPUB/nav.xhtml"));
        Require(navigation.Descendants(html + "a").Count() == book.Chapters.Count, "EPUB omitted a TOC chapter.");
        foreach (var link in navigation.Descendants(html + "a"))
            Require(archive.GetEntry("EPUB/" + link.Attribute("href")!.Value) is not null, "EPUB has a broken TOC link.");
        var chapter = XDocument.Parse(Read("EPUB/chapter-1.xhtml"));
        Require(string.Join("\n\n", chapter.Descendants(html + "p").Select(p => p.Value)) == expectedText,
            "EPUB differs from the displayed reader-selected edition.");
        Require(!chapter.Descendants(html + "script").Any() && !chapter.Descendants(html + "img").Any(),
            "Prose became executable markup or a remote image.");
        Require(!Read("EPUB/package.opf").Contains("workspace-1", StringComparison.Ordinal)
            && !Read("EPUB/package.opf").Contains("local-single-user", StringComparison.Ordinal),
            "EPUB exposed private workspace or owner identity.");
        Require(!Read("EPUB/style.css").Contains("color:", StringComparison.Ordinal),
            "EPUB defeats the user's reading theme.");
        Require(JsonSerializer.Serialize(book.Projection) == original, "EPUB export mutated canonical history.");
        Console.WriteLine("PASS Origin EPUB: readable offline, localized, safe selected prose and complete TOC " + locale);
    }

    public static void RunAuthoringSource()
    {
        var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
        var opened = service.Start("workspace-1").Value!;
        var prepared = service.Prepare(opened, "choice-1").Value!;
        var book = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "authoring-source-fixture", true).Value!.Checkpoint.Projection;
        var chapter = book.VisibleChapters.Single();
        var fixture = book with
        {
            AllowedCanonicalFactIds = ["metatype", "answer", "contributions", "future", "future-contributions", "private"],
            CanonicalLayer = book.CanonicalLayer with
            {
                AcceptedDecisionIds = [chapter.ThroughAcceptedDecisionId, "later"],
                Facts = [
                    new("metatype", "accepted-metatype", "Elf", chapter.ThroughAcceptedDecisionId, ["private-anchor"], ""),
                    new("answer", "accepted-life-module-answer", "Childhood: Renraku", chapter.ThroughAcceptedDecisionId, [], ""),
                    new("contributions", "accepted-life-module-contributions", "Confirmed module contributions, not final ratings: Survival +1", chapter.ThroughAcceptedDecisionId, ["private-effect-anchor"], ""),
                    new("future", "accepted-life-module-answer", "Future not chosen in this chapter", "later", [], ""),
                    new("future-contributions", "accepted-life-module-contributions", "Future bonus not chosen in this chapter", "later", [], ""),
                    new("private", "private-notes", "Do not send", chapter.ThroughAcceptedDecisionId, [], ""),
                    new("not-allowed", "accepted-life-module-answer", "Not approved", chapter.ThroughAcceptedDecisionId, [], "")
                ]
            }
        };
        string before = JsonSerializer.Serialize(fixture);
        var source = OriginBookAuthoringSource.Create(fixture, chapter);
        Require(source.Facts.Count == 3 && source.Facts.All(f => f.DecisionId == chapter.ThroughAcceptedDecisionId)
            && source.Facts.Single(f => f.FactId == "contributions").Text == fixture.CanonicalLayer.Facts.Single(f => f.FactId == "contributions").LocalizedSummary,
            "Provider input leaked a later, private or non-allowlisted fact.");
        var historical = fixture with {
            CanonicalLayer = fixture.CanonicalLayer with {
                Facts = fixture.CanonicalLayer.Facts.Where(f => f.FactKind != "accepted-life-module-contributions").ToArray() }
        };
        var historicalSource = OriginBookAuthoringSource.Create(historical, chapter);
        Require(historicalSource.Facts.Count == 2
            && historicalSource.Facts.All(f => f.FactId is "metatype" or "answer"),
            "Historical books acquired newly inferred mechanics or new request identities.");
        string serialized = JsonSerializer.Serialize(source);
        Require(!serialized.Contains("private-anchor", StringComparison.Ordinal)
            && !serialized.Contains("private-effect-anchor", StringComparison.Ordinal)
            && !serialized.Contains(chapter.VisibleMarkdown, StringComparison.Ordinal)
            && before == JsonSerializer.Serialize(fixture), "Authoring projection copied source prose or mutated the Core book.");
        bool rejected = false;
        try { OriginBookAuthoringSource.Create(fixture with { AllowedCanonicalFactIds = [] }, chapter); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "A chapter with no approved facts could be sent to a provider.");
        var nextChapter = chapter with { ChapterId = "next-chapter", ChapterDigest = new string('f', 64), ThroughAcceptedDecisionId = "later" };
        var chronological = fixture with { VisibleChapters = [nextChapter, chapter] };
        var unreviewedBook = new RetainedOriginBook(chronological);
        Require(unreviewedBook.TryGetAuthoringPredecessor(chapter, out var firstPrevious) && firstPrevious is null,
            "The first chapter acquired an invented predecessor.");
        Require(!unreviewedBook.TryGetAuthoringPredecessor(nextChapter, out _), "A successor skipped its unreviewed preceding chapter.");
        string jobId = OriginChapterSourceIdentity.RequestId(source);
        var prose = OriginBookProseDraft.Create(chapter, chronological.CurrentTurn.Locale, jobId, new string('b', 64), "Reviewed fictional scene.");
        var readings = new OriginBookReadingState("local-single-user", source.WorkspaceId, [new(chapter.ChapterId, prose, null)]);
        var reader = new RetainedOriginBook(chronological, readings);
        Require(unreviewedBook.MechanicsAfterReading(chapter).Count == 0
            && reader.MechanicsAfterReading(chapter).SequenceEqual(new[] {
                "Confirmed module contributions, not final ratings: Survival +1" }),
            "Rule changes appeared before reading or included a future/private contribution.");
        Require(reader.TryGetAuthoringPredecessor(nextChapter, out var previous)
            && previous?.RequestId == jobId && previous.SourceDigest == OriginChapterSourceIdentity.Digest(source)
            && previous.ProviderReceiptDigest == prose.ProviderReceiptDigest
            && previous.TextDigest == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(prose.Text))).ToLowerInvariant(),
            "Continuation guessed provider order or lost the selected exact reading.");
        var restoredReadings = JsonSerializer.Deserialize<OriginBookReadingState>(JsonSerializer.Serialize(readings))!;
        Require(new RetainedOriginBook(chronological, restoredReadings).TryGetAuthoringPredecessor(nextChapter, out var restoredPrevious)
            && restoredPrevious == previous, "Reading restart changed the predecessor binding.");
        var pendingOnly = readings with { Chapters = [new(chapter.ChapterId, null, prose)] };
        Require(new RetainedOriginBook(chronological, pendingOnly).MechanicsAfterReading(chapter).Count == 0,
            "Generating a chapter counted as the user having read it.");
        Require(!new RetainedOriginBook(chronological, pendingOnly).TryGetAuthoringPredecessor(nextChapter, out _),
            "A pending draft was treated as a selected reading.");
        Require(!new RetainedOriginBook(chronological with { VisibleChapters = [chapter, nextChapter with
            { ThroughAcceptedDecisionId = chapter.ThroughAcceptedDecisionId }] }, readings)
            .TryGetAuthoringPredecessor(chapter, out _), "Ambiguous Core chapter boundaries were guessed.");
        RunOpeningChapterBoundary(chronological, chapter, nextChapter);
        Console.WriteLine("PASS Origin authoring projection: confirmed facts only, no future choices, source prose, anchors or mutation");
    }

    private static void RunOpportunityContext()
    {
        var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(3)));
        var opened = service.Start("workspace-1").Value!;
        var prepared = service.Prepare(opened, "choice-1").Value!;
        var projection = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "opportunity-fixture", true).Value!.Checkpoint.Projection;
        var candidate = projection.CurrentTurn.LegalChoices[0];
        LifeModuleNarrativeChoiceSeed Choice(string id, decimal cost) => candidate with
        {
            ChoiceId = id, Label = "Path " + id,
            SourceAnchorIds = ["lifemodules.xml#module:" + id],
            MechanicsPreview = candidate.MechanicsPreview with { KarmaCost = cost, KarmaIsExact = true }
        };
        var choices = new[] { Choice("street", 20), Choice("corps", 20) };
        projection = projection with { AllowedChoiceIds = choices.Select(c => c.ChoiceId).ToArray(),
            CurrentTurn = projection.CurrentTurn with { LegalChoices = choices, IsTerminal = false } };
        const long savedRevision = 2;
        LifeModuleDecisionAvailabilitySnapshot Availability(OriginStoryArcSeed seed, params LifeModuleNarrativeChoiceSeed[] excluded)
            => new(new(seed.CurrentTurn.WorkspaceId, seed.CurrentTurn.WorkspaceRevision, savedRevision,
                    seed.CurrentTurn.TurnId, seed.CurrentTurn.DecisionDigest),
                seed.CurrentTurn.ContentDigest, seed.CurrentTurn.SourceDigest, seed.CurrentTurn.RulesDigest, seed.CurrentTurn.RuntimeDigest,
                seed.CurrentTurn.LegalChoices.Select(c => new LifeModuleOptionAvailability(c.ChoiceId, c.Label,
                    LifeModuleOptionAvailabilityStates.Available, c.SourceAnchorIds))
                .Concat(excluded.Select(c => new LifeModuleOptionAvailability(c.ChoiceId, c.Label,
                    LifeModuleOptionAvailabilityStates.BudgetExcluded, c.SourceAnchorIds))).ToArray());
        var availability = Availability(projection, Choice("school", 40));
        string before = JsonSerializer.Serialize(projection);
        var hints = OriginBookOpportunityContext.Create(projection, availability, savedRevision)!;
        Require(hints.Opportunities.Count == 3
            && hints.Opportunities.Single(c => c.ChoiceId == "school").Availability == OriginChapterOpportunityAvailability.Unavailable
            && hints.Opportunities.Where(c => c.ChoiceId != "school").All(c => c.Availability == OriginChapterOpportunityAvailability.Available)
            && hints.DecisionDigest == projection.CurrentTurn.DecisionDigest && hints.TurnId == projection.CurrentTurn.TurnId
            && !projection.AllowedChoiceIds.Contains("school") && choices.All(c => c.ChoiceId != "school")
            && before == JsonSerializer.Serialize(projection), "Hints guessed legal choices/costs, changed mechanics or lost the exact turn.");
        foreach (var invalid in new[] {
            availability with { Binding = availability.Binding with { WorkspaceId = "other" } },
            availability with { Binding = availability.Binding with { WorkspaceRevision = availability.Binding.WorkspaceRevision + 1 } },
            availability with { Binding = availability.Binding with { SavedRevision = savedRevision + 1 } },
            availability with { Binding = availability.Binding with { TurnId = "other" } },
            availability with { Binding = availability.Binding with { DecisionDigest = Digest("other") } },
            availability with { ContentDigest = Digest("other") }, availability with { SourceDigest = Digest("other") },
            availability with { RulesDigest = Digest("other") }, availability with { RuntimeDigest = Digest("other") },
            availability with { Options = [availability.Options[0], availability.Options[0]] },
            availability with { Options = [availability.Options[0] with { Availability = LifeModuleOptionAvailabilityStates.BudgetExcluded }] },
            availability with { Options = [availability.Options[^1] with { Availability = LifeModuleOptionAvailabilityStates.Available }] },
            availability with { Options = [availability.Options[^1] with { Availability = "other-blocker" }] },
            availability with { Options = [availability.Options[0] with { Label = "Substituted" }] },
            availability with { Options = [availability.Options[^1] with { SourceAnchorIds = [] }] } })
            Require(OriginBookOpportunityContext.Create(projection, invalid, savedRevision) is null,
                "Unbound or contradictory Core availability supplied story opportunities.");
        Require(OriginBookOpportunityContext.Create(projection with { CurrentTurn = projection.CurrentTurn with { IsTerminal = true } }, availability, savedRevision) is null,
            "A completed journey acquired future options.");
        var many = Enumerable.Range(0, 24).Select(i => Choice("path-" + i, i % 2 == 0 ? 10 : 40)).ToArray();
        var affordable = many.Where(c => c.MechanicsPreview.KarmaCost == 10).ToArray();
        var manyProjection = projection with { AllowedChoiceIds = affordable.Select(c => c.ChoiceId).ToArray(),
            CurrentTurn = projection.CurrentTurn with { LegalChoices = affordable } };
        var bounded = OriginBookOpportunityContext.Create(manyProjection,
            Availability(manyProjection, many.Where(c => c.MechanicsPreview.KarmaCost == 40).ToArray()), savedRevision)!;
        Require(bounded.Opportunities.Count == 4 && bounded.Opportunities.GroupBy(c => c.Availability).All(g => g.Count() == 2),
            "Hints exported an entire choice catalogue.");
        var chapter = projection.VisibleChapters.Single();
        var book = new RetainedOriginBook(projection, opportunities: hints);
        var frozen = book.AuthoringSource(chapter);
        var legacy = OriginBookAuthoringSource.Create(projection, chapter);
        Require(JsonSerializer.Serialize(frozen.NarrativeContext) == JsonSerializer.Serialize(hints)
            && OriginChapterSourceIdentity.RequestId(frozen) == OriginChapterSourceIdentity.RequestId(legacy)
            && OriginChapterSourceIdentity.Digest(frozen) != OriginChapterSourceIdentity.Digest(legacy),
            "Optional context changed the chapter lookup or was not bound to the approved source.");
        var approved = new OriginBookReadingState("local-single-user", "workspace-1",
            [new(chapter.ChapterId, null, null) { AuthoringSource = frozen }]);
        var later = hints with { TurnId = "later-turn", DecisionDigest = Digest("later"),
            Opportunities = [new("other", "Other path", OriginChapterOpportunityAvailability.Available)] };
        var cold = new RetainedOriginBook(projection, JsonSerializer.Deserialize<OriginBookReadingState>(JsonSerializer.Serialize(approved)), opportunities: later);
        Require(OriginChapterSourceIdentity.Digest(cold.AuthoringSource(chapter)) == OriginChapterSourceIdentity.Digest(frozen),
            "Cold reopen rewrote the approved source using new hints.");
        var pending = OriginBookProseDraft.Create(chapter, book.Locale, OriginChapterSourceIdentity.RequestId(legacy), Digest("provider"), "Existing legacy prose.");
        var legacyBook = new RetainedOriginBook(projection, approved with { Chapters = [new(chapter.ChapterId, null, pending)] }, opportunities: hints);
        Require(OriginChapterSourceIdentity.Digest(legacyBook.AuthoringSource(chapter)) == OriginChapterSourceIdentity.Digest(legacy),
            "An old draft acquired new story opportunities.");
        var advanced = projection with { CanonicalLayer = projection.CanonicalLayer with
            { AcceptedDecisionIds = [.. projection.CanonicalLayer.AcceptedDecisionIds, "future"] } };
        Require(new RetainedOriginBook(advanced, opportunities: later).AuthoringSource(chapter).NarrativeContext is null,
            "Historical chapter acquired the current turn's hints.");
        Console.WriteLine("PASS story opportunities: separate Core budget exclusions, exact owner-turn binding, bounded sampling, immutable consent, legacy identity and no history rewrite");
    }

    private static void RunOpeningChapterBoundary(OriginStoryArcSeed seed,
        OriginNarrativeChapterProjection foundation, OriginNarrativeChapterProjection childhood)
    {
        var adolescent = childhood with { ChapterId = "adolescent", ThroughAcceptedDecisionId = "teen", ChapterDigest = Digest("teen") };
        var facts = new OriginCanonicalNarrativeFact[] {
            new("race", "accepted-metatype", "Troll", foundation.ThroughAcceptedDecisionId, [], ""),
            new("birth", "accepted-life-module", "Birth background: UCAS", foundation.ThroughAcceptedDecisionId, [], ""),
            new("childhood", "accepted-life-module", "Childhood: street life", childhood.ThroughAcceptedDecisionId, [], ""),
            new("childhood-answer", "accepted-life-module-answer", "Raised by an aunt", childhood.ThroughAcceptedDecisionId, [], ""),
            new("teen", "accepted-life-module", "Future school", adolescent.ThroughAcceptedDecisionId, [], "") };
        var ready = seed with {
            CurrentTurn = seed.CurrentTurn with { JourneyId = "sr5-life-modules-foundation", StageOrder = LifeModuleJourneyStageOrders.TeenYears },
            VisibleChapters = [adolescent, foundation, childhood],
            AllowedCanonicalFactIds = facts.Select(f => f.FactId).ToArray(),
            CanonicalLayer = seed.CanonicalLayer with {
                AcceptedDecisionIds = [foundation.ThroughAcceptedDecisionId, childhood.ThroughAcceptedDecisionId, adolescent.ThroughAcceptedDecisionId], Facts = facts }
        };
        string immutable = JsonSerializer.Serialize(ready);
        var incomplete = ready with {
            CurrentTurn = ready.CurrentTurn with { StageOrder = LifeModuleJourneyStageOrders.FormativeYears },
            VisibleChapters = [foundation],
            CanonicalLayer = ready.CanonicalLayer with { AcceptedDecisionIds = [foundation.ThroughAcceptedDecisionId], Facts = facts.Take(2).ToArray() }
        };
        var setup = new RetainedOriginBook(incomplete);
        Require(!setup.OpeningSetupComplete && !setup.CanOpenAuthoring(foundation)
            && !setup.TryGetAuthoringPredecessor(foundation, out _), "A book could start before childhood was confirmed.");
        var book = new RetainedOriginBook(ready);
        Require(book.OpeningSetupComplete && !book.CanOpenAuthoring(foundation)
            && book.TryGetAuthoringPredecessor(childhood, out var first) && first is null,
            "Opening decisions were split into paid chapters or required an invented predecessor.");
        var source = OriginBookAuthoringSource.Create(ready, childhood);
        Require(source.Facts.Select(f => f.FactId).ToHashSet().SetEquals(["race", "birth", "childhood", "childhood-answer"]),
            "The first chapter omitted its initial situation or leaked a later decision.");
        Require(!book.CanOpenAuthoring(adolescent), "A later story skipped the unread first chapter.");
        var prose = OriginBookProseDraft.Create(childhood, book.Locale, OriginChapterSourceIdentity.RequestId(source), Digest("provider"), "Reviewed opening story.");
        var reading = new OriginBookReadingState("local-single-user", ready.CurrentTurn.WorkspaceId, [new(childhood.ChapterId, prose, null)]);
        var reopened = new RetainedOriginBook(JsonSerializer.Deserialize<OriginStoryArcSeed>(JsonSerializer.Serialize(ready))!,
            JsonSerializer.Deserialize<OriginBookReadingState>(JsonSerializer.Serialize(reading))!);
        Require(reopened.TryGetAuthoringPredecessor(adolescent, out var predecessor) && predecessor?.RequestId == prose.JobId,
            "Restart lost the first chapter's exact accepted predecessor.");
        Require(!new RetainedOriginBook(ready, reading with { Chapters = [new(childhood.ChapterId, null, prose)] })
            .CanOpenAuthoring(adolescent), "An unaccepted first draft unlocked a later chapter.");
        var throughChildhood = ready with {
            VisibleChapters = [foundation, childhood],
            CanonicalLayer = ready.CanonicalLayer with {
                AcceptedDecisionIds = [foundation.ThroughAcceptedDecisionId, childhood.ThroughAcceptedDecisionId],
                Facts = facts.Where(f => f.AcceptedDecisionId != "teen").ToArray() }
        };
        Require(!new RetainedOriginBook(throughChildhood).HasReadCurrentStory
            && !new RetainedOriginBook(throughChildhood, reading with { Chapters = [new(childhood.ChapterId, null, prose)] }).HasReadCurrentStory,
            "A missing or merely generated opening unlocked the next module.");
        Require(new RetainedOriginBook(throughChildhood, reading).HasReadCurrentStory && !reopened.HasReadCurrentStory,
            "Readiness ignored the latest decision or required a paid foundation chapter.");
        var finish = childhood with { ChapterId = "selection-finished", ThroughAcceptedDecisionId = "finish", ChapterDigest = Digest("finish") };
        var finishFact = new OriginCanonicalNarrativeFact("finish-fact", "accepted-module-selection-finish", "Done", "finish", [], "");
        var finished = throughChildhood with { VisibleChapters = [foundation, childhood, finish],
            AllowedCanonicalFactIds = [.. throughChildhood.AllowedCanonicalFactIds, finishFact.FactId],
            CanonicalLayer = throughChildhood.CanonicalLayer with {
                AcceptedDecisionIds = [.. throughChildhood.CanonicalLayer.AcceptedDecisionIds, "finish"],
                Facts = [.. throughChildhood.CanonicalLayer.Facts, finishFact] } };
        var finishedBook = new RetainedOriginBook(finished, reading);
        Require(finishedBook.HasReadCurrentStory && finishedBook.IsSelectionFinish(finish) && !finishedBook.CanOpenAuthoring(finish),
            "A sealed finish marker required an impossible extra paid chapter before Career.");
        Require(!new RetainedOriginBook(finished with { AllowedCanonicalFactIds = throughChildhood.AllowedCanonicalFactIds }, reading).HasReadCurrentStory,
            "An unapproved finish marker bypassed reading readiness.");
        var unrelated = OriginBookProseDraft.Create(childhood, book.Locale, "unrelated-request", Digest("provider"), "Unrelated story.");
        Require(!new RetainedOriginBook(throughChildhood, reading with { Chapters = [new(childhood.ChapterId, unrelated, null)] }).HasReadCurrentStory,
            "Reading accepted a draft not bound to the exact canonical source.");
        Require(!new RetainedOriginBook(ready with { AllowedCanonicalFactIds = ["birth", "childhood"] }).OpeningSetupComplete,
            "The opening accepted an unapproved metatype fact.");
        var legacySource = OriginBookAuthoringSource.Create(ready, foundation);
        var legacyProse = OriginBookProseDraft.Create(foundation, book.Locale,
            OriginChapterSourceIdentity.RequestId(legacySource), Digest("legacy-provider"), "Previously written opening.");
        var legacyReading = reading with { Chapters = [new(foundation.ChapterId, legacyProse, null)] };
        var legacy = new RetainedOriginBook(ready, legacyReading);
        Require(legacy.CanOpenAuthoring(foundation) && !legacy.TryGetAuthoringPredecessor(foundation, out _)
            && legacy.ChapterText(foundation) == legacyProse.Text
            && legacy.TryGetAuthoringPredecessor(childhood, out var legacyPrevious)
            && legacyPrevious?.RequestId == legacyProse.JobId,
            "An existing opening was lost, regenerated or omitted from the exact predecessor chain.");
        Require(!new RetainedOriginBook(ready, legacyReading with { Chapters = [new(foundation.ChapterId, null, legacyProse)] })
            .TryGetAuthoringPredecessor(childhood, out _), "A legacy pending opening was silently skipped.");
        Require(immutable == JsonSerializer.Serialize(ready), "Opening chapter grouping rewrote Core history.");
        Console.WriteLine("PASS first story: confirmed race/birth/childhood together, no future facts, no early generation, read-before-next and cold reopen");
    }

    private static async Task RunReadBeforeNextChoiceAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(3)));
            var checkpoint = service.Start("workspace-1").Value!;
            OriginDossierLifeModulePhoneResult Display(int stage, string locale) => new(
                LifeModuleOriginDossierOutcomes.Success,
                OriginDossierLifeModuleInteractionProjector.Project(checkpoint) with { StageOrder = stage, Locale = locale }, [],
                LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                FoundationSnapshotDigest: "sha256:" + Digest("foundation"),
                BoundContentDigest: checkpoint.BoundContentDigest, BoundSourceDigest: checkpoint.BoundSourceDigest,
                BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest,
                StoryCheckpoint: checkpoint with { Projection = checkpoint.Projection with {
                    CurrentTurn = checkpoint.Projection.CurrentTurn with { JourneyId = "sr5-life-modules-foundation", StageOrder = stage, Locale = locale } } });
            foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
            {
                bool ready = false;
                int reads = 0, prepares = 0, progressOpens = 0;
                TaskCompletionSource<bool>? pending = null;
                var copy = AndroidSurfaceStrings.Resolve(locale);
                OriginDossierLifeModuleDecisionPage Page(int stage) => new(Display(stage, locale), locale,
                    (_, _) => { prepares++; return Task.FromResult<OriginDossierLifeModulePhoneResult?>(null); },
                    (_, _) => throw new InvalidOperationException("A reading check may not mutate."),
                    readStoryReady: async (_, current) => { reads++; return (pending is null ? ready : await pending.Task) && current(); },
                    openStoryProgress: (_, current) =>
                    {
                        Require(current(), "A retired reading check opened progress.");
                        progressOpens++; return Task.CompletedTask;
                    });
                Task Appear(OriginDossierLifeModuleDecisionPage page) => ui.BeginAsyncVoid(() =>
                    typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnAppearing",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(page, null));
                bool Choices(OriginDossierLifeModuleDecisionPage page) => Elements(page).OfType<Button>()
                    .Any(button => button.AutomationId?.StartsWith("origin-life-choice-", StringComparison.Ordinal) == true);
                foreach (int stage in new[] { LifeModuleJourneyStageOrders.Nationality, LifeModuleJourneyStageOrders.FormativeYears })
                {
                    var initial = Page(stage); await Appear(initial);
                    Require(Choices(initial) && reads == 0, "Initial race/birth/childhood decisions waited for an impossible first chapter.");
                }
                var next = Page(LifeModuleJourneyStageOrders.TeenYears);
                Require(!Choices(next), "The next choices flashed before reading readiness was known.");
                await Appear(next);
                Require(progressOpens == 1, "The unread chapter did not open automatically before the next module.");
                Require(!Choices(next) && Elements(next).OfType<Label>().Any(label =>
                    label.AutomationId == "origin-life-story-wait" && label.Text == copy["Origin.StoryReadRequired"]),
                    "Unread story did not explain the next-step gate in the selected language.");
                var refresh = Elements(next).OfType<Button>().Single(b => b.AutomationId == "origin-life-story-refresh");
                await ui.BeginAsyncVoid(() => ((IButtonController)refresh).SendClicked());
                Require(progressOpens == 2 && !Choices(next) && prepares == 0,
                    "Unread status returned silently instead of opening the existing chapter status.");
                ready = true;
                refresh = Elements(next).OfType<Button>().Single(b => b.AutomationId == "origin-life-story-refresh");
                await ui.BeginAsyncVoid(() => ((IButtonController)refresh).SendClicked());
                Require(Choices(next) && prepares == 0 && progressOpens == 2,
                    "Reading confirmation failed to unlock choices or opened unnecessary progress.");
                var retiredChoice = Elements(next).OfType<Button>().First(b => b.AutomationId?.StartsWith("origin-life-choice-", StringComparison.Ordinal) == true);
                pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var checking = Appear(next);
                Require(!Choices(next) && Elements(next).OfType<ActivityIndicator>().Single().IsRunning,
                    "Returning from the reader exposed the old choices during a pending read.");
                typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnDisappearing",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(next, null);
                pending.SetResult(true); await checking;
                await ui.BeginAsyncVoid(() => ((IButtonController)retiredChoice).SendClicked());
                Require(!Choices(next) && prepares == 0, "A late read or departed control unlocked a retired page.");
                pending = null; ready = false;
                await Appear(next);
                Require(!Choices(next), "Reopening cached an obsolete read permission.");
                pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                refresh = Elements(next).OfType<Button>().Single(b => b.AutomationId == "origin-life-story-refresh");
                var lateStatus = ui.BeginAsyncVoid(() => ((IButtonController)refresh).SendClicked());
                typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnDisappearing",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(next, null);
                pending.SetResult(false); await lateStatus;
                Require(progressOpens == 2 && !Choices(next),
                    "A departed status check opened a chapter view after leaving the wizard.");
            }
            ui.AssertHealthy();
        });
        Console.WriteLine("PASS phone story pacing: initial setup free, read-before-next, DE/EN/ES, no auto mutation, retired-read rejection");
    }

    private static async Task RunConfirmOffUiContextAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            SynchronizationContext? callerContext = SynchronizationContext.Current;
            Require(callerContext is not null, "The regression must start on a UI synchronization context.");
            var authority = new DecisionAuthority(1);
            var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var pending = interaction.Prepare(interaction.Start("workspace-1").Value!, "choice-1").Value!;
            var store = new SynchronousStore(pending);
            authority.AfterCommit = () => Require(!ReferenceEquals(callerContext, SynchronizationContext.Current),
                "Core confirmation ran on the phone UI synchronization context.");
            var runtime = new OriginDossierLifeModulePhoneRuntime(new TestOrigin(interaction), store);
            var result = await runtime.ConfirmAsync(TestOwner, "workspace-1", "choice-1", pending.PendingPreview!.PreviewDigest);
            Require(result.IsSuccess && authority.MutationCount == 1 && store.Checkpoint.PendingPreview is null,
                "Background confirmation did not retain the exact accepted chapter.");
        });
        Console.WriteLine("PASS Origin book: confirmation off UI context");
    }

    private static async Task RunFollowUpPageAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var authority = new DecisionAuthority(3);
            var choice = authority.Current.LegalChoices.Single();
            authority.Current = authority.Current with { LegalChoices = [choice with
            {
                FollowUps = [new("name", "Arcology", "text", true, [], choice.SourceAnchorIds, "effect", "text")
                    { DisplayLabel = "Street · Arcology" },
                    new("language", "Language", "single-select", true,
                        [new("english", "English", true, null, new Dictionary<string, string>(), "English")],
                        choice.SourceAnchorIds, "effect", "select")],
                MechanicsPreview = choice.MechanicsPreview with { PendingFollowUpIds = ["name", "language"] }
            }] };
            var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var store = new SynchronousStore(interaction.Start("workspace-1").Value!);
            var runtime = new OriginDossierLifeModulePhoneRuntime(new TestOrigin(interaction), store);
            OriginDossierLifeModulePhoneResult Display(OriginDossierLifeModulePhoneResult result) => result with
            {
                LifeModuleBudget = new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                FoundationSnapshotDigest = "sha256:" + Digest("foundation-" + result.State!.WorkspaceRevision)
            };
            int requests = 0;
            var page = new OriginDossierLifeModuleDecisionPage(Display(await runtime.OpenAsync(TestOwner, "workspace-1")), "en-US",
                async (id, values) => { requests++; return Display(await runtime.PrepareAsync(TestOwner, "workspace-1", id, followUpValues: values)); },
                async (id, digest) => Display(await runtime.ConfirmAsync(TestOwner, "workspace-1", id, digest)));
            Require(page.BackgroundColor == NativeTheme.Paper,
                "Life Modules' fixed dark text must not inherit a system-dark page background.");
            Button Button(string id) => Elements(page).OfType<Button>().Single(button => button.AutomationId == id);
            async Task Click(Button button) => await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
            await Click(Button("origin-life-choice-0"));
            var review = Button("origin-life-review-answers");
            var editingElements = Elements(page).ToArray();
            Require(Array.IndexOf(editingElements, review) < Array.FindIndex(editingElements,
                    element => element.AutomationId == "origin-life-locale"),
                "Selected follow-up questions remain below the setup header after the scroll content resets.");
            Require(!review.IsEnabled && requests == 0 && authority.MutationCount == 0,
                "Opening the form invented required answers or a mutation.");
            Require(Elements(page).OfType<Label>().Any(label => label.Text == "Street · Arcology *")
                && Elements(page).OfType<Label>().Any(label => label.Text == "Language *"),
                "The form lost fresh display context or the canonical-label fallback.");
            var answer = Elements(page).OfType<Entry>().Single();
            var options = Elements(page).OfType<Picker>().Single();
            Require(answer.TextColor == NativeTheme.Text && answer.PlaceholderColor == NativeTheme.Muted
                && answer.BackgroundColor == NativeTheme.Surface,
                "A Life Modules answer must retain contrasting text and placeholder on its light card in dark mode.");
            Require(options.TextColor == NativeTheme.Text && options.TitleColor == NativeTheme.Muted
                && options.BackgroundColor == NativeTheme.Surface,
                "A Life Modules choice must retain contrasting selected text and title on its light card in dark mode.");
            answer.Text = "Renraku";
            Require(!review.IsEnabled, "A required unselected answer was treated as a default choice.");
            Elements(page).OfType<Picker>().Single().SelectedIndex = 0;
            Require(review.IsEnabled, "Explicit complete answers did not enable review.");
            await Click(review);
            Require(requests == 1 && authority.MutationCount == 0 && store.Checkpoint.PendingPreview is not null,
                "Review must only persist the bound preview.");
            Require(Elements(page).OfType<Label>().Any(label => label.Text == "Street · Arcology: Renraku")
                && Elements(page).OfType<Label>().Any(label => label.Text == "Language: English"),
                "The review lost the question context or changed the answer.");
            var oldConfirm = Button("origin-life-confirm");
            var reviewedElements = Elements(page).ToArray();
            Require(Array.IndexOf(reviewedElements, oldConfirm) < Array.FindIndex(reviewedElements,
                    element => element.AutomationId == "origin-life-locale"),
                "The reviewed answers and confirmation remain below the setup header.");
            var reopened = await runtime.OpenAsync(TestOwner, "workspace-1");
            Require(reopened.IsSuccess && reopened.StoryCheckpoint!.PendingPreview!.InputResolution!.Values["name"] == "Renraku",
                "Reviewed answers did not survive reopen.");
            await Click(Button("origin-life-choice-0"));
            Require(Elements(page).OfType<Entry>().Single().Text == "Renraku", "Editing lost the reviewed answers.");
            Elements(page).OfType<Entry>().Single().Text = "NeoNET";
            await Click(oldConfirm);
            Require(authority.MutationCount == 0, "Editing left the previous confirmation callable.");
            await Click(Button("origin-life-review-answers"));
            Require(store.Checkpoint.PendingPreview!.InputResolution!.Values["name"] == "NeoNET", "Updated answer was not rebound.");
            await Click(Button("origin-life-confirm"));
            Require(authority.MutationCount == 1 && store.Checkpoint.Projection.VisibleChapters.Count == 1,
                "Confirmed answers did not advance exactly one turn/chapter.");
        });
        Console.WriteLine("PASS Origin book: required input form, exact review, reopen and stale-confirm rejection");
    }

    private static async Task RunCitySuggestionsAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(3)));
            var checkpoint = interaction.Start("workspace-1").Value!;
            var choice = checkpoint.Projection.CurrentTurn.LegalChoices.Single();
            LifeModuleFollowUpPromptDto City(string id, string label) => new(id, label, "text", true, [], [], "effect", "name");
            var prompts = new[] { City("city-a", "City"), City("city-b", "city"), City("arcology", "Arcology") };
            var facts = new[] {
                new OriginCanonicalNarrativeFact("birth-city", "accepted-life-module-answer", "City: Seattle", "birth", [], Digest("birth")),
                new OriginCanonicalNarrativeFact("teen-city", "accepted-life-module-answer", "city: Berlin", "teen", [], Digest("teen")),
                new OriginCanonicalNarrativeFact("future-city", "accepted-life-module-answer", "City: Boston", "future", [], Digest("future")) };
            var projection = checkpoint.Projection with {
                CurrentTurn = checkpoint.Projection.CurrentTurn with { LegalChoices = [choice with { FollowUps = prompts }] },
                CanonicalLayer = checkpoint.Projection.CanonicalLayer with { AcceptedDecisionIds = ["birth", "teen"], Facts = facts },
                AllowedCanonicalFactIds = ["birth-city", "teen-city"] };
            checkpoint = checkpoint with { Projection = projection };
            Require(OriginDossierLifeModuleDecisionPage.LastConfirmedCity(projection) == "Berlin"
                && OriginDossierLifeModuleDecisionPage.LastConfirmedCity(projection with {
                    CanonicalLayer = projection.CanonicalLayer with { AcceptedDecisionIds = [] } }) is null,
                "The city suggestion used an unaccepted/future fact or a device-global default.");
            var display = new OriginDossierLifeModulePhoneResult(LifeModuleOriginDossierOutcomes.Success,
                OriginDossierLifeModuleInteractionProjector.Project(checkpoint), [],
                LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                FoundationSnapshotDigest: "sha256:" + Digest("foundation"),
                BoundContentDigest: checkpoint.BoundContentDigest, BoundSourceDigest: checkpoint.BoundSourceDigest,
                BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest, StoryCheckpoint: checkpoint);
            OriginDossierLifeModuleDecisionPage Page() => new(display, "en-US",
                (_, _) => throw new InvalidOperationException("Prefilling must not prepare or mutate."),
                (_, _) => throw new InvalidOperationException("Prefilling must not confirm."));
            async Task Open(OriginDossierLifeModuleDecisionPage page)
                => await ui.BeginAsyncVoid(() => ((IButtonController)Elements(page).OfType<Button>()
                    .Single(b => b.AutomationId == "origin-life-choice-0")).SendClicked());
            var page = Page(); await Open(page);
            Entry Field(string id) => Elements(page).OfType<Entry>().Single(e => e.AutomationId == "origin-life-answer-" + id);
            Require(Field("city-a").Text == "Berlin" && Field("city-b").Text == "Berlin"
                && string.IsNullOrEmpty(Field("arcology").Text), "City defaults overwrote a different kind of question.");
            Field("city-a").Text = "Hamburg";
            Require(Field("city-b").Text == "Hamburg", "Repeated city fields did not follow the first answer.");
            Field("city-b").Text = "Bremen";
            Field("city-a").Text = "Kiel";
            Require(Field("city-b").Text == "Bremen", "An explicitly different city was overwritten.");
            var reopened = Page(); await Open(reopened);
            Require(Elements(reopened).OfType<Entry>().First().Text == "Berlin",
                "Cold page reopen remembered unsaved edits instead of confirmed runner history.");
            var savedAuthority = new DecisionAuthority(3);
            var savedChoice = savedAuthority.Current.LegalChoices.Single();
            savedAuthority.Current = savedAuthority.Current with { LegalChoices = [savedChoice with {
                FollowUps = prompts.Select(p => p with { SourceAnchorIds = savedChoice.SourceAnchorIds }).ToArray(),
                MechanicsPreview = savedChoice.MechanicsPreview with { PendingFollowUpIds = prompts.Select(p => p.PromptId).ToArray() }
            }] };
            var savedInteraction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(savedAuthority));
            var savedStart = savedInteraction.Start("workspace-1");
            Require(savedStart.Value is not null, "City fixture start: " + string.Join(", ", savedStart.Blockers));
            var savedCheckpoint = savedInteraction.Prepare(savedStart.Value!, choice.ChoiceId,
                new Dictionary<string, string> { ["city-a"] = "Kiel", ["city-b"] = "Bremen", ["arcology"] = "Renraku" }).Value!;
            Require(savedCheckpoint.PendingPreview?.InputResolution is not null, "The saved city fixture has no resolved answers.");
            display = display with { State = OriginDossierLifeModuleInteractionProjector.Project(savedCheckpoint),
                StoryCheckpoint = savedCheckpoint, BoundContentDigest = savedCheckpoint.BoundContentDigest,
                BoundSourceDigest = savedCheckpoint.BoundSourceDigest, BoundMechanicsSnapshotDigest = savedCheckpoint.BoundMechanicsSnapshotDigest };
            page = Page(); await Open(page);
            Require(Field("city-a").Text == "Kiel" && Field("city-b").Text == "Bremen", "Reopening lost the explicit city answers.");
            Field("city-a").Text = "Hamburg";
            Require(Field("city-b").Text == "Bremen" && Field("arcology").Text == "Renraku",
                "Editing a restored city overwrote another explicitly saved answer.");
            ui.AssertHealthy();
        });
        Console.WriteLine("PASS city suggestions: confirmed chronology, cold page, repeated fields, explicit override and non-city isolation");
    }

    private static async Task RunLiveContinuationPageAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var authority = new DecisionAuthority(3);
            var extra = authority.Current.LegalChoices[0] with
            {
                ChoiceId = "choice-1-extra", Label = "Another path", DecisionCommandDigest = Digest("extra")
            };
            authority.Current = authority.Current with { LegalChoices = [.. authority.Current.LegalChoices, extra] };
            var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var store = new SynchronousStore(interaction.Start("workspace-1").Value!);
            var runtime = new OriginDossierLifeModulePhoneRuntime(new TestOrigin(interaction), store);
            OriginDossierLifeModulePhoneResult Display(OriginDossierLifeModulePhoneResult result)
            {
                decimal spent = result.StoryCheckpoint!.Projection.VisibleChapters.Count * 15m;
                return result with
                {
                    LifeModuleBudget = new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, spent, 750 - spent, true, [], "karma"),
                    FoundationSnapshotDigest = "sha256:" + Digest("foundation-" + result.State!.WorkspaceRevision)
                };
            }
            TaskCompletionSource? confirmEntered = null, confirmRelease = null;
            bool rejectConfirmation = true;
            var page = new OriginDossierLifeModuleDecisionPage(Display(await runtime.OpenAsync(TestOwner, "workspace-1")), "en-US",
                async (choice, answers) => Display(await runtime.PrepareAsync(TestOwner, "workspace-1", choice, followUpValues: answers)),
                async (choice, preview) =>
                {
                    confirmEntered!.SetResult();
                    await confirmRelease!.Task;
                    if (rejectConfirmation) { rejectConfirmation = false; return null; }
                    return Display(await runtime.ConfirmAsync(TestOwner, "workspace-1", choice, preview));
                });
            Button Button(string id) => Elements(page).OfType<Button>().Single(button => button.AutomationId == id);
            bool Visible(string id) => Elements(page).Any(element => element.AutomationId == id);
            async Task Click(Button button) => await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
            Require(!Visible("origin-life-effect-0-0") && !Visible("origin-life-effect-1-0"),
                "Unselected choices must remain compact, not expand every effect.");
            for (int chapter = 1; chapter <= 2; chapter++)
            {
                string selectedIndex = chapter == 1 ? "1" : "0";
                await Click(Button("origin-life-choice-" + selectedIndex));
                var oldConfirm = Button("origin-life-confirm");
                var focusedElements = Elements(page).ToArray();
                Require(Array.IndexOf(focusedElements, oldConfirm) < Array.FindIndex(focusedElements,
                        element => element.AutomationId == "origin-life-locale"),
                    "A choice without follow-up questions hid its confirmation beneath the setup header.");
                Require(!Visible("origin-life-effect-" + selectedIndex + "-0")
                    && !Visible("origin-life-choice-anchors-" + selectedIndex)
                    && oldConfirm.IsEnabled,
                    "A text-only selection exposed mechanics or lost its exact reviewed confirmation.");
                if (chapter == 1)
                {
                    var controls = Elements(page).ToArray();
                    Require(Array.IndexOf(controls, oldConfirm) < Array.IndexOf(controls, Button("origin-life-choice-0"))
                        && !Visible("origin-life-effect-0-0"),
                        "Confirmation is hidden behind unrelated expanded alternatives.");
                }
                confirmEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                confirmRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Task saving = Click(oldConfirm);
                await confirmEntered.Task;
                var progress = Elements(page).OfType<ActivityIndicator>().Single(x => x.AutomationId == "origin-life-saving");
                Require(progress.IsRunning && progress.IsVisible && !oldConfirm.IsEnabled
                    && oldConfirm.Text == "Saving decision…"
                    && page.Content is ScrollView { Content: VerticalStackLayout { IsEnabled: false } },
                    "A pending save must show immediate feedback and disable overlapping decisions.");
                ((IButtonController)oldConfirm).SendClicked();
                Require(authority.MutationCount == chapter - 1, "Pending confirmation replayed a mutation.");
                confirmRelease.SetResult();
                await saving;
                if (chapter == 1)
                {
                    Require(!progress.IsRunning && !progress.IsVisible && oldConfirm.IsEnabled
                        && oldConfirm.Text == "Confirm this decision"
                        && page.Content is ScrollView { Content: VerticalStackLayout { IsEnabled: true } }
                        && authority.MutationCount == 0 && store.Checkpoint.Projection.VisibleChapters.Count == 0,
                        "A rejected save must restore usable controls without inventing an accepted chapter.");
                    confirmEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    confirmRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    saving = Click(oldConfirm);
                    await confirmEntered.Task;
                    Require(progress.IsRunning && progress.IsVisible && !oldConfirm.IsEnabled,
                        "The explicit retry lost pending feedback.");
                    confirmRelease.SetResult();
                    await saving;
                }
                Require(!progress.IsRunning && !progress.IsVisible && !oldConfirm.IsEnabled,
                    "Completed save left a running indicator or a callable old confirmation.");
                Require(authority.MutationCount == chapter && store.Checkpoint.Projection.VisibleChapters.Count == chapter,
                    "The live page did not retain exactly one chapter per confirmation.");
                Require(Visible("origin-life-read-book") && Visible("origin-life-choice-0") && !Visible("origin-life-confirm"),
                    "A successful intermediate decision failed to render the next scene and book action.");
                // The old confirm is now disabled as well as detached; MAUI
                // correctly does not enter its async event handler at all.
                ((IButtonController)oldConfirm).SendClicked();
                Require(authority.MutationCount == chapter, "A detached previous-turn control replayed confirmation.");
            }
            var wrongWorkspace = Display(await runtime.OpenAsync(TestOwner, "workspace-1"));
            wrongWorkspace = wrongWorkspace with { State = wrongWorkspace.State! with { WorkspaceId = "another-runner" } };
            bool adopted = (bool)typeof(OriginDossierLifeModuleDecisionPage).GetMethod("TryAdoptConfirmed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, [wrongWorkspace])!;
            Require(!adopted, "A continuation for a different runner was adopted.");
            ui.AssertHealthy();
        });
        Console.WriteLine("PASS Origin book: live next-turn rendering and stale-confirm rejection");
    }

    private static async Task RunFinishChoiceOrderingAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var authority = new DecisionAuthority(1);
            var ordinary = authority.Current.LegalChoices.Single();
            var finish = ordinary with
            {
                ChoiceId = "zz-source-issued-finish", Label = "Modulauswahl beenden",
                DecisionCommandDigest = Digest("finish-command"),
                MechanicsPreview = ordinary.MechanicsPreview with
                {
                    KarmaCost = 0, KarmaRaw = "0",
                    Items = [new("finish-effect", "creation-stage", CharacterCreationLifeModuleStageIds.SelectionFinished,
                        "false", "true", 0, ordinary.SourceAnchorIds, string.Empty)]
                }
            };
            authority.Current = authority.Current with { LegalChoices = [ordinary, finish] };
            var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var store = new SynchronousStore(interaction.Start("workspace-1").Value!);
            var runtime = new OriginDossierLifeModulePhoneRuntime(new TestOrigin(interaction), store);
            var opened = await runtime.OpenAsync(TestOwner, "workspace-1");
            opened = opened with
            {
                LifeModuleBudget = new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 145, 605, true, [], "karma"),
                FoundationSnapshotDigest = "sha256:" + Digest("foundation-finish")
            };
            string? preparedChoice = null;
            var page = new OriginDossierLifeModuleDecisionPage(opened, "en-US", (choice, _) =>
            {
                preparedChoice = choice;
                return Task.FromResult<OriginDossierLifeModulePhoneResult?>(null);
            }, (_, _) => throw new InvalidOperationException("Selection must not confirm a mutation."));
            var choices = Elements(page).OfType<Button>().Where(button =>
                button.AutomationId?.StartsWith("origin-life-choice-", StringComparison.Ordinal) == true).ToArray();
            Require(choices.Length == 2 && choices[0].AutomationId == "origin-life-choice-1",
                "The typed finish action was buried after unrelated module choices or lost its stable identity.");
            Require(authority.MutationCount == 0 && !Elements(page).Any(element => element.AutomationId == "origin-life-confirm"),
                "Promoting the finish action invented a reviewed or confirmed decision.");
            await ui.BeginAsyncVoid(() => ((IButtonController)choices[0]).SendClicked());
            Require(preparedChoice == finish.ChoiceId && authority.MutationCount == 0,
                "The promoted control prepared a different command or skipped explicit confirmation.");
            foreach (decimal remaining in new[] { 0m, ordinary.MechanicsPreview.KarmaCost - 1m, ordinary.MechanicsPreview.KarmaCost })
            {
                var budgeted = opened with
                {
                    LifeModuleBudget = opened.LifeModuleBudget! with { Used = 750m - remaining, Remaining = remaining }
                };
                var filtered = new OriginDossierLifeModuleDecisionPage(budgeted, "en-US",
                    (_, _) => throw new InvalidOperationException("Rendering may not prepare."),
                    (_, _) => throw new InvalidOperationException("Rendering may not mutate."));
                var visible = Elements(filtered).OfType<Button>().Where(button =>
                    button.AutomationId?.StartsWith("origin-life-choice-", StringComparison.Ordinal) == true).ToArray();
                Require(visible[0].AutomationId == "origin-life-choice-1"
                    && visible.Length == (remaining >= ordinary.MechanicsPreview.KarmaCost ? 2 : 1),
                    "Unaffordable module remained visible, exact-budget option disappeared, or free finish lost its identity.");
                Require(authority.MutationCount == 0, "Affordability display changed the runner.");
            }
        });
        Console.WriteLine("PASS Origin book: typed finish action first, stable identity, preview only");
    }

    private sealed class SynchronousStore(LifeModuleOriginDossierDraftCheckpoint checkpoint) : IOriginDossierDraftTimelineStore
    {
        public LifeModuleOriginDossierDraftCheckpoint Checkpoint { get; private set; } = checkpoint;
        public Task<LifeModuleOriginDossierDraftCheckpoint?> LoadAsync(string owner, string workspace, CancellationToken ct = default)
            => Task.FromResult<LifeModuleOriginDossierDraftCheckpoint?>(Checkpoint);
        public Task SaveAsync(LifeModuleOriginDossierDraftCheckpoint value, CancellationToken ct = default)
        {
            Checkpoint = value;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string owner, string workspace, CancellationToken ct = default)
            => throw new InvalidOperationException("The book must be retained.");
    }

    private static async Task RunTextOnlyDecisionDisplayAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
            {
                var authority = new DecisionAuthority(1);
                authority.Current = authority.Current with { Locale = locale };
                // These are explicit compiler-output display fixtures. Numeric
                // defaults and target binding are covered by Core's real-source tests.
                LifeModuleEffectContribution Contribution(string kind, string target, decimal? amount,
                    string selection = "", string status = CharacterCreationFoundationEffectCompilationStatuses.Supported)
                    => new(kind, kind, "bound-" + kind, target, amount, selection,
                        new Dictionary<string, string>(), ["lifemodules.xml#module:fixture"], status, null);
                authority.Contributions = [
                    Contribution("attributelevel", "LOG", 1),
                    Contribution("skilllevel", "Survival", 1),
                    Contribution("knowledgeskilllevel", "FreeKnowledgeSkills", 1) with
                        { DescriptiveMetadata = new Dictionary<string, string> { ["name"] = "History" } },
                    Contribution("qualitylevel", "SINner (National)", 1),
                    Contribution("pushtext", "", null, "Salish-Shidhe", "pending"),
                    Contribution("unknown-effect", "Unresolved effect", null, status: "unsupported")
                ];
                var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
                var checkpoint = interaction.Prepare(interaction.Start("workspace-1").Value!, "choice-1").Value!;
                OriginDossierLifeModulePhoneResult Display(LifeModuleOriginDossierDraftCheckpoint value) => new(
                    LifeModuleOriginDossierOutcomes.Success, OriginDossierLifeModuleInteractionProjector.Project(value), [],
                    LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                    FoundationSnapshotDigest: "sha256:" + Digest("foundation"),
                    BoundContentDigest: value.BoundContentDigest, BoundSourceDigest: value.BoundSourceDigest,
                    BoundMechanicsSnapshotDigest: value.BoundMechanicsSnapshotDigest, StoryCheckpoint: value);
                int confirmations = 0;
                OriginDossierLifeModuleDecisionPage Page(LifeModuleOriginDossierDraftCheckpoint value) => new(Display(value), locale,
                    (_, _) => Task.FromResult<OriginDossierLifeModulePhoneResult?>(null),
                    (_, _) => { confirmations++; return Task.FromResult<OriginDossierLifeModulePhoneResult?>(null); });
                var page = Page(checkpoint);
                string before = JsonSerializer.Serialize(checkpoint);
                var copy = AndroidSurfaceStrings.Resolve(locale);
                Require(!Elements(page).Any(element => element is Switch ||
                        element.AutomationId == "origin-life-budget"
                        || element.AutomationId == "origin-life-ltd-provenance"
                        || element.AutomationId?.StartsWith("origin-life-effect-", StringComparison.Ordinal) == true
                        || element.AutomationId?.StartsWith("origin-life-choice-source-", StringComparison.Ordinal) == true
                        || element.AutomationId?.StartsWith("origin-life-choice-anchors-", StringComparison.Ordinal) == true)
                    && Elements(page).OfType<Button>().Single(b => b.AutomationId == "origin-life-choice-0").Text
                        == OriginStoryDecisionText.Choice(Display(checkpoint).State!, "choice-1"),
                    "Origin exposed mechanics or a mode toggle, or lost its text-only choice.");
                Require(!Elements(page).OfType<Label>().Any(label => label.Text == copy.Format("Origin.EffectSourceContext", "History"))
                    && !Elements(page).OfType<Label>().Any(label => label.Text == copy["Origin.ContributionScope"])
                    && Elements(page).OfType<Button>().Single(button => button.AutomationId == "origin-life-confirm").IsEnabled,
                    "Text-only review exposed compiler metadata or lost confirmation.");
                Require(!Elements(Page(checkpoint)).Any(element => element is Switch || element.AutomationId == "origin-life-budget"),
                    "Reopening Origin restored a mechanics setting.");
                Require(JsonSerializer.Serialize(checkpoint) == before && authority.MutationCount == 0,
                    "Rendering rewrote the reviewed checkpoint or applied mechanics.");

                // Display-only historical fixture. Actual legacy restore and
                // re-prepare admission are exercised against real Core storage.
                var legacy = Page(checkpoint with { PendingPreview = checkpoint.PendingPreview! with { EffectReview = null } });
                var oldConfirm = Elements(legacy).OfType<Button>().Single(button => button.AutomationId == "origin-life-confirm");
                Require(!oldConfirm.IsEnabled && Elements(legacy).OfType<Label>().Any(label => label.Text == copy["Origin.StoryChoiceReviewRequired"])
                    && !Elements(legacy).Any(element => element.AutomationId?.StartsWith("origin-life-effect-", StringComparison.Ordinal) == true),
                    "A legacy raw preview was shown as compiler-reviewed or remained confirmable.");
                ((IButtonController)oldConfirm).SendClicked();
                await Task.Yield();
                Require(confirmations == 0, "A stale preview dispatched confirmation through a disabled control.");
            }
        });
        Console.WriteLine("PASS Origin book: always text-only DE/EN/ES choices, no mode toggle, exact compiler authority retained and legacy previews blocked");
    }

    private static async Task RunStoryChoiceCopyAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
            {
                var authority = new DecisionAuthority(1);
                authority.Current = authority.Current with { Locale = locale };
                var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
                var checkpoint = interaction.Start("workspace-1").Value!;
                var state = OriginDossierLifeModuleInteractionProjector.Project(checkpoint);
                var original = state.Choices.Single();
                // Display fixtures only; Core acceptance/affordability remain
                // covered by the real-source Life Modules route tests.
                var school = original with { ChoiceId = "school", Label = "Military School", KarmaCost = 50,
                    SourceAnchorIds = ["lifemodules.xml#module:15bd4283-f287-4be7-b174-9e5ab97bda1a"] };
                var street = original with { ChoiceId = "street", Label = "Street Kid", KarmaCost = 40,
                    SourceAnchorIds = ["lifemodules.xml#module:21c3cb79-e0d9-49b8-9ad3-9232bab4f12b"] };
                state = state with { StageOrder = LifeModuleJourneyStageOrders.TeenYears, Choices = [school, street],
                    VisibleStoryMarkdown = "DO NOT DISPLAY raw wizard $DICE or Karma values", DecisionPrompt = "RAW prompt" };
                string before = JsonSerializer.Serialize(state);
                string? selected = null;
                OriginDossierLifeModulePhoneResult Display(decimal remaining) => new(
                    LifeModuleOriginDossierOutcomes.Success, state, [],
                    LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 750 - remaining, remaining, true, [], "karma"),
                    FoundationSnapshotDigest: "sha256:" + Digest("foundation"), BoundContentDigest: checkpoint.BoundContentDigest,
                    BoundSourceDigest: checkpoint.BoundSourceDigest, BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest,
                    StoryCheckpoint: checkpoint);
                OriginDossierLifeModuleDecisionPage Page(decimal remaining) => new(Display(remaining), locale,
                    (id, _) => { selected = id; return Task.FromResult<OriginDossierLifeModulePhoneResult?>(null); },
                    (_, _) => throw new InvalidOperationException("Copy must not confirm a choice."));
                var page = Page(50);
                var button = Elements(page).OfType<Button>().Single(b => b.AutomationId == "origin-life-choice-0");
                Require(button.Text == OriginStoryDecisionText.Choice(state, "school") && button.Text != school.Label
                    && button.LineBreakMode == LineBreakMode.WordWrap,
                    "The native school control lost its shared narrative caption or text wrapping.");
                Require(Elements(page).OfType<Label>().Single(l => l.AutomationId == "origin-life-prompt").Text
                        == OriginStoryDecisionText.Prompt(state)
                    && !Elements(page).OfType<Label>().Any(l => l.Text?.Contains("RAW", StringComparison.Ordinal) == true
                        || l.Text?.Contains("DO NOT DISPLAY", StringComparison.Ordinal) == true),
                    "The old mechanics/template lead-in leaked back into the story decision.");
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                Require(selected == "school" && authority.MutationCount == 0 && JsonSerializer.Serialize(state) == before,
                    "Narrative copy selected a different ID, changed authority or committed a module.");
                var filtered = Elements(Page(49)).OfType<Button>().Where(b => b.AutomationId?.StartsWith("origin-life-choice-", StringComparison.Ordinal) == true).ToArray();
                Require(filtered.Length == 1 && filtered[0].AutomationId == "origin-life-choice-1"
                    && filtered[0].Text == OriginStoryDecisionText.Choice(state, "street"),
                    "Story copy revealed an unaffordable option or renumbered the remaining exact choice.");
            }
            ui.AssertHealthy();
        });
        Console.WriteLine("PASS narrative choice captions DE/EN/ES: exact dispatch IDs, affordability, no source-template prose or mutation");
    }

    private static void RunOpeningStoryDetailsStorage()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-story-details-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new OriginBookReadingStore(directory);
            var empty = store.Load("owner-a", "workspace-1");
            Require(!JsonSerializer.Serialize(empty).Contains("StoryProfile", StringComparison.Ordinal),
                "The optional field changed legacy reading-file digests.");
            var source = new OriginChapterSource("workspace-1", "chapter-1", Digest("chapter"), "decision-1",
                "en-US", "Example", [new("metatype", "decision-1", "Elf")]);
            var profile = new OriginStoryProfile("other", "they/them", "epic", "Find a home", "Mara, an old friend");
            byte[] oldProfile = JsonSerializer.SerializeToUtf8Bytes(new {
                Gender = "other", Pronouns = "they/them", Tone = "epic", Motivation = "Find a home", ImportantPerson = "Mara, an old friend"
            });
            Require(JsonSerializer.SerializeToUtf8Bytes(profile).SequenceEqual(oldProfile),
                "New background fields changed the exact legacy profile bytes.");
            string oldId = "player-story-brief-" + Convert.ToHexString(SHA256.HashData(oldProfile)).ToLowerInvariant();
            var saved = store.Save(empty, empty with { StoryProfile = profile }, () => true, default);
            var reopened = new OriginBookReadingStore(directory).Load("owner-a", "workspace-1");
            Require(reopened.StoryProfile == profile && reopened.Digest == saved.Digest
                && store.Load("owner-b", "workspace-1").StoryProfile is null,
                "Opening details did not survive cold read or crossed owners.");
            Require(OriginChapterSourceIdentity.Digest(new OriginStoryProfile().Apply(source)) == OriginChapterSourceIdentity.Digest(source),
                "Skipping optional details changed historical request identity.");
            var composed = reopened.StoryProfile!.Apply(source);
            Require(composed.Facts.Count == 2 && composed.Facts[0] == source.Facts[0]
                && composed.Facts[1].DecisionId == oldId
                && composed.Facts[1].Text.Contains("they/them", StringComparison.Ordinal)
                && composed.Facts[1].Text.Contains("Epic", StringComparison.Ordinal)
                && OriginChapterSourceIdentity.RequestId(composed) == OriginChapterSourceIdentity.RequestId(profile.Apply(source))
                && OriginChapterSourceIdentity.RequestId(composed) != OriginChapterSourceIdentity.RequestId(source),
                "The story brief impersonated rules authority, lost values or changed identity on reopen.");
            foreach (var bad in new[] { profile with { Gender = "guessed" }, profile with { Tone = "unknown" },
                profile with { Pronouns = "\ninvalid" }, profile with { Motivation = new string('x', 513) } })
            {
                bool rejected = false;
                try { store.Save(saved, saved with { StoryProfile = bad }, () => true, default); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && store.Load("owner-a", "workspace-1").Digest == saved.Digest,
                    "Invalid story details overwrote the saved brief.");
            }
            bool stale = false, canceled = false;
            try { store.Save(empty, empty with { StoryProfile = profile }, () => true, default); }
            catch (InvalidOperationException) { stale = true; }
            try { store.Save(saved, saved with { StoryProfile = profile with { Tone = "dark" } }, () => false, default); }
            catch (OperationCanceledException) { canceled = true; }
            Require(stale && canceled && store.Load("owner-a", "workspace-1").Digest == saved.Digest,
                "A stale or retired story editor overwrote the saved brief.");
            var approved = composed with { NarrativeContext = new("next-turn", Digest("decision"),
                [new("school", "Military school", OriginChapterOpportunityAvailability.Unavailable)]) };
            var retainedChapter = new OriginBookReadingChapter(source.ChapterId, null, null) { AuthoringSource = approved };
            var retained = store.Save(saved, saved with { Chapters = [retainedChapter] }, () => true, default);
            string legacyDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(retained))).ToLowerInvariant();
            byte[] legacyFile = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Schema = "chummer.android.origin-reading-edition/v1", State = retained, Digest = legacyDigest
            });
            string savedPath = Directory.GetFiles(Path.Combine(directory, "origin-reading-editions"), "*.json").Single();
            Require(legacyDigest == retained.Digest && File.ReadAllBytes(savedPath).SequenceEqual(legacyFile),
                "Streaming changed the existing v1 file bytes or digest and would invalidate installed reading editions.");
            var cold = new OriginBookReadingStore(directory).Load("owner-a", "workspace-1");
            Require(cold.Digest == retained.Digest
                && OriginChapterSourceIdentity.Digest(cold.Chapters.Single().AuthoringSource!) == OriginChapterSourceIdentity.Digest(approved)
                && store.Load("owner-b", "workspace-1").Chapters.Count == 0,
                "Cold read lost the exact approved source or crossed owners.");
            Require(!JsonSerializer.Serialize(new OriginBookReadingChapter(source.ChapterId, null, null))
                    .Contains("AuthoringSource", StringComparison.Ordinal),
                "The optional frozen source changed legacy chapter bytes.");
            foreach (var badSource in new[] { approved with { WorkspaceId = "wrong" },
                approved with { ChapterId = "wrong" }, approved with { NarrativeContext = approved.NarrativeContext! with
                    { Opportunities = [new("school", "Military school", "completed")] } } })
            {
                bool rejected = false;
                try { store.Save(retained, retained with { Chapters = [retainedChapter with { AuthoringSource = badSource }] }, () => true, default); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && store.Load("owner-a", "workspace-1").Digest == retained.Digest,
                    "An invalid frozen source replaced the approved chapter input.");
            }
            var background = new OriginStoryBackground("Seattle, raised by an aunt", "Betrayal after leaving school",
                "Gambling", "recovery", "A broken promise", "Mara's dry humour", "adult", "Recovery begins at 24, not in childhood");
            bool frozen = false;
            try { store.Save(retained, retained with { StoryProfile = profile with { Background = background } }, () => true, default); }
            catch (InvalidOperationException) { frozen = true; }
            Require(frozen && store.Load("owner-a", "workspace-1").Digest == retained.Digest,
                "New background details rewrote a previously retained authoring input.");
            var other = store.Load("owner-a", "workspace-2");
            var detailed = profile with { Background = background };
            var detailSaved = store.Save(other, other with { StoryProfile = detailed }, () => true, default);
            var detailCold = new OriginBookReadingStore(directory).Load("owner-a", "workspace-2");
            Require(detailCold.StoryProfile == detailed && detailCold.Digest == detailSaved.Digest,
                "Optional personal background did not survive a cold read.");
            foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
            {
                var localizedSource = source with { Locale = locale };
                var copy = AndroidSurfaceStrings.Resolve(locale);
                var input = detailCold.StoryProfile!.Apply(localizedSource);
                Require(input.Facts.Count == 8 && input.Facts[0] == localizedSource.Facts[0]
                    && input.Facts[1] == profile.Apply(localizedSource).Facts[1]
                    && input.Facts.Skip(2).All(f => f.DecisionId.StartsWith("player-background-", StringComparison.Ordinal)
                        && f.Text.Contains(copy["Origin.BackgroundBrief"], StringComparison.Ordinal))
                    && input.Facts.Single(f => f.FactId.EndsWith("-addiction", StringComparison.Ordinal)).Text
                        .Contains(copy["Origin.Addiction.recovery"], StringComparison.Ordinal)
                    && input.Facts.Single(f => f.FactId.EndsWith("-experiences", StringComparison.Ordinal)).Text
                        .Contains(copy["Origin.Period.adult"], StringComparison.Ordinal)
                    && input.Facts.Single(f => f.FactId.EndsWith("-chronology", StringComparison.Ordinal)).Text
                        .Contains(background.Chronology!, StringComparison.Ordinal)
                    && OriginChapterSourceIdentity.RequestId(input) == OriginChapterSourceIdentity.RequestId(detailed.Apply(localizedSource)),
                    "Background lost its chronology, status, player-only boundary or stable source identity.");
                var undated = new OriginStoryProfile { Background = new(Experiences: "A remembered event") }.Apply(localizedSource);
                Require(undated.Facts.Count == 2 && undated.Facts[1].Text.Contains(copy["Origin.Period.unspecified"], StringComparison.Ordinal)
                    && !undated.Facts[1].Text.Contains(copy["Origin.Addiction.current"], StringComparison.Ordinal),
                    "An undated detail invented a life period or addiction status.");
                string full = new('\u754c', 256);
                var maximum = new OriginStoryProfile("other", new('\u754c', 120), "mixed", new('\u754c', 512), new('\u754c', 512))
                    { Background = new(full, full, full, "current", full, full, "adult", full) };
                var bounded = maximum.Apply(localizedSource);
                Require(bounded.Facts.All(f => f.Text.Length <= 2048)
                    && JsonSerializer.SerializeToUtf8Bytes(bounded, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length <= 32768,
                    "A fully filled Unicode background exceeded the existing provider input bounds.");
            }
            foreach (var bad in new[] { background with { Period = "guessed" }, background with { AddictionStatus = "diagnosed" },
                background with { Experiences = new string('x', 257) }, background with { Chronology = "age\nunknown" },
                background with { BirthplaceFamily = " padded " }, new OriginStoryBackground() })
            {
                bool rejected = false;
                try { store.Save(detailSaved, detailSaved with { StoryProfile = detailed with { Background = bad } }, () => true, default); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && store.Load("owner-a", "workspace-2").Digest == detailSaved.Digest,
                    "Invalid background data overwrote the saved profile.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("PASS optional opening brief: cold persistence, owner isolation, stable source identity, legacy omission and rejected stale/invalid writes");
    }

    private static void RunStoryLanguageStorage()
    {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "chummer-story-language-" + Guid.NewGuid().ToString("N"));
        string directory = fixtureRoot;
        try
        {
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
            var pending = service.Prepare(service.Start("workspace-1").Value!, "choice-1").Value!;
            var projection = service.Confirm(pending, pending.PendingPreview!.PreviewDigest, "language-test", true).Value!.Checkpoint.Projection;
            string canonical = JsonSerializer.Serialize(projection);
            var chapter = projection.VisibleChapters.Single();
            var store = new OriginBookReadingStore(directory);
            var empty = store.Load("owner-a", "workspace-1");
            var original = new RetainedOriginBook(projection, empty);
            var legacySource = original.AuthoringSource(chapter);
            Require(!JsonSerializer.Serialize(new OriginStoryProfile()).Contains("StoryLanguage", StringComparison.Ordinal),
                "An absent book language changed legacy profile bytes.");
            foreach (string language in new[] { "de-DE", "en-US", "es-ES" })
            {
                var expected = store.Load("owner-a", "workspace-1");
                var profile = new OriginStoryProfile { StoryLanguage = language };
                var saved = store.Save(expected, expected with { StoryProfile = profile }, () => true, default);
                var cold = new OriginBookReadingStore(directory).Load("owner-a", "workspace-1");
                var book = new RetainedOriginBook(projection, cold);
                var source = book.AuthoringSource(chapter);
                Require(cold.Digest == saved.Digest && cold.StoryProfile == profile && book.Locale == language
                    && source.Locale == language && source.Facts.SequenceEqual(legacySource.Facts)
                    && JsonSerializer.Serialize(projection) == canonical,
                    "Book language was lost on cold reopen or changed canonical choices/facts.");
                string request = OriginChapterSourceIdentity.RequestId(source);
                var prose = OriginBookProseDraft.Create(chapter, language, request, Digest("provider"), "A complete test chapter.");
                var retained = saved with { Chapters = [new(chapter.ChapterId, prose, null) { AuthoringSource = source }] };
                var readable = new RetainedOriginBook(projection, retained);
                Require(readable.ReadableChapter(chapter) == prose && readable.HasExportableChapters
                    && readable.ToHtml(AndroidSurfaceStrings.Resolve("en-US")).Contains("lang=\"" + language + "\"", StringComparison.Ordinal),
                    "A book in a language different from the decision UI cannot be read or exported.");
                store.Save(saved, retained, () => true, default);
                bool rejected = false;
                try { store.Save(retained, retained with { StoryProfile = profile with { StoryLanguage = "de-DE" == language ? "en-US" : "de-DE" } }, () => true, default); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected && store.Load("owner-a", "workspace-1").Digest == retained.Digest,
                    "A language change rewrote an already requested/generated chapter.");
                // Independent fixture workspace for the next language, not a production reset.
                directory = Path.Combine(directory, "next");
                store = new OriginBookReadingStore(directory);
            }
            Require(!new OriginStoryProfile { StoryLanguage = "invalid" }.IsValid,
                "An unsupported book language was admitted.");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true);
        }
        Console.WriteLine("PASS story language: independent output, cold reopen, readable/exportable prose, immutable existing requests and legacy bytes");
    }

    private static async Task RunStoryLanguagePickerAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
            var checkpoint = service.Start("workspace-1").Value!;
            checkpoint = checkpoint with { Projection = checkpoint.Projection with {
                CurrentTurn = checkpoint.Projection.CurrentTurn with { JourneyId = "sr5-life-modules-foundation" } } };
            foreach (string uiLocale in new[] { "en-US", "de-DE", "es-ES" })
            {
                int saves = 0;
                bool failSave = false;
                int loads = 0;
                OriginBookReadingState saved = new("owner-a", "workspace-1", []);
                var display = new OriginDossierLifeModulePhoneResult(LifeModuleOriginDossierOutcomes.Success,
                    OriginDossierLifeModuleInteractionProjector.Project(checkpoint) with { Locale = uiLocale }, [],
                    LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                    FoundationSnapshotDigest: "sha256:" + Digest("foundation"), BoundContentDigest: checkpoint.BoundContentDigest,
                    BoundSourceDigest: checkpoint.BoundSourceDigest, BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest,
                    StoryCheckpoint: checkpoint);
                OriginDossierLifeModuleDecisionPage Page() => new(display, uiLocale,
                    (_, _) => throw new InvalidOperationException("Language must not prepare a rules decision."),
                    (_, _) => throw new InvalidOperationException("Language must not confirm a rules decision."),
                    openingDetails: saved, saveOpeningDetails: (expected, profile, current) =>
                    {
                        Require(current() && expected == saved, "A stale language editor saved.");
                        saves++;
                        if (failSave) throw new IOException("Synthetic save failure");
                        saved = expected with { StoryProfile = profile };
                        return Task.FromResult<OriginBookReadingState?>(saved);
                    }, loadOpeningDetails: (_, current) =>
                    {
                        Require(current(), "A stale appearance loaded language authority.");
                        loads++;
                        return Task.FromResult<OriginBookReadingState?>(saved);
                    });
                var page = Page();
                Picker Picker() => Elements(page).OfType<Picker>().Single(p => p.AutomationId == "origin-story-language");
                Require(Picker().SelectedIndex == 0 && Picker().Items.Count == 4 && saves == 0,
                    "Story language is not selectable, invented a default, or wrote on render.");
                var gender = Elements(page).OfType<Picker>().Single(p => p.AutomationId == "origin-story-gender");
                gender.SelectedIndex = 2;
                await ui.BeginAsyncVoid(() => Picker().SelectedIndex = 1);
                Require(saved.StoryProfile is { StoryLanguage: "de-DE", Gender: null } && saves == 1,
                    "Language was not saved or silently committed other optional details.");
                var languageLabel = Elements(page).OfType<Label>().Single(l => l.AutomationId == "origin-life-locale");
                Require(SemanticProperties.GetDescription(languageLabel) == languageLabel.Text
                    && languageLabel.Text!.Contains("de-DE", StringComparison.Ordinal),
                    "The visible and accessible book languages disagree.");
                page = Page();
                Require(Picker().SelectedIndex == 1, "Reopening forgot the explicit book language.");
                failSave = true;
                await ui.BeginAsyncVoid(() => Picker().SelectedIndex = 3);
                Require(Picker().SelectedIndex == 1 && saved.StoryProfile?.StoryLanguage == "de-DE" && saves == 2,
                    "A failed language save left a false visible selection or recursively saved.");
                failSave = false;
                await ui.BeginAsyncVoid(() => Picker().SelectedIndex = 0);
                Require(saved.StoryProfile is null && saves == 3, "Resetting language retained an empty/invalid profile.");
                saved = saved with { StoryProfile = new() { StoryLanguage = "es-ES" } };
                await ui.BeginAsyncVoid(() => typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnAppearing",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(page, null));
                Require(loads == 1 && Picker().SelectedIndex == 3 && saves == 3,
                    "Returning to the decision page did not refresh language authority without writing.");
                saved = saved with { Chapters = [new("requested-chapter", null, null)], StoryProfile = new() { StoryLanguage = "es-ES" } };
                await ui.BeginAsyncVoid(() => typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnAppearing",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(page, null));
                Require(!Elements(page).Any(p => p.AutomationId == "origin-story-language")
                    && Elements(page).OfType<Label>().Any(l => l.Text == AndroidSurfaceStrings.Resolve(uiLocale)["Origin.StoryLanguageFrozen"]),
                    "An already requested chapter allowed a language switch or hid the reason.");
            }
            ui.AssertHealthy();
        });
        Console.WriteLine("PASS story language UI: select/save/reopen, failure recovery, no rule/provider action, frozen requests in DE/EN/ES");
    }

    private static void RunReadingStreamBounds()
    {
        using var output = new MemoryStream();
        using var bounded = new OriginBookReadingStore.BoundedEditionStream(output, 8);
        bounded.Write("1234"u8);
        bounded.Write("5678"u8);
        bool writeRejected = false;
        try { bounded.Write("9"u8); }
        catch (InvalidDataException) { writeRejected = true; }
        Require(writeRejected && output.ToArray().SequenceEqual("12345678"u8.ToArray()),
            "The streaming limit wrote bytes beyond the cap.");
        foreach (bool oversized in new[] { false, true })
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(oversized ? "123456789" : "12345678"));
            using var reader = new OriginBookReadingStore.BoundedEditionStream(input, 8);
            byte[] chunk = new byte[4];
            Require(reader.Read(chunk, 0, chunk.Length) == 4 && reader.Read(chunk) == 4,
                "A bounded stream truncated input before the exact cap.");
            bool readRejected = false;
            try { Require(reader.Read(chunk) == 0, "Exact-size input did not reach EOF."); }
            catch (InvalidDataException) { readRejected = true; }
            Require(readRejected == oversized, "The read cap depended on a length precheck or rejected valid exact-size input.");
        }
        using var cancellation = new CancellationTokenSource();
        using var canceled = new OriginBookReadingStore.BoundedEditionStream(output, 8, cancellation.Token);
        cancellation.Cancel();
        bool canceledWrite = false;
        try { canceled.Write("x"u8); }
        catch (OperationCanceledException) { canceledWrite = true; }
        Require(canceledWrite && output.Length == 8, "A canceled stream still wrote to disk.");
        Console.WriteLine("PASS reading stream: exact cap, chunked overflow, no partial oversized write and cancellation");
    }

    private static void RunLongBookStorage()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-long-book-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
            var prepared = service.Prepare(service.Start("workspace-1").Value!, "choice-1").Value!;
            var projection = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest,
                "long-book-fixture", true).Value!.Checkpoint.Projection;
            var seed = projection.VisibleChapters.Single();
            var chapters = Enumerable.Range(1, 128).Select(i => seed with
            {
                ChapterId = "long-chapter-" + i, ChapterDigest = Digest("long-chapter-" + i),
                ThroughAcceptedDecisionId = "decision-" + i, Sequence = i,
                Title = "Straße " + i + " — 🌧️", VisibleMarkdown = "Not the selected prose."
            }).ToArray();
            string Prose(int i, bool pending)
            {
                string first = (pending ? "PRIVATE PENDING " : "Selected ") + i + ": Über den Dächern 🌧️\n\n";
                string last = "\n\nEnde — " + i;
                // '<' exercises the JSON encoder's sixfold expansion without
                // increasing the existing 64 KiB UTF-8 per-chapter contract.
                return first + new string('<', OriginBookProseDraft.MaximumTextBytes
                    - Encoding.UTF8.GetByteCount(first + last)) + last;
            }
            var retained = chapters.Select((chapter, index) =>
            {
                var source = OriginChapterSourceIdentity.Capture(new OriginChapterSource("workspace-1",
                    chapter.ChapterId, chapter.ChapterDigest, chapter.ThroughAcceptedDecisionId,
                    "de-DE", "Synthetic long-book fixture", [new("fact-" + index,
                        chapter.ThroughAcceptedDecisionId, "A confirmed synthetic memory.")]));
                string request = OriginChapterSourceIdentity.RequestId(source);
                return new OriginBookReadingChapter(chapter.ChapterId,
                    OriginBookProseDraft.Create(chapter, "de-DE", request, Digest("selected"), Prose(index, false)),
                    OriginBookProseDraft.Create(chapter, "de-DE", request, Digest("pending"), Prose(index, true)))
                    { AuthoringSource = source };
            }).ToArray();
            var store = new OriginBookReadingStore(directory);
            var empty = store.Load("owner-a", "workspace-1");
            Require(empty.Digest == Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(empty))).ToLowerInvariant(),
                "Streaming changed the historical reading-state digest.");
            var saved = store.Save(empty, empty with { Chapters = retained }, () => true, default);
            string path = Directory.GetFiles(Path.Combine(directory, "origin-reading-editions"), "*.json").Single();
            long size = new FileInfo(path).Length;
            Require(size > 2 * 1024 * 1024, "The book fixture did not cross the previous aggregate limit.");
            var cold = new OriginBookReadingStore(directory).Load(empty.Owner, empty.Workspace);
            Require(cold.Digest == saved.Digest && cold.Chapters.Count == 128
                && store.Load("owner-b", empty.Workspace).Chapters.Count == 0,
                "The full book did not survive cold read or crossed owners.");
            for (int i = 0; i < chapters.Length; i++)
                Require(cold.Chapters[i].Selected == retained[i].Selected && cold.Chapters[i].Pending == retained[i].Pending
                    && OriginChapterSourceIdentity.Digest(cold.Chapters[i].AuthoringSource!)
                        == OriginChapterSourceIdentity.Digest(retained[i].AuthoringSource!),
                    "Cold read changed full prose, pending review or the frozen authoring input.");
            WriteLongBookDeviceFixture(chapters);
            var book = new RetainedOriginBook(projection with
            {
                CurrentTurn = projection.CurrentTurn with { Locale = "de-DE" }, VisibleChapters = chapters,
                CanonicalLayer = projection.CanonicalLayer with
                    { AcceptedDecisionIds = chapters.Select(c => c.ThroughAcceptedDecisionId).ToArray() }
            }, cold);
            using (var archive = new ZipArchive(new MemoryStream(OriginBookEpub.Create(book,
                AndroidSurfaceStrings.Resolve("de-DE"))), ZipArchiveMode.Read))
            {
                XNamespace html = "http://www.w3.org/1999/xhtml";
                for (int i = 0; i < chapters.Length; i++)
                {
                    using var stream = archive.GetEntry($"EPUB/chapter-{i + 1}.xhtml")!.Open();
                    var page = XDocument.Load(stream);
                    Require(string.Join("\n\n", page.Descendants(html + "p").Select(p => p.Value)) == Prose(i, false),
                        "The persisted full book EPUB truncated prose, substituted a summary or exported pending text.");
                }
            }
            bool stale = false, retired = false, canceled = false;
            try { store.Save(empty, saved, () => true, default); }
            catch (InvalidOperationException) { stale = true; }
            try { store.Save(saved, saved with { Chapters = [] }, () => false, default); }
            catch (OperationCanceledException) { retired = true; }
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try { store.Save(saved, saved with { Chapters = [] }, () => true, cancellation.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Require(stale && retired && canceled && store.Load(empty.Owner, empty.Workspace).Digest == saved.Digest
                && Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0,
                "A stale/canceled write damaged the full book or left a temporary edition.");
            bool overflow = false;
            try { store.Save(saved, saved with { Chapters = retained.Append(new("extra", null, null)).ToArray() }, () => true, default); }
            catch (InvalidDataException) { overflow = true; }
            Require(overflow && new FileInfo(path).Length == size, "The chapter-count boundary was removed.");
            bool oversizedChapter = false;
            try { store.Save(saved, saved with { Chapters = [retained[0] with
                { Selected = retained[0].Selected! with { Text = retained[0].Selected!.Text + "x" } }] }, () => true, default); }
            catch (InvalidDataException) { oversizedChapter = true; }
            Require(oversizedChapter && new FileInfo(path).Length == size, "The per-chapter text/digest boundary was removed.");
            // Sparse hostile input: the length precheck must reject it before
            // materializing JSON. This is our disposable fixture, not user data.
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(128L * 1024 * 1024 + 1);
            bool oversized = false;
            try { store.Load(empty.Owner, empty.Workspace); }
            catch (InvalidDataException) { oversized = true; }
            Require(oversized, "The bounded book file accepted oversized input.");
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(100);
            bool truncated = false;
            try { store.Load(empty.Owner, empty.Workspace); }
            catch (JsonException) { truncated = true; }
            Require(truncated, "A truncated book silently became an empty or partial edition.");
            Console.WriteLine($"PASS long book: 128 full UTF-8 chapters, selected/pending/frozen input, {size} JSON bytes, cold read, complete EPUB and write/size guards");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // Opt-in synthetic app-private fixture for the isolated Debug emulator.
    // Never touch the canonical checkpoint or imply provider/book completion.
    private static void WriteLongBookDeviceFixture(OriginNarrativeChapterProjection[] chapters)
    {
        string? directory = Environment.GetEnvironmentVariable("CHUMMER_ORIGIN_CAPACITY_DEVICE_FIXTURE");
        if (string.IsNullOrEmpty(directory)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "checkpoint.json")));
        Require(document.RootElement.GetProperty("ownerId").GetString() == "local-single-user",
            "Capacity fixtures are restricted to a synthetic local owner.");
        var projection = document.RootElement.GetProperty("projection").Deserialize<OriginStoryArcSeed>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var chapter = projection.VisibleChapters.Single();
        var entries = chapters.Skip(1).Take(31).Select(c => new OriginBookReadingChapter(c.ChapterId,
            OriginBookProseDraft.Create(c, projection.CurrentTurn.Locale, "synthetic-capacity-fixture", Digest("synthetic"),
                "SYNTHETIC STORAGE ONLY — not provider prose. " + new string('x', 64000)),
            OriginBookProseDraft.Create(c, projection.CurrentTurn.Locale, "synthetic-capacity-fixture", Digest("synthetic-pending"),
                "SYNTHETIC PENDING STORAGE ONLY. " + new string('y', 64000)))).ToList();
        entries.Add(new(chapter.ChapterId,
            OriginBookProseDraft.Create(chapter, projection.CurrentTurn.Locale, "synthetic-capacity-fixture", Digest("before"),
                "Synthetic capacity test: previous reading edition. Not an AI chapter."),
            OriginBookProseDraft.Create(chapter, projection.CurrentTurn.Locale, "synthetic-capacity-fixture", Digest("after"),
                "Synthetic capacity test: this revised reading edition must survive Save, Back and process restart.\n\n"
                + "Über den Dächern lag Regen. 🌧️ The other stored entries exercise a book larger than two MiB; "
                + "they are not additional accepted modules or provider chapters.\n\nEND OF SYNTHETIC CAPACITY TEST.")));
        var store = new OriginBookReadingStore(directory);
        var empty = store.Load("local-single-user", projection.CurrentTurn.WorkspaceId);
        Require(empty.Chapters.Count == 0, "Do not overwrite an existing capacity fixture.");
        store.Save(empty, empty with { Chapters = entries }, () => true, default);
        Console.WriteLine("PASS wrote opt-in synthetic >2 MiB device fixture, no canonical or provider changes");
    }

    private static async Task RunOptionalOpeningDetailsAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var authority = new DecisionAuthority(1);
            var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var prepared = service.Prepare(service.Start("workspace-1").Value!, "choice-1").Value!;
            prepared = prepared with { Projection = prepared.Projection with {
                CurrentTurn = prepared.Projection.CurrentTurn with { JourneyId = "sr5-life-modules-foundation" } } };
            var display = new OriginDossierLifeModulePhoneResult(LifeModuleOriginDossierOutcomes.Success,
                OriginDossierLifeModuleInteractionProjector.Project(prepared), [],
                LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                FoundationSnapshotDigest: "sha256:" + Digest("foundation"), BoundContentDigest: prepared.BoundContentDigest,
                BoundSourceDigest: prepared.BoundSourceDigest, BoundMechanicsSnapshotDigest: prepared.BoundMechanicsSnapshotDigest,
                StoryCheckpoint: prepared);
            foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
            {
                int saves = 0, confirmations = 0;
                OriginBookReadingState saved = new("owner-a", "workspace-1", []);
                var localized = display with { State = display.State! with { Locale = locale } };
                OriginDossierLifeModuleDecisionPage Page() => new(localized, locale,
                    (_, _) => throw new InvalidOperationException("Details cannot prepare a module."),
                    (_, _) => { confirmations++; return Task.FromResult<OriginDossierLifeModulePhoneResult?>(null); },
                    openingDetails: saved, saveOpeningDetails: (expected, profile, current) =>
                    {
                        Require(current() && expected == saved, "A stale opening editor saved.");
                        saves++; saved = expected with { StoryProfile = profile };
                        return Task.FromResult<OriginBookReadingState?>(saved);
                    });
                var page = Page();
                T Find<T>(string id) where T : Element => Elements(page).OfType<T>().Single(e => e.AutomationId == id);
                Task Click(string id) => ui.BeginAsyncVoid(() => ((IButtonController)Find<Button>(id)).SendClicked());
                void Custom(string field, string text)
                {
                    var picker = Find<Picker>("origin-story-" + field + "-suggestions");
                    picker.SelectedIndex = picker.Items.Count - 1;
                    var entry = Find<Entry>("origin-story-" + field);
                    Require(entry.IsVisible, "Custom did not reveal its text field.");
                    entry.Text = text;
                }
                Require(!Find<VerticalStackLayout>("origin-story-details").IsVisible && saves == 0,
                    "Optional story details started expanded or wrote on render.");
                Require(Elements(page).OfType<Entry>().Where(e => e.AutomationId?.StartsWith("origin-story-") == true)
                    .All(e => !e.IsVisible), "An unspecified custom story field started visible.");
                Require(Find<Picker>("origin-story-period").SelectedIndex == 0
                    && Find<Picker>("origin-story-addiction-status").SelectedIndex == 0
                    && string.IsNullOrEmpty(Find<Entry>("origin-story-experiences").Text),
                    "An example or suggested status became preselected character history.");
                await Click("origin-life-confirm");
                Require(saves == 0 && confirmations == 1 && saved.StoryProfile is null,
                    "Skipping details blocked a decision or invented identity details.");
                ((IButtonController)Find<Button>("origin-story-details-expand")).SendClicked();
                Find<Picker>("origin-story-gender").SelectedIndex = 3;
                Custom("pronouns", "they/them");
                Find<Picker>("origin-story-tone").SelectedIndex = 4;
                Custom("motivation", "Find a home");
                Custom("family", "Seattle, an aunt");
                Custom("experiences", "A betrayal");
                Custom("addiction", "Gambling");
                Find<Picker>("origin-story-addiction-status").SelectedIndex = 3;
                Find<Picker>("origin-story-period").SelectedIndex = 3;
                Custom("chronology", "Only after leaving school");
                Custom("turning-points", "A broken promise");
                Custom("anchors", "A loyal friend");
                Require(saves == 0 && confirmations == 1, "Editing sent or saved unconfirmed details.");
                ((IButtonController)Find<Button>("origin-story-details-expand")).SendClicked();
                await Click("origin-life-confirm");
                Require(saves == 1 && confirmations == 2 && saved.StoryProfile is
                    { Gender: "other", Pronouns: "they/them", Tone: "epic", Motivation: "Find a home",
                      Background: { BirthplaceFamily: "Seattle, an aunt", Experiences: "A betrayal", AddictionHistory: "Gambling",
                          AddictionStatus: "recovery", Period: "adult", Chronology: "Only after leaving school",
                          TurningPoints: "A broken promise", PositiveAnchors: "A loyal friend" } },
                    "Collapsed details were lost or failed to save before the explicit decision.");
                page = Page();
                Require(!Find<VerticalStackLayout>("origin-story-details").IsVisible
                    && Find<Entry>("origin-story-pronouns").Text == "they/them"
                    && Find<Picker>("origin-story-gender").SelectedIndex == 3 && saves == 1
                    && Find<Entry>("origin-story-experiences").Text == "A betrayal"
                    && Find<Picker>("origin-story-addiction-status").SelectedIndex == 3
                    && Find<Picker>("origin-story-period").SelectedIndex == 3,
                    "Reopen lost saved details, changed defaults or saved automatically.");
                Require(!Elements(page).Any(e => e is Switch || e.AutomationId?.Contains("avoid", StringComparison.Ordinal) == true),
                    "Opening details introduced a mechanics toggle or avoidance setting.");
                var addiction = Find<Picker>("origin-story-addiction-suggestions");
                var copy = AndroidSurfaceStrings.Resolve(locale);
                Require(addiction.Items.Contains(copy["Origin.Suggestion.gambling"])
                    && addiction.Items.Contains(copy["Origin.Suggestion.shopping"]),
                    "Addiction suggestions omitted gambling or shopping.");
                addiction.SelectedIndex = addiction.Items.IndexOf(copy["Origin.Suggestion.shopping"]);
                Require(!Find<Entry>("origin-story-addiction").IsVisible,
                    "A preset left the custom text field visible.");
                // Even a stale hidden entry callback must not replace a preset.
                Find<Entry>("origin-story-addiction").Text = "Hidden stale input";
                await Click("origin-life-confirm");
                Require(saves == 2 && saved.StoryProfile?.Background?.AddictionHistory == copy["Origin.Suggestion.shopping"],
                    "A preset was not saved or was replaced by hidden custom text.");
                page = Page();
                Require(!Find<Entry>("origin-story-addiction").IsVisible
                    && Find<Picker>("origin-story-addiction-suggestions").SelectedIndex == 2,
                    "Reopen did not restore the selected shopping suggestion.");
                foreach (string id in new[] { "family", "experiences", "addiction", "chronology", "turning-points", "anchors" })
                    Find<Picker>("origin-story-" + id + "-suggestions").SelectedIndex = 0;
                Find<Picker>("origin-story-period").SelectedIndex = 0;
                Find<Picker>("origin-story-addiction-status").SelectedIndex = 0;
                await Click("origin-life-confirm");
                Require(saves == 3 && confirmations == 4 && saved.StoryProfile is { Background: null, Pronouns: "they/them" },
                    "Clearing optional background left an invalid empty object or erased unrelated opening details.");
            }
            ui.AssertHealthy();
        });
        Console.WriteLine("PASS optional opening UI: collapsed, skippable background/status/chronology, no preselected examples, explicit save/reopen/clear in DE/EN/ES");
    }

    private static async Task RunMetatypePageAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var authority = new DecisionAuthority(1);
            var original = authority.Current.LegalChoices.Single();
            LifeModuleDecisionAuthorityChoice Choice(string id, string metatype, decimal cost) => original with
            {
                ChoiceId = id, Label = metatype + " · Nationality", DecisionCommandDigest = Digest(id),
                MechanicsPreview = original.MechanicsPreview with
                {
                    KarmaCost = cost, KarmaRaw = cost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Items = [new("foundation:requested-metatype", "metatype-choice", metatype,
                        string.Empty, metatype, 0, ["metatypes.xml#" + metatype], string.Empty),
                        .. original.MechanicsPreview.Items]
                }
            };
            authority.Current = authority.Current with { LegalChoices = [Choice("human", "Human", 15), Choice("elf", "Elf", 55)] };
            var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var started = interaction.Start("workspace-1").Value!;
            OriginDossierLifeModulePhoneResult Display(LifeModuleOriginDossierDraftCheckpoint checkpoint) => new(
                LifeModuleOriginDossierOutcomes.Success, OriginDossierLifeModuleInteractionProjector.Project(checkpoint), [],
                LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                FoundationSnapshotDigest: "sha256:" + Digest("foundation"),
                BoundContentDigest: checkpoint.BoundContentDigest, BoundSourceDigest: checkpoint.BoundSourceDigest,
                BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest, StoryCheckpoint: checkpoint);
            int prepared = 0, confirmed = 0;
            var initialDisplay = Display(started);
            string ChoiceControl(string choiceId) => "origin-life-choice-" + Array.FindIndex(
                initialDisplay.State!.Choices.ToArray(), choice => choice.ChoiceId == choiceId);
            string humanControl = ChoiceControl("human"), elfControl = ChoiceControl("elf");
            var page = new OriginDossierLifeModuleDecisionPage(initialDisplay, "en-US", (choiceId, answers) =>
            {
                prepared++;
                return Task.FromResult<OriginDossierLifeModulePhoneResult?>(Display(interaction.Prepare(started, choiceId).Value!));
            }, (_, _) => { confirmed++; return Task.FromResult<OriginDossierLifeModulePhoneResult?>(null); });
            Button Button(string id) => Elements(page).OfType<Button>().Single(button => button.AutomationId == id);
            bool Visible(string id) => Elements(page).Any(element => element.AutomationId == id);
            async Task Click(Button button) => await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());

            Require(!Visible(humanControl) && !Visible(elfControl) && prepared == 0,
                "The phone silently chose a default metatype.");
            ((IButtonController)Button("origin-life-metatype-Elf")).SendClicked();
            Require(Visible(elfControl) && !Visible(humanControl) && prepared == 0,
                "Metatype filtering exposed the wrong nationality or dispatched a mechanics action.");
            var oldElf = Button(elfControl);
            await Click(oldElf);
            Require(prepared == 1 && Visible("origin-life-confirm") && confirmed == 0, "Explicit preview was skipped.");
            var oldConfirm = Button("origin-life-confirm");
            ((IButtonController)Button("origin-life-metatype-Human")).SendClicked();
            Require(!Visible("origin-life-confirm") && Visible(humanControl),
                "A hidden Elf preview remained confirmable after choosing Human.");
            await Click(oldConfirm);
            await Click(oldElf);
            Require(confirmed == 0 && prepared == 1, "A stale detached control dispatched an action.");
            await Click(Button(humanControl));
            await Click(Button("origin-life-confirm"));
            Require(prepared == 2 && confirmed == 1 && authority.MutationCount == 0,
                "Rendering applied rules itself or failed to use the supplied confirmation boundary.");
            var departedConfirm = Button("origin-life-confirm");
            typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnDisappearing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(page, null);
            await Click(departedConfirm);
            Require(confirmed == 1, "A control from a departed page dispatched confirmation.");
            typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnAppearing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(page, null);
            Require(Visible("origin-life-confirm"), "Reopening did not issue fresh controls for the reviewed choice.");
            ui.AssertHealthy();
            Console.WriteLine("PASS Origin book: explicit metatype filter, preview, stale-control rejection");
        });
    }

    private static IEnumerable<Element> Elements(Element root)
    {
        yield return root;
        IEnumerable<Element> children = root switch
        {
            ContentPage page when page.Content is not null => [page.Content],
            ScrollView scroll when scroll.Content is not null => [scroll.Content],
            Border border when border.Content is not null => [border.Content],
            Layout layout => layout.Children.OfType<Element>(), _ => []
        };
        foreach (Element child in children)
        foreach (Element descendant in Elements(child)) yield return descendant;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FaultStore(IOriginDossierDraftTimelineStore inner) : IOriginDossierDraftTimelineStore
    {
        public bool FailNextSave { get; set; }
        public Task<LifeModuleOriginDossierDraftCheckpoint?> LoadAsync(string owner, string workspace, CancellationToken ct = default)
            => inner.LoadAsync(owner, workspace, ct);
        public Task SaveAsync(LifeModuleOriginDossierDraftCheckpoint checkpoint, CancellationToken ct = default)
        {
            if (FailNextSave) { FailNextSave = false; throw new IOException("Injected chapter write failure"); }
            return inner.SaveAsync(checkpoint, ct);
        }
        public Task DeleteAsync(string owner, string workspace, CancellationToken ct = default)
            => throw new InvalidOperationException("Confirmed story must never be deleted by the wizard.");
    }

    // A deterministic authority double exercises the real interaction, Core
    // projection and Android storage. It does not claim full Life Modules rules.
    private sealed class DecisionAuthority(int terminalAfter) : ILifeModuleDecisionAuthority, ILifeModuleDecisionInputAuthority,
        ILifeModuleDecisionEffectReviewAuthority
    {
        private readonly Dictionary<string, LifeModuleDecisionAcceptance> _accepted = new(StringComparer.Ordinal);
        public int MutationCount { get; private set; }
        public Action? AfterCommit { get; set; }
        public IReadOnlyList<LifeModuleEffectContribution> Contributions { get; set; } =
            [new("fixture-effect", "skilllevel", "fixture-skill", "Etiquette", 1, string.Empty,
                new Dictionary<string, string>(), ["lifemodules.xml#module:fixture"],
                CharacterCreationFoundationEffectCompilationStatuses.Supported, null)];
        public LifeModuleDecisionAuthorityStep Current { get; set; } = new(
            OriginDossierSchemas.DecisionAuthorityStepV1, "sr5", "workspace-1", 1,
            "local-single-user", "runner-1", "Neon", "en-US", "journey-1", "nationality", 1, "turn-1", 1,
            "A runner's story begins.", "Which path?", [Choice(1)], [], [],
            LifeModuleOriginDossierService.TurnLedgerRootDigest, Digest("graph-1"), Digest("decision-1"),
            Digest("content-1"), Digest("source"), Digest("rules"), Digest("runtime"), Digest("mechanics-0"));

        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> Load(string workspaceId)
            => new(LifeModuleOriginDossierOutcomes.Success, Current, []);

        public LifeModuleDecisionAuthorityResult<LifeModuleEffectReview> ReviewEffects(LifeModuleDecisionInputRequest request)
            => new(LifeModuleOriginDossierOutcomes.Success, LifeModuleEffectReviewIntegrity.Seal(new(request,
                "sha256:" + Digest("fixture-preview"), "sha256:" + Digest("fixture-compiler"), "sha256:" + Digest("fixture-compilation"),
                Contributions, string.Empty)), []);
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> FindAcceptance(string workspaceId, string key)
            => _accepted.TryGetValue(key, out var found)
                ? new(LifeModuleOriginDossierOutcomes.Success, found, [])
                : new(LifeModuleOriginDossierOutcomes.Missing, null, []);

        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionInputResolution> ResolveInputs(LifeModuleDecisionInputRequest request)
        {
            var choice = Current.LegalChoices.Single(item => item.ChoiceId == request.ChoiceId);
            Require(request.WorkspaceRevision == Current.WorkspaceRevision, "Input request was stale.");
            var projection = new LifeModuleOriginDossierService(new DecisionAuthority(3) { Current = Current with
            {
                LegalChoices = [choice with { MechanicsPreview = choice.MechanicsPreview with { PendingFollowUpIds = [] } }]
            }}).Project(request.WorkspaceId).Value!;
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in request.Values) sorted.Add(item.Key, item.Value);
            var result = new LifeModuleDecisionInputResolution(request.WorkspaceId, request.WorkspaceRevision,
                request.ChoiceId, request.DecisionDigest, request.DecisionCommandDigest, sorted,
                "sha256:" + Digest("fixture-resolved"), projection.CurrentTurn.LegalChoices.Single().MechanicsPreview, string.Empty);
            result = result with { ResolutionDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result))).ToLowerInvariant() };
            return new(LifeModuleOriginDossierOutcomes.Success, result, []);
        }

        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(LifeModuleDecisionAcceptanceCommand command)
        {
            Require(command.WorkspaceRevision == Current.WorkspaceRevision
                && command.ExpectedContentDigest == Current.ContentDigest, "Authority received stale command.");
            int number = ++MutationCount;
            string id = "decision-" + number;
            var fact = new OriginCanonicalNarrativeFact("fact-" + number, "accepted-life-module",
                "Accepted fact " + number, id, ["lifemodules.xml#module:" + number], string.Empty);
            var receipt = new LifeModuleAcceptedDecisionReceipt(
                OriginDossierSchemas.AcceptedDecisionReceiptV1, id, command.ChoiceId,
                command.DecisionCommandDigest, command.IdempotencyKeyDigest,
                Current.WorkspaceRevision, Current.WorkspaceRevision + 1,
                Current.ContentDigest, Digest("content-" + (number + 1)), Current.SourceDigest, Current.RulesDigest,
                Current.RuntimeDigest, Current.DecisionDigest, Current.MechanicsSnapshotDigest,
                Digest("graph-" + (number + 1)), Digest("mechanics-" + number),
                "Accepted consequence " + number + ".", [fact], Digest("receipt-" + number))
            { InputResolutionDigest = command.InputResolution?.ResolutionDigest };
            Current = Current with
            {
                WorkspaceRevision = receipt.WorkspaceRevision,
                StageId = "stage-" + (number + 1), StageOrder = number + 1,
                TurnId = "turn-" + (number + 1), TurnSequence = number + 1,
                DecisionLeadInMarkdown = "The next scene begins.", DecisionPrompt = "Continue?",
                LegalChoices = number == terminalAfter ? [] : [Choice(number + 1)],
                CanonicalFacts = [.. Current.CanonicalFacts, fact], AcceptedDecisionIds = [.. Current.AcceptedDecisionIds, id],
                PreviousTurnDigest = command.ExpectedTurnSeedDigest,
                DecisionGraphDigest = receipt.AcceptedDecisionGraphDigest, DecisionDigest = Digest("decision-" + (number + 1)),
                ContentDigest = receipt.ContentDigest, MechanicsSnapshotDigest = receipt.MechanicsSnapshotDigest,
                IsTerminal = number == terminalAfter
            };
            var acceptance = new LifeModuleDecisionAcceptance(receipt, Current);
            _accepted.Add(command.IdempotencyKeyDigest, acceptance);
            AfterCommit?.Invoke();
            return new(LifeModuleOriginDossierOutcomes.Success, acceptance, []);
        }

        private static LifeModuleDecisionAuthorityChoice Choice(int number)
        {
            string anchor = "lifemodules.xml#module:" + number;
            var preview = new LifeModuleMechanicsPreview(15, "15", true,
                [new("effect-" + number, "active-skill", "Etiquette", "0", "1", 0, [anchor], string.Empty)],
                [], [anchor], string.Empty);
            return new("choice-" + number, "Path " + number, "RF", "66", Digest("command-" + number), preview, [anchor], [], true);
        }
    }
}
