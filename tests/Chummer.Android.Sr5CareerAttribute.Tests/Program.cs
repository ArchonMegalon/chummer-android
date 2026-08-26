using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

internal static class Program
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("sr5-attribute-runner");
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime ExpenseDate =
        new(2081, 6, 3, 19, 30, 0, DateTimeKind.Unspecified);

    private static async Task Main()
    {
        Run(nameof(ExactDraftBindsTypedIdentityQuotePlanAndDigests),
            ExactDraftBindsTypedIdentityQuotePlanAndDigests);
        Run(nameof(BlockedQuotesNeverBecomeDrafts), BlockedQuotesNeverBecomeDrafts);
        Run(nameof(CheckpointRejectsTamperingAndPriorSchemaLocks),
            CheckpointRejectsTamperingAndPriorSchemaLocks);
        await RunAsync(nameof(CoordinatorVerifiesOnlyFreshExactReceiptAsync),
            CoordinatorVerifiesOnlyFreshExactReceiptAsync);
        await RunAsync(nameof(ApplyingCrashResolvesWithoutReplayAsync),
            ApplyingCrashResolvesWithoutReplayAsync);
        Run(nameof(CheckpointCasRejectsForgedResolutionAndWrongOwner),
            CheckpointCasRejectsForgedResolutionAndWrongOwner);
        Console.WriteLine("SR5 Career Attribute authority tests passed: 6");
    }

    private static void Run(string name, Action test)
    {
        Console.Error.WriteLine($"START {name}");
        test();
        Console.Error.WriteLine($"PASS  {name}");
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        Console.Error.WriteLine($"START {name}");
        await test().WaitAsync(TimeSpan.FromSeconds(10));
        Console.Error.WriteLine($"PASS  {name}");
    }

    private static void ExactDraftBindsTypedIdentityQuotePlanAndDigests()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote();
        CareerAttributeAdvanceEditorState editor = Editor(41, quote);
        Require(Sr5CareerAttributeDraft.TryCreate(
            editor,
            quote,
            OwnerId,
            ActionId,
            ExpenseDate,
            out Sr5CareerAttributeDraft draft,
            out string blocker), blocker);
        Require(draft.IsExact(), "The reviewed draft must remain exact.");
        Require(draft.Quote.Identity == new CharacterCareerAttributeIdentity(
            "BOD",
            CharacterCareerAttributeKind.Normal), "Typed identity must remain exact.");
        Require(draft.Plan.Identity == draft.Quote.Identity, "Plan identity must match quote identity.");
        Require(draft.ActionPlan.Kind == Sr5CareerActionKind.AttributeAdvance, "Action kind must be typed.");
        Require(draft.ActionPlan.CostQuote.KarmaCost == 15, "Core quote must own Karma cost.");
        Require(draft.ActionPlan.IdempotencyKey.Length == 64, "Action must have a canonical idempotency key.");
        Require(draft.ToRequest().ExpectedLogicalRevision == quote.LogicalRevision, "Logical revision must remain bound.");
        Require(draft.ToRequest().ExpectedSourceRevision == quote.SourceRevision, "Source revision must remain bound.");
        Require(draft.ToRequest().ExpectedRuleDigest == quote.RuleDigest, "Rule digest must remain bound.");

        CharacterCareerAttributeAdvanceQuote forgedKind = quote with
        {
            Identity = quote.Identity with { Kind = CharacterCareerAttributeKind.Edge }
        };
        Require(!Sr5CareerAttributeDraft.TryCreate(
            editor,
            forgedKind,
            OwnerId,
            ActionId,
            ExpenseDate,
            out _,
            out _), "Changing only the typed kind must fail closed.");
        Require(!Sr5CareerAttributeDraft.TryCreate(
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
        (string Name, CharacterCareerAttributeAdvanceQuote Quote, CharacterCareerAttributeAdvanceBlocker Blocker)[] cases =
        [
            ("karma", Quote(availableKarma: 0), CharacterCareerAttributeAdvanceBlocker.InsufficientKarma),
            ("maximum", Quote(effectiveValue: 6, naturalMaximum: 6), CharacterCareerAttributeAdvanceBlocker.AtNaturalMaximum),
            ("special", Quote(
                abbreviation: "MAG",
                kind: CharacterCareerAttributeKind.Magic,
                magicEnabled: false), CharacterCareerAttributeAdvanceBlocker.SpecialAttributeDisabled)
        ];
        foreach ((string name, CharacterCareerAttributeAdvanceQuote quote, CharacterCareerAttributeAdvanceBlocker expected) in cases)
        {
            Require(quote.Blocker == expected && !quote.CanAdvance, $"{name} quote must have the exact blocker.");
            Require(!Sr5CareerAttributeDraft.TryCreate(
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
        Sr5CareerAttributeDraft draft = Draft();
        Sr5CareerAttributeCheckpoint checkpoint = Sr5CareerAttributeCheckpoint.FromDraft(draft);
        Require(checkpoint.IsStructurallyValid(), "Exact reviewed checkpoint must be valid.");
        Require(!(checkpoint with { IdempotencyKey = new string('0', 64) }).IsStructurallyValid(),
            "Forged idempotency must invalidate the checkpoint.");
        Require(!(checkpoint with
        {
            Draft = draft with { Plan = draft.Plan with { ExpenseAmount = draft.Plan.ExpenseAmount + 1 } }
        }).IsStructurallyValid(), "Tampered expense plan must invalidate the checkpoint.");

        MemoryBackend backend = new(JsonSerializer.Serialize(checkpoint with
        {
            SchemaVersion = Sr5CareerAttributeCheckpoint.CurrentSchemaVersion - 1
        }));
        Sr5CareerAttributeCheckpointStore store = new(backend);
        Require(!store.TryRead(out _, out string blocker)
            && blocker.Contains("replay-blocking", StringComparison.Ordinal),
            "Prior schema must remain an explicit replay-blocking lock.");
        Require(!store.TryCreate(checkpoint, out _, out _), "Unreadable lock must prevent overwrite/replay.");
        Require(!string.IsNullOrWhiteSpace(backend.Read()), "Unreadable lock must remain durable.");
    }

    private static async Task CoordinatorVerifiesOnlyFreshExactReceiptAsync()
    {
        Sr5CareerAttributeDraft draft = Draft();
        FakePresenter presenter = FakePresenter.Before(draft);
        (Sr5CareerAttributeCheckpointStore store, Sr5CareerAttributeCheckpoint applying) =
            ApplyingStore(draft, presenter);
        TaskCompletionSource<bool> applyEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> allowApply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        presenter.ApplyHandler = async _ =>
        {
            applyEntered.TrySetResult(true);
            await allowApply.Task;
            presenter.PublishApplied(draft);
            return true;
        };
        Sr5CareerAttributeCoordinator coordinator = new(
            presenter,
            new FixedOwner(OwnerId));
        Task<Sr5CareerAttributeApplyResult> pendingApply = coordinator.ApplyAsync(
            draft,
            applying,
            store);
        Task first = await Task.WhenAny(applyEntered.Task, pendingApply);
        if (first == pendingApply)
        {
            await pendingApply;
            throw new InvalidOperationException(
                "The coordinator completed before entering the presenter apply boundary.");
        }
        Sr5CareerAttributeRecoveryResolution prematureNotApplied =
            Sr5CareerAttributeCoordinator.Resolve(applying, presenter.Binding, presenter.Editor);
        Require(prematureNotApplied.Status == Sr5CareerAttributeRecoveryStatus.NotAppliedVerified,
            "The pre-mutation projection should otherwise look not-applied.");
        Require(!store.TryRecordAuthoritativeResolution(
            Sr5CareerAttributeCheckpointCas.From(applying),
            prematureNotApplied,
            out _,
            out string activeBlocker)
            && activeBlocker.Contains("still running", StringComparison.Ordinal),
            "A concurrent recovery must not clear Applying while mutation owns its lease.");
        allowApply.TrySetResult(true);
        Sr5CareerAttributeApplyResult result = await pendingApply;
        Require(result.Status == Sr5CareerAttributeApplyStatus.Applied, result.Message);
        Require(result.Receipt is not null
            && Sr5CareerAttributeCoordinator.ReceiptMatchesDraft(draft, result.Receipt),
            "Receipt must match every reviewed quote and plan value.");
        Require(presenter.ApplyCalls == 1, "Apply must execute exactly once.");

        CharacterCareerAttributeAdvanceReceipt exact = result.Receipt!;
        CharacterCareerAttributeAdvanceReceipt[] forgeries =
        [
            exact with { Identity = new CharacterCareerAttributeIdentity("AGI", CharacterCareerAttributeKind.Normal) },
            exact with { TransactionId = Guid.NewGuid(), ExpenseId = Guid.NewGuid() },
            exact with { LogicalRevision = new string('0', 64) },
            exact with { SourceRevision = new string('0', 64) },
            exact with { RuleDigest = new string('0', 64) },
            exact with { ReceiptDigest = new string('0', 64) }
        ];
        foreach (CharacterCareerAttributeAdvanceReceipt forged in forgeries)
        {
            Require(!Sr5CareerAttributeCoordinator.ReceiptMatchesDraft(draft, forged),
                "Every forged receipt identity or digest must fail closed.");
        }

        FakePresenter partial = FakePresenter.Before(draft);
        partial.BindingValue = partial.BindingValue with
        {
            ContentRevision = 42,
            SavedRevision = 42
        };
        partial.Editor = Editor(42, Successor(draft), recoverable: []);
        Sr5CareerAttributeRecoveryResolution partialResolution =
            await new Sr5CareerAttributeCoordinator(partial, new FixedOwner(OwnerId))
                .ResolveAsync(applying);
        Require(partialResolution.Status == Sr5CareerAttributeRecoveryStatus.OutcomeUnknown,
            "A changed attribute without the exact recoverable receipt must be outcome-unknown.");
    }

    private static async Task ApplyingCrashResolvesWithoutReplayAsync()
    {
        Sr5CareerAttributeDraft draft = Draft();
        FakePresenter presenter = FakePresenter.Before(draft);
        (Sr5CareerAttributeCheckpointStore store, Sr5CareerAttributeCheckpoint applying) =
            ApplyingStore(draft, presenter);
        presenter.ApplyHandler = _ =>
        {
            presenter.PublishApplied(draft);
            throw new InvalidOperationException("simulated process death after durable save");
        };
        Sr5CareerAttributeCoordinator first = new(presenter, new FixedOwner(OwnerId));
        await RequireThrowsAsync<InvalidOperationException>(
            () => first.ApplyAsync(draft, applying, store),
            "The simulated crash must escape with the Applying checkpoint intact.");
        Require(presenter.ApplyCalls == 1, "First process must call apply exactly once.");

        Sr5CareerAttributeCoordinator restarted = new(presenter, new FixedOwner(OwnerId));
        Sr5CareerAttributeRecoveryResolution recovered = await restarted.ResolveAsync(applying);
        Require(recovered.Status == Sr5CareerAttributeRecoveryStatus.AppliedVerified,
            recovered.Message);
        Require(presenter.ApplyCalls == 1, "Restart resolution must never replay the mutation.");
    }

    private static void CheckpointCasRejectsForgedResolutionAndWrongOwner()
    {
        Sr5CareerAttributeDraft draft = Draft();
        MutableOwner authority = new(OwnerId);
        Sr5CareerRunnerBinding binding = FakePresenter.Before(draft).Binding;
        Sr5CareerAttributeLiveCheckpointAuthority liveAuthority = new(
            authority,
            Editor(draft.ExpectedContentRevision, draft.Quote),
            () => binding);
        MemoryBackend backend = new();
        Sr5CareerAttributeCheckpointStore store = new(backend, liveAuthority);
        Require(store.TryCreate(
            Sr5CareerAttributeCheckpoint.FromDraft(draft),
            out Sr5CareerAttributeCheckpoint reviewed,
            out string blocker), blocker);
        Require(store.TryBeginApply(
            Sr5CareerAttributeCheckpointCas.From(reviewed),
            out Sr5CareerAttributeCheckpoint applying,
            out blocker), blocker);

        CharacterCareerAttributeAdvanceReceipt receipt = Receipt(draft);
        Sr5CareerAttributeRecoveryResolution exact = Sr5CareerAttributeRecoveryProof.Create(
            applying,
            Sr5CareerAttributeRecoveryStatus.AppliedVerified,
            receipt,
            "exact");
        Require(!store.TryRecordAuthoritativeResolution(
            Sr5CareerAttributeCheckpointCas.From(applying),
            exact with { Message = "forged" },
            out _,
            out _), "Forged signed resolution fields must fail closed.");
        Require(!store.TryRecordAuthoritativeResolution(
            Sr5CareerAttributeCheckpointCas.From(applying),
            exact,
            out _,
            out _), "AppliedVerified must fail while the live runner remains at the original revision.");
        authority.OwnerId = Guid.NewGuid();
        Require(!store.TryRecordAuthoritativeResolution(
            Sr5CareerAttributeCheckpointCas.From(applying),
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
            Sr5CareerAttributeCheckpointCas.From(applying),
            exact,
            out Sr5CareerAttributeCheckpoint applied,
            out blocker), blocker);
        Require(applied.Phase == Sr5CareerCheckpointPhase.Applied,
            "Exact resolution must advance Applying to Applied.");

        DeleteReadFailureBackend deleteFailure = new(JsonSerializer.Serialize(applied));
        Sr5CareerAttributeCheckpointStore deleteStore = new(deleteFailure, liveAuthority);
        Require(!deleteStore.TryDeleteApplied(
            Sr5CareerAttributeCheckpointCas.From(applied),
            receipt,
            out _), "A failed delete read-back must not claim acknowledgement.");
        Require(deleteStore.TryRead(
            out Sr5CareerAttributeCheckpoint restored,
            out blocker)
            && restored.Phase == Sr5CareerCheckpointPhase.Applied,
            "A failed delete read-back must restore the exact replay-blocking Applied checkpoint.");
    }

    private static Sr5CareerAttributeDraft Draft()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote();
        Require(Sr5CareerAttributeDraft.TryCreate(
            Editor(41, quote),
            quote,
            OwnerId,
            ActionId,
            ExpenseDate,
            out Sr5CareerAttributeDraft draft,
            out string blocker), blocker);
        return draft;
    }

    private static (
        Sr5CareerAttributeCheckpointStore Store,
        Sr5CareerAttributeCheckpoint Applying) ApplyingStore(
            Sr5CareerAttributeDraft draft,
            FakePresenter presenter)
    {
        Sr5CareerAttributeLiveCheckpointAuthority liveAuthority = new(
            new FixedOwner(OwnerId),
            Editor(draft.ExpectedContentRevision, draft.Quote),
            () => presenter.Binding);
        Sr5CareerAttributeCheckpointStore store = new(new MemoryBackend(), liveAuthority);
        Require(store.TryCreate(
            Sr5CareerAttributeCheckpoint.FromDraft(draft),
            out Sr5CareerAttributeCheckpoint reviewed,
            out string blocker), blocker);
        Require(store.TryBeginApply(
            Sr5CareerAttributeCheckpointCas.From(reviewed),
            out Sr5CareerAttributeCheckpoint applying,
            out blocker), blocker);
        return (store, applying);
    }

    private static CharacterCareerAttributeAdvanceQuote Quote(
        int availableKarma = 35,
        int effectiveValue = 2,
        int naturalMaximum = 6,
        string abbreviation = "BOD",
        CharacterCareerAttributeKind kind = CharacterCareerAttributeKind.Normal,
        bool magicEnabled = true)
    {
        CharacterCareerAttributeAdvanceInput input = new(
            new CharacterCareerAttributeIdentity(abbreviation, kind),
            Created: true,
            RulesetId: CharacterCareerAttributeAdvanceRules.RulesetId,
            DisplayName: abbreviation == "BOD" ? "Body" : abbreviation,
            BasePoints: 1,
            KarmaPoints: Math.Max(0, effectiveValue - 1),
            EffectiveValue: effectiveValue,
            NaturalMaximum: naturalMaximum,
            MetatypeMinimum: 1,
            AvailableKarma: availableKarma,
            MagicEnabled: magicEnabled,
            MysticAdept: false,
            MysticAdeptSecondMagicAttributeEnabled: false,
            ResonanceEnabled: true,
            BurnedEdgePoints: 0,
            Settings: new CharacterCareerAttributeAdvanceSettings(
                5,
                AlternateMetatypeAttributeKarma: false),
            Modifiers: [],
            RawSourceState: $"<attribute name='{abbreviation}' effective='{effectiveValue}' />",
            RawRuleState: "<settings karmaattribute='5' />");
        Require(CharacterCareerAttributeAdvanceRules.TryCreateQuote(input, out var quote),
            "Test quote must be coherent.");
        return quote;
    }

    private static CareerAttributeAdvanceEditorState Editor(
        long revision,
        CharacterCareerAttributeAdvanceQuote quote,
        IReadOnlyList<CharacterCareerAttributeAdvanceReceipt>? recoverable = null)
        => new(
            WorkspaceId,
            revision,
            [quote],
            OmittedAttributeCount: 0,
            recoverable ?? [],
            OmittedReceiptCount: 0);

    private static CharacterCareerAttributeAdvanceQuote Successor(Sr5CareerAttributeDraft draft)
    {
        CharacterCareerAttributeAdvanceInput input = new(
            draft.Quote.Identity,
            Created: true,
            RulesetId: CharacterCareerAttributeAdvanceRules.RulesetId,
            DisplayName: draft.Quote.DisplayName,
            BasePoints: draft.Quote.BasePoints,
            KarmaPoints: draft.Plan.SavedAttributeKarmaPoints,
            EffectiveValue: draft.Quote.TargetValue,
            NaturalMaximum: draft.Quote.NaturalMaximum,
            MetatypeMinimum: draft.Quote.MetatypeMinimum,
            AvailableKarma: draft.Plan.SavedCharacterKarma,
            MagicEnabled: true,
            MysticAdept: false,
            MysticAdeptSecondMagicAttributeEnabled: false,
            ResonanceEnabled: true,
            BurnedEdgePoints: draft.Plan.SavedBurnedEdgePoints,
            Settings: new CharacterCareerAttributeAdvanceSettings(
                5,
                AlternateMetatypeAttributeKarma: false),
            Modifiers: [],
            RawSourceState: "<attribute successor='true' />",
            RawRuleState: "<settings karmaattribute='5' />");
        Require(CharacterCareerAttributeAdvanceRules.TryCreateQuote(input, out var quote),
            "Successor quote must be coherent.");
        return quote;
    }

    private static CharacterCareerAttributeAdvanceReceipt Receipt(Sr5CareerAttributeDraft draft)
    {
        Require(CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
            draft.Plan.ExpenseId,
            draft.Quote,
            draft.Plan,
            draft.Plan.SavedAttributeKarmaPoints,
            draft.Plan.SavedCharacterKarma,
            draft.Plan.SavedBurnedEdgePoints,
            expenseExistsExactlyOnce: true,
            out CharacterCareerAttributeAdvanceReceipt receipt),
            "Test receipt must be coherent.");
        return receipt;
    }

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

    private sealed class FakePresenter : ISr5CareerAttributePresenter
    {
        public required Sr5CareerRunnerBinding BindingValue { get; set; }
        public required CareerAttributeAdvanceEditorState Editor { get; set; }
        public Func<CareerAttributeAdvanceRequest, Task<bool>>? ApplyHandler { get; set; }
        public int ApplyCalls { get; private set; }
        public Sr5CareerRunnerBinding Binding => BindingValue;

        public static FakePresenter Before(Sr5CareerAttributeDraft draft)
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

        public Task<CareerAttributeAdvanceEditorState?> LoadAttributesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<CareerAttributeAdvanceEditorState?>(Editor);

        public Task<bool> ApplyAndSaveAsync(
            CareerAttributeAdvanceRequest request,
            CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return ApplyHandler?.Invoke(request) ?? Task.FromResult(false);
        }

        public void PublishApplied(Sr5CareerAttributeDraft draft)
        {
            CharacterCareerAttributeAdvanceReceipt receipt = Program.Receipt(draft);
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
