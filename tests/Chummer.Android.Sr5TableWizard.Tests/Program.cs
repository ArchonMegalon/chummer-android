using Chummer.Android.Native;
using Chummer.Android.Proof;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

var backend = new MemoryBackend();
var store = new Sr5TableWizardCheckpointStore(backend);
await AssertTableAuthorityRequiresDurableCheckpointAsync();
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

AssertObservedSnapshotConflict(
    applying!,
    snapshot with { Schema = "chummer.sr5_table_wizard.snapshot.invalid" },
    "a snapshot with an unknown schema must fail closed before recovery classification");
AssertObservedSnapshotConflict(
    applying!,
    snapshot with { SnapshotDigest = "sha256:" + new string('0', 64) },
    "a snapshot whose digest does not match its typed projection must fail closed");
AssertObservedSnapshotConflict(
    applying!,
    snapshot with { Actions = [] },
    "a snapshot with a truncated typed action catalog must fail closed");

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

AssertApi36ProofStateContract();

Console.WriteLine("SR5 Before Run / Playtime Android draft-store tests passed.");

static async Task AssertTableAuthorityRequiresDurableCheckpointAsync()
{
    var workspaceId = new CharacterWorkspaceId("workspace-table-authority");

    RunnerSessionCoordinator dirty = AuthorityCoordinator(
        workspaceId,
        contentRevision: 1,
        savedRevision: 0,
        isDirty: true);
    Assert(await new RunnerSessionSr5TableWizardPhoneAuthority(dirty)
            .LoadAsync(Sr5TableWizardLane.Playtime) is null,
        "Playtime must not expose table authority for the imported dirty 1/0 workspace");
    Assert(dirty.EdgeReadCount == 0 && dirty.WeaponReadCount == 0,
        "dirty authority must fail before any typed table projection is read");

    RunnerSessionCoordinator mismatched = AuthorityCoordinator(
        workspaceId,
        contentRevision: 2,
        savedRevision: 1,
        isDirty: false);
    Assert(await new RunnerSessionSr5TableWizardPhoneAuthority(mismatched)
            .LoadAsync(Sr5TableWizardLane.BeforeRun) is null,
        "revision mismatch must fail closed even when a hostile state reports IsDirty=false");
    Assert(mismatched.EdgeReadCount == 0,
        "revision mismatch must fail before composing table authority");

    RunnerSessionCoordinator hostileDirtyFlag = AuthorityCoordinator(
        workspaceId,
        contentRevision: 3,
        savedRevision: 3,
        isDirty: true);
    Assert(await new RunnerSessionSr5TableWizardPhoneAuthority(hostileDirtyFlag)
            .LoadAsync(Sr5TableWizardLane.BeforeRun) is null,
        "IsDirty must fail closed even when revision numbers appear checkpointed");

    RunnerSessionCoordinator clean = AuthorityCoordinator(
        workspaceId,
        contentRevision: 4,
        savedRevision: 4,
        isDirty: false);
    Sr5TableWizardSnapshot? cleanSnapshot =
        await new RunnerSessionSr5TableWizardPhoneAuthority(clean)
            .LoadAsync(Sr5TableWizardLane.BeforeRun);
    Assert(cleanSnapshot is { WorkspaceRevision: 4 },
        "a clean exact saved workspace must preserve Before Run table authority");
    Assert(clean.EdgeReadCount == 1 && clean.WeaponReadCount == 0,
        "Before Run clean authority must read only its typed Edge projection");

    var weaponIdentity = new CharacterWeaponFireIdentity(
        Guid.Parse("77777777-7777-4777-8777-777777777777"),
        AmmoSlot: 1,
        Guid.Empty);
    Assert(CharacterWeaponFireRules.TryCreateState(
            weaponIdentity,
            created: true,
            displayName: "Checkpoint Test Weapon",
            ammoRemaining: 6,
            ammoGearQuantity: null,
            new CharacterWeaponFireSource(
                RangeType: "Ranged",
                Ammo: "6(c)",
                BaseModes: "BF",
                AllowSingleShot: false,
                AllowShortBurst: true,
                AllowLongBurst: false,
                AllowFullBurst: false,
                AllowSuppressiveFire: false,
                SingleShot: 1,
                ShortBurst: 3,
                LongBurst: 6,
                FullBurst: 10,
                SuppressiveFire: 20,
                Accessories: []),
            hasUnsupportedModeSemantics: false,
            out CharacterWeaponFireState weapon),
        "clean Playtime authority fixture must be valid");
    RunnerSessionCoordinator cleanPlaytime = AuthorityCoordinator(
        workspaceId,
        contentRevision: 4,
        savedRevision: 4,
        isDirty: false);
    cleanPlaytime.WeaponState = new CareerWeaponFireCatalogEditorState(
        workspaceId,
        4,
        [new CareerWeaponFireEditorState(workspaceId, 4, weapon)]);
    Sr5TableWizardSnapshot? cleanPlaytimeSnapshot =
        await new RunnerSessionSr5TableWizardPhoneAuthority(cleanPlaytime)
            .LoadAsync(Sr5TableWizardLane.Playtime);
    Assert(cleanPlaytimeSnapshot is { WorkspaceRevision: 4 }
           && cleanPlaytimeSnapshot.Actions.Any(action =>
               action.Identity.Kind == Sr5TableWizardActionKind.FireWeapon),
        "a clean exact saved workspace must preserve Playtime weapon authority");
    Assert(cleanPlaytime.EdgeReadCount == 1 && cleanPlaytime.WeaponReadCount == 1,
        "clean Playtime authority must compose each typed projection once");

    RunnerSessionCoordinator raced = AuthorityCoordinator(
        workspaceId,
        contentRevision: 5,
        savedRevision: 5,
        isDirty: false);
    raced.AfterEdgeRead = () =>
    {
        raced.State.ContentRevision = 6;
        raced.State.IsDirty = true;
    };
    Assert(await new RunnerSessionSr5TableWizardPhoneAuthority(raced)
            .LoadAsync(Sr5TableWizardLane.BeforeRun) is null,
        "a workspace that becomes dirty during projection must not establish a review snapshot");
}

