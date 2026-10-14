using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

var tests = new (string Name, Action Run)[]
{
    ("Core projection and stable OptionId draft", ProjectionAndDraft),
    ("Rated qualities group by typed identity without inventing levels", RatedQualityGrouping),
    ("Changing a quality rating preserves unrelated selections", RatedQualityReplacement),
    ("Selected quality keeps only Core-admitted alternative ratings", RatedQualityAvailability),
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

static void RatedQualityGrouping()
{
    CharacterCreationQualityCatalogOption first = RatedOption("rated-1", 1, 4);
    CharacterCreationQualityCatalogOption third = RatedOption("rated-3", 3, 12);
    CharacterCreationQualityCatalogOption seventh = RatedOption("rated-7", 7, 24);
    CharacterCreationQualitiesState state = State([
        seventh, first, third,
        Rehash(first with { OptionId = "other-source", SourceId = Guid.Parse("44444444-4444-4444-4444-444444444444") }),
        Rehash(first with { OptionId = "follow-a", FollowUpChoiceId = "choice-a", FollowUpChoiceLabel = "Same choice" }),
        Rehash(third with { OptionId = "follow-b", FollowUpChoiceId = "choice-b", FollowUpChoiceLabel = "Same choice" })
    ]);
    CharacterCreationQualitiesEditorState editor = CreationQualitiesPhoneAuthority.ProjectEditor(
        state, Overview(state.Binding));
    CharacterCreationQualitiesDesktopOption[][] groups = CreationQualityRatingChoices.Group(editor.Options);
    Assert(groups.Length == 4, "equal names or follow-up labels must not merge distinct typed qualities");
    CharacterCreationQualitiesDesktopOption[] ratings = groups.Single(group => group.Length == 3);
    Assert(ratings.Select(option => option.Rating).Order().SequenceEqual([1, 3, 7]),
        "sparse Core ratings must remain sparse without synthesized intermediate levels");
    Assert(groups.SelectMany(group => group).Select(option => option.OptionId).Order(StringComparer.Ordinal)
        .SequenceEqual(editor.Options.Select(option => option.OptionId).Order(StringComparer.Ordinal)),
        "grouping must retain every exact Core OptionId once");

    CharacterCreationQualitiesDesktopOption original = ratings.Single(option => option.Rating == 1);
    foreach (CharacterCreationQualitiesDesktopOption distinct in new[]
    {
        original with { SelectionKey = "another-selection" },
        original with { Type = CharacterCreationQualityType.Negative },
        original with { IsMetagenic = true },
        original with { IsFreeOrGranted = true }
    })
        Assert(!CreationQualityRatingChoices.SameQuality(original, distinct),
            "a different typed selection, category or grant must not replace an existing quality");

    CharacterCreationQualitiesDesktopOption[][] ambiguous = CreationQualityRatingChoices.Group([
        original,
        ratings.Single(option => option.Rating == 3),
        original with { OptionId = "ambiguous-same-rating" }
    ]);
    Assert(ambiguous.Length == 3 && ambiguous.All(group => group.Length == 1),
        "repeated ratings must remain separate instead of silently choosing one option identity");
}

static void RatedQualityReplacement()
{
    CharacterCreationQualitiesState state = State([
        RatedOption("rated-1", 1, 4),
        RatedOption("rated-3", 3, 12),
        Option("positive", CharacterCreationQualityType.Positive, 6, selectable: true, exact: true),
        Option("negative", CharacterCreationQualityType.Negative, -4, selectable: true, exact: true)
    ]);
    CharacterOverviewState overview = Overview(state.Binding);
    CharacterCreationQualitiesEditorState editor = CreationQualitiesPhoneAuthority.ProjectEditor(state, overview);
    var draft = new CreationQualitiesPhoneDraft();
    Assert(draft.Bind(state, overview), "rated fixture must bind to its rehashed Core authority");
    AdoptSelection(draft, state, overview, ["rated-1", "positive", "negative"]);
    CharacterCreationQualitiesDesktopOption target = editor.Options.Single(option => option.OptionId == "rated-3");

    // Even a broader supplied list may only replace exact typed siblings.
    IReadOnlyList<string> replacement = draft.WithToggle(target, editor.Options);
    Assert(replacement.SequenceEqual(["negative", "positive", "rated-3"]),
        "changing a rating must replace its previous OptionId and preserve unrelated selected qualities");
    Assert(draft.SelectedOptionIds.SequenceEqual(["negative", "positive", "rated-1"]),
        "proposing a replacement must not change the draft before Core accepts its preview");
    AdoptSelection(draft, state, overview, replacement);
    Assert(draft.Preview is { CanConfirm: true, KarmaRemaining: 11 }
        && draft.Preview.Selections.Single(option => option.OptionId == "rated-3").Rating == 3,
        "the adopted preview must preserve the target rating and its exact Core budget");
    Assert(draft.WithToggle(target, editor.Options).SequenceEqual(["negative", "positive"]),
        "toggling the current rating must remove that quality without selecting another level");
}

static void RatedQualityAvailability()
{
    CharacterCreationQualitiesState state = State([
        RatedOption("rated-1", 1, 4),
        RatedOption("rated-3", 3, 12),
        RatedOption("rated-5", 5, 24),
        Rehash(RatedOption("rated-disabled", 7, 8) with
        {
            IsSelectable = false,
            DisableReasonKey = "unsupported-variable-or-requirement"
        }),
        Option("blocked-whole", CharacterCreationQualityType.Positive, 26, selectable: true, exact: true),
        Option("positive", CharacterCreationQualityType.Positive, 6, selectable: true, exact: true)
    ]);
    CharacterOverviewState overview = Overview(state.Binding);
    CharacterCreationQualitiesEditorState editor = CreationQualitiesPhoneAuthority.ProjectEditor(state, overview);
    var draft = new CreationQualitiesPhoneDraft();
    Assert(draft.Bind(state, overview), "rating availability fixture must bind");
    Assert(draft.AvailableOptions(state, overview, editor, default).Any(option => option.OptionId == "rated-5"),
        "the high rating must be valid on its own so this case checks the current draft budget");
    AdoptSelection(draft, state, overview, ["rated-3", "positive"]);

    string[] available = draft.AvailableOptions(state, overview, editor, default)
        .Select(option => option.OptionId).Order(StringComparer.Ordinal).ToArray();
    Assert(available.SequenceEqual(["positive", "rated-1", "rated-3"]),
        "an affordable replacement must remain reachable while unaffordable and disabled ratings stay absent");
    Assert(draft.SelectedOptionIds.SequenceEqual(["positive", "rated-3"])
        && draft.Preview is { KarmaRemaining: 7 },
        "checking alternate ratings must not remove the selected quality or change its current preview");
    Assert(draft.WithToggle(editor.Options.Single(option => option.OptionId == "rated-disabled"), editor.Options)
        .SequenceEqual(draft.SelectedOptionIds), "a disabled variant must not replace the selected rating");

    string[] invalidSelection = ["positive", "rated-5"];
    var invalidPreview = CharacterCreationQualitiesRules.Evaluate(new(state.Binding, state.Authority, invalidSelection));
    Assert(!invalidPreview.CanConfirm && draft.TryAdopt(state, overview,
        new(CharacterCreationFoundationOutcomes.Blocked, invalidPreview, invalidPreview.Blockers), invalidSelection),
        "an exact invalid draft must remain visible for correction");
    var retained = draft.AvailableOptions(state, overview, editor, default);
    Assert(retained.Any(option => option.OptionId == "rated-5") && !draft.IsAdmitted("rated-5")
        && !retained.Any(option => option.OptionId == "blocked-whole")
        && draft.IsAdmitted("rated-1") && draft.IsAdmitted("rated-3"),
        "an invalid chosen rating must remain removable, stay out of the selectable ratings, and leave valid replacements reachable");
    Assert(draft.WithToggle(editor.Options.Single(option => option.OptionId == "rated-5"), editor.Options)
        .SequenceEqual(["positive"]), "an unavailable retained rating must remain removable");
    Assert(draft.WithToggle(editor.Options.Single(option => option.OptionId == "rated-5") with
        { IsSelectable = false, DisableReasonKey = "creation-qualities-source-disabled" }, editor.Options)
        .SequenceEqual(["positive"]), "loss of configurability must not trap a retained selected rating");
}

static void AdoptSelection(CreationQualitiesPhoneDraft draft, CharacterCreationQualitiesState state,
    CharacterOverviewState overview, IReadOnlyList<string> selected)
{
    CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
        state.Binding, state.Authority, selected));
    Assert(preview.CanConfirm, $"fixture selection must be Core-admitted: {string.Join(',', preview.Blockers)}");
    Assert(draft.TryAdopt(state, overview, new(CharacterCreationFoundationOutcomes.Success, preview, preview.Blockers), selected),
        "draft must adopt the exact Core evaluation for the selected OptionIds");
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

