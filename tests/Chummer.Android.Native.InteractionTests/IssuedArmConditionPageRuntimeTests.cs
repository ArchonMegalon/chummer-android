using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Dispatching;

// Headless managed regressions, not Android input/dispatcher or account-custody
// evidence. The real account fixture uses a test HTTP handler and temporary keys.
// All mutation, owner leases, projections, and cold reads use actual Core/UI code.
internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunIssuedArmConditionPageCasesAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var failures = new List<string>();
            int passed = 0;
            var cases = (from entry in new[] { "primary-arm", "condition-apply", "condition-clear" }
                         from scenario in new[] { "same", "owner-b", "owner-aba", "departure" }
                         select (entry, scenario)).Concat(new[]
                         {
                             ("condition-apply", "old-render"), ("condition-clear", "old-render"),
                             ("primary-arm", "unissued-copy")
                         }).ToArray();
            foreach (var (entry, scenario) in cases)
            {
                try
                {
                    await RunIssuedArmConditionPageCaseAsync(contentRoot, ui, entry, scenario);
                    passed++;
                    Console.WriteLine($"PASS actual issued page: {entry}/{scenario}");
                }
                catch (IssuedPageUnjoinedException) { throw; } // Never start another case over a live callback.
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    failures.Add($"{entry}/{scenario}: {error.Message}");
                    Console.WriteLine($"FAIL actual issued page: {entry}/{scenario}: {error}");
                    if (error.Message.StartsWith("SETUP", StringComparison.Ordinal) || error.InnerException is not null)
                        throw; // Never continue the batch after invalid setup/action infrastructure.
                }
            }
            Console.WriteLine($"ISSUED_PAGE_SUMMARY total={cases.Length} passed={passed} failed={failures.Count} skipped=0");
            Require(failures.Count == 0, "Original issued-page regression: " + string.Join("; ", failures));
        }); // The process scope owns the overall deadline; do not tear down a live UI pump.
    }

    private static async Task RunIssuedArmConditionPageCaseAsync(
        string contentRoot, IssuedPageUiContext ui, string entry, string scenario)
    {
        // Obtain both partition identities from the actual account service. These
        // controlled HTTP responses are fixture construction, not real Hub grants.
        using var account = new ActualAccountFixture();
        await account.Account.InitializeAsync();
        await account.LinkAsync("issued-page-B", "issued-page-grant-B-seed");
        OwnerScope ownerB = account.Owner.Capture().Owner;
        await account.Account.UnlinkAsync();
        await account.LinkAsync("issued-page-A", "issued-page-grant-A-seed");
        OwnerScope ownerA = account.Owner.Capture().Owner;
        Require(ownerA != ownerB && ownerA != OwnerScope.LocalSingleUser,
            "SETUP: real account fixture did not produce distinct linked owners.");
        await using var runtime = new NativeRewardRuntime(contentRoot,
            linkedOwners: account.Owner, accountService: account.Account);
        await runtime.Coordinator.InitializeAsync();
        await AwaitNativeAccountOwnerInitializationAsync(runtime, account, account.Owner.Capture());
        await runtime.LoadRunnerAsync("""
            <character><name>Issued owner page</name><gameedition>SR5</gameedition>
            <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings>
            <metatype>Human</metatype><buildmethod>Priority</buildmethod>
            <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
            <created>True</created><karma>30</karma><nuyen>1000</nuyen>
            <streetcred>10</streetcred><notoriety>4</notoriety><publicawareness>6</publicawareness>
            <burntstreetcred>0</burntstreetcred><improvements/><primaryarm>Right</primaryarm>
            <physicalcm>10</physicalcm><physicalcmoverflow>3</physicalcmoverflow><physicalcmfilled>2</physicalcmfilled>
            <stuncm>10</stuncm><stuncmfilled>4</stuncmfilled>
            <contacts/><expenses/><notes>Preserve unrelated issued-page data</notes></character>
            """);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var originalRow = store.Get(ownerA, runtime.Id);
        Require(originalRow.Success && originalRow.Value is { ContentRevision: 1, SavedRevision: 1 },
            "SETUP: real saved A runner is unavailable.");
        Require(store.CreateWorkspaceDocument(ownerB, runtime.Id, originalRow.Value!.Document).Success
            && store.SaveCheckpoint(ownerB, runtime.Id, 1).Success,
            "SETUP: real same-ID saved B partition could not be constructed.");
        var initialActivation = await runtime.Coordinator.SwitchWorkspaceAsync(
            runtime.Coordinator.State.OpenWorkspaces.Single(value => value.Id == runtime.Id));
        Require(initialActivation is { Kind: NativeWorkspaceActivationKind.WorkspaceSwitch }
            && initialActivation.WorkspaceId == runtime.Id
            && runtime.Coordinator.IsWorkspaceActivationCurrent(initialActivation, NativeWorkspaceActivationKind.WorkspaceSwitch),
            "SETUP: real native initial activation did not join presenter and shell.");
        await SelectSurfaceAsync();
        RequireNativeAccountDisplay(runtime, account, account.Owner.Capture(), runtime.Id, "before issuing editor");
        OwnerContextStamp issuedOwner = account.Owner.Capture();
        CharacterOverviewState issuedDisplay = runtime.Coordinator.State;
        PrimaryArmEditorState? issuedEditor = entry == "primary-arm"
            ? await runtime.Coordinator.PreparePrimaryArmEditAsync() : null;
        if (entry == "primary-arm")
            Require(issuedEditor is { Value: "Right", Ambidextrous: false }
                && runtime.Coordinator.IsPrimaryArmEditorCurrent(issuedEditor),
                "SETUP: real coordinator did not issue an actionable primary-arm editor.");
        PrimaryArmEditorState? pageEditor = scenario == "unissued-copy" ? issuedEditor! with { } : issuedEditor;
        if (scenario == "unissued-copy")
            Require(pageEditor == issuedEditor && !ReferenceEquals(pageEditor, issuedEditor)
                && !runtime.Coordinator.IsPrimaryArmEditorCurrent(pageEditor!),
                "SETUP: copied editor did not isolate issued-reference admission.");
        // Successful primary-arm preparation legitimately publishes a fresh
        // overview record. Retain the original issued frame/owner and editor,
        // but compare appearance against the actual post-prepare display.
        CharacterOverviewState appearanceBaseline = runtime.Coordinator.State;
        NativePageBase page = entry == "primary-arm"
            ? new PrimaryArmPage(runtime.Coordinator, pageEditor!)
            : new ConditionMonitorEditPage(runtime.Coordinator, WorkspaceConditionMonitorTrack.Physical);
        var rootPage = new ContentPage { Title = "Original route" };
        var navigation = new NavigationPage(rootPage);
        await navigation.PushAsync(page, animated: false);
        var window = new Window(navigation);
        using var alerts = new IssuedPageAlerts(page, window);
        await alerts.PreflightAsync();
        Task? pending = null;
        bool gateHeld = false;
        var activation = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        var actionGate = (NativePageActionGate)typeof(NativePageBase)
            .GetField("_actionGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        var before = ColdRows();
        Exception? failure = null;
        string delivery = "ordinary SendClicked";
        Page expectedTop = page;
        long appearance = 0;
        try
        {
            Require(IssuedPageField<long>(page, "_appearanceGeneration") == 0,
                "SETUP: navigation unexpectedly started an unjoined appearance.");
            pending = ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            await JoinIssuedPageAsync(pending);
            pending = null;
            appearance = IssuedPageField<long>(page, "_appearanceGeneration");
            var appearanceReadiness = new
            {
                generationStarted = appearance > 0,
                subscribed = IssuedPageField<int>(page, "_subscribed") == 1,
                refreshJoined = IssuedPageField<int>(page, "_appearanceRefreshActive") == 0,
                lifetimeReleased = IssuedPageField<object?>(page, "_appearanceLifetime") is null,
                postPrepareDisplayRetained = ReferenceEquals(runtime.Coordinator.State, appearanceBaseline),
                originalFrameRetained = ReferenceEquals(runtime.Coordinator.State, issuedDisplay),
                noAlerts = alerts.Titles.Count == 0
            };
            Console.WriteLine("ISSUED_APPEARANCE_READINESS " + JsonSerializer.Serialize(new { entry, scenario, appearanceReadiness }));
            Require(appearanceReadiness.generationStarted && appearanceReadiness.subscribed
                && appearanceReadiness.refreshJoined && appearanceReadiness.lifetimeReleased
                && appearanceReadiness.postPrepareDisplayRetained && appearanceReadiness.noAlerts,
                "SETUP: actual appearance readiness failed: " + JsonSerializer.Serialize(appearanceReadiness));
            Button button = IssuedElements(page).OfType<Button>().Single(value => value.AutomationId == (entry switch
            {
                "primary-arm" => "primary-arm-save", "condition-apply" => "condition-monitor-save-physical",
                _ => "condition-monitor-clear-physical"
            }));
            Picker picker = IssuedElements(page).OfType<Picker>().Single();
            picker.SelectedIndex = entry == "primary-arm" ? 0 : 3;
            Require(button.IsEnabled == (scenario != "unissued-copy"),
                "SETUP: issued button capability differs from the intended case.");
            if (scenario is "owner-b" or "owner-aba")
            {
                await ChangeOwnerAsync("issued-page-B", ownerB);
                if (scenario == "owner-aba") await ChangeOwnerAsync("issued-page-A", ownerA);
                bool staleAccepted = account.Owner.TryAcquire(issuedOwner, out var unexpectedLease);
                unexpectedLease?.Dispose();
                Require(account.Owner.Capture() != issuedOwner && !staleAccepted,
                    "SETUP: B/ABA did not invalidate the actual original account epoch.");
            }
            else if (scenario == "old-render")
            {
                long oldRender = (long)typeof(ConditionMonitorEditPage)
                    .GetField("_renderGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                typeof(ConditionMonitorEditPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                Require(ReferenceEquals(runtime.Coordinator.State, issuedDisplay)
                    && !IssuedElements(page).Contains(button)
                    && (long)typeof(ConditionMonitorEditPage).GetField("_renderGeneration",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)! > oldRender,
                    "SETUP: actual Refresh did not detach only the original condition render.");
            }
            Require(ColdRows().All(pair => before[pair.Key] == pair.Value),
                "SETUP: appearance or account hydration already changed a canonical row.");
            if (scenario == "departure")
            {
                await activation.WaitAsync();
                gateHeld = true;
            }
            int startedBefore = ui.AsyncVoidStarts;
            if (!button.IsEnabled)
            {
                ((IButtonController)button).SendClicked();
                Require(ui.AsyncVoidStarts == startedBefore && !actionGate.IsClaimed,
                    "Ordinary disabled-button delivery entered an action.");
                delivery = "adversarial registered Clicked propagation after disabled ordinary rejection";
                // MAUI 10.0.20's internal propagation entry invokes this exact
                // Button's real registered handler. Do not enable the button or
                // rewrite any page guard. This is not an Android disabled tap.
                Type element = typeof(Button).Assembly.GetType("Microsoft.Maui.Controls.Internals.IButtonElement", true)!;
                MethodInfo propagate = element.GetMethod("PropagateUpClicked")
                    ?? throw new InvalidOperationException("SETUP: actual MAUI Clicked propagation is unavailable.");
                pending = ui.BeginAsyncVoid(() => propagate.Invoke(button, null));
            }
            else pending = ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
            Require(ui.AsyncVoidStarts > startedBefore, "SETUP: actual button callback never started.");
            if (scenario == "departure")
            {
                Require(!pending.IsCompleted && actionGate.IsClaimed,
                    "SETUP: real page callback did not queue behind the activation barrier.");
                IssuedPageLifecycle(page, "OnDisappearing");
                Require(IssuedPageField<int>(page, "_subscribed") == 0
                    && IssuedPageField<long>(page, "_appearanceGeneration") > appearance,
                    "SETUP: real departure did not retire page appearance.");
                expectedTop = new ContentPage { Title = "Newer route" };
                await navigation.PushAsync(expectedTop, animated: false);
            }
            if (gateHeld) { activation.Release(); gateHeld = false; }
            await JoinIssuedPageAsync(pending);
            pending = null;
            ui.AssertHealthy();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { failure = error; }
        finally
        {
            if (gateHeld) activation.Release();
            if (IssuedPageField<int>(page, "_subscribed") != 0) IssuedPageLifecycle(page, "OnDisappearing");
            if (pending is not null) await JoinIssuedPageAsync(pending);
        }
        // Complete cold rows are observed after every joined dispatch, including
        // a swallowed page error. A navigation/alert failure cannot hide a write.
        var after = ColdRows();
        string[] changed = after.Keys.Where(key => before[key] != after[key]).ToArray();
        Console.WriteLine("ISSUED_PAGE_OBSERVATION " + JsonSerializer.Serialize(new
        {
            entry, scenario, delivery, issuedOwner, liveOwner = account.Owner.Capture(), appearance,
            displayOwner = runtime.Coordinator.State.DisplayOwnerContext,
            sessionOwner = runtime.Coordinator.State.Session.OwnerContext,
            runtime.Coordinator.State.WorkspaceId, runtime.Coordinator.State.ContentRevision,
            runtime.Coordinator.State.SavedRevision, runtime.Coordinator.State.Error,
            changed, before = Summarize(before), after = Summarize(after), alerts.Titles,
            navigationDepth = navigation.Navigation.NavigationStack.Count,
            failure = failure?.GetType().Name, failureDetails = failure?.ToString()
        }));
        if (failure is not null) throw new InvalidOperationException("SETUP/action failure after cold observation.", failure);
        Require(!actionGate.IsClaimed && alerts.Titles.Count == 0,
            "Actual page action was left claimed or hid a rejected/failed positive in an alert.");
        if (scenario == "same")
        {
            var row = new FileWorkspaceStore(runtime.StateDirectory).Get(ownerA, runtime.Id).Value!;
            string field = entry == "primary-arm" ? "primaryarm" : "physicalcmfilled";
            string value = entry == "primary-arm" ? "Left" : entry == "condition-apply" ? "3" : "0";
            var expectedXml = XDocument.Parse(originalRow.Value!.Document.Content);
            expectedXml.Root!.Element(field)!.Value = value;
            Require(changed.SequenceEqual(new[] { "a" }) && row.ContentRevision == 2
                && row.SavedRevision == (entry == "primary-arm" ? 2 : 1)
                && XDocument.Parse(row.Document.Content).ToString(SaveOptions.DisableFormatting)
                    == expectedXml.ToString(SaveOptions.DisableFormatting)
                && runtime.Coordinator.State.DisplayOwnerContext == issuedOwner
                && runtime.Coordinator.State.Session.OwnerContext == issuedOwner
                && runtime.Coordinator.State.ContentRevision == row.ContentRevision
                && runtime.Coordinator.State.SavedRevision == row.SavedRevision,
                "POSITIVE: real issued page did not mutate only A with exact existing checkpoint semantics.");
            if (entry == "primary-arm") expectedTop = rootPage;
        }
        else Require(changed.Length == 0, "DEFECT: original issued page mutated canonical partition(s): " + string.Join(",", changed));
        Require(ReferenceEquals(navigation.CurrentPage, expectedTop),
            "An old page popped or replaced a newer route, or a successful primary-arm save did not return.");
        Require(account.Owner.TryAcquire(account.Owner.Capture(), out var lease),
            "Joined page action left actual owner admission unavailable.");
        lease!.Dispose();

        async Task SelectSurfaceAsync()
        {
            await runtime.Coordinator.SelectTabAsync("tab-combat", default);
            Require(runtime.Presenter.State.ActiveTabId == "tab-combat" && runtime.Shell.State.ActiveTabId == "tab-combat",
                "SETUP: actual native tab selection did not synchronize shell/presenter.");
            if (entry != "primary-arm")
            {
                var surface = runtime.Services.GetRequiredService<IShellSurfaceResolver>().Resolve(runtime.Presenter.State, runtime.Shell.State);
                await runtime.Presenter.ExecuteWorkspaceActionAsync(surface.WorkspaceActions.Single(item => item.Id == "tab-combat.conditionmonitor"), default);
                Require(runtime.Coordinator.State.ActiveConditionMonitor is { CareerEditable: true } monitor
                    && monitor.Tracks.Single(track => track.Track == WorkspaceConditionMonitorTrack.Physical)
                        is { Filled: 2, EditableMaximum: >= 3 },
                    "SETUP: genuine career condition action is unavailable.");
            }
        }

        async Task ChangeOwnerAsync(string subject, OwnerScope expectedOwner)
        {
            OwnerContextStamp current;
            // Join the final linked owner, as the real account regression does.
            // The transient empty Local partition is not this stale-page case.
            await activation.WaitAsync();
            try
            {
                await account.Account.UnlinkAsync();
                await account.LinkAsync(subject, "issued-page-grant-" + Guid.NewGuid().ToString("N"));
                current = account.Owner.Capture();
            }
            finally { activation.Release(); }
            Require(current.Owner == expectedOwner, "SETUP: fixture changed the wrong actual account identity.");
            await AwaitNativeAccountOwnerInitializationAsync(runtime, account, current);
            var item = runtime.Coordinator.State.OpenWorkspaces.Single(value => value.Id == runtime.Id);
            var receipt = await runtime.Coordinator.SwitchWorkspaceAsync(item);
            Require(receipt is { Kind: NativeWorkspaceActivationKind.WorkspaceSwitch }
                && receipt.WorkspaceId == runtime.Id
                && runtime.Coordinator.IsWorkspaceActivationCurrent(receipt, NativeWorkspaceActivationKind.WorkspaceSwitch),
                "SETUP: actual native same-ID account reopening failed.");
            await SelectSurfaceAsync();
            RequireNativeAccountDisplay(runtime, account, current, runtime.Id, "after actual linked owner reopening");
            Require(runtime.Coordinator.State.ContentRevision == 1 && runtime.Coordinator.State.SavedRevision == 1,
                "SETUP: same-ID owner transition did not retain exact saved revisions.");
        }

        Dictionary<string, string> ColdRows()
        {
            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            return new[] { (Name: "a", Owner: ownerA), (Name: "b", Owner: ownerB) }.ToDictionary(pair => pair.Name, pair =>
            {
                var read = cold.Get(pair.Owner, runtime.Id);
                Require(read.Success && read.Value is not null, "SETUP: canonical owner partition disappeared.");
                return JsonSerializer.Serialize(read.Value);
            });
        }
        static object Summarize(Dictionary<string, string> rows) => rows.ToDictionary(pair => pair.Key, pair =>
        {
            using var json = JsonDocument.Parse(pair.Value);
            return new { contentRevision = json.RootElement.GetProperty("ContentRevision").GetInt64(),
                savedRevision = json.RootElement.GetProperty("SavedRevision").GetInt64(),
                completeRowSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value))),
                documentSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                    json.RootElement.GetProperty("Document").GetRawText()))) };
        });
    }

    private static async Task JoinIssuedPageAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException error) { throw new IssuedPageUnjoinedException("Actual page callback did not join; stop this run.", error); }
    }
    private sealed class IssuedPageUnjoinedException(string message, Exception inner) : Exception(message, inner);
    private static T IssuedPageField<T>(NativePageBase page, string field)
        => (T)typeof(NativePageBase).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
    private static void IssuedPageLifecycle(NativePageBase page, string method)
        => typeof(NativePageBase).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null, types: Type.EmptyTypes, modifiers: null)!.Invoke(page, null);
    private static IEnumerable<Element> IssuedElements(Element root)
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
        foreach (Element descendant in IssuedElements(child)) yield return descendant;
    }

    // A serial managed UI pump, not a native dispatcher emulator. It tracks the
    // actual async-void methods via their BCL lifecycle, not a forged Task result.
    private sealed class IssuedPageUiContext : SynchronizationContext, IDispatcherProvider, IDispatcher, IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        private readonly IDispatcherProvider _prior = DispatcherProvider.Current;
        private readonly List<Exception> _errors = [];
        private TaskCompletionSource? _callback;
        private int _operations;
        public int AsyncVoidStarts { get; private set; }
        public IssuedPageUiContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (Action action in _queue.GetConsumingEnumerable())
                    try { action(); }
                    catch (Exception error) { _errors.Add(error); _callback?.TrySetException(error); }
            }) { IsBackground = true, Name = "issued-page-managed-ui" };
            DispatcherProvider.SetCurrent(this);
            _thread.Start();
        }
        public Task RunAsync(Func<Task> action)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ => _ = CompleteAsync(), null);
            return completed.Task;
            async Task CompleteAsync()
            {
                try { await action(); AssertHealthy(); completed.SetResult(); }
                catch (Exception error) { completed.SetException(error); }
            }
        }
        public Task BeginAsyncVoid(Action action)
        {
            Require(ReferenceEquals(Current, this) && _operations == 0,
                "SETUP: overlapping or off-context async-void admission.");
            _callback = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int previous = AsyncVoidStarts;
            action();
            Require(AsyncVoidStarts > previous, "SETUP: callback was not an actual async-void page entry.");
            if (_operations == 0) _callback.TrySetResult();
            return _callback.Task;
        }
        public override void OperationStarted() { _operations++; AsyncVoidStarts++; }
        public override void OperationCompleted() { if (--_operations == 0) _callback?.TrySetResult(); }
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add(() => callback(state));
        public IDispatcher GetForCurrentThread() => this;
        public bool IsDispatchRequired => Environment.CurrentManagedThreadId != _thread.ManagedThreadId;
        public bool Dispatch(Action action) { _queue.Add(action); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) => throw new NotSupportedException("Unexpected delayed dispatcher action.");
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException("Unexpected dispatcher timer.");
        public void AssertHealthy() => Require(_errors.Count == 0 && _operations == 0,
            "Headless UI callback fault/unjoined operation: " + string.Join("; ", _errors.Select(error => error.Message)));
        public void Dispose()
        {
            DispatcherProvider.SetCurrent(_prior);
            _queue.CompleteAdding();
            Require(_thread.Join(TimeSpan.FromSeconds(5)), "Headless UI pump did not terminate.");
            _queue.Dispose();
        }
    }

