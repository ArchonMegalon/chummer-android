using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

var backend = new MemoryBackend();
var store = new Sr5TableWizardCheckpointStore(backend);
Sr5TableWizardSnapshot snapshot = Sr5TableWizardProjector.Project(
    Sr5TableWizardLane.BeforeRun,
    new CareerEdgeUseEditorState(
        new CharacterWorkspaceId("workspace-before-run"),
        ContentRevision: 31,
        new CharacterCareerEdgeUseState(
            EdgeUsed: 1,
            TotalEdge: 4,
            CanSpend: true,
            CanRegain: true)));
var session = new Sr5TableWizardSession();
session.Bind(snapshot);
Sr5TableWizardActionState spend = snapshot.Actions.Single(action =>
    action.Identity.Kind == Sr5TableWizardActionKind.SpendEdge);
Assert(session.TrySelect(spend.Identity), "exact Before Run Edge identity must select");
Assert(store.TryWrite(session), "review checkpoint must be write/read-back verified");

Sr5TableWizardCheckpointRead read = store.Read();
Assert(read.Status == Sr5TableWizardCheckpointReadStatus.Ready && read.Checkpoint is not null,
    "verified checkpoint must be readable");
Sr5TableWizardState resumed = new Sr5TableWizardSession().Bind(snapshot, read.Checkpoint);
Assert(resumed.Resume.Restored && resumed.SelectedAction?.Identity == spend.Identity,
    "phone draft must resume the exact typed action");

backend.Payload = "not canonical base64";
Sr5TableWizardCheckpointRead invalid = store.Read();
Assert(invalid.Status == Sr5TableWizardCheckpointReadStatus.Invalid
       && backend.Payload.Length == 0,
    "tampered phone draft must be removed and fail closed");

var corruptingBackend = new MemoryBackend { CorruptReadBack = true };
var corruptingStore = new Sr5TableWizardCheckpointStore(corruptingBackend);
Assert(!corruptingStore.TryWrite(session),
    "navigation must not continue when durable read-back differs");

var unavailableStore = new Sr5TableWizardCheckpointStore(
    new MemoryBackend { ThrowOnRead = true });
Assert(unavailableStore.Read().Status == Sr5TableWizardCheckpointReadStatus.Unavailable,
    "unavailable durable storage must keep review closed");

backend.Payload = Convert.ToBase64String(new byte[40 * 1024]);
Assert(store.Read().Status == Sr5TableWizardCheckpointReadStatus.Invalid,
    "oversized checkpoint payload must be rejected and removed");

AssertCapabilityBoundaries();

var transactionBackend = new MemoryBackend();
var transactionStore = new Sr5TableWizardTransactionStore(transactionBackend);
Guid ownerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
Guid transactionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
Assert(transactionStore.TryWriteReview(session, ownerId, transactionId, out Sr5TableWizardTransactionJournal? review)
       && review is { Phase: Sr5TableWizardTransactionPhase.Reviewed }
       && review.IsExact(),
    "the exact typed quote must persist as a signed review transaction");

// Recreate the store to model process restart before confirmation.
transactionStore = new Sr5TableWizardTransactionStore(transactionBackend);
Assert(transactionStore.TryRead(out Sr5TableWizardTransactionJournal? restartedReview)
       == Sr5TableWizardCheckpointReadStatus.Ready
       && restartedReview == review,
    "a reviewed typed transaction must survive process restart exactly");

var raceBackend = new MemoryBackend();
var raceStore = new Sr5TableWizardTransactionStore(raceBackend);
Assert(raceStore.TryWriteReview(
        session,
        ownerId,
        Guid.Parse("66666666-6666-6666-6666-666666666666"),
        out Sr5TableWizardTransactionJournal? raceReview),
    "concurrency fixture must persist an exact review");
