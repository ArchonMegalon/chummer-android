using System.Reflection;
using Chummer.Android.Native;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCreationReloadAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var previousApp = Application.Current;
            var app = new ReviewRouteApplication();
            Application.Current = app;
            var window = (Window)((Microsoft.Maui.IApplication)app).CreateWindow(null!);
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true);
            var saved = PrepareActualFinalizationReadyContext(runtime);
            try
            {
                foreach (string scenario in new[] { "abandoned-press", "navigating", "old-render", "old-appearance", "owner-aba" })
                {
                    await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                    var page = new BuildPage(runtime.Coordinator);
                    var shell = new Shell();
                    shell.Items.Add(new ShellContent { Content = page });
                    var scroll = (ScrollView)page.Content!;
                    scroll.ScrollToRequested += (_, _) => scroll.SendScrollFinished();
                    void Invoke(string name) => typeof(BuildPage).GetMethod(name,
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(page, null);
                    var projectionField = typeof(BuildPage).GetField("_creationProjection",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var lease = (CreationNavigationRefreshLease)typeof(BuildPage)
                        .GetField("_creationNavigationRefreshLease", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(page)!;
                    window.Page = shell;
                    await ui.DrainDispatchedAsyncVoidAsync();
                    try
                    {
                        Require(ReferenceEquals(Shell.Current?.CurrentPage, page), "SETUP: dashboard must be visible.");
                        // Complete a real appearance, then inject only a failed read result.
                        // All runner state, binding and retry controls are from production code.
                        Invoke("OnDisappearing");
                        await ui.BeginAsyncVoid(() => Invoke("OnAppearing"));
                        await ui.DrainDispatchedAsyncVoidAsync();
                        Invoke("CancelCreationProjectionQueues");
                        Require(CreationDashboardProjectionBinding.TryCreate(runtime.Coordinator.State,
                            runtime.Coordinator.State.CreationWizard!, out var binding), "SETUP: missing current binding.");
                        var failed = CreationDashboardAuthorityProjection.Loading(binding!);
                        foreach (var phase in Enum.GetValues<CreationDashboardAuthorityPhase>())
                            failed = failed with { Progress = failed.Progress.WithTerminal(phase, failed: true) };
                        projectionField.SetValue(page, failed);
                        Invoke("Refresh");
                        Invoke("CancelCreationProjectionQueues");
                        var retry = IssuedElements(page).OfType<Button>().Single(button =>
                            button.AutomationId == "creation-dashboard-authority-retry");
                        long press = lease.BeginPress();
                        Require(lease.TryDeferRefresh(), "SETUP: read completion must wait for the press.");
                        var rejected = new CreationNavigationReleaseScheduler(static (_, _) => false);
                        Require(!rejected.TrySchedule(TimeSpan.FromMilliseconds(100), () => lease.CancelPress(press)),
                            "SETUP: emulate rejected release settlement with no later Clicked.");
                        long navigation = 0;
                        if (scenario == "navigating")
                            Require(lease.TryBeginNavigation(press, out navigation), "SETUP: navigation must be claimed.");
                        if (scenario == "old-render") Invoke("Refresh");
                        if (scenario == "old-appearance") Invoke("OnDisappearing");
                        if (scenario == "owner-aba")
                        {
                            owners.Set(ContactsOwnerB);
                            owners.Set(OwnerScope.LocalSingleUser);
                        }
                        object? beforeClick = projectionField.GetValue(page);
                        ((IButtonController)retry).SendClicked();
                        if (scenario == "abandoned-press")
                        {
                            Require(!lease.IsActive,
                                "Explicit Retry is still stranded behind the abandoned navigation press.");
                            Require(projectionField.GetValue(page) is CreationDashboardAuthorityProjection fresh
                                && !ReferenceEquals(fresh, beforeClick) && fresh.Binding.Equals(binding),
                                "Explicit Retry did not start fresh authority reads on the same page.");
                            Require(!lease.TryCancelPress(press, out _) && !lease.TryBeginNavigation(press, out _),
                                "Late release/click from the abandoned gesture survived explicit Retry.");
                        }
                        else
                        {
                            Require(ReferenceEquals(projectionField.GetValue(page), beforeClick),
                                $"Retry reset authority during {scenario}.");
                            Require(scenario == "old-appearance"
                                ? !lease.IsActive && !lease.HasPendingRefresh
                                : lease.IsActive && lease.HasPendingRefresh,
                                $"Retry discarded the protected navigation lease during {scenario}.");
                            if (scenario == "navigating")
                                Require(lease.CompleteNavigation(navigation, departed: false),
                                    "In-flight navigation lost its single pending refresh.");
                        }
                        Console.WriteLine($"PASS Creation explicit reload: {scenario}");
                    }
                    finally { Invoke("OnDisappearing"); }
                    await ui.DrainDispatchedAsyncVoidAsync();
                }
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                Require(FinalizationDocumentDigest(cold) == FinalizationDocumentDigest(saved),
                    "Read-only retry changed the saved runner.");
                ui.AssertHealthy();
            }
            finally
            {
                app.CloseWindow(window);
                Application.Current = previousApp!;
            }
        });
    }
}
