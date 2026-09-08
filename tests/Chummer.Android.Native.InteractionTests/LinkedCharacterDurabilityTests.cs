using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Chummer.Presentation.Overview;

internal static class LinkedCharacterDurabilityTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);
    private static readonly WorkspaceCollectionItemTarget Target = new(
        WorkspaceCollectionKind.Contact, "11111111-1111-4111-8111-111111111111");

    public static async Task RunAsync()
    {
        await DurabilityBarriersRunOffCallerContextAsync();
        await FaultsPreserveOnlyPostRenameFilesAsync();
        await CancellationRespectsRenameBoundaryAsync();
        await PickerCancellationDoesNotLeakSelectedBytesAsync();
        await CollisionCannotReplaceOrDeleteExistingFileAsync();
        await DefaultDurabilityUsesRealFilesAsync();
        await Task.Run(SharedDirectoryHelperPreservesJournalBackendBytes).WaitAsync(Bound);
        Console.WriteLine("PASS linked-character staging and journal storage acknowledgements (real codec/files/fsync and injected faults; no power-cut or device proof)");
    }

    private static async Task DurabilityBarriersRunOffCallerContextAsync()
    {
        using var fixture = new Fixture();
        var events = new ConcurrentQueue<string>();
        var parentEntered = Signal();
        var parentRelease = Signal();
        var flushEntered = Signal();
        var flushRelease = Signal();
        var finalEntered = Signal();
        var finalRelease = Signal();
        var context = new RecordingContext();
        void RequireWorker()
            => Require(SynchronizationContext.Current is null
                && Thread.CurrentThread.IsThreadPoolThread,
                "Durability work did not run on a context-free worker thread.");
        Guid fileId = Guid.NewGuid();
        var service = fixture.Service(
            codec: new ObservingCanonicalCodec(() => { RequireWorker(); events.Enqueue("decode"); }),
            fileId: () => { RequireWorker(); events.Enqueue("file-id"); return fileId; },
            flush: stream =>
            {
                RequireWorker();
                events.Enqueue("flush");
                stream.Flush(flushToDisk: true);
                flushEntered.TrySetResult(true);
                flushRelease.Task.WaitAsync(Bound).GetAwaiter().GetResult();
            },
            sync: path =>
            {
                RequireWorker();
                bool parent = path == fixture.Root;
                Require(parent || path == fixture.LinkRoot, "Directory sync escaped the private fixture.");
                events.Enqueue(parent ? "parent-sync" : "final-sync");
                AndroidPrivateFileDurability.SyncDirectory(path);
                (parent ? parentEntered : finalEntered).TrySetResult(true);
                (parent ? parentRelease : finalRelease).Task.WaitAsync(Bound).GetAwaiter().GetResult();
            });
        Task<AndroidStagedLinkedCharacter?> stage;
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            stage = service.StageAsync(Target, CancellationToken.None);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        try
        {
            await RequireBarrierAsync(parentEntered.Task, stage, "parent sync");
            Require(!stage.IsCompleted && fixture.Files().Length == 0,
                "Staging wrote bytes or completed before parent directory sync.");
            parentRelease.TrySetResult(true);
            await RequireBarrierAsync(flushEntered.Task, stage, "file flush");
            Require(!stage.IsCompleted && fixture.Files() is [var temporary]
                && temporary.EndsWith(".tmp", StringComparison.Ordinal),
                "Staging published its final file before the file-flush barrier completed.");
            flushRelease.TrySetResult(true);
            await RequireBarrierAsync(finalEntered.Task, stage, "final directory sync");
            Require(!stage.IsCompleted, "Staging returned before final directory sync acknowledged durability.");
            string final = fixture.OnlyFinal();
            Require(File.ReadAllBytes(final).SequenceEqual(Documents.Payload),
                "The renamed file differs from the selected bytes.");
            finalRelease.TrySetResult(true);
            AndroidStagedLinkedCharacter? result = await stage.WaitAsync(Bound);
            Require(result is not null && result.FileName == final && result.Identity.CharacterName == "Nightshade",
                "Staging did not return the exact durable file with canonical decoded identity.");
            string expectedHash = Convert.ToHexStringLower(SHA256.HashData(Documents.Payload))[..16];
            Require(Path.GetFileName(final).Contains("-" + expectedHash + "-", StringComparison.Ordinal)
                && final.EndsWith($"-{fileId:N}.chum5", StringComparison.Ordinal),
                "The published path does not bind the actual selected-byte hash and exclusive invocation ID.");
            Require(events.SequenceEqual(["decode", "file-id", "parent-sync", "flush", "final-sync"]),
                "Canonical decode, staging identity and publication durability ran out of order.");
            Require(context.Posts == 0 && context.Sends == 0,
                "Staging completion used the caller SynchronizationContext.");
            fixture.RequireZeroed();
        }
        finally
        {
            parentRelease.TrySetResult(true);
            flushRelease.TrySetResult(true);
            finalRelease.TrySetResult(true);
            await DrainAsync(stage);
        }
    }

    private static async Task FaultsPreserveOnlyPostRenameFilesAsync()
    {
        foreach (string barrier in new[] { "parent-sync", "flush", "final-sync" })
        foreach (bool cancellation in new[] { false, true })
        {
            using var fixture = new Fixture();
            using var lifetime = new CancellationTokenSource();
            var events = new List<string>();
            void Visit(string step)
            {
                events.Add(step);
                if (step != barrier) return;
                if (cancellation)
                {
                    lifetime.Cancel();
                    throw new OperationCanceledException(lifetime.Token);
                }
                throw new IOException("Injected acknowledgement failure at " + step);
            }
            var service = fixture.Service(
                flush: stream => { stream.Flush(flushToDisk: true); Visit("flush"); },
                sync: path =>
                {
                    AndroidPrivateFileDurability.SyncDirectory(path);
                    Visit(path == fixture.Root ? "parent-sync" : "final-sync");
                });
            Exception? failure = await FailureAsync(service.StageAsync(Target, lifetime.Token));
            Require(cancellation ? failure is OperationCanceledException : failure is IOException,
                $"{barrier} did not propagate the injected {(cancellation ? "cancellation" : "I/O failure")}.");
            Require(events.SequenceEqual(ExpectedThrough(barrier)),
                "Staging continued past a failed durability acknowledgement.");
            if (barrier == "final-sync")
            {
                Require(File.ReadAllBytes(fixture.OnlyFinal()).SequenceEqual(Documents.Payload),
                    "Post-rename acknowledgement failure deleted or changed the exclusive final file.");
            }
            else Require(fixture.Files().Length == 0, "Pre-rename failure leaked a temporary or final file.");
            fixture.RequireZeroed();
        }
    }

    private static async Task CancellationRespectsRenameBoundaryAsync()
    {
        foreach (string barrier in new[] { "parent-sync", "flush", "final-sync" })
        {
            using var fixture = new Fixture();
            using var lifetime = new CancellationTokenSource();
            void CancelAt(string step) { if (step == barrier) lifetime.Cancel(); }
            var service = fixture.Service(
                flush: stream => { stream.Flush(flushToDisk: true); CancelAt("flush"); },
                sync: path =>
                {
                    AndroidPrivateFileDurability.SyncDirectory(path);
                    CancelAt(path == fixture.Root ? "parent-sync" : "final-sync");
                });
            Task<AndroidStagedLinkedCharacter?> stage = service.StageAsync(Target, lifetime.Token);
            if (barrier == "final-sync")
            {
                AndroidStagedLinkedCharacter? result = await stage.WaitAsync(Bound);
                Require(result is not null && result.FileName == fixture.OnlyFinal()
                    && File.ReadAllBytes(result.FileName).SequenceEqual(Documents.Payload),
                    "Late cancellation hid an already durably staged file from coordinator cleanup.");
            }
            else
            {
                Require(await FailureAsync(stage) is OperationCanceledException,
                    "Pre-rename cancellation did not abort staging.");
                Require(fixture.Files().Length == 0, "Canceled staging published or retained pre-rename bytes.");
            }
            fixture.RequireZeroed();
        }
    }

    private static async Task PickerCancellationDoesNotLeakSelectedBytesAsync()
    {
        using (var fixture = new Fixture())
        {
            using var lifetime = new CancellationTokenSource();
            lifetime.Cancel();
            Require(await FailureAsync(fixture.Service().StageAsync(Target, lifetime.Token)) is OperationCanceledException,
                "An already canceled staging request did not stop before the picker.");
            Require(fixture.Documents.OpenCalls == 0 && fixture.Files().Length == 0,
                "An already canceled staging request opened a picker or created a file.");
        }
        using (var fixture = new Fixture())
        {
            using var lifetime = new CancellationTokenSource();
            // A completed picker can hand back bytes despite a concurrent lifetime cancellation.
            fixture.Documents.OnReturn = lifetime.Cancel;
            Require(await FailureAsync(fixture.Service().StageAsync(Target, lifetime.Token)) is OperationCanceledException,
                "Cancellation after picker completion did not stop staging.");
            Require(fixture.Documents.OpenCalls == 1 && fixture.Documents.Returned.Count == 1
                && fixture.Files().Length == 0,
                "Picker-race cancellation staged bytes or skipped the actual selected-buffer fixture.");
            fixture.RequireZeroed();
        }
        using (var fixture = new Fixture())
        {
            fixture.Documents.ReturnNull = true;
            var service = fixture.Service(
                flush: _ => throw new InvalidOperationException("Canceled picker reached file flush."),
                sync: _ => throw new InvalidOperationException("Canceled picker reached directory sync."));
            Require(await service.StageAsync(Target, CancellationToken.None).WaitAsync(Bound) is null
                && fixture.Documents.OpenCalls == 1 && fixture.Files().Length == 0,
                "Picker dismissal staged a file or returned a linked document.");
        }
    }

    private static async Task CollisionCannotReplaceOrDeleteExistingFileAsync()
    {
        using var fixture = new Fixture();
        Guid identity = Guid.NewGuid();
        AndroidStagedLinkedCharacter? prior = await fixture.Service(fileId: () => identity)
            .StageAsync(Target, CancellationToken.None).WaitAsync(Bound);
        Require(prior is not null, "The real baseline staging operation returned no file.");
        byte[] original = File.ReadAllBytes(prior!.FileName);
        var events = new List<string>();
        var collision = fixture.Service(fileId: () => identity,
            flush: stream => { stream.Flush(flushToDisk: true); events.Add("flush"); },
            sync: path =>
            {
                AndroidPrivateFileDurability.SyncDirectory(path);
                events.Add(path == fixture.Root ? "parent-sync" : "final-sync");
            });
        Require(await FailureAsync(collision.StageAsync(Target, CancellationToken.None)) is IOException,
            "A colliding staging identity overwrote or reused an existing final path.");
        Require(fixture.Files() is [var retained] && retained == prior.FileName
            && File.ReadAllBytes(retained).SequenceEqual(original)
            && events.SequenceEqual(["parent-sync", "flush"]),
            "Collision cleanup deleted old bytes, leaked a temporary file or acknowledged a nonexistent publication.");
        fixture.RequireZeroed();
    }

    private static async Task DefaultDurabilityUsesRealFilesAsync()
    {
        using var fixture = new Fixture();
        var service = fixture.Service(); // No durability overrides: actual Flush(true) and directory fsync.
        AndroidStagedLinkedCharacter? result = await service.StageAsync(Target, CancellationToken.None).WaitAsync(Bound);
        Require(result is not null && result.FileName == fixture.OnlyFinal()
            && result.RelativeFileName == "linked-characters/" + Path.GetFileName(result.FileName)
            && result.Identity.CharacterName == "Nightshade"
            && File.ReadAllBytes(result.FileName).SequenceEqual(Documents.Payload),
            "The production durability defaults did not stage exact canonical runner bytes.");
        fixture.RequireZeroed();
    }

    private static void SharedDirectoryHelperPreservesJournalBackendBytes()
    {
        foreach (Sr5CareerCommandJournalDomain domain in new[]
            { Sr5CareerCommandJournalDomain.AfterRunReward, Sr5CareerCommandJournalDomain.Reputation })
        {
            using var fixture = new Fixture();
            // These are storage smoke payloads, not typed domain commands or fabricated receipts.
            const string first = "{\"storageSmoke\":\"first-ä-Ω\"}\n";
            const string second = "{\"storageSmoke\":\"replacement-ß-λ\"}\n";
            var backend = new FileSr5CareerCommandJournalBackend(fixture.Root, domain);
            Require(backend.Read() == string.Empty, "A fresh test-private journal was unexpectedly populated.");
            backend.Write(first);
            var cold = new FileSr5CareerCommandJournalBackend(fixture.Root, domain);
            Require(cold.Read() == first, "Journal bytes did not survive a new backend instance after initial write.");
            string[] initialFiles = fixture.Files();
            Require(initialFiles is [var initial] && initial.EndsWith(".v1.json", StringComparison.Ordinal)
                && Path.GetDirectoryName(initial) == fixture.Root,
                "Initial journal publication retained temporary files or escaped its fixture.");
            cold.Write(second);
            Require(new FileSr5CareerCommandJournalBackend(fixture.Root, domain).Read() == second
                && fixture.Files().SequenceEqual(initialFiles)
                && File.ReadAllBytes(initialFiles[0]).SequenceEqual(Encoding.UTF8.GetBytes(second)),
                "Journal replacement/cold read changed exact bytes, retained temporary files or created another journal.");

            RequireDirectorySyncIOException(initialFiles[0]); // A regular file is not an fsync-able directory authority.
            RequireDirectorySyncIOException(Path.Combine(fixture.Root, "nonexistent-directory"));
            Require(fixture.Files().SequenceEqual(initialFiles)
                && new FileSr5CareerCommandJournalBackend(fixture.Root, domain).Read() == second,
                "Rejected directory-sync targets mutated the existing journal or created a missing path.");
        }
    }

    private static void RequireDirectorySyncIOException(string path)
    {
        bool rejected = false;
        try { AndroidPrivateFileDurability.SyncDirectory(path); }
        catch (IOException) { rejected = true; }
        Require(rejected, "Directory sync accepted a regular file or nonexistent path.");
    }

    private static IEnumerable<string> ExpectedThrough(string step)
        => step switch
        {
            "parent-sync" => ["parent-sync"],
            "flush" => ["parent-sync", "flush"],
            _ => ["parent-sync", "flush", "final-sync"]
        };

    private static TaskCompletionSource<bool> Signal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task RequireBarrierAsync(Task entered, Task stage, string name)
    {
        await Task.WhenAny(entered, stage).WaitAsync(Bound);
        if (stage.IsFaulted) await stage; // Preserve a codec/worker assertion instead of masking it as a missing barrier.
        Require(entered.IsCompletedSuccessfully, "Staging completed without reaching the " + name + " barrier.");
    }

    private static async Task<Exception?> FailureAsync(Task stage)
    {
        try { await stage.WaitAsync(Bound); return null; }
        catch (Exception failure) when (failure is not TimeoutException) { return failure; }
    }

    private static async Task DrainAsync(Task stage)
    {
        try { await stage.WaitAsync(Bound); }
        catch (Exception failure) when (failure is not TimeoutException) { }
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-linked-durability-").FullName;
        public string LinkRoot => Path.Combine(Root, "linked-characters");
        public Documents Documents { get; } = new();

        public AndroidLinkedCharacterFileService Service(Action<FileStream>? flush = null,
            Action<string>? sync = null, Func<Guid>? fileId = null, ICharacterLinkedDocumentCodec? codec = null)
            => new(Documents, codec ?? new Chummer5LinkedDocumentCodec(), () => Root, fileId ?? Guid.NewGuid,
                flushFile: flush, syncDirectory: sync);

        public string[] Files() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories);
        public string OnlyFinal()
        {
            string[] files = Files();
            Require(files is [var file] && file.EndsWith(".chum5", StringComparison.Ordinal)
                && Path.GetDirectoryName(file) == LinkRoot,
                "Expected exactly one final file under this fixture's private linked-character directory.");
            return files[0];
        }
        public void RequireZeroed()
            => Require(Documents.Returned.Count > 0 && Documents.AllReturnedZero,
                "Staging retained a selected document buffer after completion or failure.");
        public void Dispose() => Directory.Delete(Root, recursive: true); // Only this newly created test-private root.
    }

    private sealed class ObservingCanonicalCodec(Action observe) : ICharacterLinkedDocumentCodec
    {
        private readonly Chummer5LinkedDocumentCodec _canonical = new();

        public bool TryDecode(string fileName, ReadOnlySpan<byte> content, out CharacterLinkedDocument document)
        {
            observe();
            return _canonical.TryDecode(fileName, content, out document);
        }
    }

    private sealed class Documents : IAndroidDocumentService
    {
        public static readonly byte[] Payload = Encoding.UTF8.GetBytes(
            "<character><name>Test Runner</name><alias>Nightshade</alias><metatype>Human</metatype></character>");
        public readonly List<byte[]> Returned = [];
        public bool AllReturnedZero => Returned.All(bytes => bytes.All(value => value == 0));
        public int OpenCalls { get; private set; }
        public bool ReturnNull { get; set; }
        public Action? OnReturn { get; set; }
        public Task<AndroidDocument?> OpenAsync(CancellationToken cancellationToken)
        {
            OpenCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (ReturnNull) return Task.FromResult<AndroidDocument?>(null);
            byte[] bytes = Payload.ToArray();
            Returned.Add(bytes);
            OnReturn?.Invoke(); // Deliberately do not honor cancellation after picker selection.
            return Task.FromResult<AndroidDocument?>(new("runner.chum5", "content://test/durability", "application/xml", bytes));
        }
        public Task<bool> SaveAsAsync(string name, string mediaType, Stream content, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Staging must not export a runner.");
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private int _posts;
        private int _sends;
        public int Posts => Volatile.Read(ref _posts);
        public int Sends => Volatile.Read(ref _sends);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
        public override void Send(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _sends);
            callback(state);
        }
    }
}
