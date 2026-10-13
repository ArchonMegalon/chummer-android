using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.OriginBooks;
using Chummer.Run.Contracts.Community;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleDashboardGuidanceAsync(string contentRoot, string? smokeDirectory = null)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            owners.Set(OwnerScope.LocalSingleUser);
            LifeBookReadProbe? probe = null;
            DashboardAvailabilityProbe? availability = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners,
                lifeBookDecorator: actual => probe = new(actual),
                lifeOriginDecorator: actual => availability = new(actual));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await CreateRunner();
            var page = new BuildPage(runtime.Coordinator);
            // Headless MAUI has no native scroll handler. Acknowledge geometry
            // only; all story state still comes from the real coordinator.
            var scroll = (ScrollView)page.Content!;
            scroll.ScrollToRequested += (_, _) => scroll.SendScrollFinished();
            var window = new Window(new NavigationPage(page));
            using var alerts = new IssuedPageAlerts(page, window);
            await alerts.PreflightAsync();
            var copy = AndroidSurfaceStrings.Resolve();
            await Observe(copy["Origin.OpeningSetupRequired"], false, "fresh runner");
            probe!.Transform = _ => new(LifeModuleOriginDossierOutcomes.Missing, null,
                ["life-module-origin-history-unreadable"]);
            await Observe(copy["Origin.BookUnavailable"], false, "unreadable ledger");
            probe.Transform = _ => throw new IOException("Synthetic reading failure.");
            await Observe(copy["Origin.BookUnavailable"], false, "read failure");
            probe.Transform = null;
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 1));
            await runtime.Presenter.LoadAsync(id, default);
            await Observe(copy["Origin.OpeningSetupRequired"], false, "birth chosen, childhood pending");
            Dump("birth");

            await CreateRunner();
            id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 2));
            await runtime.Presenter.LoadAsync(id, default);
            await Observe(copy["Origin.ReaderFullTextPending"], false, "opening complete, no prose");
            Dump("pending");
            int availabilityReads = availability!.Reads;
            var book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            Require(availability.Reads == availabilityReads + 1 && book.Opportunities is not null,
                "The full reader lost the Core-owned narrative opportunities needed for authoring.");
            var chapter = book.Chapters.Last();
            var draft = OriginBookProseDraft.Create(chapter, book.Locale,
                OriginChapterSourceIdentity.RequestId(book.AuthoringSource(chapter)), new string('f', 64),
                "Synthetic full chapter for the dashboard-state regression, not provider evidence.");
            book = (await runtime.Coordinator.StageOriginBookProseDraftAsync(book, draft, () => true, default))!;
            await Observe(copy["Origin.MechanicsAfterReading"], false, "returned prose, not read");
            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            book = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(book, draft, true, true, () => true, default))!;
            Require(book.HasReadCurrentStory, "The fixture failed to acknowledge the chapter.");
            await Observe(CreationAllocationStrings.Get("LifeDashboard.StoryHelp", ""), true, "read chapter reopened from disk");
            Dump("read");

            // A completed earlier chapter remains readable when a later module
            // awaits prose (including an uncertain provider outcome). The local
            // dashboard must not claim either an empty book or a running job.
            await CreateRunner();
            id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 3));
            await runtime.Presenter.LoadAsync(id, default);
            await Observe(copy["Origin.ReaderFullTextPending"], false, "another runner has no prose");
            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            chapter = book.Chapters[1];
            draft = OriginBookProseDraft.Create(chapter, book.Locale,
                OriginChapterSourceIdentity.RequestId(book.AuthoringSource(chapter)), new string('e', 64),
                "The accepted first chapter remains readable while its successor is unavailable.");
            book = (await runtime.Coordinator.StageOriginBookProseDraftAsync(book, draft, () => true, default))!;
            await Observe(copy["Origin.MechanicsAfterReading"], false, "earlier chapter awaits reading");
            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            book = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(book, draft, true, true, () => true, default))!;
            Require(book.HasExportableChapters && !book.HasReadCurrentStory
                && book.ReadableChapter(book.Chapters.Last()) is null,
                "SETUP: expected a saved first chapter and an unavailable successor.");
            string readingDigest = book.Readings!.Digest;
            await Observe(copy["Origin.ReaderNextChapterPending"], false, "saved chapter with unavailable successor");
            await Observe(copy["Origin.ReaderNextChapterPending"], false, "partial book re-entry from disk");
            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            Require(book.Readings!.Digest == readingDigest && book.ReadableChapter(chapter)?.Text == draft.Text
                && !book.HasReadCurrentStory,
                "Dashboard guidance changed the reading edition or acknowledged missing prose.");
            Dump("partial-book");
            Require(alerts.Titles.Count == 0, "Dashboard guidance raised an unexpected blocking alert.");
            // A real dirty workspace refusal used to look like a broken picker:
            // its only explanation was below the entire creation dashboard.
            var previousRunner = runtime.Coordinator.State.WorkspaceId;
            await CreateRunner(save: false);
            // Reproduce the native ordering: opening Life Modules can precede
            // the user's explicit Save. A save-only change must not strand
            // this untouched first choice after the phone runtime is recreated.
            var opening = await runtime.Coordinator.OpenSr5LifeModuleOriginAsync();
            Require(opening.IsSuccess && opening.State?.Choices.Count > 0
                && opening.State.Timeline.Count == 0 && opening.StoryCheckpoint?.PendingPreview is null,
                "SETUP: the unsaved runner needs an undecided opening.");
            var openingCheckpoint = opening.StoryCheckpoint
                ?? throw new InvalidOperationException("SETUP: opening checkpoint was not retained.");
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            var dirty = runtime.Coordinator.State;
            Require(dirty.Session.ActiveWorkspace?.IsDirty == true, "SETUP: expected unsaved runner.");
            int target = dirty.OpenWorkspaces.ToList().FindIndex(w => w.Id == previousRunner);
            Require(target >= 0, "SETUP: previous runner is missing.");
            var picker = IssuedElements(page).OfType<Picker>().Single(p => p.AutomationId == "build-workspace-picker");
            await ui.BeginAsyncVoid(() => picker.SelectedIndex = target);
            await ui.DrainDispatchedAsyncVoidAsync();
            Require(alerts.Titles.Count == 1 && alerts.Messages.Last() == PhoneStrings.Get("HomeUnsavedSwitch", "Save your current runner before switching."),
                "A refused workspace switch needs an immediate, readable explanation.");
            Require(runtime.Coordinator.State.WorkspaceId == dirty.WorkspaceId
                && runtime.Coordinator.State.ContentRevision == dirty.ContentRevision
                && runtime.Coordinator.State.SavedRevision == dirty.SavedRevision
                && runtime.Coordinator.State.Session.ActiveWorkspace?.IsDirty == true,
                "Explaining the refusal must not save, discard or switch the runner.");
            var body = (VerticalStackLayout)scroll.Content!;
            Require(body.Children.OfType<Label>().Take(2).Any(l => l.Text == alerts.Messages.Last()),
                "The refusal must remain above the dashboard after dismissing the alert.");
            // Explicit Save is the recovery path, not a silent mutation during selection.
            var save = page.ToolbarItems.Single(item => item.AutomationId == "build-save-runner");
            await ui.BeginAsyncVoid(() => save.Command.Execute(null));
            await ui.DrainDispatchedAsyncVoidAsync();
            var savedId = dirty.WorkspaceId!.Value;
            var saved = runtime.Coordinator.State;
            Require(saved.ContentRevision == dirty.ContentRevision
                && saved.SavedRevision == saved.ContentRevision
                && saved.Session.ActiveWorkspace?.IsDirty == false,
                "Explicit Save did not preserve content while advancing its saved revision.");
            Require(availability!.Restore(owners.Capture(), openingCheckpoint).Outcome
                == LifeModuleOriginDossierOutcomes.Conflict,
                "The regression must keep the original command admission stale after Save.");
            var workspaceStore = new FileWorkspaceStore(runtime.StateDirectory);
            string savedBytes = JsonSerializer.Serialize(workspaceStore.Get(savedId));
            var coldTimeline = new FileOriginDossierDraftTimelineStore(runtime.StateDirectory);
            var retainedOpening = await coldTimeline.LoadAsync(owners.Capture().Owner.NormalizedValue, savedId.Value);
            Require(retainedOpening?.CheckpointDigest == openingCheckpoint.CheckpointDigest,
                "SETUP: cold reopening must exercise the original persisted pre-Save checkpoint.");
            var coldPhone = new OriginDossierLifeModulePhoneRuntime(availability, coldTimeline);
            var reopened = await coldPhone.OpenAsync(owners.Capture(), savedId.Value);
            Require(reopened.IsSuccess && reopened.State?.Choices.Count > 0
                && reopened.State.Timeline.Count == 0 && reopened.StoryCheckpoint?.PendingPreview is null,
                "Saved opening lost its metatype choices: " + string.Join(", ", reopened.Blockers));
            var reopenedCheckpoint = reopened.StoryCheckpoint
                ?? throw new InvalidOperationException("Cold reopen did not return a checkpoint.");
            Require(reopenedCheckpoint.CheckpointDigest != openingCheckpoint.CheckpointDigest
                && reopenedCheckpoint.Projection.CurrentTurn.LegalChoices.Select(c => c.ChoiceId)
                    .SequenceEqual(openingCheckpoint.Projection.CurrentTurn.LegalChoices.Select(c => c.ChoiceId)),
                "Cold reopen must return fresh admission for the same choices.");
            var continued = await runtime.Coordinator.OpenSr5LifeModuleOriginAsync();
            Require(continued.IsSuccess
                && continued.StoryCheckpoint?.CheckpointDigest == reopenedCheckpoint.CheckpointDigest
                && JsonSerializer.Serialize(workspaceStore.Get(savedId)) == savedBytes,
                "Continue must reuse the fresh checkpoint without changing runner data or accepting a choice.");
            Console.WriteLine("PASS Life Modules opening: open before Save, toolbar Save, cold phone reopen, same choices, unchanged runner, no implicit decision");
            picker = IssuedElements(page).OfType<Picker>().Single(p => p.AutomationId == "build-workspace-picker");
            target = runtime.Coordinator.State.OpenWorkspaces.ToList().FindIndex(w => w.Id == previousRunner);
            await ui.BeginAsyncVoid(() => picker.SelectedIndex = target);
            await ui.DrainDispatchedAsyncVoidAsync();
            Require(runtime.Coordinator.State.WorkspaceId == previousRunner && alerts.Titles.Count == 1,
                "An explicitly saved runner should switch without another refusal.");
            IssuedPageLifecycle(page, "OnDisappearing");
            Console.WriteLine("PASS workspace picker: dirty refusal visible, no implicit save/discard, explicit save then switch");
            ui.AssertHealthy();

            async Task CreateRunner(bool save = true)
            {
                await runtime.Coordinator.CreateRunnerAsync();
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Opening guidance fixture", default);
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
                await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
                if (save) await runtime.Coordinator.SaveAsync();
            }

            void Dump(string name)
            {
                if (smokeDirectory is null) return;
                string destination = Path.Combine(smokeDirectory, name);
                Require(Path.IsPathFullyQualified(destination) && !Directory.Exists(destination),
                    "Smoke fixtures need a fresh absolute destination.");
                Directory.CreateDirectory(destination);
                foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*.json", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(destination, Path.GetRelativePath(runtime.StateDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }

            async Task Observe(string expected, bool showMechanics, string scenario)
            {
                int reads = availability!.Reads;
                availability.FailIfRead = true;
                bool loadingToolbarDisabled = false;
                try
                {
                    Task appearance = ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    loadingToolbarDisabled = page.ToolbarItems.All(item => !item.IsEnabled);
                    // Join the real appearance before asserting: a failure must
                    // not dispose the UI pump under its pending callbacks.
                    await appearance.WaitAsync(TimeSpan.FromSeconds(30));
                }
                finally { availability.FailIfRead = false; }
                Require(loadingToolbarDisabled,
                    "Runner loading kept a stale toolbar action enabled: " + scenario);
                Require(page.ToolbarItems.Single(item => item.AutomationId == "creation-rules-summary").IsEnabled,
                    "The current creation rules menu did not recover after loading: " + scenario);
                Require(availability.Reads == reads,
                    "Story readiness unnecessarily queried next-module availability: " + scenario);
                var elements = IssuedElements(page).ToArray();
                Require(elements.OfType<Label>().Count(e => e.Text?.Contains(expected, StringComparison.Ordinal) == true) == 1,
                    "Life Modules guidance must appear once, inside the actionable next-decision row: " + scenario);
                Require(elements.Any(e => e.AutomationId == "creation-life-module-budget") == showMechanics,
                    "Dashboard mechanics were revealed before reading: " + scenario);
                var next = elements.Single(e => e.AutomationId == "creation-life-module-continue");
                Require(SemanticProperties.GetDescription(next).Contains(expected, StringComparison.Ordinal),
                    "The next-decision card contradicts the dashboard: " + scenario);
                IssuedPageLifecycle(page, "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine("PASS Life Modules dashboard guidance: " + scenario);
            }
        });
    }

    private sealed class DashboardAvailabilityProbe(IOwnerBoundLifeModuleOriginService inner)
        : IOwnerBoundLifeModuleOriginService, IOwnerBoundLifeModuleAvailabilityService
    {
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public bool FailIfRead { get; set; }
        public bool IsCurrent(OwnerContextStamp owner) => inner.IsCurrent(owner);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Start(OwnerContextStamp owner, string id)
            => inner.Start(owner, id);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Restore(OwnerContextStamp owner,
            LifeModuleOriginDossierDraftCheckpoint checkpoint) => inner.Restore(owner, checkpoint);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Reopen(OwnerContextStamp owner,
            LifeModuleOriginDossierDraftCheckpoint checkpoint) => inner.Reopen(owner, checkpoint);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Prepare(OwnerContextStamp owner,
            LifeModuleOriginDossierDraftCheckpoint checkpoint, string choiceId,
            IReadOnlyDictionary<string, string>? followUpValues = null)
            => inner.Prepare(owner, checkpoint, choiceId, followUpValues);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> Confirm(OwnerContextStamp owner,
            LifeModuleOriginDossierDraftCheckpoint checkpoint, string previewDigest, string idempotencyKey,
            bool explicitlyConfirmed) => inner.Confirm(owner, checkpoint, previewDigest, idempotencyKey, explicitlyConfirmed);
        public LifeModuleOriginDossierResult<LifeModuleDecisionAvailabilitySnapshot> LoadAvailability(OwnerContextStamp owner,
            LifeModuleDecisionAvailabilityRequest request)
        {
            Interlocked.Increment(ref _reads);
            if (FailIfRead) throw new IOException("Next-module availability must not block story readiness.");
            return ((IOwnerBoundLifeModuleAvailabilityService)inner).LoadAvailability(owner, request);
        }
    }
}
