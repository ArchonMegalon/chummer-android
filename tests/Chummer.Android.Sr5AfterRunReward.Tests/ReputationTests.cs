using System.Text.Json;
using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Android.Sr5AfterRunReward.Tests;

internal static partial class ReputationTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> Cases =>
    [
        ("ReputationExactJournalBeforeCoreAndColdReplay", ExactJournalAndReplay),
        ("ReputationDuplicateConfirmEvents", DuplicateConfirm),
        ("ReputationLostCoreResponse", LostCoreResponse),
        ("ReputationLookupDoesNotCommit", LookupDoesNotCommit),
        ("ReputationConfirmationWriteLostAck", () => JournalLostAck(1)),
        ("ReputationApplyingWriteLostAck", () => JournalLostAck(2)),
        ("ReputationReceiptWriteLostAck", () => JournalLostAck(4)),
        ("ReputationOtherCareerOwnerBlocks", OtherOwnerBlocks),
        ("ReputationActivationRoundTripFencesReview", GenerationFences),
        ("ReputationCancellationAfterCommitIsNotRollback", CancellationAfterCommit),
        ("ReputationOwnerSwitchRetainsRecovery", OwnerSwitch),
        ("ReputationCoreAndJournalOffCallerThread", OffThread),
        ("ReputationCorruptJournalPreserved", CorruptJournal),
        ("ReputationBurnUsesRealCore", BurnUsesCore),
        ("ReputationSourceDriftBeforeConfirmation", SourceDrift),
        ("ReputationCoreReadErrorIsPreserved", ReadError),
        ("ReputationMalformedReviewRejected", MalformedReview),
        ("ReputationOwnerReleaseFailureRecovery", ReleaseFailure),
        ("ReputationCoherentForeignReceiptCannotRelease", ForeignReceipt),
        ("ReputationSupersededCommandCanBeExplicitlyClosed", SupersededCommand),
        ("ReputationSupersededLostJournalAcknowledgement", () => SupersededCommand(true)),
        ("ReputationNotFoundAloneCannotCloseUnknownOutcome", NotFoundCannotClose),
        ("ReputationUnavailableLookupCannotCloseUnknownOutcome", UnavailableCannotClose),
        ("ReputationInFlightMutationCannotBeClosed", InFlightCannotClose),
        ("ReputationPhoneUsesPreviewThenExplicitConfirmation", PhoneConfirmation),
        ("ReputationPhoneEditsInvalidateReviewWithoutNewIdentity", PhoneDraft),
        ("ReputationPhoneInvalidInputDoesNotWrite", PhoneInvalid),
        ("ReputationPhoneBurnReviewDoesNotMutate", PhoneBurn),
        ("ReputationPhoneColdEntryDoesNotRetry", PhoneCold),
        ("ReputationPhoneRefreshFailureRetainsAppliedReceipt", PhoneRefreshFailure),
        ("ReputationPhoneCanceledConfirmationKeepsOriginalReview", PhoneCancellation),
        ("ReputationPhoneSupersededOutcomeIsNotSaved", PhoneSuperseded),
        ("ReputationPhoneAwayAndBackClearsVisibleFacts", PhoneChangedRunner)
    ];

    private static async Task ExactJournalAndReplay()
    {
        using var f = new Fixture();
        var review = await f.Review();
        Equal(0, f.Backend.Writes);
        f.Core.BeforeCommit = command =>
        {
            Check(f.Journal.TryGet(f.Authority.Current.OwnerId, f.Id, command.Request.OperationId, out var stored, out _));
            Equal(Sr5CareerReputationPhase.Applying, stored!.Phase);
            Equal(command, stored.Command);
            Check(f.Owner.Read().Length > 0);
        };
        var result = await f.Coordinator.ConfirmAsync(review);
        Recorded(result);
        Equal(1, f.Core.Commits);
        Equal(2L, f.Store.Get(f.Id).Value!.ContentRevision);
        Equal("", f.Owner.Read());
        var coldCore = new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(f.Workspaces), f.Resolver);
        var coldJournal = new Sr5CareerReputationJournal(new FileSr5CareerCommandJournalBackend(f.Root, Sr5CareerCommandJournalDomain.Reputation),
            new Sr5CareerMutationOwnerStore(f.Owner), coldCore);
        var cold = new Sr5CareerReputationCoordinator(coldCore, f.Authority, coldJournal);
        string before = JsonSerializer.Serialize(f.Store.Get(f.Id).Value);
        Recorded(await cold.RecoverAsync(review.Preview.Command.Request.OperationId));
        Equal(before, JsonSerializer.Serialize(f.Store.Get(f.Id).Value));
        Equal(1, (await cold.InspectAsync()).History.Count);
    }

    private static async Task DuplicateConfirm()
    {
        using var f = new Fixture();
        var review = await f.Review();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.Coordinator.ConfirmAsync(review)));
        Recorded(await f.Coordinator.RecoverAsync(review.Preview.Command.Request.OperationId));
        Equal(1, f.Core.Commits);
        Equal("", f.Owner.Read());
        Equal(1, (await f.Coordinator.InspectAsync()).History.Count);
    }

    private static async Task LostCoreResponse()
    {
        using var f = new Fixture();
        var review = await f.Review();
        f.Core.LoseResponse = true;
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown, (await f.Coordinator.ConfirmAsync(review)).Status);
        Equal(2L, f.Store.Get(f.Id).Value!.ContentRevision);
        Check(f.Owner.Read().Length > 0);
        f.Core.LoseResponse = false;
        Recorded(await f.Coordinator.RecoverAsync(review.Preview.Command.Request.OperationId));
        Equal(1, f.Core.Commits);
    }

    private static async Task LookupDoesNotCommit()
    {
        using var f = new Fixture();
        var review = await f.Review();
        f.Core.BeforeCommit = _ => throw new IOException("Simulated failure before Core entry.");
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown, (await f.Coordinator.ConfirmAsync(review)).Status);
        Equal(1L, f.Store.Get(f.Id).Value!.ContentRevision);
        f.Core.BeforeCommit = null;
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown,
            (await f.Coordinator.RecoverAsync(review.Preview.Command.Request.OperationId)).Status);
        Equal(0, f.Core.Commits);
        Recorded(await f.Coordinator.RetryAsync(review.Preview.Command.Request.OperationId));
        Equal(1, f.Core.Commits);
    }

    private static async Task JournalLostAck(int write)
    {
        using var f = new Fixture();
        var review = await f.Review();
        f.Backend.LoseAtWrite = write;
        var result = await f.Coordinator.ConfirmAsync(review);
        if (write < 4) Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown, result.Status);
        Equal(write < 4 ? 0 : 1, f.Core.Commits);
        f.Backend.LoseAtWrite = 0;
        Recorded(await f.Coordinator.RetryAsync(review.Preview.Command.Request.OperationId));
        Equal(1, f.Core.Commits);
        Equal("", f.Owner.Read());
        Equal(1, (await f.Coordinator.InspectAsync()).History.Count);
    }

    private static async Task OtherOwnerBlocks()
    {
        using var f = new Fixture();
        var foreign = new Sr5CareerMutationOwner(1, "attribute-advance", f.Id.Value, Guid.NewGuid(), Guid.NewGuid(), 2, 1, new('a', 64));
        f.Owner.Write(JsonSerializer.Serialize(foreign));
        var result = await f.Coordinator.PreviewAsync(f.Request());
        Check(result.Review is null);
        Equal(0, f.Backend.Writes);
        Equal(0, f.Core.Commits);
        Equal(JsonSerializer.Serialize(foreign), f.Owner.Read());
    }

    private static async Task GenerationFences()
    {
        using var f = new Fixture();
        var review = await f.Review();
        f.Authority.Current = f.Authority.Current with { ActivationGeneration = 3 };
        Equal(Sr5CareerReputationResolutionStatus.Blocked, (await f.Coordinator.ConfirmAsync(review)).Status);
        Equal(0, f.Backend.Writes);
        Equal(0, f.Core.Commits);
    }

    private static async Task CancellationAfterCommit()
    {
        using var f = new Fixture();
        using var token = new CancellationTokenSource();
        f.Core.AfterCommit = () => token.Cancel();
        Recorded(await f.Coordinator.ConfirmAsync(await f.Review(), token.Token));
        Equal(1, f.Core.Commits);
        Equal(2L, f.Store.Get(f.Id).Value!.SavedRevision);
    }

    private static async Task OwnerSwitch()
    {
        using var f = new Fixture();
        var original = f.Authority.Current;
        var review = await f.Review();
        f.Core.AfterCommit = () => f.Authority.Current = original with { OwnerId = Guid.NewGuid(), ActivationGeneration = 2 };
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown, (await f.Coordinator.ConfirmAsync(review)).Status);
        Check(f.Owner.Read().Length > 0);
        f.Authority.Current = original with { ActivationGeneration = 3 };
        Recorded(await f.Coordinator.RecoverAsync(review.Preview.Command.Request.OperationId));
        Equal(1, f.Core.Commits);
    }

    private static async Task OffThread()
    {
        using var f = new Fixture();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int caller = 0;
        var thread = new Thread(() =>
        {
            try
            {
                caller = Environment.CurrentManagedThreadId;
                Equal(CharacterCareerReputationOutcome.Available, f.Coordinator.ReadAsync().GetAwaiter().GetResult().Outcome);
                Recorded(f.Coordinator.ConfirmAsync(f.Review().GetAwaiter().GetResult()).GetAwaiter().GetResult());
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        });
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Check(f.Core.Threads.All(id => id != caller));
        Check(f.Backend.WriteThreads.All(id => id != caller));
    }

    private static async Task CorruptJournal()
    {
        using var f = new Fixture();
        string path = Path.Combine(f.Root, "sr5-career-reputation.v1.json");
        foreach (string payload in new[] { " ", "{}", "{\"SchemaVersion\":1,\"SchemaVersion\":1}", "" })
        {
            File.WriteAllText(path, payload);
            Check((await f.Coordinator.PreviewAsync(f.Request())).Review is null);
            Equal(payload, File.ReadAllText(path));
            Equal(0, f.Core.Commits);
        }
    }

    private static async Task BurnUsesCore()
    {
        using var f = new Fixture();
        var request = f.Request() with { Operation = CharacterCareerReputationOperation.BurnStreetCred, Adjustment = null };
        var review = (await f.Coordinator.PreviewAsync(request)).Review!;
        Equal(6, review.Preview.Quote.Before.TotalStreetCred);
        Recorded(await f.Coordinator.ConfirmAsync(review));
        var xml = XElement.Parse(f.Store.Get(f.Id).Value!.Document.Content);
        Equal("2", xml.Element("burntstreetcred")!.Value);
        Equal("3", xml.Element("streetcred")!.Value);
        Equal("2", xml.Element("notoriety")!.Value);
    }

    private static async Task SourceDrift()
    {
        using var f = new Fixture();
        var review = await f.Review();
        File.AppendAllText(Path.Combine(f.Root, "data", "settings.xml"), "\n<!-- source changed -->");
        Equal(Sr5CareerReputationResolutionStatus.Blocked, (await f.Coordinator.ConfirmAsync(review)).Status);
        Equal(0, f.Core.Commits);
        Equal(0, f.Backend.Writes);
    }

    private static async Task ReadError()
    {
        using var f = new Fixture();
        f.Core.ReadOverride = new(CharacterCareerReputationOutcome.Unavailable, Error: "exact-source-unavailable");
        Equal("exact-source-unavailable", (await f.Coordinator.ReadAsync()).Error);
        Equal(0, f.Backend.Writes);
    }

    private static async Task MalformedReview()
    {
        using var f = new Fixture();
        var review = await f.Review();
        foreach (var bad in new[]
        {
            review with { Preview = review.Preview with { Command = review.Preview.Command with { Binding = null! } } },
            review with { Preview = review.Preview with { PreviewDigest = new('0', 64) } },
            review with { Snapshot = review.Snapshot with { SavedRevision = 2 } }
        }) Equal(Sr5CareerReputationResolutionStatus.Blocked, (await f.Coordinator.ConfirmAsync(bad)).Status);
        Equal(0, f.Core.Commits);
        Equal(0, f.Backend.Writes);
    }

    private static async Task ReleaseFailure()
    {
        using var f = new Fixture();
        var review = await f.Review();
        f.Owner.FailRemove = true;
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown, (await f.Coordinator.ConfirmAsync(review)).Status);
        Check(f.Owner.Read().Length > 0);
        Equal(Sr5CareerReputationPhase.Applied, (await f.Coordinator.InspectAsync()).RecoveryRequired!.Phase);
        f.Owner.FailRemove = false;
        Recorded(await f.Coordinator.RecoverAsync(review.Preview.Command.Request.OperationId));
        Equal("", f.Owner.Read());
        Equal(1, f.Core.Commits);
    }

    private static async Task ForeignReceipt()
    {
        using var f = new Fixture();
        using var other = new Fixture();
        var foreign = await other.Coordinator.ConfirmAsync(await other.Review());
        Recorded(foreign);
        var review = await f.Review();
        f.Core.AfterCommit = () => f.Core.LookupOverride = new(CharacterCareerReputationOutcome.Replayed,
            foreign.Checkpoint!.Receipt, 2);
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown, (await f.Coordinator.ConfirmAsync(review)).Status);
        Check(f.Owner.Read().Length > 0);
        f.Core.LookupOverride = null;
        Recorded(await f.Coordinator.RecoverAsync(review.Preview.Command.Request.OperationId));
        Equal(1, f.Core.Commits);
    }

    private static Task SupersededCommand() => SupersededCommand(false);
    private static async Task SupersededCommand(bool loseAcknowledgement)
    {
        using var f = new Fixture();
        var review = await f.Review();
        var operation = review.Preview.Command.Request.OperationId;
        f.Core.BeforeCommit = _ => f.SavedEdit();
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown, (await f.Coordinator.ConfirmAsync(review)).Status);
        f.Core.BeforeCommit = null;
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown, (await f.Coordinator.RecoverAsync(operation)).Status);
        if (loseAcknowledgement) f.Backend.LoseAtWrite = f.Backend.Writes + 1;
        Equal(Sr5CareerReputationResolutionStatus.Superseded, (await f.Coordinator.CloseSupersededAsync(operation)).Status);
        Equal("", f.Owner.Read());
        Equal(0, (await f.Coordinator.InspectAsync()).History.Count);
        Equal(1, (await f.Coordinator.InspectAsync()).SupersededHistory.Count);
        Equal(Sr5CareerReputationResolutionStatus.Superseded, (await f.Coordinator.RetryAsync(operation)).Status);
        Equal(1, f.Core.Commits); // the rejected old CAS was called only once
        var before = f.Store.Get(f.Id).Value!;
        Equal("3", XElement.Parse(before.Document.Content).Element("streetcred")!.Value);
        Check(before.Document.AuxiliaryState.CharacterCareerReputationReceipts is not { Count: > 0 });
        f.Authority.Current = f.Authority.Current with { ContentRevision = 2, SavedRevision = 2 };
        Recorded(await f.Coordinator.ConfirmAsync(await f.Review()));
        Equal(1, (await f.Coordinator.InspectAsync()).History.Count);
        Equal(1, (await f.Coordinator.InspectAsync()).SupersededHistory.Count);
    }

    private static async Task NotFoundCannotClose()
    {
        using var f = new Fixture();
        var review = await f.Review();
        f.Core.BeforeCommit = _ => throw new IOException("Not entered.");
        await f.Coordinator.ConfirmAsync(review);
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown,
            (await f.Coordinator.CloseSupersededAsync(review.Preview.Command.Request.OperationId)).Status);
        Check(f.Owner.Read().Length > 0);
        Equal(0, (await f.Coordinator.InspectAsync()).SupersededHistory.Count);
        f.Core.BeforeCommit = null;
        Recorded(await f.Coordinator.RetryAsync(review.Preview.Command.Request.OperationId));
        Equal(1, f.Core.Commits);
    }

    private static async Task UnavailableCannotClose()
    {
        using var f = new Fixture();
        var review = await f.Review();
        f.Core.BeforeCommit = _ => f.SavedEdit();
        await f.Coordinator.ConfirmAsync(review);
        f.Core.LookupOverride = new(CharacterCareerReputationOutcome.Unavailable, CurrentWorkspaceRevision: 2);
        Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown,
            (await f.Coordinator.CloseSupersededAsync(review.Preview.Command.Request.OperationId)).Status);
        Check(f.Owner.Read().Length > 0);
        Equal(0, (await f.Coordinator.InspectAsync()).SupersededHistory.Count);
    }

    private static async Task InFlightCannotClose()
    {
        using var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var review = await f.Review();
        f.Core.BeforeCommit = _ => { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(20))); };
        var commit = f.Coordinator.ConfirmAsync(review);
        try
        {
            Check(entered.Wait(TimeSpan.FromSeconds(10)));
            Equal(Sr5CareerReputationResolutionStatus.OutcomeUnknown,
                (await f.Coordinator.CloseSupersededAsync(review.Preview.Command.Request.OperationId).WaitAsync(TimeSpan.FromSeconds(10))).Status);
            Check(f.Owner.Read().Length > 0);
        }
        finally { release.Set(); }
        Recorded(await commit);
        Equal(0, (await f.Coordinator.InspectAsync()).SupersededHistory.Count);
        Equal(1, f.Core.Commits);
    }

    private static async Task<Sr5CareerReputationPhoneModel> PhoneReview(Fixture f)
    {
        var model = f.Phone();
        await model.InitializeAsync();
        Check(model.CanEdit);
        Check(model.UpdateDraft(new("+1", "-1", "", "After Run")));
        await model.PreviewAsync(culture: CultureInfo.GetCultureInfo("de-AT"));
        Check(model.CanConfirm);
        return model;
    }

    private static async Task PhoneConfirmation()
    {
        using var f = new Fixture();
        var model = await PhoneReview(f);
        Equal(0, f.Core.Commits);
        Equal(0, f.Backend.Writes);
        Equal(6, model.Review!.Preview.Quote.Before.TotalStreetCred);
        Equal(7, model.Review.Preview.Quote.After.TotalStreetCred);
        await Task.WhenAll(model.ConfirmAsync(), model.ConfirmAsync());
        Equal(Sr5CareerReputationPhoneStatus.Recorded, model.Status);
        Check(model.CanFinish && !model.CanEdit);
        Equal(1, f.Refresh.Calls);
        Equal(2L, model.Snapshot!.SavedRevision);
        Equal(1, f.Core.Commits);
    }

    private static async Task PhoneDraft()
    {
        using var f = new Fixture();
        var model = await PhoneReview(f);
        Guid original = model.OperationId;
        model.UpdateDraft(model.Draft with { StreetCred = "+2" });
        Check(model.Review is null && model.CanEdit && !model.CanConfirm);
        Equal(original, model.OperationId);
        await model.ConfirmAsync();
        Equal(0, f.Core.Commits);
        await model.PreviewAsync();
        Check(model.CanConfirm);
    }

    private static async Task PhoneInvalid()
    {
        using var f = new Fixture();
        var model = f.Phone();
        await model.InitializeAsync();
        foreach (string value in new[] { "", "one", "1.5", "1,5", "2147483648", "-" })
        {
            model.UpdateDraft(new(value, "", "", ""));
            await model.PreviewAsync(culture: CultureInfo.GetCultureInfo("es-MX"));
            Check(!model.CanConfirm && model.CanEdit);
            await model.ConfirmAsync();
        }
        model.UpdateDraft(new("-99", "", "", ""));
        await model.PreviewAsync(); // valid integer, illegal Core outcome
        Equal(Sr5CareerReputationPhoneStatus.ReviewUnavailable, model.Status);
        Check(model.CanEdit);
        model.UpdateDraft(new("1", "", "", ""));
        await model.PreviewAsync();
        Check(model.CanConfirm);
        Equal(0, f.Core.Commits);
        Equal(0, f.Backend.Writes);
    }

    private static async Task PhoneBurn()
    {
        using var f = new Fixture();
        var model = f.Phone();
        await model.InitializeAsync();
        await model.PreviewAsync(burnStreetCred: true);
        Check(model.CanConfirm);
        Equal(CharacterCareerReputationOperation.BurnStreetCred, model.Review!.Preview.Command.Request.Operation);
        Equal(0, f.Core.Commits);
        await model.ConfirmAsync();
        Equal(Sr5CareerReputationPhoneStatus.Recorded, model.Status);
        Equal(2, model.Checkpoint!.Receipt!.Quote.After.Inputs.BurntStreetCred);
    }

    private static async Task PhoneCold()
    {
        using var f = new Fixture();
        var original = await PhoneReview(f);
        f.Core.BeforeCommit = _ => throw new IOException("Did not enter Core.");
        await original.ConfirmAsync();
        var cold = f.Phone();
        await cold.InitializeAsync();
        Equal(Sr5CareerReputationPhoneStatus.Pending, cold.Status);
        Equal(original.OperationId, cold.OperationId);
        Check(!cold.CanEdit && !cold.CanConfirm && cold.CanRecover && cold.CanRetry);
        Equal(0, f.Core.Commits);
        f.Core.BeforeCommit = null;
        await cold.RecoverAsync();
        Equal(0, f.Core.Commits);
        await cold.RetryAsync();
        Equal(Sr5CareerReputationPhoneStatus.Recorded, cold.Status);
        Equal(1, f.Core.Commits);
    }

    private static async Task PhoneRefreshFailure()
    {
        using var f = new Fixture();
        var model = await PhoneReview(f);
        f.Refresh.Fail = true;
        await model.ConfirmAsync();
        Equal(Sr5CareerReputationPhoneStatus.RefreshRequired, model.Status);
        Equal(Sr5CareerReputationPhase.Applied, model.Checkpoint!.Phase);
        Check(model.CanRecover && !model.CanFinish && !model.CanRetry);
        f.Refresh.Fail = false;
        await model.RecoverAsync();
        Equal(Sr5CareerReputationPhoneStatus.Recorded, model.Status);
        Equal(1, f.Core.Commits);
    }

    private static async Task PhoneCancellation()
    {
        using var f = new Fixture();
        var model = await PhoneReview(f);
        using var token = new CancellationTokenSource();
        token.Cancel();
        await model.ConfirmAsync(token.Token);
        Check(model.CanConfirm && !model.HasRetainedIntent);
        Equal(0, f.Backend.Writes);
    }

    private static async Task PhoneSuperseded()
    {
        using var f = new Fixture();
        var model = await PhoneReview(f);
        f.Core.BeforeCommit = _ => f.SavedEdit();
        await model.ConfirmAsync();
        Equal(Sr5CareerReputationPhoneStatus.OutcomeUnknown, model.Status);
        await model.CloseSupersededAsync();
        Equal(Sr5CareerReputationPhoneStatus.Superseded, model.Status);
        Check(model.CanFinish && model.Checkpoint!.Receipt is null && !model.CanRetry);
        Equal(1, f.Core.Commits);
        Equal(6, model.Snapshot!.Reputation.TotalStreetCred);
    }

    private static async Task PhoneChangedRunner()
    {
        using var f = new Fixture();
        var model = await PhoneReview(f);
        f.Authority.Current = f.Authority.Current with { ActivationGeneration = 3 };
        Check(!model.CanConfirm && !model.CanFinish && !model.CanEdit);
        await model.InitializeAsync();
        Equal(Sr5CareerReputationPhoneStatus.RunnerChanged, model.Status);
        Check(model.Snapshot is null && model.Review is null);
        Equal(0, f.Core.Commits);
    }

    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Reputation assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }
    private static void Recorded(Sr5CareerReputationResolution result)
    { if (result.Status != Sr5CareerReputationResolutionStatus.Recorded) throw new InvalidOperationException(result.Message); }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-android-reputation-").FullName;
        public string Workspaces => Path.Combine(Root, "store");
        public CharacterWorkspaceId Id { get; } = new("android-reputation-test");
        public FileWorkspaceStore Store { get; }
        public FileSystemCharacterSourceDataResolver Resolver { get; }
        public TrackedCore Core { get; }
        public MemoryOwner Owner { get; } = new();
        public Authority Authority { get; }
        public TrackedJournal Backend { get; }
        public Sr5CareerReputationJournal Journal { get; }
        public Sr5CareerReputationCoordinator Coordinator { get; }
        public SavedRefresh Refresh { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "data"));
            foreach (string name in new[] { "settings.xml", "books.xml" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, "core-data", name), Path.Combine(Root, "data", name));
            Store = new(Workspaces);
            Check(Store.CreateWorkspaceDocument(Id, new WorkspaceDocument("""
                <character><settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings><created>True</created>
                <karma>130</karma><nuyen>1000</nuyen><streetcred>3</streetcred><notoriety>2</notoriety>
                <publicawareness>1</publicawareness><burntstreetcred>0</burntstreetcred><contacts/><improvements/>
                <expenses><expense><type>Karma</type><amount>30</amount></expense></expenses></character>
                """, "sr5")).Success);
            Check(Store.SaveCheckpoint(Id, 1).Success);
            Resolver = new(new FileSystemContentOverlayCatalogService(Root, Root, null));
            Core = new(new WorkspaceCharacterCareerReputationService(Store, Resolver));
            Authority = new(new(Guid.NewGuid(), Id, 1, true, "SR5", 1, 1, false, null));
            Backend = new(new FileSr5CareerCommandJournalBackend(Root, Sr5CareerCommandJournalDomain.Reputation));
            Journal = new(Backend, new Sr5CareerMutationOwnerStore(Owner), Core);
            Coordinator = new(Core, Authority, Journal);
            Refresh = new(this);
        }
        public Sr5CareerReputationPhoneModel Phone() => new(Coordinator, Authority, Refresh);
        public CharacterCareerReputationRequest Request() => new(Id, Guid.NewGuid(), CharacterCareerReputationOperation.AdjustManualAwards, new(1, -1, 2), "After Run consequence");
        public async Task<Sr5CareerReputationReview> Review()
        { var result = await Coordinator.PreviewAsync(Request()); return result.Review ?? throw new InvalidOperationException(result.Error); }
        public void SavedEdit()
        {
            var saved = Store.Get(Id).Value!;
            var document = saved.Document with { State = saved.Document.State with
            { Payload = saved.Document.Content.Replace("</character>", "<notes>Later edit</notes></character>", StringComparison.Ordinal) } };
            Check(Store.ReplaceWorkspaceDocument(Id, saved.ContentRevision, document).Success);
            Check(Store.SaveCheckpoint(Id, saved.ContentRevision + 1).Success);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class Authority(Sr5AfterRunRewardRunnerBinding initial) : ISr5AfterRunRewardRunnerAuthority
    { private Sr5AfterRunRewardRunnerBinding _current = initial; public Sr5AfterRunRewardRunnerBinding Current { get => Volatile.Read(ref _current); set => Volatile.Write(ref _current, value); } }
    private sealed class SavedRefresh(Fixture fixture) : ISr5AfterRunRewardSavedRunnerRefresh
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Task ReloadAsync(CharacterWorkspaceId id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            if (Fail) throw new IOException("Presenter reload unavailable.");
            Equal(fixture.Id, id);
            var saved = fixture.Store.Get(id).Value!;
            fixture.Authority.Current = fixture.Authority.Current with { ContentRevision = saved.ContentRevision, SavedRevision = saved.SavedRevision };
            return Task.CompletedTask;
        }
    }
    private sealed class MemoryOwner : ISr5CareerCheckpointBackend
    {
        private string _value = "";
        public bool FailRemove { get; set; }
        public string Read() => _value;
        public void Write(string payload) => _value = payload;
        public void Remove() { if (FailRemove) throw new IOException("Owner release failed."); _value = ""; }
    }
    private sealed class TrackedJournal(ISr5CareerCommandJournalBackend inner) : ISr5CareerCommandJournalBackend
    {
        public int Writes { get; private set; }
        public int LoseAtWrite { get; set; }
        public List<int> WriteThreads { get; } = [];
        public string Read() => inner.Read();
        public void Write(string payload)
        { Writes++; WriteThreads.Add(Environment.CurrentManagedThreadId); inner.Write(payload); if (Writes == LoseAtWrite) throw new IOException("Simulated journal ack loss."); }
    }
    private sealed class TrackedCore(ICharacterCareerReputationService inner) : ICharacterCareerReputationService
    {
        public int Commits { get; private set; }
        public Action<CharacterCareerReputationCommand>? BeforeCommit { get; set; }
        public Action? AfterCommit { get; set; }
        public bool LoseResponse { get; set; }
        public ConcurrentQueue<int> Threads { get; } = new();
        public CharacterCareerReputationReadResult? ReadOverride { get; set; }
        public CharacterCareerReputationResult? LookupOverride { get; set; }
        public CharacterCareerReputationReadResult Read(CharacterWorkspaceId id) { Threads.Enqueue(Environment.CurrentManagedThreadId); return ReadOverride ?? inner.Read(id); }
        public CharacterCareerReputationPreviewResult Preview(CharacterCareerReputationRequest request) { Threads.Enqueue(Environment.CurrentManagedThreadId); return inner.Preview(request); }
        public CharacterCareerReputationResult Lookup(CharacterWorkspaceId id, Guid operation, string digest) { Threads.Enqueue(Environment.CurrentManagedThreadId); return LookupOverride ?? inner.Lookup(id, operation, digest); }
        public CharacterCareerReputationResult Commit(CharacterCareerReputationCommand command, CancellationToken token = default)
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId); BeforeCommit?.Invoke(command); Commits++;
            var result = inner.Commit(command, token); AfterCommit?.Invoke();
            if (LoseResponse) throw new IOException("Simulated lost Core response.");
            return result;
        }
    }
}
