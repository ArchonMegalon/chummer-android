using Chummer.Android.Native;
using Chummer.Contracts.Characters;

CharacterVehicleWorkshopCatalog catalog = Catalog();
CharacterVehicleWorkshopPreparation preparation = Preparation(catalog);
Sr5VehicleWorkshopDraft draft = CompleteDraft(preparation);
CharacterVehicleWorkshopChassisEntry selectedChassis = preparation.Chassis.Single();
Sr5VehicleWorkshopFactoryModificationProjection factoryProjection =
    Sr5VehicleWorkshopFactoryModificationConsumer.Project(
        selectedChassis,
        draft.NewVehicleInstanceId);

Check(Sr5VehicleWorkshopCheckpoint.CurrentSchemaVersion == 2
      && Sr5VehicleWorkshopRoutes.IsKnown(Sr5VehicleWorkshopRoutes.FactoryModifications),
    "factory-modification deep route and schema-v2 checkpoint must be explicit");
Check(draft.IsValid(out _), "complete typed draft must validate");
Check(factoryProjection.CanContinue
      && factoryProjection.Rows is [{ Included: true, Removable: false }],
    "exact factory modification must be included and non-removable");
Sr5VehicleWorkshopFactoryModificationRow includedFactoryModification =
    factoryProjection.Rows.Single();
Check(includedFactoryModification.SourceId.Value
      == Guid.Parse("70000000-0000-4000-8000-000000000001")
      && includedFactoryModification.InstructionId.Value != Guid.Empty
      && includedFactoryModification.InstanceId.Value != Guid.Empty,
    "factory preview must preserve typed source, instruction, and deterministic instance identities");
Check(includedFactoryModification.InstanceId
      == CharacterVehicleWorkshopRules.DeriveFactoryModificationInstanceId(
          draft.NewVehicleInstanceId,
          includedFactoryModification.InstructionId),
    "factory instance identity must be derived from the durable vehicle and instruction identities");
Check(draft.Matches("workspace-1", preparation), "draft must resume against exact binding");
Check(!draft.Matches("workspace-2", preparation), "draft must reject another workspace");
Check(!draft.Matches("workspace-1", preparation with { ContentRevision = 8 }),
    "draft must reject a stale revision");

Check(draft.TryCreateSelection(out CharacterVehicleWorkshopSelection selection),
    "complete draft must create a Core selection");
CharacterVehicleWorkshopQuote quote = CharacterVehicleWorkshopRules.Quote(preparation, selection);
Check(quote.Exact && CharacterVehicleWorkshopRules.IsCanonicalDigest(quote.QuoteDigest),
    "four-part mount and exact modification must quote exactly");
Check(quote.Lines.Count == 5, "quote must contain one mod and four mount components");

draft = draft with { QuoteDigest = quote.QuoteDigest };
var command = new CharacterVehicleWorkshopCommitCommand(
    preparation.ContentRevision,
    preparation.CharacterDigest,
    preparation.CatalogDigest,
    quote.QuoteDigest,
    "android-workshop-test-key",
    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
    DateTimeOffset.Parse("2026-08-27T06:00:00+00:00"),
    selection);
var pending = new Sr5VehicleWorkshopCheckpoint(
    Sr5VehicleWorkshopCheckpoint.CurrentSchemaVersion,
    Sr5VehicleWorkshopCheckpointStage.PendingCommit,
    draft with { RouteId = Sr5VehicleWorkshopRoutes.Recovery },
    command,
    null,
    preparation.ContentRevision + 1,
    H('f'),
    string.Empty);
Check(pending.IsValid(out _), "pending command checkpoint must validate");

var backend = new MemoryBackend();
var store = new Sr5VehicleWorkshopCheckpointStore(backend);
Check(store.TryWrite(pending, out _), "pending checkpoint must save");
Check(store.TryRead(out Sr5VehicleWorkshopCheckpoint? resumed, out _)
      && resumed is not null
      && resumed.Stage == pending.Stage
      && resumed.Command is not null
      && CharacterVehicleWorkshopRules.ComputeCommandDigest(resumed.Command)
         == CharacterVehicleWorkshopRules.ComputeCommandDigest(command)
      && resumed.Draft.TryCreateSelection(out CharacterVehicleWorkshopSelection resumedSelection)
      && CharacterVehicleWorkshopRules.ComputeCommandDigest(command with { Selection = resumedSelection })
         == CharacterVehicleWorkshopRules.ComputeCommandDigest(command),
    "pending checkpoint must round-trip without losing typed identities");
