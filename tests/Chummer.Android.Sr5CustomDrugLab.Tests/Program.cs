using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using System.Xml.Linq;

CreationQueuesOnlyTypedFinalizerContribution();
CreationRejectsMismatchedCoreRequestDigest();
CareerUsesFullAuthorityRecipeAndAtomicReceipt();
StaleReviewIsInvalidatedButSelectionSurvives();
InterruptedCareerCommitRecoversOnlyByReceiptLookup();
ExactFoundationBlockEnhancerConstraintsComeFromCoreQuote();

Console.WriteLine("SR5 custom-drug Android behavior tests passed.");
return;

static void CreationQueuesOnlyTypedFinalizerContribution()
{
    CharacterWorkspaceId workspaceId = new("creation-runner");
    var authority = new FakeAuthority();
    var workspaces = new FakeWorkspaceStore(workspaceId, Fixture.CreationXml, 7);
    var checkpoints = new MemoryCheckpointStore();
    var contributions = new FakeCreationContributionService(authority, workspaces)
    {
        ReturnUnavailableAfterPersist = true
    };
    var lab = new Sr5CustomDrugLabService(authority, contributions, workspaces, checkpoints);

    Sr5CustomDrugLabSnapshot edited = lab.UpdateSelection(
        workspaceId,
        CharacterCustomDrugContext.Creation,
        authority.ValidSelection("Nova"));
    Assert(edited.Quote?.Exact == true, edited.Quote?.BlockReason ?? "creation quote missing");
    Sr5CustomDrugLabSnapshot reviewed = lab.Review(
        workspaceId,
        CharacterCustomDrugContext.Creation);
    Assert(reviewed.CanConfirm, "creation review must require a separate confirmation");
    string serialized = System.Text.Json.JsonSerializer.Serialize(reviewed.Checkpoint);
    Sr5CustomDrugLabCheckpoint restored =
        System.Text.Json.JsonSerializer.Deserialize<Sr5CustomDrugLabCheckpoint>(serialized)
        ?? throw new InvalidOperationException("serialized review did not restore");
    Assert(restored.HasFreshReview(reviewed.Quote!),
        "process-resume serialization must retain the exact locked review");
    Sr5CustomDrugLabSnapshot queued = lab.ConfirmCreation(workspaceId);

    Assert(queued.IsQueuedForFinalization, "creation must end in queued-finalizer state");
    Assert(queued.Notice == Sr5CustomDrugLabNotices.FinalizerContributionRecovered,
        "an unknown queue response must recover only through Core lookup");
    Assert(contributions.QueueCalls == 1, "creation must use the typed Core queue exactly once");
    Assert(authority.CommitCalls == 0, "creation must never call direct authority Commit");
    Assert(authority.LookupCalls == 0, "creation must not perform Career receipt lookup");
    Assert(workspaces.WriteCalls == 0, "creation must never replace character XML");
    Assert(workspaces.Current.Document.Content == Fixture.CreationXml, "creation character bytes changed");
    CharacterCreationCustomDrugFinalizationContribution contribution =
        lab.ReadCreationContribution(workspaceId)
        ?? throw new InvalidOperationException("typed creation contribution missing");
    Assert(CharacterCreationCustomDrugContributionRules.IsValid(
            contribution,
            workspaceId,
            workspaces.Current.ContentRevision),
        "Core creation contribution digest is invalid");
    Assert(contribution.Selection.Name == "Nova", "typed selection was not preserved");
    Assert(contribution.ToVerificationCommand().NewComponentInstanceIds.Count == 3,
        "finalizer component identities must match the exact selection count");

    var restarted = new Sr5CustomDrugLabService(
        authority,
        contributions,
        workspaces,
        new MemoryCheckpointStore());
    Sr5CustomDrugLabSnapshot reopened = restarted.Load(
        workspaceId,
        CharacterCustomDrugContext.Creation);
    Assert(reopened.IsQueuedForFinalization,
        "process restart must recover the contribution from Core auxiliary state");
    Assert(reopened.Notice == Sr5CustomDrugLabNotices.FinalizerContributionRecovered,
        "restart recovery needs an explicit finalizer receipt posture");
}

