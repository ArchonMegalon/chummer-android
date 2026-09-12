using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

internal static partial class AfterRunAuthorityHarness
{
    // Actual managed native lifecycle/button dispatch, owner-scoped File/Core
    // mutation/save, and durable journal APIs. Not Android input, authentication,
    // process-kill durability, or operation-ID attribution evidence.
    public static async Task RunDamageReviewOwnerDispatchCasesAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            try { await PreflightDamageReviewNavigationAsync(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.WriteLine("FAIL SETUP damage review navigation observer preflight: " + error);
                throw;
            }
            string[] cases = ["same", "owner-b", "owner-aba", "queued-b", "queued-aba",
                "departure", "old-render", "duplicate"];
            var failures = new List<string>();
            int executed = 0, passed = 0;
            foreach (string scenario in cases)
            {
                executed++;
                try
                {
                    await RunDamageReviewOwnerDispatchCaseAsync(contentRoot, ui, scenario);
                    passed++;
                    Console.WriteLine("PASS actual damage review dispatch: " + scenario);
                }
                catch (DamageReviewDispatchProductException error)
                {
                    failures.Add(scenario + ": " + error.Message);
                    Console.WriteLine($"FAIL PRODUCT actual damage review dispatch: {scenario}: {error}");
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Console.WriteLine($"FAIL SETUP/unjoined actual damage review dispatch: {scenario}: {error}");
                    Console.WriteLine($"DAMAGE_REVIEW_DISPATCH total=8 executed={executed} passed={passed} productFailed={failures.Count} setupFailed=1 notRun={cases.Length - executed}");
                    throw; // Never run another case over unavailable setup or a live callback.
                }
            }
            Console.WriteLine($"DAMAGE_REVIEW_DISPATCH total=8 executed={executed} passed={passed} productFailed={failures.Count} setupFailed=0 notRun=0");
            Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        });
    }

    private static async Task RunDamageReviewOwnerDispatchCaseAsync(
        string contentRoot, IssuedPageUiContext ui, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
        await runtime.Coordinator.InitializeAsync();
        await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
        await SeedJournalAccountRuntimeAsync(runtime, owners);
        var activationReceipt = await runtime.Coordinator.SwitchWorkspaceAsync(
            runtime.Coordinator.State.OpenWorkspaces.Single(item => item.Id == runtime.Id));
        Require(activationReceipt is { Kind: NativeWorkspaceActivationKind.WorkspaceSwitch }
                && runtime.Coordinator.IsWorkspaceActivationCurrent(activationReceipt, NativeWorkspaceActivationKind.WorkspaceSwitch),
            "SETUP: actual initial workspace activation did not join.");
        await HydrateJournalAccountRuntimeAsync(runtime, owners, 1);
        var issuedOwner = owners.Capture();
        var issuedFrame = runtime.Coordinator.State;
        var snapshot = Sr5PlaytimeDamageWizardPage.ProjectForOriginalOwner(
            runtime.Coordinator, issuedOwner, runtime.Id, JournalAccountTrack);
        Require(snapshot is { Filled: 2, WorkspaceRevision: 1, SavedRevision: 1 }
                && snapshot.AccountOwner == ContactsOwnerA && snapshot.IsExact(),
            "SETUP: real original-owner damage projection is unavailable.");
        Require(Sr5PlaytimeDamageIntegrity.TryQuote(snapshot!, 3, Guid.NewGuid(), out var quote),
            "SETUP: actual projection could not be quoted.");
        var store = JournalAccountStore(ContactsOwnerA, runtime.Id);
        Require(store.TryWriteReview(quote, Guid.NewGuid(), out var review, out var blocker),
            "SETUP: actual durable review write failed: " + blocker);
        var aBackend = JournalAccountBackend(ContactsOwnerA, runtime.Id);
        var bBackend = JournalAccountBackend(ContactsOwnerB, runtime.Id);
        var reservationBackend = new PreferencesSr5CareerMutationOwnerBackend();
        string reviewedBytes = aBackend.Read(), initialReservation = reservationBackend.Read();
        Require(reviewedBytes.Length > 0 && bBackend.Read().Length == 0 && initialReservation.Length == 0,
            "SETUP: original reviewed journal or empty foreign/reservation namespace differs.");
        var before = ReadJournalAccountRows(runtime);
        string originalXml = new FileWorkspaceStore(runtime.StateDirectory)
            .Get(ContactsOwnerA, runtime.Id).Value!.Document.Content;

        var page = new Sr5PlaytimeDamageReviewPage(runtime.Coordinator, store, review);
        var rootPage = new ContentPage { Title = "Original damage route" };
        var navigation = new NavigationPage(rootPage);
        await navigation.PushAsync(page, animated: false);
        var window = new Window(navigation);
        using var navigationObserver = new DamageReviewNavigationObserver(page);
        int poppedEvents = 0;
        navigation.Popped += (_, _) => poppedEvents++; // Diagnostic only; absent without a native handler.
        using var alerts = new IssuedPageAlerts(page, window);
        await alerts.PreflightAsync();
        var activation = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        var actionGate = (NativePageActionGate)typeof(NativePageBase)
            .GetField("_actionGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Task? pending = null;
        bool gateHeld = false, unjoined = false;
        Exception? actionFailure = null;
        Page expectedTop = page;
        string? applyingBytes = null, applyingReservation = null;
        int clicks = 0;
        var deliveries = new List<string>();
        try
        {
            Require(IssuedPageField<long>(page, "_appearanceGeneration") == 0,
                "SETUP: navigation unexpectedly started an unjoined appearance.");
            pending = ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            await JoinPendingAsync();
            long appearance = IssuedPageField<long>(page, "_appearanceGeneration");
            var readiness = new
            {
                appeared = appearance > 0,
                subscribed = IssuedPageField<int>(page, "_subscribed") == 1,
                refreshJoined = IssuedPageField<int>(page, "_appearanceRefreshActive") == 0,
                lifetimeReleased = IssuedPageField<object?>(page, "_appearanceLifetime") is null,
                originalDisplayRetained = ReferenceEquals(runtime.Coordinator.State, issuedFrame),
                noAlerts = alerts.Titles.Count == 0
            };
            Console.WriteLine("DAMAGE_REVIEW_APPEARANCE " + JsonSerializer.Serialize(new { scenario, readiness }));
            Require(readiness.appeared && readiness.subscribed && readiness.refreshJoined
                    && readiness.lifetimeReleased && readiness.originalDisplayRetained && readiness.noAlerts,
                "SETUP: actual review appearance did not join its original display: " + JsonSerializer.Serialize(readiness));
            Button originalButton = IssuedElements(page).OfType<Button>()
                .Single(button => button.AutomationId == "sr5-playtime-damage-physical-confirm");
            Require(originalButton.IsEnabled, "SETUP: genuine original review confirmation is not enabled.");

            if (scenario is "owner-b" or "owner-aba") await ChangeOwnerAsync(scenario == "owner-aba");
            if (scenario == "old-render")
            {
                typeof(Sr5PlaytimeDamageReviewPage).GetMethod("Refresh",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    binder: null, types: Type.EmptyTypes, modifiers: null)!.Invoke(page, null);
                Require(ReferenceEquals(runtime.Coordinator.State, issuedFrame)
                        && !IssuedElements(page).Contains(originalButton)
                        && IssuedElements(page).OfType<Button>().Single(button =>
                            button.AutomationId == "sr5-playtime-damage-physical-confirm").IsEnabled,
                    "SETUP: actual Refresh did not replace only the original confirmation render.");
            }
            if (scenario is "queued-b" or "queued-aba" or "departure" or "duplicate")
            {
                await activation.WaitAsync();
                gateHeld = true;
            }
            pending = ui.BeginAsyncVoid(() => Deliver(originalButton));
            if (gateHeld)
            {
                Require(!pending.IsCompleted && actionGate.IsClaimed,
                    "SETUP: actual Confirm callback did not queue behind the native activation barrier.");
                Require(store.TryRead(out var applying, out blocker)
                        && applying is { Phase: Sr5PlaytimeDamageTransactionPhase.Applying }
                        && applying.Quote == quote && applying.IsExact(),
                    "SETUP: durable Applying was not written before queued mutation admission: " + blocker);
                applyingBytes = aBackend.Read();
                applyingReservation = reservationBackend.Read();
                Require(applyingReservation.Length > 0, "SETUP: queued Applying has no durable shared reservation.");
                DamageReviewDispatchRequire(owners.ActiveLeases == 0,
                    "The queued review retained an account lease across await.");
                if (scenario is "queued-b" or "queued-aba") await ChangeOwnerAsync(scenario == "queued-aba");
                if (scenario == "departure")
                {
                    IssuedPageLifecycle(page, "OnDisappearing");
                    Require(IssuedPageField<int>(page, "_subscribed") == 0
                            && IssuedPageField<long>(page, "_appearanceGeneration") > appearance,
                        "SETUP: actual departure did not retire this appearance.");
                    expectedTop = new ContentPage { Title = "Newer route" };
                    await navigation.PushAsync(expectedTop, animated: false);
                }
                if (scenario == "duplicate")
                {
                    // The UI pump tracks both real async-void callbacks. Do not
                    // manufacture a second tracking Task over the pending first.
                    Deliver(originalButton);
                    DamageReviewDispatchRequire(actionGate.IsClaimed && aBackend.Read() == applyingBytes
                            && reservationBackend.Read() == applyingReservation && navigationObserver.PopCalls == 0,
                        "A duplicate Confirm changed the pending journal/reservation or navigation.");
                }
                activation.Release();
                gateHeld = false;
            }
            await JoinPendingAsync();
            ui.AssertHealthy();
        }
        catch (IssuedPageUnjoinedException) { unjoined = true; throw; }
        catch (Exception error) when (error is not OutOfMemoryException) { actionFailure = error; }
        finally
        {
            if (gateHeld) activation.Release();
            if (pending is not null && !unjoined) await JoinPendingAsync();
            if (IssuedPageField<int>(page, "_subscribed") != 0) IssuedPageLifecycle(page, "OnDisappearing");
        }

        // No canonical cold read occurs over a live appearance or Clicked task.
        var after = ReadJournalAccountRows(runtime);
        bool found = JournalAccountStore(ContactsOwnerA, runtime.Id).TryRead(out var journal, out blocker);
        string[] changed = before.Keys.Where(key => before[key].Complete != after[key].Complete).ToArray();
        Console.WriteLine("DAMAGE_REVIEW_DISPATCH_OBSERVATION " + JsonSerializer.Serialize(new
        {
            scenario, issuedOwner, liveOwner = owners.Capture(), clicks, deliveries,
            before = Summary(before), after = Summary(after), changed,
            phase = journal?.Phase.ToString(), receiptExact = journal?.Receipt?.IsExact(),
            accountOwner = journal?.Quote.Original.AccountOwner.NormalizedValue,
            reservationRetained = reservationBackend.Read().Length > 0,
            foreignJournalEmpty = bBackend.Read().Length == 0,
            pops = navigationObserver.PopCalls, navigationObserver.PopInvocations, poppedEvents,
            navigationDepth = navigation.Navigation.NavigationStack.Count, alerts.Titles,
            actionClaimed = actionGate.IsClaimed, owners.ActiveLeases,
            actionFailure = actionFailure?.ToString()
        }));
        if (actionFailure is DamageReviewDispatchProductException product) throw product;
        if (actionFailure is not null) throw new InvalidOperationException("SETUP: actual review action failed after cold observation.", actionFailure);
        DamageReviewDispatchRequire(!actionGate.IsClaimed && owners.ActiveLeases == 0,
            "A joined review left an action or account lease held.");
        DamageReviewDispatchRequire(found && journal is not null && journal.IsExact()
                && journal.Quote == quote && bBackend.Read().Length == 0,
            "Dispatch lost its original A review binding or wrote a foreign journal.");
        bool positive = scenario is "same" or "duplicate";
        if (positive)
        {
            var expected = XDocument.Parse(originalXml);
            expected.Root!.Element("physicalcmfilled")!.Value = "3";
            string actual = new FileWorkspaceStore(runtime.StateDirectory)
                .Get(ContactsOwnerA, runtime.Id).Value!.Document.Content;
            DamageReviewDispatchRequire(changed.SequenceEqual(new[] { "a" })
                    && after["a"] is { Content: 2, Saved: 2, Filled: "3" }
                    && XDocument.Parse(actual).ToString(SaveOptions.DisableFormatting) == expected.ToString(SaveOptions.DisableFormatting)
                    && journal is { Phase: Sr5PlaytimeDamageTransactionPhase.Applied, Receipt: { } receipt }
                    && receipt.IsExact() && receipt.AccountOwner == ContactsOwnerA
                    && receipt.WorkspaceId == runtime.Id && receipt.ActionId == quote.ActionId
                    && receipt.ExpectedWorkspaceRevision == 1 && receipt.AppliedWorkspaceRevision == 2
                    && receipt.FilledBefore == 2 && receipt.FilledAfter == 3
                    && navigationObserver.PopCalls == 1 && reservationBackend.Read().Length == 0
                    && alerts.Titles.SequenceEqual(new[] { "Damage saved" }),
                "Positive review did not produce exactly one A2/2 successor, applied receipt, success alert and pop.");
            expectedTop = rootPage;
        }
        else
        {
            DamageReviewDispatchRequire(changed.Length == 0 && navigationObserver.PopCalls == 0 && journal!.Receipt is null
                    && !alerts.Titles.Contains("Damage saved"),
                "An old review changed a canonical partition, claimed success or navigated.");
            if (applyingBytes is not null)
                DamageReviewDispatchRequire(journal.Phase == Sr5PlaytimeDamageTransactionPhase.Applying
                        && aBackend.Read() == applyingBytes && reservationBackend.Read() == applyingReservation,
                    "Rejected queued review did not retain exact Applying journal/reservation for recovery.");
            else
                DamageReviewDispatchRequire(journal.Phase == Sr5PlaytimeDamageTransactionPhase.Reviewed
                        && aBackend.Read() == reviewedBytes && reservationBackend.Read() == initialReservation,
                    "Rejected pre-admission review changed durable journal/reservation state.");
        }
        Page[] expectedStack = positive ? [rootPage]
            : scenario == "departure" ? [rootPage, page, expectedTop] : [rootPage, page];
        DamageReviewDispatchRequire(ReferenceEquals(navigation.CurrentPage, expectedTop)
                && navigation.Navigation.NavigationStack.SequenceEqual(expectedStack)
                && clicks == (scenario == "duplicate" ? 2 : 1),
            "An old review replaced a newer route or did not exercise the intended callback count.");
        Require(owners.TryAcquire(owners.Capture(), out var releasedLease),
            "SETUP: live owner admission unavailable after joined review.");
        releasedLease!.Dispose();
        DamageReviewDispatchRequire(owners.ActiveLeases == 0, "Post-review owner admission leaked its lease.");

        async Task JoinPendingAsync()
        {
            Task joining = pending!;
            try { await JoinIssuedPageAsync(joining); }
            finally { if (joining.IsCompleted) pending = null; }
        }
        async Task ChangeOwnerAsync(bool aba)
        {
            owners.Set(ContactsOwnerB);
            if (aba) owners.Set(ContactsOwnerA);
            await HydrateJournalAccountRuntimeAsync(runtime, owners, 1);
            bool staleAccepted = owners.TryAcquire(issuedOwner, out var staleLease);
            staleLease?.Dispose();
            Require(owners.Capture() != issuedOwner && !staleAccepted,
                "SETUP: real B/ABA transition did not retire the original owner epoch.");
        }
        void Deliver(Button button)
        {
            int started = ui.AsyncVoidStarts;
            string delivery = "ordinary SendClicked";
            ((IButtonController)button).SendClicked();
            if (!button.IsEnabled && ui.AsyncVoidStarts == started)
            {
                // Exercise this detached/disabled Button's registered handler,
                // without enabling controls or invoking private ConfirmAsync.
                Type element = typeof(Button).Assembly.GetType("Microsoft.Maui.Controls.Internals.IButtonElement", true)!;
                element.GetMethod("PropagateUpClicked")!.Invoke(button, null);
                delivery = "adversarial registered Clicked propagation after disabled rejection";
            }
            Require(ui.AsyncVoidStarts > started, "SETUP: actual Confirm button callback never started.");
            clicks++;
            deliveries.Add(delivery);
        }
        static object Summary(Dictionary<string, JournalAccountRow> rows)
            => rows.ToDictionary(pair => pair.Key, pair => new
            { pair.Value.Content, pair.Value.Saved, pair.Value.Filled, pair.Value.Digest, pair.Value.DocumentDigest });
    }

    private static async Task PreflightDamageReviewNavigationAsync()
    {
        // Presentation-only: no workspace, journal, owner, or product callback.
        foreach (string entry in new[] { "page-default", "page-bool", "observer-default" })
        {
            var root = new ContentPage();
            var probe = new ContentPage();
            var navigation = new NavigationPage(root);
            await navigation.PushAsync(probe, animated: false);
            var window = new Window(navigation);
            var proxy = (NavigationProxy)probe.Navigation;
            INavigation prior = proxy.Inner;
            using (var observer = new DamageReviewNavigationObserver(probe))
            {
                Require(observer.PopCalls == 0 && ReferenceEquals(probe.Window, window)
                        && navigation.Navigation.NavigationStack.SequenceEqual(new[] { root, probe }),
                    "SETUP: navigation observer preflight lacks the genuine two-page stack.");
                Task<Page> pending = entry switch
                {
                    "page-default" => probe.Navigation.PopAsync(),
                    "page-bool" => probe.Navigation.PopAsync(animated: false),
                    _ => ((INavigation)observer).PopAsync()
                };
                Require(observer.PopCalls == 1,
                    "SETUP: actual PopAsync admission was not recorded exactly once.");
                Page popped = await pending;
                Require(ReferenceEquals(popped, probe) && observer.PopCalls == 1
                        && ReferenceEquals(navigation.CurrentPage, root)
                        && navigation.Navigation.NavigationStack.SequenceEqual(new[] { root }),
                    "SETUP: recorded PopAsync did not forward to the actual navigation stack.");
            }
            Require(ReferenceEquals(proxy.Inner, prior),
                "SETUP: navigation observer did not restore its prior implementation.");
        }
        Console.WriteLine("DAMAGE_REVIEW_NAVIGATION_PREFLIGHT PASS actual delegated PopAsync; presentation-only, not a product case");
    }

    private sealed class DamageReviewNavigationObserver : INavigation, IDisposable
    {
        // MAUI 10.0.20 NavigationPage.SendHandlerUpdateAsync changes the stack
        // before its no-handler return, but raises Popped only after that return.
        // https://github.com/dotnet/maui/blob/10.0.20/src/Controls/src/Core/NavigationPage/NavigationPage.cs#L581-L645
        // Count the page's actual admission before forwarding; never infer calls
        // from stack shape and never replace the prior navigation's behavior.
        private readonly NavigationProxy _proxy;
        private readonly INavigation _prior;
        private readonly List<string> _popInvocations = [];
        public DamageReviewNavigationObserver(Page page)
        {
            _proxy = page.Navigation as NavigationProxy
                ?? throw new InvalidOperationException("SETUP: page navigation is not the expected MAUI proxy.");
            _prior = _proxy.Inner
                ?? throw new InvalidOperationException("SETUP: page has no actual prior navigation implementation.");
            try { _proxy.Inner = this; }
            catch { _proxy.Inner = _prior; throw; }
        }
        public void Dispose() => _proxy.Inner = _prior;
        public int PopCalls => _popInvocations.Count;
        public IReadOnlyList<string> PopInvocations => _popInvocations;
        public IReadOnlyList<Page> NavigationStack => _prior.NavigationStack;
        public IReadOnlyList<Page> ModalStack => _prior.ModalStack;
        public Task<Page> PopAsync()
        {
            _popInvocations.Add("PopAsync()");
            return _prior.PopAsync();
        }
        public Task<Page> PopAsync(bool animated)
        {
            _popInvocations.Add($"PopAsync({animated})");
            return _prior.PopAsync(animated);
        }
        public void InsertPageBefore(Page value, Page before) => _prior.InsertPageBefore(value, before);
        public Task<Page> PopModalAsync() => _prior.PopModalAsync();
        public Task<Page> PopModalAsync(bool animated) => _prior.PopModalAsync(animated);
        public Task PopToRootAsync() => _prior.PopToRootAsync();
        public Task PopToRootAsync(bool animated) => _prior.PopToRootAsync(animated);
        public Task PushAsync(Page value) => _prior.PushAsync(value);
        public Task PushAsync(Page value, bool animated) => _prior.PushAsync(value, animated);
        public Task PushModalAsync(Page value) => _prior.PushModalAsync(value);
        public Task PushModalAsync(Page value, bool animated) => _prior.PushModalAsync(value, animated);
        public void RemovePage(Page value) => _prior.RemovePage(value);
    }

    private sealed class DamageReviewDispatchProductException(string message) : Exception(message);
    private static void DamageReviewDispatchRequire([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new DamageReviewDispatchProductException(message);
    }
}
