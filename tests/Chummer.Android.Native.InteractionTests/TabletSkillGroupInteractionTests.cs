using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Maui.Controls;

// Real native page/coordinator/checkpoint and Core service/Android XML adapter.
// The store is a private in-memory CAS fixture, not a physical-device durability proof.
internal static class TabletSkillGroupInteractionTests
{
    private static readonly CharacterWorkspaceId Workspace = new("tablet-group-runner");
    private static readonly Guid GroupA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid GroupB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid SourceA = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid SourceB = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly OwnerContextStamp InitialOwner = new(new OwnerScope("tablet-group-owner"), "tablet-group-fixture", 1);
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task RunAsync()
    {
        LoadControlRequiresSavedCareerOwner();
        await ExactIdentityReviewDoesNotMutateAsync();
        await UnboundMutationCannotUseAmbientOwnerAsync();
        await CoreBlockersNeverCreateAnApplicableDraftAsync();
        await InvalidAndUnavailableCatalogCannotAuthorizeAsync();
        await DelayedCatalogCannotCrossContextAsync();
        await RetiredCatalogCannotOverwriteReplacementPanelAsync();
        await CheckpointReadbackFailureCannotApplyAsync();
        await MutatedReviewedQuoteCannotDispatchAsync();
        await ConfirmationAndActivationWaitRemainBoundAsync();
        await DurableLeaseWaitRemainsBoundAsync();
        await UnavailableAndForgedResultsKeepApplyingAsync();
        await SameCommandRestartRecoveryCreatesOneExpenseAsync();
        await AbandonAndAcknowledgeArePhaseBoundAsync();
        await AfterRunAuthorityHarness.RunTabletSkillGroupActualOwnerCasesAsync();
        Console.WriteLine("PASS tablet skill-group managed interaction: real Core atomic command/ledger, controlled host/store; not Android device persistence");
    }

    private static void LoadControlRequiresSavedCareerOwner()
    {
        foreach (string change in new[] { "creation", "edition", "saved-revision", "owner-live", "busy", "error" })
        {
            using var fixture = new Fixture();
            if (change == "creation") fixture.State = fixture.State with { Profile = fixture.State.Profile! with { Created = false } };
            else if (change == "edition") fixture.State = fixture.State with { Rules = new CharacterRulesSection("SR6", "", "", 0, 0, 0, 0, []) };
            else Change(fixture, change);
            fixture.Refresh();
            Require(!fixture.Enabled("tablet-career-skill-groups-load") && fixture.Service.Commands.Count == 0,
                $"Load action was enabled for {change}.");
        }
    }

    private static async Task ExactIdentityReviewDoesNotMutateAsync()
    {
        using var fixture = new Fixture();
        // Distinct coherent IDs with equal display labels test selection only.
        // The real XML adapter correctly rejects duplicate names in persisted groups.
        fixture.LoadOverride = () => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(
            fixture.Editor() with { SkillGroups = [ControlledQuote(GroupA), ControlledQuote(GroupB)] });
        await fixture.LoadAsync().WaitAsync(Bound);
        fixture.Select(GroupB);
        await fixture.ActionAsync("Review").WaitAsync(Bound);
        var draft = Value<Sr5CareerSkillGroupDraft>(fixture.Review, "Draft");
        Require(draft.Quote.Identity.InternalId == GroupB, "Equal labels selected the wrong typed group.");
        Require(fixture.ReadCheckpoint().Draft.Quote.Identity.InternalId == GroupB,
            "The durable review lost the selected typed group identity.");
        Require(fixture.Has("tablet-career-skill-group-apply") && fixture.Service.Commands.Count == 0
            && fixture.Store.Replacements == 0, "Review mutated the runner or failed to expose explicit Apply.");
        fixture.Confirmation = Task.FromResult(false);
        await fixture.ActionAsync("Apply").WaitAsync(Bound);
        Require(fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Reviewed
            && fixture.Service.Commands.Count == 0, "Declined confirmation entered Applying or Core dispatch.");
    }

    internal static async Task AssertActualOwnerRetirementBeforeDispatchAsync(
        IOwnerContextLeaseAccessor owners, Func<Task> retire)
    {
        using var fixture = new Fixture(owners);
        await fixture.PrepareAsync();
        OwnerContextStamp original = owners.Capture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.BeforeOwnerAdmission = () =>
        {
            entered.TrySetResult();
            release.Task.WaitAsync(Bound).GetAwaiter().GetResult();
        };
        Task action = fixture.ActionAsync("Apply");
        try
        {
            await entered.Task.WaitAsync(Bound);
            await retire().WaitAsync(Bound);
            Require(owners.Capture() != original, "The real credential writer did not retire the admitted stamp.");
        }
        finally
        {
            release.TrySetResult();
            await GuardAsync(action);
        }
        Require(fixture.Store.Replacements == 0 && fixture.Service.LastResult is null
            && fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Applying,
            "A retired owner mutated the runner or manufactured a completed result after queue admission.");
    }