static void CreationRejectsMismatchedCoreRequestDigest()
{
    CharacterWorkspaceId workspaceId = new("digest-attack-runner");
    var authority = new FakeAuthority();
    var workspaces = new FakeWorkspaceStore(workspaceId, Fixture.CreationXml, 4);
    var contributions = new FakeCreationContributionService(authority, workspaces)
    {
        ReturnMismatchedRequestDigest = true
    };
    var lab = new Sr5CustomDrugLabService(
        authority,
        contributions,
        workspaces,
        new MemoryCheckpointStore());
    lab.UpdateSelection(
        workspaceId,
        CharacterCustomDrugContext.Creation,
        authority.ValidSelection("Digest trap"));
    lab.Review(workspaceId, CharacterCustomDrugContext.Creation);

    bool rejected = false;
    try
    {
        _ = lab.ConfirmCreation(workspaceId);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    Assert(rejected, "Android must reject a structurally valid contribution for another request digest");
    Assert(authority.CommitCalls == 0, "digest rejection must never fall back to Career Commit");
    Assert(workspaces.WriteCalls == 0, "digest rejection must never use Android XML persistence");
    Assert(string.Equals(workspaces.Current.Document.Content, Fixture.CreationXml, StringComparison.Ordinal),
        "digest rejection changed raw creation XML");
}

static void CareerUsesFullAuthorityRecipeAndAtomicReceipt()
{
    CharacterWorkspaceId workspaceId = new("career-runner");
    var authority = new FakeAuthority();
    var workspaces = new FakeWorkspaceStore(workspaceId, Fixture.CareerXml, 10);
    var checkpoints = new MemoryCheckpointStore();
    var lab = new Sr5CustomDrugLabService(
        authority,
        new FakeCreationContributionService(authority, workspaces),
        workspaces,
        checkpoints);

    lab.UpdateSelection(
        workspaceId,
        CharacterCustomDrugContext.Career,
        authority.ValidSelection("Redline"));
    Sr5CustomDrugLabSnapshot reviewed = lab.Review(
        workspaceId,
        CharacterCustomDrugContext.Career);
    CharacterCustomDrugQuote quote = reviewed.Quote
        ?? throw new InvalidOperationException("Career preview is missing");
    Assert(quote is { Exact: true, UnitCost: 95m, ChargedCost: 0m },
        "Career preview must retain exact unit/free-initial-dose economics");
    Assert(quote.Availability == 6, "availability preview drifted");
    Assert(quote.AddictionRating == 6 && quote.AddictionThreshold == 4,
        "addiction preview drifted");

    Sr5CustomDrugLabSnapshot applied = lab.ConfirmCareer(workspaceId);
    Assert(applied.HasAppliedReceipt, "Career commit must end at a verified receipt");
    Assert(authority.CommitCalls == 1, "Career must call authority Commit exactly once");
    Assert(authority.LookupCalls >= 2, "Career must lookup before and after the commit boundary");
    Assert(workspaces.WriteCalls == 1, "Career must use one atomic replace+checkpoint");
    Assert(workspaces.Current.ContentRevision == 11 && workspaces.Current.SavedRevision == 11,
        "Career write must advance and checkpoint one exact revision");

    Sr5CustomDrugLabSnapshot reopened = lab.Load(workspaceId, CharacterCustomDrugContext.Career);
    Assert(reopened.HasAppliedReceipt, "reopen must prove the saved receipt again");
    Sr5CustomDrugLabSnapshot undone = lab.UndoCareer(workspaceId);
    Assert(undone.Notice == Sr5CustomDrugLabNotices.UndoApplied, "receipt undo was not verified");
    Assert(authority.UndoCalls == 1, "Career undo must call authority Undo");
    Assert(workspaces.WriteCalls == 2, "undo must use a second atomic replace+checkpoint");
    Assert(workspaces.Current.Document.Content == Fixture.CareerXml, "undo did not restore exact pre-recipe bytes");
}

static void StaleReviewIsInvalidatedButSelectionSurvives()
{
    CharacterWorkspaceId workspaceId = new("stale-runner");
    var authority = new FakeAuthority();
    var workspaces = new FakeWorkspaceStore(workspaceId, Fixture.CreationXml, 3);
    var checkpoints = new MemoryCheckpointStore();
    var lab = new Sr5CustomDrugLabService(
        authority,
        new FakeCreationContributionService(authority, workspaces),
        workspaces,
        checkpoints);
    lab.UpdateSelection(
        workspaceId,
        CharacterCustomDrugContext.Creation,
        authority.ValidSelection("Afterglow"));
    lab.Review(workspaceId, CharacterCustomDrugContext.Creation);

    workspaces.AdvanceUnchanged();
    Sr5CustomDrugLabSnapshot stale = lab.Load(workspaceId, CharacterCustomDrugContext.Creation);
    Assert(stale.Notice == Sr5CustomDrugLabNotices.ReviewStale,
        "revision drift must explicitly invalidate the review");
    Assert(!stale.CanConfirm, "stale review must not remain confirmable");
    Assert(stale.Selection.Name == "Afterglow", "stale recovery must preserve typed input");
    Assert(stale.Checkpoint?.Phase == Sr5CustomDrugCheckpointPhase.Editing,
        "stale contribution must return to editing");
}

static void InterruptedCareerCommitRecoversOnlyByReceiptLookup()
{
    CharacterWorkspaceId workspaceId = new("recovery-runner");
    var authority = new FakeAuthority();
    var workspaces = new FakeWorkspaceStore(workspaceId, Fixture.CareerXml, 20);
    var checkpoints = new MemoryCheckpointStore();
    var contributions = new FakeCreationContributionService(authority, workspaces);
    var lab = new Sr5CustomDrugLabService(authority, contributions, workspaces, checkpoints);
    lab.UpdateSelection(
        workspaceId,
        CharacterCustomDrugContext.Career,
        authority.ValidSelection("Recall"));
    lab.Review(workspaceId, CharacterCustomDrugContext.Career);
    Sr5CustomDrugLabCheckpoint reviewed = checkpoints.Last!;
    checkpoints.Write(reviewed with { Phase = Sr5CustomDrugCheckpointPhase.Applying });
    CharacterCustomDrugCommitResult committed = authority.Commit(
        Fixture.CareerXml,
        20,
        CharacterCustomDrugContext.Career,
        reviewed.Command!);
    workspaces.ApplyExternal(committed.CharacterXml);
    int commitsBeforeRecovery = authority.CommitCalls;

    var resumed = new Sr5CustomDrugLabService(
        authority,
        contributions,
        workspaces,
        checkpoints);
    Sr5CustomDrugLabSnapshot recovered = resumed.Load(
        workspaceId,
        CharacterCustomDrugContext.Career);
    Assert(recovered.HasAppliedReceipt, "interrupted commit must recover from LookupReceipt");
    Assert(recovered.Notice == Sr5CustomDrugLabNotices.CommitRecovered,
        "recovered receipt needs explicit UX posture");
    Assert(authority.CommitCalls == commitsBeforeRecovery,
        "recovery must not replay Commit");
}

static void ExactFoundationBlockEnhancerConstraintsComeFromCoreQuote()
{
    CharacterWorkspaceId workspaceId = new("constraint-runner");
    var authority = new FakeAuthority();
    var workspaces = new FakeWorkspaceStore(workspaceId, Fixture.CreationXml, 1);
    var checkpoints = new MemoryCheckpointStore();
    var lab = new Sr5CustomDrugLabService(
        authority,
        new FakeCreationContributionService(authority, workspaces),
        workspaces,
        checkpoints);

    CharacterCustomDrugSelection missing = authority.ValidSelection("Missing") with
    {
        Components = authority.ValidSelection("Missing").Components.Skip(1).ToArray()
    };
    Sr5CustomDrugLabSnapshot missingSnapshot = lab.UpdateSelection(
        workspaceId,
        CharacterCustomDrugContext.Creation,
        missing);
    Assert(missingSnapshot.Quote?.BlockReason == CharacterCustomDrugBlockers.MissingFoundation,
        "exact one-Foundation rule must come from Core Quote");

    CharacterCustomDrugSelection conflict = authority.ValidSelection("Conflict") with
    {
        Components =
        [
            new CharacterCustomDrugComponentSelection(FakeAuthority.FoundationId, 0),
            new CharacterCustomDrugComponentSelection(FakeAuthority.BlockId, 2)
        ]
    };
    Sr5CustomDrugLabSnapshot conflictSnapshot = lab.UpdateSelection(
        workspaceId,
        CharacterCustomDrugContext.Creation,
        conflict);
    Assert(conflictSnapshot.Quote?.BlockReason == CharacterCustomDrugBlockers.FoundationConflict,
        "level-three Block/Foundation conflict must come from Core Quote");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class MemoryCheckpointStore : ISr5CustomDrugLabCheckpointStore
{
    private readonly Dictionary<(string, CharacterCustomDrugContext), Sr5CustomDrugLabCheckpoint> _values = [];
    public Sr5CustomDrugLabCheckpoint? Last { get; private set; }

    public Sr5CustomDrugLabCheckpoint? Read(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context)
        => _values.GetValueOrDefault((workspaceId.Value, context));

    public void Write(Sr5CustomDrugLabCheckpoint checkpoint)
    {
        _values[(checkpoint.WorkspaceId.Value, checkpoint.Context)] = checkpoint;
        Last = checkpoint;
    }

    public void Clear(CharacterWorkspaceId workspaceId, CharacterCustomDrugContext context)
        => _values.Remove((workspaceId.Value, context));
}

static class Fixture
{
    public const string CareerXml =
        "<character><created>True</created><nuyen>5000</nuyen><drugs /></character>";
    public const string CreationXml =
        "<character><created>False</created><nuyen>5000</nuyen><drugs /></character>";
}

sealed class FakeWorkspaceStore : ISr5CustomDrugWorkspaceStore
{
    public FakeWorkspaceStore(CharacterWorkspaceId workspaceId, string xml, long revision)
    {
        Current = new Sr5CustomDrugWorkspaceSnapshot(
            workspaceId,
            revision,
            revision,
            new WorkspaceDocument(xml, "sr5"));
    }

    public Sr5CustomDrugWorkspaceSnapshot Current { get; private set; }
    public int WriteCalls { get; private set; }

    public Sr5CustomDrugWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId)
        => workspaceId == Current.WorkspaceId ? Current : null;

    public Sr5CustomDrugWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CustomDrugWorkspaceSnapshot expected,
        string characterXml)
    {
        WriteCalls++;
        if (expected.ContentRevision != Current.ContentRevision)
            return new(false, true, Current.ContentRevision, Current.SavedRevision, "conflict");
        Current = Current with
        {
            ContentRevision = checked(Current.ContentRevision + 1),
            SavedRevision = checked(Current.SavedRevision + 1),
            Document = Current.Document with
            {
                State = Current.Document.State with { Payload = characterXml }
            }
        };
        return new(true, false, Current.ContentRevision, Current.SavedRevision, string.Empty);
    }

    public void AdvanceUnchanged()
        => Current = Current with
        {
            ContentRevision = checked(Current.ContentRevision + 1),
            SavedRevision = checked(Current.SavedRevision + 1)
        };

    public void ApplyExternal(string characterXml)
    {
        Current = Current with
        {
            ContentRevision = checked(Current.ContentRevision + 1),
            SavedRevision = checked(Current.SavedRevision + 1),
            Document = Current.Document with
            {
                State = Current.Document.State with { Payload = characterXml }
            }
        };
    }
}