Sr5VehicleWorkshopFactoryModificationProjection reopenedFactoryProjection =
    Sr5VehicleWorkshopFactoryModificationConsumer.Project(
        selectedChassis,
        resumed!.Draft.NewVehicleInstanceId);
Check(reopenedFactoryProjection.CanContinue
      && reopenedFactoryProjection.Rows.Single().InstanceId
         == includedFactoryModification.InstanceId,
    "restart must rehydrate the same deterministic factory instance without an editable draft copy");

CharacterVehicleWorkshopCommitReceipt receipt = Receipt(command, quote, preparation);
var completed = pending with
{
    Stage = Sr5VehicleWorkshopCheckpointStage.Receipt,
    Draft = pending.Draft with { RouteId = Sr5VehicleWorkshopRoutes.Receipt },
    Receipt = receipt
};
Check(completed.IsValid(out _), "digest-bound receipt checkpoint must validate");
Check(store.TryWrite(completed, out _)
      && store.TryRead(out resumed, out _)
      && resumed?.Receipt is { } resumedReceipt
      && CharacterVehicleWorkshopRules.FixedEquals(
          resumedReceipt.ReceiptDigest,
          receipt.ReceiptDigest),
    "receipt must reopen after process restart");

CharacterVehicleWorkshopCommitCommand injected = command with
{
    Selection = command.Selection with { CustomName = "Injected" }
};
Check(!(pending with { Command = injected }).IsValid(out _),
    "checkpoint must reject a command injected after quote");

Sr5VehicleWorkshopDraft duplicateInstance = draft with
{
    Modifications =
    [
        draft.Modifications[0],
        draft.Modifications[0] with
        {
            SourceId = new CharacterVehicleModificationSourceId(Guid.NewGuid())
        }
    ]
};
Check(!duplicateInstance.IsValid(out _), "draft must reject duplicate instance identities");

Sr5VehicleWorkshopDraft duplicateModificationSource = draft with
{
    Modifications =
    [
        draft.Modifications[0],
        draft.Modifications[0] with
        {
            InstanceId = new CharacterVehicleModificationInstanceId(Guid.NewGuid())
        }
    ]
};
Check(!duplicateModificationSource.IsValid(out _),
    "draft must reject duplicate modification source identities");

Sr5VehicleWorkshopDraft sourceInstanceCollision = draft with
{
    NewVehicleInstanceId = new CharacterVehicleInstanceId(
        draft.Modifications[0].SourceId.Value)
};
Check(!sourceInstanceCollision.IsValid(out _),
    "draft must reject a source identity reused as an instance identity");

Sr5VehicleWorkshopDraft chassisInstanceCollision = draft with
{
    NewVehicleInstanceId = new CharacterVehicleInstanceId(
        draft.ChassisSourceId!.Value.Value)
};
Check(!chassisInstanceCollision.IsValid(out _),
    "draft must reject a chassis source identity reused as an instance identity");

Sr5VehicleWorkshopDraft duplicateMountComponentSource = draft with
{
    WeaponMounts =
    [
        draft.WeaponMounts[0] with
        {
            Components =
            [
                draft.WeaponMounts[0].Components[0],
                draft.WeaponMounts[0].Components[0] with
                {
                    InstanceId = new CharacterVehicleWeaponMountComponentInstanceId(Guid.NewGuid())
                }
            ]
        }
    ],
    QuoteDigest = string.Empty
};
Check(!duplicateMountComponentSource.IsValid(out _),
    "draft must reject a duplicated source component inside one mount");

Sr5VehicleWorkshopDraft incompleteMount = draft with
{
    WeaponMounts =
    [
        draft.WeaponMounts[0] with
        {
            Components = draft.WeaponMounts[0].Components.Take(3).ToArray()
        }
    ],
    QuoteDigest = string.Empty
};
Check(incompleteMount.TryCreateSelection(out CharacterVehicleWorkshopSelection incompleteSelection),
    "an in-progress typed mount may remain resumable");
Check(!CharacterVehicleWorkshopRules.Quote(preparation, incompleteSelection).Exact,
    "Core must fail closed until all four mount kinds exist");

CharacterVehicleWorkshopFactoryModificationEntry unsupportedFactoryModification =
    selectedChassis.FactoryModifications.Single() with
    {
        ProjectionStatus = CharacterVehicleWorkshopProjectionStatus.Unsupported,
        UnsupportedReason = "The factory select prompt is not projected exactly."
    };
CharacterVehicleWorkshopChassisEntry unsupportedFactoryChassis = selectedChassis with
{
    ProjectionStatus = CharacterVehicleWorkshopProjectionStatus.Unsupported,
    UnsupportedReason = unsupportedFactoryModification.UnsupportedReason,
    FactoryModifications = [unsupportedFactoryModification]
};
Sr5VehicleWorkshopFactoryModificationProjection unsupportedFactoryProjection =
    Sr5VehicleWorkshopFactoryModificationConsumer.Project(
        unsupportedFactoryChassis,
        draft.NewVehicleInstanceId);