static CharacterCreationQualitiesState State(CharacterCreationQualityCatalogOption[]? options = null)
{
    CharacterCreationQualityCatalogOption positive = Option(
        "positive", CharacterCreationQualityType.Positive, 6, selectable: true, exact: true);
    CharacterCreationQualityCatalogOption negative = Option(
        "negative", CharacterCreationQualityType.Negative, -4, selectable: true, exact: true);
    CharacterCreationQualityCatalogOption unsupported = Option(
        "unsupported", CharacterCreationQualityType.Positive, 9, selectable: false, exact: false);
    CharacterCreationQualitiesAuthority authority = Authority(options ?? [positive, negative, unsupported]);
    CharacterCreationQualitiesBinding binding = new(
        new CharacterWorkspaceId("quality-workspace"),
        ContentRevision: 7,
        SavedRevision: 7,
        RawCharacterXmlDigest: Digest('5'),
        AuxiliaryStateDigest: new string('6', 64),
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
        SourceNodeXml: "<quality />",
        SourceNodeDigest: CharacterCreationQualitiesRules.ComputeSourceNodeDigest("<quality />"),
        OptionDigest: string.Empty);
    return option with
    {
        OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(option)
    };
}

static CharacterCreationQualityCatalogOption RatedOption(string id, int rating, int cost)
    => Rehash(Option("positive", CharacterCreationQualityType.Positive, cost, selectable: true, exact: true) with
    {
        OptionId = id,
        SelectionKey = "rated-quality",
        Name = "Same quality",
        Rating = rating
    });

static CharacterCreationQualityCatalogOption Rehash(CharacterCreationQualityCatalogOption option)
    => option with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(option) };

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