sealed class FakeCreationContributionService(
    FakeAuthority authority,
    FakeWorkspaceStore workspaces)
    : ICharacterCreationCustomDrugContributionService
{
    private CharacterCreationCustomDrugFinalizationContribution? _contribution;

    public int QueueCalls { get; private set; }
    public bool ReturnUnavailableAfterPersist { get; init; }
    public bool ReturnMismatchedRequestDigest { get; init; }

    public CharacterCreationCustomDrugResult Load(CharacterCreationCustomDrugLoadRequest request)
    {
        if (_contribution is null || request.WorkspaceId != workspaces.Current.WorkspaceId)
            return Result(CharacterCreationCustomDrugOutcomes.NotFound);
        return CharacterCreationCustomDrugContributionRules.IsValid(
            _contribution,
            request.WorkspaceId,
            workspaces.Current.ContentRevision)
            ? new CharacterCreationCustomDrugResult(
                CharacterCreationCustomDrugOutcomes.Available,
                _contribution,
                [])
            : Result(
                CharacterCreationCustomDrugOutcomes.Blocked,
                CharacterCreationCustomDrugBlockers.ProjectionRejected);
    }

    public CharacterCreationCustomDrugResult Queue(CharacterCreationCustomDrugQueueRequest request)
    {
        QueueCalls++;
        string idempotencyDigest = CharacterCreationCustomDrugContributionRules
            .ComputeRequestIdempotencyKeyDigest(request.IdempotencyKey);
        string commandDigest = CharacterCreationCustomDrugContributionRules
            .ComputeRequestCommandDigest(request);
        if (_contribution is not null
            && _contribution.ExpectedContentRevision == workspaces.Current.ContentRevision
            && CharacterCreationFinalizationDigest.EqualsFixedTime(
                _contribution.RequestIdempotencyKeyDigest,
                idempotencyDigest))
        {
            return CharacterCreationFinalizationDigest.EqualsFixedTime(
                _contribution.RequestCommandDigest,
                commandDigest)
                ? new CharacterCreationCustomDrugResult(
                    CharacterCreationCustomDrugOutcomes.Replayed,
                    _contribution,
                    [])
                : Result(
                    CharacterCreationCustomDrugOutcomes.Conflict,
                    CharacterCreationCustomDrugBlockers.IdempotencyConflict);
        }

        Sr5CustomDrugWorkspaceSnapshot current = workspaces.Current;
        CharacterCustomDrugCommitCommand requested = request.VerificationCommand;
        if (!request.ExplicitlyConfirmed
            || request.WorkspaceId != current.WorkspaceId
            || request.ExpectedContentRevision != current.ContentRevision
            || request.ExpectedSavedRevision != current.SavedRevision
            || !string.Equals(
                request.ExpectedAuxiliaryStateDigest,
                current.Document.AuxiliaryStateDigest,
                StringComparison.Ordinal)
            || requested.ExpectedContentRevision != current.ContentRevision)
        {
            return Result(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleWorkspaceRevision);
        }

        CharacterCustomDrugPreparation reviewedPreparation = authority.Prepare(
            current.Document.Content,
            current.ContentRevision,
            CharacterCustomDrugContext.Creation);
        CharacterCustomDrugQuote reviewedQuote = authority.Quote(
            reviewedPreparation,
            requested.Selection);
        if (!reviewedQuote.Exact
            || !string.Equals(
                reviewedQuote.QuoteDigest,
                requested.ExpectedQuoteDigest,
                StringComparison.Ordinal))
        {
            return Result(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleQuoteDigest);
        }

        long nextRevision = checked(current.ContentRevision + 1);
        CharacterCustomDrugPreparation nextPreparation = reviewedPreparation with
        {
            ContentRevision = nextRevision
        };
        CharacterCustomDrugQuote nextQuote = authority.Quote(
            nextPreparation,
            requested.Selection);
        var nextCommand = requested with
        {
            ExpectedContentRevision = nextRevision,
            ExpectedQuoteDigest = nextQuote.QuoteDigest
        };
        CharacterCustomDrugCreationProjection projection = authority.ProjectCreation(
            current.Document.Content,
            nextRevision,
            nextCommand);
        if (!projection.Exact)
            return Result(
                CharacterCreationCustomDrugOutcomes.Blocked,
                CharacterCreationCustomDrugBlockers.ProjectionRejected);

        var unsigned = new CharacterCreationCustomDrugFinalizationContribution(
            CharacterCreationCustomDrugSchemas.ContributionV1,
            current.WorkspaceId,
            nextRevision,
            nextPreparation.CharacterDigest,
            nextPreparation.CatalogDigest,
            nextPreparation.RulesDigest,
            requested.Selection,
            nextQuote,
            requested.NewDrugInstanceId,
            requested.NewComponentInstanceIds.ToArray(),
            projection.DrugXml,
            projection.DrugXmlDigest,
            idempotencyDigest,
            commandDigest,
            ContributionDigest: string.Empty);
        _contribution = unsigned with
        {
            ContributionDigest = CharacterCreationCustomDrugContributionRules
                .ComputeContributionDigest(unsigned)
        };
        if (ReturnMismatchedRequestDigest)
        {
            CharacterCreationCustomDrugFinalizationContribution mismatched = _contribution with
            {
                RequestCommandDigest = CharacterCreationFinalizationDigest.ComputeUtf8(
                    "adversarial-mismatched-queue-request"),
                ContributionDigest = string.Empty
            };
            _contribution = mismatched with
            {
                ContributionDigest = CharacterCreationCustomDrugContributionRules
                    .ComputeContributionDigest(mismatched)
            };
        }
        workspaces.AdvanceUnchanged();
        if (!CharacterCreationCustomDrugContributionRules.IsValid(
                _contribution,
                current.WorkspaceId,
                workspaces.Current.ContentRevision))
        {
            return Result(
                CharacterCreationCustomDrugOutcomes.Blocked,
                CharacterCreationCustomDrugBlockers.ProjectionRejected);
        }
        if (ReturnUnavailableAfterPersist)
        {
            return Result(
                CharacterCreationCustomDrugOutcomes.Unavailable,
                CharacterCreationCustomDrugBlockers.PersistenceAuthorityRequired);
        }
        return new CharacterCreationCustomDrugResult(
            CharacterCreationCustomDrugOutcomes.Applied,
            _contribution,
            []);
    }

    private static CharacterCreationCustomDrugResult Result(
        string outcome,
        params string[] blockers)
        => new(outcome, null, blockers);
}

