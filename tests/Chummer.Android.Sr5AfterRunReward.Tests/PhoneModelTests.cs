using System.Globalization;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Android.Sr5AfterRunReward.Tests;

internal static partial class RewardPhoneTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> PhoneModelCases =>
    [
        (nameof(PhonePreCancelledConfirmationPreservesReview), PhonePreCancelledConfirmationPreservesReview),
        (nameof(PhoneCancellationDuringPreCommitReadDoesNotInventPendingIntent), PhoneCancellationDuringPreCommitReadDoesNotInventPendingIntent),
        (nameof(CanceledDuplicateConfirmationRetainsExistingJournal), CanceledDuplicateConfirmationRetainsExistingJournal),
        (nameof(PhoneCancellationAfterJournalDoesNotDiscardIntent), PhoneCancellationAfterJournalDoesNotDiscardIntent),
        (nameof(PhoneConfirmationReloadsBeforeFreshHandoff), PhoneConfirmationReloadsBeforeFreshHandoff),
        (nameof(PhoneDraftEditInvalidatesReviewWithoutChangingIdentity), PhoneDraftEditInvalidatesReviewWithoutChangingIdentity),
        (nameof(PhoneInvalidInputNeverConfirms), PhoneInvalidInputNeverConfirms),
        (nameof(PhoneExplicitNoAwardStillHasDurableReceipt), PhoneExplicitNoAwardStillHasDurableReceipt),
        (nameof(PhoneRepeatedConfirmDoesNotCreditTwice), PhoneRepeatedConfirmDoesNotCreditTwice),
        (nameof(PhoneRefreshFailureRetainsRecordedReward), PhoneRefreshFailureRetainsRecordedReward),
        (nameof(PhoneCancellationAfterCommitRetainsRecoveryIdentity), PhoneCancellationAfterCommitRetainsRecoveryIdentity),
        (nameof(PhoneColdPendingRecoveryDoesNotCommitOnAppearance), PhoneColdPendingRecoveryDoesNotCommitOnAppearance),
        (nameof(PhoneColdRetryUsesOriginalCommand), PhoneColdRetryUsesOriginalCommand),
        (nameof(PhoneChangedRunnerDuringRefreshCannotContinue), PhoneChangedRunnerDuringRefreshCannotContinue),
        (nameof(PhoneChangedRunnerDuringFreshReadCannotContinue), PhoneChangedRunnerDuringFreshReadCannotContinue),
        (nameof(PhoneHistoricalReceiptUsesCurrentSavedFacts), PhoneHistoricalReceiptUsesCurrentSavedFacts),
        (nameof(PhoneCorruptJournalCannotStartNewReward), PhoneCorruptJournalCannotStartNewReward)
    ];

    private static async Task PhonePreCancelledConfirmationPreservesReview()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, refresh) = await PhoneReview(host);
        var review = model.Review;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await model.ConfirmAsync(cancel.Token);
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
        Equal(0, refresh.Calls);
        Equal(Sr5AfterRunRewardPhoneStatus.Review, model.Status);
        Require(model.CanConfirm && !model.HasRetainedIntent && model.Review == review,
            "Cancellation before entry stranded an unstarted operation.");
        await model.ConfirmAsync();
        Equal(1, host.Service.CommitCalls);
        Require(model.CanContinue, "A later explicit confirmation could not complete.");
    }

    private static async Task PhoneCancellationDuringPreCommitReadDoesNotInventPendingIntent()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        using var cancel = new CancellationTokenSource();
        host.Service.AfterRead = cancel.Cancel;
        await model.ConfirmAsync(cancel.Token);
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
        Require(!model.HasRetainedIntent && model.CanEdit && model.Checkpoint is null,
            "A canceled read with no prepared journal was presented as an uncertain mutation.");
        host.Service.AfterRead = null;
        await model.PreviewAsync(CultureInfo.InvariantCulture);
        await model.ConfirmAsync();
        Equal(1, host.Service.CommitCalls);
        Require(model.CanContinue, "Fresh review after pre-journal cancellation could not complete.");
    }

    private static async Task PhoneConfirmationReloadsBeforeFreshHandoff()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, refresh) = await PhoneReview(host);
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
        refresh.BeforeReload = () => Equal(1L, host.Authority.Current.ContentRevision);
        await model.ConfirmAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.Recorded, model.Status);
        Equal(1, refresh.Calls);
        Require(model.CanContinue && model.Handoff is not null, "Missing current saved runner handoff.");
        Equal(2L, model.Handoff!.Runner.ContentRevision);
        Equal(2L, model.Handoff.Snapshot.SavedRevision);
        Equal(38, model.Handoff.Snapshot.AvailableKarma);
        Equal(13500m, model.Handoff.Snapshot.AvailableNuyen);
        Equal(model.OperationId, model.Handoff.Recorded.Command.OperationId);
        Require(!model.CanEdit && !model.CanConfirm && !model.CanRetry,
            "Recorded reward can be submitted again.");
        host.Authority.Current = host.Authority.Current with { ActivationGeneration = 2 };
        Require(!model.CanContinue, "Handoff survived runner reactivation.");
    }

    private static async Task CanceledDuplicateConfirmationRetainsExistingJournal()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        var review = model.Review!;
        host.Journal.AfterWrite = write => { if (write == 1) throw new IOException("Confirmed ack lost."); };
        await model.ConfirmAsync();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var result = await host.Coordinator.ConfirmAsync(review, cancel.Token);
        Equal(Sr5AfterRunRewardResolutionStatus.Blocked, result.Status);
        Require(result.Checkpoint is not null, "Canceled duplicate discarded an earlier durable intent.");
        Equal(model.OperationId, result.Checkpoint!.Command.OperationId);
        Equal(model.Checkpoint!.CommandDigest, result.Checkpoint.CommandDigest);
        Equal(1, host.Journal.Writes);
        Equal(0, host.Service.CommitCalls);
    }

    private static async Task PhoneCancellationAfterJournalDoesNotDiscardIntent()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        using var cancel = new CancellationTokenSource();
        host.Journal.AfterWrite = _ => cancel.Cancel();
        await model.ConfirmAsync(cancel.Token);
        Require(model.HasRetainedIntent && !model.CanEdit && model.Checkpoint is not null,
            "Cancellation after the journal write was mistaken for pre-commit rejection.");
        var command = model.Checkpoint!.Command;
        host.Journal.AfterWrite = null;
        await model.RecoverAsync();
        Equal(0, host.Service.CommitCalls);
        Require(model.CanRetry && !model.CanEdit, "Lookup NotFound discarded the durable intent.");
        await model.RetryAsync();
        Equal(command, host.Service.Commands.Single());
        Require(model.CanContinue, "Original intent could not be explicitly retried.");
    }

    private static async Task PhoneDraftEditInvalidatesReviewWithoutChangingIdentity()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        var operation = model.OperationId;
        var reward = model.RewardId;
        Require(model.UpdateDraft(model.Draft with { Karma = "3" }), "Draft was not editable.");
        Require(model.Review is null && !model.CanConfirm, "Stale review survived input change.");
        await model.ConfirmAsync();
        Equal(0, host.Service.CommitCalls);
        await model.PreviewAsync(CultureInfo.InvariantCulture);
        Equal(operation, model.OperationId);
        Equal(reward, model.RewardId);
        Equal(3, model.Review!.Preview.Command.KarmaAmount);
    }

    private static async Task PhoneInvalidInputNeverConfirms()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        foreach (string invalid in new[] { "-1", "1.5", "999999999999", "", "1e3" })
        {
            Require(model.UpdateDraft(model.Draft with { Karma = invalid }), "Draft unavailable.");
            await model.PreviewAsync(CultureInfo.InvariantCulture);
            Equal(Sr5AfterRunRewardPhoneStatus.InvalidInput, model.Status);
            await model.ConfirmAsync();
        }
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
    }

    private static async Task PhoneExplicitNoAwardStillHasDurableReceipt()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        model.UpdateDraft(model.Draft with { Karma = "0", Nuyen = "0", NoAward = false });
        await model.PreviewAsync();
        Require(!model.CanConfirm, "Zero amounts became an implicit no-award confirmation.");
        model.UpdateDraft(model.Draft with { NoAward = true });
        await model.PreviewAsync();
        await model.ConfirmAsync();
        Require(model.CanContinue, "Explicit no-award receipt was not accepted.");
        Equal(CharacterAfterRunRewardKind.NoAward, model.Checkpoint!.Command.Kind);
        Equal(30, model.Handoff!.Snapshot.AvailableKarma);
        Equal(1000m, model.Handoff.Snapshot.AvailableNuyen);
        Equal(0, model.Handoff.Snapshot.Expenses.Count);
    }

    private static async Task PhoneRepeatedConfirmDoesNotCreditTwice()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        host.Service.BeforeCommit = _ =>
        {
            Require(model.IsBusy, "Model did not guard an in-flight confirmation.");
            model.ConfirmAsync().GetAwaiter().GetResult();
            Require(!model.UpdateDraft(model.Draft with { Karma = "99" }), "In-flight draft changed.");
        };
        await model.ConfirmAsync();
        await model.ConfirmAsync();
        await model.InitializeAsync();
        await model.RetryAsync();
        Equal(1, host.Service.CommitCalls);
        Equal(38, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.AvailableKarma);
    }

    private static async Task PhoneRefreshFailureRetainsRecordedReward()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, refresh) = await PhoneReview(host);
        refresh.BeforeReload = () => throw new IOException("Reload unavailable.");
        await model.ConfirmAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.RefreshRequired, model.Status);
        Equal(Sr5AfterRunRewardCheckpointPhase.Applied, model.Checkpoint!.Phase);
        Require(model.HasRetainedIntent && model.CanRecover && !model.CanContinue && !model.CanRetry,
            "A refresh failure discarded the recorded identity or allowed another credit.");
        var operation = model.OperationId;
        refresh.BeforeReload = null;
        await model.RecoverAsync();
        Require(model.CanContinue, "Receipt recovery did not refresh.");
        Equal(operation, model.OperationId);
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task PhoneCancellationAfterCommitRetainsRecoveryIdentity()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        using var cancel = new CancellationTokenSource();
        host.Service.AfterCommit = _ => { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); };
        await model.ConfirmAsync(cancel.Token);
        Require(model.HasRetainedIntent && !model.CanEdit && !model.CanContinue,
            "Post-commit cancellation was treated as rollback.");
        Equal(38, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.AvailableKarma);
        await model.RecoverAsync();
        Require(model.CanContinue, "Committed reward did not recover by lookup.");
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task PhoneColdPendingRecoveryDoesNotCommitOnAppearance()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        host.Service.AfterCommit = _ => throw new IOException("Lost response.");
        await model.ConfirmAsync();
        using var cold = host.Cold();
        cold.RefreshBinding();
        var recovered = NewPhone(cold, out _);
        await recovered.InitializeAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.Pending, recovered.Status);
        Equal(model.OperationId, recovered.OperationId);
        Equal(model.RewardId, recovered.RewardId);
        Equal(0, cold.Service.CommitCalls);
        Equal(0, cold.Service.LookupCalls);
        Require(!recovered.CanEdit && !recovered.CanConfirm, "Pending identity was editable.");
        await recovered.RecoverAsync();
        Require(recovered.CanContinue, "Cold lookup could not create a fresh handoff.");
        Equal(0, cold.Service.CommitCalls);
    }

    private static async Task PhoneColdRetryUsesOriginalCommand()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, _) = await PhoneReview(host);
        var command = model.Review!.Preview.Command with { ExplicitlyConfirmed = true };
        host.Journal.AfterWrite = write => { if (write == 1) throw new IOException("Confirmed ack lost."); };
        await model.ConfirmAsync();
        Equal(0, host.Service.CommitCalls);
        using var cold = host.Cold();
        var recovered = NewPhone(cold, out _);
        await recovered.InitializeAsync();
        await recovered.RecoverAsync();
        Equal(0, cold.Service.CommitCalls);
        Require(recovered.CanRetry && !recovered.CanEdit, "NotFound cleared or replaced pending intent.");
        await recovered.RetryAsync();
        Equal(command, cold.Service.Commands.Single());
        Require(recovered.CanContinue, "Explicit retry failed.");
    }

    private static async Task PhoneChangedRunnerDuringRefreshCannotContinue()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, refresh) = await PhoneReview(host);
        refresh.BeforeReload = () => host.Authority.Current = host.Authority.Current with { ActivationGeneration = 2 };
        await model.ConfirmAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.RunnerChanged, model.Status);
        Require(model.Checkpoint?.Phase == Sr5AfterRunRewardCheckpointPhase.Applied
            && model.Handoff is null && !model.CanContinue, "Receipt was lost or transferred to a new activation.");
    }

    private static async Task PhoneChangedRunnerDuringFreshReadCannotContinue()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, refresh) = await PhoneReview(host);
        refresh.BeforeReload = () => host.Service.AfterRead = () =>
            host.Authority.Current = host.Authority.Current with { OwnerId = Guid.NewGuid() };
        await model.ConfirmAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.RunnerChanged, model.Status);
        Require(!model.CanContinue && model.Handoff is null, "Fresh facts were handed to a different owner.");
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task PhoneHistoricalReceiptUsesCurrentSavedFacts()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var (model, refresh) = await PhoneReview(host);
        refresh.BeforeReload = () => throw new IOException("Reload unavailable.");
        await model.ConfirmAsync();
        var receipt = model.Checkpoint!.Receipt;
        SavedEdit(fixture);
        host.RefreshBinding();
        refresh.BeforeReload = null;
        await model.RecoverAsync();
        Require(model.CanContinue, "Historical reward blocked fresh saved facts.");
        // Deserializing a receipt creates new collection instances. Compare its
        // validated canonical identity, not record equality of those lists.
        Equal(receipt!.ReceiptDigest, model.Handoff!.Recorded.Receipt!.ReceiptDigest);
        Equal(receipt.CommandDigest, model.Handoff.Recorded.Receipt.CommandDigest);
        Equal(2L, receipt!.CommittedWorkspaceRevision);
        Equal(3L, model.Handoff.Snapshot.ContentRevision);
        Equal(1, host.Service.CommitCalls);
        SavedEdit(fixture);
        host.RefreshBinding();
        Require(!model.CanContinue, "Handoff survived a later revision change.");
    }

    private static async Task PhoneCorruptJournalCannotStartNewReward()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        File.WriteAllText(Path.Combine(host.HostDirectory, "sr5-after-run-rewards.v1.json"), "{}");
        var model = NewPhone(host, out _);
        await model.InitializeAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.JournalUnavailable, model.Status);
        Require(!model.CanEdit && !model.CanConfirm, "Corrupt journal became empty history.");
        Equal(0, host.Service.CommitCalls);
    }

    private static Sr5AfterRunRewardPhoneModel NewPhone(Host host, out PhoneRefresh refresh)
    {
        refresh = new(host);
        return new(host.Coordinator, host.Authority, refresh,
            new DateTime(2078, 9, 6, 18, 30, 0, DateTimeKind.Local));
    }

    private static async Task<(Sr5AfterRunRewardPhoneModel Model, PhoneRefresh Refresh)> PhoneReview(Host host)
    {
        var model = NewPhone(host, out var refresh);
        await model.InitializeAsync();
        Require(model.UpdateDraft(model.Draft with { Karma = "8", Nuyen = "12500", Reason = "Local reward" }),
            "Model did not admit local draft.");
        await model.PreviewAsync(CultureInfo.InvariantCulture);
        Require(model.CanConfirm, "Core review was not available.");
        Equal(DateTimeKind.Unspecified, model.Review!.Preview.Command.ExpenseDateLocal.Kind);
        return (model, refresh);
    }

    // Only the native presenter reload seam is substituted. Every reward read,
    // preview, commit and lookup still executes the actual file-backed Core.
    private sealed class PhoneRefresh(Host host) : ISr5AfterRunRewardSavedRunnerRefresh
    {
        internal int Calls;
        internal Action? BeforeReload;
        public Task ReloadAsync(CharacterWorkspaceId workspaceId, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            Equal(host.Fixture.WorkspaceId, workspaceId);
            BeforeReload?.Invoke();
            host.RefreshBinding();
            return Task.CompletedTask;
        }
    }
}