int confirmationWinners = 0;
Parallel.For(0, 16, contenderIndex =>
{
    _ = contenderIndex;
    var contender = new Sr5TableWizardTransactionStore(raceBackend);
    if (contender.TryBeginApplying(raceReview!, out _))
        Interlocked.Increment(ref confirmationWinners);
});
Assert(confirmationWinners == 1,
    "process-wide journal CAS must admit exactly one concurrent confirmation");

Assert(transactionStore.TryBeginApplying(restartedReview!, out Sr5TableWizardTransactionJournal? applying)
       && applying is { Phase: Sr5TableWizardTransactionPhase.Applying },
    "only the exact durable review may enter Applying");
Assert(!transactionStore.TryBeginApplying(restartedReview!, out _),
    "a stale duplicate confirmation must fail closed");

Assert(Sr5TableWizardTypedTransactionPresenter.Observe(applying!, snapshot, out _)
       == Sr5TableWizardRecoveryObservation.Original,
    "restart recovery must recognize that the reviewed mutation did not run");
Assert(transactionStore.TryReturnToReview(applying!, out Sr5TableWizardTransactionJournal? recoveredReview)
       && recoveredReview is { Phase: Sr5TableWizardTransactionPhase.Reviewed },
    "an unobserved Applying transaction may return to explicit review");
Assert(transactionStore.TryBeginApplying(recoveredReview!, out applying),
    "the recovered review may be confirmed once");

Sr5TableWizardSnapshot appliedSnapshot = Sr5TableWizardProjector.Project(
    Sr5TableWizardLane.BeforeRun,
    new CareerEdgeUseEditorState(
        new CharacterWorkspaceId("workspace-before-run"),
        ContentRevision: 32,
        new CharacterCareerEdgeUseState(
            EdgeUsed: 2,
            TotalEdge: 4,
            CanSpend: true,
            CanRegain: true)));
Assert(Sr5TableWizardTypedTransactionPresenter.Observe(applying!, appliedSnapshot, out string postcondition)
       == Sr5TableWizardRecoveryObservation.Applied
       && postcondition == applying!.ExpectedPostconditionDigest,
    "recovery must accept only the exact next-revision typed postcondition");
Assert(transactionStore.TryComplete(applying!, appliedSnapshot, out Sr5TableWizardTransactionJournal? applied)
       && applied is { Phase: Sr5TableWizardTransactionPhase.Applied, Receipt: not null }
       && applied.Receipt.IsExact(),
    "the exact observed postcondition must produce a durable idempotent receipt");
Assert(!transactionStore.TryComplete(applying!, appliedSnapshot, out _),
    "a duplicate completion must not create a second receipt");

transactionStore = new Sr5TableWizardTransactionStore(transactionBackend);
Assert(transactionStore.TryRead(out Sr5TableWizardTransactionJournal? restartedReceipt)
       == Sr5TableWizardCheckpointReadStatus.Ready
       && restartedReceipt == applied,
    "the exact applied receipt must survive process restart");
Assert(transactionStore.TryClearApplied(restartedReceipt!)
       && transactionStore.TryRead(out _) == Sr5TableWizardCheckpointReadStatus.Empty,
    "only the exact applied receipt may be acknowledged and cleared");

var conflictSession = new Sr5TableWizardSession();
conflictSession.Bind(snapshot);
Assert(conflictSession.TrySelect(spend.Identity), "conflict fixture must select exact Edge quote");
Assert(transactionStore.TryWriteReview(
        conflictSession,
        ownerId,
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        out Sr5TableWizardTransactionJournal? conflictReview),
    "conflict fixture must persist its exact review");
Assert(transactionStore.TryBeginApplying(
        conflictReview!,
        out Sr5TableWizardTransactionJournal? conflictApplying),
    "conflict fixture must enter Applying");