sealed class FakeAuthority : ICharacterCustomDrugAuthority
{
    public static CharacterCustomDrugComponentId FoundationId { get; } = new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"));
    public static CharacterCustomDrugComponentId BlockId { get; } = new(
        Guid.Parse("22222222-2222-4222-8222-222222222222"));
    private static CharacterCustomDrugComponentId EnhancerId { get; } = new(
        Guid.Parse("33333333-3333-4333-8333-333333333333"));
    private static CharacterCustomDrugGradeId GradeId { get; } = new(
        Guid.Parse("44444444-4444-4444-8444-444444444444"));
    private static readonly CharacterCustomDrugCalculationPolicy Policy = new(
        MultiplyComponentCostByLevel: false,
        ApplyGradeCostMultiplier: false,
        ApplyGradeAddictionThresholdModifier: false,
        MaximumComponents: 10,
        MaximumQuantity: 100m,
        QuantityDecimalPlaces: 2);
    private static readonly CharacterCustomDrugGrade[] Grades =
    [
        new(
            GradeId,
            "Pharmaceutical",
            1m,
            0,
            "Chrome Flesh",
            H('1'),
            ["grade.pharmaceutical"])
    ];
    private static readonly CharacterCustomDrugComponentSource[] Components =
    [
        Component(
            FoundationId,
            "Tank",
            CharacterCustomDrugComponentCategory.Foundation,
            1,
            1,
            50m,
            2,
            1,
            [Effect(0, body: -1m)]),
        Component(
            BlockId,
            "Crush",
            CharacterCustomDrugComponentCategory.Block,
            2,
            3,
            30m,
            3,
            2,
            [Effect(0, body: 1m), Effect(1, initiative: 2), Effect(2, body: 1m)]),
        Component(
            EnhancerId,
            "Accelerator",
            CharacterCustomDrugComponentCategory.Enhancer,
            1,
            2,
            15m,
            1,
            1,
            [Effect(0, speed: 1)])
    ];
    private static readonly string RulesDigest = CharacterCustomDrugRules.ComputeRulesDigest(Policy);
    private static readonly string CatalogDigest = CharacterCustomDrugRules.ComputeCatalogDigest(
        "sr5",
        "full-house",
        RulesDigest,
        Grades,
        Components);

