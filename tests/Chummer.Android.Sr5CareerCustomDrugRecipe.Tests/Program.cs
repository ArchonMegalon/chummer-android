using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

internal static class Program
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("workspace-career-custom-drug");
    private static readonly CharacterCustomDrugGradeId GradeId = new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"));
    private static readonly CharacterCustomDrugComponentId FoundationId = new(
        Guid.Parse("22222222-2222-4222-8222-222222222222"));
    private static readonly CharacterCustomDrugComponentId BlockId = new(
        Guid.Parse("33333333-3333-4333-8333-333333333333"));

    private static int Main()
    {
        try
        {
            ReviewCommitRestartAndUndo();
            UnknownOutcomeNeverReplays();
            StaleReviewIsDiscarded();
            FixedRecipeDefinitionOptionsAndFoundationAreEnforced();
            SourceDriftRemovesUnavailableIdentity();
            Console.WriteLine("SR5 Career custom-drug Android orchestration tests passed (5 hostile scenarios).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ReviewCommitRestartAndUndo()
    {
        FakeAuthority authority = new();
        FakeCheckpointStore checkpoints = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), revision: 7)
        {
            BeforeReplace = () => checkpoints.History.Any(checkpoint =>
                    checkpoint.Phase == Sr5CareerCustomDrugRecipePhase.Applying
                    && checkpoint.Receipt is null)
                && checkpoints.Current is
                {
                    Phase: Sr5CareerCustomDrugRecipePhase.Applying,
                    Receipt: not null
                }
        };
        Sr5CareerCustomDrugRecipeService first = new(authority, workspaces, checkpoints);
        CharacterCustomDrugSelection selected = ValidSelection("Jazz Plus");

        Sr5CareerCustomDrugRecipeSnapshot editing = first.UpdateSelection(WorkspaceId, selected);
        Assert(editing.Quote?.Exact == true && editing.Quote.ChargedCost == 0m,
            "Core quotes one free recipe-definition dose");
        Sr5CareerCustomDrugRecipeSnapshot reviewed = first.Review(WorkspaceId);
        Assert(reviewed.CanConfirm, "separate review is required");
        CharacterCustomDrugCommitCommand command = reviewed.Checkpoint!.Command!;
        Assert(command.Selection.Quantity == 1m
               && !command.Selection.Stolen
               && !command.Selection.FreeCost
               && command.Selection.MarkupPercent == 0m,
            "fixed recipe-definition options");
        Assert(command.NewDrugInstanceId.Value != Guid.Empty
               && command.NewComponentInstanceIds.Count == 2
               && command.NewComponentInstanceIds.Distinct().Count() == 2
               && !command.NewComponentInstanceIds.Contains(command.NewDrugInstanceId.Value),
            "new exact identities are stable and distinct");

        Sr5CareerCustomDrugRecipeSnapshot applied = first.Confirm(WorkspaceId);
        Assert(applied.HasAppliedReceipt, "applied receipt");
        Assert(workspaces.Revision == 8 && workspaces.SavedRevision == 8,
            "one atomic saved revision");
        Assert(authority.CommitCalls == 1, "one Core commit");
        Assert(workspaces.ObservedPreCasAuthority,
            "durable applying checkpoint and Core receipt both precede external CAS");
        ExpectInvalid(() => first.Confirm(WorkspaceId), "applied commit cannot replay");
        ExpectInvalid(() => first.Review(WorkspaceId), "applied receipt cannot be replaced by review");
        ExpectInvalid(() => first.UpdateSelection(WorkspaceId, selected), "applied receipt locks editing");
        Assert(authority.CommitCalls == 1, "blocked actions never replay Core commit");

        Sr5CareerCustomDrugRecipeService restarted = new(authority, workspaces, checkpoints);
        Sr5CareerCustomDrugRecipeSnapshot recovered = restarted.Load(WorkspaceId);
        Assert(recovered.HasAppliedReceipt, "LookupReceipt recovers after restart");
        Sr5CareerCustomDrugRecipeSnapshot undone = restarted.Undo(WorkspaceId);
        Assert(undone.Notice == Sr5CareerCustomDrugRecipeNotices.UndoApplied,
            "receipt-bound undo");
        Assert(workspaces.Revision == 9 && !workspaces.Xml.Contains("<custom-drug", StringComparison.Ordinal),
            "undo removes only the proven recipe in one saved revision");
        ExpectInvalid(() => restarted.Reopen(WorkspaceId), "only an applied receipt may be closed");
    }

    private static void UnknownOutcomeNeverReplays()
    {
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), revision: 3) { AmbiguousWrite = true };
        FakeCheckpointStore checkpoints = new();
        Sr5CareerCustomDrugRecipeService service = new(authority, workspaces, checkpoints);
        _ = service.UpdateSelection(WorkspaceId, ValidSelection("Crash Guard"));
        _ = service.Review(WorkspaceId);
        Sr5CareerCustomDrugRecipeSnapshot result = service.Confirm(WorkspaceId);
        Assert(result.IsRecoveryUnknown && authority.CommitCalls == 1,
            "ambiguous CAS is locked after one Core commit");

        Sr5CareerCustomDrugRecipeService restarted = new(authority, workspaces, checkpoints);
        Sr5CareerCustomDrugRecipeSnapshot recovered = restarted.Load(WorkspaceId);
        Assert(recovered.CanConfirm
               && recovered.Notice == Sr5CareerCustomDrugRecipeNotices.CommitNotApplied,
            "unchanged pre-CAS revision recovers as not applied");
        ExpectInvalid(() => restarted.Review(WorkspaceId),
            "resolved reviewed recovery cannot be overwritten by another review");
        Assert(authority.CommitCalls == 1, "restart never replays recipe mutation");
    }

    private static void StaleReviewIsDiscarded()
    {
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), revision: 11);
        FakeCheckpointStore checkpoints = new();
        Sr5CareerCustomDrugRecipeService service = new(authority, workspaces, checkpoints);
        _ = service.UpdateSelection(WorkspaceId, ValidSelection("Revision Test"));
        Assert(service.Review(WorkspaceId).CanConfirm, "review is initially fresh");
        workspaces.ExternalReplace(CharacterXml().Replace("100000", "99999", StringComparison.Ordinal));

        Sr5CareerCustomDrugRecipeSnapshot rebound = service.Load(WorkspaceId);
        Assert(!rebound.CanConfirm
               && rebound.Checkpoint?.Phase == Sr5CareerCustomDrugRecipePhase.Editing
               && rebound.Notice == Sr5CareerCustomDrugRecipeNotices.ReviewStale,
            "workspace change discards stale review");
    }

    private static void FixedRecipeDefinitionOptionsAndFoundationAreEnforced()
    {
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), revision: 5);
        FakeCheckpointStore checkpoints = new();
        Sr5CareerCustomDrugRecipeService service = new(authority, workspaces, checkpoints);
        CharacterCustomDrugSelection hostile = ValidSelection("Bounded") with
        {
            Quantity = 99m,
            Stolen = true,
            FreeCost = true,
            MarkupPercent = 1000m,
            Components =
            [
                new(FoundationId, 1),
                new(FoundationId, 1)
            ]
        };
        Sr5CareerCustomDrugRecipeSnapshot normalized = service.UpdateSelection(WorkspaceId, hostile);
        Assert(normalized.Selection.Quantity == 1m
               && !normalized.Selection.Stolen
               && !normalized.Selection.FreeCost
               && normalized.Selection.MarkupPercent == 0m,
            "Android cannot widen recipe-definition options");
        Assert(normalized.Quote?.Exact == false
               && normalized.Quote.BlockReason == CharacterCustomDrugBlockers.DuplicateFoundation,
            "Core rejects duplicate Foundation");
        Sr5CareerCustomDrugRecipeCheckpoint structurallyHostile = normalized.Checkpoint! with
        {
            Phase = (Sr5CareerCustomDrugRecipePhase)999
        };
        Assert(!structurallyHostile.BelongsTo(WorkspaceId),
            "unknown checkpoint phase grants no local recipe authority");
        ExpectInvalid(() => service.Review(WorkspaceId), "invalid Foundation recipe cannot review");

        _ = service.UpdateSelection(WorkspaceId, ValidSelection("No Foundation") with
        {
            Components = [new(BlockId, 1)]
        });
        ExpectInvalid(() => service.Review(WorkspaceId), "missing Foundation cannot review");
        Sr5CareerCustomDrugRecipeSnapshot overLimit = service.UpdateSelection(
            WorkspaceId,
            ValidSelection("Block Limit") with
            {
                Components =
                [
                    new(FoundationId, 1),
                    new(BlockId, 1),
                    new(BlockId, 1),
                    new(BlockId, 1)
                ]
            });
        Assert(overLimit.Quote?.Exact == false
               && overLimit.Quote.BlockReason == CharacterCustomDrugBlockers.ComponentLimit,
            "Core owns the source-defined Block repeat limit");
        Sr5CareerCustomDrugRecipeSnapshot invalidLevel = service.UpdateSelection(
            WorkspaceId,
            ValidSelection("Exact Level") with
            {
                Components = [new(FoundationId, 1), new(BlockId, 63)]
            });
        Assert(invalidLevel.Selection.Components.Count == 1
               && invalidLevel.Selection.Components[0].ComponentId == FoundationId,
            "unavailable effect level is removed during exact catalog rebind");
        Assert(authority.CommitCalls == 0, "invalid recipes never reach commit");
    }

    private static void SourceDriftRemovesUnavailableIdentity()
    {
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), revision: 13);
        FakeCheckpointStore checkpoints = new();
        Sr5CareerCustomDrugRecipeService service = new(authority, workspaces, checkpoints);
        _ = service.UpdateSelection(WorkspaceId, ValidSelection("Catalog Drift"));
        authority.IncludeBlock = false;
        workspaces.ExternalReplace(CharacterXml().Replace("100000", "99998", StringComparison.Ordinal));

        Sr5CareerCustomDrugRecipeSnapshot rebound = service.Load(WorkspaceId);
        Assert(rebound.Selection.Components.Count == 1
               && rebound.Selection.Components[0].ComponentId == FoundationId,
            "source drift removes an unavailable exact component identity");
        Assert(!rebound.CanConfirm, "source drift invalidates confirmation");
    }

    private static CharacterCustomDrugSelection ValidSelection(string name)
        => new(
            name,
            GradeId,
            Quantity: 1m,
            Stolen: false,
            FreeCost: false,
            MarkupPercent: 0m,
            Components: [new(FoundationId, 1), new(BlockId, 1)]);

    private static string CharacterXml()
        => "<character><created>True</created><nuyen>100000</nuyen><drugs/></character>";

    private static void ExpectInvalid(Action action, string name)
    {
        try
        {
            action();
            throw new InvalidOperationException($"Assertion failed: {name}");
        }
        catch (InvalidOperationException exception) when (!exception.Message.StartsWith("Assertion failed", StringComparison.Ordinal))
        {
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Assertion failed: {name}");
    }

    private sealed class FakeCheckpointStore : ISr5CareerCustomDrugRecipeCheckpointStore
    {
        private Sr5CareerCustomDrugRecipeCheckpoint? _checkpoint;
        public List<Sr5CareerCustomDrugRecipeCheckpoint> History { get; } = [];
        public Sr5CareerCustomDrugRecipeCheckpoint? Current => _checkpoint;
        public Sr5CareerCustomDrugRecipeCheckpoint? Read(CharacterWorkspaceId workspaceId)
            => _checkpoint?.WorkspaceId == workspaceId ? _checkpoint : null;
        public void Write(Sr5CareerCustomDrugRecipeCheckpoint checkpoint)
        {
            _checkpoint = checkpoint;
            History.Add(checkpoint);
        }
        public void Clear(CharacterWorkspaceId workspaceId) => _checkpoint = null;
    }

    private sealed class FakeWorkspaceStore(string xml, long revision) : ISr5CareerCustomDrugWorkspaceStore
    {
        public string Xml { get; private set; } = xml;
        public long Revision { get; private set; } = revision;
        public long SavedRevision { get; private set; } = revision;
        public bool AmbiguousWrite { get; init; }
        public Func<bool>? BeforeReplace { get; init; }
        public bool ObservedPreCasAuthority { get; private set; }
        private int ReplaceCalls { get; set; }

        public Sr5CareerCustomDrugWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId)
            => workspaceId == WorkspaceId ? new(workspaceId, Revision, SavedRevision, new WorkspaceDocument(Xml)) : null;

        public Sr5CareerCustomDrugWorkspaceWriteResult ReplaceAndCheckpoint(
            Sr5CareerCustomDrugWorkspaceSnapshot expected,
            string characterXml)
        {
            if (ReplaceCalls++ == 0)
                ObservedPreCasAuthority = BeforeReplace?.Invoke() ?? true;
            if (ReplaceCalls == 1 && !ObservedPreCasAuthority)
                throw new InvalidOperationException("pre-CAS authority checkpoint missing");
            if (AmbiguousWrite)
                return new(false, false, Revision, SavedRevision, "write-outcome-unknown");
            if (expected.ContentRevision != Revision)
                return new(false, true, Revision, SavedRevision, "revision-conflict");
            Xml = characterXml;
            Revision++;
            SavedRevision = Revision;
            return new(true, false, Revision, SavedRevision, string.Empty);
        }

        public void ExternalReplace(string replacement)
        {
            Xml = replacement;
            Revision++;
            SavedRevision = Revision;
        }
    }

    private sealed class FakeAuthority : ICharacterCustomDrugAuthority
    {
        private readonly Dictionary<string, CharacterCustomDrugCommitReceipt> _receipts = new(StringComparer.Ordinal);
        public int CommitCalls { get; private set; }
        public bool IncludeBlock { get; set; } = true;

        public CharacterCustomDrugPreparation Prepare(
            string characterXml,
            long contentRevision,
            CharacterCustomDrugContext context)
        {
            CharacterCustomDrugComponentSource[] components = IncludeBlock
                ? [Component(FoundationId, CharacterCustomDrugComponentCategory.Foundation, 1),
                   Component(BlockId, CharacterCustomDrugComponentCategory.Block, 2)]
                : [Component(FoundationId, CharacterCustomDrugComponentCategory.Foundation, 1)];
            return new(
                Exact: context == CharacterCustomDrugContext.Career,
                Blockers: context == CharacterCustomDrugContext.Career ? [] : [CharacterCustomDrugBlockers.NotCareer],
                context,
                CharacterCustomDrugQuotePurpose.RecipeDefinition,
                contentRevision,
                CharacterCustomDrugRules.ComputeCharacterDigest(characterXml),
                CatalogDigest: IncludeBlock ? new string('a', 64) : new string('b', 64),
                RulesDigest: new string('c', 64),
                SettingsProfileId: "sr5-test",
                AvailableNuyen: 100000m,
                new CharacterCustomDrugCalculationPolicy(true, false, false, 8, 100m, 2),
                Grades: [new(GradeId, "Standard", 1m, 0, "SR5", new string('d', 64), ["grade-standard"])],
                Components: components);
        }

        public CharacterCustomDrugQuote Quote(
            CharacterCustomDrugPreparation preparation,
            CharacterCustomDrugSelection selection)
            => CharacterCustomDrugRules.Quote(preparation, selection);

        public CharacterCustomDrugCommitResult Commit(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugCommitCommand command)
        {
            CommitCalls++;
            CharacterCustomDrugPreparation preparation = Prepare(characterXml, currentContentRevision, context);
            CharacterCustomDrugQuote quote = Quote(preparation, command.Selection);
            if (!quote.Exact
                || command.ExpectedContentRevision != currentContentRevision
                || command.ExpectedCharacterDigest != preparation.CharacterDigest
                || command.ExpectedCatalogDigest != preparation.CatalogDigest
                || command.ExpectedRulesDigest != preparation.RulesDigest
                || command.ExpectedQuoteDigest != quote.QuoteDigest)
                return Blocked(characterXml, currentContentRevision, "stale");

            string node = $"<custom-drug id=\"{command.NewDrugInstanceId.Value:D}\" command=\"{command.IdempotencyKey}\"/>";
            string output = characterXml.Replace("</character>", node + "</character>", StringComparison.Ordinal);
            string outputDigest = CharacterCustomDrugRules.ComputeCharacterDigest(output);
            var unsigned = new CharacterCustomDrugCommitReceipt(
                currentContentRevision,
                currentContentRevision + 1,
                preparation.CharacterDigest,
                outputDigest,
                preparation.CatalogDigest,
                preparation.RulesDigest,
                quote.QuoteDigest,
                CharacterCustomDrugRules.ComputeCommandDigest(command),
                CharacterCustomDrugRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey),
                command.NewDrugInstanceId,
                command.NewComponentInstanceIds,
                new string('e', 64),
                ReceiptDigest: string.Empty);
            CharacterCustomDrugCommitReceipt receipt = unsigned with
            {
                ReceiptDigest = CharacterCustomDrugRules.ComputeReceiptDigest(unsigned)
            };
            _receipts[command.IdempotencyKey] = receipt;
            return new(true, false, string.Empty, currentContentRevision, currentContentRevision + 1,
                preparation.CharacterDigest, outputDigest, output, receipt);
        }

        public CharacterCustomDrugCommitResult LookupReceipt(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugCommitCommand command)
        {
            bool hasReceipt = _receipts.TryGetValue(
                command.IdempotencyKey,
                out CharacterCustomDrugCommitReceipt? receipt);
            bool present = hasReceipt
                           && characterXml.Contains(
                               command.NewDrugInstanceId.Value.ToString("D"),
                               StringComparison.Ordinal);
            return present && receipt is not null
                ? new CharacterCustomDrugCommitResult(true, true, string.Empty,
                    receipt.PreviousContentRevision, currentContentRevision,
                    receipt.PreviousCharacterDigest,
                    CharacterCustomDrugRules.ComputeCharacterDigest(characterXml),
                    characterXml,
                    receipt)
                : Blocked(characterXml, currentContentRevision, "not-applied");
        }

        public CharacterCustomDrugCommitResult Undo(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugUndoCommand command)
        {
            CharacterCustomDrugCommitReceipt? receipt = command.Receipt;
            if (receipt is null
                || receipt.ContentRevision != currentContentRevision
                || receipt.CharacterDigest != CharacterCustomDrugRules.ComputeCharacterDigest(characterXml)
                || receipt.ReceiptDigest != CharacterCustomDrugRules.ComputeReceiptDigest(receipt))
                return Blocked(characterXml, currentContentRevision, "stale-undo");
            int start = characterXml.IndexOf("<custom-drug", StringComparison.Ordinal);
            int end = start < 0 ? -1 : characterXml.IndexOf("/>", start, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                return Blocked(characterXml, currentContentRevision, "missing-receipt-node");
            string output = characterXml.Remove(start, end + 2 - start);
            return new(true, false, string.Empty, currentContentRevision, currentContentRevision + 1,
                receipt.CharacterDigest,
                CharacterCustomDrugRules.ComputeCharacterDigest(output),
                output,
                Receipt: null);
        }

        private static CharacterCustomDrugComponentSource Component(
            CharacterCustomDrugComponentId id,
            CharacterCustomDrugComponentCategory category,
            int limit)
            => new(
                id,
                category == CharacterCustomDrugComponentCategory.Foundation ? "Foundation A" : "Block A",
                category,
                limit,
                AvailabilityModifier: 2,
                CharacterCustomDrugLegality.Restricted,
                CostPerLevel: 100m,
                AddictionRating: 1,
                AddictionThreshold: 1,
                SourceBook: "SR5",
                Page: "414",
                SourceNodeDigest: new string(category == CharacterCustomDrugComponentCategory.Foundation ? 'f' : '1', 64),
                SourceAnchorIds: ["source-anchor"],
                Effects: [new(1, [], [], [], [], 0, 0, 0, 0, 1)]);

        private static CharacterCustomDrugCommitResult Blocked(string xml, long revision, string reason)
        {
            string digest = CharacterCustomDrugRules.ComputeCharacterDigest(xml);
            return new(false, false, reason, revision, revision, digest, digest, xml, null);
        }
    }
}
