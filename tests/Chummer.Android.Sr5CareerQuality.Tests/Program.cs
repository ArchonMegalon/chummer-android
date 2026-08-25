using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Sr5CareerQuality.Tests;

internal static class Program
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid InternalId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid SourceId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly CharacterWorkspaceId WorkspaceId = new("quality-workspace");

    public static async Task Main()
    {
        await ExactReviewBindsAllAuthorityAsync();
        await AtomicApplyAndRestartRecoveryNeverReplayAsync();
        await UnknownTransportOutcomesResolveWithoutRetryAsync();
        await UnsupportedAmbiguousAndDriftedAuthorityFailsClosedAsync();
        await CompensatingCorrectionRestoresAndRetiresAsync();
        await RegisteredTypedPersistenceSeamFailsClosedOnAuthorityDriftAsync();
        MalformedSchemaAndCasConflictsRemainReplayLocks();
        Console.WriteLine("PASS: 7 SR5 Career quality phone authority behavior groups");
    }

    private static async Task ExactReviewBindsAllAuthorityAsync()
    {
        FakeAtomicWorkspace workspace = new();
        TestPresenter presenter = new(workspace);
        OwnerAuthority owner = new();
        Sr5CareerQualityCoordinator coordinator = new(presenter, owner);
        CareerQualityEditorState editor = await coordinator.PrepareAsync()
            ?? throw new InvalidOperationException("editor unavailable");
        CharacterCareerQualityQuote quote = editor.Quotes.Single();
        Guid transactionId = Guid.Parse("10000000-0000-4000-8000-000000000001");
        Sr5CareerQualityDraft draft = await coordinator.ReviewAsync(
            editor,
            quote,
            transactionId,
            new DateTime(2026, 8, 25, 20, 45, 12));

        Require(draft.IsExact(), "reviewed draft must be exact");
        Require(draft.OwnerId == OwnerId, "typed owner");
        Require(draft.Review.Quote.Identity == new CharacterCareerQualityIdentity(InternalId, SourceId), "typed InternalId/SourceId");
        Require(draft.Review.Quote.Authority.GmAllows, "GM legality");
        Require(draft.Review.Quote.Authority.Effects.IsExact, "effect exactness");
        Require(draft.Review.Quote.Authority.Effects.UnsupportedFamilies.Count == 0, "no unsupported effects");
        Require(draft.Review.Quote.Definition.SourceEnabled, "source enabled");
        Require(draft.ExpectedWorkspaceRevision == 41 && draft.ExpectedSavedRevision == 41, "both revisions");
        Require(draft.RuntimeAuthority.ContentDigest == Sr5CareerQualityRuntimeAuthority.CurrentContentDigest, "content digest");
        Require(draft.RuntimeAuthority.RuntimeDigest == Sr5CareerQualityRuntimeAuthority.CurrentRuntimeDigest, "runtime digest");
        Require(draft.ActionPlan.IdempotencyKey.Length == 64, "idempotency digest");
        Require(draft.ActionPlan.DomainIdentity.Contains(InternalId.ToString("D"), StringComparison.Ordinal), "identity in action");
        Require(draft.ActionPlan.DomainIdentity.Contains(SourceId.ToString("D"), StringComparison.Ordinal), "source identity in action");

        MemoryBackend backend = new();
        Sr5CareerQualityLiveCheckpointAuthority authority = new(owner, editor, () => presenter.Binding);
        Sr5CareerQualityCheckpointStore store = new(backend, authority);
        Sr5CareerQualityCheckpoint checkpoint = Sr5CareerQualityCheckpoint.FromDraft(draft);
        Require(store.TryCreate(checkpoint, out Sr5CareerQualityCheckpoint stored, out string blocker), blocker);
        Require(stored.IsStructurallyValid(), "checkpoint exact");
        Require(backend.Payload.Length > 0, "durable checkpoint payload");
    }

    private static async Task AtomicApplyAndRestartRecoveryNeverReplayAsync()
    {
        Fixture fixture = await Fixture.CreateAsync("20000000-0000-4000-8000-000000000002");
        Require(fixture.Store.TryBeginApply(
            Sr5CareerQualityCheckpointCas.From(fixture.Checkpoint),
            out Sr5CareerQualityCheckpoint applying,
            out string blocker), blocker);
        Sr5CareerQualityApplyResult result = await fixture.Coordinator.ApplyAsync(
            fixture.Draft,
            applying,
            fixture.Store);
        Require(result.Status == Sr5CareerQualityApplyStatus.Applied, result.Message);
        Require(result.Receipt is not null, "receipt required");
        Require(fixture.Workspace.CommitCount == 1, "single atomic commit");
        Require(fixture.Presenter.Binding.ContentRevision == 42, "target workspace revision");
        Require(fixture.Presenter.Binding.SavedRevision == 42, "target saved revision");
        Require(Sr5CareerQualityCoordinator.ReceiptMatchesDraft(fixture.Draft, result.Receipt!), "receipt binding");
        Require(fixture.Store.TryRecordAuthoritativeResolution(
            Sr5CareerQualityCheckpointCas.From(applying),
            result.Resolution,
            out Sr5CareerQualityCheckpoint applied,
            out blocker), blocker);

        Sr5CareerQualityLiveCheckpointAuthority restartedAuthority = new(
            fixture.Owner,
            fixture.Editor,
            () => fixture.Presenter.Binding);
        Sr5CareerQualityCheckpointStore restartedStore = new(fixture.Backend, restartedAuthority);
        Require(restartedStore.TryRead(out Sr5CareerQualityCheckpoint recovered, out blocker), blocker);
        Require(recovered.Phase == Sr5CareerCheckpointPhase.Applied, "applied phase survives restart");
        Sr5CareerQualityRecoveryResolution resolution = await fixture.Coordinator.ResolveAsync(recovered);
        Require(resolution.Status == Sr5CareerQualityRecoveryStatus.AppliedVerified, resolution.Message);
        Require(resolution.Receipt?.TransactionId == fixture.Draft.TransactionId, "recovered exact receipt");
        Require(fixture.Workspace.CommitCount == 1, "restart lookup never replays mutation");
        Require(applied.Version == recovered.Version, "exact persisted CAS version");
    }

    private static async Task UnknownTransportOutcomesResolveWithoutRetryAsync()
    {
        Fixture committed = await Fixture.CreateAsync("30000000-0000-4000-8000-000000000003");
        committed.Presenter.ThrowAfterCommit = true;
        Require(committed.Store.TryBeginApply(
            Sr5CareerQualityCheckpointCas.From(committed.Checkpoint),
            out Sr5CareerQualityCheckpoint applying,
            out string blocker), blocker);
        Sr5CareerQualityApplyResult committedResult = await committed.Coordinator.ApplyAsync(
            committed.Draft,
            applying,
            committed.Store);
        Require(committedResult.Status == Sr5CareerQualityApplyStatus.Applied, "commit-then-transport-failure must recover applied");
        Require(committed.Workspace.CommitCount == 1, "commit-then-failure no retry");

        Fixture rejected = await Fixture.CreateAsync("30000000-0000-4000-8000-000000000004");
        rejected.Presenter.ThrowBeforeCommit = true;
        Require(rejected.Store.TryBeginApply(
            Sr5CareerQualityCheckpointCas.From(rejected.Checkpoint),
            out applying,
            out blocker), blocker);
        Sr5CareerQualityApplyResult rejectedResult = await rejected.Coordinator.ApplyAsync(
            rejected.Draft,
            applying,
            rejected.Store);
        Require(rejectedResult.Status == Sr5CareerQualityApplyStatus.RejectedBeforeMutation, "pre-commit failure must prove not applied");
        Require(rejected.Workspace.CommitCount == 0, "pre-commit failure no mutation");
    }

    private static async Task UnsupportedAmbiguousAndDriftedAuthorityFailsClosedAsync()
    {
        FakeAtomicWorkspace unsupported = new();
        unsupported.SetCandidate(unsupported.Candidate with
        {
            Effects = unsupported.Candidate.Effects with
            {
                UnsupportedFamilies = [CharacterCareerQualityEffectFamily.ChoiceSelection]
            }
        });
        CareerQualityEditorState unsupportedEditor =
            await new Sr5CareerQualityCoordinator(new TestPresenter(unsupported), new OwnerAuthority()).PrepareAsync()
            ?? throw new InvalidOperationException();
        CharacterCareerQualityQuote unsupportedQuote = unsupportedEditor.Quotes.Single();
        Require(!unsupportedQuote.CanApply, "unsupported effect blocked");
        Require(unsupportedQuote.Blocker == CharacterCareerQualityBlocker.UnsupportedEffectFamily, "unsupported effect blocker");

        FakeAtomicWorkspace gmRestricted = new();
        gmRestricted.SetCandidate(gmRestricted.Candidate with { GmAllows = false });
        CharacterCareerQualityQuote gmQuote = (await new Sr5CareerQualityCoordinator(
            new TestPresenter(gmRestricted),
            new OwnerAuthority()).PrepareAsync())!.Quotes.Single();
        Require(gmQuote.Blocker == CharacterCareerQualityBlocker.GmRestricted, "GM restriction blocker");

        FakeAtomicWorkspace ambiguous = new() { DuplicateCandidate = true };
        await RequireThrowsAsync<InvalidOperationException>(
            async () => _ = await new Sr5CareerQualityCoordinator(
                new TestPresenter(ambiguous),
                new OwnerAuthority()).PrepareAsync(),
            "ambiguous duplicate candidates must close entire lane");

        FakeAtomicWorkspace runtimeDrift = new();
        runtimeDrift.SetCandidate(runtimeDrift.Candidate with
        {
            Binding = runtimeDrift.Candidate.Binding with { RuntimeFingerprint = new string('d', 64) }
        });
        await RequireThrowsAsync<InvalidOperationException>(
            async () => _ = await new Sr5CareerQualityCoordinator(
                new TestPresenter(runtimeDrift),
                new OwnerAuthority()).PrepareAsync(),
            "runtime drift must fail closed");

        Fixture stale = await Fixture.CreateAsync("40000000-0000-4000-8000-000000000004");
        stale.Presenter.OverrideBinding = stale.Presenter.Binding with { ContentRevision = 42, SavedRevision = 42 };
        await RequireThrowsAsync<InvalidOperationException>(
            async () => _ = await stale.Coordinator.ReviewAsync(
                stale.Editor,
                stale.Editor.Quotes.Single(),
                Guid.NewGuid(),
                DateTime.Now),
            "revision drift must block review");
    }

    private static async Task CompensatingCorrectionRestoresAndRetiresAsync()
    {
        Fixture fixture = await Fixture.CreateAsync("50000000-0000-4000-8000-000000000005");
        Require(fixture.Store.TryBeginApply(
            Sr5CareerQualityCheckpointCas.From(fixture.Checkpoint),
            out Sr5CareerQualityCheckpoint applying,
            out string blocker), blocker);
        Sr5CareerQualityApplyResult result = await fixture.Coordinator.ApplyAsync(
            fixture.Draft,
            applying,
            fixture.Store);
        Require(fixture.Store.TryRecordAuthoritativeResolution(
            Sr5CareerQualityCheckpointCas.From(applying),
            result.Resolution,
            out Sr5CareerQualityCheckpoint applied,
            out blocker), blocker);
        CharacterCareerQualityReceipt receipt = result.Receipt!;
        Guid correctionId = Guid.Parse("50000000-0000-4000-8000-000000000006");
        CharacterCareerQualityCorrectionPlan correction = await fixture.Coordinator.CorrectAsync(
            applied,
            receipt,
            correctionId,
            "Test correction");
        Require(correction.CorrectionId == correctionId, "correction identity");
        Require(correction.RestoreInstances.SequenceEqual(receipt.InstancesBefore), "restored exact instances");
        Require(correction.SavedCharacterKarma == receipt.CharacterKarmaBefore, "restored Karma");
        Require(correction.RemoveExpense == receipt.CreatesExpense, "expense removal binding");
        Require(fixture.Presenter.Binding.ContentRevision == 43, "correction target workspace revision");
        Require(fixture.Presenter.Binding.SavedRevision == 43, "correction target saved revision");
        Require(fixture.Store.TryDeleteCorrected(
            Sr5CareerQualityCheckpointCas.From(applied),
            receipt,
            correction,
            out blocker), blocker);
        Require(string.IsNullOrEmpty(fixture.Backend.Payload), "corrected checkpoint retired");
        Require(fixture.Workspace.CommitCount == 1, "correction does not replay original");
        Require(fixture.Workspace.CorrectionCount == 1, "single compensation");
    }

    private static async Task RegisteredTypedPersistenceSeamFailsClosedOnAuthorityDriftAsync()
    {
        FakeAtomicWorkspace backend = new();
        CareerQualityAuthoritySnapshot initial = backend.Snapshot;
        CareerQualityEditorState state = CareerQualityWorkflow.Project(initial);
        CharacterCareerQualityQuote quote = state.Quotes.Single();
        CareerQualityDraft draft = CareerQualityWorkflow.CreateDraft(state, quote);
        CareerQualityReview review = CareerQualityWorkflow.Review(initial, draft);
        CharacterCareerQualityPlan plan = CareerQualityWorkflow.PlanConfirmation(
            initial,
            review,
            confirmed: true,
            Guid.Parse("70000000-0000-4000-8000-000000000007"),
            new DateTime(2026, 8, 26, 10, 15, 0));

        using AndroidCareerQualityAtomicWorkspace unavailable = new(new PlainClient());
        Require(await unavailable.ReadAsync(WorkspaceId, CancellationToken.None) is null,
            "a client without the typed atomic capability must be unavailable");
        Require(await unavailable.CommitAsync(plan, CancellationToken.None) is null,
            "an unsupported client must never fall back to document mutation");
        Require(backend.CommitCount == 0, "unsupported client cannot reach persistence");

        using AndroidCareerQualityAtomicWorkspace registered = new(
            new TypedPersistenceClient(backend));
        CareerQualityAuthoritySnapshot? loaded = await registered.ReadAsync(
            WorkspaceId,
            CancellationToken.None);
        Require(loaded is not null && loaded.Binding == initial.Binding,
            "registered adapter delegates the exact typed authority snapshot");

        CharacterCareerQualityPlan staleDigest = plan with
        {
            ExpectedContentDigest = new string('0', 64)
        };
        CareerQualityAtomicCommitResult? staleDigestResult = null;
        try
        {
            staleDigestResult = await registered.CommitAsync(
                staleDigest,
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // A coherent-but-drifted payload must throw; an internally incoherent
            // payload may return unavailable. Both paths are fail closed.
        }
        Require(staleDigestResult is null, "content-digest drift must fail closed");
        Require(backend.CommitCount == 0, "digest drift rejected before persistence");

        backend.SetCandidate(backend.Candidate with
        {
            Binding = backend.Candidate.Binding with
            {
                WorkspaceRevision = 42,
                SavedRevision = 42
            }
        });
        await RequireThrowsAsync<InvalidOperationException>(
            async () => _ = await registered.CommitAsync(plan, CancellationToken.None),
            "workspace and saved revision drift must throw before persistence");
        Require(backend.CommitCount == 0, "revision drift rejected before persistence");

        backend.SetCandidate(backend.Candidate with { Binding = initial.Binding });
        CareerQualityAtomicCommitResult committed = await registered.CommitAsync(
                plan,
                CancellationToken.None)
            ?? throw new InvalidOperationException("typed atomic commit unavailable");
        Require(backend.CommitCount == 1, "exact plan delegated exactly once");
        Require(committed.Receipt.TransactionId == plan.TransactionId,
            "exact receipt returned through registration seam");

        CareerQualityAuthoritySnapshot correctionSnapshot = backend.Snapshot;
        CharacterCareerQualityReceipt original = committed.Receipt;
        CareerQualityCorrectionRequest correctionRequest = new(
            WorkspaceId,
            correctionSnapshot.Binding.OwnerId,
            correctionSnapshot.Binding.WorkspaceRevision,
            correctionSnapshot.Binding.SavedRevision,
            correctionSnapshot.RulesetId,
            original,
            original.ReceiptDigest,
            Confirmed: true,
            Guid.Parse("70000000-0000-4000-8000-000000000008"),
            "Registered seam correction");
        CharacterCareerQualityCorrectionPlan correction =
            CareerQualityWorkflow.PlanCorrection(correctionSnapshot, correctionRequest);
        CareerQualityAtomicCorrectionResult corrected = await registered.CorrectAsync(
                correction,
                CancellationToken.None)
            ?? throw new InvalidOperationException("typed atomic correction unavailable");
        Require(backend.CorrectionCount == 1, "exact correction delegated exactly once");
        Require(corrected.Correction.CorrectionId == correction.CorrectionId,
            "exact correction returned through registration seam");
    }

    private static void MalformedSchemaAndCasConflictsRemainReplayLocks()
    {
        MemoryBackend malformedBackend = new() { Payload = "{not-json" };
        Sr5CareerQualityCheckpointStore malformed = new(malformedBackend);
        Require(!malformed.TryRead(out _, out string malformedBlocker), "malformed payload rejected");
        Require(malformedBlocker.Contains("replay-blocking", StringComparison.Ordinal), "malformed remains lock");
        Require(malformedBackend.Payload == "{not-json", "malformed payload not silently deleted");

        MemoryBackend priorBackend = new() { Payload = "{\"SchemaVersion\":0}" };
        Sr5CareerQualityCheckpointStore prior = new(priorBackend);
        Require(!prior.TryRead(out _, out string priorBlocker), "prior schema rejected");
        Require(priorBlocker.Contains("replay-blocking", StringComparison.Ordinal), "prior schema remains lock");

        Fixture fixture = Fixture.CreateAsync("60000000-0000-4000-8000-000000000006")
            .GetAwaiter().GetResult();
        Sr5CareerQualityCheckpointCas wrong = Sr5CareerQualityCheckpointCas.From(fixture.Checkpoint) with
        {
            Version = 99
        };
        Require(!fixture.Store.TryBeginApply(wrong, out _, out string blocker), "wrong CAS rejected");
        Require(blocker.Contains("CAS failed", StringComparison.Ordinal), "CAS explanation");
        Require(fixture.Backend.Payload.Length > 0, "CAS failure retains exact review lock");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task RequireThrowsAsync<T>(Func<Task> action, string message)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private sealed class OwnerAuthority : ISr5CareerCheckpointOwnerAuthority
    {
        public Guid CurrentOwnerId => OwnerId;
    }

    private sealed class PlainClient : IChummerClient
    {
    }

    private sealed class TypedPersistenceClient : IChummerClient,
        ICareerQualityAtomicWorkspace
    {
        private readonly FakeAtomicWorkspace _workspace;

        public TypedPersistenceClient(FakeAtomicWorkspace workspace)
        {
            _workspace = workspace;
        }

        public Task<CareerQualityAuthoritySnapshot?> ReadAsync(
            CharacterWorkspaceId workspaceId,
            CancellationToken ct)
            => _workspace.ReadAsync(workspaceId, ct);

        public Task<CareerQualityAtomicCommitResult?> CommitAsync(
            CharacterCareerQualityPlan plan,
            CancellationToken ct)
            => _workspace.CommitAsync(plan, ct);

        public Task<CareerQualityAtomicCorrectionResult?> CorrectAsync(
            CharacterCareerQualityCorrectionPlan correction,
            CancellationToken ct)
            => _workspace.CorrectAsync(correction, ct);
    }

    private sealed class MemoryBackend : ISr5CareerCheckpointBackend
    {
        public string Payload { get; set; } = string.Empty;
        public string Read() => Payload;
        public void Write(string payload) => Payload = payload;
        public void Remove() => Payload = string.Empty;
    }

    private sealed record Fixture(
        FakeAtomicWorkspace Workspace,
        TestPresenter Presenter,
        OwnerAuthority Owner,
        Sr5CareerQualityCoordinator Coordinator,
        CareerQualityEditorState Editor,
        Sr5CareerQualityDraft Draft,
        MemoryBackend Backend,
        Sr5CareerQualityCheckpointStore Store,
        Sr5CareerQualityCheckpoint Checkpoint)
    {
        public static async Task<Fixture> CreateAsync(string transactionId)
        {
            FakeAtomicWorkspace workspace = new();
            TestPresenter presenter = new(workspace);
            OwnerAuthority owner = new();
            Sr5CareerQualityCoordinator coordinator = new(presenter, owner);
            CareerQualityEditorState editor = await coordinator.PrepareAsync()
                ?? throw new InvalidOperationException();
            Sr5CareerQualityDraft draft = await coordinator.ReviewAsync(
                editor,
                editor.Quotes.Single(),
                Guid.Parse(transactionId),
                new DateTime(2026, 8, 25, 21, 0, 0));
            MemoryBackend backend = new();
            Sr5CareerQualityLiveCheckpointAuthority authority = new(
                owner,
                editor,
                () => presenter.Binding);
            Sr5CareerQualityCheckpointStore store = new(backend, authority);
            Sr5CareerQualityCheckpoint checkpoint = Sr5CareerQualityCheckpoint.FromDraft(draft);
            Require(store.TryCreate(checkpoint, out checkpoint, out string blocker), blocker);
            return new(workspace, presenter, owner, coordinator, editor, draft, backend, store, checkpoint);
        }
    }

    private sealed class TestPresenter : ISr5CareerQualityPresenter
    {
        private readonly FakeAtomicWorkspace _workspace;
        private readonly CareerQualityInteractionPresenter _interaction;

        public TestPresenter(FakeAtomicWorkspace workspace)
        {
            _workspace = workspace;
            _interaction = new CareerQualityInteractionPresenter(workspace);
        }

        public bool ThrowBeforeCommit { get; set; }
        public bool ThrowAfterCommit { get; set; }
        public Sr5CareerRunnerBinding? OverrideBinding { get; set; }

        public Sr5CareerRunnerBinding Binding => OverrideBinding ?? new(
            Created: true,
            GameEdition: "SR5",
            WorkspaceId,
            _workspace.Snapshot.Binding.WorkspaceRevision,
            _workspace.Snapshot.Binding.SavedRevision,
            IsDirty: false,
            Error: null);

        public async Task<CareerQualityEditorState?> LoadAsync(CancellationToken cancellationToken)
            => await _interaction.ProjectAsync(WorkspaceId, cancellationToken);

        public Task<CareerQualityReview> ReviewAsync(
            CareerQualityDraft draft,
            CancellationToken cancellationToken)
            => _interaction.ReviewAsync(draft, cancellationToken);

        public async Task<CareerQualityConfirmation> ConfirmAndRefreshAsync(
            CareerQualityReview review,
            Guid transactionId,
            DateTime expenseDateLocal,
            CancellationToken cancellationToken)
        {
            if (ThrowBeforeCommit)
            {
                throw new IOException("transport failed before commit");
            }
            CareerQualityConfirmation confirmation = await _interaction.ConfirmAsync(
                review,
                confirmed: true,
                transactionId,
                expenseDateLocal,
                cancellationToken);
            if (ThrowAfterCommit)
            {
                throw new IOException("transport failed after commit");
            }
            return confirmation;
        }

        public Task<CareerQualityCorrectionConfirmation> CorrectAndRefreshAsync(
            CareerQualityCorrectionRequest request,
            CancellationToken cancellationToken)
            => _interaction.CorrectAsync(request, cancellationToken);
    }

    private sealed class FakeAtomicWorkspace : ICareerQualityAtomicWorkspace
    {
        private CharacterCareerQualityInput _candidate = CreateInput();
        private readonly List<CareerQualityPersistedReceiptProjection> _receipts = [];
        private readonly List<CareerQualityPersistedCorrectionProjection> _corrections = [];

        public int CommitCount { get; private set; }
        public int CorrectionCount { get; private set; }
        public bool DuplicateCandidate { get; set; }
        public CharacterCareerQualityInput Candidate => _candidate;

        public CareerQualityAuthoritySnapshot Snapshot => new(
            CharacterCareerQualityRules.RulesetId,
            _candidate.Binding,
            DuplicateCandidate ? [_candidate, _candidate] : [_candidate],
            _receipts.ToArray(),
            _corrections.ToArray(),
            []);

        public void SetCandidate(CharacterCareerQualityInput candidate) => _candidate = candidate;

        public Task<CareerQualityAuthoritySnapshot?> ReadAsync(
            CharacterWorkspaceId workspaceId,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Require(workspaceId == WorkspaceId, "workspace read identity");
            return Task.FromResult<CareerQualityAuthoritySnapshot?>(Snapshot);
        }

        public Task<CareerQualityAtomicCommitResult?> CommitAsync(
            CharacterCareerQualityPlan plan,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CommitCount++;
            Require(CharacterCareerQualityRules.TryCreateQuote(_candidate, out CharacterCareerQualityQuote reviewed), "fresh quote");
            Require(reviewed.LogicalRevision == plan.ExpectedLogicalRevision, "atomic CAS logical revision");
            Require(!_receipts.Any(value => value.Receipt.TransactionId == plan.TransactionId), "unique transaction");
            CharacterCareerQualityExecutionBinding targetBinding = _candidate.Binding with
            {
                WorkspaceRevision = plan.TargetWorkspaceRevision,
                SavedRevision = plan.TargetSavedRevision
            };
            Require(CharacterCareerQualityRules.TryCreateStateObservation(
                plan.Identity,
                plan.Definition,
                plan.Extra,
                plan.SourceName,
                plan.InstancesAfter,
                plan.SavedCharacterKarma,
                targetBinding,
                _candidate.RawSourceState,
                reviewed.RuleDigest,
                out CharacterCareerQualityStateObservation observed), "post state");
            CharacterCareerQualityExpenseObservation expense = Expense(plan);
            Require(CharacterCareerQualityRules.TryCreateReceipt(
                plan.TransactionId,
                reviewed,
                plan,
                observed,
                expense,
                out CharacterCareerQualityReceipt receipt), "receipt");
            _candidate = _candidate with
            {
                Operation = CharacterCareerQualityOperation.RemoveAllLevels,
                Identity = plan.Identity,
                ProposedInternalIdUnused = false,
                TargetOwnedByCharacter = true,
                AvailableKarma = plan.SavedCharacterKarma,
                MatchingInstances = plan.InstancesAfter,
                Binding = targetBinding
            };
            _receipts.Add(new CareerQualityPersistedReceiptProjection(receipt, observed, expense));
            return Task.FromResult<CareerQualityAtomicCommitResult?>(new(
                plan,
                receipt,
                observed,
                expense,
                Snapshot));
        }

        public Task<CareerQualityAtomicCorrectionResult?> CorrectAsync(
            CharacterCareerQualityCorrectionPlan correction,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CorrectionCount++;
            CareerQualityPersistedReceiptProjection persisted = _receipts.Single(
                value => value.Receipt.TransactionId == correction.OriginalTransactionId);
            CharacterCareerQualityReceipt receipt = persisted.Receipt;
            CharacterCareerQualityExecutionBinding targetBinding = _candidate.Binding with
            {
                WorkspaceRevision = correction.TargetWorkspaceRevision,
                SavedRevision = correction.TargetSavedRevision
            };
            Require(CharacterCareerQualityRules.TryCreateStateObservation(
                receipt.Identity,
                receipt.Definition,
                receipt.Extra,
                receipt.SourceName,
                receipt.InstancesBefore,
                receipt.CharacterKarmaBefore,
                targetBinding,
                _candidate.RawSourceState,
                correction.ExpectedRuleDigest,
                out CharacterCareerQualityStateObservation restored), "restored state");
            CharacterCareerQualityExpenseObservation noExpense = NoExpense();
            _candidate = _candidate with
            {
                Operation = CharacterCareerQualityOperation.AcquireLevel,
                Identity = receipt.Identity,
                ProposedInternalIdUnused = true,
                TargetOwnedByCharacter = false,
                AvailableKarma = receipt.CharacterKarmaBefore,
                MatchingInstances = receipt.InstancesBefore,
                Binding = targetBinding
            };
            _corrections.Add(new CareerQualityPersistedCorrectionProjection(
                correction,
                receipt,
                restored,
                noExpense));
            return Task.FromResult<CareerQualityAtomicCorrectionResult?>(new(
                correction,
                restored,
                noExpense,
                Snapshot));
        }

        private static CharacterCareerQualityInput CreateInput()
            => new(
                CharacterCareerQualityOperation.AcquireLevel,
                new CharacterCareerQualityIdentity(InternalId, SourceId),
                Created: true,
                RulesetId: CharacterCareerQualityRules.RulesetId,
                DefinitionProjectionIsExact: true,
                IdentityProjectionIsExact: true,
                ProposedInternalIdUnused: true,
                TargetOwnedByCharacter: false,
                GmAllows: true,
                GmFreeCostApproved: false,
                HasMentorSpiritWay: false,
                MetagenicLimit: 0,
                AvailableKarma: 100,
                Extra: string.Empty,
                SourceName: "SR5 Core",
                Definition: new CharacterCareerQualityDefinition(
                    SourceId,
                    "Test Quality",
                    CharacterCareerQualityType.Positive,
                    BaseKarma: 5,
                    Implemented: true,
                    SourceEnabled: true,
                    CareerOnly: false,
                    ChargenOnly: false,
                    OnlyPriorityGiven: false,
                    DoubleCostCareer: true,
                    StagedPurchase: false,
                    RefundKarmaOnRemove: false,
                    NoLevels: false,
                    LimitIsUnlimited: false,
                    LevelLimit: 3,
                    Metagenic: false,
                    ContributeToBp: true,
                    CostDiscountDefined: false,
                    CostDiscountProjectionIsExact: true,
                    CostDiscountRequirementsMet: false,
                    CostDiscountValue: 0),
                Settings: new CharacterCareerQualitySettings(1, false, false),
                Eligibility: new CharacterCareerQualityEligibilityProjection(
                    IsExact: true,
                    GeneralRequirementsMet: true,
                    RequiredOneOfQualityMet: true,
                    RequiredOneOfMetatypeMet: true,
                    RequiredAllQualitiesMet: true,
                    ForbiddenQualitiesClear: true,
                    ConflictingQualityInternalIds: [],
                    MissingRequirementIds: [],
                    ProjectionDigest: new string('e', 64)),
                Effects: new CharacterCareerQualityEffectProjection(
                    IsExact: true,
                    AppliedFamilies: [],
                    UnsupportedFamilies: [],
                    MutationCount: 0,
                    DeltaDigest: new string('f', 64)),
                MatchingInstances: [],
                Binding: new CharacterCareerQualityExecutionBinding(
                    OwnerId.ToString("D"),
                    WorkspaceId.Value,
                    WorkspaceRevision: 41,
                    SavedRevision: 41,
                    Sr5CareerQualityRuntimeAuthority.CurrentRuntimeDigest,
                    Sr5CareerQualityRuntimeAuthority.CurrentContentDigest),
                RawSourceState: "source-state",
                RawRuleState: "rule-state");

        private static CharacterCareerQualityExpenseObservation Expense(
            CharacterCareerQualityPlan plan)
            => plan.CreatesExpense
                ? new CharacterCareerQualityExpenseObservation(
                    MatchingEntryCount: 1,
                    plan.ExpenseId,
                    plan.ExpenseDateLocal,
                    plan.ExpenseAmount,
                    plan.ExpenseReason,
                    plan.ExpenseType,
                    plan.ExpenseRefund,
                    plan.ForceCareerVisible,
                    plan.KarmaUndoType,
                    plan.NuyenUndoType,
                    plan.UndoObjectId,
                    plan.UndoQuantity,
                    plan.UndoExtra)
                : NoExpense();

        private static CharacterCareerQualityExpenseObservation NoExpense()
            => new(
                MatchingEntryCount: 0,
                Guid.Empty,
                DateTime.MinValue,
                Amount: 0,
                Reason: string.Empty,
                ExpenseType: string.Empty,
                Refund: false,
                ForceCareerVisible: false,
                KarmaUndoType: string.Empty,
                NuyenUndoType: string.Empty,
                UndoObjectId: string.Empty,
                UndoQuantity: 0m,
                UndoExtra: string.Empty);
    }
}
