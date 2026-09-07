using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Android.Sr5AfterRunReward.Tests;

internal static partial class RewardPhoneTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> ColdEntryCases =>
    [
        (nameof(ColdAppliedRewardWithUnreleasedOwnerRequiresRecovery), ColdAppliedRewardWithUnreleasedOwnerRequiresRecovery),
        (nameof(ColdForeignCareerOwnerBlocksNewReward), ColdForeignCareerOwnerBlocksNewReward),
        (nameof(ColdCorruptCareerOwnerBlocksNewReward), ColdCorruptCareerOwnerBlocksNewReward),
        (nameof(RecordedHistoryResumesByLookupWithFreshFacts), RecordedHistoryResumesByLookupWithFreshFacts),
        (nameof(RecordedHistoryCannotCrossOwners), RecordedHistoryCannotCrossOwners),
        (nameof(UnknownRecordedIdentityDoesNotCreateIntent), UnknownRecordedIdentityDoesNotCreateIntent),
        (nameof(NewPendingRewardTakesPriorityOverHistorySelection), NewPendingRewardTakesPriorityOverHistorySelection),
        (nameof(CorruptJournalAfterAppearanceInvalidatesHistorySelection), CorruptJournalAfterAppearanceInvalidatesHistorySelection),
        (nameof(RecordedHistoryLookupFailureCannotEnableHandoff), RecordedHistoryLookupFailureCannotEnableHandoff),
        (nameof(ChangedOwnerDuringEntryReadCannotExposeHistory), ChangedOwnerDuringEntryReadCannotExposeHistory),
        (nameof(MismatchedOwnerCannotBeReconciledByRecordedHistory), MismatchedOwnerCannotBeReconciledByRecordedHistory),
        (nameof(ForeignConfirmedRewardWithoutOwnerStillBlocksEntry), ForeignConfirmedRewardWithoutOwnerStillBlocksEntry),
        (nameof(EntryReadCannotRunInsideMutationExecutionLease), EntryReadCannotRunInsideMutationExecutionLease),
        (nameof(EntryReadHoldsSharedGateWithoutWriting), EntryReadHoldsSharedGateWithoutWriting)
    ];

    private static async Task ColdAppliedRewardWithUnreleasedOwnerRequiresRecovery()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        host.OwnerBackend.FailRemove = true;
        await original.ConfirmAsync();
        Equal(Sr5AfterRunRewardCheckpointPhase.Applied,
            (await host.Coordinator.ReadHistoryAsync()).Single().Phase);
        using var cold = host.Cold();
        cold.RefreshBinding();
        var model = NewPhone(cold, out _);
        await model.InitializeAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.Pending, model.Status);
        Equal(original.OperationId, model.OperationId);
        Equal(Sr5AfterRunRewardCheckpointPhase.Applied, model.Checkpoint!.Phase);
        Require(!model.CanEdit && !model.CanRetry && model.CanRecover && !model.CanContinue,
            "Applied journal was mistaken for released ownership or current Core proof.");
        Equal(0, cold.Service.CommitCalls);
        Equal(0, cold.Service.LookupCalls);
        Require(cold.OwnerBackend.Read().Length > 0, "Appearance released an unverified owner.");
        await model.RecoverAsync();
        Require(model.CanContinue, "Service-verified receipt did not release and reload.");
        Equal(0, cold.Service.CommitCalls);
        Equal(string.Empty, cold.OwnerBackend.Read());
        Equal(38, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.AvailableKarma);
    }

    private static async Task ColdForeignCareerOwnerBlocksNewReward()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var owner = new Sr5CareerMutationOwner(1, Sr5CareerMutationDomains.ActiveSkillAdvance,
            fixture.WorkspaceId.Value, host.Authority.Current.OwnerId, Guid.NewGuid(), 2, 1, new string('a', 64));
        string payload = JsonSerializer.Serialize(owner);
        host.OwnerBackend.Write(payload);
        var model = NewPhone(host, out _);
        await model.InitializeAsync();
        Require(!model.CanEdit && !model.CanConfirm && !model.CanContinue,
            "Another Career mutation was hidden by an empty reward journal.");
        Equal(Sr5AfterRunRewardPhoneStatus.JournalUnavailable, model.Status);
        Equal(payload, host.OwnerBackend.Read());
        Equal(0, host.Journal.Writes);
        Equal(0, host.Service.CommitCalls);
    }

    private static async Task ColdCorruptCareerOwnerBlocksNewReward()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        host.OwnerBackend.Write("{}");
        var model = NewPhone(host, out _);
        await model.InitializeAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.JournalUnavailable, model.Status);
        Require(!model.CanEdit && !model.CanRecover, "Corrupt ownership was interpreted as absent.");
        Equal("{}", host.OwnerBackend.Read());
        Equal(0, host.Journal.Writes);
    }

    private static async Task RecordedHistoryResumesByLookupWithFreshFacts()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        await original.ConfirmAsync();
        var receipt = original.Checkpoint!.Receipt!;
        Recorded(await host.Coordinator.ConfirmAsync(await Review(host)));
        host.RefreshBinding();
        using var cold = host.Cold();
        var model = NewPhone(cold, out _);
        await model.InitializeAsync();
        Equal(2, model.RecordedRewards.Count);
        Require(model.CanEdit && !model.CanContinue && !model.HasRetainedIntent,
            "Appearance treated history as live Core proof.");
        Equal(0, cold.Service.LookupCalls);
        await model.ResumeRecordedRewardAsync(original.OperationId);
        Require(model.CanContinue, "Recorded history could not resume by lookup.");
        Equal(receipt.ReceiptDigest, model.Handoff!.Recorded.Receipt!.ReceiptDigest);
        Equal(2L, model.Handoff.Recorded.Receipt.CommittedWorkspaceRevision);
        Equal(3L, model.Handoff.Snapshot.ContentRevision);
        Equal(46, model.Handoff.Snapshot.AvailableKarma);
        Equal(0, cold.Service.CommitCalls);
        Equal(1, cold.Service.LookupCalls);
        Equal(0, cold.Journal.Writes);
    }

    private static async Task RecordedHistoryCannotCrossOwners()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        await original.ConfirmAsync();
        using var cold = host.Cold();
        cold.Authority.Current = cold.Authority.Current with { OwnerId = Guid.NewGuid() };
        var model = NewPhone(cold, out _);
        var operation = model.OperationId;
        await model.InitializeAsync();
        Equal(0, model.RecordedRewards.Count);
        await model.ResumeRecordedRewardAsync(original.OperationId);
        Equal(operation, model.OperationId);
        Require(!model.HasRetainedIntent && model.Handoff is null && model.RecordedRewards.Count == 0,
            "Recorded history crossed the owner boundary.");
        Equal(0, cold.Service.LookupCalls);
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task UnknownRecordedIdentityDoesNotCreateIntent()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var model = NewPhone(host, out _);
        var operation = model.OperationId;
        await model.InitializeAsync();
        await model.ResumeRecordedRewardAsync(Guid.NewGuid());
        Require(model.CanEdit && !model.HasRetainedIntent && !model.CanContinue,
            "Unknown history identity was promoted to an intent or receipt.");
        Equal(operation, model.OperationId);
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Service.LookupCalls);
        Equal(0, host.Journal.Writes);
    }

    private static async Task NewPendingRewardTakesPriorityOverHistorySelection()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        await original.ConfirmAsync();
        using var cold = host.Cold();
        var model = NewPhone(cold, out _);
        await model.InitializeAsync();
        Equal(1, model.RecordedRewards.Count);
        var second = await Review(host);
        host.Journal.AfterWrite = _ => throw new IOException("Confirmed acknowledgement lost.");
        Unknown(await host.Coordinator.ConfirmAsync(second));
        await model.ResumeRecordedRewardAsync(original.OperationId);
        Equal(second.Preview.Command.OperationId, model.OperationId);
        Equal(Sr5AfterRunRewardPhoneStatus.Pending, model.Status);
        Require(!model.CanEdit && !model.CanContinue && model.CanRecover,
            "A stale history menu bypassed a newly pending reward.");
        Equal(0, cold.Service.LookupCalls);
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task CorruptJournalAfterAppearanceInvalidatesHistorySelection()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        await original.ConfirmAsync();
        using var cold = host.Cold();
        var model = NewPhone(cold, out _);
        await model.InitializeAsync();
        Equal(1, model.RecordedRewards.Count);
        File.WriteAllText(Path.Combine(host.HostDirectory, "sr5-after-run-rewards.v1.json"), "{}");
        await model.ResumeRecordedRewardAsync(original.OperationId);
        Equal(Sr5AfterRunRewardPhoneStatus.JournalUnavailable, model.Status);
        Equal(0, model.RecordedRewards.Count);
        Require(!model.CanContinue && !model.CanEdit, "Cached history survived journal corruption.");
        Equal(0, cold.Service.LookupCalls);
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task RecordedHistoryLookupFailureCannotEnableHandoff()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        await original.ConfirmAsync();
        using var cold = host.Cold();
        var model = NewPhone(cold, out _);
        await model.InitializeAsync();
        cold.Service.AfterLookup = () => throw new IOException("Core receipt lookup unavailable.");
        await model.ResumeRecordedRewardAsync(original.OperationId);
        Require(model.HasRetainedIntent && model.CanRecover && !model.CanRetry && !model.CanContinue,
            "Stored receipt coherence replaced a live Core observation.");
        cold.Service.AfterLookup = null;
        await model.RecoverAsync();
        Require(model.CanContinue, "Historical lookup retry failed.");
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task ChangedOwnerDuringEntryReadCannotExposeHistory()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        await original.ConfirmAsync();
        using var cold = host.Cold();
        var model = NewPhone(cold, out _);
        cold.Journal.BeforeRead = () => cold.Authority.Current = cold.Authority.Current with { OwnerId = Guid.NewGuid() };
        await model.InitializeAsync();
        Require(model.RecordedRewards.Count == 0 && !model.CanEdit && !model.CanContinue,
            "History read returned after its owner changed.");
        Equal(0, cold.Service.LookupCalls);
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task MismatchedOwnerCannotBeReconciledByRecordedHistory()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        await original.ConfirmAsync();
        var checkpoint = original.Checkpoint!;
        foreach (var owner in new[]
        {
            checkpoint.MutationOwner() with { IdempotencyKey = new string('a', 64) },
            checkpoint.MutationOwner() with { ExpectedContentRevision = 2 },
            checkpoint.MutationOwner() with { ActionId = Guid.NewGuid() },
            checkpoint.MutationOwner() with { OwnerId = Guid.NewGuid() }
        })
        {
            using var cold = host.Cold();
            string payload = JsonSerializer.Serialize(owner);
            cold.OwnerBackend.Write(payload);
            var model = NewPhone(cold, out _);
            await model.InitializeAsync();
            Equal(Sr5AfterRunRewardPhoneStatus.JournalUnavailable, model.Status);
            Require(!model.CanEdit && !model.CanRecover && model.RecordedRewards.Count == 0,
                "A matching operation alone replaced exact shared-owner identity.");
            Equal(payload, cold.OwnerBackend.Read());
            Equal(0, cold.Service.LookupCalls);
        }
    }

    private static async Task ForeignConfirmedRewardWithoutOwnerStillBlocksEntry()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (original, _) = await PhoneReview(host);
        host.Journal.AfterWrite = _ => throw new IOException("Confirmed acknowledgement lost.");
        await original.ConfirmAsync();
        Equal(string.Empty, host.OwnerBackend.Read());
        using var cold = host.Cold();
        cold.Authority.Current = cold.Authority.Current with { OwnerId = Guid.NewGuid() };
        var model = NewPhone(cold, out _);
        await model.InitializeAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.JournalUnavailable, model.Status);
        Require(!model.CanEdit && !model.CanRecover && model.RecordedRewards.Count == 0,
            "Empty shared owner hid a foreign pending reward journal.");
        Equal(0, cold.Journal.Writes);
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task EntryReadCannotRunInsideMutationExecutionLease()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        bool rejected = false;
        host.Service.BeforeCommit = _ =>
        {
            try { host.Coordinator.ReadEntryStateAsync().GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "An entry snapshot was admitted during a leased mutation.");
        };
        await model.ConfirmAsync();
        Require(rejected && model.CanContinue, "Busy inspection damaged the owning mutation.");
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task EntryReadHoldsSharedGateWithoutWriting()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var competingOwners = new Sr5CareerMutationOwnerStore(host.OwnerBackend);
        bool competingActionRan = false;
        host.Journal.BeforeRead = () =>
        {
            bool admitted = competingOwners.TryRunWhenUnowned(() =>
            {
                competingActionRan = true;
                return (true, string.Empty);
            }, out _);
            Require(!admitted, "Journal inspection did not hold the shared gate.");
        };
        var state = await host.Coordinator.ReadEntryStateAsync();
        Require(state.Recorded.Count == 0 && state.RecoveryRequired is null && !competingActionRan,
            "Read-only inspection performed a transition.");
        Equal(string.Empty, host.OwnerBackend.Read());
        Equal(0, host.Journal.Writes);
        host.Journal.BeforeRead = null;
        Require(competingOwners.TryRunWhenUnowned(() => (true, string.Empty), out _),
            "Inspection leaked the shared gate.");
    }
}