Check(!unsupportedFactoryProjection.CanContinue
      && unsupportedFactoryProjection.Rows.Single().Posture
         == Sr5VehicleWorkshopFactoryModificationPosture.Unsupported
      && !unsupportedFactoryProjection.Rows.Single().Included
      && !unsupportedFactoryProjection.Rows.Single().Removable
      && unsupportedFactoryProjection.Blockers.Contains(
          unsupportedFactoryModification.UnsupportedReason),
    "unsupported factory instruction must remain visible, non-editable, and fail closed");

CharacterVehicleWorkshopFactoryModificationEntry forgedFactoryInstruction =
    selectedChassis.FactoryModifications.Single() with
    {
        InstructionId = new CharacterVehicleFactoryModificationInstructionId(Guid.NewGuid())
    };
Sr5VehicleWorkshopFactoryModificationProjection forgedFactoryProjection =
    Sr5VehicleWorkshopFactoryModificationConsumer.Project(
        selectedChassis with { FactoryModifications = [forgedFactoryInstruction] },
        draft.NewVehicleInstanceId);
Check(!forgedFactoryProjection.CanContinue
      && forgedFactoryProjection.Rows.Single().Posture
         == Sr5VehicleWorkshopFactoryModificationPosture.Unsupported,
    "consumer must reject a forged typed factory instruction identity");

backend.Payload = "{ malformed";
Check(!store.TryRead(out _, out string malformedReason)
      && malformedReason.Length != 0,
    "malformed persistence must fail closed with a reason");
backend.Payload = new string('x', 513 * 1024);
Check(!store.TryRead(out _, out string oversizedReason)
      && oversizedReason.Length != 0,
    "oversized persistence must fail closed");

Console.WriteLine("SR5 Vehicle & Drone Workshop draft tests passed (typed resume, quote, receipt, recovery journal, fail-closed validation).");
return;

static CharacterVehicleWorkshopCatalog Catalog()
{
    var binding = new CharacterVehicleWorkshopSourceBinding(
        "SR5", "settings.xml", CharacterVehicleWorkshopRules.SemanticsVersion,
        H('1'), H('2'), H('3'), H('4'), H('5'),
        false, 1m, false, 1m, true);
    var chassisId = new CharacterVehicleChassisSourceId(
        Guid.Parse("10000000-0000-0000-0000-000000000001"));
    var chassis = new CharacterVehicleWorkshopChassisEntry(
        chassisId,
        CharacterVehicleChassisKind.Drone,
        CharacterVehicleChassisPosture.Stock,
        "MCT Fly-Spy",
        "Drones",
        4, 4, 3, 3, 3, 3, 3, 1, 0, 0, 3, 8, 8,
        2000m,
        new CharacterVehicleWorkshopAvailability(6, CharacterVehicleWorkshopLegality.Restricted, false),
        "SR5", "466", string.Empty,
        CharacterVehicleWorkshopProjectionStatus.Exact,
        string.Empty,
        [],
        [FactoryModification(chassisId)]);
    var modification = new CharacterVehicleWorkshopModificationEntry(
        new CharacterVehicleModificationSourceId(
            Guid.Parse("20000000-0000-0000-0000-000000000001")),
        "Improved Economy", "Powertrain", 1, 3,
        100m, 50m, 1, 0, 1, 0,
        new CharacterVehicleWorkshopAvailability(4, CharacterVehicleWorkshopLegality.Legal, true),
        "R5", "160", [chassisId],
        CharacterVehicleWorkshopProjectionStatus.Exact,
        string.Empty);
    CharacterVehicleWeaponMountComponentEntry[] components = Enum
        .GetValues<CharacterVehicleWeaponMountComponentKind>()
        .Select((kind, index) => new CharacterVehicleWeaponMountComponentEntry(
            new CharacterVehicleWeaponMountComponentSourceId(
                Guid.Parse($"30000000-0000-0000-0000-00000000000{index + 1}")),
            kind,
            kind.ToString(),
            100m + index,
            1,
            1,
            new CharacterVehicleWorkshopAvailability(2, CharacterVehicleWorkshopLegality.Legal, true),
            "R5", "162", [chassisId], [], [],
            CharacterVehicleWorkshopProjectionStatus.Exact,
            string.Empty))
        .ToArray();
    var unsigned = new CharacterVehicleWorkshopCatalog(binding, [chassis], [modification], components, string.Empty);
    return unsigned with { DeclaredCatalogDigest = CharacterVehicleWorkshopRules.ComputeCatalogDigest(unsigned) };
}