    public int PrepareCalls { get; private set; }
    public int QuoteCalls { get; private set; }
    public int CommitCalls { get; private set; }
    public int LookupCalls { get; private set; }
    public int UndoCalls { get; private set; }

    public CharacterCustomDrugSelection ValidSelection(string name)
        => new(
            name,
            GradeId,
            1m,
            false,
            false,
            0m,
            [
                new CharacterCustomDrugComponentSelection(FoundationId, 0),
                new CharacterCustomDrugComponentSelection(BlockId, 1),
                new CharacterCustomDrugComponentSelection(EnhancerId, 0)
            ]);

    public CharacterCustomDrugPreparation Prepare(
        string characterXml,
        long contentRevision,
        CharacterCustomDrugContext context)
    {
        PrepareCalls++;
        return new CharacterCustomDrugPreparation(
            true,
            [],
            context,
            CharacterCustomDrugQuotePurpose.RecipeDefinition,
            contentRevision,
            CharacterCustomDrugRules.ComputeCharacterDigest(characterXml),
            CatalogDigest,
            RulesDigest,
            "full-house",
            5000m,
            Policy,
            Grades,
            Components);
    }

    public CharacterCustomDrugQuote Quote(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection)
    {
        QuoteCalls++;
        return CharacterCustomDrugRules.Quote(preparation, selection);
    }

