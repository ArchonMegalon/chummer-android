using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.CreationWizard.Presentation.Tests;
using Chummer.Presentation.Overview;

var tests = new (string Name, Action Run)[]
{
    ("Typed Core review produces deterministic durable command", TypedReview),
    ("Reviewed Confirming Confirmed journal survives read-back", DurableLifecycle),
    ("Malformed journal locks replay", MalformedLocksReplay),
    ("Unsupported artificial-intelligence Talent stays closed", UnsupportedAiFailsClosed),
};

foreach ((string name, Action run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

return;

static void TypedReview()
{
    CharacterCreationMagicResonanceState core = State();
    var service = new StubMagicResonanceService(core);
    CharacterCreationMagicResonanceEditorState editor =
        CharacterCreationMagicResonanceWorkflow.Project(core);
    CharacterCreationMagicResonanceDesktopDraft ordered =
        CharacterCreationMagicResonanceWorkflow.CreateDraft(
            editor,
            CharacterCreationMagicResonanceTestFixture.TraditionId,
            null,
            [],
            [
                CharacterCreationMagicResonanceTestFixture.SpellOneId,
                CharacterCreationMagicResonanceTestFixture.SpellTwoId
            ],
            []);
    CharacterCreationMagicResonanceDesktopDraft reversed =
        CharacterCreationMagicResonanceWorkflow.CreateDraft(
            editor,
            CharacterCreationMagicResonanceTestFixture.TraditionId,
            null,
            [],
            [
                CharacterCreationMagicResonanceTestFixture.SpellTwoId,
                CharacterCreationMagicResonanceTestFixture.SpellOneId
            ],
            []);
    CharacterCreationMagicResonanceReview left =
        CharacterCreationMagicResonanceWorkflow.Review(service, editor, ordered);
    CharacterCreationMagicResonanceReview right =
        CharacterCreationMagicResonanceWorkflow.Review(service, editor, reversed);
    Assert(CreationMagicResonancePhoneAuthority.ReviewMatches(
        editor, left, requireConfirmable: true), "review must retain exact Core budgets");
    Assert(CreationMagicResonancePhoneAuthority.ReviewsEqual(left, right),
        "Presentation normalization must make reordered identities identical");
    Assert(CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(left)
           == CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(right),
        "idempotency key must bind the normalized typed command");
}

static void DurableLifecycle()
{
    CharacterCreationMagicResonanceState core = State();
    var service = new StubMagicResonanceService(core);
    CharacterCreationMagicResonanceEditorState editor =
        CharacterCreationMagicResonanceWorkflow.Project(core);
    CharacterCreationMagicResonanceDesktopDraft draft =
        CreationMagicResonancePhoneAuthority.CreateDraft(
            editor,
            CharacterCreationMagicResonanceTestFixture.CompleteSelections);
    CharacterCreationMagicResonanceReview review =
        CharacterCreationMagicResonanceWorkflow.Review(service, editor, draft);
    CharacterCreationMagicResonanceCheckpoint candidate =
        CharacterCreationMagicResonanceCheckpoint.CreateReviewed(review);
    var backend = new MemoryBackend();
    var store = new CharacterCreationMagicResonanceCheckpointStore(backend);
    Assert(store.TryCreate(
        candidate,
        out CharacterCreationMagicResonanceCheckpoint reviewed,
        out _), "Reviewed checkpoint must survive exact read-back");
    Assert(store.TryBeginConfirm(
        CharacterCreationMagicResonanceCheckpointCas.From(reviewed),
        out CharacterCreationMagicResonanceCheckpoint confirming,
        out _), "Reviewed must advance durably before Core confirmation");
    CharacterCreationMagicResonanceConfirmation confirmation =
        CharacterCreationMagicResonanceWorkflow.Confirm(
            service,
            confirming.Review,
            confirming.IdempotencyKey,
            explicitlyConfirmed: true);
    Assert(store.TryRecordConfirmed(
        CharacterCreationMagicResonanceCheckpointCas.From(confirming),
        confirmation,
        out CharacterCreationMagicResonanceCheckpoint confirmed,
        out _), "only the exact receipt may close Confirming");
    Assert(confirmed.Confirmation?.Receipt.ReceiptDigest
           == confirmation.Receipt.ReceiptDigest,
        "Core receipt must be durable");
    Assert(!confirmation.Receipt.CharacterDocumentChanged,
        "Magic/Resonance confirmation must not mutate character XML");
    Assert(store.TryAcknowledgeConfirmed(
        CharacterCreationMagicResonanceCheckpointCas.From(confirmed),
        out _), "Confirmed receipt can be explicitly acknowledged");
    Assert(!store.TryRead(out _, out string emptyBlocker)
           && emptyBlocker.Length == 0,
        "acknowledged store must be empty");
}

static void MalformedLocksReplay()
{
    var backend = new MemoryBackend { Payload = "{not-json" };
    var store = new CharacterCreationMagicResonanceCheckpointStore(backend);
    Assert(!store.TryRead(out _, out string blocker),
        "malformed checkpoint cannot be read");
    Assert(blocker.Contains("blocks replay", StringComparison.Ordinal),
        "malformed checkpoint must stay an explicit replay lock");

    CharacterCreationMagicResonanceState core = State();
    var service = new StubMagicResonanceService(core);
    CharacterCreationMagicResonanceEditorState editor =
        CharacterCreationMagicResonanceWorkflow.Project(core);
    CharacterCreationMagicResonanceReview review =
        CharacterCreationMagicResonanceWorkflow.Review(
            service,
            editor,
            CreationMagicResonancePhoneAuthority.CreateDraft(
                editor,
                CharacterCreationMagicResonanceTestFixture.CompleteSelections));
    Assert(!store.TryCreate(
        CharacterCreationMagicResonanceCheckpoint.CreateReviewed(review),
        out _,
        out blocker), "new reviews must not overwrite a malformed replay lock");
}

static void UnsupportedAiFailsClosed()
{
    CharacterCreationMagicResonanceState ai =
        CharacterCreationMagicResonanceTestFixture.CreateState(
            CharacterCreationMagicResonanceTestFixture.Digest('0'),
            talentKind: CharacterCreationMagicResonanceKinds.ArtificialIntelligence);
    Assert(!CharacterCreationMagicResonanceWorkflow.TryProject(ai, out _),
        "AI Talent must remain unsupported at the Presentation boundary");
    Assert(!CharacterCreationMagicResonancePresentationContract.IsSupportedTalentKind(
        CharacterCreationMagicResonanceKinds.ArtificialIntelligence),
        "Android cannot activate an unsupported AI fallback");
}

static CharacterCreationMagicResonanceState State()
    => CharacterCreationMagicResonanceTestFixture.CreateState(
        CharacterCreationMagicResonanceTestFixture.Digest('0'));

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class MemoryBackend : ICharacterCreationMagicResonanceCheckpointBackend
{
    public string Payload { get; set; } = string.Empty;
    public string Read() => Payload;
    public void Write(string payload) => Payload = payload;
    public void Remove() => Payload = string.Empty;
}
