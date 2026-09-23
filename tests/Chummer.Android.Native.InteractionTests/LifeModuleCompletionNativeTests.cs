using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.LifeModules;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Chummer.Presentation.OriginBooks;
using System.Text.Json;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleCompletionPagesAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            var bookOutput = new LifeBookOutputProbe();
            var authoringAccount = System.Reflection.DispatchProxy.Create<IAndroidAccountLinkService, OriginAuthoringPageAccount>();
            var authoringProbe = (OriginAuthoringPageAccount)authoringAccount;
            LifeCompletionNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, outputDocuments: bookOutput,
                accountService: authoringAccount,
                lifeCompletionDecorator: actual => probe = new(actual));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Life Modules phone choices", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeStory(runtime, id));
            await runtime.Presenter.LoadAsync(id, default);
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var drafts = new LifeModuleCompletionDraftStore(runtime.StateDirectory);
            var root = new LifeModuleCompletionPage(runtime.Coordinator, store: drafts);
            var navigation = new NavigationPage(new ContentPage());
            await navigation.PushAsync(root, false);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(root, window);
            await alerts.PreflightAsync();
            await Appear();
            Require(Session().Input is { TalentSelection: null, AttributePurchases: null, SkillSelection: null,
                GearSelection: null, LifestyleSelection: null, ContactSelection: null, MagicSelection: null },
                "Opening the completion wizard silently chose a talent or empty purchases.");
            var initialSession = Session();
            var initialPreview = initialSession.Preview;
            int openingPreviewCalls = probe!.PreviewCalls;
            await Click("life-open-qualities");
            Require(ReferenceEquals(Session().Preview, initialPreview) && probe.PreviewCalls == openingPreviewCalls,
                "Opening an unchanged child page repeated the expensive Core preview.");
            var qualityInputs = IssuedElements(Current()).OfType<Entry>()
                .Where(e => e.AutomationId?.StartsWith("life-quality-", StringComparison.Ordinal) == true).ToArray();
            var dependentInputs = Session().Preview!.ModuleSequence!.DependentQualityInstances!;
            Require(dependentInputs.Count == 2 && qualityInputs.Length == 3,
                "Military School requires both dependent quality inputs as well as the corporate SIN.");
            foreach (var entry in qualityInputs)
            {
                var dependent = dependentInputs.SingleOrDefault(row => entry.AutomationId == "life-quality-" + row.InstancePrompt.PromptId);
                entry.Text = dependent is null ? "Renraku"
                    : dependent.InstancePrompt.Label == "Code of Honor" ? "Protect noncombatants" : "Academy cadet";
            }
            await Click("life-review-qualities");
            Require(probe.PreviewCalls == openingPreviewCalls + 1 && !ReferenceEquals(Session().Preview, initialPreview),
                "Changed quality inputs reused the earlier review.");
            await Back();
            Require(probe.PreviewCalls == openingPreviewCalls + 1,
                "Returning to the overview repeated an unchanged saved review.");
            await Click("life-open-talent");
            await Click("life-talent-mundane");
            await Back();
            await Click("life-open-attributes");
            await Click("life-begin-attributes");
            var oldAgility = Element<Stepper>("life-attribute-AGI");
            oldAgility.Value = 1;
            await Click("life-review-attributes");
            await Back();
            int reviewedAttributeCalls = probe.PreviewCalls;
            await Click("life-open-attributes");
            Require(probe.PreviewCalls == reviewedAttributeCalls,
                "Reopening reviewed attributes repeated the Core preview.");
            oldAgility.Value = 2;
            Require(Element<Stepper>("life-attribute-AGI").Value == 1, "Departed attribute control changed the current draft.");
            await Back();
            await Click("life-open-skills");
            await Click("life-begin-skills");
            Element<SearchBar>("life-search").Text = "English";
            await Click("life-search-go");
            var english = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "English");
            await Click(english.AutomationId);
            Element<Switch>("life-native-language").IsToggled = true;
            await Click("life-use-skill");
            await Back();
            await Click("life-open-resources");
            Element<Entry>("life-resource-investment").Text = "0";
            await Click("life-use-resources");
            await Back();
            await Click("life-open-gear"); await Click("life-begin-gear"); await Back();
            await Click("life-open-lifestyles"); await Click("life-begin-lifestyles"); await Back();
            await Click("life-open-contacts"); await Click("life-begin-contacts");
            await Click("life-add-contact");
            Element<Entry>("life-contact-name").Text = "Mara";
            Element<Entry>("life-contact-role").Text = "Fixer";
            await Click("life-use-contact"); await Back();
            await Click("life-open-magic"); await Click("life-begin-magic"); await Back();
            Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision
                && probe!.ConfirmCalls == 0, "Draft phone inputs changed the runner before final confirmation.");
            string expected = JsonSerializer.Serialize(Session().Input);
            var retainedInput = Session().Input!;
            IssuedPageLifecycle(Current(), "OnDisappearing");
            string ownerKey = runtime.Coordinator.State.DisplayOwnerContext!.Value.Owner.Value;
            var staleInput = retainedInput with { Binding = retainedInput.Binding with { ContentRevision = retainedInput.Binding.ContentRevision + 1 } };
            await drafts.SaveAsync(ownerKey, staleInput, default);
            var staleSession = new LifeModuleCompletionSession(runtime.Coordinator, drafts);
            await staleSession.OpenAsync(default, () => true);
            int supersededPreviewCalls = probe.PreviewCalls;
            await initialSession.OpenAsync(default, () => true);
            Require(!initialSession.Reviewed && !initialSession.CanConfirm && probe.PreviewCalls == supersededPreviewCalls,
                "A superseded session reused or reissued the retired review.");
            Require(staleSession.Halted && staleSession.CanDiscardInputDraft && !staleSession.CanConfirm
                && JsonSerializer.Serialize(await drafts.LoadAsync(ownerKey, id, default)) == JsonSerializer.Serialize(staleInput),
                "Stale persisted choices were silently rebound or overwritten.");
            await staleSession.DiscardInputDraftAsync(default, () => true);
            Require(staleSession.Input?.TalentSelection is null && staleSession.Ready
                && new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision,
                "Explicit input discard altered modules/runner or failed to reacquire review.");
            // Restore the player's original test choices, not a cached preview or write permission.
            await drafts.SaveAsync(ownerKey, retainedInput, default);
            // A fresh page/session/store reads only inputs and obtains a new Core review.
            // This is managed cold reopen, not Android force-stop evidence.
            int beforeColdReopenCalls = probe.PreviewCalls;
            var reopened = new LifeModuleCompletionPage(runtime.Coordinator, store: new(runtime.StateDirectory));
            await navigation.PushAsync(reopened, false); await Appear();
            Require(JsonSerializer.Serialize(Session().Input) == expected && Session().Reviewed
                && probe.PreviewCalls == beforeColdReopenCalls + 1,
                "Cold phone reopen lost exact choices or reused old review authority.");
            Require(await drafts.LoadAsync("different-owner", id, default) is null, "Input storage crossed owner namespaces.");
            await Click("life-open-review");
            Element<Entry>("life-starting-dice").Text = "6";
            await Click("life-review-completion");
            Require(Session().CanConfirm, "Phone-selected Life Modules runner is blocked: " + string.Join(", ", Session().Blockers));
            Require(!Element<Button>("life-confirm-completion").IsEnabled, "Completion lacks explicit user acknowledgement.");
            Element<Switch>("life-completion-confirmed").IsToggled = true;
            var oldConfirm = Element<Button>("life-confirm-completion");
            Element<Entry>("life-starting-dice").Text = "5";
            await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
            Require(probe!.ConfirmCalls == 0, "Changed visible dice accepted the previous final review.");
            Element<Entry>("life-starting-dice").Text = "6";
            await Click("life-review-completion");
            Element<Switch>("life-completion-confirmed").IsToggled = true;
            var finalConfirm = Element<Button>("life-confirm-completion");
            var finalAcknowledgement = Element<Switch>("life-completion-confirmed");
            var saving = Element<ActivityIndicator>("life-completion-saving");
            var confirmationRow = finalConfirm.Parent as Grid;
            Require(confirmationRow is not null && ReferenceEquals(saving.Parent, confirmationRow)
                && Grid.GetRow(finalConfirm) == Grid.GetRow(saving)
                && Grid.GetColumn(finalConfirm) != Grid.GetColumn(saving)
                && confirmationRow.ColumnDefinitions[Grid.GetColumn(saving)].Width.IsAbsolute
                && confirmationRow.HeightRequest == finalConfirm.HeightRequest
                && saving.HeightRequest > 0 && saving.HeightRequest <= confirmationRow.HeightRequest,
                "The saving indicator must share a stable-height confirmation row, not insert a new scroll row.");
            double confirmationRowHeight = confirmationRow!.HeightRequest;
            using var releaseConfirmation = new ManualResetEventSlim();
            var confirmationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.BeforeConfirm = () =>
            {
                confirmationEntered.TrySetResult();
                Require(releaseConfirmation.Wait(TimeSpan.FromSeconds(20)), "Test did not release the blocked Core confirmation.");
            };
            Task confirmation = ui.BeginAsyncVoid(() => ((IButtonController)finalConfirm).SendClicked());
            try
            {
                await confirmationEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Require(!finalConfirm.IsEnabled && !finalAcknowledgement.IsEnabled
                    && !Element<Entry>("life-starting-dice").IsEnabled,
                    "Pending Core confirmation still presents enabled confirmation/input controls.");
                Require(saving.IsVisible && saving.IsRunning && finalConfirm.Text != CreationKarmaCopy.ConfirmCompletion,
                    "Pending Core confirmation has no visible saving feedback.");
                Require(ReferenceEquals(finalConfirm.Parent, confirmationRow)
                    && ReferenceEquals(saving.Parent, confirmationRow)
                    && confirmationRow.HeightRequest == confirmationRowHeight,
                    "Showing save feedback replaced or expanded the confirmation row.");
                finalAcknowledgement.IsToggled = false;
                finalAcknowledgement.IsToggled = true;
                ((IButtonController)finalConfirm).SendClicked();
                Require(!finalConfirm.IsEnabled && probe.ConfirmCalls == 1,
                    "A pending confirmation was visually rearmed or dispatched twice.");
            }
            finally
            {
                releaseConfirmation.Set();
                await confirmation;
                probe.BeforeConfirm = null;
            }
            Require(Element<Label>("life-completion-saved").Text == CreationKarmaCopy.CareerReady && probe!.ConfirmCalls == 1,
                "Phone confirmation did not reopen the exact Career runner.");
            var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var finalQualities = System.Xml.Linq.XDocument.Parse(cold.Document.Content).Root!
                .Element("qualities")!.Elements("quality").ToArray();
            Require(finalQualities.Single(row => row.Element("name")?.Value == "Code of Honor").Element("extra")?.Value == "Protect noncombatants"
                && finalQualities.Single(row => row.Element("name")?.Value == "Rank (Neither Military nor Law Enforcement) I").Element("extra")?.Value == "Academy cadet",
                "Cold Career reopen lost or swapped explicit dependent-quality descriptions.");
            Require(cold.ContentRevision == before.ContentRevision + 1 && cold.SavedRevision == cold.ContentRevision
                && cold.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null,
                "Phone completion was not one durable Career transition.");
            IssuedPageLifecycle(Current(), "OnDisappearing");
            await VerifyRetainedBookAsync();
            ui.AssertHealthy();
            Console.WriteLine("PASS Life Modules phone pages: choices, input recovery, Career, retained book/cold read, HTML export and stale-owner rejection");

            async Task VerifyRetainedBookAsync()
            {
                Require(!runtime.Coordinator.CanOpenLifeModuleCompletion(), "Career reopened mutable Creation.");
                var owner = owners.Capture();
                var service = new OwnerBoundLifeModuleBookService(new FileWorkspaceStore(runtime.StateDirectory), owners);
                var projected = service.Load(owner, id, cold.ContentRevision, cold.SavedRevision);
                Require(projected.Value is { VisibleChapters.Count: > 0 },
                    "Career book did not recover from the cold Core store: " + string.Join(", ", projected.Blockers));
                var finishFact = projected.Value!.CanonicalLayer.Facts.Single(fact => fact.FactKind == "accepted-module-selection-finish");
                var finishChapter = projected.Value.VisibleChapters.Single(chapter => chapter.ThroughAcceptedDecisionId == finishFact.AcceptedDecisionId);
                string finishText = OriginBookChapterText.Render(projected.Value, finishChapter);
                Require(projected.Value.CurrentTurn.Locale == "de-DE"
                    && finishText != finishChapter.VisibleMarkdown && !finishText.Contains("**", StringComparison.Ordinal)
                    && finishText.StartsWith("Deine Modulauswahl ist abgeschlossen.", StringComparison.Ordinal),
                    "The real saved finish-selection chapter still contains the old Creation instructions.");
                Require(service.Load(owner, id, cold.ContentRevision + 1, cold.SavedRevision).Value is null,
                    "Book accepted the wrong current revision.");
                var runner = new BuildPage(runtime.Coordinator);
                // Headless MAUI has no native ScrollView handler to acknowledge
                // the landing page's scroll-to-top. This supplies geometry-only
                // completion, not lifecycle, navigation or book authority.
                var scroll = (ScrollView)runner.Content!;
                scroll.ScrollToRequested += (_, _) => scroll.SendScrollFinished();
                Console.WriteLine("Life book: opening the real phone Runner entry.");
                await navigation.PushAsync(runner, false); await Appear();
                Require(!IssuedElements(runner).Any(element => element.AutomationId == "build-origin-dossier"),
                    "Reading the Career book restored the deferred generic editing surface.");
                var openBook = Element<Button>("build-retained-origin-book");
                await Click("build-retained-origin-book");
                Require(Current() is RetainedOriginBookPage, "Phone Runner entry did not open the retained book.");
                var page = (RetainedOriginBookPage)Current();
                Require(IssuedElements(page).OfType<Label>().Any(label => label.AutomationId?.StartsWith("origin-retained-chapter-", StringComparison.Ordinal) == true),
                    "Career book page has no saved prose.");
                Require(Element<Label>($"origin-retained-chapter-{finishChapter.Sequence}").Text == finishText,
                    "The native Career reader did not use the completed-selection display.");
                Require(!IssuedElements(page).Any(element => element.AutomationId == "origin-book-account"),
                    "A linked reader was asked to link again.");
                Button? retiredAccountButton = null;
                foreach (string locale in new[] { "de-DE", "en-US", "es-ES" })
                {
                    authoringProbe.Status = AndroidAccountLinkStatus.Unlinked;
                    var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
                    RetainedOriginBookPage unlinkedReader;
                    try
                    {
                        System.Globalization.CultureInfo.CurrentUICulture = new(locale);
                        unlinkedReader = new RetainedOriginBookPage(runtime.Coordinator);
                    }
                    finally { System.Globalization.CultureInfo.CurrentUICulture = previousCulture; }
                    await navigation.PushAsync(unlinkedReader, false); await Appear();
                    var localizedCopy = AndroidSurfaceStrings.Resolve(locale);
                    Require(Element<Label>("origin-book-account-explanation").Text == localizedCopy["Origin.BookAccountExplanation"],
                        "The unlinked reader did not explain the account requirement in its UI language.");
                    Require(Element<Button>("origin-book-export").IsEnabled
                        && !IssuedElements(Current()).Any(e => e.AutomationId?.StartsWith("origin-author-chapter-", StringComparison.Ordinal) == true),
                        "An unlinked reader lost local export or exposed a generation action.");
                    retiredAccountButton = Element<Button>("origin-book-account");
                    Require(retiredAccountButton.Text == localizedCopy["Origin.BookAccount"], "Account navigation was not localized.");
                    authoringProbe.Status = AndroidAccountLinkStatus.Loading;
                    typeof(RetainedOriginBookPage).GetMethod("Refresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(unlinkedReader, null);
                    Require(!Element<Button>("origin-book-account").IsEnabled, "Account loading exposed an enabled account action.");
                    authoringProbe.Status = AndroidAccountLinkStatus.Unlinked;
                    typeof(RetainedOriginBookPage).GetMethod("Refresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(unlinkedReader, null);
                    await Click("origin-book-account");
                    Require(Current() is AccountPrivacyPage && authoringProbe.LinkStarts == 0
                        && authoringProbe.Requests == 0 && authoringProbe.Reads == 0,
                        "Opening account settings started authentication or sent chapter facts.");
                    await Back();
                    Require(Current() is RetainedOriginBookPage && Element<Button>("origin-book-export").IsEnabled,
                        "Returning from account settings lost the saved book/export.");
                    await Back();
                }
                authoringProbe.Status = AndroidAccountLinkStatus.Linked;
                typeof(RetainedOriginBookPage).GetMethod("Refresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, null);
                int beforeRetiredAccountClick = navigation.Navigation.NavigationStack.Count;
                await ui.BeginAsyncVoid(() => ((IButtonController)retiredAccountButton!).SendClicked());
                Require(navigation.Navigation.NavigationStack.Count == beforeRetiredAccountClick && authoringProbe.LinkStarts == 0,
                    "A departed book action reopened account settings or began authentication.");
                Console.WriteLine("PASS unlinked book: DE/EN/ES account explanation, explicit navigation, offline export and no automatic sign-in/generation");
                await Click("origin-book-export");
                Require(bookOutput.Deliveries == 1, "The context-bound HTML export was not delivered.");
                Require(bookOutput.Html.Contains("<meta name=\"author\" content=\"chummer.run\">", StringComparison.Ordinal),
                    "Private HTML export lost the technical author.");
                Require(bookOutput.Html.Contains(System.Net.WebUtility.HtmlEncode(finishText), StringComparison.Ordinal),
                    "The exported book restored the stale finish-selection instructions.");
                Require(bookOutput.Html.Contains("<h1>" + System.Net.WebUtility.HtmlEncode(projected.Value!.CurrentTurn.RunnerDisplayName) + "</h1>", StringComparison.Ordinal),
                    "Private HTML export lost the Core-issued runner display name.");
                var malicious = projected.Value! with
                {
                    CurrentTurn = projected.Value!.CurrentTurn with { RunnerDisplayName = "<script>runner</script>" },
                    VisibleChapters = projected.Value!.VisibleChapters.Select(chapter => chapter with
                        { VisibleMarkdown = "<script>alert(1)</script><img src='https://invalid.example/pixel'>" }).ToArray()
                };
                string escaped = new RetainedOriginBook(malicious).ToHtml(AndroidSurfaceStrings.Resolve("de"));
                Require(!escaped.Contains("<script>", StringComparison.Ordinal) && !escaped.Contains("<img ", StringComparison.Ordinal)
                    && escaped.Contains("&lt;script&gt;", StringComparison.Ordinal), "Book export executed untrusted prose markup.");
                var book = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                Require(book is not null, "Retained book load failed.");
                var chapter = book!.Chapters[0];
                string canonicalText = book.ChapterText(chapter);
                var proposal = OriginBookProseDraft.Create(chapter, book.Locale, "synthetic-review-job",
                    new string('a', 64), "Synthetic proposed chapter. <script>not executable</script>");
                Require(await runtime.Coordinator.StageOriginBookProseDraftAsync(book,
                        proposal with { Text = "changed after sealing" }, () => true, default) is null,
                    "An edited proposal was admitted with its old digest.");
                var wrongChapter = OriginBookProseDraft.Create(chapter with { ChapterDigest = new string('c', 64) },
                    book.Locale, "synthetic-wrong-chapter", new string('a', 64), "Unrelated chapter.");
                Require(await runtime.Coordinator.StageOriginBookProseDraftAsync(book, wrongChapter, () => true, default) is null,
                    "A valid proposal for different chapter bytes was admitted.");
                var staged = await runtime.Coordinator.StageOriginBookProseDraftAsync(book, proposal, () => true, default);
                Require(staged?.Pending(chapter) == proposal && staged.ChapterText(chapter) == canonicalText
                    && !staged.ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains("Synthetic proposed", StringComparison.Ordinal),
                    "Staging changed the readable/exported book without confirmation.");
                bool staleExportRejected = false;
                try { await runtime.Coordinator.ExportRetainedOriginBookAsync(book, AndroidSurfaceStrings.Resolve("en"), () => true, default); }
                catch (OperationCanceledException) { staleExportRejected = true; }
                Require(staleExportRejected && bookOutput.Deliveries == 1,
                    "An old reading edition remained exportable after a new draft was saved.");
                Require(await runtime.Coordinator.ReviewOriginBookProseDraftAsync(staged!, proposal, true, false, () => true, default) is null,
                    "A draft was selected without explicit reader confirmation.");
                // Exercise the actual MAUI review controls, not a direct acceptance.
                var review = new OriginBookProseReviewPage(runtime.Coordinator, staged!, chapter, proposal);
                // The managed harness has no Android navigation handler to send
                // the preceding page's lifecycle event for a direct test push.
                IssuedPageLifecycle(page, "OnDisappearing");
                await navigation.PushAsync(review, false); await Appear();
                Require(!Element<Button>("origin-prose-use").IsEnabled, "Reading acceptance starts enabled.");
                Element<Switch>("origin-prose-confirmed").IsToggled = true;
                await Click("origin-prose-use");
                Require(!IssuedElements(review).Any(e => e.AutomationId == "origin-prose-use"),
                    "A completed review retained its acceptance action.");
                Require(!runtime.Coordinator.IsRetainedOriginBookCurrent(staged!),
                    "The pre-acceptance reading edition remained current after review.");
                var selected = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                Require(selected?.Pending(chapter) is null && selected!.ChapterText(chapter) == proposal.Text
                    && selected.ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains("&lt;script&gt;not executable&lt;/script&gt;", StringComparison.Ordinal),
                    "Selected prose failed to reopen/export safely.");
                var coldReading = new OriginBookReadingStore(runtime.StateDirectory).Load(owner.Owner.Value, id.Value);
                Require(coldReading.Chapters.Single().Selected?.DraftDigest == proposal.DraftDigest,
                    "The selected reading version was not durable.");
                var nextProposal = OriginBookProseDraft.Create(chapter, selected.Locale, "synthetic-second-job", new string('b', 64), "Rejected rewrite.");
                var nextDraft = await runtime.Coordinator.StageOriginBookProseDraftAsync(selected, nextProposal, () => true, default);
                var coldStore = new OriginBookReadingStore(runtime.StateDirectory);
                bool staleSaveRejected = false;
                try { coldStore.Save(coldReading, coldReading, () => true, default); }
                catch (InvalidOperationException) { staleSaveRejected = true; }
                Require(staleSaveRejected && coldStore.Load(owner.Owner.Value, id.Value).Chapters.Single().Pending?.DraftDigest == nextProposal.DraftDigest,
                    "A stale store overwrote the newer pending proposal.");
                var discarded = await runtime.Coordinator.ReviewOriginBookProseDraftAsync(nextDraft!, nextProposal, false, false, () => true, default);
                Require(discarded?.ChapterText(chapter) == proposal.Text && discarded.Pending(chapter) is null,
                    "Discarding a later draft destroyed the previously selected chapter.");
                using (var cancellation = new CancellationTokenSource())
                {
                    var beforeCancel = coldStore.Load(owner.Owner.Value, id.Value);
                    var canceledState = beforeCancel with { Chapters = [new(chapter.ChapterId, proposal, nextProposal)] };
                    cancellation.Cancel();
                    bool saveCanceled = false;
                    try { coldStore.Save(beforeCancel, canceledState, () => true, cancellation.Token); }
                    catch (OperationCanceledException) { saveCanceled = true; }
                    Require(saveCanceled && coldStore.Load(owner.Owner.Value, id.Value).Digest == beforeCancel.Digest,
                        "Canceling before the save boundary changed the durable reading edition.");
                }
                Require(new OriginBookReadingStore(runtime.StateDirectory).Load(ContactsOwnerB.Value, id.Value).Chapters.Count == 0,
                    "Reading versions leaked into another account.");
                await Back();
                // Actual consent -> request -> refresh -> stage -> review UI.
                await Click($"origin-author-chapter-{chapter.Sequence}");
                Require(Current() is OriginBookAuthoringPage && authoringProbe.Requests == 0 && authoringProbe.Reads == 0
                    && !Element<Button>("origin-authoring-request").IsEnabled,
                    "Opening authoring sent private facts or enabled generation without consent.");
                await Click("origin-authoring-refresh");
                Require(authoringProbe.Reads == 1 && authoringProbe.Requests == 0, "Read-only status created a job.");
                Element<Switch>("origin-authoring-consent").IsToggled = true;
                await Click("origin-authoring-request");
                Require(authoringProbe.Requests == 1 && authoringProbe.Reads == 2,
                    "Consented chapter request did not read before creating.");
                authoringProbe.Ready = true;
                await Click("origin-authoring-refresh");
                var authored = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                Require(authored?.Pending(chapter)?.Text.StartsWith("Synthetic transport", StringComparison.Ordinal) == true
                    && authored.ChapterText(chapter) == proposal.Text && authoringProbe.Requests == 1 && authoringProbe.Acceptances == 0,
                    "Readback auto-adopted prose or submitted another generation.");
                await Click("origin-authoring-review");
                authoringProbe.FailAcceptance = true;
                Element<Switch>("origin-prose-confirmed").IsToggled = true;
                await Click("origin-prose-use");
                Require(authoringProbe.Acceptances == 1 && new OriginBookReadingStore(runtime.StateDirectory)
                    .Load(owner.Owner.Value, id.Value).Chapters.Single().Selected?.Text == "Synthetic transport chapter for explicit review.",
                    "An offline reader acknowledgement lost the locally accepted book.");
                await Back();
                Require(Current() is OriginBookAuthoringPage && !Element<Switch>("origin-authoring-consent").IsToggled,
                    "Returning from review retained consent or failed to reopen the current reading edition.");
                authoringProbe.FailAcceptance = false;
                await Click("origin-authoring-refresh");
                Require(authoringProbe.Acceptances == 2 && authoringProbe.Requests == 1,
                    "Refreshing the saved reading edition failed to reconcile acceptance or recreated a paid job.");
                await Click("origin-authoring-refresh");
                Require(authoringProbe.Acceptances == 2, "A confirmed reader acknowledgement was resent unnecessarily.");
                await Back();
                Require(IssuedElements(Current()).OfType<Label>().Any(label => label.Text == "Synthetic transport chapter for explicit review."),
                    "The accepted transport draft did not return to the reader.");
                Console.WriteLine("PASS actual MAUI chapter consent, read-before-create, explicit adoption, offline acceptance recovery and book return");
                book = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                bookOutput.BeforeRead = () => { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); };
                bool canceled = false;
                try { await runtime.Coordinator.ExportRetainedOriginBookAsync(book!, AndroidSurfaceStrings.Resolve("en"), () => true, default); }
                catch (OperationCanceledException) { canceled = true; }
                Require(canceled && bookOutput.Deliveries == 1 && !runtime.Coordinator.IsRetainedOriginBookCurrent(book!),
                    "Owner A→B→A during the document picker exported the old book.");
                bool rejectedOwner = false;
                try { rejectedOwner = await runtime.Coordinator.StageOriginBookProseDraftAsync(book!, proposal, () => true, default) is null; }
                catch (OperationCanceledException) { rejectedOwner = true; }
                Require(rejectedOwner, "A retired owner stamp admitted narrative text.");
                int navigationCount = navigation.Navigation.NavigationStack.Count;
                await ui.BeginAsyncVoid(() => ((IButtonController)openBook).SendClicked())
                    .WaitAsync(TimeSpan.FromSeconds(10));
                Require(navigation.Navigation.NavigationStack.Count == navigationCount,
                    "A departed Runner button opened a book after an owner transition.");
                Require(service.Load(owner, id, cold.ContentRevision, cold.SavedRevision).Value is null,
                    "Core book reader accepted a retired owner stamp.");
                owners.Set(ContactsOwnerB);
                Require(service.Load(owners.Capture(), id, cold.ContentRevision, cold.SavedRevision).Value is null,
                    "Linked owner fell back to a local runner's book.");
                owners.Set(OwnerScope.LocalSingleUser);
                Require(service.Load(owners.Capture(), id, cold.ContentRevision, cold.SavedRevision).Value?.SeedDigest == projected.Value!.SeedDigest,
                    "New legitimate owner context could not reopen the original saved book.");
                // A refresh cannot retain another account's prose or export button.
                typeof(RetainedOriginBookPage).GetMethod("Refresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, null);
                Require(!IssuedElements(page).Any(element => element.AutomationId == "origin-book-export"
                    || element.AutomationId?.StartsWith("origin-retained-chapter-", StringComparison.Ordinal) == true),
                    "Stale book page retained content after owner change.");
                IssuedPageLifecycle(page, "OnDisappearing");
                var after = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(JsonSerializer.Serialize(after.Document) == JsonSerializer.Serialize(cold.Document)
                    && after.ContentRevision == cold.ContentRevision && after.SavedRevision == cold.SavedRevision
                    && probe!.ConfirmCalls == 1 && owners.ActiveLeases == 0,
                    "Book reading/export changed the runner, replayed completion or retained a lease.");
            }

            NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
            LifeModuleCompletionSession Session() => (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Current())!;
            T Element<T>(string key) where T : Element => IssuedElements(Current()).OfType<T>().Single(e => e.AutomationId == key);
            async Task Appear()
            {
                var page = Current();
                if (IssuedPageField<int>(page, "_subscribed") != 0) return;
                Task appearance = ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                if (page is RetainedOriginBookPage)
                {
                    Require(Element<ActivityIndicator>("origin-book-loading").IsRunning
                        && !string.IsNullOrWhiteSpace(Element<Label>("origin-book-loading-message").Text)
                        && !IssuedElements(page).Any(e => e is Button),
                        "Opening or returning to the reader must show loading without stale book actions.");
                }
                await appearance.WaitAsync(TimeSpan.FromSeconds(30));
                if (page is RetainedOriginBookPage)
                    Require(!IssuedElements(page).Any(e => e.AutomationId == "origin-book-loading"),
                        "The completed book reader retained its loading indicator.");
            }
            async Task Click(string key)
            {
                var previous = Current(); var button = Element<Button>(key);
                Require(button.IsEnabled, "Disabled Life Modules action: " + key);
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0) IssuedPageLifecycle(previous, "OnDisappearing");
                await Appear();
            }
            async Task Back()
            { IssuedPageLifecycle(Current(), "OnDisappearing"); await navigation.PopAsync(false); await Appear(); }
        });
    }

    internal static async Task RunLifeModuleCompletionAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in new[] { "save-career-reopen", "owner-aba-during-open", "cancel-after-commit" })
            {
                var owners = new ControlledLinkedOwner();
                LifeCompletionNativeProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners,
                    lifeCompletionDecorator: actual => probe = new(actual));
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.Coordinator.CreateRunnerAsync();
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Life Modules completion", default);
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
                await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
                Require(runtime.Coordinator.State.WorkspaceId is not null, "Native Life Modules bootstrap did not create a runner.");
                var id = runtime.Coordinator.State.WorkspaceId!.Value;
                // Seed exact typed module confirmations. These tests exercise
                // the native final allocation boundary, not the story page.
                await Task.Run(() => SeedNativeLifeSequence(runtime.Services.GetRequiredService<CharacterCreationFoundationService>(), id));
                await runtime.Presenter.LoadAsync(id, default);
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                var before = store.Get(id).Value!;
                Require(runtime.Coordinator.CanOpenLifeModuleCompletion(), "Finished module sequence has no native completion entry.");
                if (scenario == "owner-aba-during-open") probe!.AfterLoad = () =>
                { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); };
                var loaded = await runtime.Coordinator.LoadLifeModuleCompletionAsync();
                if (scenario == "owner-aba-during-open")
                {
                    Require(loaded.Value is null && probe!.ConfirmCalls == 0 && store.Get(id).Value!.ContentRevision == before.ContentRevision,
                        "Owner A→B→A issued a completion editor or mutated the runner.");
                    Console.WriteLine("PASS Life Modules native completion: " + scenario);
                    continue;
                }
                Require(loaded.Value is not null, "Native completion load failed: " + string.Join(", ", loaded.Blockers));
                var state = loaded.Value!;
                var request = new CharacterCreationFoundationFinalizationPreviewRequest(state.Binding,
                    state.PendingDraft!.DraftRevision, state.PendingDraft.DraftDigest);
                var first = (await runtime.Coordinator.PreviewLifeModuleCompletionAsync(state, request)).Value!;
                Require(first.ModuleSequence is not null, "Full module sequence was not previewed.");
                request = request with
                {
                    QualityInstanceValues = first.ModuleSequence!.QualityLevels.Where(row => row.InstancePrompt is not null)
                        .ToDictionary(row => row.InstancePrompt!.PromptId, _ => "Renraku"),
                    TalentSelection = new("mundane"), AttributePurchases = []
                };
                var options = (await runtime.Coordinator.PreviewLifeModuleCompletionAsync(state, request)).Value!;
                Require(options.SkillsCatalog is not null, "Skills catalog missing after explicit talent/attribute selection.");
                var native = options.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
                request = request with
                {
                    SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []),
                    KarmaResourceInvestment = 0, GearSelection = [], LifestyleSelection = [], ContactSelection = [],
                    MagicSelection = new(null, null, [], [], []), StartingNuyenDiceTotal = 6
                };
                var reviewed = await runtime.Coordinator.PreviewLifeModuleCompletionAsync(state, request);
                Require(reviewed.Value is { CanApply: true, FinalizationPlan: not null },
                    "Native full allocation is not confirmable: " + string.Join(", ", reviewed.Blockers));
                var preview = reviewed.Value!;
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, false)).Value is null
                    && probe!.ConfirmCalls == 0, "An unconfirmed preview dispatched a write.");
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview with { }, true)).Value is null,
                    "An unissued copy acquired native confirmation authority.");
                using var cancel = new CancellationTokenSource();
                if (scenario == "cancel-after-commit") probe!.AfterConfirm = cancel.Cancel;
                var applied = await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, true, cancel.Token);
                Require(applied.Value is { CharacterCreated: true, CharacterEffectsApplied: true },
                    "Native completion lost a committed result: " + string.Join(", ", applied.Blockers));
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(cold.ContentRevision == before.ContentRevision + 1 && cold.ContentRevision == cold.SavedRevision
                    && cold.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null,
                    "Native completion did not retain the single atomic Career transition.");
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, true)).Value is null
                    && probe!.ConfirmCalls == 1, "A consumed review replayed its native command.");
                if (scenario == "save-career-reopen") Require(runtime.Coordinator.IsLifeModuleCompletionReceiptCurrent(applied.Value!),
                    "Known saved Life Modules runner did not reopen in Career.");
                else Require(applied.Blockers.Contains(CharacterCreationFinalizationBlockers.PostCommitReopenRequired),
                    "Cancellation after commit did not preserve the known result/reopen instruction.");
                ui.AssertHealthy();
                Console.WriteLine("PASS Life Modules native completion: " + scenario);
            }
        });
    }

    private static void SeedNativeLifeStory(NativeRewardRuntime runtime, CharacterWorkspaceId id)
    {
        var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(
            new CharacterCreationFoundationLifeModuleDecisionAuthority(
                runtime.Services.GetRequiredService<IWorkspaceStore>(),
                runtime.Services.GetRequiredService<ICharacterCreationFoundationService>(),
                runtime.Services.GetRequiredService<ICharacterFileQueries>(), () => "de-DE")));
        var start = interaction.Start(id.Value);
        Require(start.Value is not null, "Story setup failed: " + string.Join(", ", start.Blockers));
        var checkpoint = start.Value!;
        string[] modules = ["83c132b5-fcf5-4a43-b9de-6c8ab206a586", "924ccfd0-136c-4385-94fe-a8d7be2eb7ed",
            "15bd4283-f287-4be7-b174-9e5ab97bda1a", "5a2eee69-cedb-403e-9649-fdc9a1377374", "47bf63cf-9a2a-4008-b455-c8ab68add581"];
        for (int index = 0; index <= modules.Length; index++)
        {
            var choices = checkpoint.Projection.CurrentTurn.LegalChoices;
            var choice = index == modules.Length ? choices.Single(row => row.ChoiceId == "finish-life-module-selection")
                : index == 0 ? choices.Single(row => row.Label.StartsWith("Elf ·", StringComparison.Ordinal)
                    && row.SourceAnchorIds.Any(anchor => anchor.Contains("604831d9-0fdc-4579-aa7e-bc5d99bcee5d", StringComparison.Ordinal)))
                : choices.Single(row => row.ChoiceId != "finish-life-module-selection"
                    && row.SourceAnchorIds.Any(anchor => anchor.Contains(modules[index], StringComparison.Ordinal)));
            var answers = choice.FollowUps?.ToDictionary(prompt => prompt.PromptId,
                prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Renraku");
            var prepared = interaction.Prepare(checkpoint, choice.ChoiceId, answers);
            Require(prepared.Value?.PendingPreview is not null, "Story review failed: " + string.Join(", ", prepared.Blockers));
            var accepted = interaction.Confirm(prepared.Value!, prepared.Value!.PendingPreview!.PreviewDigest, "book-native-" + index, true);
            Require(accepted.Value is not null, "Story confirmation failed: " + string.Join(", ", accepted.Blockers));
            checkpoint = accepted.Value!.Checkpoint;
        }
        Require(checkpoint.Projection.CurrentTurn.IsTerminal && checkpoint.Projection.VisibleChapters.Count == modules.Length + 1,
            "Story setup did not retain every confirmed chapter.");
    }

    private static void SeedNativeLifeSequence(CharacterCreationFoundationService service, CharacterWorkspaceId id)
    {
        var start = service.Load(new(id)).Value!;
        var root = service.Preview(new(start.Binding, "Elf", new(
            "83c132b5-fcf5-4a43-b9de-6c8ab206a586", "604831d9-0fdc-4579-aa7e-bc5d99bcee5d")));
        Require(root.Value is { CanConfirm: true }, "Nationality setup blocked: " + string.Join(", ", root.Blockers));
        var selected = root.Value!;
        Require(service.Confirm(new(selected.Binding, "Elf", selected.Selection, selected.PreviewDigest, true, selected.FollowUpValues)).Value is not null,
            "Nationality setup not saved.");
        foreach (string moduleId in new[] { "924ccfd0-136c-4385-94fe-a8d7be2eb7ed", "f0393b9e-2698-4955-bd31-112b619ac7b8",
                     "5a2eee69-cedb-403e-9649-fdc9a1377374", "47bf63cf-9a2a-4008-b455-c8ab68add581" })
        {
            var journey = service.LoadJourney(new(id)).Value!;
            var option = journey.Options.Single(row => row.ModuleId == moduleId);
            var answers = option.FollowUps.ToDictionary(prompt => prompt.PromptId,
                prompt => prompt.Options.FirstOrDefault(row => row.IsEnabled)?.SourceValue ?? "Renraku");
            var module = service.PreviewModule(new(journey.Binding, journey.DraftRevision, journey.DraftDigest, new(moduleId, null), answers));
            Require(module.Value is { CanConfirm: true }, "Module setup blocked: " + string.Join(", ", module.Blockers));
            Require(service.ConfirmModule(new(module.Value!.Request, module.Value.PreviewDigest, true)).Value is not null, "Module setup not saved.");
        }
        var last = service.LoadJourney(new(id)).Value!;
        var finish = new CharacterCreationLifeModuleFinishRequest(last.Binding, last.DraftRevision, last.DraftDigest);
        var preview = service.PreviewFinishSelection(finish).Value!;
        Require(service.ConfirmFinishSelection(new(finish, preview.PreviewDigest, true)).Value is not null, "Module selection finish not saved.");
    }

    private sealed class LifeCompletionNativeProbe(IOwnerBoundCharacterCreationLifeModuleFinalizationService inner)
        : IOwnerBoundCharacterCreationLifeModuleFinalizationService
    {
        public Action? AfterLoad, BeforeConfirm, AfterConfirm;
        public int ConfirmCalls, PreviewCalls;
        public CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(OwnerContextStamp owner, CharacterWorkspaceId id)
        { Require(SynchronizationContext.Current is null, "Life Modules load blocks the UI context."); var r = inner.Load(owner, id); AfterLoad?.Invoke(); return r; }
        public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview> Preview(OwnerContextStamp owner, CharacterCreationFoundationFinalizationPreviewRequest request)
        { Require(SynchronizationContext.Current is null, "Life Modules preview blocks the UI context."); PreviewCalls++; return inner.Preview(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> Confirm(OwnerContextStamp owner, CharacterCreationFoundationFinalizationConfirmRequest request)
        { Require(SynchronizationContext.Current is null, "Life Modules confirmation blocks the UI context."); ConfirmCalls++; BeforeConfirm?.Invoke(); var r = inner.Confirm(owner, request); AfterConfirm?.Invoke(); return r; }
    }

    private sealed class LifeBookOutputProbe : IAndroidDocumentService
    {
        public Action? BeforeRead { get; set; }
        public int Deliveries { get; private set; }
        public string Html { get; private set; } = string.Empty;
        public Task<AndroidDocument?> OpenAsync(CancellationToken ct) => throw new InvalidOperationException("No import expected.");
        public Task<bool> SaveAsAsync(string name, string mediaType, Stream content, CancellationToken ct)
            => throw new InvalidOperationException("Book export must be context-bound.");
        public async Task<bool> SaveAsAsync(string name, string mediaType, Stream content, Func<bool> isCurrent, CancellationToken ct)
        {
            Require(name == "origin-dossier.html" && mediaType == "text/html", "Unexpected book output format.");
            BeforeRead?.Invoke();
            if (!isCurrent()) throw new OperationCanceledException();
            using var reader = new StreamReader(content, leaveOpen: true);
            Html = await reader.ReadToEndAsync(ct);
            Deliveries++;
            return true;
        }
    }
}
