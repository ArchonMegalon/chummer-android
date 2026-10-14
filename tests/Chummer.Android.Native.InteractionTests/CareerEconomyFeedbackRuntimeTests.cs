using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCareerEconomyFeedbackAsync(string contentRoot, string fixture)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(fixture));
        string xml = json.RootElement.GetProperty("Envelope").GetProperty("Payload").GetString()!;
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string currency in new[] { "karma", "nuyen" })
            foreach (string scenario in new[] { "saved", "departure", "shell-failure", "owner-aba-before", "owner-aba-during" })
            {
                NativePageBase? page = null;
                bool observing = false;
                int pendingObservations = 0;
                int shellListsAfterSave = 0;
                NativeRewardRuntime? observedRuntime = null;
                Chummer.Presentation.Overview.CharacterOverviewState? initialState = null;
                Button? gain = null;
                ActivityIndicator? progress = null;
                var owners = new ControlledLinkedOwner();
                void ObserveSave()
                {
                    if (!observing || pendingObservations != 0) return;
                    void Observe()
                    {
                        pendingObservations++;
                        Require(progress is { IsRunning: true, IsVisible: true },
                            "Manual Career booking has no visible pending feedback.");
                        Require(MinimalVisible(page!).OfType<Button>().All(x => !x.IsEnabled)
                            && MinimalVisible(page!).OfType<Entry>().All(x => !x.IsEnabled)
                            && MinimalVisible(page!).OfType<Editor>().All(x => !x.IsEnabled),
                            "Pending booking allows editing or another submission.");
                        ((IButtonController)gain!).SendClicked();
                        if (scenario == "departure") IssuedPageLifecycle(page!, "OnDisappearing");
                        if (scenario == "owner-aba-during")
                        {
                            OwnerScope original = owners.Current;
                            owners.Set(new OwnerScope("career-save-other"));
                            owners.Set(original);
                        }
                    }
                    if (ReferenceEquals(SynchronizationContext.Current, ui)) Observe();
                    else
                    {
                        var checkedUi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(_ =>
                        {
                            try { Observe(); checkedUi.SetResult(); }
                            catch (Exception error) { checkedUi.SetException(error); }
                        }, null);
                        checkedUi.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    }
                }
                await using var runtime = new NativeRewardRuntime(contentRoot,
                    linkedOwners: owners, productionCreationOverview: true,
                    beforeShellWorkspaceList: () =>
                    {
                        if (!observing) return;
                        shellListsAfterSave++;
                        var state = observedRuntime!.Coordinator.State;
                        var shell = observedRuntime.Shell.State;
                        Console.WriteLine("ECONOMY_REFRESH_CONTEXT " + JsonSerializer.Serialize(new
                        {
                            state.IsBusy, state.IsDirty, Error = state.Error is not null,
                            Conflict = state.ConflictState is not null,
                            OldRevision = initialState!.ContentRevision, state.ContentRevision, state.SavedRevision,
                            Created = state.Profile?.Created, OriginalCreated = initialState.Profile?.Created,
                            OriginalOwnerValid = initialState.DisplayOwnerContext?.IsValid,
                            OriginalSessionMatches = initialState.DisplayOwnerContext == initialState.Session.OwnerContext,
                            DisplayMatches = initialState.DisplayOwnerContext == state.DisplayOwnerContext,
                            SessionMatches = initialState.DisplayOwnerContext == state.Session.OwnerContext,
                            OriginalActive = initialState.Session.ActiveWorkspaceId == initialState.WorkspaceId,
                            ActiveMatches = state.Session.ActiveWorkspaceId == initialState.WorkspaceId,
                            ShellActiveMatches = shell.ActiveWorkspaceId == initialState.WorkspaceId,
                            ShellOwnerMatches = shell.OwnerContext == initialState.DisplayOwnerContext,
                            ShellBusy = shell.IsBusy, ShellError = shell.Error is not null,
                            Commands = shell.Commands.Count, Tabs = shell.NavigationTabs.Count,
                            RulesetMatches = state.ActiveWorkspace?.RulesetId == shell.ActiveRulesetId,
                            OriginalRuleset = initialState.ActiveWorkspace?.RulesetId,
                            SavedRuleset = state.ActiveWorkspace?.RulesetId,
                            ShellRuleset = shell.ActiveRulesetId,
                            ProfileEdition = state.Rules?.GameEdition,
                            Workflows = shell.WorkflowDefinitions?.Count,
                            Surfaces = shell.WorkflowSurfaces?.Count,
                            RosterMatches = state.OpenWorkspaces.SequenceEqual(state.Session.OpenWorkspaces),
                            ShellRosterMatches = shell.OpenWorkspaces.SequenceEqual(state.OpenWorkspaces.Select(x =>
                                new Chummer.Presentation.Shell.ShellWorkspaceState(x.Id, x.Name, x.Alias, x.LastOpenedUtc,
                                    x.RulesetId, x.HasSavedWorkspace)).OrderByDescending(x => x.LastOpenedUtc))
                        }));
                        if (scenario == "shell-failure") throw new IOException("Synthetic booking shell failure.");
                    });
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.LoadRunnerAsync(xml);
                var c = runtime.Coordinator;
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                var before = store.Get(runtime.Id).Value!;
                observedRuntime = runtime;
                initialState = c.State;
                runtime.Presenter.StateChanged += (_, _) =>
                {
                    // Observe actual persistence, not the redundant shell refresh
                    // this regression removes. No fake mutation or save result.
                    if (runtime.Presenter.State.IsBusy && runtime.Presenter.State.IsDirty)
                        ObserveSave();
                };
                if (scenario == "shell-failure")
                {
                    // An incomplete shell must keep the full refresh/failure path.
                    typeof(Chummer.Presentation.Shell.ShellPresenter).GetProperty("State")!
                        .SetValue(runtime.Shell, runtime.Shell.State with { Commands = [] });
                }
                page = currency == "karma"
                    ? new CareerManualKarmaPage(c, (await c.PrepareCareerManualKarmaEditAsync())!)
                    : new CareerManualNuyenPage(c, (await c.PrepareCareerManualNuyenEditAsync())!);
                string prefix = "career-manual-" + currency;
                var root = new ContentPage();
                var navigation = new NavigationPage(root);
                await navigation.PushAsync(page, false);
                var window = new Window(navigation);
                using var alerts = new IssuedPageAlerts(page, window);
                await alerts.PreflightAsync();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                var options = body.Children.OfType<VerticalStackLayout>().Single(x => x.AutomationId == prefix + "-options");
                var toggle = body.Children.OfType<Button>().Single(x => x.AutomationId == prefix + "-options-toggle");
                gain = body.Children.OfType<Button>().Single(x => x.AutomationId == prefix + "-gain");
                progress = body.Children.OfType<ActivityIndicator>().Single(x => x.AutomationId == prefix + "-save-progress");
                Require(!options.IsVisible && body.Children.IndexOf(gain) < body.Children.IndexOf(toggle),
                    "Optional bookkeeping fields hide the main Career action.");
                ((IButtonController)toggle).SendClicked();
                Require(options.IsVisible, "Optional date/exchange settings became inaccessible.");
                ((IButtonController)toggle).SendClicked();
                Require(!options.IsVisible, "Bookkeeping options did not collapse.");
                RequireSameRewardDocument(before, store.Get(runtime.Id).Value!);
                body.Children.OfType<Entry>().Single(x => x.AutomationId == prefix + "-amount").Text = "5";
                Require(gain.IsEnabled, "Actual saved runner cannot record a normal gain.");
                if (scenario == "owner-aba-before")
                {
                    OwnerScope original = owners.Current;
                    owners.Set(new OwnerScope("manual-economy-other"));
                    owners.Set(original);
                    page.GetType().GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                    Require(!gain.IsEnabled && !toggle.IsEnabled
                        && body.Children.OfType<Label>().Single(x => x.AutomationId == prefix + "-save-status").IsVisible,
                        "Owner ABA revived a stale manual booking or left it silently disabled.");
                    ((IButtonController)gain).SendClicked();
                    await ui.DrainDispatchedAsyncVoidAsync();
                    RequireSameRewardDocument(before, store.Get(runtime.Id).Value!);
                    Require(navigation.Navigation.NavigationStack.Count == 2 && !progress.IsRunning,
                        "Stale owner action started work or navigated away.");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    Console.WriteLine($"PASS Career {currency}/owner-aba-before: stale action rejected, guidance visible, saved bytes unchanged.");
                    continue;
                }
                observing = true;
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)gain).SendClicked()));
                observing = false;
                if (scenario is "saved" or "departure")
                    Require(shellListsAfterSave == 0,
                        "An exact durable economy save unnecessarily repeated shell roster/inbound synchronization.");
                if (scenario == "shell-failure")
                    Require(shellListsAfterSave > 0, "An incomplete shell skipped its required refresh.");
                if (scenario == "saved") VerifyCareerEconomyShellGuards(runtime, initialState!);
                Require(pendingObservations > 0 && !progress.IsRunning && !progress.IsVisible,
                    "Booking did not show bounded save feedback or left its spinner active.");
                Require(navigation.Navigation.NavigationStack.Count == (scenario == "saved" ? 1 : 2),
                    "Booking failed to continue, or a late completion navigated a departed/failed page.");
                var after = store.Get(runtime.Id).Value!;
                ((IButtonController)gain).SendClicked();
                await ui.DrainDispatchedAsyncVoidAsync();
                RequireSameRewardDocument(after, store.Get(runtime.Id).Value!);
                if (scenario is not ("shell-failure" or "owner-aba-during"))
                {
                    var oldXml = XElement.Parse(before.Document.Content);
                    var savedXml = XElement.Parse(after.Document.Content);
                    Require(after.ContentRevision == before.ContentRevision + 1
                        && after.SavedRevision == after.ContentRevision
                        && decimal.Parse(savedXml.Element(currency)!.Value, System.Globalization.CultureInfo.InvariantCulture)
                            == decimal.Parse(oldXml.Element(currency)!.Value, System.Globalization.CultureInfo.InvariantCulture) + 5
                        && savedXml.Element("expenses")!.Elements("expense").Count()
                            == oldXml.Element("expenses")!.Elements("expense").Count() + 1
                        && XNode.DeepEquals(oldXml.Element("improvements"), savedXml.Element("improvements")),
                        "Manual booking changed the wrong balance, grants, expense count or durable revision.");
                    await runtime.Presenter.LoadAsync(runtime.Id, default);
                    RequireSameRewardDocument(after, store.Get(runtime.Id).Value!);
                }
                Require(!gain.IsEnabled, "Retained completed/uncertain booking can be submitted again.");
                IssuedPageLifecycle(page, "OnDisappearing");
                Console.WriteLine($"PASS Career {currency}/{scenario}: actual Core/native handler, visible saving, frozen inputs, one submission, safe return and retained saved state.");
            }
        });
    }

    private static void VerifyCareerEconomyShellGuards(NativeRewardRuntime runtime,
        Chummer.Presentation.Overview.CharacterOverviewState original)
    {
        var saved = runtime.Coordinator.State;
        var shell = runtime.Shell.State;
        var admission = typeof(RunnerSessionCoordinator).GetMethod("CanReuseCareerEconomyShell",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool Admitted(Chummer.Presentation.Overview.CharacterOverviewState before,
            Chummer.Presentation.Overview.CharacterOverviewState after)
            => (bool)admission.Invoke(runtime.Coordinator, [before, after])!;
        Require(Admitted(original, saved), "Exact saved SR5 economy context did not reuse its shell.");
        foreach (var invalid in new[]
        {
            saved with { IsBusy = true },
            saved with { Error = "Synthetic error" },
            saved with { Profile = null },
            saved with { Profile = saved.Profile! with { Created = false } },
            saved with { Rules = null },
            saved with { Rules = saved.Rules! with { GameEdition = "SR6" } },
            saved with { DisplayOwnerContext = null },
            saved with { Session = saved.Session with { OwnerContext = null } },
            saved with { Session = saved.Session with { ActiveWorkspaceId = null } },
            saved with { OpenWorkspaces = [] },
            saved with { Session = saved.Session with
            {
                OpenWorkspaces = saved.Session.OpenWorkspaces.Select(x => x.Id == saved.WorkspaceId
                    ? x with { SavedRevision = x.SavedRevision - 1 } : x).ToArray()
            } },
            saved with { Session = saved.Session with
            {
                OpenWorkspaces = saved.Session.OpenWorkspaces.Select(x => x.Id == saved.WorkspaceId
                    ? x with { ConflictState = new Chummer.Presentation.Overview.WorkspaceConflictState(
                        "save", x.ContentRevision, x.ContentRevision, "Synthetic conflict") } : x).ToArray()
            } }
        }) Require(!Admitted(original, invalid), "Unbound/dirty/conflicted economy state bypassed full refresh.");
        Require(!Admitted(saved, saved), "Unchanged revision was mistaken for this operation's saved result.");
        Require(!Admitted(original with { DisplayOwnerContext = null }, saved), "Unbound original owner reused shell.");
        Require(!Admitted(original with { Rules = original.Rules! with { GameEdition = "SR6" } }, saved),
            "Changed edition reused the previous shell.");
        var shellProperty = typeof(Chummer.Presentation.Shell.ShellPresenter).GetProperty("State")!;
        try
        {
            foreach (var invalid in new[]
            {
                shell with { IsBusy = true }, shell with { Error = "Synthetic shell error" },
                shell with { Commands = [] }, shell with { NavigationTabs = [] },
                shell with { WorkflowDefinitions = [] }, shell with { WorkflowSurfaces = [] },
                shell with { ActiveRulesetId = "sr6" }, shell with { ActiveWorkspaceId = null },
                shell with { OwnerContext = null }, shell with { OpenWorkspaces = [] }
            })
            {
                shellProperty.SetValue(runtime.Shell, invalid);
                Require(!Admitted(original, saved), "Incomplete/stale shell bypassed its required refresh.");
            }
        }
        finally { shellProperty.SetValue(runtime.Shell, shell); }
        Console.WriteLine("PASS Career economy shell reuse: exact durable context and 25 negative authority/catalog cases.");
    }
}
