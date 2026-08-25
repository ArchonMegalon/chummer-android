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
        Console.WriteLine("4 apply verification");
        await CoordinatorVerifiesOnlyFreshExactReceiptAsync();
        Console.WriteLine("5 restart recovery");
        await ApplyingCrashResolvesWithoutReplayAsync();
        Console.WriteLine("6 CAS");
        CheckpointCasRejectsForgedResolutionAndWrongOwner();
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
        Require(draft.ToRequest().ExpectedLogicalRevision == quote.LogicalRevision, "Logical revision must remain bound.");
        Require(draft.ToRequest().ExpectedSourceRevision == quote.SourceRevision, "Source revision must remain bound.");
        Require(draft.ToRequest().ExpectedRuleDigest == quote.RuleDigest, "Rule digest must remain bound.");

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

    private static async Task CoordinatorVerifiesOnlyFreshExactReceiptAsync()
    {
        Sr5CareerSkillGroupDraft draft = Draft();
        FakePresenter presenter = FakePresenter.Before(draft);
        (Sr5CareerSkillGroupCheckpointStore store, Sr5CareerSkillGroupCheckpoint applying) =
            ApplyingStore(draft, presenter);
        presenter.ApplyHandler = _ =>
        {
            presenter.PublishApplied(draft);
            return Task.FromResult(true);
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
        Require(presenter.ApplyCalls == 1, "Apply must execute exactly once.");

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

        FakePresenter partial = FakePresenter.Before(draft);
        partial.BindingValue = partial.BindingValue with
        {
            ContentRevision = 42,
            SavedRevision = 42
        };
        partial.Editor = Editor(42, Successor(draft), recoverable: []);
        Sr5CareerSkillGroupRecoveryResolution partialResolution =
            await new Sr5CareerSkillGroupCoordinator(partial, new FixedOwner(OwnerId))
                .ResolveAsync(applying);
        Require(partialResolution.Status == Sr5CareerSkillGroupRecoveryStatus.OutcomeUnknown,
            "A changed attribute without the exact recoverable receipt must be outcome-unknown.");
    }

    private static async Task ApplyingCrashResolvesWithoutReplayAsync()
    {
        Sr5CareerSkillGroupDraft draft = Draft();
        FakePresenter presenter = FakePresenter.Before(draft);
        (Sr5CareerSkillGroupCheckpointStore store, Sr5CareerSkillGroupCheckpoint applying) =
            ApplyingStore(draft, presenter);
        presenter.ApplyHandler = _ =>
        {
            presenter.PublishApplied(draft);
            throw new InvalidOperationException("simulated process death after durable save");
        };
        Sr5CareerSkillGroupCoordinator first = new(presenter, new FixedOwner(OwnerId));
        await RequireThrowsAsync<InvalidOperationException>(
            () => first.ApplyAsync(draft, applying, store),
            "The simulated crash must escape with the Applying checkpoint intact.");
        Require(presenter.ApplyCalls == 1, "First process must call apply exactly once.");

        Sr5CareerSkillGroupCoordinator restarted = new(presenter, new FixedOwner(OwnerId));
        Sr5CareerSkillGroupRecoveryResolution recovered = await restarted.ResolveAsync(applying);
        Require(recovered.Status == Sr5CareerSkillGroupRecoveryStatus.AppliedVerified,
            recovered.Message);
        Require(presenter.ApplyCalls == 1, "Restart resolution must never replay the mutation.");
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
        bool disabled = false)
    {
        CharacterCareerSkillGroupAdvanceInput input = new(
            new CharacterCareerSkillGroupIdentity(GroupId),
            Created: true,
            RulesetId: CharacterCareerSkillGroupAdvanceRules.RulesetId,
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
        CharacterCareerSkillGroupAdvanceQuote quote,
        IReadOnlyList<CharacterCareerSkillGroupAdvanceReceipt>? recoverable = null)
        => new(
            WorkspaceId,
            revision,
            [quote],
            OmittedSkillGroupCount: 0,
            recoverable ?? [],
            OmittedReceiptCount: 0);

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
        public Func<CareerSkillGroupAdvanceRequest, Task<bool>>? ApplyHandler { get; set; }
        public int ApplyCalls { get; private set; }
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

        public Task<bool> ApplyAndSaveAsync(
            CareerSkillGroupAdvanceRequest request,
            CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return ApplyHandler?.Invoke(request) ?? Task.FromResult(false);
        }

        public Task<CharacterCareerSkillGroupCorrectionPlan?> CorrectAndSaveAsync(
            CareerSkillGroupCorrectionRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<CharacterCareerSkillGroupCorrectionPlan?>(null);

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
            Editor = Program.Editor(
                BindingValue.ContentRevision,
                Program.Successor(draft),
                [receipt]);
        }
    }
}
