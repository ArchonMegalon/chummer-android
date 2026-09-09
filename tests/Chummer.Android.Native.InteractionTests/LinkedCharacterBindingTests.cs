using System.Reflection;
using System.Text;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Infrastructure.Xml;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;
using Fixture = TabletInspectorBindingTests.Fixture;

internal static class LinkedCharacterBindingTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    public static async Task RunAsync()
    {
        await SlowOwnerReadDoesNotBlockCallerAsync();
        await FinalOwnerReadAfterBeginRevalidatesBeforeDispatchAsync();
        await PickerAndActivationWaitsRetainExactAuthorityAsync();
        await AmbiguousDispatchPreservesEveryReferencedFileAsync();
        await NativeControlsRejectStaleDialogsAndPickersAsync();
        await RealStagingOwnsUniqueFilesAndPreservesCollisionAsync();
        await LinkedCharacterDurabilityTests.RunAsync();
        await LinkedCharacterIntentJournalTests.RunAsync();
        RecoveryEntryDoesNotRequireTheOriginalRunner();
        SuccessfulDocumentReplaceIsNotACheckpoint();
        Console.WriteLine("PASS linked-character binding (managed native controls, canonical codec and real temporary files; no device/Core mutation receipt)");
    }

    private static async Task SlowOwnerReadDoesNotBlockCallerAsync()
    {
        using var reader = new BlockingOwnerReader();
        using var cancellation = new CancellationTokenSource();
        var files = new Files();
        files.Picker.SetResult(null);
        using var fixture = new Fixture(linkedFiles: files, linkedReader: reader);
        var expected = fixture.State;
        var target = expected.ActiveCollectionEditor!.Items[0].Target;
        var returned = new TaskCompletionSource<Task<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = new Thread(() =>
        {
            try { returned.TrySetResult(fixture.Coordinator.TryAttachBoundLinkedCharacterAsync(
                target, expected, () => true, cancellation.Token)); }
            catch (Exception error) { returned.TrySetException(error); }
        }) { IsBackground = true };
        bool responsive = false;
        caller.Start();
        try
        {
            Require(reader.Entered.Wait(TimeSpan.FromSeconds(5)), "Owner read did not enter.");
            try
            {
                await returned.Task.WaitAsync(TimeSpan.FromSeconds(1));
                responsive = true;
            }
            catch (TimeoutException) { }
        }
        finally
        {
            cancellation.Cancel();
            reader.Release.Set();
        }
        Task<bool> action = await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Require(caller.Join(TimeSpan.FromSeconds(5)), "Owner-read caller did not finish.");
        Require(responsive, "Linked owner file access blocked the native action caller.");
        Require(files.StageCalls == 0 && fixture.Requests.Count == 0,
            "Cancellation while reading owner opened a picker or dispatched a mutation.");
    }

    private static async Task FinalOwnerReadAfterBeginRevalidatesBeforeDispatchAsync()
    {
        foreach (bool attach in new[] { true, false })
        foreach (string drift in new[] { "workspace", "revision", "section", "capability", "duplicate", "view", "owner", "cancel", "unchanged" })
        {
            var files = new Files();
            PausedFinalOwnerReader? reader = null;
            using var fixture = new Fixture(linkedFiles: files,
                linkedReaderDecorator: inner => reader = new PausedFinalOwnerReader(inner));
            reader!.ShouldPause = () => fixture.LinkedJournal.ReadAll("local-single-user", true)
                .Any(record => !record.NotDispatched && !record.EffectObserved);
            var expected = fixture.State;
            var target = expected.ActiveCollectionEditor!.Items[0].Target;
            bool currentView = true;
            using var cancellation = new CancellationTokenSource();
            files.Picker.SetResult(files.Staged);
            Task<bool> action = attach
                ? fixture.Coordinator.TryAttachBoundLinkedCharacterAsync(target, expected, () => currentView, cancellation.Token)
                : fixture.Coordinator.TryRemoveBoundLinkedCharacterAsync(target, expected, () => currentView, cancellation.Token);
            bool canceled = false;
            bool applied = false;
            try
            {
                await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var pending = new AndroidLinkedCharacterIntentJournal(fixture.LinkedJournalDirectory)
                    .ReadAll("local-single-user", true).Single();
                Require(!pending.NotDispatched && !pending.EffectObserved && fixture.Requests.Count == 0
                    && !action.IsCompleted && !reader.FinalToken.CanBeCanceled,
                    "Final owner read was not joined after durable Begin and before dispatch.");
                switch (drift)
                {
                    case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                    case "revision": fixture.AdvanceRevision(); break;
                    case "section": fixture.State = fixture.State with { ActiveSectionId = "pets" }; break;
                    case "capability":
                        var items = (WorkspaceCollectionItemEditorState[])fixture.State.ActiveCollectionEditor!.Items;
                        items[0] = items[0] with { LinkedCharacter = items[0].LinkedCharacter! with { CanAttach = false, CanRemove = false } };
                        break;
                    case "duplicate":
                        var duplicates = (WorkspaceCollectionItemEditorState[])fixture.State.ActiveCollectionEditor!.Items;
                        duplicates[1] = duplicates[0];
                        break;
                    case "view": currentView = false; break;
                    case "owner": reader.OwnerOverride = new("changed-owner", false); break;
                    case "cancel": cancellation.Cancel(); break;
                }
                Require(!action.IsCompleted && fixture.Requests.Count == 0,
                    "A suspended post-Begin owner read detached or dispatched during cancellation/drift.");
            }
            finally
            {
                reader.Release.TrySetResult();
                Exception? observationFailure = null;
                try { applied = await action.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) { canceled = true; }
                catch (Exception error) { observationFailure = error; throw; }
                finally
                {
                    // WaitAsync bounds observation, not the underlying mutation.
                    // Preserve that failure but never dispose a live journal/action.
                    try { await action; }
                    catch (OperationCanceledException) { canceled = true; }
                    catch (Exception) when (observationFailure is not null) { }
                }
            }
            Require(!applied && canceled == (drift is "cancel" or "unchanged")
                && fixture.Requests.Count == (drift == "unchanged" ? 1 : 0),
                $"Final owner read {drift} dispatched or reported the wrong outcome.");
            var record = new AndroidLinkedCharacterIntentJournal(fixture.LinkedJournalDirectory)
                .ReadAll("local-single-user", true).Single();
            Require(record.NotDispatched == (drift != "unchanged") && !record.EffectObserved
                && record.Intent.Target == target && record.Intent.ContentRevision == 5
                && record.Intent.SavedRevision == 5 && record.Intent.Attach == attach,
                $"Final owner read {drift} lost the original durable intent or forged an outcome.");
            Require(files.Deleted.Count == (attach && drift != "unchanged" ? 1 : 0)
                && files.Deleted.All(path => path == files.Staged.FileName),
                $"Final owner read {drift} deleted a prior/shared file or lost staged-file custody.");
        }
    }

    private static async Task PickerAndActivationWaitsRetainExactAuthorityAsync()
    {
        foreach (bool attach in new[] { true, false })
        foreach (string drift in new[] { "workspace", "revision", "section", "capability", "duplicate", "view", "cancel", "unchanged" })
        {
            var files = new Files();
            using var fixture = new Fixture(linkedFiles: files);
            var expected = fixture.State;
            var target = expected.ActiveCollectionEditor!.Items[0].Target;
            bool currentView = true;
            using var cancellation = new CancellationTokenSource();
            Require(fixture.ActivationGate.Wait(0), "Could not hold activation gate for link test.");
            Task<bool> action;
            try
            {
                files.Picker.SetResult(files.Staged);
                action = attach
                    ? fixture.Coordinator.TryAttachBoundLinkedCharacterAsync(target, expected, () => currentView, cancellation.Token)
                    : fixture.Coordinator.TryRemoveBoundLinkedCharacterAsync(target, expected, () => currentView, cancellation.Token);
                Require(!action.IsCompleted && fixture.Requests.Count == 0, "Link did not wait on the actual activation gate.");
                switch (drift)
                {
                    case "workspace": fixture.State = fixture.State with { WorkspaceId = new("different-runner") }; break;
                    case "revision": fixture.AdvanceRevision(); break;
                    case "section": fixture.State = fixture.State with { ActiveSectionId = "pets" }; break;
                    case "capability":
                        // Same editor/list object, changed authority inside the existing array.
                        var items = (WorkspaceCollectionItemEditorState[])fixture.State.ActiveCollectionEditor!.Items;
                        items[0] = items[0] with { LinkedCharacter = items[0].LinkedCharacter! with { CanAttach = false, CanRemove = false } };
                        break;
                    case "duplicate":
                        var duplicates = (WorkspaceCollectionItemEditorState[])fixture.State.ActiveCollectionEditor!.Items;
                        duplicates[1] = duplicates[0];
                        break;
                    case "view": currentView = false; break;
                    case "cancel": cancellation.Cancel(); break;
                }
            }
            finally { fixture.ActivationGate.Release(); }
            bool canceled = false;
            try { Require(!await action.WaitAsync(TimeSpan.FromSeconds(10)), "Rejected authority was reported applied."); }
            catch (OperationCanceledException) { canceled = true; }
            Require(fixture.Requests.Count == (drift == "unchanged" ? 1 : 0), $"Gate {drift} dispatched the wrong number of links.");
            Require(canceled == (drift is "unchanged" or "cancel"), "Unexpected cancellation outcome.");
            Require(files.Deleted.Count == (attach && drift != "unchanged" ? 1 : 0),
                $"Gate {drift} discarded an uncertain/prior file or leaked undispatched staging.");
            Require(files.Deleted.All(path => path == files.Staged.FileName), "Cleanup targeted a prior linked file.");
        }
        foreach (bool duplicate in new[] { true, false })
        {
            var files = new Files();
            using var fixture = new Fixture(linkedFiles: files);
            var items = (WorkspaceCollectionItemEditorState[])fixture.State.ActiveCollectionEditor!.Items;
            if (duplicate) items[1] = items[0];
            else items[0] = items[0] with { LinkedCharacter = items[0].LinkedCharacter! with { CanAttach = false } };
            Require(!await fixture.Coordinator.TryAttachBoundLinkedCharacterAsync(items[0].Target, fixture.State, () => true)
                && files.StageCalls == 0, "Invalid initial authority opened a picker.");
        }
    }

    private static async Task AmbiguousDispatchPreservesEveryReferencedFileAsync()
    {
        foreach (bool attach in new[] { true, false })
        foreach (string outcome in new[] { "cancel-after-replace", "exception-after-replace", "shell-failure-after-replace", "error-state" })
        {
            var files = new Files();
            using var fixture = new Fixture(linkedFiles: files);
            var expected = fixture.State;
            var target = expected.ActiveCollectionEditor!.Items[0].Target;
            fixture.CollectionResult = request =>
            {
                // Controlled presenter projection, NOT a fabricated durable Core receipt.
                fixture.AdvanceRevision(); // Real FileWorkspaceStore replacement is 5/5 -> 6/5.
                var editor = fixture.State.ActiveCollectionEditor!;
                fixture.State = fixture.State with { ActiveCollectionEditor = editor with
                { Items = editor.Items.Select(item => item.Target == target ? item with
                    { LinkedCharacter = item.LinkedCharacter! with
                        { IsLinked = attach, FileName = attach ? files.Staged.FileName : "",
                          RelativeFileName = attach ? files.Staged.RelativeFileName : "",
                          DisplayName = attach ? files.Staged.DisplayName : "", IdentityResolved = attach } } : item).ToArray() },
                    Error = outcome == "error-state" ? "Postcommit observation failed." : null };
                return outcome switch
                {
                    "cancel-after-replace" => Task.FromCanceled(new CancellationToken(true)),
                    "exception-after-replace" => Task.FromException(new IOException("Postcommit capture failed.")),
                    _ => Task.CompletedTask // The fixture's strict shell rejects subsequent synchronization.
                };
            };
            files.Picker.SetResult(files.Staged);
            bool faulted = false;
            try
            {
                if (attach) await fixture.Coordinator.TryAttachBoundLinkedCharacterAsync(target, expected, () => true);
                else await fixture.Coordinator.TryRemoveBoundLinkedCharacterAsync(target, expected, () => true);
            }
            catch (Exception) { faulted = true; }
            Require(faulted && fixture.Requests.Count == 1 && files.Deleted.Count == 0,
                $"{outcome} retried the operation or deleted a potentially referenced file.");
            var retained = new AndroidLinkedCharacterIntentJournal(fixture.LinkedJournalDirectory)
                .ReadAll("local-single-user", true).Single();
            Require(!retained.EffectObserved && !retained.NotDispatched
                && retained.Intent.Target == target && retained.Intent.ContentRevision == 5
                && retained.Intent.SavedRevision == 5 && retained.Intent.Attach == attach,
                "Uncertain dispatch lost its durable original target/revision intent.");
            Require(fixture.State.ContentRevision == 6 && fixture.State.SavedRevision == 5
                && fixture.Coordinator.Notice?.Contains("could not be confirmed", StringComparison.Ordinal) == true,
                "An uncertain post-dispatch outcome masqueraded as uncommitted or successful.");
            // The other item/workspace projection still uses the old path.
            Require(fixture.State.ActiveCollectionEditor!.Items[1].LinkedCharacter!.FileName == "/test-private/prior.chum5",
                "Replacing one link removed another reference to the old file.");
        }
    }

    private static void RecoveryEntryDoesNotRequireTheOriginalRunner()
    {
        using var fixture = new Fixture(account: DispatchProxy.Create<IAndroidAccountLinkService, LinkedRecoveryUnlinkedAccountProxy>());
        fixture.State = fixture.State with { Profile = null, WorkspaceId = null, OpenWorkspaces = [],
            ActiveCollectionEditor = null, ActiveSectionId = null };
        var home = new HomePage(fixture.Coordinator);
        Refresh(home);
        Require(Elements(home).OfType<Button>().Single(button => button.AutomationId == "home-linked-runner-recovery").IsEnabled,
            "Recovery disappeared after its workspace or target was removed.");
        var recovery = new LinkedCharacterRecoveryPage(fixture.Coordinator);
        Require(recovery.AutomationId == "linked-runner-recovery-page"
            && Elements(recovery).OfType<Button>().Single(button => button.AutomationId == "linked-recovery-refresh").IsEnabled,
            "Recovery cannot be refreshed independently of normal runner initialization.");
        Require(!Elements(recovery).OfType<Button>().Any(button => button.Text is "Apply" or "Retry" or "Delete")
            && fixture.Requests.Count == 0, "Recovery introduced an unreviewed mutation or replay control.");
    }

    private static async Task NativeControlsRejectStaleDialogsAndPickersAsync()
    {
        foreach (bool phone in new[] { true, false })
        foreach (bool attach in new[] { true, false })
        foreach (string drift in new[] { "refresh", "departure", "workspace", "section", "capability", "duplicate", "selection", "unchanged" })
        {
            if (phone && drift == "selection") continue; // Tablet selection stays in a distinct master pane.
            var files = new Files();
            using var fixture = new Fixture(linkedFiles: files);
            var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int phoneDialogs = 0;
            fixture.Confirmation = confirmation.Task;
            ContentPage page = phone
                ? new CollectionItemEditorPage(fixture.Coordinator, fixture.State.ActiveCollectionEditor!.Items[0].Target,
                    (_, _, _, _) => { phoneDialogs++; return confirmation.Task; })
                : fixture.Page;
            if (phone) Refresh(page);
            string prefix = phone ? $"collection-linked-{(attach ? "attach" : "remove")}-" : $"tablet-linked-{(attach ? "attach" : "remove")}";
            Button button = Elements(page).OfType<Button>().Single(value => value.AutomationId.StartsWith(prefix, StringComparison.Ordinal));
            var gate = (NativePageActionGate)typeof(NativePageBase).GetField("_actionGate", Private)!.GetValue(page)!;
            ((IButtonController)button).SendClicked();
            Require(gate.IsClaimed && fixture.Requests.Count == 0
                && (attach ? files.StageCalls == 1 : (phone ? phoneDialogs : fixture.DialogCalls) == 1),
                "Real native control did not enter the controlled picker/confirmation.");
            ((IButtonController)button).SendClicked();
            Require(attach ? files.StageCalls == 1 : (phone ? phoneDialogs : fixture.DialogCalls) == 1,
                "Overlapping native clicks opened another operation.");
            switch (drift)
            {
                case "refresh": Refresh(page); break;
                case "departure": page.GetType().GetMethod("OnDisappearing", Private)!.Invoke(page, null); break;
                case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                case "section": fixture.State = fixture.State with { ActiveSectionId = "pets" }; break;
                case "capability":
                    var items = (WorkspaceCollectionItemEditorState[])fixture.State.ActiveCollectionEditor!.Items;
                    items[0] = items[0] with { LinkedCharacter = items[0].LinkedCharacter! with { CanAttach = false, CanRemove = false } };
                    break;
                case "duplicate":
                    var duplicates = (WorkspaceCollectionItemEditorState[])fixture.State.ActiveCollectionEditor!.Items;
                    duplicates[1] = duplicates[0];
                    break;
                case "selection": fixture.Click("tablet-collection-item-22222222-2222-4222-8222-222222222222"); break;
            }
            if (attach) files.Picker.SetResult(files.Staged);
            else confirmation.SetResult(true);
            await WaitUntilAsync(() => !gate.IsClaimed);
            Require(fixture.Requests.Count == (drift == "unchanged" ? 1 : 0),
                $"{(phone ? "Phone" : "Tablet")} {drift} callback applied against stale authority.");
            Require(files.Deleted.Count == (attach && drift != "unchanged" ? 1 : 0),
                "Native page cleanup lost an uncertain attachment.");
            if (drift != "unchanged")
            {
                ((IButtonController)button).SendClicked(); // Detached original control must stay dead.
                Require(fixture.Requests.Count == 0 && files.StageCalls == (attach ? 1 : 0),
                    "Detached native control became live after its action completed.");
            }
        }
    }

    private static async Task RealStagingOwnsUniqueFilesAndPreservesCollisionAsync()
    {
        string root = Directory.CreateTempSubdirectory("chummer-linked-file-safety-").FullName;
        try
        {
            var documents = new Documents();
            var codec = new Chummer5LinkedDocumentCodec();
            var target = new WorkspaceCollectionItemTarget(WorkspaceCollectionKind.Contact, "11111111-1111-4111-8111-111111111111");
            var service = new AndroidLinkedCharacterFileService(documents, codec, () => root, Guid.NewGuid);
            var first = (await service.StageAsync(target, CancellationToken.None))!;
            var second = (await service.StageAsync(target, CancellationToken.None))!;
            Require(first.FileName != second.FileName && first.RelativeFileName != second.RelativeFileName,
                "Identical picks shared ownership of one content-addressed file.");
            Require(File.ReadAllBytes(first.FileName).SequenceEqual(Documents.Payload)
                && File.ReadAllBytes(second.FileName).SequenceEqual(Documents.Payload)
                && first.Identity.CharacterName == "Nightshade", "Canonical linked-document decode/file bytes changed.");
            Require(documents.Returned.All(bytes => bytes.All(value => value == 0)), "Selected document buffers were not cleared.");
            string expectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Documents.Payload));
            Require(first.ContentSha256 == expectedHash && second.ContentSha256 == expectedHash
                && await service.MatchesStagedFileAsync(target, first.FileName, expectedHash, default),
                "Durable staging did not bind the full selected-file digest.");
            Require(!await service.MatchesStagedFileAsync(target, first.FileName, new string('0', 64), default)
                && !await service.MatchesStagedFileAsync(target, first.FileName, "sha256:" + expectedHash, default)
                && !await service.MatchesStagedFileAsync(target with { ItemId = Guid.NewGuid().ToString("D") }, first.FileName, expectedHash, default),
                "Staged readback accepted a stale digest, wrong digest format or another typed target.");
            File.WriteAllBytes(first.FileName, System.Text.Encoding.UTF8.GetBytes("changed after staging"));
            Require(!await service.MatchesStagedFileAsync(target, first.FileName, expectedHash, default),
                "Changed file bytes retained valid staged authority.");
            using (var oversized = new FileStream(first.FileName, FileMode.Open, FileAccess.Write))
                oversized.SetLength(8 * 1024 * 1024 + 1);
            Require(!await service.MatchesStagedFileAsync(target, first.FileName, expectedHash, default),
                "Oversized staged readback escaped the pre-allocation limit.");
            File.Delete(first.FileName); // Only this fixture's newly-created private staged file.
            File.CreateSymbolicLink(first.FileName, second.FileName);
            Require(!await service.MatchesStagedFileAsync(target, first.FileName, expectedHash, default),
                "A symlink substituted another staged file.");
            await service.DeleteOwnedAsync(target, first.FileName, CancellationToken.None);
            Require(!File.Exists(first.FileName) && File.Exists(second.FileName), "Cleanup of one pick removed another pick's file.");
            var collisionService = new AndroidLinkedCharacterFileService(documents, codec, () => root,
                () => Guid.Parse("99999999-9999-4999-8999-999999999999"));
            var existing = (await collisionService.StageAsync(target, CancellationToken.None))!;
            bool rejected = false;
            try { await collisionService.StageAsync(target, CancellationToken.None); } catch (IOException) { rejected = true; }
            Require(rejected && File.ReadAllBytes(existing.FileName).SequenceEqual(Documents.Payload)
                && File.Exists(second.FileName), "Destination collision overwrote or deleted the existing linked document.");
            Require(!Directory.EnumerateFiles(Path.Combine(root, "linked-characters"), "*.tmp").Any(),
                "Failed collision retained its partial temporary write.");
            Require(documents.Returned.All(bytes => bytes.All(value => value == 0)), "Failed staging retained selected bytes.");
        }
        finally { Directory.Delete(root, recursive: true); } // Only this test's newly created temporary directory.
    }

    private static void SuccessfulDocumentReplaceIsNotACheckpoint()
    {
        using var fixture = new Fixture(linkedFiles: new Files());
        var expected = fixture.State;
        fixture.AdvanceRevision();
        bool accepted = (bool)typeof(RunnerSessionCoordinator).GetMethod("LinkedSuccessorIsCurrent", Private)!
            .Invoke(fixture.Coordinator, [expected])!;
        Require(accepted && fixture.State.ContentRevision == 6 && fixture.State.SavedRevision == 5,
            "An ordinary document replacement was falsely rejected for not being a file checkpoint.");
        var successor = fixture.State;
        foreach (string drift in new[] { "error", "busy", "workspace", "section", "checkpoint" })
        {
            fixture.State = drift switch
            {
                "error" => successor with { Error = "Missing postcommit projection." },
                "busy" => successor with { IsBusy = true },
                "workspace" => successor with { WorkspaceId = new("unrelated-runner") },
                "section" => successor with { ActiveSectionId = "pets" },
                _ => successor with { Session = successor.Session with { OpenWorkspaces = successor.OpenWorkspaces
                    .Select(workspace => workspace with { SavedRevision = workspace.ContentRevision }).ToArray() },
                    OpenWorkspaces = successor.OpenWorkspaces.Select(workspace => workspace with
                    { SavedRevision = workspace.ContentRevision }).ToArray() }
            };
            Require(!(bool)typeof(RunnerSessionCoordinator).GetMethod("LinkedSuccessorIsCurrent", Private)!
                .Invoke(fixture.Coordinator, [expected])!, $"A {drift} successor falsely confirmed the link.");
        }
        fixture.State = successor;
        fixture.AdvanceRevision();
        Require(!(bool)typeof(RunnerSessionCoordinator).GetMethod("LinkedSuccessorIsCurrent", Private)!
            .Invoke(fixture.Coordinator, [expected])!, "An unrelated extra revision satisfied link successor authority.");
    }

    private static void Refresh(ContentPage page) => page.GetType().GetMethod("Refresh", Private)!.Invoke(page, null);
    private static async Task WaitUntilAsync(Func<bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!done()) await Task.Delay(1, timeout.Token);
    }
    private static IEnumerable<Element> Elements(Element root)
    {
        yield return root;
        IEnumerable<Element> children = root switch
        {
            ContentPage page when page.Content is not null => [page.Content],
            ScrollView scroll when scroll.Content is not null => [scroll.Content],
            Border border when border.Content is not null => [border.Content],
            Layout layout => layout.Children.OfType<Element>(),
            _ => []
        };
        foreach (Element child in children)
        foreach (Element element in Elements(child)) yield return element;
    }
    private sealed class BlockingOwnerReader : IAndroidLinkedWorkspaceReader, IDisposable
    {
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim Release = new();
        public bool IsAvailable => true;
        public AndroidLinkedOwner CurrentOwner
        {
            get
            {
                Entered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Owner fixture was not released.");
                return new("local-single-user", true);
            }
        }
        public Task<AndroidLinkedOwner> ReadCurrentOwnerAsync(CancellationToken token)
            => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                AndroidLinkedOwner owner = CurrentOwner;
                token.ThrowIfCancellationRequested();
                return owner;
            }, CancellationToken.None);
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadAsync(
            Chummer.Contracts.Workspaces.CharacterWorkspaceId id, string section, CancellationToken token)
            => throw new InvalidOperationException("Cancelled owner capture must not read the workspace.");
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }

    private sealed class PausedFinalOwnerReader(IAndroidLinkedWorkspaceReader inner) : IAndroidLinkedWorkspaceReader
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<bool> ShouldPause { get; set; } = () => false;
        public AndroidLinkedOwner? OwnerOverride { get; set; }
        public CancellationToken FinalToken { get; private set; }
        private bool _paused;
        public bool IsAvailable => inner.IsAvailable;
        public AndroidLinkedOwner CurrentOwner => OwnerOverride ?? inner.CurrentOwner;
        public async Task<AndroidLinkedOwner> ReadCurrentOwnerAsync(CancellationToken token)
        {
            // Suspend at the real journal boundary, not an assumed Nth read.
            if (!_paused && ShouldPause())
            {
                _paused = true;
                FinalToken = token;
                Entered.TrySetResult();
                await Release.Task;
            }
            return OwnerOverride ?? await inner.ReadCurrentOwnerAsync(token);
        }
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadAsync(
            Chummer.Contracts.Workspaces.CharacterWorkspaceId id, string section, CancellationToken token)
            => inner.ReadAsync(id, section, token);
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(
            Chummer.Contracts.Workspaces.CharacterWorkspaceId id, string section,
            WorkspaceCollectionMutationRequest request, CancellationToken token)
            => inner.ReadForMutationAsync(id, section, request, token);
    }

    private sealed class Files : IAndroidLinkedCharacterFileService
    {
        public readonly TaskCompletionSource<AndroidStagedLinkedCharacter?> Picker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly AndroidStagedLinkedCharacter Staged = new("/test-private/new-link.chum5", "linked-characters/new-link.chum5",
            "new-link.chum5", new("Linked runner", "Runner", "Alias", "Human", "", "", "")) { ContentSha256 = new('a', 64) };
        public readonly List<string?> Deleted = [];
        public int StageCalls;
        public Task<AndroidStagedLinkedCharacter?> StageAsync(WorkspaceCollectionItemTarget target, CancellationToken token)
        { StageCalls++; return Picker.Task; }
        public Task DeleteOwnedAsync(WorkspaceCollectionItemTarget target, string? path, CancellationToken token)
        { Deleted.Add(path); return Task.CompletedTask; }
        public Task<bool> MatchesStagedFileAsync(WorkspaceCollectionItemTarget target, string file, string hash, CancellationToken token)
            => Task.FromResult(file == Staged.FileName && hash == Staged.ContentSha256);
    }
    private sealed class Documents : IAndroidDocumentService
    {
        public static readonly byte[] Payload = Encoding.UTF8.GetBytes("<character><name>Test Runner</name><alias>Nightshade</alias><metatype>Human</metatype></character>");
        public readonly List<byte[]> Returned = [];
        public Task<AndroidDocument?> OpenAsync(CancellationToken cancellationToken)
        {
            byte[] bytes = Payload.ToArray();
            Returned.Add(bytes);
            return Task.FromResult<AndroidDocument?>(new("runner.chum5", "content://test/selected", "application/xml", bytes));
        }
        public Task<bool> SaveAsAsync(string name, string type, Stream content, CancellationToken token)
            => throw new InvalidOperationException("Staging must not export a runner.");
    }
}

// Deliberately logged-out outer account adapter for the real Home page. All
// network, linking, mutation and initialization calls remain forbidden.
public class LinkedRecoveryUnlinkedAccountProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => method?.Name switch
        {
            "get_Snapshot" => new AndroidAccountLinkSnapshot(AndroidAccountLinkStatus.Unlinked, "Unlinked"),
            "add_Changed" or "remove_Changed" => null,
            _ => throw new InvalidOperationException("Recovery must not invoke an account operation.")
        };
}