Sr5TableWizardSnapshot skippedRevision = Sr5TableWizardProjector.Project(
    Sr5TableWizardLane.BeforeRun,
    new CareerEdgeUseEditorState(
        new CharacterWorkspaceId("workspace-before-run"),
        ContentRevision: 33,
        new CharacterCareerEdgeUseState(
            EdgeUsed: 2,
            TotalEdge: 4,
            CanSpend: true,
            CanRegain: true)));
Assert(Sr5TableWizardTypedTransactionPresenter.Observe(conflictApplying!, skippedRevision, out _)
       == Sr5TableWizardRecoveryObservation.Conflict,
    "a skipped revision must remain locked as an ambiguous conflict");

Guid weaponId = Guid.Parse("44444444-4444-4444-4444-444444444444");
var weaponIdentity = new CharacterWeaponFireIdentity(weaponId, AmmoSlot: 1, Guid.Empty);
var weaponSource = new CharacterWeaponFireSource(
    RangeType: "Ranged",
    Ammo: "30(c)",
    BaseModes: "BF",
    AllowSingleShot: false,
    AllowShortBurst: true,
    AllowLongBurst: true,
    AllowFullBurst: false,
    AllowSuppressiveFire: false,
    SingleShot: 1,
    ShortBurst: 3,
    LongBurst: 6,
    FullBurst: 10,
    SuppressiveFire: 20,
    Accessories: []);
Assert(CharacterWeaponFireRules.TryCreateState(
        weaponIdentity,
        created: true,
        displayName: "Ares Alpha",
        ammoRemaining: 6,
        ammoGearQuantity: null,
        weaponSource,
        hasUnsupportedModeSemantics: false,
        out CharacterWeaponFireState weaponBefore),
    "weapon fixture must be accepted by Core firing authority");
var playtimeWorkspace = new CharacterWorkspaceId("workspace-playtime");
Sr5TableWizardSnapshot weaponSnapshot = Sr5TableWizardProjector.Project(
    Sr5TableWizardLane.Playtime,
    new CareerEdgeUseEditorState(
        playtimeWorkspace,
        ContentRevision: 50,
        new CharacterCareerEdgeUseState(EdgeUsed: 0, TotalEdge: 4, CanSpend: true, CanRegain: false)),
    new CareerWeaponFireCatalogEditorState(
        playtimeWorkspace,
        ContentRevision: 50,
        [new CareerWeaponFireEditorState(playtimeWorkspace, 50, weaponBefore)]));
Sr5TableWizardActionState fire = weaponSnapshot.Actions.Single(action =>
    action.Identity.Kind == Sr5TableWizardActionKind.FireWeapon
    && action.Identity.FireMode == CharacterWeaponFireMode.ShortBurst);
var weaponSession = new Sr5TableWizardSession();
weaponSession.Bind(weaponSnapshot);
Assert(weaponSession.TrySelect(fire.Identity), "exact weapon quote must select");
var weaponBackend = new MemoryBackend();
var weaponStore = new Sr5TableWizardTransactionStore(weaponBackend);
Assert(weaponStore.TryWriteReview(
        weaponSession,
        ownerId,
        Guid.Parse("55555555-5555-5555-5555-555555555555"),
        out Sr5TableWizardTransactionJournal? weaponReview),
    "weapon quote must persist");
Assert(weaponStore.TryBeginApplying(
        weaponReview!,
        out Sr5TableWizardTransactionJournal? weaponApplying),
    "weapon quote must enter Applying once");
Assert(CharacterWeaponFireRules.TryCreateState(
        weaponIdentity,
        created: true,
        displayName: "Ares Alpha",
        ammoRemaining: 3,
        ammoGearQuantity: null,
        weaponSource,
        hasUnsupportedModeSemantics: false,
        out CharacterWeaponFireState weaponAfter),
    "post-fire fixture must be accepted by Core firing authority");
