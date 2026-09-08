using System.Text;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Presentation.Overview;

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
        Console.WriteLine("PASS 2 actual linked-character/native/runtime/file-store cases: attach/replace/remove, shared-path preservation and post-commit failure; presenter/store reopen, not Android process death or checkpoint proof");
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

            await RequireLinkedShellFailureAsync(runtime, attach: true);
            WorkspaceStoredDocument attached = ReadLinkedWorkspace(runtime);
            Require(attached.ContentRevision == 2 && attached.SavedRevision == 1,
                "The controlled shell failure did not follow one real document commit.");
            RequireOnlyLinkedAssociationChanged(initial, attached);
            string committedPath = ContactElement(attached, LinkedContactId).Element("file")?.Value
                ?? throw new InvalidOperationException("The real commit has no persisted linked path.");
            Require(File.Exists(committedPath) && File.ReadAllBytes(committedPath).SequenceEqual(LinkedRuntimeDocuments.FirstPayload),
                "An uncertain post-commit attach deleted its referenced file.");
            await RequireLinkedReloadAsync(runtime, attached);
            RequireLinkedFile(CurrentLinkedContact(runtime).LinkedCharacter!, "first.chum5", LinkedRuntimeDocuments.FirstPayload);

            await RequireLinkedShellFailureAsync(runtime, attach: false);
            WorkspaceStoredDocument removed = ReadLinkedWorkspace(runtime);
            Require(removed.ContentRevision == 3 && removed.SavedRevision == 1
                && ContactElement(removed, LinkedContactId).Element("file")?.Value == string.Empty,
                "The controlled remove failure did not follow one real removal commit.");
            RequireOnlyLinkedAssociationChanged(initial, removed);
            Require(File.Exists(committedPath) && File.ReadAllBytes(committedPath).SequenceEqual(LinkedRuntimeDocuments.FirstPayload),
                "An uncertain post-commit removal reclaimed a previously referenced file.");
            await RequireLinkedReloadAsync(runtime, removed);
            Require(!CurrentLinkedContact(runtime).LinkedCharacter!.IsLinked && documents.OpenCount == 1,
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

    private static async Task SelectLinkedContactsAsync(NativeRewardRuntime runtime)
    {
        await runtime.Coordinator.SelectTabAsync("tab-contacts");
        Require(runtime.Coordinator.State.WorkspaceId == runtime.Id && runtime.Coordinator.State.ActiveSectionId == "contacts"
            && runtime.Presenter.State.Error is null && runtime.Coordinator.State.ActiveCollectionEditor?.Items.Count == 2,
            $"Actual contacts projection is unavailable: {runtime.Presenter.State.Error}");
        Require(CurrentLinkedContact(runtime).LinkedCharacter is { CanAttach: true },
            "Canonical contact link capability is missing; this test cannot substitute an editor projection.");
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
