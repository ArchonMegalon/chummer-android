using System.Globalization;
using Chummer.Android.Native;
using Chummer.Android.Sr5AfterRunReward.Tests;
using Chummer.Contracts.Workspaces;

internal static partial class RewardPhoneTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> RunnerHostCases =>
    [
        (nameof(RealHostPartialReloadsCommittedRewardWithoutChangingSelection), RealHostPartialReloadsCommittedRewardWithoutChangingSelection),
        (nameof(RealHostReloadErrorStillAllowsReadOnlyRecovery), RealHostReloadErrorStillAllowsReadOnlyRecovery),
        (nameof(RealHostCancelledReloadKeepsReadOnlyRecoveryAvailable), RealHostCancelledReloadKeepsReadOnlyRecoveryAvailable),
        (nameof(RealHostReloadErrorDoesNotPermitDirtyRecovery), RealHostReloadErrorDoesNotPermitDirtyRecovery),
        (nameof(RealHostShellSyncActivationChangePreventsHandoff), RealHostShellSyncActivationChangePreventsHandoff),
        (nameof(RealHostReloadGateRejectsNewRevision), RealHostReloadGateRejectsNewRevision),
        (nameof(RealHostExplicitActivationFencesSameWorkspaceReview), RealHostExplicitActivationFencesSameWorkspaceReview),
        (nameof(RealHostAwayAndBackFencesReview), RealHostAwayAndBackFencesReview),
        (nameof(RealHostRejectsWrongWorkspaceReload), RealHostRejectsWrongWorkspaceReload),
        (nameof(RealHostReloadRevalidatesAfterActivationGate), RealHostReloadRevalidatesAfterActivationGate),
        (nameof(RealHostOwnerSwitchDuringReloadPreventsHandoff), RealHostOwnerSwitchDuringReloadPreventsHandoff),
        (nameof(RealHostBusyAndDirtyFramesCannotOverwriteUnsavedState), RealHostBusyAndDirtyFramesCannotOverwriteUnsavedState),
        (nameof(RealHostUncomposedAndDisposedSessionsStayUnavailable), RealHostUncomposedAndDisposedSessionsStayUnavailable),
        (nameof(EntryRecoveryCannotBeHiddenByAnAvailableCatalog), EntryRecoveryCannotBeHiddenByAnAvailableCatalog),
        (nameof(EntryReleasedHistoryDoesNotReplaceAnAvailableCatalog), EntryReleasedHistoryDoesNotReplaceAnAvailableCatalog),
        (nameof(EntryMissingCatalogAllowsOrdinaryLocalReward), EntryMissingCatalogAllowsOrdinaryLocalReward),
        (nameof(EntryCorruptJournalCannotFallThroughToAnotherSettlement), EntryCorruptJournalCannotFallThroughToAnotherSettlement),
        (nameof(EntryReadRejectsAwayAndBackSelection), EntryReadRejectsAwayAndBackSelection),
        (nameof(EntryReadCancellationCannotChooseADestination), EntryReadCancellationCannotChooseADestination),
        (nameof(EntryRecoveryPreservesAppliedButUnreleasedOwner), EntryRecoveryPreservesAppliedButUnreleasedOwner)
    ];

    private static async Task RealHostPartialReloadsCommittedRewardWithoutChangingSelection()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var adapter = new RunnerSessionSr5AfterRunRewardHost(session, owner);
        var before = adapter.Current;
        var model = await SessionReview(session, owner);
        await model.ConfirmAsync();
        Require(model.CanContinue, "The real reward partial could not reload after Commit.");
        Equal(before.ActivationGeneration, adapter.Current.ActivationGeneration);
        Equal(2L, adapter.Current.ContentRevision);
        Equal(1, presenter.Loads);
        Equal(1, session.Notifications);
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task RealHostReloadErrorStillAllowsReadOnlyRecovery()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var model = await SessionReview(session, owner);
        presenter.BeforeLoad = _ => throw new IOException("Presenter reload unavailable.");
        await model.ConfirmAsync();
        Equal(Sr5AfterRunRewardPhoneStatus.RefreshRequired, model.Status);
        Require(session.State.Error is not null && model.CanRecover && !model.CanEdit && !model.CanContinue,
            "Presenter error permanently stranded a durably recorded reward.");
        presenter.BeforeLoad = null;
        await model.RecoverAsync();
        Require(model.CanContinue, "Read-only refresh and lookup did not recover.");
        Equal(1, host.Service.CommitCalls);
        Equal(38, model.Handoff!.Snapshot.AvailableKarma);
    }

    private static async Task RealHostExplicitActivationFencesSameWorkspaceReview()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        var model = await SessionReview(session, owner);
        session.AdvanceSelectionForTest();
        Require(!model.CanConfirm, "Explicit same-workspace activation retained an old review.");
        await model.ConfirmAsync();
        Equal(0, host.Service.CommitCalls);
    }

    private static async Task RealHostCancelledReloadKeepsReadOnlyRecoveryAvailable()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var model = await SessionReview(session, owner);
        using var cancellation = new CancellationTokenSource();
        presenter.BeforeLoad = token => { cancellation.Cancel(); throw new OperationCanceledException(token); };
        await model.ConfirmAsync(cancellation.Token);
        Require(model.CanRecover && !model.CanRetry && !model.CanContinue,
            "Cancellation during presenter reload discarded read-only recovery.");
        presenter.BeforeLoad = null;
        await model.RecoverAsync();
        Require(model.CanContinue, "Recorded reward did not recover after canceled reload.");
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task RealHostReloadErrorDoesNotPermitDirtyRecovery()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var model = await SessionReview(session, owner);
        presenter.BeforeLoad = _ => throw new IOException("Reload unavailable.");
        await model.ConfirmAsync();
        presenter.Publish(presenter.State with { IsDirty = true, ContentRevision = 2 });
        int loads = presenter.Loads;
        Require(!model.CanRecover && !model.CanRetry, "Recovery could overwrite unsaved UI changes.");
        await model.RecoverAsync();
        Equal(loads, presenter.Loads);
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task RealHostShellSyncActivationChangePreventsHandoff()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        var model = await SessionReview(session, owner);
        session.ShellSync = () => { session.AdvanceSelectionForTest(); return Task.CompletedTask; };
        await model.ConfirmAsync();
        Require(!model.CanContinue && model.Handoff is null, "Selection change during shell sync escaped fencing.");
        Equal(0, session.Notifications);
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task RealHostReloadGateRejectsNewRevision()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var adapter = new RunnerSessionSr5AfterRunRewardHost(session, owner);
        await session.HoldActivationForTest();
        Task pending;
        try
        {
            pending = adapter.ReloadAsync(fixture.WorkspaceId, default);
            presenter.Publish(presenter.State with { ContentRevision = 2, SavedRevision = 2 });
        }
        finally { session.ReleaseActivationForTest(); }
        await MustReject(() => pending);
        Equal(0, presenter.Loads);
    }

    private static async Task RealHostAwayAndBackFencesReview()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var model = await SessionReview(session, owner);
        var before = presenter.State;
        presenter.Publish(before with { WorkspaceId = new CharacterWorkspaceId("other-runner") });
        presenter.Publish(before);
        Require(!model.CanConfirm, "A -> B -> A reused the original selection generation.");
        await model.ConfirmAsync();
        Equal(0, host.Service.CommitCalls);
    }

    private static async Task RealHostRejectsWrongWorkspaceReload()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var adapter = new RunnerSessionSr5AfterRunRewardHost(session, owner);
        await MustReject(() => adapter.ReloadAsync(new("not-selected"), default));
        Equal(0, presenter.Loads);
    }

    private static async Task RealHostReloadRevalidatesAfterActivationGate()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var adapter = new RunnerSessionSr5AfterRunRewardHost(session, owner);
        await session.HoldActivationForTest();
        Task pending;
        try
        {
            pending = adapter.ReloadAsync(fixture.WorkspaceId, default);
            session.AdvanceSelectionForTest();
        }
        finally { session.ReleaseActivationForTest(); }
        await MustReject(() => pending);
        Equal(0, presenter.Loads);
    }

    private static async Task RealHostOwnerSwitchDuringReloadPreventsHandoff()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var model = await SessionReview(session, owner);
        presenter.BeforeLoad = _ => { owner.CurrentOwnerId = Guid.NewGuid(); return Task.CompletedTask; };
        await model.ConfirmAsync();
        Require(!model.CanContinue && model.Handoff is null, "Reload crossed owner identity.");
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task RealHostBusyAndDirtyFramesCannotOverwriteUnsavedState()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        var adapter = new RunnerSessionSr5AfterRunRewardHost(session, owner);
        var saved = presenter.State;
        foreach (var forbidden in new[]
        {
            saved with { IsBusy = true },
            saved with { IsDirty = true, ContentRevision = 2 },
            saved with { Profile = new(false) },
            saved with { Rules = new("SR6") }
        })
        {
            presenter.Publish(forbidden);
            Require(!adapter.Current.IsCleanSavedSr5(), "An invalid runner frame admitted mutations.");
            await MustReject(() => adapter.ReloadAsync(fixture.WorkspaceId, default));
        }
        Equal(0, presenter.Loads);
    }

    private static async Task RealHostUncomposedAndDisposedSessionsStayUnavailable()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out var presenter, out var owner);
        using var missing = new RunnerSessionCoordinator(presenter, null, null);
        Require(!missing.SupportsAfterRunRewardEntry, "Missing Core composition was enabled.");
        await MustReject(() => { missing.CreateAfterRunRewardModel(DateTime.Now, owner); return Task.CompletedTask; });
        session.Dispose();
        Require(!session.SupportsAfterRunRewardEntry, "Disposed session retained entry capability.");
        var adapter = new RunnerSessionSr5AfterRunRewardHost(session, owner);
        await MustReject(() => adapter.ReloadAsync(fixture.WorkspaceId, default));
        Equal(0, presenter.Loads);
    }

    private static RunnerSessionCoordinator RewardSession(Host host,
        out RewardHostPresenterProbe presenter, out RewardHostOwner owner)
    {
        RewardHostTestState ReadSaved(CharacterWorkspaceId id)
        {
            var saved = host.Fixture.Store.Get(id).Value ?? throw new IOException("Missing saved runner.");
            return new(id, saved.ContentRevision, saved.SavedRevision, false, false, null, new(true), new("SR5"));
        }
        presenter = new(ReadSaved(host.Fixture.WorkspaceId), ReadSaved);
        owner = new() { CurrentOwnerId = host.Authority.Current.OwnerId };
        return new(presenter, host.Service, host.Store);
    }

    private static async Task EntryRecoveryCannotBeHiddenByAnAvailableCatalog()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        var original = await SessionReview(session, owner);
        host.Service.BeforeCommit = _ => throw new IOException("Interrupted before Core Commit.");
        await original.ConfirmAsync();
        int commits = host.Service.CommitCalls;
        int lookups = host.Service.LookupCalls;
        int writes = host.Journal.Writes;
        string sharedOwner = host.OwnerBackend.Read();
        var entry = await session.PrepareAfterRunRewardEntryAsync(allowNewReward: false, owner: owner);
        Require(entry is not null && entry.HasRetainedIntent && !entry.CanEdit && entry.CanRecover,
            "An available or unavailable catalog stranded the original pending local reward.");
        Equal(original.OperationId, entry!.OperationId);
        Equal(original.RewardId, entry.RewardId);
        Equal(commits, host.Service.CommitCalls);
        Equal(lookups, host.Service.LookupCalls);
        Equal(writes, host.Journal.Writes);
        Equal(sharedOwner, host.OwnerBackend.Read());
    }

    private static async Task EntryReleasedHistoryDoesNotReplaceAnAvailableCatalog()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        Require(await session.PrepareAfterRunRewardEntryAsync(false, owner: owner) is null,
            "An empty journal displaced the governed catalog.");
        var original = await SessionReview(session, owner);
        await original.ConfirmAsync();
        Require(original.CanContinue, "Fixture reward was not recorded and reloaded.");
        int lookups = host.Service.LookupCalls;
        int writes = host.Journal.Writes;
        Require(await session.PrepareAfterRunRewardEntryAsync(false, owner: owner) is null,
            "Released historical receipts were mistaken for unfinished recovery.");
        Equal(1, host.Service.CommitCalls);
        Equal(lookups, host.Service.LookupCalls);
        Equal(writes, host.Journal.Writes);
    }

    private static async Task EntryMissingCatalogAllowsOrdinaryLocalReward()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        var entry = await session.PrepareAfterRunRewardEntryAsync(true, owner: owner);
        Require(entry is not null && entry.CanEdit && !entry.HasRetainedIntent,
            "A missing catalog blocked ordinary local rewards.");
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Service.LookupCalls);
        Equal(0, host.Journal.Writes);
    }

    private static async Task EntryCorruptJournalCannotFallThroughToAnotherSettlement()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        host.OwnerBackend.Write("{}");
        await MustReject(() => session.PrepareAfterRunRewardEntryAsync(false, owner: owner));
        Equal("{}", host.OwnerBackend.Read());
        var blocked = await session.PrepareAfterRunRewardEntryAsync(true, owner: owner);
        Require(blocked is not null && !blocked.CanEdit && !blocked.CanRecover
            && blocked.Status == Sr5AfterRunRewardPhoneStatus.JournalUnavailable,
            "A corrupt owner admitted a new local award.");
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
    }

    private static async Task EntryReadRejectsAwayAndBackSelection()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        host.Journal.BeforeRead = () => session.AdvanceSelectionForTest();
        await MustReject(() => session.PrepareAfterRunRewardEntryAsync(true, owner: owner));
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
    }

    private static async Task EntryReadCancellationCannotChooseADestination()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        using var cancellation = new CancellationTokenSource();
        host.Journal.BeforeRead = cancellation.Cancel;
        try
        {
            await session.PrepareAfterRunRewardEntryAsync(true, cancellation.Token, owner);
            throw new InvalidOperationException("Canceled route preparation selected a destination.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Equal(0, host.Service.CommitCalls);
        Equal(0, host.Journal.Writes);
    }

    private static async Task EntryRecoveryPreservesAppliedButUnreleasedOwner()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        using var session = RewardSession(host, out _, out var owner);
        var original = await SessionReview(session, owner);
        host.OwnerBackend.FailRemove = true;
        await original.ConfirmAsync();
        int writes = host.Journal.Writes;
        string sharedOwner = host.OwnerBackend.Read();
        var entry = await session.PrepareAfterRunRewardEntryAsync(false, owner: owner);
        Require(entry is not null && entry.HasRetainedIntent && entry.CanRecover && !entry.CanRetry,
            "An Applied receipt with unresolved shared ownership fell through to another settlement.");
        Equal(original.OperationId, entry!.OperationId);
        Equal(sharedOwner, host.OwnerBackend.Read());
        Equal(writes, host.Journal.Writes);
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task<Sr5AfterRunRewardPhoneModel> SessionReview(
        RunnerSessionCoordinator session, RewardHostOwner owner)
    {
        var model = session.CreateAfterRunRewardModel(new DateTime(2078, 9, 6, 18, 30, 0), owner);
        await model.InitializeAsync();
        Require(model.UpdateDraft(model.Draft with { Karma = "8", Nuyen = "12500", Reason = "Local reward" }), "Draft unavailable.");
        await model.PreviewAsync(CultureInfo.InvariantCulture);
        Require(model.CanConfirm, "Real host could not prepare a review.");
        return model;
    }

    private static async Task MustReject(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected operation to fail closed.");
    }

    private sealed class RewardHostOwner : ISr5CareerCheckpointOwnerAuthority
    {
        public Guid CurrentOwnerId { get; set; }
    }
}
