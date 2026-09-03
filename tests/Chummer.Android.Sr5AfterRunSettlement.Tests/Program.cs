using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using System.Text.Json;

return await AfterRunAuthorityHarness.RunAsync();

internal static class AfterRunAuthorityHarness
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("workspace-41");
    private static readonly CharacterAfterRunSettlementIdentity Identity = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly Guid OwnerId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TransactionId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static async Task<int> RunAsync()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            (nameof(ExactDraftBindsProposalReviewsDigestsAndPlan),
                () => Sync(ExactDraftBindsProposalReviewsDigestsAndPlan)),
            (nameof(AcknowledgementsPersistExactlySixInputsAndIgnoreDerivedInjection),
                () => Sync(AcknowledgementsPersistExactlySixInputsAndIgnoreDerivedInjection)),
            (nameof(PendingReviewsAndUnavailableCompositionFailClosed),
                () => Sync(PendingReviewsAndUnavailableCompositionFailClosed)),
            (nameof(EntryGuardRejectsEveryNonCleanSavedBinding),
                () => Sync(EntryGuardRejectsEveryNonCleanSavedBinding)),
            (nameof(CheckpointCasRejectsTamperingAndMalformedPayloadLocks),
                () => Sync(CheckpointCasRejectsTamperingAndMalformedPayloadLocks)),
            (nameof(SharedMutationOwnerBlocksCrossLaneWhileOutcomeUnknownAsync),
                SharedMutationOwnerBlocksCrossLaneWhileOutcomeUnknownAsync),
            (nameof(ExactAtomicResultPersistsCoreReceiptAsync),
                ExactAtomicResultPersistsCoreReceiptAsync),
            (nameof(RestartReplaysOnlyExactCommandAndRecoversReceiptAsync),
                RestartReplaysOnlyExactCommandAndRecoversReceiptAsync),
            (nameof(ManualProposalPublishesToBothSeamsAndSurvivesRestart),
                () => Sync(ManualProposalPublishesToBothSeamsAndSurvivesRestart)),
            (nameof(IncompleteStaleAndTamperedManualProposalsFailClosed),
                () => Sync(IncompleteStaleAndTamperedManualProposalsFailClosed)),
        };

        int failed = 0;
        foreach ((string name, Func<Task> run) in tests)
        {
            try
            {
                await run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} managed After Run authority tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static Task Sync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static void ExactDraftBindsProposalReviewsDigestsAndPlan()
    {
        Sr5AfterRunSettlementEditorState editor = Editor(Input());
        Sr5AfterRunSettlementDraft draft = Draft(editor);
        Require(draft.IsExact(), "Exact reviewed draft was rejected.");
        Require(draft.Binding.Identity == Identity, "Typed proposal/run/character identity changed.");
        Require(draft.Acknowledgements.AllReviewed, "All six explicit review acknowledgements were not retained.");
        Require(draft.Plan.GmReviewDigest == draft.Quote.GmReviewDigest, "GM review digest was lost.");
        Require(draft.Plan.OwnerReviewDigest == draft.Quote.OwnerReviewDigest, "Owner review digest was lost.");
        Require(draft.ToCommand().ExpectedBindingDigest == draft.Binding.BindingDigest, "Binding digest was lost.");
        Require(draft.ActionPlan.Kind == Sr5CareerActionKind.AfterRunSettlement, "Wrong action kind.");
        Require(draft.ActionPlan.IdempotencyKey.Length == 64, "Idempotency key is not canonical.");
        Require(draft.Candidate.RewardContext.IsExact(), "Reward context is not digest-bound.");
    }

    private static void AcknowledgementsPersistExactlySixInputsAndIgnoreDerivedInjection()
    {
        string[] expectedFields =
        [
            nameof(Sr5AfterRunReviewAcknowledgements.RunContextReviewed),
            nameof(Sr5AfterRunReviewAcknowledgements.RewardsReviewed),
            nameof(Sr5AfterRunReviewAcknowledgements.ConsequencesReviewed),
            nameof(Sr5AfterRunReviewAcknowledgements.ContactsReviewed),
            nameof(Sr5AfterRunReviewAcknowledgements.GmApprovalReviewed),
            nameof(Sr5AfterRunReviewAcknowledgements.OwnerApprovalReviewed)
        ];
        Sr5AfterRunReviewAcknowledgements reviewed = AllReviewed();
        string payload = JsonSerializer.Serialize(reviewed);
        using (JsonDocument document = JsonDocument.Parse(payload))
        {
            string[] actualFields = document.RootElement.EnumerateObject()
                .Select(static property => property.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            Require(actualFields.SequenceEqual(
                    expectedFields.OrderBy(static name => name, StringComparer.Ordinal),
                    StringComparer.Ordinal),
                $"Review acknowledgements persisted fields outside the closed six-input schema: {payload}");
            Require(!document.RootElement.TryGetProperty(
                    nameof(Sr5AfterRunReviewAcknowledgements.AllReviewed),
                    out _),
                "Derived AllReviewed leaked into the durable checkpoint schema.");
        }

        Sr5AfterRunReviewAcknowledgements? roundTrip =
            JsonSerializer.Deserialize<Sr5AfterRunReviewAcknowledgements>(payload);
        Require(roundTrip == reviewed && roundTrip.AllReviewed,
            "Six-input review acknowledgements did not round-trip exactly.");

        Sr5AfterRunSettlementCheckpoint checkpoint =
            Sr5AfterRunSettlementCheckpoint.FromDraft(Draft(Editor(Input())));
        string checkpointPayload = JsonSerializer.Serialize(checkpoint);
        using (JsonDocument checkpointDocument = JsonDocument.Parse(checkpointPayload))
        {
            JsonElement persistedAcknowledgements = checkpointDocument.RootElement
                .GetProperty(nameof(Sr5AfterRunSettlementCheckpoint.Draft))
                .GetProperty(nameof(Sr5AfterRunSettlementDraft.Acknowledgements));
            string[] checkpointFields = persistedAcknowledgements.EnumerateObject()
                .Select(static property => property.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            Require(checkpointFields.SequenceEqual(
                    expectedFields.OrderBy(static name => name, StringComparer.Ordinal),
                    StringComparer.Ordinal),
                "The actual durable checkpoint did not embed exactly six acknowledgement inputs.");
        }
        Sr5AfterRunSettlementCheckpoint? checkpointRoundTrip =
            JsonSerializer.Deserialize<Sr5AfterRunSettlementCheckpoint>(checkpointPayload);
        Require(checkpointRoundTrip is not null
                && checkpointRoundTrip.IsStructurallyValid()
                && checkpointRoundTrip.Draft.Acknowledgements == reviewed
                && checkpointRoundTrip.Draft.SemanticallyEquals(checkpoint.Draft),
            "The actual durable After Run checkpoint did not round-trip exactly.");

        string hostilePayload = payload[..^1]
            + $",\"{nameof(Sr5AfterRunReviewAcknowledgements.AllReviewed)}\":false}}";
        Sr5AfterRunReviewAcknowledgements? hostile =
            JsonSerializer.Deserialize<Sr5AfterRunReviewAcknowledgements>(hostilePayload);
        Require(hostile == reviewed && hostile.AllReviewed,
            "Injected derived AllReviewed changed the six authoritative review inputs.");
        string normalized = JsonSerializer.Serialize(hostile);
        Require(!normalized.Contains(
                $"\"{nameof(Sr5AfterRunReviewAcknowledgements.AllReviewed)}\"",
                StringComparison.Ordinal),
            "A hostile derived field survived acknowledgement normalization.");

        Sr5AfterRunReviewAcknowledgements pending = reviewed with
        {
            OwnerApprovalReviewed = false
        };
        Sr5AfterRunReviewAcknowledgements? pendingRoundTrip =
            JsonSerializer.Deserialize<Sr5AfterRunReviewAcknowledgements>(
                JsonSerializer.Serialize(pending));
        Require(pendingRoundTrip == pending && !pendingRoundTrip.AllReviewed,
            "Round-trip fabricated complete review from one false authoritative input.");
    }

    private static void PendingReviewsAndUnavailableCompositionFailClosed()
    {
        Sr5AfterRunSettlementEditorState pending = Editor(Input() with
        {
            OwnerReview = null
        });
        bool created = Sr5AfterRunSettlementDraft.TryCreate(
            pending,
            pending.Candidates.Single(),
            OwnerId,
            TransactionId,
            AllReviewed(),
            out _,
            out string blocker);
        Require(!created, "Pending owner review became a settlement draft.");
        Require(blocker.Contains("owner", StringComparison.OrdinalIgnoreCase),
            "Pending owner review did not explain the blocker.");

        Sr5AfterRunSettlementEditorState unavailable =
            Sr5AfterRunSettlementEditorState.Unavailable(
                WorkspaceId,
                41,
                "workspace adapter unavailable");
        Require(unavailable.IsExact(), "Explicit unavailable projection is incoherent.");
        Require(!Sr5AfterRunSettlementDraft.TryCreate(
                unavailable,
                null,
                OwnerId,
                TransactionId,
                AllReviewed(),
                out _,
                out _),
            "Unavailable default composition fabricated a draft.");
    }

    private static void EntryGuardRejectsEveryNonCleanSavedBinding()
    {
        Sr5CareerRunnerBinding exact = Binding(41);
        Require(Sr5AfterRunSettlementEntryGuard.TryValidate(exact, out string blocker),
            blocker);

        Sr5CareerRunnerBinding[] hostile =
        [
            exact with { SavedRevision = 40 },
            exact with { IsDirty = true },
            exact with { Error = "workspace read failed" },
            exact with { Created = false },
            exact with { GameEdition = "SR6" },
            exact with { WorkspaceId = null }
        ];
        foreach (Sr5CareerRunnerBinding binding in hostile)
        {
            Require(!Sr5AfterRunSettlementEntryGuard.TryValidate(
                    binding,
                    out blocker),
                "A non-clean/saved runner binding opened After Run settlement.");
            Require(!string.IsNullOrWhiteSpace(blocker),
                "A rejected After Run binding did not expose a blocker.");
        }

        foreach (Sr5CareerRunnerBinding binding in hostile)
        {
            bool rejected = false;
            try
            {
                Sr5AfterRunSettlementEntryGuard.Require(binding);
            }
            catch (InvalidOperationException exception)
            {
                rejected = true;
                Require(!string.IsNullOrWhiteSpace(exception.Message),
                    "The throwing guard lost its explicit blocker.");
            }
            Require(rejected,
                "A hostile After Run binding bypassed the throwing guard.");
        }
    }

    private static void CheckpointCasRejectsTamperingAndMalformedPayloadLocks()
    {
        Sr5AfterRunSettlementEditorState editor = Editor(Input());
        Sr5AfterRunSettlementDraft draft = Draft(editor);
        var presenter = new FakePresenter(Binding(41));
        var owner = new TestOwner(OwnerId);
        var authority = new Sr5AfterRunSettlementLiveCheckpointAuthority(
            owner,
            editor,
            () => presenter.Binding);
        var backend = new MemoryBackend();
        var store = new Sr5AfterRunSettlementCheckpointStore(
            backend,
            authority,
            new Sr5CareerMutationOwnerStore(new MemoryBackend()));
        Sr5AfterRunSettlementCheckpoint reviewed =
            Sr5AfterRunSettlementCheckpoint.FromDraft(draft);
        Require(store.TryCreate(reviewed, out Sr5AfterRunSettlementCheckpoint stored, out string blocker), blocker);
        Sr5AfterRunSettlementCheckpointCas forged =
            Sr5AfterRunSettlementCheckpointCas.From(stored) with { Version = 999 };
        Require(!store.TryBeginApply(forged, out _, out blocker),
            "Forged CAS began settlement apply.");
        Require(store.TryDeleteReviewed(
            Sr5AfterRunSettlementCheckpointCas.From(stored), out blocker), blocker);

        backend.Payload = "{malformed";
        Require(!store.TryRead(out _, out blocker), "Malformed journal was readable.");
        Require(blocker.Contains("replay-blocking", StringComparison.Ordinal),
            "Malformed journal did not remain a replay-blocking lock.");
        Require(!store.TryCreate(reviewed, out _, out blocker),
            "Malformed prior journal was silently overwritten.");
    }

    private static async Task SharedMutationOwnerBlocksCrossLaneWhileOutcomeUnknownAsync()
    {
        Sr5AfterRunSettlementEditorState editor = Editor(Input());
        Sr5AfterRunSettlementDraft draft = Draft(editor);
        var presenter = new FakePresenter(Binding(41))
        {
            SettleHandler = _ => null
        };
        var owner = new TestOwner(OwnerId);
        var checkpointBackend = new MemoryBackend();
        var ownerBackend = new MemoryBackend();
        var mutationOwners = new Sr5CareerMutationOwnerStore(ownerBackend);
        var authority = new Sr5AfterRunSettlementLiveCheckpointAuthority(
            owner,
            editor,
            () => presenter.Binding);
        var store = new Sr5AfterRunSettlementCheckpointStore(
            checkpointBackend,
            authority,
            mutationOwners);
        Require(store.TryCreate(
            Sr5AfterRunSettlementCheckpoint.FromDraft(draft),
            out Sr5AfterRunSettlementCheckpoint reviewed,
            out string blocker), blocker);
        Require(store.TryBeginApply(
            Sr5AfterRunSettlementCheckpointCas.From(reviewed),
            out Sr5AfterRunSettlementCheckpoint applying,
            out blocker), blocker);

        var coordinator = new Sr5AfterRunSettlementCoordinator(presenter, owner);
        Sr5AfterRunSettlementApplyResult result = await coordinator.ApplyAsync(
            draft,
            applying,
            store);
        Require(result.Status == Sr5AfterRunSettlementApplyStatus.OutcomeUnknown,
            "Missing service result did not remain OutcomeUnknown.");
        Require(!store.TryRecordAuthoritativeResolution(
                Sr5AfterRunSettlementCheckpointCas.From(applying),
                result.Resolution,
                out _,
                out _),
            "OutcomeUnknown advanced the durable checkpoint.");

        var restartedOwners = new Sr5CareerMutationOwnerStore(ownerBackend);
        Require(!restartedOwners.TryRunWhenUnowned(
                () => (true, string.Empty),
                out blocker),
            "A foreign Career lane bypassed the unresolved After Run owner.");
        Require(blocker.Contains("unresolved", StringComparison.OrdinalIgnoreCase),
            "Cross-lane durable owner blocker was not visible.");
    }

    private static async Task ExactAtomicResultPersistsCoreReceiptAsync()
    {
        Sr5AfterRunSettlementEditorState editor = Editor(Input());
        Sr5AfterRunSettlementDraft draft = Draft(editor);
        CharacterAfterRunSettlementReceipt receipt = Receipt(draft);
        var presenter = new FakePresenter(Binding(41));
        presenter.SettleHandler = command =>
        {
            presenter.Binding = Binding(42);
            return SuccessResult(draft, receipt, command, replayed: false);
        };
        var owner = new TestOwner(OwnerId);
        var ownerBackend = new MemoryBackend();
        var authority = new Sr5AfterRunSettlementLiveCheckpointAuthority(
            owner,
            editor,
            () => presenter.Binding);
        var store = new Sr5AfterRunSettlementCheckpointStore(
            new MemoryBackend(),
            authority,
            new Sr5CareerMutationOwnerStore(ownerBackend));
        Require(store.TryCreate(
            Sr5AfterRunSettlementCheckpoint.FromDraft(draft),
            out Sr5AfterRunSettlementCheckpoint reviewed,
            out string blocker), blocker);
        Require(store.TryBeginApply(
            Sr5AfterRunSettlementCheckpointCas.From(reviewed),
            out Sr5AfterRunSettlementCheckpoint applying,
            out blocker), blocker);

        var coordinator = new Sr5AfterRunSettlementCoordinator(presenter, owner);
        Sr5AfterRunSettlementApplyResult result = await coordinator.ApplyAsync(
            draft,
            applying,
            store);
        Require(result.Status == Sr5AfterRunSettlementApplyStatus.Applied,
            result.Message);
        Require(store.TryRecordAuthoritativeResolution(
            Sr5AfterRunSettlementCheckpointCas.From(applying),
            result.Resolution,
            out Sr5AfterRunSettlementCheckpoint applied,
            out blocker), blocker);
        Require(applied.Phase == Sr5CareerCheckpointPhase.Applied,
            "Applied receipt was not durably journaled.");
        Require(applied.Receipt?.ReceiptDigest == receipt.ReceiptDigest,
            "Wrong Core receipt was stored.");
        Require(string.IsNullOrWhiteSpace(ownerBackend.Payload),
            "Resolved mutation owner was not retired.");
    }

    private static async Task RestartReplaysOnlyExactCommandAndRecoversReceiptAsync()
    {
        Sr5AfterRunSettlementEditorState editor = Editor(Input());
        Sr5AfterRunSettlementDraft draft = Draft(editor);
        CharacterAfterRunSettlementReceipt receipt = Receipt(draft);
        var presenter = new FakePresenter(Binding(41));
        var owner = new TestOwner(OwnerId);
        var checkpointBackend = new MemoryBackend();
        var ownerBackend = new MemoryBackend();
        var authority = new Sr5AfterRunSettlementLiveCheckpointAuthority(
            owner,
            editor,
            () => presenter.Binding);
        var firstStore = new Sr5AfterRunSettlementCheckpointStore(
            checkpointBackend,
            authority,
            new Sr5CareerMutationOwnerStore(ownerBackend));
        Require(firstStore.TryCreate(
            Sr5AfterRunSettlementCheckpoint.FromDraft(draft),
            out Sr5AfterRunSettlementCheckpoint reviewed,
            out string blocker), blocker);
        Require(firstStore.TryBeginApply(
            Sr5AfterRunSettlementCheckpointCas.From(reviewed),
            out Sr5AfterRunSettlementCheckpoint applying,
            out blocker), blocker);

        var restartedStore = new Sr5AfterRunSettlementCheckpointStore(
            checkpointBackend,
            authority,
            new Sr5CareerMutationOwnerStore(ownerBackend));
        Require(restartedStore.TryRead(
            out Sr5AfterRunSettlementCheckpoint recoveredApplying,
            out blocker), blocker);
        Require(
            Sr5AfterRunSettlementCheckpointCas.From(recoveredApplying)
                .Matches(applying)
            && recoveredApplying.Draft.SemanticallyEquals(applying.Draft),
            "Restart did not recover the exact Applying command.");
        int calls = 0;
        presenter.SettleHandler = command =>
        {
            calls++;
            Require(command == draft.ToCommand(), "Restart replayed a different command.");
            presenter.Binding = Binding(42);
            return SuccessResult(draft, receipt, command, replayed: true);
        };
        var coordinator = new Sr5AfterRunSettlementCoordinator(presenter, owner);
        Sr5AfterRunSettlementRecoveryResolution resolution = await coordinator.ResolveAsync(
            recoveredApplying,
            restartedStore);
        Require(calls == 1, "Restart recovery did not issue exactly one idempotent replay.");
        Require(resolution.Status == Sr5AfterRunSettlementRecoveryStatus.AppliedVerified,
            resolution.Message);
        Require(restartedStore.TryRecordAuthoritativeResolution(
            Sr5AfterRunSettlementCheckpointCas.From(recoveredApplying),
            resolution,
            out Sr5AfterRunSettlementCheckpoint applied,
            out blocker), blocker);
        Require(applied.Receipt?.ReceiptDigest == receipt.ReceiptDigest,
            "Restart recovery stored a different receipt.");
    }

    private static void ManualProposalPublishesToBothSeamsAndSurvivesRestart()
    {
        var snapshots = new ManualSnapshotSource(ManualSnapshot(41, 'f'));
        var backend = new ManualProposalBackend();
        var source = new Sr5AfterRunManualProposalSource(snapshots, backend);
        Sr5AfterRunManualProposalSubmission submission = ManualSubmission();

        Sr5AfterRunManualProposalPublishResult published = source.Publish(submission);
        Require(published.Published && !published.Replayed, published.Blocker);
        Require(published.Proposal?.IsExact() == true,
            "Published manual proposal is not canonical and digest-bound.");
        Require(!string.IsNullOrWhiteSpace(backend.Payload),
            "Published manual proposal was not durable.");

        Sr5AfterRunProposalCatalogResult catalog = source.Load(WorkspaceId);
        Require(catalog.Status == Sr5AfterRunCatalogStatus.Available
            && catalog.Entries.Count == 1,
            "Published proposal did not enter the Android catalog exactly once.");
        CharacterAfterRunSettlementProposalProjectionResult projection = source.Read(
            new CharacterAfterRunSettlementProposalProjectionRequest(
                WorkspaceId,
                41,
                Identity,
                snapshots.Snapshot.CharacterProjectionDigest));
        Require(
            projection.Outcome
                == CharacterAfterRunSettlementProposalProjectionOutcome.Available
            && projection.Projection is not null,
            projection.Error ?? "Core projection seam remained unavailable.");
        CharacterAfterRunSettlementProposalProjection exactProjection =
            projection.Projection
            ?? throw new InvalidOperationException(
                "Available Core projection response omitted the projection.");
        Require(CombinedInputCreatesSettleableQuote(
                snapshots.Snapshot,
                exactProjection),
            "Published projection did not survive the same Core quote preflight.");

        var restarted = new Sr5AfterRunManualProposalSource(snapshots, backend);
        Require(restarted.Load(WorkspaceId).Status == Sr5AfterRunCatalogStatus.Available,
            "Process restart did not reopen the durable manual proposal.");
        Sr5AfterRunManualProposalPublishResult replay = restarted.Publish(submission);
        Require(replay.Published && replay.Replayed
            && replay.Proposal?.ProposalDigest == published.Proposal!.ProposalDigest,
            "Exact repeat publication was not a deterministic replay.");
    }

    private static void IncompleteStaleAndTamperedManualProposalsFailClosed()
    {
        var snapshots = new ManualSnapshotSource(ManualSnapshot(41, 'f'));
        var backend = new ManualProposalBackend();
        var source = new Sr5AfterRunManualProposalSource(snapshots, backend);
        Sr5AfterRunManualProposalPublishResult pending = source.Publish(
            ManualSubmission() with { GmApproved = false });
        Require(!pending.Published && string.IsNullOrWhiteSpace(backend.Payload),
            "Missing explicit GM approval registered a proposal.");

        Sr5AfterRunManualProposalPublishResult exact = source.Publish(ManualSubmission());
        Require(exact.Published, exact.Blocker);
        string durable = backend.Payload;
        backend.Payload = durable.Replace(
            "\"KarmaAward\":8",
            "\"KarmaAward\":9",
            StringComparison.Ordinal);
        Require(source.Load(WorkspaceId).Status == Sr5AfterRunCatalogStatus.Corrupt,
            "Tampered durable proposal remained available.");

        backend.Payload = durable;
        snapshots.Snapshot = ManualSnapshot(42, 'e');
        Require(source.Load(WorkspaceId).Status == Sr5AfterRunCatalogStatus.Missing,
            "Proposal from an old saved revision remained discoverable.");
        CharacterAfterRunSettlementProposalProjectionResult stale = source.Read(
            new CharacterAfterRunSettlementProposalProjectionRequest(
                WorkspaceId,
                42,
                Identity,
                snapshots.Snapshot.CharacterProjectionDigest));
        Require(
            stale.Outcome
                == CharacterAfterRunSettlementProposalProjectionOutcome.Conflict,
            "Core projection accepted a stale workspace/revision binding.");
    }

    private static Sr5AfterRunManualProposalSubmission ManualSubmission()
        => new(
            WorkspaceId,
            ExpectedWorkspaceRevision: 41,
            Identity,
            RunTitle: "Operation Glass Harbor",
            CompletedAt: new DateTimeOffset(2026, 8, 26, 20, 0, 0, TimeSpan.Zero),
            KarmaAward: 8,
            NuyenAward: 12_500m,
            RewardReceiptDigest: new string('a', 64),
            TargetOwnedByCharacter: true,
            RunCompleted: true,
            CurrentHeat: 1,
            HeatDelta: 2,
            StreetCredDelta: 2,
            NotorietyDelta: 1,
            PublicAwarenessDelta: 1,
            Settings: Input().Settings,
            ContactProposals: Input().ContactProposals,
            ExpectedGmActorId: "gm-17",
            GmReviewId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            GmReviewReason: "Run settlement approved",
            GmApproved: true,
            ExpectedOwnerActorId: "owner-23",
            OwnerReviewId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            OwnerReviewReason: "Complete proposal accepted",
            OwnerApproved: true);

    private static Sr5AfterRunWorkspaceSnapshot ManualSnapshot(
        long revision,
        char digestCharacter)
        => new(
            WorkspaceId,
            revision,
            revision,
            CharacterAfterRunSettlementRules.RulesetId,
            Created: true,
            CurrentStreetCred: 10,
            CurrentNotoriety: 4,
            CurrentPublicAwareness: 6,
            CurrentKarma: 30,
            new string(digestCharacter, 64));

    private static bool CombinedInputCreatesSettleableQuote(
        Sr5AfterRunWorkspaceSnapshot snapshot,
        CharacterAfterRunSettlementProposalProjection projection)
    {
        var input = new CharacterAfterRunSettlementInput(
            projection.Identity,
            snapshot.Created,
            snapshot.RulesetId,
            projection.TargetOwnedByCharacter,
            projection.ProjectionIsExact,
            projection.RunCompleted,
            ProposalAlreadySettled: false,
            projection.ExpectedGmActorId,
            projection.ExpectedOwnerActorId,
            projection.CurrentHeat,
            snapshot.CurrentStreetCred,
            snapshot.CurrentNotoriety,
            snapshot.CurrentPublicAwareness,
            snapshot.CurrentKarma,
            projection.HeatDelta,
            projection.StreetCredDelta,
            projection.NotorietyDelta,
            projection.PublicAwarenessDelta,
            projection.Settings,
            projection.ContactProposals,
            projection.GmReview,
            projection.OwnerReview,
            projection.RawSourceState,
            projection.RawCustomDataState,
            projection.RawGmPolicyState,
            projection.RawRuntimeState);
        return CharacterAfterRunSettlementRules.TryCreateQuote(
                input,
                out CharacterAfterRunSettlementQuote quote)
            && quote.CanSettle;
    }

    private static Sr5AfterRunSettlementEditorState Editor(
        CharacterAfterRunSettlementInput input)
    {
        Require(CharacterAfterRunSettlementRules.TryCreateQuote(
            input,
            out CharacterAfterRunSettlementQuote quote),
            "Could not create Core quote.");
        Require(CharacterAfterRunSettlementServiceIntegrity.TryComputeBindingDigest(
            WorkspaceId,
            41,
            quote,
            out string bindingDigest),
            "Could not bind Core quote.");
        var binding = new CharacterAfterRunSettlementQuoteBinding(
            CharacterAfterRunSettlementServiceSchemas.QuoteV1,
            WorkspaceId,
            41,
            quote.Identity,
            quote,
            bindingDigest);
        Sr5AfterRunRewardContext reward = Sr5AfterRunRewardContext.Create(
            Identity,
            "Operation Glass Harbor",
            new DateTimeOffset(2026, 8, 26, 20, 0, 0, TimeSpan.Zero),
            karmaAward: 8,
            nuyenAward: 12_500m,
            rewardReceiptDigest: new string('a', 64));
        var editor = new Sr5AfterRunSettlementEditorState(
            WorkspaceId,
            41,
            Sr5AfterRunCatalogStatus.Available,
            [new Sr5AfterRunSettlementCandidate(reward, binding)],
            OmittedProposalCount: 0,
            Blockers: []);
        Require(editor.IsExact(), "Editor fixture is not exact.");
        return editor;
    }

    private static Sr5AfterRunSettlementDraft Draft(
        Sr5AfterRunSettlementEditorState editor)
    {
        Require(Sr5AfterRunSettlementDraft.TryCreate(
            editor,
            editor.Candidates.Single(),
            OwnerId,
            TransactionId,
            AllReviewed(),
            out Sr5AfterRunSettlementDraft draft,
            out string blocker), blocker);
        return draft;
    }

    private static Sr5AfterRunReviewAcknowledgements AllReviewed()
        => new(true, true, true, true, true, true);

    private static CharacterAfterRunSettlementInput Input()
        => new(
            Identity,
            Created: true,
            RulesetId: CharacterAfterRunSettlementRules.RulesetId,
            TargetOwnedByCharacter: true,
            ProjectionIsExact: true,
            RunCompleted: true,
            ProposalAlreadySettled: false,
            ExpectedGmActorId: "gm-17",
            ExpectedOwnerActorId: "owner-23",
            CurrentHeat: 1,
            CurrentStreetCred: 10,
            CurrentNotoriety: 4,
            CurrentPublicAwareness: 6,
            CurrentKarma: 30,
            HeatDelta: 2,
            StreetCredDelta: 2,
            NotorietyDelta: 1,
            PublicAwarenessDelta: 1,
            new CharacterAfterRunSettlementSettings(
                MaximumHeat: 20,
                MaximumReputation: 100,
                MaximumConnection: 12,
                MaximumLoyalty: 6,
                KarmaPerContactPoint: 1,
                AllowRunRewardContacts: true,
                AllowKarmaPurchasedContacts: true,
                UseCalculatedPublicAwareness: false),
            ContactProposals:
            [
                new(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "Fixer Jane",
                    "Fixer",
                    "Seattle",
                    4,
                    3,
                    CharacterAfterRunContactProposalKind.RunReward),
                new(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "Doc Red",
                    "Street Doc",
                    "Tacoma",
                    6,
                    5,
                    CharacterAfterRunContactProposalKind.KarmaPurchase)
            ],
            GmReview: new CharacterAfterRunReview(
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                CharacterAfterRunReviewRole.GameMaster,
                "gm-17",
                CharacterAfterRunReviewDecision.Approved,
                "Run settlement approved"),
            OwnerReview: new CharacterAfterRunReview(
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                CharacterAfterRunReviewRole.CharacterOwner,
                "owner-23",
                CharacterAfterRunReviewDecision.Approved,
                "Complete proposal accepted"),
            RawSourceState: "source:v1",
            RawCustomDataState: "custom:v1",
            RawGmPolicyState: "gm-policy:v1",
            RawRuntimeState: "runtime:v1");

    private static CharacterAfterRunSettlementReceipt Receipt(
        Sr5AfterRunSettlementDraft draft)
    {
        CharacterAfterRunSettlementPlan plan = draft.Plan;
        var observation = new CharacterAfterRunSettlementObservation(
            MatchingTransactionCount: 1,
            plan.TargetHeat,
            plan.TargetStreetCred,
            plan.TargetNotoriety,
            plan.TargetPublicAwareness,
            plan.TargetKarma,
            plan.ContactsToAdd,
            new CharacterAfterRunExpenseObservation(
                plan.ContactKarmaCost == 0 ? 0 : 1,
                plan.ExpenseId,
                plan.ExpenseAmount,
                plan.ExpenseReason,
                plan.ContactKarmaCost == 0 ? string.Empty : "Karma",
                Refund: false),
            plan.ExpectedSourceDigest,
            plan.ExpectedCustomDataDigest,
            plan.ExpectedGmPolicyDigest,
            plan.ExpectedRuntimeDigest);
        Require(CharacterAfterRunSettlementRules.TryCreateReceipt(
            plan.TransactionId,
            draft.Quote,
            plan,
            observation,
            out CharacterAfterRunSettlementReceipt receipt),
            "Could not create exact receipt.");
        return receipt;
    }

    private static CharacterAfterRunSettlementResult SuccessResult(
        Sr5AfterRunSettlementDraft draft,
        CharacterAfterRunSettlementReceipt receipt,
        CharacterAfterRunSettlementCommand command,
        bool replayed)
    {
        Require(CharacterAfterRunSettlementServiceIntegrity.TryComputeCommandDigest(
            command,
            out string commandDigest), "Could not compute command digest.");
        var unsigned = new CharacterAfterRunSettlementResult(
            CharacterAfterRunSettlementServiceSchemas.ResultV1,
            replayed
                ? CharacterAfterRunSettlementServiceOutcome.Replayed
                : CharacterAfterRunSettlementServiceOutcome.Applied,
            draft.WorkspaceId,
            draft.ExpectedWorkspaceRevision,
            draft.ExpectedWorkspaceRevision + 1,
            draft.Quote.Identity,
            draft.Plan.TransactionId,
            commandDigest,
            draft.Quote,
            receipt,
            [],
            string.Empty);
        Require(CharacterAfterRunSettlementServiceIntegrity.TryComputeResultDigest(
            unsigned,
            out string resultDigest), "Could not compute result digest.");
        return unsigned with { ResultDigest = resultDigest };
    }

    private static Sr5CareerRunnerBinding Binding(long revision)
        => new(
            Created: true,
            GameEdition: "SR5",
            WorkspaceId,
            ContentRevision: revision,
            SavedRevision: revision,
            IsDirty: false,
            Error: null);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestOwner(Guid ownerId) : ISr5CareerCheckpointOwnerAuthority
    {
        public Guid CurrentOwnerId { get; } = ownerId;
    }

    private sealed class MemoryBackend : ISr5CareerCheckpointBackend
    {
        public string Payload { get; set; } = string.Empty;
        public string Read() => Payload;
        public void Write(string payload) => Payload = payload;
        public void Remove() => Payload = string.Empty;
    }

    private sealed class ManualProposalBackend : ISr5AfterRunManualProposalBackend
    {
        public string Payload { get; set; } = string.Empty;
        public string Read() => Payload;
        public void Write(string payload) => Payload = payload;
    }

    private sealed class ManualSnapshotSource(Sr5AfterRunWorkspaceSnapshot snapshot) :
        IAndroidAfterRunWorkspaceSnapshotSource
    {
        public Sr5AfterRunWorkspaceSnapshot Snapshot { get; set; } = snapshot;

        public bool TryRead(
            CharacterWorkspaceId workspaceId,
            out Sr5AfterRunWorkspaceSnapshot snapshotValue,
            out string blocker)
        {
            snapshotValue = Snapshot;
            blocker = string.Empty;
            return workspaceId == Snapshot.WorkspaceId;
        }
    }

    private sealed class FakePresenter(Sr5CareerRunnerBinding binding) :
        ISr5AfterRunSettlementPresenter
    {
        public Sr5CareerRunnerBinding Binding { get; set; } = binding;
        public Func<CharacterAfterRunSettlementCommand,
            CharacterAfterRunSettlementResult?>? SettleHandler { get; set; }

        public Task<Sr5AfterRunSettlementEditorState> LoadAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(Editor(Input()));

        public Task<CharacterAfterRunSettlementResult?> SettleAsync(
            CharacterAfterRunSettlementCommand command,
            CancellationToken cancellationToken)
            => Task.FromResult(SettleHandler?.Invoke(command));
    }
}
