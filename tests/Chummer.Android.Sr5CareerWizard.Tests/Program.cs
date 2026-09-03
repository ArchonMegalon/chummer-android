using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using System.Text.Json;

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
        await ApplyingCheckpointMustMatchCompleteReviewedDraftAsync();
        CheckpointStoreEnforcesOwnerActionCasAndReadBack();
        ReviewedCheckpointAccessRejectsForeignOwnerWorkspaceAndBindings();
        await AppliedCheckpointAcknowledgementRequiresLiveOwnerAndExactReceiptAsync();
        PriorSchemaCheckpointRemainsAReplayBlockingLock();
        await ApplyingCrashIsResolvedWithoutReplayAsync();
#if AUTHORITY_LIGHTWEIGHT
        await SavedExpenseXmlProjectionIsPresenceAwareAndMalformedValuesFailClosedAsync();
        Console.WriteLine("SR5 Career authority tests passed: 10");
#else
        Console.WriteLine("SR5 Career authority tests passed: 9");
#endif
    }

    private static async Task CreatedSr5BoundaryRejectsOtherLifecycleAndEditionAsync()
    {
        FakePresenter nonSr5 = FakePresenter.BeforeApply();
        nonSr5.BindingValue = nonSr5.BindingValue with { GameEdition = "SR6" };
        Sr5CareerActiveSkillCoordinator authority = new(nonSr5, new FixedOwnerAuthority(OwnerId));
        await RequireThrowsAsync<InvalidOperationException>(
            () => authority.PrepareAsync(),
            "A non-SR5 runner must be rejected at the public prepare boundary.");

        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint applying = Sr5CareerDraftCheckpoint.FromDraft(
            draft,
            Sr5CareerCheckpointPhase.Applying) with { Version = 2 };
        Sr5CareerDraftCheckpointStore unopenedStore = new(new MemoryBackend());
        await RequireThrowsAsync<InvalidOperationException>(
            () => authority.ApplyAsync(draft, applying, unopenedStore),
            "A non-SR5 runner must be rejected at the public apply boundary.");

        FakePresenter creation = FakePresenter.BeforeApply();
        creation.BindingValue = creation.BindingValue with { Created = false };
        authority = new Sr5CareerActiveSkillCoordinator(creation, new FixedOwnerAuthority(OwnerId));
        await RequireThrowsAsync<InvalidOperationException>(
            () => authority.PrepareAsync(),
            "An uncreated runner must be rejected at the public prepare boundary.");

        await RequireThrowsAsync<InvalidOperationException>(
            () => authority.ApplyAsync(draft, applying, unopenedStore),
            "An uncreated runner must be rejected again at apply time.");

        FakePresenter valid = FakePresenter.BeforeApply();
        await RequireThrowsAsync<InvalidOperationException>(
            () => new Sr5CareerActiveSkillCoordinator(
                valid,
                new FixedOwnerAuthority(Guid.NewGuid())).ResolveAsync(applying),
            "A foreign local owner must not resolve another owner's Applying checkpoint.");
    }

    private static async Task CoordinatorBuildsReceiptOnlyFromReloadedSkillAndExpenseAsync()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint reviewed = Sr5CareerDraftCheckpoint.FromDraft(draft);
        FakePresenter presenter = FakePresenter.BeforeApply();
        Sr5CareerLiveReviewedCheckpointAuthority checkpointAuthority = new(
            new FixedOwnerAuthority(OwnerId),
            new CareerActiveSkillAdvanceEditorState(
                draft.WorkspaceId,
                draft.ExpectedContentRevision,
                [draft.Quote],
                OmittedSkillCount: 0),
            () => presenter.Binding);
        MemoryBackend backend = new();
        MemoryBackend mutationOwnerBackend = new();
        Sr5CareerDraftCheckpointStore store = new(
            backend,
            checkpointAuthority,
            new Sr5CareerMutationOwnerStore(mutationOwnerBackend));
        Require(store.TryCreate(reviewed, out reviewed, out string blocker), blocker);

        Sr5CareerLiveReviewedCheckpointAuthority restartedCheckpointAuthority = new(
            new FixedOwnerAuthority(OwnerId),
            new CareerActiveSkillAdvanceEditorState(
                draft.WorkspaceId,
                draft.ExpectedContentRevision,
                [draft.Quote],
                OmittedSkillCount: 0),
            () => presenter.Binding);
        store = new Sr5CareerDraftCheckpointStore(
            backend,
            restartedCheckpointAuthority,
            new Sr5CareerMutationOwnerStore(mutationOwnerBackend));
        Require(
            store.TryRead(out Sr5CareerDraftCheckpoint recoveredReviewed, out blocker)
            && recoveredReviewed == reviewed,
            "A new process must recover the exact reviewed action before Applying.");
        Require(
            store.TryBeginApply(
                Sr5CareerCheckpointCas.From(recoveredReviewed),
                out Sr5CareerDraftCheckpoint applying,
                out blocker),
            blocker);

        presenter.ApplyHandler = _ =>
        {
            presenter.PublishApplied(draft, includeExpense: true);
            return Task.FromResult(true);
        };
        Sr5CareerActiveSkillCoordinator authority = new(presenter, new FixedOwnerAuthority(OwnerId));
        Sr5CareerApplyResult result = await authority.ApplyAsync(draft, applying, store);

        Require(result.Status == Sr5CareerApplyStatus.Applied, result.Message);
        Require(
            presenter.ApplyCalls == 1,
            "An exact clean reviewed revision must execute the typed mutation exactly once.");
        Sr5CareerActiveSkillReceipt receipt = result.Receipt!;
        Require(receipt.SkillId == SkillId && receipt.SourceSkillId == SourceSkillId, "Receipt must use reloaded skill identity.");
        Require(receipt.SavedRating == draft.Quote.TotalBaseRating + 1, "Receipt must use the reloaded target rating.");
        Require(receipt.ExpenseId == ActionId, "Receipt must use the reloaded expense GUID.");
        Require(
            receipt.ExpenseDateLocal == SerializedExpenseDate
            && draft.Plan.ExpenseDateLocal == SerializedExpenseDate,
            "The plan and receipt must use the exact second-precision date serialized by Chummer5.");
        Require(receipt.ExpenseReason == draft.Plan.ExpenseReason, "Receipt must use the reloaded reason.");
        Require(receipt.ExpenseType == "Karma", "Receipt must use the exact reloaded expense type.");
        Require(!receipt.ExpenseRefund, "Receipt must prove the reloaded expense is not a refund.");
        Require(!receipt.ExpenseForceCareerVisible, "Receipt must prove the reloaded expense is not force-visible.");
        Require(receipt.KarmaUndoType == draft.Plan.KarmaUndoType, "Receipt must use the reloaded undo type.");
        Require(receipt.NuyenUndoType == draft.Plan.NuyenUndoType, "Receipt must use the reloaded Nuyen undo type.");
        Require(receipt.UndoObjectId == draft.Plan.UndoObjectId, "Receipt must use the reloaded undo object identity.");
        Require(receipt.UndoQuantity == draft.Plan.UndoQuantity, "Receipt must use the reloaded undo quantity.");
        Require(receipt.UndoExtra == draft.Plan.UndoExtra, "Receipt must use the reloaded undo extra value.");
        Require(receipt.RuleDigest == presenter.Skills!.Skills.Single().RuleDigest, "Receipt must use the post-save rule digest.");
        Sr5CareerRecoveryResolution forgedStatus = result.Resolution with
        {
            Status = Sr5CareerRecoveryStatus.NotAppliedVerified,
            Receipt = null
        };
        Require(
            !store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                forgedStatus,
                out _,
                out _),
            "A caller must not forge an authoritative NotApplied result from an Applied proof.");
        Sr5CareerRecoveryResolution forgedReceipt = result.Resolution with
        {
            Receipt = receipt with { ExpenseReason = receipt.ExpenseReason + " forged" }
        };
        Require(
            !store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                forgedReceipt,
                out _,
                out _),
            "A caller must not alter any signed authoritative receipt field.");
        Sr5CareerRecoveryResolution malformedProof = result.Resolution with
        {
            AuthorityProof = null!
        };
        Require(
            !store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                malformedProof,
                out _,
                out _),
            "A null or malformed authority proof must fail closed without escaping the store boundary.");
        Require(
            store.TryRead(out Sr5CareerDraftCheckpoint stillApplying, out blocker)
            && stillApplying == applying,
            "Forged resolutions must leave the exact Applying lock unchanged.");
        Require(
            store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerDraftCheckpoint applied,
                out blocker),
            blocker);
        Require(applied.Phase == Sr5CareerCheckpointPhase.Applied, "Verified apply must advance the checkpoint to Applied.");
    }

    private static async Task ApplyingCheckpointMustMatchCompleteReviewedDraftAsync()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint applying = Sr5CareerDraftCheckpoint.FromDraft(
            draft,
            Sr5CareerCheckpointPhase.Applying) with { Version = 2 };
        Sr5CareerDraftCheckpoint tampered = applying with
        {
            ExpenseReason = applying.ExpenseReason + " tampered"
        };
        FakePresenter presenter = FakePresenter.BeforeApply();
        presenter.ApplyHandler = _ => Task.FromResult(true);
        Sr5CareerActiveSkillCoordinator coordinator = new(
            presenter,
            new FixedOwnerAuthority(OwnerId));

        await RequireThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyAsync(
                draft,
                tampered,
                new Sr5CareerDraftCheckpointStore(new MemoryBackend())),
            "Apply must reject an Applying checkpoint whose non-identity plan fields differ from the reviewed draft.");
        Require(
            presenter.ApplyCalls == 0,
            "A tampered Applying checkpoint must be rejected before any mutation call.");
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
            await new Sr5CareerActiveSkillCoordinator(wrongSkill, new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(
            wrongSkillResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && wrongSkillResult.Receipt is null,
            "Wrong source identity must not produce a receipt.");

        FakePresenter missingExpense = FakePresenter.BeforeApply();
        missingExpense.PublishApplied(draft, includeExpense: false);
        Sr5CareerRecoveryResolution missingExpenseResult =
            await new Sr5CareerActiveSkillCoordinator(missingExpense, new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(
            missingExpenseResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && missingExpenseResult.Receipt is null,
            "A missing exact expense must not produce a receipt.");

        FakePresenter changedRuleEnvironment = FakePresenter.BeforeApply();
        changedRuleEnvironment.PublishApplied(draft, includeExpense: true);
        changedRuleEnvironment.Skills = changedRuleEnvironment.Skills! with
        {
            Skills = [Quote(
                karmaPoints: 2,
                totalRating: 4,
                availableKarma: draft.Plan.SavedCharacterKarma,
                rawRuleState: "<settings changed='true' />")]
        };
        Sr5CareerRecoveryResolution changedRuleResult =
            await new Sr5CareerActiveSkillCoordinator(
                changedRuleEnvironment,
                new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(
            changedRuleResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && changedRuleResult.Receipt is null,
            "A changed rule environment must not verify the reviewed advancement.");

        FakePresenter mismatchedSkillKarma = FakePresenter.BeforeApply();
        mismatchedSkillKarma.PublishApplied(draft, includeExpense: true);
        mismatchedSkillKarma.Skills = mismatchedSkillKarma.Skills! with
        {
            Skills = [Quote(
                karmaPoints: 2,
                totalRating: 4,
                availableKarma: draft.Plan.SavedCharacterKarma + 1)]
        };
        Sr5CareerRecoveryResolution mismatchedSkillKarmaResult =
            await new Sr5CareerActiveSkillCoordinator(
                mismatchedSkillKarma,
                new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(
            mismatchedSkillKarmaResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && mismatchedSkillKarmaResult.Receipt is null,
            "Skill and expense projections that disagree on saved Karma must not verify.");

        FakePresenter wrongExpense = FakePresenter.BeforeApply();
        wrongExpense.PublishApplied(draft, includeExpense: true);
        CharacterCareerKarmaExpenseEntry loadedExpense = wrongExpense.Expenses!.Expenses.Single();
        wrongExpense.Expenses = wrongExpense.Expenses with
        {
            Expenses = [loadedExpense with { RawKarmaUndoType = "AddSkill" }]
        };
        Sr5CareerRecoveryResolution wrongExpenseResult =
            await new Sr5CareerActiveSkillCoordinator(wrongExpense, new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(
            wrongExpenseResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && wrongExpenseResult.Receipt is null,
            "A mismatched exact expense undo type must not produce a receipt.");

        (string Name, Func<CharacterCareerKarmaExpenseEntry, CharacterCareerKarmaExpenseEntry> Tamper)[] tamperCases =
        [
            ("expense guid", expense => expense with { ExpenseId = Guid.NewGuid() }),
            ("expense date", expense => expense with { ExpenseDateLocal = expense.ExpenseDateLocal.AddSeconds(1) }),
            ("expense date kind", expense => expense with
            {
                ExpenseDateLocal = DateTime.SpecifyKind(expense.ExpenseDateLocal, DateTimeKind.Utc)
            }),
            ("expense amount", expense => expense with { Amount = expense.Amount + 1m }),
            ("expense reason", expense => expense with { Reason = expense.Reason + " tampered" }),
            ("refund", expense => expense with { Refund = true }),
            ("missing refund", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RefundElementPresent = false }
            }),
            ("forcecareervisible", expense => expense with { ForceCareerVisible = true }),
            ("missing forcecareervisible", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with
                {
                    ForceCareerVisibleElementPresent = false
                }
            }),
            ("undo karmatype", expense => expense with { RawKarmaUndoType = "AddSkill" }),
            ("undo karmatype case", expense => expense with { RawKarmaUndoType = expense.RawKarmaUndoType!.ToLowerInvariant() }),
            ("missing undo karmatype", expense => expense with
            {
                KarmaUndoTypeElementPresent = false,
                RawKarmaUndoType = null
            }),
            ("expense type", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RawExpenseType = "Nuyen" }
            }),
            ("expense type case", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RawExpenseType = "karma" }
            }),
            ("expense type whitespace", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RawExpenseType = " Karma" }
            }),
            ("missing expense type", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with
                {
                    ExpenseTypeElementPresent = false,
                    RawExpenseType = null
                }
            }),
            ("undo nuyentype", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RawNuyenUndoType = "ManualAdd" }
            }),
            ("undo nuyentype whitespace", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with
                {
                    RawNuyenUndoType = expense.SourceAuthority.RawNuyenUndoType + " "
                }
            }),
            ("missing undo nuyentype", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with
                {
                    NuyenUndoTypeElementPresent = false,
                    RawNuyenUndoType = null
                }
            }),
            ("undo objectid", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RawUndoObjectId = Guid.NewGuid().ToString("D") }
            }),
            ("malformed undo objectid", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RawUndoObjectId = "not-a-guid" }
            }),
            ("missing undo objectid", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with
                {
                    UndoObjectIdElementPresent = false,
                    RawUndoObjectId = null
                }
            }),
            ("undo qty", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { UndoQuantity = 1m }
            }),
            ("missing undo qty", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with
                {
                    UndoQuantityElementPresent = false,
                    UndoQuantity = null
                }
            }),
            ("undo extra", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RawUndoExtra = "tampered" }
            }),
            ("undo extra whitespace", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with { RawUndoExtra = " " }
            }),
            ("missing undo extra", expense => expense with
            {
                SourceAuthority = expense.SourceAuthority with
                {
                    UndoExtraElementPresent = false,
                    RawUndoExtra = null
                }
            })
        ];
        foreach ((string name, Func<CharacterCareerKarmaExpenseEntry, CharacterCareerKarmaExpenseEntry> tamper) in tamperCases)
        {
            FakePresenter presenter = FakePresenter.BeforeApply();
            presenter.PublishApplied(draft, includeExpense: true);
            CharacterCareerKarmaExpenseEntry expense = presenter.Expenses!.Expenses.Single();
            presenter.Expenses = presenter.Expenses with { Expenses = [tamper(expense)] };
            Sr5CareerRecoveryResolution resolution =
                await new Sr5CareerActiveSkillCoordinator(presenter, new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
            Require(
                resolution.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
                && resolution.Receipt is null,
                $"Tampered saved {name} must not produce a receipt.");
        }

        FakePresenter duplicateExpense = FakePresenter.BeforeApply();
        duplicateExpense.PublishApplied(draft, includeExpense: true);
        CharacterCareerKarmaExpenseEntry exact = duplicateExpense.Expenses!.Expenses.Single();
        duplicateExpense.Expenses = duplicateExpense.Expenses with { Expenses = [exact, exact] };
        Sr5CareerRecoveryResolution duplicateResult =
            await new Sr5CareerActiveSkillCoordinator(duplicateExpense, new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(
            duplicateResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && duplicateResult.Receipt is null,
            "Duplicate exact saved expense identities must not produce a receipt.");

        FakePresenter conflictingDuplicate = FakePresenter.BeforeApply();
        conflictingDuplicate.PublishApplied(draft, includeExpense: true);
        exact = conflictingDuplicate.Expenses!.Expenses.Single();
        conflictingDuplicate.Expenses = conflictingDuplicate.Expenses with
        {
            Expenses = [exact, exact with { Reason = exact.Reason + " conflict" }]
        };
        Sr5CareerRecoveryResolution conflictingDuplicateResult =
            await new Sr5CareerActiveSkillCoordinator(conflictingDuplicate, new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(
            conflictingDuplicateResult.Status == Sr5CareerRecoveryStatus.OutcomeUnknown
            && conflictingDuplicateResult.Receipt is null,
            "Conflicting duplicate saved expense identities must not produce a receipt.");
    }

    private static void CheckpointStoreEnforcesOwnerActionCasAndReadBack()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint reviewed = Sr5CareerDraftCheckpoint.FromDraft(draft);
        FakePresenter presenter = FakePresenter.BeforeApply();
        Sr5CareerLiveReviewedCheckpointAuthority authority = new(
            new FixedOwnerAuthority(OwnerId),
            new CareerActiveSkillAdvanceEditorState(
                draft.WorkspaceId,
                draft.ExpectedContentRevision,
                [draft.Quote],
                OmittedSkillCount: 0),
            () => presenter.Binding);
        MemoryBackend backend = new();
        Sr5CareerDraftCheckpointStore store = new(backend, authority);
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
        FakePresenter appliedPresenter = FakePresenter.BeforeApply();
        appliedPresenter.PublishApplied(Draft(), includeExpense: true);
        Sr5CareerActiveSkillReceipt receipt = Sr5CareerActiveSkillCoordinator.Resolve(
            applying,
            appliedPresenter.Binding,
            appliedPresenter.Skills,
            appliedPresenter.Expenses).Receipt!;
        Require(
            !store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applying),
                receipt,
                out _),
            "Applying cannot be blindly cleared.");

        MemoryBackend nondurableBackend = new() { DropWrites = true };
        Sr5CareerDraftCheckpointStore nondurable = new(
            nondurableBackend,
            authority);
        Require(
            !nondurable.TryCreate(reviewed, out _, out string durabilityBlocker)
            && durabilityBlocker.Contains("read-back", StringComparison.OrdinalIgnoreCase),
            "A write without exact read-back must fail durability proof.");
    }

    private static void ReviewedCheckpointAccessRejectsForeignOwnerWorkspaceAndBindings()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint reviewed = Sr5CareerDraftCheckpoint.FromDraft(draft);
        Sr5CareerRunnerBinding binding = FakePresenter.BeforeApply().Binding;
        Sr5CareerReviewedCheckpointAccess current =
            Sr5CareerReviewedCheckpointAccess.FromCurrent(OwnerId, draft, binding);
        Require(current.Owns(reviewed), "The exact current SR5 owner and runner must own its review.");

        Sr5CareerReviewedCheckpointAccess[] foreignAccesses =
        [
            current with { OwnerId = Guid.NewGuid() },
            current with { WorkspaceId = "foreign-workspace" },
            current with { ExpectedContentRevision = current.ExpectedContentRevision + 1 },
            current with { ActionId = Guid.NewGuid() },
            current with { IdempotencyKey = new string('a', 64) },
            current with { SchemaVersion = current.SchemaVersion + 1 },
            current with { RouteId = Sr5CareerWizardRoutes.ActiveSkillChoose },
            current with { CharacterCreated = false },
            current with { GameEdition = "SR6" }
        ];
        foreach (Sr5CareerReviewedCheckpointAccess foreign in foreignAccesses)
        {
            Require(!foreign.Owns(reviewed), "Every foreign owner/workspace/revision/action/schema/edition binding must fail closed.");
        }

        MemoryBackend backend = new();
        MutableReviewedAuthority reviewedAuthority = new(current);
        Sr5CareerDraftCheckpointStore store = new(
            backend,
            reviewedAuthority);
        Require(store.TryCreate(reviewed, out reviewed, out string blocker), blocker);
        reviewedAuthority.CurrentAccess = current with { OwnerId = Guid.NewGuid() };
        Require(
            !store.TryDeleteReviewed(
                Sr5CareerCheckpointCas.From(reviewed),
                out _),
            "A foreign owner must not delete a globally loaded Reviewed checkpoint.");
        Require(store.TryRead(out Sr5CareerDraftCheckpoint unchanged, out blocker), blocker);
        Require(unchanged == reviewed, "The foreign-owner delete attempt must leave the checkpoint intact.");
        reviewedAuthority.CurrentAccess = current with { WorkspaceId = "foreign-workspace" };
        Require(
            !store.TryDeleteReviewed(
                Sr5CareerCheckpointCas.From(reviewed),
                out _),
            "A foreign workspace must not delete a globally loaded Reviewed checkpoint.");
        Require(store.TryRead(out unchanged, out blocker), blocker);
        Require(unchanged == reviewed, "The foreign-workspace delete attempt must leave the checkpoint intact.");
        foreach (Sr5CareerReviewedCheckpointAccess foreign in foreignAccesses.Skip(2))
        {
            reviewedAuthority.CurrentAccess = foreign;
            Require(
                !store.TryDeleteReviewed(Sr5CareerCheckpointCas.From(reviewed), out _),
                "A changed live revision/action/schema/edition binding must prevent deletion.");
            Require(store.TryRead(out unchanged, out blocker), blocker);
            Require(unchanged == reviewed, "Every changed live binding must leave the checkpoint intact.");
        }
        reviewedAuthority.CurrentAccess = current;
        Require(
            store.TryDeleteReviewed(
                Sr5CareerCheckpointCas.From(reviewed),
                out blocker),
            blocker);
        Require(!store.TryRead(out _, out blocker) && string.IsNullOrWhiteSpace(blocker),
            "The exact authenticated current owner must be able to abandon its Reviewed checkpoint.");

        FakePresenter imported = FakePresenter.BeforeApply();
        Sr5CareerLiveReviewedCheckpointAuthority live = new(
            new FixedOwnerAuthority(OwnerId),
            new CareerActiveSkillAdvanceEditorState(
                draft.WorkspaceId,
                draft.ExpectedContentRevision,
                [draft.Quote],
                OmittedSkillCount: 0),
            () => imported.Binding);
        Sr5CareerDraftCheckpoint applying = reviewed with
        {
            Version = 2,
            Phase = Sr5CareerCheckpointPhase.Applying
        };
        Require(
            live.OwnsCurrentRunner(reviewed)
            && live.OwnsCurrentRunner(applying),
            "The exact clean saved revision must own Reviewed and Applying before the typed atomic save.");

        Sr5CareerRunnerBinding exactImportedBinding = imported.Binding;
        Sr5CareerRunnerBinding[] incoherentOrStaleBindings =
        [
            exactImportedBinding with
            {
                SavedRevision = exactImportedBinding.ContentRevision - 1,
                IsDirty = true
            },
            exactImportedBinding with { IsDirty = true },
            exactImportedBinding with { SavedRevision = exactImportedBinding.ContentRevision + 1 },
            exactImportedBinding with { ContentRevision = exactImportedBinding.ContentRevision - 1 },
            exactImportedBinding with { Error = "workspace failure" },
            exactImportedBinding with { WorkspaceId = new CharacterWorkspaceId("foreign-workspace") }
        ];
        foreach (Sr5CareerRunnerBinding hostile in incoherentOrStaleBindings)
        {
            imported.BindingValue = hostile;
            Require(
                !live.OwnsCurrentRunner(reviewed)
                && !live.OwnsCurrentRunner(applying),
                "Incoherent, stale, failed, or foreign runner state must not own the typed checkpoint.");
        }

        foreach ((long contentRevision, long savedRevision) in new[]
        {
            (ContentRevision: 2L, SavedRevision: 0L),
            (ContentRevision: 5L, SavedRevision: 3L)
        })
        {
            Sr5CareerActiveSkillDraft dirtyDraft = Draft(contentRevision);
            Sr5CareerDraftCheckpoint dirtyReviewed =
                Sr5CareerDraftCheckpoint.FromDraft(dirtyDraft);
            Sr5CareerDraftCheckpoint dirtyApplying = dirtyReviewed with
            {
                Version = 2,
                Phase = Sr5CareerCheckpointPhase.Applying
            };
            FakePresenter dirty = FakePresenter.BeforeApply();
            dirty.BindingValue = dirty.BindingValue with
            {
                ContentRevision = contentRevision,
                SavedRevision = savedRevision,
                IsDirty = true
            };
            Sr5CareerLiveReviewedCheckpointAuthority dirtyAuthority = new(
                new FixedOwnerAuthority(OwnerId),
                new CareerActiveSkillAdvanceEditorState(
                    dirtyDraft.WorkspaceId,
                    dirtyDraft.ExpectedContentRevision,
                    [dirtyDraft.Quote],
                    OmittedSkillCount: 0),
                () => dirty.Binding);
            Require(
                !dirtyAuthority.Owns(dirtyReviewed)
                && !dirtyAuthority.OwnsCurrentRunner(dirtyReviewed)
                && !dirtyAuthority.OwnsCurrentRunner(dirtyApplying),
                $"Dirty {contentRevision}/{savedRevision} must not create or own Reviewed or Applying.");
        }

        imported.BindingValue = exactImportedBinding;

        imported.PublishApplied(draft, includeExpense: true);
        Sr5CareerDraftCheckpoint applied = applying with
        {
            Version = 3,
            Phase = Sr5CareerCheckpointPhase.Applied
        };
        Require(
            live.OwnsCurrentRunner(applying)
            && live.OwnsCurrentRunner(applied),
            "Only the exact clean saved successor may own Applying recovery and Applied receipt state.");
        imported.BindingValue = imported.BindingValue with { IsDirty = true };
        Require(
            !live.OwnsCurrentRunner(applying)
            && !live.OwnsCurrentRunner(applied),
            "A dirty successor must remain unresolved and must not own an Applying or Applied receipt transition.");
    }

    private static void PriorSchemaCheckpointRemainsAReplayBlockingLock()
    {
        Sr5CareerDraftCheckpoint legacy = Sr5CareerDraftCheckpoint.FromDraft(
            Draft(),
            Sr5CareerCheckpointPhase.Applying) with
        {
            SchemaVersion = Sr5CareerDraftCheckpoint.CurrentSchemaVersion - 1,
            Version = 2
        };
        string legacyPayload = JsonSerializer.Serialize(legacy);
        MemoryBackend backend = new(legacyPayload);
        Sr5CareerDraftCheckpointStore store = new(backend);

        Require(
            !store.TryRead(out _, out string readBlocker)
            && readBlocker.Contains("unreadable", StringComparison.OrdinalIgnoreCase),
            "A prior-schema Applying checkpoint must fail closed rather than disappear.");
        Require(
            !store.TryCreate(
                Sr5CareerDraftCheckpoint.FromDraft(Draft()),
                out _,
                out string createBlocker)
            && !string.IsNullOrWhiteSpace(createBlocker),
            "A prior-schema Applying lock must block creation and replay under the current schema.");
        Require(
            backend.Read() == legacyPayload,
            "A prior-schema Applying lock must remain durable for explicit recovery instead of being deleted.");
    }

    private static async Task AppliedCheckpointAcknowledgementRequiresLiveOwnerAndExactReceiptAsync()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        Sr5CareerDraftCheckpoint reviewed = Sr5CareerDraftCheckpoint.FromDraft(draft);
        MutableOwnerAuthority ownerAuthority = new(OwnerId);
        Sr5CareerRunnerBinding liveBinding = FakePresenter.BeforeApply().Binding;
        CareerActiveSkillAdvanceEditorState reviewedEditor = new(
            draft.WorkspaceId,
            draft.ExpectedContentRevision,
            [draft.Quote],
            OmittedSkillCount: 0);
        Sr5CareerLiveReviewedCheckpointAuthority checkpointAuthority = new(
            ownerAuthority,
            reviewedEditor,
            () => liveBinding);
        MemoryBackend backend = new();
        Sr5CareerDraftCheckpointStore store = new(backend, checkpointAuthority);
        Require(store.TryCreate(reviewed, out reviewed, out string blocker), blocker);
        Require(
            store.TryBeginApply(
                Sr5CareerCheckpointCas.From(reviewed),
                out Sr5CareerDraftCheckpoint applying,
                out blocker),
            blocker);

        FakePresenter presenter = FakePresenter.BeforeApply();
        presenter.PublishApplied(draft, includeExpense: true);
        liveBinding = presenter.Binding;
        Sr5CareerRecoveryResolution resolution = await new Sr5CareerActiveSkillCoordinator(
            presenter,
            new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(resolution.Status == Sr5CareerRecoveryStatus.AppliedVerified, resolution.Message);
        ownerAuthority.CurrentOwnerId = Guid.NewGuid();
        Require(
            !store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                resolution,
                out _,
                out _),
            "A valid resolution must not commit after the authenticated local owner changes.");
        Require(
            store.TryRead(out Sr5CareerDraftCheckpoint unchangedApplying, out blocker)
            && unchangedApplying == applying,
            "A foreign-owner resolution commit must preserve the Applying lock.");
        ownerAuthority.CurrentOwnerId = OwnerId;
        Sr5CareerRunnerBinding exactResolvedBinding = liveBinding;
        liveBinding = liveBinding with
        {
            WorkspaceId = new CharacterWorkspaceId("foreign-workspace")
        };
        Require(
            !store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                resolution,
                out _,
                out _),
            "A valid resolution must not commit after the live workspace changes.");
        Require(
            store.TryRead(out unchangedApplying, out blocker)
            && unchangedApplying == applying,
            "A foreign-workspace resolution commit must preserve the Applying lock.");
        liveBinding = exactResolvedBinding;
        Require(
            store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                resolution,
                out Sr5CareerDraftCheckpoint applied,
                out blocker),
            blocker);
        Sr5CareerActiveSkillReceipt receipt = resolution.Receipt!;

        ownerAuthority.CurrentOwnerId = Guid.NewGuid();
        Require(
            !store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applied),
                receipt,
                out _),
            "A foreign local owner must not acknowledge or delete an Applied checkpoint.");
        Require(store.TryRead(out Sr5CareerDraftCheckpoint unchanged, out blocker), blocker);
        Require(unchanged == applied, "A foreign-owner acknowledgement must preserve the lock.");

        ownerAuthority.CurrentOwnerId = OwnerId;
        Sr5CareerRunnerBinding exactSavedBinding = liveBinding;
        liveBinding = liveBinding with
        {
            WorkspaceId = new CharacterWorkspaceId("foreign-workspace")
        };
        Require(
            !store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applied),
                receipt,
                out _),
            "A foreign workspace must not acknowledge or delete an Applied checkpoint.");
        Require(store.TryRead(out unchanged, out blocker), blocker);
        Require(unchanged == applied, "A foreign-workspace acknowledgement must preserve the lock.");

        liveBinding = exactSavedBinding with { IsDirty = true };
        Require(
            !store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applied),
                receipt,
                out _),
            "A dirty live runner must not acknowledge or delete an Applied checkpoint.");
        Require(store.TryRead(out unchanged, out blocker), blocker);
        Require(unchanged == applied, "A dirty-runner acknowledgement must preserve the lock.");

        liveBinding = exactSavedBinding with
        {
            ContentRevision = exactSavedBinding.ContentRevision + 1,
            SavedRevision = exactSavedBinding.SavedRevision + 1
        };
        Require(
            !store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applied),
                receipt,
                out _),
            "A later live revision must not acknowledge an older Applied checkpoint.");
        Require(store.TryRead(out unchanged, out blocker), blocker);
        Require(unchanged == applied, "A later-revision acknowledgement must preserve the lock.");

        liveBinding = exactSavedBinding;
        Require(
            !store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applied),
                receipt with { IdempotencyKey = new string('0', 64) },
                out _),
            "A receipt with a foreign action binding must not delete the checkpoint.");
        Require(store.TryRead(out unchanged, out blocker), blocker);
        Require(unchanged == applied, "A mismatched receipt acknowledgement must preserve the lock.");

        Require(
            !store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applied),
                receipt with { LogicalRevision = new string('0', 64) },
                out _),
            "A receipt with forged loaded logical authority must not delete the checkpoint.");
        Require(store.TryRead(out unchanged, out blocker), blocker);
        Require(unchanged == applied, "A forged logical revision must preserve the lock.");

        Require(
            !store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applied),
                receipt with { RuleDigest = new string('0', 64) },
                out _),
            "A receipt with forged loaded rule authority must not delete the checkpoint.");
        Require(store.TryRead(out unchanged, out blocker), blocker);
        Require(unchanged == applied, "A forged rule digest must preserve the lock.");

        Require(
            store.TryDeleteApplied(
                Sr5CareerCheckpointCas.From(applied),
                receipt,
                out blocker),
            blocker);
        Require(
            !store.TryRead(out _, out blocker) && string.IsNullOrWhiteSpace(blocker),
            "Only the live exact owner and receipt may durably acknowledge the Applied checkpoint.");
    }

    private static async Task ApplyingCrashIsResolvedWithoutReplayAsync()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        MemoryBackend backend = new();
        MemoryBackend mutationOwnerBackend = new();
        FakePresenter presenter = FakePresenter.BeforeApply();
        Sr5CareerLiveReviewedCheckpointAuthority firstProcessAuthority = new(
            new FixedOwnerAuthority(OwnerId),
            new CareerActiveSkillAdvanceEditorState(
                draft.WorkspaceId,
                draft.ExpectedContentRevision,
                [draft.Quote],
                OmittedSkillCount: 0),
            () => presenter.Binding);
        Sr5CareerDraftCheckpointStore firstProcessStore = new(
            backend,
            firstProcessAuthority,
            new Sr5CareerMutationOwnerStore(mutationOwnerBackend));
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

        presenter.ApplyHandler = _ =>
        {
            presenter.PublishApplied(draft, includeExpense: true);
            throw new InvalidOperationException("simulated process death after durable save");
        };
        Sr5CareerActiveSkillCoordinator firstProcess = new(
            presenter,
            new FixedOwnerAuthority(OwnerId));
        await RequireThrowsAsync<InvalidOperationException>(
            () => firstProcess.ApplyAsync(draft, applying, firstProcessStore),
            "The simulated crash must escape while the checkpoint remains Applying.");
        Require(presenter.ApplyCalls == 1, "The first process must attempt apply exactly once.");

        Sr5CareerLiveReviewedCheckpointAuthority restartedAuthority = new(
            new FixedOwnerAuthority(OwnerId),
            new CareerActiveSkillAdvanceEditorState(
                draft.WorkspaceId,
                draft.ExpectedContentRevision,
                [draft.Quote],
                OmittedSkillCount: 0),
            () => presenter.Binding);
        Sr5CareerDraftCheckpointStore restartedStore = new(
            backend,
            restartedAuthority,
            new Sr5CareerMutationOwnerStore(mutationOwnerBackend));
        Require(restartedStore.TryRead(out Sr5CareerDraftCheckpoint recovered, out blocker), blocker);
        Require(recovered.Phase == Sr5CareerCheckpointPhase.Applying, "Restart must observe the durable Applying phase.");
        Sr5CareerRecoveryResolution resolution =
            await new Sr5CareerActiveSkillCoordinator(presenter, new FixedOwnerAuthority(OwnerId)).ResolveAsync(recovered);
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