    public CharacterCustomDrugCreationProjection ProjectCreation(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugCommitCommand command)
    {
        CharacterCustomDrugPreparation preparation = Prepare(
            characterXml,
            currentContentRevision,
            CharacterCustomDrugContext.Creation);
        CharacterCustomDrugQuote quote = Quote(preparation, command.Selection);
        if (!quote.Exact
            || currentContentRevision != command.ExpectedContentRevision
            || !string.Equals(
                preparation.CharacterDigest,
                command.ExpectedCharacterDigest,
                StringComparison.Ordinal)
            || !string.Equals(
                preparation.CatalogDigest,
                command.ExpectedCatalogDigest,
                StringComparison.Ordinal)
            || !string.Equals(
                preparation.RulesDigest,
                command.ExpectedRulesDigest,
                StringComparison.Ordinal)
            || !string.Equals(
                quote.QuoteDigest,
                command.ExpectedQuoteDigest,
                StringComparison.Ordinal)
            || command.NewComponentInstanceIds.Count != command.Selection.Components.Count)
        {
            return new CharacterCustomDrugCreationProjection(
                false,
                CharacterCreationCustomDrugBlockers.ProjectionRejected,
                string.Empty,
                string.Empty,
                quote.QuoteDigest);
        }

        XElement components = new(
            "drugcomponents",
            command.Selection.Components.Select((component, index) => new XElement(
                "drugcomponent",
                new XElement("guid", command.NewComponentInstanceIds[index].ToString("D")),
                new XElement("sourceid", component.ComponentId.Value.ToString("D")))));
        string drugXml = new XElement(
                "drug",
                new XElement("guid", command.NewDrugInstanceId.Value.ToString("D")),
                components)
            .ToString(SaveOptions.DisableFormatting);
        return new CharacterCustomDrugCreationProjection(
            true,
            string.Empty,
            drugXml,
            CharacterCustomDrugRules.ComputeCharacterDigest(drugXml),
            quote.QuoteDigest);
    }