static CharacterVehicleWorkshopFactoryModificationEntry FactoryModification(
    CharacterVehicleChassisSourceId chassisId)
{
    var sourceId = new CharacterVehicleFactoryModificationSourceId(
        Guid.Parse("70000000-0000-4000-8000-000000000001"));
    string instructionDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(
        "<name>Rigger Interface</name>");
    CharacterVehicleFactoryModificationInstructionId instructionId =
        CharacterVehicleWorkshopRules.DeriveFactoryModificationInstructionId(
            chassisId,
            sourceId,
            0,
            instructionDigest);
    return new CharacterVehicleWorkshopFactoryModificationEntry(
        instructionId,
        chassisId,
        0,
        sourceId,
        "Rigger Interface",
        "Electromagnetic",
        string.Empty,
        "0",
        string.Empty,
        0,
        "0",
        "String_Rating",
        0,
        new CharacterVehicleWorkshopAvailability(
            4,
            CharacterVehicleWorkshopLegality.Legal,
            false),
        "1000",
        string.Empty,
        "SR5",
        "461",
        string.Empty,
        string.Empty,
        0m,
        0m,
        string.Empty,
        false,
        H('7'),
        instructionDigest,
        CharacterVehicleWorkshopProjectionStatus.Exact,
        string.Empty);
}

static CharacterVehicleWorkshopPreparation Preparation(CharacterVehicleWorkshopCatalog catalog)
    => new(
        true,
        [],
        7,
        CharacterVehicleWorkshopRules.ComputeCharacterDigest("<character />"),
        100000m,
        catalog.Binding,
        catalog.DeclaredCatalogDigest,
        catalog.Chassis,
        catalog.Modifications,
        catalog.WeaponMountComponents,
        []);

static Sr5VehicleWorkshopDraft CompleteDraft(CharacterVehicleWorkshopPreparation preparation)
{
    CharacterVehicleWorkshopModificationEntry modification = preparation.Modifications.Single();
    Sr5VehicleWorkshopMountComponentDraft[] components = preparation.WeaponMountComponents
        .Select((entry, index) => new Sr5VehicleWorkshopMountComponentDraft(
            entry.SourceId,
            new CharacterVehicleWeaponMountComponentInstanceId(
                Guid.Parse($"50000000-0000-0000-0000-00000000000{index + 1}"))))
        .ToArray();
    return Sr5VehicleWorkshopDraft.Create("workspace-1", preparation) with
    {
        RouteId = Sr5VehicleWorkshopRoutes.Review,
        ChassisSourceId = preparation.Chassis.Single().SourceId,
        CustomName = "Eyes Above",
        Modifications =
        [
            new Sr5VehicleWorkshopModificationDraft(
                modification.SourceId,
                new CharacterVehicleModificationInstanceId(
                    Guid.Parse("40000000-0000-0000-0000-000000000001")),
                2)
        ],
        WeaponMounts =
        [
            new Sr5VehicleWorkshopMountDraft(
                new CharacterVehicleWeaponMountInstanceId(
                    Guid.Parse("60000000-0000-0000-0000-000000000001")),
                components)
        ]
    };
}

static CharacterVehicleWorkshopCommitReceipt Receipt(
    CharacterVehicleWorkshopCommitCommand command,
    CharacterVehicleWorkshopQuote quote,
    CharacterVehicleWorkshopPreparation preparation)
{
    var unsigned = new CharacterVehicleWorkshopCommitReceipt(
        preparation.ContentRevision + 1,
        H('f'),
        preparation.ContentRevision,
        preparation.CharacterDigest,
        preparation.AvailableNuyen,
        preparation.CatalogDigest,
        quote.QuoteDigest,
        CharacterVehicleWorkshopRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey),
        CharacterVehicleWorkshopRules.ComputeCommandDigest(command),
        command.Selection.NewVehicleInstanceId,
        command.NewExpenseId,
        quote.NuyenDelta,
        H('a'),
        H('b'),
        true,
        string.Empty);
    return unsigned with
    {
        ReceiptDigest = CharacterVehicleWorkshopRules.ComputeReceiptDigest(unsigned)
    };
}

static string H(char value) => new(value, 64);

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class MemoryBackend : ISr5VehicleWorkshopCheckpointBackend
{
    public string Payload { get; set; } = string.Empty;
    public string Read() => Payload;
    public void Write(string payload) => Payload = payload;
    public void Remove() => Payload = string.Empty;
}