#if AUTHORITY_LIGHTWEIGHT
    private static async Task SavedExpenseXmlProjectionIsPresenceAwareAndMalformedValuesFailClosedAsync()
    {
        Sr5CareerActiveSkillDraft draft = Draft();
        string exactXml = $"<character><created>True</created><karma>{draft.Plan.SavedCharacterKarma}</karma>"
            + "<expenses><expense>"
            + $"<guid>{draft.Plan.ExpenseId:D}</guid>"
            + $"<date>{draft.Plan.ExpenseDateLocal:s}</date>"
            + $"<amount>{draft.Plan.ExpenseAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}</amount>"
            + $"<reason>{draft.Plan.ExpenseReason}</reason>"
            + "<type>Karma</type><refund>False</refund><forcecareervisible>False</forcecareervisible>"
            + "<undo>"
            + $"<karmatype>{draft.Plan.KarmaUndoType}</karmatype>"
            + $"<nuyentype>{draft.Plan.NuyenUndoType}</nuyentype>"
            + $"<objectid>{draft.Plan.UndoObjectId}</objectid>"
            + $"<qty>{draft.Plan.UndoQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}</qty>"
            + $"<extra>{draft.Plan.UndoExtra}</extra>"
            + "</undo></expense></expenses></character>";
        CareerKarmaExpenseEditorState projected = CareerKarmaExpenseEditorProjector.Project(
            exactXml,
            WorkspaceId,
            42);
        CharacterCareerKarmaExpenseEntry expense = projected.Expenses.Single();
        Require(expense.SourceAuthority.ExpenseTypeElementPresent, "Exact expense type presence must be projected.");
        Require(expense.SourceAuthority.RefundElementPresent, "Exact refund presence must be projected.");
        Require(expense.SourceAuthority.ForceCareerVisibleElementPresent, "Exact force-visible presence must be projected.");
        Require(expense.SourceAuthority.NuyenUndoTypeElementPresent, "Exact Nuyen undo type presence must be projected.");
        Require(expense.SourceAuthority.UndoObjectIdElementPresent, "Exact undo object presence must be projected.");
        Require(expense.SourceAuthority.UndoQuantityElementPresent, "Exact undo quantity presence must be projected.");
        Require(expense.SourceAuthority.UndoExtraElementPresent, "Exact undo extra presence must be projected.");

        FakePresenter presenter = FakePresenter.BeforeApply();
        presenter.PublishApplied(draft, includeExpense: false);
        presenter.Expenses = projected;
        Sr5CareerDraftCheckpoint applying = Sr5CareerDraftCheckpoint.FromDraft(
            draft,
            Sr5CareerCheckpointPhase.Applying) with { Version = 2 };
        Sr5CareerRecoveryResolution exact =
            await new Sr5CareerActiveSkillCoordinator(presenter, new FixedOwnerAuthority(OwnerId)).ResolveAsync(applying);
        Require(exact.Status == Sr5CareerRecoveryStatus.AppliedVerified, exact.Message);

        string[] malformedPayloads =
        [
            exactXml.Replace("<guid>", "<guid>not-a-guid</guid><guid>"),
            exactXml.Replace("<date>", "<date>not-a-date</date><date>"),
            exactXml.Replace("<amount>", "<amount>not-an-amount</amount><amount>"),
            exactXml.Replace("<refund>False</refund>", "<refund>not-a-bool</refund>"),
            exactXml.Replace("<forcecareervisible>False</forcecareervisible>", "<forcecareervisible>not-a-bool</forcecareervisible>"),
            exactXml.Replace("<type>Karma</type>", "<type>Karma</type><type>Karma</type>"),
            exactXml.Replace("</karmatype>", $"</karmatype><karmatype>{draft.Plan.KarmaUndoType}</karmatype>"),
            exactXml.Replace("</nuyentype>", $"</nuyentype><nuyentype>{draft.Plan.NuyenUndoType}</nuyentype>"),
            exactXml.Replace("</objectid>", $"</objectid><objectid>{draft.Plan.UndoObjectId}</objectid>"),
            exactXml.Replace("<qty>0</qty>", "<qty>not-a-decimal</qty>"),
            exactXml.Replace("</qty>", "</qty><qty>0</qty>"),
            exactXml.Replace("</extra>", "</extra><extra></extra>"),
            exactXml.Replace("</undo>", "</undo><undo></undo>")
        ];
        foreach (string malformed in malformedPayloads)
        {
            RequireThrows<InvalidOperationException>(
                () => CareerKarmaExpenseEditorProjector.Project(malformed, WorkspaceId, 42),
                "Malformed or duplicate saved expense source values must fail projection closed.");
        }
    }