    private static async Task UnboundMutationCannotUseAmbientOwnerAsync()
    {
        using var fixture = new Fixture();
        await fixture.PrepareAsync();
        CharacterCareerSkillGroupAdvanceCommand command = fixture.ReadCheckpoint().Draft.ToCommand();
        int scoped = fixture.Store.ScopedCalls;
        bool rejected = false;
        try { fixture.Service.Advance(command); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && fixture.Store.ScopedCalls == scoped && fixture.Store.UnscopedCalls == 0
            && fixture.Store.Replacements == 0 && fixture.Service.Commands.Count == 0,
            "A command without the originally displayed stamp acquired ambient mutation authority.");
    }

    internal static async Task AssertActualOwnerWriterExcludedFromCommitAsync(
        IOwnerContextLeaseAccessor owners, Func<Task> writer)
    {
        using var fixture = new Fixture(owners);
        await fixture.PrepareAsync();
        OwnerContextStamp original = owners.Capture();
        Task? transition = null;
        fixture.Store.BeforeCommit = () =>
        {
            Require(owners.Capture() == original, "Core reached the store under another issuing stamp.");
            transition = writer();
            Require(!transition.IsCompleted && owners.Capture() == original,
                "The actual credential writer was not excluded during Core's synchronous CAS.");
        };
        await GuardAsync(fixture.ActionAsync("Apply"));
        Require(transition is not null, "The real Core command never reached the atomic store boundary.");
        await transition!.WaitAsync(Bound);
        Require(owners.Capture() != original && fixture.Store.Replacements == 1 && fixture.Store.ExpenseCount == 1,
            "The owner lease was leaked or Core did not perform exactly one atomic mutation before transition.");
        Require(original.Owner == OwnerScope.LocalSingleUser
                ? fixture.Store.UnscopedCalls > 0 && fixture.Store.ScopedCalls == 0
                : fixture.Store.ScopedCalls > 0 && fixture.Store.UnscopedCalls == 0,
            "The command crossed the local/account workspace partition.");
    }

    private static async Task CoreBlockersNeverCreateAnApplicableDraftAsync()
    {
        foreach (var quote in new[]
        {
            ControlledQuote(GroupA, available: 0), ControlledQuote(GroupA, broken: true),
            ControlledQuote(GroupA, disabled: true), ControlledQuote(GroupA, maximum: 1),
            ControlledQuote(GroupA, membersExact: false)
        })
        {
            using var fixture = new Fixture();
            Require(!quote.CanAdvance, "The Core blocker fixture is not actually blocked.");
            fixture.LoadOverride = () => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(fixture.Editor() with { SkillGroups = [quote] });
            await fixture.LoadAsync().WaitAsync(Bound);
            fixture.Select(GroupA);
            await GuardAsync(fixture.ActionAsync("Review"));
            Require(!fixture.Enabled("tablet-career-skill-group-apply") && fixture.Backend.Read().Length == 0
                && fixture.Service.Commands.Count == 0, $"Core blocker {quote.Blocker} became an applicable draft.");
        }
    }

    private static async Task InvalidAndUnavailableCatalogCannotAuthorizeAsync()
    {
        foreach (string change in new[] { "null", "throw", "duplicate-id", "empty-id", "foreign-workspace", "stale-revision", "malformed-quote" })
        {
            using var fixture = new Fixture();
            CareerSkillGroupAdvanceEditorState original = fixture.Editor();
            fixture.LoadOverride = () => change switch
            {
                "null" => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(null),
                "throw" => Task.FromException<CareerSkillGroupAdvanceEditorState?>(new InvalidOperationException("Unavailable controlled catalog")),
                "duplicate-id" => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(original with { SkillGroups = [original.SkillGroups[0], original.SkillGroups[0]] }),
                "empty-id" => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(original with { SkillGroups = [original.SkillGroups[0] with { Identity = new(Guid.Empty) }] }),
                "foreign-workspace" => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(original with { WorkspaceId = new("foreign") }),
                "stale-revision" => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(original with { ContentRevision = original.ContentRevision + 1 }),
                _ => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(original with { SkillGroups = [original.SkillGroups[0] with { RuleDigest = "broken" }] })
            };
            await GuardAsync(fixture.LoadAsync());
            Require(fixture.Service.Commands.Count == 0 && fixture.Store.Replacements == 0
                && fixture.Backend.Read().Length == 0 && !fixture.Enabled("tablet-career-skill-group-apply"),
                $"Invalid catalog {change} authorized mutation/review.");
            if (change is "null" or "throw" or "foreign-workspace" or "stale-revision")
                Require(!string.IsNullOrWhiteSpace(fixture.Label("tablet-career-skill-groups-status").Text)
                    && fixture.Enabled("tablet-career-skill-groups-load"), $"Catalog failure {change} was silent or disabled explicit retry.");
            // A failed observation must allow a new explicit load on this same panel.
            fixture.LoadOverride = null;
            await fixture.LoadAsync().WaitAsync(Bound);
            Require(fixture.Has($"tablet-career-skill-group-{GroupA:D}"), $"Catalog failure {change} poisoned same-panel retry.");
        }
    }

