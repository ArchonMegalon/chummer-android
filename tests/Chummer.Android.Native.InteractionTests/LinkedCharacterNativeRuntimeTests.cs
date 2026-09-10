using System.Reflection;
using System.Diagnostics.CodeAnalysis;
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
    private const string LinkedPetId = "33333333-3333-4333-8333-333333333333";
    private static readonly WorkspaceCollectionItemTarget LinkedContactTarget =
        new(WorkspaceCollectionKind.Contact, LinkedContactId);
    private static readonly WorkspaceCollectionItemTarget LinkedPetTarget =
        new(WorkspaceCollectionKind.Pet, LinkedPetId);

    private static void RequireLinkedRuntimeContentRoot(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply an explicit Core directory containing data/.", nameof(contentRoot));
        Require(typeof(FileWorkspaceStore).Assembly != typeof(Program).Assembly
            && typeof(CharacterOverviewPresenter).Assembly != typeof(Program).Assembly
            && typeof(RunnerSessionCoordinator).Assembly != typeof(Program).Assembly
            && typeof(Chummer5LinkedDocumentCodec).Assembly != typeof(Program).Assembly,
            "Link integration requires actual Core, Presentation and native assemblies, not test substitutes.");
    }

    public static async Task RunLinkedCharacterNativeRuntimeCasesAsync(string contentRoot)
    {
        RequireLinkedRuntimeContentRoot(contentRoot);
        await NativeLinkedAttachReplaceRemovePersistsAsync(contentRoot);
        await NativeLinkedPostCommitFailureRetainsFilesAsync(contentRoot);
        foreach (string drift in new[] { "missing-file", "later-revision", "deleted-target", "deleted-workspace" })
            await NativeLinkedUncertainDriftStaysPendingAsync(contentRoot, drift);
        foreach (string field in new[] { "name", "metatype", "gender", "age" })
            await NativeLinkedSubstitutedIdentityStaysPendingAsync(contentRoot, field);
        Console.WriteLine("PASS 10 actual linked-character/native/runtime/file-store cases: exact host intent/current-effect observations, shared paths, post-commit failure, conservative drift and identity substitution; no operation lookup, Android process-death or checkpoint proof");
        await NativeLinkedReaderOwnerBoundariesAsync(contentRoot);
        await NativeLinkedOwnerReadCancellationJoinsAccessorAsync(contentRoot);
        await RunLinkedOwnerAbaPublicationCasesAsync(contentRoot);
        foreach (string boundary in new[] { "same-effect", "uncertain-acknowledgement", "owner-change", "owner-aba" })
            await NativeConcurrentLinkedRecoveryAsync(contentRoot, boundary);
        Console.WriteLine("PASS 4 actual concurrent linked-recovery cases: immutable observation, uncertain acknowledgement, changed owner and historical scope recovery after ABA; no mutation replay");
    }

    public static async Task RunLinkedOwnerAbaPublicationCasesAsync(string contentRoot)
    {
        RequireLinkedRuntimeContentRoot(contentRoot);
        // The unchanged and persistent-change controls must pass before the ABA
        // invariant is attempted. The controlled stamped authority is shared by
        // the real native reader and mutation client; no Core receipt is fabricated.
        foreach (string boundary in new[] { "unchanged-owner", "persistent-owner-change", "owner-aba", "dispatch-owner-aba" })
        {
            await NativeLinkedOwnerAbaDuringIntentPublicationAsync(contentRoot, boundary);
            Console.WriteLine($"PASS linked owner publication boundary: {boundary}");
        }
        await NativeLinkedOwnerAbaDuringIntentPublicationAsync(contentRoot, "dispatch-owner-aba", LinkedPetTarget);
        await NativeLinkedOwnerAbaDuringIntentPublicationAsync(contentRoot, "dispatch-owner-aba", LinkedContactTarget, attach: false);
        await NativeLinkedOwnerAbaDuringIntentPublicationAsync(contentRoot, "dispatch-owner-aba", LinkedPetTarget, attach: false);
        Console.WriteLine("PASS actual contact/pet attach/remove final-inspector ABA: original Core stamp rejects dispatch; canonical targets, immutable intent and retained prior link");
        await NativeLinkedOwnerAbaDuringStagingAsync(contentRoot);
        await RunLinkedStaleDisplayOwnerCaptureCaseAsync(contentRoot);
    }

    public static async Task RunLinkedStaleDisplayOwnerCaptureCaseAsync(string contentRoot)
    {
        RequireLinkedRuntimeContentRoot(contentRoot);
        string appData = Directory.CreateTempSubdirectory("chummer-native-linked-display-owner-").FullName;
        try
        {
            var owners = new ControlledLinkedOwner();
            var documents = new LinkedRuntimeDocuments();
            var files = new AndroidLinkedCharacterFileService(documents, new Chummer5LinkedDocumentCodec(), () => appData, Guid.NewGuid);
            FirstLinkedOwnerCaptureReader? reader = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedCharacters: files, linkedOwners: owners,
                linkedReaderDecorator: inner => reader = new FirstLinkedOwnerCaptureReader(inner));
            await runtime.LoadRunnerAsync(LinkedRuntimeRunnerXml);
            await SelectLinkedContactsAsync(runtime);
            OwnerScope ownerA = owners.Current;
            OwnerScope ownerB = new("linked-identical-stale-display-owner-b");
            CharacterOverviewState displayedByA = runtime.Coordinator.State;
            WorkspaceStoredDocument beforeA = ReadLinkedWorkspace(runtime);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            // Deliberately identical canonical document, ID and revisions in two
            // real Core partitions. Equality of editor bytes is not owner authority.
            Require(store.CreateWorkspaceDocument(ownerB, runtime.Id, beforeA.Document).Success
                && store.SaveCheckpoint(ownerB, runtime.Id, beforeA.ContentRevision).Success,
                "Could not create the same-ID/revision canonical B fixture without changing the A display.");
            WorkspaceStoredDocument beforeB = store.Get(ownerB, runtime.Id).Value!;
            RequireSameRewardDocument(beforeA, beforeB);
            Require(beforeA.Id == beforeB.Id && owners.Current == ownerA
                && ReferenceEquals(runtime.Coordinator.State.ActiveCollectionEditor, displayedByA.ActiveCollectionEditor),
                "The stale-display fixture did not retain its original A-owned presenter frame.");
            var workspaceBytes = Directory.EnumerateFiles(runtime.StateDirectory, runtime.Id.Value + ".json", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
            Require(workspaceBytes.Count == 2, "The collision fixture must have two actual serialized Core workspace files.");
            Require(runtime.LinkedJournal.ReadAll(ownerA.NormalizedValue, ownerA.IsLocalSingleUser).Count == 0
                && runtime.LinkedJournal.ReadAll(ownerB.NormalizedValue, ownerB.IsLocalSingleUser).Count == 0,
                "The pre-capture fixture must start without any inserted intent or observation authority.");

            documents.Enqueue("stale-display.chum5", LinkedRuntimeDocuments.FirstPayload);
            reader!.Armed = true;
            Task<bool> action = runtime.Coordinator.TryAttachBoundLinkedCharacterAsync(LinkedContactTarget, displayedByA, () => true);
            bool applied = false;
            Exception? actionFailure = null;
            try
            {
                // This is before the first actual capture, not after A's stamp
                // has been sampled. Capturing B later must not rebind A's view.
                await reader.BeforeCapture.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(!action.IsCompleted && reader.Pauses == 1 && owners.ActiveLeases == 0 && documents.OpenCount == 0,
                    "The stale A display did not stop before the first real owner capture and picker.");
                owners.Set(ownerB);
                reader.ReleaseCapture.TrySetResult();
                try { applied = await action.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception error) { actionFailure = error; }
            }
            finally
            {
                reader.ReleaseCapture.TrySetResult();
                try { await action; }
                catch (Exception error) { actionFailure ??= error; }
            }

            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            WorkspaceStoredDocument afterA = cold.Get(runtime.Id).Value!;
            WorkspaceStoredDocument afterB = cold.Get(ownerB, runtime.Id).Value!;
            int intentsA = runtime.LinkedJournal.ReadAll(ownerA.NormalizedValue, ownerA.IsLocalSingleUser).Count;
            int intentsB = runtime.LinkedJournal.ReadAll(ownerB.NormalizedValue, ownerB.IsLocalSingleUser).Count;
            int stagedFiles = Directory.Exists(Path.Combine(appData, "linked-characters"))
                ? Directory.EnumerateFiles(Path.Combine(appData, "linked-characters")).Count() : 0;
            bool workspaceBytesUnchanged = workspaceBytes.All(entry => File.Exists(entry.Key)
                && File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value));
            Console.WriteLine("LINKED_STALE_DISPLAY_OWNER_OBSERVATION " + JsonSerializer.Serialize(new
            {
                applied, error = actionFailure?.GetType().Name, intentsA, intentsB, stagedFiles,
                pickerReads = documents.OpenCount, beforeARevision = beforeA.ContentRevision,
                afterARevision = afterA.ContentRevision, beforeBRevision = beforeB.ContentRevision,
                afterBRevision = afterB.ContentRevision, workspaceBytesUnchanged
            }));
            Require(reader.Pauses == 1 && reader.FirstCapturedStamp?.Owner == ownerB && owners.Current == ownerB,
                "The fixture did not make the first real capture observe B after displaying A.");
            Require(!applied && intentsA == 0 && intentsB == 0 && stagedFiles == 0 && workspaceBytesUnchanged,
                "A stale A-owned display adopted B's first-captured authority: neither identical-ID owner partition may mutate, "
                + "no intent may be published, and any newly staged file must be reclaimed.");
            RequireSameRewardDocument(beforeA, afterA);
            RequireSameRewardDocument(beforeB, afterB);
            Require(beforeA.LastUpdatedUtc == afterA.LastUpdatedUtc && beforeB.LastUpdatedUtc == afterB.LastUpdatedUtc,
                "Stale-display rejection changed one owner's persisted workspace metadata.");
            Require(actionFailure is null or InvalidOperationException,
                "The stale-display action failed for an unrelated exception: " + actionFailure?.GetType().Name);
            Console.WriteLine("PASS actual stale A display / first B capture: identical Core ID/revisions/editor do not grant B mutation authority");
        }
        finally { Directory.Delete(appData, recursive: true); }
    }

    private static async Task NativeLinkedOwnerAbaDuringIntentPublicationAsync(string contentRoot, string boundary,
        WorkspaceCollectionItemTarget? selectedTarget = null, bool attach = true)
    {
        WorkspaceCollectionItemTarget target = selectedTarget ?? LinkedContactTarget;
        string appData = Directory.CreateTempSubdirectory("chummer-native-linked-owner-aba-").FullName;
        try
        {
            var documents = new LinkedRuntimeDocuments();
            var files = new AndroidLinkedCharacterFileService(documents, new Chummer5LinkedDocumentCodec(), () => appData, Guid.NewGuid);
            var owners = new ControlledLinkedOwner();
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim(false);
            bool armed = false;
            int pauses = 0;
            var existingIntentPaths = new HashSet<string>(StringComparer.Ordinal);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedCharacters: files, linkedOwners: owners,
                linkedJournalFactory: state => new AndroidLinkedCharacterIntentJournal(state, directory =>
                {
                    AndroidPrivateFileDurability.SyncDirectory(directory);
                    if (!Volatile.Read(ref armed)
                        || !directory.EndsWith("linked-character-intents-v1", StringComparison.Ordinal)
                        || !Directory.EnumerateFiles(directory, "*.intent.json").Any(path => !existingIntentPaths.Contains(path))
                        || Interlocked.CompareExchange(ref pauses, 1, 0) != 0) return;
                    // This is the real post-rename/fsync Begin boundary, not an
                    // Nth owner read. Begin still holds the journal ProcessGate.
                    published.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(30)))
                        throw new IOException("Owner-ABA fixture intent publication was not released.");
                }));
            await runtime.LoadRunnerAsync(target.Kind == WorkspaceCollectionKind.Pet
                ? LinkedRuntimeRunnerXml.Replace("</contacts>", LinkedRuntimePetXml + "</contacts>", StringComparison.Ordinal)
                : LinkedRuntimeRunnerXml);
            await SelectLinkedAbaTargetAsync(runtime, target);
            string? originalLinkedPath = null;
            if (!attach)
            {
                // Removal starts from a real acknowledged native/Core attachment,
                // not a fabricated editor or a journal entry inserted as authority.
                documents.Enqueue("original-link.chum5", LinkedRuntimeDocuments.FirstPayload);
                Require(await runtime.Coordinator.TryAttachBoundLinkedCharacterAsync(target, runtime.Coordinator.State, () => true),
                    "The actual removal fixture could not establish its original saved link.");
                await runtime.Presenter.LoadAsync(runtime.Id, default);
                await SelectLinkedAbaTargetAsync(runtime, target);
                WorkspaceLinkedCharacterState linked = runtime.Coordinator.State.ActiveCollectionEditor!.Items
                    .Single(item => CollectionItemEditorPage.TargetsMatch(item.Target, target)).LinkedCharacter!;
                Require(linked.CanRemove, "Canonical saved target does not authorize linked removal.");
                RequireLinkedFile(linked, "original-link.chum5", LinkedRuntimeDocuments.FirstPayload);
                originalLinkedPath = linked.FileName;
            }
            var productionReader = LinkedReader(runtime);
            Require(productionReader is AndroidLinkedWorkspaceReader
                && ReferenceEquals(owners, typeof(AndroidLinkedWorkspaceReader)
                    .GetField("_owners", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(productionReader))
                && ReferenceEquals(owners, typeof(InProcessChummerClient)
                    .GetField("_ownerContextAccessor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Client)),
                "The ABA fixture must share the actual runtime client/reader owner accessor.");
            OwnerScope originalScope = owners.Current;
            OwnerContextStamp originalStamp = owners.Capture();
            AndroidLinkedOwner originalOwner = await productionReader.ReadCurrentOwnerAsync(default);
            var priorRecords = runtime.LinkedJournal.Read(originalOwner.Scope, originalOwner.TrustedLocal, runtime.Id.Value);
            Require(priorRecords.Count == (attach ? 0 : 1) && priorRecords.All(record => record.EffectObserved),
                "Only a real acknowledged setup attachment may precede the tested command.");
            string journalDirectory = Path.Combine(runtime.StateDirectory, "linked-character-intents-v1");
            if (Directory.Exists(journalDirectory))
                existingIntentPaths.UnionWith(Directory.EnumerateFiles(journalDirectory, "*.intent.json"));
            var priorIntentBytes = existingIntentPaths.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
            WorkspaceStoredDocument before = ReadLinkedWorkspace(runtime);
            CharacterOverviewState beforeUi = runtime.Coordinator.State;
            if (attach) documents.Enqueue("owner-bound.chum5", LinkedRuntimeDocuments.FirstPayload);
            Volatile.Write(ref armed, true);
            int dispatchFlips = 0;
            bool IsCurrentInspector()
            {
                // This callback is the final inspector check AFTER the last native
                // owner read. Only the original stamp carried into Core can close
                // this gap; another scope-equality check already returned A.
                if (boundary == "dispatch-owner-aba" && Volatile.Read(ref pauses) == 1 && release.IsSet
                    && Interlocked.CompareExchange(ref dispatchFlips, 1, 0) == 0)
                {
                    Require(owners.ActiveLeases == 0, "A native owner lease escaped across an awaited boundary.");
                    owners.Set(new OwnerScope("different-owner-before-core-dispatch"));
                    owners.Set(originalScope);
                }
                return true;
            }
            Task<bool> action = attach
                ? runtime.Coordinator.TryAttachBoundLinkedCharacterAsync(target, beforeUi, IsCurrentInspector)
                : runtime.Coordinator.TryRemoveBoundLinkedCharacterAsync(target, beforeUi, IsCurrentInspector);
            bool applied = false;
            Exception? actionFailure = null;
            string? intentPath = null;
            byte[]? intentBytes = null;
            try
            {
                await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(!action.IsCompleted && Volatile.Read(ref pauses) == 1,
                    "The actual intent-publication boundary was not held.");
                Require(owners.ActiveLeases == 0, "An owner lease was held across durable journal publication.");
                intentPath = Directory.EnumerateFiles(journalDirectory, "*.intent.json")
                    .Single(path => !existingIntentPaths.Contains(path));
                intentBytes = File.ReadAllBytes(intentPath);
                Require(Directory.EnumerateFiles(journalDirectory, "*.observation.json").Count() == priorRecords.Count,
                    "Owner transition did not precede the actual observation.");
                RequireSameRewardDocument(before, ReadLinkedWorkspace(runtime));
                // Never call journal.Read here: the paused Begin owns its lock.
                if (boundary is "persistent-owner-change" or "owner-aba")
                {
                    var differentOwner = new OwnerScope("different-owner-during-linked-publication");
                    owners.Set(differentOwner);
                    Require(owners.Current == differentOwner, "The fixture did not switch away from the original owner.");
                    if (boundary == "owner-aba")
                    {
                        owners.Set(originalScope);
                        Require(owners.Current == originalScope, "The fixture did not switch back to the original owner.");
                    }
                }
                release.Set();
                try { applied = await action.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception error) { actionFailure = error; }
            }
            finally
            {
                release.Set();
                // A timeout/assertion must not detach the real worker or dispose
                // its runtime, staged bytes, journal directory or wait handle.
                try { await action; }
                catch (Exception error) { actionFailure ??= error; }
            }
            Require(actionFailure is null, $"Owner publication action faulted: {actionFailure?.GetType().Name}.");
            if (boundary is "owner-aba" or "dispatch-owner-aba")
            {
                OwnerContextStamp returned = owners.Capture();
                Require(returned.Owner == originalStamp.Owner && returned.AuthorityInstanceId == originalStamp.AuthorityInstanceId
                    && returned.TransitionRevision == originalStamp.TransitionRevision + 2
                    && (boundary != "dispatch-owner-aba" || dispatchFlips == 1),
                    "The ABA fixture did not retain equal scope with a distinct original Core stamp.");
            }
            var allRecords = runtime.LinkedJournal.Read(originalOwner.Scope, originalOwner.TrustedLocal, runtime.Id.Value);
            Require(allRecords.Count == priorRecords.Count + 1 && priorRecords.All(prior =>
                JsonSerializer.Serialize(allRecords.Single(current => current.Intent.OperationId == prior.Intent.OperationId))
                    == JsonSerializer.Serialize(prior))
                && priorIntentBytes.All(prior => File.ReadAllBytes(prior.Key).SequenceEqual(prior.Value)),
                "The ABA operation replaced or reinterpreted genuine prior attachment history.");
            var record = allRecords.Single(current => !priorRecords.Any(prior => prior.Intent.OperationId == current.Intent.OperationId));
            WorkspaceStoredDocument after = ReadLinkedWorkspace(runtime);
            bool stagedExists = record.Intent.StagedFileName is { } stagedPath && File.Exists(stagedPath);
            Require(intentPath is not null && intentBytes is not null
                && File.ReadAllBytes(intentPath).SequenceEqual(intentBytes)
                && record.Intent.OwnerScope == originalOwner.Scope
                && record.Intent.TrustedLocalOwner == originalOwner.TrustedLocal
                && record.Intent.ContentRevision == before.ContentRevision
                && record.Intent.SavedRevision == before.SavedRevision
                && record.Intent.DocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(before.Document)
                && record.Intent.Attach == attach && CollectionItemEditorPage.TargetsMatch(record.Intent.Target, target)
                && documents.OpenCount == 1,
                "Owner publication fixture changed intent authority or replayed document selection.");
            RequireLinkedIntentHasNoTransientStamp(intentBytes!, originalStamp);
            WorkspaceCollectionMutationRequest originalRequest = attach
                ? record.Intent.Attachment! : new WorkspaceRemoveLinkedCharacterRequest(target);
            Require(record.Intent.RequestSha256 == Convert.ToHexStringLower(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(originalRequest, originalRequest.GetType()))),
                "The original target/path-bound request digest changed across the owner boundary.");
            if (!attach)
                Require(record.Intent.StagedFileName is null && record.Intent.Attachment is null
                    && File.Exists(originalLinkedPath) && File.ReadAllBytes(originalLinkedPath!).SequenceEqual(LinkedRuntimeDocuments.FirstPayload),
                    "A rejected removal reclaimed or changed the original committed linked file.");
            Console.WriteLine("LINKED_OWNER_PUBLICATION_OBSERVATION " + JsonSerializer.Serialize(new
            {
                boundary, targetKind = target.Kind.ToString(), attach, applied, beforeRevision = before.ContentRevision, afterRevision = after.ContentRevision,
                savedRevision = after.SavedRevision, record.EffectObserved, record.NotDispatched,
                stagedExists, pickerReads = documents.OpenCount,
                documentUnchanged = RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(before.Document)
                    == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(after.Document)
            }));
            if (boundary == "unchanged-owner")
            {
                Require(applied && record.EffectObserved && !record.NotDispatched && stagedExists
                    && after.ContentRevision == before.ContentRevision + 1 && after.SavedRevision == before.SavedRevision,
                    "Unchanged-owner control did not commit exactly one acknowledged current effect.");
                RequireOnlyLinkedAssociationChanged(before, after);
                Require(File.ReadAllBytes(record.Intent.StagedFileName!).SequenceEqual(LinkedRuntimeDocuments.FirstPayload),
                    "Unchanged-owner control did not retain the committed staged bytes.");
            }
            else
            {
                Require(!applied && record.NotDispatched && !record.EffectObserved && !stagedExists,
                    $"Owner transition during durable intent publication must invalidate dispatch, even after A-to-B-to-A: "
                    + $"boundary={boundary}, applied={applied}, revision={before.ContentRevision}->{after.ContentRevision}, "
                    + $"effectObserved={record.EffectObserved}, notDispatched={record.NotDispatched}, stagedExists={stagedExists}.");
                RequireSameRewardDocument(before, after);
                Require(Directory.EnumerateFiles(Path.Combine(appData, "linked-characters")).Count() == (attach ? 0 : 1),
                    "Rejected owner transition retained uncommitted staging or reclaimed a previously committed file.");
            }
        }
        finally { Directory.Delete(appData, recursive: true); } // This test's fresh private directory only.
    }

    private static void RequireLinkedIntentHasNoTransientStamp(byte[] intentBytes, OwnerContextStamp originalStamp)
    {
        string raw = Encoding.UTF8.GetString(intentBytes);
        using JsonDocument intent = JsonDocument.Parse(intentBytes);
        Require(!raw.Contains(originalStamp.AuthorityInstanceId, StringComparison.Ordinal)
            && !ContainsTransientProperty(intent.RootElement),
            "A transient authority issuer or transition revision leaked into the stable durable intent identity.");

        static bool ContainsTransientProperty(JsonElement element)
            => element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject().Any(property =>
                    property.Name.Equals("authorityInstanceId", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("transitionRevision", StringComparison.OrdinalIgnoreCase)
                    || ContainsTransientProperty(property.Value)),
                JsonValueKind.Array => element.EnumerateArray().Any(ContainsTransientProperty),
                _ => false
            };
    }

    private static async Task SelectLinkedAbaTargetAsync(NativeRewardRuntime runtime, WorkspaceCollectionItemTarget target)
    {
        if (target.Kind == WorkspaceCollectionKind.Contact)
        {
            await SelectLinkedContactsAsync(runtime);
            return;
        }

        await runtime.Coordinator.SelectTabAsync("tab-relationships");
        var petsAction = runtime.Coordinator.Surface.WorkspaceActions.Single(action => action.Id == "tab-relationships.pets");
        await runtime.Coordinator.ExecuteWorkspaceActionAsync(petsAction);
        CharacterOverviewState state = runtime.Coordinator.State;
        Require(state.WorkspaceId == runtime.Id && state.ActiveSectionId == "pets"
            && runtime.Presenter.State.Error is null && state.ActiveCollectionEditor?.Items.Count == 1,
            $"Actual pets projection is unavailable: {runtime.Presenter.State.Error}");
        WorkspaceCollectionItemEditorState pet = state.ActiveCollectionEditor!.Items.Single();
        AndroidLinkedWorkspaceSnapshot? snapshot = await LinkedReader(runtime).ReadAsync(runtime.Id, "pets", default);
        Require(CollectionItemEditorPage.TargetsMatch(pet.Target, LinkedPetTarget)
            && pet.LinkedCharacter is { CanAttach: true }
            && snapshot is not null && snapshot.Contacts.Count == 1 && snapshot.Contacts.Single().Guid == LinkedPetId
            && snapshot.DocumentAuthoritySha256 == RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(ReadLinkedWorkspace(runtime).Document)
            && JsonSerializer.Serialize(snapshot.Editor) == JsonSerializer.Serialize(state.ActiveCollectionEditor),
            "The actual saved Core pet identity/reader/presenter must agree; a synthetic editor is not fixture authority.");
    }

    private static async Task NativeLinkedOwnerAbaDuringStagingAsync(string contentRoot)
    {
        string appData = Directory.CreateTempSubdirectory("chummer-native-linked-stage-aba-").FullName;
        try
        {
            var owners = new ControlledLinkedOwner();
            var documents = new LinkedRuntimeDocuments();
            int transitions = 0;
            bool armed = false;
            var files = new AndroidLinkedCharacterFileService(documents, new Chummer5LinkedDocumentCodec(),
                () => appData, Guid.NewGuid, syncDirectory: directory =>
                {
                    AndroidPrivateFileDurability.SyncDirectory(directory);
                    if (!armed || Interlocked.CompareExchange(ref transitions, 1, 0) != 0) return;
                    Require(owners.ActiveLeases == 0, "The original owner lease crossed asynchronous file staging.");
                    OwnerScope original = owners.Current;
                    owners.Set(new OwnerScope("owner-during-linked-stage"));
                    owners.Set(original);
                });
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedCharacters: files, linkedOwners: owners);
            await runtime.LoadRunnerAsync(LinkedRuntimeRunnerXml);
            await SelectLinkedContactsAsync(runtime);
            WorkspaceStoredDocument before = ReadLinkedWorkspace(runtime);
            CharacterOverviewState beforeUi = runtime.Coordinator.State;
            OwnerContextStamp originalStamp = owners.Capture();
            documents.Enqueue("staging-aba.chum5", LinkedRuntimeDocuments.FirstPayload);
            armed = true;
            Require(!await runtime.Coordinator.TryAttachBoundLinkedCharacterAsync(LinkedContactTarget, beforeUi, () => true),
                "A scope-equal ABA during real file staging authorized a new link mutation.");
            OwnerContextStamp after = owners.Capture();
            Require(transitions == 1 && after.Owner == originalStamp.Owner
                && after.TransitionRevision == originalStamp.TransitionRevision + 2 && owners.ActiveLeases == 0
                && documents.OpenCount == 1 && runtime.LinkedJournal.ReadAll(after.Owner.NormalizedValue, after.Owner.IsLocalSingleUser).Count == 0
                && !Directory.EnumerateFiles(Path.Combine(appData, "linked-characters")).Any(),
                "Staging ABA lost original authority, replayed selection, began a journal or retained undispatched bytes.");
            RequireSameRewardDocument(before, ReadLinkedWorkspace(runtime));
            Console.WriteLine("PASS actual linked staging ABA: original stamped action rejected, real file cleaned, no journal or mutation");
        }
        finally { Directory.Delete(appData, recursive: true); } // This fixture's fresh private directory only.
    }

    private static async Task NativeConcurrentLinkedRecoveryAsync(string contentRoot, string boundary)
    {
        string appData = Directory.CreateTempSubdirectory("chummer-native-linked-concurrent-").FullName;
        try
        {
            var documents = new LinkedRuntimeDocuments();
            var files = new AndroidLinkedCharacterFileService(documents, new Chummer5LinkedDocumentCodec(), () => appData, Guid.NewGuid);
            var owners = new ControlledLinkedOwner();
            ConcurrentLinkedReader? reader = null;
            bool failObservationAcknowledgement = false;
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedCharacters: files,
                linkedReaderDecorator: inner => reader = new ConcurrentLinkedReader(inner), linkedOwners: owners,
                linkedJournalFactory: state => new AndroidLinkedCharacterIntentJournal(state, directory =>
                {
                    AndroidPrivateFileDurability.SyncDirectory(directory);
                    if (failObservationAcknowledgement && directory.EndsWith("linked-character-intents-v1", StringComparison.Ordinal)
                        && Directory.EnumerateFiles(directory, "*.observation.json").Any())
                        throw new IOException("Injected observation directory acknowledgement failure.");
                }));
            await runtime.LoadRunnerAsync(LinkedRuntimeRunnerXml);
            await SelectLinkedContactsAsync(runtime);
            WorkspaceStoredDocument before = ReadLinkedWorkspace(runtime);
            CharacterOverviewState beforeUi = runtime.Coordinator.State;
            documents.Enqueue("concurrent.chum5", LinkedRuntimeDocuments.FirstPayload);
            await RequireLinkedShellFailureAsync(runtime, attach: true);
            var pending = await RequireLinkedIntentRecordAsync(runtime, before, beforeUi, attach: true, observed: false, count: 1);
            WorkspaceStoredDocument committed = ReadLinkedWorkspace(runtime);
            string observationPath = Path.Combine(runtime.StateDirectory, "linked-character-intents-v1",
                $"{pending.Intent.OperationId:N}.observation.json");
            string intentPath = Path.Combine(runtime.StateDirectory, "linked-character-intents-v1",
                $"{pending.Intent.OperationId:N}.intent.json");
            byte[] intentBytes = File.ReadAllBytes(intentPath);
            failObservationAcknowledgement = boundary == "uncertain-acknowledgement";
            reader!.Armed = true;
            Task<bool> first = runtime.Coordinator.CheckLinkedCharacterIntentAsync(pending.Intent.OperationId);
            Task<bool> second = runtime.Coordinator.CheckLinkedCharacterIntentAsync(pending.Intent.OperationId);
            Task<bool[]> both = Task.WhenAll(first, second);
            Exception? failure = null;
            bool[]? outcomes = null;
            try
            {
                // Both callers have read the real pending journal and completed
                // their first canonical snapshot before either may publish.
                await reader.BothPending.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!File.Exists(observationPath), "Concurrent fixture did not stop before observation publication.");
                if (boundary is "owner-change" or "owner-aba")
                {
                    Require(owners.ActiveLeases == 0, "Recovery retained a reader lease across an await.");
                    OwnerScope original = owners.Current;
                    owners.Set(new OwnerScope("different-live-link-owner"));
                    if (boundary == "owner-aba") owners.Set(original);
                }
                reader.Release.TrySetResult();
                try { outcomes = await both.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception error) { failure = error; }
            }
            finally
            {
                reader.Release.TrySetResult();
                // Never dispose the runtime or fixture files while a real read,
                // journal write or failed observation is still being joined.
                try { await both; } catch { }
            }
            var record = runtime.LinkedJournal.Read(pending.Intent.OwnerScope,
                pending.Intent.TrustedLocalOwner, runtime.Id.Value).Single();
            if (boundary is "same-effect" or "owner-aba")
            {
                Require(failure is null && outcomes is [true, true],
                    $"Concurrent read-only checks misreported one healthy observation as failure: {failure?.GetType().Name}.");
                Require(record.EffectObserved && !record.NotDispatched && File.Exists(observationPath),
                    "Concurrent checks failed to retain one exact acknowledged observation.");
                byte[] observedBytes = File.ReadAllBytes(observationPath);
                Require(await runtime.Coordinator.CheckLinkedCharacterIntentAsync(pending.Intent.OperationId)
                    && File.ReadAllBytes(observationPath).SequenceEqual(observedBytes),
                    "Revisiting concurrent recovery rewrote the immutable observation.");
            }
            else if (boundary == "uncertain-acknowledgement")
                Require(failure is IOException && first.IsFaulted && second.IsFaulted
                    && !record.EffectObserved && !record.NotDispatched && !record.ObservationAcknowledged
                    && File.Exists(observationPath),
                    "A failed durability acknowledgement was promoted by the conflicting read-only check.");
            else
                Require(failure is null && outcomes is [false, false] && record.Observation is null
                    && !File.Exists(observationPath), "Owner drift acquired another owner's recovery authority.");
            RequireSameRewardDocument(committed, ReadLinkedWorkspace(runtime));
            Require(File.ReadAllBytes(intentPath).SequenceEqual(intentBytes) && documents.OpenCount == 1
                && File.ReadAllBytes(pending.Intent.StagedFileName!).SequenceEqual(LinkedRuntimeDocuments.FirstPayload)
                && Directory.EnumerateFiles(Path.Combine(appData, "linked-characters")).Count() == 1,
                "Read-only recovery mutated intent, replayed a picker or reclaimed staged bytes.");
        }
        finally { Directory.Delete(appData, recursive: true); } // This test's fresh private directory only.
    }

    private sealed class FirstLinkedOwnerCaptureReader(IAndroidLinkedWorkspaceReader inner) : IAndroidLinkedWorkspaceReader
    {
        private int _pauses;
        public IAndroidLinkedWorkspaceReader Inner => inner;
        public bool Armed { get; set; }
        public int Pauses => Volatile.Read(ref _pauses);
        public OwnerContextStamp? FirstCapturedStamp { get; private set; }
        public TaskCompletionSource BeforeCapture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCapture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => inner.IsAvailable;
        public AndroidLinkedOwner CurrentOwner => inner.CurrentOwner;
        public async Task<OwnerContextStamp> CaptureOwnerContextAsync(CancellationToken token)
        {
            bool first = Armed && Interlocked.CompareExchange(ref _pauses, 1, 0) == 0;
            if (first)
            {
                BeforeCapture.TrySetResult();
                await ReleaseCapture.Task.ConfigureAwait(false);
            }
            OwnerContextStamp captured = await inner.CaptureOwnerContextAsync(token).ConfigureAwait(false);
            if (first) FirstCapturedStamp = captured;
            return captured;
        }
        public Task<AndroidLinkedOwner> ReadCurrentOwnerAsync(CancellationToken token) => inner.ReadCurrentOwnerAsync(token);
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadAsync(CharacterWorkspaceId id, string section, CancellationToken token)
            => inner.ReadAsync(id, section, token);
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(OwnerContextStamp expectedOwner,
            CharacterWorkspaceId id, string section, WorkspaceCollectionMutationRequest request, CancellationToken token)
            => inner.ReadForMutationAsync(expectedOwner, id, section, request, token);
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(CharacterWorkspaceId id, string section,
            WorkspaceCollectionMutationRequest request, CancellationToken token)
            => inner.ReadForMutationAsync(id, section, request, token);
    }

    private sealed class ConcurrentLinkedReader(IAndroidLinkedWorkspaceReader inner) : IAndroidLinkedWorkspaceReader
    {
        private int _reads;
        public IAndroidLinkedWorkspaceReader Inner => inner;
        public bool Armed { get; set; }
        public TaskCompletionSource BothPending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => inner.IsAvailable;
        public AndroidLinkedOwner CurrentOwner => inner.CurrentOwner;
        public Task<OwnerContextStamp> CaptureOwnerContextAsync(CancellationToken token) => inner.CaptureOwnerContextAsync(token);
        public Task<AndroidLinkedOwner> ReadCurrentOwnerAsync(CancellationToken token) => inner.ReadCurrentOwnerAsync(token);
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(OwnerContextStamp expectedOwner,
            CharacterWorkspaceId id, string section, WorkspaceCollectionMutationRequest request, CancellationToken token)
            => inner.ReadForMutationAsync(expectedOwner, id, section, request, token);
        public Task<AndroidLinkedWorkspaceSnapshot?> ReadForMutationAsync(CharacterWorkspaceId id, string section,
            WorkspaceCollectionMutationRequest request, CancellationToken token)
            => inner.ReadForMutationAsync(id, section, request, token);
        public async Task<AndroidLinkedWorkspaceSnapshot?> ReadAsync(CharacterWorkspaceId id, string section, CancellationToken token)
        {
            AndroidLinkedWorkspaceSnapshot? result = await inner.ReadAsync(id, section, token);
            if (Armed && Interlocked.Increment(ref _reads) <= 2)
            {
                if (Volatile.Read(ref _reads) == 2) BothPending.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return result;
        }
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
        Console.WriteLine("PASS actual linked-reader async owner read: exact scope, precancellation without access, responsive caller and joined cancellation; controlled accessor, no Desktop writer or Android device proof");
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
        var codecs = (IRulesetWorkspaceCodecResolver)typeof(AndroidLinkedWorkspaceReader)
            .GetField("_codecs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Reader)!;
        var scopeOnly = new AndroidLinkedWorkspaceReader(runtime.Client, runtime.Store, new ScopeOnlyLinkedOwner(), codecs);
        Require(!scopeOnly.IsAvailable && await scopeOnly.ReadAsync(localDocument.Id, "contacts", default) is null,
            "Scope-only owner polling was silently upgraded to actual lease authority.");
        var foreignOwners = new ControlledLinkedOwner();
        var foreignAuthority = new AndroidLinkedWorkspaceReader(runtime.Client, runtime.Store, foreignOwners, codecs);
        Require(await foreignAuthority.ReadAsync(localDocument.Id, "contacts", default) is null,
            "A same-scope reader with a different authority issuer obtained workspace evidence.");
        Require(await foreignAuthority.ReadForMutationAsync(foreignOwners.Capture(), localDocument.Id, "contacts",
            new WorkspaceRemoveLinkedCharacterRequest(LinkedContactTarget), default) is null,
            "An explicit stamp from the foreign reader's own issuer bypassed the real mutation client's authority.");
        bool foreignCaptureRejected = false;
        try { _ = await foreignAuthority.CaptureOwnerContextAsync(default); }
        catch (InvalidOperationException) { foreignCaptureRejected = true; }
        Require(foreignCaptureRejected, "A foreign reader issuer adopted the actual client's stamp.");
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

        // The original stamp is an actual authority generation, not a scope
        // reconstructed after returning to A. Every failed preview is read-only.
        foreach (string drift in new[] { "persistent", "aba", "foreign-authority" })
        {
            runtime.Owners.Set(accountA);
            OwnerContextStamp original = await runtime.Reader.CaptureOwnerContextAsync(default);
            if (drift == "foreign-authority") original = original with { AuthorityInstanceId = Guid.NewGuid().ToString("N") };
            else
            {
                runtime.Owners.Set(accountB);
                if (drift == "aba") runtime.Owners.Set(accountA);
            }
            Require(await runtime.Reader.ReadForMutationAsync(original, sharedId, "contacts", remove, default) is null,
                $"The real reader accepted an original {drift} owner stamp for mutation preview.");
        }
        await RequireLinkedReaderLeaseExcludesWriterAsync(runtime, accountA, accountB, sharedId, remove);
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
        Console.WriteLine("PASS actual linked-reader owner boundaries: trusted/forged local, account partitions, stale/ABA/foreign stamps and synchronous writer exclusion; controlled lease authority, no Desktop writer or Android process-death proof");
    }

    private static async Task RequireLinkedReaderLeaseExcludesWriterAsync(LinkedOwnerRuntime runtime,
        OwnerScope accountA, OwnerScope accountB, CharacterWorkspaceId workspaceId,
        WorkspaceCollectionMutationRequest request)
    {
        runtime.Owners.Set(accountA);
        OwnerContextStamp original = await runtime.Reader.CaptureOwnerContextAsync(default);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var writerEntered = new ManualResetEventSlim();
        runtime.Owners.AfterAcquire = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The real reader's synchronous owner lease was not released.");
        };
        runtime.Owners.BeforeWrite = () => writerEntered.Set();
        Task<AndroidLinkedWorkspaceSnapshot?> read = runtime.Reader.ReadForMutationAsync(
            original, workspaceId, "contacts", request, default);
        Task? writer = null;
        AndroidLinkedWorkspaceSnapshot? observed = null;
        try
        {
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "The real reader did not acquire its original owner lease.");
            writer = Task.Run(() => runtime.Owners.Set(accountB));
            Require(writerEntered.Wait(TimeSpan.FromSeconds(5)) && !writer.IsCompleted
                && runtime.Owners.ActiveLeases == 1,
                "A mutable owner writer crossed the reader's live exclusion boundary.");
        }
        finally
        {
            release.Set();
            try { observed = await read; }
            finally
            {
                if (writer is not null) await writer;
                runtime.Owners.AfterAcquire = null;
                runtime.Owners.BeforeWrite = null;
            }
        }
        Require(observed is not null && observed.Owner == new AndroidLinkedOwner(accountA.NormalizedValue, false)
            && runtime.Owners.ActiveLeases == 0 && runtime.Owners.Current == accountB,
            "Reader lease did not preserve A's exact observation and release its same-thread writer exclusion.");
        runtime.Owners.Set(accountA);
        Require(await runtime.Reader.ReadForMutationAsync(original, workspaceId, "contacts", request, default) is null,
            "A completed A-to-B-to-A writer transition revived the original reader stamp.");
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
        IAndroidLinkedWorkspaceReader productionReader = reader switch
        {
            ConcurrentLinkedReader timed => timed.Inner,
            FirstLinkedOwnerCaptureReader captured => captured.Inner,
            _ => reader
        };
        Require(productionReader is AndroidLinkedWorkspaceReader,
            "The timing decorator must still wrap the actual production reader.");
        CharacterOverviewState expected = runtime.Coordinator.State;
        WorkspaceStoredDocument stored = ReadLinkedWorkspace(runtime);
        Require(reader.IsAvailable, "Synthetic linked fixture has no available actual in-process/file-store read capability.");
        AndroidLinkedOwner owner = await Task.Run(() => reader.CurrentOwner);
        var codecs = (IRulesetWorkspaceCodecResolver)productionReader.GetType()
            .GetField("_codecs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(productionReader)!;
        object canonical = codecs.Resolve(stored.Document.RulesetId).ParseSection("contacts", stored.Document.PayloadEnvelope);
        Require(canonical is Chummer.Contracts.Characters.CharacterContactsSection,
            "Actual linked codec section type: " + canonical.GetType().FullName);
        Require(stored.Document.Format == WorkspaceDocumentFormat.NativeXml && stored.Document.SchemaVersion > 0,
            "Actual linked fixture envelope is not an identified native document.");
        _ = RunnerSessionCoordinator.ComputeDocumentAuthoritySha256(stored.Document);
        var readerStore = (IWorkspaceStore)productionReader.GetType().GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(productionReader)!;
        var ownerAccessor = (Chummer.Application.Owners.IOwnerContextAccessor)productionReader.GetType()
            .GetField("_owners", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(productionReader)!;
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

    private sealed class ScopeOnlyLinkedOwner : IOwnerContextAccessor
    {
        public OwnerScope Current => OwnerScope.LocalSingleUser;
    }

    // Test-only authority shared by the actual Core client and native reader.
    // Its writer uses the same gate as live leases; it is not a runtime adapter
    // over Current and does not stand in for Desktop install-writer evidence.
    private sealed class ControlledLinkedOwner : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _authorityId = Guid.NewGuid().ToString("N");
        private OwnerScope _current = OwnerScope.LocalSingleUser;
        private long _revision;
        private int _reads;
        private int _activeLeases;
        private int _leaseThread;

        public int ReadCount { get { lock (_gate) return _reads; } }
        public int ActiveLeases => Volatile.Read(ref _activeLeases);
        public Action? BeforeRead { get; set; }
        public Action? AfterRead { get; set; }
        public Action? AfterAcquire { get; set; }
        public Action? BeforeWrite { get; set; }
        public OwnerScope Current => Capture().Owner;
        private OwnerContextStamp Stamp => new(_current, _authorityId, _revision);

        public OwnerContextStamp Capture()
        {
            try
            {
                BeforeRead?.Invoke();
                lock (_gate)
                {
                    _reads++;
                    return Stamp;
                }
            }
            finally { AfterRead?.Invoke(); }
        }

        public void Set(OwnerScope owner)
        {
            BeforeWrite?.Invoke();
            lock (_gate)
            {
                if (_activeLeases != 0)
                    throw new InvalidOperationException("Owner writer re-entered an active same-thread lease.");
                if (_current != owner) _revision = checked(_revision + 1);
                _current = owner;
                _reads = 0;
            }
        }

        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            lease = null;
            Monitor.Enter(_gate);
            if (!expected.IsValid || expected != Stamp || _activeLeases != 0)
            {
                Monitor.Exit(_gate);
                return false;
            }
            _leaseThread = Environment.CurrentManagedThreadId;
            Volatile.Write(ref _activeLeases, 1);
            var acquired = new ControlledLease(this, expected);
            try { AfterAcquire?.Invoke(); }
            catch { acquired.Dispose(); throw; }
            lease = acquired;
            return true;
        }

        private sealed class ControlledLease(ControlledLinkedOwner authority, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(ControlledLease));
            public void Dispose()
            {
                if (_disposed) return;
                if (authority._leaseThread != Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException("An owner lease crossed an asynchronous/thread boundary.");
                _disposed = true;
                authority._leaseThread = 0;
                Volatile.Write(ref authority._activeLeases, 0);
                Monitor.Exit(authority._gate);
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

    private const string LinkedRuntimePetXml = """
        <contact><guid>33333333-3333-4333-8333-333333333333</guid><name>Original pet</name>
        <metatype>Hell Hound</metatype><type>Pet</type><notes>Keep original pet notes</notes></contact>
        """;

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
