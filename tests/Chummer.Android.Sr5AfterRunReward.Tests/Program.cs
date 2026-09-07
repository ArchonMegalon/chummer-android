using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Android.Sr5AfterRunReward.Tests;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;

if (args.Length == 6 && args[0] == "--recover-process")
    return await RewardPhoneTests.RunRecoveryChild(args);

var cases = RewardPhoneTests.Cases.Concat(RewardPhoneTests.PhoneModelCases).Concat(JournalOwnershipTests.Cases).ToArray();
int failed = 0;
foreach ((string name, Func<Task> run) in cases)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} real-Core Android reward tests passed.");
return failed == 0 ? 0 : 1;

internal static partial class RewardPhoneTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> Cases =>
    [
        (nameof(PreviewAndCommitRunOffCallerThread), PreviewAndCommitRunOffCallerThread),
        (nameof(ExactConfirmedCommandIsDurableBeforeCommit), ExactConfirmedCommandIsDurableBeforeCommit),
        (nameof(ExplicitNoAwardProducesReceiptWithoutCurrencyMutation), ExplicitNoAwardProducesReceiptWithoutCurrencyMutation),
        (nameof(DefaultZeroAndInvalidDatesNeverReachCommit), DefaultZeroAndInvalidDatesNeverReachCommit),
        (nameof(AssociationUsesSelectedIdWithoutNewCredit), AssociationUsesSelectedIdWithoutNewCredit),
        (nameof(DuplicateEventsReuseOriginalOperation), DuplicateEventsReuseOriginalOperation),
        (nameof(StaleApplyingCannotRecreateReleasedOwner), StaleApplyingCannotRecreateReleasedOwner),
        (nameof(StaleCoordinatorReconcilesWinnerThroughCoreLookup), () => StaleCoordinatorReconcilesWinnerThroughCoreLookup(false)),
        ("StaleCoordinatorReconcilesRecreatedOwnerThroughCoreLookup", () => StaleCoordinatorReconcilesWinnerThroughCoreLookup(true)),
        (nameof(LostCommitAcknowledgementRecoversFromColdCoreAndHost), LostCommitAcknowledgementRecoversFromColdCoreAndHost),
        (nameof(NewProcessRecoversApplyingWithoutOldProcessProof), NewProcessRecoversApplyingWithoutOldProcessProof),
        (nameof(NotFoundRecoveryDoesNotCommitUntilExplicitRetry), NotFoundRecoveryDoesNotCommitUntilExplicitRetry),
        (nameof(ConfirmedWriteAcknowledgementLossRetainsOriginalIdentity), ConfirmedWriteAcknowledgementLossRetainsOriginalIdentity),
        (nameof(ApplyingWriteAcknowledgementLossRequiresDurableRetry), ApplyingWriteAcknowledgementLossRequiresDurableRetry),
        (nameof(ReceiptWriteFailureKeepsOwnerUntilColdLookup), ReceiptWriteFailureKeepsOwnerUntilColdLookup),
        (nameof(ReleaseFailureRecoversOriginalOwnerAfterLaterEdit), ReleaseFailureRecoversOriginalOwnerAfterLaterEdit),
        (nameof(CancellationAfterCommitIsNotRollback), CancellationAfterCommitIsNotRollback),
        (nameof(OwnerSwitchDuringCommitKeepsRecoveryLock), OwnerSwitchDuringCommitKeepsRecoveryLock),
        (nameof(StalePreviewRejectsBeforeJournalOrCommit), StalePreviewRejectsBeforeJournalOrCommit),
        (nameof(ChangedSavedSourceRejectsEvenWithStaleHostBinding), ChangedSavedSourceRejectsEvenWithStaleHostBinding),
        (nameof(ChangedPreviewDigestAndOwnerFailClosed), ChangedPreviewDigestAndOwnerFailClosed),
        (nameof(LateWorkspaceConflictStaysVisiblyUnresolved), LateWorkspaceConflictStaysVisiblyUnresolved),
        (nameof(HistoricalReplayRetainsAllOperationMappings), HistoricalReplayRetainsAllOperationMappings),
        (nameof(SubstitutedCoherentReceiptCannotResolveOriginalReward), SubstitutedCoherentReceiptCannotResolveOriginalReward),
        (nameof(PresentEmptyAndBomOnlyFilesAreCorrupt), PresentEmptyAndBomOnlyFilesAreCorrupt)
    ];

    private static async Task PreviewAndCommitRunOffCallerThread()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int callerThread = 0;
        var thread = new Thread(() =>
        {
            try
            {
                callerThread = Environment.CurrentManagedThreadId;
                var read = host.Coordinator.ReadAsync().GetAwaiter().GetResult();
                Equal(CharacterAfterRunRewardOutcome.Available, read.Outcome);
                var review = Review(host).GetAwaiter().GetResult();
                Equal(1L, fixture.Store.Get(fixture.WorkspaceId).Value!.ContentRevision);
                Equal(0, host.Journal.Writes);
                Recorded(host.Coordinator.ConfirmAsync(review).GetAwaiter().GetResult());
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Require(host.Service.Threads.All(id => id != callerThread), "Core ran on the caller thread.");
        Require(host.Journal.WriteThreads.All(id => id != callerThread), "Journal ran on the caller thread.");
    }

    private static async Task ExactConfirmedCommandIsDurableBeforeCommit()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        var expected = review.Preview.Command with { ExplicitlyConfirmed = true };
        host.Service.BeforeCommit = command =>
        {
            Require(host.Store.TryReadOwned(host.Authority.Current.OwnerId, fixture.WorkspaceId,
                out var entries, out _), "Journal unreadable before Commit.");
            var pending = entries.Single();
            Equal(Sr5AfterRunRewardCheckpointPhase.Applying, pending.Phase);
            Equal(expected.CommandDigest(), pending.CommandDigest);
            Equal(expected, pending.Command);
            Equal(expected, command);
            Require(host.OwnerBackend.Read().Length > 0, "Shared owner absent before Commit.");
        };
        var result = await host.Coordinator.ConfirmAsync(review);
        Recorded(result);
        Equal(38, result.Checkpoint!.Receipt!.KarmaAfter);
        Equal(13500m, result.Checkpoint.Receipt.NuyenAfter);
        Equal(2L, result.CurrentWorkspaceRevision);
        Equal(string.Empty, host.OwnerBackend.Read());
        Equal(2, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.Expenses.Count);
        Require(fixture.Store.Get(fixture.WorkspaceId).Value!.Document.Content.Contains("Keep unrelated runner state"),
            "Unrelated XML changed.");
    }

    private static async Task ExplicitNoAwardProducesReceiptWithoutCurrencyMutation()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        string before = fixture.Store.Get(fixture.WorkspaceId).Value!.Document.Content;
        var review = await Review(host, Request(fixture) with
        { KarmaAmount = 0, NuyenAmount = 0, Kind = CharacterAfterRunRewardKind.NoAward });
        Equal(CharacterAfterRunRewardKind.NoAward, review.Preview.Kind);
        var result = await host.Coordinator.ConfirmAsync(review);
        Recorded(result);
        Equal(before, fixture.Store.Get(fixture.WorkspaceId).Value!.Document.Content);
        Equal(0, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.Expenses.Count);
        Equal(CharacterAfterRunRewardKind.NoAward, result.Checkpoint!.Receipt!.Kind);
        Equal(2L, result.Checkpoint.Receipt.CommittedWorkspaceRevision);
        Equal(30, result.Checkpoint.Receipt.KarmaAfter);
    }

    private static async Task DefaultZeroAndInvalidDatesNeverReachCommit()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        foreach (var request in new[]
        {
            Request(fixture) with { KarmaAmount = 0, NuyenAmount = 0 },
            Request(fixture) with { ExpenseDateLocal = DateTime.SpecifyKind(Request(fixture).ExpenseDateLocal, DateTimeKind.Utc) },
            Request(fixture) with { ExpenseDateLocal = Request(fixture).ExpenseDateLocal.AddTicks(1) }
        })
        {
            var preparation = await host.Coordinator.PreviewAsync(request);
            Require(preparation.Review is null, "Invalid request produced a review.");
        }
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
    }

    private static async Task AssociationUsesSelectedIdWithoutNewCredit()
    {
        Guid selected = Guid.NewGuid();
        Guid similar = Guid.NewGuid();
        var document = RealAfterRunRewardFixture.CreateDocument();
        XDocument xml = XDocument.Parse(document.Content);
        xml.Root!.Element("karma")!.Value = "46";
        xml.Root.Element("expenses")!.Add(ManualGain(selected), ManualGain(similar));
        using var fixture = new RealAfterRunRewardFixture(document with { State = document.State with { Payload = xml.ToString() } });
        using var host = new Host(fixture);
        var review = await Review(host, Request(fixture) with { ExistingKarmaExpenseId = selected });
        Require(review.Preview.KarmaAlreadyRecorded, "Explicit association missing.");
        Equal(46, review.Preview.KarmaAfter);
        var result = await host.Coordinator.ConfirmAsync(review);
        Recorded(result);
        Equal(selected, result.Checkpoint!.Receipt!.KarmaExpenseId!.Value);
        Equal(46, result.Checkpoint.Receipt.KarmaAfter);
        Equal(3, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.Expenses.Count);
        Equal(13500m, result.Checkpoint.Receipt.NuyenAfter);
    }

    private static async Task DuplicateEventsReuseOriginalOperation()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => host.Coordinator.ConfirmAsync(review)));
        Equal(string.Empty, host.OwnerBackend.Read(), "Duplicate callbacks left an owner behind before recovery.");
        Recorded(await host.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
        Equal(1, host.Service.CommitCalls);
        Equal(1, (await host.Coordinator.ReadHistoryAsync()).Count);
        Equal(2, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.Expenses.Count);
    }

    private static async Task StaleApplyingCannotRecreateReleasedOwner()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Service.AfterCommit = _ => throw new IOException("lost original response");
        Unknown(await host.Coordinator.ConfirmAsync(review));
        var stale = (await host.Coordinator.ReadHistoryAsync()).Single();
        Recorded(await host.Coordinator.RecoverAsync(stale.Command.OperationId));
        Equal(string.Empty, host.OwnerBackend.Read());
        Require(!host.Store.TryOwnApplying(stale, () => true, out _), "Stale Applying was re-owned.");
        Equal(string.Empty, host.OwnerBackend.Read(), "A stale journal snapshot recreated the released shared owner.");
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task StaleCoordinatorReconcilesWinnerThroughCoreLookup(bool recreateAtReservationBoundary)
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Service.AfterCommit = _ => throw new IOException("lost original response");
        Unknown(await host.Coordinator.ConfirmAsync(review));
        var originalApplying = (await host.Coordinator.ReadHistoryAsync()).Single();
        using var release = new ManualResetEventSlim();
        var paused = new PausingAuthority(host.Authority.Current, release);
        var probe = new ProbeService(fixture.ColdReopenService());
        var staleCoordinator = new Sr5AfterRunRewardCoordinator(probe, paused, host.Store);
        Task<Sr5AfterRunRewardResolution> stale = staleCoordinator.RecoverAsync(review.Preview.Command.OperationId);
        try
        {
            await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The stale caller has read Applying but has not entered ownership.
            Recorded(await host.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
            Equal(string.Empty, host.OwnerBackend.Read());
            if (recreateAtReservationBoundary)
            {
                // Deterministically model the narrow precheck/reservation gap
                // with the actual shared-owner primitive, not fabricated receipt
                // bytes: the late reservation succeeds, its journal CAS fails.
                var owners = new Sr5CareerMutationOwnerStore(host.OwnerBackend);
                Require(!owners.TryBegin(originalApplying.MutationOwner(),
                    () => new(false, false, "test late reservation sees advanced Applied journal"), out _),
                    "Fault boundary unexpectedly completed Applying.");
                Require(host.OwnerBackend.Read().Length > 0, "Late reservation fault was not established.");
            }
        }
        finally { release.Set(); }
        Recorded(await stale.WaitAsync(TimeSpan.FromSeconds(10)));
        Equal(0, probe.CommitCalls);
        Require(probe.LookupCalls > 0, "The stale caller trusted local Applied bytes without Core Lookup.");
        Equal(string.Empty, host.OwnerBackend.Read(), "The losing caller left a recreated owner behind.");
    }

    private static async Task LostCommitAcknowledgementRecoversFromColdCoreAndHost()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Service.AfterCommit = _ => throw new IOException("test lost response after real commit");
        Unknown(await host.Coordinator.ConfirmAsync(review));
        Require(host.OwnerBackend.Read().Length > 0, "Unknown result released owner.");
        using var cold = host.Cold();
        var result = await cold.Coordinator.RecoverAsync(review.Preview.Command.OperationId);
        Recorded(result);
        Equal(0, cold.Service.CommitCalls);
        Equal(1, host.Service.CommitCalls);
        Equal(review.Preview.Command.RewardId, result.Checkpoint!.Receipt!.RewardId);
        Equal(string.Empty, cold.OwnerBackend.Read());
    }

    private static async Task NotFoundRecoveryDoesNotCommitUntilExplicitRetry()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Service.BeforeCommit = _ => throw new IOException("test before Core entry");
        Unknown(await host.Coordinator.ConfirmAsync(review));
        using var cold = host.Cold();
        Unknown(await cold.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
        Equal(0, cold.Service.CommitCalls);
        var result = await cold.Coordinator.RetryAsync(review.Preview.Command.OperationId);
        Recorded(result);
        Equal(1, cold.Service.CommitCalls);
        Equal(review.Preview.Command with { ExplicitlyConfirmed = true }, cold.Service.Commands.Single());
    }

    private static async Task NewProcessRecoversApplyingWithoutOldProcessProof()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Service.AfterCommit = _ => throw new IOException("lost acknowledgement after real atomic commit");
        Unknown(await host.Coordinator.ConfirmAsync(review));
        Equal(Sr5AfterRunRewardCheckpointPhase.Applying, (await host.Coordinator.ReadHistoryAsync()).Single().Phase);
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing current executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(RewardPhoneTests).Assembly.Location);
        foreach (string argument in new[] { "--recover-process", fixture.DirectoryPath, host.HostDirectory,
                     fixture.WorkspaceId.Value, host.Authority.Current.OwnerId.ToString("D"),
                     review.Preview.Command.OperationId.ToString("D") })
            start.ArgumentList.Add(argument);
        using Process child = Process.Start(start) ?? throw new InvalidOperationException("Could not start recovery process.");
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        Task<string> errors = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch { if (!child.HasExited) child.Kill(entireProcessTree: true); throw; }
        Equal(0, child.ExitCode, await errors);
        using JsonDocument proof = JsonDocument.Parse(await output);
        Require(proof.RootElement.GetProperty("ProcessId").GetInt32() != Environment.ProcessId,
            "Recovery did not execute in a fresh OS process.");
        Equal(0, proof.RootElement.GetProperty("CommitCalls").GetInt32());
        Equal("Recorded", proof.RootElement.GetProperty("Status").GetString());
        Equal((review.Preview.Command with { ExplicitlyConfirmed = true }).CommandDigest(),
            proof.RootElement.GetProperty("CommandDigest").GetString());
        Equal(string.Empty, host.OwnerBackend.Read());
        Equal(Sr5AfterRunRewardCheckpointPhase.Applied, (await host.Coordinator.ReadHistoryAsync()).Single().Phase);
        Equal(2, fixture.ColdReopenService().Read(fixture.WorkspaceId).Snapshot!.Expenses.Count);
    }

    internal static async Task<int> RunRecoveryChild(string[] arguments)
    {
        try
        {
            string root = Path.GetFullPath(arguments[1]);
            string journalDirectory = Path.GetFullPath(arguments[2]);
            if (!Path.GetFileName(root).StartsWith("chummer-android-after-run-reward-tests-", StringComparison.Ordinal)
                || journalDirectory != Path.Combine(root, "phone-journal"))
                throw new InvalidOperationException("Recovery child requires the exact generated test workspace.");
            var workspace = new CharacterWorkspaceId(arguments[3]);
            Guid owner = Guid.Parse(arguments[4]);
            Guid operation = Guid.Parse(arguments[5]);
            var coreStore = new FileWorkspaceStore(root);
            var saved = coreStore.Get(workspace).Value ?? throw new InvalidOperationException("Missing cold workspace.");
            var service = new ProbeService(new WorkspaceCharacterAfterRunRewardService(coreStore));
            var authority = new MutableAuthority(new(owner, workspace, 1, true, "SR5",
                saved.ContentRevision, saved.SavedRevision, false, null));
            var ownerBackend = new FileOwnerBackend(Path.Combine(journalDirectory, "shared-owner.json"));
            var journal = new Sr5AfterRunRewardCheckpointStore(new FileSr5AfterRunRewardJournalBackend(journalDirectory),
                new Sr5CareerMutationOwnerStore(ownerBackend));
            var result = await new Sr5AfterRunRewardCoordinator(service, authority, journal).RecoverAsync(operation);
            Recorded(result);
            Equal(0, service.CommitCalls);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Environment.ProcessId, service.CommitCalls, Status = result.Status.ToString(),
                result.Checkpoint!.CommandDigest
            }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task ConfirmedWriteAcknowledgementLossRetainsOriginalIdentity()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Journal.AfterWrite = write => { if (write == 1) throw new IOException("published Confirmed, lost ack"); };
        var unknown = await host.Coordinator.ConfirmAsync(review);
        Unknown(unknown);
        Equal(review.Preview.Command.OperationId, unknown.Checkpoint!.Command.OperationId);
        Equal(0, host.Service.CommitCalls);
        using var cold = host.Cold();
        Unknown(await cold.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
        Equal(0, cold.Service.CommitCalls);
        Recorded(await cold.Coordinator.RetryAsync(review.Preview.Command.OperationId));
        Equal(1, cold.Service.CommitCalls);
    }

    private static async Task ApplyingWriteAcknowledgementLossRequiresDurableRetry()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Journal.AfterWrite = write => { if (write == 2) throw new IOException("published Applying, lost ack"); };
        Unknown(await host.Coordinator.ConfirmAsync(review));
        Equal(0, host.Service.CommitCalls);
        using var cold = host.Cold();
        cold.Journal.BeforeWrite = _ => throw new IOException("durability repair still unavailable");
        Unknown(await cold.Coordinator.RetryAsync(review.Preview.Command.OperationId));
        Equal(0, cold.Service.CommitCalls);
        cold.Journal.BeforeWrite = null;
        Recorded(await cold.Coordinator.RetryAsync(review.Preview.Command.OperationId));
        Equal(1, cold.Service.CommitCalls);
    }

    private static async Task ReceiptWriteFailureKeepsOwnerUntilColdLookup()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Journal.BeforeWrite = payload =>
        {
            if (payload.Contains("\"Phase\":2", StringComparison.Ordinal)) throw new IOException("receipt write unavailable");
        };
        Unknown(await host.Coordinator.ConfirmAsync(review));
        Require(host.OwnerBackend.Read().Length > 0, "Receipt write failure released owner.");
        using var cold = host.Cold();
        Recorded(await cold.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task ReleaseFailureRecoversOriginalOwnerAfterLaterEdit()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.OwnerBackend.FailRemove = true;
        Unknown(await host.Coordinator.ConfirmAsync(review));
        Equal(Sr5AfterRunRewardCheckpointPhase.Applied, (await host.Coordinator.ReadHistoryAsync()).Single().Phase);
        SavedEdit(fixture);
        using var cold = host.Cold();
        cold.RefreshBinding();
        var result = await cold.Coordinator.RecoverAsync(review.Preview.Command.OperationId);
        Recorded(result);
        Equal(2L, result.Checkpoint!.Receipt!.CommittedWorkspaceRevision);
        Equal(3L, result.CurrentWorkspaceRevision);
        Equal(0, cold.Service.CommitCalls);
        Equal(string.Empty, cold.OwnerBackend.Read());
    }

    private static async Task CancellationAfterCommitIsNotRollback()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var cancellation = new CancellationTokenSource();
        var review = await Review(host);
        host.Service.AfterCommit = _ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); };
        Unknown(await host.Coordinator.ConfirmAsync(review, cancellation.Token));
        using var cold = host.Cold();
        Recorded(await cold.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
        Equal(0, cold.Service.CommitCalls);
        Equal(38, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.AvailableKarma);
    }

    private static async Task OwnerSwitchDuringCommitKeepsRecoveryLock()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        var original = host.Authority.Current;
        host.Service.AfterCommit = result =>
        {
            host.Authority.Current = original with { OwnerId = Guid.NewGuid() };
            return result;
        };
        Unknown(await host.Coordinator.ConfirmAsync(review));
        Equal(Sr5AfterRunRewardResolutionStatus.Blocked,
            (await host.Coordinator.RecoverAsync(review.Preview.Command.OperationId)).Status);
        Require(host.OwnerBackend.Read().Length > 0, "Owner switch released original lock.");
        host.Authority.Current = original;
        Recorded(await host.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
    }

    private static async Task StalePreviewRejectsBeforeJournalOrCommit()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        SavedEdit(fixture);
        host.RefreshBinding();
        Equal(Sr5AfterRunRewardResolutionStatus.Blocked, (await host.Coordinator.ConfirmAsync(review)).Status);
        Equal(0, host.Journal.Writes);
        Equal(0, host.Service.CommitCalls);
    }

    private static async Task ChangedSavedSourceRejectsEvenWithStaleHostBinding()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        SavedEdit(fixture); // deliberately do not refresh the host projection
        Equal(Sr5AfterRunRewardResolutionStatus.Blocked, (await host.Coordinator.ConfirmAsync(review)).Status);
        Equal(0, host.Journal.Writes);
        Equal(0, host.Service.CommitCalls);
    }

    private static async Task ChangedPreviewDigestAndOwnerFailClosed()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        foreach (var altered in new[]
        {
            review.Preview.Command with { KarmaAmount = 9 },
            review.Preview.Command with { ExpectedSourceDigest = new string('a', 64) },
            review.Preview.Command with { ExpectedAuxiliaryStateDigest = new string('b', 64) },
            review.Preview.Command with { RewardId = Guid.NewGuid() }
        })
            Equal(Sr5AfterRunRewardResolutionStatus.Blocked,
                (await host.Coordinator.ConfirmAsync(review with { Preview = review.Preview with { Command = altered } })).Status);
        host.Authority.Current = host.Authority.Current with { ActivationGeneration = 2 };
        Equal(Sr5AfterRunRewardResolutionStatus.Blocked, (await host.Coordinator.ConfirmAsync(review)).Status);
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
    }

    private static async Task LateWorkspaceConflictStaysVisiblyUnresolved()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var review = await Review(host);
        host.Service.BeforeCommit = _ => SavedEdit(fixture);
        Unknown(await host.Coordinator.ConfirmAsync(review));
        using var cold = host.Cold();
        cold.RefreshBinding();
        Unknown(await cold.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
        Equal(0, cold.Service.CommitCalls);
        Require(cold.OwnerBackend.Read().Length > 0, "Uncertain late conflict released its owner.");
        Require((await cold.Coordinator.PreviewAsync(Request(fixture))).Review is null,
            "Unresolved conflict silently prepared another reward.");
    }

    private static async Task HistoricalReplayRetainsAllOperationMappings()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var first = await Review(host);
        Recorded(await host.Coordinator.ConfirmAsync(first));
        host.RefreshBinding();
        var second = await Review(host, Request(fixture) with { KarmaAmount = 1, NuyenAmount = 0 });
        Recorded(await host.Coordinator.ConfirmAsync(second));
        SavedEdit(fixture);
        host.RefreshBinding();
        var historical = await host.Coordinator.ConfirmAsync(first); // duplicate old callback, not a new grant
        Recorded(historical);
        Equal(2L, historical.Checkpoint!.Receipt!.CommittedWorkspaceRevision);
        Equal(4L, historical.CurrentWorkspaceRevision);
        Equal(2, host.Service.CommitCalls);
        Equal(2, (await host.Coordinator.ReadHistoryAsync()).Count);
        Equal(39, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.AvailableKarma);
    }

    private static async Task SubstitutedCoherentReceiptCannotResolveOriginalReward()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var otherFixture = new RealAfterRunRewardFixture(workspaceId: new("different-runner"));
        using var host = new Host(fixture);
        var otherCommand = otherFixture.Service.Preview(Request(otherFixture)).Preview!.Command with { ExplicitlyConfirmed = true };
        var foreign = otherFixture.Service.Commit(otherCommand);
        var review = await Review(host);
        host.Service.AfterCommit = _ => foreign;
        Unknown(await host.Coordinator.ConfirmAsync(review));
        Require(host.OwnerBackend.Read().Length > 0, "Foreign coherent receipt released owner.");
        using var cold = host.Cold();
        Recorded(await cold.Coordinator.RecoverAsync(review.Preview.Command.OperationId));
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task PresentEmptyAndBomOnlyFilesAreCorrupt()
    {
        foreach (byte[] malformed in new[] { Array.Empty<byte>(), new byte[] { 0xef, 0xbb, 0xbf } })
        {
            using var fixture = new RealAfterRunRewardFixture();
            using var host = new Host(fixture);
            File.WriteAllBytes(Path.Combine(host.HostDirectory, "sr5-after-run-rewards.v1.json"), malformed);
            var preparation = await host.Coordinator.PreviewAsync(Request(fixture));
            Require(preparation.Review is null, "Existing truncated journal was treated as absent.");
            Equal(Sr5AfterRunRewardResolutionStatus.Blocked,
                (await host.Coordinator.RecoverAsync(Guid.NewGuid())).Status);
            Equal(0, host.Service.CommitCalls);
            Equal(0, host.Journal.Writes);
        }
    }

    private static CharacterAfterRunRewardPreviewRequest Request(RealAfterRunRewardFixture fixture)
        => new(fixture.WorkspaceId, Guid.NewGuid(), Guid.NewGuid(), 8, 12500,
            new DateTime(2078, 9, 6, 18, 30, 0, DateTimeKind.Unspecified), "Exact local reward reason");

    private static async Task<Sr5AfterRunRewardReview> Review(Host host, CharacterAfterRunRewardPreviewRequest? request = null)
    {
        var result = await host.Coordinator.PreviewAsync(request ?? Request(host.Fixture));
        Require(result.Review is not null, result.Blocker ?? "Missing Core review.");
        Require(!result.Review!.Preview.Command.ExplicitlyConfirmed, "Preview synthesized confirmation.");
        return result.Review;
    }

    private static void SavedEdit(RealAfterRunRewardFixture fixture)
    {
        var saved = fixture.Store.Get(fixture.WorkspaceId).Value!;
        var document = saved.Document with { State = saved.Document.State with
        { Payload = saved.Document.Content.Replace("</notes>", " later edit</notes>", StringComparison.Ordinal) } };
        Require(fixture.Store.ReplaceWorkspaceDocument(fixture.WorkspaceId, saved.ContentRevision, document).Success,
            "Could not perform later saved edit.");
        Require(fixture.Store.SaveCheckpoint(fixture.WorkspaceId, saved.ContentRevision + 1).Success,
            "Could not save later edit.");
    }

    private static XElement ManualGain(Guid id) => new("expense",
        new XElement("guid", id.ToString("D")), new XElement("date", "2078-09-05T12:00:00"),
        new XElement("amount", 8), new XElement("reason", "Same displayed gain"),
        new XElement("type", "Karma"), new XElement("refund", "False"), new XElement("forcecareervisible", "False"),
        new XElement("undo", new XElement("karmatype", "ManualAdd"), new XElement("nuyentype", "AddCyberware"),
            new XElement("objectid"), new XElement("qty", "0"), new XElement("extra")));

    private static void Recorded(Sr5AfterRunRewardResolution result)
        => Equal(Sr5AfterRunRewardResolutionStatus.Recorded, result.Status, result.Message);
    private static void Unknown(Sr5AfterRunRewardResolution result)
        => Equal(Sr5AfterRunRewardResolutionStatus.OutcomeUnknown, result.Status, result.Message);
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string? message = null)
        => Require(EqualityComparer<T>.Default.Equals(expected, actual),
            message ?? $"Expected {expected}, actual {actual}.");

    private sealed class MutableAuthority(Sr5AfterRunRewardRunnerBinding initial) : ISr5AfterRunRewardRunnerAuthority
    {
        private Sr5AfterRunRewardRunnerBinding _current = initial;
        public Sr5AfterRunRewardRunnerBinding Current
        { get => Volatile.Read(ref _current); set => Volatile.Write(ref _current, value); }
    }

    private sealed class PausingAuthority(Sr5AfterRunRewardRunnerBinding binding,
        ManualResetEventSlim release) : ISr5AfterRunRewardRunnerAuthority
    {
        private int _reads;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Sr5AfterRunRewardRunnerBinding Current
        {
            get
            {
                if (Interlocked.Increment(ref _reads) == 2)
                {
                    Entered.SetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Stale caller test was not released.");
                }
                return binding;
            }
        }
    }

    private sealed class Host : IDisposable
    {
        internal readonly RealAfterRunRewardFixture Fixture;
        internal readonly string HostDirectory;
        internal readonly MutableAuthority Authority;
        internal readonly FaultJournal Journal;
        internal readonly FileOwnerBackend OwnerBackend;
        internal readonly Sr5AfterRunRewardCheckpointStore Store;
        internal readonly ProbeService Service;
        internal readonly Sr5AfterRunRewardCoordinator Coordinator;

        internal Host(RealAfterRunRewardFixture fixture, string? existingDirectory = null,
            Sr5AfterRunRewardRunnerBinding? binding = null)
        {
            Fixture = fixture;
            HostDirectory = existingDirectory ?? Directory.CreateDirectory(Path.Combine(fixture.DirectoryPath, "phone-journal")).FullName;
            Authority = new(binding ?? new(Guid.NewGuid(), fixture.WorkspaceId, 1, true, "SR5", 1, 1, false, null));
            Journal = new(new FileSr5AfterRunRewardJournalBackend(HostDirectory));
            OwnerBackend = new(Path.Combine(HostDirectory, "shared-owner.json"));
            Store = new(Journal, new Sr5CareerMutationOwnerStore(OwnerBackend));
            Service = new(fixture.ColdReopenService());
            Coordinator = new(Service, Authority, Store);
        }
        internal Host Cold() => new(Fixture, HostDirectory, Authority.Current);
        internal void RefreshBinding()
        {
            var saved = Fixture.Store.Get(Fixture.WorkspaceId).Value!;
            Authority.Current = Authority.Current with { ContentRevision = saved.ContentRevision, SavedRevision = saved.SavedRevision };
        }
        public void Dispose() { } // The owning fixture removes only its generated temporary directory.
    }

    private sealed class FaultJournal(ISr5AfterRunRewardJournalBackend inner) : ISr5AfterRunRewardJournalBackend
    {
        internal int Writes;
        internal Action<string>? BeforeWrite;
        internal Action<int>? AfterWrite;
        internal readonly ConcurrentBag<int> WriteThreads = [];
        public string Read() => inner.Read();
        public void Write(string payload)
        {
            int number = Interlocked.Increment(ref Writes);
            WriteThreads.Add(Environment.CurrentManagedThreadId);
            BeforeWrite?.Invoke(payload);
            inner.Write(payload);
            AfterWrite?.Invoke(number);
        }
    }

    // Test-only file-backed shared-owner adapter. Production still composes the
    // existing MAUI owner backend; these tests do not certify Preferences fsync.
    private sealed class FileOwnerBackend(string path) : ISr5CareerCheckpointBackend
    {
        internal bool FailRemove;
        public string Read() => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        public void Write(string payload)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            stream.Write(bytes);
            stream.Flush(true);
        }
        public void Remove()
        {
            if (FailRemove) throw new IOException("test owner release unavailable");
            File.Delete(path);
        }
    }

    // Fault/observation wrapper only: successful mechanics and persistence always
    // execute through the actual Core service and real FileWorkspaceStore.
    private sealed class ProbeService(ICharacterAfterRunRewardService inner) : ICharacterAfterRunRewardService
    {
        internal int CommitCalls;
        internal int LookupCalls;
        internal Action? AfterRead;
        internal Action<CharacterAfterRunRewardCommand>? BeforeCommit;
        internal Func<CharacterAfterRunRewardResult, CharacterAfterRunRewardResult>? AfterCommit;
        internal readonly ConcurrentBag<int> Threads = [];
        internal readonly ConcurrentQueue<CharacterAfterRunRewardCommand> Commands = [];
        public CharacterAfterRunRewardReadResult Read(CharacterWorkspaceId id)
        { Threads.Add(Environment.CurrentManagedThreadId); var result = inner.Read(id); AfterRead?.Invoke(); return result; }
        public CharacterAfterRunRewardPreviewResult Preview(CharacterAfterRunRewardPreviewRequest request)
        { Threads.Add(Environment.CurrentManagedThreadId); return inner.Preview(request); }
        public CharacterAfterRunRewardResult Lookup(CharacterWorkspaceId id, Guid operationId, string digest)
        { Interlocked.Increment(ref LookupCalls); Threads.Add(Environment.CurrentManagedThreadId); return inner.Lookup(id, operationId, digest); }
        public CharacterAfterRunRewardResult Commit(CharacterAfterRunRewardCommand command, CancellationToken token = default)
        {
            Interlocked.Increment(ref CommitCalls);
            Threads.Add(Environment.CurrentManagedThreadId);
            Commands.Enqueue(command);
            BeforeCommit?.Invoke(command);
            var result = inner.Commit(command, token);
            return AfterCommit is null ? result : AfterCommit(result);
        }
    }
}