    private static async Task DelayedCatalogCannotCrossContextAsync()
    {
        foreach (string change in new[] { "workspace", "revision", "saved-revision", "section", "editor", "refresh", "departure", "owner-live", "owner-aba", "checkpoint-owner", "busy", "error" })
        {
            using var fixture = new Fixture();
            var pending = new TaskCompletionSource<CareerSkillGroupAdvanceEditorState?>(TaskCreationOptions.RunContinuationsAsynchronously);
            CareerSkillGroupAdvanceEditorState original = fixture.Editor();
            fixture.LoadOverride = () => pending.Task;
            Task action = fixture.LoadAsync();
            Require(!action.IsCompleted, "Catalog test did not actually wait.");
            Change(fixture, change);
            pending.SetResult(original);
            await GuardAsync(action);
            Require(!fixture.Enabled("tablet-career-skill-group-apply") && fixture.Backend.Read().Length == 0
                && fixture.Service.Commands.Count == 0, $"Delayed catalog survived {change}.");
        }
    }

    private static async Task RetiredCatalogCannotOverwriteReplacementPanelAsync()
    {
        foreach (string completion in new[] { "success", "null", "throw" })
        {
            using var fixture = new Fixture();
            var pending = new TaskCompletionSource<CareerSkillGroupAdvanceEditorState?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CareerSkillGroupAdvanceEditorState original = fixture.Editor();
            fixture.LoadOverride = () => pending.Task;
            Label retired = fixture.Label("tablet-career-skill-groups-status");
            Task action = fixture.LoadAsync();
            Require(!action.IsCompleted, "Retired catalog test never entered its controlled wait.");

            fixture.Refresh();
            Label replacement = fixture.Label("tablet-career-skill-groups-status");
            string? replacementText = replacement.Text;
            Require(!ReferenceEquals(retired, replacement), "Refresh did not replace the captured panel.");
            if (completion == "throw") pending.SetException(new InvalidOperationException("Retired catalog unavailable."));
            else pending.SetResult(completion == "success" ? original : null);
            await GuardAsync(action);

            Require(ReferenceEquals(replacement, fixture.Label("tablet-career-skill-groups-status"))
                && replacement.Text == replacementText
                && !fixture.Has($"tablet-career-skill-group-{GroupA:D}")
                && fixture.Backend.Read().Length == 0 && fixture.Service.Commands.Count == 0,
                $"Retired catalog {completion} changed its successor's status, selection, or authority.");
            fixture.LoadOverride = null;
            await fixture.LoadAsync().WaitAsync(Bound);
            Require(fixture.Has($"tablet-career-skill-group-{GroupA:D}"),
                $"Retired catalog {completion} kept the successor's explicit load locked.");
        }
    }

    private static async Task CheckpointReadbackFailureCannotApplyAsync()
    {
        using var fixture = new Fixture();
        fixture.Backend.DropWrites = true;
        await fixture.LoadAsync().WaitAsync(Bound);
        fixture.Select(GroupA);
        await GuardAsync(fixture.ActionAsync("Review"));
        Require(!fixture.Enabled("tablet-career-skill-group-apply") && fixture.Backend.Read().Length == 0
                && fixture.Service.Commands.Count == 0, "Failed checkpoint readback authorized Apply.");
    }

    private static async Task MutatedReviewedQuoteCannotDispatchAsync()
    {
        using var fixture = new Fixture();
        CareerSkillGroupAdvanceEditorState editor = fixture.Editor();
        var quotes = editor.SkillGroups.ToList();
        fixture.LoadOverride = () => Task.FromResult<CareerSkillGroupAdvanceEditorState?>(editor with { SkillGroups = quotes });
        await fixture.PrepareAsync();
        quotes[0] = quotes[0] with { RuleDigest = new string('0', 64) };
        await GuardAsync(fixture.ActionAsync("Apply"));
        Require(fixture.Service.Commands.Count == 0 && fixture.Store.Replacements == 0
            && fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Reviewed,
            "In-place quote authority drift crossed the confirmation/dispatch fence.");
    }

