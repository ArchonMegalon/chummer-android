using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

var tests = new (string Name, Action Run)[]
{
    ("Core projection and stable OptionId draft", ProjectionAndDraft),
    ("Durable Reviewed Applying Applied CAS", DurableCheckpointLifecycle),
    ("Malformed checkpoint is a replay-blocking lock", MalformedCheckpointLocks),
    ("Reordered identities produce one command binding", StableCommandBinding),
};

foreach ((string name, Action run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

return;

static void ProjectionAndDraft()
{
    CharacterCreationQualitiesState state = State();
    CharacterOverviewState overview = Overview(state.Binding);
    Assert(CreationQualitiesPhoneAuthority.IsReady(state, overview), "state must be ready");
    CharacterCreationQualitiesEditorState editor =
        CreationQualitiesPhoneAuthority.ProjectEditor(state, overview);
    Assert(editor.Options.Count == 3, "all exact and disabled options must be visible");

    var draft = new CreationQualitiesPhoneDraft();
    draft.Bind(state, overview);
    CharacterCreationQualitiesDesktopOption positive = editor.Options.Single(option =>
        option.OptionId == "positive");
    IReadOnlyList<string> selected = draft.WithToggle(positive);
    CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
        state.Binding,
        state.Authority,
        selected));
    var result = new CharacterCreationFoundationResult<CharacterCreationQualitiesPreview>(
        CharacterCreationFoundationOutcomes.Success,
        preview,
        []);
    Assert(draft.Matches(state, overview), "bound phone draft must match the live state");
    Assert(CreationQualitiesPhoneAuthority.CanDisplayPreview(
        state,
        overview,
        result,
        selected),
        $"Core preview must be displayable: {string.Join(',', preview.Blockers)}");
    Assert(draft.TryAdopt(state, overview, result, selected), "fresh Core preview must be adopted");
    Assert(draft.IsSelected("positive"), "stable option identity must be retained");
    Assert(!CreationQualitiesPhoneAuthority.IsOptionConfigurable(
        editor.Options.Single(option => option.OptionId == "unsupported")),
        "inexact/unsupported requirement must stay disabled");
}

static void DurableCheckpointLifecycle()
{
    CharacterCreationQualitiesState state = State();
    CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
        state.Binding,
        state.Authority,
        ["negative", "positive"]));
    Guid transactionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    CharacterCreationQualitiesCheckpoint reviewed =
        CharacterCreationQualitiesCheckpoint.CreateReviewed(
            preview,
            ["positive", "negative"],
            transactionId);
    var backend = new MemoryBackend();
    var store = new CharacterCreationQualitiesCheckpointStore(backend);
    Assert(store.TryCreate(reviewed, out CharacterCreationQualitiesCheckpoint durable, out _),
        "Reviewed checkpoint must write/read-back");
    Assert(store.TryBeginApply(
        CharacterCreationQualitiesCheckpointCas.From(durable),
        out CharacterCreationQualitiesCheckpoint applying,
        out _), "Reviewed must transition atomically to Applying");
    Assert(CreationQualitiesPhoneAuthority.TryCreatePlan(
        applying.Preview,
        applying.SelectedOptionIds,
        applying.IdempotencyKey,
        applying.TransactionId,
        out CharacterCreationQualitiesDraftPlan plan), "checkpoint must retain the exact plan");
    string draftDigest = Digest('d');
    CharacterCreationQualitiesDraftReceipt receipt = Receipt(plan, draftDigest);
    Assert(store.TryRecordApplied(
        CharacterCreationQualitiesCheckpointCas.From(applying),
        receipt,
        out CharacterCreationQualitiesCheckpoint applied,
        out _), "only exact receipt may transition Applying to Applied");
    Assert(applied.Receipt?.ReceiptDigest == receipt.ReceiptDigest, "receipt must be durable");
    Assert(store.TryAcknowledgeApplied(
        CharacterCreationQualitiesCheckpointCas.From(applied),
        out _), "Applied receipt can be explicitly acknowledged");
    Assert(!store.TryRead(out _, out string emptyBlocker) && emptyBlocker.Length == 0,
        "acknowledged store must be empty");
}

static void MalformedCheckpointLocks()
{
    var backend = new MemoryBackend { Payload = "{not-json" };
    var store = new CharacterCreationQualitiesCheckpointStore(backend);
    Assert(!store.TryRead(out _, out string blocker), "malformed state cannot be read");
    Assert(blocker.Contains("blocks replay", StringComparison.Ordinal),
        "malformed state must be an explicit replay lock");
    CharacterCreationQualitiesState state = State();
    CharacterCreationQualitiesCheckpoint candidate =
        CharacterCreationQualitiesCheckpoint.CreateReviewed(
            CharacterCreationQualitiesRules.Evaluate(new(
                state.Binding,
                state.Authority,
                ["positive"])),
            ["positive"],
            Guid.NewGuid());
    Assert(!store.TryCreate(candidate, out _, out blocker),
        "a malformed prior lock must never be overwritten");
}

static void StableCommandBinding()
{
    CharacterCreationQualitiesState state = State();
    CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
        state.Binding,
        state.Authority,
        ["positive", "negative"]));
    string left = CreationQualitiesPhoneAuthority.ComputeIdempotencyKey(
        preview,
        ["positive", "negative"]);
    string right = CreationQualitiesPhoneAuthority.ComputeIdempotencyKey(
        preview,
        ["negative", "positive"]);
    Assert(left == right, "selection order must not alter the exact command binding");
}

