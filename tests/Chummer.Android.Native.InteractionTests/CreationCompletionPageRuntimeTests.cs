using System.Globalization;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCreationCompletionPageAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var previousApp = Application.Current;
            var previousCulture = CultureInfo.CurrentUICulture;
            var app = new ReviewRouteApplication();
            Application.Current = app;
            var window = (Window)((Microsoft.Maui.IApplication)app).CreateWindow(null!);
            try
            {
                foreach (string scenario in new[] { "cancel", "apply", "lost-reply", "unknown" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(scenario == "apply" ? "de-AT" : "en-GB");
                    var owners = new ControlledLinkedOwner();
                    CompletionServiceProbe? service = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                        creationCompletion: true, creationFinalization: true, productionCreationOverview: true,
                        completionServiceDecorator: actual => service = new(actual, ui)
                        {
                            ThrowAfterApply = scenario == "lost-reply",
                            ThrowBeforeApply = scenario == "unknown"
                        });
                    await runtime.Coordinator.InitializeAsync();
                    await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                    var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true);
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    var files = CompletionFiles(runtime);
                    var coordinator = runtime.Coordinator;
                    var root = new BuildPage(coordinator);
                    var rootScroll = (IScrollViewController)(ScrollView)root.Content!;
                    rootScroll.ScrollToRequested += (_, _) => rootScroll.SendScrollFinished();
                    var shell = new Shell();
                    shell.Items.Add(new ShellContent { Content = root });
                    window.Page = shell;
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(ReferenceEquals(Shell.Current?.CurrentPage, root),
                        "SETUP: the actual Shell must display the creation dashboard.");
                    IssuedPageLifecycle(root, "OnDisappearing");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(root, "OnAppearing")));
                    await ui.DrainDispatchedAsyncVoidAsync();
                    var navigation = root.Navigation;
                    Button open = CompletionPageControl<Button>(root, "creation-completion-open");
                    Require(open.IsEnabled, "SETUP: the unfinished Priority runner has no completion entry.");
                    await ClickCompletionControlAsync(ui, open);
                    Require(navigation.NavigationStack.Last() is CreationCompletionPage,
                        "The real Build entry did not open the completion assistant.");
                    var page = (CreationCompletionPage)navigation.NavigationStack.Last();
                    IssuedPageLifecycle(root, "OnDisappearing");
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));

                    Require(!CompletionPageControl<CheckBox>(page, "creation-completion-allow-attributes").IsChecked
                        && !CompletionPageControl<CheckBox>(page, "creation-completion-allow-skills").IsChecked,
                        "The interim assistant silently opted into increasing player allocations.");
                    Require(!MinimalVisible(page).Any(x => x.AutomationId == "creation-completion-apply"),
                        "An unreviewed proposal offered Apply.");
                    Picker archetype = CompletionPageControl<Picker>(page, "creation-completion-archetype");
                    string general = CreationCompletionPage.Copy("Archetype.General", "General survivor");
                    archetype.SelectedIndex = archetype.ItemsSource.Cast<string>().ToList().IndexOf(general);
                    Require(archetype.SelectedIndex >= 0, "SETUP: editable general-survivor archetype is missing.");
                    CompletionPageControl<CheckBox>(page, "creation-completion-allow-attributes").IsChecked = true;
                    CompletionPageControl<CheckBox>(page, "creation-completion-allow-skills").IsChecked = true;
                    var language = MinimalVisible(page).OfType<Picker>()
                        .SingleOrDefault(x => x.AutomationId == "creation-completion-native-language");
                    if (language is not null)
                    {
                        Require(language.ItemsSource.Count > 0, "SETUP: no actual source-backed native language available.");
                        language.SelectedIndex = 0;
                    }
                    await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-completion-preview"));
                    MinimalRequireNoMachineValues(page);
                    Entry cash = CompletionPageControl<Entry>(page, "creation-completion-cash-roll");
                    Button check = CompletionPageControl<Button>(page, "creation-completion-review");
                    Require(string.IsNullOrEmpty(cash.Text) && !check.IsEnabled && service!.ApplyCalls == 0,
                        "The assistant fabricated a cash roll or enabled completion without explicit starting cash.");
                    AssertCompletionFilesUnchanged(runtime, files);

                    // The test enters a legal roll explicitly; production must not
                    // invent a roll or silently use this fixture's deterministic value.
                    var originalCash = coordinator.LoadCreationFinalization().Value!;
                    var cashSource = originalCash.StartingCashSources?.FirstOrDefault()
                        ?? originalCash.StartingCashSource;
                    Require(cashSource is not null, "SETUP: actual starting-cash authority is missing.");
                    cash.Text = (cashSource!.Dice * 3).ToString(CultureInfo.InvariantCulture);
                    Require(check.IsEnabled, "A valid explicit cash total did not enable the Core review.");
                    await ClickCompletionControlAsync(ui, check);
                    Button apply = CompletionPageControl<Button>(page, "creation-completion-apply");
                    Require(apply.IsEnabled && service!.ReviewCalls == 1 && service.ApplyCalls == 0,
                        "The explicit final review was not produced read-only by Core.");
                    MinimalRequireNoMachineValues(page);
                    AssertCompletionFilesUnchanged(runtime, files);

                    if (scenario == "cancel")
                    {
                        Button cancel = CompletionPageControl<Button>(page, "creation-completion-cancel");
                        await ClickCompletionControlAsync(ui, cancel);
                        Require(navigation.NavigationStack.Count == 1
                            && ReferenceEquals(Shell.Current?.CurrentPage, root),
                            "Cancel did not return to the original creation page.");
                        ((IButtonController)apply).SendClicked();
                        await ui.DrainDispatchedAsyncVoidAsync();
                        AssertCompletionFilesUnchanged(runtime, files);
                        Require(service!.ApplyCalls == 0 && coordinator.State.Profile?.Created == false,
                            "A retained Apply control saved after cancellation.");
                    }
                    else
                    {
                        await ClickCompletionControlAsync(ui, apply);
                        Require(service!.ApplyCalls == 1, "The explicit UI action did not dispatch exactly one Apply.");
                        if (scenario == "unknown")
                        {
                            Button recover = CompletionPageControl<Button>(page, "creation-completion-check-result");
                            Require(recover.IsEnabled
                                && !MinimalVisible(page).Any(x => x.AutomationId == "creation-completion-apply"),
                                "An unknown outcome offered a fresh Apply instead of receipt-only recovery.");
                            await ClickCompletionControlAsync(ui, recover);
                            ((IButtonController)apply).SendClicked();
                            await ui.DrainDispatchedAsyncVoidAsync();
                            Require(service.ApplyCalls == 1
                                && ReferenceEquals(navigation.NavigationStack.Last(), page),
                                "Unknown-outcome recovery repeated Apply or claimed a completed Career transition.");
                            AssertCompletionFilesUnchanged(runtime, files);
                            MinimalRequireNoMachineValues(page);
                        }
                        else
                        {
                            var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                            Require(saved.ContentRevision == before.ContentRevision + 1
                                && saved.SavedRevision == saved.ContentRevision
                                && saved.Document.AuxiliaryState.CharacterCreationFinalizationReceipts?.Count == 1
                                && coordinator.State.Profile?.Created == true
                                && coordinator.State.ContentRevision == saved.ContentRevision,
                                "Completion did not cold-read one atomic finalization into Career.");
                            Require(navigation.NavigationStack.Count == 1
                                && ReferenceEquals(Shell.Current?.CurrentPage, root),
                                "Completion required an extra Back click instead of entering Career directly.");
                            var committed = CompletionFiles(runtime);
                            ((IButtonController)apply).SendClicked();
                            await ui.DrainDispatchedAsyncVoidAsync();
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(root, "OnAppearing")));
                            Require(MinimalVisible(root).Any(x => x.AutomationId == "build-sr5-career-wizard")
                                && !MinimalVisible(root).Any(x => x.AutomationId == "creation-completion-open"),
                                "The restored root did not expose the usable Career route.");
                            AssertCompletionFilesUnchanged(runtime, committed);
                            Require(service.ApplyCalls == 1, "Retained UI replayed a committed completion.");
                            IssuedPageLifecycle(root, "OnDisappearing");
                        }
                    }
                    Require(owners.ActiveLeases == 0 && alerts.Titles.Count == 0,
                        "Completion leaked an owner lease or hid a callback error in an alert.");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS actual Core/MAUI completion page: " + scenario);
                }
            }
            finally
            {
                app.CloseWindow(window);
                Application.Current = previousApp!;
                CultureInfo.CurrentUICulture = previousCulture;
            }
        });
    }

    private static T CompletionPageControl<T>(NativePageBase page, string id) where T : VisualElement
        => MinimalVisible(page).OfType<T>().SingleOrDefault(x => x.AutomationId == id)
           ?? throw new InvalidOperationException("Missing completion control: " + id + "\n" + MinimalVisibleText(page));

    private static Task ClickCompletionControlAsync(IssuedPageUiContext ui, Button button)
        => JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked()));
}
