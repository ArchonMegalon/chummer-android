using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using System.Text.Json;

return KnowledgeSkillAuthorityHarness.Run();

internal static class KnowledgeSkillAuthorityHarness
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("runner-alpha");
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SkillId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ActionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTime ExpenseDate = new(2080, 5, 17, 20, 30, 0, DateTimeKind.Unspecified);

    public static int Run()
    {
        (string Name, Action Test)[] tests =
        [
            (nameof(DraftKeepsKnowledgeIdentityAndAllCasDimensions), DraftKeepsKnowledgeIdentityAndAllCasDimensions),
            (nameof(ActionKindNumericIdentityIsStable), ActionKindNumericIdentityIsStable),
            (nameof(NativeLanguageCannotCreateReviewedDraft), NativeLanguageCannotCreateReviewedDraft),
            (nameof(DurableCheckpointCasMovesReviewedApplyingApplied), DurableCheckpointCasMovesReviewedApplyingApplied),
            (nameof(DurableOwnerSurvivesRestartAndReconcilesResolvedJournal), DurableOwnerSurvivesRestartAndReconcilesResolvedJournal),
            (nameof(LegacyApplyingWithoutSharedOwnerFailsClosed), LegacyApplyingWithoutSharedOwnerFailsClosed),
            (nameof(MalformedSharedOwnerRemainsReplayBlocking), MalformedSharedOwnerRemainsReplayBlocking),
            (nameof(RecoveryDistinguishesAppliedNotAppliedAndUnknown), RecoveryDistinguishesAppliedNotAppliedAndUnknown),
            (nameof(MalformedCheckpointRemainsReplayBlocking), MalformedCheckpointRemainsReplayBlocking)
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

        Console.WriteLine($"{tests.Length - failed}/{tests.Length} managed Knowledge/Language authority tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void ActionKindNumericIdentityIsStable()
    {
        Check((int)Sr5CareerActionKind.ActiveSkillAdvance == 0, "ActiveSkill persisted ordinal changed.");
        Check((int)Sr5CareerActionKind.AttributeAdvance == 1, "Attribute persisted ordinal changed.");
        Check((int)Sr5CareerActionKind.SkillGroupAdvance == 2, "SkillGroup persisted ordinal changed.");
        Check((int)Sr5CareerActionKind.QualityTransaction == 3, "Quality persisted ordinal changed.");
        Check((int)Sr5CareerActionKind.KnowledgeSkillAdvance == 4, "Knowledge must append at ordinal 4.");
        string payload = JsonSerializer.Serialize(Sr5CareerActionKind.KnowledgeSkillAdvance);
        Check(payload == "4", "Default durable JSON must encode Knowledge as numeric value 4.");
        Check(JsonSerializer.Deserialize<Sr5CareerActionKind>(payload)
                == Sr5CareerActionKind.KnowledgeSkillAdvance,
            "Knowledge numeric JSON did not round-trip.");
    }

    private static void DraftKeepsKnowledgeIdentityAndAllCasDimensions()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(sourceBacked: false);
        CareerKnowledgeSkillAdvanceEditorState editor = Editor(41, [quote]);

        Check(Sr5CareerKnowledgeSkillDraft.TryCreate(
            editor,
            quote,
            OwnerId,
            ActionId,
            ExpenseDate,
            out Sr5CareerKnowledgeSkillDraft draft,
            out string blocker), blocker);

        Check(draft.Quote.Identity.SourceSkillId is null, "Custom knowledge identity gained a fake source GUID.");
        Check(draft.ActionPlan.Kind == Sr5CareerActionKind.KnowledgeSkillAdvance, "Wrong action kind.");
        Check(draft.ActionPlan.RouteId == Sr5CareerWizardRoutes.KnowledgeSkillReview, "Wrong review route.");
        Check(draft.ActionPlan.DomainIdentity == $"{SkillId:D}:custom", "Custom identity was not explicit.");
        CareerKnowledgeSkillAdvanceRequest request = draft.ToRequest();
        Check(request.ExpectedCharacterRevision == quote.CharacterRevision, "Character revision CAS was dropped.");
        Check(request.ExpectedLogicalRevision == quote.LogicalRevision, "Logical revision CAS was dropped.");
        Check(request.ExpectedSourceRevision == quote.SourceRevision, "Source revision CAS was dropped.");
        Check(request.ExpectedRuleDigest == quote.RuleDigest, "Rule digest CAS was dropped.");
        Check(draft.ActionPlan.IdempotencyKey.Length == 64, "Idempotency key is not SHA-256 shaped.");
    }

    private static void NativeLanguageCannotCreateReviewedDraft()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(
            sourceBacked: true,
            skillType: "Language",
            nativeLanguage: true);
        Check(!quote.CanAdvance, "Native language quote unexpectedly advances.");
        Check(quote.Blocker == CharacterCareerKnowledgeSkillAdvanceBlocker.NativeLanguage,
            "Native language did not retain its specialized blocker.");

        bool created = Sr5CareerKnowledgeSkillDraft.TryCreate(
            Editor(41, [quote]),
            quote,
            OwnerId,
            ActionId,
            ExpenseDate,
            out _,
            out string blocker);
        Check(!created, "Native language produced a reviewed draft.");
        Check(blocker.Contains("native language", StringComparison.OrdinalIgnoreCase),
            "Native-language blocker text was lost.");
    }

    private static void DurableCheckpointCasMovesReviewedApplyingApplied()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(sourceBacked: true);
        Sr5CareerKnowledgeSkillDraft draft = Draft(quote);
        Sr5CareerKnowledgeSkillCheckpoint reviewed = Sr5CareerKnowledgeSkillCheckpoint.FromDraft(draft);
        MemoryBackend backend = new();
        MutableAuthority authority = new(OwnerId);
        Sr5CareerKnowledgeSkillCheckpointStore store = new(backend, authority);

        Check(store.TryCreate(reviewed, out Sr5CareerKnowledgeSkillCheckpoint stored, out string blocker), blocker);
        Sr5CareerKnowledgeSkillCheckpointCas reviewedCas = Sr5CareerKnowledgeSkillCheckpointCas.From(stored);
        Check(store.TryBeginApply(reviewedCas, out Sr5CareerKnowledgeSkillCheckpoint applying, out blocker), blocker);
        Check(applying.Version == 2 && applying.Phase == Sr5CareerCheckpointPhase.Applying,
            "Reviewed-to-Applying CAS did not advance exactly once.");
        Check(!store.TryBeginApply(reviewedCas, out _, out _), "Stale Reviewed CAS replay succeeded.");

        CharacterCareerKnowledgeSkillAdvanceReceipt receipt = Receipt(draft);
        authority.AllowedResolution = Sr5CareerKnowledgeSkillRecoveryStatus.AppliedVerified;
        Sr5CareerKnowledgeSkillRecoveryResolution resolution = Sr5CareerKnowledgeSkillRecoveryProof.Create(
            applying,
            Sr5CareerKnowledgeSkillRecoveryStatus.AppliedVerified,
            receipt,
            "Verified by the fresh saved projection.");
        Check(store.TryRecordAuthoritativeResolution(
            Sr5CareerKnowledgeSkillCheckpointCas.From(applying),
            resolution,
            out Sr5CareerKnowledgeSkillCheckpoint applied,
            out blocker), blocker);
        Check(applied.Version == 3 && applied.Phase == Sr5CareerCheckpointPhase.Applied,
            "Applying-to-Applied CAS did not advance exactly once.");
        Check(store.TryDeleteApplied(
            Sr5CareerKnowledgeSkillCheckpointCas.From(applied),
            receipt,
            out blocker), blocker);
        Check(string.IsNullOrEmpty(backend.Read()), "Acknowledged receipt left a durable replay lock.");
    }

    private static void DurableOwnerSurvivesRestartAndReconcilesResolvedJournal()
    {
        Sr5CareerKnowledgeSkillDraft draft = Draft(Quote(sourceBacked: true));
        Sr5CareerKnowledgeSkillCheckpoint reviewed = Sr5CareerKnowledgeSkillCheckpoint.FromDraft(draft);
        MemoryBackend checkpointBackend = new();
        MemoryBackend ownerBackend = new();
        MutableAuthority authority = new(OwnerId);
        Sr5CareerMutationOwnerStore owners = new(ownerBackend);
        Sr5CareerKnowledgeSkillCheckpointStore store = new(
            checkpointBackend,
            authority,
            owners);

        Check(store.TryCreate(reviewed, out Sr5CareerKnowledgeSkillCheckpoint stored, out string blocker), blocker);
        Check(store.TryBeginApply(
            Sr5CareerKnowledgeSkillCheckpointCas.From(stored),
            out Sr5CareerKnowledgeSkillCheckpoint applying,
            out blocker), blocker);
        Check(!string.IsNullOrWhiteSpace(ownerBackend.Payload),
            "Knowledge must reserve the durable shared owner before Applying is observable.");
        Sr5CareerMutationOwner persistedOwner =
            JsonSerializer.Deserialize<Sr5CareerMutationOwner>(ownerBackend.Payload)
            ?? throw new InvalidOperationException("Shared Knowledge owner did not deserialize.");
        Check(persistedOwner.Domain == Sr5CareerMutationDomains.KnowledgeSkillAdvance,
            "Knowledge used the wrong shared mutation domain.");
        Check(persistedOwner.ApplyingCheckpointVersion == applying.Version,
            "Shared owner lost the exact Applying version.");

        Sr5CareerMutationOwnerStore restartedOwners = new(ownerBackend);
        Sr5CareerKnowledgeSkillCheckpointStore restartedStore = new(
            checkpointBackend,
            authority,
            restartedOwners);
        Sr5CareerMutationOwner otherLane = new(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.AttributeAdvance,
            WorkspaceId.Value,
            OwnerId,
            Guid.Parse("88888888-8888-4888-8888-888888888888"),
            ApplyingCheckpointVersion: 2,
            ExpectedContentRevision: 41,
            new string('8', 64));
        Check(!restartedOwners.TryBegin(
                otherLane,
                () => new Sr5CareerMutationBeginResult(true, false, string.Empty),
                out string otherLaneBlocker)
            && otherLaneBlocker.Contains("knowledge-skill-advance", StringComparison.Ordinal),
            "A restarted process did not block another Career lane behind Knowledge.");

        using (restartedStore.AcquireDurableApplyingLeaseAsync(
            applying,
            CancellationToken.None).GetAwaiter().GetResult())
        {
            Check(true, "Restarted store acquired the exact Knowledge execution lease.");
        }

        Sr5CareerKnowledgeSkillRecoveryResolution unknown =
            Sr5CareerKnowledgeSkillRecoveryProof.Create(
                applying,
                Sr5CareerKnowledgeSkillRecoveryStatus.OutcomeUnknown,
                receipt: null,
                "Fresh authority is still ambiguous.");
        authority.AllowedResolution = Sr5CareerKnowledgeSkillRecoveryStatus.OutcomeUnknown;
        Check(!restartedStore.TryRecordAuthoritativeResolution(
                Sr5CareerKnowledgeSkillCheckpointCas.From(applying),
                unknown,
                out _,
                out _),
            "An unknown outcome advanced the Knowledge journal.");
        Check(!string.IsNullOrWhiteSpace(ownerBackend.Payload),
            "Unknown outcome released the shared Knowledge owner.");

        Sr5CareerKnowledgeSkillRecoveryResolution notApplied =
            Sr5CareerKnowledgeSkillRecoveryProof.Create(
                applying,
                Sr5CareerKnowledgeSkillRecoveryStatus.NotAppliedVerified,
                receipt: null,
                "Fresh authority proves no mutation or receipt was saved.");
        authority.AllowedResolution = Sr5CareerKnowledgeSkillRecoveryStatus.NotAppliedVerified;
        ownerBackend.FailNextRemove = true;
        Check(!restartedStore.TryRecordAuthoritativeResolution(
                Sr5CareerKnowledgeSkillCheckpointCas.From(applying),
                notApplied,
                out _,
                out string releaseBlocker)
            && releaseBlocker.Contains("domain outcome is durable", StringComparison.OrdinalIgnoreCase),
            "Interrupted owner release did not report the already-durable Knowledge outcome.");
        Check(!string.IsNullOrWhiteSpace(ownerBackend.Payload),
            "Interrupted release silently discarded the shared owner.");

        Sr5CareerMutationOwnerStore reconciledOwners = new(ownerBackend);
        Sr5CareerKnowledgeSkillCheckpointStore reconciledStore = new(
            checkpointBackend,
            authority,
            reconciledOwners);
        Check(reconciledStore.TryRead(
            out Sr5CareerKnowledgeSkillCheckpoint resolved,
            out blocker), blocker);
        Check(resolved.Phase == Sr5CareerCheckpointPhase.Reviewed && resolved.Version == 3,
            "Authoritative NotApplied journal did not survive interrupted owner release.");
        Check(string.IsNullOrWhiteSpace(ownerBackend.Payload),
            "Restart reconciliation did not retire the exact resolved Knowledge owner.");
        Check(reconciledOwners.TryBegin(
            otherLane,
            () => new Sr5CareerMutationBeginResult(true, false, string.Empty),
            out blocker), blocker);
        Check(reconciledOwners.TryComplete(
            otherLane,
            () => (true, string.Empty),
            out blocker), blocker);
    }

    private static void LegacyApplyingWithoutSharedOwnerFailsClosed()
    {
        Sr5CareerKnowledgeSkillCheckpoint applying =
            Sr5CareerKnowledgeSkillCheckpoint.FromDraft(Draft(Quote(sourceBacked: true))) with
            {
                Version = 2,
                Phase = Sr5CareerCheckpointPhase.Applying
            };
        MemoryBackend checkpointBackend = new()
        {
            Payload = JsonSerializer.Serialize(applying)
        };
        Sr5CareerKnowledgeSkillCheckpointStore store = new(
            checkpointBackend,
            new MutableAuthority(OwnerId),
            new Sr5CareerMutationOwnerStore(new MemoryBackend()));
        bool failedClosed = false;
        try
        {
            store.AcquireDurableApplyingLeaseAsync(applying, CancellationToken.None)
                .GetAwaiter().GetResult().Dispose();
        }
        catch (InvalidOperationException)
        {
            failedClosed = true;
        }
        Check(failedClosed,
            "A legacy Knowledge Applying checkpoint without a shared owner was replayable.");
        Check(checkpointBackend.Payload == JsonSerializer.Serialize(applying),
            "Legacy Applying checkpoint was silently changed or cleared.");
    }

    private static void MalformedSharedOwnerRemainsReplayBlocking()
    {
        Sr5CareerKnowledgeSkillCheckpoint reviewed =
            Sr5CareerKnowledgeSkillCheckpoint.FromDraft(Draft(Quote(sourceBacked: true)));
        MemoryBackend checkpointBackend = new();
        MemoryBackend ownerBackend = new() { Payload = "{not-json" };
        Sr5CareerKnowledgeSkillCheckpointStore store = new(
            checkpointBackend,
            new MutableAuthority(OwnerId),
            new Sr5CareerMutationOwnerStore(ownerBackend));
        Check(store.TryCreate(reviewed, out Sr5CareerKnowledgeSkillCheckpoint stored, out string blocker), blocker);
        Check(!store.TryBeginApply(
                Sr5CareerKnowledgeSkillCheckpointCas.From(stored),
                out _,
                out blocker)
            && blocker.Contains("replay-blocking", StringComparison.OrdinalIgnoreCase),
            "Malformed shared owner did not block Knowledge apply.");
        Check(ownerBackend.Payload == "{not-json",
            "Malformed shared owner was silently deleted.");
        Check(checkpointBackend.Payload == JsonSerializer.Serialize(stored),
            "Malformed shared owner changed the Reviewed Knowledge journal.");
    }

    private static void RecoveryDistinguishesAppliedNotAppliedAndUnknown()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(sourceBacked: true);
        Sr5CareerKnowledgeSkillDraft draft = Draft(quote);
        Sr5CareerKnowledgeSkillCheckpoint applying = Sr5CareerKnowledgeSkillCheckpoint.FromDraft(draft) with
        {
            Version = 2,
            Phase = Sr5CareerCheckpointPhase.Applying
        };
        CharacterCareerKnowledgeSkillAdvanceReceipt receipt = Receipt(draft);

        Sr5CareerKnowledgeSkillRecoveryResolution applied = Sr5CareerKnowledgeSkillCoordinator.Resolve(
            applying,
            Binding(42),
            Editor(42, [Quote(sourceBacked: true, rating: 4, karmaPoints: 3, availableKarma: 46,
                rawCharacterState: "runner-after")], [receipt]));
        Check(applied.Status == Sr5CareerKnowledgeSkillRecoveryStatus.AppliedVerified,
            "Exact post-save receipt was not recognized.");

        Sr5CareerKnowledgeSkillRecoveryResolution notApplied = Sr5CareerKnowledgeSkillCoordinator.Resolve(
            applying,
            Binding(41),
            Editor(41, [quote]));
        Check(notApplied.Status == Sr5CareerKnowledgeSkillRecoveryStatus.NotAppliedVerified,
            "Exact unchanged quote was not recognized.");

        Sr5CareerKnowledgeSkillRecoveryResolution unknown = Sr5CareerKnowledgeSkillCoordinator.Resolve(
            applying,
            Binding(42),
            Editor(42, [Quote(sourceBacked: true, rating: 4, karmaPoints: 3, availableKarma: 46,
                rawCharacterState: "runner-after")]));
        Check(unknown.Status == Sr5CareerKnowledgeSkillRecoveryStatus.OutcomeUnknown,
            "Missing receipt at the advanced revision did not fail closed.");
    }

    private static void MalformedCheckpointRemainsReplayBlocking()
    {
        MemoryBackend backend = new() { Payload = "{not-json" };
        MutableAuthority authority = new(OwnerId);
        Sr5CareerKnowledgeSkillCheckpointStore store = new(backend, authority);
        Check(!store.TryRead(out _, out string blocker), "Malformed checkpoint parsed successfully.");
        Check(blocker.Contains("replay-blocking", StringComparison.Ordinal),
            "Malformed payload was not described as replay-blocking.");
        Check(backend.Read() == "{not-json", "Malformed durable payload was silently cleared.");
    }

    private static CharacterCareerKnowledgeSkillAdvanceQuote Quote(
        bool sourceBacked,
        string skillType = "Academic",
        bool nativeLanguage = false,
        int rating = 3,
        int karmaPoints = 2,
        int availableKarma = 50,
        string rawCharacterState = "runner-before")
    {
        CharacterCareerKnowledgeSkillAdvanceInput input = new(
            new CharacterCareerKnowledgeSkillIdentity(SkillId, sourceBacked ? SourceId : null),
            Created: true,
            CharacterCareerKnowledgeSkillAdvanceRules.RulesetId,
            IsKnowledgeSkill: true,
            AllowUpgrade: true,
            IsNativeLanguage: nativeLanguage,
            Name: skillType == "Language" ? "Sperethiel" : "Matrix Security",
            SkillType: skillType,
            SkillCategory: skillType == "Language" ? "Language" : "Academic",
            DictionaryKey: skillType == "Language" ? "Sperethiel" : "Matrix Security",
            BasePoints: 1,
            KarmaPoints: karmaPoints,
            TotalBaseRating: rating,
            RatingMaximum: 12,
            AvailableKarma: availableKarma,
            new CharacterCareerKnowledgeSkillAdvanceSettings(
                KarmaNewKnowledgeSkill: 2,
                KarmaImproveKnowledgeSkill: 1),
            Array.Empty<CharacterCareerKnowledgeSkillKarmaModifier>(),
            RawCharacterState: rawCharacterState,
            RawSourceState: sourceBacked ? "source-backed-skill" : "custom-skill",
            RawRuleState: "sr5-knowledge-settings");
        Check(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(input, out CharacterCareerKnowledgeSkillAdvanceQuote quote),
            "Core refused the test Knowledge/Language input.");
        return quote;
    }

    private static Sr5CareerKnowledgeSkillDraft Draft(CharacterCareerKnowledgeSkillAdvanceQuote quote)
    {
        Check(Sr5CareerKnowledgeSkillDraft.TryCreate(
            Editor(41, [quote]), quote, OwnerId, ActionId, ExpenseDate,
            out Sr5CareerKnowledgeSkillDraft draft, out string blocker), blocker);
        return draft;
    }

    private static CharacterCareerKnowledgeSkillAdvanceReceipt Receipt(Sr5CareerKnowledgeSkillDraft draft)
    {
        Check(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateReceipt(
            draft.Plan.ExpenseId,
            draft.Quote,
            draft.Plan,
            draft.Plan.SavedSkillKarmaPoints,
            draft.Plan.SavedCharacterKarma,
            expenseExistsExactlyOnce: true,
            out CharacterCareerKnowledgeSkillAdvanceReceipt receipt),
            "Core refused the exact observed receipt.");
        return receipt;
    }

    private static CareerKnowledgeSkillAdvanceEditorState Editor(
        long revision,
        IReadOnlyList<CharacterCareerKnowledgeSkillAdvanceQuote> skills,
        IReadOnlyList<CharacterCareerKnowledgeSkillAdvanceReceipt>? receipts = null)
        => new(WorkspaceId, revision, skills, 0, receipts ?? [], 0);

    private static Sr5CareerRunnerBinding Binding(long revision)
        => new(true, "sr5", WorkspaceId, revision, revision, false, null);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Assertion failed." : message);
        }
    }

    private sealed class MemoryBackend : ISr5CareerCheckpointBackend
    {
        public string Payload { get; set; } = string.Empty;
        public bool FailNextRemove { get; set; }
        public string Read() => Payload;
        public void Write(string payload) => Payload = payload;
        public void Remove()
        {
            if (FailNextRemove)
            {
                FailNextRemove = false;
                throw new IOException("simulated interrupted durable owner release");
            }
            Payload = string.Empty;
        }
    }

    private sealed class MutableAuthority(Guid currentOwnerId) : ISr5CareerKnowledgeSkillCheckpointAuthority
    {
        public Guid CurrentOwnerId { get; } = currentOwnerId;
        public Sr5CareerKnowledgeSkillRecoveryStatus? AllowedResolution { get; set; }

        public bool OwnsReviewed(Sr5CareerKnowledgeSkillCheckpoint checkpoint)
            => checkpoint.Draft.OwnerId == CurrentOwnerId
                && checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed;

        public bool OwnsCurrentRunner(Sr5CareerKnowledgeSkillCheckpoint checkpoint)
            => checkpoint.Draft.OwnerId == CurrentOwnerId
                && checkpoint.Phase is Sr5CareerCheckpointPhase.Reviewed
                    or Sr5CareerCheckpointPhase.Applying
                    or Sr5CareerCheckpointPhase.Applied;

        public bool OwnsResolution(
            Sr5CareerKnowledgeSkillCheckpoint checkpoint,
            Sr5CareerKnowledgeSkillRecoveryStatus status)
            => checkpoint.Draft.OwnerId == CurrentOwnerId
                && checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
                && AllowedResolution == status;
    }
}