Sr5TableWizardSnapshot weaponAppliedSnapshot = Sr5TableWizardProjector.Project(
    Sr5TableWizardLane.Playtime,
    new CareerEdgeUseEditorState(
        playtimeWorkspace,
        ContentRevision: 51,
        new CharacterCareerEdgeUseState(EdgeUsed: 0, TotalEdge: 4, CanSpend: true, CanRegain: false)),
    new CareerWeaponFireCatalogEditorState(
        playtimeWorkspace,
        ContentRevision: 51,
        [new CareerWeaponFireEditorState(playtimeWorkspace, 51, weaponAfter)]));
Assert(Sr5TableWizardTypedTransactionPresenter.Observe(
        weaponApplying!,
        weaponAppliedSnapshot,
        out string weaponPostcondition)
       == Sr5TableWizardRecoveryObservation.Applied
       && weaponPostcondition == weaponApplying!.ExpectedPostconditionDigest,
    "direct weapon ammunition recovery must require the exact next-revision clip state");
Assert(weaponStore.TryComplete(weaponApplying!, weaponAppliedSnapshot, out var weaponApplied)
       && weaponApplied?.Receipt is { } weaponReceipt
       && weaponReceipt.IsExact()
       && weaponReceipt.ActionKind == Sr5TableWizardActionKind.FireWeapon,
    "direct weapon ammunition must produce the same durable typed receipt");

Console.WriteLine("SR5 Before Run / Playtime Android draft-store tests passed.");

static void AssertCapabilityBoundaries()
{
    Assert(
        Sr5CareerRunCapabilityCatalog.BeforeRun.Single(capability =>
            capability.Id == "before-run-edge").Status
        == Sr5CareerRunCapabilityStatus.Available,
        "Before Run must expose only the existing typed Edge preparation lane");
    foreach (string blocked in new[]
             {
                 "before-run-loadout",
                 "before-run-preparation",
                 "before-run-contacts",
                 "before-run-commitments"
             })
    {
        Assert(
            Sr5CareerRunCapabilityCatalog.BeforeRun.Single(capability =>
                capability.Id == blocked).Status
            == Sr5CareerRunCapabilityStatus.Unavailable,
            $"{blocked} must stay fail-closed without typed authority");
    }

    foreach (string readOnly in new[] { "after-run-karma", "after-run-nuyen" })
    {
        Assert(
            Sr5CareerRunCapabilityCatalog.AfterRun.Single(capability =>
                capability.Id == readOnly).Status
            == Sr5CareerRunCapabilityStatus.ReadOnly,
            $"{readOnly} must remain signed proposal context and never be re-awarded");
    }
    foreach (string available in new[]
             {
                 "after-run-heat",
                 "after-run-street-cred",
                 "after-run-notoriety",
                 "after-run-public-awareness",
                 "after-run-contacts"
             })
    {
        Assert(
            Sr5CareerRunCapabilityCatalog.AfterRun.Single(capability =>
                capability.Id == available).Status
            == Sr5CareerRunCapabilityStatus.Available,
            $"{available} must map to the typed atomic settlement");
    }
    foreach (string blocked in new[]
             {
                 "after-run-injuries",
                 "after-run-ammo",
                 "after-run-loot",
                 "after-run-expenses",
                 "after-run-log"
             })
    {
        Assert(
            Sr5CareerRunCapabilityCatalog.AfterRun.Single(capability =>
                capability.Id == blocked).Status
            == Sr5CareerRunCapabilityStatus.Unavailable,
            $"{blocked} must stay fail-closed without typed authority");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class MemoryBackend : ISr5TableWizardCheckpointBackend
{
    public string Payload { get; set; } = string.Empty;
    public bool CorruptReadBack { get; init; }
    public bool ThrowOnRead { get; init; }

    public string Read()
    {
        if (ThrowOnRead)
            throw new IOException("unavailable");
        return CorruptReadBack && Payload.Length > 0 ? Payload + "A" : Payload;
    }

    public void Write(string payload) => Payload = payload;
    public void Remove() => Payload = string.Empty;
}
