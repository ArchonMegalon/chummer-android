using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

internal static class Program
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("sr5-skill-group-runner");
    private static readonly Guid GroupId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime ExpenseDate =
        new(2081, 6, 3, 19, 30, 0, DateTimeKind.Unspecified);

    private static async Task Main()
    {
        Console.WriteLine("1 exact draft");
        ExactDraftBindsTypedIdentityQuotePlanAndDigests();
        Console.WriteLine("2 blockers");
        BlockedQuotesNeverBecomeDrafts();
        Console.WriteLine("3 checkpoint tampering");
        CheckpointRejectsTamperingAndPriorSchemaLocks();
        Console.WriteLine("4 atomic apply verification");
        await CoordinatorVerifiesOnlyAtomicCoreResultAsync();
        Console.WriteLine("5 idempotent restart recovery");
        await ApplyingCrashResolvesByExactCommandReplayAsync();
        Console.WriteLine("6 CAS and shared mutation owner");
        CheckpointCasRejectsForgedResolutionAndWrongOwner();
        SharedMutationOwnerBlocksCrossLaneApply();
        Console.WriteLine("SR5 Career SkillGroup authority tests passed: 7");
    }

    private static void ExactDraftBindsTypedIdentityQuotePlanAndDigests()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote();
        CareerSkillGroupAdvanceEditorState editor = Editor(41, quote);
        Require(Sr5CareerSkillGroupDraft.TryCreate(
            editor,
            quote,
            OwnerId,
            ActionId,
            ExpenseDate,
            out Sr5CareerSkillGroupDraft draft,
            out string blocker), blocker);
        Require(draft.IsExact(), "The reviewed draft must remain exact.");
        Require(draft.Quote.Identity == new CharacterCareerSkillGroupIdentity(GroupId),
            "Typed InternalId must remain exact.");
        Require(draft.Plan.Identity == draft.Quote.Identity, "Plan identity must match quote identity.");
        Require(draft.ActionPlan.Kind == Sr5CareerActionKind.SkillGroupAdvance, "Action kind must be typed.");
        Require(draft.ActionPlan.CostQuote.KarmaCost == 20, "Core quote must own Karma cost.");
        Require(draft.ActionPlan.IdempotencyKey.Length == 64, "Action must have a canonical idempotency key.");
        Require(draft.RuntimeAuthority.IsCurrent(), "Content and runtime authority must match the exact product graph.");
        Require(draft.RuntimeAuthority.ContentDigest == Sr5CareerSkillGroupRuntimeAuthority.CurrentContentDigest,
            "The verified bundled-content digest must remain bound.");
        Require(draft.RuntimeAuthority.RuntimeDigest == Sr5CareerSkillGroupRuntimeAuthority.CurrentRuntimeDigest,
            "The exact Core/Presentation/content runtime digest must remain bound.");
        CareerSkillGroupAdvanceRequest compatibility = draft.ToRequest();
        Require(compatibility.ExpectedSkillGroup == quote
            && compatibility.ExpectedRuleDigest == quote.RuleDigest,
            "The Presentation compatibility projection must retain its actual seven-field contract.");
        CharacterCareerSkillGroupAdvanceCommand command = draft.ToCommand();
        Require(command.ContractName == CharacterCareerSkillGroupAdvanceServiceSchemas.CommandV1,
            "Mutation must use the atomic Core command contract.");
        Require(command.Identity == quote.Identity
            && command.ExpectedLogicalRevision == quote.LogicalRevision
            && command.ExpectedSourceRevision == quote.SourceRevision
            && command.ExpectedRuleDigest == quote.RuleDigest
            && command.ExpectedBindingDigest == draft.Binding.BindingDigest,
            "Identity and every quote/binding revision must remain bound into the Core command.");
        Require(CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeCommandDigest(command, out _),
            "The exact Core command must have a canonical digest.");

        CharacterCareerSkillGroupAdvanceQuote forgedIdentity = quote with
        {
            Identity = new CharacterCareerSkillGroupIdentity(Guid.NewGuid())
        };
        Require(!Sr5CareerSkillGroupDraft.TryCreate(
            editor,
            forgedIdentity,
            OwnerId,
            ActionId,
            ExpenseDate,
            out _,
            out _), "Changing only the typed InternalId must fail closed.");
        Require(!Sr5CareerSkillGroupDraft.TryCreate(
            editor,
            quote with { RuleDigest = new string('0', 64) },
            OwnerId,
            ActionId,
            ExpenseDate,
            out _,
            out _), "Changing the rule digest must fail closed.");
    }

    private static void BlockedQuotesNeverBecomeDrafts()
    {
        (string Name, CharacterCareerSkillGroupAdvanceQuote Quote, CharacterCareerSkillGroupAdvanceBlocker Blocker)[] cases =
        [
            ("karma", Quote(availableKarma: 0), CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma),
            ("maximum", Quote(ratingMaximum: 3), CharacterCareerSkillGroupAdvanceBlocker.AtMaximum),
            ("members", Quote(memberProjectionIsExact: false), CharacterCareerSkillGroupAdvanceBlocker.InvalidMemberProjection),
            ("broken", Quote(broken: true), CharacterCareerSkillGroupAdvanceBlocker.Broken),
            ("disabled", Quote(disabled: true), CharacterCareerSkillGroupAdvanceBlocker.Disabled)
        ];
        foreach ((string name, CharacterCareerSkillGroupAdvanceQuote quote, CharacterCareerSkillGroupAdvanceBlocker expected) in cases)
        {
            Require(quote.Blocker == expected && !quote.CanAdvance, $"{name} quote must have the exact blocker.");
            Require(!Sr5CareerSkillGroupDraft.TryCreate(
                Editor(41, quote),
                quote,
                OwnerId,
                ActionId,
                ExpenseDate,
                out _,
                out string blocker), $"{name} blocker must prevent a draft.");
            Require(!string.IsNullOrWhiteSpace(blocker), $"{name} blocker must be explainable.");
        }
        CharacterCareerSkillGroupAdvanceQuote wrongRulesetQuote = Quote(rulesetId: "sr6");
        CareerSkillGroupAdvanceEditorState wrongRuleset = Editor(41, wrongRulesetQuote);
        Require(!Sr5CareerSkillGroupDraft.TryCreate(
            wrongRuleset,
            wrongRulesetQuote,
            OwnerId,
            ActionId,
            ExpenseDate,
            out _,
            out _), "A non-SR5 editor must never become an SR5 skill-group draft.");
    }

    private static void CheckpointRejectsTamperingAndPriorSchemaLocks()
    {
        Sr5CareerSkillGroupDraft draft = Draft();
        Sr5CareerSkillGroupCheckpoint checkpoint = Sr5CareerSkillGroupCheckpoint.FromDraft(draft);
        Require(checkpoint.IsStructurallyValid(), "Exact reviewed checkpoint must be valid.");
        Require(!(checkpoint with { IdempotencyKey = new string('0', 64) }).IsStructurallyValid(),
            "Forged idempotency must invalidate the checkpoint.");
        Require(!(checkpoint with
        {
            Draft = draft with { Plan = draft.Plan with { ExpenseAmount = draft.Plan.ExpenseAmount + 1 } }
        }).IsStructurallyValid(), "Tampered expense plan must invalidate the checkpoint.");
        Require(!(checkpoint with
        {
            Draft = draft with
            {
                RuntimeAuthority = draft.RuntimeAuthority with { ContentDigest = new string('0', 64) }
            }
        }).IsStructurallyValid(), "Tampered content authority must invalidate the checkpoint.");
        Require(!(checkpoint with
        {
            Draft = draft with
            {
                RuntimeAuthority = draft.RuntimeAuthority with { RuntimeDigest = new string('0', 64) }
            }
        }).IsStructurallyValid(), "Tampered runtime authority must invalidate the checkpoint.");

        MemoryBackend backend = new(JsonSerializer.Serialize(checkpoint with
        {
            SchemaVersion = Sr5CareerSkillGroupCheckpoint.CurrentSchemaVersion - 1
        }));
        Sr5CareerSkillGroupCheckpointStore store = new(backend);
        Require(!store.TryRead(out _, out string blocker)
            && blocker.Contains("replay-blocking", StringComparison.Ordinal),
            "Prior schema must remain an explicit replay-blocking lock.");
        Require(!store.TryCreate(checkpoint, out _, out _), "Unreadable lock must prevent overwrite/replay.");
        Require(!string.IsNullOrWhiteSpace(backend.Read()), "Unreadable lock must remain durable.");
    }

    private static async Task CoordinatorVerifiesOnlyAtomicCoreResultAsync()
    {
        Sr5CareerSkillGroupDraft draft = Draft();
        FakePresenter presenter = FakePresenter.Before(draft);
        (Sr5CareerSkillGroupCheckpointStore store, Sr5CareerSkillGroupCheckpoint applying) =
            ApplyingStore(draft, presenter);
        presenter.AdvanceHandler = command =>
        {
            presenter.PublishApplied(draft);
            return Task.FromResult<CharacterCareerSkillGroupAdvanceResult?>(
                SuccessResult(draft, command, CharacterCareerSkillGroupAdvanceServiceOutcome.Applied));
        };
        Sr5CareerSkillGroupCoordinator coordinator = new(
            presenter,
            new FixedOwner(OwnerId));
        Sr5CareerSkillGroupApplyResult result = await coordinator.ApplyAsync(
            draft,
            applying,
            store);
        Require(result.Status == Sr5CareerSkillGroupApplyStatus.Applied, result.Message);
        Require(result.Receipt is not null
            && Sr5CareerSkillGroupCoordinator.ReceiptMatchesDraft(draft, result.Receipt),
            "Receipt must match every reviewed quote and plan value.");
        Require(presenter.AdvanceCalls == 1, "The atomic Core command must execute exactly once.");

        CharacterCareerSkillGroupAdvanceReceipt exact = result.Receipt!;
        CharacterCareerSkillGroupAdvanceReceipt[] forgeries =
        [
            exact with { Identity = new CharacterCareerSkillGroupIdentity(Guid.NewGuid()) },
            exact with { TransactionId = Guid.NewGuid(), ExpenseId = Guid.NewGuid() },
            exact with { LogicalRevisionBefore = new string('0', 64) },
            exact with { SourceRevisionBefore = new string('0', 64) },
            exact with { RuleDigestBefore = new string('0', 64) },
            exact with { ReceiptDigest = new string('0', 64) }
        ];
        foreach (CharacterCareerSkillGroupAdvanceReceipt forged in forgeries)
        {
            Require(!Sr5CareerSkillGroupCoordinator.ReceiptMatchesDraft(draft, forged),
                "Every forged receipt identity or digest must fail closed.");
        }

        Sr5CareerSkillGroupCheckpoint applyingForForgery = applying with
        {
            Version = applying.Version + 2
        };
        CharacterCareerSkillGroupAdvanceResult success = SuccessResult(
            draft,
            draft.ToCommand(),
            CharacterCareerSkillGroupAdvanceServiceOutcome.Applied);
        Require(Sr5CareerSkillGroupCoordinator.ResolveServiceResult(
                applyingForForgery,
                presenter.Binding,
                success with { CommandDigest = new string('0', 64) }).Status
            == Sr5CareerSkillGroupRecoveryStatus.OutcomeUnknown,
            "A result for another command digest must fail closed.");
    }

    private static async Task ApplyingCrashResolvesByExactCommandReplayAsync()
    {
        Sr5CareerSkillGroupDraft draft = Draft();
        FakePresenter presenter = FakePresenter.Before(draft);
        (Sr5CareerSkillGroupCheckpointStore store, Sr5CareerSkillGroupCheckpoint applying) =
            ApplyingStore(draft, presenter);
        CharacterCareerSkillGroupAdvanceCommand? firstCommand = null;
        presenter.AdvanceHandler = command =>
        {
            if (presenter.AdvanceCalls == 1)
            {
                firstCommand = command;
                presenter.PublishApplied(draft);
                throw new InvalidOperationException("simulated transport loss after durable save");
            }
            Require(command == firstCommand,
                "Restart recovery must submit the byte-equivalent typed command and transaction identity.");
            return Task.FromResult<CharacterCareerSkillGroupAdvanceResult?>(
                SuccessResult(draft, command, CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed));
        };
        Sr5CareerSkillGroupCoordinator first = new(presenter, new FixedOwner(OwnerId));
        Sr5CareerSkillGroupApplyResult unresolved = await first.ApplyAsync(draft, applying, store);
        Require(unresolved.Status == Sr5CareerSkillGroupApplyStatus.OutcomeUnknown,
            "Transport loss after commit must retain an unresolved Applying checkpoint.");
        Require(presenter.AdvanceCalls == 1, "First process must submit the command exactly once.");

        Sr5CareerSkillGroupCoordinator restarted = new(presenter, new FixedOwner(OwnerId));
        Sr5CareerSkillGroupRecoveryResolution recovered = await restarted.ResolveAsync(applying, store);
        Require(recovered.Status == Sr5CareerSkillGroupRecoveryStatus.AppliedVerified,
            recovered.Message);
        Require(presenter.AdvanceCalls == 2,
            "Restart resolution must use the Core service's idempotent transaction lookup exactly once.");
        Require(store.TryRecordAuthoritativeResolution(
            Sr5CareerSkillGroupCheckpointCas.From(applying),
            recovered,
            out Sr5CareerSkillGroupCheckpoint applied,
            out string blocker), blocker);
        Require(applied.Receipt == recovered.Receipt,
            "The validated Core receipt must survive in the durable Applied checkpoint.");
    }

    private static void CheckpointCasRejectsForgedResolutionAndWrongOwner()
    {
        Sr5CareerSkillGroupDraft draft = Draft();
        MutableOwner authority = new(OwnerId);
        Sr5CareerRunnerBinding binding = FakePresenter.Before(draft).Binding;
        Sr5CareerSkillGroupLiveCheckpointAuthority liveAuthority = new(
            authority,
            Editor(draft.ExpectedContentRevision, draft.Quote),
            () => binding);
        MemoryBackend backend = new();
        Sr5CareerSkillGroupCheckpointStore store = new(backend, liveAuthority);
        Require(store.TryCreate(
            Sr5CareerSkillGroupCheckpoint.FromDraft(draft),
            out Sr5CareerSkillGroupCheckpoint reviewed,
            out string blocker), blocker);
        Require(store.TryBeginApply(
            Sr5CareerSkillGroupCheckpointCas.From(reviewed),
            out Sr5CareerSkillGroupCheckpoint applying,
            out blocker), blocker);

        CharacterCareerSkillGroupAdvanceReceipt receipt = Receipt(draft);
        Sr5CareerSkillGroupRecoveryResolution exact = Sr5CareerSkillGroupRecoveryProof.Create(
            applying,
            Sr5CareerSkillGroupRecoveryStatus.AppliedVerified,
            receipt,
            "exact");
        Require(!store.TryRecordAuthoritativeResolution(
            Sr5CareerSkillGroupCheckpointCas.From(applying),
            exact with { Message = "forged" },
            out _,
            out _), "Forged signed resolution fields must fail closed.");
        Require(!store.TryRecordAuthoritativeResolution(
            Sr5CareerSkillGroupCheckpointCas.From(applying),
            exact,
            out _,
            out _), "AppliedVerified must fail while the live runner remains at the original revision.");
        authority.OwnerId = Guid.NewGuid();
        Require(!store.TryRecordAuthoritativeResolution(
            Sr5CareerSkillGroupCheckpointCas.From(applying),
            exact,
            out _,
            out _), "A foreign owner must not finalize the checkpoint.");
        authority.OwnerId = OwnerId;
        binding = binding with
        {
            ContentRevision = checked(draft.ExpectedContentRevision + 1),
            SavedRevision = checked(draft.ExpectedContentRevision + 1)
        };
        Require(store.TryRecordAuthoritativeResolution(
            Sr5CareerSkillGroupCheckpointCas.From(applying),
            exact,
            out Sr5CareerSkillGroupCheckpoint applied,
            out blocker), blocker);
        Require(applied.Phase == Sr5CareerCheckpointPhase.Applied,
            "Exact resolution must advance Applying to Applied.");

        DeleteReadFailureBackend deleteFailure = new(JsonSerializer.Serialize(applied));
        Sr5CareerSkillGroupCheckpointStore deleteStore = new(deleteFailure, liveAuthority);
        Require(!deleteStore.TryDeleteApplied(
            Sr5CareerSkillGroupCheckpointCas.From(applied),
            receipt,
            out _), "A failed delete read-back must not claim acknowledgement.");
        Require(deleteStore.TryRead(
            out Sr5CareerSkillGroupCheckpoint restored,
            out blocker)
            && restored.Phase == Sr5CareerCheckpointPhase.Applied,
            "A failed delete read-back must restore the exact replay-blocking Applied checkpoint.");

        Require(CharacterCareerSkillGroupAdvanceRules.TryPlanCorrection(
            receipt,
            Successor(draft),
            Expense(draft),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "User-requested correction",
            correctionIdAlreadyExists: false,
            originalTransactionAlreadyCorrected: false,
            receipt.ReceiptDigest,
            out CharacterCareerSkillGroupCorrectionPlan correction),
            "Core must create one exact compensating correction.");
        Require(!store.TryDeleteCorrected(
            Sr5CareerSkillGroupCheckpointCas.From(applied),
            receipt,
            correction,
            out _), "Correction acknowledgement must fail before the clean corrected revision exists.");
        binding = binding with
        {
            ContentRevision = checked(draft.ExpectedContentRevision + 2),
            SavedRevision = checked(draft.ExpectedContentRevision + 2)
        };
        Require(store.TryDeleteCorrected(
            Sr5CareerSkillGroupCheckpointCas.From(applied),
            receipt,
            correction,
            out blocker), blocker);
    }

    private static void SharedMutationOwnerBlocksCrossLaneApply()
    {
        Sr5CareerSkillGroupDraft draft = Draft();
        Sr5CareerRunnerBinding binding = FakePresenter.Before(draft).Binding;
        Sr5CareerSkillGroupLiveCheckpointAuthority authority = new(
            new FixedOwner(OwnerId),
            Editor(draft.ExpectedContentRevision, draft.Quote),
            () => binding);
        MemoryBackend mutationBackend = new();
        Sr5CareerMutationOwnerStore owners = new(mutationBackend);
        Sr5CareerSkillGroupCheckpointStore store = new(
            new MemoryBackend(),
            authority,
            owners);
        Require(store.TryCreate(
            Sr5CareerSkillGroupCheckpoint.FromDraft(draft),
            out Sr5CareerSkillGroupCheckpoint reviewed,
            out string blocker), blocker);

        Sr5CareerMutationOwner foreign = new(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.ActiveSkillAdvance,
            draft.WorkspaceId.Value,
            draft.OwnerId,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ApplyingCheckpointVersion: 2,
            draft.ExpectedContentRevision,
            new string('a', 64));
        Require(owners.TryBegin(
            foreign,
            () => new Sr5CareerMutationBeginResult(true, false, string.Empty),
            out blocker), blocker);
        Require(!store.TryBeginApply(
            Sr5CareerSkillGroupCheckpointCas.From(reviewed),
            out _,
            out blocker)
            && blocker.Contains("already owns", StringComparison.Ordinal),
            "A durable foreign lane owner must block skill-group apply.");
        Require(owners.TryComplete(foreign, () => (true, string.Empty), out blocker), blocker);
        Require(store.TryBeginApply(
            Sr5CareerSkillGroupCheckpointCas.From(reviewed),
            out Sr5CareerSkillGroupCheckpoint applying,
            out blocker), blocker);
        Require(applying.Phase == Sr5CareerCheckpointPhase.Applying,
            "Skill-group apply may begin after the foreign owner is durably released.");
    }

    private static Sr5CareerSkillGroupDraft Draft()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote();
        Require(Sr5CareerSkillGroupDraft.TryCreate(
            Editor(41, quote),
            quote,
            OwnerId,
            ActionId,
            ExpenseDate,
            out Sr5CareerSkillGroupDraft draft,
            out string blocker), blocker);
        return draft;
    }

    private static (
        Sr5CareerSkillGroupCheckpointStore Store,
        Sr5CareerSkillGroupCheckpoint Applying) ApplyingStore(
            Sr5CareerSkillGroupDraft draft,
            FakePresenter presenter)
    {
        Sr5CareerSkillGroupLiveCheckpointAuthority liveAuthority = new(
            new FixedOwner(OwnerId),
            Editor(draft.ExpectedContentRevision, draft.Quote),
            () => presenter.Binding);
        Sr5CareerSkillGroupCheckpointStore store = new(new MemoryBackend(), liveAuthority);
        Require(store.TryCreate(
            Sr5CareerSkillGroupCheckpoint.FromDraft(draft),
            out Sr5CareerSkillGroupCheckpoint reviewed,
            out string blocker), blocker);
        Require(store.TryBeginApply(
            Sr5CareerSkillGroupCheckpointCas.From(reviewed),
            out Sr5CareerSkillGroupCheckpoint applying,
            out blocker), blocker);
        return (store, applying);
    }

    private static CharacterCareerSkillGroupAdvanceQuote Quote(
        int availableKarma = 40,
        int ratingMaximum = 6,
        bool memberProjectionIsExact = true,
        bool broken = false,
        bool disabled = false,
        string? rulesetId = null)
    {
        CharacterCareerSkillGroupAdvanceInput input = new(
            new CharacterCareerSkillGroupIdentity(GroupId),
            Created: true,
            RulesetId: rulesetId ?? CharacterCareerSkillGroupAdvanceRules.RulesetId,
            TargetOwnedByCharacter: true,
            MemberProjectionIsExact: memberProjectionIsExact,
            Name: "Stealth",
            BasePoints: 2,
            KarmaPoints: 1,
            RatingMaximum: ratingMaximum,
            AvailableKarma: availableKarma,
            Disabled: disabled,
            Broken: broken,
            Settings: new CharacterCareerSkillGroupAdvanceSettings(5, 5),
            Members:
            [
                new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 3, true, "Physical Active"),
                new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 3, true, "Physical Active")
            ],
            Modifiers: [],
            RawSourceState: "<skills><group name='Stealth' /></skills>",
            RawRuleState: "<settings karmanewskillgroup='5' karmaimproveskillgroup='5' />");
        Require(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(input, out var quote),
            "Test quote must be coherent.");
        return quote;
    }

    private static CareerSkillGroupAdvanceEditorState Editor(
        long revision,
        CharacterCareerSkillGroupAdvanceQuote quote)
        => new(
            WorkspaceId,
            revision,
            [quote],
            0);

    private static CharacterCareerSkillGroupAdvanceQuote Successor(Sr5CareerSkillGroupDraft draft)
    {
        CharacterCareerSkillGroupAdvanceInput input = new(
            draft.Quote.Identity,
            Created: true,
            RulesetId: CharacterCareerSkillGroupAdvanceRules.RulesetId,
            TargetOwnedByCharacter: true,
            MemberProjectionIsExact: true,
            Name: draft.Quote.Name,
            BasePoints: draft.Quote.BasePoints,
            KarmaPoints: draft.Plan.SavedGroupKarmaPoints,
            RatingMaximum: draft.Quote.RatingMaximum,
            AvailableKarma: draft.Plan.SavedCharacterKarma,
            Disabled: draft.Quote.Disabled,
            Broken: draft.Quote.Broken,
            Settings: new CharacterCareerSkillGroupAdvanceSettings(5, 5),
            Members:
            [
                new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), draft.Plan.TargetCostRating, true, "Physical Active"),
                new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), draft.Plan.TargetCostRating, true, "Physical Active")
            ],
            Modifiers: [],
            RawSourceState: "<skills><group name='Stealth' karma='2' /></skills>",
            RawRuleState: "<settings karmanewskillgroup='5' karmaimproveskillgroup='5' />");
        Require(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(input, out var quote),
            "Successor quote must be coherent.");
        return quote;
    }

    private static CharacterCareerSkillGroupAdvanceReceipt Receipt(Sr5CareerSkillGroupDraft draft)
    {
        CharacterCareerSkillGroupAdvanceQuote after = Successor(draft);
        CharacterCareerSkillGroupExpenseObservation expense = Expense(draft);
        Require(CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
            draft.Plan.ExpenseId,
            draft.Quote,
            draft.Plan,
            after,
            expense,
            out CharacterCareerSkillGroupAdvanceReceipt receipt),
            "Test receipt must be coherent.");
        return receipt;
    }

    private static CharacterCareerSkillGroupAdvanceResult SuccessResult(
        Sr5CareerSkillGroupDraft draft,
        CharacterCareerSkillGroupAdvanceCommand command,
        CharacterCareerSkillGroupAdvanceServiceOutcome outcome)
    {
        Require(CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeCommandDigest(
            command,
            out string commandDigest), "Test command must have a canonical digest.");
        CharacterCareerSkillGroupAdvanceResult unsigned = new(
            CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
            outcome,
            draft.WorkspaceId,
            draft.ExpectedContentRevision,
            checked(draft.ExpectedContentRevision + 1),
            draft.Quote.Identity,
            draft.Plan.TransactionId,
            commandDigest,
            draft.Quote,
            Receipt(draft),
            [],
            string.Empty);
        Require(CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeResultDigest(
            unsigned,
            out string resultDigest), "Test Core result must have a canonical digest.");
        return unsigned with { ResultDigest = resultDigest };
    }

    private static CharacterCareerSkillGroupExpenseObservation Expense(
        Sr5CareerSkillGroupDraft draft)
        => new(
            MatchingEntryCount: 1,
            draft.Plan.ExpenseId,
            draft.Plan.ExpenseDateLocal,
            draft.Plan.ExpenseAmount,
            draft.Plan.ExpenseReason,
            ExpenseType: "Karma",
            Refund: false,
            ForceCareerVisible: true,
            draft.Plan.KarmaUndoType,
            draft.Plan.NuyenUndoType,
            draft.Plan.UndoObjectId,
            draft.Plan.UndoQuantity,
            draft.Plan.UndoExtra);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task RequireThrowsAsync<TException>(
        Func<Task> action,
        string message) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private sealed record FixedOwner(Guid CurrentOwnerId) :
        ISr5CareerCheckpointOwnerAuthority;

    private sealed class MutableOwner(Guid ownerId) :
        ISr5CareerCheckpointOwnerAuthority
    {
        public Guid OwnerId { get; set; } = ownerId;
        public Guid CurrentOwnerId => OwnerId;
    }

    private sealed class MemoryBackend(string payload = "") :
        ISr5CareerCheckpointBackend
    {
        private string _payload = payload;
        public string Read() => _payload;
        public void Write(string value) => _payload = value;
        public void Remove() => _payload = string.Empty;
    }

    private sealed class DeleteReadFailureBackend(string payload) :
        ISr5CareerCheckpointBackend
    {
        private string _payload = payload;
        private bool _failNextRead;

        public string Read()
        {
            if (_failNextRead)
            {
                _failNextRead = false;
                throw new IOException("simulated delete read-back failure");
            }
            return _payload;
        }

        public void Write(string value)
        {
            _payload = value;
            _failNextRead = false;
        }

        public void Remove()
        {
            _payload = string.Empty;
            _failNextRead = true;
        }
    }

    private sealed class FakePresenter : ISr5CareerSkillGroupPresenter
    {
        public required Sr5CareerRunnerBinding BindingValue { get; set; }
        public required CareerSkillGroupAdvanceEditorState Editor { get; set; }
        public Func<CharacterCareerSkillGroupAdvanceCommand,
            Task<CharacterCareerSkillGroupAdvanceResult?>>? AdvanceHandler { get; set; }
        public int AdvanceCalls { get; private set; }
        public Sr5CareerRunnerBinding Binding => BindingValue;

        public static FakePresenter Before(Sr5CareerSkillGroupDraft draft)
            => new()
            {
                BindingValue = new(
                    Created: true,
                    GameEdition: "SR5",
                    draft.WorkspaceId,
                    draft.ExpectedContentRevision,
                    draft.ExpectedContentRevision,
                    IsDirty: false,
                    Error: null),
                Editor = Program.Editor(draft.ExpectedContentRevision, draft.Quote)
            };

        public Task<CareerSkillGroupAdvanceEditorState?> LoadSkillGroupsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(Editor);

        public Task<CharacterCareerSkillGroupAdvanceResult?> AdvanceAsync(
            CharacterCareerSkillGroupAdvanceCommand command,
            CancellationToken cancellationToken)
        {
            AdvanceCalls++;
            return AdvanceHandler?.Invoke(command)
                ?? Task.FromResult<CharacterCareerSkillGroupAdvanceResult?>(null);
        }

        public void PublishApplied(Sr5CareerSkillGroupDraft draft)
        {
            CharacterCareerSkillGroupAdvanceReceipt receipt = Program.Receipt(draft);
            BindingValue = BindingValue with
            {
                ContentRevision = checked(draft.ExpectedContentRevision + 1),
                SavedRevision = checked(draft.ExpectedContentRevision + 1),
                IsDirty = false,
                Error = null
            };
            Editor = Program.Editor(BindingValue.ContentRevision, Program.Successor(draft));
        }
    }
}