    public CharacterCustomDrugCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugCommitCommand command)
    {
        CommitCalls++;
        if (context != CharacterCustomDrugContext.Career)
            throw new InvalidOperationException("creation called direct Commit");
        CharacterCustomDrugPreparation preparation = Prepare(characterXml, currentContentRevision, context);
        CharacterCustomDrugQuote quote = Quote(preparation, command.Selection);
        if (currentContentRevision != command.ExpectedContentRevision
            || preparation.CharacterDigest != command.ExpectedCharacterDigest
            || quote.QuoteDigest != command.ExpectedQuoteDigest)
        {
            return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.StaleRevision);
        }
        string output = characterXml.Replace("<drugs />", $"<drugs><drug id=\"{command.NewDrugInstanceId.Value:D}\" /></drugs>", StringComparison.Ordinal);
        CharacterCustomDrugCommitReceipt receipt = Receipt(command, output);
        return new(
            true,
            false,
            string.Empty,
            currentContentRevision,
            checked(currentContentRevision + 1),
            preparation.CharacterDigest,
            CharacterCustomDrugRules.ComputeCharacterDigest(output),
            output,
            receipt);
    }

    public CharacterCustomDrugCommitResult LookupReceipt(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugCommitCommand command)
    {
        LookupCalls++;
        if (context != CharacterCustomDrugContext.Career
            || currentContentRevision != command.ExpectedContentRevision + 1
            || !characterXml.Contains(command.NewDrugInstanceId.Value.ToString("D"), StringComparison.Ordinal))
        {
            return Blocked(characterXml, currentContentRevision, string.Empty);
        }
        CharacterCustomDrugCommitReceipt receipt = Receipt(command, characterXml);
        return new(
            true,
            true,
            string.Empty,
            command.ExpectedContentRevision,
            currentContentRevision,
            command.ExpectedCharacterDigest,
            CharacterCustomDrugRules.ComputeCharacterDigest(characterXml),
            characterXml,
            receipt);
    }

    public CharacterCustomDrugCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugUndoCommand command)
    {
        UndoCalls++;
        if (context != CharacterCustomDrugContext.Career || command.Receipt is null)
            return Blocked(characterXml, currentContentRevision, CharacterCustomDrugBlockers.StaleReceipt);
        return new(
            true,
            false,
            string.Empty,
            currentContentRevision,
            checked(currentContentRevision + 1),
            CharacterCustomDrugRules.ComputeCharacterDigest(characterXml),
            CharacterCustomDrugRules.ComputeCharacterDigest(Fixture.CareerXml),
            Fixture.CareerXml,
            null);
    }

    private static CharacterCustomDrugCommitReceipt Receipt(
        CharacterCustomDrugCommitCommand command,
        string output)
    {
        var unsigned = new CharacterCustomDrugCommitReceipt(
            command.ExpectedContentRevision,
            checked(command.ExpectedContentRevision + 1),
            command.ExpectedCharacterDigest,
            CharacterCustomDrugRules.ComputeCharacterDigest(output),
            command.ExpectedCatalogDigest,
            command.ExpectedRulesDigest,
            command.ExpectedQuoteDigest,
            CharacterCustomDrugRules.ComputeCommandDigest(command),
            CharacterCustomDrugRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey),
            command.NewDrugInstanceId,
            command.NewComponentInstanceIds,
            H('9'),
            string.Empty);
        return unsigned with { ReceiptDigest = CharacterCustomDrugRules.ComputeReceiptDigest(unsigned) };
    }

    private static CharacterCustomDrugCommitResult Blocked(string xml, long revision, string reason)
        => new(false, false, reason, revision, revision,
            CharacterCustomDrugRules.ComputeCharacterDigest(xml),
            CharacterCustomDrugRules.ComputeCharacterDigest(xml), xml, null);

    private static CharacterCustomDrugComponentSource Component(
        CharacterCustomDrugComponentId id,
        string name,
        CharacterCustomDrugComponentCategory category,
        int limit,
        int availability,
        decimal cost,
        int addictionRating,
        int addictionThreshold,
        IReadOnlyList<CharacterCustomDrugEffectLevel> effects)
        => new(
            id,
            name,
            category,
            limit,
            availability,
            CharacterCustomDrugLegality.Restricted,
            cost,
            addictionRating,
            addictionThreshold,
            "Chrome Flesh",
            "190",
            H('a'),
            [$"component.{name.ToLowerInvariant()}"],
            effects);

    private static CharacterCustomDrugEffectLevel Effect(
        int level,
        decimal? body = null,
        int initiative = 0,
        int speed = 0)
        => new(
            level,
            body.HasValue ? [new CharacterCustomDrugAttributeEffect("BOD", body.Value)] : [],
            [],
            [],
            [],
            initiative,
            0,
            0,
            speed,
            0);

    private static string H(char value) => new(value, 64);
}