    private static async Task ConfirmationAndActivationWaitRemainBoundAsync()
    {
        foreach (bool gateWait in new[] { false, true })
        foreach (string change in new[] { "selection", "workspace", "revision", "saved-revision", "section", "editor", "refresh", "departure", "owner-live", "owner-aba", "checkpoint-owner", "busy", "error", "unchanged" })
        {
            using var fixture = new Fixture();
            await fixture.PrepareAsync();
            object review = fixture.Review;
            var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!gateWait) fixture.Confirmation = confirmation.Task;
            if (gateWait) Require(fixture.ActivationGate.Wait(0), "Could not reserve the actual activation gate.");
            Task first;
            Task second;
            try
            {
                first = fixture.ActionAsync("Apply", review);
                Require(!first.IsCompleted && fixture.Service.Commands.Count == 0, "Apply did not wait before dispatch.");
                second = fixture.ActionAsync("Apply", review);
                Change(fixture, change);
            }
            finally
            {
                if (gateWait) fixture.ActivationGate.Release();
                else confirmation.TrySetResult(true);
            }
            await GuardAsync(first);
            await GuardAsync(second);
            int expected = change == "unchanged" ? 1 : 0;
            Require(fixture.Service.Commands.Count == expected && fixture.Store.Replacements == expected,
                $"{(gateWait ? "Gate" : "Confirmation")} wait dispatched after {change} or replayed a double tap.");
            Require(fixture.Store.ExpenseCount == expected, "The actual stored expense cardinality differs from admitted mutations.");
        }
    }

    private static async Task DurableLeaseWaitRemainsBoundAsync()
    {
        foreach (string change in new[] { "selection", "revision", "owner-live", "owner-aba", "checkpoint-owner", "departure", "unchanged" })
        {
            using var fixture = new Fixture();
            await fixture.PrepareAsync();
            object review = fixture.Review;
            var store = Value<Sr5CareerSkillGroupCheckpointStore>(review, "Store");
            var checkpoint = Value<Sr5CareerSkillGroupCheckpoint>(review, "Checkpoint");
            Require(store.TryBeginApply(Sr5CareerSkillGroupCheckpointCas.From(checkpoint), out var applying, out string blocker), blocker);
            SetValue(review, "Checkpoint", applying);
            using var timeout = new CancellationTokenSource(Bound);
            IDisposable lease = await store.AcquireDurableApplyingLeaseAsync(applying, timeout.Token).WaitAsync(Bound);
            Task action;
            try
            {
                action = Value<Sr5CareerSkillGroupCoordinator>(review, "Authority").ApplyAsync(
                    Value<Sr5CareerSkillGroupDraft>(review, "Draft"), applying, store, timeout.Token);
                Require(!action.IsCompleted && fixture.Service.Commands.Count == 0, "Apply did not wait on the actual durable lease.");
                Change(fixture, change);
            }
            finally { lease.Dispose(); }
            await GuardAsync(action);
            Require(fixture.Service.Commands.Count == (change == "unchanged" ? 1 : 0),
                $"Lease continuation lost its exact context after {change}.");
        }
    }

    private static async Task UnavailableAndForgedResultsKeepApplyingAsync()
    {
        foreach (string mode in new[] { "unavailable", "forged", "throw" })
        {
            using var fixture = new Fixture();
            await fixture.PrepareAsync();
            fixture.Service.Mode = mode;
            await GuardAsync(fixture.ActionAsync("Apply"));
            CharacterCareerSkillGroupAdvanceCommand admitted = fixture.Service.Commands.Single();
            Require(fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Applying
                && !fixture.Enabled("tablet-career-skill-group-apply") && fixture.Store.ExpenseCount == 0,
                $"{mode} became a passing receipt or unlocked a fresh Apply.");
            await GuardAsync(fixture.ActionAsync("Abandon"));
            Require(fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Applying,
                "Unknown outcome was abandoned without authoritative resolution.");
            fixture.Service.Mode = string.Empty;
            await fixture.ActionAsync("Resolve").WaitAsync(Bound);
            Require(fixture.Service.Commands.Count == 2 && fixture.Service.Commands[1] == admitted
                && fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Applied
                && fixture.Store.Replacements == 1 && fixture.Store.ExpenseCount == 1,
                "Recovery did not reuse the exact atomic command and produce exactly one real XML expense.");
        }
    }

    private static async Task SameCommandRestartRecoveryCreatesOneExpenseAsync()
    {
        using var fixture = new Fixture();
        await fixture.PrepareAsync();
        fixture.Service.Mode = "lost-response-after-commit";
        await GuardAsync(fixture.ActionAsync("Apply"));
        var admitted = fixture.Service.Commands.Single();
        Require(fixture.Store.Replacements == 1 && fixture.Store.ExpenseCount == 1
            && fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Applying,
            "The fault did not occur after the real atomic XML/receipt commit.");
        fixture.Service.Mode = string.Empty;
        fixture.Service.RecreateCore(); // New Core service/adapter, same saved document and ledger.
        fixture.SyncFromStore();
        fixture.RecreatePage();
        await fixture.LoadAsync().WaitAsync(Bound);
        fixture.Select(GroupA);
        await fixture.ActionAsync("Review").WaitAsync(Bound);
        await fixture.ActionAsync("Resolve").WaitAsync(Bound);
        Require(fixture.Service.Commands.Count == 2 && fixture.Service.Commands[1] == admitted
            && fixture.Service.LastResult?.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed,
            "Restart did not recover the exact admitted command through Core ledger replay.");
        var checkpoint = fixture.ReadCheckpoint();
        Require(checkpoint.Phase == Sr5CareerCheckpointPhase.Applied && checkpoint.Receipt is { } receipt
            && receipt.TransactionId == admitted.TransactionId && fixture.Store.Replacements == 1
            && fixture.Store.ExpenseCount == 1, "Restart created a second expense or accepted a different receipt.");
        Require(fixture.Store.ExpenseId == admitted.TransactionId && fixture.Store.LedgerId == admitted.TransactionId,
            "Saved expense and immutable ledger do not belong to the original transaction.");
    }

    private static async Task AbandonAndAcknowledgeArePhaseBoundAsync()
    {
        using var fixture = new Fixture();
        await fixture.PrepareAsync();
        await fixture.ActionAsync("Abandon").WaitAsync(Bound);
        Require(fixture.Backend.Read().Length == 0 && fixture.Store.Replacements == 0,
            "Abandon did not remove only the unexecuted reviewed draft.");
        await fixture.PrepareAsync();
        await GuardAsync(fixture.ActionAsync("Acknowledge"));
        Require(fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Reviewed,
            "Acknowledgement erased an unapplied review.");
        await fixture.ActionAsync("Apply").WaitAsync(Bound);
        Require(fixture.ReadCheckpoint().Phase == Sr5CareerCheckpointPhase.Applied, "The real successful apply did not persist Applied.");
        await fixture.ActionAsync("Acknowledge").WaitAsync(Bound);
        Require(fixture.Backend.Read().Length == 0 && fixture.Store.ExpenseCount == 1,
            "Acknowledgement changed the ledger/expense or retained the acknowledged checkpoint.");
    }

    private static void Change(Fixture fixture, string change)
    {
        switch (change)
        {
            case "selection": fixture.Select(GroupB); break;
            case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other") }; break;
            case "revision": fixture.Revisions(6, 6); break;
            case "saved-revision": fixture.Revisions(5, 4); break;
            case "section": fixture.State = fixture.State with { ActiveSectionId = "gear" }; break;
            case "editor": fixture.State = fixture.State with { ActiveCollectionEditor = fixture.State.ActiveCollectionEditor! with { } }; break;
            case "refresh": fixture.Refresh(); break;
            case "departure": fixture.Depart(); break;
            case "owner-live": fixture.LiveOwner = new(new OwnerScope("other"), InitialOwner.AuthorityInstanceId, 2); break;
            case "owner-aba": fixture.LiveOwner = InitialOwner with { TransitionRevision = InitialOwner.TransitionRevision + 2 }; break;
            case "checkpoint-owner": fixture.Owner.CurrentOwnerId = Guid.NewGuid(); break;
            case "busy": fixture.State = fixture.State with { IsBusy = true }; break;
            case "error": fixture.State = fixture.State with { Error = "controlled failure" }; break;
            case "unchanged": break;
            default: throw new InvalidOperationException("Unknown context mutation.");
        }
    }

    private static CharacterCareerSkillGroupAdvanceQuote ControlledQuote(Guid id, int available = 40,
        bool broken = false, bool disabled = false, int maximum = 12, bool membersExact = true)
    {
        var input = new CharacterCareerSkillGroupAdvanceInput(new(id), true, "sr5", true, membersExact,
            "Equal display label", 0, 1, maximum, available, disabled, broken, new(5, 5),
            [new(SourceA, 1, true, "Physical Active")], [], "<group />", "<settings />");
        Require(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(input, out var quote), "Controlled quote is invalid.");
        return quote;
    }

    private static async Task GuardAsync(Task action)
    {
        try { await action.WaitAsync(Bound); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { } // Negative/unknown host outcomes only; assertions follow.
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static T Value<T>(object owner, string name) => (T)(owner.GetType()
        .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(owner)
        ?? throw new InvalidOperationException($"Missing review {name}."));
    private static void SetValue(object owner, string name, object value) => owner.GetType()
        .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(owner, value);

    private sealed class Backend : ISr5CareerCheckpointBackend
    {
        private string _value = string.Empty;
        public bool DropWrites;
        public string Read() => _value;
        public void Write(string value) { if (!DropWrites) _value = value; }
        public void Remove() => _value = string.Empty;
    }
    private sealed class Owner : ISr5CareerCheckpointOwnerAuthority
    { public Guid CurrentOwnerId { get; set; } = Guid.Parse("55555555-5555-4555-8555-555555555555"); }

    private sealed class Fixture : IDisposable
    {
        public CharacterOverviewState State;
        public OwnerContextStamp LiveOwner = InitialOwner;
        public readonly Backend Backend = new();
        public readonly Backend MutationBackend = new();
        public readonly Owner Owner = new();
        public readonly MemoryStore Store = new();
        public readonly Service Service;
        public readonly RunnerSessionCoordinator Coordinator;
        public TabletBuildPage Page;
        public Task<bool> Confirmation = Task.FromResult(true);
        public Func<Task<CareerSkillGroupAdvanceEditorState?>>? LoadOverride;
        public readonly IOwnerContextLeaseAccessor Owners;

        public Fixture(IOwnerContextLeaseAccessor? owners = null)
        {
            Owners = owners ?? new ControlledOwners(() => LiveOwner);
            LiveOwner = Owners.Capture();
            Store.PartitionOwner = LiveOwner.Owner;
            State = Program.NewCreationOverview(Workspace, 5, 5);
            State = State with
            {
                DisplayOwnerContext = LiveOwner, Session = State.Session with { OwnerContext = LiveOwner },
                Profile = State.Profile! with { Created = true },
                Rules = new CharacterRulesSection("SR5", "", "", 0, 0, 0, 0, []),
                ActiveSectionId = "skills", ActiveCollectionEditor = new("skills", WorkspaceCollectionKind.Skill, null, [])
            };
            Service = new(Store, Owners);
            var presenter = TabletGroupTestProxy.Create<ICharacterOverviewPresenter>((name, args) => name switch
            {
                "get_State" => State,
                "PrepareCareerSkillGroupAdvanceAsync" => LoadOverride?.Invoke() ?? Task.FromResult<CareerSkillGroupAdvanceEditorState?>(Editor()),
                "LoadAsync" => LoadSaved(args),
                _ => throw new InvalidOperationException($"Unexpected group presenter call {name}; no compatibility mutation allowed.")
            });
            var shell = TabletGroupTestProxy.Create<IShellPresenter>((name, _) => name switch
            {
                "get_State" => ShellState.Empty with { OwnerContext = Owners.Capture() },
                "SyncWorkspaceContextAsync" => Task.CompletedTask,
                _ => throw new InvalidOperationException($"Unexpected shell call {name}.")
            });
            Coordinator = new(presenter, TabletOwnerCaptureProxy.Create(() => Owners.Capture()), null!, null!, null!, null!, null!,
                shell, TabletGroupTestProxy.Create<IShellSurfaceResolver>((name, _) => name == "Resolve"
                    ? ShellSurfaceState.Empty : throw new InvalidOperationException(name)), null!, null!, null!, null!,
                StrictPageProxy.Create<IAndroidAccountLinkService>(), null!, null!, careerSkillGroupService: Service);
            Page = NewPage();
            Refresh();
        }

        private Task LoadSaved(object?[]? args)
        {
            Require(args is [CharacterWorkspaceId id, CancellationToken] && id == Workspace, "Reload lost the exact workspace.");
            SyncFromStore();
            return Task.CompletedTask;
        }
        public CareerSkillGroupAdvanceEditorState Editor() => new(Workspace, Store.Current.ContentRevision,
            new[] { GroupA, GroupB }.Select(id => Service.Quote(new(Workspace, new(id))).Binding?.Quote
                ?? throw new InvalidOperationException("Real Core fixture cannot quote group.")).ToArray(), 0);
        public void SyncFromStore() => Revisions(Store.Current.ContentRevision, Store.Current.SavedRevision);
        public void Revisions(long content, long saved)
        {
            var open = State.ActiveWorkspace! with { ContentRevision = content, SavedRevision = saved };
            State = State with { OpenWorkspaces = [open], Session = State.Session with { OpenWorkspaces = [open] } };
        }
        private TabletBuildPage NewPage() => new(Coordinator, (_, _, _, _) => Confirmation,
            skillGroupStoreFactory: authority => new Sr5CareerSkillGroupCheckpointStore(Backend, authority, new Sr5CareerMutationOwnerStore(MutationBackend)),
            skillGroupOwner: Owner, skillGroupDispatch: action => action());
        public object Review => typeof(TabletBuildPage).GetField("_skillGroupReview", Private)!.GetValue(Page)
            ?? throw new InvalidOperationException("Missing selected group review.");
        public Task LoadAsync() => (Task)typeof(TabletBuildPage).GetMethod("LoadTabletSkillGroupsAsync", Private)!
            .Invoke(Page, [State, Generation])!;
        public Task ActionAsync(string action, object? review = null) => (Task)typeof(TabletBuildPage)
            .GetMethod(action + "TabletSkillGroupAsync", Private)!.Invoke(Page, [review ?? Review])!;
        public async Task PrepareAsync()
        {
            await LoadAsync().WaitAsync(Bound); Select(GroupA);
            await ActionAsync("Review").WaitAsync(Bound);
        }
        public Sr5CareerSkillGroupCheckpoint ReadCheckpoint()
        {
            var store = new Sr5CareerSkillGroupCheckpointStore(Backend);
            Require(store.TryRead(out var checkpoint, out string blocker), blocker);
            return checkpoint;
        }
        public void Select(Guid id) => ((IButtonController)Button($"tablet-career-skill-group-{id:D}")).SendClicked();
        public long Generation => (long)typeof(TabletBuildPage).GetField("_collectionGeneration", Private)!.GetValue(Page)!;
        public SemaphoreSlim ActivationGate => (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate", Private)!.GetValue(Coordinator)!;
        public Button Button(string id) => Elements(Page).OfType<Button>().Single(element => element.AutomationId == id);
        public Label Label(string id) => Elements(Page).OfType<Label>().Single(element => element.AutomationId == id);
        public bool Has(string id) => Elements(Page).Any(element => element.AutomationId == id);
        public bool Enabled(string id) => Elements(Page).OfType<Button>().Any(element => element.AutomationId == id && element.IsEnabled && element.IsVisible);
        public void Refresh() => typeof(TabletBuildPage).GetMethod("Refresh", Private)!.Invoke(Page, null);
        public void Depart() => typeof(TabletBuildPage).GetMethod("OnDisappearing", Private)!.Invoke(Page, null);
        public void RecreatePage() { Depart(); Page = NewPage(); Refresh(); }
        public void Dispose() { Depart(); Coordinator.Dispose(); }
    }

    private sealed class Service(MemoryStore store, IOwnerContextLeaseAccessor owners) :
        ICharacterCareerSkillGroupAdvanceService, IAndroidOwnerBoundCareerSkillGroupService
    {
        private AndroidOwnerBoundCareerSkillGroupService _core = CreateCore(store, owners);
        public readonly List<CharacterCareerSkillGroupAdvanceCommand> Commands = [];
        public string Mode = string.Empty;
        public Action? BeforeOwnerAdmission;
        public CharacterCareerSkillGroupAdvanceResult? LastResult;
        public void RecreateCore() => _core = CreateCore(store, owners);
        public CharacterCareerSkillGroupQuoteResult Quote(CharacterCareerSkillGroupQuoteRequest request) => _core.Quote(request);
        public CharacterCareerSkillGroupAdvanceResult Advance(CharacterCareerSkillGroupAdvanceCommand command)
            => _core.Advance(command);
        public CharacterCareerSkillGroupAdvanceResult Advance(OwnerContextStamp owner, CharacterCareerSkillGroupAdvanceCommand command)
        {
            BeforeOwnerAdmission?.Invoke();
            Commands.Add(command);
            if (Mode == "throw") throw new InvalidOperationException("Controlled pre-dispatch failure.");
            store.Unavailable = Mode is "unavailable" or "forged";
            CharacterCareerSkillGroupAdvanceResult result;
            try { result = _core.Advance(owner, command); }
            finally { store.Unavailable = false; }
            if (Mode == "lost-response-after-commit") throw new InvalidOperationException("Controlled lost response after real commit.");
            if (Mode == "forged") result = result with { TransactionId = Guid.NewGuid() };
            LastResult = result;
            return result;
        }
        private static AndroidOwnerBoundCareerSkillGroupService CreateCore(MemoryStore store, IOwnerContextLeaseAccessor owners)
            => new(store.Interface, new Sources(), new Settings(), owners);
    }

    // Controlled page tests only. Real credential exclusion is separately
    // exercised below with AndroidAccountOwnerContextAccessor and its writer.
    private sealed class ControlledOwners(Func<OwnerContextStamp> capture) : IOwnerContextLeaseAccessor
    {
        public OwnerScope Current => Capture().Owner;
        public OwnerContextStamp Capture() => capture();
        public bool TryAcquire(OwnerContextStamp expected,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            lease = expected.IsValid && expected == Capture() ? new ControlledLease(expected) : null;
            return lease is not null;
        }
        private sealed class ControlledLease(OwnerContextStamp stamp) : IOwnerContextLease
        {
            public OwnerContextStamp Stamp => stamp;
            public void Dispose() { }
        }
    }

    private sealed class MemoryStore
    {
        private readonly object _gate = new();
        public WorkspaceStoredDocument Current { get; private set; } = new(Workspace,
            new WorkspaceDocument(Xml(), CharacterCareerSkillGroupAdvanceRules.RulesetId), 5, 5, DateTimeOffset.UtcNow);
        public bool Unavailable;
        public OwnerScope PartitionOwner = InitialOwner.Owner;
        public Action? BeforeCommit;
        public int UnscopedCalls;
        public int ScopedCalls;
        public int Replacements { get; private set; }
        public IWorkspaceStore Interface => TabletGroupTestProxy.Create<IWorkspaceStore>(Invoke);
        public int ExpenseCount => XDocument.Parse(Current.Document.Content).Root!.Element("expenses")?.Elements("expense").Count() ?? 0;
        public Guid ExpenseId => Guid.Parse(XDocument.Parse(Current.Document.Content).Root!.Element("expenses")!.Element("expense")!.Element("guid")!.Value);
        public Guid LedgerId => Guid.Parse(XDocument.Parse(Current.Document.Content).Root!.Element("androidcareerskillgroupadvanceledger")!.Element("entry")!.Element("transactionid")!.Value);
        private object Invoke(string name, object?[]? args)
        {
            lock (_gate)
            {
                bool scoped = args is [OwnerScope, ..];
                if (scoped)
                {
                    ScopedCalls++;
                    Require((OwnerScope)args![0]! == PartitionOwner, "Core touched a foreign owner partition.");
                    args = args![1..];
                }
                else
                {
                    UnscopedCalls++;
                    Require(PartitionOwner == OwnerScope.LocalSingleUser, "An account runner used the local store overload.");
                }
                if (name == "Get" && args is [CharacterWorkspaceId id])
                    return Unavailable ? new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Unavailable)
                        : id == Current.Id ? new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Success, Current)
                        : new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Missing);
                if (name != "ReplaceWorkspaceDocumentAndCheckpoint"
                    || args is not [CharacterWorkspaceId target, long expected, WorkspaceDocument document])
                    throw new InvalidOperationException($"Unexpected store operation {name}; only exact read/atomic CAS are supported.");
                if (target != Current.Id) return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Missing);
                if (expected != Current.ContentRevision) return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Conflict);
                BeforeCommit?.Invoke();
                Replacements++;
                Current = Current with { Document = document, ContentRevision = expected + 1, SavedRevision = expected + 1 };
                return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Success,
                    new WorkspaceStoreEntry(Current.Id, Current.LastUpdatedUtc, Current.ContentRevision, Current.SavedRevision));
            }
        }
        private static string Xml() => $"""
            <character><created>True</created><settings>profile-sr5</settings><karma>40</karma>
            <newskills><skills>
            <skill><guid>aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa</guid><suid>{SourceA:D}</suid><isknowledge>False</isknowledge><skillcategory>Combat Active</skillcategory><base>0</base><karma>0</karma></skill>
            <skill><guid>bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb</guid><suid>{SourceB:D}</suid><isknowledge>False</isknowledge><skillcategory>Physical Active</skillcategory><base>0</base><karma>0</karma></skill>
            </skills><groups>
            <group><id>{GroupA:D}</id><name>Firearms</name><base>0</base><karma>1</karma><isbroken>False</isbroken></group>
            <group><id>{GroupB:D}</id><name>Athletics</name><base>0</base><karma>1</karma><isbroken>False</isbroken></group>
            </groups></newskills><improvements /></character>
            """;
    }
    private sealed class Settings : IAndroidCareerSkillGroupSettingsCatalog
    {
        public string ReadCatalogJson() => JsonSerializer.Serialize(new
        {
            ActiveProfileId = "profile-sr5", Profiles = new[] { new { Id = "profile-sr5", Name = "Controlled SR5",
                Xml = "<settings><karmacost><karmanewskillgroup>5</karmanewskillgroup><karmaimproveskillgroup>5</karmaimproveskillgroup></karmacost><maxskillrating>12</maxskillrating><usepointsonbrokengroups>False</usepointsonbrokengroups></settings>" } }
        });
    }
    private sealed class Sources : ICharacterSourceDataResolver, ICharacterSourceDataContext
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml) => this;
        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName, string improvementSource, out int deviceRating)
            => throw new InvalidOperationException("The skill-group fixture must not resolve cyberware grades.");

        public bool TryResolveVehicleModBonuses(
            string sourceId, string name, out CharacterVehicleModSourceBonuses bonuses)
            => throw new InvalidOperationException("The skill-group fixture must not resolve vehicle modifications.");

        public bool TryResolveActiveSkillSource(string sourceSkillId, out CharacterActiveSkillSource source)
        {
            if (!Guid.TryParse(sourceSkillId, out Guid id) || id != SourceA && id != SourceB)
            { source = CharacterActiveSkillSource.Unavailable; return false; }
            string name = id == SourceA ? "Automatics" : "Running", group = id == SourceA ? "Firearms" : "Athletics";
            source = new(id.ToString("D"), name, id == SourceA ? "Combat Active" : "Physical Active", group, "AGI", false, false, false, false,
                $"<skill><id>{id:D}</id><name>{name}</name><skillgroup>{group}</skillgroup></skill>");
            return true;
        }
    }
    private static IEnumerable<Element> Elements(Element root)
    {
        yield return root;
        IEnumerable<Element> children = root switch
        {
            ContentPage page when page.Content is not null => [page.Content],
            ScrollView scroll when scroll.Content is not null => [scroll.Content],
            Border border when border.Content is not null => [border.Content],
            Layout layout => layout.Children.OfType<Element>(), _ => []
        };
        foreach (Element child in children)
        foreach (Element descendant in Elements(child)) yield return descendant;
    }
}

public class TabletGroupTestProxy : DispatchProxy
{
    private Func<string, object?[]?, object?> _invoke = null!;
    public static T Create<T>(Func<string, object?[]?, object?> invoke) where T : class
    {
        T instance = Create<T, TabletGroupTestProxy>();
        ((TabletGroupTestProxy)(object)instance)._invoke = invoke;
        return instance;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        string name = targetMethod?.Name ?? throw new InvalidOperationException("Missing fixture dependency method.");
        return name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal)
            ? null : _invoke(name, args);
    }
}
