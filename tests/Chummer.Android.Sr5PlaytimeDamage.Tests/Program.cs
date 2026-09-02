using Chummer.Android.Native;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

CharacterWorkspaceId workspaceId = new("workspace-playtime-damage");
ConditionMonitorEditorState editor = Editor(physical: 1, stun: 2);
Assert(Sr5PlaytimeDamageIntegrity.TryProject(
        characterCreated: true,
        gameEdition: "SR5",
        workspaceId,
        workspaceRevision: 40,
        savedRevision: 40,
        isDirty: false,
        error: null,
        editor,
        WorkspaceConditionMonitorTrack.Physical,
        out Sr5PlaytimeDamageSnapshot physical),
    "an exact clean saved SR5 Physical track must project");
Assert(physical.IsExact() && physical.Filled == 1 && physical.EditableMaximum == 12,
    "Physical snapshot must bind current and maximum boxes");
Assert(Sr5PlaytimeDamageIntegrity.TryProject(
        true,
        "sr5",
        workspaceId,
        40,
        40,
        false,
        null,
        editor,
        WorkspaceConditionMonitorTrack.Stun,
        out Sr5PlaytimeDamageSnapshot stun),
    "Stun must use the same closed authority");
Assert(stun.Filled == 2 && stun.EditableMaximum == 10,
    "Stun snapshot must bind its independent track");
Assert(!Sr5PlaytimeDamageIntegrity.TryProject(
        true,
        "SR5",
        workspaceId,
        40,
        40,
        false,
        null,
        editor,
        (WorkspaceConditionMonitorTrack)999,
        out _),
    "future or forged condition-track enum values must not widen the Physical/Stun authority");
ConditionMonitorEditorState alternateTrack = editor with
{
    Tracks =
    [
        editor.Tracks[0] with { Label = "Core", ActsAsAlternateTrack = true },
        editor.Tracks[1]
    ]
};
Assert(!Sr5PlaytimeDamageIntegrity.TryProject(
        true,
        "SR5",
        workspaceId,
        40,
        40,
        false,
        null,
        alternateTrack,
        WorkspaceConditionMonitorTrack.Physical,
        out _),
    "alternate Core/Matrix tracks must not enter the bounded runner Physical/Stun flow");

foreach ((long saved, bool dirty, string? error, bool expected) in new[]
         {
             (40L, false, (string?)null, true),
             (39L, false, (string?)null, false),
             (40L, true, (string?)null, false),
             (40L, false, (string?)"workspace-error", false)
         })
{
    Assert(Sr5PlaytimeDamageIntegrity.TryProject(
            true,
            "SR5",
            workspaceId,
            40,
            saved,
            dirty,
            error,
            editor,
            WorkspaceConditionMonitorTrack.Physical,
            out _) == expected,
        "only one clean saved revision may project a damage quote");
}

Guid actionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
Guid ownerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
Assert(Sr5PlaytimeDamageIntegrity.TryQuote(physical, filledAfter: 4, actionId, out Sr5PlaytimeDamageQuote quote)
       && quote.IsExact(),
    "a bounded changed value must produce an exact quote");
Assert(!Sr5PlaytimeDamageIntegrity.TryQuote(physical, physical.Filled, Guid.NewGuid(), out _)
       && !Sr5PlaytimeDamageIntegrity.TryQuote(physical, physical.EditableMaximum + 1, Guid.NewGuid(), out _),
    "no-op and out-of-range values must fail closed");

var backend = new DamageMemoryBackend();
Sr5PlaytimeDamageJournalStore store = Sr5PlaytimeDamageJournalStore.CreateIsolated(backend);
Assert(store.TryWriteReview(quote, ownerId, out Sr5PlaytimeDamageJournal review, out string blocker), blocker);
Assert(review.IsExact() && review.Phase == Sr5PlaytimeDamageTransactionPhase.Reviewed,
    "review must be durable and exact");

store = Sr5PlaytimeDamageJournalStore.CreateIsolated(backend);
Assert(store.TryRead(out Sr5PlaytimeDamageJournal? restarted, out blocker)
       && restarted == review,
    "review must survive process restart");
Assert(store.TryBeginApplying(restarted!, out Sr5PlaytimeDamageJournal applying, out blocker), blocker);
Assert(!store.TryBeginApplying(restarted!, out _, out _),
    "stale duplicate confirmation must not claim mutation");

Assert(Sr5PlaytimeDamageIntegrity.Observe(applying, physical, out _)
       == Sr5PlaytimeDamageRecoveryObservation.Original,
    "same-revision exact state proves that mutation did not run");
Assert(store.TryReturnToReview(applying, out Sr5PlaytimeDamageJournal recovered, out blocker), blocker);
Assert(store.TryBeginApplying(recovered, out applying, out blocker), blocker);

ConditionMonitorEditorState afterEditor = Editor(physical: 4, stun: 2);
Assert(Sr5PlaytimeDamageIntegrity.TryProject(
        true,
        "SR5",
        workspaceId,
        41,
        41,
        false,
        null,
        afterEditor,
        WorkspaceConditionMonitorTrack.Physical,
        out Sr5PlaytimeDamageSnapshot observed),
    "the exact successor snapshot must project");
Assert(Sr5PlaytimeDamageIntegrity.Observe(applying, observed, out string postcondition)
       == Sr5PlaytimeDamageRecoveryObservation.Applied
       && postcondition == applying.ExpectedPostconditionDigest,
    "only the exact next-revision postcondition may classify as applied");
Assert(store.TryComplete(applying, observed, out Sr5PlaytimeDamageJournal applied, out blocker), blocker);
Assert(applied.IsExact()
       && applied.Receipt is { } receipt
       && receipt.IsExact()
       && receipt.Track == WorkspaceConditionMonitorTrack.Physical
       && receipt.Label == "Physical"
       && receipt.FilledBefore == 1
       && receipt.FilledAfter == 4,
    "completion must emit the exact typed damage receipt");

