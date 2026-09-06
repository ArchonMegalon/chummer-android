using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Sr5AfterRunReward.Tests;

/// <summary>
/// Host failure simulation only: journal/owner backends are deliberately in
/// memory. Commands, previews, receipts, and saved runner state use real Core.
/// These cases do not establish Android storage or process-restart durability.
/// </summary>
internal static class JournalOwnershipTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> Cases
    {
        get
        {
            foreach (string corruption in new[] { "syntax", "root-duplicate", "nested-duplicate", "digest", "unknown-property" })
                yield return ($"journal corruption fails closed: {corruption}", () => CorruptJournal(corruption));
            foreach (string fault in new[] { "before-write", "after-write", "readback" })
            {
                yield return ($"Confirmed fault never calls Core: {fault}", () => ConfirmedFaultNeverCallsCore(fault));
                yield return ($"Applying fault preserves original intent: {fault}", () => ApplyingFaultPreservesIntent(fault));
                yield return ($"owner reservation fault preserves journal: {fault}", () => OwnerReservationFault(fault));
            }
            yield return ("stale ownership before Begin makes no reservation", StaleBeforeBegin);
            yield return ("reservation race releases only exact Confirmed", () => OwnershipChangesDuringReservation(false));
            yield return ("reservation race retains owner with changed journal", () => OwnershipChangesDuringReservation(true));
            foreach (string fault in new[] { "before-remove", "after-remove", "unacknowledged-remove" })
                yield return ($"owner release fault requires explicit reconciliation: {fault}", () => OwnerReleaseFault(fault));
            yield return ("cross-domain ownership blocks reward writes", CrossDomainOwnership);
            yield return ("execution lease must end before receipt resolution", LeaseMustEndBeforeResolution);
            yield return ("coherent receipt without process proof cannot resolve", UnprovenReceiptCannotResolve);
            yield return ("lookup-only journal reads never release Applying owner", ReadsNeverReleaseApplyingOwner);
        }
    }

    private static Task CorruptJournal(string corruption)
    {
        using var context = new Context();
        context.Prepare();
        context.Journal.Payload = corruption switch
        {
            "syntax" => "{",
            "root-duplicate" => context.Journal.Payload.Replace(
                "\"SchemaVersion\":1", "\"SchemaVersion\":1,\"SchemaVersion\":1", StringComparison.Ordinal),
            "nested-duplicate" => context.Journal.Payload.Replace(
                "\"Reason\":", "\"Reason\":\"duplicate\",\"Reason\":", StringComparison.Ordinal),
            "digest" => context.Journal.Payload.Replace("\"Digest\":\"", "\"Digest\":\"0", StringComparison.Ordinal),
            _ => context.Journal.Payload.Insert(1, "\"Unexpected\":true,")
        };
        string damaged = context.Journal.Payload;
        int writes = context.Journal.WriteCount;
        Check(!context.Store.TryReadOwned(context.OwnerId, context.Core.WorkspaceId, out var entries, out string blocker),
            "Corrupt journal was accepted.");
        Check(entries.Count == 0 && !string.IsNullOrWhiteSpace(blocker), "Corrupt read returned entries or no blocker.");
        Check(!context.Store.TryGet(context.OwnerId, context.Core.WorkspaceId, context.Confirmed.Command.OperationId,
            out var found, out _), "Corrupt exact lookup succeeded.");
        Check(found is null, "Corrupt exact lookup returned a checkpoint.");
        Check(!context.Store.TryPrepare(context.Confirmed, () => true, out _), "Corrupt history allowed a new prepare.");
        Check(!context.Store.TryBegin(context.Confirmed, () => true, out _, out _), "Corrupt history allowed Begin.");
        Equal(damaged, context.Journal.Payload, "Corrupt bytes were replaced.");
        Equal(writes, context.Journal.WriteCount, "Corrupt history caused a journal write.");
        Equal(0, context.Owner.WriteCount, "Corrupt history reserved ownership.");
        context.AssertCoreUnchanged();
        return Task.CompletedTask;
    }

    private static async Task ConfirmedFaultNeverCallsCore(string fault)
    {
        using var context = new Context();
        InjectWriteFault(context.Journal, fault);
        var counted = new CountingCoreService(context.Core.Service);
        var coordinator = new Sr5AfterRunRewardCoordinator(counted,
            new FixedAuthority(context.Review.Runner), context.Store);
        Sr5AfterRunRewardResolution result = await coordinator.ConfirmAsync(context.Review);
        Equal(Sr5AfterRunRewardResolutionStatus.OutcomeUnknown, result.Status,
            "Unacknowledged Confirmed write lost its uncertain persistence status.");
        Check(result.Checkpoint is not null
            && result.Checkpoint.CommandDigest == context.Confirmed.CommandDigest,
            "Uncertain Confirmed persistence did not return the exact original command.");
        Equal(0, counted.CommitCount, "Confirmed journal fault entered Core Commit.");
        Equal(0, counted.LookupCount, "Confirmed journal fault began Core execution lookup.");
        Equal(0, context.Owner.WriteCount, "Confirmed journal fault reserved shared ownership.");
        context.Journal.ClearFaults();
        if (fault == "before-write")
            Equal(string.Empty, context.Journal.Payload, "Before-write fault unexpectedly published bytes.");
        else
            Equal(Sr5AfterRunRewardCheckpointPhase.Confirmed, context.ReadCheckpoint().Phase,
                "Published Confirmed intent was lost after acknowledgement failure.");
        context.AssertCoreUnchanged();
    }

    private static Task StaleBeforeBegin()
    {
        using var context = new Context();
        context.Prepare();
        string confirmedBytes = context.Journal.Payload;
        Check(!context.Store.TryBegin(context.Confirmed, () => false, out var applying, out _), "Stale owner began Applying.");
        Check(applying is null, "Stale owner returned Applying.");
        Equal(0, context.Owner.WriteCount, "Stale precheck reserved owner.");
        Equal(0, context.Owner.RemoveCount, "Stale precheck attempted an owner release.");
        Equal(confirmedBytes, context.Journal.Payload, "Stale precheck changed Confirmed intent.");
        context.AssertCoreUnchanged();
        return Task.CompletedTask;
    }

    private static Task OwnershipChangesDuringReservation(bool damageJournal)
    {
        using var context = new Context();
        context.Prepare();
        string confirmedBytes = context.Journal.Payload;
        int checks = 0;
        bool Owns()
        {
            if (++checks == 1) return true;
            if (damageJournal) context.Journal.Payload = "{";
            return false;
        }
        Check(!context.Store.TryBegin(context.Confirmed, Owns, out var applying, out _), "Changed owner began Applying.");
        Check(applying is null && checks == 2, "Reservation race did not recheck ownership.");
        Equal(1, context.Owner.WriteCount, "Reservation callback did not run after exact owner reservation.");
        if (damageJournal)
        {
            Equal("{", context.Journal.Payload, "Uncertain journal was rolled back.");
            Equal(0, context.Owner.RemoveCount, "Owner released without exact Confirmed recovery.");
            context.AssertOwnerMatches(context.Confirmed);
        }
        else
        {
            Equal(confirmedBytes, context.Journal.Payload, "Exact Confirmed journal changed.");
            Equal(1, context.Owner.RemoveCount, "Exact Confirmed rollback did not release its reservation.");
            Equal(string.Empty, context.Owner.Payload, "Exact Confirmed rollback retained owner.");
        }
        context.AssertCoreUnchanged();
        return Task.CompletedTask;
    }

    private static Task ApplyingFaultPreservesIntent(string fault)
    {
        using var context = new Context();
        context.Prepare();
        string confirmedBytes = context.Journal.Payload;
        InjectWriteFault(context.Journal, fault);
        Check(!context.Store.TryBegin(context.Confirmed, () => true, out var applying, out _),
            "Unacknowledged Applying write was accepted.");
        Check(applying is null, "Failed Applying transition returned success state.");
        context.Journal.ClearFaults();
        Equal(2, context.Journal.WriteCount, "Applying failure rewrote or rolled back journal bytes.");
        if (fault == "before-write")
        {
            Equal(confirmedBytes, context.Journal.Payload, "Before-write failure lost exact Confirmed state.");
            Equal(string.Empty, context.Owner.Payload, "Exact Confirmed recovery did not release ownership.");
            Equal(1, context.Owner.RemoveCount, "Exact Confirmed recovery did not release once.");
        }
        else
        {
            Sr5AfterRunRewardCheckpoint retained = context.ReadCheckpoint();
            Equal(Sr5AfterRunRewardCheckpointPhase.Applying, retained.Phase, "Published Applying intent was rolled back.");
            Equal(context.Confirmed.CommandDigest, retained.CommandDigest, "Applying fault re-keyed command.");
            Equal(0, context.Owner.RemoveCount, "Uncertain Applying write released ownership.");
            context.AssertOwnerMatches(retained);
            Check(context.Store.TryOwnApplying(retained, () => true, out _), "Original Applying intent could not be re-owned.");
        }
        context.AssertCoreUnchanged();
        return Task.CompletedTask;
    }

    private static Task OwnerReservationFault(string fault)
    {
        using var context = new Context();
        context.Prepare();
        string confirmedBytes = context.Journal.Payload;
        InjectWriteFault(context.Owner, fault);
        Check(!context.Store.TryBegin(context.Confirmed, () => true, out var applying, out _), "Owner write fault began Applying.");
        Check(applying is null, "Owner write fault returned Applying checkpoint.");
        context.Owner.ClearFaults();
        Equal(confirmedBytes, context.Journal.Payload, "Owner write fault altered domain journal.");
        Equal(1, context.Journal.WriteCount, "Owner write fault called Applying persistence.");
        Equal(0, context.Owner.RemoveCount, "Unknown owner reservation was removed.");
        if (fault == "before-write") Equal(string.Empty, context.Owner.Payload, "Before-write owner fault published bytes.");
        else context.AssertOwnerMatches(context.Confirmed);
        context.AssertCoreUnchanged();
        return Task.CompletedTask;
    }

    private static Task OwnerReleaseFault(string fault)
    {
        using var context = new Context();
        Sr5AfterRunRewardCheckpoint applying = context.Begin();
        var observation = context.CommitAndObserve(applying);
        if (fault == "before-remove") context.Owner.BeforeRemove = () => throw new IOException("Simulated remove failure.");
        else if (fault == "after-remove") context.Owner.AfterRemove = () => throw new IOException("Simulated lost remove acknowledgement.");
        else context.Owner.IgnoreRemove = true;
        Check(!context.Store.TryRecordReceipt(applying, observation, () => true, out var applied, out _),
            "Unacknowledged owner release claimed resolved state.");
        Check(applied is null, "Owner release fault returned success checkpoint.");
        Sr5AfterRunRewardCheckpoint retained = context.ReadCheckpoint();
        Equal(Sr5AfterRunRewardCheckpointPhase.Applied, retained.Phase, "Applied journal was lost after owner release fault.");
        Equal(observation.Receipt.ReceiptDigest, retained.Receipt!.ReceiptDigest, "Applied journal lost its real receipt.");
        int removes = context.Owner.RemoveCount;
        string ownerBytes = context.Owner.Payload;
        string journalBytes = context.Journal.Payload;
        Check(context.Store.TryReadOwned(context.OwnerId, context.Core.WorkspaceId, out var history, out _), "Applied history read failed.");
        Equal(1, history.Count, "Applied history was hidden by owner release failure.");
        context.ReadCheckpoint();
        Equal(removes, context.Owner.RemoveCount, "Reading Applied history released owner.");
        Equal(ownerBytes, context.Owner.Payload, "Reading Applied history changed owner bytes.");
        Equal(journalBytes, context.Journal.Payload, "Reading Applied history rewrote journal.");
        context.Owner.ClearFaults();
        var recoveredObservation = context.ObserveFromColdCore(retained);
        Check(context.Store.TryRecordReceipt(retained, recoveredObservation, () => true, out var resolved, out string blocker),
            $"Exact Applied history could not reconcile owner: {blocker}");
        Check(resolved is not null && resolved.Phase == Sr5AfterRunRewardCheckpointPhase.Applied,
            "Reconciliation lost Applied phase.");
        Equal(string.Empty, context.Owner.Payload, "Explicit exact reconciliation did not clear owner.");
        Equal(journalBytes, context.Journal.Payload, "Reconciliation rewrote resolved journal.");
        return Task.CompletedTask;
    }

    private static Task CrossDomainOwnership()
    {
        using var context = new Context();
        context.Prepare();
        Sr5CareerMutationOwner different = context.Confirmed.MutationOwner() with
        {
            Domain = Sr5CareerMutationDomains.ActiveSkillAdvance,
            ActionId = Guid.Parse("55555555-5555-4555-8555-555555555555")
        };
        Check(context.Owners.TryBegin(different, () => new(true, false, string.Empty), out _), "Could not reserve other domain.");
        string originalOwner = context.Owner.Payload;
        string originalJournal = context.Journal.Payload;
        Check(!context.Store.TryBegin(context.Confirmed, () => true, out _, out _), "Reward displaced other domain owner.");
        Check(!context.Store.TryPrepare(context.Confirmed, () => true, out _), "Reward preparation ignored other domain owner.");
        Equal(originalOwner, context.Owner.Payload, "Reward changed other domain ownership.");
        Equal(originalJournal, context.Journal.Payload, "Reward changed its journal under another domain owner.");
        Equal(0, context.Owner.RemoveCount, "Reward released another domain owner.");
        context.AssertCoreUnchanged();
        return Task.CompletedTask;
    }

    private static async Task LeaseMustEndBeforeResolution()
    {
        using var context = new Context();
        Sr5AfterRunRewardCheckpoint applying = context.Begin();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        IDisposable lease = await context.Store.AcquireApplyingLeaseAsync(applying, () => true, true, timeout.Token);
        Sr5AfterRunRewardReceiptObservation observation;
        try
        {
            observation = context.CommitAndObserve(applying);
            Check(!context.Store.TryRecordReceipt(applying, observation, () => true, out _, out _),
                "Resolution acquired shared process gate while execution lease remained active.");
            Equal(Sr5AfterRunRewardCheckpointPhase.Applying, context.ReadCheckpoint().Phase,
                "Held execution lease allowed Applied journal persistence.");
            Equal(0, context.Owner.RemoveCount, "Held execution lease allowed owner removal.");
        }
        finally { lease.Dispose(); }
        lease.Dispose(); // Shared process lease disposal must be idempotent.
        Check(context.Store.TryRecordReceipt(applying, observation, () => true, out var applied, out string blocker),
            $"Receipt resolution failed after execution lease disposal: {blocker}");
        Check(applied is not null, "Released execution lease did not permit Applied state.");
        Equal(string.Empty, context.Owner.Payload, "Released lease left resolved mutation owner.");
    }

    private static Task UnprovenReceiptCannotResolve()
    {
        using var context = new Context();
        Sr5AfterRunRewardCheckpoint applying = context.Begin();
        Sr5AfterRunRewardReceiptObservation genuine = context.CommitAndObserve(applying);
        Check(applying.ReceiptMatches(genuine.Receipt), "Real Core receipt is not structurally coherent.");
        string ownerBytes = context.Owner.Payload;
        string journalBytes = context.Journal.Payload;
        foreach (var forged in new[]
        {
            new Sr5AfterRunRewardReceiptObservation(genuine.Receipt, genuine.CurrentWorkspaceRevision, new string('0', 64)),
            genuine with { CurrentWorkspaceRevision = genuine.CurrentWorkspaceRevision + 1 },
            Sr5AfterRunRewardReceiptObservation.Create(context.Confirmed, genuine.Receipt, genuine.CurrentWorkspaceRevision)
        })
        {
            Check(!context.Store.TryRecordReceipt(applying, forged, () => true, out var applied, out _),
                "A coherent receipt without the exact process proof resolved ownership.");
            Check(applied is null, "Invalid process proof returned Applied checkpoint.");
        }
        Equal(ownerBytes, context.Owner.Payload, "Unproven receipt altered shared owner.");
        Equal(journalBytes, context.Journal.Payload, "Unproven receipt altered journal.");
        Equal(0, context.Owner.RemoveCount, "Unproven receipt released shared owner.");
        return Task.CompletedTask;
    }

    private static Task ReadsNeverReleaseApplyingOwner()
    {
        using var context = new Context();
        Sr5AfterRunRewardCheckpoint applying = context.Begin();
        string ownerBytes = context.Owner.Payload;
        string journalBytes = context.Journal.Payload;
        int writes = context.Journal.WriteCount;
        Check(context.Store.TryReadOwned(context.OwnerId, context.Core.WorkspaceId, out var entries, out _), "Owned history read failed.");
        Check(entries.Count == 1 && entries[0].CommandDigest == applying.CommandDigest, "Read changed original operation identity.");
        context.ReadCheckpoint();
        Check(context.Store.TryReadOwned(Guid.Parse("66666666-6666-4666-8666-666666666666"), context.Core.WorkspaceId,
            out var unrelated, out _), "Other-owner filtered read failed.");
        Equal(0, unrelated.Count, "Other owner saw reward history.");
        Equal(0, context.Owner.RemoveCount, "Journal read released unresolved owner.");
        Equal(ownerBytes, context.Owner.Payload, "Journal read changed owner payload.");
        Equal(journalBytes, context.Journal.Payload, "Journal read changed history.");
        Equal(writes, context.Journal.WriteCount, "Journal read performed a write.");
        context.AssertCoreUnchanged();
        return Task.CompletedTask;
    }

    private static void InjectWriteFault(FaultingMemoryBackend backend, string fault)
    {
        if (fault == "before-write") backend.BeforeWrite = _ => throw new IOException("Simulated pre-publication failure.");
        else if (fault == "after-write") backend.AfterWrite = _ => throw new IOException("Simulated lost publication acknowledgement.");
        else backend.AfterWrite = _ => backend.FailNextRead = true;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
        => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message} Expected {expected}; actual {actual}.");

    private sealed class Context : IDisposable
    {
        public RealAfterRunRewardFixture Core { get; } = new();
        public FaultingMemoryBackend Journal { get; } = new();
        public FaultingMemoryBackend Owner { get; } = new();
        public Guid OwnerId { get; } = Guid.Parse("33333333-3333-4333-8333-333333333333");
        public Sr5CareerMutationOwnerStore Owners { get; }
        public Sr5AfterRunRewardCheckpointStore Store { get; }
        public Sr5AfterRunRewardReview Review { get; }
        public Sr5AfterRunRewardCheckpoint Confirmed { get; }

        public Context()
        {
            Owners = new Sr5CareerMutationOwnerStore(Owner);
            Store = new Sr5AfterRunRewardCheckpointStore(Journal, Owners);
            var request = new CharacterAfterRunRewardPreviewRequest(Core.WorkspaceId,
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                Guid.Parse("22222222-2222-4222-8222-222222222222"),
                8, 12500, new DateTime(2078, 9, 6, 18, 30, 0, DateTimeKind.Unspecified), "After Run reward");
            var preview = Core.Service.Preview(request);
            Check(preview.Outcome == CharacterAfterRunRewardOutcome.Available && preview.Preview is not null,
                $"Real Core preview unavailable: {preview.Error}");
            Review = new(new(OwnerId, Core.WorkspaceId, 1, true, "SR5", 1, 1, false, null), preview.Preview!);
            Check(Review.IsExact(), "Real Core review was not exact.");
            var command = Review.Preview.Command with { ExplicitlyConfirmed = true };
            Confirmed = new(1, 1, OwnerId, Sr5AfterRunRewardCheckpointPhase.Confirmed, command, command.CommandDigest());
            Check(Confirmed.IsExact(), "Real Core confirmed command was not exact.");
        }

        public void Prepare()
            => Check(Store.TryPrepare(Confirmed, () => true, out string blocker), $"Could not prepare exact command: {blocker}");

        public Sr5AfterRunRewardCheckpoint Begin()
        {
            Prepare();
            Check(Store.TryBegin(Confirmed, () => true, out var applying, out string blocker),
                $"Could not begin exact command: {blocker}");
            Check(applying is not null, "Successful Begin returned no Applying state.");
            return applying!;
        }

        public Sr5AfterRunRewardCheckpoint ReadCheckpoint()
        {
            Check(Store.TryGet(OwnerId, Core.WorkspaceId, Confirmed.Command.OperationId, out var checkpoint, out string blocker),
                $"Could not read original command: {blocker}");
            Check(checkpoint is not null, "Original command disappeared.");
            return checkpoint!;
        }

        public Sr5AfterRunRewardReceiptObservation CommitAndObserve(Sr5AfterRunRewardCheckpoint checkpoint)
        {
            var committed = Core.Service.Commit(checkpoint.Command);
            Equal(CharacterAfterRunRewardOutcome.Applied, committed.Outcome, $"Real Core commit failed: {committed.Error}");
            return ObserveFromColdCore(checkpoint);
        }

        public Sr5AfterRunRewardReceiptObservation ObserveFromColdCore(Sr5AfterRunRewardCheckpoint checkpoint)
        {
            var found = Core.ColdReopenService().Lookup(Core.WorkspaceId, checkpoint.Command.OperationId, checkpoint.CommandDigest);
            Check(found.Outcome == CharacterAfterRunRewardOutcome.Replayed && found.Receipt is not null
                && found.CurrentWorkspaceRevision is not null, $"Real cold Core lookup failed: {found.Error}");
            return Sr5AfterRunRewardReceiptObservation.Create(checkpoint, found.Receipt!, found.CurrentWorkspaceRevision!.Value);
        }

        public void AssertOwnerMatches(Sr5AfterRunRewardCheckpoint checkpoint)
            => Equal(checkpoint.MutationOwner(), JsonSerializer.Deserialize<Sr5CareerMutationOwner>(Owner.Payload),
                "Shared owner no longer binds the original command.");

        public void AssertCoreUnchanged()
        {
            var snapshot = Core.ColdReopenService().Read(Core.WorkspaceId).Snapshot;
            Check(snapshot is not null, "Real Core fixture became unreadable.");
            Equal(1L, snapshot!.ContentRevision, "Host fault changed real Core revision.");
            Equal(30, snapshot.AvailableKarma, "Host fault changed real Core karma.");
            Equal(1000m, snapshot.AvailableNuyen, "Host fault changed real Core nuyen.");
            Equal(0, snapshot.Expenses.Count, "Host fault added a real expense.");
        }

        public void Dispose() => Core.Dispose();
    }

    // Explicitly simulated storage. Payload retention here tests transition
    // policy only; the real file-journal tests own disk durability evidence.
    private sealed class FaultingMemoryBackend : ISr5AfterRunRewardJournalBackend, ISr5CareerCheckpointBackend
    {
        public string Payload { get; set; } = string.Empty;
        public int WriteCount { get; private set; }
        public int RemoveCount { get; private set; }
        public bool FailNextRead { get; set; }
        public bool IgnoreRemove { get; set; }
        public Action<string>? BeforeWrite { get; set; }
        public Action<string>? AfterWrite { get; set; }
        public Action? BeforeRemove { get; set; }
        public Action? AfterRemove { get; set; }

        public string Read()
        {
            if (!FailNextRead) return Payload;
            FailNextRead = false;
            throw new IOException("Simulated read-back failure.");
        }

        public void Write(string payload)
        {
            WriteCount++;
            BeforeWrite?.Invoke(payload);
            Payload = payload;
            AfterWrite?.Invoke(payload);
        }

        public void Remove()
        {
            RemoveCount++;
            BeforeRemove?.Invoke();
            if (!IgnoreRemove) Payload = string.Empty;
            AfterRemove?.Invoke();
        }

        public void ClearFaults()
        {
            FailNextRead = false;
            IgnoreRemove = false;
            BeforeWrite = null;
            AfterWrite = null;
            BeforeRemove = null;
            AfterRemove = null;
        }
    }

    private sealed class FixedAuthority(Sr5AfterRunRewardRunnerBinding binding) : ISr5AfterRunRewardRunnerAuthority
    {
        public Sr5AfterRunRewardRunnerBinding Current => binding;
    }

    // A pass-through observer of the real Core service, not a replacement rule
    // implementation or fabricated response source.
    private sealed class CountingCoreService(ICharacterAfterRunRewardService inner) : ICharacterAfterRunRewardService
    {
        public int CommitCount { get; private set; }
        public int LookupCount { get; private set; }
        public CharacterAfterRunRewardReadResult Read(CharacterWorkspaceId workspaceId) => inner.Read(workspaceId);
        public CharacterAfterRunRewardPreviewResult Preview(CharacterAfterRunRewardPreviewRequest request) => inner.Preview(request);
        public CharacterAfterRunRewardResult Commit(CharacterAfterRunRewardCommand command, CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return inner.Commit(command, cancellationToken);
        }
        public CharacterAfterRunRewardResult Lookup(CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest)
        {
            LookupCount++;
            return inner.Lookup(workspaceId, operationId, commandDigest);
        }
    }
}