static RunnerSessionCoordinator AuthorityCoordinator(
    CharacterWorkspaceId workspaceId,
    long contentRevision,
    long savedRevision,
    bool isDirty)
{
    var coordinator = new RunnerSessionCoordinator();
    coordinator.State.WorkspaceId = workspaceId;
    coordinator.State.ContentRevision = contentRevision;
    coordinator.State.SavedRevision = savedRevision;
    coordinator.State.IsDirty = isDirty;
    coordinator.EdgeState = new CareerEdgeUseEditorState(
        workspaceId,
        contentRevision,
        new CharacterCareerEdgeUseState(
            EdgeUsed: 0,
            TotalEdge: 4,
            CanSpend: true,
            CanRegain: false));
    return coordinator;
}

static void AssertApi36ProofStateContract()
{
    var build = new Api36ProofBuildIdentity(
        new string('1', 40),
        new string('2', 40),
        new string('3', 64),
        "hosted-123-1",
        "com.myexternalbrain.chummer",
        "0.1.0-preview.10",
        "10",
        "android-x64");
    var surface = new Api36ProofSurfaceState(
        "runner",
        "sr5-career/before-run/review",
        4,
        "before-run",
        "review-ready",
        Settled: true);
    var workspace = new Api36ProofWorkspaceState(
        "workspace-before-run",
        31,
        31,
        new string('4', 64),
        new string('5', 64),
        "sha256:" + new string('6', 64));
    var transaction = new Api36ProofTransactionState(
        "ready",
        "reviewed",
        1,
        "33333333-3333-3333-3333-333333333333",
        "sha256:" + new string('7', 64),
        "before-run.edge.spend",
        "spend-edge",
        "sha256:" + new string('8', 64),
        31,
        null,
        "sha256:" + new string('9', 64),
        null,
        null,
        ResumeRestored: true,
        CanConfirm: true,
        StatusCode: null);
    Api36ProofState state = Api36ProofStateContract.Create(
        7,
        4242,
        "44444444-4444-4444-4444-444444444444",
        2,
        build,
        surface,
        workspace,
        transaction);
    Assert(Api36ProofStateContract.IsExact(state),
        "the debug-only proof state must validate its exact digest-bound fields");
    Assert(state.StateDigest == "sha256:5d5ec21d03f3054d3f265b5f307357d42646ba229870d595c6f9e3b8b643456f",
        "the C# proof digest must match the strict Python reader's canonical vector");
    Assert(Api36ProofStateContract.Serialize(state).Length > 0,
        "the exact proof state must serialize to bounded JSON");
    Assert(!Api36ProofStateContract.IsExact(state with { Sequence = 8 }),
        "changing proof state without recomputing its digest must fail closed");
    Assert(!Api36ProofStateContract.IsExact(state with
        {
            Build = state.Build with { RuntimeIdentifier = "android-arm64" }
        }),
        "the proof state must reject ARM64 identity");
    Assert(!Api36ProofStateContract.IsExact(state with
        {
            Workspace = state.Workspace! with { PayloadSha256 = new string('A', 64) }
        }),
        "the proof state must reject noncanonical workspace digests");

    var resourcesSurface = new Api36ProofSurfaceState(
        "runner",
        "creation-resources-page",
        3,
        "creation-resources",
        "authority-ready",
        Settled: true);
    var resourcesWorkspace = new Api36ProofWorkspaceState(
        "workspace-resources",
        42,
        42,
        new string('4', 64),
        new string('5', 64),
        "sha256:" + new string('6', 64));
    var resources = new Api36ProofCreationResourcesState(
        "creation-resources-page",
        "workspace-resources",
        42,
        42,
        42,
        "sha256:" + new string('7', 64),
        "sha256:" + new string('8', 64),
        "sha256:" + new string('9', 64),
        "sha256:" + new string('a', 64),
        "sha256:" + new string('6', 64),
        "sha256:" + new string('b', 64),
        new string('c', 64),
        5,
        "sha256:" + new string('d', 64),
        50000m,
        50000m,
        "karma:0",
        1,
        "sha256:" + new string('e', 64));
    Api36ProofState resourceState = Api36ProofStateContract.Create(
        8,
        4242,
        "44444444-4444-4444-4444-444444444444",
        2,
        build,
        resourcesSurface,
        resourcesWorkspace,
        transaction: null,
        creationResources: resources);
    Assert(Api36ProofStateContract.IsExact(resourceState),
        "Creation Resources proof state must bind its page, workspace and exact digests");
    Assert(!Api36ProofStateContract.IsExact(resourceState with
        {
            CreationResources = resources with { WorkspaceRevision = 41 }
        }),
        "Creation Resources proof state must reject a foreign workspace revision");
    Assert(!Api36ProofStateContract.IsExact(resourceState with
        {
            CreationResources = resources with { PendingDraftDigest = null }
        }),
        "Creation Resources proof state must reject a partial pending-draft identity");
    Assert(!Api36ProofStateContract.IsExact(resourceState with
        {
            CreationResources = null
        }),
        "Creation Resources proof state must reject missing typed page authority");
    Assert(!Api36ProofStateContract.IsExact(state with
        {
            CreationResources = resources
        }),
        "non-Creation proof surfaces must reject injected Creation Resources authority");
}

