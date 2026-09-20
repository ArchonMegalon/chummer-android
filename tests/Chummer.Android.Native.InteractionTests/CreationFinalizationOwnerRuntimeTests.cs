using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunCreationMagicBackgroundAsync(string contentRoot, string sourceDirectory,
        CharacterWorkspaceId id, CharacterCreationMagicResonanceDesktopDraft draft)
    {
        var owners = new ControlledLinkedOwner();
        MagicReadProbe? probe = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            productionCreationOverview: true, creationSkillsSeed: target =>
            {
                foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(target, Path.GetRelativePath(sourceDirectory, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination);
                }
            }, magicDecorator: actual => probe = new(actual, owners));
        runtime.Id = id;
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var before = store.Get(id).Value!;
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            probe!.UiThreadId = Environment.CurrentManagedThreadId;
            var original = runtime.Coordinator.State;
            var page = new CreationMagicResonancePage(runtime.Coordinator);
            var prepare = typeof(CreationMagicResonancePage).GetMethod("PrepareForAppearanceRefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var refresh = typeof(CreationMagicResonancePage).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.BeforeRead = () =>
            {
                entered.TrySetResult();
                Require(release.Wait(TimeSpan.FromSeconds(10)), "Magic test read was not released.");
            };
            Task loading = (Task)prepare.Invoke(page, [CancellationToken.None])!;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(!loading.IsCompleted, "Core read unexpectedly completed before release.");
                var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ui.Post(_ => heartbeat.SetResult(), null);
                await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally { release.Set(); }
            await loading;
            probe.BeforeRead = null;
            refresh.Invoke(page, null);
            refresh.Invoke(page, null);
            Require(probe.Loads == 1, "Rendering reloaded the entire Magic catalog.");
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            Require(body.Children.OfType<Button>().Any(button => button.AutomationId == "creation-magic-resonance-open-review" && button.IsEnabled),
                "Background catalog did not expose the actual Core-ready editor.");
            var review = await runtime.Coordinator.ReviewCreationMagicResonanceForDisplayAsync(original, original.CreationMagicResonanceEditor!, draft);
            Require(review.Preview.CanConfirm && probe.Previews == 1 && owners.ActiveLeases == 0,
                "Background preview lost authority or retained an owner lease.");

            using var canceled = new CancellationTokenSource();
            probe.BeforeRead = canceled.Cancel;
            try
            {
                await runtime.Coordinator.LoadCreationMagicResonanceForDisplayAsync(original, canceled.Token);
                throw new Exception("Canceled Magic read returned authority.");
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
            probe.BeforeRead = null;
            Require(owners.ActiveLeases == 0, "Canceled read retained its owner lease.");
            int reads = probe.Loads;
            var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime.Coordinator)!;
            await gate.WaitAsync();
            Task stale;
            try
            {
                stale = runtime.Coordinator.ReviewCreationMagicResonanceForDisplayAsync(original, original.CreationMagicResonanceEditor!, draft);
                owners.Set(ContactsOwnerB);
                owners.Set(OwnerScope.LocalSingleUser);
            }
            finally { gate.Release(); }
            try { await stale; throw new Exception("Owner ABA accepted an old Magic preview."); }
            catch (InvalidOperationException error) when (error.Message == CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision) { }
            Require(probe.Loads == reads && owners.ActiveLeases == 0, "Stale Magic worker entered Core.");
            refresh.Invoke(page, null);
            Require(!body.Children.OfType<Button>().Any(button => button.AutomationId == "creation-magic-resonance-open-review"),
                "Owner transition left the old Magic catalog actionable.");
        });
        RequireSameRewardDocument(before, store.Get(id).Value!);
        Console.WriteLine("PASS Magic background catalog/preview, UI heartbeat, render without reload, cancellation, owner ABA and unchanged workspace");
    }

    private sealed class MagicReadProbe(ICharacterCreationMagicResonanceService inner, ControlledLinkedOwner owners)
        : ICharacterCreationMagicResonanceService
    {
        public int UiThreadId { get; set; }
        public int Loads { get; private set; }
        public int Previews { get; private set; }
        public Action? BeforeRead { get; set; }
        private void Check()
        {
            Require(Environment.CurrentManagedThreadId != UiThreadId && owners.ActiveLeases == 1,
                "Magic read must run off UI with the original owner lease.");
            BeforeRead?.Invoke();
        }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(CharacterCreationMagicResonanceLoadRequest request)
        { Check(); Loads++; return inner.Load(request); }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(CharacterCreationMagicResonancePreviewRequest request)
        { Check(); Previews++; return inner.Preview(request); }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(CharacterCreationMagicResonanceConfirmRequest request)
            => throw new InvalidOperationException("Read-only background test must not confirm.");
    }

    public static async Task RunCreationEntryGearCasesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));

        var owners = new ControlledLinkedOwner();
        QualitiesReadProbe? qualitiesProbe = null;
        await using (var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationFinalization: true,
            productionCreationOverview: true, qualitiesDecorator: actual => qualitiesProbe = new(actual, owners)))
        {
            var stored = PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true);
            await HydrateFinalizationOwnerAsync(runtime, owners, stored);
            var overview = runtime.Coordinator.State;
            var loaded = runtime.Services.GetRequiredService<ICharacterCreationQualitiesService>().Load(new(runtime.Id));
            Console.WriteLine("QUALITIES_ENTRY " + JsonSerializer.Serialize(new
            {
                loaded.Outcome, loaded.Blockers, overview.WorkspaceId, overview.ContentRevision, overview.SavedRevision,
                created = overview.Profile?.Created, loaded.Value?.Binding, loaded.Value?.CanEdit,
                stateBlockers = loaded.Value?.Blockers, pending = loaded.Value?.PendingDraft is not null,
                matchesOverview = loaded.Value is { } value && CreationQualitiesPhoneAuthority.MatchesOverview(value, overview),
                snapshotMatches = loaded.Value is { } snapshot && snapshot.SnapshotDigest == CharacterCreationQualitiesRules.ComputeStateDigest(snapshot)
            }));
            Require(loaded.Value is { PendingDraft: null } && CreationQualitiesPhoneAuthority.IsReady(loaded.Value, overview),
                "Actual Core Qualities must be ready before its first draft exists.");
            var stage = overview.CreationWizard!.Steps.Single(item => item.StepId == CharacterCreationWizardStepIds.Qualities);
            Console.WriteLine("QUALITIES_STAGE " + JsonSerializer.Serialize(stage));
            Require(stage.Blockers.SequenceEqual([CharacterCreationFinalizationBlockers.QualitiesDraftRequired]),
                "Expected the actual wizard's own Qualities-draft finalization blocker.");
            Require(BuildPageUiProjection.CanOpenExactTypedCreationStage(stage,
                CharacterCreationWizardStepIds.Qualities, CreationQualitiesPhoneAuthority.IsReady(loaded.Value!, overview)),
                "Actual Core-ready Qualities cannot open to author its first draft.");
            Require(!CreationQualitiesPhoneAuthority.IsReady(loaded.Value! with { Binding = loaded.Value!.Binding with { ContentRevision = overview.ContentRevision + 1 } }, overview),
                "Qualities accepted a stale revision.");
            string digest = loaded.Value!.Binding.AuxiliaryStateDigest;
            foreach (string invalid in new[] { "sha256:" + digest, digest.ToUpperInvariant(), digest[..63], new string('g', 64), "", " " + digest })
                Require(!CreationQualitiesPhoneAuthority.MatchesOverview(loaded.Value with { Binding = loaded.Value.Binding with { AuxiliaryStateDigest = invalid } }, overview),
                    "Qualities accepted a noncanonical workspace auxiliary hash.");
            Require(!CreationQualitiesPhoneAuthority.MatchesOverview(loaded.Value with { Binding = loaded.Value.Binding with { RawCharacterXmlDigest = digest } }, overview),
                "The auxiliary-hash fix relaxed another Qualities digest contract.");
            using var ui = new IssuedPageUiContext();
            await ui.RunAsync(() => VerifyQualitiesCatalogNavigationAsync(runtime, qualitiesProbe!, owners, ui));
            Console.WriteLine("PASS actual Core Qualities entry before first draft, stale revision rejected");
        }

        owners = new ControlledLinkedOwner();
        await using (var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationFinalization: true, productionCreationOverview: true))
        {
            var stored = PrepareActualFinalizationReadyContext(runtime);
            await HydrateFinalizationOwnerAsync(runtime, owners, stored);
            var overview = runtime.Coordinator.State;
            var presenter = new CharacterCreationGearInteractionPresenter(runtime.Services.GetRequiredService<ICharacterCreationGearService>());
            var loaded = presenter.Load(overview);
            Require(loaded.State is not null, "Actual Core Gear did not load: " + loaded.Outcome);
            var state = loaded.State!;
            Require(CreationPrerequisitePhoneAuthority.IsCanonicalAuxiliaryStateDigest(state.Binding.AuxiliaryStateDigest),
                "Actual Core Gear must return the raw lowercase workspace auxiliary hash.");
            Require(CreationGearPhoneAuthority.IsReady(state, overview),
                "Actual Core Gear was rejected by the native validator.");
            string digest = state.Binding.AuxiliaryStateDigest;
            foreach (string invalid in new[] { "sha256:" + digest, digest.ToUpperInvariant(), digest[..63], new string('g', 64), "", " " + digest })
                Require(!CreationGearPhoneAuthority.IsReady(state with { Binding = state.Binding with { AuxiliaryStateDigest = invalid } }, overview),
                    "Gear accepted a noncanonical workspace auxiliary hash.");
            Require(!CreationGearPhoneAuthority.IsReady(state with { Binding = state.Binding with { ContentRevision = overview.ContentRevision + 1 } }, overview),
                "Gear accepted a stale workspace revision.");
            Require(!CreationGearPhoneAuthority.IsReady(state with { Binding = state.Binding with { RawCharacterXmlDigest = digest } }, overview),
                "The auxiliary-hash fix relaxed a different digest contract.");
            var flashlight = state.Authority.Options.Single(option => option.OptionId == "gear:c49a893a-d445-4aac-bec0-c8501cba4c2c");
            var prepared = presenter.Prepare(overview, [new(flashlight.OptionId, 1)]);
            Require(prepared.PreparedPreview is not null, "Real Gear receipt fixture did not prepare: " + JsonSerializer.Serialize(prepared.Blockers));
            var proposal = prepared.PreparedPreview!;
            var confirmed = presenter.Confirm(overview, new(proposal, proposal.Preview.PreviewDigest, proposal.IdempotencyKey, true));
            Console.WriteLine("GEAR_CONFIRM " + JsonSerializer.Serialize(new
            {
                confirmed.Outcome, confirmed.Blockers, confirmed.Receipt,
                receiptMatches = confirmed.Receipt is { } r && CreationGearPhoneAuthority.ReceiptMatches(proposal, r),
                refreshedMatches = confirmed.Receipt is { } receipt && confirmed.RefreshedState is { } refreshed
                    && CreationGearPhoneAuthority.RefreshedStateMatches(proposal, receipt, refreshed),
                budget = confirmed.RefreshedState?.Budget, draftBudget = confirmed.RefreshedState?.PendingDraft?.Budget
            }));
            Require(confirmed.Outcome == CharacterCreationGearOutcomes.Applied && confirmed.Receipt is { } saved
                && confirmed.RefreshedState is { } current && CreationGearPhoneAuthority.ReceiptMatches(proposal, saved)
                && CreationGearPhoneAuthority.RefreshedStateMatches(proposal, saved, current),
                "Actual nonempty Gear receipt was rejected by the native boundary.");
            var persisted = confirmed.RefreshedState!;
            var savedReceipt = confirmed.Receipt!;
            var copiedBudget = persisted.Budget with { Blockers = new List<string>(persisted.Budget.Blockers) };
            Require(CreationGearPhoneAuthority.RefreshedStateMatches(proposal, savedReceipt, persisted with { Budget = copiedBudget }),
                "Equal deserialized budget values must not depend on collection reference identity.");
            foreach (var corrupt in new[]
            {
                copiedBudget with { TotalStartingNuyen = copiedBudget.TotalStartingNuyen + 1 },
                copiedBudget with { BasketCost = copiedBudget.BasketCost + 1 },
                copiedBudget with { RemainingNuyen = copiedBudget.RemainingNuyen + 1 },
                copiedBudget with { Overspend = copiedBudget.Overspend + 1 },
                copiedBudget with { IsExact = false },
                copiedBudget with { Blockers = ["unexpected-budget-blocker"] }
            })
                Require(!CreationGearPhoneAuthority.RefreshedStateMatches(proposal, savedReceipt, persisted with { Budget = corrupt }),
                    "Gear receipt accepted a changed budget value or blocker list.");
            await HydrateFinalizationOwnerAsync(runtime, owners, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
            var cold = presenter.Load(runtime.Coordinator.State);
            Require(cold.State is { } reopened && CreationGearPhoneAuthority.RefreshedStateMatches(proposal, savedReceipt, reopened),
                "Gear receipt failed after a fresh persisted-state load.");
            Console.WriteLine("PASS actual nonempty Gear receipt, cold reload, value-equal budgets and hostile budget deltas");
            using var ui = new IssuedPageUiContext();
            await ui.RunAsync(() => VerifyGearCatalogNavigationAsync(runtime, presenter, owners, ui));
            Console.WriteLine("PASS actual Core Gear raw auxiliary digest, hostile formats and stale revision rejected");
        }
    }

    private static async Task VerifyQualitiesCatalogNavigationAsync(NativeRewardRuntime runtime,
        QualitiesReadProbe probe, ControlledLinkedOwner owners, IssuedPageUiContext ui)
    {
        var before = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var page = new CreationQualitiesPage(runtime.Coordinator);
        var refresh = typeof(CreationQualitiesPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var filter = typeof(CreationQualitiesPage).GetMethod("ApplyFilter", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var prepare = typeof(CreationQualitiesPage).GetMethod("PrepareForAppearanceRefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task Prepare(CreationQualitiesPage target, CancellationToken token = default)
            => (Task)prepare.Invoke(target, [token])!;
        VerticalStackLayout Body() => (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        Border[] Rows() => Body().Children.OfType<Border>()
            .Where(row => row.Content is Grid grid && grid.Children.OfType<Button>()
                .Any(button => button.AutomationId?.StartsWith("creation-quality-option-", StringComparison.Ordinal) == true)).ToArray();
        string OptionId(Border row) => ((Grid)row.Content!).Children.OfType<Button>().Single().AutomationId;
        Button Pager(string suffix) => Body().Children.OfType<HorizontalStackLayout>()
            .SelectMany(row => row.Children.OfType<Button>())
            .Single(button => button.AutomationId == "creation-qualities-catalog-" + suffix);

        probe.UiThreadId = Environment.CurrentManagedThreadId;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.BeforeLoad = () =>
        {
            entered.TrySetResult();
            Require(release.Wait(TimeSpan.FromSeconds(10)), "Qualities test read was never released.");
        };
        Task loading = Prepare(page);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Require(!loading.IsCompleted && Rows().Length == 0
                    && Body().Children.OfType<ActivityIndicator>().Any(item => item.IsRunning),
                "Qualities must show loading without exposing actions before Core completes.");
            var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Post(_ => heartbeat.SetResult(), null);
            await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { release.Set(); }
        await loading;
        probe.BeforeLoad = null;
        refresh.Invoke(page, null);
        string[] first = Rows().Select(OptionId).ToArray();
        Require(first.Length == 20 && Pager("next").IsEnabled && !Pager("previous").IsEnabled,
            "The real catalog must render only the first bounded page, with honest navigation.");
        Button review = Body().Children.OfType<Button>().Single(button => button.AutomationId == "creation-qualities-open-review");
        Require(review.IsEnabled && Body().Children.IndexOf(review) < Body().Children.IndexOf(Rows()[0]),
            "Review must be available before the catalog, including an empty valid selection.");

        ((IButtonController)Pager("next")).SendClicked();
        Require(Rows().Length == 20 && !Rows().Select(OptionId).Intersect(first).Any()
                && Pager("previous").IsEnabled,
            "Next must show another bounded set of exact Core identities.");
        ((IButtonController)Pager("previous")).SendClicked();
        Require(Rows().Select(OptionId).SequenceEqual(first),
            "Previous must restore the same exact identities.");

        filter.Invoke(page, ["no-such-quality-regression-20260919"]);
        Require(Rows().Length == 0 && !Pager("next").IsEnabled && !Pager("previous").IsEnabled,
            "An empty search must not leave stale rows or enabled navigation.");
        var search = Body().Children.OfType<SearchBar>().Single();
        search.Text = string.Empty;
        Require(Rows().Select(OptionId).SequenceEqual(first),
            "Clearing search must return to the first catalog page.");
        Require(probe.LoadCalls == 1,
            "Rendering, paging and filtering must not reload Core after appearance preparation.");

        // Cancellation may not stop Core's synchronous read, but must discard
        // its result and release both the activation gate and original-owner lease.
        var canceledPage = new CreationQualitiesPage(runtime.Coordinator);
        using var cancellation = new CancellationTokenSource();
        release.Reset();
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.BeforeLoad = () =>
        {
            entered.TrySetResult();
            Require(release.Wait(TimeSpan.FromSeconds(10)), "Canceled Qualities read was never released.");
        };
        Task canceled = Prepare(canceledPage, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
        }
        finally { release.Set(); }
        try { await canceled; throw new InvalidOperationException("Canceled Qualities load was accepted."); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Require(typeof(CreationQualitiesPage).GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(canceledPage) is null && owners.ActiveLeases == 0,
            "A departed appearance retained Core data or its owner lease.");
        probe.BeforeLoad = null;

        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime.Coordinator)!;
        await gate.WaitAsync();
        Task stale;
        try
        {
            stale = Prepare(new CreationQualitiesPage(runtime.Coordinator));
            owners.Set(ContactsOwnerB);
            owners.Set(OwnerScope.LocalSingleUser);
        }
        finally { gate.Release(); }
        await stale;
        Require(probe.LoadCalls == 2, "Queued Qualities load crossed an owner A→B→A transition.");
        refresh.Invoke(page, null);
        Require(Rows().Length == 0 && !Body().Children.OfType<Button>()
                .Any(button => button.AutomationId == "creation-qualities-open-review"),
            "An old cached catalog remained actionable after an owner transition.");

        RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        Require(runtime.Coordinator.State.ContentRevision == before.ContentRevision,
            "Catalog navigation must not mutate the workspace or increment its revision.");
        Console.WriteLine("PASS native Qualities background read, live UI heartbeat, one-read paging/search, canceled load and owner ABA rejection");
    }

    private static async Task VerifyGearCatalogNavigationAsync(NativeRewardRuntime runtime,
        ICharacterCreationGearInteractionPresenter presenter, ControlledLinkedOwner owners, IssuedPageUiContext ui)
    {
        var before = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var probe = new GearReadProbe(presenter, owners) { UiThreadId = Environment.CurrentManagedThreadId };
        var page = new CreationGearPage(runtime.Coordinator, probe, runtime.Presenter);
        var refresh = typeof(CreationGearPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var prepare = typeof(CreationGearPage).GetMethod("PrepareForAppearanceRefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task Prepare(CreationGearPage target, CancellationToken token = default) => (Task)prepare.Invoke(target, [token])!;
        VerticalStackLayout Body() => (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        string[] Rows() => Body().Children.OfType<Border>().SelectMany(row => row.Content is Grid grid
                ? grid.Children.OfType<Button>() : Enumerable.Empty<Button>())
            .Where(button => button.AutomationId?.StartsWith("creation-gear-catalog-", StringComparison.Ordinal) == true)
            .Select(button => button.AutomationId).ToArray();
        Button Pager(string suffix) => Body().Children.OfType<HorizontalStackLayout>()
            .SelectMany(row => row.Children.OfType<Button>())
            .Single(button => button.AutomationId == "creation-gear-catalog-" + suffix);

        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.BeforeLoad = () =>
        {
            entered.TrySetResult();
            Require(release.Wait(TimeSpan.FromSeconds(10)), "Gear test read was never released.");
        };
        Task loading = Prepare(page);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Require(!loading.IsCompleted && Rows().Length == 0
                && Body().Children.OfType<ActivityIndicator>().Any(item => item.IsRunning),
                "Gear must render loading without exposing catalog actions before Core completes.");
            var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Post(_ => heartbeat.SetResult(), null);
            await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { release.Set(); }
        await loading;
        probe.BeforeLoad = null;
        refresh.Invoke(page, null);
        string[] first = Rows();
        Require(first.Length == 40 && Pager("next").IsEnabled, "Gear did not render a bounded catalog.");
        ((IButtonController)Pager("next")).SendClicked();
        Require(!Rows().Intersect(first).Any() && Pager("previous").IsEnabled, "Gear paging did not advance exact identities.");
        ((IButtonController)Pager("previous")).SendClicked();
        Require(Rows().SequenceEqual(first), "Gear previous page changed identities.");
        typeof(CreationGearPage).GetMethod("ApplyFilter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, ["Flashlight"]);
        Require(Rows().Length > 0 && Rows().Length < first.Length, "Real Gear search did not filter the catalog.");
        Body().Children.OfType<SearchBar>().Single().Text = string.Empty;
        Require(Rows().SequenceEqual(first) && probe.LoadCalls == 1,
            "Gear search, clear and paging must reuse one accepted Core read.");

        var canceledPage = new CreationGearPage(runtime.Coordinator, probe, runtime.Presenter);
        using var cancellation = new CancellationTokenSource();
        release.Reset();
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.BeforeLoad = () =>
        {
            entered.TrySetResult();
            Require(release.Wait(TimeSpan.FromSeconds(10)), "Canceled Gear read was never released.");
        };
        Task canceled = Prepare(canceledPage, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
        }
        finally { release.Set(); }
        try { await canceled; throw new InvalidOperationException("Canceled Gear load was accepted."); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Require(typeof(CreationGearPage).GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(canceledPage) is null && owners.ActiveLeases == 0,
            "Canceled Gear read retained its result or original-owner lease.");
        probe.BeforeLoad = null;

        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        await gate.WaitAsync();
        Task stale;
        try
        {
            stale = Prepare(new CreationGearPage(runtime.Coordinator, probe, runtime.Presenter));
            owners.Set(ContactsOwnerB);
            owners.Set(OwnerScope.LocalSingleUser);
        }
        finally { gate.Release(); }
        await stale;
        refresh.Invoke(page, null);
        Require(probe.LoadCalls == 2 && Rows().Length == 0,
            "Gear loaded or retained actionable rows across an owner A→B→A transition.");
        RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        Console.WriteLine("PASS native Gear background read, UI heartbeat, one-read paging/search, cancellation and owner ABA rejection");
    }

    private sealed class GearReadProbe(ICharacterCreationGearInteractionPresenter inner,
        ControlledLinkedOwner owners) : ICharacterCreationGearInteractionPresenter
    {
        public int UiThreadId { get; init; }
        public int LoadCalls { get; private set; }
        public Action? BeforeLoad { get; set; }
        public CharacterCreationGearInteractionLoadResult Load(CharacterOverviewState overview)
        {
            Require(Environment.CurrentManagedThreadId != UiThreadId && owners.ActiveLeases == 1,
                "Gear read must run off UI with the original owner lease.");
            LoadCalls++;
            BeforeLoad?.Invoke();
            return inner.Load(overview);
        }
        public CharacterCreationGearInteractionPrepareResult Prepare(CharacterOverviewState overview,
            IReadOnlyList<CharacterCreationGearSelection> basket) => inner.Prepare(overview, basket);
        public CharacterCreationGearInteractionConfirmResult Confirm(CharacterOverviewState overview,
            CharacterCreationGearConfirmation confirmation) => inner.Confirm(overview, confirmation);
        public CharacterCreationGearInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState overview,
            string idempotencyKey) => inner.LookupReceipt(overview, idempotencyKey);
    }

    private sealed class QualitiesReadProbe(ICharacterCreationQualitiesService inner,
        ControlledLinkedOwner owners) : ICharacterCreationQualitiesService
    {
        public int UiThreadId { get; set; }
        public int LoadCalls { get; private set; }
        public Action? BeforeLoad { get; set; }
        public CharacterCreationFoundationResult<CharacterCreationQualitiesState> Load(CharacterCreationQualitiesLoadRequest request)
        {
            Require(Environment.CurrentManagedThreadId != UiThreadId && owners.ActiveLeases == 1,
                "Qualities Core read must run off UI with the original owner lease.");
            LoadCalls++;
            BeforeLoad?.Invoke();
            return inner.Load(request);
        }
        public CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> Preview(CharacterCreationQualitiesPreviewRequest request)
            => inner.Preview(request);
        public CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> Confirm(CharacterCreationQualitiesConfirmRequest request)
            => inner.Confirm(request);
    }

    public static async Task RunStartingCashPhonePagesAsync(string contentRoot)
    {
        foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
        {
            using var ui = new IssuedPageUiContext();
            await ui.RunAsync(async () =>
            {
                var owners = new ControlledLinkedOwner();
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationFinalization: true);
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                var before = PrepareActualFinalizationReadyContext(runtime, buildMethod: method);
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var loaded = runtime.Coordinator.LoadCreationFinalization().Value!;
                Require(runtime.Coordinator.IsCreationFinalizationStateCurrent(loaded), "Starting cash needs an issued current state.");
                var source = loaded.StartingCashSource!;
                var page = new CreationStartingCashPage(runtime.Coordinator, loaded);
                var navigation = new NavigationPage(new ContentPage());
                await navigation.PushAsync(page, false);
                var window = new Window(navigation);
                using var alerts = new IssuedPageAlerts(page, window);
                await alerts.PreflightAsync();
                await Appear();
                var input = Element<Entry>("creation-starting-cash-roll");
                Require(string.IsNullOrEmpty(input.Text) && !Element<Button>("creation-starting-cash-preview").IsEnabled,
                    "Starting cash must not implicitly choose or roll a value.");
                input.Text = "not a roll";
                Require(!Element<Button>("creation-starting-cash-preview").IsEnabled, "Invalid text was admitted.");
                input.Text = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Click("creation-starting-cash-preview");
                Require(ReferenceEquals(Current(), page), "An out-of-range total reached confirm.");
                input = Element<Entry>("creation-starting-cash-roll");
                input.Text = source.Dice.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Click("creation-starting-cash-preview");
                Require(Current() is CreationFinalizationPage, "Explicit legal roll did not reach the actual review.");
                var review = (CharacterCreationFinalizationReview)typeof(CreationFinalizationPage)
                    .GetField("_review", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Current())!;
                Require(review.Plan!.StartingCash == new CharacterCreationStartingCashChoice(source.AuthorityDigest, source.Dice),
                    "The native review did not retain the displayed source and explicit total.");
                input.Text = "999";
                Require(review.Plan.StartingCash?.DiceTotal == source.Dice, "A departed Entry changed the sealed review.");
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                await Click("creation-finalization-confirm");
                Require(Current() is CreationFinalizationReceiptPage, "The actual confirm did not show its receipt.");
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                var receipt = cold.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt;
                Require(cold.ContentRevision == before.ContentRevision + 1 && cold.SavedRevision == cold.ContentRevision
                    && receipt.StartingCash == review.Plan.StartingCash
                    && receipt.StartingCashAuthorityDigest == review.Plan.StartingCashAuthorityDigest,
                    "The native confirm lost its cash choice or saved more than one revision.");
                owners.Set(ContactsOwnerB);
                owners.Set(OwnerScope.LocalSingleUser);
                Require(!runtime.Coordinator.IsCreationFinalizationStateCurrent(loaded), "Owner ABA revived the old cash page.");
                ui.AssertHealthy();
                Console.WriteLine("PASS " + method + " starting-cash page: explicit roll, Core rejection, preview, stale Entry, confirm and cold receipt");

                NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
                T Element<T>(string id) where T : Element => IssuedElements(Current()).OfType<T>().Single(e => e.AutomationId == id);
                async Task Appear()
                {
                    if (IssuedPageField<int>(Current(), "_subscribed") == 0)
                        await ui.BeginAsyncVoid(() => IssuedPageLifecycle(Current(), "OnAppearing"));
                }
                async Task Click(string id)
                {
                    var previous = Current();
                    var button = Element<Button>(id);
                    Require(button.IsEnabled, "Button disabled: " + id);
                    await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                    if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0)
                        IssuedPageLifecycle(previous, "OnDisappearing");
                    await Appear();
                }
            });
        }
    }

    private static CharacterCreationStartingCashChoice FinalizationFixtureCash(CharacterCreationFinalizationState state)
        => state.StartingCashSource is { } source ? new(source.AuthorityDigest, source.Dice)
            : throw new InvalidOperationException("The actual finalization fixture has no Core-owned starting cash source.");

    public static async Task RunCreationFinalizationOwnerCasesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        // A missing/invalid ready fixture must stop before any owner diagnosis.
        await RunCreationFinalizationLocalBaselineAsync(contentRoot);
        var failures = new List<string>();
        foreach (string scenario in new[] { "linked-only", "linked-shadow", "queued-owner-b", "queued-owner-aba", "queued-owner-b-view",
                     "stale-review-b", "stale-review-aba", "stale-view-reload" })
        {
            try { await RunCreationFinalizationLinkedAsync(contentRoot, scenario); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { failures.Add(scenario + ": " + error.Message); }
        }
        foreach (string fault in new[] { "cancellation", "owner-switch", "subscriber" })
        {
            try { await RunFinalizationPostCommitFaultAsync(contentRoot, fault); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { failures.Add(fault + ": " + error.Message); }
        }
        Require(failures.Count == 0, "Native finalization original-owner regressions:\n" + string.Join("\n", failures));
    }

    public static Task RunSumToTenFinalizationAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        return RunCreationFinalizationLocalBaselineAsync(contentRoot, CharacterCreationBuildMethods.SumToTen);
    }

    private static async Task RunCreationFinalizationLocalBaselineAsync(string contentRoot,
        string method = CharacterCreationBuildMethods.Priority)
    {
        var owners = new ControlledLinkedOwner();
        var uiContext = new SynchronizationContext();
        FinalizationThreadProbe? threadProbe = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationFinalization: true,
            finalizationDecorator: actual => threadProbe = new FinalizationThreadProbe(actual, uiContext));
        Require(owners.Current == OwnerScope.LocalSingleUser, "Local positive control did not retain the trusted local scope.");
        var before = PrepareActualFinalizationReadyContext(runtime, buildMethod: method);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var qualities = runtime.Services.GetRequiredService<ICharacterCreationQualitiesService>()
            .Load(new(runtime.Id));
        Require(qualities.Value is { } exactQualities
            && CreationQualitiesPhoneAuthority.IsReady(exactQualities, runtime.Coordinator.State),
            "The real method-bound Qualities state must be admitted on the phone: "
            + JsonSerializer.Serialize(new { qualities.Outcome, qualities.Blockers,
                method = qualities.Value?.Binding.BuildMethod, qualities.Value?.CanEdit }));
        var loaded = runtime.Coordinator.LoadCreationFinalization();
        Require(loaded.Value is { CanReview: true }, "Local native finalization Load failed: " + JsonSerializer.Serialize(loaded));
        var review = await StartFromUiContext(uiContext,
            () => runtime.Coordinator.ReviewCreationFinalizationAsync(loaded.Value!.Binding, FinalizationFixtureCash(loaded.Value)));
        Require(review.Value is { CanConfirm: true, Plan: not null },
            "Local native finalization Review failed: " + JsonSerializer.Serialize(review));
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        Require(FinalizationDocumentDigest(store.Get(runtime.Id).Value!) == FinalizationDocumentDigest(before),
            "Finalization Load/Review mutated the local ready fixture.");
        var applied = await StartFromUiContext(uiContext,
            () => runtime.Coordinator.ConfirmCreationFinalizationAsync(review.Value!, "native-finalization-local-baseline"));
        Require(threadProbe is { OffContextReviews: 1, OffContextConfirms: 1 },
            "Actual Core finalization did not leave the caller's UI synchronization context.");
        var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        Require(applied.Outcome == CharacterCreationFinalizationOutcomes.Applied && applied.Value is not null,
            "Local native finalization Confirm failed: " + JsonSerializer.Serialize(applied));
        Require(cold.ContentRevision == before.ContentRevision + 1 && cold.SavedRevision == cold.ContentRevision
            && cold.Document.AuxiliaryState.CharacterCreationFinalizationReceipts?.Single().Receipt.ReceiptDigest == applied.Value!.ReceiptDigest
            && runtime.Coordinator.State.Profile?.Created == true
            && runtime.Coordinator.State.DisplayOwnerContext == owners.Capture()
            && runtime.Coordinator.State.Session.OwnerContext == owners.Capture()
            && runtime.Coordinator.State.ContentRevision == cold.ContentRevision
            && runtime.Coordinator.State.SavedRevision == cold.SavedRevision,
            "Local baseline did not preserve the actual atomic finalization receipt/checkpoint and live owner.");
        Require(applied.Value!.BuildMethod == method
            && runtime.Coordinator.LoadPersistedPriorityTableCreationReceipt()?.ReceiptDigest == applied.Value.ReceiptDigest,
            "The native Career route must retain the exact method's persisted receipt.");
        await HydrateFinalizationOwnerAsync(runtime, owners, cold, expectedCreated: true);
        Require(runtime.Coordinator.LoadPersistedPriorityTableCreationReceipt()?.ReceiptDigest == applied.Value.ReceiptDigest,
            "A fresh native display from the cold store lost the finalization receipt.");
        Console.WriteLine("FINALIZATION_LOCAL_BASELINE " + JsonSerializer.Serialize(new
        {
            applied.Outcome, applied.Value.BuildMethod, before.ContentRevision, before.SavedRevision,
            afterContentRevision = cold.ContentRevision, afterSavedRevision = cold.SavedRevision,
            receiptDigest = applied.Value!.ReceiptDigest,
            beforeDigest = FinalizationDocumentDigest(before), afterDigest = FinalizationDocumentDigest(cold)
        }));
        Console.WriteLine("PASS actual local native/Core finalization baseline");
    }

    private static async Task RunCreationFinalizationLinkedAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationFinalization: true);
        var ready = PrepareActualFinalizationReadyContext(runtime);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, ready);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerB, ready);
        if (scenario == "linked-only")
            Require(store.Delete(runtime.Id, ready.ContentRevision).Success, "Could not remove only the temporary local fixture.");
        owners.Set(ContactsOwnerA);
        await HydrateFinalizationOwnerAsync(runtime, owners, ready);
        var originalOwner = owners.Capture();
        var originalDisplay = runtime.Coordinator.State;
        var before = ColdPartitions();
        var loaded = runtime.Coordinator.LoadCreationFinalization();
        var reviewed = loaded.Value is { CanReview: true }
            ? runtime.Coordinator.ReviewCreationFinalization(loaded.Value.Binding, FinalizationFixtureCash(loaded.Value)) : null;
        Require(ColdPartitions().All(pair => pair.Value == before[pair.Key]),
            "Native finalization Load/Review mutated an owner partition.");
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>? result = null;
        bool confirmAttempted = false;
        Exception? dispatchError = null;
        Exception? setupError = null;
        if (reviewed?.Value is { CanConfirm: true, Plan: not null } review)
        {
            if (scenario.StartsWith("stale-", StringComparison.Ordinal))
            {
                if (scenario != "stale-view-reload")
                {
                    owners.Set(ContactsOwnerB);
                    if (scenario == "stale-review-aba") owners.Set(ContactsOwnerA);
                }
                await HydrateFinalizationOwnerAsync(runtime, owners, ready);
                Require(!ReferenceEquals(originalDisplay.Profile, runtime.Coordinator.State.Profile),
                    "Stale-view fixture did not actually replace the original display.");
                var lateLoad = runtime.Coordinator.LoadCreationFinalization(originalDisplay);
                Require(lateLoad.Value is null, "Delayed background load rebound an original display to its replacement.");
                Require(runtime.Coordinator.ReviewCreationFinalization(loaded.Value!.Binding).Value is null,
                    "Old loaded binding authorized a new review after display replacement.");
            }
            var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            await gate.WaitAsync();
            Task<CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>>? pending = null;
            try
            {
                pending = runtime.Coordinator.ConfirmCreationFinalizationAsync(review, "native-finalization-" + scenario);
                confirmAttempted = true;
                Require(!pending.IsCompleted, "Finalization did not wait at the actual activation gate.");
                if (scenario.StartsWith("queued-", StringComparison.Ordinal))
                {
                    owners.Set(ContactsOwnerB);
                    if (scenario == "queued-owner-aba") owners.Set(ContactsOwnerA);
                    if (scenario == "queued-owner-b-view")
                        await HydrateFinalizationOwnerAsync(runtime, owners, ready);
                    else
                        Require(runtime.Coordinator.State.DisplayOwnerContext == originalOwner,
                            "Stale-display fixture unexpectedly rebound before gate release.");
                    Require(owners.Capture() != originalOwner, "Owner epoch never changed in queued regression.");
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { setupError = error; }
            finally { gate.Release(); }
            // Always join and cold-read even if refresh throws after a real commit.
            if (pending is not null)
                try { result = await pending.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception error) when (error is not OutOfMemoryException) { dispatchError = error; }
        }
        var after = ColdPartitions();
        var coldStore = new FileWorkspaceStore(runtime.StateDirectory);
        var afterRows = new Dictionary<string, WorkspaceStoreReadResult>
        {
            ["local"] = coldStore.Get(runtime.Id), ["account-a"] = coldStore.Get(ContactsOwnerA, runtime.Id),
            ["account-b"] = coldStore.Get(ContactsOwnerB, runtime.Id)
        };
        Console.WriteLine("FINALIZATION_OWNER_OBSERVATION " + JsonSerializer.Serialize(new
        {
            scenario, originalOwner, liveOwner = owners.Capture(), displayOwner = runtime.Coordinator.State.DisplayOwnerContext,
            sessionOwner = runtime.Coordinator.State.Session.OwnerContext,
            loadOutcome = loaded.Outcome, loadBlockers = loaded.Blockers,
            reviewOutcome = reviewed?.Outcome, reviewBlockers = reviewed?.Blockers,
            confirmAttempted, confirmOutcome = result?.Outcome,
            confirmBlockers = result?.Blockers, receipt = result?.Value,
            setupError = setupError?.Message, dispatchError = dispatchError?.GetType().Name,
            changed = after.Keys.Where(key => after[key] != before[key]).ToArray(), before, after,
            cold = afterRows.ToDictionary(pair => pair.Key, pair => new
            {
                pair.Value.Outcome, pair.Value.Value?.ContentRevision, pair.Value.Value?.SavedRevision,
                finalizationReceipts = pair.Value.Value?.Document.AuxiliaryState.CharacterCreationFinalizationReceipts
            })
        }));
        Require(setupError is null, "Queued fixture failed before release: " + setupError?.Message);
        Require(owners.ActiveLeases == 0, "Fixture retained an owner lease after dispatch.");
        if (scenario.StartsWith("queued-", StringComparison.Ordinal) || scenario.StartsWith("stale-", StringComparison.Ordinal))
        {
            Require(loaded.Value is { CanReview: true } && reviewed?.Value is { CanConfirm: true, Plan: not null }
                && confirmAttempted,
                "Queued regression never dispatched an actual confirmable native/Core finalization review.");
            Require(after.All(pair => pair.Value == before[pair.Key]) && result?.Value is null,
                "Retired original-owner finalization mutated a partition or returned an applied receipt.");
        }
        else
        {
            Require(loaded.Value is { CanReview: true } && reviewed?.Value is { CanConfirm: true },
                "A canonically ready linked runner is unavailable while the exact same fixture passes local finalization.");
            Require(after["local"] == before["local"] && after["account-b"] == before["account-b"]
                && after["account-a"] != before["account-a"] && result?.Value is not null
                && afterRows["account-a"].Value?.ContentRevision == ready.ContentRevision + 1
                && afterRows["account-a"].Value?.SavedRevision == ready.ContentRevision + 1
                && afterRows["account-a"].Value?.Document.AuxiliaryState.CharacterCreationFinalizationReceipts?
                    .Single().Receipt.ReceiptDigest == result?.Value?.ReceiptDigest,
                "Finalization did not atomically affect only the admitted linked owner's exact ready runner.");
            Require(dispatchError is null, "Linked finalization lost its observed result after commit.");
        }
        Console.WriteLine("PASS actual finalization original-owner boundary: " + scenario);

        Dictionary<string, string> ColdPartitions()
        {
            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            return new()
            {
                ["local"] = Describe(cold.Get(runtime.Id)), ["account-a"] = Describe(cold.Get(ContactsOwnerA, runtime.Id)),
                ["account-b"] = Describe(cold.Get(ContactsOwnerB, runtime.Id))
            };
        }
        static string Describe(WorkspaceStoreReadResult read) => read.Value is { } row
            ? row.ContentRevision + "/" + row.SavedRevision + ":" + FinalizationDocumentDigest(row) : read.Outcome.ToString();
    }

    private static async Task RunFinalizationPostCommitFaultAsync(string contentRoot, string fault)
    {
        using var cancellation = new CancellationTokenSource();
        var owners = new ControlledLinkedOwner();
        RunnerSessionCoordinator? coordinator = null;
        int observedCommits = 0;
        EventHandler throwing = (_, _) => throw new IOException("Injected postcommit notification failure.");
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationFinalization: true, finalizationDecorator: actual => new FinalizationPostCommitProbe(actual, () =>
            {
                observedCommits++;
                if (fault == "cancellation") cancellation.Cancel();
                else if (fault == "owner-switch") owners.Set(ContactsOwnerB);
                else coordinator!.Changed += throwing;
            }));
        coordinator = runtime.Coordinator;
        var ready = PrepareActualFinalizationReadyContext(runtime);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, ready);
        CloneFinalizationRecordFixture(runtime, ContactsOwnerB, ready);
        owners.Set(ContactsOwnerA);
        await HydrateFinalizationOwnerAsync(runtime, owners, ready);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        string localBefore = FinalizationDocumentDigest(store.Get(runtime.Id).Value!);
        string bBefore = FinalizationDocumentDigest(store.Get(ContactsOwnerB, runtime.Id).Value!);
        var loaded = coordinator.LoadCreationFinalization();
        Require(loaded.Value is { CanReview: true }, "Postcommit fixture did not load a real ready draft.");
        var reviewed = coordinator.ReviewCreationFinalization(loaded.Value!.Binding, FinalizationFixtureCash(loaded.Value));
        Require(reviewed.Value is { CanConfirm: true, Plan: not null }, "Postcommit fixture has no actual reviewed plan.");
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> result;
        try
        {
            result = await coordinator.ConfirmCreationFinalizationAsync(
                reviewed.Value!, "postcommit-finalization-" + fault, cancellation.Token);
        }
        finally { coordinator.Changed -= throwing; }
        var cold = new FileWorkspaceStore(runtime.StateDirectory);
        var committed = cold.Get(ContactsOwnerA, runtime.Id).Value!;
        Require(observedCommits == 1 && result.Value is not null
            && result.Outcome == CharacterCreationFinalizationOutcomes.Applied
            && result.Blockers.Contains(CharacterCreationFinalizationBlockers.PostCommitReopenRequired)
            && committed.ContentRevision == ready.ContentRevision + 1
            && committed.SavedRevision == committed.ContentRevision
            && committed.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt == result.Value,
            "A postcommit fault lost the exact real receipt or replayed the mutation.");
        Require(FinalizationDocumentDigest(cold.Get(runtime.Id).Value!) == localBefore
            && FinalizationDocumentDigest(cold.Get(ContactsOwnerB, runtime.Id).Value!) == bBefore,
            "Postcommit recovery changed another owner's runner.");
        if (fault == "owner-switch")
            Require(!coordinator.CanDisplayCreationFinalizationReceipt(result.Value!), "Receipt leaked into the new account's display.");
        Require(owners.ActiveLeases == 0, "Postcommit fault retained an owner lease.");
        Console.WriteLine("PASS actual native finalization postcommit fault: " + fault);
    }

    private static Task<T> StartFromUiContext<T>(SynchronizationContext uiContext, Func<Task<T>> start)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(uiContext);
            return start();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    // Delegates to real Core; observes the synchronization boundary, not a fake
    // rule result. ControlledLinkedOwner independently checks lease thread affinity.
    private sealed class FinalizationThreadProbe(
        IOwnerBoundCharacterCreationFinalizationService actual, SynchronizationContext uiContext)
        : IOwnerBoundCharacterCreationFinalizationService
    {
        public int OffContextReviews { get; private set; }
        public int OffContextConfirms { get; private set; }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
            OwnerContextStamp owner, CharacterCreationFinalizationLoadRequest request) => actual.Load(owner, request);
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
            OwnerContextStamp owner, CharacterCreationFinalizationReviewRequest request)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, uiContext),
                "Core Review ran synchronously on the caller's UI context.");
            OffContextReviews++;
            return actual.Review(owner, request);
        }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
            OwnerContextStamp owner, CharacterCreationFinalizationConfirmRequest request)
        {
            Require(!ReferenceEquals(SynchronizationContext.Current, uiContext),
                "Core Confirm ran synchronously on the caller's UI context.");
            OffContextConfirms++;
            return actual.Confirm(owner, request);
        }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
            OwnerContextStamp owner, CharacterCreationFinalizationReceiptLookupRequest request) => actual.LookupReceipt(owner, request);
    }

    private sealed class FinalizationPostCommitProbe(
        IOwnerBoundCharacterCreationFinalizationService actual, Action afterActualCommit)
        : IOwnerBoundCharacterCreationFinalizationService
    {
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
            OwnerContextStamp owner, CharacterCreationFinalizationLoadRequest request) => actual.Load(owner, request);
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
            OwnerContextStamp owner, CharacterCreationFinalizationReviewRequest request) => actual.Review(owner, request);
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
            OwnerContextStamp owner, CharacterCreationFinalizationConfirmRequest request)
        {
            var result = actual.Confirm(owner, request);
            Require(result.Outcome == CharacterCreationFinalizationOutcomes.Applied && result.Value is not null,
                "Fault injection requires an actual durable Core commit.");
            afterActualCommit();
            return result;
        }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
            OwnerContextStamp owner, CharacterCreationFinalizationReceiptLookupRequest request)
            => actual.LookupReceipt(owner, request);
    }

    private static void CloneFinalizationRecordFixture(NativeRewardRuntime runtime, OwnerScope destination,
        WorkspaceStoredDocument canonical)
    {
        // TEST STORAGE CONSTRUCTION ONLY, NOT authenticated cross-owner restore.
        // Core creates the private destination/permissions; copy the already-real
        // record byte-for-byte, without minting drafts, receipts, revisions or plans.
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        string source = Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json");
        var prior = Directory.GetFiles(runtime.StateDirectory, runtime.Id.Value + ".json", SearchOption.AllDirectories)
            .ToHashSet(StringComparer.Ordinal);
        var placeholder = canonical.Document with { State = canonical.Document.State with
            { AuxiliaryState = new WorkspaceDocumentAuxiliaryState() } };
        Require(store.CreateWorkspaceDocument(destination, runtime.Id, placeholder).Success,
            "Core could not create the private fixture destination.");
        string target = Directory.GetFiles(runtime.StateDirectory, runtime.Id.Value + ".json", SearchOption.AllDirectories)
            .Single(path => !prior.Contains(path));
        Require(File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint) == false
            && File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint) == false,
            "Fixture paths must be real private files.");
        byte[] original = File.ReadAllBytes(source);
        File.WriteAllBytes(target, original);
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        Require(File.ReadAllBytes(target).SequenceEqual(original), "Fixture record copy changed canonical bytes.");
        var copied = new FileWorkspaceStore(runtime.StateDirectory).Get(destination, runtime.Id);
        Require(copied.Success && FinalizationDocumentDigest(copied.Value!) == FinalizationDocumentDigest(canonical)
            && copied.Value!.LastUpdatedUtc == canonical.LastUpdatedUtc,
            "Cold owner-scoped read rejected or changed the complete real ready record.");
    }

    private static async Task HydrateFinalizationOwnerAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, WorkspaceStoredDocument expected, bool expectedCreated = false)
    {
        // Same real lifecycle order as native owner initialization. A same-ID
        // Switch shortcut can legitimately skip Load after Initialize.
        await runtime.Shell.InitializeAsync(default);
        await runtime.Presenter.InitializeAsync(default);
        await runtime.Presenter.LoadAsync(expected.Id, default);
        var state = runtime.Coordinator.State;
        Require(state.WorkspaceId == expected.Id && state.Session.ActiveWorkspaceId == expected.Id
            && state.Profile?.Created == expectedCreated && state.Error is null && !state.IsBusy
            && state.DisplayOwnerContext == owners.Capture() && state.Session.OwnerContext == owners.Capture()
            && runtime.Shell.State.OwnerContext == owners.Capture()
            && state.ContentRevision == expected.ContentRevision && state.SavedRevision == expected.SavedRevision,
            "Finalization fixture never established exact actual native owner/session/revisions: " + JsonSerializer.Serialize(new
            { state.WorkspaceId, state.ContentRevision, state.SavedRevision, state.DisplayOwnerContext, state.Error }));
    }

    private static string FinalizationDocumentDigest(WorkspaceStoredDocument stored) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { stored.Id, stored.Document, stored.ContentRevision, stored.SavedRevision }))).ToLowerInvariant();

    private static WorkspaceStoredDocument PrepareActualFinalizationReadyContext(NativeRewardRuntime runtime,
        bool stopBeforeQualities = false, string buildMethod = CharacterCreationBuildMethods.Priority)
    {
        // Test fixture adapted from Core f750 CharacterCreationFinalizationServiceTests.ReadyContext:
        // canonical Priority or repeated-rank Sum-to-Ten/Human/Mundane;
        // actual services issue every draft and receipt.
        // No manual auxiliary state, fake admission, finalization plan or receipt.
        var services = runtime.Services;
        var store = services.GetRequiredService<IWorkspaceStore>();
        var bootstrap = services.GetRequiredService<ICharacterCreationBootstrapService>();
        Require(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            buildMethod, out string profile), "Canonical build-method profile missing.");
        var created = bootstrap.Create(new(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, "sr5", "Finalization Runner", "Finalizer",
            buildMethod, profile));
        Require(created.Outcome == CharacterCreationBootstrapOutcomes.Success && created.Value is not null,
            "Actual Bootstrap failed: " + JsonSerializer.Serialize(created));
        runtime.Id = created.Value!.WorkspaceId;
        var prerequisites = services.GetRequiredService<ICharacterCreationPrerequisiteService>();
        var initial = prerequisites.Load(new(runtime.Id)).Value!;
        var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CharacterCreationPriorityCategoryIds.Heritage] = "A",
            [CharacterCreationPriorityCategoryIds.Talent] = "E",
            [CharacterCreationPriorityCategoryIds.Attributes] = "B",
            [CharacterCreationPriorityCategoryIds.Skills] = "C",
            [CharacterCreationPriorityCategoryIds.Resources] = "D"
        };
        if (buildMethod == CharacterCreationBuildMethods.SumToTen)
        {
            ranks[CharacterCreationPriorityCategoryIds.Heritage] = "E";
            ranks[CharacterCreationPriorityCategoryIds.Attributes] = "A";
            ranks[CharacterCreationPriorityCategoryIds.Skills] = "A";
            ranks[CharacterCreationPriorityCategoryIds.Resources] = "C";
        }
        var human = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && item.Rank == ranks[CharacterCreationPriorityCategoryIds.Heritage]).HeritageOptions.First(item => item.IsEnabled && item.MetavariantSourceId is null
                && item.MetatypeName == "Human");
        var mundane = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && item.Rank == "E").TalentOptions.First(item => item.IsEnabled
                && item.Value.Equals(CharacterCreationMagicResonanceKinds.Mundane, StringComparison.OrdinalIgnoreCase)
                && item.Magic is null && item.Resonance is null && item.Depth is null
                && item.ActiveSkillGrant is null && item.SkillGroupGrant is null);
        var prerequisitePreview = prerequisites.Preview(new(initial.Binding, ranks)
            { HeritageSelectionId = human.SelectionId, TalentSelectionId = mundane.SelectionId }).Value!;
        var prerequisiteReceipt = prerequisites.Confirm(new(prerequisitePreview.Binding, ranks,
            prerequisitePreview.PreviewDigest, ExplicitlyConfirmed: true)
            { HeritageSelectionId = human.SelectionId, TalentSelectionId = mundane.SelectionId });
        Require(prerequisiteReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual prerequisite failed: " + JsonSerializer.Serialize(prerequisiteReceipt));
        var attributes = services.GetRequiredService<ICharacterCreationAttributesService>();
        var attributePreview = attributes.Preview(new(attributes.Load(new(runtime.Id)).Value!.Binding, [])).Value!;
        var attributeReceipt = attributes.Confirm(new(attributePreview.Binding, [], attributePreview.PreviewDigest, true));
        Require(attributeReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual Attributes failed: " + JsonSerializer.Serialize(attributeReceipt));
        var skills = services.GetRequiredService<ICharacterCreationSkillsService>();
        var skillState = skills.Load(new(runtime.Id)).Value!;
        var native = skillState.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
        CharacterCreationSkillAllocation[] allocations =
            [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
        var skillPreview = skills.Preview(new(skillState.Binding, allocations, [])).Value!;
        var skillReceipt = skills.Confirm(new(skillPreview.Binding, allocations, [], skillPreview.PreviewDigest,
            "native-finalization-skills", true));
        Require(skillReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual Skills failed: " + JsonSerializer.Serialize(skillReceipt));
        if (stopBeforeQualities)
            return new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var qualities = services.GetRequiredService<ICharacterCreationQualitiesService>();
        var qualityPreview = qualities.Preview(new(qualities.Load(new(runtime.Id)).Value!.Binding, [])).Value!;
        var qualityReceipt = qualities.Confirm(new(qualityPreview.Binding, [], qualityPreview.PreviewDigest,
            "native-finalization-qualities", Guid.NewGuid(), true));
        Require(qualityReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual Qualities failed: " + JsonSerializer.Serialize(qualityReceipt));
        var resources = services.GetRequiredService<ICharacterCreationResourcesService>();
        var resourcesState = resources.Load(new(runtime.Id)).Value!;
        var zero = resourcesState.Options.First(item => item.IsEnabled && item.KarmaInvestment == 0);
        var resourcePreview = resources.Preview(new(resourcesState.Binding, zero.OptionId)).Value!;
        var resourceReceipt = resources.Confirm(new(resourcePreview.Binding, zero.OptionId, resourcePreview.PreviewDigest,
            "native-finalization-resources", true));
        Require(resourceReceipt.Outcome == CharacterCreationResourcesOutcomes.Applied,
            "Actual Resources failed: " + JsonSerializer.Serialize(resourceReceipt));
        var gear = services.GetRequiredService<ICharacterCreationGearService>();
        var gearPreview = gear.Preview(new(gear.Load(new(runtime.Id)).Value!.Binding, [])).Value!;
        var gearReceipt = gear.Confirm(new(gearPreview.Binding, [], gearPreview.PreviewDigest, "native-finalization-gear", true));
        Require(gearReceipt.Outcome == CharacterCreationGearOutcomes.Applied,
            "Actual Gear failed: " + JsonSerializer.Serialize(gearReceipt));
        var finalizer = services.GetRequiredService<ICharacterCreationFinalizationService>();
        var loaded = finalizer.Load(new(runtime.Id));
        Require(loaded.Value is { CanReview: true }, "Core ReadyContext is not ready: " + JsonSerializer.Serialize(loaded));
        var preview = finalizer.Review(new(loaded.Value!.Binding) { StartingCash = FinalizationFixtureCash(loaded.Value) });
        Require(preview.Value is { CanConfirm: true, Plan: not null },
            "Core ReadyContext is not confirmable: " + JsonSerializer.Serialize(preview));
        var read = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id);
        Require(read.Success && read.Value is not null, "Canonical ready fixture cold read failed: " + read.Error);
        var stored = read.Value!;
        Require(stored.ContentRevision > 0 && stored.SavedRevision == stored.ContentRevision
            && stored.Document.AuxiliaryState.CharacterCreationFinalizationReceipts is null,
            "ReadyContext is not an actual unfinalized canonical checkpoint.");
        return stored;
    }
}
