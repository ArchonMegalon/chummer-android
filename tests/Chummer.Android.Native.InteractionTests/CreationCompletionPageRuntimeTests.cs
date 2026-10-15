using System.Globalization;
using System.Reflection;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Creation;
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
                foreach (string scenario in new[] { "prerequisite", "busy-cancel", "cancel", "apply", "lost-reply", "unknown" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(scenario == "apply" ? "de-AT" : "en-GB");
                    var owners = new ControlledLinkedOwner();
                    using var reviewPause = scenario == "busy-cancel" ? new CompletionReviewPause() : null;
                    CompletionServiceProbe? service = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                        creationCompletion: true, creationFinalization: true, creationPrerequisite: true,
                        productionCreationOverview: true,
                        completionServiceDecorator: actual =>
                        {
                            service = new(actual, ui)
                            {
                                ThrowAfterApply = scenario == "lost-reply",
                                ThrowBeforeApply = scenario == "unknown"
                            };
                            return reviewPause is null ? service : new PausedCompletionReviewService(service, reviewPause);
                        });
                    await runtime.Coordinator.InitializeAsync();
                    await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                    var before = scenario == "prerequisite"
                        ? PreparePrerequisiteOwnerFixture(runtime, CharacterCreationBuildMethods.Priority)
                        : PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: scenario != "cancel");
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    if (scenario == "prerequisite")
                    {
                        var prerequisite = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
                        Require(prerequisite is { Outcome: CharacterCreationFoundationOutcomes.Success,
                                Value: { CanEnterAttributes: false, PendingDraft: null } }
                            && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(prerequisite.Value),
                            "SETUP: fresh Priority must have genuine unconfirmed prerequisite authority.");
                    }
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
                    AssertCompletionHasNoAllocationSettings(page);

                    if (scenario == "prerequisite")
                    {
                        string guidance = CreationCompletionPage.Copy("LoadBlocked",
                            "Complete Build method first, then reopen these suggestions. Your existing choices are unchanged.");
                        string downstream = CreationFlowStrings.FinalizationBlocker("creation-finalization-skills-draft-required");
                        Require(MinimalVisible(page).OfType<Label>().Count(label => label.Text == guidance) == 1
                            && !MinimalVisibleText(page).Contains(downstream, StringComparison.Ordinal),
                            "Fresh Priority must show one prerequisite instruction, not an unreachable Skills instruction.");
                        Require(!MinimalVisible(page).Any(element => element.AutomationId is
                                "creation-completion-archetype" or "creation-completion-preview"
                                or "creation-completion-review" or "creation-completion-apply"),
                            "Unconfirmed prerequisites exposed suggestion or mutation controls.");
                        MinimalRequireNoMachineValues(page);
                        AssertCompletionFilesUnchanged(runtime, files);
                        await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-completion-cancel"));
                        Require(navigation.NavigationStack.Count == 1
                            && ReferenceEquals(Shell.Current?.CurrentPage, root)
                            && coordinator.State.Profile?.Created == false
                            && service!.ReviewCalls == 0 && service.ApplyCalls == 0,
                            "Cancel from prerequisite guidance must return to creation without review or Apply.");
                        AssertCompletionFilesUnchanged(runtime, files);
                        Require(owners.ActiveLeases == 0 && alerts.Titles.Count == 0,
                            "Prerequisite guidance leaked an owner lease or hid a callback error.");
                        IssuedPageLifecycle(page, "OnDisappearing");
                        ui.AssertHealthy();
                        Console.WriteLine("PASS actual Core/MAUI completion page: prerequisite");
                        continue;
                    }

                    Require(!MinimalVisible(page).Any(x => x.AutomationId == "creation-completion-apply"),
                        "An unreviewed proposal offered Apply.");
                    var inputs = (CharacterCreationCompletionReviewInputs?)typeof(CreationCompletionPage)
                        .GetField("_inputs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)
                        ?? throw new InvalidOperationException("SETUP: the actual completion input read is missing.");
                    string inputsDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs);
                    Picker archetype = CompletionPageControl<Picker>(page, "creation-completion-archetype");
                    string general = CreationCompletionPage.Copy("Archetype.General", "General survivor");
                    archetype.SelectedIndex = archetype.ItemsSource.Cast<string>().ToList().IndexOf(general);
                    Require(archetype.SelectedIndex >= 0, "SETUP: editable general-survivor archetype is missing.");
                    var language = MinimalVisible(page).OfType<Picker>()
                        .SingleOrDefault(x => x.AutomationId == "creation-completion-native-language");
                    if (language is not null)
                    {
                        Require(language.ItemsSource.Count > 0, "SETUP: no actual source-backed native language available.");
                        language.SelectedIndex = 0;
                    }
                    await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-completion-preview"));
                    AssertCompletionHasNoAllocationSettings(page);
                    var proposal = (CompletionAssistantProposal?)typeof(CreationCompletionPage)
                        .GetField("_proposal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)
                        ?? throw new InvalidOperationException("The real Preview handler did not produce a proposal.");
                    Require(proposal.AllocationPolicy == CompletionAllocationPolicy.MissingOnly
                        && proposal.Intent.Permissions.AttributeIds.Count == 0
                        && proposal.Intent.Permissions.SkillTargets.Count == 0
                        && proposal.Intent.InputsSnapshotDigest == inputs.SnapshotDigest
                        && CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs) == inputsDigest,
                        "The default UI must request missing-only suggestions without additive permissions or changed original inputs.");
                    if (scenario == "cancel")
                    {
                        var savedAttributes = inputs.Skills.Attributes.PendingDraft
                            ?? throw new InvalidOperationException("SETUP: Cancel must use actual saved Attributes.");
                        Require(savedAttributes.Allocations.Count > 0
                            && savedAttributes.Allocations.All(row => row.PriorityPoints == 0 && row.KarmaLevels == 0)
                            && inputs.Skills.Allocations.Any(row => row.IsNativeLanguage),
                            "SETUP: saved baseline must retain actual zero attribute rows and a selected native language.");
                        var selections = proposal.Evaluation.Value?.Selections
                            ?? throw new InvalidOperationException("Saved-baseline suggestions have no Core-evaluated selections.");
                        Require(proposal.Request.Attributes.PermittedAttributeOrder.Count == 0
                            && selections.AttributeAllocations.OrderBy(row => row.AttributeId, StringComparer.Ordinal)
                                .SequenceEqual(savedAttributes.Allocations.OrderBy(row => row.AttributeId, StringComparer.Ordinal))
                            && inputs.Skills.Allocations.All(original => selections.SkillAllocations.Count(row =>
                                row.Kind == original.Kind && row.SourceSkillId == original.SourceSkillId) == 1
                                && selections.SkillAllocations.Any(row => row == original))
                            && inputs.Skills.GroupAllocations.All(original => selections.GroupAllocations.Count(row =>
                                row.GroupId == original.GroupId) == 1
                                && selections.GroupAllocations.Any(row => row == original))
                            && selections.SkillAllocations.Count(row => row.IsNativeLanguage)
                                == inputs.Skills.Allocations.Count(row => row.IsNativeLanguage),
                            "Missing-only suggestions changed saved attribute/skill choices or replaced the native language.");
                        string guidance = CreationCompletionPage.Copy("AttributeChoiceIntent", "missing-attribute-choice-intent");
                        Require(proposal.RemainingTasks.Contains(CompletionAssistantRules.AttributeChoiceIntentRequired)
                            && guidance != "missing-attribute-choice-intent"
                            && MinimalVisible(page).OfType<Label>().Any(label => label.Text == guidance),
                            "Saved under-target attributes must remain unchanged with visible localized choice-intent guidance.");
                    }
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
                    if (reviewPause is not null)
                    {
                        Task pending = ui.BeginAsyncVoid(() => ((IButtonController)check).SendClicked());
                        try
                        {
                            await reviewPause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                            AssertCompletionReadProgress(page);
                            AssertCompletionFilesUnchanged(runtime, files);
                            // Join both actual callbacks through the in-flight
                            // UI operation; do not start a second harness gate.
                            ((IButtonController)CompletionPageControl<Button>(page, "creation-completion-cancel")).SendClicked();
                            await JoinIssuedPageAsync(pending);
                        }
                        finally
                        {
                            reviewPause.Release.Set();
                            await JoinIssuedPageAsync(pending);
                        }
                        ((IButtonController)check).SendClicked();
                        await ui.DrainDispatchedAsyncVoidAsync();
                        Require(navigation.NavigationStack.Count == 1
                            && ReferenceEquals(Shell.Current?.CurrentPage, root)
                            && coordinator.State.Profile?.Created == false
                            && service!.ReviewCalls == 0 && service.ApplyCalls == 0
                            && !MinimalVisible(page).Any(element => element.AutomationId == "creation-completion-apply"),
                            "Cancel during a held read-only review must return without publishing or replaying completion.");
                        AssertCompletionFilesUnchanged(runtime, files);
                        Require(owners.ActiveLeases == 0 && alerts.Titles.Count == 0,
                            "Cancel during read-only progress leaked a lease or hid a callback error.");
                        IssuedPageLifecycle(page, "OnDisappearing");
                        ui.AssertHealthy();
                        Console.WriteLine("PASS actual Core/MAUI completion page: busy-cancel");
                        continue;
                    }
                    await ClickCompletionControlAsync(ui, check);
                    Button apply = CompletionPageControl<Button>(page, "creation-completion-apply");
                    Require(apply.IsEnabled && service!.ReviewCalls == 1 && service.ApplyCalls == 0,
                        "The explicit final review was not produced read-only by Core.");
                    AssertCompletionHasNoAllocationSettings(page);
                    MinimalRequireNoMachineValues(page);
                    AssertCompletionFilesUnchanged(runtime, files);
                    Require(!MinimalVisible(page).Any(element => element.AutomationId == "creation-completion-read-busy"),
                        "Read-only progress remained visible after review completed.");

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

    private static void AssertCompletionHasNoAllocationSettings(NativePageBase page)
    {
        var visible = MinimalVisible(page).ToArray();
        Require(!visible.OfType<CheckBox>().Any()
            && !visible.Any(element => element.AutomationId?.StartsWith(
                "creation-completion-allow-", StringComparison.Ordinal) == true),
            "Missing-only completion must not expose allocation opt-ins or a configurable replacement policy.");
    }

    private static void AssertCompletionReadProgress(CreationCompletionPage page)
    {
        var progress = CompletionPageControl<Grid>(page, "creation-completion-read-busy");
        var spinner = CompletionPageControl<ActivityIndicator>(page, "creation-completion-read-progress");
        var status = CompletionPageControl<Label>(page, "creation-completion-read-status");
        var cancel = CompletionPageControl<Button>(page, "creation-completion-cancel");
        Require(progress.Parent is VerticalStackLayout footer
            && ReferenceEquals(footer.Parent, page.Content) && Grid.GetRow(footer) == 1
            && spinner.IsRunning && spinner.IsVisible
            && status.Text == CreationCompletionPage.Copy("Working", "missing-busy-status")
            && status.Text != "missing-busy-status"
            && cancel.IsEnabled && ReferenceEquals(cancel.Parent, progress.Parent)
            && !MinimalVisible(page).OfType<Button>().Any(button => button.IsEnabled && button.AutomationId is
                ("creation-completion-preview" or "creation-completion-review" or "creation-completion-apply")),
            "Read-only work must show a running, localized pinned indicator while Cancel remains usable and mutation actions stay disabled.");
    }

    private sealed class CompletionReviewPause : IDisposable
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public void Dispose() => Release.Dispose();
    }

    private sealed class PausedCompletionReviewService(IOwnerBoundCharacterCreationCompletionService actual,
        CompletionReviewPause pause) : IOwnerBoundCharacterCreationCompletionService
    {
        public CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
            OwnerContextStamp owner, CharacterWorkspaceId workspaceId, CharacterCreationCompletionReviewRequest proposal,
            CharacterCreationStartingCashChoice startingCash, CancellationToken cancellationToken = default)
        {
            pause.Entered.TrySetResult(true);
            pause.Release.Wait(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return actual.Review(owner, workspaceId, proposal, startingCash, cancellationToken);
        }

        public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
            OwnerContextStamp owner, CharacterCreationCompletionApplyRequest request,
            CancellationToken cancellationToken = default)
            => actual.Apply(owner, request, cancellationToken);
    }
}
