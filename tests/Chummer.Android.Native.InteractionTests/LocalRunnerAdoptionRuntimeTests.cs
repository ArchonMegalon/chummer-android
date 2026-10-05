using System.IO.Compression;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.OriginBooks;
using Microsoft.Extensions.DependencyInjection;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLocalRunnerAdoptionAsync(string contentRoot)
    {
        await RunOpeningStoryAfterLocalAdoptionAsync(contentRoot);
        foreach (bool partial in new[] { false, true })
        {
            var owners = new ControlledLinkedOwner();
            owners.Set(OwnerScope.LocalSingleUser);
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners,
                lifeCompletionDecorator: actual => actual, lifeModuleInputDrafts: true, localRunnerAdoption: true);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Local book traveller", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 2));
            await runtime.Presenter.LoadAsync(id, default);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var before = store.Get(id).Value!;
            var origin = runtime.Services.GetRequiredService<IOwnerBoundLifeModuleOriginService>();
            var local = origin.Start(owners.Capture(), id.Value).Value
                ?? throw new InvalidOperationException("Local story fixture cannot resume.");
            var timeline = new FileOriginDossierDraftTimelineStore(runtime.StateDirectory);
            await timeline.SaveAsync(local);
            var readings = new OriginBookReadingStore(runtime.StateDirectory);
            var empty = readings.Load(OwnerScope.LocalSingleUser.NormalizedValue, id.Value);
            var profile = readings.Save(empty, empty with
            {
                StoryProfile = new(Gender: "female", Pronouns: "she/her", Tone: "hopeful")
            }, () => true, default);
            var retained = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            var chapter = retained.Chapters.Last();
            string prose = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Full retained paragraph {i}. Mira returned to Denver. 🌧️"));
            var source = retained.AuthoringSource(chapter);
            string requestId = Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.RequestId(source);
            var draft = OriginBookProseDraft.Create(chapter, retained.Locale, requestId, new string('d', 64), prose);
            var edition = readings.Save(profile, profile with
            {
                Chapters = [new(chapter.ChapterId, draft, null) { AuthoringSource = source }]
            }, () => true, default);
            retained = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            Require(Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.RequestId(retained.AuthoringSource(chapter)) == requestId,
                "The local fixture changed story preferences after its authoring request.");
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
            var scenes = new OriginBookSceneStore(runtime.StateDirectory);
            var picture = scenes.Save(scenes.Load(empty.Owner, id.Value), new(empty.Owner, id.Value,
                [OriginBookScene.ForChapter(retained, chapter, "Returning home", png)]), () => true, default);
            var inputs = new LifeModuleCompletionDraftStore(runtime.StateDirectory);
            var foundation = runtime.Services.GetRequiredService<ICharacterCreationFoundationService>().Load(new(id)).Value!;
            var input = new CharacterCreationFoundationFinalizationPreviewRequest(foundation.Binding, 0, "")
                { KarmaResourceInvestment = 12 };
            await inputs.SaveAsync(empty.Owner, input, default);

            Require(!runtime.Coordinator.CanAdoptLocalRunner, "Unlinked mode offered an account transfer.");
            owners.Set(ContactsOwnerA);
            await runtime.Coordinator.RetryLocalRunnerAdoptionsAsync();
            Require(store.Get(id).Success && !store.Get(ContactsOwnerA, id).Success,
                "Account initialization automatically claimed the local runner.");
            Require(runtime.Coordinator.State.Error is null && runtime.Coordinator.State.OpenWorkspaces.Count == 0,
                "A previous owner's selected-runner preference broke the empty account roster: " + runtime.Coordinator.State.Error);
            Require(runtime.Coordinator.CanAdoptLocalRunner,
                $"Account adoption unavailable: pending={runtime.Coordinator.HasPendingLocalRunnerAdoption}; "
                + $"owner={owners.Capture()}; overview={runtime.Coordinator.State.Session.OwnerContext}; "
                + $"shell={runtime.Shell.State.OwnerContext}; error={runtime.Coordinator.State.Error}; "
                + $"shellError={runtime.Shell.State.Error}; notice={runtime.Coordinator.Notice}");
            var candidate = (await runtime.Coordinator.ListLocalRunnerCandidatesAsync(default)).Single(c => c.WorkspaceId == id);
            var character = System.Xml.Linq.XDocument.Parse(before.Document.Content).Root!;
            string? expectedName = (string?)character.Element("alias");
            if (string.IsNullOrWhiteSpace(expectedName)) expectedName = (string?)character.Element("name");
            Require(!string.IsNullOrWhiteSpace(expectedName) && candidate.Name == expectedName && candidate.Name != id.Value,
                "Local runner list did not use the retained character's name or alias.");
            var review = await runtime.Coordinator.ReviewLocalRunnerAdoptionAsync(candidate)
                ?? throw new InvalidOperationException("The native adoption review is unavailable.");
            var abandonedJournal = review.Journal;
            Require(!await runtime.Coordinator.ConfirmLocalRunnerAdoptionAsync(review, false)
                && store.Get(id).Success, "Canceled review moved local data.");
            owners.Set(ContactsOwnerB); owners.Set(ContactsOwnerA);
            Require(!await runtime.Coordinator.ConfirmLocalRunnerAdoptionAsync(review, true) && store.Get(id).Success,
                "Owner A-B-A revived the transfer review.");
            await runtime.Coordinator.RetryLocalRunnerAdoptionsAsync();
            candidate = (await runtime.Coordinator.ListLocalRunnerCandidatesAsync(default)).Single(c => c.WorkspaceId == id);
            review = (await runtime.Coordinator.ReviewLocalRunnerAdoptionAsync(candidate))!;
            // Simulate process death after writing an older intention but before
            // its Core claim. A later, separately reviewed transfer must not leave
            // startup stuck on this superseded, never-admitted operation.
            string abandonedDirectory = Path.Combine(runtime.StateDirectory, "local-runner-book-adoptions",
                OriginAdoptionFiles.Digest(abandonedJournal.Owner));
            Directory.CreateDirectory(abandonedDirectory);
            OriginAdoptionFiles.CommitNewOrExact(Path.Combine(abandonedDirectory, abandonedJournal.Operation.ToString("N") + ".json"),
                JsonSerializer.SerializeToUtf8Bytes(abandonedJournal), () => true, default);
            Require(await runtime.LocalAdoption!.RecoverPendingAsync(owners.Capture()) && store.Get(id).Success,
                "An intention without a durable Core claim changed local ownership.");
            if (partial)
            {
                // Exact durable cut: the intention, real Core claim and first
                // private file exist; the process ended before the other files.
                var journal = review.Journal;
                string directory = Path.Combine(runtime.StateDirectory, "local-runner-book-adoptions", OriginAdoptionFiles.Digest(journal.Owner));
                Directory.CreateDirectory(directory);
                OriginAdoptionFiles.CommitNewOrExact(Path.Combine(directory, journal.Operation.ToString("N") + ".json"),
                    JsonSerializer.SerializeToUtf8Bytes(journal), () => true, default);
                // The real issuer must perform the claim. It has already issued
                // this review inside the Android service; no receipt is forged.
                var core = (WorkspaceLocalAdoptionService)typeof(AndroidLocalRunnerAdoptionService)
                    .GetField("_core", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(runtime.LocalAdoption)!;
                Require(core.Confirm(owners.Capture(), review.Core, true).Outcome == WorkspaceLocalAdoptionOutcome.Applied,
                    "Partial placement fixture failed to commit its actual Core claim.");
                readings.AdoptLocalEdition(journal.Owner, id.Value, edition.Digest, () => true, default);
                var cold = new AndroidLocalRunnerAdoptionService(runtime.StateDirectory, store, owners, origin,
                    new(runtime.StateDirectory), new(runtime.StateDirectory), new(runtime.StateDirectory), new(runtime.StateDirectory),
                    runtime.Services.GetRequiredService<ICharacterFileQueries>());
                Require(await cold.RecoverPendingAsync(owners.Capture()), "Cold partial book placement did not recover.");
                await runtime.Coordinator.RetryLocalRunnerAdoptionsAsync();
                Require(runtime.Coordinator.State.OpenWorkspaces.Any(item => item.Id == id),
                    $"Recovered runner missing from refreshed roster: core={store.Get(ContactsOwnerA, id).Success}; "
                    + $"overview={runtime.Coordinator.State.OpenWorkspaces.Count}; shell={runtime.Shell.State.OpenWorkspaces.Count}; "
                    + $"pending={runtime.Coordinator.HasPendingLocalRunnerAdoption}; error={runtime.Coordinator.State.Error}");
                var row = runtime.Coordinator.State.OpenWorkspaces.Single(item => item.Id == id);
                Require(await runtime.Coordinator.SwitchWorkspaceAsync(row) is not null, "Recovered runner cannot be opened.");
            }
            else
                Require(await runtime.Coordinator.ConfirmLocalRunnerAdoptionAsync(review, true),
                    "Native transfer did not activate the real account workspace: " + runtime.Coordinator.Notice
                    + $"; error={runtime.Coordinator.State.Error}; workspace={runtime.Coordinator.State.WorkspaceId}; "
                    + $"session={runtime.Coordinator.State.Session.ActiveWorkspaceId}; count={runtime.Coordinator.State.OpenWorkspaces.Count}");

            Require(!store.Get(id).Success && !store.Get(ContactsOwnerB, id).Success,
                "Adoption exposed a second or foreign runner.");
            var after = store.Get(ContactsOwnerA, id).Value!;
            Require(before.ContentRevision == after.ContentRevision && before.SavedRevision == after.SavedRevision
                && JsonSerializer.Serialize(before.Document) == JsonSerializer.Serialize(after.Document),
                "Native adoption recreated the runner or rewrote its rules/history.");
            var rebound = await timeline.LoadAsync(ContactsOwnerA.NormalizedValue, id.Value);
            Require(rebound is not null && origin.Restore(owners.Capture(), rebound).Value is not null
                && JsonSerializer.Serialize(rebound.Projection) == JsonSerializer.Serialize(local.Projection),
                "Native adoption lost the exact Core story timeline.");
            var continued = await runtime.Coordinator.OpenSr5LifeModuleOriginAsync();
            Require(continued.Outcome == LifeModuleOriginDossierOutcomes.Success && continued.State is not null,
                "The adopted runner cannot continue its story: " + string.Join(", ", continued.Blockers));
            Require(continued.State!.OwnerId == ContactsOwnerA.NormalizedValue
                && continued.State.BoundTurnSeedDigest == rebound!.Projection.CurrentTurn.SeedDigest,
                "The continued story changed custody or its original narrative identity.");
            var freshPhone = new OriginDossierLifeModulePhoneRuntime(origin,
                new FileOriginDossierDraftTimelineStore(runtime.StateDirectory));
            var reopened = await freshPhone.OpenAsync(owners.Capture(), id.Value);
            Require(reopened.IsSuccess && reopened.StoryCheckpoint?.CheckpointDigest == rebound!.CheckpointDigest,
                "Cold adopted-story projection did not retain the exact checkpoint.");
            Require(OriginAdoptionFiles.Digest(inputs.ReadForAdoption(ContactsOwnerA.NormalizedValue, id))
                == OriginAdoptionFiles.Digest(input), "Native adoption lost unfinished completion inputs.");
            var book = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true)
                ?? throw new InvalidOperationException("The adopted book cannot be read.");
            Require(book.ChapterText(chapter) == prose && book.Readings!.StoryProfile == edition.StoryProfile
                && Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.RequestId(book.AuthoringSource(chapter)) == requestId,
                "The real account reader lost full prose, preferences or its original provider identity.");
            Require(scenes.Load(ContactsOwnerA.NormalizedValue, id.Value).Digest
                == new OriginBookScenes(ContactsOwnerA.NormalizedValue, id.Value, picture.Scenes).Digest,
                "Account illustrations differ from the originals.");
            using var epub = new ZipArchive(new MemoryStream(OriginBookEpub.Create(book, AndroidSurfaceStrings.Resolve("en-US"))), ZipArchiveMode.Read);
            Require(epub.Entries.Any(entry => entry.FullName.EndsWith(".png", StringComparison.Ordinal)),
                "The real account EPUB omitted retained illustrations.");
            Require(await runtime.LocalAdoption!.RecoverPendingAsync(owners.Capture())
                && (await runtime.Coordinator.ListLocalRunnerCandidatesAsync(default)).All(c => c.WorkspaceId != id),
                "A completed adoption stayed pending or adoptable.");
            Console.WriteLine("PASS native local runner adoption " + (partial ? "cold partial recovery" : "explicit coordinator handoff")
                + ": same identity/history, owner ABA, full chapter, illustration, EPUB, timeline and completion inputs");
        }
    }

    private static async Task RunOpeningStoryAfterLocalAdoptionAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(OwnerScope.LocalSingleUser);
        await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
            productionCreationOverview: true, linkedOwners: owners,
            lifeCompletionDecorator: actual => actual, lifeModuleInputDrafts: true, localRunnerAdoption: true);
        await runtime.Coordinator.InitializeAsync();
        await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
        await runtime.Coordinator.CreateRunnerAsync();
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Opening after adoption", default);
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
        await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
        var id = runtime.Coordinator.State.WorkspaceId!.Value;
        var local = await runtime.Coordinator.OpenSr5LifeModuleOriginAsync();
        Require(local.IsSuccess && local.State?.Timeline.Count == 0,
            "The local opening fixture is not an undecided Life Modules runner.");
        owners.Set(ContactsOwnerA);
        await runtime.Coordinator.RetryLocalRunnerAdoptionsAsync();
        var candidate = (await runtime.Coordinator.ListLocalRunnerCandidatesAsync(default)).Single(c => c.WorkspaceId == id);
        var review = (await runtime.Coordinator.ReviewLocalRunnerAdoptionAsync(candidate))!;
        Require(await runtime.Coordinator.ConfirmLocalRunnerAdoptionAsync(review, true),
            "The empty local story could not be adopted through the real coordinator.");
        var opened = await runtime.Coordinator.OpenSr5LifeModuleOriginAsync();
        Require(opened.IsSuccess && opened.State?.Choices.Count > 0 && opened.State.Timeline.Count == 0,
            "The adopted opening cannot present metatype choices: " + string.Join(", ", opened.Blockers));
        var checkpoint = opened.StoryCheckpoint!;
        Require(checkpoint.OwnerId == ContactsOwnerA.NormalizedValue
            && checkpoint.Projection.CurrentTurn.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
            && JsonSerializer.Serialize(checkpoint.Projection) == JsonSerializer.Serialize(local.StoryCheckpoint!.Projection),
            "Rendering rewrote the immutable original narrative owner or history.");
        var owner = owners.Capture();
        foreach (Action rejectedProjection in new Action[]
        {
            () => OriginDossierLifeModuleInteractionProjector.Project(checkpoint),
            () => OriginDossierLifeModuleInteractionProjector.ProjectAdmitted(checkpoint, default),
            () => OriginDossierLifeModuleInteractionProjector.ProjectAdmitted(checkpoint, owner with { Owner = ContactsOwnerB }),
            () => OriginDossierLifeModuleInteractionProjector.ProjectAdmitted(checkpoint with { WorkspaceRevision = checkpoint.WorkspaceRevision + 1 }, owner)
        })
        {
            bool rejected = false;
            try { rejectedProjection(); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Raw, wrong-owner or structurally inconsistent projection was accepted.");
        }
        var service = runtime.Services.GetRequiredService<IOwnerBoundLifeModuleOriginService>();
        Require(service.Restore(owner, checkpoint with { CheckpointDigest = new string('0', 64) }).Value is null
            && service.Restore(owner, checkpoint with { OwnerId = ContactsOwnerB.NormalizedValue }).Value is null,
            "Core admitted a corrupt or foreign custody checkpoint.");
        var choice = checkpoint.Projection.CurrentTurn.LegalChoices.First(row => row.Label.StartsWith("Human ·", StringComparison.Ordinal));
        var answers = choice.FollowUps?.ToDictionary(prompt => prompt.PromptId,
            prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Denver");
        var prepared = await runtime.Coordinator.PrepareSr5LifeModuleOriginAsync(choice.ChoiceId, followUpValues: answers);
        Require(prepared.IsSuccess && prepared.State?.PendingPreviewDigest is not null,
            "The adopted opening choice cannot be reviewed: " + string.Join(", ", prepared.Blockers));
        var phone = new OriginDossierLifeModulePhoneRuntime(service, new FileOriginDossierDraftTimelineStore(runtime.StateDirectory));
        owners.Set(ContactsOwnerB);
        Require(!(await phone.OpenAsync(owners.Capture(), id.Value)).IsSuccess
            && service.Restore(owners.Capture(), prepared.StoryCheckpoint!).Value is null,
            "Another account opened the adopted story.");
        owners.Set(ContactsOwnerA);
        Require(!(await phone.ConfirmAsync(owner, id.Value, choice.ChoiceId, prepared.State!.PendingPreviewDigest!)).IsSuccess,
            "Owner A-B-A revived the captured story confirmation.");
        var restored = await phone.OpenAsync(owners.Capture(), id.Value);
        Require(restored.IsSuccess && restored.State?.PendingPreviewDigest == prepared.State.PendingPreviewDigest,
            "Fresh owner admission did not reopen the exact pending adopted decision.");
        var confirmed = await phone.ConfirmAsync(owners.Capture(), id.Value, choice.ChoiceId, restored.State!.PendingPreviewDigest!);
        Require(confirmed.IsSuccess && confirmed.State?.Timeline.Count == 1,
            "Adopted opening commit failed: " + string.Join(", ", confirmed.Blockers));
        var cold = new OriginDossierLifeModulePhoneRuntime(service, new FileOriginDossierDraftTimelineStore(runtime.StateDirectory));
        var reopened = await cold.OpenAsync(owners.Capture(), id.Value);
        Require(reopened.IsSuccess && reopened.StoryCheckpoint?.CheckpointDigest == confirmed.StoryCheckpoint!.CheckpointDigest
            && reopened.State?.Timeline.Count == 1,
            "Disk reopen lost or duplicated the first adopted story decision.");
        Console.WriteLine("PASS adopted opening: actual Core custody, unchanged narrative, prepare/confirm, cold reopen, raw/foreign/corrupt rejection and ABA");
    }
}