static CharacterCreationQualitiesState State()
{
    CharacterCreationQualityCatalogOption positive = Option(
        "positive", CharacterCreationQualityType.Positive, 6, selectable: true, exact: true);
    CharacterCreationQualityCatalogOption negative = Option(
        "negative", CharacterCreationQualityType.Negative, -4, selectable: true, exact: true);
    CharacterCreationQualityCatalogOption unsupported = Option(
        "unsupported", CharacterCreationQualityType.Positive, 9, selectable: false, exact: false);
    CharacterCreationQualitiesAuthority authority = Authority(positive, negative, unsupported);
    CharacterCreationQualitiesBinding binding = new(
        new CharacterWorkspaceId("quality-workspace"),
        ContentRevision: 7,
        SavedRevision: 7,
        RawCharacterXmlDigest: Digest('5'),
        AuxiliaryStateDigest: Digest('6'),
        PrerequisiteDraftRevision: 2,
        PrerequisiteDraftDigest: Digest('7'),
        AttributesDraftRevision: 3,
        AttributesDraftDigest: Digest('8'),
        RulesetId: "sr5",
        BuildMethod: CharacterCreationBuildMethods.Priority,
        CharacterCreated: false,
        CreationKarmaTotal: 25,
        CreationKarmaUsedBeforeQualities: 0,
        AuthorityDigest: authority.AuthorityDigest,
        RuntimeDigest: authority.RuntimeDigest);
    CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
        binding,
        authority,
        []));
    var state = new CharacterCreationQualitiesState(
        CharacterCreationQualitiesSchemas.StateV1,
        binding,
        authority,
        new CharacterCreationPrerequisiteDraft(2, Digest('7')),
        new CharacterCreationAttributesDraft(3, Digest('8')),
        PendingDraft: null,
        preview,
        Blockers: [],
        CanEdit: true,
        SnapshotDigest: string.Empty);
    return state with
    {
        SnapshotDigest = CharacterCreationQualitiesRules.ComputeStateDigest(state)
    };
}

static CharacterOverviewState Overview(CharacterCreationQualitiesBinding binding) => new()
{
    Profile = new CharacterOverviewProfileStub(Created: false),
    WorkspaceId = binding.WorkspaceId,
    ContentRevision = binding.ContentRevision,
    SavedRevision = binding.SavedRevision,
    IsDirty = false,
    Error = null
};

static CharacterCreationQualityCatalogOption Option(
    string id,
    CharacterCreationQualityType type,
    int cost,
    bool selectable,
    bool exact)
{
    var option = new CharacterCreationQualityCatalogOption(
        id,
        Guid.Parse(id switch
        {
            "positive" => "11111111-1111-1111-1111-111111111111",
            "negative" => "22222222-2222-2222-2222-222222222222",
            _ => "33333333-3333-3333-3333-333333333333"
        }),
        id,
        id,
        type,
        Rating: 1,
        KarmaCost: cost,
        MaximumSelections: 1,
        IsMetagenic: false,
        CountsAgainstQualityLimit: true,
        CountsAgainstKarma: true,
        IsFreeOrGranted: false,
        IsSelectable: selectable,
        EligibilityIsExact: exact,
        DisableReasonKey: selectable ? null : "unsupported-variable-or-requirement",
        FollowUpChoiceId: null,
        FollowUpChoiceLabel: null,
        SourceAnchorIds: [$"qualities.xml#quality:{id}"],
        OptionDigest: string.Empty);
    return option with
    {
        OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(option)
    };
}

static CharacterCreationQualitiesAuthority Authority(
    params CharacterCreationQualityCatalogOption[] options)
{
    var authority = new CharacterCreationQualitiesAuthority(
        CharacterCreationQualitiesSchemas.AuthorityV1,
        "sr5",
        "settings-profile",
        QualityKarmaLimit: 25,
        MayExceedPositiveQualityLimit: false,
        MayExceedNegativeQualityLimit: false,
        MetagenicLimit: 5,
        Options: options,
        GrantedQualities: [],
        SourceAnchorIds: ["qualities.xml", "settings.xml#setting:settings-profile"],
        Blockers: [],
        IsAuthoritative: true,
        SourceDigest: Digest('1'),
        ProfileDigest: Digest('2'),
        GmPolicyDigest: Digest('3'),
        RuntimeDigest: Digest('4'),
        AuthorityDigest: string.Empty);
    return authority with
    {
        AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority)
    };
}

static CharacterCreationQualitiesDraftReceipt Receipt(
    CharacterCreationQualitiesDraftPlan plan,
    string draftDigest)
{
    var receipt = new CharacterCreationQualitiesDraftReceipt(
        CharacterCreationQualitiesSchemas.ReceiptV1,
        plan.TransactionId,
        plan.WorkspaceId,
        plan.ExpectedContentRevision,
        plan.TargetContentRevision,
        plan.ExpectedSavedRevision,
        plan.TargetSavedRevision,
        plan.AuthorityDigest,
        plan.RuntimeDigest,
        plan.PreviewDigest,
        plan.IdempotencyKeyDigest,
        plan.CommandDigest,
        plan.PlanDigest,
        draftDigest,
        CharacterCreationQualitiesRules.ReceiptLedgerRootDigest,
        CharacterDocumentChanged: false,
        ReceiptDigest: string.Empty);
    return receipt with
    {
        ReceiptDigest = CharacterCreationQualitiesRules.ComputeReceiptDigest(receipt)
    };
}

static string Digest(char value) => "sha256:" + new string(value, 64);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class MemoryBackend : ICharacterCreationQualitiesCheckpointBackend
{
    public string Payload { get; set; } = string.Empty;
    public string Read() => Payload;
    public void Write(string payload) => Payload = payload;
    public void Remove() => Payload = string.Empty;
}
