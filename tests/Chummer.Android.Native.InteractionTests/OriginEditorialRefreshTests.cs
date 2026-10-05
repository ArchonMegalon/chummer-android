using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.OriginBooks;
using Chummer.Run.Contracts.Community;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunOriginEditorialRefreshAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            var account = DispatchProxy.Create<IAndroidAccountLinkService, OriginSuccessorAccount>();
            var remote = (OriginSuccessorAccount)account;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, accountService: account);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Editorial refresh", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await runtime.Coordinator.SaveAsync();
            await Task.Run(() => SeedNativeLifeStory(runtime, id));
            await runtime.Presenter.LoadAsync(id, default);
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            var chapter = book.Chapters[1];
            var source = book.AuthoringSource(chapter);
            var original = OriginBookProseDraft.Create(chapter, book.Locale, OriginChapterSourceIdentity.RequestId(source),
                new string('d', 64), "The full original story is readable while its correction is checked.");
            var revised = OriginBookProseDraft.Create(chapter, book.Locale, original.JobId, new string('e', 64),
                "The full corrected story preserves the character's name and granted abilities.");
            remote.Seed(source, original);
            book = (await runtime.Coordinator.SyncOriginChapterAsync(book, chapter, source, false, () => true, default)).Book!;
            Require(book?.Reading(chapter)?.Pending?.DraftDigest == original.DraftDigest, "Original pending prose was not retained.");
            string originalEdition = book!.Readings!.Digest;
            string TextHash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            var editorial = new OriginChapterEditorialProvenance(TextHash(original.Text), original.ProviderReceiptDigest,
                "ea_ai_with_codex_edit");
            OriginChapterAuthoringJob Revision(OriginChapterAuthoringJob job) => job with
            {
                DraftText = revised.Text, ProviderReceiptDigest = revised.ProviderReceiptDigest, Editorial = editorial
            };
            string DiskDigest() => new OriginBookReadingStore(runtime.StateDirectory)
                .Load(owners.Capture().Owner.Value, id.Value).Digest;

            Require(await runtime.Coordinator.StageOriginBookProseDraftAsync(book, revised, () => true, default) is null,
                "Generic staging silently overwrote a pending draft.");
            foreach (var corrupt in new Func<OriginChapterAuthoringJob, OriginChapterAuthoringJob>[]
            {
                job => job with { Editorial = null },
                job => job with { Editorial = editorial with { OriginalTextDigest = new string('f', 64) } },
                job => job with { Editorial = editorial with { OriginalProviderReceiptDigest = new string('f', 64) } },
                job => job with { Editorial = editorial with { Method = "unverified" } },
                job => job with { ReaderAcceptedTextDigest = TextHash(revised.Text) },
                job => job with { RequestId = new string('f', 64) },
                job => job with { SourceDigest = new string('f', 64) }
            })
            {
                remote.RewriteReadJob = job => corrupt(Revision(job));
                var rejected = await runtime.Coordinator.SyncOriginChapterAsync(book, chapter, source, false, () => true, default);
                Require((rejected.Book is null || rejected.Result.Outcome == AndroidOriginChapterOutcome.Conflict)
                    && DiskDigest() == originalEdition, "Unbound editorial readback changed the retained chapter.");
            }
            remote.RewriteReadJob = Revision;
            using (var canceled = new CancellationTokenSource())
            {
                remote.AfterPredecessorRead = canceled.Cancel;
                var stopped = await runtime.Coordinator.SyncOriginChapterAsync(book, chapter, source, false, () => true, canceled.Token);
                Require(stopped.Book is null && DiskDigest() == originalEdition, "Canceled revision changed saved prose.");
            }
            remote.AfterPredecessorRead = null;

            var reader = new RetainedOriginBookPage(runtime.Coordinator);
            var window = new Window(new NavigationPage(reader));
            using var alerts = new IssuedPageAlerts(reader, window);
            await alerts.PreflightAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            remote.BeforePredecessorRead = () => { entered.TrySetResult(); return release.Task; };
            Task opening = ui.BeginAsyncVoid(() => IssuedPageLifecycle(reader, "OnAppearing"));
            Button? staleRead = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(IssuedElements(reader).OfType<Label>().Any(l => l.Text == original.Text),
                    "A slow editorial check hid the locally saved full chapter.");
                staleRead = IssuedElements(reader).OfType<Button>().Single(b => b.AutomationId == $"origin-chapter-read-{chapter.Sequence}");
            }
            finally { release.TrySetResult(); }
            await opening;
            remote.BeforePredecessorRead = null;
            Require(IssuedElements(reader).OfType<Label>().Any(l => l.Text == revised.Text)
                && !IssuedElements(reader).OfType<Label>().Any(l => l.Text == original.Text),
                "Opening the reader did not display the authenticated replacement for unread prose.");
            Require(remote.Acceptances == 0 && remote.Requests == 0,
                "Refreshing unread prose accepted a chapter or generated another one.");
            await ui.BeginAsyncVoid(() => ((IButtonController)staleRead!).SendClicked());
            Require(remote.Acceptances == 0 && remote.Requests == 0, "A stale Read action accepted the superseded chapter.");
            IssuedPageLifecycle(reader, "OnDisappearing");

            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            Require(book.Reading(chapter) is { Selected: null, Pending: { } pending }
                && pending.DraftDigest == revised.DraftDigest && DiskDigest() != originalEdition,
                "Cold reread lost the pending correction or implicitly marked it read.");
            book = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(book, revised, true, true, () => true, default))!;
            string acceptedEdition = DiskDigest();
            remote.Seed(source, revised);
            remote.RewriteReadJob = job => job with
            {
                DraftText = "A later unauthorized rewrite of accepted prose.", ProviderReceiptDigest = new string('f', 64),
                Editorial = new(TextHash(revised.Text), revised.ProviderReceiptDigest, "ea_ai")
            };
            var acceptedConflict = await runtime.Coordinator.SyncOriginChapterAsync(book, chapter, source, false, () => true, default);
            Require(acceptedConflict.Result.Outcome == AndroidOriginChapterOutcome.Conflict
                && DiskDigest() == acceptedEdition, "A remotely revised chapter changed an already-read local edition.");
            remote.AfterPredecessorRead = () => { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); };
            var aba = await runtime.Coordinator.SyncOriginChapterAsync(book, chapter, source, false, () => true, default);
            Require(aba.Book is null && DiskDigest() == acceptedEdition, "Owner A-to-B-to-A admitted an editorial response.");
            RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
            Require(alerts.Titles.Count == 0, "Editorial refresh displayed an unexpected page error.");
            ui.AssertHealthy();
            Console.WriteLine("PASS editorial: exact unread correction, visible local prose during read, no paid writes/acceptance, stale actions, cold persistence, accepted immutability, cancellation/owner ABA");
        });
    }
}
