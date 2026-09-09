using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

internal static partial class AfterRunAuthorityHarness
{
    private const string LinkedContactId = "11111111-1111-4111-8111-111111111111";
    private const string UntouchedContactId = "22222222-2222-4222-8222-222222222222";
    private static readonly WorkspaceCollectionItemTarget LinkedContactTarget =
        new(WorkspaceCollectionKind.Contact, LinkedContactId);

    public static async Task RunLinkedCharacterNativeRuntimeCasesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply an explicit Core directory containing data/.", nameof(contentRoot));
        Require(typeof(FileWorkspaceStore).Assembly != typeof(Program).Assembly
            && typeof(CharacterOverviewPresenter).Assembly != typeof(Program).Assembly
            && typeof(RunnerSessionCoordinator).Assembly != typeof(Program).Assembly
            && typeof(Chummer5LinkedDocumentCodec).Assembly != typeof(Program).Assembly,
            "Link integration requires actual Core, Presentation and native assemblies, not test substitutes.");
        await NativeLinkedAttachReplaceRemovePersistsAsync(contentRoot);
        await NativeLinkedPostCommitFailureRetainsFilesAsync(contentRoot);
        foreach (string drift in new[] { "missing-file", "later-revision", "deleted-target", "deleted-workspace" })
            await NativeLinkedUncertainDriftStaysPendingAsync(contentRoot, drift);
        foreach (string field in new[] { "name", "metatype", "gender", "age" })
            await NativeLinkedSubstitutedIdentityStaysPendingAsync(contentRoot, field);
        Console.WriteLine("PASS 10 actual linked-character/native/runtime/file-store cases: exact host intent/current-effect observations, shared paths, post-commit failure, conservative drift and identity substitution; no operation lookup, Android process-death or checkpoint proof");
        await NativeLinkedReaderOwnerBoundariesAsync(contentRoot);
        await NativeLinkedOwnerReadCancellationJoinsAccessorAsync(contentRoot);
    }

    private static async Task NativeLinkedOwnerReadCancellationJoinsAccessorAsync(string contentRoot)
    {
        await using var runtime = new LinkedOwnerRuntime(contentRoot);
        Require(runtime.Reader.IsAvailable
            && runtime.Reader.GetType().Assembly != typeof(Program).Assembly,
            "Owner scheduling checks must exercise the actual production linked-workspace reader.");
        foreach (OwnerScope owner in new[] { OwnerScope.LocalSingleUser, new OwnerScope("linked-async-owner") })
        {
            runtime.Owners.Set(owner);
            Task<AndroidLinkedOwner> read = runtime.Reader.ReadCurrentOwnerAsync(default);
            AndroidLinkedOwner observed;
            try { observed = await read.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { if (!read.IsCompleted) await read; }
            Require(observed == new AndroidLinkedOwner(owner.NormalizedValue, owner.IsLocalSingleUser)
                && runtime.Owners.ReadCount == 1,
                "The production asynchronous owner read changed the exact scope or trusted-local capability.");
        }

        runtime.Owners.Set(OwnerScope.LocalSingleUser);
        using (var precanceled = new CancellationTokenSource())
        {
            precanceled.Cancel();
            Task<AndroidLinkedOwner> read = runtime.Reader.ReadCurrentOwnerAsync(precanceled.Token);
            bool canceled = false;
            try { await read.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException error) when (error.CancellationToken == precanceled.Token)
            { canceled = true; }
            finally
            {
                if (!read.IsCompleted)
                    try { await read; }
                    catch (OperationCanceledException error) when (error.CancellationToken == precanceled.Token) { }
            }
            Require(canceled && read.IsCanceled && runtime.Owners.ReadCount == 0,
                $"A precanceled production owner read accessed the owner or returned usable authority: matchedCancellation={canceled}, status={read.Status}, accessorReads={runtime.Owners.ReadCount}.");
        }

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var exited = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var returned = new TaskCompletionSource<Task<AndroidLinkedOwner>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AndroidLinkedOwner>? pending = null;
        int callerThread = 0;
        int accessorThread = 0;
        bool canceledAfterRelease = false;
        runtime.Owners.BeforeRead = () =>
        {
            accessorThread = Environment.CurrentManagedThreadId;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The controlled production owner read was not released.");
        };
        runtime.Owners.AfterRead = () => exited.Set();
        var caller = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            try { returned.TrySetResult(runtime.Reader.ReadCurrentOwnerAsync(cancellation.Token)); }
            catch (Exception error) { returned.TrySetException(error); }
        }) { IsBackground = true };
        caller.Start();
        try
        {
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "The production reader did not enter its owner accessor.");
            pending = await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(accessorThread != callerThread && !pending.IsCompleted && !exited.IsSet,
                "The production reader blocked its action caller or returned before its owner read finished.");
            cancellation.Cancel();
            Task first = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMilliseconds(150)));
            Require(first != pending && !pending.IsCompleted && !exited.IsSet,
                "Cancellation detached the production owner read while its accessor was still blocked.");
        }
        finally
        {
            // Release and join even when an assertion/timeout fails. In particular,
            // never dispose the runtime or wait handles with an accessor still live.
            release.Set();
            try
            {
                pending ??= await returned.Task;
                try { await pending; }
                catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token)
                { canceledAfterRelease = true; }
            }
            finally
            {
                bool joined = caller.Join(TimeSpan.FromSeconds(5));
                bool accessorFinished = exited.Wait(TimeSpan.FromSeconds(5));
                runtime.Owners.BeforeRead = null;
                runtime.Owners.AfterRead = null;
                Require(joined, "The production owner-read action caller was not joined.");
                Require(accessorFinished, "The controlled production owner accessor was not joined.");
            }
        }
        Require(canceledAfterRelease && pending is { IsCanceled: true } && exited.IsSet && runtime.Owners.ReadCount == 1,
            "The production owner read did not report cancellation after joining exactly one completed accessor read.");
        Console.WriteLine("PASS actual linked-reader async owner read: exact scope, precancellation without access, responsive caller and joined cancellation; no owner lease or Android device proof");
    }

    private static async Task NativeLinkedReaderOwnerBoundariesAsync(string contentRoot)
    {
        await using var runtime = new LinkedOwnerRuntime(contentRoot);
        OwnerScope local = OwnerScope.LocalSingleUser;
        OwnerScope accountA = new("linked-fixture-account-a");
        OwnerScope accountB = new("linked-fixture-account-b");
        WorkspaceStoredDocument localDocument = await runtime.ImportAsync(local, "Local fixture");
        WorkspaceStoredDocument accountADocument = await runtime.ImportAsync(accountA, "Account A fixture");
        WorkspaceStoredDocument accountBDocument = await runtime.ImportAsync(accountB, "Account B fixture");

        // The exact Core scoped API deliberately rejects even the privileged
        // sentinel. The reader must take WorkspaceService's trusted-local route.
        Require(runtime.Store.Get(local, localDocument.Id).Outcome == WorkspaceOperationOutcome.Unavailable,
            "The local-boundary regression no longer exercises Core's reserved scoped-owner rejection.");
        runtime.Owners.Set(local);
        Require(runtime.Reader.IsAvailable && runtime.Reader.CurrentOwner == new AndroidLinkedOwner(local.NormalizedValue, true),
            "The actual runtime's privileged local sentinel was not retained.");
        await RequireOwnedLinkedSnapshotAsync(runtime, local, localDocument);
        var remove = new WorkspaceRemoveLinkedCharacterRequest(LinkedContactTarget);
        var preview = await runtime.Reader.ReadForMutationAsync(localDocument.Id, "contacts", remove, default);
        Require(preview is not null
            && preview.Owner == new AndroidLinkedOwner(local.NormalizedValue, true)
            && preview.DocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(localDocument.Document)
            && preview.ExpectedDocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(
                WorkspaceLinkedCharacterMutationPreview.Create(localDocument.Document, remove)),
            "Trusted local mutation preview did not use the exact real stored document and shared transformation.");
        RequireSameRewardDocument(localDocument, runtime.Store.Get(localDocument.Id).Value!);

        // An apparently identical serialized name has no privileged sentinel bit.
        // Even serializing the trusted value must not manufacture that capability.
        OwnerScope[] forgedOwners =
        [
            new(local.Value),
            new("  LOCAL-SINGLE-USER  "),
            JsonSerializer.Deserialize<OwnerScope>(JsonSerializer.Serialize(local))
        ];
        foreach (OwnerScope forged in forgedOwners)
        {
            Require(forged.UsesLocalSingleUserValue && !forged.IsLocalSingleUser,
                "The forged-local fixture unexpectedly acquired Core's private trusted sentinel.");
            runtime.Owners.Set(forged);
            Require(await runtime.Reader.ReadAsync(localDocument.Id, "contacts", default) is null
                && await runtime.Reader.ReadForMutationAsync(localDocument.Id, "contacts", remove, default) is null,
                "An untrusted reserved-local owner obtained a persisted observation or mutation preview.");
            bool ownerRejected = false;
            try { _ = runtime.Reader.CurrentOwner; }
            catch (InvalidOperationException) { ownerRejected = true; }
            Require(ownerRejected, "The reader advertised a forged reserved-local owner as usable authority.");
        }

        runtime.Owners.Set(accountA);
        await RequireOwnedLinkedSnapshotAsync(runtime, accountA, accountADocument);
        Require(runtime.Store.Get(accountA, accountBDocument.Id).Outcome == WorkspaceOperationOutcome.Missing
            && runtime.Store.Get(accountA, localDocument.Id).Outcome == WorkspaceOperationOutcome.Missing,
            "The isolation fixtures unexpectedly exist in account A's actual partition.");
        foreach (CharacterWorkspaceId foreign in new[] { accountBDocument.Id, localDocument.Id })
        {
            Require(await runtime.Reader.ReadAsync(foreign, "contacts", default) is null
                && await runtime.Reader.ReadForMutationAsync(foreign, "contacts", remove, default) is null,
                "Account A read a foreign workspace or previewed its mutation through a fallback owner.");
            Require(!(await runtime.Client.GetWorkspaceAsync(foreign, default)).Success,
                "Reader isolation no longer agrees with the actual in-process client's owner-bound read.");
        }

        // Same workspace ID, three real persisted documents: owner isolation must
        // come from Core's partition, not merely from unique imported IDs.
        CharacterWorkspaceId sharedId = new("linked-owner-shared-id");
        Require(runtime.Store.CreateWorkspaceDocument(sharedId, localDocument.Document).Success
            && runtime.Store.CreateWorkspaceDocument(accountA, sharedId, accountADocument.Document).Success
            && runtime.Store.CreateWorkspaceDocument(accountB, sharedId, accountBDocument.Document).Success,
            "Could not create the real same-ID owner-partition fixtures.");
        WorkspaceStoredDocument sharedLocal = runtime.Store.Get(sharedId).Value!;
        WorkspaceStoredDocument sharedA = runtime.Store.Get(accountA, sharedId).Value!;
        WorkspaceStoredDocument sharedB = runtime.Store.Get(accountB, sharedId).Value!;
        Require(new[] { sharedLocal, sharedA, sharedB }
                .Select(entry => RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(entry.Document)).Distinct().Count() == 3,
            "Same-ID owner fixtures do not have distinguishable canonical documents.");
        foreach (var partition in new[] { (Owner: local, Stored: sharedLocal), (Owner: accountA, Stored: sharedA), (Owner: accountB, Stored: sharedB) })
        {
            runtime.Owners.Set(partition.Owner);
            await RequireOwnedLinkedSnapshotAsync(runtime, partition.Owner, partition.Stored);
        }

        // Accessor reads occur at initial capture, after the first actual stored
        // read, after the second stored read, and after canonical projection.
        // Each injected change persists (A -> B); this intentionally proves no
        // owner-ABA detection and does not claim a lease over owner identity.
        foreach (int changeAt in new[] { 2, 3, 4 })
        {
            foreach (bool mutationPreview in new[] { false, true })
            {
                runtime.Owners.ChangeOnRead(accountA, accountB, changeAt);
                AndroidLinkedWorkspaceSnapshot? changed = mutationPreview
                    ? await runtime.Reader.ReadForMutationAsync(sharedId, "contacts", remove, default)
                    : await runtime.Reader.ReadAsync(sharedId, "contacts", default);
                Require(runtime.Owners.ChangeApplied && runtime.Owners.ReadCount == changeAt,
                    $"The controlled live-owner change was not observed at reader checkpoint {changeAt}.");
                Require(changed is null,
                    $"Reader returned an observation across a persistent owner change at checkpoint {changeAt}.");
            }
        }
        runtime.Owners.Set(accountB);
        await RequireOwnedLinkedSnapshotAsync(runtime, accountB, sharedB);

        // Cold store reconstruction, not Android process death. All observations
        // and failed previews above must leave every partition and revision alone.
        var cold = new FileWorkspaceStore(runtime.StateDirectory);
        foreach (var pair in new[]
        {
            (Before: localDocument, After: cold.Get(localDocument.Id).Value!),
            (Before: accountADocument, After: cold.Get(accountA, accountADocument.Id).Value!),
            (Before: accountBDocument, After: cold.Get(accountB, accountBDocument.Id).Value!),
            (Before: sharedLocal, After: cold.Get(sharedId).Value!),
            (Before: sharedA, After: cold.Get(accountA, sharedId).Value!),
            (Before: sharedB, After: cold.Get(accountB, sharedId).Value!)
        })
        {
            Require(pair.After is not null && pair.Before.Id == pair.After.Id
                && pair.Before.LastUpdatedUtc == pair.After.LastUpdatedUtc,
                "Owner-bound read checks changed or lost a real persisted workspace.");
            RequireSameRewardDocument(pair.Before, pair.After!);
        }
        Console.WriteLine("PASS 4 actual linked-reader owner-boundary groups: trusted local, forged local rejection, account partition isolation, and persistent owner changes; no owner-ABA or Android process-death proof");
    }

    private static async Task RequireOwnedLinkedSnapshotAsync(
        LinkedOwnerRuntime runtime, OwnerScope owner, WorkspaceStoredDocument stored)
    {
        var clientRead = await runtime.Client.GetWorkspaceAsync(stored.Id, default);
        Require(clientRead.Success && clientRead.Value is not null
            && RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(clientRead.Value.Document)
                == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(stored.Document),
            "The actual in-process client did not read this owner's canonical fixture.");
        AndroidLinkedWorkspaceSnapshot? snapshot = await runtime.Reader.ReadAsync(stored.Id, "contacts", default);
        Require(snapshot is not null && snapshot.Owner == new AndroidLinkedOwner(owner.NormalizedValue, owner.IsLocalSingleUser)
            && snapshot.WorkspaceId == stored.Id.Value
            && snapshot.ContentRevision == stored.ContentRevision && snapshot.SavedRevision == stored.SavedRevision
            && snapshot.DocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(stored.Document)
            && snapshot.Editor is { Items.Count: 2 } editor && snapshot.Contacts.Count == 2
            && editor.Items.Count(item => CollectionItemEditorPage.TargetsMatch(item.Target, LinkedContactTarget)) == 1,
            "Owner-bound reader did not return the exact real document and canonical typed contact projection.");
    }

    private static async Task NativeLinkedAttachReplaceRemovePersistsAsync(string contentRoot)
    {
        string appData = Directory.CreateTempSubdirectory("chummer-native-linked-runtime-").FullName;
        try
        {
            var documents = new LinkedRuntimeDocuments();
            var files = new AndroidLinkedCharacterFileService(documents, new Chummer5LinkedDocumentCodec(),
                () => appData, Guid.NewGuid);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedCharacters: files);
            await runtime.LoadRunnerAsync(LinkedRuntimeRunnerXml);
            await SelectLinkedContactsAsync(runtime);
            WorkspaceStoredDocument initial = ReadLinkedWorkspace(runtime);
            Require(!CurrentLinkedContact(runtime).LinkedCharacter!.IsLinked, "Initial contact unexpectedly has a link.");

            documents.Enqueue("first.chum5", LinkedRuntimeDocuments.FirstPayload);
            CharacterOverviewState expected = runtime.Coordinator.State;
            Require(await runtime.Coordinator.TryAttachBoundLinkedCharacterAsync(LinkedContactTarget, expected, () => true),
                $"Actual attach did not confirm its document successor: {runtime.Coordinator.Notice}; {runtime.Presenter.State.Error}");
            WorkspaceStoredDocument attached = ReadLinkedWorkspace(runtime);
            RequireLinkedRevisions(runtime, attached, contentRevision: 2, savedRevision: 1);
            await RequireLinkedIntentRecordAsync(runtime, initial, expected, attach: true, observed: true, count: 1);
            RequireOnlyLinkedAssociationChanged(initial, attached);
            WorkspaceLinkedCharacterState first = CurrentLinkedContact(runtime).LinkedCharacter!;
            RequireLinkedFile(first, "first.chum5", LinkedRuntimeDocuments.FirstPayload);
            Require(CurrentLinkedContact(runtime).Label == "Nightshade", "Canonical linked identity was not projected by Presentation.");
            await RequireLinkedReloadAsync(runtime, attached);

            // A real second workspace deliberately shares the first saved link path.
            // Neither replacing nor removing the first workspace's link may reclaim it.
            var duplicate = await runtime.Client.ImportAsync(new WorkspaceImportDocument(attached.Document.Content, "sr5"), default);
            Require(duplicate.Id != runtime.Id && (await runtime.Client.SaveAsync(duplicate.Id, default)).Success,
                "Could not create the independent workspace sharing the original linked path.");
            WorkspaceStoredDocument sharedBefore = ReadLinkedWorkspace(runtime, duplicate.Id);
            Require(ContactElement(sharedBefore, LinkedContactId).Element("file")?.Value == first.FileName,
                "The second workspace did not preserve the shared old path.");
            await RequireLinkedReloadAsync(runtime, attached);

            documents.Enqueue("replacement.chum5", LinkedRuntimeDocuments.SecondPayload);
            expected = runtime.Coordinator.State;
            Require(await runtime.Coordinator.TryAttachBoundLinkedCharacterAsync(LinkedContactTarget, expected, () => true),
                $"Actual replacement did not confirm its document successor: {runtime.Coordinator.Notice}; {runtime.Presenter.State.Error}");
            WorkspaceStoredDocument replaced = ReadLinkedWorkspace(runtime);
            RequireLinkedRevisions(runtime, replaced, contentRevision: 3, savedRevision: 1);
            await RequireLinkedIntentRecordAsync(runtime, attached, expected, attach: true, observed: true, count: 2);
            RequireOnlyLinkedAssociationChanged(initial, replaced);
            WorkspaceLinkedCharacterState second = CurrentLinkedContact(runtime).LinkedCharacter!;
            RequireLinkedFile(second, "replacement.chum5", LinkedRuntimeDocuments.SecondPayload);
            Require(first.FileName != second.FileName && first.RelativeFileName != second.RelativeFileName,
                "Replacement reused another invocation's staged file.");
            RequireLinkedFile(first, "first.chum5", LinkedRuntimeDocuments.FirstPayload);
            RequireSameRewardDocument(sharedBefore, ReadLinkedWorkspace(runtime, duplicate.Id));
            await RequireLinkedReloadAsync(runtime, replaced);

            expected = runtime.Coordinator.State;
            Require(await runtime.Coordinator.TryRemoveBoundLinkedCharacterAsync(LinkedContactTarget, expected, () => true),
                $"Actual removal did not confirm its document successor: {runtime.Coordinator.Notice}; {runtime.Presenter.State.Error}");
            WorkspaceStoredDocument removed = ReadLinkedWorkspace(runtime);
            RequireLinkedRevisions(runtime, removed, contentRevision: 4, savedRevision: 1);
            await RequireLinkedIntentRecordAsync(runtime, replaced, expected, attach: false, observed: true, count: 3);
            RequireOnlyLinkedAssociationChanged(initial, removed);
            Require(CurrentLinkedContact(runtime) is { Label: "Original contact", LinkedCharacter: { IsLinked: false } },
                "Removal did not restore the original contact identity.");
            XElement removedContact = ContactElement(removed, LinkedContactId);
            Require(removedContact.Element("file")?.Value == string.Empty
                && removedContact.Element("relative")?.Value == string.Empty
                && removedContact.Element("chummercomplete")?.Element("linkedcharacter") is null,
                "Removal retained a persisted linked association.");
            RequireLinkedFile(first, "first.chum5", LinkedRuntimeDocuments.FirstPayload);
            RequireLinkedFile(second, "replacement.chum5", LinkedRuntimeDocuments.SecondPayload);
            RequireSameRewardDocument(sharedBefore, ReadLinkedWorkspace(runtime, duplicate.Id));
            await RequireLinkedReloadAsync(runtime, removed);
            Require(documents.OpenCount == 2, "Attach/replace/remove replayed a document picker.");
            Require(documents.Returned.All(bytes => bytes.All(value => value == 0)), "Staging retained selected buffers.");
            Require(Directory.EnumerateFiles(Path.Combine(appData, "linked-characters")).Count() == 2,
                "The normal flow created unexpected staged or temporary files.");
        }
        finally { Directory.Delete(appData, recursive: true); } // Only this test's freshly-created private directory.
    }

    private static async Task NativeLinkedPostCommitFailureRetainsFilesAsync(string contentRoot)
    {
        string appData = Directory.CreateTempSubdirectory("chummer-native-linked-unknown-").FullName;
        try
        {
            var documents = new LinkedRuntimeDocuments();
            var files = new AndroidLinkedCharacterFileService(documents, new Chummer5LinkedDocumentCodec(),
                () => appData, Guid.NewGuid);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedCharacters: files);
            await runtime.LoadRunnerAsync(LinkedRuntimeRunnerXml);
            await SelectLinkedContactsAsync(runtime);
            WorkspaceStoredDocument initial = ReadLinkedWorkspace(runtime);
            documents.Enqueue("first.chum5", LinkedRuntimeDocuments.FirstPayload);

            CharacterOverviewState attachExpected = runtime.Coordinator.State;
            await RequireLinkedShellFailureAsync(runtime, attach: true);
            WorkspaceStoredDocument attached = ReadLinkedWorkspace(runtime);
            Require(attached.ContentRevision == 2 && attached.SavedRevision == 1,
                "The controlled shell failure did not follow one real document commit.");
            RequireOnlyLinkedAssociationChanged(initial, attached);
            AndroidLinkedCharacterIntentRecord pendingAttach = await RequireLinkedIntentRecordAsync(
                runtime, initial, attachExpected, attach: true, observed: false, count: 1);
            string committedPath = ContactElement(attached, LinkedContactId).Element("file")?.Value
                ?? throw new InvalidOperationException("The real commit has no persisted linked path.");
            Require(File.Exists(committedPath) && File.ReadAllBytes(committedPath).SequenceEqual(LinkedRuntimeDocuments.FirstPayload),
                "An uncertain post-commit attach deleted its referenced file.");
            await RequireLinkedReloadAsync(runtime, attached);
            RequireLinkedFile(CurrentLinkedContact(runtime).LinkedCharacter!, "first.chum5", LinkedRuntimeDocuments.FirstPayload);

            await RequireNewLinkedCommandsBlockedAsync(runtime, documents, pendingAttach);
            await RequireReadOnlyLinkedResolutionAsync(runtime, documents, pendingAttach);

            CharacterOverviewState removeExpected = runtime.Coordinator.State;
            await RequireLinkedShellFailureAsync(runtime, attach: false);
            WorkspaceStoredDocument removed = ReadLinkedWorkspace(runtime);
            Require(removed.ContentRevision == 3 && removed.SavedRevision == 1
                && ContactElement(removed, LinkedContactId).Element("file")?.Value == string.Empty,
                "The controlled remove failure did not follow one real removal commit.");
            RequireOnlyLinkedAssociationChanged(initial, removed);
            AndroidLinkedCharacterIntentRecord pendingRemove = await RequireLinkedIntentRecordAsync(
                runtime, attached, removeExpected, attach: false, observed: false, count: 2);
            Require(File.Exists(committedPath) && File.ReadAllBytes(committedPath).SequenceEqual(LinkedRuntimeDocuments.FirstPayload),
                "An uncertain post-commit removal reclaimed a previously referenced file.");
            await RequireLinkedReloadAsync(runtime, removed);
            int selectionsBeforeRemoveRecovery = documents.OpenCount;
            await RequireReadOnlyLinkedResolutionAsync(runtime, documents, pendingRemove);
            Require(!CurrentLinkedContact(runtime).LinkedCharacter!.IsLinked
                && documents.OpenCount == selectionsBeforeRemoveRecovery,
                "Read-only presenter/store reopen replayed a link operation.");
        }
        finally { Directory.Delete(appData, recursive: true); } // No shared app-data or workspace directory is used.
    }

    private static async Task RequireLinkedShellFailureAsync(NativeRewardRuntime runtime, bool attach)
    {
        CharacterOverviewState expected = runtime.Coordinator.State;
        int previousRejections = runtime.Settings.RejectedWrites;
        bool failed = false;
        runtime.Settings.FailSelectedWorkspaceWrite = true;
        try
        {
            if (attach)
                await runtime.Coordinator.TryAttachBoundLinkedCharacterAsync(LinkedContactTarget, expected, () => true);
            else
                await runtime.Coordinator.TryRemoveBoundLinkedCharacterAsync(LinkedContactTarget, expected, () => true);
        }
        catch (IOException) { failed = true; }
        finally { runtime.Settings.FailSelectedWorkspaceWrite = false; }
        Require(failed && runtime.Settings.RejectedWrites == previousRejections + 1,
            "Link operation did not reach the real post-commit native shell preference-write failure.");
    }

    private static async Task NativeLinkedUncertainDriftStaysPendingAsync(string contentRoot, string drift)
    {
        string appData = Directory.CreateTempSubdirectory("chummer-native-linked-drift-").FullName;
        try
        {
            var documents = new LinkedRuntimeDocuments();
            var files = new AndroidLinkedCharacterFileService(documents, new Chummer5LinkedDocumentCodec(), () => appData, Guid.NewGuid);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedCharacters: files);
            await runtime.LoadRunnerAsync(LinkedRuntimeRunnerXml);
            await SelectLinkedContactsAsync(runtime);
            WorkspaceStoredDocument before = ReadLinkedWorkspace(runtime);
            CharacterOverviewState beforeUi = runtime.Coordinator.State;
            documents.Enqueue("uncertain.chum5", LinkedRuntimeDocuments.FirstPayload);
            await RequireLinkedShellFailureAsync(runtime, attach: true);
            var pending = await RequireLinkedIntentRecordAsync(runtime, before, beforeUi, attach: true, observed: false, count: 1);
            WorkspaceStoredDocument attached = ReadLinkedWorkspace(runtime);
            string stagedPath = pending.Intent.StagedFileName!;
            await RequireLinkedReloadAsync(runtime, attached);

            switch (drift)
            {
                case "missing-file":
                    // Remove availability without destroying bytes; only this synthetic fixture owns both paths.
                    File.Move(stagedPath, Path.Combine(appData, "retained-but-unavailable.chum5"));
                    break;
                case "later-revision":
                    // A real owner CAS, even of unchanged bytes, advances beyond the exact original successor.
                    Require((await runtime.Client.ReplaceWorkspaceDocumentAsync(runtime.Id, attached.ContentRevision,
                        attached.Document, default)).Success, "Could not establish a real later workspace revision.");
                    break;
                case "deleted-target":
                    await runtime.Presenter.ApplyCollectionMutationAsync(new WorkspaceDeleteCollectionItemRequest(LinkedContactTarget), default);
                    Require(!XDocument.Parse(ReadLinkedWorkspace(runtime).Document.Content).Descendants("contact")
                        .Any(contact => contact.Element("guid")?.Value == LinkedContactId), "Actual Core target deletion did not occur.");
                    break;
                case "deleted-workspace":
                    // The real ordinary deletion route requires a clean runner.
                    // Establish that checkpoint explicitly; never bypass its
                    // dirty-document guard or fake a missing store projection.
                    Require((await runtime.Client.SaveAsync(runtime.Id, default)).Success,
                        "The synthetic runner could not be checkpointed before actual deletion.");
                    await RequireLinkedReloadAsync(runtime, ReadLinkedWorkspace(runtime));
                    await runtime.Presenter.DeleteWorkspaceAsync(runtime.Id, confirmed: true, ct: default);
                    Require(new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Outcome == WorkspaceOperationOutcome.Missing,
                        "Actual workspace deletion did not remove the synthetic runner.");
                    break;
            }

            WorkspaceStoreReadResult afterDrift = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id);
            int selections = documents.OpenCount;
            string history = JsonSerializer.Serialize(await ReadExactLinkedHistoryAsync(runtime));
            Require(!await runtime.Coordinator.CheckLinkedCharacterIntentAsync(pending.Intent.OperationId),
                $"{drift} falsely resolved the original pending link intent.");
            Require(JsonSerializer.Serialize(await ReadExactLinkedHistoryAsync(runtime)) == history
                && documents.OpenCount == selections, $"{drift} rewrote journal history or replayed the picker.");
            WorkspaceStoreReadResult afterCheck = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id);
            Require(afterCheck.Outcome == afterDrift.Outcome, $"{drift} recovery recreated or removed a workspace.");
            if (afterDrift.Value is { } stillStored)
                RequireSameRewardDocument(stillStored, afterCheck.Value!);
            if (drift != "missing-file")
                Require(File.Exists(stagedPath) && File.ReadAllBytes(stagedPath).SequenceEqual(LinkedRuntimeDocuments.FirstPayload),
                    $"{drift} recovery reclaimed the retained staged file.");
        }
        finally { Directory.Delete(appData, recursive: true); }
    }

    private static async Task NativeLinkedSubstitutedIdentityStaysPendingAsync(string contentRoot, string field)
    {
        string appData = Directory.CreateTempSubdirectory("chummer-native-linked-identity-").FullName;
        try
        {
            var documents = new LinkedRuntimeDocuments();
            var files = new AndroidLinkedCharacterFileService(documents, new Chummer5LinkedDocumentCodec(), () => appData, Guid.NewGuid);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedCharacters: files);
            await runtime.LoadRunnerAsync(LinkedRuntimeRunnerXml);
            await SelectLinkedContactsAsync(runtime);
            CharacterOverviewState beforeUi = runtime.Coordinator.State;
            WorkspaceStoredDocument before = ReadLinkedWorkspace(runtime);
            AndroidLinkedWorkspaceSnapshot snapshot = await RequireLinkedReaderMatchesPresenterAsync(runtime);
            documents.Enqueue("reviewed.chum5", LinkedRuntimeDocuments.FirstPayload);
            AndroidStagedLinkedCharacter staged = await files.StageAsync(LinkedContactTarget, default)
                ?? throw new InvalidOperationException("Canonical staged fixture is unavailable.");
            var reviewed = new WorkspaceSetLinkedCharacterRequest(LinkedContactTarget,
                staged.FileName, staged.RelativeFileName, staged.DisplayName, staged.Identity);
            var preview = await LinkedReader(runtime).ReadForMutationAsync(runtime.Id, "contacts", reviewed, default)
                ?? throw new InvalidOperationException("Canonical exact successor preview is unavailable.");
            Require(preview.DocumentAuthoritySha256 == snapshot.DocumentAuthoritySha256
                && preview.ExpectedDocumentAuthoritySha256 is { Length: 64 },
                "Canonical preview does not bind the original immutable baseline.");
            // Real host custody precedes one real owner mutation with a substituted field.
            // This creates no Core receipt and simulates no projection: the reader must
            // distinguish two different commands producing the same R+1/S and file paths.
            var intent = new AndroidLinkedCharacterIntent(Guid.NewGuid(), snapshot.Owner.Scope, snapshot.Owner.TrustedLocal,
                runtime.Id.Value, snapshot.ContentRevision, snapshot.SavedRevision, snapshot.DocumentAuthoritySha256,
                preview.ExpectedDocumentAuthoritySha256!,
                RunnerSessionCoordinator.LinkedEditorAuthority(beforeUi, CurrentLinkedContact(runtime)), "contacts", LinkedContactTarget,
                true, Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(reviewed, reviewed.GetType()))),
                staged.FileName, staged.ContentSha256, reviewed);
            runtime.LinkedJournal.Begin(intent);
            var substituted = reviewed with
            {
                Identity = field switch
                {
                    "name" => reviewed.Identity with { CharacterName = "Substituted runner identity" },
                    "metatype" => reviewed.Identity with { Metatype = "Ork", Metavariant = "" },
                    "gender" => reviewed.Identity with { Gender = "Substituted gender" },
                    _ => reviewed.Identity with { Age = "99" }
                }
            };
            await runtime.Presenter.ApplyCollectionMutationAsync(substituted, default);
            WorkspaceStoredDocument changed = ReadLinkedWorkspace(runtime);
            Require(changed.ContentRevision == before.ContentRevision + 1 && changed.SavedRevision == before.SavedRevision,
                "Identity substitution did not use one real canonical document-replacement boundary.");
            XElement identity = ContactElement(changed, LinkedContactId).Element("chummercomplete")!.Element("linkedcharacter")!;
            string expectedChanged = field switch
            {
                "name" => substituted.Identity.CharacterName,
                "metatype" => substituted.Identity.DisplayMetatype,
                "gender" => substituted.Identity.Gender,
                _ => substituted.Identity.Age
            };
            Require(identity.Element(field)?.Value == expectedChanged
                && ContactElement(changed, LinkedContactId).Element("file")?.Value == staged.FileName,
                "Canonical persisted identity substitution did not preserve the original staged path.");
            var pending = await RequireLinkedIntentRecordAsync(runtime, before, beforeUi, attach: true, observed: false, count: 1);
            Require(!await runtime.Coordinator.CheckLinkedCharacterIntentAsync(pending.Intent.OperationId),
                $"A substituted persisted {field} field passed path-only link recovery at the exact successor revision.");
            RequireSameRewardDocument(changed, ReadLinkedWorkspace(runtime));
            Require((await ReadExactLinkedHistoryAsync(runtime)).Single().Observation is null
                && documents.OpenCount == 1 && File.ReadAllBytes(staged.FileName).SequenceEqual(LinkedRuntimeDocuments.FirstPayload),
                "Identity-mismatched recovery resolved intent, replayed a picker, or changed retained staged bytes.");
        }
        finally { Directory.Delete(appData, recursive: true); }
    }

    private static async Task SelectLinkedContactsAsync(NativeRewardRuntime runtime)
    {
        await runtime.Coordinator.SelectTabAsync("tab-contacts");
        Require(runtime.Coordinator.State.WorkspaceId == runtime.Id && runtime.Coordinator.State.ActiveSectionId == "contacts"
            && runtime.Presenter.State.Error is null && runtime.Coordinator.State.ActiveCollectionEditor?.Items.Count == 2,
            $"Actual contacts projection is unavailable: {runtime.Presenter.State.Error}");
        Require(CurrentLinkedContact(runtime).LinkedCharacter is { CanAttach: true },
            "Canonical contact link capability is missing; this test cannot substitute an editor projection.");
        await RequireLinkedReaderMatchesPresenterAsync(runtime);
    }

    private static IAndroidLinkedWorkspaceReader LinkedReader(NativeRewardRuntime runtime)
        => typeof(RunnerSessionCoordinator).GetField("_linkedWorkspaceReader", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(runtime.Coordinator) as IAndroidLinkedWorkspaceReader
            ?? throw new InvalidOperationException("The real coordinator has no production linked-workspace reader.");

    private static async Task<AndroidLinkedWorkspaceSnapshot> RequireLinkedReaderMatchesPresenterAsync(NativeRewardRuntime runtime)
    {
        IAndroidLinkedWorkspaceReader reader = LinkedReader(runtime);
        CharacterOverviewState expected = runtime.Coordinator.State;
        WorkspaceStoredDocument stored = ReadLinkedWorkspace(runtime);
        Require(reader.IsAvailable, "Synthetic linked fixture has no available actual in-process/file-store read capability.");
        AndroidLinkedOwner owner = await Task.Run(() => reader.CurrentOwner);
        var codecs = (IRulesetWorkspaceCodecResolver)reader.GetType()
            .GetField("_codecs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
        object canonical = codecs.Resolve(stored.Document.RulesetId).ParseSection("contacts", stored.Document.PayloadEnvelope);
        Require(canonical is Chummer.Contracts.Characters.CharacterContactsSection,
            "Actual linked codec section type: " + canonical.GetType().FullName);
        Require(stored.Document.Format == WorkspaceDocumentFormat.NativeXml && stored.Document.SchemaVersion > 0,
            "Actual linked fixture envelope is not an identified native document.");
        _ = RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(stored.Document);
        var readerStore = (IWorkspaceStore)reader.GetType().GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
        var ownerAccessor = (Chummer.Application.Owners.IOwnerContextAccessor)reader.GetType()
            .GetField("_owners", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
        var liveScope = ownerAccessor.Current;
        var firstRead = liveScope.IsLocalSingleUser ? readerStore.Get(runtime.Id) : readerStore.Get(liveScope, runtime.Id);
        var secondRead = liveScope.IsLocalSingleUser ? readerStore.Get(runtime.Id) : readerStore.Get(liveScope, runtime.Id);
        Require(firstRead.Success && secondRead.Success, $"Actual scoped read status {firstRead.Outcome}/{secondRead.Outcome}");
        Require(firstRead.Value!.LastUpdatedUtc == secondRead.Value!.LastUpdatedUtc
            && RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(firstRead.Value.Document)
                == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(secondRead.Value.Document),
            "Actual scoped double read changed metadata or digest.");
        _ = WorkspaceCollectionEditorProjector.TryProject("contacts",
            JsonSerializer.SerializeToNode(canonical, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        AndroidLinkedWorkspaceSnapshot? snapshot = await reader.ReadAsync(runtime.Id, "contacts", default);
        string frame = $"fixture UI={expected.ContentRevision}/{expected.SavedRevision}, stored={stored.ContentRevision}/{stored.SavedRevision}, "
            + $"reader={(snapshot is null ? "null" : $"{snapshot.ContentRevision}/{snapshot.SavedRevision}")}, "
            + $"trustedLocal={owner.TrustedLocal}, ownerScopeSha={LinkedSha(owner.Scope)}";
        Require(snapshot is not null, "Canonical linked-reader preflight returned no snapshot; " + frame);
        Require(snapshot!.Owner == owner && snapshot.WorkspaceId == runtime.Id.Value
            && snapshot.ContentRevision == expected.ContentRevision && snapshot.SavedRevision == expected.SavedRevision
            && snapshot.DocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(stored.Document),
            "Canonical linked-reader preflight owner/document/revision mismatch; " + frame);
        string expectedJson = JsonSerializer.Serialize(expected.ActiveCollectionEditor);
        string actualJson = JsonSerializer.Serialize(snapshot.Editor);
        Require(expectedJson == actualJson,
            "Canonical linked-reader preflight editor mismatch; " + frame + "; "
            + FirstLinkedJsonDifference(JsonNode.Parse(expectedJson), JsonNode.Parse(actualJson), "$"));
        Require(snapshot.Contacts.Count == 2 && snapshot.Contacts.Count(contact => contact.Guid == LinkedContactId) == 1,
            "Canonical reader omitted or duplicated the synthetic target's typed contact identity.");
        return snapshot;
    }

    private static string FirstLinkedJsonDifference(JsonNode? expected, JsonNode? actual, string path)
    {
        if (JsonNode.DeepEquals(expected, actual)) return path + ": equal JSON values; serialized ordering differs";
        if (expected is JsonObject left && actual is JsonObject right)
        {
            foreach (string key in left.Select(property => property.Key).Concat(right.Select(property => property.Key)).Distinct())
            {
                if (!left.ContainsKey(key) || !right.ContainsKey(key)) return path + "." + key + ": property missing";
                if (!JsonNode.DeepEquals(left[key], right[key])) return FirstLinkedJsonDifference(left[key], right[key], path + "." + key);
            }
        }
        if (expected is JsonArray leftArray && actual is JsonArray rightArray)
        {
            if (leftArray.Count != rightArray.Count) return path + $": array counts {leftArray.Count}/{rightArray.Count}";
            for (int index = 0; index < leftArray.Count; index++)
                if (!JsonNode.DeepEquals(leftArray[index], rightArray[index]))
                    return FirstLinkedJsonDifference(leftArray[index], rightArray[index], path + $"[{index}]");
        }
        // Only synthetic-fixture metadata is emitted; never dump runner XML, names or private paths.
        return path + ": expected=" + LinkedJsonShape(expected) + ", actual=" + LinkedJsonShape(actual);
    }

    private static string LinkedJsonShape(JsonNode? node)
        => node is null ? "null" : $"{node.GetValueKind()}(sha256={LinkedSha(node.ToJsonString())})";

    private static string LinkedSha(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<IReadOnlyList<AndroidLinkedCharacterIntentRecord>> ReadExactLinkedHistoryAsync(NativeRewardRuntime runtime)
    {
        AndroidLinkedOwner owner = await Task.Run(() => LinkedReader(runtime).CurrentOwner);
        var live = runtime.LinkedJournal.Read(owner.Scope, owner.TrustedLocal, runtime.Id.Value);
        var cold = new AndroidLinkedCharacterIntentJournal(runtime.StateDirectory).Read(owner.Scope, owner.TrustedLocal, runtime.Id.Value);
        Require(JsonSerializer.Serialize(live) == JsonSerializer.Serialize(cold),
            "Link intent/observation bytes differ after constructing a fresh journal instance.");
        Require(live.All(record => record.Intent.OwnerScope == owner.Scope
            && record.Intent.TrustedLocalOwner == owner.TrustedLocal && record.Intent.WorkspaceId == runtime.Id.Value),
            "Link history returned an unrelated owner or workspace.");
        Require(runtime.LinkedJournal.Read(owner.Scope + ":unrelated", owner.TrustedLocal, runtime.Id.Value).Count == 0
            && runtime.LinkedJournal.Read(owner.Scope, owner.TrustedLocal, "unrelated-fixture-workspace").Count == 0,
            "Link journal scope filtering exposed another owner/workspace's operation.");
        return live;
    }

    private static async Task<AndroidLinkedCharacterIntentRecord> RequireLinkedIntentRecordAsync(NativeRewardRuntime runtime,
        WorkspaceStoredDocument before, CharacterOverviewState beforeUi, bool attach, bool observed, int count)
    {
        var records = await ReadExactLinkedHistoryAsync(runtime);
        Require(records.Count == count, "A real link operation duplicated or omitted its retained host intent.");
        var record = records.Single(entry => entry.Intent.ContentRevision == before.ContentRevision);
        var intent = record.Intent;
        var item = beforeUi.ActiveCollectionEditor!.Items.Single(entry => CollectionItemEditorPage.TargetsMatch(entry.Target, LinkedContactTarget));
        Require(intent.OperationId != Guid.Empty && intent.Target == LinkedContactTarget && intent.SectionId == "contacts"
            && intent.Attach == attach && intent.SavedRevision == before.SavedRevision
            && intent.DocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(before.Document)
            && intent.EditorAuthoritySha256 == RunnerSessionCoordinator.LinkedEditorAuthority(beforeUi, item)
            && !record.NotDispatched && record.EffectObserved == observed,
            "Real link intent did not retain its exact owner/workspace/target/revision/document/editor tuple.");
        WorkspaceCollectionMutationRequest request = attach ? intent.Attachment! : new WorkspaceRemoveLinkedCharacterRequest(intent.Target);
        Require(request is not null && intent.RequestSha256 == Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(request, request.GetType()))), "Persisted typed link request digest differs.");
        if (attach)
            Require(intent.Attachment is not null && intent.StagedFileName == intent.Attachment.FileName
                && intent.StagedFileSha256 == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(intent.StagedFileName!))),
                "The retained link intent does not bind its actual exclusive staged bytes.");
        if (observed)
        {
            WorkspaceStoredDocument after = ReadLinkedWorkspace(runtime);
            Require(record.Observation is { Outcome: "current-effect-observed" } effect
                && effect.OperationId == intent.OperationId && effect.ContentRevision == before.ContentRevision + 1
                && effect.SavedRevision == before.SavedRevision
                && effect.DocumentAuthoritySha256 == intent.ExpectedDocumentAuthoritySha256
                && effect.DocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(after.Document),
                "Link completion lacks an exact current-effect observation or falsely changed checkpoint revisions.");
        }
        else Require(record.Observation is null, "An uncertain shell failure already acquired a terminal host observation.");
        return record;
    }

    private static async Task RequireNewLinkedCommandsBlockedAsync(NativeRewardRuntime runtime,
        LinkedRuntimeDocuments documents, AndroidLinkedCharacterIntentRecord pending)
    {
        WorkspaceStoredDocument before = ReadLinkedWorkspace(runtime);
        string history = JsonSerializer.Serialize(await ReadExactLinkedHistoryAsync(runtime));
        foreach (bool attach in new[] { true, false })
        {
            if (attach) documents.Enqueue("explicit-but-blocked.chum5", LinkedRuntimeDocuments.FirstPayload);
            bool refused = false;
            try
            {
                bool accepted = attach
                    ? await runtime.Coordinator.TryAttachBoundLinkedCharacterAsync(LinkedContactTarget, runtime.Coordinator.State, () => true)
                    : await runtime.Coordinator.TryRemoveBoundLinkedCharacterAsync(LinkedContactTarget, runtime.Coordinator.State, () => true);
                refused = !accepted;
            }
            catch (IOException error) when (error.Message.Contains("unresolved link intent", StringComparison.Ordinal)) { refused = true; }
            Require(refused, "A new link command bypassed the original pending operation.");
            RequireSameRewardDocument(before, ReadLinkedWorkspace(runtime));
            Require(JsonSerializer.Serialize(await ReadExactLinkedHistoryAsync(runtime)) == history,
                "A blocked new command changed or replaced the pending host intent.");
        }
        Require((await ReadExactLinkedHistoryAsync(runtime)).Single(record => record.Intent.OperationId == pending.Intent.OperationId)
            .Observation is null, "Blocking another command silently resolved the original operation.");
    }

    private static async Task RequireReadOnlyLinkedResolutionAsync(NativeRewardRuntime runtime,
        LinkedRuntimeDocuments documents, AndroidLinkedCharacterIntentRecord pending)
    {
        WorkspaceStoredDocument before = ReadLinkedWorkspace(runtime);
        int selections = documents.OpenCount;
        Require(await runtime.Coordinator.CheckLinkedCharacterIntentAsync(pending.Intent.OperationId),
            "Read-only canonical saved-state inspection did not observe the original pending intent's exact effect.");
        RequireSameRewardDocument(before, ReadLinkedWorkspace(runtime));
        var observed = (await ReadExactLinkedHistoryAsync(runtime)).Single(record => record.Intent.OperationId == pending.Intent.OperationId);
        Require(observed.EffectObserved && observed.Observation is { Outcome: "current-effect-observed" } effect
            && effect.ContentRevision == before.ContentRevision && effect.SavedRevision == before.SavedRevision
            && effect.DocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(before.Document)
            && documents.OpenCount == selections,
            "Recovery replayed a mutation/picker or reported anything other than the actual current-effect observation.");
        Require(await runtime.Coordinator.CheckLinkedCharacterIntentAsync(pending.Intent.OperationId),
            "Recorded host observation could not be revisited without replay.");
        RequireSameRewardDocument(before, ReadLinkedWorkspace(runtime));
        Require(documents.OpenCount == selections, "Revisiting host history reopened a document picker.");
    }

    private static WorkspaceCollectionItemEditorState CurrentLinkedContact(NativeRewardRuntime runtime)
        => runtime.Coordinator.State.ActiveCollectionEditor?.Items.Single(item =>
            CollectionItemEditorPage.TargetsMatch(item.Target, LinkedContactTarget))
            ?? throw new InvalidOperationException("The actual presenter has no unique linked contact target.");

    private static WorkspaceStoredDocument ReadLinkedWorkspace(NativeRewardRuntime runtime, CharacterWorkspaceId? id = null)
    {
        // Reconstructing FileWorkspaceStore proves persisted document readback,
        // not Android process death, external-file checkpointing or a new receipt.
        WorkspaceStoreReadResult read = new FileWorkspaceStore(runtime.StateDirectory).Get(id ?? runtime.Id);
        Require(read.Success && read.Value is not null, "Fresh FileWorkspaceStore could not read the linked workspace.");
        return read.Value!;
    }

    private static void RequireLinkedRevisions(NativeRewardRuntime runtime, WorkspaceStoredDocument stored,
        long contentRevision, long savedRevision)
        => Require(stored.ContentRevision == contentRevision && stored.SavedRevision == savedRevision
            && runtime.Presenter.State.ContentRevision == contentRevision && runtime.Presenter.State.SavedRevision == savedRevision,
            $"Link replacement revision mismatch: stored {stored.ContentRevision}/{stored.SavedRevision}, presenter "
            + $"{runtime.Presenter.State.ContentRevision}/{runtime.Presenter.State.SavedRevision}; expected {contentRevision}/{savedRevision}.");

    private static async Task RequireLinkedReloadAsync(NativeRewardRuntime runtime, WorkspaceStoredDocument expected)
    {
        await runtime.Presenter.LoadAsync(runtime.Id, default);
        await SelectLinkedContactsAsync(runtime);
        RequireLinkedRevisions(runtime, ReadLinkedWorkspace(runtime), expected.ContentRevision, expected.SavedRevision);
        RequireSameRewardDocument(expected, ReadLinkedWorkspace(runtime));
        XElement contact = ContactElement(expected, LinkedContactId);
        WorkspaceLinkedCharacterState linked = CurrentLinkedContact(runtime).LinkedCharacter!;
        Require(linked.FileName == (contact.Element("file")?.Value ?? string.Empty)
            && linked.RelativeFileName == (contact.Element("relative")?.Value ?? string.Empty),
            "Presenter reload disagrees with the fresh persisted linked paths.");
    }

    private static void RequireLinkedFile(WorkspaceLinkedCharacterState linked, string displayName, byte[] bytes)
        => Require(linked.IsLinked && linked.IdentityResolved && linked.DisplayName == displayName
            && Path.IsPathFullyQualified(linked.FileName)
            && linked.RelativeFileName == "linked-characters/" + Path.GetFileName(linked.FileName)
            && File.Exists(linked.FileName) && File.ReadAllBytes(linked.FileName).SequenceEqual(bytes),
            "Actual linked projection, staged bytes or path identity differs from the selected document.");

    private static XElement ContactElement(WorkspaceStoredDocument document, string id)
        => XDocument.Parse(document.Document.Content).Root!.Element("contacts")!.Elements("contact")
            .Single(contact => contact.Element("guid")?.Value == id);

    private static void RequireOnlyLinkedAssociationChanged(WorkspaceStoredDocument before, WorkspaceStoredDocument after)
    {
        Require(before.Document.AuxiliaryStateDigest == after.Document.AuxiliaryStateDigest
            && XNode.DeepEquals(ContactElement(before, UntouchedContactId), ContactElement(after, UntouchedContactId)),
            "Link mutation changed auxiliary state or the second contact.");
        XElement original = XDocument.Parse(before.Document.Content).Root!;
        XElement changed = XDocument.Parse(after.Document.Content).Root!;
        foreach (XElement root in new[] { original, changed })
        {
            // Comparison only: remove precisely the association fields from each
            // readback, never synthesize or submit a replacement document.
            XElement contact = root.Element("contacts")!.Elements("contact")
                .Single(item => item.Element("guid")?.Value == LinkedContactId);
            contact.Elements("file").Remove();
            contact.Elements("relative").Remove();
            XElement? extension = contact.Element("chummercomplete");
            extension?.Elements("linkedcharacter").Remove();
            if (extension is { HasElements: false }) extension.Remove();
        }
        Require(XNode.DeepEquals(original, changed), "Link mutation changed saved identity or unrelated runner data.");
    }

    private sealed class LinkedOwnerRuntime : IAsyncDisposable
    {
        private readonly Dictionary<string, string?> _environment = new();
        private readonly ServiceProvider _provider;
        public string StateDirectory { get; }
        public ControlledLinkedOwner Owners { get; } = new();
        public FileWorkspaceStore Store { get; }
        public IChummerClient Client { get; }
        public AndroidLinkedWorkspaceReader Reader { get; }

        public LinkedOwnerRuntime(string contentRoot)
        {
            StateDirectory = Directory.CreateTempSubdirectory("chummer-native-linked-owner-").FullName;
            try
            {
                SetEnvironment("CHUMMER_STATE_PATH", StateDirectory);
                SetEnvironment("CHUMMER_WORKSPACE_STORE_PATH", StateDirectory);
                SetEnvironment("CHUMMER_CLIENT_MODE", "local");
                SetEnvironment("CHUMMER_DESKTOP_CLIENT_MODE", "local");
                var services = new ServiceCollection();
                services.AddChummerLocalRuntimeClient(contentRoot, contentRoot);
                Store = new FileWorkspaceStore(StateDirectory);
                services.Replace(ServiceDescriptor.Singleton<IWorkspaceStore>(Store));
                services.Replace(ServiceDescriptor.Singleton<IOwnerContextAccessor>(Owners));
                _provider = services.BuildServiceProvider();
                Client = _provider.GetRequiredService<IChummerClient>();
                Require(Client is InProcessChummerClient
                    && ReferenceEquals(Store, _provider.GetRequiredService<IWorkspaceStore>())
                    && ReferenceEquals(Owners, _provider.GetRequiredService<IOwnerContextAccessor>()),
                    "Owner-boundary fixture must use one real in-process client/store/owner composition.");
                var codecs = _provider.GetRequiredService<IRulesetWorkspaceCodecResolver>();
                Require(codecs.Resolve("sr5").GetType().Assembly != typeof(Program).Assembly,
                    "Owner-boundary fixture must resolve the actual canonical ruleset codec.");
                Reader = new AndroidLinkedWorkspaceReader(Client, Store, Owners, codecs);
            }
            catch
            {
                try { _provider?.Dispose(); }
                finally { RestoreHost(); }
                throw;
            }
        }

        public async Task<WorkspaceStoredDocument> ImportAsync(OwnerScope owner, string fixtureName)
        {
            Owners.Set(owner);
            // Only a synthetic import fixture name differs between partitions.
            // Canonical import/save/validation supplies the actual stored envelope;
            // no copied codec, projection, or substitute persisted store is used.
            string xml = LinkedRuntimeRunnerXml.Replace("<name>Native linked runner</name>",
                $"<name>{fixtureName}</name>", StringComparison.Ordinal);
            var imported = await Client.ImportAsync(new WorkspaceImportDocument(xml, "sr5"), default);
            Require((await Client.SaveAsync(imported.Id, default)).Success,
                "Actual owner-scoped imported fixture could not be checkpointed.");
            Require((await Client.ValidateAsync(imported.Id, default)).IsValid,
                "Actual owner-scoped fixture failed canonical validation.");
            WorkspaceStoreReadResult read = owner.IsLocalSingleUser ? Store.Get(imported.Id) : Store.Get(owner, imported.Id);
            Require(read.Success && read.Value is not null && read.Value.ContentRevision == 1 && read.Value.SavedRevision == 1,
                "Canonical owner fixture was not persisted as the initial saved revision.");
            return read.Value!;
        }

        private void SetEnvironment(string name, string value)
        {
            _environment.Add(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }

        private void RestoreHost()
        {
            foreach (var pair in _environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            Directory.Delete(StateDirectory, recursive: true); // Only this fixture's fresh private directory.
        }

        public async ValueTask DisposeAsync()
        {
            try { await _provider.DisposeAsync(); }
            finally { RestoreHost(); }
        }
    }

    private sealed class ControlledLinkedOwner : IOwnerContextAccessor
    {
        private readonly object _gate = new();
        private OwnerScope _current = OwnerScope.LocalSingleUser;
        private OwnerScope _next;
        private int _changeAt;
        private int _reads;
        private bool _changed;

        public int ReadCount { get { lock (_gate) return _reads; } }
        public bool ChangeApplied { get { lock (_gate) return _changed; } }
        public Action? BeforeRead { get; set; }
        public Action? AfterRead { get; set; }
        public OwnerScope Current
        {
            get
            {
                try
                {
                    BeforeRead?.Invoke();
                    lock (_gate)
                    {
                        _reads++;
                        if (_changeAt > 0 && _reads == _changeAt)
                        {
                            _current = _next;
                            _changed = true;
                        }
                        return _current;
                    }
                }
                finally { AfterRead?.Invoke(); }
            }
        }

        public void Set(OwnerScope owner)
        {
            lock (_gate)
            {
                _current = owner;
                _changeAt = 0;
                _reads = 0;
                _changed = false;
            }
        }

        public void ChangeOnRead(OwnerScope before, OwnerScope after, int readNumber)
        {
            lock (_gate)
            {
                _current = before;
                _next = after;
                _changeAt = readNumber;
                _reads = 0;
                _changed = false;
            }
        }
    }

    private sealed class LinkedRuntimeDocuments : IAndroidDocumentService
    {
        public static readonly byte[] FirstPayload = Encoding.UTF8.GetBytes(
            "<character><name>First runner</name><alias>Nightshade</alias><metatype>Human</metatype></character>");
        public static readonly byte[] SecondPayload = Encoding.UTF8.GetBytes(
            "<character><name>Second runner</name><alias>Neon Fox</alias><metatype>Elf</metatype></character>");
        private readonly Queue<(string Name, byte[] Bytes)> _selections = new();
        public readonly List<byte[]> Returned = [];
        public int OpenCount { get; private set; }
        public void Enqueue(string name, byte[] bytes) => _selections.Enqueue((name, bytes.ToArray()));
        public Task<AndroidDocument?> OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_selections.Count == 0) throw new InvalidOperationException("Unexpected link picker/replay.");
            (string name, byte[] bytes) = _selections.Dequeue();
            Returned.Add(bytes);
            OpenCount++;
            return Task.FromResult<AndroidDocument?>(new(name, "content://test/native-link", "application/xml", bytes));
        }
        public Task<bool> SaveAsAsync(string name, string type, Stream content, CancellationToken token)
            => throw new InvalidOperationException("Link staging must not export or checkpoint a runner.");
    }

    private const string LinkedRuntimeRunnerXml = """
        <character><name>Native linked runner</name><gameedition>SR5</gameedition>
        <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings>
        <metatype>Human</metatype><buildmethod>Priority</buildmethod>
        <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
        <created>True</created><karma>30</karma><nuyen>1000</nuyen>
        <streetcred>10</streetcred><notoriety>4</notoriety><publicawareness>6</publicawareness>
        <burntstreetcred>0</burntstreetcred><improvements/>
        <contacts>
          <contact><guid>11111111-1111-4111-8111-111111111111</guid><name>Original contact</name>
          <metatype>Human</metatype><gender>Female</gender><age>38</age><type>Contact</type>
          <connection>2</connection><loyalty>3</loyalty><notes>Keep original contact notes</notes></contact>
          <contact><guid>22222222-2222-4222-8222-222222222222</guid><name>Untouched contact</name>
          <metatype>Elf</metatype><type>Contact</type><connection>4</connection><loyalty>2</loyalty>
          <notes>Keep second contact and ratings</notes></contact>
        </contacts><expenses/><notes>Retain unrelated data</notes></character>
        """;
}