Assert(Sr5PlaytimeDamageIntegrity.TryProject(
        true,
        "SR5",
        workspaceId,
        42,
        42,
        false,
        null,
        afterEditor,
        WorkspaceConditionMonitorTrack.Physical,
        out Sr5PlaytimeDamageSnapshot skipped),
    "skipped-revision fixture must project");
Assert(Sr5PlaytimeDamageIntegrity.Observe(applying, skipped, out _)
       == Sr5PlaytimeDamageRecoveryObservation.Conflict,
    "skipped revision must remain outcome-unknown");

Sr5PlaytimeDamageJournal forged = applied with
{
    Quote = applied.Quote with { FilledAfter = 5 }
};
Assert(!forged.IsExact(), "tampering with the reviewed target must invalidate the journal");
Assert(store.TryClearApplied(applied, out blocker), blocker);
Assert(!store.TryRead(out _, out blocker) && string.IsNullOrWhiteSpace(blocker),
    "only the exact applied receipt may be acknowledged and cleared");

var corrupting = new DamageMemoryBackend { FailReadBack = true };
var corruptingStore = Sr5PlaytimeDamageJournalStore.CreateIsolated(corrupting);
Assert(!corruptingStore.TryWriteReview(quote, ownerId, out _, out blocker)
       && !string.IsNullOrWhiteSpace(blocker),
    "nondurable or noncanonical storage must keep the mutation closed");

var resolutionBackend = new DamageMemoryBackend();
var ownerBackend = new DamageOwnerBackend();
var ownerStore = new Sr5CareerMutationOwnerStore(ownerBackend);
var partialResolutionStore = new Sr5PlaytimeDamageJournalStore(
    resolutionBackend,
    ownerStore);
Assert(partialResolutionStore.TryWriteReview(
        quote,
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        out Sr5PlaytimeDamageJournal partialReview,
        out blocker),
    blocker);
Assert(partialResolutionStore.TryBeginApplying(
        partialReview,
        out Sr5PlaytimeDamageJournal partialApplying,
        out blocker),
    blocker);
ownerBackend.FailNextRemove = true;
Assert(!partialResolutionStore.TryComplete(
        partialApplying,
        observed,
        out Sr5PlaytimeDamageJournal partialApplied,
        out blocker)
       && partialApplied.IsExact()
       && blocker.Contains("outcome is durable", StringComparison.OrdinalIgnoreCase),
    "a failed owner release must report the durable resolved journal without authorizing replay");
Assert(!ownerStore.TryRunWhenUnowned(() => (true, string.Empty), out _),
    "the unresolved shared owner must still block other Career mutation lanes");
var restartedResolutionStore = new Sr5PlaytimeDamageJournalStore(
    resolutionBackend,
    new Sr5CareerMutationOwnerStore(ownerBackend));
ownerBackend.FailNextRemove = true;
Assert(!restartedResolutionStore.TryRead(out _, out blocker)
       && !string.IsNullOrWhiteSpace(blocker),
    "a resolved receipt must stay inaccessible while its matching owner cannot be released");
Assert(!new Sr5CareerMutationOwnerStore(ownerBackend).TryRunWhenUnowned(
        () => (true, string.Empty),
        out _),
    "failed restart reconciliation must keep every other Career mutation blocked");
Assert(restartedResolutionStore.TryRead(
        out Sr5PlaytimeDamageJournal? reconciledApplied,
        out blocker)
       && reconciledApplied == partialApplied,
    blocker);
Assert(new Sr5CareerMutationOwnerStore(ownerBackend).TryRunWhenUnowned(
        () => (true, string.Empty),
        out blocker),
    "an exact resolved journal read must reconcile only its matching durable owner");

CharacterWorkspaceId otherWorkspaceId = new("workspace-playtime-damage-other-runner");
Sr5PlaytimeDamageJournalStore defaultStore =
    Sr5PlaytimeDamageJournalStore.CreateDefault(
        WorkspaceConditionMonitorTrack.Physical,
        workspaceId);
Assert(defaultStore.TryWriteReview(
        quote,
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        out Sr5PlaytimeDamageJournal workspaceReview,
        out blocker),
    blocker);
Sr5PlaytimeDamageJournalStore otherRunnerStore =
    Sr5PlaytimeDamageJournalStore.CreateDefault(
        WorkspaceConditionMonitorTrack.Physical,
        otherWorkspaceId);
Assert(!otherRunnerStore.TryRead(out _, out blocker)
       && string.IsNullOrWhiteSpace(blocker),
    "damage recovery journals must be isolated by runner workspace and track");
Assert(defaultStore.TryDiscardReview(workspaceReview, out blocker), blocker);

Console.WriteLine("SR5 Playtime Physical/Stun damage transaction tests passed.");

static ConditionMonitorEditorState Editor(int physical, int stun)
    => new(
        CareerEditable: true,
        Tracks:
        [
            new ConditionMonitorTrackState(
                WorkspaceConditionMonitorTrack.Physical,
                "Physical",
                physical,
                TrackMaximum: 10,
                Overflow: 2,
                EditableMaximum: 12,
                ThresholdOffset: 0,
                NaturalRecovery: "Body",
                ActsAsAlternateTrack: false),
            new ConditionMonitorTrackState(
                WorkspaceConditionMonitorTrack.Stun,
                "Stun",
                stun,
                TrackMaximum: 10,
                Overflow: 0,
                EditableMaximum: 10,
                ThresholdOffset: 0,
                NaturalRecovery: "Willpower",
                ActsAsAlternateTrack: false)
        ]);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