#endif

    private static Sr5CareerActiveSkillDraft Draft(long contentRevision = 41)
    {
        CharacterCareerActiveSkillAdvanceQuote quote = Quote(
            karmaPoints: 1,
            totalRating: 3,
            availableKarma: 20);
        CareerActiveSkillAdvanceEditorState editor = new(
            WorkspaceId,
            contentRevision,
            [quote],
            0);
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
        int availableKarma,
        string rawRuleState = "<settings />")
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
            RawRuleState: rawRuleState);
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
                new CharacterCareerKarmaExpenseSourceAuthority(
                    ExpenseTypeElementPresent: true,
                    RawExpenseType: "Karma",
                    RefundElementPresent: true,
                    ForceCareerVisibleElementPresent: true,
                    NuyenUndoTypeElementPresent: true,
                    RawNuyenUndoType: draft.Plan.NuyenUndoType,
                    UndoObjectIdElementPresent: true,
                    RawUndoObjectId: draft.Plan.UndoObjectId,
                    UndoQuantityElementPresent: true,
                    UndoQuantity: draft.Plan.UndoQuantity,
                    UndoExtraElementPresent: true,
                    RawUndoExtra: draft.Plan.UndoExtra),
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

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
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
        private string _payload;
        public bool DropWrites { get; init; }

        public MemoryBackend(string payload = "")
        {
            _payload = payload;
        }

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

    private sealed class MutableReviewedAuthority(
        Sr5CareerReviewedCheckpointAccess currentAccess) :
        ISr5CareerReviewedCheckpointAuthority
    {
        public Sr5CareerReviewedCheckpointAccess CurrentAccess { get; set; } = currentAccess;
        public Guid CurrentOwnerId => CurrentAccess.OwnerId;
        public bool Owns(Sr5CareerDraftCheckpoint checkpoint)
            => CurrentAccess.Owns(checkpoint);
        public bool OwnsCurrentRunner(Sr5CareerDraftCheckpoint checkpoint)
            => CurrentAccess.OwnerId == checkpoint.OwnerId
               && string.Equals(
                   CurrentAccess.WorkspaceId,
                   checkpoint.WorkspaceId,
                   StringComparison.Ordinal)
               && CurrentAccess.CharacterCreated
               && string.Equals(CurrentAccess.GameEdition, "SR5", StringComparison.OrdinalIgnoreCase)
               && checkpoint.IsStructurallyValid();
    }

    private sealed class FixedOwnerAuthority(Guid currentOwnerId) :
        ISr5CareerCheckpointOwnerAuthority
    {
        public Guid CurrentOwnerId { get; } = currentOwnerId;
    }

    private sealed class MutableOwnerAuthority(Guid currentOwnerId) :
        ISr5CareerCheckpointOwnerAuthority
    {
        public Guid CurrentOwnerId { get; set; } = currentOwnerId;
    }
}