#pragma warning disable CS0618 // Locked MAUI presentation-only alert adapter, no authority fields.
    private sealed class IssuedPageAlerts : IDisposable
    {
        private readonly Page _page;
        private readonly bool _priorEnabled;
        private readonly object _manager;
        private readonly FieldInfo _subscription;
        private readonly object? _priorSubscription;
        public List<string> Titles { get; } = [];
        public IssuedPageAlerts(Page page, Window window)
        {
            _page = page;
            _priorEnabled = ((IVisualElementController)page).IsPlatformEnabled;
            Require(ReferenceEquals(page.GetParentWindow(), window), "SETUP: alert page has no real containing Window.");
            _manager = typeof(Window).GetProperty("AlertManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(window) ?? throw new InvalidOperationException("SETUP: real Window alert manager is absent.");
            _subscription = _manager.GetType().GetField("_subscription", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("SETUP: locked MAUI alert transport field is absent.");
            Require(_subscription.FieldType.IsInterface
                && _subscription.FieldType.FullName == "Microsoft.Maui.Controls.Platform.AlertManager+IAlertManagerSubscription",
                "SETUP: locked MAUI alert transport identity differs.");
            _priorSubscription = _subscription.GetValue(_manager);
            object transport = DispatchProxy.Create(_subscription.FieldType, typeof(IssuedPageAlertTransport));
            ((IssuedPageAlertTransport)transport).Acknowledge = (source, args) =>
            {
                Require(ReferenceEquals(source, page), "Unrelated page reached filtered alert handler.");
                Require(string.IsNullOrEmpty(args.Accept) && args.Cancel == "OK",
                    "Headless adapter cannot answer confirmation or a non-informational dialog.");
                Titles.Add(args.Title ?? "");
                args.SetResult(false); // Informational OK dismissal only, never confirmation/receipt.
            };
            try
            {
                // MAUI 10 uses Window.AlertManager, not the obsolete messaging
                // signal. Only this private test Window's presentation transport
                // is supplied; no lifecycle, coordinator, or authority fields.
                _subscription.SetValue(_manager, transport);
                ((IVisualElementController)page).IsPlatformEnabled = true;
            }
            catch { Dispose(); throw; }
        }
        public async Task PreflightAsync()
        {
            await _page.DisplayAlertAsync("Issued-page adapter preflight", "No product action.", "OK")
                .WaitAsync(TimeSpan.FromSeconds(2));
            Require(Titles.SequenceEqual(new[] { "Issued-page adapter preflight" }), "SETUP: actual alert adapter did not acknowledge once.");
            Titles.Clear();
        }
        public void Dispose()
        {
            try { _subscription.SetValue(_manager, _priorSubscription); }
            finally { ((IVisualElementController)_page).IsPlatformEnabled = _priorEnabled; }
        }
    }
#pragma warning restore CS0618
}

// DispatchProxy's base must be publicly visible; the reflected locked MAUI
// interface is internal. This proxy cannot supply navigation or mutation results.
public class IssuedPageAlertTransport : DispatchProxy
{
    internal Action<Page, AlertArguments>? Acknowledge;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != "OnAlertRequested" || method.ReturnType != typeof(void)
            || args is not [Page page, AlertArguments alert])
            throw new InvalidOperationException("Unexpected headless presentation transport request.");
        (Acknowledge ?? throw new InvalidOperationException("Headless alert transport is unconfigured."))(page, alert);
        return null;
    }
}
