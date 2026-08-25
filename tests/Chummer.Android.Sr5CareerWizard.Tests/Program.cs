using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

internal static class Program
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("sr5-career-runner");
    private static readonly Guid SkillId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SourceSkillId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OwnerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ActionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTime ExpenseDate = new(2081, 6, 3, 19, 30, 0, 987, DateTimeKind.Local);
    private static readonly DateTime SerializedExpenseDate = new(2081, 6, 3, 19, 30, 0, DateTimeKind.Unspecified);

    private static async Task Main()
    {
        await CreatedSr5BoundaryRejectsOtherLifecycleAndEditionAsync();
        await CoordinatorBuildsReceiptOnlyFromReloadedSkillAndExpenseAsync();
        await CoordinatorRejectsWrongSkillAndMissingExpenseAsync();
        CheckpointStoreEnforcesOwnerActionCasAndReadBack();
        await ApplyingCrashIsResolvedWithoutReplayAsync();
        Console.WriteLine("SR5 Career authority tests passed: 5");
    }

    private static async Task CreatedSr5BoundaryRejectsOtherLifecycleAndEditionAsync()
    {
        FakePresenter nonSr5 = FakePresenter.BeforeApply();
        nonSr5.BindingValue = nonSr5.BindingValue with { GameEdition = "SR6" };
        Sr5CareerActiveSkillCoordinator authority = new(nonSr5);
        await RequireThrowsAsync<InvalidOperationException>(
            () => authority.PrepareAsync(),
            "A non-SR5 runner must be rejected at the public prepare boundary.");

        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint applying = Sr5CareerDraftCheckpoint.FromDraft(
            draft,
            Sr5CareerCheckpointPhase.Applying) with { Version = 2 };
        await RequireThrowsAsync<InvalidOperationException>(
            () => authority.ApplyAsync(draft, applying),
            "A non-SR5 runner must be rejected at the public apply boundary.");

        FakePresenter creation = FakePresenter.BeforeApply();
        creation.BindingValue = creation.BindingValue with { Created = false };
        authority = new Sr5CareerActiveSkillCoordinator(creation);
        await RequireThrowsAsync<InvalidOperationException>(
            () => authority.PrepareAsync(),
            "An uncreated runner must be rejected at the public prepare boundary.");

        await RequireThrowsAsync<InvalidOperationException>(
            () => authority.ApplyAsync(draft, applying),
            "An uncreated runner must be rejected again at apply time.");
    }

    private static async Task CoordinatorBuildsReceiptOnlyFromReloadedSkillAndExpenseAsync()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint reviewed = Sr5CareerDraftCheckpoint.FromDraft(draft);
        MemoryBackend backend = new();
        Sr5CareerDraftCheckpointStore store = new(backend);
        Require(store.TryCreate(reviewed, out reviewed, out string blocker), blocker);
        Require(
            store.TryBeginApply(
                Sr5CareerCheckpointCas.From(reviewed),
                out Sr5CareerDraftCheckpoint applying,
                out blocker),
            blocker);

        FakePresenter presenter = FakePresenter.BeforeApply();
        presenter.ApplyHandler = _ =>
        {
            presenter.PublishApplied(draft, includeExpense: true);
            return Task.FromResult(true);
        };
        Sr5CareerActiveSkillCoordinator authority = new(presenter);
        Sr5CareerApplyResult result = await authority.ApplyAsync(draft, applying);

        Require(result.Status == Sr5CareerApplyStatus.Applied, result.Message);
        Sr5CareerActiveSkillReceipt receipt = result.Receipt!;
        Require(receipt.SkillId == SkillId && receipt.SourceSkillId == SourceSkillId, "Receipt must use reloaded skill identity.");
        Require(receipt.SavedRating == draft.Quote.TotalBaseRating + 1, "Receipt must use the reloaded target rating.");
        Require(receipt.ExpenseId == ActionId, "Receipt must use the reloaded expense GUID.");
        Require(
            receipt.ExpenseDateLocal == SerializedExpenseDate
            && draft.Plan.ExpenseDateLocal == SerializedExpenseDate,
            "The plan and receipt must use the exact second-precision date serialized by Chummer5.");
        Require(receipt.ExpenseReason == draft.Plan.ExpenseReason, "Receipt must use the reloaded reason.");
        Require(receipt.KarmaUndoType == draft.Plan.KarmaUndoType, "Receipt must use the reloaded undo type.");
        Require(receipt.RuleDigest == presenter.Skills!.Skills.Single().RuleDigest, "Receipt must use the post-save rule digest.");
        Require(
            store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerDraftCheckpoint applied,
                out blocker),
            blocker);
        Require(applied.Phase == Sr5CareerCheckpointPhase.Applied, "Verified apply must advance the checkpoint to Applied.");
    }

    private static async Task CoordinatorRejectsWrongSkillAndMissingExpenseAsync()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint applying = Sr5CareerDraftCheckpoint.FromDraft(
            draft,
            Sr5CareerCheckpointPhase.Applying) with { Version = 2 };

        FakePresenter wrongSkill = FakePresenter.BeforeApply();
        wrongSkill.PublishApplied(draft, includeExpense: true);
        CharacterCareerActiveSkillAdvanceQuote loaded = wrongSkill.Skills!.Skills.Single();
        wrongSkill.Skills = wrongSkill.Skills with
        {
            Skills = [loaded with
            {
                Identity = new CharacterCareerActiveSkillIdentity(SkillId, Guid.NewGuid())
            }]
        };
        Sr5CareerRecoveryResolution wrongSkillResult =
            await new Sr5CareerActiveSkillCoordinator(wrongSkill).ResolveAsync(applying);
        Require(
            wrongSkillResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && wrongSkillResult.Receipt is null,
            "Wrong source identity must not produce a receipt.");

        FakePresenter missingExpense = FakePresenter.BeforeApply();
        missingExpense.PublishApplied(draft, includeExpense: false);
        Sr5CareerRecoveryResolution missingExpenseResult =
            await new Sr5CareerActiveSkillCoordinator(missingExpense).ResolveAsync(applying);
        Require(
            missingExpenseResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && missingExpenseResult.Receipt is null,
            "A missing exact expense must not produce a receipt.");

        FakePresenter wrongExpense = FakePresenter.BeforeApply();
        wrongExpense.PublishApplied(draft, includeExpense: true);
        CharacterCareerKarmaExpenseEntry loadedExpense = wrongExpense.Expenses!.Expenses.Single();
        wrongExpense.Expenses = wrongExpense.Expenses with
        {
            Expenses = [loadedExpense with { RawKarmaUndoType = "AddSkill" }]
        };
        Sr5CareerRecoveryResolution wrongExpenseResult =
            await new Sr5CareerActiveSkillCoordinator(wrongExpense).ResolveAsync(applying);
        Require(
            wrongExpenseResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && wrongExpenseResult.Receipt is null,
            "A mismatched exact expense undo type must not produce a receipt.");
    }

    private static void CheckpointStoreEnforcesOwnerActionCasAndReadBack()
    {
        Sr5CareerDraftCheckpoint reviewed = Sr5CareerDraftCheckpoint.FromDraft(Draft());
        MemoryBackend backend = new();
        Sr5CareerDraftCheckpointStore store = new(backend);
        Require(store.TryCreate(reviewed, out Sr5CareerDraftCheckpoint stored, out string blocker), blocker);

        Sr5CareerDraftCheckpoint foreign = reviewed with
        {
            OwnerId = Guid.NewGuid(),
            ActionId = Guid.NewGuid(),
            IdempotencyKey = new string('a', 64)
        };
        Require(!store.TryCreate(foreign, out _, out _), "A foreign owner/action must not overwrite the checkpoint.");
        Require(store.TryRead(out Sr5CareerDraftCheckpoint unchanged, out _), "The original checkpoint must remain readable.");
        Require(unchanged == stored, "Failed overwrite must leave the original bytes intact.");

        Sr5CareerCheckpointCas stale = Sr5CareerCheckpointCas.From(stored) with { Version = stored.Version + 1 };
        Require(!store.TryBeginApply(stale, out _, out _), "A stale CAS must not begin apply.");
        Require(
            store.TryBeginApply(
                Sr5CareerCheckpointCas.From(stored),
                out Sr5CareerDraftCheckpoint applying,
                out blocker),
            blocker);
        Require(applying.Phase == Sr5CareerCheckpointPhase.Applying && applying.Version == 2, "Reviewed→Applying must be one exact CAS transition.");
        Require(
            !store.TryDeleteResolvedOrReviewed(Sr5CareerCheckpointCas.From(applying), out _),
            "Applying cannot be blindly cleared.");

        MemoryBackend nondurableBackend = new() { DropWrites = true };
        Sr5CareerDraftCheckpointStore nondurable = new(nondurableBackend);
        Require(
            !nondurable.TryCreate(reviewed, out _, out string durabilityBlocker)
            && durabilityBlocker.Contains("read-back", StringComparison.OrdinalIgnoreCase),
            "A write without exact read-back must fail durability proof.");
    }

    private static async Task ApplyingCrashIsResolvedWithoutReplayAsync()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        MemoryBackend backend = new();
        Sr5CareerDraftCheckpointStore firstProcessStore = new(backend);
        Require(
            firstProcessStore.TryCreate(
                Sr5CareerDraftCheckpoint.FromDraft(draft),
                out Sr5CareerDraftCheckpoint reviewed,
                out string blocker),
            blocker);
        Require(
            firstProcessStore.TryBeginApply(
                Sr5CareerCheckpointCas.From(reviewed),
                out Sr5CareerDraftCheckpoint applying,
                out blocker),
            blocker);

        FakePresenter presenter = FakePresenter.BeforeApply();
        presenter.ApplyHandler = _ =>
        {
            presenter.PublishApplied(draft, includeExpense: true);
            throw new InvalidOperationException("simulated process death after durable save");
        };
        Sr5CareerActiveSkillCoordinator firstProcess = new(presenter);
        await RequireThrowsAsync<InvalidOperationException>(
            () => firstProcess.ApplyAsync(draft, applying),
            "The simulated crash must escape while the checkpoint remains Applying.");
        Require(presenter.ApplyCalls == 1, "The first process must attempt apply exactly once.");

        Sr5CareerDraftCheckpointStore restartedStore = new(backend);
        Require(restartedStore.TryRead(out Sr5CareerDraftCheckpoint recovered, out blocker), blocker);
        Require(recovered.Phase == Sr5CareerCheckpointPhase.Applying, "Restart must observe the durable Applying phase.");
        Sr5CareerRecoveryResolution resolution =
            await new Sr5CareerActiveSkillCoordinator(presenter).ResolveAsync(recovered);
        Require(resolution.Status == Sr5CareerRecoveryStatus.AppliedVerified, resolution.Message);
        Require(
            restartedStore.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(recovered),
                resolution,
                out Sr5CareerDraftCheckpoint applied,
                out blocker),
            blocker);
        Require(applied.Phase == Sr5CareerCheckpointPhase.Applied, "Restart resolution must record Applied by CAS.");
        Require(presenter.ApplyCalls == 1, "Authoritative restart resolution must not replay the mutation.");
    }

    private static Sr5CareerActiveSkillDraft Draft()
    {
        CharacterCareerActiveSkillAdvanceQuote quote = Quote(
            karmaPoints: 1,
            totalRating: 3,
            availableKarma: 20);
        CareerActiveSkillAdvanceEditorState editor = new(WorkspaceId, 41, [quote], 0);
        Require(
            Sr5CareerActiveSkillDraft.TryCreate(
                editor,
                quote,
                OwnerId,
                ActionId,
                ExpenseDate,
                out Sr5CareerActiveSkillDraft draft,
                out string blocker),
            blocker);
        return draft;
    }

    private static CharacterCareerActiveSkillAdvanceQuote Quote(
        int karmaPoints,
        int totalRating,
        int availableKarma)
    {
        CharacterCareerActiveSkillAdvanceInput input = new(
            new CharacterCareerActiveSkillIdentity(SkillId, SourceSkillId),
            Created: true,
            "Sneaking",
            "Physical Active",
            "Sneaking",
            BasePoints: 2,
            karmaPoints,
            totalRating,
            RatingMaximum: 12,
            availableKarma,
            new CharacterCareerActiveSkillAdvanceSettings(2, 2, 5, 5, false),
            OtherGroupMembers: [],
            Modifiers: [],
            RawSourceState: "<skill><name>Sneaking</name></skill>",
            RawRuleState: "<settings />");
        Require(
            CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(input, out CharacterCareerActiveSkillAdvanceQuote quote)
            && CharacterCareerActiveSkillAdvanceRules.IsCoherent(quote),
            "Test quote authority must be coherent.");
        return quote;
    }

    private static CharacterCareerKarmaExpenseEntry Expense(Sr5CareerActiveSkillDraft draft)
    {
        Require(
            CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
                draft.Plan.ExpenseId,
                draft.Plan.ExpenseDateLocal,
                draft.Plan.ExpenseAmount,
                draft.Plan.ExpenseReason,
                refund: false,
                forceCareerVisible: false,
                karmaUndoTypeElementPresent: true,
                draft.Plan.KarmaUndoType,
                out CharacterCareerKarmaExpenseEntry? expense)
            && expense is not null,
            "Test expense authority must be coherent.");
        return expense!;
    }

    private static async Task RequireThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakePresenter : ISr5CareerActiveSkillPresenter
    {
        public Sr5CareerRunnerBinding BindingValue { get; set; } = null!;
        public CareerActiveSkillAdvanceEditorState? Skills { get; set; }
        public CareerKarmaExpenseEditorState? Expenses { get; set; }
        public Func<CareerActiveSkillAdvanceRequest, Task<bool>> ApplyHandler { get; set; } = _ => Task.FromResult(false);
        public int ApplyCalls { get; private set; }

        public Sr5CareerRunnerBinding Binding => BindingValue;

        public static FakePresenter BeforeApply()
        {
            CharacterCareerActiveSkillAdvanceQuote quote = Quote(1, 3, 20);
            return new FakePresenter
            {
                BindingValue = new Sr5CareerRunnerBinding(
                    Created: true,
                    GameEdition: "SR5",
                    WorkspaceId,
                    ContentRevision: 41,
                    SavedRevision: 41,
                    IsDirty: false,
                    Error: null),
                Skills = new CareerActiveSkillAdvanceEditorState(WorkspaceId, 41, [quote], 0),
                Expenses = new CareerKarmaExpenseEditorState(WorkspaceId, 41, 20, [])
            };
        }

        public void PublishApplied(Sr5CareerActiveSkillDraft draft, bool includeExpense)
        {
            CharacterCareerActiveSkillAdvanceQuote loaded = Quote(
                karmaPoints: 2,
                totalRating: 4,
                availableKarma: draft.Plan.SavedCharacterKarma);
            BindingValue = BindingValue with
            {
                ContentRevision = 42,
                SavedRevision = 42,
                IsDirty = false,
                Error = null
            };
            Skills = new CareerActiveSkillAdvanceEditorState(WorkspaceId, 42, [loaded], 0);
            Expenses = new CareerKarmaExpenseEditorState(
                WorkspaceId,
                42,
                draft.Plan.SavedCharacterKarma,
                includeExpense ? [Expense(draft)] : []);
        }

        public Task<CareerActiveSkillAdvanceEditorState?> LoadActiveSkillsAsync(CancellationToken cancellationToken)
            => Task.FromResult(Skills);

        public Task<CareerKarmaExpenseEditorState?> LoadKarmaExpensesAsync(CancellationToken cancellationToken)
            => Task.FromResult(Expenses);

        public Task<bool> ApplyAndSaveAsync(
            CareerActiveSkillAdvanceRequest request,
            CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return ApplyHandler(request);
        }
    }

    private sealed class MemoryBackend : ISr5CareerCheckpointBackend
    {
        private string _payload = string.Empty;
        public bool DropWrites { get; init; }

        public string Read() => _payload;
        public void Write(string payload)
        {
            if (!DropWrites)
            {
                _payload = payload;
            }
        }
        public void Remove() => _payload = string.Empty;
    }
}
