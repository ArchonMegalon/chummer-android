using System.Reflection;
using System.Globalization;
using System.Text.RegularExpressions;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunMinimalUiAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (var (locale, saveGear, saveBudget) in new[]
            {
                ("en-GB", "Save equipment", "Save budget"),
                ("de-AT", "Ausrüstung speichern", "Budget speichern"),
                ("es-MX", "Guardar equipo", "Guardar presupuesto")
            })
            {
                var localized = AndroidSurfaceStrings.Resolve(locale);
                Require(localized["GearPreview.Confirm"] == saveGear
                    && localized["ResourcesPreview.Confirm"] == saveBudget,
                    "Creation confirmation must describe the action in the player's language.");
                foreach (var key in new[] { "Resources.CurrentBudget", "Resources.CoreAuthority",
                    "Gear.DraftBasket", "Gear.ActiveCatalog", "GearPreview.ExactProjection" })
                    Require(!Regex.IsMatch(localized[key], "Core|XML|authority|autoridad|Autorität",
                        RegexOptions.IgnoreCase), "Player-facing headings expose implementation jargon.");
            }
            const string id = "c49a893a-d445-4aac-bec0-c8501cba4c2c";
            var label = NativeTheme.Body(id);
            var details = NativeTheme.TechnicalDetails(label, "test-diagnostics");
            var root = new VerticalStackLayout { details };
            var toggle = details.Children.OfType<Button>().Single();
            Require(!label.IsVisible && !MinimalVisibleText(root).Contains(id),
                "Diagnostics are exposed by default.");
            ((IButtonController)toggle).SendClicked();
            Require(label.IsVisible && label.Text == id && MinimalVisibleText(root).Contains(id),
                "Explicit troubleshooting lost the exact value.");
            ((IButtonController)toggle).SendClicked();
            Require(!label.IsVisible, "Diagnostics cannot be collapsed.");
            root.Clear();
            ((IButtonController)toggle).SendClicked();
            Require(!label.IsVisible, "A detached disclosure reopened old diagnostics.");
            Require(NativeTheme.Body(id).Text == id && NativeTheme.BookProse(id).Text == id,
                "Minimalism must not rewrite arbitrary player text or prose.");
            Require(NativeTheme.Body("Readable").FontSize >= 15
                && NativeTheme.BookProse("Chapter").FontSize >= 18
                && NativeTheme.PrimaryButton("Continue").HeightRequest >= 48,
                "Simplification shrank readable text or touch targets.");
            var overlay = NativeAuthoritySemantics.Overlay(NativeTheme.Body("Saved"),
                NativeAuthoritySemantics.Identifier("test-machine-id", id));
            Require(!MinimalVisibleText(overlay).Contains(id),
                "Ordinary-build TalkBack exposes invisible machine values.");

            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true, creationPrerequisite: true);
            var before = PrepareActualFinalizationReadyContext(runtime);
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            AssertCreationReadinessCopy(runtime.Coordinator);
            var currentCulture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (locale, words) in new[] { ("en-GB", "saved Attributes"),
                             ("de-AT", "gespeicherten Attribute"), ("es-MX", "Atributos guardados") })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    const string locked = "creation-prerequisite-dependent-attributes-draft-exists";
                    string message = CreationFlowStrings.DashboardBlocker(locked);
                    Require(message.Contains(words) && message == CreationFlowStrings.Get("Dashboard.MethodLocked", "missing"),
                        "Locked method lacks localized dependent-Attributes guidance.");
                    Require(CreationFlowStrings.DashboardBlocker("fixture-unknown-9d74b5ce")
                        == CreationFlowStrings.Get("Dashboard.StepBlocked", "missing"),
                        "Unknown codes must stay in diagnostics rather than leak into ordinary guidance.");
                }
            }
            finally { CultureInfo.CurrentUICulture = currentCulture; }
            var lockedMethod = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
            Require(lockedMethod.Blockers.Concat(lockedMethod.Value?.Blockers ?? []).Contains(
                "creation-prerequisite-dependent-attributes-draft-exists"),
                "SETUP: saved Attributes must actually lock prerequisite edits.");
            var dashboard = new BuildPage(runtime.Coordinator);
            var snapshot = runtime.Coordinator.State.CreationWizard!;
            Require(CreationDashboardProjectionBinding.TryCreate(runtime.Coordinator.State, snapshot, out var binding),
                "SETUP: current dashboard binding missing.");
            var projection = CreationDashboardAuthorityProjection.Loading(binding!) with
            {
                Prerequisite = lockedMethod,
                Progress = CreationDashboardAuthorityPhaseProgress.ForBuildMethod(snapshot.BuildMethod) with
                { Prerequisite = CreationDashboardAuthorityPhaseState.Ready }
            };
            var methodRoute = (CreationBudgetRoute)typeof(BuildPage).GetMethod("AddCreationMethodRoute",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, [snapshot, projection, lockedMethod])!;
            Require(!methodRoute.CanOpen && methodRoute.Detail == CreationFlowStrings.DashboardBlocker(
                "creation-prerequisite-dependent-attributes-draft-exists")
                && methodRoute.Blockers.SequenceEqual(["creation-prerequisite-dependent-attributes-draft-exists"])
                && !MinimalVisible(dashboard).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-stage-method").IsEnabled,
                "Plain locked-method guidance changed readiness or gave a misleading Karma reason.");
            var header = new VerticalStackLayout();
            bool hasPicker = (bool)typeof(BuildPage).GetMethod("AddWorkspacePicker",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, [header])!;
            Require(hasPicker == (runtime.Coordinator.State.OpenWorkspaces.Count > 1)
                && (!hasPicker || header.Children.OfType<Picker>().Single().AutomationId == "build-workspace-picker"),
                "Dashboard heading must retain the workspace selector only when needed.");
            var actual = new CharacterCreationGearInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
            // Priority editing is intentionally locked once dependent stages
            // exist. Use a real new runner, not the ready-for-Gear fixture.
            await using var priorityRuntime = new NativeRewardRuntime(contentRoot,
                linkedOwners: owners, creationPrerequisite: true);
            await priorityRuntime.Coordinator.InitializeAsync();
            await AccountStartupTask(priorityRuntime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            var prioritySeed = PreparePrerequisiteOwnerFixture(priorityRuntime);
            await HydrateFinalizationOwnerAsync(priorityRuntime, owners, prioritySeed);
            var prerequisite = (await priorityRuntime.Coordinator.LoadCreationPrerequisiteAsync()).Value
                ?? throw new InvalidOperationException("SETUP: real prerequisite authority unavailable.");
            var priorities = new CreationPrerequisitePage(priorityRuntime.Coordinator, prerequisite);
            MinimalRender(priorities);
            MinimalRequireNoMachineValues(priorities);
            Require(MinimalVisible(priorities).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-prerequisite-prepare-preview"),
                "Minimal Priorities hid its review action.");
            var draft = new CreationPrerequisitePhoneDraft();
            draft.Bind(prerequisite, priorityRuntime.Coordinator.State);
            var category = new CreationPriorityCategoryPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Attributes);
            MinimalRender(category);
            MinimalRequireNoMachineValues(category);
            Require(MinimalVisible(category).OfType<Button>().Count(x =>
                    x.AutomationId?.StartsWith("creation-prerequisite-rank-") == true) == 5,
                "Minimal rank list removed choices or their unavailability explanations.");
            Require(draft.TrySelect(prerequisite, priorityRuntime.Coordinator.State,
                CharacterCreationPriorityCategoryIds.Heritage, "A"), "SETUP: Heritage rank unavailable.");
            var heritage = new CreationPriorityDetailPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Heritage);
            MinimalRender(heritage);
            MinimalRequireNoMachineValues(heritage);
            Require(MinimalVisible(heritage).OfType<Button>().Any(x =>
                x.AutomationId?.StartsWith("creation-prerequisite-heritage-option-") == true),
                "Minimal Heritage list has no actual choices.");
            Require(draft.TrySelect(prerequisite, priorityRuntime.Coordinator.State,
                CharacterCreationPriorityCategoryIds.Talent, "B"), "SETUP: Talent rank unavailable.");
            var talent = new CreationPriorityDetailPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Talent);
            MinimalRender(talent);
            MinimalRequireNoMachineValues(talent);
            Require(MinimalVisible(talent).OfType<Button>().Any(x =>
                x.AutomationId?.StartsWith("creation-prerequisite-talent-option-") == true),
                "Minimal Talent list has no actual choices.");
            var gear = new CreationGearPage(runtime.Coordinator, actual, runtime.Presenter);
            await MinimalPrepareAsync(gear);
            var visible = MinimalVisibleText(gear);
            MinimalRequireNoMachineValues(gear);
            Require(MinimalVisible(gear).OfType<Button>().Any(x => x.AutomationId == "creation-gear-preview")
                && MinimalVisible(gear).OfType<SearchBar>().Any()
                && visible.Contains("¥"), "Minimal Gear hid budget, search or review.");
            var copy = AndroidSurfaceStrings.Resolve();
            var unchanged = MinimalVisible(gear).OfType<Label>()
                .Single(x => x.AutomationId == "creation-gear-preview-authority");
            Require(unchanged.Text == copy["Gear.ChangeBasket"]
                && unchanged.TextColor.Equals(NativeTheme.Muted)
                && !MinimalVisible(gear).OfType<Button>()
                    .Single(x => x.AutomationId == "creation-gear-preview").IsEnabled,
                "An unchanged saved basket is neutral guidance, not an error or a new save.");
            var disclosure = MinimalVisible(gear).OfType<Button>()
                .Single(x => x.AutomationId == "creation-gear-details-toggle");
            ((IButtonController)disclosure).SendClicked();
            Require(MinimalVisible(gear).OfType<Label>().Any(x =>
                    x.AutomationId == "creation-gear-binding-snapshot-digest"),
                "Gear diagnostic anchors were deleted instead of disclosed.");
            // A refresh must not retain an expanded old snapshot.
            MinimalRender(gear);
            MinimalRequireNoMachineValues(gear);

            var original = runtime.Coordinator.State;
            var prepared = actual.Prepare(original, [new("gear:" + id, 1)]).PreparedPreview
                ?? throw new InvalidOperationException("SETUP: actual Gear preview unavailable.");
            var preview = new CreationGearPreviewPage(runtime.Coordinator, actual, runtime.Presenter,
                prepared, AndroidSurfaceStrings.Resolve(), original);
            await MinimalPrepareAsync(preview);
            MinimalRequireNoMachineValues(preview);
            Require(MinimalVisible(preview).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-gear-confirm" && x.IsEnabled
                    && x.Text == copy["GearPreview.Confirm"]),
                "Minimal preview hid or disabled explicit confirmation.");

            var resources = new CharacterCreationResourcesInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
            var resourcePage = new CreationResourcesPage(runtime.Coordinator, resources, runtime.Presenter, actual);
            await MinimalPrepareAsync(resourcePage);
            MinimalRequireNoMachineValues(resourcePage);
            var resourceText = MinimalVisibleText(resourcePage);
            Require(resourceText.Contains("¥")
                && !resourceText.Contains(copy["Common.DraftRevision"])
                && !resourceText.Contains(copy["Resources.ExactBudget"])
                && MinimalVisible(resourcePage).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-resources-open-gear" && x.IsEnabled),
                "Resources must retain budget and next action without repeating technical status.");
            // Exercise the warning branch without modifying the workspace or
            // pretending a fabricated budget is admissible for a save.
            var budget = resources.Load(original).State!.Budget;
            typeof(CreationResourcesPage).GetMethod("AddBudget", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(resourcePage, [budget with { IsExact = false }, "Warning test", "test-incomplete-budget"]);
            Require(MinimalVisible(resourcePage).OfType<Label>().Any(x =>
                x.Text == copy["Resources.IncompleteBudget"] && x.TextColor.Equals(NativeTheme.Danger)),
                "Minimal Resources suppressed an incomplete-cost warning.");
            Require(FinalizationDocumentDigest(new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!)
                    == FinalizationDocumentDigest(before),
                "Rendering/disclosing minimal UI mutated the workspace.");
        });
        Console.WriteLine("PASS minimal native UI: localized plain actions, collapsed diagnostics, neutral unchanged basket, retained budget/warnings/confirm, unchanged prose and persistence");
    }

    private static async Task MinimalPrepareAsync(NativePageBase page)
    {
        await (Task)page.GetType().GetMethod("PrepareForAppearanceRefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [CancellationToken.None])!;
        MinimalRender(page);
    }

    private static void MinimalRender(NativePageBase page) => page.GetType()
        .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);

    private static IEnumerable<VisualElement> MinimalVisible(IVisualTreeElement element)
    {
        if (element is VisualElement { IsVisible: false }) yield break;
        if (element is VisualElement view) yield return view;
        foreach (var child in element.GetVisualChildren())
            foreach (var descendant in MinimalVisible(child))
                yield return descendant;
    }

    private static string MinimalVisibleText(IVisualTreeElement element) => string.Join("\n",
        MinimalVisible(element).SelectMany(view => new[]
        {
            view is Label label ? label.Text : view is Button button ? button.Text : string.Empty,
            SemanticProperties.GetDescription(view)
        }));

    private static void MinimalRequireNoMachineValues(IVisualTreeElement element)
    {
        Require(!Regex.IsMatch(MinimalVisibleText(element),
            @"sha256:|\b[0-9a-f]{32}\b|\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b",
            RegexOptions.IgnoreCase), "Normal UI/accessibility contains machine identifiers.");
    }
}