static void AssertObservedSnapshotConflict(
    Sr5TableWizardTransactionJournal applying,
    Sr5TableWizardSnapshot observed,
    string message)
{
    Assert(
        Sr5TableWizardTypedTransactionPresenter.Observe(
            applying,
            observed,
            out string observedPostconditionDigest)
        == Sr5TableWizardRecoveryObservation.Conflict
        && observedPostconditionDigest.Length == 0,
        message);
}

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

namespace Chummer.Android.Native
{
    public sealed record RunnerProfileStub(bool Created);
    public sealed record RunnerRulesStub(string? GameEdition);

    public sealed class RunnerStateStub
    {
        public RunnerProfileStub? Profile { get; set; } = new(true);
        public RunnerRulesStub? Rules { get; set; } = new("SR5");
        public CharacterWorkspaceId? WorkspaceId { get; set; } =
            new("workspace-table-authority");
        public long ContentRevision { get; set; } = 1;
        public long SavedRevision { get; set; } = 1;
        public bool IsDirty { get; set; }
    }

    public sealed class RunnerSessionCoordinator
    {
        public RunnerStateStub State { get; } = new();
        public CareerEdgeUseEditorState? EdgeState { get; set; }
        public CareerWeaponFireCatalogEditorState? WeaponState { get; set; }
        public Action? AfterEdgeRead { get; set; }
        public int EdgeReadCount { get; private set; }
        public int WeaponReadCount { get; private set; }

        public Task<CareerEdgeUseEditorState?> PrepareCareerEdgeUseEditAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EdgeReadCount++;
            CareerEdgeUseEditorState? result = EdgeState;
            AfterEdgeRead?.Invoke();
            return Task.FromResult(result);
        }

        public Task<CareerWeaponFireCatalogEditorState?> PrepareCareerWeaponFireCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WeaponReadCount++;
            return Task.FromResult(WeaponState);
        }
    }

    public static class Sr5CareerWizardCatalog
    {
        public static bool IsSr5CareerRunner(bool characterCreated, string? gameEdition)
            => characterCreated
               && string.Equals(gameEdition, "SR5", StringComparison.OrdinalIgnoreCase);
    }
}
