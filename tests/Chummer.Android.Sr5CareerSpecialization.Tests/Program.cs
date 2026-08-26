using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

return SpecializationHarness.Run();

internal static class SpecializationHarness
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("runner-specialization");
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SkillId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SpecializationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ExpenseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTime ExpenseDate = new(2081, 6, 3, 19, 30, 0, DateTimeKind.Unspecified);
    private static readonly CharacterCareerSkillSpecializationOption Option = new(
        new string('a', 64),
        "Urban",
        CharacterCareerSkillSpecializationOptionKind.SourceCatalog,
        "skills.xml:sneaking:urban");

    public static int Run()
    {
        (string Name, Action Test)[] tests =
        [
            (nameof(DraftBindsTypedIdentitySelectionAndFourRevisions), DraftBindsTypedIdentitySelectionAndFourRevisions),
            (nameof(CustomKnowledgeIdentityNeverGetsFabricatedSourceOrOptionId), CustomKnowledgeIdentityNeverGetsFabricatedSourceOrOptionId),
            (nameof(SharedOwnerAndImmediateProjectionReceiptAreExact), SharedOwnerAndImmediateProjectionReceiptAreExact),
            (nameof(RestartedChangedRevisionStaysOutcomeUnknown), RestartedChangedRevisionStaysOutcomeUnknown),
            (nameof(PhysicalProofSkeletonFailsClosed), PhysicalProofSkeletonFailsClosed)
        ];
        int failed = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} specialization authority tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void DraftBindsTypedIdentitySelectionAndFourRevisions()
    {
        CharacterCareerSkillSpecializationQuote quote = Quote(
            CharacterCareerSkillKind.Active,
            SourceId,
            new("Urban", CharacterCareerSkillSpecializationOptionKind.SourceCatalog, Option.OptionIdentity));
        Sr5CareerSpecializationDraft draft = Draft(Editor(17, quote), quote);
        CareerSkillSpecializationRequest request = draft.ToRequest();
        Check(draft.ActionPlan.Kind == Sr5CareerActionKind.SkillSpecializationAdd, "Wrong action kind.");
        Check((int)draft.ActionPlan.Kind == 5, "Specialization action must append at stable ordinal 5.");
        Check(draft.ActionPlan.RouteId == Sr5CareerWizardRoutes.SpecializationReview, "Wrong review route.");
        Check(draft.ActionPlan.DomainIdentity == $"Active:{SkillId:D}:{SourceId:D}", "Typed identity was flattened incorrectly.");
        Check(request.ExpectedCharacterRevision == quote.CharacterRevision, "Character revision CAS was dropped.");
        Check(request.ExpectedSourceRevision == quote.SourceRevision, "Source revision CAS was dropped.");
        Check(request.ExpectedRuleDigest == quote.RuleDigest, "Rule digest CAS was dropped.");
        Check(request.ExpectedLogicalRevision == quote.LogicalRevision, "Logical revision CAS was dropped.");
        Check(request.SpecializationId == SpecializationId && request.ExpenseId == ExpenseId, "Saved identities changed.");
        Check(draft.ActionPlan.IdempotencyKey.Length == 64, "Action key is not SHA-256 shaped.");
    }

    private static void CustomKnowledgeIdentityNeverGetsFabricatedSourceOrOptionId()
    {
        CharacterCareerSkillSpecializationQuote quote = Quote(
            CharacterCareerSkillKind.Knowledge,
            sourceId: null,
            new("Halloweeners", CharacterCareerSkillSpecializationOptionKind.Custom, OptionIdentity: null));
        Sr5CareerSpecializationDraft draft = Draft(Editor(17, quote), quote);
        Check(draft.Quote.Identity.SourceSkillId is null, "Custom knowledge gained a fake source ID.");
        Check(draft.Quote.Selection.OptionIdentity is null, "Custom selection gained a fake option ID.");
        Check(draft.ActionPlan.DomainIdentity.EndsWith(":custom", StringComparison.Ordinal), "Custom identity is not explicit.");
    }

    private static void SharedOwnerAndImmediateProjectionReceiptAreExact()
    {
        CharacterCareerSkillSpecializationQuote quote = Quote(
            CharacterCareerSkillKind.Active,
            SourceId,
            new("Urban", CharacterCareerSkillSpecializationOptionKind.SourceCatalog, Option.OptionIdentity));
        CareerSkillSpecializationEditorState editor = Editor(17, quote);
        Sr5CareerSpecializationDraft draft = Draft(editor, quote);
        MutableOwner owner = new(OwnerId);
        FakePresenter presenter = new(editor, quote);
        Sr5CareerSpecializationLiveCheckpointAuthority authority = new(owner, editor, () => presenter.Binding);
        MemoryBackend checkpointBackend = new();
        MemoryBackend ownerBackend = new();
        Sr5CareerSpecializationCheckpointStore store = new(
            checkpointBackend,
            authority,
            new Sr5CareerMutationOwnerStore(ownerBackend));
        Sr5CareerSpecializationCheckpoint reviewed = Sr5CareerSpecializationCheckpoint.FromDraft(draft);
        Check(store.TryCreate(reviewed, out Sr5CareerSpecializationCheckpoint stored, out string blocker), blocker);
        Check(store.TryBeginApply(
            Sr5CareerSpecializationCheckpointCas.From(stored),
            out Sr5CareerSpecializationCheckpoint applying,
            out blocker), blocker);
        Check(ownerBackend.Read().Contains(Sr5CareerMutationDomains.SkillSpecializationAdd, StringComparison.Ordinal),
            "Specialization did not reserve the shared durable owner.");
        Sr5CareerSpecializationCoordinator coordinator = new(presenter, owner);
        Sr5CareerSpecializationApplyResult result = coordinator.ApplyAsync(draft, applying, store)
            .GetAwaiter().GetResult();
        Check(result.Status == Sr5CareerSpecializationRecoveryStatus.AppliedVerifiedInCurrentProcess,
            result.Message);
        Check(result.Receipt is not null && Sr5CareerSpecializationCoordinator.VerifiesReceipt(draft, result.Receipt),
            "Current-process projection receipt did not verify.");
        Check(store.TryRecordImmediateApplied(
            Sr5CareerSpecializationCheckpointCas.From(applying),
            result.Receipt!,
            out Sr5CareerSpecializationCheckpoint applied,
            out blocker), blocker);
        Check(applied.Phase == Sr5CareerCheckpointPhase.Applied && applied.Version == 3,
            "Applying did not advance to exact Applied CAS.");
        Check(string.IsNullOrWhiteSpace(ownerBackend.Read()), "Resolved mutation owner was not released.");
        Check(store.TryDeleteApplied(
            Sr5CareerSpecializationCheckpointCas.From(applied),
            result.Receipt!,
            out blocker), blocker);
    }

    private static void RestartedChangedRevisionStaysOutcomeUnknown()
    {
        CharacterCareerSkillSpecializationQuote quote = Quote(
            CharacterCareerSkillKind.Active,
            SourceId,
            new("Urban", CharacterCareerSkillSpecializationOptionKind.SourceCatalog, Option.OptionIdentity));
        CareerSkillSpecializationEditorState editor = Editor(17, quote);
        Sr5CareerSpecializationDraft draft = Draft(editor, quote);
        MutableOwner owner = new(OwnerId);
        FakePresenter presenter = new(editor, quote) { ForceAppliedBinding = true };
        Sr5CareerSpecializationCoordinator coordinator = new(presenter, owner);
        Sr5CareerSpecializationResolution resolution = coordinator.ResolveAsync(
            Sr5CareerSpecializationCheckpoint.FromDraft(draft) with
            {
                Version = 2,
                Phase = Sr5CareerCheckpointPhase.Applying
            }).GetAwaiter().GetResult();
        Check(resolution.Status == Sr5CareerSpecializationRecoveryStatus.OutcomeUnknown,
            "Changed revision was guessed as applied/not-applied without persisted receipt authority.");
        Check(resolution.Message.Contains("Do not replay", StringComparison.OrdinalIgnoreCase),
            "Unknown recovery did not explicitly prohibit replay.");
    }

    private static void PhysicalProofSkeletonFailsClosed()
    {
        Sr5CareerSpecializationPhysicalProofContract empty = new("", 36, "", "", "", "");
        Check(!empty.IsSatisfied(), "Empty physical evidence was accepted.");
        Check(Sr5CareerSpecializationPhysicalProofContract.RequiredRoutes.Count == 4,
            "Physical proof contract does not cover all deep routes.");
    }

    private static CareerSkillSpecializationEditorState Editor(
        long revision,
        CharacterCareerSkillSpecializationQuote quote)
        => new(
            WorkspaceId,
            revision,
            [new CareerSkillSpecializationCandidate(
                quote.Identity,
                quote.SkillName,
                quote.SkillCategory,
                quote.SkillGroup,
                quote.TotalBaseRating,
                quote.ExistingSpecializationCount,
                quote.Selection.Kind == CharacterCareerSkillSpecializationOptionKind.Custom ? [] : [Option])],
            OmittedSkillCount: 0);

    private static Sr5CareerSpecializationDraft Draft(
        CareerSkillSpecializationEditorState editor,
        CharacterCareerSkillSpecializationQuote quote)
    {
        Check(Sr5CareerSpecializationDraft.TryCreate(
            editor,
            quote,
            OwnerId,
            SpecializationId,
            ExpenseId,
            ExpenseDate,
            out Sr5CareerSpecializationDraft draft,
            out string blocker), blocker);
        return draft;
    }

    private static CharacterCareerSkillSpecializationQuote Quote(
        CharacterCareerSkillKind kind,
        Guid? sourceId,
        CharacterCareerSkillSpecializationSelection selection,
        int existing = 1,
        int karma = 20,
        string rawCharacter = "character-before")
    {
        CharacterCareerSkillIdentity identity = new(SkillId, sourceId, kind);
        IReadOnlyList<CharacterCareerSkillSpecializationOption> options =
            selection.Kind == CharacterCareerSkillSpecializationOptionKind.Custom ? [] : [Option];
        CharacterCareerSkillSpecializationInput input = new(
            identity,
            Created: true,
            Enabled: true,
            IsExoticSkill: false,
            KarmaUnlocked: true,
            AllowUpgrade: true,
            IsNativeLanguage: false,
            SkillName: kind == CharacterCareerSkillKind.Active ? "Sneaking" : "Seattle Gangs",
            SkillCategory: kind == CharacterCareerSkillKind.Active ? "Physical Active" : "Street",
            DictionaryKey: kind == CharacterCareerSkillKind.Active ? "Sneaking" : "Seattle Gangs",
            SkillGroup: kind == CharacterCareerSkillKind.Active ? "Stealth" : string.Empty,
            TotalBaseRating: 4,
            ExistingSpecializationCount: existing,
            AvailableKarma: karma,
            EnabledSkillGroupMemberCount: kind == CharacterCareerSkillKind.Active ? 2 : 0,
            SkillSpecializationsBlocked: false,
            SkillCategorySpecializationsBlocked: false,
            new CharacterCareerSkillSpecializationSettings(7, 3, true),
            Modifiers: [],
            AvailableOptions: options,
            selection,
            RawCharacterState: rawCharacter,
            RawSourceState: "source-state",
            RawRuleState: "rule-state");
        Check(CharacterCareerSkillSpecializationRules.TryCreateQuote(input, out CharacterCareerSkillSpecializationQuote quote),
            "Core quote construction failed.");
        return quote;
    }

    private sealed class MutableOwner(Guid ownerId) : ISr5CareerCheckpointOwnerAuthority
    {
        public Guid CurrentOwnerId { get; } = ownerId;
    }

    private sealed class MemoryBackend : ISr5CareerCheckpointBackend
    {
        private string _payload = string.Empty;
        public string Read() => _payload;
        public void Write(string payload) => _payload = payload;
        public void Remove() => _payload = string.Empty;
    }

    private sealed class FakePresenter(
        CareerSkillSpecializationEditorState originalEditor,
        CharacterCareerSkillSpecializationQuote originalQuote) : ISr5CareerSpecializationPresenter
    {
        private bool _applied;
        public bool ForceAppliedBinding { get; init; }

        public Sr5CareerRunnerBinding Binding
            => new(
                Created: true,
                GameEdition: "SR5",
                WorkspaceId,
                ContentRevision: _applied || ForceAppliedBinding ? originalEditor.ContentRevision + 1 : originalEditor.ContentRevision,
                SavedRevision: _applied || ForceAppliedBinding ? originalEditor.ContentRevision + 1 : originalEditor.ContentRevision,
                IsDirty: false,
                Error: null);

        public Task<CareerSkillSpecializationEditorState?> LoadAsync(CancellationToken cancellationToken)
            => Task.FromResult<CareerSkillSpecializationEditorState?>(_applied
                ? UpdatedEditor()
                : originalEditor);

        public Task<CharacterCareerSkillSpecializationQuote?> QuoteAsync(
            CareerSkillSpecializationQuoteRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<CharacterCareerSkillSpecializationQuote?>(_applied ? UpdatedQuote() : originalQuote);

        public Task<Sr5CareerSpecializationApplyObservation?> ApplyAndSaveAsync(
            CareerSkillSpecializationRequest request,
            CancellationToken cancellationToken)
        {
            _applied = true;
            CharacterCareerSkillSpecializationQuote updated = UpdatedQuote();
            CareerSkillSpecializationEditorState editor = UpdatedEditor();
            return Task.FromResult<Sr5CareerSpecializationApplyObservation?>(
                new(editor, updated, originalEditor.ContentRevision + 1));
        }

        private CharacterCareerSkillSpecializationQuote UpdatedQuote()
            => Quote(
                originalQuote.Identity.Kind,
                originalQuote.Identity.SourceSkillId,
                originalQuote.Selection,
                existing: originalQuote.ExistingSpecializationCount + 1,
                karma: originalQuote.AvailableKarma - originalQuote.KarmaCost,
                rawCharacter: "character-after");

        private CareerSkillSpecializationEditorState UpdatedEditor()
            => Editor(originalEditor.ContentRevision + 1, UpdatedQuote());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
