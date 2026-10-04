using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.LifeModules;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Chummer.Presentation.OriginBooks;
using Chummer.Presentation;
using Chummer.Presentation.Shell;
using System.Reflection;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunOriginSuccessorAcceptanceAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        var account = DispatchProxy.Create<IAndroidAccountLinkService, OriginSuccessorAccount>();
        var remote = (OriginSuccessorAccount)account;
        await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
            productionCreationOverview: true, linkedOwners: owners, accountService: account);
        await runtime.Coordinator.InitializeAsync();
        await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
        await runtime.Coordinator.CreateRunnerAsync();
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Successor recovery", default);
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
        await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
        var id = runtime.Coordinator.State.WorkspaceId!.Value;
        await runtime.Coordinator.SaveAsync();
        await Task.Run(() => SeedNativeLifeStory(runtime, id));
        await runtime.Presenter.LoadAsync(id, default);
        var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
        var book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
        var first = book.Chapters[1];
        var next = book.Chapters[2];
        var firstSource = OriginBookAuthoringSource.Create(book.Projection, first);
        var nextSource = OriginBookAuthoringSource.Create(book.Projection, next);
        var draft = OriginBookProseDraft.Create(first, book.Locale,
            Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.RequestId(firstSource),
            new string('d', 64), "Synthetic accepted opening, retained before the network acknowledgement.");
        remote.Seed(firstSource, draft);
        book = (await runtime.Coordinator.StageOriginBookProseDraftAsync(book, draft, () => true, default))!;
        var pending = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, default);
        Require(pending.Result.Outcome == AndroidOriginChapterOutcome.Conflict && remote.Acceptances == 0 && remote.Requests == 0,
            "An unaccepted predecessor was acknowledged or generated a successor.");
        book = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(book, draft, true, true, () => true, default))!;
        remote.FailAcceptance = true;
        Require(!await runtime.Coordinator.RecordOriginBookReaderAcceptanceAsync(book, draft, () => true, default),
            "Synthetic offline acknowledgement unexpectedly succeeded.");
        // A new disk-backed edition must be sufficient: no transient page flag is acceptance authority.
        book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
        string readingDigest = book.Readings!.Digest;
        remote.ResetCounts();
        var readOnly = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, false, () => true, default);
        Require(readOnly.Result.Outcome == AndroidOriginChapterOutcome.NotFound && remote.Acceptances == 0 && remote.Requests == 0,
            "Reading successor status acknowledged a predecessor or created a job.");
        var failed = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, default);
        Require(failed.Result.Outcome == AndroidOriginChapterOutcome.Unavailable && remote.Acceptances == 1 && remote.Requests == 0,
            "Successor creation did not recover the saved acceptance first, or dispatched despite a lost acknowledgement.");
        remote.FailAcceptance = false;
        foreach (var failure in new[] { AndroidOriginChapterOutcome.Unavailable, AndroidOriginChapterOutcome.Unauthorized,
            AndroidOriginChapterOutcome.Conflict, AndroidOriginChapterOutcome.NotFound })
        {
            remote.PredecessorReadFailure = failure;
            remote.ResetCounts();
            var result = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, default);
            Require(result.Result.Outcome != AndroidOriginChapterOutcome.Available && remote.Acceptances == 0 && remote.Requests == 0,
                "A failed predecessor read was treated as permission to acknowledge or generate.");
        }
        remote.PredecessorReadFailure = null;
        remote.CorruptPredecessor = true;
        remote.ResetCounts();
        var wrong = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, default);
        Require(wrong.Result.Outcome == AndroidOriginChapterOutcome.Conflict && remote.Acceptances == 0 && remote.Requests == 0,
            "Different remote prose was accepted as the locally selected predecessor.");
        remote.CorruptPredecessor = false;
        bool current = true;
        remote.AfterPredecessorRead = () => current = false;
        var departed = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => current, default);
        Require(departed.Book is null && remote.Acceptances == 0 && remote.Requests == 0,
            "A departed page acknowledged or created a successor after its read.");
        remote.AfterPredecessorRead = null;
        current = true;
        using (var canceled = new CancellationTokenSource())
        {
            remote.AfterAcceptance = canceled.Cancel;
            var stopped = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, canceled.Token);
            Require(stopped.Book is null && remote.Acceptances == 1 && remote.Requests == 0,
                "Cancellation after acknowledgement still generated the successor.");
        }
        remote.AfterAcceptance = null;
        remote.ResetCounts();
        var unrelated = OriginBookProseDraft.Create(next, book.Locale, "legacy-other-request",
            new string('e', 64), "Synthetic legacy proposal with a different authoring identity.");
        book = (await runtime.Coordinator.StageOriginBookProseDraftAsync(book, unrelated, () => true, default))!;
        string unrelatedEdition = book.Readings!.Digest;
        var identityConflict = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, default);
        Require(identityConflict.Result.Outcome == AndroidOriginChapterOutcome.Conflict && remote.Requests == 0
            && new OriginBookReadingStore(runtime.StateDirectory).Load(OwnerScope.LocalSingleUser.Value, id.Value).Digest == unrelatedEdition,
            "A different legacy job was rebound, discarded or dispatched instead of returning a conflict.");
        book = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(book, unrelated, false, false, () => true, default))!;
        remote.ResetCounts();
        remote.BeforeRequest = source =>
        {
            var savedSource = new OriginBookReadingStore(runtime.StateDirectory).Load(OwnerScope.LocalSingleUser.Value, id.Value)
                .Chapters.Single(c => c.ChapterId == next.ChapterId).AuthoringSource;
            Require(savedSource is not null
                && Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.Digest(savedSource)
                    == Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.Digest(source),
                "HTTP dispatch began before the exact approved source was durable.");
        };
        remote.LoseRequestResponse = true;
        var recovered = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, default);
        Require(recovered.Result.UnknownRemoteOutcome && recovered.Book is not null && remote.Requests == 1 && remote.Acceptances == 0,
            "Unknown dispatch lost its frozen edition, resent the predecessor, or repeated the successor.");
        Require(!runtime.Coordinator.IsRetainedOriginBookCurrent(book), "The pre-dispatch reading edition remained admitted.");
        book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
        remote.LoseRequestResponse = false;
        var readBack = await runtime.Coordinator.SyncOriginChapterAsync(book, next, book.AuthoringSource(next), true, () => true, default);
        Require(readBack.Result.Outcome == AndroidOriginChapterOutcome.Available && remote.Requests == 1 && remote.Acceptances == 0,
            "Cold status recovery generated the existing successor twice.");
        book = readBack.Book!;
        remote.RemoveSuccessor();
        remote.Seed(firstSource, draft); // Simulate an independently lost server acknowledgement again.
        remote.ResetCounts();
        var restored = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, default);
        Require(restored.Result.Outcome == AndroidOriginChapterOutcome.Available && remote.Acceptances == 1 && remote.Requests == 1,
            "Cold saved acceptance did not recover then generate exactly one successor in order.");
        book = restored.Book!;
        remote.RemoveSuccessor(); remote.Seed(firstSource, draft); remote.ResetCounts();
        remote.AfterAcceptance = () => { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); };
        var aba = await runtime.Coordinator.SyncOriginChapterAsync(book, next, nextSource, true, () => true, default);
        Require(aba.Book is null && remote.Acceptances == 1 && remote.Requests == 0,
            "Owner A-to-B-to-A dispatched after predecessor acknowledgement.");
        var finalReading = new OriginBookReadingStore(runtime.StateDirectory).Load(OwnerScope.LocalSingleUser.Value, id.Value);
        Require(finalReading.Chapters.Single(c => c.ChapterId == first.ChapterId).Selected?.DraftDigest == draft.DraftDigest
            && finalReading.Chapters.Single(c => c.ChapterId == next.ChapterId) is { Pending: null, Selected: null, AuthoringSource: not null }
            && finalReading.Digest != readingDigest,
            "Dispatch did not retain its source separately from immutable accepted prose.");
        RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
        Console.WriteLine("PASS successor: cold explicit acceptance recovery, read-only/no-replay boundaries, failed/mismatched reads, cancellation and owner ABA");
    }

    internal static async Task RunLifeModuleLinkedOwnerStartAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
            productionCreationOverview: true, linkedOwners: owners,
            lifeCompletionDecorator: actual => actual);
        await runtime.Coordinator.InitializeAsync();
        await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
        await runtime.Coordinator.CreateRunnerAsync();
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Linked Life Modules", default);
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
        await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
        var created = runtime.Coordinator.State;
        Require(created.WorkspaceId is not null && created.DisplayOwnerContext?.Owner == ContactsOwnerA,
            "The linked-owner creation fixture did not create a runner for owner A.");
        Require(runtime.Coordinator.IsLifeModuleDashboardCurrent(created),
            $"Linked Life Modules dashboard unavailable: foundation={created.CreationFoundation is not null}, "
            + $"revision={created.ContentRevision}/{created.SavedRevision}, error={created.Error is not null}.");
        var opened = await runtime.Coordinator.OpenSr5LifeModuleOriginAsync();
        Require(opened.IsSuccess && opened.State is not null,
            "Linked Life Modules could not load its first real Core decision: " + string.Join(",", opened.Blockers));
        var owner = owners.Capture();
        var id = created.WorkspaceId!.Value;
        Require(opened.State!.OwnerId == ContactsOwnerA.NormalizedValue,
            "The linked Origin decision is still labeled as local-single-user.");
        var openingDetails = await runtime.Coordinator.LoadOpeningStoryDetailsAsync(opened.StoryCheckpoint!, () => true);
        Require(openingDetails is { StoryProfile: null, Chapters.Count: 0 }
            && await runtime.Coordinator.LoadOpeningStoryDetailsAsync(opened.StoryCheckpoint!, () => false) is null,
            "The opening editor was unavailable before the first choice, or loaded for a departed page.");
        var profile = new OriginStoryProfile("other", "they/them", "hopeful", "Find a home", "Mara");
        Require(await runtime.Coordinator.SaveOpeningStoryDetailsAsync(openingDetails! with { }, profile, () => true) is null
            && await runtime.Coordinator.SaveOpeningStoryDetailsAsync(openingDetails!, profile, () => false) is null,
            "An unissued or departed opening editor wrote story details.");
        var choice = opened.StoryCheckpoint!.Projection.CurrentTurn.LegalChoices.First(row =>
            row.Label.StartsWith("Elf ·", StringComparison.Ordinal)
            && row.SourceAnchorIds.Any(anchor => anchor.Contains("604831d9-0fdc-4579-aa7e-bc5d99bcee5d", StringComparison.Ordinal)));
        var answers = choice.FollowUps?.ToDictionary(prompt => prompt.PromptId,
            prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Renraku");
        var prepared = await runtime.Coordinator.PrepareSr5LifeModuleOriginAsync(choice.ChoiceId, followUpValues: answers);
        Require(prepared.IsSuccess && prepared.State?.PendingPreviewDigest is not null,
            "Linked choice review failed: " + string.Join(",", prepared.Blockers));
        var savedDetails = await runtime.Coordinator.SaveOpeningStoryDetailsAsync(openingDetails!, profile, () => true);
        Require(savedDetails?.StoryProfile == profile
            && new OriginBookReadingStore(runtime.StateDirectory).Load(ContactsOwnerA.Value, id.Value).Digest == savedDetails.Digest
            && new OriginBookReadingStore(runtime.StateDirectory).Load(ContactsOwnerB.Value, id.Value).StoryProfile is null,
            "Preparing the real Core choice lost the issued opening editor, persistence or owner isolation.");
        var confirmed = await runtime.Coordinator.ConfirmSr5LifeModuleOriginAsync(
            choice.ChoiceId, prepared.State!.PendingPreviewDigest!);
        Require(confirmed.IsSuccess && confirmed.State?.Timeline.Count == 1,
            "Linked choice commit/reload failed: " + string.Join(",", confirmed.Blockers));
        await runtime.Coordinator.SaveAsync();
        var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, id).Value!;
        Require(cold.ContentRevision == 2 && cold.SavedRevision == 2
            && !new FileWorkspaceStore(runtime.StateDirectory).Get(id).Success,
            "Linked choice was not saved exactly once, or leaked into the local store.");
        var service = runtime.Services.GetRequiredService<IOwnerBoundLifeModuleOriginService>();
        var freshPhone = new OriginDossierLifeModulePhoneRuntime(service,
            new FileOriginDossierDraftTimelineStore(runtime.StateDirectory));
        var reopened = await freshPhone.OpenAsync(owner, id.Value);
        Require(reopened.IsSuccess && reopened.State?.Timeline.Count == 1
            && reopened.StoryCheckpoint?.CheckpointDigest == confirmed.StoryCheckpoint!.CheckpointDigest,
            "A new phone runtime/disk read did not recover the exact linked chapter.");
        var retainedOpening = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
        Require(retainedOpening is not null && retainedOpening.Digest == confirmed.StoryCheckpoint!.Projection.SeedDigest
            && !retainedOpening.OpeningSetupComplete
            && !await runtime.Coordinator.HasReadCurrentLifeModuleStoryAsync(confirmed.StoryCheckpoint!, () => true),
            "The retained Core book and live checkpoint disagree, or birth alone became a completed first story.");
        Require(retainedOpening!.Readings?.StoryProfile == profile
            && await runtime.Coordinator.LoadOpeningStoryDetailsAsync(confirmed.StoryCheckpoint!, () => true) is null
            && await runtime.Coordinator.SaveOpeningStoryDetailsAsync(savedDetails!, profile with { Tone = "dark" }, () => true) is null,
            "The opening brief was lost, or an old editor could rewrite the story after the first accepted decision.");
        Require(retainedOpening.Opportunities is { Opportunities.Count: > 0 } hints
            && hints.DecisionDigest == confirmed.StoryCheckpoint!.Projection.CurrentTurn.DecisionDigest
            && hints.Opportunities.All(h => confirmed.StoryCheckpoint!.Projection.AllowedChoiceIds.Contains(h.ChoiceId)),
            "Real Core continuation did not supply exact current source-bound story opportunities.");
        var stored = new FileOriginDossierDraftTimelineStore(runtime.StateDirectory);
        Require(await stored.LoadAsync(OwnerScope.LocalSingleUser.NormalizedValue, id.Value) is null,
            "Linked Origin checkpoint leaked into the local timeline namespace.");
        owners.Set(ContactsOwnerB);
        Require(service.Start(owners.Capture(), id.Value).Value is null
            && service.Restore(owners.Capture(), prepared.StoryCheckpoint!).Value is null,
            "Another owner read the linked decision or restored its checkpoint.");
        owners.Set(ContactsOwnerA);
        Require(!service.IsCurrent(owner)
            && service.Confirm(owner, prepared.StoryCheckpoint!, prepared.State.PendingPreviewDigest!, "stale-linked-origin", true).Value is null,
            "Owner A-to-B-to-A accepted an old confirmation authority.");
        RequireSameRewardDocument(cold, new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, id).Value!);
        Require(service.Restore(owners.Capture(), reopened.StoryCheckpoint!).Value is not null,
            "Returning owner could not restore the current persisted chapter with fresh admission.");
        Console.WriteLine("PASS linked-owner Life Modules start, review, commit, save, cold reopen, namespace isolation and ABA rejection");
    }

    internal static async Task RunOriginReaderLocalTextAsync(string contentRoot, string? smokeDirectory = null,
        bool automatic = false, bool recoverUnreadSequence = false)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            var account = DispatchProxy.Create<IAndroidAccountLinkService, OriginSuccessorAccount>();
            var remote = (OriginSuccessorAccount)account;
            var output = new LifeBookOutputProbe();
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, accountService: account, outputDocuments: output);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Saved book while next chapter waits", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await runtime.Coordinator.SaveAsync();
            await Task.Run(() => SeedNativeLifeStory(runtime, id));
            await runtime.Presenter.LoadAsync(id, default);
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            var first = book.Chapters[1];
            var next = book.Chapters[2];
            var source = book.AuthoringSource(first);
            var prose = OriginBookProseDraft.Create(first, book.Locale,
                Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.RequestId(source), new string('d', 64),
                "The complete saved opening. This text must stay readable while the next chapter is still being written.");
            void CheckExportActions(RetainedOriginBook edition, bool available)
            {
                var reader = new RetainedOriginBookPage(runtime.Coordinator);
                typeof(RetainedOriginBookPage).GetField("_book",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(reader, edition);
                typeof(RetainedOriginBookPage).GetMethod("Refresh",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(reader, null);
                foreach (string exportId in new[] { "origin-book-export", "origin-book-export-epub" })
                    Require(IssuedElements(reader).OfType<Button>().Any(b => b.AutomationId == exportId && b.IsEnabled) == available
                        && (available || !IssuedElements(reader).Any(e => e.AutomationId == exportId)),
                        "HTML/EPUB actions must be absent until full chapter prose is accepted, then become available.");
            }
            CheckExportActions(book, false);
            remote.Seed(source, prose);
            book = (await runtime.Coordinator.StageOriginBookProseDraftAsync(book, prose, () => true, default))!;
            CheckExportActions(book, false);
            if (recoverUnreadSequence)
            {
                // Restored multi-decision books can contain an unread opening
                // while later already-confirmed Life Modules still need prose.
                // Reading must resume that exact history, not require reopening
                // the page or expose an extra provider-writing button.
                var recoveredReader = new RetainedOriginBookPage(runtime.Coordinator);
                var recoveredWindow = new Window(new NavigationPage(recoveredReader));
                using var recoveredAlerts = new IssuedPageAlerts(recoveredReader, recoveredWindow);
                await recoveredAlerts.PreflightAsync();
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(recoveredReader, "OnAppearing"));
                Require(remote.Requests == 0 && remote.Acceptances == 0,
                    "Opening recovered prose implicitly accepted it or started a successor.");
                var read = IssuedElements(recoveredReader).OfType<Button>().Single(b =>
                    b.AutomationId == $"origin-chapter-read-{first.Sequence}");
                var acknowledgementEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var acknowledgementRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                remote.BeforeAcceptance = ct =>
                {
                    acknowledgementEntered.TrySetResult();
                    return acknowledgementRelease.Task.WaitAsync(ct);
                };
                Task reading = ui.BeginAsyncVoid(() => ((IButtonController)read).SendClicked());
                try
                {
                    await acknowledgementEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(remote.Requests == 0, "A successor started before its predecessor acknowledgement.");
                    Require(IssuedElements(recoveredReader).OfType<ActivityIndicator>().Any(e =>
                        e.AutomationId == "origin-reader-writing-spinner" && e.IsRunning && e.IsVisible),
                        "Automatic recovered continuation has no visible busy indicator.");
                    var exportEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    output.BeforeRead = () => exportEntered.TrySetResult();
                    var export = IssuedElements(recoveredReader).OfType<Button>().Single(b => b.AutomationId == "origin-book-export-epub");
                    // Join both real async-void handlers through the existing
                    // reading task; BeginAsyncVoid admits only an idle pump.
                    ((IButtonController)export).SendClicked();
                    await exportEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { acknowledgementRelease.TrySetResult(); }
                await reading;
                output.BeforeRead = null;
                Require(output.EpubDeliveries == 1, "Waiting for restored successor admission blocked export of read prose.");
                remote.BeforeAcceptance = null;
                Require(remote.Acceptances == 1 && remote.Requests == 1,
                    "Reading a recovered chapter did not automatically resume its already-confirmed successor.");
                long recoveredGeneration = IssuedPageField<long>(recoveredReader, "_appearanceGeneration");
                await recoveredReader.PollChapterOnceAsync(recoveredGeneration, default);
                Require(IssuedElements(recoveredReader).Any(e => e.AutomationId == "origin-reader-writing-spinner")
                    && !IssuedElements(recoveredReader).Any(e => e.AutomationId == $"origin-read-chapter-effects-{next.Sequence}"),
                    "Successor observation lacks visible progress or exposes unread mechanics.");
                IssuedPageLifecycle(recoveredReader, "OnDisappearing");
                await recoveredReader.PollChapterOnceAsync(recoveredGeneration, default);
                var reopened = new RetainedOriginBookPage(runtime.Coordinator);
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                Require(remote.Acceptances == 1 && remote.Requests == 1,
                    "Status observation or reopen replayed reader acceptance or successor generation.");
                var restoredReading = new OriginBookReadingStore(runtime.StateDirectory).Load(owners.Capture().Owner.Value, id.Value);
                Require(restoredReading.Chapters.Single(c => c.ChapterId == first.ChapterId).Selected?.Text == prose.Text
                    && restoredReading.Chapters.Single(c => c.ChapterId == next.ChapterId) is { AuthoringSource: not null, Selected: null },
                    "Cold reading state lost the explicit decision or exact successor admission.");
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
                IssuedPageLifecycle(reopened, "OnDisappearing");
                Require(recoveredAlerts.Titles.Count == 0, "Recovered chapter continuation displayed an unexpected error.");
                ui.AssertHealthy();
                Console.WriteLine("PASS recovered multi-decision book: explicit read resumes exact successor, progress/export responsive, cold no-replay and unchanged runner");
                return;
            }
            book = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(book, prose, true, true, () => true, default))!;
            CheckExportActions(book, true);
            if (!automatic)
            {
                var queued = await runtime.Coordinator.SyncOriginChapterAsync(book, next, book.AuthoringSource(next), true, () => true, default);
                Require(queued.Result.Job?.State == Chummer.Run.Contracts.Community.OriginChapterAuthoringStates.AwaitingAuthoring
                    && queued.Book?.Reading(next)?.AuthoringSource is not null,
                    "The reader fixture needs an accepted full chapter and one existing pending successor.");
            }
            else
                Require(await runtime.Coordinator.RecordOriginBookReaderAcceptanceAsync(book, prose, () => true, default),
                    "The automatic successor fixture needs the first chapter's explicit read acknowledgement.");
            int requests = remote.Requests, acceptances = remote.Acceptances;
            if (automatic) remote.SuccessorReadFailure = new(AndroidOriginChapterOutcome.Unavailable,
                RetryableReadFailure: true);
            var page = new RetainedOriginBookPage(runtime.Coordinator);
            var window = new Window(new NavigationPage(page));
            using var alerts = new IssuedPageAlerts(page, window);
            await alerts.PreflightAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            remote.BeforeSuccessorRead = () => { entered.TrySetResult(); return release.Task; };
            Task appearance = ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            bool visibleDuringRead = false;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                visibleDuringRead = IssuedElements(page).OfType<Label>().Any(label =>
                    label.AutomationId == $"origin-retained-chapter-{first.Sequence}" && label.Text == prose.Text)
                    && !IssuedElements(page).Any(e => e.AutomationId == "origin-book-loading")
                    && IssuedElements(page).OfType<Button>().Any(b => b.AutomationId == "origin-book-export-epub" && b.IsEnabled);
                Require(IssuedElements(page).OfType<Label>().First(label =>
                        label.AutomationId?.StartsWith("origin-retained-chapter-", StringComparison.Ordinal) == true
                        || label.AutomationId?.StartsWith("origin-reader-status-", StringComparison.Ordinal) == true)
                    .AutomationId == $"origin-retained-chapter-{first.Sequence}",
                    "Pending chapter placeholders pushed existing full prose below the reading content.");
                var layout = page.Content as Grid;
                var pinned = IssuedElements(page).Single(e => e.AutomationId == "origin-reader-pinned-progress");
                Require(layout is not null && layout.Children.Contains((Microsoft.Maui.IView)pinned)
                    && Grid.GetRow(pinned) == 0 && IssuedElements(page).OfType<ActivityIndicator>()
                        .Any(e => e.AutomationId == "origin-reader-writing-spinner" && e.IsRunning),
                    "The active generation/status spinner can scroll off screen.");
            }
            finally
            {
                release.TrySetResult();
                await appearance.WaitAsync(TimeSpan.FromSeconds(15));
                remote.BeforeSuccessorRead = null;
            }
            Require(visibleDuringRead, "An existing full chapter and its EPUB action remained hidden behind the next chapter's network status read.");
            if (automatic)
            {
                // A connection can fail before the request has any durable
                // authoring identity. Exhaust only the bounded read reserve,
                // then recover through the real visible page action.
                long initialGeneration = IssuedPageField<long>(page, "_appearanceGeneration");
                await page.PollChapterOnceAsync(initialGeneration, default);
                await page.PollChapterOnceAsync(initialGeneration, default);
                Require(remote.Requests == requests && remote.Acceptances == acceptances
                    && new OriginBookReadingStore(runtime.StateDirectory).Load(owners.Capture().Owner.Value, id.Value)
                        .Chapters.All(c => c.ChapterId != next.ChapterId || c.AuthoringSource is null),
                    "Failed pre-admission reads generated or froze a new paid chapter.");
                Require(!IssuedElements(page).OfType<ActivityIndicator>().Any(e =>
                    e.AutomationId == "origin-reader-writing-spinner" && e.IsRunning),
                    "The exhausted status reserve still shows an active generation spinner.");
                var recover = IssuedElements(page).OfType<Button>().SingleOrDefault(b =>
                    b.AutomationId == "origin-reader-refresh" && b.IsEnabled);
                Require(recover is not null,
                    "A paused pre-admission chapter has no visible recovery action; the user is stuck until reopening the book.");
                remote.SuccessorReadFailure = null;
                await ui.BeginAsyncVoid(() => ((IButtonController)recover!).SendClicked());
                Require(remote.Requests == requests + 1 && remote.Acceptances == acceptances,
                    "Recovering a pre-admission connection failure did not start the saved next chapter exactly once.");
                requests = remote.Requests;
                Require(!IssuedElements(page).Any(e => e.AutomationId is "origin-authoring-consent" or "origin-authoring-request"
                    || e.AutomationId?.StartsWith("origin-author-chapter-", StringComparison.Ordinal) == true),
                    "The automatic reader still exposes a consent toggle or separate writing button.");
            }
            long generation = IssuedPageField<long>(page, "_appearanceGeneration");
            // An unexpected read/storage exception stops observation, not the
            // provider job. The pinned UI must stop claiming that it is still
            // checking, preserve saved prose, and allow an explicit read-only
            // recovery without spending credits again.
            var displayedProse = IssuedElements(page).OfType<Label>().Single(e =>
                e.AutomationId == $"origin-retained-chapter-{first.Sequence}");
            var displayedExport = IssuedElements(page).OfType<Button>().Single(e =>
                e.AutomationId == "origin-book-export-epub");
            remote.BeforeSuccessorRead = () => Task.FromException(new IOException("Synthetic chapter observation failure."));
            try { await page.PollChapterOnceAsync(generation, default); }
            finally { remote.BeforeSuccessorRead = null; }
            Require(alerts.Titles.Count == 1, "An unexpected chapter observation error was swallowed.");
            Require(!IssuedElements(page).OfType<ActivityIndicator>().Any(e =>
                e.AutomationId == "origin-reader-writing-spinner" && e.IsRunning),
                "A stopped chapter observer left its pinned spinner running after an exception.");
            var pinnedStatus = (VerticalStackLayout)IssuedElements(page).Single(e =>
                e.AutomationId == "origin-reader-pinned-progress");
            var copy = AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name);
            Require(pinnedStatus.Children.OfType<Label>().Any(e => e.Text == copy["Origin.AuthoringStatusPaused"]),
                "The observation pause is not explained in the always-visible reader status.");
            Require(ReferenceEquals(displayedProse, IssuedElements(page).OfType<Label>().Single(e =>
                    e.AutomationId == $"origin-retained-chapter-{first.Sequence}"))
                && ReferenceEquals(displayedExport, IssuedElements(page).OfType<Button>().Single(e =>
                    e.AutomationId == "origin-book-export-epub")),
                "Pausing the status observer rebuilt the user's reading or export controls.");
            int stoppedReads = remote.Reads;
            await page.PollChapterOnceAsync(generation, default);
            Require(remote.Reads == stoppedReads, "An unclassified failure automatically retried the chapter read.");
            var retryObservation = IssuedElements(page).OfType<Button>().Single(b =>
                b.AutomationId == "origin-reader-refresh" && b.IsEnabled);
            var recoveryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var recoveryReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            remote.BeforeSuccessorRead = () => { recoveryEntered.TrySetResult(); return recoveryReleased.Task; };
            Task recovering = ui.BeginAsyncVoid(() => ((IButtonController)retryObservation).SendClicked());
            try
            {
                await recoveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(pinnedStatus.Children.OfType<ActivityIndicator>().Any(e => e.IsRunning && e.IsVisible)
                    && !pinnedStatus.Children.OfType<Label>().Any(e => e.Text == copy["Origin.AuthoringStatusPaused"]),
                    "An explicit active status check still presents itself as paused.");
            }
            finally
            {
                recoveryReleased.TrySetResult();
                await recovering.WaitAsync(TimeSpan.FromSeconds(15));
                remote.BeforeSuccessorRead = null;
            }
            Require(remote.Requests == requests && remote.Acceptances == acceptances,
                "Explicit observer recovery replayed generation or reader acceptance.");
            Require(IssuedElements(page).OfType<ActivityIndicator>().Any(e =>
                e.AutomationId == "origin-reader-writing-spinner" && e.IsRunning)
                && IssuedElements(page).OfType<Label>().Any(e => e.Text == prose.Text),
                "Observer recovery failed to resume pending status while preserving the complete saved chapter.");
            Console.WriteLine("PASS unexpected reader status failure: pinned pause, unchanged prose/export controls, explicit recovery and no paid replay");
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            remote.BeforeSuccessorRead = () => { entered.TrySetResult(); return release.Task; };
            Task poll = page.PollChapterOnceAsync(generation, default);
            bool exportedDuringRead = false;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                int activeReads = remote.Reads;
                await page.PollChapterOnceAsync(generation, default);
                await page.PollChapterOnceAsync(generation - 1, default);
                using (var canceled = new CancellationTokenSource())
                {
                    canceled.Cancel();
                    await page.PollChapterOnceAsync(generation, canceled.Token);
                }
                Require(remote.Reads == activeReads && !poll.IsCompleted,
                    "Concurrent, departed or canceled observations duplicated the active chapter read.");
                var export = IssuedElements(page).OfType<Button>().Single(b => b.AutomationId == "origin-book-export-epub");
                await ui.BeginAsyncVoid(() => ((IButtonController)export).SendClicked()).WaitAsync(TimeSpan.FromSeconds(10));
                var html = IssuedElements(page).OfType<Button>().Single(b => b.AutomationId == "origin-book-export");
                await ui.BeginAsyncVoid(() => ((IButtonController)html).SendClicked()).WaitAsync(TimeSpan.FromSeconds(10));
                exportedDuringRead = output.EpubDeliveries == 1 && output.Epub.Length > 0
                    && output.Deliveries == 1 && output.Html.Contains(prose.Text, StringComparison.Ordinal) && !poll.IsCompleted;
            }
            finally
            {
                release.TrySetResult();
                await poll.WaitAsync(TimeSpan.FromSeconds(15));
                remote.BeforeSuccessorRead = null;
                IssuedPageLifecycle(page, "OnDisappearing");
            }
            Require(exportedDuringRead, "A background chapter-status read held the action gate or blocked HTML/EPUB export of completed chapters.");
            int departedReads = remote.Reads;
            await page.PollChapterOnceAsync(generation, default);
            Require(remote.Reads == departedReads && !IssuedElements(page).Any(e => e is Button or Label),
                "The departed reader continued private status work or retained book content.");
            Require(remote.Requests == requests && remote.Acceptances == acceptances && alerts.Titles.Count == 1,
                "Reading/exporting the saved book generated, accepted or failed an unrelated chapter.");
            var cold = new OriginBookReadingStore(runtime.StateDirectory).Load(owners.Capture().Owner.Value, id.Value);
            Require(cold.Chapters.Single(c => c.ChapterId == first.ChapterId).Selected?.DraftDigest == prose.DraftDigest
                && cold.Chapters.Single(c => c.ChapterId == next.ChapterId).Selected is null,
                "The reader changed the accepted or pending chapter during read-only status checks.");
            RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
            if (automatic)
            {
                remote.RemoveSuccessor();
                var reopened = new RetainedOriginBookPage(runtime.Coordinator);
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                Require(remote.Requests == requests && remote.Acceptances == acceptances,
                    "Reopening an admitted but missing/uncertain job spent credits a second time.");
                Require(!IssuedElements(reopened).Any(e => e.AutomationId == $"origin-read-chapter-effects-{next.Sequence}"),
                    "An unread or ungenerated chapter exposed its rule changes.");
                Require(IssuedElements(reopened).OfType<Button>().Count(b => b.IsEnabled
                    && b.AutomationId is "origin-book-export" or "origin-book-export-epub") == 2,
                    "Reopening a book with completed prose and a missing successor hid its HTML/EPUB actions.");
                IssuedPageLifecycle(reopened, "OnDisappearing");
                Console.WriteLine("PASS automatic chapter: bounded pre-admission failure and visible recovery, one dispatch, pinned spinner, cold no-replay, hidden unread mechanics");
            }
            ui.AssertHealthy();
            Console.WriteLine("PASS HTML/EPUB absent without completed prose; both export saved chapters during pending status and cold reopen, no runner mutation");
            if (smokeDirectory is not null)
            {
                // Synthetic offline fixture only, for an isolated emulator app.
                // Never export a real account store or claim provider execution.
                Require(Path.IsPathFullyQualified(smokeDirectory) && !Directory.Exists(smokeDirectory),
                    "Native smoke output must be a fresh absolute directory.");
                Directory.CreateDirectory(smokeDirectory);
                foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*.json", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
                Console.WriteLine("ORIGIN_READER_SMOKE_WORKSPACE " + id.Value);
                Console.WriteLine("ORIGIN_READER_SMOKE_READING_DIGEST " + cold.Digest);
            }
        });
    }

    internal static async Task RunOriginReaderNextModuleAsync(string contentRoot, string? smokeDirectory = null)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            // The real Android host always supplies an app locale; the
            // isolated Linux test process otherwise starts invariant.
            CultureInfo.CurrentUICulture = new("de-DE");
            var owners = new ControlledLinkedOwner();
            var account = DispatchProxy.Create<IAndroidAccountLinkService, OriginSuccessorAccount>();
            var remote = (OriginSuccessorAccount)account;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, accountService: account,
                lifeCompletionDecorator: actual => actual);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Chapter to next Life Module", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await runtime.Coordinator.SaveAsync();
            // Only birth and childhood are committed. Teen years must wait
            // for the full opening chapter, not a finished allocation fixture.
            await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 2));
            await runtime.Presenter.LoadAsync(id, default);
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            var chapter = book.Chapters.Single(c => !book.IsOpeningSetup(c));
            var source = book.AuthoringSource(chapter);
            string text = string.Join("\n\n", Enumerable.Range(1, 60).Select(index =>
                $"Scene {index}. Mara remembered the rain over Renraku's courtyard. She and her friend had spent the afternoon repairing a broken radio, listening for a voice beyond the corporate walls. Tomorrow, she would have to choose where to go."))
                + "\n\nEnd of the complete childhood chapter.";
            var prose = OriginBookProseDraft.Create(chapter, book.Locale,
                Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.RequestId(source), new string('e', 64), text);
            remote.Seed(source, prose);
            var received = await runtime.Coordinator.SyncOriginChapterAsync(book, chapter, source,
                consentToCreate: false, () => true, default, reconcileReaderAcceptance: false);
            Require(received.Book?.Reading(chapter)?.AuthoringSource is not null && remote.Requests == 0
                && remote.Acceptances == 0, "The completed chapter read did not retain its exact source without a new request.");
            book = received.Book!;
            Require(!book.HasReadCurrentStory, "Staged prose unexpectedly unlocked the next module.");
            if (smokeDirectory is not null)
            {
                Require(Path.IsPathFullyQualified(smokeDirectory) && !Directory.Exists(smokeDirectory),
                    "Native fixture output must be a fresh absolute directory.");
                Directory.CreateDirectory(smokeDirectory);
                foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*.json", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
                Console.WriteLine("ORIGIN_READER_SMOKE_WORKSPACE " + id.Value);
            }
            var root = new BuildPage(runtime.Coordinator);
            ((ScrollView)root.Content!).ScrollToRequested += (_, _) => ((ScrollView)root.Content!).SendScrollFinished();
            var navigation = new NavigationPage(root);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(root, window);
            await alerts.PreflightAsync();
            await Appear(root);
            Require(runtime.Coordinator.CanOpenSr5LifeModuleOrigin(), "The saved childhood fixture cannot continue through the real Origin runtime.");
            Require(!IssuedElements(root).Any(e => e.AutomationId == "creation-life-module-budget"),
                "Unread module bonuses were shown before the chapter.");
            await Click("creation-life-module-continue");
            var decision = navigation.Navigation.NavigationStack.OfType<OriginDossierLifeModuleDecisionPage>().SingleOrDefault();
            Require(decision is not null, "Continue did not open the current Life Modules decision: "
                + string.Join(" -> ", navigation.Navigation.NavigationStack.Select(p => p.GetType().Name))
                + "; " + string.Join("; ", alerts.Messages));
            if (ReferenceEquals(Current(), decision)) await Appear(decision!);
            Require(Current() is RetainedOriginBookPage
                && !IssuedElements(decision!).Any(e => e.AutomationId?.StartsWith("origin-life-choice-", StringComparison.Ordinal) == true),
                "The real decision route offered the next module before leading to the unread full chapter.");
            Leave(decision!);
            var reader = Current();
            await Appear(reader);
            Require(IssuedElements(reader).OfType<Label>().Single(e => e.AutomationId == $"origin-retained-chapter-{chapter.Sequence}").Text == text
                && !IssuedElements(reader).Any(e => e.AutomationId == $"origin-read-chapter-effects-{chapter.Sequence}"),
                "The native reader truncated the complete chapter or exposed unread bonuses.");
            remote.FailAcceptance = true;
            var acceptanceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var acceptanceRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken acceptanceToken = default;
            remote.BeforeAcceptance = ct =>
            {
                acceptanceToken = ct;
                acceptanceEntered.TrySetResult();
                // Model a transport that returns after navigation, even when
                // cancellation was requested. It must not revive the old page.
                return acceptanceRelease.Task;
            };
            var readButton = IssuedElements(reader).OfType<Button>().Single(e => e.AutomationId == $"origin-chapter-read-{chapter.Sequence}");
            Task acknowledgment = ui.BeginAsyncVoid(() => ((IButtonController)readButton).SendClicked());
            try
            {
                await acceptanceEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(IssuedElements(reader).Any(e => e.AutomationId == $"origin-read-chapter-effects-{chapter.Sequence}")
                    && IssuedElements(reader).OfType<Button>().Any(e => e.AutomationId == "origin-book-export-epub" && e.IsEnabled),
                    "The durable reading decision and export stayed hidden behind its server acknowledgment.");
                var back = IssuedElements(reader).OfType<Button>().Single(e => e.AutomationId == "origin-book-return-to-runner");
                ((IButtonController)back).SendClicked();
                // Inspect the real navigation stack: headless MAUI has no
                // platform animation completion to issue the Popped event.
                await Task.Yield();
                Require(ReferenceEquals(Current(), decision), "A slow reader acknowledgment blocked returning to the next module.");
                Leave(reader);
                Require(acceptanceToken.IsCancellationRequested, "Leaving the reader did not cancel its pending acknowledgment.");
            }
            finally
            {
                acceptanceRelease.TrySetResult();
                await acknowledgment.WaitAsync(TimeSpan.FromSeconds(15));
                remote.BeforeAcceptance = null;
            }
            Require(remote.Acceptances == 1 && remote.Requests == 0
                && !IssuedElements(reader).Any(e => e.AutomationId == $"origin-read-chapter-effects-{chapter.Sequence}"),
                "A late remote acknowledgment revived a departed reader or generated a chapter.");
            Require(ReferenceEquals(Current(), decision), "Read completion returned to the wrong route.");
            await Appear(decision!);
            Require(ReferenceEquals(Current(), decision)
                && IssuedElements(decision).OfType<Button>().Any(e => e.IsEnabled
                    && e.AutomationId?.StartsWith("origin-life-choice-", StringComparison.Ordinal) == true),
                "Returning from the complete chapter did not unlock the real next Life Modules choices.");
            var cold = new OriginBookReadingStore(runtime.StateDirectory).Load(owners.Capture().Owner.Value, id.Value);
            Require(cold.Chapters.Single().Selected?.Text == text && cold.Chapters.Single().Pending is null,
                "Reading confirmation did not survive a fresh disk read.");
            RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
            Require(alerts.Titles.Count == 0 && remote.Requests == 0 && remote.Acceptances == 1,
                "The reading route mutated or re-generated the character's story.");
            Leave(decision!); Leave(root);
            ui.AssertHealthy();
            Console.WriteLine("PASS actual Core birth/childhood -> full reader -> explicit read -> next module while acknowledgment is pending, cancellation/late-result isolation, durable local acceptance, no paid generation");

            Page Current() => navigation.Navigation.NavigationStack.Last();
            void Lifecycle(Page page, string method) => page.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null)!.Invoke(page, null);
            void Leave(Page page) => Lifecycle(page, "OnDisappearing");
            Task Appear(Page page) => page is NativePageBase native && IssuedPageField<int>(native, "_subscribed") != 0
                ? Task.CompletedTask : ui.BeginAsyncVoid(() => Lifecycle(page, "OnAppearing"))
                    .WaitAsync(TimeSpan.FromSeconds(30));
            async Task Click(string key)
            {
                var page = Current();
                var button = IssuedElements(page).OfType<Button>().Single(e => e.AutomationId == key);
                Require(button.IsEnabled, "Disabled reading-route action: " + key);
                using var actionAlerts = new IssuedPageAlerts(page, window);
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked()).WaitAsync(TimeSpan.FromSeconds(30));
                Require(actionAlerts.Titles.Count == 0, "Reading-route action failed: " + key + ": " + string.Join("; ", actionAlerts.Messages));
                if (!ReferenceEquals(page, Current())) Leave(page);
            }
        });
    }

    internal static async Task RunLifeModuleCompletionPagesAsync(string contentRoot)
    {
        await RunOriginBookCredentialReadContentionAsync(contentRoot);
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            var bookOutput = new LifeBookOutputProbe();
            var sceneInput = new LifeSceneInputProbe();
            var authoringAccount = System.Reflection.DispatchProxy.Create<IAndroidAccountLinkService, OriginAuthoringPageAccount>();
            var authoringProbe = (OriginAuthoringPageAccount)authoringAccount;
            LifeCompletionNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, outputDocuments: bookOutput,
                accountService: authoringAccount,
                originSceneDocuments: sceneInput,
                lifeCompletionDecorator: actual => probe = new(actual));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Life Modules phone choices", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            var unsaved = runtime.Coordinator.State;
            Require(unsaved.ContentRevision == 1 && unsaved.SavedRevision == 0
                && runtime.Coordinator.IsLifeModuleDashboardCurrent(unsaved),
                "Fresh Life Modules setup did not expose the actual unsaved dashboard.");
            await runtime.Coordinator.SaveAsync();
            Require(runtime.Coordinator.State.ContentRevision == 1 && runtime.Coordinator.State.SavedRevision == 1
                && runtime.Coordinator.HasDurableSaveNotice
                && runtime.Coordinator.IsLifeModuleDashboardCurrent(runtime.Coordinator.State)
                && runtime.Coordinator.CanOpenSr5LifeModuleOrigin()
                && runtime.Coordinator.State.CreationFoundation!.Binding.SavedRevision == 1
                && !ReferenceEquals(unsaved.CreationFoundation, runtime.Coordinator.State.CreationFoundation),
                "Toolbar Save left the Life Modules dashboard bound to the old saved revision.");
            var alreadySaved = runtime.Coordinator.State.CreationFoundation;
            await runtime.Coordinator.SaveAsync();
            Require(ReferenceEquals(alreadySaved, runtime.Coordinator.State.CreationFoundation),
                "Saving an unchanged Life Modules runner needlessly rebuilt its current projection.");
            var beforeOpening = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var emptyLoad = await runtime.Coordinator.LoadOriginBookReaderAsync(default, () => true);
            Require(emptyLoad is { OpeningNotStarted: true, Book: null }
                && runtime.Coordinator.CanReadRetainedOriginBook(emptyLoad.Display)
                && await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true) is null,
                "A verified empty decision history must explain the opening, not invent a book.");
            Require((await runtime.Coordinator.LoadOriginBookReaderAsync(default, () => false))
                    is { OpeningNotStarted: false, Book: null },
                "A departed reader was mistaken for a verified empty story.");
            foreach (string locale in new[] { "de-DE", "en-US", "es-ES" })
            {
                var priorCulture = CultureInfo.CurrentUICulture;
                RetainedOriginBookPage emptyReader;
                try { CultureInfo.CurrentUICulture = new(locale); emptyReader = new(runtime.Coordinator); }
                finally { CultureInfo.CurrentUICulture = priorCulture; }
                var emptyRoot = new BuildPage(runtime.Coordinator);
                ((ScrollView)emptyRoot.Content!).ScrollToRequested += (_, _) => ((ScrollView)emptyRoot.Content!).SendScrollFinished();
                var emptyNavigation = new NavigationPage(emptyRoot);
                var emptyWindow = new Window(emptyNavigation);
                await emptyNavigation.PushAsync(emptyReader, false);
                using var emptyAlerts = new IssuedPageAlerts(emptyReader, emptyWindow);
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(emptyReader, "OnAppearing"));
                var labels = IssuedElements(emptyReader).OfType<Label>().Select(label => label.Text).ToArray();
                var copy = AndroidSurfaceStrings.Resolve(locale);
                Require(labels.Contains(copy["Origin.BookNotStarted"])
                    && labels.Contains(copy["Origin.OpeningSetupRequired"])
                    && labels.Contains(copy["Origin.BookNotStartedDetail"])
                    && !labels.Contains(copy["Origin.BookUnavailable"])
                    && !IssuedElements(emptyReader).Any(e => e is ProgressBar
                        || e.AutomationId is "origin-book-export" or "origin-book-export-epub"),
                    "A new story needs localized next steps, not a missing-account error, fake progress or empty export.");
                var backToRunner = IssuedElements(emptyReader).OfType<Button>()
                    .Single(button => button.AutomationId == "origin-book-return-to-runner");
                Require(backToRunner.Text == copy["Origin.BookReturnToRunner"], "The return action lost its localized label.");
                await ui.BeginAsyncVoid(() => ((IButtonController)backToRunner).SendClicked());
                Require(ReferenceEquals(emptyNavigation.Navigation.NavigationStack.Last(), emptyRoot),
                    "The reader's next-step action did not return to the runner.");
                IssuedPageLifecycle(emptyReader, "OnDisappearing");
                await ui.BeginAsyncVoid(() => ((IButtonController)backToRunner).SendClicked());
                Require(emptyNavigation.Navigation.NavigationStack.Count == 1 && emptyAlerts.Titles.Count == 0,
                    "A retired reading action navigated again or raised an alert.");
                IssuedPageLifecycle(emptyRoot, "OnDisappearing");
            }
            Require(authoringProbe.Reads == 0 && authoringProbe.Requests == 0 && authoringProbe.Acceptances == 0
                && bookOutput.Deliveries == 0
                && JsonSerializer.Serialize(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!)
                    == JsonSerializer.Serialize(beforeOpening),
                "The empty reader changed the runner, sent character facts, generated or exported a book.");
            Console.WriteLine("PASS empty Origin reader: verified opening state, DE/EN/ES next step, no fake progress, mutations or provider requests");
            var opening = await runtime.Coordinator.OpenSr5LifeModuleOriginAsync();
            var openingBrief = await runtime.Coordinator.LoadOpeningStoryDetailsAsync(opening.StoryCheckpoint!, () => true);
            var storyProfile = new OriginStoryProfile("other", "they/them", "epic", "Find a home", "Mara");
            Require(openingBrief is not null && (await runtime.Coordinator.SaveOpeningStoryDetailsAsync(
                openingBrief, storyProfile, () => true))?.StoryProfile == storyProfile,
                "The full book fixture could not save its optional opening brief.");
            await Task.Run(() => SeedNativeLifeStory(runtime, id));
            await runtime.Presenter.LoadAsync(id, default);
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var drafts = new LifeModuleCompletionDraftStore(runtime.StateDirectory);
            var root = new BuildPage(runtime.Coordinator);
            // Headless MAUI supplies scroll geometry only, never route or budget authority.
            ((ScrollView)root.Content!).ScrollToRequested += (_, _) => ((ScrollView)root.Content!).SendScrollFinished();
            var navigation = new NavigationPage(root);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(root, window);
            await alerts.PreflightAsync();
            await Appear();
            Require(!IssuedElements(root).Any(e => e.AutomationId == "creation-life-module-budget"),
                "The dashboard exposed module rule changes before the story was read.");
            await Click("creation-life-module-continue");
            Require(Current() is LifeModuleCompletionPage
                && IssuedElements(Current()).Any(e => e.AutomationId == "life-completion-story-first")
                && !IssuedElements(Current()).Any(e => e.AutomationId == "life-open-qualities"),
                "Unseen module prose did not hold back the completion mechanics.");
            await Back();
            // This allocation fixture starts after explicit reading. Keep the
            // original empty edition for the separate offline book tests below.
            var readingStore = new OriginBookReadingStore(runtime.StateDirectory);
            var unreadEdition = readingStore.Load(owners.Capture().Owner.Value, id.Value);
            var readBook = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            Require(readBook.Chapters.Count(readBook.IsSelectionFinish) == 1
                && !readBook.CanOpenAuthoring(readBook.Chapters.Single(readBook.IsSelectionFinish)),
                "Finishing module selection was treated as another paid narrative chapter.");
            foreach (var completedChapter in readBook.Chapters.Where(c => !readBook.IsOpeningSetup(c) && !readBook.IsSelectionFinish(c)).ToArray())
            {
                var completedProse = OriginBookProseDraft.Create(completedChapter, readBook.Locale,
                    Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.RequestId(readBook.AuthoringSource(completedChapter)),
                    new string('f', 64), "Synthetic completed chapter for the allocation-page fixture.");
                readBook = (await runtime.Coordinator.StageOriginBookProseDraftAsync(readBook, completedProse, () => true, default))!;
                readBook = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(readBook, completedProse, true, true, () => true, default))!;
            }
            Require(readBook.HasReadCurrentStory, "The allocation fixture did not acknowledge each completed chapter.");
            IssuedPageLifecycle(root, "OnDisappearing");
            await Appear();
            var displayedWizard = runtime.Coordinator.State.CreationWizard!;
            string originalWizard = JsonSerializer.Serialize(displayedWizard);
            var moduleBudget = runtime.Coordinator.State.CreationFoundation!.LifeModuleBudget;
            var technicalDetails = Element<VerticalStackLayout>("creation-wizard-details");
            var diagnosticContent = technicalDetails.Children.OfType<VerticalStackLayout>().Single();
            var diagnosticElements = IssuedElements(diagnosticContent).ToHashSet();
            Require(IssuedElements(root).Any(element => element.AutomationId == "creation-life-module-budget")
                && !diagnosticContent.IsVisible
                && !IssuedElements(root).OfType<Label>().Any(label => !diagnosticElements.Contains(label)
                    && label.Text?.Contains("creation-wizard-budget-authority-unavailable", StringComparison.Ordinal) == true),
                "The Life Modules landing page still shows unrelated Priority budget failures.");
            Require(Element<Label>("creation-life-module-budget-values").Text == CreationAllocationStrings.Format(
                    "LifeDashboard.Budget", "Confirmed modules and metatype: {0} / {1} Karma · remaining before final allocations: {2}",
                    moduleBudget.Used, moduleBudget.Total, moduleBudget.Remaining),
                "Life Modules landing budget differs from the current Core projection.");
            Require(Element<Label>("creation-life-module-scope").Text.Contains("Career", StringComparison.Ordinal)
                && JsonSerializer.Serialize(runtime.Coordinator.State.CreationWizard) == originalWizard,
                "The dashboard lost the final-review boundary or rewrote generic finalization authority.");
            var dashboardState = runtime.Coordinator.State;
            Require(runtime.Coordinator.IsLifeModuleDashboardCurrent(dashboardState)
                && !runtime.Coordinator.IsLifeModuleDashboardCurrent(dashboardState with { WorkspaceId = new CharacterWorkspaceId("another-runner") })
                && !runtime.Coordinator.IsLifeModuleDashboardCurrent(dashboardState with { CreationFoundation = dashboardState.CreationFoundation! with { SnapshotDigest = "sha256:" + new string('0', 64) } }),
                "A different workspace or replacement Foundation gained dashboard authority.");
            await Click("creation-life-module-continue");
            Require(Current() is LifeModuleCompletionPage, "The completed module selection did not open its own completion wizard.");
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
            var reviewed = Session().Preview!;
            string unchangedReview = JsonSerializer.Serialize(reviewed);
            string unchangedInputs = JsonSerializer.Serialize(Session().Input);
            int reviewedCalls = probe.PreviewCalls;
            var changes = reviewed.FinalizationPlan!.OrderedDeltas;
            var details = Element<VerticalStackLayout>("life-calculation-details");
            var detailToggle = Element<Button>("life-toggle-calculation-details");
            Require(!details.IsVisible && details.Children.Count == 0,
                "Technical details must start collapsed and unmaterialized.");
            foreach (var delta in changes)
            {
                Require(Element<Label>("life-change-" + delta.Order).Text == LifeModuleCompletionPage.ReviewChange(reviewed, delta),
                    "An ordered change was omitted or displayed from a different review.");
                var costs = IssuedElements(Current()).OfType<Label>().Where(label => label.AutomationId == "life-cost-" + delta.Order).ToArray();
                Require(delta.KarmaCost == 0 && delta.NuyenCost == 0 ? costs.Length == 0
                    : costs.Single().Text == CreationKarmaCopy.DeltaCost(delta.KarmaCost, delta.NuyenCost),
                    "A nonzero charge or credit was omitted or changed.");
            }
            var englishDelta = changes.Single(delta => delta.Kind == CharacterCreationFinalizationDeltaKinds.Skill && delta.AfterValue == "native");
            Require(LifeModuleCompletionPage.ReviewChange(reviewed, englishDelta).Contains("English", StringComparison.Ordinal)
                && !LifeModuleCompletionPage.ReviewChange(reviewed, englishDelta).Contains("life-module-skill:", StringComparison.Ordinal),
                "The reader still sees an internal skill identity instead of its exact reviewed name.");
            // Current Core deltas supply a reviewed TargetName. Clear that name
            // when testing the legacy identity-only fallback; keeping it would
            // correctly display the reviewed name without any catalog lookup.
            var unknownSkill = englishDelta with { TargetId = englishDelta.TargetId + ":different-identity", TargetName = null };
            Require(LifeModuleCompletionPage.ReviewChange(reviewed, unknownSkill).Contains(unknownSkill.TargetId, StringComparison.Ordinal),
                "A merely similar skill identity received another skill's display name.");
            var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (locale, native, detailsText) in new[]
                {
                    ("en", "Native language", "Show calculation details and sources"),
                    ("de", "Muttersprache", "Berechnungsdetails und Quellen anzeigen"),
                    ("es", "Lengua materna", "Mostrar cálculos y fuentes")
                })
                {
                    System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(locale);
                    Require(LifeModuleCompletionPage.ReviewChange(reviewed, englishDelta) == "English: " + native
                        && CreationAllocationStrings.Get("LifeCompletion.ShowDetails", "missing") == detailsText,
                        "Completion review localization fell back or changed the skill name: " + locale);
                }
            }
            finally { System.Globalization.CultureInfo.CurrentUICulture = originalCulture; }
            ((IButtonController)detailToggle).SendClicked();
            Require(details.IsVisible && details.Children.Count == changes.Count, "The complete calculation trace did not expand.");
            foreach (var delta in changes)
                Require(Element<Label>("life-calculation-" + delta.Order).Text == delta.Kind + " · " + delta.TargetId + ": "
                    + delta.BeforeValue + " → " + delta.AfterValue + "\n" + CreationKarmaCopy.DeltaCost(delta.KarmaCost, delta.NuyenCost)
                    + "\n" + string.Join(" · ", delta.SourceAnchorIds), "The original calculation/source trace was altered.");
            ((IButtonController)detailToggle).SendClicked();
            Require(!details.IsVisible && ReferenceEquals(Session().Preview, reviewed)
                && JsonSerializer.Serialize(reviewed) == unchangedReview && JsonSerializer.Serialize(Session().Input) == unchangedInputs
                && probe.PreviewCalls == reviewedCalls && probe.ConfirmCalls == 0
                && !Element<Button>("life-confirm-completion").IsEnabled,
                "Inspecting calculation details recomputed, modified, confirmed or acknowledged the runner.");
            Element<Switch>("life-completion-confirmed").IsToggled = true;
            var oldConfirm = Element<Button>("life-confirm-completion");
            var oldAcknowledgement = Element<Switch>("life-completion-confirmed");
            Require(oldConfirm.IsEnabled, "The exact acknowledged review should be confirmable before an edit.");
            Element<Entry>("life-starting-dice").Text = "5";
            Require(!oldConfirm.IsEnabled && !oldAcknowledgement.IsToggled && !oldAcknowledgement.IsEnabled,
                "Editing the dice leaves an enabled but inert confirmation and an acknowledgement of the retired review.");
            foreach (string changed in new[] { "", "not-a-roll", "6" })
            {
                Element<Entry>("life-starting-dice").Text = changed;
                oldAcknowledgement.IsToggled = true;
                Require(!oldConfirm.IsEnabled && !oldAcknowledgement.IsEnabled && !Session().CanConfirm,
                    "Invalid input or restoring the old total rearmed the retired review without a fresh preview.");
            }
            ((IButtonController)detailToggle).SendClicked();
            Require(!details.IsVisible, "Changed inputs reopened the retired calculation details.");
            ((IButtonController)oldConfirm).SendClicked();
            Require(probe!.ConfirmCalls == 0, "Changed visible dice accepted the previous final review.");
            Element<Entry>("life-starting-dice").Text = "6";
            await Click("life-review-completion");
            Require(!Element<Switch>("life-completion-confirmed").IsToggled
                && Element<Switch>("life-completion-confirmed").IsEnabled
                && !Element<Button>("life-confirm-completion").IsEnabled,
                "A fresh review reused the retired acknowledgement.");
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
                var pendingDetails = Element<VerticalStackLayout>("life-calculation-details");
                ((IButtonController)Element<Button>("life-toggle-calculation-details")).SendClicked();
                Require(!pendingDetails.IsVisible, "Pending confirmation accepted a details action on disabled content.");
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
            await Click("life-completion-open-career");
            Require(ReferenceEquals(Current(), root)
                && Element<Label>("phone-runner-sheet").Text == "CAREER RUNNER"
                && !IssuedElements(root).Any(e => e.AutomationId is "phone-runner-create" or "creation-life-module-continue" or "phone-runner-loading")
                && probe.ConfirmCalls == 1,
                "Warm Career navigation retained Creation, its loading state, or replayed finalization.");
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
            // New fixture phase: exercise an offline, not-yet-written book.
            readingStore.Save(readingStore.Load(unreadEdition.Owner, unreadEdition.Workspace), unreadEdition, () => true, default);
            authoringProbe.Status = AndroidAccountLinkStatus.Unlinked;
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
                long readerAppearance = IssuedPageField<long>(page, "_appearanceGeneration");
                await page.PollChapterOnceAsync(readerAppearance, default);
                Require(authoringProbe.Reads == 0 && authoringProbe.Requests == 0,
                    "A book without an existing job started private status reads or paid authoring.");
                var watchField = typeof(RetainedOriginBookPage).GetField("_watchPending",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                watchField.SetValue(page, true);
                await page.PollChapterOnceAsync(readerAppearance - 1, default);
                using (var retiredReaderCancellation = new CancellationTokenSource())
                {
                    retiredReaderCancellation.Cancel();
                    await page.PollChapterOnceAsync(readerAppearance, retiredReaderCancellation.Token);
                }
                Require(watchField.GetValue(page) is true && authoringProbe.Reads == 0,
                    "A retired or canceled observer stopped the current reader's watch.");
                watchField.SetValue(page, false);
                var unwrittenChapter = projected.Value.VisibleChapters[1];
                Require(!IssuedElements(page).OfType<Label>().Any(label => label.AutomationId?.StartsWith("origin-retained-chapter-", StringComparison.Ordinal) == true)
                    && Element<ProgressBar>($"origin-reader-progress-{unwrittenChapter.Sequence}").Progress == 0
                    && !string.IsNullOrWhiteSpace(Element<Label>($"origin-reader-eta-{unwrittenChapter.Sequence}").Text)
                    && !IssuedElements(page).Any(e => e.AutomationId == $"origin-reader-progress-{finishChapter.Sequence}")
                    && !IssuedElements(page).Any(e => e.AutomationId is "origin-book-export" or "origin-book-export-epub"),
                    "The reader showed a decision draft instead of waiting for the full chapter with progress/ETA.");
                Require(IssuedElements(page).Any(element => element.AutomationId == "origin-book-account"),
                    "The offline reader did not offer the account route.");
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
                    Require(!Element<VerticalStackLayout>("origin-reader-pinned-progress").IsVisible,
                        "The offline reader pinned a generation status/ETA without a linked writer.");
                    Require(!IssuedElements(Current()).Any(e => e.AutomationId is "origin-book-export" or "origin-book-export-epub")
                        && !IssuedElements(Current()).Any(e => e.AutomationId?.StartsWith("origin-author-chapter-", StringComparison.Ordinal) == true),
                        "An unlinked reader exported missing prose or exposed a generation action.");
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
                    Require(Current() is RetainedOriginBookPage
                        && !IssuedElements(Current()).Any(e => e.AutomationId is "origin-book-export" or "origin-book-export-epub"),
                        "Returning from account settings enabled an empty book export.");
                    await Back();
                }
                authoringProbe.Status = AndroidAccountLinkStatus.Linked;
                typeof(RetainedOriginBookPage).GetMethod("Refresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, null);
                int beforeRetiredAccountClick = navigation.Navigation.NavigationStack.Count;
                await ui.BeginAsyncVoid(() => ((IButtonController)retiredAccountButton!).SendClicked());
                Require(navigation.Navigation.NavigationStack.Count == beforeRetiredAccountClick && authoringProbe.LinkStarts == 0,
                    "A departed book action reopened account settings or began authentication.");
                authoringProbe.Status = AndroidAccountLinkStatus.Unlinked;
                Console.WriteLine("PASS unlinked book: DE/EN/ES account explanation, explicit navigation, offline export and no automatic sign-in/generation");
                Require(bookOutput.Deliveries == 0, "An unfinished book was exported as full prose.");
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
                Require(book!.OpeningSetupComplete && book.IsOpeningSetup(book.Chapters[0])
                    && runtime.Coordinator.PrepareOriginChapterSource(book, book.Chapters[0]) is null,
                    "The birth-background decision was offered as an independent generated chapter.");
                var chapter = book.Chapters[1];
                Require(book.Readings?.StoryProfile == storyProfile
                    && book.AuthoringSource(chapter).Facts.Any(f =>
                        f.DecisionId.StartsWith("player-story-brief-", StringComparison.Ordinal)
                        && f.Text.Contains("they/them", StringComparison.Ordinal)) == true,
                    "The retained book did not carry the optional brief into the approved chapter source.");
                Require(book.TryGetAuthoringPredecessor(chapter, out var openingPrevious) && openingPrevious is null,
                    "The first story chapter required a nonexistent pre-childhood story.");
                string canonicalText = book.ChapterText(chapter);
                var proposal = OriginBookProseDraft.Create(chapter, book.Locale,
                    Chummer.Run.Contracts.Community.OriginChapterSourceIdentity.RequestId(book.AuthoringSource(chapter)),
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
                Require(staleExportRejected && bookOutput.Deliveries == 0,
                    "An old reading edition remained exportable after a new draft was saved.");
                Require(await runtime.Coordinator.ReviewOriginBookProseDraftAsync(staged!, proposal, true, false, () => true, default) is null,
                    "A draft was selected without explicit reader confirmation.");
                // Read opens the entire returned chapter without a detour to a
                // draft-comparison screen; acknowledgement still needs a click.
                var review = new RetainedOriginBookPage(runtime.Coordinator);
                // The managed harness has no Android navigation handler to send
                // the preceding page's lifecycle event for a direct test push.
                IssuedPageLifecycle(page, "OnDisappearing");
                await navigation.PushAsync(review, false); await Appear();
                Require(Element<Label>($"origin-retained-chapter-{chapter.Sequence}").Text == proposal.Text
                    && !IssuedElements(review).Any(e => e.AutomationId is "origin-book-export" or "origin-book-export-epub")
                    && staged!.Pending(chapter) == proposal && !staged.HasReadCurrentStory,
                    "Read did not expose the exact full text, or silently acknowledged/exported it.");
                await Click($"origin-chapter-read-{chapter.Sequence}");
                Require(!IssuedElements(review).Any(e => e.AutomationId == $"origin-chapter-read-{chapter.Sequence}")
                    && Element<Button>("origin-book-export").IsEnabled,
                    "A completed reading retained its acceptance action or failed to enable full-prose export.");
                Require(!runtime.Coordinator.IsRetainedOriginBookCurrent(staged!),
                    "The pre-acceptance reading edition remained current after review.");
                var selected = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                Require(selected?.Pending(chapter) is null && selected!.ChapterText(chapter) == proposal.Text
                    && selected.ToHtml(AndroidSurfaceStrings.Resolve("en")).Contains("&lt;script&gt;not executable&lt;/script&gt;", StringComparison.Ordinal),
                    "Selected prose failed to reopen/export safely.");
                Require(await runtime.Coordinator.ExportRetainedOriginBookAsync(selected!, AndroidSurfaceStrings.Resolve("en"), () => true, default)
                    && bookOutput.Deliveries == 1, "The complete context-bound HTML export was not delivered.");
                Require(bookOutput.Html.Contains("<meta name=\"author\" content=\"chummer.run\">", StringComparison.Ordinal)
                    && bookOutput.Html.Contains("<h1>" + System.Net.WebUtility.HtmlEncode(projected.Value!.CurrentTurn.RunnerDisplayName) + "</h1>", StringComparison.Ordinal)
                    && !bookOutput.Html.Contains(System.Net.WebUtility.HtmlEncode(finishText), StringComparison.Ordinal),
                    "Full-prose export lost author/title or inserted a decision summary for an unwritten chapter.");
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
                // Preserve the legacy recovery-page safety tests explicitly.
                // Production reading starts automatically; no writing button
                // is present there (covered by the automatic reader test).
                authoringProbe.Status = AndroidAccountLinkStatus.Linked;
                var legacyBook = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
                IssuedPageLifecycle(Current(), "OnDisappearing");
                await navigation.PushAsync(new OriginBookAuthoringPage(runtime.Coordinator, legacyBook, chapter), false);
                await Appear();
                Require(Current() is OriginBookAuthoringPage && authoringProbe.Requests == 0 && authoringProbe.Reads == 1
                    && authoringProbe.Acceptances == 0
                    && !Element<Button>("origin-authoring-request").IsEnabled,
                    "Opening authoring must read existing status without generation, acknowledgement or consent.");
                var authoringBody = (VerticalStackLayout)((ScrollView)Current().Content!).Content;
                Require(authoringBody.Children[1] is Border { Content: VerticalStackLayout { AutomationId: "origin-authoring-status" } },
                    "Progress and ETA were buried beneath the chapter facts.");
                var authoringPage = (OriginBookAuthoringPage)Current();
                long authoringAppearance = IssuedPageField<long>(authoringPage, "_appearanceGeneration");
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == 1 && authoringProbe.Requests == 0,
                    "A confirmed absent job started automatic observation or generation.");
                await Click("origin-authoring-refresh");
                Require(authoringProbe.Reads == 2 && authoringProbe.Requests == 0, "Read-only status created a job.");
                Element<Switch>("origin-authoring-consent").IsToggled = true;
                await Click("origin-authoring-request");
                Require(authoringProbe.Requests == 1 && authoringProbe.Reads == 3,
                    "Consented chapter request did not read before creating.");
                Require(Element<ProgressBar>("origin-authoring-progress").Progress == 1d / 3d
                    && Element<Label>("origin-authoring-eta").Text == AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name)["Origin.AuthoringEtaUnknown"],
                    "A queued request needs confirmed-stage progress and an honest unknown ETA.");
                var waitingConsent = Element<Switch>("origin-authoring-consent");
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == 4 && authoringProbe.Requests == 1 && authoringProbe.Acceptances == 0
                    && ReferenceEquals(waitingConsent, Element<Switch>("origin-authoring-consent")),
                    "Unchanged automatic status rebuilt controls, generated prose or accepted a draft.");
                using (var canceledPoll = new CancellationTokenSource())
                {
                    canceledPoll.Cancel();
                    await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, canceledPoll.Token);
                }
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance - 1, default);
                Require(authoringProbe.Reads == 4, "Canceled or retired appearance polled private chapter status.");
                authoringProbe.TransientReadFailure = true;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(Element<ProgressBar>("origin-authoring-progress").Progress == 1d / 3d,
                    "A transient status read erased the last confirmed authoring stage.");
                authoringProbe.TransientReadFailure = false;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == 6 && authoringProbe.Requests == 1 && authoringProbe.Acceptances == 0,
                    "A classified transient read did not recover the same job without another generation or acceptance.");
                authoringProbe.TransientReadFailure = true;
                for (int failedRead = 0; failedRead < 3; failedRead++)
                    await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                authoringProbe.TransientReadFailure = false;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == 9 && authoringProbe.Requests == 1 && authoringProbe.Acceptances == 0,
                    "The consecutive transient-read limit failed to stop automatic observation.");
                Require(IssuedElements(Current()).OfType<Label>().Any(label => label.Text ==
                    AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name)["Origin.AuthoringStatusPaused"]),
                    "Exhausted status observation silently left the reader waiting.");
                await Click("origin-authoring-refresh");
                int beforeBudgetPause = authoringProbe.Reads;
                await authoringPage.PausePendingStatusObservationAsync(authoringAppearance, default);
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == beforeBudgetPause
                    && Element<ProgressBar>("origin-authoring-progress").Progress == 1d / 3d
                    && IssuedElements(Current()).OfType<Label>().Any(label => label.Text ==
                        AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name)["Origin.AuthoringStatusPaused"]),
                    "Elapsed observation budget did not preserve progress, explain the pause and stop reads.");
                await Click("origin-authoring-refresh");
                int beforeUnclassifiedFailure = authoringProbe.Reads;
                authoringProbe.FailRead = true;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                authoringProbe.FailRead = false;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == beforeUnclassifiedFailure + 1 && authoringProbe.Requests == 1,
                    "An automatic status failure retried without an explicit refresh.");
                await Click("origin-authoring-refresh");
                authoringProbe.Dispatched = true;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(Element<ProgressBar>("origin-authoring-progress").Progress == 2d / 3d
                    && IssuedElements(Current()).OfType<Label>().Any(label =>
                        label.AutomationId == "origin-authoring-outcome-unconfirmed" && !string.IsNullOrWhiteSpace(label.Text))
                    && authoringProbe.Requests == 1 && authoringProbe.Acceptances == 0,
                    "Dispatch without a confirmed result must visibly explain uncertainty, not imply an active writer.");
                // A stopped provider and a running provider share this remote
                // state. A failed observation must not erase that distinction.
                authoringProbe.TransientReadFailure = true;
                for (int failedRead = 0; failedRead < 3; failedRead++)
                    await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                int pausedUnconfirmedReads = authoringProbe.Reads;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == pausedUnconfirmedReads
                    && authoringProbe.Requests == 1 && authoringProbe.Acceptances == 0
                    && Element<ProgressBar>("origin-authoring-progress").Progress == 2d / 3d
                    && IssuedElements(Current()).Any(element => element.AutomationId == "origin-authoring-outcome-unconfirmed")
                    && IssuedElements(Current()).OfType<Label>().Any(label => label.Text ==
                        AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name)["Origin.AuthoringStatusPaused"]),
                    "Paused status reads hid the unconfirmed outcome, restarted writing or accepted prose.");
                authoringProbe.TransientReadFailure = false;
                // Leave and reopen while the remote outcome is uncertain. The
                // first read restores progress without a button press or replay.
                long departedAuthoringAppearance = authoringAppearance;
                IssuedPageLifecycle(authoringPage, "OnDisappearing");
                await Appear();
                authoringAppearance = IssuedPageField<long>(authoringPage, "_appearanceGeneration");
                Require(authoringProbe.Reads == pausedUnconfirmedReads + 1
                    && authoringProbe.Requests == 1 && authoringProbe.Acceptances == 0
                    && Element<ProgressBar>("origin-authoring-progress").Progress == 2d / 3d
                    && !Element<Switch>("origin-authoring-consent").IsToggled
                    && IssuedElements(Current()).Any(e => e.AutomationId == "origin-authoring-eta"),
                    "Reopening lost existing progress, granted consent or repeated paid work.");
                await authoringPage.PollPendingChapterOnceAsync(departedAuthoringAppearance, default);
                Require(authoringProbe.Reads == pausedUnconfirmedReads + 1,
                    "A retired appearance continued observing after reopen.");
                authoringProbe.Ready = true;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(Element<ProgressBar>("origin-authoring-progress").Progress == 1d
                    && !IssuedElements(Current()).Any(element => element.AutomationId is "origin-authoring-eta"
                        or "origin-authoring-outcome-unconfirmed"),
                    "A ready draft retained a fabricated time estimate or incorrect generation progress.");
                var authored = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                Require(authored?.Pending(chapter)?.Text.StartsWith("Synthetic transport", StringComparison.Ordinal) == true
                    && authored.ChapterText(chapter) == proposal.Text && authoringProbe.Requests == 1 && authoringProbe.Acceptances == 0,
                    "Readback auto-adopted prose or submitted another generation.");
                int terminalReads = authoringProbe.Reads;
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == terminalReads, "A terminal draft kept polling the provider job.");
                authoringProbe.TransientReadFailure = true;
                await Click("origin-authoring-refresh");
                Require(Element<ProgressBar>("origin-authoring-progress").Progress == 1d
                    && IssuedElements(Current()).OfType<Label>().Any(label => label.Text ==
                        AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name)["Origin.AuthoringStatusInterrupted"])
                    && authoringProbe.Reads == terminalReads + 1 && authoringProbe.Requests == 1
                    && authoringProbe.Acceptances == 0,
                    "A manual transport interruption erased the confirmed draft or claimed an account/generation failure.");
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == terminalReads + 1,
                    "A failed manual read of a terminal job silently started automatic polling.");
                Element<Switch>("origin-authoring-consent").IsToggled = true;
                await Click("origin-authoring-request");
                Require(Element<ProgressBar>("origin-authoring-progress").Progress == 1d
                    && IssuedElements(Current()).OfType<Label>().Any(label => label.Text ==
                        AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name)["Origin.AuthoringStatusInterrupted"])
                    && authoringProbe.Reads == terminalReads + 2 && authoringProbe.Requests == 1
                    && authoringProbe.Acceptances == 0,
                    "Request/resume misclassified its failed preflight read, erased the draft or created another job.");
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == terminalReads + 2,
                    "Request/resume started automatic work after an unconfirmed preflight read.");
                authoringProbe.TransientReadFailure = false;
                await Click("origin-authoring-refresh");
                terminalReads = authoringProbe.Reads;
                await Click("origin-authoring-review");
                await authoringPage.PollPendingChapterOnceAsync(authoringAppearance, default);
                Require(authoringProbe.Reads == terminalReads, "A departed authoring page polled private chapter status.");
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
                var imageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var imageRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                authoringProbe.SceneBytes = LifeSceneInputProbe.Png.ToArray();
                authoringProbe.BeforeAutomaticSceneReturn = async () => { imageStarted.SetResult(); await imageRelease.Task; };
                await Back();
                Require(IssuedElements(Current()).OfType<Label>().Any(label => label.Text == "Synthetic transport chapter for explicit review."),
                    "The accepted transport draft did not return to the reader.");
                Console.WriteLine("PASS actual MAUI chapter consent, read-before-create, explicit adoption, offline acceptance recovery and book return");
                await imageStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(!IssuedElements(Current()).Any(e => e.AutomationId == $"origin-scene-chapter-{chapter.Sequence}")
                    && Element<Button>("origin-book-export-epub").IsEnabled && authoringProbe.SceneDecisions == 0
                    && authoringProbe.SceneRequests == 0, "Automatic artwork still required an image picker or human image decision.");
                await Click("origin-book-export-epub");
                Require(bookOutput.Epub.Length > 0, "Pending artwork blocked full-book export.");
                // The image can finish while Android's Save As picker is open.
                // Export the exact edition chosen at the click; optional artwork
                // must not silently cancel an otherwise valid document save.
                int beforeIllustratedSave = bookOutput.EpubDeliveries;
                RetainedOriginBook VisibleEdition() => (RetainedOriginBook)typeof(RetainedOriginBookPage)
                    .GetField("_book", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(Current())!;
                bookOutput.BeforeReadAsync = async () =>
                {
                    imageRelease.TrySetResult();
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (VisibleEdition().Scene(chapter) is null
                        && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
                    Require(VisibleEdition().Scene(chapter) is not null,
                        "The illustration did not finish during the simulated Save As picker.");
                };
                try { await Click("origin-book-export-epub"); }
                finally { bookOutput.BeforeReadAsync = null; imageRelease.TrySetResult(); }
                Require(bookOutput.EpubDeliveries == beforeIllustratedSave + 1,
                    "Finishing an automatic illustration silently canceled the in-progress EPUB save.");
                using (var snapshot = new System.IO.Compression.ZipArchive(new MemoryStream(bookOutput.Epub), System.IO.Compression.ZipArchiveMode.Read))
                    Require(!snapshot.Entries.Any(entry => entry.FullName.StartsWith("EPUB/images/", StringComparison.Ordinal)),
                        "A pending export changed its selected bytes after the Save As picker opened.");
                var imageWait = System.Diagnostics.Stopwatch.StartNew();
                while (!IssuedElements(Current()).Any(e => e.AutomationId == $"origin-book-scene-{chapter.Sequence}")
                    && imageWait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
                Require(Element<Image>($"origin-book-scene-{chapter.Sequence}").Source is StreamImageSource
                    && authoringProbe.AutomaticSceneRequests == 1 && authoringProbe.SceneDecisions == 0,
                    "Automatically retained image did not appear with the full chapter.");
                await Click("origin-book-export-epub");
                using (var epubWithAutoArt = new System.IO.Compression.ZipArchive(new MemoryStream(bookOutput.Epub), System.IO.Compression.ZipArchiveMode.Read))
                {
                    using var picture = epubWithAutoArt.GetEntry("EPUB/images/scene-1.png")!.Open();
                    using var captured = new MemoryStream(); picture.CopyTo(captured);
                    Require(captured.ToArray().SequenceEqual(LifeSceneInputProbe.Png), "Automatic image was absent from the downloadable EPUB.");
                }
                IssuedPageLifecycle(Current(), "OnDisappearing");
                await Appear();
                Require(Element<Image>($"origin-book-scene-{chapter.Sequence}").Source is StreamImageSource
                    && authoringProbe.AutomaticSceneRequests == 1
                    && new OriginBookReadingStore(runtime.StateDirectory).Load(owner.Owner.Value, id.Value).IllustrationPolicy
                        == OriginBookReadingState.AutomaticIllustrations,
                    "Reader reopen lost book consent or replayed the retained illustration.");
                Console.WriteLine("PASS automatic private illustration without picker/review, responsive full-text export, exact image EPUB and cold reopen without replay");

                // Preserve coverage of historical manual admissions and local
                // artwork without re-exposing their picker in the new reader.
                IssuedPageLifecycle(Current(), "OnDisappearing");
                var legacyReadingStore = new OriginBookReadingStore(runtime.StateDirectory);
                var modernReading = legacyReadingStore.Load(owner.Owner.Value, id.Value);
                legacyReadingStore.Save(modernReading, modernReading with { IllustrationPolicy = null }, () => true, default);
                var legacySceneStore = new OriginBookSceneStore(runtime.StateDirectory);
                var modernScenes = legacySceneStore.Load(owner.Owner.Value, id.Value);
                legacySceneStore.Save(modernScenes, new(modernScenes.Owner, modernScenes.Workspace, []), () => true, default);
                authoringProbe.ResetSyntheticScenes();
                await Appear();
                await OpenLegacyScene();
                Require(!Element<Switch>("origin-scene-consent").IsToggled && authoringProbe.SceneReads == 0
                    && authoringProbe.SceneRequests == 0, "Opening illustrations granted consent or contacted a provider.");
                Element<Editor>("origin-scene-description").Text = "Synthetic chapter scene";
                Element<Editor>("origin-scene-excerpt").Text = "Synthetic transport chapter";
                var departedConsent = Element<Switch>("origin-scene-consent");
                departedConsent.IsToggled = true;
                var departedRequest = Element<Button>("origin-scene-request");
                var returningScenePage = Current();
                IssuedPageLifecycle(returningScenePage, "OnDisappearing");
                Require(!IssuedElements(returningScenePage).OfType<Editor>().Any(),
                    "A hidden illustration page retained visible private inputs.");
                await Appear();
                Require(Element<Editor>("origin-scene-excerpt").Text == "Synthetic transport chapter"
                    && Element<Editor>("origin-scene-description").Text == "Synthetic chapter scene"
                    && !Element<Switch>("origin-scene-consent").IsToggled
                    && !Element<Button>("origin-scene-request").IsEnabled
                    && authoringProbe.SceneReads == 0 && authoringProbe.SceneRequests == 0,
                    "Returning to illustrations lost the reviewed excerpt, retained consent or contacted a provider.");
                departedConsent.IsToggled = false;
                departedConsent.IsToggled = true;
                // Model an already queued tap even though the retired toggle
                // has correctly disabled the old control in the meantime.
                departedRequest.IsEnabled = true;
                await ui.BeginAsyncVoid(() => ((IButtonController)departedRequest).SendClicked());
                Require(!Element<Switch>("origin-scene-consent").IsToggled
                    && !Element<Button>("origin-scene-request").IsEnabled
                    && authoringProbe.SceneReads == 0 && authoringProbe.SceneRequests == 0,
                    "Departed illustration controls granted consent or dispatched a request on return.");
                await Click("origin-scene-read");
                Require(authoringProbe.SceneReads == 1 && authoringProbe.SceneRequests == 0,
                    "Read-only absence generated an image.");
                Element<Editor>("origin-scene-description").Text = "Synthetic chapter scene";
                Element<Editor>("origin-scene-excerpt").Text = "unchosen future";
                Element<Switch>("origin-scene-consent").IsToggled = true;
                Require(!Element<Button>("origin-scene-request").IsEnabled, "An invented scene could be submitted.");
                Element<Editor>("origin-scene-excerpt").Text = "Synthetic transport chapter";
                Require(Element<Button>("origin-scene-request").IsEnabled, "Exact excerpt consent did not enable generation.");
                authoringProbe.FailSceneRead = true;
                await Click("origin-scene-request");
                Require(authoringProbe.SceneRequests == 0, "Transport failure was treated as confirmed absence.");
                authoringProbe.FailSceneRead = false;
                Element<Switch>("origin-scene-consent").IsToggled = true;
                await Click("origin-scene-request");
                Require(authoringProbe.SceneRequests == 1 && authoringProbe.SceneDecisions == 0
                    && !Element<Switch>("origin-scene-consent").IsToggled
                    && Element<Image>("origin-scene-preview").Source is StreamImageSource
                    && new OriginBookSceneStore(runtime.StateDirectory).Load(owner.Owner.Value, id.Value).Scenes.Count == 0,
                    "Remote preview was auto-adopted, regenerated or retained blanket consent.");
                int sceneReads = authoringProbe.SceneReads;
                var copy = AndroidSurfaceStrings.Resolve(System.Globalization.CultureInfo.CurrentUICulture);
                authoringProbe.SceneReadOverride = new(AndroidOriginSceneOutcome.Unavailable, RetryableReadFailure: true);
                await Click("origin-scene-read");
                Require(Element<Image>("origin-scene-preview").Source is StreamImageSource
                    && Element<Button>("origin-scene-save").IsEnabled
                    && Element<Label>("origin-scene-status").Text == copy["Origin.ScenePreviewRetained"]
                    && authoringProbe.SceneReads == sceneReads + 1 && authoringProbe.SceneRequests == 1
                    && authoringProbe.SceneDecisions == 0
                    && new OriginBookSceneStore(runtime.StateDirectory).Load(owner.Owner.Value, id.Value).Scenes.Count == 0,
                    "An interrupted scene read discarded, adopted or regenerated the retained preview.");
                foreach (var unavailable in new[] { new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Unavailable),
                    new(AndroidOriginSceneOutcome.Unauthorized), new(AndroidOriginSceneOutcome.Conflict),
                    new(AndroidOriginSceneOutcome.Available, "dispatching"), new(AndroidOriginSceneOutcome.Available, "uncertain") })
                {
                    authoringProbe.SceneReadOverride = unavailable;
                    await Click("origin-scene-read");
                    Require(!IssuedElements(Current()).OfType<Image>().Any(image => image.AutomationId == "origin-scene-preview")
                        && !Element<Button>("origin-scene-save").IsEnabled && authoringProbe.SceneRequests == 1,
                        "An invalidated/pending scene retained its remote preview or generated another image.");
                    if (unavailable.State is "dispatching" or "uncertain")
                        Require(Element<Label>("origin-scene-status").Text == copy[unavailable.State == "dispatching"
                            ? "Origin.SceneDispatching" : "Origin.SceneUncertain"], "Scene progress invented a completed render.");
                    authoringProbe.SceneReadOverride = null;
                    await Click("origin-scene-read");
                    Require(Element<Image>("origin-scene-preview").Source is StreamImageSource && authoringProbe.SceneRequests == 1,
                        "Re-reading an existing scene did not restore the preview without regeneration.");
                }
                authoringProbe.FailSceneDecision = true;
                await Click("origin-scene-save");
                Require(Current() is OriginBookScenePage && authoringProbe.SceneDecisions == 1
                    && new OriginBookSceneStore(runtime.StateDirectory).Load(owner.Owner.Value, id.Value).Scenes.Count == 0,
                    "Unknown server approval silently committed the image.");
                authoringProbe.FailSceneDecision = false;
                await Click("origin-scene-read");
                Require(authoringProbe.SceneRequests == 1, "Recovering the existing image generated a duplicate.");
                var remoteScenePage = Current();
                await Click("origin-scene-save");
                IssuedPageLifecycle(remoteScenePage, "OnDisappearing");
                Require(Current() is RetainedOriginBookPage && authoringProbe.SceneDecisions == 2
                    && new OriginBookSceneStore(runtime.StateDirectory).Load(owner.Owner.Value, id.Value).Scenes.Single().Identity.ImageDigest
                        == OriginBookScene.Hash(LifeSceneInputProbe.Png), "Explicit remote adoption failed cold image readback.");
                await Click("origin-book-export-epub");
                using (var generatedEpub = new System.IO.Compression.ZipArchive(new MemoryStream(bookOutput.Epub), System.IO.Compression.ZipArchiveMode.Read))
                {
                    using var picture = generatedEpub.GetEntry("EPUB/images/scene-1.png")!.Open();
                    using var captured = new MemoryStream(); picture.CopyTo(captured);
                    Require(captured.ToArray().SequenceEqual(LifeSceneInputProbe.Png), "The generated scene did not reach the offline EPUB.");
                }
                await OpenLegacyScene();
                var removeScenePage = Current();
                await Click("origin-scene-remove");
                IssuedPageLifecycle(removeScenePage, "OnDisappearing");
                Require(authoringProbe.SceneDecisions == 2 && authoringProbe.SceneRequests == 1,
                    "Removing the local copy changed private remote approval or started another render.");
                Console.WriteLine("PASS actual MAUI exact scene consent, read-before-create, unknown approval recovery, explicit adoption, cold storage and offline EPUB");
                await OpenLegacyScene();
                Require(Current() is OriginBookScenePage && !Element<Button>("origin-scene-save").IsEnabled,
                    "Scene selection did not require a reviewed image.");
                Require(Element<Button>("origin-scene-choose").IsEnabled
                    && Element<Label>("origin-scene-description-required").IsVisible,
                    "Choosing an image was silently blocked until a description was entered.");
                sceneInput.Canceled = true;
                await Click("origin-scene-choose");
                Require(!IssuedElements(Current()).Any(e => e.AutomationId == "origin-scene-preview")
                    && !Element<Button>("origin-scene-save").IsEnabled,
                    "Canceling the picker fabricated an image or admitted a save.");
                sceneInput.Canceled = false;
                sceneInput.BeforeReturnAsync = async () =>
                {
                    var departed = Current();
                    IssuedPageLifecycle(departed, "OnDisappearing");
                    var destination = new ContentPage();
                    await navigation.PushAsync(destination, false);
                    // As with appearance, headless navigation needs the
                    // Android handler's real navigation lifecycle callback.
                    typeof(OriginBookScenePage).GetMethod("OnNavigatingFrom",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(departed, [Activator.CreateInstance(typeof(NavigatingFromEventArgs),
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                            binder: null, args: [destination, NavigationType.Push], culture: null)]);
                    await navigation.PopAsync(false);
                    // The outer Click tracks all nested async-void lifecycle
                    // callbacks; BeginAsyncVoid must not be reentered.
                    IssuedPageLifecycle(Current(), "OnAppearing");
                    await Task.Yield();
                };
                await Click("origin-scene-choose");
                Require(!IssuedElements(Current()).Any(e => e.AutomationId == "origin-scene-preview"),
                    "Leaving and reentering the page admitted a retired picker result.");
                sceneInput.BeforeReturnAsync = async () =>
                {
                    IssuedPageLifecycle(Current(), "OnDisappearing");
                    IssuedPageLifecycle(Current(), "OnAppearing");
                    await Task.Yield();
                };
                await Click("origin-scene-choose");
                sceneInput.BeforeReturnAsync = null;
                Require(sceneInput.Reads == 3 && Element<Image>("origin-scene-preview").Source is StreamImageSource
                    && !Element<Button>("origin-scene-save").IsEnabled
                    && new OriginBookSceneStore(runtime.StateDirectory).Load(owner.Owner.Value, id.Value).Scenes.Count == 0,
                    "Selecting a scene fetched a URL or committed it before confirmation.");
                Element<Editor>("origin-scene-description").Text = "Forest clearing and a spiral of stones";
                Require(Element<Button>("origin-scene-save").IsEnabled
                    && !Element<Label>("origin-scene-description-required").IsVisible,
                    "Describing the reviewed image did not enable explicit saving.");
                var scenePage = Current();
                await Click("origin-scene-save");
                // Headless navigation has no Android handler to send disappearance on PopAsync.
                IssuedPageLifecycle(scenePage, "OnDisappearing");
                Require(Current() is RetainedOriginBookPage
                    && Element<Image>($"origin-book-scene-{chapter.Sequence}").Source is StreamImageSource,
                    "Confirmed scene did not return to the readable book.");
                int beforeSceneExport = bookOutput.EpubDeliveries;
                await Click("origin-book-export-epub");
                Require(bookOutput.EpubDeliveries == beforeSceneExport + 1, "The scene reader did not deliver an EPUB through Save As.");
                using (var epub = new System.IO.Compression.ZipArchive(new MemoryStream(bookOutput.Epub), System.IO.Compression.ZipArchiveMode.Read))
                {
                    using var picture = epub.GetEntry("EPUB/images/scene-1.png")!.Open();
                    using var captured = new MemoryStream(); picture.CopyTo(captured);
                    Require(captured.ToArray().SequenceEqual(LifeSceneInputProbe.Png), "The actual Save As EPUB lost the chosen scene bytes.");
                }
                var retainedScene = new OriginBookSceneStore(runtime.StateDirectory).Load(owner.Owner.Value, id.Value);
                Require(retainedScene.Scenes.Count == 1 && new OriginBookSceneStore(runtime.StateDirectory)
                    .Load(ContactsOwnerB.Value, id.Value).Scenes.Count == 0, "Scene restart crossed owner storage boundaries.");
                string scenePath = Directory.EnumerateFiles(Path.Combine(runtime.StateDirectory, "origin-book-scenes"), "*.zip").Single();
                byte[] sceneArchive = File.ReadAllBytes(scenePath);
                File.WriteAllBytes(scenePath, [1, 2, 3]);
                var damagedArtBook = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                Require(damagedArtBook is { ScenesUnavailable: true } && damagedArtBook.ChapterText(chapter) ==
                    "Synthetic transport chapter for explicit review." && !runtime.Coordinator.CanSelectOriginBookScene(damagedArtBook),
                    "Corrupt optional art made the story unreadable or allowed overwriting the damaged archive.");
                File.WriteAllBytes(scenePath, sceneArchive);
                Console.WriteLine("PASS actual MAUI scene preview, explicit adoption, reader return, offline EPUB Save As and cold image read");
                book = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                bookOutput.BeforeRead = () => { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); };
                bool canceled = false;
                try { await runtime.Coordinator.ExportRetainedOriginBookAsync(book!, AndroidSurfaceStrings.Resolve("en"), () => true, default); }
                catch (OperationCanceledException) { canceled = true; }
                Require(canceled && bookOutput.Deliveries == 1 && !runtime.Coordinator.IsRetainedOriginBookCurrent(book!),
                    "Owner A→B→A during the document picker exported the old book.");
                int sceneReadsBeforeRetiredPicker = sceneInput.Reads;
                Require(await runtime.Coordinator.PickOriginBookSceneAsync(book!, chapter,
                    "Retired picker", () => true, default) is null
                    && sceneInput.Reads == sceneReadsBeforeRetiredPicker
                    && File.ReadAllBytes(scenePath).SequenceEqual(sceneArchive),
                    "Owner A→B→A admitted a retired scene picker or changed the stored image.");
                typeof(OriginBookScenePage).GetMethod("Refresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(returningScenePage, null);
                Require(!IssuedElements(returningScenePage).Any(element => element is Microsoft.Maui.Controls.Editor
                    or Microsoft.Maui.Controls.Image or Microsoft.Maui.Controls.Switch or Microsoft.Maui.Controls.Button),
                    "A retired illustration page exposed retained inputs after an owner transition.");
                Require(await runtime.Coordinator.SaveOriginBookSceneAsync(book!, chapter, retainedScene.Scenes.Single(),
                    true, () => true, default) is null, "A retired owner stamp saved a scene.");
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
                if (page is BuildPage && runtime.Coordinator.State.Profile?.Created == true)
                {
                    Require(Element<ActivityIndicator>("phone-runner-loading").IsRunning
                        && !string.IsNullOrWhiteSpace(Element<Label>("phone-runner-loading-message").Text)
                        && !page.ToolbarItems.Any(item => item.IsEnabled)
                        && !IssuedElements(page).Any(e => e is Button || e.AutomationId is "phone-runner-create" or "phone-runner-sheet"),
                        "Career reappearance exposes the old Creation surface while its current receipt is loading.");
                }
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
                using (var actionAlerts = new IssuedPageAlerts(previous, window))
                {
                    await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked())
                        .WaitAsync(TimeSpan.FromSeconds(45));
                    Require(actionAlerts.Titles.Count == 0, "Unexpected Life Modules action alert: " + key
                        + ": " + string.Join("; ", actionAlerts.Messages));
                }
                if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0) IssuedPageLifecycle(previous, "OnDisappearing");
                await Appear();
            }
            async Task Back()
            { IssuedPageLifecycle(Current(), "OnDisappearing"); await navigation.PopAsync(false); await Appear(); }
            async Task OpenLegacyScene()
            {
                var retained = await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
                Require(retained is not null && retained.Readings?.IllustrationPolicy is null,
                    "Legacy artwork test unexpectedly gained automatic book consent.");
                var legacyChapter = retained!.Chapters.Single(c => retained.Reading(c)?.Selected is not null);
                IssuedPageLifecycle(Current(), "OnDisappearing");
                await navigation.PushAsync(new OriginBookScenePage(runtime.Coordinator, retained, legacyChapter,
                    AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name)), false);
                await Appear();
            }
        });
        await ui.RunAsync(async () =>
        {
            await RunLifeModuleToolbarSaveBoundariesAsync(contentRoot);
            ui.AssertHealthy();
        });
    }

    internal static async Task RunLifeModuleToolbarSaveBoundariesAsync(string contentRoot)
    {
        foreach (string scenario in new[] { "post-cancel", "post-b", "post-aba" })
        {
            var owners = new ControlledLinkedOwner();
            var roaming = new PersistenceRoamingProbe(owners);
            var account = System.Reflection.DispatchProxy.Create<IAndroidAccountLinkService, OriginAuthoringPageAccount>();
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, persistenceRoaming: roaming,
                accountService: account, lifeCompletionDecorator: actual => actual);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var original = runtime.Coordinator.State;
            var id = original.WorkspaceId!.Value;
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var before = store.Get(id).Value!;
            int saves = roaming.BoundCalls;
            using var cancellation = new CancellationTokenSource();
            roaming.ExpectedOwner = owners.Capture();
            roaming.AfterCommit = () =>
            {
                if (scenario == "post-cancel") cancellation.Cancel();
                else
                {
                    owners.Set(ContactsOwnerB);
                    if (scenario == "post-aba") owners.Set(OwnerScope.LocalSingleUser);
                }
            };
            await runtime.Coordinator.SaveAsync(cancellation.Token);
            var saved = store.Get(id).Value!;
            Require(saved.ContentRevision == before.ContentRevision && saved.SavedRevision == saved.ContentRevision
                && saved.Document.Content == before.Document.Content
                && store.Get(ContactsOwnerB, id).Value is null
                && roaming.BoundCalls == saves + 1 && roaming.UnboundCalls == 0 && owners.ActiveLeases == 0,
                "Post-save refresh changed the runner, replayed its save or retargeted another owner: " + scenario);
            Require(runtime.Coordinator.HasDurableSaveNotice == (scenario == "post-cancel")
                // Account transition may clear the display entirely. It must
                // never replace the old snapshot with a newly admitted owner.
                && (runtime.Coordinator.State.CreationFoundation is null
                    || ReferenceEquals(original.CreationFoundation, runtime.Coordinator.State.CreationFoundation))
                && !runtime.Coordinator.IsLifeModuleDashboardCurrent(runtime.Coordinator.State),
                "Canceled/stale-owner refresh fabricated a current Life Modules projection: " + scenario);
            Console.WriteLine("PASS Life Modules toolbar Save boundary: " + scenario);
        }
        foreach (string scenario in new[] { "same", "post-b", "post-aba", "post-cancel" })
            await RunNativeSaveCommitAsync(contentRoot, scenario);
    }

    internal static async Task RunLifeModuleCompletionAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in new[] { "save-career-reopen", "owner-aba-during-open", "cancel-after-commit",
                         "shell-sync-failure", "owner-aba-during-shell-sync", "cancel-during-shell-sync" })
            {
                var owners = new ControlledLinkedOwner();
                using var cancel = new CancellationTokenSource();
                int shellLists = 0;
                bool observingCommit = false;
                LifeCompletionNativeProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners,
                    lifeCompletionDecorator: actual => probe = new(actual),
                    beforeShellWorkspaceList: () =>
                    {
                        shellLists++;
                        if (!observingCommit) return;
                        if (scenario == "shell-sync-failure") throw new IOException("Synthetic shell read failure.");
                        if (scenario == "owner-aba-during-shell-sync")
                        { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); }
                        if (scenario == "cancel-during-shell-sync") cancel.Cancel();
                    });
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
                int beforeNormalLoad = shellLists;
                await runtime.Presenter.LoadAsync(id, default);
                Require(shellLists == beforeNormalLoad + 1, "Normal presenter reload must retain its shell synchronization.");
                var refresh = (IOwnerBoundWorkspaceRefreshPresenter)runtime.Presenter;
                int beforeDeferredLoad = shellLists;
                await refresh.LoadBeforeShellSyncAsync(runtime.Presenter.State.DisplayOwnerContext!.Value, id, default);
                Require(shellLists == beforeDeferredLoad && runtime.Presenter.State.WorkspaceId == id
                    && runtime.Presenter.State.Error is null,
                    "Host-owned shell synchronization must not repeat the presenter's workspace-list read.");
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
                var nativeDelta = preview.FinalizationPlan!.OrderedDeltas.Single(delta =>
                    delta.Kind == CharacterCreationFinalizationDeltaKinds.Skill && delta.AfterValue == "native");
                Require(nativeDelta.TargetName == native.Name
                    && LifeModuleCompletionPage.ReviewChange(preview, nativeDelta)
                        .StartsWith(native.Name + ": ", StringComparison.Ordinal),
                    "The real Life Modules plan lost the admitted native-language name in final review.");
                foreach (var delta in preview.FinalizationPlan.OrderedDeltas.Where(delta =>
                    delta.Kind == CharacterCreationFinalizationDeltaKinds.Metatype
                    || delta.DeltaId.StartsWith("module:", StringComparison.Ordinal)
                    || delta.DeltaId == "lifestyle:default"))
                    Require(!string.IsNullOrWhiteSpace(delta.TargetName) && delta.TargetName == delta.AfterValue,
                        "The real Life Modules plan exposes a technical ID instead of its admitted name: " + delta.DeltaId);
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, false)).Value is null
                    && probe!.ConfirmCalls == 0, "An unconfirmed preview dispatched a write.");
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview with { }, true)).Value is null,
                    "An unissued copy acquired native confirmation authority.");
                if (scenario == "cancel-after-commit") probe!.AfterConfirm = cancel.Cancel;
                observingCommit = true;
                int beforeCommitShellLists = shellLists;
                var applied = await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, true, cancel.Token);
                observingCommit = false;
                Require(shellLists == beforeCommitShellLists + (scenario == "cancel-after-commit" ? 0 : 1),
                    "Native completion must perform exactly one shell list, or none when canceled before refresh.");
                Require(applied.Value is { CharacterCreated: true, CharacterEffectsApplied: true },
                    "Native completion lost a committed result: " + string.Join(", ", applied.Blockers));
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(cold.ContentRevision == before.ContentRevision + 1 && cold.ContentRevision == cold.SavedRevision
                    && cold.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null,
                    "Native completion did not retain the single atomic Career transition.");
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, true)).Value is null
                    && probe!.ConfirmCalls == 1, "A consumed review replayed its native command.");
                if (scenario == "save-career-reopen") Require(runtime.Coordinator.IsLifeModuleCompletionReceiptCurrent(applied.Value!)
                    && applied.Blockers.Count == 0 && runtime.Shell.State.ActiveWorkspaceId == id
                    && runtime.Shell.State.OwnerContext == runtime.Presenter.State.DisplayOwnerContext,
                    "Known saved Life Modules runner did not reopen in Career with the current owner-bound shell.");
                else Require(applied.Blockers.Contains(CharacterCreationFinalizationBlockers.PostCommitReopenRequired),
                    "Cancellation after commit did not preserve the known result/reopen instruction.");
                ui.AssertHealthy();
                Console.WriteLine("PASS Life Modules native completion: " + scenario);
            }
        });
    }

    // Observe only the real shell's roster reads. All other calls, including
    // exact owner capture/admission, continue to the real in-process client.
    public interface IObservedShellClient : IChummerClient, IOwnerBoundShellStateClient { }

    public class ObservedShellClientProxy : DispatchProxy
    {
        private IChummerClient _inner = null!;
        private Action _beforeList = null!;

        internal static IChummerClient Wrap(IChummerClient inner, Action beforeList)
        {
            var client = Create<IObservedShellClient, ObservedShellClientProxy>();
            var proxy = (ObservedShellClientProxy)(object)client;
            proxy._inner = inner;
            proxy._beforeList = beforeList;
            return client;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(method);
            if (method.Name == nameof(IOwnerBoundShellStateClient.ListWorkspacesAsync)) _beforeList();
            try { return method.Invoke(_inner, args); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }

    private static async Task RunOriginBookCredentialReadContentionAsync(string contentRoot)
    {
        await RunOriginBookReadLeaseBoundariesAsync();
        using var account = new ActualAccountFixture();
        await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
            productionCreationOverview: true, linkedOwners: account.Owner, accountService: account.Account);
        await runtime.Coordinator.InitializeAsync();
        await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
        await runtime.Coordinator.CreateRunnerAsync();
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Cold book owner read", default);
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
        await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
        var id = runtime.Coordinator.State.WorkspaceId!.Value;
        await runtime.Coordinator.SaveAsync();
        await Task.Run(() => SeedNativeLifeStory(runtime, id));
        await runtime.Presenter.LoadAsync(id, default);
        var original = runtime.Coordinator.State;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        account.Metadata.BeforeReadAsync = _ => { entered.TrySetResult(); return release.Task; };
        Task writer = account.Owner.InitializeAsync();
        Task<RetainedOriginBook?>? reading = null;
        bool returnedWhileWriterHeld = false;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(account.Owner.Capture() == original.DisplayOwnerContext,
                "The contention control changed the owner instead of excluding a reader.");
            reading = runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true);
            await Task.WhenAny(reading, Task.Delay(150));
            returnedWhileWriterHeld = reading.IsCompleted;
        }
        finally
        {
            release.TrySetResult();
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
            account.Metadata.BeforeReadAsync = null;
        }
        var book = await reading!.WaitAsync(TimeSpan.FromSeconds(10));
        Require(!returnedWhileWriterHeld && book is { Chapters.Count: > 0 }
            && runtime.Coordinator.IsRetainedOriginBookCurrent(book),
            "A current saved book was rejected while the real credential reader held its short exclusion gate.");
        Require(runtime.Coordinator.State.ContentRevision == original.ContentRevision
            && runtime.Coordinator.State.SavedRevision == original.SavedRevision && account.Requests == 0,
            "Waiting for local book access changed the runner or contacted Hub.");
        Console.WriteLine("PASS saved Origin book waits for actual local credential hydration without provider calls or mutations");
        await RunOriginDecisionReadContentionAsync(runtime, account);
    }

    private static async Task RunOriginDecisionReadContentionAsync(NativeRewardRuntime runtime, ActualAccountFixture account)
    {
        var service = runtime.Services.GetRequiredService<IOwnerBoundLifeModuleOriginService>();
        var store = new FileOriginDossierDraftTimelineStore(runtime.StateDirectory);
        var id = runtime.Coordinator.State.WorkspaceId!.Value;
        var gate = (SemaphoreSlim)typeof(AndroidAccountLinkService).GetField("_credentialCommitGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(account.Account)!;
        foreach (string phase in new[] { "before", "after", "cancel", "aba" })
        {
            var owner = account.Owner.Capture();
            var before = await store.LoadAsync(owner.Owner.NormalizedValue, id.Value);
            Require(before is not null, "The decision-read fixture has no retained checkpoint.");
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool held = false;
            void ExcludeReader()
            {
                Require(gate.Wait(0), "The diagnostic writer could not acquire the released Core lease.");
                held = true;
                entered.TrySetResult();
            }
            var observed = new RestoreReadProbe(service, () => { if (phase != "before") ExcludeReader(); });
            var phone = new OriginDossierLifeModulePhoneRuntime(observed, store, account.Owner);
            Task<OriginDossierLifeModulePhoneResult>? opening = null;
            try
            {
                if (phase == "before") ExcludeReader();
                opening = phone.OpenAsync(owner, id.Value, cancellation.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await Task.WhenAny(opening, Task.Delay(150));
                Require(!opening.IsCompleted && account.Owner.Capture() == owner,
                    "A current Life Modules read was rejected solely because credential access was busy.");
                if (phase == "cancel")
                {
                    cancellation.Cancel();
                    try
                    {
                        await opening.WaitAsync(TimeSpan.FromSeconds(5));
                        throw new InvalidOperationException("Canceled Life Modules read gained admission.");
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                }
                if (phase == "aba")
                {
                    account.Account.OwnerAuthority.Invalidate();
                    account.Account.OwnerAuthority.PublishLocal();
                }
            }
            finally { if (held) gate.Release(); }
            if (phase != "cancel")
            {
                var result = await opening!.WaitAsync(TimeSpan.FromSeconds(30));
                Require(result.IsSuccess == (phase != "aba"),
                    "Life Modules read confused credential contention with a retired owner transition.");
            }
            var after = await store.LoadAsync(owner.Owner.NormalizedValue, id.Value);
            Require(after?.CheckpointDigest == before!.CheckpointDigest && account.Requests == 0,
                "Decision read rewrote its checkpoint or contacted a provider.");
        }
        Console.WriteLine("PASS Life Modules read: busy-before, busy-after-restore, cancellation and owner ABA without provider calls");
    }

    private sealed class RestoreReadProbe(IOwnerBoundLifeModuleOriginService inner, Action afterRestore)
        : IOwnerBoundLifeModuleOriginService
    {
        public bool IsCurrent(OwnerContextStamp owner) => inner.IsCurrent(owner);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Start(OwnerContextStamp owner, string workspaceId)
            => inner.Start(owner, workspaceId);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Restore(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint)
        {
            var result = inner.Restore(owner, checkpoint);
            Require(result.Outcome == LifeModuleOriginDossierOutcomes.Success, "The real Core restore failed before contention.");
            afterRestore();
            return result;
        }
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Prepare(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
            string choiceId, IReadOnlyDictionary<string, string>? followUpValues = null)
            => inner.Prepare(owner, checkpoint, choiceId, followUpValues);
        public LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> Confirm(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
            string previewDigest, string idempotencyKey, bool explicitlyConfirmed)
            => inner.Confirm(owner, checkpoint, previewDigest, idempotencyKey, explicitlyConfirmed);
    }

    private static async Task RunOriginBookReadLeaseBoundariesAsync()
    {
        using var account = new ActualAccountFixture();
        await account.Owner.InitializeAsync();
        var gate = (SemaphoreSlim)typeof(AndroidAccountLinkService).GetField("_credentialCommitGate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(account.Account)!;
        foreach (bool cancel in new[] { true, false })
        {
            var owner = account.Owner.Capture();
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await gate.WaitAsync();
            Task<bool>? read = null;
            try
            {
                Require(!account.Owner.TryAcquire(owner, out _), "Ordinary owner admission started blocking.");
                read = account.Owner.RunReadAsync(owner, () =>
                {
                    entered.TrySetResult();
                    bool acquired = account.Owner.TryAcquire(owner, out var lease);
                    using (lease) return acquired;
                }, cancellation.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (cancel)
                {
                    cancellation.Cancel();
                    try
                    {
                        await read.WaitAsync(TimeSpan.FromSeconds(5));
                        throw new InvalidOperationException("A canceled book read acquired credential access.");
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                }
                else
                {
                    // Exercise the same publisher under its real exclusion:
                    // Local → unavailable → Local must retire the old epoch.
                    account.Account.OwnerAuthority.Invalidate();
                    account.Account.OwnerAuthority.PublishLocal();
                    Require(account.Owner.Capture().Owner == owner.Owner && account.Owner.Capture() != owner,
                        "The queued read negative control did not create an ABA transition.");
                }
            }
            finally { gate.Release(); }
            if (!cancel) Require(!await read!.WaitAsync(TimeSpan.FromSeconds(5)),
                "A queued book read accepted an owner stamp retired while waiting.");
        }
        var current = account.Owner.Capture();
        bool admitted = await account.Owner.RunReadAsync(current, () =>
        {
            Require(account.Owner.TryAcquire(current, out var lease), "Current read did not acquire its lease.");
            using (lease)
                Require(!account.Owner.TryAcquire(current, out _), "Nested read admission waited on or bypassed its own lease.");
            return account.Owner.Capture() == current;
        }, default).WaitAsync(TimeSpan.FromSeconds(5));
        Require(admitted && gate.CurrentCount == 1 && account.Requests == 0,
            "Book read admission leaked its lease or performed a remote call.");
        Console.WriteLine("PASS book read lease: cancellation while excluded, post-wait owner ABA rejection, non-reentrancy and no network");
    }

    private static void SeedNativeLifeStory(NativeRewardRuntime runtime, CharacterWorkspaceId id, int? stopAfterDecisions = null)
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
        int decisions = stopAfterDecisions ?? modules.Length + 1;
        Require(decisions > 0 && decisions <= modules.Length + 1, "Invalid story fixture length.");
        for (int index = 0; index < decisions; index++)
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
        Require(checkpoint.Projection.CurrentTurn.IsTerminal == (decisions == modules.Length + 1)
            && checkpoint.Projection.VisibleChapters.Count == decisions,
            "Story setup did not retain every confirmed chapter.");
        // Match the phone runtime's durable timeline, so opening the real Build
        // page restores the accepted story rather than inventing a new one.
        new FileOriginDossierDraftTimelineStore(runtime.StateDirectory).SaveAsync(checkpoint).GetAwaiter().GetResult();
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
        public Func<Task>? BeforeReadAsync { get; set; }
        public int Deliveries { get; private set; }
        public string Html { get; private set; } = string.Empty;
        public int EpubDeliveries { get; private set; }
        public byte[] Epub { get; private set; } = [];
        public Task<AndroidDocument?> OpenAsync(CancellationToken ct) => throw new InvalidOperationException("No import expected.");
        public Task<bool> SaveAsAsync(string name, string mediaType, Stream content, CancellationToken ct)
            => throw new InvalidOperationException("Book export must be context-bound.");
        public async Task<bool> SaveAsAsync(string name, string mediaType, Stream content, Func<bool> isCurrent, CancellationToken ct)
        {
            Require(name == "origin-dossier.html" && mediaType == "text/html"
                || name == "origin-dossier.epub" && mediaType == "application/epub+zip", "Unexpected book output format.");
            BeforeRead?.Invoke();
            if (BeforeReadAsync is { } beforeRead) await beforeRead();
            if (!isCurrent()) throw new OperationCanceledException();
            if (mediaType == "application/epub+zip")
            {
                using var saved = new MemoryStream(); await content.CopyToAsync(saved, ct);
                Epub = saved.ToArray(); EpubDeliveries++; return true;
            }
            using var reader = new StreamReader(content, leaveOpen: true);
            Html = await reader.ReadToEndAsync(ct);
            Deliveries++;
            return true;
        }
    }

    private sealed class LifeSceneInputProbe : IAndroidImageDocumentService
    {
        internal static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
        internal int Reads { get; private set; }
        internal Func<Task>? BeforeReturnAsync { get; set; }
        internal bool Canceled { get; set; }
        public async Task<AndroidImageDocumentCandidate?> OpenValidatedAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Reads++;
            if (BeforeReturnAsync is { } beforeReturn) await beforeReturn();
            if (Canceled) return null;
            Require(AndroidImageDocumentValidation.TryCreateCandidate("scene.png", "content://test/scene",
                "image/png", "image/png", 1, 1, Png, out var candidate), "Scene test candidate invalid.");
            return candidate;
        }
    }
}
