using System.Reflection;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;

internal static partial class AfterRunAuthorityHarness
{
    private static async Task RunOutputOwnerCasesAsync(string contentRoot)
    {
        var failures = new List<string>();
        foreach (string kind in new[] { "download", "export", "print" })
        foreach (string scenario in new[] { "same", "stale-b", "stale-aba", "queued-b", "queued-aba" })
        {
            try { await RunOutputOwnerAsync(contentRoot, kind, scenario); }
            catch (Exception error) { failures.Add(kind + "/" + scenario + ": " + error.Message); }
        }
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS 15 actual output original-owner cases");
        foreach (string kind in new[] { "download", "export", "print" })
        foreach (string phase in new[] { "before", "after" })
        foreach (string change in new[] { "b", "aba", "revision", "cancel" })
        {
            try { await RunDelayedOutputOwnerAsync(contentRoot, kind, phase, change); }
            catch (Exception error) { failures.Add(kind + "/" + phase + "/" + change + ": " + error.Message); }
        }
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS 24 actual delayed output/reentry/cancellation cases");
    }

    private static async Task RunDelayedOutputOwnerAsync(string contentRoot, string kind, string phase, string change)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var output = new OutputProbe();
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            outputDocuments: output, outputSystem: output);
        await InitializeReputationOwnerAsync(runtime);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        async Task Block()
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        if (phase == "before") output.BeforeRead = Block; else output.AfterRead = Block;
        Task Start(CancellationToken ct) => kind switch
        {
            "download" => runtime.Presenter.DownloadAsync(ct),
            "export" => runtime.Coordinator.ExportAsync(ct),
            _ => runtime.Coordinator.PrintAsync(ct)
        };
        Task pending = Start(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (change == "cancel") cancellation.Cancel();
            else
            {
                if (change == "revision")
                    Require((await runtime.Client.UpdateMetadataAsync(runtime.Id, 1, PersistenceMetadata(), default)).Success,
                        "Revision control mutation failed.");
                else
                {
                    owners.Set(ContactsOwnerB);
                    if (change == "aba") owners.Set(ContactsOwnerA);
                }
                await runtime.Presenter.LoadAsync(runtime.Id, default);
            }
        }
        finally { release.TrySetResult(); }
        try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) when (change == "cancel") { }
        var outputGate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_outputGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        Require(await outputGate.WaitAsync(TimeSpan.FromSeconds(5)), "Late output failed to drain.");
        outputGate.Release();
        Require(output.Deliveries == (phase == "before" ? 0 : 1), "Delayed output crossed a revoked context or replayed.");
        string? notice = (string?)typeof(RunnerSessionCoordinator).GetField("_notice", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime.Coordinator);
        Require(notice is null || (!notice.StartsWith("Saved ", StringComparison.Ordinal)
            && notice != "Print dialog opened."), "Late output published a success notice after its context changed.");
        output.BeforeRead = null;
        output.AfterRead = null;
        await Start(default).WaitAsync(TimeSpan.FromSeconds(10));
        Require(await outputGate.WaitAsync(TimeSpan.FromSeconds(5)), "Fresh output failed to drain.");
        outputGate.Release();
        Require(output.Deliveries == (phase == "before" ? 1 : 2), "A fresh current-owner output was suppressed by old output identity.");
        Console.WriteLine("PASS delayed output: " + kind + "/" + phase + "/" + change);
    }

    private static async Task RunOutputOwnerAsync(string contentRoot, string kind, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        var output = new OutputProbe();
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            persistenceRoaming: roaming, outputDocuments: output, outputSystem: output);
        await InitializeReputationOwnerAsync(runtime);
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        void ChangeOwner()
        {
            owners.Set(ContactsOwnerB);
            if (scenario.EndsWith("aba", StringComparison.Ordinal)) owners.Set(ContactsOwnerA);
        }
        if (scenario.StartsWith("stale-", StringComparison.Ordinal)) ChangeOwner();
        Task? blocker = null;
        if (scenario.StartsWith("queued-", StringComparison.Ordinal))
        {
            roaming.BlockInbound = true;
            blocker = runtime.Client.ListWorkspacesAsync(default);
            await roaming.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Task pending = kind switch
        {
            "download" => runtime.Presenter.DownloadAsync(default),
            "export" => runtime.Coordinator.ExportAsync(),
            _ => runtime.Coordinator.PrintAsync()
        };
        if (blocker is not null)
        {
            ChangeOwner();
            roaming.BlockInbound = false;
            roaming.Release.TrySetResult();
            try { await blocker.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (InvalidOperationException) { }
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(20));
        var outputGate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_outputGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        Require(await outputGate.WaitAsync(TimeSpan.FromSeconds(5)), "Output delivery did not drain.");
        outputGate.Release();
        var after = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        Require(before.All(pair => after[pair.Key] == pair.Value), "Output mutated a workspace partition.");
        bool same = scenario == "same";
        Require(same ? output.Deliveries == 1 : output.Deliveries == 0,
            $"Expected {(same ? 1 : 0)} original-authorized output deliveries; got {output.Deliveries}. {runtime.Presenter.State.Error}");
        if (!same)
            Require(runtime.Presenter.State.PendingDownload is null && runtime.Presenter.State.PendingExport is null
                && runtime.Presenter.State.PendingPrint is null, "A stale output remained available in a replacement context.");
        Require(owners.ActiveLeases == 0, "Output leaked the owner lease.");
        Console.WriteLine("PASS actual output owner: " + kind + "/" + scenario);
    }

    private sealed class OutputProbe : IAndroidDocumentService, IAndroidSystemService
    {
        public int Deliveries { get; private set; }
        public Func<Task>? BeforeRead { get; set; }
        public Func<Task>? AfterRead { get; set; }
        public Task<AndroidDocument?> OpenAsync(CancellationToken ct) => throw new InvalidOperationException("No input expected.");
        public async Task<bool> SaveAsAsync(string name, string mediaType, Stream content, Func<bool> isCurrent, CancellationToken ct)
        {
            if (BeforeRead is not null) await BeforeRead();
            if (!isCurrent()) throw new OperationCanceledException("Original output context changed.");
            return await ConsumeAsync(content, ct);
        }
        public async Task<bool> SaveAsAsync(string name, string mediaType, Stream content, CancellationToken ct)
        {
            if (BeforeRead is not null) await BeforeRead();
            return await ConsumeAsync(content, ct);
        }
        private async Task<bool> ConsumeAsync(Stream content, CancellationToken ct)
        {
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes, ct);
            Require(bytes.Length > 0, "Original export bytes missing.");
            Deliveries++;
            if (AfterRead is not null) await AfterRead();
            return true;
        }
        public Task<bool> PrintPdfAsync(string name, string content, string title, CancellationToken ct)
        {
            Require(Convert.FromBase64String(content).Length > 0, "Original print bytes missing.");
            Deliveries++;
            return Task.FromResult(true);
        }
        public async Task<bool> PrintPdfAsync(string name, string content, string title, Func<bool> isCurrent, CancellationToken ct)
        {
            if (BeforeRead is not null) await BeforeRead();
            if (!isCurrent()) return false;
            bool result = await PrintPdfAsync(name, content, title, ct);
            if (AfterRead is not null) await AfterRead();
            return result;
        }
        public Task<bool> OpenUriAsync(Uri uri) => throw new InvalidOperationException("No external URI expected.");
        public Task<AndroidUpdateCheckResult> CheckForUpdatesAsync() => throw new InvalidOperationException("No update expected.");
        public Task ShareTextAsync(string text) => throw new InvalidOperationException("No share expected.");
    }
}
