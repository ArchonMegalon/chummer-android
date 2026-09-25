using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private static readonly OwnerContextStamp TestOwner = new(OwnerScope.LocalSingleUser, "origin-test-owner", 0);
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
        RunFinishedSelectionDisplay();
        await RunEffectContributionDisplayAsync();
        await RunMetatypePageAsync();
        await RunConfirmOffUiContextAsync();
        await RunLiveContinuationPageAsync();
        await RunFinishChoiceOrderingAsync();
        await RunFollowUpPageAsync();
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
                && retained.ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains(System.Net.WebUtility.HtmlEncode(text), StringComparison.Ordinal),
                "Career reader and private export disagree about the legacy chapter.");
        }
        var dollarAnswer = fixture with { CanonicalLayer = fixture.CanonicalLayer with { Facts = [
            new("literal", "accepted-life-module-answer", "Nickname: $real <script>", original.ThroughAcceptedDecisionId, [], "")
        ] } };
        Require(OriginBookChapterText.Render(dollarAnswer, legacy).Contains("Nickname: $real <script>", StringComparison.Ordinal)
            && new RetainedOriginBook(dollarAnswer).ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains("&lt;script&gt;", StringComparison.Ordinal),
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
                && retained.ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains(System.Net.WebUtility.HtmlEncode(text), StringComparison.Ordinal),
                "Career reading and HTML export disagree about completed selection.");
            Require(JsonSerializer.Serialize(localized) == original, "Rendering rewrote the archived chapter, fact or digest.");

            var prose = OriginBookProseDraft.Create(chapter, locale, "reviewed-finish", new string('b', 64),
                "I chose my own path. <script>literal prose</script>");
            var selected = new RetainedOriginBook(localized, new("local-single-user", "workspace-1", [new(chapter.ChapterId, prose, null)]));
            Require(selected.ChapterText(chapter) == prose.Text
                && selected.ToHtml(AndroidSurfaceStrings.Resolve("de")).Contains(System.Net.WebUtility.HtmlEncode(prose.Text), StringComparison.Ordinal),
                "Finish display rewrote reader-approved narration or exported executable markup.");
            var pending = new RetainedOriginBook(localized, new("local-single-user", "workspace-1", [new(chapter.ChapterId, null, prose)]));
            Require(pending.ChapterText(chapter) == text, "Unconfirmed narration replaced the completed-selection display.");
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

    public static void RunAuthoringSource()
    {
        var service = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(new DecisionAuthority(1)));
        var opened = service.Start("workspace-1").Value!;
        var prepared = service.Prepare(opened, "choice-1").Value!;
        var book = service.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "authoring-source-fixture", true).Value!.Checkpoint.Projection;
        var chapter = book.VisibleChapters.Single();
        var fixture = book with
        {
            AllowedCanonicalFactIds = ["metatype", "answer", "future", "private"],
            CanonicalLayer = book.CanonicalLayer with
            {
                AcceptedDecisionIds = [chapter.ThroughAcceptedDecisionId, "later"],
                Facts = [
                    new("metatype", "accepted-metatype", "Elf", chapter.ThroughAcceptedDecisionId, ["private-anchor"], ""),
                    new("answer", "accepted-life-module-answer", "Childhood: Renraku", chapter.ThroughAcceptedDecisionId, [], ""),
                    new("future", "accepted-life-module-answer", "Future not chosen in this chapter", "later", [], ""),
                    new("private", "private-notes", "Do not send", chapter.ThroughAcceptedDecisionId, [], ""),
                    new("not-allowed", "accepted-life-module-answer", "Not approved", chapter.ThroughAcceptedDecisionId, [], "")
                ]
            }
        };
        string before = JsonSerializer.Serialize(fixture);
        var source = OriginBookAuthoringSource.Create(fixture, chapter);
        Require(source.Facts.Count == 2 && source.Facts.All(f => f.DecisionId == chapter.ThroughAcceptedDecisionId),
            "Provider input leaked a later, private or non-allowlisted fact.");
        string serialized = JsonSerializer.Serialize(source);
        Require(!serialized.Contains("private-anchor", StringComparison.Ordinal)
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
        Require(!new RetainedOriginBook(chronological, pendingOnly).TryGetAuthoringPredecessor(nextChapter, out _),
            "A pending draft was treated as a selected reading.");
        Require(!new RetainedOriginBook(chronological with { VisibleChapters = [chapter, nextChapter with
            { ThroughAcceptedDecisionId = chapter.ThroughAcceptedDecisionId }] }, readings)
            .TryGetAuthoringPredecessor(chapter, out _), "Ambiguous Core chapter boundaries were guessed.");
        Console.WriteLine("PASS Origin authoring projection: confirmed facts only, no future choices, source prose, anchors or mutation");
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
                Require(Visible("origin-life-effect-" + selectedIndex + "-0")
                    && Visible("origin-life-choice-anchors-" + selectedIndex),
                    "Selection did not expand the exact effects and source anchors for review.");
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

    private static async Task RunEffectContributionDisplayAsync()
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
                string Effect(int index) => Elements(page).OfType<Label>().Single(label =>
                    label.AutomationId == "origin-life-effect-0-" + index).Text;
                Require(Effect(0) == copy.Format("Origin.AttributeContribution", "LOG", "+1")
                    && Effect(1) == copy.Format("Origin.SkillContribution", "Survival", "+1")
                    && Effect(2) == copy.Format("Origin.KnowledgePoolContribution", "+1")
                    && Effect(3) == copy.Format("Origin.QualityLevelContribution", "SINner (National)", "1")
                    && Effect(4) == copy.Format("Origin.SelectionContribution", "Salish-Shidhe")
                    && Effect(5) == copy.Format("Origin.EffectPending", "Unresolved effect"),
                    "The native review lost exact Core contributions, quality identity, or pool/selection semantics.");
                Require(Elements(page).OfType<Label>().Any(label => label.Text == copy.Format("Origin.EffectSourceContext", "History"))
                    && Elements(page).OfType<Label>().Any(label => label.Text == copy["Origin.ContributionScope"])
                    && Elements(page).OfType<Button>().Single(button => button.AutomationId == "origin-life-confirm").IsEnabled,
                    "The reviewed page confused descriptive metadata with grants or lost confirmation.");
                Require(JsonSerializer.Serialize(checkpoint) == before && authority.MutationCount == 0,
                    "Rendering rewrote the reviewed checkpoint or applied mechanics.");

                // Display-only historical fixture. Actual legacy restore and
                // re-prepare admission are exercised against real Core storage.
                var legacy = Page(checkpoint with { PendingPreview = checkpoint.PendingPreview! with { EffectReview = null } });
                var oldConfirm = Elements(legacy).OfType<Button>().Single(button => button.AutomationId == "origin-life-confirm");
                Require(!oldConfirm.IsEnabled && Elements(legacy).OfType<Label>().Any(label => label.Text == copy["Origin.EffectReviewRequired"])
                    && !Elements(legacy).Any(element => element.AutomationId?.StartsWith("origin-life-effect-", StringComparison.Ordinal) == true),
                    "A legacy raw preview was shown as compiler-reviewed or remained confirmable.");
                ((IButtonController)oldConfirm).SendClicked();
                await Task.Yield();
                Require(confirmations == 0, "A stale preview dispatched confirmation through a disabled control.");
            }
        });
        Console.WriteLine("PASS Origin book: exact contribution display in DE/EN/ES; legacy preview requires review, no rendering mutation");
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
